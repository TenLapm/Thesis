using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests
{
    // A small, fast episode for replay and harness tests: a 12x6 open map and a
    // dozen agents per wave, so three waves are a few thousand ticks on a 72-tile
    // grid instead of fifteen thousand on SampleScene's 1,444.
    public static class TestEpisodes
    {
        public const string SmallMap = @"
            . . . . . . . . . . . .
            . S . . . . . . . . . .
            . . . . . . . . . . . .
            . . . . . . . . . . . .
            . . . . . . . . . . C .
            . . . . . . . . . . . .";

        // A core that cannot die in a short test, so runs end on the wave limit.
        public static SimConfig SmallConfig()
        {
            return new SimConfig { AgentsPerWave = 12, AgentsPerWaveIncrement = 2, PrepSeconds = 2f, IntermissionSeconds = 1f, CoreMaxHp = 1000 };
        }

        public static EpisodeOptions Small(ulong seed = 1, int waves = 3, bool tickHashes = true, IPlayerPolicy policy = null)
        {
            return new EpisodeOptions
            {
                Config = SmallConfig(),
                Map = AsciiMap.Parse(SmallMap, "small").Map,
                Shapes = TestShapes.SampleSceneLibrary(),
                Seed = seed,
                Policy = policy ?? new GreedyDetourPolicy(),
                MaxWaves = waves,
                RecordTickHashes = tickHashes,
                Build = "test",
                Session = "test",
            };
        }

        public static ReplayFile SmallReplay(ulong seed = 1, int waves = 3, bool tickHashes = true)
        {
            return EpisodeRunner.Run(Small(seed, waves, tickHashes)).Replay;
        }

        // Through JSON and back, the way a file on disk is read. Also gives each
        // test its own copy to tamper with.
        public static ReplayFile Reload(ReplayFile file) { return ReplayFile.FromJson(file.ToJson()); }
    }
}
