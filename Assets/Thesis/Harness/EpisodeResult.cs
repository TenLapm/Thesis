using System.Collections.Generic;
using Thesis.Sim;

namespace Thesis.Harness
{
    // How one headless episode ended, plus its replay. See EpisodeRunner.
    public sealed class EpisodeResult
    {
        public ReplayFile Replay;
        public List<WaveOutcome> Outcomes = new List<WaveOutcome>();
        public int WavesResolved;
        public bool GameOver;
        public bool HitTickLimit;
        public int Ticks;
        public int CoreHp;
        public float BuildBudget;
        public int Placements;      // wall pieces and towers together
        public int TowersPlaced;
    }
}
