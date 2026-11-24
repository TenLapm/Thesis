using System.IO;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // A tester's three points of 2026-11-24, as the student decided them. Each rule is a SimConfig
    // switch, on in the game and off in the C# defaults (the older rules the tests and pinned
    // replays keep).
    public class TesterRulesTests
    {
        private static SimConfig Game() { return Json.Deserialize<SimConfig>(File.ReadAllText(Path.Combine(TestPaths.Maps, "SimConfig.json"))); }

        private static Simulation New(SimConfig config)
        {
            MapData map = GameMaps.Generate(109);
            var sim = new Simulation(config, map, TestSims.GameShapes(), TestSims.GameTowers(), 109UL, new EscalationPlanner(config, map));
            TestSims.Send(sim, SimCommand.PlaceCore(sim.State.Core.X, sim.State.Core.Y));
            return sim;
        }

        [Test]
        public void TheGameTurnsAllThreeOnAndTheDefaultsLeaveThemOff()
        {
            SimConfig game = Game(), old = new SimConfig();
            Assert.IsTrue(game.CollectorOnDryDeposit && game.TowerAddsToFoundation && game.PercentDamageGear);
            Assert.IsFalse(old.CollectorOnDryDeposit || old.TowerAddsToFoundation || old.PercentDamageGear);
        }

        // 1. "If the vein runs out the collector can't be placed again and then the tower won't get powered."
        [TestCase(true)] [TestCase(false)]
        public void ACollectorGoesBackOnADryDepositAsPowerOnly(bool rule)
        {
            SimConfig config = Game();
            config.CollectorOnDryDeposit = rule;
            Simulation sim = New(config);
            SimState s = sim.State;
            Deposit d = sim.Map.Deposits[0];
            s.DepositLeft[0] = 0; // run dry
            TestSims.Send(sim, SimCommand.BuildCollector(d.Tile.X, d.Tile.Y));
            Assert.AreEqual(rule ? Occupant.Collector : Occupant.None, s.Grid.Get(d.Tile).Occupant);
            if (rule) Assert.AreEqual(0, sim.CollectorIncome(s), "it earns nothing");
        }

        // 2. "The tower on a fort wall is somehow more attractive than a normal fort wall."
        [TestCase(true)] [TestCase(false)]
        public void ATowerOnAWallAddsItsToughnessToTheWalls(bool rule)
        {
            SimConfig config = Game();
            config.TowerAddsToFoundation = rule;
            Simulation sim = New(config);
            SimState s = sim.State;
            // A fort wall (tier 2) two tiles from the core, within its power: the tester's case.
            SimNode rock = null;
            foreach (var (dx, dy) in new[] { (2, 0), (-2, 0), (0, 2), (0, -2), (2, 2), (-2, -2) })
            {
                var at = new TileCoord(s.Core.X + dx, s.Core.Y + dy);
                if (rock != null || !s.Grid.InBounds(at) || s.Grid.Get(at).Occupant != Occupant.None || !s.Grid.Get(at).IsWalkable) continue;
                TestSims.Send(sim, SimCommand.PlaceShape(at.X, at.Y, 2));
                if (s.Grid.Get(at).Occupant == Occupant.Wall) rock = s.Grid.Get(at);
            }
            Assert.IsNotNull(rock, "rig: a fort wall by the core");
            int rockCost = rock.TerrainCost;
            float rockHealth = rock.WallHealth;
            int archer = 0;
            TestSims.Send(sim, SimCommand.BuyTower(archer, rock.X, rock.Y));
            SimNode tile = s.Grid.Get(new TileCoord(rock.X, rock.Y));
            Assert.AreEqual(Occupant.Tower, tile.Occupant, "rig: the tower stands");
            TowerDef def = sim.TowerLibrary[archer];
            if (rule)
            {
                Assert.AreEqual(rockCost + def.DigCost, tile.TerrainCost, "walking through costs both");
                Assert.AreEqual(rockHealth + def.TowerHealth, tile.WallHealth, 1e-4f, "chewing through takes both");
            }
            else Assert.AreEqual(def.DigCost, tile.TerrainCost, "the old rule: the tower's own numbers");

            // Sold, the rock is back as it was.
            TestSims.Send(sim, SimCommand.SellTower(rock.X, rock.Y));
            Assert.AreEqual(rockCost, tile.TerrainCost);
        }

        [Test]
        public void AMovedTowerTakesOnlyItsOwnPartAndLeavesTheWallWhole()
        {
            Simulation sim = New(Game());
            SimState s = sim.State;
            var fort = new TileCoord(s.Core.X + 2, s.Core.Y);
            var other = new TileCoord(s.Core.X, s.Core.Y + 2);
            TestSims.Send(sim, SimCommand.PlaceShape(fort.X, fort.Y, 2));
            TestSims.Send(sim, SimCommand.PlaceShape(other.X, other.Y, 0)); // a plain wall
            Assume.That(s.Grid.Get(fort).Occupant == Occupant.Wall && s.Grid.Get(other).Occupant == Occupant.Wall, "rig: two walls by the core");
            int fortCost = s.Grid.Get(fort).TerrainCost, wallCost = s.Grid.Get(other).TerrainCost;
            TestSims.Send(sim, SimCommand.BuyTower(0, fort.X, fort.Y));
            TestSims.Send(sim, SimCommand.MoveTower(fort.X, fort.Y, other.X, other.Y));
            Assert.AreEqual(Occupant.Tower, s.Grid.Get(other).Occupant, "rig: moved");
            Assert.AreEqual(fortCost, s.Grid.Get(fort).TerrainCost, "the fort stays whole");
            Assert.AreEqual(wallCost + sim.TowerLibrary[0].DigCost, s.Grid.Get(other).TerrainCost, "the tower adds only its own part to the new wall");
        }

        // 3. "Damage gear is less of a benefit on a tower with a damage upgrade than on one with crowd control."
        [Test]
        public void WithPercentGearTheDamageGearMultipliesAndTheFlatPiecesAreNotSold()
        {
            SimConfig config = Game();
            GearDef[] roster = GearRoster.Default();
            GearDef honed = null, edge = null;
            foreach (GearDef g in roster) { if (g.Id == "honed") honed = g; if (g.Id == "edge") edge = g; }
            Assert.IsNotNull(honed);
            Assert.Greater(honed.DamageMul, 1f);
            Assert.IsTrue(honed.OnSale(config));
            Assert.IsFalse(edge.OnSale(config), "the flat +1 a second is replaced");

            config.PercentDamageGear = false;
            Assert.IsFalse(honed.OnSale(config));
            Assert.IsTrue(edge.OnSale(config));
            Assert.IsFalse(honed.OnSale(new SimConfig()), "never in the older rounds' shops");
        }
    }
}
