using System;
using Thesis.Core;

namespace Thesis.Sim
{
    // DEVELOPMENT ONLY (2026-10-26): a rough first cut of the director's authored layer
    // (CLAUDE.md §5, layer 2), built early because the student found towers stacked at the
    // core still beat most waves. Not a study condition and not the director: no learning,
    // no build profile, no threat cost table. It reads the board as the build phase begins
    // and picks one way to send the wave that RaidPlanner composes:
    //
    //   outpost_raid  part of the wave (at most MaxRaidShare) goes for the collectors.
    //                 Chosen when collectors stand bare, wherever the towers are.
    //   siege_stack   one to three siege enemies (EnemyArchetypes.Siege) come with it,
    //                 each in place of SiegeWorth plain enemies. Chosen when most of the
    //                 damage comes from one cluster of towers, anywhere on the board.
    //   resist_mix    part of the wave shrugs off the damage type most of the damage is
    //                 (armoured against physical, ember against fire).
    //   air_raid      part of the wave flies, when few towers can hit flyers (never when
    //                 none can: the player must have an answer).
    //   maze_breach   part of the wave comes as sappers, which dig through walls. Chosen
    //                 only against a long wall maze (2026-10-26, the student: sappers make
    //                 walls pointless if they come all the time; keep them rare). Every
    //                 other wave has no sappers: the composer's are sent as plain walkers.
    //   baseline      the wave as composed.
    //
    // 2026-10-28, after a tester's 77 waves in which it answered nothing after wave 11 (it
    // looked only at towers next to the core, and that player spread them): every weakness is
    // scored on its own, the cluster anywhere, the raid without needing a stack.
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
        public const string ResistMix = "resist_mix";
        public const string AirRaid = "air_raid";
        public const string ResistPhysical = "resist_physical"; // announcement ids of resist_mix
        public const string ResistFire = "resist_fire";

        // The board's numbers that decide the choice, first guesses all.
        public const float ClusterTiles = 3f;        // towers this close to one another count as one cluster
        public const float StackFloor = 0.4f;        // a cluster share below this is no stack at all
        public const float StackSpan = 0.4f;         // above the floor, this much more is a full stack
        public const float BareCover = 0.1f;         // a collector reached by less than this share of the damage is bare
        public const float BaselineScore = 0.3f;
        public const float RepeatPenalty = 0.15f;
        public const float MaxRaidShare = 0.4f;
        public const int SiegeWorth = 4;             // plain enemies a siege enemy replaces
        public const int FirstRaidWave = 4;
        public const int FirstSiegeWave = 5;
        public const int FirstCounterWave = 5;
        public const int MinTowers = 4;
        public const float MazeFloor = 1.4f;         // a route this many times the straight line is no maze yet
        public const int MinWalls = 10;
        public const int FirstBreachWave = 6;
        public const float BreachShare = 0.3f;
        public const float DominantFloor = 0.6f;     // one damage type above this share of the damage is relied on
        public const float AirWeak = 0.25f;          // anti-air below this share of the damage is weak
        public const float CounterShare = 0.4f;      // the share of the wave a counter (resist, air) takes

        private readonly RaidPlanner composer;

        public RoughDirector(SimConfig config, MapData map) { composer = new RaidPlanner(config, map); }

        public string Name => Id;

        public void OnWaveResolved(WaveOutcome outcome) { }

        // One authored line per announcement, shown before the wave (CLAUDE.md §5: templated, never generated).
        public static string Line(string announcementId)
        {
            switch (announcementId)
            {
                case Raid: return "Raiders are coming for your undefended outposts.";
                case SiegeStack: return "Siege engines are coming for your tower cluster. Outrange them, slow them, or spread out.";
                case MazeBreach: return "Sappers are coming to dig through your maze.";
                case ResistPhysical: return "Armoured enemies are coming: they shrug off physical damage.";
                case ResistFire: return "Ember enemies are coming: they shrug off fire.";
                case AirRaid: return "Flyers are coming, and few of your towers can hit them.";
                default: return null;
            }
        }

        // What the director saw. Public for tests and the inspector.
        public struct Board
        {
            public int Towers;
            public float StackShare;  // share of tower damage in the densest cluster (ClusterTiles)
            public float StackLean;   // 0 = spread, 1 = fully stacked
            public int Collectors;
            public int BareCollectors;
            public int Walls;         // wall tiles on the board
            public float Stretch;     // the shortest walk from an entry to the core over the straight line; 0 unknown
            public DamageType Dominant;
            public float DominantShare; // share of the damage of the commonest type
            public float AirShare;    // share of the damage from towers that can hit flyers
            public float BareShare => Collectors == 0 ? 0f : (float)BareCollectors / Collectors;
        }

        public static Board Read(WaveContext context)
        {
            SimState s = context.State;
            TileCoord core = context.Map.Core;
            var b = new Board();
            float total = 0f, air = 0f;
            var byType = new float[DamageTypes.Count];
            float cluster = (float)(ClusterTiles * ClusterTiles);
            for (int t = 0; t < s.Towers.Count; t++)
            {
                TowerState tower = s.Towers[t];
                if (!Shoots(tower)) continue;
                b.Towers++;
                float dps = Dps(tower);
                total = (float)(total + dps);
                byType[(int)tower.Def.DamageType] = (float)(byType[(int)tower.Def.DamageType] + dps);
                if (tower.CanHitFlying) air = (float)(air + dps);
            }
            // The densest cluster: for each tower, the damage of every tower within ClusterTiles of it.
            float densest = 0f;
            for (int t = 0; t < s.Towers.Count; t++)
            {
                TowerState tower = s.Towers[t];
                if (!Shoots(tower)) continue;
                float here = 0f;
                for (int u = 0; u < s.Towers.Count; u++)
                {
                    TowerState other = s.Towers[u];
                    if (!Shoots(other)) continue;
                    int dx = other.Tile.X - tower.Tile.X, dy = other.Tile.Y - tower.Tile.Y;
                    if ((float)(dx * dx + dy * dy) <= cluster) here = (float)(here + Dps(other));
                }
                if (here > densest) densest = here;
            }
            b.StackShare = total > 0f ? (float)(densest / total) : 0f;
            b.StackLean = Math.Max(0f, Math.Min(1f, (float)((float)(b.StackShare - StackFloor) / StackSpan)));
            for (int k = 0; k < byType.Length; k++)
            {
                float share = total > 0f ? (float)(byType[k] / total) : 0f;
                if (share > b.DominantShare) { b.DominantShare = share; b.Dominant = (DamageType)k; }
            }
            b.AirShare = total > 0f ? (float)(air / total) : 0f;

            for (int i = 0; i < s.Grid.NodeCount; i++) if (s.Grid.ByIndex(i).Occupant == Occupant.Wall && s.Grid.ByIndex(i).PieceId != SimNode.RockPiece) b.Walls++;
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
                        if (!Shoots(tower)) continue;
                        int dx = tower.Tile.X - tile.X, dy = tower.Tile.Y - tile.Y;
                        if ((float)(dx * dx + dy * dy) <= (float)(tower.RangeTiles * tower.RangeTiles)) cover = (float)(cover + Dps(tower));
                    }
                    if (cover < (float)(BareCover * total) || total <= 0f) b.BareCollectors++;
                }
            return b;
        }

        private static bool Shoots(TowerState t) { return t.IsAlive && !t.OnBench && t.PowerCapacity == 0; }

        private static float Dps(TowerState t) { return (float)((float)(t.Damage * t.Targets) / Math.Max(1, t.FireIntervalTicks)); }

        private static float Clamp01(float v) { return Math.Max(0f, Math.Min(1f, v)); }

        // The authored scores (layer 2) and the choice. Ties go to the earlier of raid, siege,
        // resist, air, breach, baseline.
        public static string Choose(Board b, int wave, string previous)
        {
            float none = float.NegativeInfinity;
            float raid = wave >= FirstRaidWave && b.Collectors > 0 ? (float)(b.BareShare * Math.Min(1f, (float)(b.Collectors / 2f))) : none;
            float siege = wave >= FirstSiegeWave && b.Towers >= MinTowers ? b.StackLean : none;
            float resist = wave >= FirstCounterWave && b.Towers >= MinTowers && b.Dominant != DamageType.Frost
                ? Clamp01((float)((float)(b.DominantShare - DominantFloor) / (float)(1f - DominantFloor))) : none;
            // Veto (layer 1): no flyers against a board that cannot hit them at all.
            float air = wave >= FirstCounterWave && b.Towers >= MinTowers && b.AirShare > 0f && b.AirShare < AirWeak
                ? Clamp01((float)((float)(AirWeak - b.AirShare) / AirWeak)) : none;
            float breach = wave >= FirstBreachWave && b.Walls >= MinWalls && b.Stretch > MazeFloor
                ? Math.Min(1f, (float)(b.Stretch - MazeFloor)) : none;
            float baseline = BaselineScore;
            if (previous == Raid) raid = (float)(raid - RepeatPenalty);
            if (previous == SiegeStack) siege = (float)(siege - RepeatPenalty);
            if (previous == ResistMix) resist = (float)(resist - RepeatPenalty);
            if (previous == AirRaid) air = (float)(air - RepeatPenalty);
            if (previous == MazeBreach) breach = (float)(breach - RepeatPenalty);

            string best = Baseline;
            float top = baseline;
            // Walked from the last tie-break to the first, so the earliest of equals wins.
            if (breach >= top) { best = MazeBreach; top = breach; }
            if (air >= top) { best = AirRaid; top = air; }
            if (resist >= top) { best = ResistMix; top = resist; }
            if (siege >= top) { best = SiegeStack; top = siege; }
            if (raid >= top) { best = Raid; top = raid; }
            return best;
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
            string announcement = strategy;
            if (strategy == Raid)
            {
                // The barer the outposts, the bigger the raid.
                float share = Math.Min(MaxRaidShare, (float)(0.15f + (float)(0.25f * b.BareShare)));
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
            else if (strategy == ResistMix)
            {
                EnemyArchetype resistant = b.Dominant == DamageType.Fire ? EnemyArchetypes.Ember() : EnemyArchetypes.Armored();
                announcement = b.Dominant == DamageType.Fire ? ResistFire : ResistPhysical;
                Scale(plan, (float)(1f - (float)(CounterShare * resistant.CountFactor)));
                Add(plan, resistant.Apply(basic, CounterShare));
            }
            else if (strategy == AirRaid)
            {
                EnemyArchetype flyer = EnemyArchetypes.Flyer();
                Scale(plan, (float)(1f - (float)(CounterShare * flyer.CountFactor)));
                Add(plan, flyer.Apply(basic, CounterShare));
            }
            else if (strategy == MazeBreach)
            {
                // Sappers come at half the count of plain enemies, like raiders.
                Scale(plan, (float)(1f - (float)(BreachShare * 0.5f)));
                Add(plan, EnemyArchetypes.Sapper().Apply(basic, BreachShare));
            }
            plan.StrategyId = strategy;
            plan.AnnouncementId = strategy == Baseline ? null : announcement;
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
