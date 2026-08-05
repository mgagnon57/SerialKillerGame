using System.Collections.Generic;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Sim;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The region map exists to refuse impossible journeys without searching for them. The
    /// cheap mistake is a fill more generous than the search - a route gets attempted and
    /// fails, costing time. The EXPENSIVE mistake is a fill meaner than the search, which
    /// refuses journeys that are perfectly walkable and leaves people standing in the street
    /// with nothing in the log to say why. Most of what is below is aimed at that second one.
    /// </summary>
    public class WalkableRegionsTests
    {
        /// <summary>Open floor with walls wherever the map says '#'. Row 0 is the top line.</summary>
        private static TileGrid Draw(params string[] rows)
        {
            var grid = new TileGrid(rows[0].Length, rows.Length);
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                {
                    bool wall = rows[y][x] == '#';
                    grid.Set(x, y, wall ? Terrain.Wall : Terrain.Grass,
                             wall ? TileFlags.BlocksSight : TileFlags.Walkable);
                }
            return grid;
        }

        [Test]
        public void AnOpenFieldIsOneRegion()
        {
            var regions = new WalkableRegions(Draw(
                "....",
                "....",
                "...."));

            Assert.That(regions.RegionCount, Is.EqualTo(1));
            Assert.That(regions.SizeOf(0), Is.EqualTo(12));
            Assert.That(regions.Connected(new Tile(0, 0), new Tile(3, 2)), Is.True);
        }

        [Test]
        public void AWallDownTheMiddleMakesTwo()
        {
            var regions = new WalkableRegions(Draw(
                "..#..",
                "..#..",
                "..#.."));

            Assert.That(regions.RegionCount, Is.EqualTo(2));
            Assert.That(regions.Connected(new Tile(0, 0), new Tile(4, 0)), Is.False);
            Assert.That(regions.Connected(new Tile(0, 0), new Tile(1, 2)), Is.True);
        }

        [Test]
        public void ADoorwayJoinsThemAgain()
        {
            var regions = new WalkableRegions(Draw(
                "..#..",
                ".....",     // the door
                "..#.."));

            Assert.That(regions.RegionCount, Is.EqualTo(1));
            Assert.That(regions.Connected(new Tile(0, 0), new Tile(4, 0)), Is.True);
        }

        [Test]
        public void AnUnwalkableTileBelongsToNoRegion()
        {
            var regions = new WalkableRegions(Draw(
                "...",
                ".#.",
                "..."));

            Assert.That(regions.RegionAt(new Tile(1, 1)), Is.EqualTo(WalkableRegions.None));
            Assert.That(regions.Connected(new Tile(1, 1), new Tile(0, 0)), Is.False);
        }

        /// <summary>
        /// The corner rule, which is the whole reason this cannot be a plain flood fill.
        ///
        /// Two open tiles touching only at a corner, with both shared orthogonal neighbours
        /// walled, are NOT connected - the search refuses to slip between the two walls, so
        /// the fill must refuse it too or it will promise a route nobody can walk.
        /// </summary>
        [Test]
        public void TilesTouchingOnlyAtABlockedCornerAreNotConnected()
        {
            var regions = new WalkableRegions(Draw(
                ".#",
                "#."));

            Assert.That(regions.RegionCount, Is.EqualTo(2));
            Assert.That(regions.Connected(new Tile(0, 0), new Tile(1, 1)), Is.False);
        }

        [Test]
        public void ADiagonalWithOneSideOpenIsConnected()
        {
            var regions = new WalkableRegions(Draw(
                "..",
                "#."));

            Assert.That(regions.RegionCount, Is.EqualTo(1));
            Assert.That(regions.Connected(new Tile(0, 0), new Tile(1, 1)), Is.True);
        }

        /// <summary>
        /// The property that actually matters, checked against the search itself rather than
        /// against my reading of it: on a map full of awkward corners, the regions agree with
        /// what A* can really do, for every pair of walkable tiles.
        /// </summary>
        [Test]
        public void TheRegionsAgreeWithTheSearchOnEveryPair()
        {
            // Two rings, one inside the other: open ground outside, a corridor between them,
            // and one tile sealed dead centre. Three areas that genuinely cannot reach each
            // other, which is the half of the behaviour a scattered handful of walls does not
            // test - the first version of this fixture left every pair reachable and proved
            // nothing, which is what the guard at the bottom is here to catch.
            var grid = Draw(
                ".........",
                ".#######.",
                ".#.....#.",
                ".#.###.#.",
                ".#.#.#.#.",
                ".#.###.#.",
                ".#.....#.",
                ".#######.",
                ".........");

            var regions = new WalkableRegions(grid);
            Assert.That(regions.RegionCount, Is.EqualTo(3),
                "outside, the corridor between the rings, and the sealed middle");
            var finder = new Pathfinder(grid);
            var scratch = new List<Tile>();

            int checkedPairs = 0, connected = 0;
            for (int ay = 0; ay < grid.Height; ay++)
            for (int ax = 0; ax < grid.Width; ax++)
            {
                var a = new Tile(ax, ay);
                if (!grid.IsWalkable(a)) continue;

                for (int by = 0; by < grid.Height; by++)
                for (int bx = 0; bx < grid.Width; bx++)
                {
                    var b = new Tile(bx, by);
                    if (!grid.IsWalkable(b)) continue;

                    var outcome = finder.FindPath(a, b, scratch);
                    bool searchFoundOne = outcome == PathOutcome.Found;

                    // GaveUp would make this comparison meaningless - the search would be
                    // saying "I do not know", not "there is none".
                    Assert.That(outcome, Is.Not.EqualTo(PathOutcome.GaveUp),
                        $"search ran out of budget on {a} -> {b}, which this map is far too "
                        + "small to justify");

                    Assert.That(regions.Connected(a, b), Is.EqualTo(searchFoundOne),
                        $"regions and search disagree about {a} -> {b}");

                    checkedPairs++;
                    if (searchFoundOne) connected++;
                }
            }

            // A map where everything happened to be reachable would pass the loop above while
            // proving nothing about the interesting half of the behaviour.
            Assert.That(checkedPairs, Is.GreaterThan(1000));
            Assert.That(connected, Is.LessThan(checkedPairs),
                "this fixture is supposed to contain unreachable pairs");
        }

        [Test]
        public void LargestFindsTheTownRatherThanTheOuthouse()
        {
            var regions = new WalkableRegions(Draw(
                "....#.",
                "....#.",
                "....#."));

            int largest = regions.Largest();
            Assert.That(regions.SizeOf(largest), Is.EqualTo(12));
            Assert.That(regions.RegionCount, Is.EqualTo(2));
        }
    }
}
