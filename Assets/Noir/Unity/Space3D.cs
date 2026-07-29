using UnityEngine;
using Noir.Core.Contracts;

namespace Noir.Unity
{
    /// <summary>
    /// Simulation coordinates to 3D world space, in one place.
    ///
    /// One tile is one metre, so the mapping is nearly free: grid X becomes world X, and grid Y
    /// becomes world -Z. The negation is the only subtlety - the grid counts rows southward
    /// (row 0 is the north edge, as written in village.txt) while Unity's +Z points north.
    /// Doing that flip here rather than in each renderer means if it is ever wrong, it is wrong
    /// in exactly one place.
    /// </summary>
    public static class Space3D
    {
        /// <summary>Storey height in metres. A cottage wall, not a warehouse.</summary>
        public const float WallHeight = 3.0f;

        public static Vector3 ToWorld(float gx, float gy) => new Vector3(gx, 0f, -gy);
        public static Vector3 ToWorld(Vec2 v) => new Vector3(v.X, 0f, -v.Y);
        public static Vector3 ToWorld(Tile t) => new Vector3(t.X + 0.5f, 0f, -(t.Y + 0.5f));

        public static Vector3 ToWorld(Vec2 v, float height) => new Vector3(v.X, height, -v.Y);

        public static Vec2 FromWorld(Vector3 w) => new Vec2(w.x, -w.z);
        public static Tile TileAt(Vector3 w) => FromWorld(w).ToTile();

        /// <summary>Centre of the whole map, for framing the camera.</summary>
        public static Vector3 Centre(int width, int height) =>
            new Vector3(width * 0.5f, 0f, -height * 0.5f);
    }
}
