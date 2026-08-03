using System;
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

        private RoadPath(Vec2 from, Vec2 to)
        {
            _from = from;
            _to = to;
            IsStraightAxisAligned = true;

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

        private float Clamp(float s) => s < 0f ? 0f : (s > Length ? Length : s);

        public Vec2 PointAt(float s)
        {
            s = Clamp(s);
            // One of these two terms is always zero, so the surviving coordinate is the
            // declared one untouched: no drift on the cross axis, ever.
            return new Vec2(_from.X + _tangent.X * s, _from.Y + _tangent.Y * s);
        }

        public Vec2 TangentAt(float s) => _tangent;

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
        /// The nearest point on the centre line: how far along, and how far to the side.
        ///
        /// Lateral is SIGNED - positive on the road's right - because a lane needs the side and
        /// RoadNetwork.At only needs the magnitude. Returning the absolute value would serve the
        /// caller that matters least.
        /// </summary>
        public (float S, float Lateral) Project(Vec2 p)
        {
            var d = p - _from;
            float s = Clamp(d.X * _tangent.X + d.Y * _tangent.Y);

            var on = PointAt(s);
            var off = p - on;
            var n = NormalAt(s);
            return (s, off.X * n.X + off.Y * n.Y);
        }

        public override string ToString() =>
            (IsStraightAxisAligned ? "straight " : "curved ") + _from + ".." + _to
            + " (" + Length + "m)";
    }
}
