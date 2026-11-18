using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Harness
{
    // WP10, the replay schema after WP-C3's: a replay stores every plan its planner handed the
    // simulation, so a session plays again without the planner's code (RecordedPlanner), and
    // `replay --rerun-director` can check that the planner, rebuilt, still makes the same plans.
    public class RecordedPlanTests
    {
        private static SimConfig GameConfig() { return Json.Deserialize<SimConfig>(File.ReadAllText(Path.Combine(TestPaths.Maps, "SimConfig.json"))); }

        private static ReplayFile Session(string planner, ulong seed, int waves)
        {
            var o = new EpisodeOptions
            {
                Config = GameConfig(), Map = GameMaps.Generate(seed), Shapes = TestSims.GameShapes(), Towers = TestSims.GameTowers(),
                Seed = seed, Planner = planner, Policy = new BuilderPolicy(), MaxWaves = waves,
            };
            return EpisodeRunner.Run(o).Replay;
        }

        // Through JSON, as a file on disk would be.
        private static ReplayFile Reloaded(ReplayFile file) { return ReplayFile.FromJson(file.ToJson()); }

        [Test]
        public void AReplayStoresEveryPlanItsPlannerMadeInOrder()
        {
            ReplayFile file = Session(WaveDirector.HeuristicId, 3, 4);
            Assert.AreEqual(ReplayFile.CurrentSchema, file.Schema);
            Assert.IsNotNull(file.Plans);
            Assert.GreaterOrEqual(file.Plans.Count, 4, "one plan a wave at least");
            for (int i = 1; i < file.Plans.Count; i++) Assert.GreaterOrEqual(file.Plans[i].Wave, file.Plans[i - 1].Wave);
            foreach (RecordedPlan p in file.Plans) Assert.AreEqual(p.Wave, p.Plan.WaveIndex);
        }

        [Test]
        public void ASessionReplaysExactlyFromItsRecordedPlansWithThePlannerGone()
        {
            // The must-pass: a session with the random planner, replayed with that planner unknown to
            // the factory. The simulation must not need it.
            ReplayFile file = Reloaded(Session(RandomPlanner.Id, 5, 5));
            file.Planner = "a-planner-that-no-longer-exists";
            Assert.Throws<ArgumentException>(() => PlannerFactory.Create(file.Planner, file.Config, file.MapData, file.RngSeed));

            ReplayReport report = ReplayRunner.Verify(file, false);
            Assert.IsTrue(report.Ok, report.Describe());
        }

        [Test]
        public void AReplayOfTheSchemaBeforeStillLoadsAndRebuildsItsPlannerByName()
        {
            // A file of schema 15 (recorded before plans were stored): the same session, as it would
            // have been written then. It loads, and its planner is rebuilt by name.
            ReplayFile file = Session(WaveDirector.HeuristicId, 3, 3);
            file.Schema = ReplayFile.SchemaBeforePlans;
            file.Plans = null;
            ReplayFile old = ReplayFile.FromJson(file.ToJson());
            Assert.AreEqual(ReplayFile.SchemaBeforePlans, old.Schema);
            Assert.IsNull(old.Plans);
            Assert.IsTrue(ReplayRunner.Verify(old, false).Ok);
        }

        [Test]
        public void ThePinnedFullDirectorEpisodeIsReMadeByItsRebuiltDirectorOnEitherRuntime([Values("dotnet", "mono")] string runtime)
        {
            // Recorded under .NET and under Mono: the director rebuilt here, on this runtime, must make
            // the plans the other runtime recorded.
            string path = Path.Combine(TestPaths.ProjectRoot, "Results", "pinned-replays", runtime + "-full-director-8waves.replay.json");
            ReplayFile file = ReplayFile.Load(path);
            Assert.AreEqual(WaveDirector.FullId, file.Planner);
            Assert.IsNotNull(file.PlannerSetup);
            PlanComparison c = ReplayRunner.CompareRebuiltPlanner(file);
            Assert.IsTrue(c.Ok, c.ToString());
        }

        [Test]
        public void RerunningTheDirectorReproducesEveryRecordedPlan()
        {
            ReplayFile file = Reloaded(Session(WaveDirector.HeuristicId, 4, 5));
            PlanComparison c = ReplayRunner.CompareRebuiltPlanner(file);
            Assert.IsTrue(c.Ok, c.ToString());
            Assert.AreEqual(file.Plans.Count, c.PlansCompared);
        }

        [Test]
        public void RerunningTheDirectorNamesTheFirstPlanThatDiffers()
        {
            ReplayFile file = Reloaded(Session(WaveDirector.HeuristicId, 4, 5));
            file.Plans[2].Plan.Groups[0].Count += 1; // as if the director's code had changed
            PlanComparison c = ReplayRunner.CompareRebuiltPlanner(file);
            Assert.IsFalse(c.Ok);
            Assert.AreEqual(2, c.FirstDifferentPlan);
            StringAssert.Contains("wave " + file.Plans[2].Wave, c.ToString());
        }

        [Test]
        public void AReplayWithoutEnoughPlansFailsClearly()
        {
            ReplayFile file = Reloaded(Session(RandomPlanner.Id, 6, 4));
            file.Plans.RemoveRange(1, file.Plans.Count - 1);
            ReplayReport report = ReplayRunner.Verify(file, false);
            Assert.IsFalse(report.Ok);
            StringAssert.Contains("recorded plan", report.Describe());
        }

        [Test]
        public void TheTelemetryOfADirectorSessionIsStillMadeAgainByteForByte()
        {
            // Telemetry carries the director's reasons (profile, scores), which only the planner knows,
            // so it is made with the planner rebuilt by name; the plans it sends must then match.
            var o = new EpisodeOptions
            {
                Config = GameConfig(), Map = GameMaps.Generate(4), Shapes = TestSims.GameShapes(), Towers = TestSims.GameTowers(),
                Seed = 4, Planner = WaveDirector.HeuristicId, Policy = new BuilderPolicy(), MaxWaves = 4,
            };
            EpisodeResult r = EpisodeRunner.Run(o);
            List<WaveRecord> again = ReplayRunner.Telemetry(Reloaded(r.Replay));
            Assert.AreEqual(r.Telemetry.Count, again.Count);
            for (int i = 0; i < again.Count; i++) Assert.AreEqual(r.Telemetry[i].ToJsonLine(), again[i].ToJsonLine(), "row " + i);
        }
    }
}
