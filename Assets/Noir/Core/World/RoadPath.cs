using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// Where a road's centre line actually runs.
    ///
    /// This is the generalisation of RoadLine.Centre, which is A SINGLE FLOAT - "the x of a
    /// north-south road, the y of an east-west one" - and therefore cannot describe a road that
    /// bends. Illinois Route 1 through Rossville bends 100m off its own chord, and the town is
    /// built with it drawn as a straight line, which puts 85% of its length inside the county's
    /// own lot boundaries.
    ///
    /// THE STRAIGHT CASE IS EXACT AND THAT IS THE WHOLE SAFETY ARGUMENT. Every one of the 27
    /// roads in Content/city.txt is two points on one axis, and for those this class does no
    /// smoothing, no resampling and no square roots - PointAt is the declared coordinate,
    /// returned bit for bit. A curve costs what a curve costs; a straight road costs nothing
    /// and changes nothing.
    ///
    /// NO TRANSCENDENTALS. Tangents are differences, normals are (-y, x), and which way a turn
    /// goes is the sign of a cross product. Sqrt appears once, for arc length, and is allowed
    /// because IEEE-754 requires it correctly rounded - see CoreDeterminismTests.
    /// </summary>
    public sealed class RoadPath
    {
        private readonly Vec2 _from;
        private readonly Vec2 _to;
        private readonly Vec2 _tangent;      // unit, exact for an axis-aligned run

        public bool IsStraightAxisAligned { get; }
        public float Length { get; }

        /// <summary>
        /// The smallest axis-aligned box holding every point of this path.
        ///
        /// Here so that asking "could these two roads possibly meet?" costs four comparisons
        /// instead of walking both centre lines. RoadNetwork asks it once per pair of roads and
        /// there are N squared pairs, so the cheap answer is the whole point - and it is only
        /// ever used to say NO. A pair whose boxes overlap is still measured properly.
        /// </summary>
        public float MinX { get; }
        public float MinY { get; }
        public float MaxX { get; }
        public float MaxY { get; }

        /// <summary>
        /// Sub-divisions inserted between each pair of declared vertices. FOUR, because that is
        /// what MapFeatures.Smoothed has always used and the committed rail bed was built with -
        /// see RoadPath.Smooth.
        /// </summary>
        public const int SmoothSteps = 4;

        /// <summary>
        /// Metres between resampled points. One, matching what CityRailBed already resamples the
        /// rail bed at, so a long straight and a tight bend are built to the same resolution.
        /// Only a curve pays for this; a straight road never reaches the resampler.
        /// </summary>
        public const float ResamplePitch = 1f;

        private readonly Vec2[] _dense;          // null for the straight case
        private readonly float[] _cumulative;    // null for the straight case

        private RoadPath(Vec2 from, Vec2 to)
        {
            _dense = null;
            _cumulative = null;
            _from = from;
            _to = to;
            IsStraightAxisAligned = true;

            MinX = from.X < to.X ? from.X : to.X;
            MaxX = from.X < to.X ? to.X : from.X;
            MinY = from.Y < to.Y ? from.Y : to.Y;
            MaxY = from.Y < to.Y ? to.Y : from.Y;

            float dx = to.X - from.X, dy = to.Y - from.Y;

            // Exactly one axis moves, so the length is that difference and the tangent is a
            // cardinal unit vector. Deliberately NOT sqrt(dx*dx+dy*dy): for dy=2400 that is
            // sqrt(5760000), which is 2400 to the last bit but arrives there through a rounding
            // nobody needs to trust.
            if (dx == 0f)
            {
                Length = dy < 0f ? -dy : dy;
                _tangent = new Vec2(0f, dy < 0f ? -1f : 1f);
            }
            else
            {
                Length = dx < 0f ? -dx : dx;
                _tangent = new Vec2(dx < 0f ? -1f : 1f, 0f);
            }
        }

        private RoadPath(Vec2[] dense, float[] cumulative)
        {
            _dense = dense;
            _cumulative = cumulative;
            IsStraightAxisAligned = false;
            Length = cumulative[cumulative.Length - 1];
            _from = dense[0];
            _to = dense[dense.Length - 1];

            // OVER THE DENSE SAMPLES, not the declared vertices. A Catmull-Rom arc bulges past
            // its own control points, and a box drawn round the vertices would cut the corner
            // off - which is exactly where a bending road meets the one it bends towards.
            float minX = dense[0].X, maxX = minX, minY = dense[0].Y, maxY = minY;
            for (int i = 1; i < dense.Length; i++)
            {
                if (dense[i].X < minX) minX = dense[i].X;
                if (dense[i].X > maxX) maxX = dense[i].X;
                if (dense[i].Y < minY) minY = dense[i].Y;
                if (dense[i].Y > maxY) maxY = dense[i].Y;
            }
            MinX = minX; MaxX = maxX; MinY = minY; MaxY = maxY;
        }

        /// <summary>
        /// A straight run between two points on one axis. Throws if they are not: this
        /// constructor's promise is exactness, and it cannot keep it off-axis.
        /// </summary>
        public static RoadPath Straight(Vec2 from, Vec2 to)
        {
            if (from.X != to.X && from.Y != to.Y)
                throw new ArgumentException(
                    "RoadPath.Straight needs two points sharing an axis; got "
                    + from + " and " + to + ". A road that bends is built with RoadPath.Through.");
            if (from.X == to.X && from.Y == to.Y)
                throw new ArgumentException("RoadPath.Straight needs two distinct points; got " + from);

            return new RoadPath(from, to);
        }

        /// <summary>
        /// A road through the points it was declared with. Two points on one axis short-circuit
        /// to the exact straight case - which is every road in the real map, and the reason
        /// Phase A changes no numbers.
        /// </summary>
        public static RoadPath Through(IReadOnlyList<Vec2> points)
        {
            if (points == null || points.Count < 2)
                throw new ArgumentException("a road path needs at least two points");

            if (points.Count == 2 && (points[0].X == points[1].X || points[0].Y == points[1].Y))
                return Straight(points[0], points[1]);

            var dense = Resample(Smooth(points), ResamplePitch);

            var cumulative = new float[dense.Length];
            for (int i = 1; i < dense.Length; i++)
                cumulative[i] = cumulative[i - 1] + Distance(dense[i - 1], dense[i]);

            return new RoadPath(dense, cumulative);
        }

        /// <summary>
        /// Catmull-Rom through the declared vertices, unchanged - every original point is still
        /// on the curve exactly where it was, only the straight segments between them become an
        /// arc. End points are their own neighbour, the standard clamp for a spline with nothing
        /// before its first control point.
        ///
        /// MOVED HERE FROM MapFeatures.Smoothed, which drew the railway with it. One curve, used
        /// by the rail and the roads alike, and testable under dotnet test - which it never was
        /// on the Unity side. Change nothing about the arithmetic without re-rendering the rail
        /// snapshots: the committed bed was built from these exact numbers.
        /// </summary>
        public static Vec2[] Smooth(IReadOnlyList<Vec2> pts)
        {
            if (pts.Count < 3)
            {
                var copy = new Vec2[pts.Count];
                for (int i = 0; i < pts.Count; i++) copy[i] = pts[i];
                return copy;
            }

            var result = new List<Vec2>((pts.Count - 1) * SmoothSteps + 1) { pts[0] };
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var p0 = pts[i - 1 < 0 ? 0 : i - 1];
                var p1 = pts[i];
                var p2 = pts[i + 1];
                var p3 = pts[i + 2 > pts.Count - 1 ? pts.Count - 1 : i + 2];

                for (int s = 1; s <= SmoothSteps; s++)
                    result.Add(CatmullRom(p0, p1, p2, p3, (float)s / SmoothSteps));
            }
            return result.ToArray();
        }

        private static Vec2 CatmullRom(Vec2 p0, Vec2 p1, Vec2 p2, Vec2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return (p1 * 2f
                  + (p2 - p0) * t
                  + (p0 * 2f - p1 * 5f + p2 * 4f - p3) * t2
                  + (p1 * 3f - p0 - p2 * 3f + p3) * t3) * 0.5f;
        }

        /// <summary>Even spacing along the polyline, so equal steps of s are equal ground.</summary>
        private static Vec2[] Resample(Vec2[] pts, float pitch)
        {
            var result = new List<Vec2> { pts[0] };
            float carried = 0f;

            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vec2 a = pts[i], b = pts[i + 1];
                float span = Distance(a, b);
                if (span <= 1e-6f) continue;

                float travelled = pitch - carried;
                while (travelled <= span)
                {
                    result.Add(Vec2.Lerp(a, b, travelled / span));
                    travelled += pitch;
                }
                carried = span - (travelled - pitch);
            }

            var last = pts[pts.Length - 1];
            if (Distance(result[result.Count - 1], last) > 1e-4f) result.Add(last);
            return result.ToArray();
        }

        /// <summary>
        /// The one square root in Core. Allowed, and not a loophole: IEEE-754 requires Sqrt to
        /// be correctly rounded, so unlike Sin it is bit-identical on every runtime. See
        /// CoreDeterminismTests, which permits it by name and forbids the rest.
        /// </summary>
        private static float Distance(Vec2 a, Vec2 b)
        {
            var d = b - a;
            return (float)Math.Sqrt(d.X * d.X + d.Y * d.Y);
        }

        /// <summary>The dense index at or before arc length s, by bisection.</summary>
        private int IndexAt(float s)
        {
            int lo = 0, hi = _cumulative.Length - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) / 2;
                if (_cumulative[mid] <= s) lo = mid; else hi = mid;
            }
            return lo;
        }

        private float Clamp(float s) => s < 0f ? 0f : (s > Length ? Length : s);

        public Vec2 PointAt(float s)
        {
            s = Clamp(s);
            if (IsStraightAxisAligned)
                return new Vec2(_from.X + _tangent.X * s, _from.Y + _tangent.Y * s);

            int i = IndexAt(s);
            float span = _cumulative[i + 1] - _cumulative[i];
            if (span <= 1e-6f) return _dense[i];
            return Vec2.Lerp(_dense[i], _dense[i + 1], (s - _cumulative[i]) / span);
        }

        public Vec2 TangentAt(float s)
        {
            if (IsStraightAxisAligned) return _tangent;

            int i = IndexAt(Clamp(s));
            var d = _dense[i + 1] - _dense[i];
            float len = Distance(_dense[i], _dense[i + 1]);
            return len <= 1e-6f ? new Vec2(1f, 0f) : new Vec2(d.X / len, d.Y / len);
        }

        /// <summary>
        /// The right-hand side of travel, which is what a lane offset is measured along.
        ///
        /// (-y, x) rather than (y, -x), and it is not a convention picked here: Headings.Side
        /// already derives it that way from village coordinates being x east, y south. Facing
        /// north (0,-1) the right is east (1,0). Getting it backwards puts every lane in the
        /// oncoming carriageway.
        /// </summary>
        public Vec2 NormalAt(float s)
        {
            var t = TangentAt(s);
            return new Vec2(-t.Y, t.X);
        }

        /// <summary>
        /// How far along this path you have travelled when you reach a given coordinate on one
        /// axis - the inverse of asking PointAt for its x or its y.
        ///
        /// THE ONE CONVERSION, because it was open-coded as `along - line.From` in three places
        /// and that arithmetic is wrong. It assumes arc length grows the same way the declared
        /// axis does, which is true of a straight road - RoadLine always builds Path From->To -
        /// and false of a curve, which is Through(Points) in DECLARED order. The county's chained
        /// segments are declared in whichever direction the surveyor walked them, so park,
        /// greenwood, alley13 and alley18 all run right to left. Every one of the three callers
        /// was reading the wrong end of those roads: LaneGraph cut their lanes there, LaneGraph
        /// classified their turns off the tangent there, and CityTraffic DREW THE CARS there.
        ///
        /// Not clamped. A lane runs thirty metres past the edge of the map so traffic arrives
        /// from off-stage, so a legitimate `along` can sit outside the path's own span; beyond
        /// either end this carries on at that end's rate, which is what PointAt's caller then
        /// draws along the tangent.
        ///
        /// ⚠ A COORDINATE IS NOT ALWAYS ONE PLACE. Where a road runs square across the axis it is
        /// parameterised by, every point on that stretch answers to the same number - alley21's
        /// overall run is east-west, so it goes by x, and it leaves Benton heading due north.
        /// This walks the dense samples in order and takes the FIRST bracket, so such a stretch
        /// resolves to its near end, which for an alley mouth is the mouth. That is asserted, on
        /// that alley, by `ArcAtFindsTheMouthOfAnAlleyThatLeavesItsStreetSquare`.
        ///
        /// A road that genuinely doubles back to the same coordinate later has two answers and
        /// gets the first. None on this map does. The real fix for both is an arc-length
        /// parameter on `LaneSegment` rather than a coordinate on the road's declared axis - a
        /// change to what the recorded segment checksum pins, so not this one.
        /// </summary>
        public float ArcAt(float along, bool northSouth)
        {
            if (IsStraightAxisAligned)
            {
                float from = northSouth ? _from.Y : _from.X;
                float to = northSouth ? _to.Y : _to.X;
                float span = to - from;

                // The axis that does not move along this path. Every point on it answers, so the
                // start is as good as any and is the one that keeps a caller's arithmetic sane.
                if (span == 0f) return 0f;

                return (along - from) / span * Length;
            }

            float previous = northSouth ? _dense[0].Y : _dense[0].X;
            for (int i = 1; i < _dense.Length; i++)
            {
                float axis = northSouth ? _dense[i].Y : _dense[i].X;
                if (axis != previous)
                {
                    float lo = previous < axis ? previous : axis;
                    float hi = previous < axis ? axis : previous;
                    if (along >= lo && along <= hi)
                    {
                        float t = (along - previous) / (axis - previous);
                        return _cumulative[i - 1] + (_cumulative[i] - _cumulative[i - 1]) * t;
                    }
                }
                previous = axis;
            }

            // Past one end or the other. Whichever the coordinate is nearer to.
            float atStart = northSouth ? _dense[0].Y : _dense[0].X;
            float atEnd = northSouth ? _dense[_dense.Length - 1].Y : _dense[_dense.Length - 1].X;
            float toStart = along - atStart, toEnd = along - atEnd;
            if (toStart < 0f) toStart = -toStart;
            if (toEnd < 0f) toEnd = -toEnd;

            float edge = toEnd < toStart ? Length : 0f;
            float overshoot = toEnd < toStart ? along - atEnd : along - atStart;
            var heading = TangentAt(edge);
            float rate = northSouth ? heading.Y : heading.X;

            // Running square across this axis at the end: no distance along the path takes you
            // any further along it, so the end itself is the honest answer.
            if (rate > -1e-6f && rate < 1e-6f) return edge;

            return edge + overshoot / rate;
        }

        /// <summary>
        /// The nearest point on the centre line: how far along, and how far to the side.
        ///
        /// Lateral is SIGNED - positive on the road's right - because a lane needs the side and
        /// RoadNetwork.At only needs the magnitude. Returning the absolute value would serve the
        /// caller that matters least.
        /// </summary>
        public (float S, float Lateral) Project(Vec2 p)
        {
            if (IsStraightAxisAligned)
            {
                var straightD = p - _from;
                float straightS = Clamp(straightD.X * _tangent.X + straightD.Y * _tangent.Y);
                var straightOff = p - PointAt(straightS);
                var straightN = NormalAt(straightS);
                return (straightS, straightOff.X * straightN.X + straightOff.Y * straightN.Y);
            }

            // Nearest over every dense segment. Linear in the number of samples and called only
            // by RoadNetwork.At, which is not on a per-frame path.
            float bestS = 0f, bestD2 = float.MaxValue;
            for (int i = 0; i < _dense.Length - 1; i++)
            {
                Vec2 a = _dense[i], b = _dense[i + 1];
                var ab = b - a;
                float span2 = ab.LengthSquared;
                if (span2 <= 1e-9f) continue;

                var ap = p - a;
                float t = (ap.X * ab.X + ap.Y * ab.Y) / span2;
                t = t < 0f ? 0f : (t > 1f ? 1f : t);

                var on = Vec2.Lerp(a, b, t);
                float d2 = (p - on).LengthSquared;
                if (d2 >= bestD2) continue;

                bestD2 = d2;
                bestS = _cumulative[i] + (_cumulative[i + 1] - _cumulative[i]) * t;
            }

            var offset = p - PointAt(bestS);
            var normal = NormalAt(bestS);
            return (bestS, offset.X * normal.X + offset.Y * normal.Y);
        }

        public override string ToString() =>
            (IsStraightAxisAligned ? "straight " : "curved ") + _from + ".." + _to
            + " (" + Length + "m)";
    }
}
