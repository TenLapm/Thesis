using NUnit.Framework;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // The development planner that sends sappers and flyers until the director's
    // strategies exist (WP-C2). Not a study condition.
    public class ClassCyclePlannerTests
    {
        private static ClassCyclePlanner Planner() { return new ClassCyclePlanner(new SimConfig(), TestSims.SampleSceneMap()); }

        private static AgentGroup Escalation(int wave)
        {
            return new EscalationPlanner(new SimConfig(), TestSims.SampleSceneMap()).PlanFor(wave).Groups[0];
        }

        [Test]
        public void TheCycleIsGroundThenSappersThenFlyersThenAllThree()
        {
            ClassCyclePlanner planner = Planner();
            for (int wave = 1; wave <= 12; wave++)
            {
                WavePlan plan = planner.PlanFor(wave);
                Assert.AreEqual(wave, plan.WaveIndex);
                Assert.AreEqual(ClassCyclePlanner.Id, plan.StrategyId);

                switch ((wave - 1) % 4)
                {
                    case 0:
                        Assert.AreEqual(1, plan.Groups.Length, "wave " + wave);
                        Assert.AreEqual(MovementClass.Ground, plan.Groups[0].Movement, "wave " + wave);
                        break;
                    case 1:
                        Assert.AreEqual(1, plan.Groups.Length, "wave " + wave);
                        Assert.AreEqual(MovementClass.Sapper, plan.Groups[0].Movement, "wave " + wave);
                        break;
                    case 2:
                        Assert.AreEqual(1, plan.Groups.Length, "wave " + wave);
                        Assert.AreEqual(MovementClass.Flying, plan.Groups[0].Movement, "wave " + wave);
                        break;
                    default:
                        Assert.AreEqual(3, plan.Groups.Length, "wave " + wave);
                        Assert.AreEqual(MovementClass.Ground, plan.Groups[0].Movement, "wave " + wave);
                        Assert.AreEqual(MovementClass.Sapper, plan.Groups[1].Movement, "wave " + wave);
                        Assert.AreEqual(MovementClass.Flying, plan.Groups[2].Movement, "wave " + wave);
                        break;
                }
            }
        }

        [Test]
        public void AGroundWaveIsTheEscalationWaveUnchanged()
        {
            foreach (int wave in new[] { 1, 5, 9 })
            {
                AgentGroup g = Planner().PlanFor(wave).Groups[0];
                AgentGroup e = Escalation(wave);
                Assert.AreEqual(e.Count, g.Count);
                Assert.AreEqual(e.Hp, g.Hp);
                Assert.AreEqual(e.MoveSpeed, g.MoveSpeed);
                Assert.AreEqual(e.DigRate, g.DigRate);
                Assert.AreEqual(e.SpawnIntervalTicks, g.SpawnIntervalTicks);
                Assert.AreEqual(EscalationPlanner.Archetype, g.Archetype);
            }
        }

        // Hand-computed from the escalation numbers (EscalationPlannerTests):
        // wave 2 is 105 enemies of 12 HP at 1.2875; wave 3 is 110 of 14 HP at 1.325.
        [Test]
        public void SappersAndFlyersAreMultiplesOfThatWavesEscalationEnemy()
        {
            AgentGroup sappers = Planner().PlanFor(2).Groups[0];
            Assert.AreEqual(ClassCyclePlanner.SapperArchetype, sappers.Archetype);
            Assert.AreEqual(52, sappers.Count, "half of 105, rounded down");
            Assert.AreEqual(18f, sappers.Hp, "12 x 1.5");
            Assert.AreEqual(1.03f, sappers.MoveSpeed, 1e-5f, "1.2875 x 0.8");
            Assert.AreEqual(4f, sappers.DigRate, "they chew four times as fast");
            Assert.AreEqual(Escalation(2).SpawnIntervalTicks, sappers.SpawnIntervalTicks);

            AgentGroup flyers = Planner().PlanFor(3).Groups[0];
            Assert.AreEqual(ClassCyclePlanner.FlyerArchetype, flyers.Archetype);
            Assert.AreEqual(55, flyers.Count, "half of 110");
            Assert.AreEqual(7f, flyers.Hp, "14 x 0.5");
            Assert.AreEqual(1.59f, flyers.MoveSpeed, 1e-5f, "1.325 x 1.2");
            Assert.AreEqual(0f, flyers.DigRate, "a flyer never digs");

            // Wave 4: 115 enemies of 16 HP at 1.3625, split a half and two quarters.
            WavePlan mixed = Planner().PlanFor(4);
            Assert.AreEqual(57, mixed.Groups[0].Count);
            Assert.AreEqual(28, mixed.Groups[1].Count);
            Assert.AreEqual(28, mixed.Groups[2].Count);
            Assert.AreEqual(16f, mixed.Groups[0].Hp);
            Assert.AreEqual(24f, mixed.Groups[1].Hp);
            Assert.AreEqual(8f, mixed.Groups[2].Hp);
            Assert.AreEqual(113, mixed.TotalAgents());
        }

        [Test]
        public void EveryPlanItMakesIsValid()
        {
            var config = new SimConfig();
            MapData map = TestSims.SampleSceneMap();
            var planner = new ClassCyclePlanner(config, map);
            for (int wave = 1; wave <= 60; wave++)
            {
                Assert.IsNull(PlanValidator.Check(planner.PlanFor(wave), wave, map, config, null), "wave " + wave);
            }

            // A tiny wave still sends at least one of each.
            var tiny = new ClassCyclePlanner(new SimConfig { AgentsPerWave = 1, AgentsPerWaveIncrement = 0 }, map);
            foreach (AgentGroup g in tiny.PlanFor(4).Groups) Assert.AreEqual(1, g.Count);
        }

        [Test]
        public void TheSamePlanEveryTimeItIsAsked()
        {
            ClassCyclePlanner planner = Planner();
            for (int wave = 1; wave <= 8; wave++)
            {
                WavePlan a = planner.PlanFor(wave);
                WavePlan b = planner.PlanFor(wave);
                Assert.AreEqual(a.Groups.Length, b.Groups.Length);
                for (int i = 0; i < a.Groups.Length; i++)
                {
                    Assert.AreEqual(a.Groups[i].ToString(), b.Groups[i].ToString());
                    Assert.AreNotSame(a.Groups[i], b.Groups[i], "a fresh group each time: the caller may keep it");
                }
            }
        }

        [Test]
        public void ItIsKnownByNameAndPricedWhenACostTableIsGiven()
        {
            CollectionAssert.Contains(Registry.PlannerNames, ClassCyclePlanner.Id);
            IWavePlanner made = Registry.CreatePlanner(ClassCyclePlanner.Id, new SimConfig(), TestSims.SampleSceneMap());
            Assert.IsInstanceOf<ClassCyclePlanner>(made);
            Assert.AreEqual("class-cycle", made.Name);

            Assert.AreEqual(0f, Planner().PlanFor(4).ThreatSpent, "no cost table -> 0");
            var priced = new ClassCyclePlanner(new SimConfig(), TestSims.SampleSceneMap(), new FakePricer());
            Assert.AreEqual(113f, priced.PlanFor(4).ThreatSpent, "FakePricer prices a plan at its agent count");
        }

        // The reason the planner exists: a recorded game with every class in it,
        // which replays exactly.
        [Test]
        public void AnEpisodeOnItHasEveryClassAndReplaysOnEveryTick()
        {
            EpisodeResult run = EpisodeRunner.Run(TestEpisodes.Small(seed: 2, waves: 5, planner: ClassCyclePlanner.Id));
            Assert.AreEqual(5, run.WavesResolved);
            Assert.AreEqual(ClassCyclePlanner.Id, run.Replay.Planner);

            WaveOutcome ground = run.Outcomes[0], sappers = run.Outcomes[1], flyers = run.Outcomes[2], mixed = run.Outcomes[3];
            CollectionAssert.AreEqual(new[] { 12, 0, 0 }, ground.SpawnedByClass);
            CollectionAssert.AreEqual(new[] { 0, 7, 0 }, sappers.SpawnedByClass, "half of 14");
            CollectionAssert.AreEqual(new[] { 0, 0, 8 }, flyers.SpawnedByClass, "half of 16");
            CollectionAssert.AreEqual(new[] { 9, 4, 4 }, mixed.SpawnedByClass, "18: a half and two quarters");

            foreach (WaveOutcome o in run.Outcomes)
            {
                Assert.IsFalse(o.TimedOut, "wave " + o.WaveIndex);
                Assert.AreEqual(o.Spawned, o.Killed + o.Leaked, "wave " + o.WaveIndex + ": every enemy was killed or leaked");
                for (int c = 0; c < MovementClasses.Count; c++)
                    Assert.AreEqual(o.SpawnedByClass[c], o.KilledByClass[c] + o.LeakedByClass[c], "wave " + o.WaveIndex + ", class " + (MovementClass)c);
            }

            ReplayFile file = TestEpisodes.Reload(run.Replay);
            ReplayReport report = ReplayRunner.Verify(file, perTick: true);
            Assert.IsTrue(report.Ok, report.Describe());
            Assert.AreEqual(5, report.WavesVerified);
            TestContext.WriteLine("class-cycle, 5 waves: " + string.Join(" | ", run.Outcomes));
        }
    }
}
