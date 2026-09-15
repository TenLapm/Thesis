namespace Thesis.Core
{
    // Every random draw in Thesis.* goes through this interface, handed in from a
    // named stream (RngStreams). Never System.Random, never UnityEngine.Random:
    // neither can be saved into a replay, and UnityEngine.Random is shared global
    // state that any unrelated script can advance.
    public interface IRandom
    {
        uint NextUInt();

        // Uniform in [0, maxExclusive). Unbiased (rejection sampling, not modulo).
        int NextInt(int maxExclusive);

        // Uniform in [minInclusive, maxExclusive).
        int NextInt(int minInclusive, int maxExclusive);

        // Uniform in [0, 1). 24 bits of precision, so every value is exact in a float.
        float NextFloat01();

        // Uniform in [0, 1). 53 bits of precision.
        double NextDouble();
    }
}
