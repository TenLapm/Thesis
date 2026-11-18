namespace Thesis.Learning
{
    // A snapshot of an estimator, saved as JSON (CLAUDE.md §7: JSON, never raw bytes).
    // It exists to seed a participant from an offline run and for the transfer
    // condition (CLAUDE.md §5). The cells are flattened row by row: index
    // bin * Strategies + strategy. Doubles survive the JSON round trip exactly
    // (Newtonsoft writes the shortest text that reads back to the same bits).
    public sealed class EstimatorState
    {
        public const int CurrentSchema = 1;

        public int Schema = CurrentSchema;
        public string Kind;
        public int Bins;
        public int Strategies;
        public double Discount;
        public double[] Alpha;
        public double[] Beta;

        // How many updates each cell has had, not discounted. 0 means Cold.
        public int[] Updates;

        // KernelPriorEstimator only (WP11, S2); null and 0 for a plain BinnedPosterior.
        public KernelState Kernel;
        public int ThinBelow;
        public double PriorWeight;
        public double NearWeight;
    }
}
