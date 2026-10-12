using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // Watches one Simulation from its first tick and turns it into telemetry rows,
    // one per wave (WaveRecord). It is driven the way ReplayRecorder is, and knows
    // nothing about who is driving it: Unity's SimHost and the headless
    // EpisodeRunner make the same two calls.
    //
    //     sim.Tick();
    //     foreach (WaveRecord row in recorder.AfterTick()) writer.Write(row);   // usually none
    //     ...
    //     foreach (WaveRecord row in recorder.Finish()) writer.Write(row);      // the session's last, unfinished window
    //
    // AfterTick must follow every Tick() directly, before the next command is
    // sent. That is what lets a row's window end exactly at a wave's start: at that
    // moment the build log holds everything built before the wave and nothing after.
    //
    // Because it reads only the simulation, running a recorded session again
    // (ReplayRunner.Telemetry) writes the same rows, byte for byte. Nothing here
    // takes the time of day, the frame rate or the machine into account.
    public sealed class TelemetryRecorder
    {
        private readonly Simulation sim;
        private readonly string session;
        private readonly string build;
        private readonly string player;
        private readonly List<WaveRecord> completed = new List<WaveRecord>();

        // The open window.
        private int wave;                    // 0 = the build phase before the first wave
        private int tickStart;
        private int tickResolved = -1;
        private WaveRecord.PlanRow plan;
        private WaveRecord.OutcomeRow outcome;
        private string[] offered;
        private string stateHash;
        private string fallback;

        private int logCursor;               // build-log rows before this are already in a finished row
        private int ticksSeen;
        private bool finished;

        public TelemetryRecorder(Simulation sim, string session, string build, string player)
        {
            this.sim = sim ?? throw new ArgumentNullException(nameof(sim));
            if (sim.State.Tick != 0 || sim.State.PlacementLog.Count != 0)
                throw new InvalidOperationException("[Telemetry] A recorder must be attached before the first tick and the first command (tick is " + sim.State.Tick + ").");

            this.session = session;
            this.build = build;
            this.player = player;

            // The game-start roll raises no event anyone could have heard.
            offered = OfferedNames(sim.State.Shop);
        }

        // Rows finished so far, counting those already handed out.
        public int RowCount { get; private set; }

        // The rows this tick completed: the previous wave's when a new wave began,
        // and the last one when the game ended. The list is reused; read it before
        // the next call.
        public IReadOnlyList<WaveRecord> AfterTick()
        {
            completed.Clear();
            SimState s = sim.State;
            if (finished || s.Tick == ticksSeen) return completed; // a finished session, or Tick() after game over
            if (s.Tick != ticksSeen + 1)
                throw new InvalidOperationException("[Telemetry] AfterTick must follow every Tick(): saw tick " + ticksSeen + ", simulation is at " + s.Tick + ".");
            ticksSeen = s.Tick;

            IReadOnlyList<SimEvent> events = sim.LastTickEvents;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                switch (e.Kind)
                {
                    case SimEventKind.WaveStarted:
                        // The wave began on this tick, after this tick's commands
                        // were applied: everything in the log belongs to the window
                        // that now closes.
                        Close(s.CurrentOutcome != null ? s.CurrentOutcome.TickStarted : s.Tick - 1);
                        Open(e.IntA, s);
                        break;

                    case SimEventKind.WaveResolved:
                        outcome = OutcomeRow(s.LastOutcome);
                        tickResolved = s.LastOutcome.TickResolved;
                        stateHash = ReplayFile.Hex(sim.ComputeHash());
                        NoteFallback(e.IntA);
                        break;

                    case SimEventKind.ShopRolled:
                        // A roll's first set (reroll 0) is "what was offered" for
                        // the window; a paid reroll shows up as a row of the build log.
                        //
                        // The check is also what keeps a live session and its replay
                        // the same. A reroll is a command. A host applies commands
                        // between ticks (FlushInput), so this loop never sees the
                        // reroll's event; a replay applies them at the start of the
                        // next tick, so it does. Only events the tick itself raises
                        // (a wave starting or resolving, the roll that follows, game
                        // over) may change a row.
                        if (e.IntB == 0) offered = OfferedNames(s.Shop);
                        break;

                    case SimEventKind.GameOver:
                        // Nothing more can happen: the simulation takes no input now.
                        Close(s.Tick);
                        finished = true;
                        break;
                }
            }
            return completed;
        }

        // The window still open when the session stops: its wave may be unresolved
        // and its build phase cut short. Returns nothing when the game had already
        // ended. Safe to call more than once.
        public IReadOnlyList<WaveRecord> Finish()
        {
            completed.Clear();
            if (finished) return completed;
            Close(sim.State.Tick);
            finished = true;
            return completed;
        }

        private void Open(int waveIndex, SimState s)
        {
            wave = waveIndex;
            tickStart = s.CurrentOutcome != null ? s.CurrentOutcome.TickStarted : s.Tick - 1;
            tickResolved = -1;
            plan = PlanRow(s.CurrentPlan);
            outcome = null;
            offered = null;
            stateHash = null;
            fallback = null;
            NoteFallback(waveIndex);
        }

        // SafePlanner reports the last wave it had to rescue; pick it up if it is ours.
        private void NoteFallback(int waveIndex)
        {
            var safe = sim.Planner as SafePlanner;
            if (safe != null && safe.LastFallbackWave == waveIndex && fallback == null) fallback = safe.LastFallbackReason;
        }

        private void Close(int tickEnd)
        {
            List<PlacementRecord> log = sim.State.PlacementLog;
            var placements = new List<WaveRecord.PlacementRow>();
            var bought = new List<string>();
            var sold = new List<WaveRecord.SaleRow>();
            int rerolls = 0;

            for (int i = logCursor; i < log.Count; i++)
            {
                PlacementRecord r = log[i];
                placements.Add(PlacementRow(r));
                switch (r.Kind)
                {
                    case PlacementKind.Tower: bought.Add(r.ShapeName); break;
                    case PlacementKind.TowerSold: sold.Add(new WaveRecord.SaleRow { Def = r.ShapeName, X = r.OriginX, Y = r.OriginY, Refund = r.Refund }); break;
                    case PlacementKind.ShopReroll: rerolls++; break;
                }
            }
            logCursor = log.Count;

            completed.Add(new WaveRecord
            {
                Session = session,
                Condition = sim.Planner.Name,
                Player = player,
                Build = build,
                Map = sim.Map.Name,
                MapSeed = 0,
                RngSeed = sim.RngSeed,
                Wave = wave,
                TickStart = tickStart,
                TickResolved = tickResolved,
                TickEnd = tickEnd,
                Fallback = fallback,
                Plan = plan,
                Outcome = outcome,
                Placements = placements.ToArray(),
                Shop = new WaveRecord.ShopRow
                {
                    Offered = offered ?? new string[0],
                    Bought = bought.ToArray(),
                    Sold = sold.ToArray(),
                    Rerolls = rerolls,
                },
                StateHash = stateHash,
            });
            RowCount++;
        }

        // ---- simulation types -> row types ----

        private string[] OfferedNames(ShopState shop)
        {
            var names = new string[shop.Offers.Length];
            for (int i = 0; i < names.Length; i++) names[i] = sim.TowerLibrary[shop.Offers[i].TowerIndex].Id;
            return names;
        }

        private static WaveRecord.PlanRow PlanRow(WavePlan p)
        {
            if (p == null) return null;
            var row = new WaveRecord.PlanRow { Strategy = p.StrategyId, ThreatSpent = p.ThreatSpent, Groups = new WaveRecord.GroupRow[p.Groups.Length] };
            for (int i = 0; i < p.Groups.Length; i++)
            {
                AgentGroup g = p.Groups[i];
                var resist = new NamedValues();
                for (int t = 0; t < g.Resist.Length; t++) resist.Add(Name((DamageType)t), g.Resist[t]);
                row.Groups[i] = new WaveRecord.GroupRow
                {
                    Spawn = g.SpawnIndex,
                    Archetype = g.Archetype,
                    Movement = Name(g.Movement),
                    Count = g.Count,
                    Hp = g.Hp,
                    MoveSpeed = g.MoveSpeed,
                    DigRate = g.DigRate,
                    Resist = resist,
                    IntervalTicks = g.SpawnIntervalTicks,
                    StartDelayTicks = g.StartDelayTicks,
                };
            }
            return row;
        }

        private static WaveRecord.OutcomeRow OutcomeRow(WaveOutcome o)
        {
            var damage = new NamedValues();
            for (int t = 0; t < o.DamageByType.Length; t++) damage.Add(Name((DamageType)t), o.DamageByType[t]);

            return new WaveRecord.OutcomeRow
            {
                Spawned = o.Spawned,
                Killed = o.Killed,
                Leaked = o.Leaked,
                Removed = o.Removed,
                TimedOut = o.TimedOut,
                Breaches = o.WallsBreached,
                TowersDestroyed = o.TowersDestroyed,
                CoreHpBefore = o.CoreHpBefore,
                CoreHpAfter = o.CoreHpAfter,
                CoreDestroyed = o.CoreDestroyed,
                BudgetBefore = o.BudgetBefore,
                BudgetAfter = o.BudgetAfter,
                Pressure = o.PressureShare,
                DamageByType = damage,
                SpawnedByClass = ByClass(o.SpawnedByClass),
                KilledByClass = ByClass(o.KilledByClass),
                LeakedByClass = ByClass(o.LeakedByClass),
            };
        }

        private static NamedValues ByClass(int[] counts)
        {
            var values = new NamedValues();
            for (int c = 0; c < counts.Length; c++) values.Add(Name((MovementClass)c), counts[c]);
            return values;
        }

        private static WaveRecord.PlacementRow PlacementRow(PlacementRecord r)
        {
            var tiles = new int[r.Tiles.Length][];
            for (int i = 0; i < tiles.Length; i++) tiles[i] = new[] { r.Tiles[i].X, r.Tiles[i].Y };

            return new WaveRecord.PlacementRow
            {
                Tick = r.Tick,
                Kind = KindName(r.Kind),
                Name = r.ShapeName,
                Rot = r.RotationTurns,
                X = r.OriginX,
                Y = r.OriginY,
                Tiles = tiles,
                Cost = r.Cost,
                Refund = r.Refund,
                Piece = r.PieceId,
                TowerId = r.TowerId,
            };
        }

        private static string KindName(PlacementKind kind)
        {
            switch (kind)
            {
                case PlacementKind.Wall: return WaveRecord.PlacementRow.Wall;
                case PlacementKind.Tower: return WaveRecord.PlacementRow.Tower;
                case PlacementKind.WallSold: return WaveRecord.PlacementRow.SellWall;
                case PlacementKind.TowerSold: return WaveRecord.PlacementRow.SellTower;
                case PlacementKind.ShopReroll: return WaveRecord.PlacementRow.Reroll;
                default: throw new InvalidOperationException("[Telemetry] Unknown build-log kind " + kind + ".");
            }
        }

        // Enum names as they appear in a row: lower case, so "ground", "physical".
        private static string Name(DamageType type) { return type.ToString().ToLowerInvariant(); }

        private static string Name(MovementClass movement) { return movement.ToString().ToLowerInvariant(); }
    }
}
