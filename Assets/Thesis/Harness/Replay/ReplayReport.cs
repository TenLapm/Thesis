using System.Text;

namespace Thesis.Harness
{
    // What ReplayRunner.Verify found. Ok means every recorded hash that was checked
    // came out the same; anything else names where the two runs first part.
    public sealed class ReplayReport
    {
        public bool Ok = true;

        // One sentence on what went wrong; null when Ok.
        public string Problem;

        public int TicksRun;
        public int WavesVerified;

        // The first wave whose recorded hash did not match, or -1.
        public int FirstDivergentWave = -1;

        // The first tick that RAN differently, or -1 (unknown, or no divergence).
        // The two states agree at the start of this tick and differ once it has
        // run, so the state to look at is the one at State.Tick == this + 1.
        // Only known when the file carries per-tick hashes and they were checked.
        public int FirstDivergentTick = -1;

        public bool TickHashesInFile;
        public bool TickHashesChecked;

        internal void Fail(string problem)
        {
            if (!Ok) return; // keep the first problem: later ones are consequences
            Ok = false;
            Problem = problem;
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            if (Ok)
            {
                sb.Append("[Replay] OK: ").Append(TicksRun).Append(" ticks, ").Append(WavesVerified).Append(" wave hashes");
                if (TickHashesChecked) sb.Append(", every tick hash");
                sb.Append(" and the final state all match.");
                return sb.ToString();
            }

            sb.Append("[Replay] DIVERGED: ").Append(Problem).Append('\n');
            sb.Append("  waves that matched before it: ").Append(WavesVerified).Append('\n');
            if (FirstDivergentWave >= 0) sb.Append("  first divergent wave: ").Append(FirstDivergentWave).Append('\n');

            if (FirstDivergentTick >= 0)
            {
                sb.Append("  first divergent tick: ").Append(FirstDivergentTick)
                  .Append("  (the runs agree until this tick starts and differ once it has run; --dump-tick ")
                  .Append(FirstDivergentTick + 1).Append(" writes the state right after it)");
            }
            else if (TickHashesChecked)
            {
                sb.Append("  every recorded tick hash matched, so the difference is in the wave bookkeeping, not in a tick's state.");
            }
            else if (TickHashesInFile)
            {
                sb.Append("  run again with --per-tick to find the exact tick.");
            }
            else
            {
                sb.Append("  this file has no per-tick hashes, so the exact tick cannot be found from it. Record again with tick hashes on.");
            }
            return sb.ToString();
        }
    }
}
