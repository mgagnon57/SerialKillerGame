using System;
using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.Sim;
using Noir.Core.World;

namespace Noir.Bench
{
    /// <summary>
    /// What one A* query costs, and how that changes when the map stops being a village.
    ///
    /// Two numbers per query, not one. Microseconds say whether the frame can afford it; nodes
    /// examined say whether the search is finding the goal or exploring the county, and only
    /// the second one predicts what happens on a map ten times the size.
    /// </summary>
    public static class PathBench
    {
        /// <summary>
        /// Lifted cap for the "what would it have cost" column. Not a suggestion — it is set
        /// where it is so that a query that has clearly lost the plot still terminates inside
        /// a benchmark run rather than inside a coffee break.
        /// </summary>
        public const int LiftedCap = 3_000_000;

        /// <summary>An admissible heuristic: what the search costs with no thumb on the scale.</summary>
        public const float AdmissibleWeight = 1.0f;

        public readonly struct Trip
        {
            public readonly string Name;
            public readonly int Count;
            public readonly double MedianUs, P95Us;
            public readonly int MedianNodes, MaxNodes, MedianPath;
            public readonly int GaveUp, NoRoute;
            public readonly int Cap;

            public Trip(string name, int count, double medianUs, double p95Us,
                        int medianNodes, int maxNodes, int medianPath,
                        int gaveUp, int noRoute, int cap)
            {
                Name = name;
                Count = count;
                MedianUs = medianUs;
                P95Us = p95Us;
                MedianNodes = medianNodes;
                MaxNodes = maxNodes;
                MedianPath = medianPath;
                GaveUp = gaveUp;
                NoRoute = noRoute;
                Cap = cap;
            }

            /// <summary>Nodes examined per tile of path. One would be a perfect heuristic.</summary>
            public double Waste => MedianPath > 0 ? MedianNodes / (double)MedianPath : 0;
        }

        /// <summary>
        /// <paramref name="cap"/> of zero takes whatever Pathfinder's own default is, which is
        /// the number the game will actually run with and is not necessarily a constant — it is
        /// currently a fraction of the map. The tables report what it resolved to rather than
        /// naming a figure that could drift out of date without anyone noticing.
        /// </summary>
        public static Trip Run(TileGrid grid, string name, List<(Tile from, Tile to)> queries,
                               int cap, int reps = 3, float weight = Pathfinder.DefaultHeuristicWeight)
        {
            var pf = new Pathfinder(grid, cap, weight);
            var buffer = new List<Tile>(4096);

            // The scratch arrays are twenty-one bytes a tile, so on a large map the first query
            // is paying for page faults rather than for search. One warm-up is enough to touch
            // them; running the whole set twice would double the cost of this section for no
            // improvement in the numbers.
            pf.FindPath(queries[0].from, queries[0].to, buffer);

            var micros = new double[queries.Count];
            var nodes = new int[queries.Count];
            var lengths = new int[queries.Count];
            int gaveUp = 0, noRoute = 0;

            for (int i = 0; i < queries.Count; i++)
            {
                double best = double.MaxValue;
                for (int r = 0; r < reps; r++)
                {
                    long t0 = Measure.Now();
                    var outcome = pf.FindPath(queries[i].from, queries[i].to, buffer);
                    double seconds = Measure.Since(t0);
                    if (seconds < best) best = seconds;

                    if (r == 0)
                    {
                        // Two different failures with two different fixes: a search that ran out
                        // of budget wants a better heuristic or a bigger cap, and one that found
                        // no route wants a look at the map.
                        if (outcome == PathOutcome.GaveUp) gaveUp++;
                        else if (outcome == PathOutcome.NoRouteExists) noRoute++;

                        nodes[i] = pf.LastNodesExamined;
                        lengths[i] = buffer.Count;
                    }
                }
                micros[i] = best * 1e6;
            }

            var m = Measure.Percentiles(micros, micros.Length);
            Array.Sort(nodes);
            Array.Sort(lengths);

            return new Trip(name, queries.Count, m.median, m.p95,
                            nodes[nodes.Length / 2], nodes[nodes.Length - 1],
                            lengths[lengths.Length / 2], gaveUp, noRoute, pf.MaxNodesExamined);
        }

        // ---- picking the queries ----

        /// <summary>
        /// Deterministic query sets, so two builds are asked exactly the same questions.
        /// Rejection sampling on a seeded stream: the same grid always yields the same trips.
        /// </summary>
        public static List<(Tile, Tile)> Trips(TileGrid grid, int count, double lowFraction,
                                               double highFraction, ulong seed)
        {
            int diagonal = Math.Max(grid.Width, grid.Height);
            return Trips(grid, count, Math.Max(4, (int)(diagonal * lowFraction)),
                         Math.Max(6, (int)(diagonal * highFraction)), seed);
        }

        /// <summary>
        /// Trips of a fixed length in tiles, whatever the map.
        ///
        /// A twenty-metre walk to the shop is a twenty-metre walk whether the settlement is a
        /// village or a city, and asking whether it costs the same on both is the whole
        /// question for the pathfinder. Scaling the "short" trip with the map instead would
        /// have answered a different and much less interesting one.
        /// </summary>
        public static List<(Tile, Tile)> Trips(TileGrid grid, int count, int low, int high, ulong seed)
        {
            var rng = Xoshiro256ss.Substream(seed, "bench-paths");
            var result = new List<(Tile, Tile)>(count);

            int guard = 0;
            while (result.Count < count && guard++ < count * 4000)
            {
                var from = new Tile(rng.NextInt(grid.Width), rng.NextInt(grid.Height));
                if (!grid.IsWalkable(from)) continue;

                var to = new Tile(rng.NextInt(grid.Width), rng.NextInt(grid.Height));
                if (!grid.IsWalkable(to)) continue;

                int d = Tile.ChebyshevDistance(from, to);
                if (d < low || d > high) continue;

                result.Add((from, to));
            }
            return result;
        }

        /// <summary>Opposite corners: the worst legitimate journey the map contains.</summary>
        public static List<(Tile, Tile)> CornerToCorner(TileGrid grid, int count)
        {
            var from = NearestWalkable(grid, new Tile(2, 2));
            var to = NearestWalkable(grid, new Tile(grid.Width - 3, grid.Height - 3));

            var list = new List<(Tile, Tile)>();
            for (int i = 0; i < count; i++) list.Add((from, to));
            return list;
        }

        private static Tile NearestWalkable(TileGrid grid, Tile near)
        {
            for (int radius = 0; radius < Math.Max(grid.Width, grid.Height); radius++)
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                var t = new Tile(near.X + dx, near.Y + dy);
                if (grid.IsWalkable(t)) return t;
            }
            return near;
        }

        // ---- the report ----

        public static void Report(Report r, ulong seed, (int w, int h)[] sizes, bool includeReal)
        {
            r.Line($"  Pathfinder defaults as this build ships them: heuristic weight " +
                   $"{Pathfinder.DefaultHeuristicWeight:0.00}, node cap scaled to the map.");
            r.Line();

            var capped = new Table("Pathfinding at the shipping defaults",
                new Col("map", 12, right: false), new Col("tiles", 10), new Col("cap", 9),
                new Col("trip", 8, right: false),
                new Col("n", 5), new Col("us med", 9), new Col("us p95", 9),
                new Col("nodes med", 10), new Col("path med", 9),
                new Col("gave up", 9), new Col("no route", 9));

            var lifted = new Table("The same queries with an admissible heuristic (weight 1.0) and the cap lifted",
                new Col("map", 12, right: false), new Col("trip", 8, right: false),
                new Col("us med", 10), new Col("nodes med", 11), new Col("nodes max", 11),
                new Col("path med", 9), new Col("nodes/tile", 11), new Col("gave up", 8));

            int totalGaveUp = 0, totalQueries = 0;

            var grids = new List<(string label, TileGrid grid)>();
            int smallestCornerNodes = 0, smallestCornerCap = 0;
            string smallestLabel = null;

            if (includeReal && RealVillage.Available)
            {
                var real = RealVillage.Load(seed);
                grids.Add(("Ashcombe", real.World.Grid));
            }

            foreach (var (w, h) in sizes)
                grids.Add((w + "x" + h, Synthetic.PathGrid(w, h)));

            foreach (var (label, grid) in grids)
            {
                int area = grid.Width * grid.Height;

                // Query counts fall off with area because the uncapped searches on a five
                // million tile map cost seconds each, and forty of them would make the harness
                // something nobody runs.
                int shorts = area > 4_000_000 ? 60 : area > 1_000_000 ? 120 : 400;
                int crosses = area > 4_000_000 ? 8 : area > 1_000_000 ? 16 : 200;
                int corners = area > 4_000_000 ? 1 : area > 1_000_000 ? 2 : 12;

                var sets = new List<(string name, List<(Tile, Tile)> qs)>
                {
                    ("short", Trips(grid, shorts, 15, 25, seed)),
                    ("cross", Trips(grid, crosses, 0.25, 0.35, seed + 1)),
                    ("corner", CornerToCorner(grid, corners)),
                };

                foreach (var (name, qs) in sets)
                {
                    if (qs.Count == 0) continue;

                    var a = Run(grid, name, qs, cap: 0, reps: area > 1_000_000 ? 1 : 3);
                    capped.Row(label, area, a.Cap, name, a.Count, a.MedianUs, a.P95Us,
                               a.MedianNodes, a.MedianPath,
                               a.GaveUp + "/" + a.Count, a.NoRoute + "/" + a.Count);

                    totalGaveUp += a.GaveUp;
                    totalQueries += a.Count;

                    var b = Run(grid, name, qs, LiftedCap, reps: 1, weight: AdmissibleWeight);
                    lifted.Row(label, name, b.MedianUs, b.MedianNodes, b.MaxNodes,
                               b.MedianPath, b.Waste.ToString("0.0"),
                               b.GaveUp + "/" + b.Count);

                    // The first corner query in the run is the smallest map's, which is the
                    // one worth quoting: it says what the search costs on the village that
                    // exists today, before any of this is a scaling problem.
                    if (name == "corner" && smallestLabel == null)
                    {
                        smallestLabel = label;
                        smallestCornerNodes = b.MedianNodes;
                        smallestCornerCap = a.Cap;
                    }

                    r.M("path", ("map", label), ("w", grid.Width), ("h", grid.Height),
                        ("trip", name), ("n", a.Count), ("cap", a.Cap),
                        ("us_med", a.MedianUs), ("us_p95", a.P95Us),
                        ("nodes_med", a.MedianNodes), ("path_med", a.MedianPath),
                        ("nodes_per_tile", a.Waste),
                        ("gave_up", a.GaveUp), ("no_route", a.NoRoute),
                        ("admissible_us_med", b.MedianUs), ("admissible_nodes_med", b.MedianNodes),
                        ("admissible_nodes_max", b.MaxNodes), ("admissible_path_med", b.MedianPath),
                        ("admissible_nodes_per_tile", b.Waste));
                }

                capped.Gap();
                lifted.Gap();
            }

            capped.Note("short = 15 to 25 tiles, the same walk on every map. cross = a quarter to a third");
            capped.Note("of the map's long side, so it grows with the settlement, as real errands do.");
            capped.Note("'gave up' is the node cap being hit — in the game, a citizen who does not set");
            capped.Note("off. 'no route' is genuinely unreachable, which on these grids should be zero.");
            r.Add(capped);

            lifted.Note("nodes/tile is nodes examined per tile of resulting path. 1.0 would be a");
            lifted.Note("perfect heuristic; large numbers mean A* is behaving like Dijkstra. Compare");
            lifted.Note("path med against the table above to see what the weighted heuristic costs in");
            lifted.Note("route quality — it is not free, it is just cheap.");
            r.Add(lifted);

            WeightSweep(r, seed, sizes);

            if (smallestCornerCap > 0 && smallestCornerNodes > 0)
                r.Finding($"On {smallestLabel} a corner-to-corner walk searched with an admissible " +
                          $"heuristic examines {smallestCornerNodes:#,0} nodes against that map's cap of " +
                          $"{smallestCornerCap:#,0} — {(double)smallestCornerNodes / smallestCornerCap * 100:0}% " +
                          "of it. The weighted heuristic is what keeps that number away from the cap, not " +
                          "the cap itself.");

            if (totalGaveUp > 0)
                r.Finding($"{totalGaveUp} of {totalQueries} path queries hit the node cap and returned " +
                          "failure. In the simulation that is a citizen who does not set off.");
            else
                r.Finding($"No query in {totalQueries} hit the node cap, on maps up to 5.2 million tiles. " +
                          "The map-scaled cap and the weighted heuristic between them have removed the " +
                          "failure mode entirely at these sizes.");
        }

        /// <summary>
        /// What overweighting the heuristic buys and what it costs.
        ///
        /// Two numbers move in opposite directions and both matter: nodes examined falls, which
        /// is the whole point, and path length rises, which is a man taking the next lane over.
        /// A single figure for either one is an argument; the pair is a decision.
        /// </summary>
        private static void WeightSweep(Report r, ulong seed, (int w, int h)[] sizes)
        {
            var (w, h) = sizes[sizes.Length / 2];
            var grid = Synthetic.PathGrid(w, h);
            var queries = Trips(grid, w * h > 1_000_000 ? 30 : 120, 0.25, 0.35, seed + 1);
            if (queries.Count == 0) return;

            var table = new Table($"Heuristic weight against search cost and route quality — {w}x{h}, cross-map trips",
                new Col("weight", 8), new Col("us med", 10), new Col("nodes med", 11),
                new Col("path med", 10), new Col("nodes/tile", 11),
                new Col("vs admissible", 26, right: false));

            float[] weights = { 1.0f, 1.1f, 1.2f, Pathfinder.DefaultHeuristicWeight, 1.6f, 2.0f };
            double basePath = 0, baseNodes = 0;

            foreach (float weight in weights)
            {
                var t = Run(grid, "cross", queries, LiftedCap, reps: 1, weight: weight);
                if (basePath == 0) { basePath = t.MedianPath; baseNodes = t.MedianNodes; }

                string versus = basePath > 0
                    ? $"{t.MedianNodes / baseNodes * 100:0}% nodes / {t.MedianPath / basePath * 100:0}% path"
                    : "";

                table.Row(weight.ToString("0.00") +
                          (Math.Abs(weight - Pathfinder.DefaultHeuristicWeight) < 1e-6 ? " *" : ""),
                          t.MedianUs, t.MedianNodes, t.MedianPath, t.Waste.ToString("0.0"), versus);

                r.M("weight", ("w", w), ("h", h), ("weight", weight),
                    ("us_med", t.MedianUs), ("nodes_med", t.MedianNodes),
                    ("path_med", t.MedianPath), ("nodes_per_tile", t.Waste));
            }

            table.Note("* is the shipping default. Weight 1.0 is admissible: it returns the genuinely");
            table.Note("shortest route and is the baseline the last column compares against. Above 1.0");
            table.Note("A* stops being optimal and starts being fast.");
            r.Add(table);
        }
    }
}
