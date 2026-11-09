namespace Thesis.Learning
{
    // What the estimator is told about the decision (ARCHITECTURE.md §5.6): the bin
    // of the one binned feature, and the two coordinates the kernel works in. Which
    // features those are is spec gap S1, closed in WP10.
    public readonly struct DecisionContext
    {
        public readonly int Bin;
        public readonly float KernelX;
        public readonly float KernelY;

        public DecisionContext(int bin, float kernelX, float kernelY)
        {
            Bin = bin;
            KernelX = kernelX;
            KernelY = kernelY;
        }
    }
}
