using System.Collections.Generic;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // Upgrade paths, their abilities, flat gear, the per-wave damage of each tower and
    // restarting a running wave (2026-10-27).
    public class PathsAndFlatGearTests
    {
        private const string Row = "C . . . . . . . . . . S";

        private sealed class Rig
        {
            public AsciiFixture F;
            public List<TowerState> Towers = new List<TowerState>();
            public List<AgentState> Agents = new List<AgentState>();
            public DamageMap Damage;
            public float[] ByType = new float[DamageTypes.Count];
            public float Budget;

            public TowerState Tower(TowerDef def, int x, int upgrade = TowerUpgradeChoices.None)
            {
                TowerState t = TestTowers.StateOn(F, Towers.Count, def, x, 0);
                t.Upgrade = upgrade;
                t.Recompute(null);
                Towers.Add(t);
                return t;
            }

            public AgentState AgentAt(TowerState tower, float tiles, float hp, MovementClass movement = MovementClass.Ground)
            {
                var a = TestTowers.Agent(Agents.Count, new Vec2f(tower.Position.X + tiles * 2f, tower.Position.Y), hp, movement: movement);
                Agents.Add(a);
                return a;
            }

            public void Step(int times = 1)
            {
                for (int i = 0; i < times; i++) TowerSystem.Step(F.Grid, F.Grid.Get(F.Map.Core), Towers, Agents, Damage, ByType, 0.25f, ref Budget, null);
            }
        }

        private static Rig NewRig()
        {
            var rig = new Rig { F = TestMaps.Parse(Row) };
            new FlowField().Generate(rig.F.Grid, rig.F.Map.Core);
            rig.Damage = new DamageMap(rig.F.Grid.NodeCount);
            return rig;
        }

        // A gun of 10 damage every 10 ticks with one upgrade.
        private static TowerDef With(TowerLevelMod mod, bool flying = false)
        {
            TowerDef def = TestTowers.Gun(damage: 10f, range: 3f, interval: 10, canHitFlying: flying);
            def.Upgrades = new[] { mod };
            return def;
        }

        [Test]
        public void EveryGameTowerHasThreePathsOfTwoStepsAndTheSecondUpgradeFollowsTheFirst()
        {
            foreach (TowerDef def in TestSims.GameTowers())
            {
                TowerLevelMod[] list = TowerUpgradeChoices.For(def);
                Assert.AreEqual(6, list.Length, def.Id);
                for (int i = 0; i < list.Length; i++)
                {
                    Assert.AreEqual(i / 2 + 1, list[i].Path, def.Id + " " + list[i].Id);
                    Assert.AreEqual(i % 2 + 1, list[i].Step, def.Id + " " + list[i].Id);
                    Assert.IsFalse(string.IsNullOrEmpty(list[i].PathName));
                }

                var tower = new TowerState(0, def, new TileCoord(0, 0), new Vec2f(0f, 0f), 2f, 0);
                CollectionAssert.AreEqual(new[] { 0, 2, 4 }, TowerUpgradeChoices.Options(tower), def.Id + ": a first step of each path");
                tower.Upgrade = 2;
                CollectionAssert.AreEqual(new[] { 3 }, TowerUpgradeChoices.Options(tower), def.Id + ": then that path's second step only");
                tower.Upgrade2 = 3;
                Assert.AreEqual(0, TowerUpgradeChoices.Options(tower).Length);
            }
        }

        [Test]
        public void AnUpgradeOffThePathIsRefused()
        {
            var config = new SimConfig { ShopOffers = 0, StartBudget = 200f, BuildMenu = true, UpgradesPerTower = 2, WaitForStart = true, ChoiceUpgradeCost = 5f };
            AsciiFixture f = TestMaps.Parse("S . . . . C");
            var sim = new Simulation(config, f.Map, TestShapes.SampleSceneLibrary(), TestSims.GameTowers(), 1, new EscalationPlanner(config, f.Map));
            TestSims.Send(sim, SimCommand.BuyTower(0, 2, 0)); // the archer
            TowerState archer = sim.State.Towers[0];
            TestSims.Send(sim, SimCommand.ChooseUpgrade(2, 0, 1));
            Assert.AreEqual(0, archer.UpgradeCount, "a second step first");
            TestSims.Send(sim, SimCommand.ChooseUpgrade(2, 0, 4));
            TestSims.Send(sim, SimCommand.ChooseUpgrade(2, 0, 1));
            Assert.AreEqual(1, archer.UpgradeCount, "the Ranger's second step on a Marksman");
            TestSims.Send(sim, SimCommand.ChooseUpgrade(2, 0, 5));
            Assert.AreEqual(2, archer.UpgradeCount);
            Assert.AreEqual(3, archer.CritEvery);
            Assert.AreEqual(5f, archer.CritMul);
        }

        [Test]
        public void WithResearchUnlockingUpgradesEveryTowerIsBuiltAtOnceAndEachTierOpensOneUpgrade()
        {
            var config = new SimConfig { ShopOffers = 0, StartBudget = 500f, BuildMenu = true, UpgradesPerTower = 2, WaitForStart = true, ChoiceUpgradeCost = 5f,
                                         ResearchUnlocksUpgrades = true, ResearchTier2Price = 10f, ResearchTier3Price = 20f };
            AsciiFixture f = TestMaps.Parse("S . . . . C");
            var sim = new Simulation(config, f.Map, TestShapes.SampleSceneLibrary(), TestSims.GameTowers(), 1, new EscalationPlanner(config, f.Map));
            int sniper = System.Array.FindIndex(sim.TowerLibrary, d => d.Id == "sniper");
            Assert.AreEqual(3, sim.TowerLibrary[sniper].Tier);
            TestSims.Send(sim, SimCommand.BuyTower(sniper, 2, 0));
            Assert.AreEqual(1, sim.State.Towers.Count, "a tier 3 tower at research tier 1");
            TowerState tower = sim.State.Towers[0];

            TestSims.Send(sim, SimCommand.ChooseUpgrade(2, 0, 0));
            Assert.AreEqual(0, tower.UpgradeCount, "no upgrade at tier 1");
            TestSims.Send(sim, SimCommand.Research());
            TestSims.Send(sim, SimCommand.ChooseUpgrade(2, 0, 0));
            TestSims.Send(sim, SimCommand.ChooseUpgrade(2, 0, 1));
            Assert.AreEqual(1, tower.UpgradeCount, "tier 2: the first upgrade only");
            TestSims.Send(sim, SimCommand.Research());
            TestSims.Send(sim, SimCommand.ChooseUpgrade(2, 0, 1));
            Assert.AreEqual(2, tower.UpgradeCount, "tier 3: the second");
        }

        [Test]
        public void EveryThirdShotIsACrit()
        {
            Rig rig = NewRig();
            TowerState tower = rig.Tower(With(new TowerLevelMod { CritEvery = 3, CritMul = 3f }), 2, 0);
            AgentState target = rig.AgentAt(tower, 1f, 1000f);
            var dealt = new List<float>();
            float before = target.Hp;
            for (int shot = 0; shot < 6; shot++)
            {
                rig.Step(10);
                dealt.Add(before - target.Hp);
                before = target.Hp;
            }
            CollectionAssert.AreEqual(new[] { 10f, 10f, 30f, 10f, 10f, 30f }, dealt);
        }

        [Test]
        public void ExecuteFinishesAWoundedEnemyAndFlyerDamageCountsOnlyOnFlyers()
        {
            Rig rig = NewRig();
            TowerState executioner = rig.Tower(With(new TowerLevelMod { ExecuteBelow = 0.5f }), 2, 0);
            AgentState big = rig.AgentAt(executioner, 1f, 30f);
            rig.Step();
            Assert.AreEqual(20f, big.Hp, "two thirds left: not yet");
            rig.Step(10);
            Assert.IsFalse(big.IsAlive, "10 more would leave a third, under half: finished");
            Assert.AreEqual(30f, executioner.WaveDamage, "the whole of it counts as its damage");

            Rig air = NewRig();
            TowerState hunter = air.Tower(With(new TowerLevelMod { FlyingDamageMul = 2f }, flying: true), 2, 0);
            AgentState flyer = air.AgentAt(hunter, 1f, 100f, MovementClass.Flying);
            air.Step();
            Assert.AreEqual(80f, flyer.Hp);
        }

        [Test]
        public void ABurnHurtsEveryTickAndIsCreditedToTheTowerThatLitIt()
        {
            Rig rig = NewRig();
            TowerDef def = With(new TowerLevelMod { DamageMul = 0f, BurnDps = 50f, BurnTicks = 50 });
            def.FireIntervalTicks = 1000;
            TowerState torch = rig.Tower(def, 2, 0);
            AgentState target = rig.AgentAt(torch, 1f, 1000f);
            rig.Step(1);
            Assert.AreEqual(50, target.BurnTicks);
            rig.Step(50);
            Assert.AreEqual(950f, target.Hp, 0.01f, "50 a second for one second");
            Assert.AreEqual(50f, torch.WaveDamage, 0.01f);
            Assert.AreEqual(0, target.BurnTicks);
        }

        [Test]
        public void AnExposedEnemyTakesMoreFromEveryTower()
        {
            Rig rig = NewRig();
            TowerDef spotterDef = With(new TowerLevelMod { DamageMul = 0f, ExposeMul = 2f, ExposeTicks = 100 });
            TowerState spotter = rig.Tower(spotterDef, 2, 0);
            TowerState gun = rig.Tower(TestTowers.Gun(damage: 10f, range: 3f, interval: 10), 3);
            AgentState target = rig.AgentAt(spotter, 1f, 1000f);
            rig.Step(1);
            Assert.AreEqual(980f, target.Hp, "the gun's first shot, the same tick the spotter's landed");
            Assert.AreEqual(100, target.ExposeTicks);
        }

        [Test]
        public void FlatGearAddsDamageAndRangeAndIsAllTheShopsSellWithFlatGear()
        {
            GearDef[] gear = GearRoster.Default();
            int edge = System.Array.FindIndex(gear, g => g.Id == "edge");
            var t = new TowerState(0, TestTowers.Gun(damage: 0.8f, range: 2f), new TileCoord(0, 0), new Vec2f(0f, 0f), 2f, 0);
            t.Gear[0] = edge;
            t.Recompute(gear);
            Assert.AreEqual(1.8f, t.Damage, 1e-5f);

            var config = new SimConfig { FlatGear = true, ExtractorGear = true, GearCardWeight = 5f };
            var sold = new HashSet<string>();
            for (int stock = 0; stock < 60; stock++)
                foreach (TowerOffer item in ShopRoller.RollGearShop(3, stock, gear, 3, 0, config)) sold.Add(gear[item.GearIndex].Id);
            CollectionAssert.AreEquivalent(new[] { "scope", "edge", "keen-edge", "far-scope", "extractor" }, sold);
        }

        [Test]
        public void ARunningWaveCanBeRestartedFromItsBuildPhaseAndTheWaveDamageStartsAgain()
        {
            var config = new SimConfig { ShopOffers = 0, StartBudget = 100f, BuildMenu = true, WaitForStart = true, CoreMaxHp = 1000 };
            AsciiFixture f = TestMaps.Parse("S . . . . . . . . C");
            var sim = new Simulation(config, f.Map, TestShapes.SampleSceneLibrary(), new[] { TestTowers.Gun(damage: 1f, range: 3f, cost: 5) }, 1,
                                     new EscalationPlanner(config, f.Map));
            TestSims.Send(sim, SimCommand.BuyTower(0, 6, 0));
            TestSims.Send(sim, SimCommand.StartWaveNow());
            TestSims.Run(sim, 400);
            Assert.AreEqual(1, sim.State.WaveIndex);
            Assert.IsTrue(sim.State.Phase == SimPhase.Spawning || sim.State.Phase == SimPhase.Resolving, "the wave is running");
            Assert.Greater(sim.State.Towers[0].WaveDamage, 0f);
            Assert.IsTrue(sim.CanRetry);

            TestSims.Send(sim, SimCommand.RetryWave());
            SimState s = sim.State;
            Assert.AreEqual(0, s.WaveIndex, "back in the build phase before wave 1");
            Assert.AreEqual(0, s.LiveAgentCount);
            Assert.AreEqual(0, s.Towers.Count, "as the build phase began: what was bought in it is bought back");
            Assert.AreEqual(100f, s.BuildBudget);
            Assert.AreEqual(1, s.Retries);

            TestSims.Send(sim, SimCommand.BuyTower(0, 6, 0));
            TestSims.Send(sim, SimCommand.StartWaveNow());
            TestSims.Run(sim, 2);
            Assert.AreEqual(0f, s.Towers[0].WaveDamage, 0.5f, "counted from this wave");
        }
    }
}
