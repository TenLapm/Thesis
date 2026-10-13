using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // The first real roster (WP-C5): the towers the game sells, as exported to
    // Maps/Towers.json, and the enemy types. What is pinned here is what the work plan
    // asked of the roster, not its numbers: those are the balance check's
    // (Results/2026-10-12_balance-v1) and will be tuned again.
    public class RosterTests
    {
        [Test]
        public void TheRosterCoversEveryRoleTheWorkPlanAsksFor()
        {
            TowerDef[] towers = TestSims.GameTowers();
            Assert.That(towers.Length, Is.InRange(4, 10), "four to ten towers (eight since the third round of 2026-10-12; the work plan asked for four to six)");

            var types = new HashSet<DamageType>();
            bool single = false, splash = false, slow = false, antiAir = false, groundOnly = false;
            foreach (TowerDef t in towers)
            {
                types.Add(t.DamageType);
                if (t.SplashRadiusTiles > 0f) splash = true;
                else if (t.SlowTicks > 0) slow = true;
                else single = true;
                if (t.CanHitFlying) antiAir = true; else groundOnly = true;
            }

            Assert.GreaterOrEqual(types.Count, 3, "three damage types");
            Assert.IsTrue(single, "a single-target tower");
            Assert.IsTrue(splash, "a splash tower");
            Assert.IsTrue(slow, "a slowing tower");
            Assert.IsTrue(antiAir, "a tower that can hit flyers");
            Assert.IsTrue(groundOnly, "and one that cannot, or flyers would be no different from walkers");
        }

        [Test]
        public void ARosterWrittenToAFileIsReadBackTheSame()
        {
            TowerDef[] towers = TestSims.GameTowers();
            TowerDef[] back = TowerLibraryFile.FromJson(new TowerLibraryFile { Source = "test", Towers = towers }.ToJson()).Towers;

            Assert.AreEqual(towers.Length, back.Length);
            for (int i = 0; i < towers.Length; i++) Assert.AreEqual(towers[i].ToString(), back[i].ToString(), "order and numbers");

            // Stated as the one number a replay cares about.
            MapData map = TestSims.GameMap();
            Assert.AreEqual(ReplaySetup.Hash(new SimConfig(), map, TestSims.GameShapes(), towers), ReplaySetup.Hash(new SimConfig(), map, TestSims.GameShapes(), back));
        }

        [Test]
        public void ARosterWithNoTowersOrTwoOfOneNameIsRefused()
        {
            Assert.Throws<InvalidDataException>(() => new TowerLibraryFile { Source = "t", Towers = new TowerDef[0] }.Validate());

            var twice = new TowerLibraryFile { Source = "t", Towers = new[] { TestTowers.Gun(id: "a"), TestTowers.Gun(id: "a") } };
            var e = Assert.Throws<InvalidDataException>(() => twice.Validate());
            StringAssert.Contains("'a'", e.Message);

            var broken = new TowerLibraryFile { Source = "t", Towers = new[] { new TowerDef { Id = "x", FireIntervalTicks = 0 } } };
            Assert.Throws<InvalidDataException>(() => broken.Validate());
            Assert.Throws<FileNotFoundException>(() => TowerLibraryFile.Load(Path.Combine(TestPaths.Maps, "no-such-roster.json")));
        }

        // An enemy type is a set of multiples of that wave's plain walker.
        [Test]
        public void AnEnemyTypeIsAppliedToTheWavesBasicEnemy()
        {
            var basic = new AgentGroup { Count = 100, Hp = 20f, MoveSpeed = 2f, DigRate = 1f, SpawnIntervalTicks = 10 };

            AgentGroup same = EnemyArchetypes.Basic().Apply(basic, 1f);
            Assert.AreEqual(basic.ToString(), same.ToString(), "the basic type changes nothing");
            Assert.AreNotSame(basic, same);

            AgentGroup swarm = EnemyArchetypes.Swarm().Apply(basic, 1f);
            Assert.AreEqual(MovementClass.Ground, swarm.Movement);
            Assert.AreEqual(200, swarm.Count);
            Assert.AreEqual(8f, swarm.Hp);
            Assert.AreEqual(2.6f, swarm.MoveSpeed, 1e-5f);
            Assert.AreEqual(5, swarm.SpawnIntervalTicks, "half as far apart");

            AgentGroup sappers = EnemyArchetypes.Sapper().Apply(basic, 0.5f);
            Assert.AreEqual(MovementClass.Sapper, sappers.Movement);
            Assert.AreEqual(25, sappers.Count, "half the wave, at half the number");
            Assert.AreEqual(4f, sappers.DigRate);

            AgentGroup flyers = EnemyArchetypes.Flyer().Apply(basic, 1f);
            Assert.AreEqual(MovementClass.Flying, flyers.Movement);
            Assert.AreEqual(0f, flyers.DigRate, "a flyer never digs");

            Assert.AreEqual(1, EnemyArchetypes.Sapper().Apply(new AgentGroup { Count = 1, Hp = 1f, MoveSpeed = 1f, SpawnIntervalTicks = 1 }, 0.5f).Count, "never fewer than one");
        }

        [Test]
        public void EveryTypeHasItsOwnNameAndTheFourAreBasicSwarmSapperFlyer()
        {
            var ids = new List<string>();
            foreach (EnemyArchetype type in EnemyArchetypes.All()) ids.Add(type.Id);
            CollectionAssert.AreEqual(new[] { "basic", "swarm", "sapper", "flyer" }, ids);
            Assert.AreEqual(EscalationPlanner.Archetype, EnemyArchetypes.BasicId, "the baseline's enemy is the basic type");
        }

        // What the balance check reads: every tower ever bought, with what it cost and did.
        [Test]
        public void AnEpisodeReportsEveryTowerItBought()
        {
            EpisodeResult run = EpisodeRunner.Run(new EpisodeOptions
            {
                Map = TestSims.GameMap(),
                Shapes = TestSims.GameShapes(),
                Towers = TestSims.GameTowers(),
                Seed = 7, // the pinned restless episode: it lives through five waves and sells
                Policy = Registry.CreatePolicy("restless"),
                MaxWaves = 5,
            });

            Assert.AreEqual(run.TowersPlaced, run.Towers.Count);
            Assert.Greater(run.Towers.Count, 0);
            float damage = 0f;
            bool sold = false;
            foreach (TowerSummary t in run.Towers)
            {
                Assert.IsNotEmpty(t.Id);
                Assert.Greater(t.PricePaid, 0);
                Assert.GreaterOrEqual(t.TicksOnBoard, 0);
                damage += t.DamageDealt;
                if (t.Sold) sold = true;
            }
            Assert.Greater(damage, 0f, "the towers hit something");
            Assert.IsTrue(sold, "the restless player sold one");
        }
    }
}
