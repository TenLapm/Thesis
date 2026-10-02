namespace Thesis.Sim
{
    // Sizes and helpers for per-movement-class arrays. Kept next to the
    // MovementClass enum: adding a value there means raising Count here.
    public static class MovementClasses
    {
        // Length of every per-class array (WaveOutcome.SpawnedByClass and friends).
        public const int Count = 3;

        public static bool IsDefined(MovementClass movement)
        {
            return movement >= MovementClass.Ground && (int)movement < Count;
        }
    }
}
