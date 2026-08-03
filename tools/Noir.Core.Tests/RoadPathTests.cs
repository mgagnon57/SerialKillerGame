using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The centreline primitive. Straight first, and exactly - every road in the real town is
    /// straight, so this is the case that must not move by a millimetre.
    /// </summary>
    [TestFixture]
    public class RoadPathTests
    {
        // Chicago Street as the map declares it today: 30m wide, x=750, the full height.
        private static RoadPath Chicago() =>
            RoadPath.Straight(new Vec2(750f, 0f), new Vec2(750f, 2400f));

        [Test]
        public void AStraightRunKnowsItIsStraight()
        {
            Assert.That(Chicago().IsStraightAxisAligned, Is.True);
            Assert.That(Chicago().Length, Is.EqualTo(2400f));
        }

        [Test]
        public void PointAtIsExactAlongAnAxis()
        {
            // EXACT, not approximately. A straight axis-aligned road must return the declared
            // coordinate bit for bit, because that is what the existing city is built from and
            // what every snapshot was rendered against.
            var path = Chicago();
            Assert.That(path.PointAt(0f).X, Is.EqualTo(750f));
            Assert.That(path.PointAt(0f).Y, Is.EqualTo(0f));
            Assert.That(path.PointAt(1335f).X, Is.EqualTo(750f));
            Assert.That(path.PointAt(1335f).Y, Is.EqualTo(1335f));
            Assert.That(path.PointAt(2400f).Y, Is.EqualTo(2400f));
        }

        [Test]
        public void PointAtClampsRatherThanRunningOffTheEnd()
        {
            var path = Chicago();
            Assert.That(path.PointAt(-50f).Y, Is.EqualTo(0f));
            Assert.That(path.PointAt(9999f).Y, Is.EqualTo(2400f));
        }

        [Test]
        public void TangentPointsTheWayTheRoadWasDeclared()
        {
            var t = Chicago().TangentAt(500f);
            Assert.That(t.X, Is.EqualTo(0f));
            Assert.That(t.Y, Is.EqualTo(1f), "declared north to south, so travel is +y");
        }

        [Test]
        public void NormalIsTheRightHandSideTheRestOfCoreAlreadyMeansByIt()
        {
            // Headings.Side derives the right of travel (dx,dy) as (-dy,dx): village coordinates
            // are x east, y south, the same handedness as a screen. Facing south, right is west.
            var n = Chicago().NormalAt(500f);
            Assert.That(n.X, Is.EqualTo(-1f));
            Assert.That(n.Y, Is.EqualTo(0f));
        }

        [Test]
        public void AnEastWestRunNormalsSouth()
        {
            // Facing east, right is south - the greater y. This is the pairing Headings.Side
            // spells out, and getting it backwards would put every lane on the wrong side.
            var attica = RoadPath.Straight(new Vec2(0f, 1335f), new Vec2(2100f, 1335f));
            var n = attica.NormalAt(100f);
            Assert.That(n.X, Is.EqualTo(0f));
            Assert.That(n.Y, Is.EqualTo(1f));
        }

        [Test]
        public void ALaneOffsetIsTheNormalTimesTheDistance()
        {
            // The expression that replaces every `line.Centre +/- offset` in the codebase.
            var path = Chicago();
            var lane = path.PointAt(1000f) + path.NormalAt(1000f) * 6f;
            Assert.That(lane.X, Is.EqualTo(744f), "6m to the right of a southbound road is west");
            Assert.That(lane.Y, Is.EqualTo(1000f));
        }

        [Test]
        public void ProjectFindsHowFarAlongAndHowFarAside()
        {
            var (s, lateral) = Chicago().Project(new Vec2(760f, 400f));
            Assert.That(s, Is.EqualTo(400f));
            // The point is 10m EAST of a southbound road, and east is its left, so lateral is
            // negative. Signed, not absolute - RoadNetwork.At needs the magnitude but a lane
            // needs the side.
            Assert.That(lateral, Is.EqualTo(-10f));
        }

        [Test]
        public void ProjectClampsToTheEndsOfTheRun()
        {
            var (s, _) = Chicago().Project(new Vec2(750f, -200f));
            Assert.That(s, Is.EqualTo(0f));
        }

        // ---- the curve ----------------------------------------------------------------

        /// <summary>A quarter-circle-ish bend, declared coarsely the way a survey way is.</summary>
        private static RoadPath Bend() => RoadPath.Through(new[]
        {
            new Vec2(0f, 0f), new Vec2(0f, 100f), new Vec2(20f, 180f), new Vec2(60f, 240f),
        });

        [Test]
        public void ThroughTwoAxisAlignedPointsIsStillTheExactStraightCase()
        {
            // The short circuit is the whole zero-regression argument, so it is asserted at the
            // door RoadLine will actually come in through, not only at RoadPath.Straight.
            var path = RoadPath.Through(new[] { new Vec2(750f, 0f), new Vec2(750f, 2400f) });
            Assert.That(path.IsStraightAxisAligned, Is.True);
            Assert.That(path.Length, Is.EqualTo(2400f));
            Assert.That(path.PointAt(1335f).X, Is.EqualTo(750f));
        }

        [Test]
        public void ACurveKnowsItIsNotStraight()
        {
            Assert.That(Bend().IsStraightAxisAligned, Is.False);
        }

        [Test]
        public void TheCurveStartsAndEndsOnItsDeclaredEndPoints()
        {
            var path = Bend();
            Assert.That(path.PointAt(0f).X, Is.EqualTo(0f).Within(0.001f));
            Assert.That(path.PointAt(0f).Y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(path.PointAt(path.Length).X, Is.EqualTo(60f).Within(0.5f));
            Assert.That(path.PointAt(path.Length).Y, Is.EqualTo(240f).Within(0.5f));
        }

        [Test]
        public void ArcLengthIsAtLeastTheStraightLineAndNotAbsurdlyMore()
        {
            // A curve between the same endpoints is longer than the chord and, for a bend this
            // gentle, not dramatically so. Pins the arc-length table against both the classic
            // failures: summing nothing, and summing the resample points twice.
            var path = Bend();
            Assert.That(path.Length, Is.GreaterThan(240f));
            Assert.That(path.Length, Is.LessThan(330f));
        }

        [Test]
        public void WalkingTheCurveMovesAtOneMetrePerMetre()
        {
            // What arc-length parameterisation MEANS, and the thing a naive t-in-0..1 spline
            // gets wrong: equal steps of s must be equal distances on the ground, or a car
            // driving the curve speeds up and slows down through the bend.
            var path = Bend();
            for (float s = 0f; s + 10f <= path.Length; s += 10f)
            {
                var a = path.PointAt(s);
                var b = path.PointAt(s + 10f);
                float step = (b - a).LengthSquared;
                Assert.That(step, Is.EqualTo(100f).Within(6f), "uneven step at s=" + s);
            }
        }

        [Test]
        public void TheTangentIsAlwaysAUnitVector()
        {
            var path = Bend();
            for (float s = 0f; s <= path.Length; s += 5f)
                Assert.That(path.TangentAt(s).LengthSquared, Is.EqualTo(1f).Within(0.001f),
                            "tangent not unit at s=" + s);
        }

        [Test]
        public void TheTangentTurnsThroughTheBendRatherThanJumping()
        {
            // Smoothness is the reason for the spline. Consecutive tangents must stay close;
            // a kink would show as a sudden drop in the dot product.
            var path = Bend();
            for (float s = 0f; s + 2f <= path.Length; s += 2f)
            {
                var a = path.TangentAt(s);
                var b = path.TangentAt(s + 2f);
                Assert.That(a.X * b.X + a.Y * b.Y, Is.GreaterThan(0.99f), "kink at s=" + s);
            }
        }

        [Test]
        public void ProjectRoundTripsAnywhereOnTheCurve()
        {
            var path = Bend();
            for (float s = 0f; s <= path.Length; s += 7f)
            {
                var (back, lateral) = path.Project(path.PointAt(s));
                Assert.That(back, Is.EqualTo(s).Within(1.0f), "s did not round trip at " + s);
                Assert.That(lateral, Is.EqualTo(0f).Within(0.5f), "a point ON the curve is not aside");
            }
        }

        [Test]
        public void ProjectPutsTheRightHandSideOnTheRight()
        {
            var path = Bend();
            var at = path.PointAt(50f);
            var n = path.NormalAt(50f);
            var (_, lateral) = path.Project(at + n * 8f);
            Assert.That(lateral, Is.EqualTo(8f).Within(0.5f), "offset along the normal must read positive");
        }

        [Test]
        public void SmoothKeepsEveryDeclaredPointOnTheCurve()
        {
            // MapFeatures.Smoothed promises exactly this - "every original point is still on the
            // curve exactly where it was" - and Task 5 hands the railway to this code. The rail
            // bed is already built and committed against that promise.
            var declared = new[]
            {
                new Vec2(0f, 0f), new Vec2(0f, 100f), new Vec2(20f, 180f), new Vec2(60f, 240f),
            };
            var smoothed = RoadPath.Smooth(declared);

            Assert.That(smoothed.Length, Is.EqualTo((declared.Length - 1) * RoadPath.SmoothSteps + 1));
            foreach (var p in declared)
            {
                bool found = false;
                foreach (var q in smoothed)
                    if ((q - p).LengthSquared < 1e-6f) { found = true; break; }
                Assert.That(found, Is.True, "declared point " + p + " is not on the smoothed curve");
            }
        }

        [Test]
        public void SmoothReproducesTheRailwaysOwnCatmullRomToTheBit()
        {
            // The one value that proves this is the SAME curve MapFeatures.Smoothed draws, not
            // merely a similar one. Catmull-Rom at t=0.5 on the first span of the fixture above,
            // with the first point clamped as its own neighbour:
            //   0.5 * (2*p1 + (-p0+p2)*t + (2*p0-5*p1+4*p2-p3)*t^2 + (-p0+3*p1-3*p2+p3)*t^3)
            // with p0=p1=(0,0), p2=(0,100), p3=(20,180) at t=0.5 gives x = -1.25, y = 45.
            // (Verified three independent ways - direct substitution, the standard
            // blending-function form, and the Hermite/tangent form - all of which agree with
            // what the verbatim ported formula actually computes. See task-4-report.md.)
            var smoothed = RoadPath.Smooth(new[]
            {
                new Vec2(0f, 0f), new Vec2(0f, 100f), new Vec2(20f, 180f), new Vec2(60f, 240f),
            });

            Assert.That(smoothed[2].X, Is.EqualTo(-1.25f).Within(1e-4f));
            Assert.That(smoothed[2].Y, Is.EqualTo(45f).Within(1e-4f));
        }
    }
}
