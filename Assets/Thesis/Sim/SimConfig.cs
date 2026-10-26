using System;

namespace Thesis.Sim
{
    // Every tunable the simulation reads. The field initialisers are the values the
    // game is actually played with - SampleScene's serialized WaveSpawner /
    // BlockManager / PlayerCore values - NOT the C# field defaults in those scripts,
    // which differ (WaveSpawner.agentsPerWave is 15 in code but 100 in the scene).
    // ARCHITECTURE.md §4.3, last row. WP4's SimConfigAsset maps an inspector asset
    // onto this class.
    //
    // Durations are stored in seconds and converted to ticks in exactly one place,
    // Ticks(), so rounding lives in one spot.
    //
    // [Serializable] (a System attribute, so still engine-free) lets Unity show and
    // save this class directly inside SimConfigAsset: one definition of every
    // tunable, not a second hand-copied list that can drift.
    [Serializable]
    public sealed class SimConfig
    {
        // 50 Hz = Unity's default fixed timestep. x3 game speed means 3x as many
        // ticks per rendered frame, never a bigger tick.
        public float TickSeconds = 0.02f;

        // --- economy (BlockManager, PlayerCore) ---
        public float StartBudget = 60f;          // BlockManager.buildBudget
        public int CoreMaxHp = 10;               // PlayerCore.maxHealth
        public int StipendPerWave = 20;          // WaveSpawner.budgetStipendPerWave

        // --- pacing (WaveSpawner) ---
        public float PrepSeconds = 45f;          // initialPrepDuration
        public float IntermissionSeconds = 10f;  // intermissionDuration

        // --- agent. Rewards live here, not in a WavePlan: they pay the player, and
        // the director must never touch the player's budget (CLAUDE.md I11).
        public float DigRate = 1f;
        public float KillReward = 0.2f;          // was DeathReward: paid when the lifetime clock ran out
        public float WallBreakReward = 1f;       // yes, the player is paid when a wall is breached
        public float TowerBreachReward = 0f;     // a chewed-through tower pays nothing (spec gap S12)

        // --- static escalation (WaveSpawner, SampleScene values) ---
        public int AgentsPerWave = 100;
        public int AgentsPerWaveIncrement = 5;
        public float SpawnDelay = 0.2f;
        public float SpawnDelayDecrement = 0.02f;
        public float MinSpawnDelay = 0.15f;
        public float BaseMoveSpeed = 1.25f;
        public float MoveSpeedIncrementPerWave = 0.0375f;
        public float MaxMoveSpeed = 2.25f;

        // Hit points replace the lifetime that used to grow by 4 s a wave (WP-C1).
        // PLACEHOLDER numbers until the balance check (WP-C5, spec gap S12).
        public float BaseHp = 10f;
        public float HpIncrementPerWave = 2f;

        // --- shop and selling (WP-C3). Every number here is spec gap S11: a default
        // to play with, not a decision. ---
        // How many towers the shop offers per roll. It rolls at game start and every
        // time a wave resolves; each offer can be bought once.
        //
        // Four, not the three first planned: the starting budget of 60 buys four
        // towers, and with three offers a third of it cannot be spent on them. The
        // tower bot then lost wave 1 on 4 of 10 seeds, purely on what the first roll
        // held; with four offers it lost wave 1 on none (measured 2026-10-11, DEVLOG).
        public int ShopOffers = 4;

        // What a new set of offers costs. Negative means rerolls are off, which is
        // the default: with them on, a player who wants one tower can buy their way
        // to it, and the shop stops shaping what gets built.
        public float RerollCost = -1f;

        // Each reroll of the same roll costs this much more than the one before it; the
        // price is back at RerollCost when the next wave's cards come (2026-10-12).
        public float RerollCostStep = 0f;

        // The share of what was paid that selling returns: of a tower's price, and
        // of a wall piece's cost (scaled by how much of the piece still stands).
        public float SellRefund = 0.5f;
        public float WallSellRefund = 0.5f;

        // Selling is for build phases. Mid-wave it lets a player open and close a
        // gap to walk enemies back and forth under fire, the classic maze exploit.
        public bool SellDuringWave = false;

        // --- cards, levels and gear (2026-10-12; all off by default, so the numbers
        // the tests are written against play exactly as before) ---
        // The build phase has no clock: a wave starts when the player says so.
        public bool WaitForStart = false;

        // A shop slot holds a tower card, a coin card or a gear card. The two weights
        // are relative to a tower card's weight of 1; 0 means that kind never comes.
        // No two cards of one roll are the same thing (the same tower type, the same piece
        // of gear, two coin cards, two wall cards). A slot that would repeat is drawn again.
        public bool DistinctCards = false;

        // The cards before the first wave are all towers: a start with coins and gear
        // and nothing to put them on is not a start.
        public bool FirstRollTowersOnly = false;

        public float CoinCardWeight = 0f;
        public int CoinCardAmount = 8;
        public float GearCardWeight = 0f;

        // The chance that a tower card is rare (level 2) or epic (level 3). Such a
        // card costs what the tower and its upgrades would.
        public float RareTowerChance = 0f;
        public float EpicTowerChance = 0f;

        // Raising a tower one level costs its card's price times this (and half as
        // much again for the second level): TowerUpgrades.CostOf.
        public float UpgradeCostFactor = 1f;

        // Gear moves between towers for nothing, but only between waves.
        public bool MoveGearDuringWave = false;

        // The next wave is decided when a build phase BEGINS, not when it ends, so the
        // player can be told what is coming and build against it (SimState.NextPlan).
        // The planner then sees the board as it stood when the last wave resolved.
        public bool PlanAhead = false;

        // A roll of cards is a draft: the cards are free, and the player takes this
        // many of them (0 = off: every card can be bought, at its price). The roll at
        // game start allows PicksAtStart, because the first wave needs a defence.
        public int PicksPerRoll = 0;
        public int PicksAtStart = 2;

        // The gear shop: this many pieces on sale at their price, a new stock every
        // GearShopRefreshWaves waves. 0 slots = no gear shop. Its stock is a pure
        // function of the seed and of which stock it is (I10), like the cards.
        public int GearShopSlots = 0;
        public int GearShopRefreshWaves = 5;

        // A new stock for the gear shop on demand: this price, plus the step for every
        // reroll of the same stock. Negative = the gear shop cannot be rerolled.
        public float GearShopRerollCost = -1f;
        public float GearShopRerollStep = 0f;

        // --- combining, the tower cap and moving towers (2026-10-12, the third round;
        // all off by default) ---
        // This many towers of one type and one level merge into ONE tower of the next
        // level the moment the last of them is placed (TowerCombine). 0 or 1 = off.
        public int CombineCount = 0;

        // The most towers that may stand at once: TowerCap at the start and one more
        // for every TowerCapEveryWaves waves started. A tower that completes a set is
        // placed whatever the cap says, because the set then merges. 0 = no cap.
        public int TowerCap = 0;
        public int TowerCapEveryWaves = 0;

        // A standing tower can be put on another tile for nothing, between waves, with
        // its level and its gear.
        public bool MoveTowers = false;

        // --- the fourth round (2026-10-13): few, heavy actions. All off by default. ---
        // Walls come from a stock of tiles, not from the budget: WallStock at the start,
        // WallStockPerWave more after every wave. A wall laid in this build phase can be
        // taken up again freely; an older one costs one of WallRelayPerWave pick-ups.
        // A taken-up tile goes back to the stock; a breached one is lost. 0 = off.
        public int WallStock = 0;
        public int WallStockPerWave = 0;
        public int WallRelayPerWave = 0;

        // Any wall may be taken up in a build phase, as often as wanted: the limit of
        // WallRelayPerWave does not apply (2026-10-14: everything moves freely between waves).
        public bool FreeWallPickup = false;

        // A card that adds wall tiles to the stock (weight relative to a tower card's 1),
        // and a pack of tiles for budget (size 0 = not for sale).
        public float WallCardWeight = 0f;
        public int WallCardAmount = 3;
        public int WallPackSize = 0;
        public float WallPackPrice = 5f;

        // Moves: how many times per build phase a tower ON THE BOARD may be touched
        // (moved, sold, taken to the bench, brought from it, swapped with it). Placing
        // the wave's card, gear, and the bench's own order are free. 0 = no limit.
        // An extra move costs ExtraMovePrice, plus the step for each one already
        // bought this build phase; a negative price means none are for sale.
        public int MovesPerWave = 0;

        // Before the first wave nothing is limited: moves cost nothing, so the opening
        // layout can be tried and changed freely (2026-10-14).
        public bool FreeFirstBuild = false;
        public float ExtraMovePrice = -1f;
        public float ExtraMovePriceStep = 0f;

        // The bench: this many slots for towers that are owned but not on the board.
        public int BenchSlots = 0;

        // The gear inventory (2026-10-14): this many pieces of gear can be owned without
        // being on a tower. Gear is then bought or picked INTO it, and put on a tower,
        // taken off again, or moved, for nothing and as often as wanted. 0 = off.
        public int GearInventorySlots = 0;

        // A tower card can be merged into a tower of its type the player already has,
        // which then gains a level (or takes the card's level, if that is higher).
        public bool FeedDuplicates = false;

        // The one upgrade a tower can be given, with a choice of what it gains
        // (TowerUpgradeChoices): what it costs. Negative = off.
        public float ChoiceUpgradeCost = -1f;

        // How many of those upgrades one tower can take, and what each further one costs on
        // top of ChoiceUpgradeCost. Two (2026-10-14): a tower goes common, rare, epic by the
        // player's choices, and that is now the only way it gets rarer.
        public int UpgradesPerTower = 1;
        public float ChoiceUpgradeCostStep = 0f;

        // Towers stand only on a wall or on a rock of the map, never on open ground
        // (2026-10-14). The wall under a tower comes back when the tower leaves the tile.
        public bool TowersOnWalls = false;

        // A card that heals the core by HealCardAmount, up to CoreMaxHp (weight relative
        // to a tower card's 1). It is taken like a coin card and uses the pick.
        public float HealCardWeight = 0f;
        public int HealCardAmount = 3;

        // Which gear the shops draw from (GearDef.IsStatBoost, GearDef.Extracts): the plain
        // stat boosts (scope, whetstone, powder; 2026-10-14: a flat boost belongs to the
        // upgrade, not to gear), and the extractor that takes a tower's gear off.
        public bool StatGear = true;
        public bool ExtractorGear = false;

        // Beacon, anti-armor and lure (2026-10-20) in the shops and the crafting list.
        public bool AbilityGear = false;

        // --- the ore economy and the build menu (2026-10-14) ---
        // BuildMenu: towers are built from a menu for their price (TowerDef.Cost) instead
        // of drawn as cards, and gear is crafted into the inventory (GearDef.Price), both
        // up to the research tier reached. Research costs ResearchTier2Price, then
        // ResearchTier3Price. RepairPrice buys HealCardAmount hit points for the core.
        public bool BuildMenu = false;
        public float ResearchTier2Price = 30f;
        public float ResearchTier3Price = 60f;
        public float RepairPrice = 10f;

        // What crafting a piece of gear costs, as a share of its price (2026-10-23: halved).
        public float GearCraftPriceFactor = 1f;

        // How many actions of a build phase can be undone (commands Undo and Redo,
        // 2026-10-24). 0 = no undo.
        public int UndoSteps = 0;

        // A collector on an ore deposit (MapData.Deposits): what it costs, and what it
        // is as terrain. It pays its deposit's yield after every wave it stands through.
        public float CollectorPrice = 6f;
        public int CollectorDigCost = 20;
        public float CollectorHealth = 8f;

        // The share of a collector's price paid back the moment enemies destroy it
        // (2026-10-20: losing one hurt too much). 0 = nothing.
        public float CollectorLossRefund = 0f;

        // Power (2026-10-20, after StarCraft's pylons): a tower fires only while powered.
        // The core powers up to CorePowerCapacity towers within CorePowerRadius tiles,
        // every standing collector up to CollectorPowerCapacity within CollectorPowerRadius.
        // Off: every tower is powered, as before.
        public bool PowerRule = false;
        public float CorePowerRadius = 4f;
        public int CorePowerCapacity = 4;
        public float CollectorPowerRadius = 3f;
        public int CollectorPowerCapacity = 2;

        // Enemies a wave: AgentsPerWaveIncrement more every AgentsIncrementEveryWaves
        // waves; hit points grow by HpGrowthPerWave (a share) on top of HpIncrementPerWave.
        public int AgentsIncrementEveryWaves = 1;
        public float HpGrowthPerWave = 0f;

        // A power tower (TowerDef.PowerCapacity) may not stand within this many tiles of the core
        // (2026-10-26, with the pylon at 18 ore: cheap power, but not for a stack round the core).
        // 0 = anywhere.
        public float PylonCoreKeepOut = 0f;

        // The player may set which enemy a tower shoots (TowerState.Mode).
        public bool ChooseTargeting = false;

        // The siege enemy (2026-10-26; only the rough director sends it). It stops as soon as a
        // tower stands within SiegeRangeTiles, sets up for SiegeSetupTicks (a slow stretches
        // that), then every SiegeIntervalTicks hits the tower with the most towers round it,
        // and every tower within SiegeSplashTiles of that one, for SiegeDamage. A tower that
        // has taken TowerSiegeHp is knocked out: it does not fire until the next build phase,
        // when every tower is whole again. It is never destroyed by this.
        public float SiegeRangeTiles = 4.5f;
        public int SiegeSetupTicks = 100;
        public int SiegeIntervalTicks = 75;
        public float SiegeDamage = 4f;
        public float SiegeSplashTiles = 1.5f;
        public float TowerSiegeHp = 10f;

        // How many copies make the next level when the player merges (TowerMerge): 2 =
        // one card or one tower into another, 3 = three of a kind. Counts the one that stays.
        public int MergeCount = 2;

        // Before the first wave the player may put the core on another tile, at least
        // CoreEdgeMargin tiles from the edge.
        public bool CorePlacement = false;
        public int CoreEdgeMargin = 6;

        // Entry points (RaidPlanner): an entry needs a route to the core at least this
        // many tiles long, and two entries of one wave are at least this far apart.
        public float MinEntryRouteTiles = 12f;
        public int MinEntrySpacingTiles = 10;

        // --- movement classes (WP-C2) ---
        // The share of a built tile's price that a SAPPER pays when it chooses a
        // route. A wall of cost 15 reads as a 15-tile detour to an ordinary enemy
        // and, at 0.2, as a 3-tile detour to a sapper, so sappers go through walls
        // that everyone else walks around. 1 = sappers route like everyone else;
        // 0 = they ignore what is built. It changes only the ROUTE: how fast a sapper
        // chews is its DigRate. PLACEHOLDER until the balance check (spec gap S12).
        public float SapperDigCostFactor = 0.2f;

        // --- every wave must end (ARCHITECTURE.md §4.6) ---
        // The lifetime clock used to guarantee that every enemy resolves. Without
        // it, two things do. A slow can never take an enemy below this share of its
        // speed, so a slowed enemy still advances. And if a wave has still not
        // resolved after MaxWaveSeconds, its remaining enemies are removed (no core
        // damage, no reward) and the outcome is marked TimedOut. That backstop
        // should never fire in a healthy game.
        public float MinSlowFactor = 0.25f;
        public float MaxWaveSeconds = 600f;

        // --- per-wave statistics ---
        // An agent "pressured" the core if it leaked or its cheapest BestCost/10
        // (tile units) fell to this radius or below. Placeholder until spec gap S5
        // is closed; it only feeds WaveOutcome.PressureShare, which nothing reads yet.
        public float PressureRadiusTiles = 5f;

        // Allowed |ThreatSpent - budget| when an IThreatPricer is present (I2).
        public float ThreatEpsilon = 1e-3f;

        public int Ticks(float seconds)
        {
            return (int)MathF.Round((float)(seconds / TickSeconds));
        }

        // Every field is a plain number or flag, so a shallow copy is a full copy. A host
        // gives the simulation its own copy, so that editing the source object later
        // (the config asset in Unity's inspector, during play) cannot change a
        // running game.
        public SimConfig Clone() { return (SimConfig)MemberwiseClone(); }

        public void Validate()
        {
            if (!(TickSeconds > 0f)) throw new InvalidOperationException("SimConfig.TickSeconds must be positive, was " + TickSeconds + ".");
            if (CoreMaxHp <= 0) throw new InvalidOperationException("SimConfig.CoreMaxHp must be positive, was " + CoreMaxHp + ".");
            if (PrepSeconds < 0f || IntermissionSeconds < 0f) throw new InvalidOperationException("SimConfig prep/intermission durations must not be negative.");
            if (!(BaseMoveSpeed > 0f)) throw new InvalidOperationException("SimConfig.BaseMoveSpeed must be positive: an enemy that cannot move never resolves.");
            if (!(BaseHp > 0f)) throw new InvalidOperationException("SimConfig.BaseHp must be positive, was " + BaseHp + ".");
            if (HpIncrementPerWave < 0f) throw new InvalidOperationException("SimConfig.HpIncrementPerWave must not be negative, was " + HpIncrementPerWave + ".");
            if (!(MinSlowFactor > 0f) || MinSlowFactor > 1f) throw new InvalidOperationException("SimConfig.MinSlowFactor must be in (0, 1], was " + MinSlowFactor + ".");
            if (!(MaxWaveSeconds > 0f)) throw new InvalidOperationException("SimConfig.MaxWaveSeconds must be positive, was " + MaxWaveSeconds + ".");
            if (!(SapperDigCostFactor >= 0f) || SapperDigCostFactor > 1f) throw new InvalidOperationException("SimConfig.SapperDigCostFactor must be in [0, 1], was " + SapperDigCostFactor + ".");
            if (ShopOffers < 0 || ShopOffers > 16) throw new InvalidOperationException("SimConfig.ShopOffers must be between 0 and 16, was " + ShopOffers + ".");
            if (!(RerollCostStep >= 0f) || float.IsInfinity(RerollCostStep)) throw new InvalidOperationException("SimConfig.RerollCostStep must be a number >= 0, was " + RerollCostStep + ".");
            if (AgentsIncrementEveryWaves < 1 || !(HpGrowthPerWave >= 0f) || HpGrowthPerWave > 1f) throw new InvalidOperationException("SimConfig.AgentsIncrementEveryWaves must be at least 1 and HpGrowthPerWave in [0, 1].");
            if (UpgradesPerTower < 1 || UpgradesPerTower > 2) throw new InvalidOperationException("SimConfig.UpgradesPerTower must be 1 or 2, was " + UpgradesPerTower + ".");
            if (HealCardAmount < 0) throw new InvalidOperationException("SimConfig.HealCardAmount must not be negative.");
            if (MergeCount < 2 || MergeCount > 9) throw new InvalidOperationException("SimConfig.MergeCount must be between 2 and 9, was " + MergeCount + ".");
            if (GearInventorySlots < 0 || GearInventorySlots > 32) throw new InvalidOperationException("SimConfig.GearInventorySlots must be between 0 and 32.");
            if (WallStock < 0 || WallStockPerWave < 0 || WallRelayPerWave < 0 || MovesPerWave < 0 || BenchSlots < 0 || BenchSlots > 16 || CoreEdgeMargin < 0)
                throw new InvalidOperationException("SimConfig: the wall stock, the moves, the bench slots (at most 16) and the core margin must not be negative.");
            if (CombineCount < 0 || TowerCap < 0 || TowerCapEveryWaves < 0) throw new InvalidOperationException("SimConfig.CombineCount, TowerCap and TowerCapEveryWaves must not be negative.");
            if (float.IsNaN(RerollCost) || float.IsInfinity(RerollCost)) throw new InvalidOperationException("SimConfig.RerollCost must be a number (negative turns rerolls off), was " + RerollCost + ".");
            // Above 1 a sale would pay more than the purchase cost: free budget for buying and selling in a loop.
            if (!(SellRefund >= 0f) || SellRefund > 1f) throw new InvalidOperationException("SimConfig.SellRefund must be in [0, 1], was " + SellRefund + ".");
            if (!(WallSellRefund >= 0f) || WallSellRefund > 1f) throw new InvalidOperationException("SimConfig.WallSellRefund must be in [0, 1], was " + WallSellRefund + ".");
        }
    }
}
