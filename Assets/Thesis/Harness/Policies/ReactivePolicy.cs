using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Harness
{
    // A stand-in player who answers the last wave (WP12, the student 2026-11-24). `builder` never
    // changes what it built, so the full director's reward, which is mostly "did the player rebuild",
    // is about 0 against it and the ladder could not tell the directors apart. This one plays like
    // `builder` (it is one, inside) and, at the start of each build phase, first looks at what the
    // wave before did to it:
    //
    //   flyers got through      it raises its share of anti-air towers (up to MaxAntiAirShare)
    //   walkers got through     it lays more of a maze: walls on the route, each where it adds the
    //                           most to the walk from the entries (a greedy flow-field search)
    //   sappers got through, or
    //   dug through its walls   it leans less on walls: sells some of the maze and lays less
    //   a split wave got through  a collector at the deposit nearest those entries, so the next
    //                           towers stand on that side (more power places there)
    //
    // Each reaction is what a reasonable player might do; none of them is optimal, and none knows the
    // director exists. It draws nothing from rng: its choices follow from the board alone.
    //
    // Limits, stated with the results: it reacts only at the start of a build phase, never mid-wave;
    // it remembers only the last wave; it never researches, upgrades or moves towers.
    public sealed class ReactivePolicy : IPlayerPolicy
    {
        public const string Id = "reactive";

        public float StartAntiAirShare = 1f / 3f;
        public float MaxAntiAirShare = 0.6f;
        public float AntiAirStep = 0.15f;
        public int WallsPerLeak = 3;        // walls added to the plan per wave of walkers leaking
        public int MaxMaze = 30;            // at most this many maze walls standing
        public int WallsSoldOnBreach = 4;
        public int MaxCandidates = 24;      // route tiles tried per wall (one flow-field rebuild each)
        public int MinCoreDistance = 3;     // walls go between this and MaxCoreDistance tiles from the core
        public int MaxCoreDistance = 15;

        private readonly BuilderPolicy builder = new BuilderPolicy();
        private readonly FlowField field = new FlowField();
        private readonly List<SimNode> route = new List<SimNode>();
        private readonly TileCoord[] one = new TileCoord[1];
        private readonly HashSet<int> maze = new HashSet<int>(); // tile indices of the walls this player laid as maze
        private WavePlan fought;   // the plan of the wave that was just played
        private int mazeWanted;

        public string Name => Id;

        public ReactivePolicy() { builder.AntiAirShare = StartAntiAirShare; }

        // What it did at the last build phase, for the tests and the ladder's notes.
        public readonly List<string> LastReactions = new List<string>();

        public void OnIntermission(Simulation sim, Action<SimCommand> send, IRandom rng)
        {
            SimState s = sim.State;
            if (s.IsGameOver) return;
            LastReactions.Clear();
            WaveOutcome last = s.LastOutcome;
            if (last != null && fought != null && last.WaveIndex == fought.WaveIndex) React(sim, send, last);

            // Walls first: they are cheap (1 ore), and a tower may then stand on one.
            if (s.WaveIndex > 0) LayMaze(sim, send);
            builder.OnIntermission(sim, send, rng);
            fought = sim.State.NextPlan;
        }

        private void React(Simulation sim, Action<SimCommand> send, WaveOutcome last)
        {
            int flying = (int)MovementClass.Flying, sapper = (int)MovementClass.Sapper, ground = (int)MovementClass.Ground;
            if (last.LeakedByClass[flying] > 0 && builder.AntiAirShare < MaxAntiAirShare)
            {
                builder.AntiAirShare = Math.Min(MaxAntiAirShare, builder.AntiAirShare + AntiAirStep);
                LastReactions.Add("anti-air");
            }
            bool sappersHurt = last.LeakedByClass[sapper] > 0 || (last.WallsBreached > 0 && last.SpawnedByClass[sapper] > 0);
            if (sappersHurt)
            {
                mazeWanted = Math.Max(0, mazeWanted - WallsSoldOnBreach);
                SellMaze(sim, send, WallsSoldOnBreach);
                LastReactions.Add("fewer-walls");
            }
            else if (last.LeakedByClass[ground] > 0)
            {
                mazeWanted = Math.Min(MaxMaze, mazeWanted + WallsPerLeak);
                LastReactions.Add("more-maze");
            }
            if (last.Leaked > 0 && Entries(fought).Count >= 2 && CollectorTowards(sim, send, Entries(fought))) LastReactions.Add("power-on-entry-side");
        }

        private static List<int> Entries(WavePlan plan)
        {
            var entries = new List<int>();
            if (plan == null) return entries;
            foreach (AgentGroup g in plan.Groups) if (!entries.Contains(g.SpawnIndex)) entries.Add(g.SpawnIndex);
            return entries;
        }

        // ------------------------------------------------------------------ the maze

        private void SellMaze(Simulation sim, Action<SimCommand> send, int count)
        {
            // The walls nearest the core go first: the ones a sapper reaches last are the least use.
            var standing = new List<SimNode>();
            foreach (int index in maze)
            {
                SimNode node = sim.State.Grid.ByIndex(index);
                if (node.Occupant == Occupant.Wall) standing.Add(node);
            }
            TileCoord core = sim.State.Core;
            standing.Sort((a, b) =>
            {
                int da = Math.Max(Math.Abs(a.X - core.X), Math.Abs(a.Y - core.Y)), db = Math.Max(Math.Abs(b.X - core.X), Math.Abs(b.Y - core.Y));
                return da != db ? da.CompareTo(db) : a.Index.CompareTo(b.Index);
            });
            for (int i = 0; i < standing.Count && i < count; i++)
            {
                send(SimCommand.SellWall(standing[i].X, standing[i].Y));
                if (sim.State.Grid.ByIndex(standing[i].Index).Occupant != Occupant.Wall) maze.Remove(standing[i].Index);
            }
        }

        private int MazeStanding(Simulation sim)
        {
            int n = 0;
            var gone = new List<int>();
            foreach (int index in maze)
            {
                if (sim.State.Grid.ByIndex(index).Occupant == Occupant.Wall) n++;
                else gone.Add(index); // breached, or a tower now stands on it
            }
            foreach (int g in gone) maze.Remove(g);
            return n;
        }

        private void LayMaze(Simulation sim, Action<SimCommand> send)
        {
            for (int guard = 0; guard < MaxMaze && MazeStanding(sim) < mazeWanted; guard++)
            {
                SimState s = sim.State;
                if (s.BuildBudget < 1f) return;
                if (!BestWall(sim, out TileCoord tile)) return;
                int before = s.PlacementLog.Count;
                send(SimCommand.PlaceShape(tile.X, tile.Y));
                if (sim.State.PlacementLog.Count == before) return; // could not be paid or laid
                maze.Add(sim.State.Grid.Get(tile).Index);
            }
        }

        // The route tile, between MinCoreDistance and MaxCoreDistance from the core, where one wall
        // makes the walk from the next wave's entries the longest. False when no wall adds anything.
        private bool BestWall(Simulation sim, out TileCoord best)
        {
            best = default(TileCoord);
            SimState s = sim.State;
            MapData map = sim.Map;
            ShapeDef shape = s.Bag != null ? s.Bag.CurrentShape : null;
            int terrain = shape != null ? Placement.WallTerrainCost(shape) : 15;
            float health = shape != null ? Placement.WallHealth(shape) : 6f;

            List<int> entries = Entries(s.NextPlan);
            if (entries.Count == 0) entries.Add(0);
            var candidates = new List<TileCoord>();
            var seen = new HashSet<int>();
            foreach (int e in entries)
            {
                Route.Collect(s.Grid, s.Grid.Get(map.Spawns[e]), route);
                foreach (SimNode n in route)
                {
                    int d = Math.Max(Math.Abs(n.X - s.Core.X), Math.Abs(n.Y - s.Core.Y));
                    if (d < MinCoreDistance || d > MaxCoreDistance || !seen.Add(n.Index)) continue;
                    candidates.Add(new TileCoord(n.X, n.Y));
                }
            }
            if (candidates.Count > MaxCandidates)
            {
                int total = candidates.Count;
                var thinned = new List<TileCoord>(MaxCandidates);
                for (int k = 0; k < MaxCandidates; k++) thinned.Add(candidates[(int)((long)k * total / MaxCandidates)]);
                candidates = thinned;
            }

            SimGrid scratch = s.Grid.Clone();
            int baseline = Walk(scratch, map, entries);
            int bestGain = 0;
            foreach (TileCoord t in candidates)
            {
                one[0] = t;
                if (!Placement.CanPlace(scratch, map, one)) continue;
                SimNode node = scratch.Get(t);
                scratch.SetWall(node, terrain, health);
                field.Generate(scratch, s.Core);
                int gain = Walk(scratch, map, entries) - baseline;
                scratch.ClearWall(node);
                if (gain > bestGain) { bestGain = gain; best = t; } // strict: ties keep the first, so the order decides
            }
            return bestGain > 0;
        }

        private static int Walk(SimGrid grid, MapData map, List<int> entries)
        {
            int sum = 0;
            foreach (int e in entries)
            {
                int c = grid.Get(map.Spawns[e]).BestCost;
                if (c != SimNode.Infinity) sum += c;
            }
            return sum;
        }

        // ------------------------------------------------------------------ power on the entry side

        private static bool CollectorTowards(Simulation sim, Action<SimCommand> send, List<int> entries)
        {
            SimState s = sim.State;
            if (!sim.Config.BuildMenu || s.BuildBudget < sim.Config.CollectorPrice) return false;
            int best = -1;
            long bestKey = long.MaxValue;
            for (int d = 0; d < sim.Map.Deposits.Length; d++)
            {
                TileCoord t = sim.Map.Deposits[d].Tile;
                if (s.Grid.Get(t).Occupant != Occupant.None) continue;
                if (s.DepositLeft != null && d < s.DepositLeft.Length && s.DepositLeft[d] <= 0) continue;
                int toCore = Math.Abs(t.X - s.Core.X) + Math.Abs(t.Y - s.Core.Y);
                if (toCore > 20) continue; // too far out to defend anything
                int toEntry = int.MaxValue;
                foreach (int e in entries)
                {
                    TileCoord sp = sim.Map.Spawns[e];
                    toEntry = Math.Min(toEntry, Math.Abs(t.X - sp.X) + Math.Abs(t.Y - sp.Y));
                }
                long key = (long)toEntry * 10000 + d;
                if (key < bestKey) { bestKey = key; best = d; }
            }
            if (best < 0) return false;
            TileCoord tile = sim.Map.Deposits[best].Tile;
            send(SimCommand.BuildCollector(tile.X, tile.Y));
            return s.Grid.Get(tile).Occupant == Occupant.Collector;
        }
    }
}
