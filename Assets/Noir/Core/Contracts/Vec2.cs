using System;

namespace Noir.Core.Contracts
{
    /// <summary>
    /// Sub-tile position, in metres. Used for smooth movement between tiles and for rendering.
    ///
    /// Deliberately NOT UnityEngine.Vector2 — Core has zero engine references, which is what
    /// lets the whole simulation compile and run under `dotnet test` with no editor involved.
    ///
    /// Determinism note: only +, -, * and / are used here. Transcendentals (Sin/Cos/Exp/Log)
    /// are banned in Core because their results are implementation-defined and have changed
    /// between .NET runtimes — that would silently break replay on a runtime upgrade.
    /// A build-time test greps for them.
    /// </summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly float X;
        public readonly float Y;

        public Vec2(float x, float y) { X = x; Y = y; }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);

        /// <summary>Centre of the given tile. Tiles are unit squares, so the centre is +0.5.</summary>
        public static Vec2 CentreOf(Tile t) => new Vec2(t.X + 0.5f, t.Y + 0.5f);

        /// <summary>The tile containing this position.</summary>
        public Tile ToTile() => new Tile((int)MathFloor(X), (int)MathFloor(Y));

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);

        public float LengthSquared => X * X + Y * Y;

        /// <summary>Linear interpolation. Used by the renderer to smooth 20 Hz ticks to 60+ fps.</summary>
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) =>
            new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        // Local floor so Core never touches System.Math for anything that could vary.
        private static float MathFloor(float v)
        {
            int i = (int)v;
            return (v < 0f && v != i) ? i - 1 : i;
        }

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Vec2 o && Equals(o);
        public override int GetHashCode() => unchecked((X.GetHashCode() * 397) ^ Y.GetHashCode());
        public override string ToString() => $"({X:0.00},{Y:0.00})";
    }
}
