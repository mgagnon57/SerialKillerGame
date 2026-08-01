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
    ///
    /// THE SAME IS NOW TRUE OF HEIGHT. Every overload here used to hand back a literal 0f (or
    /// whatever the caller passed) as world Y - the whole map really was one flat plane, and the
    /// plane was assumed rather than stored. ElevationGrid.HeightAt is folded in here instead, so
    /// a caller passing "0f, I am at ground level" or "2.4f, I am on the first floor" gets the
    /// REAL ground under that point added underneath it automatically, without that caller ever
    /// needing to know elevation exists. That is only correct for a height meant as a LOCAL offset
    /// above local ground - a building's floors stacking on its own base, a kerb sitting above its
    /// own road - and it is wrong for anywhere something already has an absolute world Y from a
    /// non-Space3D source (a raycast hit, a rigidbody). Everything in this project that places
    /// something by simulation coordinates already means the local kind, which is what made
    /// folding it in here - one function - sufficient for most of the project rather than a
    /// change to every call site. See docs/IDEAS.md's Env section for what still needed touching
    /// directly: ground/road meshes and anything else that builds world Vector3s without going
    /// through here at all.
    /// </summary>
    public static class Space3D
    {
        /// <summary>Storey height in metres. A cottage wall, not a warehouse.</summary>
        public const float WallHeight = 3.0f;

        public static Vector3 ToWorld(float gx, float gy) =>
            new Vector3(gx, ElevationGrid.HeightAt(gx, gy), -gy);

        public static Vector3 ToWorld(Vec2 v) =>
            new Vector3(v.X, ElevationGrid.HeightAt(v.X, v.Y), -v.Y);

        public static Vector3 ToWorld(Tile t) =>
            new Vector3(t.X + 0.5f, ElevationGrid.HeightAt(t.X + 0.5f, t.Y + 0.5f), -(t.Y + 0.5f));

        public static Vector3 ToWorld(Vec2 v, float height) =>
            new Vector3(v.X, height + ElevationGrid.HeightAt(v.X, v.Y), -v.Y);

        /// <summary>The village-space point under a world position. Unaffected by elevation -
        /// horizontal position never depended on height, which is what keeps click-picking,
        /// FromWorld and everything built on it correct without any change at all.</summary>
        public static Vec2 FromWorld(Vector3 w) => new Vec2(w.x, -w.z);
        public static Tile TileAt(Vector3 w) => FromWorld(w).ToTile();

        /// <summary>Centre of the whole map, for framing the camera.</summary>
        public static Vector3 Centre(int width, int height) =>
            new Vector3(width * 0.5f, ElevationGrid.HeightAt(width * 0.5f, height * 0.5f), -height * 0.5f);

        /// <summary>
        /// Where a ray meets the REAL ground, not the flat y=0 plane every click and pick used to
        /// assume. A single flat-plane intersection is wrong wherever the real terrain under it is
        /// not at y=0 - which the map now is not, over most of its area - because the ray is
        /// rarely vertical: two different heights of "the ground" put the same ray through two
        /// different (x,z) points, not just two different heights at the same point.
        ///
        /// Iterated rather than solved directly, because the ground is a sampled height field
        /// with no closed form to intersect a ray against. Three passes converges to well under a
        /// centimetre on terrain this gentle (24m of relief over 2,500m) - each pass narrows the
        /// error by roughly the local slope, which here is under a percent.
        /// </summary>
        public static bool GroundHit(Ray ray, out Vector3 point)
        {
            point = default;
            float y = 0f;

            for (int i = 0; i < 3; i++)
            {
                var plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
                if (!plane.Raycast(ray, out float t))
                {
                    // The ray runs parallel to every horizontal plane there is (looking dead
                    // level) - no amount of iterating finds a ground it will never reach.
                    if (i == 0) return false;
                    break;
                }
                point = ray.GetPoint(t);
                y = ElevationGrid.HeightAt(point.x, -point.z);
            }
            return true;
        }
    }
}
