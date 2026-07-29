using System;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Sim;
using Noir.Core.World;

namespace Noir.Bench
{
    /// <summary>
    /// What the village weighs.
    ///
    /// Everything here is a difference between two settled heaps rather than an absolute
    /// reading, because an absolute reading includes the harness, the JIT's own tables and
    /// whatever the last measurement left behind. Differences of two configurations that
    /// differ in one dimension give a bytes-per-tile or bytes-per-citizen figure that projects
    /// honestly; a total does not.
    /// </summary>
    public static class MemoryBench
    {
        /// <summary>Bytes the heap grew by while making one thing, with the thing still alive.</summary>
        public static (long bytes, T value) Weigh<T>(Func<T> make)
        {
            long before = Measure.SettledHeap();
            T value = make();
            long after = Measure.SettledHeap();
            GC.KeepAlive(value);
            return (after - before, value);
        }

        private const double MB = 1024.0 * 1024.0;

        public static void Report(Report r, ulong seed, int[] populations)
        {
            // ---- per-unit costs, each from one dimension moved and nothing else ----

            var smallGrid = Weigh(() => new TileGrid(400, 300));
            var bigGrid = Weigh(() => new TileGrid(1200, 900));
            double bytesPerTile = (bigGrid.bytes - smallGrid.bytes) /
                                  (double)(1200 * 900 - 400 * 300);

            var smallPf = Weigh(() => new Pathfinder(smallGrid.value));
            var bigPf = Weigh(() => new Pathfinder(bigGrid.value));
            double pfPerTile = (bigPf.bytes - smallPf.bytes) /
                               (double)(1200 * 900 - 400 * 300);

            GC.KeepAlive(smallPf.value);
            GC.KeepAlive(bigPf.value);

            var perUnit = new Table("Per-unit cost, each measured by moving one dimension",
                new Col("thing", 40, right: false), new Col("bytes", 10),
                new Col("per", 10, right: false), new Col("derived from", 32, right: false));

            perUnit.Row("TileGrid", bytesPerTile.ToString("0.00"), "tile",
                        "400x300 vs 1200x900");
            perUnit.Row("Pathfinder scratch buffers", pfPerTile.ToString("0.00"), "tile",
                        "one instance per Simulation");

            r.M("mem_unit", ("thing", "tilegrid"), ("bytes_per_tile", bytesPerTile));
            r.M("mem_unit", ("thing", "pathfinder"), ("bytes_per_tile", pfPerTile));

            // ---- whole settlements ----

            var totals = new Table("Measured footprint of a whole settlement",
                new Col("settlement", 12, right: false), new Col("pop", 7),
                new Col("map", 11, right: false), new Col("places", 7),
                new Col("world MB", 9), new Col("people MB", 10),
                new Col("A* scratch MB", 14), new Col("agents MB", 10),
                new Col("+30min MB", 10), new Col("total MB", 9));

            double lastPop = 0, lastCitizenBytes = 0;
            double citizenBytes = 0, agentBytes = 0, simRunBytes = 0;

            foreach (int target in populations)
            {
                var (w, h) = Synthetic.MapFor(target);
                var layout = Synthetic.Layout(w, h, target, seed);

                var world = Weigh(() => WorldBuilder.Build(layout, seed));
                var people = Weigh(() => PopulationGenerator.Generate(
                    world.value, Synthetic.Names.Value, Synthetic.Particulars.Value, seed));

                var sim = Weigh(() => new Simulation(world.value, people.value, seed, TickBench.PeakMinute));

                // Half an hour of game time at the busiest part of the morning, so that the
                // path lists have had a chance to grow to the length of a real journey.
                long beforeRun = Measure.SettledHeap();
                sim.value.Tick(30 * GameClock.TicksPerMinute);
                long afterRun = Measure.SettledHeap();

                int pop = people.value.Count;

                // A Simulation's own memory is mostly the Pathfinder it constructs, and that is
                // a function of map area rather than of population. Reported together they make
                // a bytes-per-citizen figure that triples when the map grows and misleads
                // anyone who projects with it.
                double scratch = (double)w * h * pfPerTile;
                double agents = sim.bytes - scratch;
                double total = (world.bytes + people.bytes + sim.bytes + (afterRun - beforeRun)) / MB;

                totals.Row("synthetic", pop, w + "x" + h, world.value.PlaceCount,
                           world.bytes / MB, people.bytes / MB, scratch / MB, agents / MB,
                           (afterRun - beforeRun) / MB, total);

                r.M("mem_total", ("pop", pop), ("w", w), ("h", h),
                    ("places", world.value.PlaceCount), ("rooms", world.value.RoomCount),
                    ("world_b", world.bytes), ("people_b", people.bytes),
                    ("sim_b", sim.bytes), ("scratch_b", scratch), ("agents_b", agents),
                    ("sim_run_b", afterRun - beforeRun),
                    ("world_b_per_tile", world.bytes / (double)(w * h)),
                    ("people_b_per_citizen", people.bytes / (double)pop),
                    ("agents_b_per_citizen", agents / pop));

                citizenBytes = lastPop > 0
                    ? (people.bytes - lastCitizenBytes) / (pop - lastPop)
                    : people.bytes / (double)pop;
                agentBytes = agents / pop;
                simRunBytes = (afterRun - beforeRun) / (double)pop;

                lastPop = pop;
                lastCitizenBytes = people.bytes;

                GC.KeepAlive(sim.value);
            }

            perUnit.Row("Citizen + household + names", citizenBytes.ToString("0"), "citizen",
                        "largest two sizes");
            perUnit.Row("Simulation agent state and plans", agentBytes.ToString("0"), "citizen",
                        "sim total minus A* scratch");
            perUnit.Row("Paths retained after 30 game-minutes", simRunBytes.ToString("0"), "citizen",
                        "grows toward longest journey");
            perUnit.Note("Path lists are never released: each agent keeps the route it last walked,");
            perUnit.Note("so the steady state trends toward (longest journey x 8 bytes) per agent —");
            perUnit.Note("24 KB each on a 2720x1920 map, where a corner-to-corner route is 3,000 tiles.");
            r.Add(perUnit);
            r.Add(totals);

            r.M("mem_unit", ("thing", "citizen"), ("bytes", citizenBytes));
            r.M("mem_unit", ("thing", "agent_state"), ("bytes", agentBytes));
            r.M("mem_unit", ("thing", "paths_30min"), ("bytes", simRunBytes));

            // ---- projections ----

            var projection = new Table("Projected, from the per-unit costs above",
                new Col("settlement", 12, right: false), new Col("pop", 8),
                new Col("map", 11, right: false), new Col("tiles", 11),
                new Col("grid MB", 9), new Col("A* scratch MB", 14),
                new Col("people+agents MB", 17), new Col("rooms", 9),
                new Col("id headroom", 16, right: false));

            foreach (int pop in new[] { 109, 600, 5000, 20000, 50000 })
            {
                var (w, h) = Synthetic.MapFor(pop);
                double tiles = (double)w * h;
                int units = (int)Math.Ceiling(pop / 2.2);
                int rooms = (int)(units * 4.2);   // interiors average about four rooms a home

                projection.Row(Name(pop), pop, w + "x" + h, (long)tiles,
                               tiles * bytesPerTile / MB,
                               tiles * pfPerTile / MB,
                               pop * (citizenBytes + agentBytes) / MB,
                               rooms,
                               rooms > short.MaxValue ? "ROOM ID OVERFLOW" : "ok");

                r.M("mem_projected", ("pop", pop), ("w", w), ("h", h),
                    ("grid_mb", tiles * bytesPerTile / MB),
                    ("pathfinder_mb", tiles * pfPerTile / MB),
                    ("people_agents_mb", pop * (citizenBytes + agentBytes) / MB),
                    ("rooms", rooms), ("rooms_overflow", rooms > short.MaxValue ? 1 : 0));
            }

            projection.Note("TileGrid stores place and room ids as short. Past 32,767 rooms the cast in");
            projection.Note("SetRoom wraps to a negative and RoomAt reports 'no room' — silently, and");
            projection.Note("only for the tiles inside the affected buildings.");
            r.Add(projection);

            r.Finding("TileGrid holds place and room ids in short[]. At about 7,700 homes — a city of " +
                      "roughly 17,000 — room ids pass 32,767, the cast in TileGrid.SetRoom wraps negative " +
                      "and RoomAt starts answering 'no room' for real rooms. Nothing throws.");

            r.Finding($"Pathfinder scratch is {pfPerTile:0.0} bytes a tile against {bytesPerTile:0.0} for the " +
                      $"grid it searches: at 2720x1920 that is one {2720.0 * 1920 * pfPerTile / MB:0} MB " +
                      "allocation per Pathfinder instance, made in the constructor whether or not anyone " +
                      "ever asks for a path.");
        }

        private static string Name(int pop) =>
            pop <= 150 ? "village" : pop <= 1000 ? "town" : pop <= 10000 ? "city" : "metropolis";
    }
}
