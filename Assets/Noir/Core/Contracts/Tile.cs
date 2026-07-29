using System;

namespace Noir.Core.Contracts
{
    /// <summary>
    /// Integer tile coordinate. One tile is one metre.
    /// All simulation logic that must be deterministic works in Tile space, never in Vec2 —
    /// integers have no float-rounding divergence to worry about.
    /// </summary>
    public readonly struct Tile : IEquatable<Tile>
    {
        public readonly int X;
        public readonly int Y;

        public Tile(int x, int y) { X = x; Y = y; }

        public static readonly Tile None = new Tile(int.MinValue, int.MinValue);
        public bool IsValid => X != int.MinValue;

        /// <summary>Chebyshev distance — the number of 8-directional steps between two tiles.</summary>
        public static int ChebyshevDistance(Tile a, Tile b)
        {
            int dx = a.X - b.X; if (dx < 0) dx = -dx;
            int dy = a.Y - b.Y; if (dy < 0) dy = -dy;
            return dx > dy ? dx : dy;
        }

        /// <summary>Manhattan distance — steps if only 4-directional movement were allowed.</summary>
        public static int ManhattanDistance(Tile a, Tile b)
        {
            int dx = a.X - b.X; if (dx < 0) dx = -dx;
            int dy = a.Y - b.Y; if (dy < 0) dy = -dy;
            return dx + dy;
        }

        /// <summary>Squared euclidean distance. Integer, so exact and comparison-safe.</summary>
        public static int DistanceSquared(Tile a, Tile b)
        {
            int dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        public bool Equals(Tile other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Tile o && Equals(o);
        public override int GetHashCode() => unchecked((X * 397) ^ Y);
        public override string ToString() => $"({X},{Y})";
        public static bool operator ==(Tile a, Tile b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Tile a, Tile b) => a.X != b.X || a.Y != b.Y;
    }
}
