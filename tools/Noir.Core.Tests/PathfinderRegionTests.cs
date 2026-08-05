using System.Collections.Generic;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Sim;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// That the search actually USES the region map, and that the map cannot outlive an edit.
    ///
    /// The second half matters more than it looks. The world builder makes a Pathfinder and
    /// then keeps carving doorways, so a region map captured too early would refuse every
    /// journey through a door cut after it - a town where nobody travels and no error is ever
    /// logged. That is a far worse bug than the stutter this was written to fix.
    /// </summary>
    public class PathfinderRegionTests
    {
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
        public void ASealedOffDestinationIsRefusedWithoutSearching()
        {
            var grid = Draw(
                "...#...",
                "...#...",
                "...#...");

            var finder = new Pathfinder(grid);
            var path = new List<Tile>();

            var outcome = finder.FindPath(new Tile(0, 1), new Tile(6, 1), path);

            Assert.That(outcome, Is.EqualTo(PathOutcome.NoRouteExists));

            // The whole point: it did not go looking. Before the region map this cost a full
            // exhaustion of everything reachable, which on the real town was 1.26 million nodes.
            Assert.That(finder.LastNodesExamined, Is.Zero);
        }

        [Test]
        public void AReachableDestinationStillWorks()
        {
            var grid = Draw(
                "...#...",
                ".......",
                "...#...");

            var finder = new Pathfinder(grid);
            var path = new List<Tile>();

            Assert.That(finder.FindPath(new Tile(0, 1), new Tile(6, 1), path),
                        Is.EqualTo(PathOutcome.Found));
            Assert.That(path, Is.Not.Empty);
            Assert.That(path[path.Count - 1], Is.EqualTo(new Tile(6, 1)));
        }

        [Test]
        public void CuttingADoorAfterTheFinderExistsOpensTheRoute()
        {
            var grid = Draw(
                "...#...",
                "...#...",
                "...#...");

            var finder = new Pathfinder(grid);
            var path = new List<Tile>();

            Assert.That(finder.FindPath(new Tile(0, 1), new Tile(6, 1), path),
                        Is.EqualTo(PathOutcome.NoRouteExists),
                        "sealed to begin with");

            // The builder carves a doorway through the wall, exactly as it does when it places
            // a real one, long after this Pathfinder was constructed.
            grid.Set(3, 1, Terrain.Floor, TileFlags.Walkable);

            Assert.That(finder.FindPath(new Tile(0, 1), new Tile(6, 1), path),
                        Is.EqualTo(PathOutcome.Found),
                        "the region map has to notice the door, or nobody ever walks through it");
        }

        [Test]
        public void WallingADoorUpAfterTheFinderExistsClosesTheRoute()
        {
            var grid = Draw(
                "...#...",
                ".......",
                "...#...");

            var finder = new Pathfinder(grid);
            var path = new List<Tile>();

            Assert.That(finder.FindPath(new Tile(0, 1), new Tile(6, 1), path),
                        Is.EqualTo(PathOutcome.Found));

            grid.Set(3, 1, Terrain.Wall, TileFlags.BlocksSight);

            Assert.That(finder.FindPath(new Tile(0, 1), new Tile(6, 1), path),
                        Is.EqualTo(PathOutcome.NoRouteExists));
        }

        /// <summary>
        /// Repainting a tile without changing whether it can be walked on must NOT throw the
        /// region map away. The builder paints terrain constantly, and rebuilding a
        /// million-tile fill on every brush stroke would cost more than the bug did.
        /// </summary>
        [Test]
        public void RepaintingTerrainKeepsTheSameRegionMap()
        {
            var grid = Draw(
                "...",
                "...",
                "...");

            var finder = new Pathfinder(grid);
            var before = finder.Regions;

            grid.Set(1, 1, Terrain.Road, TileFlags.Walkable);

            Assert.That(finder.Regions, Is.SameAs(before));
        }
    }
}
