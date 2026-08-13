using System;
using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Bench
{
    /// <summary>
    /// What it costs to build a world, stage by stage.
    ///
    /// This is a load-time cost, not a per-frame one, so the question it answers is different:
    /// not "does it fit in the budget" but "how long does the player look at a progress bar",
    /// and — the part that actually bites — which stage is the one that stops being acceptable
    /// first when the map grows.
    /// </summary>
    public static class BuildBench
    {
        /// <summary>
        /// A copy of a layout with every interior-generating building turned into a garage.
        ///
        /// Garages are stamped as a walled footprint and nothing else, so building this variant
        /// and subtracting isolates the cost of interiors, furniture and counter lines without
        /// touching WorldBuilder.
        /// </summary>
        private static VillageLayout Shells(VillageLayout source)
        {
            var copy = new VillageLayout
            {
                Name = source.Name,
                Width = source.Width,
                Height = source.Height,
            };
            copy.Terrain.AddRange(source.Terrain);
            copy.Roads.AddRange(source.Roads);

            foreach (var spec in source.Places)
            {
                var kind = spec.Kind;
                if (WantsInterior(kind)) kind = PlaceKind.Garage;

                var clone = new PlaceSpec
                {
                    Kind = kind,
                    Name = spec.Name,
                    Human = spec.Human,
                    Bounds = spec.Bounds,
                    Door = spec.Door,
                    JobSlots = spec.JobSlots,
                    Units = spec.Units,
                };
                clone.Hours.AddRange(spec.Hours);
                copy.Places.Add(clone);
            }
            return copy;
        }

        private static bool WantsInterior(PlaceKind kind)
        {
            switch (kind)
            {
                case PlaceKind.Dwelling:
                case PlaceKind.Tavern:
                case PlaceKind.Shop:
                case PlaceKind.Clinic:
                case PlaceKind.School:
                case PlaceKind.PostOffice:
                case PlaceKind.VillageHall:
                case PlaceKind.Farm:
                    return true;
                default:
                    return false;
            }
        }

        private static VillageLayout Subset(VillageLayout source, bool roads, bool places)
        {
            var copy = new VillageLayout
            {
                Name = source.Name,
                Width = source.Width,
                Height = source.Height,
            };
            copy.Terrain.AddRange(source.Terrain);
            if (roads) copy.Roads.AddRange(source.Roads);
            if (places) copy.Places.AddRange(source.Places);
            return copy;
        }

        /// <summary>
        /// One layout variant's build cost, with the two stages that every variant pays for
        /// taken back out.
        ///
        /// Naive ablation does not work here, and it fails in a direction that hides the
        /// answer: PropGenerator skips road tiles and skips the floor inside a claimed place,
        /// so a layout WITH roads has a cheaper prop scan than one without, and the difference
        /// between the two builds comes out negative. Subtracting the prop pass and the two
        /// WorldModel constructions from each variant before differencing is what makes the
        /// remaining stages additive.
        /// </summary>
        private readonly struct Variant
        {
            public readonly double Build, Props, Model;
            public Variant(double build, double props, double model)
            {
                Build = build;
                Props = props;
                Model = model;
            }

            /// <summary>Build time with the two common passes removed.</summary>
            public double Core => Build - Props - 2 * Model;
        }

        /// <summary>
        /// Every quantity here is the FASTEST of the repetitions, not the median.
        ///
        /// That is the wrong default for a throughput figure and the right one here. Noise in a
        /// load-time measurement is one-sided — something else took the core, a collection
        /// landed — so the fastest run is the closest to the cost of the work itself, and it is
        /// the only summary under which differences between variants add up instead of coming
        /// out negative on a busy machine. The spread of the total is reported alongside, so
        /// the reader can see how much was thrown away.
        /// </summary>
        private readonly struct Sampled
        {
            public readonly Variant Variant;
            public readonly Stat BuildStat;
            public Sampled(Variant variant, Stat buildStat) { Variant = variant; BuildStat = buildStat; }
        }

        private static Sampled Profile(VillageLayout layout, ulong seed, int reps)
        {
            var build = Measure.PerRun(() => GC.KeepAlive(WorldBuilder.Build(layout, seed)), reps, 1);

            var world = WorldBuilder.Build(layout, seed);
            var props = Measure.PerRun(() => GC.KeepAlive(PropGenerator.Generate(world, seed)), reps, 1);

            var places = new Place[world.PlaceCount];
            for (int i = 0; i < places.Length; i++) places[i] = world.GetPlace(new PlaceId(i));
            var rooms = new List<Room>(world.AllRooms).ToArray();
            var furniture = new List<Furniture>(world.AllFurniture).ToArray();

            var model = Measure.PerRun(
                () => GC.KeepAlive(new WorldModel(world.Name, world.Grid, places, rooms, furniture)),
                reps, 1);

            return new Sampled(new Variant(build.Min, props.Min, model.Min), build);
        }

        public static void Stages(Report r, VillageLayout layout, string label, ulong seed, int reps)
        {
            int w = layout.Width, h = layout.Height;

            // Grow the GC heap before anything is timed. The first build of a large layout pays
            // to expand the heap to hold a five-megabyte grid and every build after it does not,
            // so without this the whole cost of that expansion is charged to whichever stage
            // happens to be measured first — which showed up as a city whose terrain pass cost
            // twelve milliseconds and whose road pass cost minus eleven.
            for (int i = 0; i < 2; i++) GC.KeepAlive(WorldBuilder.Build(layout, seed));
            Measure.SettledHeap();

            double grid = Measure.PerRun(() => { var g = new TileGrid(w, h); GC.KeepAlive(g); },
                                         reps, warmups: 1).Min;

            var terrain = Profile(Subset(layout, roads: false, places: false), seed, reps).Variant;
            var roads = Profile(Subset(layout, roads: true, places: false), seed, reps).Variant;
            var shells = Profile(Shells(layout), seed, reps).Variant;
            var sampled = Profile(layout, seed, reps);
            var full = sampled.Variant;

            var world = WorldBuilder.Build(layout, seed);

            var table = new Table($"WorldBuilder.Build — {label} ({w}x{h}, {world.PlaceCount} places, " +
                                  $"{world.RoomCount} rooms, {world.FurnitureCount} furniture, " +
                                  $"{world.PropCount} props)",
                new Col("stage", 36, right: false), new Col("ms", 9), new Col("share", 7),
                new Col("measured by", 13, right: false));

            // Terrain, roads and footprints are reported as one row rather than three.
            //
            // Separately they are three differences of three pairs of measurements, each worth
            // a few per cent of the build, and on a machine with anything else running the
            // noise is larger than any of them — which produced tables where laying roads took
            // seven milliseconds and stamping buildings took minus five. Together they are one
            // difference, comfortably above the noise, and they are one thing anyway:
            // rasterising the authored layout onto the grid.
            double sRaster = shells.Core - grid;
            double sInteriors = full.Core - shells.Core;
            double sProps = full.Props;
            double sModel = full.Model * 2;

            double sTerrain = terrain.Core - grid;
            double sRoads = roads.Core - terrain.Core;
            double sFootprints = shells.Core - roads.Core;

            double sum = grid + sRaster + sInteriors + sProps + sModel;

            void Row(string name, double seconds, string how) =>
                table.Row(name, seconds * 1000, (seconds / full.Build * 100).ToString("0.0") + "%", how);

            Row("grid allocation and clear", grid, "direct");
            Row("rasterise: terrain, roads, footprints", sRaster, "ablation");
            Row("interiors, furniture, counter lines", sInteriors, "ablation");
            Row("props (scans every tile)", sProps, "direct");
            Row("WorldModel indexes, built twice", sModel, "direct x2");
            Row("unattributed (array copies, noise)", full.Build - sum, "residual");
            table.Gap();
            Row("TOTAL, measured end to end", full.Build, "direct");

            table.Note("Build constructs WorldModel twice: once bare so props can read the finished");
            table.Note("grid, once more with the props in it. Both passes rebuild every counter line.");
            table.Note("Ablation rows have the prop pass and both WorldModel passes removed from each");
            table.Note("variant first — without that, adding roads appears to make the build faster,");
            table.Note("because road tiles are ones the prop scan skips.");
            table.Note($"Fastest of {reps} runs each. The total's own spread across those runs was " +
                       $"{sampled.BuildStat.SpreadCell}.");
            table.Note($"Inside the rasterise row, and too small to separate reliably: terrain " +
                       $"{sTerrain * 1000:0.00} ms, roads {sRoads * 1000:0.00} ms, footprints " +
                       $"{sFootprints * 1000:0.00} ms.");
            r.Add(table);

            r.M("build", ("label", label.Replace(' ', '_')), ("w", w), ("h", h),
                ("places", world.PlaceCount), ("rooms", world.RoomCount),
                ("furniture", world.FurnitureCount), ("props", world.PropCount),
                ("ms_total", full.Build * 1000), ("ms_grid", grid * 1000),
                ("ms_raster", sRaster * 1000),
                ("ms_terrain", sTerrain * 1000), ("ms_roads", sRoads * 1000),
                ("ms_footprints", sFootprints * 1000), ("ms_interiors", sInteriors * 1000),
                ("ms_props", sProps * 1000), ("ms_model_x2", sModel * 1000),
                ("ms_residual", (full.Build - sum) * 1000),
                // The raw per-variant figures the ablation is built from. Kept because when a
                // stage comes out negative these are the only way to see which variant moved.
                ("raw_terrain_build", terrain.Build * 1000), ("raw_terrain_props", terrain.Props * 1000),
                ("raw_roads_build", roads.Build * 1000), ("raw_roads_props", roads.Props * 1000),
                ("raw_shells_build", shells.Build * 1000), ("raw_shells_props", shells.Props * 1000),
                ("raw_full_build", full.Build * 1000), ("raw_full_props", full.Props * 1000));
        }

        /// <summary>Build cost against map area, with the settlement itself held constant.</summary>
        public static void AreaSweep(Report r, ulong seed, (int w, int h)[] sizes, int basePopulation)
        {
            var table = new Table("Build cost against map area, same settlement on a bigger grid",
                new Col("map", 12, right: false), new Col("tiles", 11), new Col("places", 7),
                new Col("ms", 9), new Col("spread", 7), new Col("ns/tile", 9), new Col("^", 6));

            var (bw, bh) = Synthetic.MapFor(basePopulation);
            double lastArea = 0, lastMs = 0;

            // The settlement's own map is always the first row: everything after it is that
            // same village sitting on an emptier and emptier grid, which is what isolates the
            // cost that is purely a function of area.
            var all = new List<(int w, int h)> { (bw, bh) };
            foreach (var size in sizes)
                if (size.w > bw && size.h > bh) all.Add(size);

            foreach (var (w, h) in all)
            {
                var layout = Synthetic.Layout(bw, bh, basePopulation, seed);
                layout.Width = w;
                layout.Height = h;

                int reps = w * h > 2_000_000 ? 9 : 41;
                var stat = Measure.PerRun(() => GC.KeepAlive(WorldBuilder.Build(layout, seed)), reps, 1);

                double area = (double)w * h;
                table.Row(w + "x" + h, (long)area, layout.Places.Count, stat.Min * 1000,
                          stat.SpreadCell, stat.Min * 1e9 / area,
                          lastArea > 0 ? TickBench.Exponent(lastArea, lastMs, area, stat.Min).ToString("0.00") : "");

                r.M("build_area", ("w", w), ("h", h), ("tiles", (long)area),
                    ("places", layout.Places.Count), ("ms", stat.Min * 1000),
                    ("ns_per_tile", stat.Min * 1e9 / area));

                lastArea = area;
                lastMs = stat.Min;
            }

            table.Note("'^' is the exponent k in ms ~ area^k. 1.0 means the cost is per-tile.");
            table.Note("Places are held constant, so every row builds the same settlement — only the");
            table.Note("empty ground around it grows. ms is the fastest run; spread covers all of them.");
            r.Add(table);
        }

        /// <summary>Build cost against place count, with the map held still.</summary>
        public static void PlaceSweep(Report r, ulong seed, int population, int[] dwellingCaps)
        {
            var (w, h) = Synthetic.MapFor(population);

            var table = new Table($"Build cost against place count, map fixed at {w}x{h}",
                new Col("places", 8), new Col("rooms", 8), new Col("furniture", 10),
                new Col("ms", 9), new Col("spread", 7), new Col("us/place", 10), new Col("^", 6));

            double lastPlaces = 0, lastMs = 0;
            double firstPlaces = 0, firstMs = 0;

            foreach (int cap in dwellingCaps)
            {
                var layout = Synthetic.Layout(w, h, population, seed, 0, cap);
                var world = WorldBuilder.Build(layout, seed);

                var stat = Measure.PerRun(() => GC.KeepAlive(WorldBuilder.Build(layout, seed)), 41, 1);

                table.Row(world.PlaceCount, world.RoomCount, world.FurnitureCount,
                          stat.Min * 1000, stat.SpreadCell,
                          stat.Min * 1e6 / world.PlaceCount,
                          lastPlaces > 0 ? TickBench.Exponent(lastPlaces, lastMs, world.PlaceCount, stat.Min).ToString("0.00") : "");

                r.M("build_places", ("places", world.PlaceCount), ("rooms", world.RoomCount),
                    ("furniture", world.FurnitureCount), ("ms", stat.Min * 1000));

                if (firstPlaces == 0) { firstPlaces = world.PlaceCount; firstMs = stat.Min; }
                lastPlaces = world.PlaceCount;
                lastMs = stat.Min;
            }

            table.Note("The whole-map prop scan is in every row and does not change, so the marginal");
            table.Note("cost of a place is the slope, not the us/place column.");

            if (lastPlaces > firstPlaces)
            {
                double marginal = (lastMs - firstMs) * 1e6 / (lastPlaces - firstPlaces);
                table.Note($"marginal cost of one more place, end to end: {marginal:0.0} us " +
                           "(footprint, interior, furniture, counter line and all).");
                r.M("build_place_marginal", ("us_per_place", marginal));
            }

            r.Add(table);
        }
    }
}
