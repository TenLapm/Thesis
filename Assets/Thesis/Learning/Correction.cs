namespace Thesis.Learning
{
    // One strategy's learned term for one decision. Value is what the director adds
    // to the authored score (I4: bounded to ±1, and exactly 0 when Cold). Theta,
    // Alpha and Beta are kept so the telemetry row can say why.
    public struct Correction
    {
        public float Theta;
        public float Alpha;
        public float Beta;
        public float Value;
        public bool Cold;
    }
}
