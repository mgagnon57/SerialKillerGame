using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Sim;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// WHAT PRICING PRIVATE GROUND COSTS THE SEARCH, measured rather than guessed.
    ///
    /// Written on 2026-08-11 because the PlayMode gate's WhyAreThePeopleNotAnimating went from a
    /// 293 s baseline to 1373 s and a 900 s timeout in the run that first stamped Rossville's
    /// parcels into the grid, and the obvious suspect was the trespass multiplier: A*'s heuristic
    /// is scaled to TileGrid.TypicalMoveCost (1.3), so every step that really costs 4 x 1.3 = 5.2
    /// is a step the heuristic underestimates fivefold, and an A* with too small a heuristic stops
    /// aiming at the goal and starts expanding everything near it.
    ///
    /// This measures that claim directly on a town-shaped grid instead of inferring it from a
    /// twenty-five minute run - a whole session was lost on 2026-08-08 to reading suite duration
    /// as if it were a frame rate, and this is the same mistake one level down.
    ///
    /// [Explicit], so it never joins the standing gate: it is an instrument, not a rule. Run it
    /// with  dotnet test -c Release --filter "Name=WhatDoesTrespassCostTheSearch"
    /// </summary>
    public class TrespassSearchCostDiagnostic
    {
        private const int Size = 420;
        private const int Block = 42;   // ten blocks each way
        private const int RoadHalf = 3;

        /// <summary>
        /// A gridded town: roads every 42 m at 6 m wide with a verge either side, and the ground
        /// between them divided into lots. Close enough to Rossville's plat for the search to
        /// behave the way it does there, and entirely synthetic so it needs no content.
        /// </summary>
        private static TileGrid Town(bool stampLots)
        {
            var grid = new TileGrid(Size, Size);

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int ox = x % Block, oy = y % Block;
                bool roadX = ox <= RoadHalf || ox >= Block - RoadHalf;
                bool roadY = oy <= RoadHalf || oy >= Block - RoadHalf;

                if (roadX || roadY)
                {
                    // The carriageway, with the outer metre of the corridor as verge.
                    bool edge = ox == RoadHalf || ox == Block - RoadHalf
                             || oy == RoadHalf || oy == Block - RoadHalf;
                    grid.Set(x, y, edge ? Terrain.Grass : Terrain.Road,
                             edge ? TileFlags.Walkable | TileFlags.Verge
                                  : TileFlags.Walkable | TileFlags.Road);
                    continue;
                }

                grid.Set(x, y, Terrain.Grass, TileFlags.Walkable);
                if (stampLots) grid.SetLot(x, y, 1 + (y / Block) * 16 + (x / Block));
            }
            return grid;
        }

        /// <summary>Deterministic, and not System.Random: the same journeys both times or the
        /// comparison measures the journeys instead of the change.</summary>
        private static int Next(ref uint s)
        {
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;
            return (int)(s % Size);
        }

        private static void Run(bool stampLots, out long nodes, out long ms, out int found)
        {
            var grid = Town(stampLots);
            var finder = new Pathfinder(grid);
            var path = new List<Tile>();

            uint seed = 1979;
            nodes = 0; found = 0;
            var watch = Stopwatch.StartNew();

            for (int i = 0; i < 200; i++)
            {
                var a = new Tile(Next(ref seed), Next(ref seed));
                var b = new Tile(Next(ref seed), Next(ref seed));
                if (!grid.IsWalkable(a) || !grid.IsWalkable(b)) continue;

                if (finder.FindPath(a, b, path) == PathOutcome.Found) found++;
                nodes += finder.LastNodesExamined;
            }

            watch.Stop();
            ms = watch.ElapsedMilliseconds;
        }

        [Test, Explicit, Category("Diagnostic")]
        public void WhatDoesTrespassCostTheSearch()
        {
            Run(false, out long plainNodes, out long plainMs, out int plainFound);
            Run(true,  out long lotNodes,   out long lotMs,   out int lotFound);

            TestContext.Out.WriteLine("---- what trespass costs the search ----");
            TestContext.Out.WriteLine($"  no lots stamped   {plainNodes,12:N0} nodes  {plainMs,6} ms  {plainFound} journeys");
            TestContext.Out.WriteLine($"  lots stamped      {lotNodes,12:N0} nodes  {lotMs,6} ms  {lotFound} journeys");
            if (plainNodes > 0)
                TestContext.Out.WriteLine($"  ratio             {(double)lotNodes / plainNodes,12:F2}x nodes, "
                                        + $"{(plainMs > 0 ? (double)lotMs / plainMs : 0),0:F2}x time");

            Assert.Pass();
        }
    }
}
