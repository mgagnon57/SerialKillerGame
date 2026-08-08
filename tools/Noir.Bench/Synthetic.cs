using System;
using System.Collections.Generic;
using System.Text;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.World;

namespace Noir.Bench
{
    /// <summary>A world and the people in it, with a label for the table it appears in.</summary>
    public sealed class Settlement
    {
        public readonly string Label;
        public readonly WorldModel World;
        public readonly Population People;
        public readonly ulong Seed;

        public Settlement(string label, WorldModel world, Population people, ulong seed)
        {
            Label = label;
            World = world;
            People = people;
            Seed = seed;
        }

        public string MapSize => World.Width + "x" + World.Height;
        public int Tiles => World.Width * World.Height;
    }

    /// <summary>
    /// Villages, towns and cities built from a seed and nothing else.
    ///
    /// The benchmark has to measure the ENGINE, not Ashcombe. Ashcombe is one hand-authored
    /// layout at one size; anything inferred from it about a town of six hundred is an
    /// extrapolation from a sample of one. These are built in code so that population, map
    /// area and place count can each be moved independently, and so the harness runs with no
    /// Content/ directory at all.
    ///
    /// The shape is a road lattice with a building plot in each block. It is not pretty, but
    /// it has the two properties that decide the numbers: journeys follow roads and go round
    /// obstacles, and terrain cost varies, which is what makes A* work for its living.
    /// </summary>
    public static class Synthetic
    {
        /// <summary>Block pitch. A plot is 18x10 with roads and verges taking the rest.</summary>
        public const int PitchX = 26;
        public const int PitchY = 22;

        private const int PlotW = PitchX - 8;   // 18
        private const int PlotH = 10;

        // ---- sizing -------------------------------------------------------

        /// <summary>
        /// A map big enough to hold this many people at village density.
        ///
        /// Population and map area are deliberately tied together here, because that is how a
        /// settlement actually scales — a town of six hundred is not a village with five times
        /// the people standing in the same lane. The sweeps that need them separated ask for
        /// an explicit size instead.
        /// </summary>
        public static (int width, int height) MapFor(int population)
        {
            int units = (int)Math.Ceiling(population / 2.2);
            int dwellings = (int)Math.Ceiling(units / 1.85);
            int cells = dwellings + AmenityCells(population);

            int rows = Math.Max(2, (int)Math.Round(Math.Sqrt(cells / 1.18)));
            int cols = Math.Max(2, (int)Math.Ceiling(cells / (double)rows));

            return (4 + cols * PitchX, 12 + rows * PitchY);
        }

        public static int ColsIn(int width) => Math.Max(1, (width - 4) / PitchX);
        public static int RowsIn(int height) => Math.Max(1, (height - 12) / PitchY);

        // ---- what a settlement contains -----------------------------------

        private readonly struct Amenity
        {
            public readonly PlaceKind Kind;
            public readonly int PerHead;    // one of these per this many people
            public Amenity(PlaceKind kind, int perHead) { Kind = kind; PerHead = perHead; }
        }

        /// <summary>
        /// Ratios lifted from Ashcombe: one shop and one pub for a hundred and nine people,
        /// one school, one church. Holding them constant as population grows is what keeps
        /// behaviour comparable across the sweep — a city with one shop would put every
        /// citizen in the same queue and measure that instead of the simulation.
        /// </summary>
        private static readonly Amenity[] Amenities =
        {
            new Amenity(PlaceKind.Shop, 180),
            new Amenity(PlaceKind.Tavern, 220),
            new Amenity(PlaceKind.School, 400),
            new Amenity(PlaceKind.PostOffice, 500),
            new Amenity(PlaceKind.Mill, 500),
            new Amenity(PlaceKind.BusStop, 500),
            new Amenity(PlaceKind.PhoneBooth, 500),
            new Amenity(PlaceKind.Green, 600),
            new Amenity(PlaceKind.Playground, 600),
            new Amenity(PlaceKind.Gardens, 600),
            new Amenity(PlaceKind.Clinic, 700),
            new Amenity(PlaceKind.Farm, 700),
            new Amenity(PlaceKind.Church, 800),
            new Amenity(PlaceKind.Churchyard, 800),
            new Amenity(PlaceKind.Garage, 900),
            new Amenity(PlaceKind.VillageHall, 900),
        };

        private static int CountOf(int population, int perHead) =>
            Math.Max(1, (population + perHead - 1) / perHead);

        public static int AmenityCells(int population)
        {
            int n = 0;
            foreach (var a in Amenities) n += CountOf(population, a.PerHead);
            return n;
        }

        // ---- layout -------------------------------------------------------

        /// <summary>
        /// Rasterisable description of a settlement.
        ///
        /// <paramref name="extraPlaces"/> adds telephone boothes, which is the only way found to
        /// move place count without moving anything else: they employ nobody, no schedule ever
        /// sends anyone to one, and they occupy a single tile. Adding shops instead would have
        /// added job slots and errand destinations, and the experiment would have been
        /// measuring the population's behaviour rather than the loop over places.
        /// </summary>
        public static VillageLayout Layout(int width, int height, int targetPopulation,
                                           ulong seed, int extraPlaces = 0,
                                           int dwellingCap = int.MaxValue)
        {
            var rng = Xoshiro256ss.Substream(seed, "bench-layout");

            var layout = new VillageLayout { Name = "Synthetic", Width = width, Height = height };
            int cols = ColsIn(width), rows = RowsIn(height);

            // Terrain first: farmland north, a wood west, the river along the south edge.
            // The river sits below the last road on purpose — water is the one terrain that
            // can disconnect a map, and a benchmark that silently measures unreachable
            // destinations measures nothing.
            layout.Terrain.Add(new TerrainPatch { Kind = Terrain.Field, Area = new TileRect(0, 0, width, 2) });
            layout.Terrain.Add(new TerrainPatch { Kind = Terrain.Wood, Area = new TileRect(0, 12 + rows * PitchY - 10, 10, 10) });
            layout.Terrain.Add(new TerrainPatch { Kind = Terrain.Field, Area = new TileRect(width - 10, 4, 10, height - 16) });
            layout.Terrain.Add(new TerrainPatch { Kind = Terrain.Water, Area = new TileRect(0, height - 4, width, 4) });

            for (int j = 0; j <= rows; j++)
            {
                var run = new RoadRun { Kind = Terrain.Road, Width = 5 };
                run.Points.Add(new Tile(0, 2 + j * PitchY));
                run.Points.Add(new Tile(width - 1, 2 + j * PitchY));
                layout.Roads.Add(run);
            }

            for (int i = 0; i <= cols; i++)
            {
                var run = new RoadRun { Kind = Terrain.Path, Width = 3 };
                run.Points.Add(new Tile(2 + i * PitchX, 2));
                run.Points.Add(new Tile(2 + i * PitchX, 2 + rows * PitchY));
                layout.Roads.Add(run);
            }

            // ---- how many of each thing, and where ----
            var wanted = new List<PlaceKind>();
            foreach (var a in Amenities)
            {
                int n = CountOf(targetPopulation, a.PerHead);
                for (int k = 0; k < n; k++) wanted.Add(a.Kind);
            }
            for (int k = 0; k < extraPlaces; k++) wanted.Add(PlaceKind.PhoneBooth);

            int totalCells = cols * rows;
            int unitsNeeded = (int)Math.Ceiling(targetPopulation / 2.2);

            // Amenities are spread by stride rather than taking the first N cells. Clustered in
            // one corner they would make every errand a cross-map trip for three quarters of
            // the population, and the harness would be measuring that artefact.
            var kindAt = new PlaceKind?[totalCells];
            int stride = Math.Max(1, totalCells / Math.Max(1, wanted.Count));
            int cell = 0;
            foreach (var kind in wanted)
            {
                int slot = FreeSlot(kindAt, cell);
                if (slot < 0) break;
                kindAt[slot] = kind;
                cell = (slot + stride) % totalCells;
            }

            int millCount = 0;
            foreach (var k in kindAt) if (k == PlaceKind.Mill) millCount++;

            int fixedJobs = 0;
            foreach (var k in kindAt)
                if (k.HasValue && k.Value != PlaceKind.Mill) fixedJobs += JobsFor(k.Value, 0);

            // The mills soak up whatever employment is left over. Getting the employed share
            // near Ashcombe's matters more than it looks: an employed citizen commutes twice a
            // day at a fixed hour, and an unemployed one wanders at random. Those are different
            // load profiles, and a sweep that drifts between them is comparing two things.
            int targetJobs = (int)(targetPopulation * 0.45);
            int millJobs = millCount > 0
                ? Math.Max(4, (targetJobs - fixedJobs) / millCount)
                : 0;

            int unitsSoFar = 0, dwellingsMade = 0;

            for (int index = 0; index < totalCells; index++)
            {
                int i = index % cols, j = index / cols;
                var plot = new TileRect(i * PitchX + 7, j * PitchY + 8, PlotW, PlotH);

                if (kindAt[index].HasValue)
                {
                    layout.Places.Add(AmenitySpec(kindAt[index].Value, plot, index,
                                                  kindAt[index].Value == PlaceKind.Mill ? millJobs : -1));
                    continue;
                }

                if (unitsSoFar >= unitsNeeded || dwellingsMade >= dwellingCap) continue;

                // One to three homes behind one frontage: a cottage, a pair, a short terrace.
                int units = 1 + rng.NextInt(3);
                layout.Places.Add(new PlaceSpec
                {
                    Kind = PlaceKind.Dwelling,
                    Name = "plot " + index,
                    Human = "Somebody lives here.",
                    Bounds = plot,
                    Door = new Tile(plot.X + plot.W / 2, plot.Y),
                    Units = units,
                });

                unitsSoFar += units;
                dwellingsMade++;
            }

            return layout;
        }

        /// <summary>First unclaimed cell at or after <paramref name="from"/>, wrapping once.</summary>
        private static int FreeSlot(PlaceKind?[] cells, int from)
        {
            for (int n = 0; n < cells.Length; n++)
            {
                int i = (from + n) % cells.Length;
                if (!cells[i].HasValue) return i;
            }
            return -1;
        }

        /// <summary>
        /// The plot index goes into the name because a place's name is now its identity: every
        /// interior and every prop hangs off it, and WorldBuilder refuses a layout with two
        /// places called the same thing. A settlement with nine mills needs nine names.
        /// </summary>
        private static PlaceSpec AmenitySpec(PlaceKind kind, TileRect plot, int index, int millJobs)
        {
            var spec = new PlaceSpec
            {
                Kind = kind,
                Name = kind + " " + index,
                Human = "A place people go.",
                Bounds = Footprint(kind, plot),
                JobSlots = kind == PlaceKind.Mill ? millJobs : JobsFor(kind, 0),
            };

            if (spec.IsBuilding)
                spec.Door = new Tile(spec.Bounds.X + spec.Bounds.W / 2, spec.Bounds.Y);

            AddHours(spec, kind);
            return spec;
        }

        /// <summary>A phone booth is one tile. Everything else takes the whole plot.</summary>
        private static TileRect Footprint(PlaceKind kind, TileRect plot)
        {
            switch (kind)
            {
                case PlaceKind.PhoneBooth: return new TileRect(plot.X, plot.Y, 1, 1);
                case PlaceKind.BusStop: return new TileRect(plot.X, plot.Y, 2, 2);
                default: return plot;
            }
        }

        private static int JobsFor(PlaceKind kind, int millJobs)
        {
            switch (kind)
            {
                case PlaceKind.Mill: return millJobs;
                case PlaceKind.Farm: return 5;
                case PlaceKind.School: return 4;
                case PlaceKind.Tavern: return 3;
                case PlaceKind.Garage: return 3;
                case PlaceKind.Shop: return 2;
                case PlaceKind.Clinic: return 2;
                case PlaceKind.Church: return 1;
                case PlaceKind.PostOffice: return 1;
                case PlaceKind.VillageHall: return 1;
                default: return 0;
            }
        }

        private const byte MonTueThuFri = 0b0001_1011;

        private static void AddHours(PlaceSpec spec, PlaceKind kind)
        {
            switch (kind)
            {
                case PlaceKind.Church:
                    spec.Hours.Add(new OpenWindow(8 * 60, 18 * 60));
                    spec.Hours.Add(new OpenWindow(9 * 60 + 30, 12 * 60, OpenWindow.SundayOnly));
                    break;
                case PlaceKind.School:
                    spec.Hours.Add(new OpenWindow(8 * 60 + 30, 15 * 60 + 30, OpenWindow.Weekdays));
                    break;
                case PlaceKind.Tavern:
                    spec.Hours.Add(new OpenWindow(11 * 60, 14 * 60 + 30));
                    spec.Hours.Add(new OpenWindow(17 * 60 + 30, 23 * 60));
                    break;
                case PlaceKind.Shop:
                    spec.Hours.Add(new OpenWindow(7 * 60, 13 * 60, 0b0011_1111));
                    spec.Hours.Add(new OpenWindow(14 * 60, 17 * 60 + 30, OpenWindow.Weekdays));
                    break;
                case PlaceKind.PostOffice:
                    spec.Hours.Add(new OpenWindow(9 * 60, 13 * 60, OpenWindow.Weekdays));
                    spec.Hours.Add(new OpenWindow(9 * 60, 12 * 60, 0b0010_0000));
                    break;
                case PlaceKind.VillageHall:
                    spec.Hours.Add(new OpenWindow(9 * 60, 22 * 60));
                    break;
                case PlaceKind.Clinic:
                    spec.Hours.Add(new OpenWindow(8 * 60 + 30, 11 * 60 + 30, OpenWindow.Weekdays));
                    spec.Hours.Add(new OpenWindow(16 * 60, 18 * 60, MonTueThuFri));
                    break;
                case PlaceKind.Garage:
                    spec.Hours.Add(new OpenWindow(8 * 60, 18 * 60, OpenWindow.Weekdays));
                    spec.Hours.Add(new OpenWindow(8 * 60, 13 * 60, 0b0010_0000));
                    break;
                case PlaceKind.Mill:
                    spec.Hours.Add(new OpenWindow(6 * 60, 14 * 60, OpenWindow.Weekdays));
                    spec.Hours.Add(new OpenWindow(14 * 60, 22 * 60, OpenWindow.Weekdays));
                    break;
                case PlaceKind.Farm:
                    spec.Hours.Add(new OpenWindow(5 * 60 + 30, 19 * 60));
                    break;
            }
        }

        // ---- the whole thing ----------------------------------------------

        public static Settlement Build(string label, int targetPopulation, ulong seed = 1979)
        {
            var (w, h) = MapFor(targetPopulation);
            return Build(label, w, h, targetPopulation, seed);
        }

        public static Settlement Build(string label, int width, int height,
                                       int targetPopulation, ulong seed = 1979,
                                       int extraPlaces = 0, int dwellingCap = int.MaxValue)
        {
            var world = WorldBuilder.Build(
                Layout(width, height, targetPopulation, seed, extraPlaces, dwellingCap), seed);
            var people = PopulationGenerator.Generate(world, Names.Value, Particulars.Value, seed);
            return new Settlement(label, world, people, seed);
        }

        // ---- a bare grid, for pathfinding ---------------------------------

        /// <summary>
        /// The same lattice as a settlement, painted straight onto a grid.
        ///
        /// Pathfinding is measured on this rather than on a full world because building a
        /// 2720x1920 settlement means generating a quarter of a million rooms, and the
        /// pathfinder cannot tell the difference between a solid building and a furnished one:
        /// it sees walkable and not-walkable. Terrain cost IS reproduced, because that is what
        /// the heuristic has to cope with.
        /// </summary>
        public static TileGrid PathGrid(int width, int height)
        {
            var grid = new TileGrid(width, height);
            int cols = ColsIn(width), rows = RowsIn(height);

            Fill(grid, new TileRect(0, 0, width, 2), Terrain.Field);
            Fill(grid, new TileRect(width - 10, 4, 10, height - 16), Terrain.Field);
            Fill(grid, new TileRect(0, 12 + rows * PitchY - 10, 10, 10), Terrain.Wood);
            Fill(grid, new TileRect(0, height - 4, width, 4), Terrain.Water);

            for (int j = 0; j <= rows; j++)
                Fill(grid, new TileRect(0, j * PitchY, width, 5), Terrain.Road);

            for (int i = 0; i <= cols; i++)
                Fill(grid, new TileRect(i * PitchX + 1, 2, 3, rows * PitchY), Terrain.Path);

            for (int j = 0; j < rows; j++)
            for (int i = 0; i < cols; i++)
                Fill(grid, new TileRect(i * PitchX + 7, j * PitchY + 8, PlotW, PlotH), Terrain.Wall);

            return grid;
        }

        private static void Fill(TileGrid grid, TileRect r, Terrain kind)
        {
            var flags = TileGrid.FlagsFor(kind);
            for (int y = r.Y; y <= r.Bottom; y++)
            for (int x = r.X; x <= r.Right; x++)
                grid.Set(x, y, kind, flags);
        }

        // ---- content, without Content/ ------------------------------------

        public static readonly Lazy<NameTable> Names = new Lazy<NameTable>(BuildNames);
        public static readonly Lazy<ParticularsTable> Particulars = new Lazy<ParticularsTable>(BuildParticulars);

        private static NameTable BuildNames()
        {
            string[] heads = { "Al", "Bre", "Cor", "Dun", "El", "Far", "Gar", "Hal", "Is", "Jen",
                               "Kel", "Lor", "Mar", "Nor", "Or", "Pen", "Quil", "Ros", "Sel", "Tor",
                               "Ur", "Vel", "Win", "Yar" };
            string[] maleTails = { "an", "wick", "bert", "ric", "don", "ley", "mund", "ford" };
            string[] femaleTails = { "a", "ith", "wen", "isse", "ora", "lyn", "ette", "ine" };
            string[] surTails = { "combe", "worth", "field", "ston", "by", "thorpe", "shaw", "ridge",
                                  "well", "ham", "low", "grave" };

            var sb = new StringBuilder();
            foreach (string h in heads)
            {
                foreach (string t in maleTails) sb.Append("male ").Append(h).Append(t).Append('\n');
                foreach (string t in femaleTails) sb.Append("female ").Append(h).Append(t).Append('\n');
                foreach (string t in surTails) sb.Append("surname ").Append(h).Append(t).Append('\n');
            }
            return NameTable.Parse(sb.ToString());
        }

        private static ParticularsTable BuildParticulars()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 48; i++)
                sb.Append("has a habit numbered ").Append(i).Append('\n');
            return ParticularsTable.Parse(sb.ToString());
        }
    }
}
