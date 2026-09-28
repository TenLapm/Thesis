using System;

namespace Thesis.Core
{
    // A position on the world XZ plane (X = world x, Y = world z). The simulation
    // keeps world units rather than tile units so speeds such as baseMoveSpeed 1.25
    // mean exactly what they meant in the Unity version (ARCHITECTURE.md §4.3).
    //
    // Every intermediate float result below is wrapped in an explicit (float) cast.
    // C# allows a runtime to keep float intermediates at higher precision; Unity's
    // Mono does (it evaluated `a + b / c * d` in double and rounded once), .NET
    // rounds each step. An explicit cast is REQUIRED by the spec to round, so the
    // casts make Mono and CoreCLR produce identical bits. Without them the two
    // differed by one bit on rare steps (WP4, DEVLOG; FloatDeterminismTests).
    public readonly struct Vec2f : IEquatable<Vec2f>
    {
        public readonly float X;
        public readonly float Y;

        public Vec2f(float x, float y)
        {
            X = x;
            Y = y;
        }

        // [JsonIgnore]: computed values must not be written into maps/replays as if
        // they were data.
        [Newtonsoft.Json.JsonIgnore]
        public float SqrMagnitude => (float)((float)(X * X) + (float)(Y * Y));

        // (float)Math.Sqrt rather than MathF.Sqrt, mirroring Unity's Vector3 so a
        // port reproduces Unity's rounding exactly.
        [Newtonsoft.Json.JsonIgnore]
        public float Magnitude => (float)Math.Sqrt(SqrMagnitude);

        public static Vec2f operator +(Vec2f a, Vec2f b) { return new Vec2f(a.X + b.X, a.Y + b.Y); }

        public static Vec2f operator -(Vec2f a, Vec2f b) { return new Vec2f(a.X - b.X, a.Y - b.Y); }

        public static Vec2f operator *(Vec2f a, float s) { return new Vec2f(a.X * s, a.Y * s); }

        // Exact float equality, on purpose: MoveTowards returns the target itself
        // once it arrives, so "reached the tile centre" is an exact comparison.
        public static bool operator ==(Vec2f a, Vec2f b) { return a.Equals(b); }

        public static bool operator !=(Vec2f a, Vec2f b) { return !a.Equals(b); }

        public static float Distance(Vec2f a, Vec2f b) { return (a - b).Magnitude; }

        // Port of UnityEngine.Vector3.MoveTowards restricted to the XZ plane (agents'
        // Y is pinned to groundHeight, so the Y term is always zero in the original).
        // The clamp is what stops a fast agent from skipping past a tile centre - and
        // therefore past a wall tile - in a single step (see FlowAgent.Update).
        public static Vec2f MoveTowards(Vec2f current, Vec2f target, float maxDistanceDelta)
        {
            float toX = (float)(target.X - current.X);
            float toY = (float)(target.Y - current.Y);
            float sqDist = (float)((float)(toX * toX) + (float)(toY * toY));

            if (sqDist == 0f || (maxDistanceDelta >= 0f && sqDist <= (float)(maxDistanceDelta * maxDistanceDelta)))
                return target;

            float dist = (float)Math.Sqrt(sqDist);
            return new Vec2f((float)(current.X + (float)((float)(toX / dist) * maxDistanceDelta)),
                             (float)(current.Y + (float)((float)(toY / dist) * maxDistanceDelta)));
        }

        public bool Equals(Vec2f other) { return X == other.X && Y == other.Y; }

        public override bool Equals(object obj) { return obj is Vec2f other && Equals(other); }

        public override int GetHashCode() { return unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode()); }

        public override string ToString() { return "(" + X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", " + Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ")"; }
    }
}
