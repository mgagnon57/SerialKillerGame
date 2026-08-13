using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// Convex-polygon overlap in tile space, integer-only so a result is exact and reproducible
    /// for a seed the way everything else in Core is. Separating Axis Theorem: two convex shapes
    /// have no positive-area intersection if some edge's own normal, treated as an axis, keeps
    /// their vertex projections apart - crucially, "apart" here includes just touching, so two
    /// rectangles sharing a full edge with zero gap are NOT an overlap by this test, only a true
    /// interior intersection is.
    ///
    /// WHY THIS EXISTS. WorldValidator's overlap check compares two Places' TileRect Bounds, and
    /// that is right for the axis-aligned majority of the town - but a Place carrying an Outline
    /// (SeatOnSurvey's real footprints, DowntownFromSanborn's terrace units) can be ROTATED, and a
    /// rotated rectangle's own bounding box always covers more ground than the rectangle itself.
    /// Two such rectangles standing edge to edge, sharing a party wall on an angled street, get
    /// bounding boxes that overlap even though the buildings do not - DowntownFromSanborn found
    /// this the day 112 S Chicago was first canted to Chicago Street's real ~18-degree line: every
    /// one of its 17 storefronts was rejected as overlapping its neighbour.
    ///
    /// CONVEX ONLY. Every unit DowntownFromSanborn lays is a rotated rectangle, so this is exact
    /// for the case it was built for. WorldValidator only ever calls it to try to EXCUSE an
    /// overlap Bounds already flagged, never to find one Bounds missed - Bounds is checked first
    /// and still owns every negative result, so a concave real footprint (SeatOnSurvey's, on the
    /// rare lot that is not roughly rectangular) can only ever fall back to the old Bounds-only
    /// answer, never end up less safe than before this existed.
    /// </summary>
    public static class Polygon
    {
        /// <summary>True if two convex simple polygons share more than a boundary - a proper
        /// interior intersection, not two edges or corners merely touching.</summary>
        public static bool Overlaps(Tile[] a, Tile[] b)
        {
            if (a == null || a.Length < 3 || b == null || b.Length < 3) return false;
            return !HasSeparatingAxis(a, b) && !HasSeparatingAxis(b, a);
        }

        /// <summary>True if some edge normal of <paramref name="poly"/> separates the two shapes'
        /// projections - including the case where the projections merely touch at one value.</summary>
        private static bool HasSeparatingAxis(Tile[] poly, Tile[] other)
        {
            int n = poly.Length;
            for (int i = 0; i < n; i++)
            {
                var p1 = poly[i];
                var p2 = poly[(i + 1) % n];
                long nx = -(long)(p2.Y - p1.Y);
                long ny = (long)(p2.X - p1.X);
                if (nx == 0 && ny == 0) continue;   // a repeated point contributes no edge

                Project(poly, nx, ny, out long minA, out long maxA);
                Project(other, nx, ny, out long minB, out long maxB);

                if (maxA <= minB || maxB <= minA) return true;
            }
            return false;
        }

        private static void Project(Tile[] poly, long nx, long ny, out long min, out long max)
        {
            min = long.MaxValue; max = long.MinValue;
            foreach (var v in poly)
            {
                long proj = nx * v.X + ny * v.Y;
                if (proj < min) min = proj;
                if (proj > max) max = proj;
            }
        }

        /// <summary>
        /// Whether a tile's own centre is inside the ring - THE SAME even-odd crossing test
        /// WorldBuilder.Inside runs, deliberately, so anything laying down a Door alongside an
        /// Outline can prove its choice survives the exact check that decides whether the Outline
        /// is kept at all, rather than guessing an offset and finding out at build time. See
        /// DowntownFromSanborn.Apply for why a door placed a fixed distance off an ANGLED wall
        /// cannot just trust the same margin an axis-aligned building gets for free.
        /// </summary>
        public static bool Contains(Tile[] ring, Tile t)
        {
            if (ring == null || ring.Length < 3 || !t.IsValid) return false;
            float px = t.X + 0.5f, py = t.Y + 0.5f;
            bool inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                float ax = ring[i].X, ay = ring[i].Y, bx = ring[j].X, by = ring[j].Y;
                if ((ay > py) != (by > py) && px < (bx - ax) * (py - ay) / (by - ay) + ax)
                    inside = !inside;
            }
            return inside;
        }
    }
}
