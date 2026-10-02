namespace Thesis.Sim
{
    // Sizes and helpers for per-damage-type arrays. Kept next to the DamageType enum:
    // adding a value there means raising Count here.
    public static class DamageTypes
    {
        // Length of every per-damage-type array (Resist, DamageByType).
        public const int Count = 3;

        public static float[] AllOnes()
        {
            var r = new float[Count];
            for (int i = 0; i < Count; i++) r[i] = 1f;
            return r;
        }
    }
}
