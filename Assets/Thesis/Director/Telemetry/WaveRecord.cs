using Newtonsoft.Json;
using Thesis.Core;

namespace Thesis.Director
{
    // One line of telemetry: one wave and the player's building around it
    // (ARCHITECTURE.md §5.8). Schema 1.
    //
    // A row's WINDOW runs from the start of its wave to the start of the next one:
    // the fight, and the build phase after it. That is the stretch in which the
    // player answers this wave, so it is what "placements" and "shop" cover. Row 0
    // is the build phase before the first wave; it has no plan and no outcome.
    //
    // Ticks are the simulation's own: tickStart is the tick the wave began on and
    // tickResolved the tick it resolved on. Something built on the very tick a wave
    // begins was built BEFORE it (a tick applies the player's commands first) and
    // is in the row before. So a row's placements have tickStart < tick <= tickEnd,
    // and row 0's have 0 <= tick <= tickEnd.
    //
    // Every study condition writes this same row. What only a director knows
    // (profile, bin, strategies, reward ...) is null in the static baseline, so one
    // piece of analysis code reads them all.
    //
    // The JSON name and position of every field is spelled out, not left to the
    // field's C# name and declaration order: the file's bytes must not change
    // because someone renamed or moved a field. Add fields; never reuse a name for
    // something else without raising CurrentSchema.
    public sealed class WaveRecord
    {
        public const int CurrentSchema = 9; // 9: collector, sellCollector and research rows, collectorsLost (2026-10-14); // // 8: heal rows; an upgrade row may be a tower's second (2026-10-14); // 7: gearReroll rows (2026-10-14); // 6: gearSold rows; combine rows now also come from a merge by choice (2026-10-14); // 5: gearStore, equip and unequip rows (2026-10-14); // 4: walls, buyMove, bench, deploy, recall, feed and core rows (2026-10-13); // 2: cards (tower, coin, gear), tower levels and gear in the build log; 3: the placement kinds "combine" and "moveTower" (both 2026-10-12)

        [JsonProperty("schema", Order = 1)] public int Schema = CurrentSchema;

        // --- which session ---
        [JsonProperty("session", Order = 2)] public string Session;
        [JsonProperty("condition", Order = 3)] public string Condition;   // IWavePlanner.Name
        [JsonProperty("player", Order = 4)] public string Player;         // "human", or a scripted policy's name
        [JsonProperty("build", Order = 5)] public string Build;
        [JsonProperty("map", Order = 6)] public string Map;
        [JsonProperty("mapSeed", Order = 7)] public ulong MapSeed;
        [JsonProperty("rngSeed", Order = 8)] public ulong RngSeed;

        // --- which wave, and its window in ticks ---
        [JsonProperty("wave", Order = 9)] public int Wave;
        [JsonProperty("tickStart", Order = 10)] public int TickStart;
        [JsonProperty("tickResolved", Order = 11)] public int TickResolved = -1;   // -1: row 0, or the session ended first
        [JsonProperty("tickEnd", Order = 12)] public int TickEnd;                  // the next wave's start, or the session's last tick

        // --- the director's decision (null without a director) ---
        [JsonProperty("profile", Order = 13)] public NamedValues Profile;
        [JsonProperty("bin", Order = 14)] public int? Bin;
        [JsonProperty("threatBudget", Order = 15)] public float? ThreatBudget;
        [JsonProperty("strategies", Order = 16)] public StrategyScore[] Strategies;
        [JsonProperty("chosen", Order = 17)] public string Chosen;
        [JsonProperty("fallback", Order = 18)] public string Fallback;             // SafePlanner's reason, when it had to step in
        [JsonProperty("announcement", Order = 19)] public string Announcement;

        // --- what was sent and how it went ---
        [JsonProperty("plan", Order = 20)] public PlanRow Plan;                    // null in row 0
        [JsonProperty("outcome", Order = 21)] public OutcomeRow Outcome;        // null in row 0, and when the session ended mid-wave

        // --- what the player did in the window ---
        [JsonProperty("placements", Order = 22)] public PlacementRow[] Placements = new PlacementRow[0];
        [JsonProperty("shop", Order = 23)] public ShopRow Shop = new ShopRow();

        // --- learning (null without a director) ---
        [JsonProperty("reward", Order = 24)] public RewardRow Reward;
        [JsonProperty("estimatorDigest", Order = 25)] public string EstimatorDigest;
        [JsonProperty("directorMicros", Order = 26)] public long? DirectorMicros;

        // The simulation's state hash on the tick the wave resolved: the same value
        // the replay stores for that wave. Null when the wave did not resolve.
        [JsonProperty("stateHash", Order = 27)] public string StateHash;

        public string ToJsonLine() { return Json.SerializeLine(this); }

        public static WaveRecord FromJsonLine(string line) { return Json.DeserializeLine<WaveRecord>(line); }

        public sealed class PlanRow
        {
            [JsonProperty("strategy", Order = 1)] public string Strategy;
            [JsonProperty("threatSpent", Order = 2)] public float ThreatSpent;
            [JsonProperty("groups", Order = 3)] public GroupRow[] Groups = new GroupRow[0];
        }

        public sealed class GroupRow
        {
            [JsonProperty("spawn", Order = 1)] public int Spawn;
            [JsonProperty("archetype", Order = 2)] public string Archetype;
            [JsonProperty("movement", Order = 3)] public string Movement;          // "ground", "sapper", "flying"
            [JsonProperty("count", Order = 4)] public int Count;
            [JsonProperty("hp", Order = 5)] public float Hp;
            [JsonProperty("moveSpeed", Order = 6)] public float MoveSpeed;
            [JsonProperty("digRate", Order = 7)] public float DigRate;
            [JsonProperty("resist", Order = 8)] public NamedValues Resist;         // per damage type
            [JsonProperty("intervalTicks", Order = 9)] public int IntervalTicks;
            [JsonProperty("startDelayTicks", Order = 10)] public int StartDelayTicks;
        }

        public sealed class OutcomeRow
        {
            [JsonProperty("spawned", Order = 1)] public int Spawned;
            [JsonProperty("killed", Order = 2)] public int Killed;
            [JsonProperty("leaked", Order = 3)] public int Leaked;
            [JsonProperty("removed", Order = 4)] public int Removed;
            [JsonProperty("timedOut", Order = 5)] public bool TimedOut;
            [JsonProperty("breaches", Order = 6)] public int Breaches;
            [JsonProperty("towersDestroyed", Order = 7)] public int TowersDestroyed;
            [JsonProperty("coreHpBefore", Order = 8)] public int CoreHpBefore;
            [JsonProperty("coreHpAfter", Order = 9)] public int CoreHpAfter;
            [JsonProperty("coreDestroyed", Order = 10)] public bool CoreDestroyed;
            [JsonProperty("budgetBefore", Order = 11)] public float BudgetBefore;
            [JsonProperty("budgetAfter", Order = 12)] public float BudgetAfter;
            [JsonProperty("pressure", Order = 13)] public float Pressure;
            [JsonProperty("damageByType", Order = 14)] public NamedValues DamageByType;
            [JsonProperty("spawnedByClass", Order = 15)] public NamedValues SpawnedByClass;
            [JsonProperty("killedByClass", Order = 16)] public NamedValues KilledByClass;
            [JsonProperty("leakedByClass", Order = 17)] public NamedValues LeakedByClass;
            [JsonProperty("collectorsLost", Order = 18)] public int CollectorsLost;
        }

        // One row of the simulation's build log (PlacementRecord), in order.
        public sealed class PlacementRow
        {
            public const string Wall = "wall";
            public const string Tower = "tower";
            public const string SellWall = "sellWall";
            public const string SellTower = "sellTower";
            public const string Reroll = "reroll";
            public const string Upgrade = "upgrade";
            public const string Gear = "gear";
            public const string MoveGear = "moveGear";
            public const string Coin = "coin";
            public const string Combine = "combine";
            public const string MoveTower = "moveTower";
            public const string Walls = "walls";
            public const string BuyMove = "buyMove";
            public const string Bench = "bench";
            public const string Deploy = "deploy";
            public const string Recall = "recall";
            public const string Feed = "feed";
            public const string Core = "core";
            public const string GearStore = "gearStore";
            public const string Equip = "equip";
            public const string Unequip = "unequip";
            public const string GearSold = "gearSold";
            public const string GearReroll = "gearReroll";
            public const string Heal = "heal";
            public const string Collector = "collector";
            public const string SellCollector = "sellCollector";
            public const string Research = "research";

            [JsonProperty("tick", Order = 1)] public int Tick;
            [JsonProperty("kind", Order = 2)] public string Kind;
            [JsonProperty("name", Order = 3)] public string Name;                  // the shape, or the tower type
            [JsonProperty("rot", Order = 4)] public int Rot;
            [JsonProperty("x", Order = 5)] public int X;
            [JsonProperty("y", Order = 6)] public int Y;
            // Every tile built or cleared, as [x, y]. The primary metric counts
            // tiles, so they are written out; a reader needs no shape geometry.
            [JsonProperty("tiles", Order = 7)] public int[][] Tiles = new int[0][];
            [JsonProperty("cost", Order = 8)] public float Cost;
            [JsonProperty("refund", Order = 9)] public float Refund;
            [JsonProperty("piece", Order = 10)] public int Piece = -1;             // a wall piece's id (the row that built it)
            [JsonProperty("tower", Order = 11)] public int TowerId = -1;

            [JsonIgnore] public bool IsBuild => Kind == Wall || Kind == Tower;
        }

        public sealed class ShopRow
        {
            // The offers of the roll made in this window: at game start for row 0,
            // and the moment the wave resolved for every other row. Empty when the
            // session ended before the wave did. They are what the player could buy
            // in the build phase of this window AND during the next wave's fight.
            [JsonProperty("offered", Order = 1)] public string[] Offered = new string[0];
            [JsonProperty("bought", Order = 2)] public string[] Bought = new string[0];
            [JsonProperty("sold", Order = 3)] public SaleRow[] Sold = new SaleRow[0];
            [JsonProperty("rerolls", Order = 4)] public int Rerolls;
        }

        public sealed class SaleRow
        {
            [JsonProperty("def", Order = 1)] public string Def;
            [JsonProperty("x", Order = 2)] public int X;
            [JsonProperty("y", Order = 3)] public int Y;
            [JsonProperty("refund", Order = 4)] public float Refund;
        }

        // ---- filled by the director (WP10, WP11); declared now so that every
        // condition's rows are the same class from the first session on ----

        public sealed class StrategyScore
        {
            [JsonProperty("id", Order = 1)] public string Id;
            [JsonProperty("veto", Order = 2)] public string Veto;
            [JsonProperty("heuristic", Order = 3)] public float? Heuristic;
            [JsonProperty("theta", Order = 4)] public float? Theta;
            [JsonProperty("cold", Order = 5)] public bool? Cold;
            [JsonProperty("correction", Order = 6)] public float? Correction;
            [JsonProperty("score", Order = 7)] public float? Score;               // "-Infinity" for a vetoed strategy
        }

        public sealed class RewardRow
        {
            [JsonProperty("restructure", Order = 1)] public float Restructure;
            [JsonProperty("survived", Order = 2)] public float Survived;
            [JsonProperty("pressure", Order = 3)] public float Pressure;
            [JsonProperty("novelty", Order = 4)] public float Novelty;
            [JsonProperty("total", Order = 5)] public float Total;
            [JsonProperty("binary", Order = 6)] public int Binary;
            [JsonProperty("credited", Order = 7)] public CreditRow[] Credited = new CreditRow[0];
        }

        public sealed class CreditRow
        {
            [JsonProperty("wave", Order = 1)] public int Wave;
            [JsonProperty("weight", Order = 2)] public float Weight;
        }
    }
}
