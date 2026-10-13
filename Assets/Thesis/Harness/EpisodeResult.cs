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

        // Every tower that was ever bought, in the order they were bought: what it
        // cost and what it did. For the balance check (`thesis balance`).
        public List<TowerSummary> Towers = new List<TowerSummary>();
    }

    public sealed class TowerSummary
    {
        public string Id;
        public int PricePaid;
        public float DamageDealt;
        public int Shots;
        public int Kills;

        // From the tick it was placed to the end of the episode. A tower that was sold
        // or chewed through is counted to the end all the same: the budget stayed spent.
        public int TicksOnBoard;
        public bool Sold;
        public bool Destroyed;
    }
}
