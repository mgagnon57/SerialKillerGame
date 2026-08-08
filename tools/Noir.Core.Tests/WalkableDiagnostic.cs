using System;
using System.Collections.Generic;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Sim;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// Where the walkable map is cut, and who is on the wrong side of the cut.
    ///
    /// The town's walkable space is in two pieces, down from four, and thirteen citizens are
    /// stranded in the smaller one. StrandedSurvey answers this at runtime and needs Play mode
    /// to do it - but the question is pure Core: a flood fill over a TileGrid. So it can be
    /// asked headlessly, in a second, as many times as it takes to close the gap.
    ///
    /// [Explicit] so it stays out of normal runs. To look:
    ///
    ///     dotnet test -c Release tools/Noir.Core.Tests --filter "Name=PrintWalkableRegions" ^
    ///                 -l "console;verbosity=detailed"
    ///
    /// WHAT TO READ IT FOR: the smaller region's bounding box says WHERE the cut is, and its
    /// size says what kind of cut. A region of a few hundred tiles is an enclosed yard or a
    /// building interior whose door is walled in. A region of thousands is a whole quarter of
    /// the town hanging off a bridge or a crossing that is not walkable.
    /// </summary>
    [TestFixture, Explicit("Diagnostic - prints where the walkable map is cut, asserts nothing")]
    public class WalkableDiagnostic
    {
        [Test]
        public void PrintWalkableRegions()
        {
            // THE REAL TOWN, NOT THE TEST FIXTURE. Village.World is village.txt - the old
            // 210x120 the fixture village map every unit test in this project is built on - and the map
            // with the cut in it is city.txt, which is what VillageHost.MapFile points at.
            // Run against the fixture this reported one region and no stranded doors, which is
            // perfectly true and answers nothing.
            var world = WorldBuilder.Build(VillageParser.Parse(TestContent.Read("city.txt")));
            var grid = world.Grid;
            var regions = new WalkableRegions(grid);

            Console.WriteLine();
            Console.WriteLine("=================================================================");
            Console.WriteLine("  THE WALKABLE MAP: {0} separate area(s) over {1} x {2}",
                              regions.RegionCount, grid.Width, grid.Height);
            Console.WriteLine("=================================================================");

            int largest = regions.Largest();

            // Bounding box and a sample tile per region, gathered in one pass over the grid.
            var minX = new Dictionary<int, int>();
            var maxX = new Dictionary<int, int>();
            var minY = new Dictionary<int, int>();
            var maxY = new Dictionary<int, int>();
            var sample = new Dictionary<int, Tile>();

            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                int r = regions.RegionAt(x, y);
                if (r == WalkableRegions.None) continue;

                if (!minX.ContainsKey(r))
                {
                    minX[r] = x; maxX[r] = x; minY[r] = y; maxY[r] = y;
                    sample[r] = new Tile(x, y);
                }
                else
                {
                    if (x < minX[r]) minX[r] = x;
                    if (x > maxX[r]) maxX[r] = x;
                    if (y < minY[r]) minY[r] = y;
                    if (y > maxY[r]) maxY[r] = y;
                }
            }

            Console.WriteLine();
            Console.WriteLine("  {0,3}  {1,10}  {2,-24}  {3}", "id", "tiles", "bounding box", "");
            for (int r = 0; r < regions.RegionCount; r++)
            {
                if (!minX.ContainsKey(r)) continue;
                Console.WriteLine("  {0,3}  {1,10:N0}  {2,4},{3,-4} to {4,4},{5,-4}  {6}",
                                  r, regions.SizeOf(r),
                                  minX[r], minY[r], maxX[r], maxY[r],
                                  r == largest ? "<- the town" : "** CUT OFF");
            }

            // ---- who and what is on the wrong side ----
            Console.WriteLine();
            Console.WriteLine("  ---- doors not in the main area ----");
            int strandedDoors = 0;
            foreach (var place in world.AllPlaces)
            {
                if (place == null || place.Door.X <= 0 || place.Door.Y <= 0) continue;
                int r = regions.RegionAt(place.Door);
                if (r == largest) continue;

                strandedDoors++;
                Console.WriteLine("     {0,4},{1,-4}  region {2,-4} ({3,6:N0} tiles)  {4}",
                                  place.Door.X, place.Door.Y,
                                  r == WalkableRegions.None ? "none" : r.ToString(),
                                  r == WalkableRegions.None ? 0 : regions.SizeOf(r),
                                  place.Name);
            }
            if (strandedDoors == 0) Console.WriteLine("     (none - every door is in the town)");

            // ---- the shape of the cut ----
            //
            // For each cut-off region, walk its edge and count what is standing next to it. The
            // answer names the thing doing the cutting: a region ringed by Wall is an interior,
            // one ringed by Water needs a crossing, one ringed by nothing walkable at all is
            // ground that simply never got joined up.
            for (int r = 0; r < regions.RegionCount; r++)
            {
                if (r == largest || !minX.ContainsKey(r)) continue;

                Console.WriteLine();
                Console.WriteLine("  ---- what surrounds region {0} ----", r);

                var touching = new Dictionary<Terrain, int>();
                int nextToMain = 0;

                for (int y = Math.Max(0, minY[r] - 1); y <= Math.Min(grid.Height - 1, maxY[r] + 1); y++)
                for (int x = Math.Max(0, minX[r] - 1); x <= Math.Min(grid.Width - 1, maxX[r] + 1); x++)
                {
                    if (regions.RegionAt(x, y) != r) continue;

                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= grid.Width || ny >= grid.Height) continue;

                        int nr = regions.RegionAt(nx, ny);
                        if (nr == r) continue;
                        if (nr == largest) { nextToMain++; continue; }
                        if (nr != WalkableRegions.None) continue;

                        var t = grid.TerrainAt(nx, ny);
                        touching[t] = touching.TryGetValue(t, out int n) ? n + 1 : 1;
                    }
                }

                foreach (var kv in touching)
                    Console.WriteLine("     {0,-12} {1,6}", kv.Key, kv.Value);

                // THE MOST USEFUL NUMBER HERE. If a cut-off region has tiles DIAGONALLY next to
                // the main one, the two are a corner apart and the corner rule is what separates
                // them - a very different repair from "these are a hundred metres apart".
                Console.WriteLine("     touching the town diagonally or otherwise: {0}", nextToMain);
                Console.WriteLine("     a tile in it: {0},{1}", sample[r].X, sample[r].Y);
            }

            Console.WriteLine();
            Console.WriteLine("  walkable tiles in total : {0:N0}", TotalWalkable(regions, grid));
            Console.WriteLine("  in the town             : {0:N0}", regions.SizeOf(largest));
        }

        private static int TotalWalkable(WalkableRegions regions, TileGrid grid)
        {
            int n = 0;
            for (int r = 0; r < regions.RegionCount; r++) n += regions.SizeOf(r);
            return n;
        }
    }

    /// <summary>
    /// The real town is one walkable piece, and every door opens onto it.
    ///
    /// NOT [Explicit] - this is the guard, and it lives beside the diagnostic above because when
    /// it goes red that diagnostic is the next thing to run. It names the doors it found cut off,
    /// which is most of the answer on its own.
    ///
    /// Worth its 765 milliseconds. The last time this was wrong it was a door written one tile
    /// inside its own wall instead of in it - a shop sealed shut, its interior a walkable island
    /// of 129 tiles that nothing could reach. Nobody could see it on the map, the town looked
    /// perfectly normal, and the only symptom was that anybody sent there searched 1.26 million
    /// nodes failing to find a way in. That was the town's stutter.
    /// </summary>
    [TestFixture]
    public class WalkableWholeness
    {
        [Test]
        public void TheTownIsAllOnePiece()
        {
            var world = WorldBuilder.Build(VillageParser.Parse(TestContent.Read("city.txt")));
            var regions = new WalkableRegions(world.Grid);
            int largest = regions.Largest();

            var cutOff = new List<string>();
            foreach (var place in world.AllPlaces)
            {
                if (place == null || place.Door.X <= 0 || place.Door.Y <= 0) continue;
                if (regions.RegionAt(place.Door) == largest) continue;
                cutOff.Add($"'{place.Name}' door {place.Door.X},{place.Door.Y}");
            }

            Assert.That(cutOff, Is.Empty,
                "these doors cannot be reached from the town. A door has to sit IN the wall, "
              + "not one tile inside it - compare the x of a door against its place's x plus "
              + "width minus one. Run PrintWalkableRegions for where the cut is.");

            Assert.That(regions.RegionCount, Is.EqualTo(1),
                $"the walkable map is in {regions.RegionCount} pieces. Every door is reachable, "
              + "so this is ground rather than a building - run PrintWalkableRegions for its "
              + "bounding box and what surrounds it.");
        }
    }
}
