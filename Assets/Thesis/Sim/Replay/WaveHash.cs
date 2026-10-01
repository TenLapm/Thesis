namespace Thesis.Sim
{
    // The state hash at the moment a wave resolved: taken right after the Tick()
    // that raised WaveResolved, so Tick is State.Tick at that point (one past the
    // tick that ran). A wave closed by the core dying is recorded the same way.
    public sealed class WaveHash
    {
        public int Wave;
        public int Tick;

        // 16 hex digits. A string, not a number: JSON readers that go through
        // double (JavaScript, some spreadsheet imports) cannot hold 64 bits.
        public string Hash;

        public override string ToString() { return "wave " + Wave + " @tick " + Tick + " " + Hash; }
    }
}
