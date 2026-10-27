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
    // Tick() is ARCHITECTURE.md §4.2 with the combat steps of §4.6, step for step:
    // commands, phase machine, TOWER pass, AGENT pass, one field rebuild, the wave
    // backstop, the wave boundary, the core. Two behaviour changes from the original
    // Unity game, both intended and recorded in DEVLOG:
    //   * a breach rebuilds the flow field once at the end of the tick, not in the
    //     middle of the agent loop;
    //   * the next intermission starts when the wave's last agent resolves, not
    //     when it spawns (decision D4).
    // Since WP-C1 enemies have hit points and towers kill them; the lifetime clock
    // of the original game is gone (CLAUDE.md §2). Since WP-C2 an enemy is Ground,
    // Sapper or Flying, and "the field" is two fields kept in step by FlowFieldSet.
    public sealed class Simulation
    {
        private readonly SimConfig config;
        private readonly MapData map;
        private readonly ShapeDef[] library;
        private readonly TowerDef[] towerLibrary;
        private readonly GearDef[] gearLibrary = GearRoster.Default();
        private readonly IWavePlanner planner;
        private readonly IThreatPricer pricer;
        private readonly int maxWaveTicks;

        // Scratch only: reusable buffers, no state. Every rebuild goes through this,
        // so the ground and the sapper field always describe the same board.
        private readonly FlowFieldSet fields;
        private readonly List<SimEvent> events = new List<SimEvent>();

        // towerLibrary is the roster the shop draws its offers from. It may be empty
        // (a game with walls only), never null.
        public Simulation(SimConfig config, MapData map, ShapeDef[] shapeLibrary, TowerDef[] towerLibrary, ulong rngSeed, IWavePlanner planner, IThreatPricer pricer = null)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            InitialMap = map ?? throw new ArgumentNullException(nameof(map));
            this.map = map.Clone(); // our own: PlaceCore moves the core on it
            library = shapeLibrary ?? throw new ArgumentNullException(nameof(shapeLibrary));
            this.towerLibrary = towerLibrary ?? throw new ArgumentNullException(nameof(towerLibrary));
            this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
            this.pricer = pricer;
            RngSeed = rngSeed;

            config.Validate();
            map.Validate();
            map = this.map;
            if (map.Spawns.Length == 0) throw new InvalidOperationException("Map '" + map.Name + "' has no spawn; a simulation needs at least one.");
            for (int i = 0; i < towerLibrary.Length; i++)
            {
                if (towerLibrary[i] == null) throw new InvalidOperationException("Tower roster entry " + i + " is null.");
                towerLibrary[i].Validate();
            }
            maxWaveTicks = Math.Max(1, config.Ticks(config.MaxWaveSeconds));
            fields = new FlowFieldSet(config.SapperDigCostFactor);

            var s = new SimState
            {
                Grid = new SimGrid(map),
                BagRng = new Pcg32(rngSeed, RngStreams.Bag),
                SpawnRng = new Pcg32(rngSeed, RngStreams.Spawn),
                BuildBudget = config.StartBudget,
                CoreHp = config.CoreMaxHp,
                Core = map.Core,
                WallsLeft = config.WallStock,
                RelaysLeft = config.WallRelayPerWave,
                MovesLeft = config.MovesPerWave,
                Bench = NewBench(config.BenchSlots),
                GearInventory = NewBench(config.GearInventorySlots),
                Phase = SimPhase.Prep,
                PhaseTicksRemaining = config.Ticks(config.PrepSeconds),
            };
            s.Bag = new ShapeBag(library, s.BagRng);
            s.Occupancy = new OccupancyMap(s.Grid.NodeCount);
            s.Damage = new DamageMap(s.Grid.NodeCount);
            fields.Generate(s.Grid, map.Core);

            // The first offers are there before the first tick: the player builds
            // from them during the prep phase.
            s.Shop = RollShop(0, 0);
            s.GearShop = RollGearShop(0);

            State = s;
            phaseStart = s.Clone();
        }

        private Simulation(Simulation source, IWavePlanner planner)
        {
            config = source.config;
            map = source.map.Clone();
            InitialMap = source.InitialMap;
            library = source.library;
            towerLibrary = source.towerLibrary;
            pricer = source.pricer;
            maxWaveTicks = source.maxWaveTicks;
            fields = new FlowFieldSet(source.fields.SapperDigCostFactor); // scratch buffers only: never shared
            RngSeed = source.RngSeed;
            this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
            State = source.State.Clone();
            events.AddRange(source.events);
        }

        // Read-only outside Thesis.Sim by convention; planners are policed by the
        // mutation guard, views simply never write.
        // Replaced as a whole only by Undo and Redo; nothing outside should keep it across a tick.
        public SimState State { get; private set; }

        public SimConfig Config => config;

        public MapData Map => map;

        // The map as the run began. Map differs from it only in the core's tile, once
        // the player has placed it; a replay stores this one and replays the command.
        public MapData InitialMap { get; }

        public IWavePlanner Planner => planner;

        // The seed every RNG stream was derived from, and the shape masters the bag
        // draws from. A replay stores both; the masters are never mutated (the bag
        // only ever hands out clones), so this array is the library as constructed.
        public ulong RngSeed { get; }

        public ShapeDef[] ShapeLibrary => library;

        // Every tower type of the game. The shop draws its offers from it; an offer
        // names a tower by its index here. Pure data, never changed during a run; a
        // replay stores it next to the shapes.
        public TowerDef[] TowerLibrary => towerLibrary;

        // Every piece of gear. A gear card and a tower name a piece by its index here.
        public GearDef[] GearLibrary => gearLibrary;

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
            fields.Generate(State.Grid, map.Core);
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
                ApplyRetryOnly(s);
                return;
            }
            ApplyPending(s);
        }

        public void Tick()
        {
            events.Clear();
            SimState s = State;

            // 0. A finished game never advances. It takes one command: RetryWave.
            if (s.Phase == SimPhase.GameOver)
            {
                ApplyRetryOnly(s);
                return;
            }

            // 1. Player input. An Undo or a Redo may have put another state in place.
            ApplyPending(s);
            s = State;

            // 2. Phase machine. The countdown is only written when no wave starts:
            // BeginWave asks the planner first and may throw, and a tick that throws
            // there must leave the state as it found it, so the next Tick() can try
            // again (see BeginWave). When the wave does start, BeginWave zeroes the
            // countdown itself, so the result is the same as decrementing first.
            if (s.Phase == SimPhase.Prep || s.Phase == SimPhase.Intermission)
            {
                // With PlanAhead the next wave is decided as the build phase begins. If
                // the planner throws, nothing has changed and the next tick asks again.
                if (config.PlanAhead && s.NextPlan == null) s.NextPlan = PlanNextWave(s);

                // With WaitForStart the build phase has no clock: only the player ends it.
                if (s.StartWaveRequested || (!config.WaitForStart && s.PhaseTicksRemaining - 1 <= 0)) BeginWave(s);
                else if (!config.WaitForStart) s.PhaseTicksRemaining--;
            }
            s.StartWaveRequested = false; // consumed, or meaningless mid-wave
            if (s.Phase == SimPhase.Spawning) SpawnDue(s);

            // 3. Tower pass (ascending tower id), before anything moves. Every hit
            // resolves inside this tick (CLAUDE.md I12).
            SimNode core = s.Grid.Get(map.Core);
            int firstCombatEvent = events.Count;
            TowerSystem.Step(s.Grid, core, s.Towers, s.Live, s.Damage, s.CurrentOutcome == null ? null : s.CurrentOutcome.DamageByType,
                             config.MinSlowFactor, ref s.BuildBudget, events, config.TickSeconds);

            // 4. Agent pass (ascending agent id). Agents killed in step 3 are skipped.
            // Each moves by its own class: ground field, sapper field, or a straight
            // line through the air (WP-C2).
            bool fieldDirty = AgentSystem.Step(s.Grid, core, s.Live, s.Towers, config.TickSeconds, s.Occupancy, config.TowerBreachReward,
                                               ref s.BuildBudget, ref s.CoreHp, events, config);
            CountCombatEvents(s, firstCombatEvent);
            RemoveDead(s.Live);

            // One rebuild per tick (both fields), after every agent has acted. A tower or
            // a collector chewed through can take power from others: worked out again now,
            // so the towers that lost it are dark from the next tick.
            if (fieldDirty)
            {
                fields.Generate(s.Grid, map.Core);
                RecomputePower(s);
            }

            // 4b. The backstop that replaces what the lifetime clock used to
            // guarantee: a wave that has run for MaxWaveSeconds is ended by force.
            if (s.CurrentOutcome != null && s.Tick - s.CurrentOutcome.TickStarted >= maxWaveTicks) TimeOutWave(s);

            // 5. Wave boundary (D4): only once every agent of the wave is resolved.
            if (s.Phase == SimPhase.Resolving && s.CoreHp > 0 && !AnyLiveAgentOfWave(s, s.WaveIndex))
            {
                CloseWave(s, coreDestroyed: false);
                // Stipend at intermission start, as WaveSpawner.IntermissionRoutine did, and
                // the ore of every collector that stood through the wave (2026-10-14).
                s.BuildBudget += config.StipendPerWave + CollectorIncome(s);
                if (config.WallStock > 0) s.WallsLeft += config.WallStockPerWave;
                s.RelaysLeft = config.WallRelayPerWave;
                s.MovesLeft = config.MovesPerWave;
                s.MovesBought = 0;
                s.PhaseLogStart = s.PlacementLog.Count;
                // Siege damage lasts one wave: every tower is whole again for the build phase.
                foreach (TowerState tower in s.Towers) { tower.SiegeDamage = 0f; tower.KnockedOut = false; }
                s.Phase = SimPhase.Intermission;
                s.PhaseTicksRemaining = config.Ticks(config.IntermissionSeconds);
                phaseStart = s.Clone();
                phaseStart.Pending.Clear();

                // New offers for the build phase that starts now. They stay through
                // the next wave, until that one resolves.
                s.Shop = RollShop(s.WaveIndex, 0);
                events.Add(SimEvent.ShopRolled(s.Shop.WaveIndex, s.Shop.RerollCount));
                if (config.GearShopSlots > 0 && config.GearShopRefreshWaves > 0 && s.WaveIndex % config.GearShopRefreshWaves == 0)
                    s.GearShop = RollGearShop(s.WaveIndex / config.GearShopRefreshWaves);
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
            // Each command goes to the state as it is now: an Undo replaces it on the way.
            // The commands are taken out first: a state put back by Undo or Redo may be the
            // one whose list this was, and putting it back empties its list.
            batch.Clear();
            batch.AddRange(s.Pending);
            s.Pending.Clear();
            for (int i = 0; i < batch.Count; i++) Apply(State, batch[i]);
            State.Pending.Clear();
        }

        private readonly List<SimCommand> batch = new List<SimCommand>();

        private void ApplyRetryOnly(SimState s)
        {
            batch.Clear();
            batch.AddRange(s.Pending);
            s.Pending.Clear();
            // While the game is lost only a retry counts; once it has put a build phase
            // back, the commands after it apply as usual (a replay hands them over together).
            for (int i = 0; i < batch.Count; i++)
            {
                if (State.Phase == SimPhase.GameOver) { if (batch[i].Kind == SimCommandKind.RetryWave) Retry(State); }
                else Apply(State, batch[i]);
            }
            State.Pending.Clear();
        }

        // --- playing a wave again (2026-10-24) ---

        // The state as each build phase began: the current one, and the one before the
        // last wave, which RetryWave puts back. Like the undo stacks they follow from
        // the commands and are not hashed.
        private SimState phaseStart;
        private SimState retryStart;

        // Any time once a wave has begun: in its build phase, while it runs (2026-10-27, the
        // student: restart a wave that is going badly), and after it, won or lost.
        public bool CanRetry => retryStart != null;

        // The wave a retry plays again.
        public int RetryWaveNumber => retryStart != null ? retryStart.WaveIndex + 1 : 0;

        private void Retry(SimState s)
        {
            if (!CanRetry) return;
            SimState back = retryStart.Clone();
            back.Tick = s.Tick; // the clock does not go back
            back.Retries = s.Retries + 1;
            back.Pending.Clear();
            undoStack.Clear();
            redoStack.Clear();
            // From a build phase the retry itself can be taken back.
            if (InBuildPhase(s) && config.UndoSteps > 0) undoStack.Add(s);
            State = back;
            phaseStart = retryStart;
            if (map.Core != back.Core)
            {
                SimNode node = back.Grid.Get(back.Core);
                map.Core = back.Core;
                map.CoreWorld = new WorldPoint(node.Position.X, map.CoreWorld != null ? map.CoreWorld.Y : 0f, node.Position.Y);
            }
            events.Add(SimEvent.StateRestored());
        }

        // --- undo and redo (SimConfig.UndoSteps, 2026-10-24) ---

        // Whole copies of the state before each action of this build phase that changed
        // anything, and the ones taken back. Not part of the hashed state: they follow
        // from the commands, so a replay rebuilds them exactly. Emptied when a wave starts:
        // only the current build phase can be taken back.
        private readonly List<SimState> undoStack = new List<SimState>();
        private readonly List<SimState> redoStack = new List<SimState>();

        public int UndoCount => undoStack.Count;

        public int RedoCount => redoStack.Count;

        private void Restore(SimState s, List<SimState> from, List<SimState> to)
        {
            if (from.Count == 0 || !InBuildPhase(s)) return;
            SimState back = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            to.Add(s);
            // The clock does not go back with the board.
            back.Tick = s.Tick;
            back.PhaseTicksRemaining = s.PhaseTicksRemaining;
            back.Pending.Clear();
            State = back;
            if (map.Core != back.Core)
            {
                SimNode node = back.Grid.Get(back.Core);
                map.Core = back.Core;
                map.CoreWorld = new WorldPoint(node.Position.X, map.CoreWorld != null ? map.CoreWorld.Y : 0f, node.Position.Y);
            }
            events.Add(SimEvent.StateRestored());
        }

        private void Apply(SimState s, SimCommand command)
        {
            if (command.Kind == SimCommandKind.Undo) { Restore(s, undoStack, redoStack); return; }
            if (command.Kind == SimCommandKind.RetryWave) { Retry(s); return; }
            if (command.Kind == SimCommandKind.Redo) { Restore(s, redoStack, undoStack); return; }
            if (config.UndoSteps <= 0 || !InBuildPhase(s) || command.Kind == SimCommandKind.StartWaveNow)
            {
                ApplyCommand(s, command);
                return;
            }
            // Kept only if the action changed something: an undo never takes back nothing.
            ulong before = StateHasher.Compute(s);
            SimState copy = s.Clone();
            copy.Pending.Clear();
            ApplyCommand(s, command);
            if (StateHasher.Compute(s) == before) return;
            undoStack.Add(copy);
            if (undoStack.Count > config.UndoSteps) undoStack.RemoveAt(0);
            redoStack.Clear();
        }

        private void ApplyCommand(SimState s, SimCommand command)
        {
            switch (command.Kind)
            {
                case SimCommandKind.PlaceShape:
                    if (config.WallStock > 0)
                    {
                        // From the stock: no budget, one tile of stock per tile laid.
                        int need = s.Bag.CurrentShape != null ? s.Bag.CurrentShape.LocalTiles.Length : 0;
                        if (need > 0 && s.WallsLeft >= need
                            && Placement.TryPlace(s.Grid, map, fields, s.Bag.CurrentShape, s.Bag.CurrentRotationTurns,
                                                  new TileCoord(command.X, command.Y), ref s.BuildBudget, s.Tick, s.PlacementLog, events, free: true))
                        {
                            s.WallsLeft -= need;
                            s.Bag.PullNextShape();
                        }
                        break;
                    }
                    if (Placement.TryPlace(s.Grid, map, fields, s.Bag.CurrentShape, s.Bag.CurrentRotationTurns,
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
                    if ((s.Phase == SimPhase.Prep || s.Phase == SimPhase.Intermission) && s.GearOverflow.Count == 0) s.StartWaveRequested = true;
                    break;
                case SimCommandKind.PlaceTower:
                    // A is the shop's offer slot. A slot that does not exist or is
                    // already bought is ignored, like an illegal tile or an empty
                    // budget: a command records what the player tried, and a try
                    // that cannot work changes nothing.
                    if (command.A >= 0 && command.A < s.Shop.Offers.Length)
                    {
                        TowerOffer offer = s.Shop.Offers[command.A];
                        if (!offer.Bought && s.Shop.MayPick && offer.Kind == CardKind.Tower
                            && TowerCombine.HasRoomFor(s.Towers, towerLibrary[offer.TowerIndex], offer.Level, config, s.WaveIndex)
                            && Placement.TryPlaceTower(s.Grid, map, fields, towerLibrary[offer.TowerIndex], offer.Price,
                                                       new TileCoord(command.X, command.Y), ref s.BuildBudget, s.Tick,
                                                       s.Towers, s.PlacementLog, events, offer.Level, config.TowersOnWalls))
                        {
                            offer.Bought = true; // each slot once
                            Picked(s);
                            TowerCombine.Combine(s.Grid, map, fields, s.Towers[s.Towers.Count - 1], config, gearLibrary, ref s.BuildBudget, s.Tick,
                                                 s.Towers, s.PlacementLog, events);
                        }
                    }
                    break;
                case SimCommandKind.TakeCoin:
                    if (command.A >= 0 && command.A < s.Shop.Offers.Length)
                    {
                        TowerOffer card = s.Shop.Offers[command.A];
                        if (!card.Bought && s.Shop.MayPick && card.Kind == CardKind.Wall)
                        {
                            s.WallsLeft += card.Amount;
                            s.PlacementLog.Add(new PlacementRecord { Kind = PlacementKind.WallsGained, Tick = s.Tick, ShapeName = "walls", Refund = card.Amount });
                            card.Bought = true;
                            Picked(s);
                        }
                        else if (!card.Bought && s.Shop.MayPick && card.Kind == CardKind.Heal && s.CoreHp < config.CoreMaxHp)
                        {
                            int before = s.CoreHp;
                            s.CoreHp = Math.Min(config.CoreMaxHp, s.CoreHp + card.Amount);
                            s.PlacementLog.Add(new PlacementRecord { Kind = PlacementKind.CoreHealed, Tick = s.Tick, ShapeName = "heal", Refund = s.CoreHp - before });
                            card.Bought = true;
                            Picked(s);
                        }
                        else if (!card.Bought && s.Shop.MayPick && card.Kind == CardKind.Coin)
                        {
                            TowerUpgrades.TakeCoin(card, ref s.BuildBudget, s.Tick, s.PlacementLog);
                            card.Bought = true;
                            Picked(s);
                        }
                    }
                    break;
                case SimCommandKind.BuyGear:
                    if (command.A >= 0 && command.A < s.Shop.Offers.Length)
                    {
                        TowerOffer card = s.Shop.Offers[command.A];
                        if (!card.Bought && s.Shop.MayPick && card.Kind == CardKind.Gear && !gearLibrary[card.GearIndex].Extracts
                            && TowerUpgrades.TryBuyGear(s.Grid, new TileCoord(command.X, command.Y), card.GearIndex, card.Price, gearLibrary,
                                                        ref s.BuildBudget, s.Tick, s.Towers, s.PlacementLog, events))
                        {
                            card.Bought = true;
                            Picked(s);
                        }
                    }
                    break;
                case SimCommandKind.BuyShopGear:
                    if (command.A >= 0 && command.A < s.GearShop.Offers.Length)
                    {
                        TowerOffer item = s.GearShop.Offers[command.A];
                        if (!item.Bought && !gearLibrary[item.GearIndex].Extracts
                            && TowerUpgrades.TryBuyGear(s.Grid, new TileCoord(command.X, command.Y), item.GearIndex, item.Price, gearLibrary,
                                                        ref s.BuildBudget, s.Tick, s.Towers, s.PlacementLog, events))
                        {
                            item.Bought = true;
                        }
                    }
                    break;
                case SimCommandKind.MoveGear:
                    if (config.MoveGearDuringWave || s.Phase == SimPhase.Prep || s.Phase == SimPhase.Intermission)
                    {
                        TowerUpgrades.TryMoveGear(s.Grid, new TileCoord(command.X, command.Y), command.A, new TileCoord(command.B, command.C), gearLibrary,
                                                  s.Tick, s.Towers, s.PlacementLog, events);
                    }
                    break;
                case SimCommandKind.MoveTower:
                    if (config.MoveTowers && InBuildPhase(s) && HasMove(s)
                        && Placement.TryMoveTower(s.Grid, map, fields, new TileCoord(command.X, command.Y), new TileCoord(command.B, command.C), s.Tick,
                                                  s.Towers, s.PlacementLog, events, config.TowersOnWalls, config.PylonCoreKeepOut))
                    {
                        UseMove(s);
                    }
                    break;
                case SimCommandKind.PlaceCore: PlaceCore(s, new TileCoord(command.X, command.Y)); break;
                case SimCommandKind.BuyWalls:
                    if (config.WallStock > 0 && config.WallPackSize > 0 && s.BuildBudget >= config.WallPackPrice)
                    {
                        s.BuildBudget -= config.WallPackPrice;
                        s.WallsLeft += config.WallPackSize;
                        s.PlacementLog.Add(new PlacementRecord { Kind = PlacementKind.WallsGained, Tick = s.Tick, ShapeName = "walls", Cost = config.WallPackPrice });
                    }
                    break;
                case SimCommandKind.BuyMove:
                    if (config.MovesPerWave > 0 && config.ExtraMovePrice >= 0f && InBuildPhase(s) && s.BuildBudget >= ExtraMovePrice)
                    {
                        float price = ExtraMovePrice;
                        s.BuildBudget -= price;
                        s.MovesLeft++;
                        s.MovesBought++;
                        s.PlacementLog.Add(new PlacementRecord { Kind = PlacementKind.MoveBought, Tick = s.Tick, ShapeName = "move", Cost = price });
                    }
                    break;
                case SimCommandKind.BenchCard: BenchCard(s, command.A, command.B); break;
                case SimCommandKind.DeployTower: DeployTower(s, command.A, new TileCoord(command.X, command.Y)); break;
                case SimCommandKind.RecallTower: RecallTower(s, new TileCoord(command.X, command.Y), command.A); break;
                case SimCommandKind.SwapBench:
                    if (OnBenchRange(s, command.A) && OnBenchRange(s, command.B))
                    {
                        int held = s.Bench[command.A];
                        s.Bench[command.A] = s.Bench[command.B];
                        s.Bench[command.B] = held;
                    }
                    break;
                case SimCommandKind.SellBenched:
                    if (OnBenchRange(s, command.A) && s.Bench[command.A] != TowerBench.Empty && MaySell(s))
                    {
                        TowerState sold = s.Towers[s.Bench[command.A]];
                        float refund = (float)(sold.PricePaid * config.SellRefund);
                        ReturnGear(s, sold);
                        sold.IsAlive = false;
                        sold.Sold = true;
                        sold.OnBench = false;
                        s.Bench[command.A] = TowerBench.Empty;
                        s.BuildBudget += refund;
                        s.PlacementLog.Add(new PlacementRecord { Kind = PlacementKind.TowerSold, Tick = s.Tick, ShapeName = sold.Def.Id, OriginX = -1, OriginY = -1, TowerId = sold.Id, Refund = refund });
                    }
                    break;
                case SimCommandKind.FeedCard: FeedCard(s, command); break;
                case SimCommandKind.BuildCollector: BuildCollector(s, new TileCoord(command.X, command.Y)); break;
                case SimCommandKind.BuyTower: BuyTower(s, command.A, new TileCoord(command.X, command.Y)); break;
                case SimCommandKind.Research: Research(s); break;
                case SimCommandKind.CraftGear:
                    if (config.BuildMenu && command.A >= 0 && command.A < gearLibrary.Length && gearLibrary[command.A].OnSale(config)
                        && gearLibrary[command.A].Tier <= s.ResearchLevel)
                        StoreGear(s, command.A, CraftPrice(command.A));
                    break;
                case SimCommandKind.RepairCore:
                    if (config.RepairPrice >= 0f && s.CoreHp < config.CoreMaxHp && s.BuildBudget >= config.RepairPrice && InBuildPhase(s))
                    {
                        int before = s.CoreHp;
                        s.BuildBudget -= config.RepairPrice;
                        s.CoreHp = Math.Min(config.CoreMaxHp, s.CoreHp + config.HealCardAmount);
                        s.PlacementLog.Add(new PlacementRecord { Kind = PlacementKind.CoreHealed, Tick = s.Tick, ShapeName = "repair", Cost = config.RepairPrice, Refund = s.CoreHp - before });
                    }
                    break;
                case SimCommandKind.MergeTower: MergeTower(s, command); break;
                case SimCommandKind.ChooseUpgrade:
                    if (config.ChoiceUpgradeCost >= 0f && InBuildPhase(s) && command.A >= 0)
                    {
                        TowerState chosen = GearTarget(s, command);
                        float price = chosen != null ? UpgradePrice(chosen) : 0f;
                        if (chosen != null && TowerUpgradeChoices.Allowed(chosen, command.A) && chosen.UpgradeCount < UpgradesAllowed(s) && s.BuildBudget >= price)
                        {
                            s.BuildBudget -= price;
                            if (chosen.Upgrade == TowerUpgradeChoices.None) chosen.Upgrade = command.A;
                            else chosen.Upgrade2 = command.A;
                            chosen.PricePaid += (int)price;
                            chosen.Recompute(gearLibrary);
                            TileCoord at = chosen.OnBench ? TowerBench.NoTile : chosen.Tile;
                            s.PlacementLog.Add(new PlacementRecord
                            {
                                Kind = PlacementKind.TowerUpgraded, Tick = s.Tick, ShapeName = chosen.Def.Id + "+" + TowerUpgradeChoices.IdOf(chosen.Def, command.A), OriginX = at.X, OriginY = at.Y,
                                Tiles = chosen.OnBench ? new TileCoord[0] : new[] { at }, TowerId = chosen.Id, Cost = price,
                            });
                            events.Add(SimEvent.TowerChanged(chosen.Id));
                        }
                    }
                    break;
                case SimCommandKind.SetPowerSource:
                    TiePower(s, command);
                    break;
                case SimCommandKind.SetTargeting:
                    if (config.ChooseTargeting && command.A >= 0 && command.A <= (int)TargetingMode.Raiders)
                    {
                        TowerState aimed = GearTarget(s, command);
                        if (aimed != null) aimed.Mode = (TargetingMode)command.A;
                    }
                    break;
                case SimCommandKind.RerollGearShop:
                    if (config.GearShopSlots > 0 && config.GearShopRerollCost >= 0f && s.BuildBudget >= GearRerollPrice)
                    {
                        float cost = GearRerollPrice;
                        s.BuildBudget -= cost;
                        int stock = s.GearShop.WaveIndex, rerolls = s.GearShop.RerollCount + 1;
                        s.GearShop = new ShopState { WaveIndex = stock, RerollCount = rerolls, Offers = ShopRoller.RollGearShop(RngSeed, stock, gearLibrary, config.GearShopSlots, rerolls, config) };
                        s.PlacementLog.Add(new PlacementRecord { Kind = PlacementKind.GearShopReroll, Tick = s.Tick, ShapeName = "gear", Cost = cost });
                    }
                    break;
                case SimCommandKind.EquipOverflow:
                    if (command.A >= 0 && command.A < s.GearOverflow.Count)
                    {
                        TowerState onto = GearTarget(s, command);
                        int free = onto != null ? onto.FreeGearSlot() : -1;
                        if (free >= 0)
                        {
                            int spare = s.GearOverflow[command.A];
                            s.GearOverflow.RemoveAt(command.A);
                            onto.Gear[free] = spare;
                            onto.Recompute(gearLibrary);
                            LogGear(s, PlacementKind.GearEquipped, spare, onto);
                        }
                    }
                    break;
                case SimCommandKind.SellOverflow:
                    if (command.A >= 0 && command.A < s.GearOverflow.Count)
                    {
                        int spare = s.GearOverflow[command.A];
                        float back = (float)(gearLibrary[spare].Price * config.SellRefund);
                        s.GearOverflow.RemoveAt(command.A);
                        s.BuildBudget += back;
                        s.PlacementLog.Add(new PlacementRecord { Kind = PlacementKind.GearSold, Tick = s.Tick, ShapeName = gearLibrary[spare].Id, OriginX = -1, OriginY = -1, Refund = back });
                    }
                    break;
                case SimCommandKind.StoreGearCard:
                    if (command.A >= 0 && command.A < s.Shop.Offers.Length)
                    {
                        TowerOffer card = s.Shop.Offers[command.A];
                        if (!card.Bought && s.Shop.MayPick && card.Kind == CardKind.Gear && StoreGear(s, card.GearIndex, card.Price))
                        {
                            card.Bought = true;
                            Picked(s);
                        }
                    }
                    break;
                case SimCommandKind.StoreShopGear:
                    if (command.A >= 0 && command.A < s.GearShop.Offers.Length)
                    {
                        TowerOffer item = s.GearShop.Offers[command.A];
                        if (!item.Bought && StoreGear(s, item.GearIndex, item.Price)) item.Bought = true;
                    }
                    break;
                case SimCommandKind.EquipGear: EquipGear(s, command); break;
                case SimCommandKind.UnequipGear: UnequipGear(s, command); break;
                case SimCommandKind.UpgradeTower:
                    // A factor of 0 switches paid upgrades off (levels then come from combining only).
                    if (config.UpgradeCostFactor > 0f)
                    TowerUpgrades.TryUpgrade(s.Grid, new TileCoord(command.X, command.Y), config, gearLibrary, ref s.BuildBudget, s.Tick,
                                             s.Towers, s.PlacementLog, events);
                    break;
                case SimCommandKind.SellTower:
                    TowerState toSell = TowerOn(s, new TileCoord(command.X, command.Y));
                    if (MaySell(s) && HasMove(s)
                        && Placement.TrySellTower(s.Grid, map, fields, new TileCoord(command.X, command.Y), config.SellRefund,
                                                  ref s.BuildBudget, s.Tick, s.Towers, s.PlacementLog, events))
                    {
                        UseMove(s);
                        if (toSell != null) ReturnGear(s, toSell);
                    }
                    break;
                case SimCommandKind.SellWall:
                    if (SellCollector(s, new TileCoord(command.X, command.Y))) break;
                    if (config.WallStock > 0)
                    {
                        TakeUpWall(s, new TileCoord(command.X, command.Y));
                        break;
                    }
                    if (MaySell(s))
                    {
                        Placement.TrySellWall(s.Grid, map, fields, new TileCoord(command.X, command.Y), config.WallSellRefund,
                                              ref s.BuildBudget, s.Tick, s.PlacementLog, events);
                    }
                    break;
                case SimCommandKind.RerollShop:
                    // Off unless the config puts a price on it. The new offers are
                    // the next roll of the SAME wave, so they too depend on nothing
                    // but the seed and the two counters (I10).
                    // In a draft the picks not yet used carry over to the new cards, and
                    // with none left there is nothing to reroll for.
                    if (config.RerollCost >= 0f && s.Shop.MayPick && s.BuildBudget >= RerollPrice)
                    {
                        float price = RerollPrice;
                        int picks = s.Shop.PicksLeft;
                        s.BuildBudget -= price;
                        s.Shop = RollShop(s.Shop.WaveIndex, s.Shop.RerollCount + 1);
                        s.Shop.PicksLeft = picks;
                        s.PlacementLog.Add(new PlacementRecord { Kind = PlacementKind.ShopReroll, Tick = s.Tick, Cost = price });
                        events.Add(SimEvent.ShopRolled(s.Shop.WaveIndex, s.Shop.RerollCount));
                    }
                    break;
                default:
                    throw new InvalidOperationException("[Sim] Unknown command kind " + command.Kind + ".");
            }

            SyncTowerTerrain(s);
            RecomputePower(s);

            // Spare gear takes the first inventory slot that has become free.
            while (s.GearOverflow.Count > 0)
            {
                int free = Array.IndexOf(s.GearInventory, TowerState.NoGear);
                if (free < 0) break;
                s.GearInventory[free] = s.GearOverflow[0];
                s.GearOverflow.RemoveAt(0);
            }
        }

        // What the next reroll costs: the base price, and the step for every reroll
        // already made of this wave's cards. Meaningless while rerolls are off.
        public float RerollPrice => (float)(config.RerollCost + (float)(config.RerollCostStep * State.Shop.RerollCount));

        // How many towers may stand right now; 0 = no limit.
        public int TowerCap => TowerCombine.CapAt(config, State.WaveIndex);

        // What the next reroll of the gear shop costs. Meaningless while it cannot be rerolled.
        public float GearRerollPrice => (float)(config.GearShopRerollCost + (float)(config.GearShopRerollStep * State.GearShop.RerollCount));

        // What `tower`'s next upgrade costs: the base price and the step for each one it has.
        public float UpgradePrice(TowerState tower)
        {
            return (float)(config.ChoiceUpgradeCost + (float)(config.ChoiceUpgradeCostStep * tower.UpgradeCount));
        }

        // --- the ore economy (2026-10-14) ---

        // The ore the standing collectors pay after a wave: each its deposit's yield.
        public int CollectorIncome(SimState s)
        {
            int ore = 0;
            for (int d = 0; d < map.Deposits.Length; d++)
            {
                Deposit deposit = map.Deposits[d];
                if (s.Grid.Get(deposit.Tile).Occupant != Occupant.Collector) continue;
                ore += deposit.Yield;
                if (NearBeacon(s, deposit.Tile)) ore++;
            }
            return ore;
        }

        // A standing tower with beacon gear within GearDef.BeaconRange tiles of `tile`.
        public static bool NearBeacon(SimState s, TileCoord tile)
        {
            int reach = (int)(float)(GearDef.BeaconRange * GearDef.BeaconRange);
            for (int t = 0; t < s.Towers.Count; t++)
            {
                TowerState tower = s.Towers[t];
                if (!tower.IsAlive || tower.OnBench || !tower.Beacon) continue;
                int dx = tower.Tile.X - tile.X, dy = tower.Tile.Y - tile.Y;
                if (dx * dx + dy * dy <= reach) return true;
            }
            return false;
        }

        // A tower's tile follows its gear (lure gear: marked for the lure field, and three
        // times as tough). Run after every command, so equipping, taking off,
        // moving and deploying all end with the right tile; the fields are rebuilt only
        // when a tile changed. A tower on a rock is static geometry and is left alone.
        private void SyncTowerTerrain(SimState s)
        {
            bool changed = false;
            for (int t = 0; t < s.Towers.Count; t++)
            {
                TowerState tower = s.Towers[t];
                if (!tower.IsAlive || tower.OnBench) continue;
                SimNode node = s.Grid.Get(tower.Tile);
                if (!node.IsWalkable || node.Occupant != Occupant.Tower || node.TowerId != tower.Id) continue;
                float full = Math.Max(0.5f, tower.Def.TowerHealth);
                if (tower.Lure) full = (float)(full * GearDef.LureHealthFactor);
                if (node.Lure == tower.Lure && node.MaxWallHealth == full) continue;
                // Damage taken so far is kept as a share of the whole.
                float share = node.MaxWallHealth > 0f ? (float)(node.WallHealth / node.MaxWallHealth) : 1f;
                node.Lure = tower.Lure;
                node.MaxWallHealth = full;
                node.WallHealth = (float)(full * share);
                changed = true;
            }
            if (changed) fields.Generate(s.Grid, map.Core);
        }

        // --- power (SimConfig.PowerRule, 2026-10-20) ---

        // Which towers have power. The sources are the core, then every standing collector
        // in deposit order, each with so many places for towers within its radius. As many
        // towers as possible are given power (a matching, found by augmenting paths), and the
        // order of towers by id and of sources as listed decides between equal answers, so
        // the result is the same on every runtime. A greedy pass was not enough: a tower in
        // reach of both the core and a collector could take a core place that a tower in
        // reach of the core only needed.
        private readonly List<TileCoord> powerSource = new List<TileCoord>();
        private readonly List<float> powerRadius = new List<float>();
        private readonly List<int> powerSlotSource = new List<int>();
        private int[] slotTower = new int[0];
        private bool[] slotSeen = new bool[0];
        private bool[] slotFixed = new bool[0];

        public void RecomputePower(SimState s)
        {
            List<TowerState> towers = s.Towers;
            if (!config.PowerRule)
            {
                for (int t = 0; t < towers.Count; t++) towers[t].Powered = true;
                return;
            }

            powerSource.Clear();
            powerRadius.Clear();
            powerSlotSource.Clear();
            AddSource(map.Core, config.CorePowerRadius, config.CorePowerCapacity);
            for (int d = 0; d < map.Deposits.Length; d++)
                if (s.Grid.Get(map.Deposits[d].Tile).Occupant == Occupant.Collector)
                    AddSource(map.Deposits[d].Tile, config.CollectorPowerRadius, config.CollectorPowerCapacity);
            for (int t = 0; t < towers.Count; t++)
                if (towers[t].IsAlive && !towers[t].OnBench && towers[t].PowerCapacity > 0)
                    AddSource(towers[t].Tile, towers[t].PowerRadius, towers[t].PowerCapacity);

            int slots = powerSlotSource.Count;
            if (slotTower.Length < slots) { slotTower = new int[slots]; slotSeen = new bool[slots]; slotFixed = new bool[slots]; }
            for (int k = 0; k < slots; k++) { slotTower[k] = -1; slotFixed[k] = false; }
            for (int t = 0; t < towers.Count; t++) { towers[t].Powered = false; towers[t].PoweredBy = TowerState.NoTile; }

            // First the towers the player tied to a source, in id order, each to a free place of
            // that source while it stands and reaches; those places are then fixed. A tie that
            // cannot be kept leaves the tower to the matching like any other.
            for (int t = 0; t < towers.Count; t++)
            {
                TowerState tower = towers[t];
                if (!tower.IsAlive || tower.OnBench || tower.PowerCapacity > 0 || tower.PowerPin.X < 0) continue;
                for (int k = 0; k < slots; k++)
                {
                    int source = powerSlotSource[k];
                    if (slotTower[k] >= 0 || !powerSource[source].Equals(tower.PowerPin)) continue;
                    if (!InPowerReach(powerSource[source], powerRadius[source], tower.Tile)) break;
                    slotTower[k] = t;
                    slotFixed[k] = true;
                    break;
                }
            }

            for (int t = 0; t < towers.Count; t++)
            {
                TowerState tower = towers[t];
                if (!tower.IsAlive || tower.OnBench) continue;
                if (tower.PowerCapacity > 0) { tower.Powered = true; continue; } // a power tower powers itself
                if (Holds(t, slots)) continue;
                for (int k = 0; k < slots; k++) slotSeen[k] = slotFixed[k];
                Augment(t, towers, slots);
            }
            for (int k = 0; k < slots; k++)
                if (slotTower[k] >= 0)
                {
                    towers[slotTower[k]].Powered = true;
                    towers[slotTower[k]].PoweredBy = powerSource[powerSlotSource[k]];
                }
        }

        private bool Holds(int t, int slots)
        {
            for (int k = 0; k < slots; k++) if (slotFixed[k] && slotTower[k] == t) return true;
            return false;
        }

        // SetPowerSource: tie a tower to a source in reach, or untie it (A < 0). A source tied to
        // more towers than it powers lets go of its oldest tie, so the newest choice is kept:
        // choosing a tower for a full collector swaps it in.
        private void TiePower(SimState s, SimCommand command)
        {
            if (!config.PowerRule || !s.Grid.InBounds(new TileCoord(command.X, command.Y))) return;
            SimNode node = s.Grid.Get(new TileCoord(command.X, command.Y));
            if (node.Occupant != Occupant.Tower) return;
            TowerState tower = s.Towers[node.TowerId];
            if (!tower.IsAlive || tower.OnBench || tower.PowerCapacity > 0) return;
            if (command.A < 0) { tower.PowerPin = TowerState.NoTile; RecomputePower(s); return; }

            var at = new TileCoord(command.A, command.B);
            float radius; int capacity;
            if (!SourceAt(s, at, out radius, out capacity) || !InPowerReach(at, radius, tower.Tile)) return;
            int order = 0;
            foreach (TowerState t in s.Towers) order = Math.Max(order, t.PowerPinOrder);
            tower.PowerPin = at;
            tower.PowerPinOrder = order + 1;
            while (true)
            {
                TowerState oldest = null;
                int tied = 0;
                foreach (TowerState t in s.Towers)
                {
                    if (!t.IsAlive || t.OnBench || !t.PowerPin.Equals(at)) continue;
                    tied++;
                    if (t != tower && (oldest == null || t.PowerPinOrder < oldest.PowerPinOrder)) oldest = t;
                }
                if (tied <= capacity || oldest == null) break;
                oldest.PowerPin = TowerState.NoTile;
            }
            RecomputePower(s);
        }

        // The power source standing on this tile now, with its reach and its room.
        private bool SourceAt(SimState s, TileCoord at, out float radius, out int capacity)
        {
            radius = 0f; capacity = 0;
            if (at.Equals(map.Core)) { radius = config.CorePowerRadius; capacity = config.CorePowerCapacity; return true; }
            if (!s.Grid.InBounds(at)) return false;
            SimNode node = s.Grid.Get(at);
            if (node.Occupant == Occupant.Collector) { radius = config.CollectorPowerRadius; capacity = config.CollectorPowerCapacity; return true; }
            if (node.Occupant == Occupant.Tower)
            {
                TowerState pylon = s.Towers[node.TowerId];
                if (pylon.IsAlive && pylon.PowerCapacity > 0) { radius = pylon.PowerRadius; capacity = pylon.PowerCapacity; return true; }
            }
            return false;
        }

        private void AddSource(TileCoord tile, float radius, int capacity)
        {
            int index = powerSource.Count;
            powerSource.Add(tile);
            powerRadius.Add(radius);
            for (int c = 0; c < capacity; c++) powerSlotSource.Add(index);
        }

        // Whether the source can reach the tower: distance between tile centres, in tiles.
        public static bool InPowerReach(TileCoord source, float radius, TileCoord tower)
        {
            int dx = source.X - tower.X, dy = source.Y - tower.Y;
            return (float)(dx * dx + dy * dy) <= (float)(radius * radius);
        }

        // Kuhn's augmenting path: a place for tower t, moving earlier towers if they have another.
        private bool Augment(int t, List<TowerState> towers, int slots)
        {
            for (int k = 0; k < slots; k++)
            {
                if (slotSeen[k]) continue;
                int source = powerSlotSource[k];
                if (!InPowerReach(powerSource[source], powerRadius[source], towers[t].Tile)) continue;
                slotSeen[k] = true;
                if (slotTower[k] < 0 || Augment(slotTower[k], towers, slots))
                {
                    slotTower[k] = t;
                    return true;
                }
            }
            return false;
        }

        // What crafting this piece costs (SimConfig.GearCraftPriceFactor).
        public int CraftPrice(int piece) { return Math.Max(0, (int)(float)(gearLibrary[piece].Price * config.GearCraftPriceFactor)); }

        // What the next research tier costs, or a negative number when all are bought.
        // How many upgrades a tower may have now: all of SimConfig.UpgradesPerTower, or with
        // ResearchUnlocksUpgrades one fewer than the research tier reached.
        public int UpgradesAllowed(SimState s) { return config.ResearchUnlocksUpgrades ? Math.Max(0, Math.Min(config.UpgradesPerTower, s.ResearchLevel - 1)) : config.UpgradesPerTower; }

        public float ResearchPrice => State.ResearchLevel >= 3 ? -1f : State.ResearchLevel == 1 ? config.ResearchTier2Price : config.ResearchTier3Price;

        private void BuildCollector(SimState s, TileCoord tile)
        {
            if (!InBuildPhase(s) || map.DepositAt(tile) < 0 || s.BuildBudget < config.CollectorPrice) return;
            SimNode node = s.Grid.Get(tile);
            if (!node.IsWalkable || node.Occupant != Occupant.None || tile == map.Core) return;
            s.BuildBudget -= config.CollectorPrice;
            s.Grid.SetCollector(node, Math.Max(2, config.CollectorDigCost), Math.Max(0.5f, config.CollectorHealth));
            fields.Generate(s.Grid, map.Core);
            s.PlacementLog.Add(new PlacementRecord { Kind = PlacementKind.CollectorBuilt, Tick = s.Tick, ShapeName = "collector", OriginX = tile.X, OriginY = tile.Y, Tiles = new[] { tile }, Cost = config.CollectorPrice });
        }

        // The sell tool on a collector: it comes down for a share of its price.
        private bool SellCollector(SimState s, TileCoord tile)
        {
            if (!s.Grid.InBounds(tile) || s.Grid.Get(tile).Occupant != Occupant.Collector) return false;
            if (!MaySell(s)) return true;
            float refund = (float)(config.CollectorPrice * config.SellRefund);
            s.Grid.ClearWall(s.Grid.Get(tile));
            s.BuildBudget += refund;
            fields.Generate(s.Grid, map.Core);
            s.PlacementLog.Add(new PlacementRecord { Kind = PlacementKind.CollectorSold, Tick = s.Tick, ShapeName = "collector", OriginX = tile.X, OriginY = tile.Y, Tiles = new[] { tile }, Refund = refund });
            return true;
        }

        // The build menu: any tower of the roster whose tier is researched, for its price.
        private void BuyTower(SimState s, int index, TileCoord tile)
        {
            if (!config.BuildMenu || index < 0 || index >= towerLibrary.Length) return;
            TowerDef def = towerLibrary[index];
            if ((!config.ResearchUnlocksUpgrades && def.Tier > s.ResearchLevel) || !TowerCombine.HasRoomFor(s.Towers, def, 1, config, s.WaveIndex)) return;
            Placement.TryPlaceTower(s.Grid, map, fields, def, def.Cost, tile, ref s.BuildBudget, s.Tick, s.Towers, s.PlacementLog, events, 1, config.TowersOnWalls, config.PylonCoreKeepOut);
        }

        private void Research(SimState s)
        {
            float price = ResearchPrice;
            if (!config.BuildMenu || price < 0f || s.BuildBudget < price) return;
            s.BuildBudget -= price;
            s.ResearchLevel++;
            s.PlacementLog.Add(new PlacementRecord { Kind = PlacementKind.Researched, Tick = s.Tick, ShapeName = "tier" + s.ResearchLevel, Cost = price });
        }

        // What the next extra move costs. Meaningless while none are for sale.
        public float ExtraMovePrice => (float)(config.ExtraMovePrice + (float)(config.ExtraMovePriceStep * State.MovesBought));

        private static int[] NewBench(int slots)
        {
            var bench = new int[slots];
            for (int i = 0; i < slots; i++) bench[i] = TowerBench.Empty;
            return bench;
        }

        private static bool InBuildPhase(SimState s) { return s.Phase == SimPhase.Prep || s.Phase == SimPhase.Intermission; }

        private static bool OnBenchRange(SimState s, int slot) { return slot >= 0 && slot < s.Bench.Length; }

        // A move is what touching a tower on the board costs (SimConfig.MovesPerWave).
        // Asked before the action and spent only when the action happened.
        private bool HasMove(SimState s) { return config.MovesPerWave <= 0 || MovesAreFree || s.MovesLeft > 0; }

        private void UseMove(SimState s) { if (config.MovesPerWave > 0 && !MovesAreFree) s.MovesLeft--; }

        // Before the first wave, with SimConfig.FreeFirstBuild, a move costs nothing.
        public bool MovesAreFree => config.FreeFirstBuild && State.WaveIndex == 0;

        // The living tower standing on a tile, or null.
        private static TowerState TowerOn(SimState s, TileCoord tile)
        {
            if (!s.Grid.InBounds(tile)) return null;
            SimNode node = s.Grid.Get(tile);
            if (node.Occupant != Occupant.Tower) return null;
            TowerState tower = s.Towers[node.TowerId];
            return tower.IsAlive ? tower : null;
        }

        // With a wall stock a wall is not sold but taken up: its tiles go back to the
        // stock. One laid in this build phase comes up for nothing; an older one uses
        // one of the phase's pick-ups.
        private void TakeUpWall(SimState s, TileCoord tile)
        {
            if (!InBuildPhase(s) || !s.Grid.InBounds(tile)) return;
            SimNode node = s.Grid.Get(tile);
            if (node.Occupant != Occupant.Wall || node.PieceId < 0) return;
            bool fresh = node.PieceId >= s.PhaseLogStart || config.FreeWallPickup;
            if (!fresh && s.RelaysLeft <= 0) return;
            if (!Placement.TrySellWall(s.Grid, map, fields, tile, 0f, ref s.BuildBudget, s.Tick, s.PlacementLog, events)) return;
            s.WallsLeft += s.PlacementLog[s.PlacementLog.Count - 1].Tiles.Length;
            if (!fresh) s.RelaysLeft--;
        }

        // The player chooses the core's tile, before the first wave only. The tile must
        // be open ground away from the edge, and every entry point must still reach it.
        private void PlaceCore(SimState s, TileCoord tile)
        {
            if (!config.CorePlacement || s.WaveIndex != 0 || s.Phase != SimPhase.Prep || !s.Grid.InBounds(tile) || tile == s.Core) return;
            SimNode node = s.Grid.Get(tile);
            if (!node.IsWalkable || node.Occupant != Occupant.None || map.DepositAt(tile) >= 0) return;
            if (map.EdgeDistance(tile.X, tile.Y) < Math.Max(map.BuildMargin, config.CoreEdgeMargin)) return;
            for (int i = 0; i < map.Spawns.Length; i++) if (map.Spawns[i] == tile) return;
            if (config.PylonCoreKeepOut > 0f)
                foreach (TowerState t in s.Towers)
                    if (t.IsAlive && !t.OnBench && t.PowerCapacity > 0)
                    {
                        int dx = t.Tile.X - tile.X, dy = t.Tile.Y - tile.Y;
                        if ((float)(dx * dx + dy * dy) <= (float)(config.PylonCoreKeepOut * config.PylonCoreKeepOut)) return; // not next to a pylon
                    }

            TileCoord old = s.Core;
            fields.Generate(s.Grid, tile);
            for (int i = 0; i < map.Spawns.Length; i++)
            {
                if (s.Grid.Get(map.Spawns[i]).BestCost != SimNode.Infinity) continue;
                fields.Generate(s.Grid, old); // an entry point would be cut off: refuse
                return;
            }

            map.Core = tile;
            map.CoreWorld = new WorldPoint(node.Position.X, map.CoreWorld != null ? map.CoreWorld.Y : 0f, node.Position.Y);
            s.Core = tile;
            s.NextPlan = null; // planned for the old core: the next tick plans again
            s.PlacementLog.Add(new PlacementRecord { Kind = PlacementKind.CorePlaced, Tick = s.Tick, ShapeName = "core", OriginX = tile.X, OriginY = tile.Y, Tiles = new[] { tile } });
            events.Add(SimEvent.CoreMoved(tile.X, tile.Y));
        }

        // The tower card in `slot` goes onto the bench instead of the board.
        private void BenchCard(SimState s, int slot, int benchSlot)
        {
            if (slot < 0 || slot >= s.Shop.Offers.Length || !OnBenchRange(s, benchSlot) || s.Bench[benchSlot] != TowerBench.Empty) return;
            TowerOffer offer = s.Shop.Offers[slot];
            if (offer.Bought || !s.Shop.MayPick || offer.Kind != CardKind.Tower || s.BuildBudget < offer.Price) return;

            s.BuildBudget -= offer.Price;
            TowerState tower = TowerBench.Create(s.Towers, towerLibrary[offer.TowerIndex], map.NodeDiameter, s.Tick, offer.Price, offer.Level, s.PlacementLog);
            s.Bench[benchSlot] = tower.Id;
            offer.Bought = true;
            Picked(s);
        }

        // Bench to board, for one move. Onto a tile where a tower stands, the two
        // change places: that one goes to the bench slot.
        private void DeployTower(SimState s, int benchSlot, TileCoord tile)
        {
            if (!OnBenchRange(s, benchSlot) || s.Bench[benchSlot] == TowerBench.Empty || !InBuildPhase(s) || !HasMove(s)) return;
            TowerState tower = s.Towers[s.Bench[benchSlot]];
            TowerState standing = TowerOn(s, tile);
            if (standing != null)
            {
                TowerBench.TakeOffBoard(s.Grid, standing, s.Tick, s.PlacementLog);
                TowerBench.PutOnBoard(s.Grid, tower, tile, s.Tick, s.PlacementLog);
                s.Bench[benchSlot] = standing.Id;
            }
            else
            {
                int cap = TowerCap;
                if (cap > 0 && TowerCombine.Standing(s.Towers) >= cap) return;
                if (!Placement.CanPlaceTower(s.Grid, map, tile, config.TowersOnWalls)) return;
                TowerBench.PutOnBoard(s.Grid, tower, tile, s.Tick, s.PlacementLog);
                s.Bench[benchSlot] = TowerBench.Empty;
            }
            tower.Recompute(gearLibrary);
            fields.Generate(s.Grid, map.Core);
            UseMove(s);
            events.Add(SimEvent.TowerChanged(tower.Id));
        }

        // Board to an empty bench slot, for one move.
        private void RecallTower(SimState s, TileCoord tile, int benchSlot)
        {
            if (!OnBenchRange(s, benchSlot) || s.Bench[benchSlot] != TowerBench.Empty || !InBuildPhase(s) || !HasMove(s)) return;
            TowerState tower = TowerOn(s, tile);
            if (tower == null) return;
            TowerBench.TakeOffBoard(s.Grid, tower, s.Tick, s.PlacementLog);
            s.Bench[benchSlot] = tower.Id;
            fields.Generate(s.Grid, map.Core);
            UseMove(s);
        }

        // A duplicate card merged into a tower the player has: one level up, or the
        // card's level when that is higher. The card is the wave's pick; no move.
        private void FeedCard(SimState s, SimCommand command)
        {
            if (!config.FeedDuplicates || command.A < 0 || command.A >= s.Shop.Offers.Length) return;
            TowerOffer offer = s.Shop.Offers[command.A];
            if (offer.Bought || !s.Shop.MayPick || offer.Kind != CardKind.Tower || s.BuildBudget < offer.Price) return;

            TowerState target;
            if (command.B > 0)
            {
                int benchSlot = command.B - 1;
                if (!OnBenchRange(s, benchSlot) || s.Bench[benchSlot] == TowerBench.Empty) return;
                target = s.Towers[s.Bench[benchSlot]];
            }
            else target = TowerOn(s, new TileCoord(command.X, command.Y));
            if (target == null || target.Def != towerLibrary[offer.TowerIndex]) return;

            int level = TowerMerge.LevelAfterCard(s, config, towerLibrary[offer.TowerIndex], offer.Level, target, mergeOthers);
            if (level <= 0 || level > TowerState.MaxLevel) return;

            var emptied = new List<TileCoord>();
            for (int i = 0; i < mergeOthers.Count; i++) Absorb(s, mergeOthers[i], target, emptied);
            if (emptied.Count > 0)
            {
                fields.Generate(s.Grid, map.Core);
                s.PlacementLog.Add(new PlacementRecord
                {
                    Kind = PlacementKind.TowerCombined, Tick = s.Tick, ShapeName = target.Def.Id, OriginX = target.OnBench ? -1 : target.Tile.X,
                    OriginY = target.OnBench ? -1 : target.Tile.Y, Tiles = emptied.ToArray(), TowerId = target.Id,
                });
            }

            s.BuildBudget -= offer.Price;
            target.Level = level;
            target.PricePaid += offer.Price;
            target.Recompute(gearLibrary);
            offer.Bought = true;
            Picked(s);
            TileCoord at = target.OnBench ? TowerBench.NoTile : target.Tile;
            s.PlacementLog.Add(new PlacementRecord
            {
                Kind = PlacementKind.TowerFed, Tick = s.Tick, ShapeName = target.Def.Id, OriginX = at.X, OriginY = at.Y,
                Tiles = target.OnBench ? new TileCoord[0] : new[] { at }, TowerId = target.Id, Cost = offer.Price,
            });
            events.Add(SimEvent.TowerChanged(target.Id));
        }

        // --- merging by choice ---

        private readonly List<TowerState> mergeOthers = new List<TowerState>();

        // One tower dropped on another of its type and level: the target gains a level,
        // the source and the other copies of the set are used up. One move when any of
        // them is on the board.
        private void MergeTower(SimState s, SimCommand command)
        {
            if (!InBuildPhase(s)) return;
            TowerState source = command.A > 0 ? Benched(s, command.A - 1) : TowerOn(s, new TileCoord(command.X, command.Y));
            TowerState target = command.C < 0 ? Benched(s, command.B) : TowerOn(s, new TileCoord(command.B, command.C));
            if (!TowerMerge.CanMerge(s, config, source, target, mergeOthers)) return;

            bool touchesBoard = !source.OnBench || !target.OnBench;
            for (int i = 0; i < mergeOthers.Count; i++) if (!mergeOthers[i].OnBench) touchesBoard = true;
            if (touchesBoard && !HasMove(s)) return;

            var emptied = new List<TileCoord>();
            Absorb(s, source, target, emptied);
            for (int i = 0; i < mergeOthers.Count; i++) Absorb(s, mergeOthers[i], target, emptied);
            target.Level++;
            target.Recompute(gearLibrary);
            if (emptied.Count > 0) fields.Generate(s.Grid, map.Core);
            if (touchesBoard) UseMove(s);
            s.PlacementLog.Add(new PlacementRecord
            {
                Kind = PlacementKind.TowerCombined, Tick = s.Tick, ShapeName = target.Def.Id, OriginX = target.OnBench ? -1 : target.Tile.X,
                OriginY = target.OnBench ? -1 : target.Tile.Y, Tiles = emptied.ToArray(), TowerId = target.Id,
            });
            events.Add(SimEvent.TowerChanged(target.Id));
        }

        private static TowerState Benched(SimState s, int slot)
        {
            return OnBenchRange(s, slot) && s.Bench[slot] != TowerBench.Empty ? s.Towers[s.Bench[slot]] : null;
        }

        // A tower used up by a merge: its gear comes back, what was paid for it goes to
        // the tower that stays, and it leaves its tile or its bench slot.
        private void Absorb(SimState s, TowerState gone, TowerState keeper, List<TileCoord> emptied)
        {
            ReturnGear(s, gone);
            keeper.PricePaid += gone.PricePaid;
            if (gone.OnBench)
            {
                int slot = Array.IndexOf(s.Bench, gone.Id);
                if (slot >= 0) s.Bench[slot] = TowerBench.Empty;
                gone.OnBench = false;
            }
            else
            {
                emptied.Add(gone.Tile);
                s.Grid.LiftTower(s.Grid.Get(gone.Tile));
            }
            gone.IsAlive = false;
            gone.Combined = true;
        }

        // With a gear inventory, the gear of a tower that is sold or merged away comes
        // back: into the inventory, or, when that is full, into the spare pieces the
        // player must deal with. Without an inventory it is gone with the tower, as before.
        private void ReturnGear(SimState s, TowerState tower)
        {
            if (config.GearInventorySlots <= 0) return;
            for (int slot = 0; slot < TowerState.MaxGearSlots; slot++)
            {
                int piece = tower.Gear[slot];
                if (piece == TowerState.NoGear) continue;
                tower.Gear[slot] = TowerState.NoGear;
                int free = Array.IndexOf(s.GearInventory, TowerState.NoGear);
                if (free >= 0) s.GearInventory[free] = piece;
                else s.GearOverflow.Add(piece);
            }
        }

        // --- the gear inventory ---

        // A piece into the first free inventory slot, for `price`. False, changing
        // nothing, when the inventory is full or the budget does not reach.
        private bool StoreGear(SimState s, int piece, int price)
        {
            if (piece < 0 || piece >= gearLibrary.Length || s.BuildBudget < price) return false;
            int free = Array.IndexOf(s.GearInventory, TowerState.NoGear);
            if (free < 0) return false;
            s.BuildBudget -= price;
            s.GearInventory[free] = piece;
            s.PlacementLog.Add(new PlacementRecord { Kind = PlacementKind.GearStored, Tick = s.Tick, ShapeName = gearLibrary[piece].Id, OriginX = -1, OriginY = -1, Cost = price });
            return true;
        }

        // The tower a gear command means: on the board at (X, Y), or with B > 0 in bench slot B - 1.
        private TowerState GearTarget(SimState s, SimCommand command)
        {
            if (command.B <= 0) return TowerOn(s, new TileCoord(command.X, command.Y));
            int benchSlot = command.B - 1;
            return OnBenchRange(s, benchSlot) && s.Bench[benchSlot] != TowerBench.Empty ? s.Towers[s.Bench[benchSlot]] : null;
        }

        // Inventory to tower. Free, at any time gear may be moved.
        private void EquipGear(SimState s, SimCommand command)
        {
            if (!(config.MoveGearDuringWave || InBuildPhase(s)) || command.A < 0 || command.A >= s.GearInventory.Length) return;
            int piece = s.GearInventory[command.A];
            TowerState tower = GearTarget(s, command);
            if (piece == TowerState.NoGear || tower == null) return;

            // The extractor is used up and the tower's gear comes off: into the inventory,
            // or the spare pieces when that is full.
            if (gearLibrary[piece].Extracts)
            {
                if (tower.Gear[0] == TowerState.NoGear && tower.Gear[1] == TowerState.NoGear) return;
                s.GearInventory[command.A] = TowerState.NoGear;
                LogGear(s, PlacementKind.GearEquipped, piece, tower);
                for (int g = 0; g < TowerState.MaxGearSlots; g++)
                {
                    if (tower.Gear[g] != TowerState.NoGear) LogGear(s, PlacementKind.GearUnequipped, tower.Gear[g], tower);
                }
                ReturnGear(s, tower);
                tower.Recompute(gearLibrary);
                return;
            }
            int slot = tower.FreeGearSlot();
            if (slot < 0) return;

            s.GearInventory[command.A] = TowerState.NoGear;
            tower.Gear[slot] = piece;
            tower.Recompute(gearLibrary);
            LogGear(s, PlacementKind.GearEquipped, piece, tower);
        }

        // Tower to the first free inventory slot. Free.
        private void UnequipGear(SimState s, SimCommand command)
        {
            if (!(config.MoveGearDuringWave || InBuildPhase(s)) || command.A < 0 || command.A >= TowerState.MaxGearSlots) return;
            TowerState tower = GearTarget(s, command);
            if (tower == null || tower.Gear[command.A] == TowerState.NoGear) return;
            int free = Array.IndexOf(s.GearInventory, TowerState.NoGear);
            if (free < 0) return;

            int piece = tower.Gear[command.A];
            // The tower's PricePaid is left alone: a piece that came from the inventory was
            // never part of it. (One bought straight onto the tower stays in it; with an
            // inventory, gear is bought into the inventory.)
            tower.Gear[command.A] = TowerState.NoGear;
            s.GearInventory[free] = piece;
            tower.Recompute(gearLibrary);
            LogGear(s, PlacementKind.GearUnequipped, piece, tower);
        }

        private void LogGear(SimState s, PlacementKind kind, int piece, TowerState tower)
        {
            TileCoord at = tower.OnBench ? TowerBench.NoTile : tower.Tile;
            s.PlacementLog.Add(new PlacementRecord
            {
                Kind = kind, Tick = s.Tick, ShapeName = gearLibrary[piece].Id, OriginX = at.X, OriginY = at.Y,
                Tiles = tower.OnBench ? new TileCoord[0] : new[] { at }, TowerId = tower.Id,
            });
            events.Add(SimEvent.TowerChanged(tower.Id));
        }

        // Selling is for build phases, unless the config says otherwise: see
        // SimConfig.SellDuringWave for why.
        private bool MaySell(SimState s)
        {
            return config.SellDuringWave || s.Phase == SimPhase.Prep || s.Phase == SimPhase.Intermission;
        }

        // A fresh set of offers. Nothing of the state goes in: only the seed and the
        // two counters (ShopRoller, CLAUDE.md I10).
        private ShopState RollShop(int waveIndex, int rerollCount)
        {
            var shop = new ShopState
            {
                WaveIndex = waveIndex,
                RerollCount = rerollCount,
                Offers = ShopRoller.Roll(RngSeed, waveIndex, rerollCount, towerLibrary, config.ShopOffers, config, gearLibrary),
            };

            // A draft: the cards cost nothing and only so many may be taken.
            if (config.PicksPerRoll > 0)
            {
                shop.PicksLeft = waveIndex == 0 ? Math.Max(config.PicksPerRoll, config.PicksAtStart) : config.PicksPerRoll;
                for (int i = 0; i < shop.Offers.Length; i++) shop.Offers[i].Price = 0;
            }
            return shop;
        }

        private static void Picked(SimState s)
        {
            if (s.Shop.PicksLeft > 0) s.Shop.PicksLeft--;
        }

        private ShopState RollGearShop(int stock)
        {
            return new ShopState { WaveIndex = stock, Offers = ShopRoller.RollGearShop(RngSeed, stock, gearLibrary, config.GearShopSlots, 0, config) };
        }

        // Asks the planner for the wave after the current one and checks it, changing
        // nothing: the caller decides what to do with the plan.
        private WavePlan PlanNextWave(SimState s)
        {
            int wave = s.WaveIndex + 1;
            var context = new WaveContext(wave, s, map, config);
            ulong before = StateHasher.Compute(s);
            WavePlan plan = planner.PlanWave(context);
            GuardUnchanged(s, before, "PlanWave", wave);
            ValidatePlan(plan, wave);
            return plan.Copy();
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
            WavePlan plan = s.NextPlan ?? PlanNextWave(s);
            undoStack.Clear();
            redoStack.Clear();
            retryStart = phaseStart;

            s.NextPlan = null;
            s.WaveIndex = wave;
            s.CurrentPlan = plan.Copy(); // our own copy: the planner cannot edit a running wave
            BuildSchedule(s, s.CurrentPlan);
            s.Occupancy.Reset();
            s.Damage.Reset();
            foreach (TowerState tower in s.Towers) { tower.WaveDamage = 0f; tower.WaveKills = 0; }
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
                var agent = new AgentState(s.Agents.Count, new Vec2f(at.X, at.Z), group.MoveSpeed, group.Hp, group.Resist,
                                           group.DigRate, config.KillReward, config.WallBreakReward, group.Movement)
                {
                    WaveIndex = s.WaveIndex,
                    Archetype = group.Archetype,
                    TargetsCollectors = group.TargetsCollectors,
                    Siege = group.Siege,
                };
                s.Agents.Add(agent);
                s.Live.Add(agent);
                s.CurrentOutcome.Spawned++;
                s.CurrentOutcome.SpawnedByClass[(int)agent.Movement]++;
                events.Add(SimEvent.AgentSpawned(agent.Id));
            }

            if (s.ScheduleCursor >= s.Schedule.Length) s.Phase = SimPhase.Resolving;
        }

        // Tallies this tick's tower-pass and agent-pass events into the open outcome.
        // Under D4 only one wave is ever live, so every such event belongs to the
        // current wave.
        private void CountCombatEvents(SimState s, int firstEvent)
        {
            WaveOutcome o = s.CurrentOutcome;
            if (o == null) return;
            for (int i = firstEvent; i < events.Count; i++)
            {
                switch (events[i].Kind)
                {
                    case SimEventKind.AgentKilled:
                        o.Killed++;
                        o.KilledByClass[(int)s.Agents[events[i].IntA].Movement]++;
                        break;
                    case SimEventKind.AgentLeaked:
                        o.Leaked++;
                        o.LeakedByClass[(int)s.Agents[events[i].IntA].Movement]++;
                        break;
                    case SimEventKind.WallBreached: o.WallsBreached++; break;
                    case SimEventKind.TowerDestroyed: o.TowersDestroyed++; break;
                    case SimEventKind.CollectorDestroyed:
                        o.CollectorsLost++;
                        s.BuildBudget += (float)(config.CollectorPrice * config.CollectorLossRefund);
                        break;
                }
            }
        }

        // The MaxWaveSeconds backstop. Whatever is still alive is taken off the
        // board - no core damage, no reward - and anything not yet spawned never
        // appears. Step 5 then closes the wave in the usual way. Nothing in a
        // healthy game gets here: enemies always move (PlanValidator), a slow has a
        // floor (MinSlowFactor), and every spawn can reach the core (MapData).
        private void TimeOutWave(SimState s)
        {
            WaveOutcome o = s.CurrentOutcome;
            o.TimedOut = true;
            for (int i = 0; i < s.Live.Count; i++)
            {
                AgentState a = s.Live[i];
                a.IsAlive = false;
                o.Removed++;
                events.Add(SimEvent.AgentRemoved(a.Id));
            }
            s.Live.Clear();
            s.ScheduleCursor = s.Schedule.Length;
            if (s.Phase == SimPhase.Spawning) s.Phase = SimPhase.Resolving;
            events.Add(SimEvent.WaveTimedOut(o.WaveIndex));
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
