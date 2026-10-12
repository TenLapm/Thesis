using System.Collections.Generic;
using Thesis.Director;
using Thesis.Sim;

namespace Thesis.Harness
{
    // How one headless episode ended, plus its replay and its telemetry. See EpisodeRunner.
    public sealed class EpisodeResult
    {
        public ReplayFile Replay;

        // One row per wave, and row 0 for the build phase before the first: what a
        // TelemetryWriter would have put in the session's .jsonl file.
        public List<WaveRecord> Telemetry = new List<WaveRecord>();
        public List<WaveOutcome> Outcomes = new List<WaveOutcome>();
        public int WavesResolved;
        public bool GameOver;
        public bool HitTickLimit;
        public int Ticks;
        public int CoreHp;
        public float BuildBudget;
        public int Placements;      // rows of the build log: wall pieces and towers built, and anything sold
        public int TowersPlaced;
    }
}
