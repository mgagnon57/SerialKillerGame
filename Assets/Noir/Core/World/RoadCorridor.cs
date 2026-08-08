using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// WHETHER SOMETHING STANDS IN A ROAD.
    ///
    /// WHY THIS EXISTS AND WHY IT IS IN CORE. This test lived as a private method inside
    /// <c>FillFromSurvey</c>, so exactly one of the three passes that put buildings on the ground
    /// could perform it. <c>FillFromSurvey</c> refused
    /// to raise a house in a road and said so in its log; <c>SeatOnSurvey</c>, which seats all 824
    /// MEASURED footprints, had no road check of any kind, and neither did
    /// <c>DowntownFromSanborn</c>. The result is the thing the owner kept reporting and nobody
    /// fixed: houses standing in the road, "not all but some" - the some being the measured ones.
    ///
    /// It is in Core rather than beside its callers because <see cref="VillageLayout"/> and
    /// <see cref="TileRect"/> are Core types, so putting it here is what makes it TESTABLE. The
    /// survey layer has no automated cover at all; this is one piece of it that now does.
    ///
    /// NO SQUARE ROOTS. Everything below compares squared distances. That is not a micro
    /// optimisation - Core bans transcendentals so a seed reproduces a village exactly, and a
    /// comparison that never leaves integer-and-multiply arithmetic cannot drift between machines.
    /// </summary>
    public static class RoadCorridor
    {
        /// <summary>
        /// Whether this box touches any road's carriageway.
        ///
        /// The corridor is the centreline swept by half the road's declared width, which is the
        /// carriageway rather than the right of way. A porch inside the right of way is ordinary
        /// in an Illinois farm town - the verge belongs to the village and the steps sit on it -
        /// so testing the wider band would refuse half the houses on Chicago Street. Testing the
        /// carriageway refuses exactly the thing that is wrong: masonry on the tarmac.
        /// </summary>
        public static bool Touches(VillageLayout layout, TileRect box) =>
            WorstPenetration(layout, box) > 0f;

        /// <summary>
        /// How far into the nearest carriageway this box reaches, in metres. Zero when it is
        /// clear. Reported rather than merely tested so a refusal can say HOW wrong it was, which
        /// is the difference between "your survey is a few centimetres out" and "this building is
        /// standing in the middle of Attica Street".
        /// </summary>
        public static float WorstPenetration(VillageLayout layout, TileRect box) =>
            WorstPenetration(layout, box, 0);

        /// <summary>
        /// The same, counting only roads at least <paramref name="minWidth"/> wide.
        ///
        /// WHY A CALLER WOULD WANT TO IGNORE THE NARROW ONES. Rossville's streets come from the
        /// county's own centrelines and their corridors are measured. Its alleys do not: they are
        /// traced out of the gaps between lots and then given a FLAT 4 m by
        /// <c>build-roads.py</c>'s corridor table, the same 4 m everywhere, because nobody has
        /// measured an alley. So when a surveyed building and an alley disagree, the alley is the
        /// guess and the building is the measurement - and in a farm town a garage genuinely does
        /// sit on the alley line. When a building and a STREET disagree, something is wrong.
        /// </summary>
        public static float WorstPenetration(VillageLayout layout, TileRect box, int minWidth)
        {
            if (layout == null || box.W <= 0 || box.H <= 0) return 0f;

            float worst = 0f;
            foreach (var run in layout.Roads)
            {
                var pts = run.Points;
                if (pts == null || pts.Count < 2) continue;
                if (run.Width < minWidth) continue;
                float half = run.Width / 2f;
                if (half <= 0f) continue;

                for (int i = 1; i < pts.Count; i++)
                {
                    float d = DistanceToBox(pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y, box);
                    if (d < half)
                    {
                        float pen = half - d;
                        if (pen > worst) worst = pen;
                    }
                }
            }
            return worst;
        }

        /// <summary>
        /// The generous form, for a pass that is CHOOSING where to put something rather than
        /// honouring a measurement. <paramref name="clearance"/> is added to the corridor, so a
        /// building is kept off the kerb as well as out of the road. FillFromSurvey wants this;
        /// SeatOnSurvey deliberately does not, because a measured building that merely stands
        /// close to the kerb is a fact about the town rather than a mistake.
        /// </summary>
        public static bool TouchesWithClearance(VillageLayout layout, TileRect box, float clearance)
        {
            if (layout == null || box.W <= 0 || box.H <= 0) return false;

            foreach (var run in layout.Roads)
            {
                var pts = run.Points;
                if (pts == null || pts.Count < 2) continue;
                float half = run.Width / 2f + clearance;

                for (int i = 1; i < pts.Count; i++)
                    if (DistanceToBox(pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y, box) < half)
                        return true;
            }
            return false;
        }

        /// <summary>
        /// The narrowest road that counts as a surveyed street rather than a traced alley.
        /// Rossville paves its streets to a 10 m corridor and its alleys to a flat 4 m.
        /// </summary>
        public const int StreetWidth = 10;

        /// <summary>
        /// The same question asked of the building's REAL OUTLINE rather than its bounding box.
        ///
        /// USE THIS WHEREVER AN OUTLINE EXISTS, and the grade school is why. It stands on three
        /// parcels in an L, so its bounding box takes in the yard and crosses Benton, Green and
        /// Chicago - three streets the school itself is nowhere near. A box test refuses it and
        /// the town loses its school. Twenty of Rossville's measured buildings fill less than 55%
        /// of their own box; every one of them is a trap of this shape.
        /// </summary>
        public static float WorstPenetration(VillageLayout layout, IReadOnlyList<Tile> outline,
                                             int minWidth)
        {
            if (layout == null || outline == null || outline.Count < 3) return 0f;

            float worst = 0f;
            foreach (var run in layout.Roads)
            {
                var pts = run.Points;
                if (pts == null || pts.Count < 2) continue;
                if (run.Width < minWidth) continue;
                float half = run.Width / 2f;
                if (half <= 0f) continue;

                for (int i = 1; i < pts.Count; i++)
                {
                    float d = DistanceToOutline(pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y, outline);
                    if (d < half)
                    {
                        float pen = half - d;
                        if (pen > worst) worst = pen;
                    }
                }
            }
            return worst;
        }

        /// <summary>Which street this outline stands in, or empty.</summary>
        public static string RoadUnder(VillageLayout layout, IReadOnlyList<Tile> outline, int minWidth)
        {
            if (layout == null || outline == null || outline.Count < 3) return "";
            string worstName = "";
            float worst = 0f;

            foreach (var run in layout.Roads)
            {
                var pts = run.Points;
                if (pts == null || pts.Count < 2) continue;
                if (run.Width < minWidth) continue;
                float half = run.Width / 2f;

                for (int i = 1; i < pts.Count; i++)
                {
                    float d = DistanceToOutline(pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y, outline);
                    if (d < half && half - d > worst) { worst = half - d; worstName = run.Name; }
                }
            }
            return worstName;
        }

        private static float DistanceToOutline(float ax, float ay, float bx, float by,
                                               IReadOnlyList<Tile> ring)
        {
            int n = ring.Count;
            if (PointInRing(ax, ay, ring) || PointInRing(bx, by, ring)) return 0f;

            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var p = ring[i];
                var q = ring[(i + 1) % n];
                if (SegmentsCross(ax, ay, bx, by, p.X, p.Y, q.X, q.Y)) return 0f;
                float d = PointToSegmentSq(p.X, p.Y, ax, ay, bx, by);
                if (d < best) best = d;
            }
            return (float)System.Math.Sqrt(best);
        }

        private static bool PointInRing(float px, float py, IReadOnlyList<Tile> ring)
        {
            bool inside = false;
            int n = ring.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = ring[i].X, yi = ring[i].Y, xj = ring[j].X, yj = ring[j].Y;
                if ((yi > py) != (yj > py) &&
                    px < (xj - xi) * (py - yi) / ((yj - yi) == 0f ? 1e-9f : (yj - yi)) + xi)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>Which road this box stands in, or empty. For the message, not the decision.</summary>
        public static string RoadUnder(VillageLayout layout, TileRect box) =>
            RoadUnder(layout, box, 0);

        /// <summary>Which road at least <paramref name="minWidth"/> wide this box stands in.</summary>
        public static string RoadUnder(VillageLayout layout, TileRect box, int minWidth)
        {
            if (layout == null) return "";
            string worstName = "";
            float worst = 0f;

            foreach (var run in layout.Roads)
            {
                var pts = run.Points;
                if (pts == null || pts.Count < 2) continue;
                if (run.Width < minWidth) continue;
                float half = run.Width / 2f;

                for (int i = 1; i < pts.Count; i++)
                {
                    float d = DistanceToBox(pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y, box);
                    if (d < half && half - d > worst) { worst = half - d; worstName = run.Name; }
                }
            }
            return worstName;
        }

        /// <summary>
        /// Distance from a segment to an axis-aligned box - zero when they meet.
        ///
        /// Three ways they can meet and all three are needed. A corner of the box can be near the
        /// segment; the segment can cross an edge of the box; or the segment can lie wholly
        /// inside the box, which is the case a corner test alone misses and which is exactly what
        /// a short alley run through a large building looks like.
        /// </summary>
        private static float DistanceToBox(float ax, float ay, float bx, float by, TileRect r)
        {
            float x0 = r.X, y0 = r.Y, x1 = r.X + r.W, y1 = r.Y + r.H;

            if (PointInBox(ax, ay, x0, y0, x1, y1) || PointInBox(bx, by, x0, y0, x1, y1)) return 0f;

            if (SegmentsCross(ax, ay, bx, by, x0, y0, x1, y0) ||
                SegmentsCross(ax, ay, bx, by, x1, y0, x1, y1) ||
                SegmentsCross(ax, ay, bx, by, x1, y1, x0, y1) ||
                SegmentsCross(ax, ay, bx, by, x0, y1, x0, y0)) return 0f;

            float best = PointToSegmentSq(x0, y0, ax, ay, bx, by);
            float d = PointToSegmentSq(x1, y0, ax, ay, bx, by); if (d < best) best = d;
            d = PointToSegmentSq(x1, y1, ax, ay, bx, by); if (d < best) best = d;
            d = PointToSegmentSq(x0, y1, ax, ay, bx, by); if (d < best) best = d;

            // The one square root in the file, on a value already reduced to a single scalar.
            return (float)System.Math.Sqrt(best);
        }

        private static bool PointInBox(float px, float py, float x0, float y0, float x1, float y1) =>
            px >= x0 && px <= x1 && py >= y0 && py <= y1;

        private static float PointToSegmentSq(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float len = dx * dx + dy * dy;
            float t = 0f;
            if (len > 0f)
            {
                t = ((px - ax) * dx + (py - ay) * dy) / len;
                if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            }
            float qx = ax + dx * t, qy = ay + dy * t;
            return (px - qx) * (px - qx) + (py - qy) * (py - qy);
        }

        private static bool SegmentsCross(float ax, float ay, float bx, float by,
                                          float cx, float cy, float dx2, float dy2)
        {
            float d1 = Cross(cx, cy, dx2, dy2, ax, ay);
            float d2 = Cross(cx, cy, dx2, dy2, bx, by);
            float d3 = Cross(ax, ay, bx, by, cx, cy);
            float d4 = Cross(ax, ay, bx, by, dx2, dy2);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
                   ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        private static float Cross(float ax, float ay, float bx, float by, float px, float py) =>
            (bx - ax) * (py - ay) - (by - ay) * (px - ax);
    }
}
