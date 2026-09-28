using System;

namespace Thesis.Sim
{
    // How many agent-ticks were spent standing on each tile during the current
    // wave. Feeds `funnel_reliance` / `chokepoint_reliance` later (WP8) and the
    // "route" ASCII layer; AgentSystem writes it, nothing here computes anything
    // yet.
    public sealed class OccupancyMap
    {
        private readonly int[] counts;

        public OccupancyMap(int nodeCount)
        {
            if (nodeCount < 0) throw new ArgumentOutOfRangeException(nameof(nodeCount), nodeCount, "Must not be negative.");
            counts = new int[nodeCount];
        }

        public int NodeCount => counts.Length;

        public int this[SimNode node] => counts[node.Index];

        public int this[int index] => counts[index];

        public void Increment(SimNode node) { counts[node.Index]++; }

        // Called at the start of every wave (ARCHITECTURE.md §4.2 step 2:
        // "Occupancy.Reset()"), so occupancy is a per-wave statistic, not
        // cumulative across the whole run.
        public void Reset() { Array.Clear(counts, 0, counts.Length); }

        public int Total()
        {
            int sum = 0;
            for (int i = 0; i < counts.Length; i++) sum += counts[i];
            return sum;
        }

        internal OccupancyMap Clone()
        {
            var c = new OccupancyMap(counts.Length);
            Array.Copy(counts, c.counts, counts.Length);
            return c;
        }

        internal void AddToHash(ref Thesis.Core.Fnv1a64 h)
        {
            h.Add(counts.Length);
            for (int i = 0; i < counts.Length; i++) h.Add(counts[i]);
        }
    }
}
