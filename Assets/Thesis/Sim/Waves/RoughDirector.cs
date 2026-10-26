using System;
using Thesis.Core;

namespace Thesis.Sim
{
    // DEVELOPMENT ONLY (2026-10-26): a rough first cut of the director's authored layer
    // (CLAUDE.md §5, layer 2), built early because the student found towers stacked at the
    // core still beat most waves. Not a study condition and not the director: no learning,
    // no build profile, no threat cost table. It reads the board as the build phase begins
    // and picks one of three ways to send the wave that RaidPlanner composes:
    //
    //   outpost_raid  part of the wave (at most MaxRaidShare) goes for the collectors.
    //                 Chosen when the towers stand by the core and collectors are bare.
    //   siege_stack   one to three siege enemies (EnemyArchetypes.Siege) come with it,
    //                 each in place of SiegeWorth plain enemies. Chosen when the towers
    //                 stand by the core and there is nothing bare to raid.
    //   maze_breach   part of the wave comes as sappers, which dig through walls. Chosen
    //                 only against a long wall maze (2026-10-26, the student: sappers make
    //                 walls pointless if they come all the time; keep them rare). Every
    //                 other wave has no sappers: the composer's are sent as plain walkers.
    //   baseline      the wave as composed.
    //
    // At least half of every wave goes for the core: the core is the one thing that can
    // be lost, so it must stay worth defending. Raiders whose collector is gone walk on
    // to the next one and then to the core (AgentSystem), so a bare outpost lets them
    // through rather than soaking them up.
    //
    // It keeps no memory of its own: everything it decides comes from the state it is
    // shown (the previous wave's strategy is State.CurrentPlan). A retried wave is
    // therefore planned exactly as the first time.
    public sealed class RoughDirector : IWavePlanner
    {
        public const string Id = "rough-director";
        public const string Raid = "outpost_raid";
        public const string SiegeStack = "siege_stack";
        public const string Baseline = "baseline";
        public const string MazeBreach = "maze_breach";

        // The board's numbers that decide the choice, first guesses all.
        public const float CoreZoneTiles = 5f;       // a tower this close to the core counts as stacked there
        public const float StackFloor = 0.35f;       // a stack share below this is no stack at all
        public const float StackSpan = 0.5f;         // above the floor, this much more is a full stack
        public const float BareCover = 0.1f;         // a collector reached by less than this share of the damage is bare
        public const float BaselineScore = 0.3f;
        public const float RepeatPenalty = 0.15f;
        public const float SiegeWithOutposts = 0.6f; // siege counts for less while there are bare outposts to raid
        public const float MaxRaidShare = 0.4f;
        public const int SiegeWorth = 4;             // plain enemies a siege enemy replaces
        public const int FirstRaidWave = 4;
        public const int FirstSiegeWave = 5;
        public const int MinTowers = 3;
        public const float MazeFloor = 1.4f;         // a route this many times the straight line is no maze yet
        public const int MinWalls = 10;
        public const int FirstBreachWave = 6;
        public const float BreachShare = 0.3f;

        private readonly RaidPlanner composer;

        public RoughDirector(SimConfig config, MapData map) { composer = new RaidPlanner(config, map); }

        public string Name => Id;

        public void OnWaveResolved(WaveOutcome outcome) { }

        // One authored line per strategy, shown before the wave (CLAUDE.md §5: templated, never generated).
        public static string Line(string announcementId)
        {
            switch (announcementId)
            {
                case Raid: return "Raiders are coming for your undefended outposts.";
                case SiegeStack: return "Siege engines are coming for your tower cluster. Outrange them, slow them, or spread out.";
                case MazeBreach: return "Sappers are coming to dig through your maze.";
                default: return null;
            }
        }

        // What the director saw. Public for tests and the inspector.
        public struct Board
        {
            public int Towers;
            public float StackShare;  // share of tower damage within CoreZoneTiles of the core
            public float StackLean;   // 0 = spread, 1 = fully stacked
            public int Collectors;
            public int BareCollectors;
            public int Walls;        // wall tiles on the board
            public float Stretch;    // the shortest walk from an entry to the core over the straight line; 0 unknown
            public float BareShare => Collectors == 0 ? 0f : (float)BareCollectors / Collectors;
        }

        public static Board Read(WaveContext context)
        {
            SimState s = context.State;
            TileCoord core = context.Map.Core;
            var b = new Board();
            float total = 0f, near = 0f;
            float zone = (float)(CoreZoneTiles * CoreZoneTiles);
            for (int t = 0; t < s.Towers.Count; t++)
            {
                TowerState tower = s.Towers[t];
                if (!tower.IsAlive || tower.OnBench || tower.PowerCapacity > 0) continue;
                b.Towers++;
                float dps = Dps(tower);
                total = (float)(total + dps);
                int dx = tower.Tile.X - core.X, dy = tower.Tile.Y - core.Y;
                if ((float)(dx * dx + dy * dy) <= zone) near = (float)(near + dps);
            }
            b.StackShare = total > 0f ? (float)(near / total) : 0f;
            b.StackLean = Math.Max(0f, Math.Min(1f, (float)((float)(b.StackShare - StackFloor) / StackSpan)));

            for (int i = 0; i < s.Grid.NodeCount; i++) if (s.Grid.ByIndex(i).Occupant == Occupant.Wall) b.Walls++;
            for (int e = 0; e < context.Map.Spawns.Length; e++)
            {
                TileCoord at = context.Map.Spawns[e];
                int cost = s.Grid.Get(at).BestCost;
                int dx = at.X - core.X, dy = at.Y - core.Y;
                float straight = (float)Math.Sqrt((float)(dx * dx + dy * dy)); // exact under IEEE 754 (DetMath)
                if (cost == SimNode.Infinity || straight < 1f) continue;
                float stretch = (float)((float)(cost / 10f) / straight);
                if (b.Stretch == 0f || stretch < b.Stretch) b.Stretch = stretch;
            }

            MapData map = context.Map;
            if (map.Deposits != null)
                for (int d = 0; d < map.Deposits.Length; d++)
                {
                    TileCoord tile = map.Deposits[d].Tile;
                    if (s.Grid.Get(tile).Occupant != Occupant.Collector) continue;
                    b.Collectors++;
                    float cover = 0f;
                    for (int t = 0; t < s.Towers.Count; t++)
                    {
                        TowerState tower = s.Towers[t];
                        if (!tower.IsAlive || tower.OnBench || tower.PowerCapacity > 0) continue;
                        int dx = tower.Tile.X - tile.X, dy = tower.Tile.Y - tile.Y;
                        if ((float)(dx * dx + dy * dy) <= (float)(tower.RangeTiles * tower.RangeTiles)) cover = (float)(cover + Dps(tower));
                    }
                    if (cover < (float)(BareCover * total) || total <= 0f) b.BareCollectors++;
                }
            return b;
        }

        private static float Dps(TowerState t) { return (float)((float)(t.Damage * t.Targets) / Math.Max(1, t.FireIntervalTicks)); }

        // The authored scores (layer 2) and the choice. Ties go to the earlier of raid, siege, baseline.
        public static string Choose(Board b, int wave, string previous)
        {
            float raid = wave >= FirstRaidWave && b.Collectors > 0 && b.Towers >= MinTowers ? (float)(b.StackLean * b.BareShare) : float.NegativeInfinity;
            float siege = wave >= FirstSiegeWave && b.Towers >= MinTowers
                ? (float)(b.StackLean * (b.BareCollectors > 0 ? SiegeWithOutposts : 1f)) : float.NegativeInfinity;
            float baseline = BaselineScore;
            float breach = wave >= FirstBreachWave && b.Walls >= MinWalls && b.Stretch > MazeFloor
                ? Math.Min(1f, (float)(b.Stretch - MazeFloor)) : float.NegativeInfinity;
            if (previous == MazeBreach) breach = (float)(breach - RepeatPenalty);
            if (previous == Raid) raid = (float)(raid - RepeatPenalty);
            if (previous == SiegeStack) siege = (float)(siege - RepeatPenalty);
            if (raid >= siege && raid >= breach && raid >= baseline) return Raid;
            if (siege >= breach && siege >= baseline) return SiegeStack;
            if (breach >= baseline) return MazeBreach;
            return Baseline;
        }

        public WavePlan PlanWave(WaveContext context)
        {
            int wave = context.WaveIndex;
            Board b = Read(context);
            string previous = context.State.CurrentPlan != null ? context.State.CurrentPlan.StrategyId : null;
            string strategy = Choose(b, wave, previous);

            WavePlan plan = composer.Compose(wave, raids: false);
            AgentGroup basic = composer.Basic(wave);
            // No sappers but the maze breach's: the composer's go as plain walkers, as many as
            // the same share of the wave makes.
            for (int g = 0; g < plan.Groups.Length; g++)
                if (plan.Groups[g].Archetype == EnemyArchetypes.SapperId)
                {
                    float share = (float)(plan.Groups[g].Count / (float)(basic.Count * EnemyArchetypes.Sapper().CountFactor));
                    plan.Groups[g] = EnemyArchetypes.Basic().Apply(basic, share);
                }
            if (strategy == Raid)
            {
                // The worse the lean and the barer the outposts, the bigger the raid.
                float share = Math.Min(MaxRaidShare, (float)(0.15f + (float)(0.25f * (float)(b.StackLean * b.BareShare))));
                // Raiders come at half the count of plain enemies (EnemyArchetypes.Raider), so
                // the rest of the wave gives up half the share in their place.
                Scale(plan, (float)(1f - (float)(share * 0.5f)));
                Add(plan, EnemyArchetypes.Raider().Apply(basic, share));
            }
            else if (strategy == SiegeStack)
            {
                int count = 1 + (b.StackLean > 0.5f ? 1 : 0) + (b.StackLean > 0.85f ? 1 : 0);
                AgentGroup siege = EnemyArchetypes.Siege().Apply(basic, 1f);
                siege.Count = count;
                int total = plan.TotalAgents();
                Scale(plan, Math.Max(0.5f, total > 0 ? (float)(1f - (float)(count * SiegeWorth) / total) : 1f));
                Add(plan, siege);
            }
            else if (strategy == MazeBreach)
            {
                // Sappers come at half the count of plain enemies, like raiders.
                Scale(plan, (float)(1f - (float)(BreachShare * 0.5f)));
                Add(plan, EnemyArchetypes.Sapper().Apply(basic, BreachShare));
            }
            plan.StrategyId = strategy;
            plan.AnnouncementId = strategy == Baseline ? null : strategy;
            return RaidPlanner.Spread(plan, composer.ChooseEntries(context));
        }

        private static void Scale(WavePlan plan, float factor)
        {
            for (int g = 0; g < plan.Groups.Length; g++)
                plan.Groups[g].Count = Math.Max(1, (int)(float)(plan.Groups[g].Count * factor));
        }

        private static void Add(WavePlan plan, AgentGroup group)
        {
            var groups = new AgentGroup[plan.Groups.Length + 1];
            Array.Copy(plan.Groups, groups, plan.Groups.Length);
            groups[plan.Groups.Length] = group;
            plan.Groups = groups;
        }
    }
}
