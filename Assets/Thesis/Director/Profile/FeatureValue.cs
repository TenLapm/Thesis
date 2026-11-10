namespace Thesis.Director
{
    // One feature's reading. A feature that has nothing to read (no towers yet, no damage
    // dealt last wave) says so instead of inventing a number: the value is the middle of the
    // range, 0.5, and HasData is false (the student, 2026-11-10). The director can then tell
    // "evenly mixed" from "nothing to go on".
    //
    // ARCHITECTURE.md §5.3 first wrote IBuildFeature.Compute as returning a bare float; the
    // flag is why it returns this instead.
    public readonly struct FeatureValue
    {
        public const float NoDataValue = 0.5f;

        public readonly float Value;
        public readonly bool HasData;

        private FeatureValue(float value, bool hasData)
        {
            Value = value;
            HasData = hasData;
        }

        public static FeatureValue Of(float value) { return new FeatureValue(value, true); }

        public static FeatureValue NoData => new FeatureValue(NoDataValue, false);

        public override string ToString() { return HasData ? Value.ToString("R") : "no data"; }
    }
}
