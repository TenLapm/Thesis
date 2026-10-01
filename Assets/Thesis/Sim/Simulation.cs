using System;
using System.Collections.Generic;
using Thesis.Core;

namespace Thesis.Sim
{
    // The whole game, headless. A host (Unity's SimHost in WP4, the CLI, a test)
    // calls Enqueue() for player input and Tick() at a fixed rate; nothing else.
    // Same config + map + library + seed + commands at the same ticks -> the same
    // state hash on every tick (CLAUDE.md I1).
    //
    // Tick() is ARCHITECTURE.md §4.2, step for step. Two behaviour changes from the
    // Unity version, both intended and recorded in DEVLOG:
    //   * step 4 - a breach rebuilds the flow field once at the end of the tick,
    //     not in the middle of the agent loop;
    //   * step 5 - the next intermission starts when the wave's last agent
    //     resolves, not when it spawns (decision D4).
    public sealed class Simulation
    {
        private readonly SimConfig config;
        private readonly MapData map;
        private readonly ShapeDef[] library;
        private readonly IWavePlanner planner;
        private readonly IThreatPricer pricer;

        // Scratch only: reusable buffers, no state.
        private readonly FlowField field = new FlowField();
        private readonly List<SimEvent> events = new List<SimEvent>();

        public Simulation(SimConfig config, MapData map, ShapeDef[] shapeLibrary, ulong rngSeed, IWavePlanner planner, IThreatPricer pricer = null)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            library = shapeLibrary ?? throw new ArgumentNullException(nameof(shapeLibrary));
            this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
            this.pricer = pricer;
            RngSeed = rngSeed;

            config.Validate();
            map.Validate();
            if (map.Spawns.Length == 0) throw new InvalidOperationException("Map '" + map.Name + "' has no spawn; a simulation needs at least one.");

            var s = new SimState
            {
                Grid = new SimGrid(map),
                BagRng = new Pcg32(rngSeed, RngStreams.Bag),
                SpawnRng = new Pcg32(rngSeed, RngStreams.Spawn),
                BuildBudget = config.StartBudget,
                CoreHp = config.CoreMaxHp,
                Phase = SimPhase.Prep,
                PhaseTicksRemaining = config.Ticks(config.PrepSeconds),
            };
            s.Bag = new ShapeBag(library, s.BagRng);
            s.Occupancy = new OccupancyMap(s.Grid.NodeCount);
            field.Generate(s.Grid, map.Core);

            State = s;
        }

        private Simulation(Simulation source, IWavePlanner planner)
        {
            config = source.config;
            map = source.map;
            library = source.library;
            pricer = source.pricer;
            RngSeed = source.RngSeed;
            this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
            State = source.State.Clone();
            events.AddRange(source.events);
        }

        // Read-only outside Thesis.Sim by convention; planners are policed by the
        // mutation guard, views simply never write.
        public SimState State { get; }

        public SimConfig Config => config;

        public MapData Map => map;

        public IWavePlanner Planner => planner;

        // The seed every RNG stream was derived from, and the shape masters the bag
        // draws from. A replay stores both; the masters are never mutated (the bag
        // only ever hands out clones), so this array is the library as constructed.
        public ulong RngSeed { get; }

        public ShapeDef[] ShapeLibrary => library;

        // Everything that happened during the most recent Tick(), in order.
        public IReadOnlyList<SimEvent> LastTickEvents => events;

        // Applied at the start of the next Tick(), in enqueue order.
        public void Enqueue(SimCommand command)
        {
            State.Pending.Add(command);
        }

        // Deep copy, RNG state included, driven by a different planner. For
        // counterfactuals: "what would wave N have done under strategy X?"
        public Simulation Clone(IWavePlanner planner)
        {
            return new Simulation(this, planner);
        }

        public ulong ComputeHash()
        {
            return StateHasher.Compute(State);
        }

        // Scenario setup only (benchmark layouts, test fixtures): after writing walls
        // straight onto State.Grid, rebuild the field so agents see them. Refused once
        // the game has started - from then on the only way in is a command.
        public void RebuildFieldForSetup()
        {
            if (State.Tick != 0) throw new InvalidOperationException("[Sim] RebuildFieldForSetup is only for setup before the first Tick (tick is " + State.Tick + ").");
            field.Generate(State.Grid, map.Core);
        }

        // Applies queued commands NOW, without advancing time. This is exactly step 1
        // of the next Tick() run early: nothing else happens between ticks, so
        // "applied at FlushInput" and "applied at the start of the next tick" give the
        // same state (FlushInputEqualsApplyingAtTheNextTick pins this). A replay that
        // records each command with the State.Tick it was flushed at is still exact.
        //
        // It exists for the host: rotating and holding must work while the game is
        // paused (Time.timeScale 0 -> no ticks), and input feels instant instead of
        // waiting up to one tick. Events raised here replace LastTickEvents.
        public void FlushInput()
        {
            events.Clear();
            SimState s = State;
            if (s.Phase == SimPhase.GameOver)
            {
                s.Pending.Clear();
                return;
            }
            ApplyPending(s);
        }

        public void Tick()
        {
            events.Clear();
            SimState s = State;

            // 0. A finished game never advances.
            if (s.Phase == SimPhase.GameOver)
            {
                s.Pending.Clear();
                return;
            }

            // 1. Player input.
            ApplyPending(s);

            // 2. Phase machine. The countdown is only written when no wave starts:
            // BeginWave asks the planner first and may throw, and a tick that throws
            // there must leave the state as it found it, so the next Tick() can try
            // again (see BeginWave). When the wave does start, BeginWave zeroes the
            // countdown itself, so the result is the same as decrementing first.
            if (s.Phase == SimPhase.Prep || s.Phase == SimPhase.Intermission)
            {
                if (s.PhaseTicksRemaining - 1 <= 0 || s.StartWaveRequested) BeginWave(s);
                else s.PhaseTicksRemaining--;
            }
            s.StartWaveRequested = false; // consumed, or meaningless mid-wave
            if (s.Phase == SimPhase.Spawning) SpawnDue(s);

            // 3. Agent pass (ascending id).
            int firstEvent = events.Count;
            bool fieldDirty = AgentSystem.Step(s.Grid, s.Live, config.TickSeconds, s.Occupancy, ref s.BuildBudget, ref s.CoreHp, events);
            CountAgentEvents(s, firstEvent);
            RemoveDead(s.Live);

            // 4. One field rebuild per tick, after every agent has acted.
            if (fieldDirty) field.Generate(s.Grid, map.Core);

            // 5. Wave boundary (D4): only once every agent of the wave is resolved.
            if (s.Phase == SimPhase.Resolving && s.CoreHp > 0 && !AnyLiveAgentOfWave(s, s.WaveIndex))
            {
                CloseWave(s, coreDestroyed: false);
                // Stipend at intermission start, as WaveSpawner.IntermissionRoutine did.
                s.BuildBudget += config.StipendPerWave;
                s.Phase = SimPhase.Intermission;
                s.PhaseTicksRemaining = config.Ticks(config.IntermissionSeconds);
            }

            // 6. Core destroyed. If a wave was running, close it first so the planner
            // learns the wave ended the run.
            if (s.CoreHp <= 0)
            {
                if (s.CurrentOutcome != null) CloseWave(s, coreDestroyed: true);
                s.Phase = SimPhase.GameOver;
                s.PhaseTicksRemaining = 0;
                events.Add(SimEvent.GameOver());
            }

            // 7.
            s.Tick++;
        }

        private void ApplyPending(SimState s)
        {
            for (int i = 0; i < s.Pending.Count; i++) Apply(s, s.Pending[i]);
            s.Pending.Clear();
        }

        private void Apply(SimState s, SimCommand command)
        {
            switch (command.Kind)
            {
                case SimCommandKind.PlaceShape:
                    if (Placement.TryPlace(s.Grid, map, field, s.Bag.CurrentShape, s.Bag.CurrentRotationTurns,
                                           new TileCoord(command.X, command.Y), ref s.BuildBudget, s.Tick,
                                           s.PlacementLog, events))
                    {
                        s.Bag.PullNextShape(); // the placed piece is consumed
                    }
                    break;
                case SimCommandKind.Rotate:
                    s.Bag.RotateCurrent();
                    break;
                case SimCommandKind.Hold:
                    s.Bag.SwapHold();
                    break;
                case SimCommandKind.StartWaveNow:
                    // Only meaningful between waves; ignored mid-wave, like the
                    // original StartWave() returning early when a wave was active.
                    if (s.Phase == SimPhase.Prep || s.Phase == SimPhase.Intermission) s.StartWaveRequested = true;
                    break;
                default:
                    throw new InvalidOperationException("[Sim] Unknown command kind " + command.Kind + ".");
            }
        }

        private void BeginWave(SimState s)
        {
            // Ask for the plan and check it BEFORE changing anything. If the planner
            // throws or hands back a bad plan, the state is exactly as it was and the
            // next Tick() asks again. WaveIndex used to be incremented first; a
            // planner that threw three times then made the first real wave "wave 4".
            //
            // So during PlanWave, State.WaveIndex is still the previous wave's number.
            // The wave being planned is context.WaveIndex.
            int wave = s.WaveIndex + 1;
            var context = new WaveContext(wave, s, map, config);
            ulong before = StateHasher.Compute(s);
            WavePlan plan = planner.PlanWave(context);
            GuardUnchanged(s, before, "PlanWave", wave);
            ValidatePlan(plan, wave);

            s.WaveIndex = wave;
            s.CurrentPlan = plan.Copy(); // our own copy: the planner cannot edit a running wave
            BuildSchedule(s, s.CurrentPlan);
            s.Occupancy.Reset();
            s.CurrentOutcome = new WaveOutcome
            {
                WaveIndex = s.WaveIndex,
                StrategyId = plan.StrategyId,
                CoreHpBefore = s.CoreHp,
                BudgetBefore = s.BuildBudget,
                TickStarted = s.Tick,
                FirstAgentId = s.Agents.Count,
            };

            s.Phase = SimPhase.Spawning;
            s.PhaseTicksRemaining = 0;
            events.Add(SimEvent.WaveStarted(s.WaveIndex));
        }

        private static void BuildSchedule(SimState s, WavePlan plan)
        {
            var slots = new SpawnSlot[plan.TotalAgents()];
            int k = 0;
            for (int g = 0; g < plan.Groups.Length; g++)
            {
                AgentGroup group = plan.Groups[g];
                for (int i = 0; i < group.Count; i++)
                {
                    slots[k++] = new SpawnSlot(s.Tick + group.StartDelayTicks + i * group.SpawnIntervalTicks, g, i);
                }
            }
            Array.Sort(slots, SpawnSlot.Compare);
            s.Schedule = slots;
            s.ScheduleCursor = 0;
        }

        private void SpawnDue(SimState s)
        {
            while (s.ScheduleCursor < s.Schedule.Length && s.Schedule[s.ScheduleCursor].Tick <= s.Tick)
            {
                SpawnSlot slot = s.Schedule[s.ScheduleCursor++];
                AgentGroup group = s.CurrentPlan.Groups[slot.Group];
                WorldPoint at = map.SpawnWorlds[group.SpawnIndex];

                // Agents enter at the spawn transform's XZ, not the tile centre -
                // exactly where WaveSpawner put them.
                var agent = new AgentState(s.Agents.Count, new Vec2f(at.X, at.Z), group.MoveSpeed, group.LifeTime,
                                           group.DigRate, config.DeathReward, config.WallBreakReward)
                {
                    WaveIndex = s.WaveIndex,
                };
                s.Agents.Add(agent);
                s.Live.Add(agent);
                s.CurrentOutcome.Spawned++;
                events.Add(SimEvent.AgentSpawned(agent.Id));
            }

            if (s.ScheduleCursor >= s.Schedule.Length) s.Phase = SimPhase.Resolving;
        }

        // Tallies this tick's agent-pass events into the open outcome. Under D4 only
        // one wave is ever live, so every such event belongs to the current wave.
        private void CountAgentEvents(SimState s, int firstEvent)
        {
            WaveOutcome o = s.CurrentOutcome;
            if (o == null) return;
            for (int i = firstEvent; i < events.Count; i++)
            {
                switch (events[i].Kind)
                {
                    case SimEventKind.AgentStalled: o.Stalled++; break;
                    case SimEventKind.AgentLeaked: o.Leaked++; break;
                    case SimEventKind.WallBreached: o.WallsBreached++; break;
                }
            }
        }

        private void CloseWave(SimState s, bool coreDestroyed)
        {
            // Finish the outcome on a COPY and tell the planner before committing it.
            // If OnWaveResolved throws, the wave is still open in the state and the
            // next Tick() closes it again. It used to be committed first; one throw
            // then left a wave that was "resolving" with no outcome, and every later
            // tick died on a null reference.
            WaveOutcome o = s.CurrentOutcome.Copy();
            o.TickResolved = s.Tick;
            o.CoreHpAfter = s.CoreHp;
            o.BudgetAfter = s.BuildBudget;
            o.CoreDestroyed = coreDestroyed;

            int pressured = 0;
            for (int id = o.FirstAgentId; id < s.Agents.Count; id++)
            {
                AgentState a = s.Agents[id];
                if (a.Leaked || (a.MinCostSeen != SimNode.Infinity && (float)(a.MinCostSeen / 10f) <= config.PressureRadiusTiles)) pressured++;
            }
            o.PressureCount = pressured;
            o.PressureShare = o.Spawned == 0 ? 0f : (float)pressured / o.Spawned;

            ulong before = StateHasher.Compute(s);
            planner.OnWaveResolved(o.Copy());
            GuardUnchanged(s, before, "OnWaveResolved", o.WaveIndex);

            s.LastOutcome = o;
            s.CurrentOutcome = null;
            events.Add(SimEvent.WaveResolved(o.WaveIndex));
        }

        // The mutation guard (ARCHITECTURE.md §4.4). Run on every planner call, not
        // only in DEBUG as first planned: it costs one state hash per wave boundary,
        // well inside G2's 16 ms, and a planner that writes to the state would
        // silently break I2 and I11.
        private void GuardUnchanged(SimState s, ulong before, string call, int wave)
        {
            if (StateHasher.Compute(s) != before)
            {
                throw new InvalidOperationException("[Sim] Planner '" + planner.Name + "' changed the simulation state inside " + call
                                                    + " (wave " + wave + ", tick " + s.Tick + "). Planners may only read WaveContext.State.");
            }
        }

        // The rules live in PlanValidator so SafePlanner can apply the same ones
        // without an exception; here a bad plan is a hard error.
        private void ValidatePlan(WavePlan plan, int wave)
        {
            string problem = PlanValidator.Check(plan, wave, map, config, pricer);
            if (problem != null) throw new InvalidOperationException("[Sim] Planner '" + planner.Name + "', wave " + wave + ": " + problem);
        }

        private static bool AnyLiveAgentOfWave(SimState s, int wave)
        {
            for (int i = 0; i < s.Live.Count; i++)
            {
                if (s.Live[i].WaveIndex == wave) return true;
            }
            return false;
        }

        // In-place, order-preserving: Live must stay in ascending id.
        private static void RemoveDead(List<AgentState> live)
        {
            int write = 0;
            for (int read = 0; read < live.Count; read++)
            {
                if (live[read].IsAlive) live[write++] = live[read];
            }
            if (write < live.Count) live.RemoveRange(write, live.Count - write);
        }
    }
}
