using System;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Sim
{
    /// <summary>
    /// Headless harness. The village runs here first, in a terminal, before Unity exists.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                string command = args.Length > 0 ? args[0].ToLowerInvariant() : "map";
                switch (command)
                {
                    case "map": return CmdMap(args);
                    case "places": return CmdPlaces();
                    case "check": return CmdCheck();
                    case "who": return CmdWho();
                    case "households": return CmdHouseholds();
                    case "day": return CmdDay(args);
                    case "density": return CmdDensity(args);
                    case "trace": return CmdTrace(args);
                    case "watch": return CmdWatch(args);
                    case "strand": return CmdStrand(args);
                    case "street": return CmdStreet(args);

                    // ---- the instruments ----
                    //
                    // These four grade the settlement rather than proving it runs. `ratio` is
                    // the one the design document set a number for, and the only one that can
                    // say the village is not yet what it claims to be.
                    case "encounters": return CmdEncounters(args);
                    case "vocab": return CmdVocab(args);
                    case "economy": return CmdEconomy(args);
                    case "ratio": return CmdRatio(args);
                    case "testimony": return CmdTestimony(args);

                    case "tiles": return CmdTiles();
                    case "audio": return CmdAudio();
                    case "house": return CmdHouse(args);
                    default:
                        Console.WriteLine(
                            "usage: Noir.Sim <command>\n" +
                            "  map [scale]      the village, as text\n" +
                            "  places           every place, with hours\n" +
                            "  check            validate the layout\n" +
                            "  who              population summary\n" +
                            "  households       everyone, by household\n" +
                            "  day <n> [day]    one person's whole day\n" +
                            "  density [day]    where everyone is, hour by hour\n" +
                            "  trace <n> [hrs] [startHour]   follow one person as it runs\n" +
                            "  watch [startHour] [hrs] [scale]  the village, moving\n" +
                            "  strand [--days n] [--tile n] [--from hour]\n" +
                            "                   who never got where they were going. --tile n runs the\n" +
                            "                   same village on a map n times bigger each way.\n" +
                            "  street [--days n]  how many people are VISIBLE from outside, hour by\n" +
                            "                   hour. density counts where plans say people are;\n" +
                            "                   this counts bodies you could photograph.\n" +
                            "\n" +
                            "  the instruments — these grade the settlement, not the code\n" +
                            "  encounters [--days n] [--tile n] [--from hour] [--range tenths]\n" +
                            "             [--regap s] [--seed n]\n" +
                            "                   is the acquaintance graph real: passes, repeats, reach,\n" +
                            "                   and the shape of the graph. --range is in TENTHS of a\n" +
                            "                   tile, so 24 is the sim's own talking range. Defaults to\n" +
                            "                   a week, which is 12.1M ticks and about a minute here;\n" +
                            "                   --days 1 divides that by seven.\n" +
                            "  vocab [--pop n] [--k n] [--samples n] [--seed n]\n" +
                            "                   the content ceiling: how many more entries each authored\n" +
                            "                   table needs. Exits 1 if any table is over the bar.\n" +
                            "                   Well under a second; no simulation runs.\n" +
                            "  economy [--tile n] [--walk m] [--pupils n] [--seed n]\n" +
                            "                   does the settlement close: jobs, school places, food\n" +
                            "                   within a walk, and hours against who is free. Exits 1 if\n" +
                            "                   it does not. Well under a second; no simulation runs.\n" +
                            "  ratio [n] [--seeds a,b,c]\n" +
                            "                   THE 2:1 RULE. Puts a watcher opposite every villager for a\n" +
                            "                   fortnight and counts what it got: distinct useless things\n" +
                            "                   against distinct useful ones. The median must be 2:1. With\n" +
                            "                   an index it reports one person you chose yourself. The\n" +
                            "                   watch length is pinned at fourteen days and there is no\n" +
                            "                   --days: the number is meaningless without it.\n" +
                            "  testimony [days]  the statement census: what the whole town could say\n" +
                            "                   about a fortnight of the player's movements.\n" +
                            "\n" +
                            "  tiles            regenerate Content/tiles/*.png\n" +
                            "  audio            regenerate Content/audio/*.wav");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        private static WorldModel LoadWorld()
        {
            var layout = VillageParser.Parse(ContentPath.ReadAll("fixture-village.txt"));
            return WorldBuilder.Build(layout);
        }

        private static int CmdMap(string[] args)
        {
            int scale = 1;
            if (args.Length > 1) int.TryParse(args[1], out scale);
            if (scale < 1) scale = 1;

            var world = LoadWorld();
            Console.WriteLine($"{world.Name} — {world.Width}x{world.Height}, "
                            + $"{world.PlaceCount} places, {world.TotalJobSlots} jobs");
            Console.WriteLine();
            Console.WriteLine(AsciiMap.Render(world, scale));
            Console.WriteLine(AsciiMap.Legend());
            return 0;
        }

        private static int CmdPlaces()
        {
            var world = LoadWorld();
            Console.WriteLine(AsciiMap.PlaceTable(world));
            return 0;
        }

        private static int CmdCheck()
        {
            var world = LoadWorld();
            var report = WorldValidator.Validate(world);
            Console.WriteLine(report.ToString());
            return report.IsValid ? 0 : 1;
        }

        private static int CmdWho()
        {
            Console.WriteLine(Reports.Summary(VillageContext.Load()));
            return 0;
        }

        private static int CmdHouseholds()
        {
            Console.WriteLine(Reports.Households(VillageContext.Load()));
            return 0;
        }

        private static int CmdDay(string[] args)
        {
            int index = args.Length > 1 && int.TryParse(args[1], out int i) ? i : 0;
            int day = args.Length > 2 && int.TryParse(args[2], out int d) ? d : 0;
            Console.WriteLine(Reports.Day(VillageContext.Load(), index, day));
            return 0;
        }

        private static int CmdDensity(string[] args)
        {
            int day = args.Length > 1 && int.TryParse(args[1], out int d) ? d : 0;
            Console.WriteLine(Reports.DensityByHour(VillageContext.Load(), day));
            return 0;
        }

        private static int CmdTrace(string[] args)
        {
            int index = args.Length > 1 && int.TryParse(args[1], out int i) ? i : 0;
            int hours = args.Length > 2 && int.TryParse(args[2], out int h) ? h : 24;
            int start = args.Length > 3 && int.TryParse(args[3], out int s) ? s : 0;
            Console.Write(LiveView.Trace(VillageContext.Load(), index, hours, start));
            return 0;
        }

        private static int CmdHouse(string[] args)
        {
            var ctx = VillageContext.Load();
            if (args.Length > 1 && int.TryParse(args[1], out int n) && n >= 0)
                Console.WriteLine(Reports.House(ctx, n));
            else if (args.Length > 1)
                Console.WriteLine(Reports.Named(ctx, string.Join(" ", args, 1, args.Length - 1)));
            else
                Console.WriteLine(Reports.Houses(ctx, 6));
            return 0;
        }

        private static int CmdTiles()
        {
            TileGenerator.GenerateAll(System.IO.Path.Combine(ContentPath.Root, "tiles"));
            Console.WriteLine();
            TileGenerator.GenerateSurfaces(System.IO.Path.Combine(ContentPath.Root, "textures"));
            return 0;
        }

        private static int CmdAudio()
        {
            AudioGenerator.GenerateAll(System.IO.Path.Combine(ContentPath.Root, "audio"));
            return 0;
        }

        /// <summary>
        /// `--tile n` runs the same content on a map n times bigger each way. The village never
        /// strands anybody, which is the point: this is the command that shows what the same
        /// simulation does once the map is the size the project eventually wants.
        /// </summary>
        private static int CmdStrand(string[] args)
        {
            int days = Flag(args, "--days", 1);
            int tiles = Flag(args, "--tile", 1);
            int startHour = Flag(args, "--from", 0);

            Console.WriteLine(StrandReport.Run(VillageContext.Load(tiles), days, startHour));
            return 0;
        }

        private static int CmdStreet(string[] args)
        {
            Console.WriteLine(StreetReport.Run(VillageContext.Load(), Flag(args, "--days", 1)));
            return 0;
        }

        private static int Flag(string[] args, string name, int fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name && int.TryParse(args[i + 1], out int v)) return v;
            return fallback;
        }

        private static ulong Seed(string[] args, ulong fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--seed" && ulong.TryParse(args[i + 1], out ulong v)) return v;
            return fallback;
        }

        /// <summary>
        /// A week by default, and it is not an arbitrary length: "distinct people met" only
        /// becomes a shape rather than a number once there has been time for the second meeting.
        /// A single day answers the volume question and none of the others.
        /// </summary>
        private static int CmdEncounters(string[] args)
        {
            int days = Flag(args, "--days", 7);
            int tiles = Flag(args, "--tile", 1);
            int startHour = Flag(args, "--from", 0);
            int regap = Flag(args, "--regap", EncounterReport.DefaultRegapSeconds);

            // Tenths of a tile, because Flag only parses integers and a fractional range is the
            // only fractional argument the harness has ever needed. 24 is the simulation's own
            // talking range; the report prints back what it was given.
            int range = Flag(args, "--range", 0);

            Console.Out.Write(EncounterReport.Run(
                VillageContext.Load(tiles, Seed(args, 1979)), days, startHour,
                range > 0 ? range / 10f : EncounterReport.DefaultRange, regap));
            return 0;
        }

        /// <summary>
        /// Exits non-zero when a table is over the bar, because this one is a gate rather than a
        /// view: it is meant to be run before a population target is committed to, and to be
        /// able to say no.
        /// </summary>
        private static int CmdVocab(string[] args)
        {
            var outcome = VocabReport.Run(
                Flag(args, "--pop", 0), Flag(args, "--k", VocabReport.DefaultK),
                Flag(args, "--samples", VocabReport.DefaultSamples), Seed(args, 1979));

            Console.Out.Write(outcome.Text);
            return outcome.Failed ? 1 : 0;
        }

        private static int CmdEconomy(string[] args)
        {
            int tiles = Flag(args, "--tile", 1);
            var outcome = EconomyReport.Run(
                VillageContext.Load(tiles, Seed(args, 1979)),
                Flag(args, "--walk", EconomyReport.DefaultWalkMetres),
                Flag(args, "--pupils", EconomyReport.DefaultPupilsPerTeacher));

            Console.Out.Write(outcome.Text);
            return outcome.Failed ? 1 : 0;
        }

        /// <summary>
        /// The 2:1 rule. Returns zero however red it is, unlike `vocab` and `economy`.
        ///
        /// Those two are asked before a decision is committed to and are meant to be able to say
        /// no. This one is knowingly failing today and will be until the beats land, so a
        /// non-zero exit would be permanent, would get wrapped in `|| true` inside a week, and
        /// the instrument would stop being read. The gate that CAN say no is
        /// tools/Noir.Core.Tests/TwoToOneTests.cs, which is red on purpose and stays red.
        ///
        /// There is no --days. See Salience.DaysWatched for why: the number is meaningless
        /// without the day count, so the day count is not a flag.
        /// </summary>
        private static int CmdRatio(string[] args)
        {
            if (args.Length > 1 && int.TryParse(args[1], out int who) && who >= 0)
            {
                Console.Out.Write(Ratio.One(VillageContext.Load(1, Seed(args, 1979)), who));
                return 0;
            }

            Console.Out.Write(Ratio.Census(SeedList(args)));
            return 0;
        }

        /// <summary>`--seeds 1979,1980,1981`. One village is one village; three is the least
        /// that shows whether a number is a property of the design or of the draw.</summary>
        private static ulong[] SeedList(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "--seeds") continue;

                var parts = args[i + 1].Split(',');
                var seeds = new System.Collections.Generic.List<ulong>();
                foreach (string part in parts)
                    if (ulong.TryParse(part.Trim(), out ulong v)) seeds.Add(v);
                if (seeds.Count > 0) return seeds.ToArray();
            }
            return new[] { Seed(args, 1979) };
        }

        private static int CmdTestimony(string[] args)
        {
            int days = args.Length > 1 && int.TryParse(args[1], out int d) ? d : 14;
            return TestimonyReport.Run(days);
        }

        private static int CmdWatch(string[] args)
        {
            int start = args.Length > 1 && int.TryParse(args[1], out int s) ? s : 6;
            int hours = args.Length > 2 && int.TryParse(args[2], out int h) ? h : 18;
            int scale = args.Length > 3 && int.TryParse(args[3], out int sc) ? sc : 2;
            LiveView.Watch(VillageContext.Load(), start, hours,
                           minutesPerFrame: 5, scale: scale, frameDelayMs: 60);
            return 0;
        }
    }
}
