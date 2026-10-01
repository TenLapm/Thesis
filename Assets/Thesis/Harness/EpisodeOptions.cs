using Thesis.Sim;

namespace Thesis.Harness
{
    // What one headless episode is built from. See EpisodeRunner.
    public sealed class EpisodeOptions
    {
        public SimConfig Config = new SimConfig();
        public MapData Map;
        public ShapeDef[] Shapes;
        public ulong Seed = 1;
        public string Planner = EscalationPlanner.Id;
        public IPlayerPolicy Policy = new IdlePolicy();

        // The episode ends when this many waves have resolved, when the core dies,
        // or at MaxTicks - whichever comes first.
        public int MaxWaves = 25;

        // A backstop against a run that never ends (two hours of game time).
        public int MaxTicks = 360000;

        // Send StartWaveNow as soon as the policy has finished building. Nothing
        // moves during a build phase, so waiting out the countdown only burns ticks
        // (45 s of prep and 10 s per intermission is about 15,000 empty ticks over 25
        // waves). The command is recorded like any other, so the replay is exact.
        public bool StartWavesEarly = true;

        // One state hash per tick in the replay. Needed to pin a divergence to a
        // tick; costs one hash per tick, so it is off unless asked for.
        public bool RecordTickHashes;

        public string Build = "headless";
        public string Session;
    }
}
