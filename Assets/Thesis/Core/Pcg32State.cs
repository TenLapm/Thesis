namespace Thesis.Core
{
    // The complete state of a Pcg32. Plain fields so it serialises into replay and
    // estimator snapshots with no custom converter.
    public struct Pcg32State
    {
        public ulong State;
        public ulong Inc;
    }
}
