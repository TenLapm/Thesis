using System;
using Thesis.Core;

namespace Thesis.Sim
{
    // How much damage enemies took while standing on each tile, during the current
    // wave. The tile is the one under the ENEMY when it was hit, not the tower's.
    // It is the input of the chokepoint_reliance feature (damage concentrated on a
    // few route tiles = one kill zone), and it is reset at the start of every wave,
    // like OccupancyMap.
    //
    // Like every damage figure in the simulation it counts effective damage only.
    public sealed class DamageMap
    {
        private readonly float[] values;

        public DamageMap(int nodeCount)
        {
            if (nodeCount < 0) throw new ArgumentOutOfRangeException(nameof(nodeCount), nodeCount, "Must not be negative.");
            values = new float[nodeCount];
        }

        public int NodeCount => values.Length;

        public float this[SimNode node] => values[node.Index];

        public float this[int index] => values[index];

        public void Add(SimNode node, float damage) { values[node.Index] = (float)(values[node.Index] + damage); }

        public void Reset() { Array.Clear(values, 0, values.Length); }

        // Summed in index order, so the result is the same on every runtime.
        public float Total()
        {
            float sum = 0f;
            for (int i = 0; i < values.Length; i++) sum = (float)(sum + values[i]);
            return sum;
        }

        internal DamageMap Clone()
        {
            var c = new DamageMap(values.Length);
            Array.Copy(values, c.values, values.Length);
            return c;
        }

        internal void AddToHash(ref Fnv1a64 h)
        {
            h.Add(values.Length);
            for (int i = 0; i < values.Length; i++) h.Add(values[i]);
        }
    }
}
