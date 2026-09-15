namespace Thesis.Sim
{
    // A full 3-D world position as exported from a scene. Kept 3-D (not Vec2f)
    // because WaveSpawner derives baseLifeTime from Vector3.Distance(spawn, core),
    // which includes any height difference between the two transforms.
    public sealed class WorldPoint
    {
        public float X;
        public float Y;
        public float Z;

        public WorldPoint() { }

        public WorldPoint(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString() { return "(" + X + ", " + Y + ", " + Z + ")"; }
    }
}
