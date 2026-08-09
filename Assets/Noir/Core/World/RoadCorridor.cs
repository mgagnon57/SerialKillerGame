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
        /// THE ROADS AS THE GAME WILL ACTUALLY HAVE THEM, built once and asked many times.
        ///
        /// WHY THIS TYPE EXISTS, and it is the whole of a defect that took a PlayMode test to see.
        /// The survey passes measured buildings against the road's DECLARED POLYLINE - straight
        /// segments between the authored points. The game does not have that road. WorldBuilder
        /// hands the same points to <see cref="RoadPath.Through"/>, which runs Catmull-Rom through
        /// them: every declared vertex stays exactly where it was and every segment BETWEEN them
        /// becomes an arc. At a bend the drawn centreline bulges off the chord and sweeps over
        /// ground the straight-line test called clear.
        ///
        /// So the town was being cleared of a road it does not have. The layout-level test read
        /// zero buildings in a road and the built-town test read 134, and both were right about
        /// the thing they were measuring - 36 of the 134 on `harrison`, which bends twice.
        ///
        /// Built once per pass rather than per question because Catmull-Rom plus a resample at one
        /// metre is not something to redo for every candidate position of every house.
        /// </summary>
        public sealed class Corridors
        {
            private readonly string[] _names;
            private readonly int[] _widths;
            private readonly RoadPath[] _paths;

            public Corridors(VillageLayout layout)
            {
                var names = new List<string>();
                var widths = new List<int>();
                var paths = new List<RoadPath>();

                if (layout != null)
                    foreach (var run in layout.Roads)
                    {
                        var pts = run.Points;
                        if (pts == null || pts.Count < 2 || run.Width <= 0) continue;

                        var v = new Vec2[pts.Count];
                        for (int i = 0; i < pts.Count; i++) v[i] = new Vec2(pts[i].X, pts[i].Y);

                        names.Add(run.Name);
                        widths.Add(run.Width);
                        paths.Add(RoadPath.Through(v));      // the same call WorldBuilder makes
                    }

                _names = names.ToArray();
                _widths = widths.ToArray();
                _paths = paths.ToArray();
            }

            public int Count => _paths.Length;

            public float WorstPenetration(TileRect box, int minWidth)
            {
                float worst = 0f;
                var corners = CornersOf(box);
                for (int i = 0; i < _paths.Length; i++)
                {
                    if (_widths[i] < minWidth) continue;
                    worst = Deepest(_paths[i], _widths[i] / 2f, corners, worst);
                }
                return worst;
            }

            public float WorstPenetration(IReadOnlyList<Tile> outline, int minWidth)
            {
                if (outline == null || outline.Count < 3) return 0f;
                var pts = new Vec2[outline.Count];
                for (int i = 0; i < outline.Count; i++) pts[i] = new Vec2(outline[i].X, outline[i].Y);

                float worst = 0f;
                for (int i = 0; i < _paths.Length; i++)
                {
                    if (_widths[i] < minWidth) continue;
                    worst = Deepest(_paths[i], _widths[i] / 2f, pts, worst);
                }
                return worst;
            }

            /// <summary>
            /// How far a point is from the nearest road's carriageway, 0 if it is on one.
            ///
            /// Here because "which way does the front door face" is a question about distance to
            /// a road, and every other method on this class asks about PENETRATION - how deep a
            /// box reaches INTO a corridor - which is zero for everything that is not already in
            /// the road and therefore cannot rank one wall of a house against another.
            /// </summary>
            public float DistanceTo(float x, float y)
            {
                var p = new Vec2(x, y);
                float nearest = float.MaxValue;

                for (int i = 0; i < _paths.Length; i++)
                {
                    var (_, lateral) = _paths[i].Project(p);
                    float d = (lateral < 0f ? -lateral : lateral) - _widths[i] / 2f;
                    if (d < 0f) d = 0f;
                    if (d < nearest) nearest = d;
                }

                return nearest;
            }

            public string RoadUnder(TileRect box, int minWidth)
            {
                string name = "";
                float worst = 0f;
                var corners = CornersOf(box);
                for (int i = 0; i < _paths.Length; i++)
                {
                    if (_widths[i] < minWidth) continue;
                    float d = Deepest(_paths[i], _widths[i] / 2f, corners, 0f);
                    if (d > worst) { worst = d; name = _names[i]; }
                }
                return name;
            }

            /// <summary>
            /// The box's corners, its centre, AND the midpoint of each edge.
            ///
            /// Corners alone miss a road clipping the middle of a long wall, which is precisely
            /// the shape of a house standing side-on to a street it overhangs.
            /// </summary>
            private static Vec2[] CornersOf(TileRect r)
            {
                float x0 = r.X, y0 = r.Y, x1 = r.X + r.W, y1 = r.Y + r.H;
                float mx = (x0 + x1) / 2f, my = (y0 + y1) / 2f;
                return new[]
                {
                    new Vec2(x0, y0), new Vec2(x1, y0), new Vec2(x1, y1), new Vec2(x0, y1),
                    new Vec2(mx, y0), new Vec2(x1, my), new Vec2(mx, y1), new Vec2(x0, my),
                    new Vec2(mx, my),
                };
            }
        }

        // ---- the same questions, asked of a BUILT world rather than a layout -------------------
        //
        // WHY BOTH. A VillageLayout is what the survey passes edit; a RoadNetwork is what the game
        // ends up driving on, and the two are not the same object or even the same shape - a
        // layout carries RoadRun with a List<Tile>, the world carries RoadLine with a RoadPath.
        // Only PlayMode can reach the second, and PlayMode is the only thing that can see what the
        // passes actually produced. Testing one and shipping the other is how "there are houses in
        // the road" stayed true for weeks while every test said otherwise.

        /// <summary>
        /// The layout question asked of the building's REAL OUTLINE rather than its bounding box.
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
                if (pts == null || pts.Count < 2 || run.Width < minWidth) continue;
                float half = run.Width / 2f;
                if (half <= 0f) continue;

                for (int i = 1; i < pts.Count; i++)
                {
                    float d = DistanceToOutline(pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y, outline);
                    if (d < half && half - d > worst) worst = half - d;
                }
            }
            return worst;
        }

        /// <summary>Which street this box stands in, or empty.</summary>
        public static string RoadUnder(VillageLayout layout, TileRect box) =>
            RoadUnder(layout, box, 0);

        public static string RoadUnder(VillageLayout layout, TileRect box, int minWidth)
        {
            if (layout == null) return "";
            string name = "";
            float worst = 0f;
            foreach (var run in layout.Roads)
            {
                var pts = run.Points;
                if (pts == null || pts.Count < 2 || run.Width < minWidth) continue;
                float half = run.Width / 2f;
                for (int i = 1; i < pts.Count; i++)
                {
                    float d = DistanceToBox(pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y, box);
                    if (d < half && half - d > worst) { worst = half - d; name = run.Name; }
                }
            }
            return name;
        }

        /// <summary>Which street this outline stands in, or empty.</summary>
        public static string RoadUnder(VillageLayout layout, IReadOnlyList<Tile> outline, int minWidth)
        {
            if (layout == null || outline == null || outline.Count < 3) return "";
            string name = "";
            float worst = 0f;
            foreach (var run in layout.Roads)
            {
                var pts = run.Points;
                if (pts == null || pts.Count < 2 || run.Width < minWidth) continue;
                float half = run.Width / 2f;
                for (int i = 1; i < pts.Count; i++)
                {
                    float d = DistanceToOutline(pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y, outline);
                    if (d < half && half - d > worst) { worst = half - d; name = run.Name; }
                }
            }
            return name;
        }

        public static float WorstPenetration(RoadNetwork roads, TileRect box, int minWidth)
        {
            float worst = 0f;
            if (roads == null) return 0f;
            foreach (var line in roads.Lines)
            {
                if (line.Width < minWidth || line.Path == null) continue;
                float half = line.Width / 2f;
                worst = Deepest(line.Path, half, Corners(box), worst);
            }
            return worst;
        }

        public static float WorstPenetration(RoadNetwork roads, IReadOnlyList<Tile> outline, int minWidth)
        {
            float worst = 0f;
            if (roads == null || outline == null || outline.Count < 3) return 0f;
            var pts = new Vec2[outline.Count];
            for (int i = 0; i < outline.Count; i++) pts[i] = new Vec2(outline[i].X, outline[i].Y);
            foreach (var line in roads.Lines)
            {
                if (line.Width < minWidth || line.Path == null) continue;
                worst = Deepest(line.Path, line.Width / 2f, pts, worst);
            }
            return worst;
        }

        public static string RoadUnder(RoadNetwork roads, TileRect box, int minWidth)
        {
            if (roads == null) return "";
            string name = "";
            float worst = 0f;
            var corners = Corners(box);
            foreach (var line in roads.Lines)
            {
                if (line.Width < minWidth || line.Path == null) continue;
                float d = Deepest(line.Path, line.Width / 2f, corners, 0f);
                if (d > worst) { worst = d; name = line.Name; }
            }
            return name;
        }

        /// <summary>
        /// How far past the kerb the deepest of these points reaches.
        ///
        /// Uses RoadPath.Project rather than walking the point list, because a RoadPath is not a
        /// point list - a curved run is resampled and smoothed, and its own projection is the only
        /// thing that knows where the tarmac actually is. Points that project off either END of
        /// the run are ignored: a house beyond the end of a street is not in it.
        /// </summary>
        private static float Deepest(RoadPath path, float half, Vec2[] points, float worst)
        {
            if (half <= 0f) return worst;
            foreach (var p in points)
            {
                var hit = path.Project(p);
                if (hit.S < 0f || hit.S > path.Length) continue;
                float lateral = hit.Lateral < 0f ? -hit.Lateral : hit.Lateral;
                if (lateral < half && half - lateral > worst) worst = half - lateral;
            }
            return worst;
        }

        private static Vec2[] Corners(TileRect r) => new[]
        {
            new Vec2(r.X, r.Y), new Vec2(r.X + r.W, r.Y),
            new Vec2(r.X + r.W, r.Y + r.H), new Vec2(r.X, r.Y + r.H),
            new Vec2(r.X + r.W / 2f, r.Y + r.H / 2f),
        };

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

        /// <summary>
        /// Distance from a segment to a closed ring - zero when they meet. Same three cases as
        /// the box: an endpoint inside the ring, the segment crossing an edge, or the ring's
        /// nearest vertex.
        /// </summary>
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
                float dy = yj - yi;
                if ((yi > py) != (yj > py) &&
                    px < (xj - xi) * (py - yi) / (dy == 0f ? 1e-9f : dy) + xi)
                    inside = !inside;
            }
            return inside;
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
