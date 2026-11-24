using System;
using System.IO;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Learning;
using Thesis.Sim;

namespace Thesis.Tests.Director
{
    // WP14: what the full director learned in one session can be saved, and another session's
    // director can start from it (the transfer condition, §5 of CLAUDE.md).
    public class DirectorSnapshotTests
    {
        private static SimConfig GameConfig() { return Json.Deserialize<SimConfig>(File.ReadAllText(Path.Combine(TestPaths.Maps, "SimConfig.json"))); }

        private static WaveDirector Played(int waves)
        {
            SimConfig config = GameConfig();
            MapData map = GameMaps.Generate(9);
            var d = (WaveDirector)PlannerFactory.Create(WaveDirector.FullId, config, map, 9UL);
            var sim = new Simulation(config, map, TestSims.GameShapes(), TestSims.GameTowers(), 9UL, d);
            var policy = new BuilderPolicy();
            var rng = new Pcg32(9UL, RngStreams.Policy);
            Action<SimCommand> send = c => { sim.Enqueue(c); sim.FlushInput(); };
            bool built = false;
            for (int t = 0; t < 400000 && !sim.State.IsGameOver && sim.State.WaveIndex < waves; t++)
            {
                SimState s = sim.State;
                bool phase = s.Phase == SimPhase.Prep || s.Phase == SimPhase.Intermission;
                if (phase && !built && s.NextPlan != null)
                {
                    policy.OnIntermission(sim, send, rng);
                    if (sim.State.NextPlan == null) continue;
                    built = true;
                    send(SimCommand.StartWaveNow());
                }
                else if (!phase) built = false;
                sim.Tick();
            }
            return d;
        }

        [Test]
        public void ADirectorStartsFromTheSnapshotAnotherOneSaved()
        {
            WaveDirector first = Played(5);
            EstimatorState learned = first.ExportEstimator();
            Assert.IsNotNull(learned);
            string fresh = Json.Serialize(DirectorConfig.Provisional().NewEstimator().Export());
            Assert.AreNotEqual(fresh, Json.Serialize(learned), "five waves taught it something");

            // Through a file's text, as the game saves and loads it.
            string snapshot = DirectorSetup.WriteSnapshot(learned);
            SimConfig config = GameConfig();
            MapData map = GameMaps.Generate(10);
            string setup = DirectorSetup.FromSnapshot(snapshot, DirectorConfig.Provisional());
            var second = (WaveDirector)PlannerFactory.Create(WaveDirector.FullId, config, map, 10UL, setup: setup);

            Assert.AreEqual(Json.Serialize(learned), Json.Serialize(second.ExportEstimator()), "the second director starts where the first stopped");
            Assert.AreEqual(Json.Serialize(learned), Json.Serialize(DirectorSetup.Read(second.PlannerSetup).Estimator), "and its replay says so");
        }

        [Test]
        public void TheHeuristicDirectorHasNothingToSave()
        {
            SimConfig config = GameConfig();
            MapData map = GameMaps.Generate(9);
            var d = (WaveDirector)PlannerFactory.Create(WaveDirector.HeuristicId, config, map, 9UL);
            Assert.IsNull(d.ExportEstimator());
        }

        [TestCase("")] [TestCase("{}")] [TestCase("not json")]
        public void ABrokenSnapshotIsRefused(string text)
        {
            Assert.Throws<ArgumentException>(() => DirectorSetup.FromSnapshot(text, DirectorConfig.Provisional()));
        }
    }
}
