using System.Collections.Generic;
using Thesis.Core;
using Thesis.Director;

namespace Thesis.Tests
{
    // Telemetry rows written by hand, for tests of what reads them (the placement
    // entropy, the file format) without running a game first.
    public static class TestRows
    {
        public static WaveRecord Row(int wave, params WaveRecord.PlacementRow[] placements)
        {
            return new WaveRecord { Session = "hand", Condition = "escalation", Player = "test", Build = "test", Map = "hand", RngSeed = 1, Wave = wave, Placements = placements };
        }

        // A wall piece on these tiles, given as x, y, x, y, ...
        public static WaveRecord.PlacementRow Wall(params int[] xy)
        {
            return new WaveRecord.PlacementRow { Kind = WaveRecord.PlacementRow.Wall, Name = "T", X = xy[0], Y = xy[1], Tiles = Tiles(xy), Cost = 4f, Piece = 0 };
        }

        public static WaveRecord.PlacementRow Tower(int x, int y)
        {
            return new WaveRecord.PlacementRow { Kind = WaveRecord.PlacementRow.Tower, Name = "archer", X = x, Y = y, Tiles = Tiles(x, y), Cost = 15f, TowerId = 0 };
        }

        public static WaveRecord.PlacementRow SoldWall(params int[] xy)
        {
            return new WaveRecord.PlacementRow { Kind = WaveRecord.PlacementRow.SellWall, Name = "T", X = xy[0], Y = xy[1], Tiles = Tiles(xy), Refund = 2f, Piece = 0 };
        }

        public static WaveRecord.PlacementRow SoldTower(int x, int y)
        {
            return new WaveRecord.PlacementRow { Kind = WaveRecord.PlacementRow.SellTower, Name = "archer", X = x, Y = y, Tiles = Tiles(x, y), Refund = 7.5f, TowerId = 0 };
        }

        public static WaveRecord.PlacementRow Reroll()
        {
            return new WaveRecord.PlacementRow { Kind = WaveRecord.PlacementRow.Reroll, Name = "reroll", Cost = 5f };
        }

        public static int[][] Tiles(params int[] xy)
        {
            var tiles = new int[xy.Length / 2][];
            for (int i = 0; i < tiles.Length; i++) tiles[i] = new[] { xy[2 * i], xy[2 * i + 1] };
            return tiles;
        }

        // A row as the adaptive director will write it (WP10, WP11): every field
        // of the schema filled in. No code produces this yet; the schema is declared
        // now so that the baseline and the director write the same class.
        public static WaveRecord DirectorRow()
        {
            WaveRecord row = Row(4, Wall(3, 3, 4, 3, 5, 3, 4, 4), Tower(9, 9), SoldTower(2, 2));
            row.Condition = "director";
            row.TickStart = 4000;
            row.TickResolved = 5200;
            row.TickEnd = 5700;
            row.Profile = new NamedValues().Add("maze_length", 0.62f).Add("breach_vulnerability", 0.2f).Add("chokepoint_reliance", 0.75f)
                                           .Add("tower_concentration", 0.4f).Add("damage_type_mix", 0.33f);
            row.Bin = 3;
            row.ThreatBudget = 140f;
            row.Strategies = new[]
            {
                new WaveRecord.StrategyScore { Id = "breach_thin_wall", Heuristic = 0.7f, Theta = 0.81f, Cold = false, Correction = 0.62f, Score = 1.32f },
                new WaveRecord.StrategyScore { Id = "swarm_chokepoint", Heuristic = 0.9f, Theta = 0.5f, Cold = true, Correction = 0f, Score = 0.9f },
                new WaveRecord.StrategyScore { Id = "flying_bypass", Veto = "Uncounterable", Score = float.NegativeInfinity },
            };
            row.Chosen = "breach_thin_wall";
            row.Announcement = "They have found the thin wall.";
            row.Plan = new WaveRecord.PlanRow
            {
                Strategy = "breach_thin_wall",
                ThreatSpent = 140f,
                Groups = new[]
                {
                    new WaveRecord.GroupRow
                    {
                        Spawn = 0, Archetype = "sapper", Movement = "sapper", Count = 40, Hp = 13.5f, MoveSpeed = 4.2f, DigRate = 4f,
                        Resist = new NamedValues().Add("physical", 1).Add("fire", 1).Add("frost", 0.5f), IntervalTicks = 12, StartDelayTicks = 50,
                    },
                },
            };
            row.Outcome = new WaveRecord.OutcomeRow
            {
                Spawned = 40, Killed = 31, Leaked = 9, Breaches = 3, TowersDestroyed = 1, CoreHpBefore = 10, CoreHpAfter = 1,
                BudgetBefore = 12.4f, BudgetAfter = 21.6f, Pressure = 0.45f,
                DamageByType = new NamedValues().Add("physical", 412.5f).Add("fire", 0).Add("frost", 88f),
                SpawnedByClass = new NamedValues().Add("ground", 0).Add("sapper", 40).Add("flying", 0),
                KilledByClass = new NamedValues().Add("ground", 0).Add("sapper", 31).Add("flying", 0),
                LeakedByClass = new NamedValues().Add("ground", 0).Add("sapper", 9).Add("flying", 0),
            };
            row.Shop = new WaveRecord.ShopRow
            {
                Offered = new[] { "cannon", "archer", "frost", "archer" },
                Bought = new[] { "archer" },
                Sold = new[] { new WaveRecord.SaleRow { Def = "archer", X = 2, Y = 2, Refund = 7.5f } },
                Rerolls = 1,
            };
            row.Reward = new WaveRecord.RewardRow
            {
                Restructure = 0.8f, Survived = 1f, Pressure = 0.45f, Novelty = 0.3f, Total = 0.73f, Binary = 1,
                Credited = new[] { new WaveRecord.CreditRow { Wave = 4, Weight = 1f }, new WaveRecord.CreditRow { Wave = 3, Weight = 0.5f } },
            };
            row.EstimatorDigest = "9f1c2ab04d77e310";
            row.DirectorMicros = 412;
            row.StateHash = "00ff00ff00ff00ff";
            return row;
        }

        public static List<WaveRecord> List(params WaveRecord[] rows) { return new List<WaveRecord>(rows); }
    }
}
