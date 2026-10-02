using System;
using System.Text;
using NUnit.Framework;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class SampleSceneRunTests
    {
        // WP3's "done when": the static baseline on the real map, with a player who
        // never builds, must end in game over at a plausible wave. Nothing shoots at
        // the enemies, so all of wave 1 reaches the core and 10 leaks end the run.
        [Test]
        public void AnIdlePlayerLosesTheGameDuringWaveOne()
        {
            var sim = TestSims.SampleScene(seed: 1);
            int guard = 200000;
            while (!sim.State.IsGameOver && guard-- > 0) sim.Tick();

            Assert.IsTrue(sim.State.IsGameOver, "an idle player must lose");
            Assert.AreEqual(1, sim.State.WaveIndex);
            WaveOutcome o = sim.State.LastOutcome;
            Assert.IsTrue(o.CoreDestroyed);
            Assert.GreaterOrEqual(o.Leaked, 10);
            Assert.AreEqual(0, o.Killed, "nothing is killed on a board with no towers");
            Assert.LessOrEqual(sim.State.CoreHp, 0);

            float seconds = sim.State.Tick * sim.Config.TickSeconds;
            TestContext.WriteLine("idle game over at tick " + sim.State.Tick + " (" + seconds.ToString("0.0") + " s): " + o);
            // 45 s prep + ~72 s straight-line travel; the first 10 of 100 agents arrive 0.2 s apart.
            Assert.That(seconds, Is.InRange(45f + 60f, 45f + 90f), "time of death is plausible");
        }

        // WP3 review probe: the same 25 waves at tick 0.02 s and 0.01 s. Counts should
        // be close but not identical; wildly different counts would mean some rule
        // depends on the number of ticks instead of elapsed time. Explicit: slow, and
        // its output is read by a person, not asserted.
        [Test, Explicit("Review probe; run on demand")]
        public void ProbeTickLengthSensitivityOnBenchChoke()
        {
            var sb = new StringBuilder();
            sb.AppendLine("wave | dt=0.02 killed/leaked  first-exit s | dt=0.01 killed/leaked  first-exit s | Δ first-exit s");
            WaveOutcome[] coarse = RunChoke(0.02f, 25, out float[] coarseExit);
            WaveOutcome[] fine = RunChoke(0.01f, 25, out float[] fineExit);
            int maxCountGap = 0;
            float maxExitGap = 0f;
            for (int w = 0; w < 25; w++)
            {
                float gap = Math.Abs(coarseExit[w] - fineExit[w]);
                sb.AppendLine((w + 1).ToString().PadLeft(4) + " | "
                              + (coarse[w].Killed + "/" + coarse[w].Leaked).PadLeft(14) + coarseExit[w].ToString("0.00").PadLeft(14) + " | "
                              + (fine[w].Killed + "/" + fine[w].Leaked).PadLeft(14) + fineExit[w].ToString("0.00").PadLeft(14) + " | "
                              + gap.ToString("0.000").PadLeft(8));
                maxCountGap = Math.Max(maxCountGap, Math.Abs(coarse[w].Leaked - fine[w].Leaked));
                maxExitGap = Math.Max(maxExitGap, gap);
            }
            sb.AppendLine("max |leak count difference| = " + maxCountGap + "; max |first-exit time difference| = " + maxExitGap.ToString("0.000") + " s");
            TestContext.WriteLine(sb.ToString());
        }

        // firstExit[w] = seconds from wave w's start to its first kill or leak - a
        // continuous quantity, so it shows tick-length sensitivity that whole-wave
        // counts hide.
        private static WaveOutcome[] RunChoke(float dt, int waves, out float[] firstExit)
        {
            MapData map = MapData.Load(TestPaths.MapFile("Bench_Choke"));
            var config = new SimConfig { TickSeconds = dt, CoreMaxHp = 1000000 };
            var sim = new Simulation(config, map, TestShapes.SampleSceneLibrary(), TestTowers.Roster(), 1, new EscalationPlanner(config, map));
            BenchScenarios.Apply(sim.State.Grid, map, BenchScenario.ChokePoints);
            sim.RebuildFieldForSetup();

            var outcomes = new WaveOutcome[waves];
            firstExit = new float[waves];
            int seen = 0;
            bool exitSeen = false;
            while (seen < waves)
            {
                int tickBefore = sim.State.Tick;
                sim.Tick();
                foreach (SimEvent e in sim.LastTickEvents)
                {
                    if (!exitSeen && (e.Kind == SimEventKind.AgentKilled || e.Kind == SimEventKind.AgentLeaked))
                    {
                        exitSeen = true;
                        firstExit[seen] = (tickBefore - sim.State.CurrentOutcome.TickStarted) * dt;
                    }
                    if (e.Kind == SimEventKind.WaveResolved)
                    {
                        outcomes[seen++] = sim.State.LastOutcome;
                        exitSeen = false;
                    }
                }
            }
            return outcomes;
        }
    }
}
