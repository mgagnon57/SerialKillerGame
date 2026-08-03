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
    }
}
