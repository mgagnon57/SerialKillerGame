using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// Guards the fault that made DowntownFromSanborn's own fix look broken: two rotated
    /// rectangles standing edge to edge, sharing a party wall with zero gap, are the terrace this
    /// game is meant to build - not an overlap. A first cut of Polygon.Overlaps flagged the shared
    /// edge as an intersection and rejected every storefront in the row.
    /// </summary>
    public class PolygonTests
    {
        [Test]
        public void TwoSquaresSharingAFullEdgeDoNotOverlap()
        {
            var a = new[] { new Tile(0, 0), new Tile(10, 0), new Tile(10, 10), new Tile(0, 10) };
            var b = new[] { new Tile(10, 0), new Tile(20, 0), new Tile(20, 10), new Tile(10, 10) };

            Assert.That(Polygon.Overlaps(a, b), Is.False,
                "a shared wall with zero gap is a terrace, not an overlap");
        }

        [Test]
        public void TwoRotatedRectanglesSharingAnAngledPartyWallDoNotOverlap()
        {
            // The exact shape of the bug: adjacent storefronts on a frontage cocked ~18 degrees
            // off true north, sharing one slanted edge exactly - what canting DowntownFromSanborn
            // to Chicago Street's real angle actually produces.
            var a = new[] { new Tile(0, 0), new Tile(10, 0), new Tile(13, 30), new Tile(3, 30) };
            var b = new[] { new Tile(10, 0), new Tile(20, 0), new Tile(23, 30), new Tile(13, 30) };

            Assert.That(Polygon.Overlaps(a, b), Is.False);
        }

        [Test]
        public void TwoSquaresSharingOnlyACornerDoNotOverlap()
        {
            var a = new[] { new Tile(0, 0), new Tile(10, 0), new Tile(10, 10), new Tile(0, 10) };
            var b = new[] { new Tile(10, 10), new Tile(20, 10), new Tile(20, 20), new Tile(10, 20) };

            Assert.That(Polygon.Overlaps(a, b), Is.False);
        }

        [Test]
        public void OverlappingSquaresDoOverlap()
        {
            var a = new[] { new Tile(0, 0), new Tile(10, 0), new Tile(10, 10), new Tile(0, 10) };
            var b = new[] { new Tile(5, 5), new Tile(15, 5), new Tile(15, 15), new Tile(5, 15) };

            Assert.That(Polygon.Overlaps(a, b), Is.True);
        }

        [Test]
        public void OneSquareFullyInsideAnotherOverlaps()
        {
            var outer = new[] { new Tile(0, 0), new Tile(20, 0), new Tile(20, 20), new Tile(0, 20) };
            var inner = new[] { new Tile(5, 5), new Tile(15, 5), new Tile(15, 15), new Tile(5, 15) };

            Assert.That(Polygon.Overlaps(outer, inner), Is.True);
        }

        [Test]
        public void DisjointSquaresDoNotOverlap()
        {
            var a = new[] { new Tile(0, 0), new Tile(10, 0), new Tile(10, 10), new Tile(0, 10) };
            var b = new[] { new Tile(100, 100), new Tile(110, 100), new Tile(110, 110), new Tile(100, 110) };

            Assert.That(Polygon.Overlaps(a, b), Is.False);
        }

        [Test]
        public void ByAOneTileGapIsNotAnOverlap()
        {
            var a = new[] { new Tile(0, 0), new Tile(10, 0), new Tile(10, 10), new Tile(0, 10) };
            var b = new[] { new Tile(11, 0), new Tile(21, 0), new Tile(21, 10), new Tile(11, 10) };

            Assert.That(Polygon.Overlaps(a, b), Is.False);
        }

        [Test]
        public void ByAOneTileOverlapIsAnOverlap()
        {
            var a = new[] { new Tile(0, 0), new Tile(10, 0), new Tile(10, 10), new Tile(0, 10) };
            var b = new[] { new Tile(9, 0), new Tile(19, 0), new Tile(19, 10), new Tile(9, 10) };

            Assert.That(Polygon.Overlaps(a, b), Is.True);
        }
    }
}
