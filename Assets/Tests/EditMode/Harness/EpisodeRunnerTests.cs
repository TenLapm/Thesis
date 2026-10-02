using System;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Harness
{
    public class EpisodeRunnerTests
    {
        // WORKPLAN WP5 "must pass": a headless 25-wave run is recorded, then replayed,
        // with every wave hash matching.
        //
        // No scripted player survives 25 waves of the real settings, so the core is
        // given enough HP to keep the run going: the point here is 25 waves of the
        // real rules - spawning, towers firing, kills, leaks, digging, breaches,
        // building between waves - not the player's skill.
        [Test]
        public void ATwentyFiveWaveRunReplaysWithEveryWaveHashMatching()
        {
            var options = new EpisodeOptions
            {
                Config = new SimConfig { CoreMaxHp = 100000 },
                Map = TestSims.SampleSceneMap(),
                Shapes = TestShapes.SampleSceneLibrary(),
                Seed = 1,
                // Walls, then towers. A smaller search than the default keeps this test in seconds.
                Policy = SequencePolicy.Mixed(wallsPerPhase: 2, towersPerPhase: 3, wallSearchOrigins: 12),
                MaxWaves = 25,
            };

            EpisodeResult run = EpisodeRunner.Run(options);
            Assert.AreEqual(25, run.WavesResolved);
            Assert.IsFalse(run.GameOver);
            Assert.IsFalse(run.HitTickLimit);
            Assert.AreEqual(25, run.Replay.WaveHashes.Count);

            ReplayFile file = TestEpisodes.Reload(run.Replay);
            ReplayReport report = ReplayRunner.Verify(file, perTick: false);
            Assert.IsTrue(report.Ok, report.Describe());
            Assert.AreEqual(25, report.WavesVerified);

            // The run must actually have exercised the rules, or this proves little.
            int killed = 0, leaked = 0, breaches = 0, towersLost = 0;
            float damage = 0f;
            foreach (WaveOutcome o in run.Outcomes)
            {
                killed += o.Killed;
                leaked += o.Leaked;
                breaches += o.WallsBreached;
                towersLost += o.TowersDestroyed;
                damage += o.TotalDamage();
                // WORKPLAN WP-C1: "in a normal 25-wave run TimedOut is never set."
                Assert.IsFalse(o.TimedOut, "wave " + o.WaveIndex + " hit the MaxWaveSeconds backstop");
                Assert.AreEqual(0, o.Removed);
                Assert.AreEqual(o.Spawned, o.Killed + o.Leaked, "wave " + o.WaveIndex + ": every enemy was either killed or leaked");
            }
            Assert.Greater(run.TowersPlaced, 20, "towers placed");
            Assert.Greater(run.Placements, run.TowersPlaced, "and walls too");
            Assert.Greater(killed, 0, "kills");
            Assert.Greater(leaked, 0, "leaks");
            Assert.Greater(damage, 0f, "damage");
            TestContext.WriteLine("25 waves: ticks=" + run.Ticks + " commands=" + file.Commands.Count + " placements=" + run.Placements + " (towers " + run.TowersPlaced + ")"
                                  + " killed=" + killed + " leaked=" + leaked + " breaches=" + breaches + " towersLost=" + towersLost + " damage=" + damage + " final=" + file.FinalHash);
        }

        // WORKPLAN WP-C1 "done when": a headless run with towers survives clearly longer
        // than the same map with no towers.
        [Test]
        public void TowersLetAPlayerSurviveClearlyLongerThanNoTowers()
        {
            Func<IPlayerPolicy, EpisodeResult> play = policy => EpisodeRunner.Run(new EpisodeOptions
            {
                Map = TestSims.SampleSceneMap(),
                Shapes = TestShapes.SampleSceneLibrary(),
                Seed = 1,
                Policy = policy,
                MaxWaves = 25,
            });

            EpisodeResult idle = play(new IdlePolicy());
            EpisodeResult wallsOnly = play(new GreedyDetourPolicy { MaxPlacementsPerIntermission = 4, MaxOriginsPerPlacement = 12 });
            EpisodeResult towers = play(new SentryPolicy());

            Assert.IsTrue(idle.GameOver);
            Assert.AreEqual(1, idle.WavesResolved, "no defence: lost in wave 1");
            Assert.AreEqual(0, idle.Outcomes[0].Killed);

            // Walls alone no longer win anything: there is no clock to run out.
            Assert.AreEqual(1, wallsOnly.WavesResolved, "walls with nothing to shoot: also lost in wave 1");
            Assert.AreEqual(0, wallsOnly.Outcomes[0].Killed);

            Assert.GreaterOrEqual(towers.WavesResolved, 5, "towers: at least five waves");
            Assert.AreEqual(towers.Outcomes[0].Spawned, towers.Outcomes[0].Killed, "and wave 1 is wiped out");
            Assert.AreEqual(0, towers.Outcomes[0].Leaked);
            TestContext.WriteLine("idle: " + idle.WavesResolved + " wave(s); walls only: " + wallsOnly.WavesResolved + " wave(s); towers: " + towers.WavesResolved
                                  + " waves with " + towers.TowersPlaced + " towers");
        }

        [Test]
        public void TheSameSeedGivesTheSameEpisodeAndADifferentSeedDoesNot()
        {
            ReplayFile a = TestEpisodes.SmallReplay(seed: 3);
            ReplayFile b = TestEpisodes.SmallReplay(seed: 3);
            ReplayFile c = TestEpisodes.SmallReplay(seed: 4);

            Assert.AreEqual(a.ToJson(), b.ToJson(), "two runs of one seed write the same file, byte for byte");
            Assert.AreNotEqual(a.FinalHash, c.FinalHash);
        }

        [Test]
        public void AnIdlePlayerLosesTheFirstWaveOnSampleScene()
        {
            var options = new EpisodeOptions
            {
                Map = TestSims.SampleSceneMap(),
                Shapes = TestShapes.SampleSceneLibrary(),
                Policy = new IdlePolicy(),
            };

            EpisodeResult run = EpisodeRunner.Run(options);
            Assert.IsTrue(run.GameOver);
            Assert.AreEqual(1, run.WavesResolved);
            Assert.AreEqual(0, run.CoreHp);
            Assert.IsTrue(run.Outcomes[0].CoreDestroyed);
            Assert.AreEqual(0, run.Placements);
            Assert.AreEqual(IdlePolicy.Id, run.Replay.Policy);

            // Only StartWaveNow was sent, and the recording stops on the game-over tick.
            Assert.AreEqual(1, run.Replay.Commands.Count);
            Assert.AreEqual(run.Ticks, run.Replay.FinalTick);
            Assert.IsTrue(ReplayRunner.Verify(run.Replay, perTick: false).Ok);
        }

        [Test]
        public void ThePolicyBuildsOnceInEveryBuildPhase()
        {
            var policy = new CountingPolicy();
            EpisodeResult run = EpisodeRunner.Run(TestEpisodes.Small(waves: 4, policy: policy));

            Assert.AreEqual(4, run.WavesResolved);
            Assert.AreEqual(4, policy.Calls, "prep plus three intermissions; the fourth wave ends the episode");
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, policy.WaveIndexSeen);
        }

        [Test]
        public void WithoutEarlyStartTheCountdownRunsOut()
        {
            EpisodeOptions early = TestEpisodes.Small(waves: 1, policy: new IdlePolicy());
            EpisodeOptions waiting = TestEpisodes.Small(waves: 1, policy: new IdlePolicy());
            waiting.StartWavesEarly = false;

            EpisodeResult a = EpisodeRunner.Run(early);
            EpisodeResult b = EpisodeRunner.Run(waiting);

            int prepTicks = early.Config.Ticks(early.Config.PrepSeconds);
            Assert.AreEqual(0, a.Outcomes[0].TickStarted);
            Assert.AreEqual(prepTicks - 1, b.Outcomes[0].TickStarted, "the wave begins on the tick the countdown reaches zero");
            Assert.AreEqual(0, b.Replay.Commands.Count);
            Assert.AreEqual(a.Ticks + prepTicks - 1, b.Ticks, "the same wave, only later");
        }

        [Test]
        public void TheTickLimitStopsARunawayEpisode()
        {
            EpisodeOptions options = TestEpisodes.Small(waves: 99);
            options.MaxTicks = 500;

            EpisodeResult run = EpisodeRunner.Run(options);
            Assert.IsTrue(run.HitTickLimit);
            Assert.AreEqual(500, run.Ticks);
            Assert.IsTrue(ReplayRunner.Verify(run.Replay, perTick: true).Ok, "a cut-off run still replays");
        }

        [Test]
        public void MissingOptionsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => EpisodeRunner.Run(null));
            Assert.Throws<ArgumentException>(() => EpisodeRunner.Run(new EpisodeOptions()));
        }

        private sealed class CountingPolicy : IPlayerPolicy
        {
            public int Calls;
            public readonly System.Collections.Generic.List<int> WaveIndexSeen = new System.Collections.Generic.List<int>();

            public string Name => "counting";

            public void OnIntermission(Simulation sim, Action<SimCommand> send, IRandom rng)
            {
                Calls++;
                WaveIndexSeen.Add(sim.State.WaveIndex);
            }
        }
    }
}
