using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Text;
using Noir.Core.People;
using Noir.Core.Sim;
using Noir.Core.World;

namespace Noir.Bench
{
    /// <summary>
    /// The benchmark harness.
    ///
    /// It exists because every estimate on this project has been wrong at least once, and the
    /// only defence against that is a command that can be run again on the next build and
    /// diffed against the last one. Everything it prints is either a measurement or is labelled
    /// as a projection.
    /// </summary>
    public static class Program
    {
        private static readonly int[] DefaultPopulations = { 109, 250, 600, 1200, 2500, 5000 };
        private static readonly int[] QuickPopulations = { 109, 600, 2500 };

        private static readonly (int w, int h)[] DefaultMaps =
        {
            (170, 120), (340, 240), (680, 480), (1360, 960), (2720, 1920),
        };

        private static readonly (int w, int h)[] QuickMaps =
        {
            (170, 120), (680, 480), (2720, 1920),
        };

        public static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            var sections = new HashSet<string>();
            ulong seed = 1979;
            bool quick = false, machine = true, real = true, help = false;
            int[] populations = null;
            (int w, int h)[] mapSizes = null;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--quick": quick = true; break;
                    case "--no-machine": machine = false; break;
                    case "--no-real": real = false; break;
                    case "-h":
                    case "--help": help = true; break;
                    case "--seed":
                        seed = ulong.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--pops":
                        populations = ParseInts(args[++i]);
                        break;
                    case "--maps":
                        mapSizes = ParseSizes(args[++i]);
                        break;
                    default:
                        if (a.StartsWith("--")) { Usage(); return 1; }
                        sections.Add(a.ToLowerInvariant());
                        break;
                }
            }

            if (help) { Usage(); return 0; }

            if (sections.Count == 0 || sections.Contains("all"))
            {
                sections.Clear();
                sections.Add("tick");
                sections.Add("systems");
                sections.Add("path");
                sections.Add("build");
                sections.Add("mem");
            }

            populations ??= quick ? QuickPopulations : DefaultPopulations;
            var maps = mapSizes ?? (quick ? QuickMaps : DefaultMaps);

            TryRaisePriority();

            var r = new Report { EmitMachineLines = machine };
            Preamble(r, seed, quick, sections);

            // Before the warm-up, not after: the warm-up builds a synthetic settlement, and
            // reading a `place` line without a kinds table installed throws.
            RealVillage.EnsureKinds();

            Progress("warming the JIT");
            WarmUp(seed);

            double anchorBefore = Measure.MachineAnchor();
            double overhead = SystemsBench.TimerOverheadSeconds();

            // The settlements are built once and shared by every section that needs them.
            // Building a city twice is thirty seconds nobody gets back.
            List<Settlement> settlements = null;
            if (sections.Contains("tick") || sections.Contains("systems"))
            {
                settlements = new List<Settlement>();
                foreach (int pop in populations)
                {
                    Progress($"building synthetic settlement for {pop} people");
                    settlements.Add(Synthetic.Build("synth-" + pop, pop, seed));
                }
            }

            if (sections.Contains("tick"))
            {
                r.Heading("1. SIMULATION THROUGHPUT");
                Progress("throughput sweep");
                var results = TickBench.Sweep(r, populations, seed, settlements);
                TickBench.Ceiling(r, results);

                Progress("throughput through the day");
                TickBench.TimeOfDay(r, Nearest(settlements, 600));

                Progress("journey demand through the day");
                foreach (var s in Representative(settlements, 109, 600, 5000))
                    TickBench.Demand(r, s);

                Progress("the worst minute of the day");
                TickBench.WorstMinute(r, settlements, overhead);

                if (real && RealVillage.Available) RealComparison(r, settlements, seed);
            }

            if (sections.Contains("systems"))
            {
                r.Heading("2. WHERE A TICK GOES");
                r.Line($"  Stopwatch overhead per reading: {overhead * 1e9:0.0} ns, subtracted from");
                r.Line("  every per-tick figure below.");
                r.Line();

                foreach (var s in Representative(settlements, 109, 600, 5000))
                {
                    Progress($"tick breakdown at {s.People.Count} people");
                    SystemsBench.Breakdown(r, s, overhead);
                }

                Progress("growth orders");
                SystemsBench.Growth(r, settlements, overhead);

                Progress("cost of a place");
                SystemsBench.PerPlaceCost(r, 600, seed);
            }

            if (sections.Contains("path"))
            {
                r.Heading("3. PATHFINDING");
                Progress("pathfinding sweep");
                PathBench.Report(r, seed, maps, real);
            }

            if (sections.Contains("build"))
            {
                r.Heading("4. WORLD BUILD");

                if (real && RealVillage.Available)
                {
                    Progress("build stages, the fixture village");
                    BuildBench.Stages(r, RealVillage.Layout(), "the fixture village (authored)", seed, 61);
                }

                Progress("build stages, town");
                var (tw, th) = Synthetic.MapFor(600);
                BuildBench.Stages(r, Synthetic.Layout(tw, th, 600, seed), "synthetic town, 600", seed, 41);

                Progress("build stages, city");
                var (cw, ch) = Synthetic.MapFor(5000);
                BuildBench.Stages(r, Synthetic.Layout(cw, ch, 5000, seed), "synthetic city, 5000", seed, 15);

                Progress("build against area");
                BuildBench.AreaSweep(r, seed, maps, 109);

                Progress("build against place count");
                BuildBench.PlaceSweep(r, seed, 2500, new[] { 20, 60, 150, 300, 600 });
            }

            if (sections.Contains("mem"))
            {
                r.Heading("5. MEMORY");
                Progress("memory");
                MemoryBench.Report(r, seed, quick ? new[] { 109, 600 } : new[] { 109, 600, 5000 });
            }

            double anchorAfter = Measure.MachineAnchor();
            Anchor(r, anchorBefore, anchorAfter);

            Console.Out.Write(r.ToString());
            Console.Out.Flush();
            return 0;
        }

        private static void Anchor(Report r, double before, double after)
        {
            double drift = before > 0 ? (after - before) / before * 100.0 : 0;

            r.Heading("MACHINE ANCHOR");
            r.Line($"  A fixed arithmetic workload, timed before and after the run. It contains no");
            r.Line($"  Noir.Core code at all, so it cannot move when the simulation does.");
            r.Line();
            r.Line($"    before   {before * 1000:0.000} ms");
            r.Line($"    after    {after * 1000:0.000} ms   ({drift:+0.0;-0.0;0} % drift)");
            r.Line();
            r.Line("  Divide two runs' anchors to compare their absolute numbers. A run whose anchor");
            r.Line("  drifted more than a few per cent was measured on a machine that changed under");
            r.Line("  it, and its own spread columns will not have said so.");
            r.Line();

            r.M("anchor", ("ms_before", before * 1000), ("ms_after", after * 1000),
                ("drift_pct", drift));
        }

        /// <summary>
        /// Run every hot path once, hard, before anything is timed.
        ///
        /// The runtime compiles a method cheaply the first time and recompiles it properly once
        /// it has been called enough — on a background thread, so the swap lands whenever it
        /// lands. Without this, the first section of a run is measured against half-optimised
        /// code and the same section measured later comes out twice as fast, which looks
        /// exactly like a regression and is not one.
        /// </summary>
        private static void WarmUp(ulong seed)
        {
            var s = Synthetic.Build("warmup", 250, seed);

            var sim = new Simulation(s.World, s.People, s.Seed, TickBench.PeakMinute);
            sim.Tick(150000);

            var pf = new Pathfinder(s.World.Grid);
            var buffer = new List<Noir.Core.Contracts.Tile>();
            foreach (var (from, to) in PathBench.Trips(s.World.Grid, 200, 0.1, 0.4, seed))
                pf.TryFindPath(from, to, buffer);

            for (int i = 0; i < s.People.Count; i++)
                DayPlanner.Plan(s.World, s.People, s.People.Get(new Noir.Core.Contracts.CitizenId(i)), 1, seed);

            GC.KeepAlive(WorldBuilder.Build(Synthetic.Layout(200, 150, 100, seed), seed));
            Measure.SettledHeap();
        }

        private static void RealComparison(Report r, List<Settlement> settlements, ulong seed)
        {
            Progress("the fixture village against a synthetic one of the same size");

            var ashcombe = RealVillage.Load(seed);
            var synthetic = Nearest(settlements, ashcombe.People.Count);

            var table = new Table("The authored village against a synthetic one, 08:30-08:35",
                new Col("world", 12, right: false), new Col("pop", 6), new Col("map", 11, right: false),
                new Col("tiles/head", 11), new Col("places", 7), new Col("rooms", 7),
                new Col("ticks/s", 10), new Col("us/tick", 9), new Col("spread", 7));

            foreach (var s in new[] { ashcombe, synthetic })
            {
                var result = TickBench.Measure1(s, TickBench.PeakMinute);
                table.Row(s.Label, s.People.Count, s.MapSize,
                          (s.Tiles / (double)s.People.Count).ToString("0"),
                          s.World.PlaceCount, s.World.RoomCount,
                          result.TicksPerSecond, result.MicrosPerTick, result.SecondsPerTick.SpreadCell);

                r.M("real_vs_synth", ("world", s.Label), ("pop", s.People.Count),
                    ("w", s.World.Width), ("h", s.World.Height), ("places", s.World.PlaceCount),
                    ("tiles_per_head", s.Tiles / (double)s.People.Count),
                    ("tps", result.TicksPerSecond), ("us_tick", result.MicrosPerTick));
            }

            table.Note("If these two disagree by much, the synthetic generator is not a stand-in for the");
            table.Note("real thing and every projection built on it needs re-reading. Read the gap against");
            table.Note("tiles/head: the lattice is looser than the fixture village, journeys are correspondingly");
            table.Note("longer, and pathfinding is the dominant cost — so the synthetic numbers are the");
            table.Note("pessimistic end of the range, not the optimistic one.");
            r.Add(table);
        }

        /// <summary>
        /// The settlements nearest the given sizes, each appearing once. Without the
        /// de-duplication a sweep that stops at 2,500 prints the same 600-person breakdown
        /// twice, once labelled as the village and once as the city.
        /// </summary>
        private static IEnumerable<Settlement> Representative(List<Settlement> settlements, params int[] wanted)
        {
            var seen = new HashSet<Settlement>();
            foreach (int want in wanted)
            {
                var s = Nearest(settlements, want);
                if (s == null || !seen.Add(s)) continue;
                if (Math.Abs(s.People.Count - want) > want) continue;
                yield return s;
            }
        }

        private static Settlement Nearest(List<Settlement> settlements, int wanted)
        {
            if (settlements == null || settlements.Count == 0) return null;

            Settlement best = settlements[0];
            foreach (var s in settlements)
                if (Math.Abs(s.People.Count - wanted) < Math.Abs(best.People.Count - wanted))
                    best = s;
            return best;
        }

        private static void Preamble(Report r, ulong seed, bool quick, HashSet<string> sections)
        {
            var sb = new StringBuilder();
            sb.Append("NOIR BENCHMARK\n");
            sb.Append("==============\n\n");
            sb.Append($"  seed          {seed}\n");
            sb.Append($"  sections      {string.Join(", ", Ordered(sections))}{(quick ? "  (--quick)" : "")}\n");
            sb.Append($"  runtime       .NET {Environment.Version}, {RuntimeInformation()}\n");
            sb.Append($"  code          Noir.Core {Optimisation(typeof(Noir.Core.Sim.Simulation).Assembly)}, " +
                      $"harness {Optimisation(typeof(Program).Assembly)}\n");
            sb.Append($"  gc            {(GCSettings.IsServerGC ? "server" : "workstation")}, " +
                      $"{(GCSettings.LatencyMode)}\n");
            sb.Append($"  cpus          {Environment.ProcessorCount}\n");
            sb.Append($"  content       {(RealVillage.Available ? RealVillage.Root : "not found — synthetic only")}\n");
            sb.Append($"  run at        {DateTime.Now:yyyy-MM-dd HH:mm}\n");
            sb.Append('\n');
            sb.Append("  The budget throughout is 6,000 ticks per real second: 20 Hz at 300x, a whole\n");
            sb.Append("  day in five minutes. Numbers are CoreCLR JIT; Unity's IL2CPP will differ, and\n");
            sb.Append("  the ratios between systems will survive that better than the absolutes.\n");
            r.Line(sb.ToString());
        }

        private static IEnumerable<string> Ordered(HashSet<string> sections)
        {
            foreach (string s in new[] { "tick", "systems", "path", "build", "mem" })
                if (sections.Contains(s)) yield return s;
        }

        /// <summary>
        /// Whether the JIT was allowed to optimise an assembly. An unoptimised Noir.Core makes
        /// every number in the run worthless, and it is the single easiest mistake to make with
        /// a benchmark, so the run says which it got rather than assuming.
        /// </summary>
        private static string Optimisation(System.Reflection.Assembly assembly)
        {
            var flag = (DebuggableAttribute)Attribute.GetCustomAttribute(assembly, typeof(DebuggableAttribute));
            bool unoptimised = flag != null && flag.IsJITOptimizerDisabled;
            return unoptimised ? "UNOPTIMISED — numbers are meaningless" : "optimised";
        }

        private static string RuntimeInformation() =>
            System.Runtime.InteropServices.RuntimeInformation.OSDescription + " / " +
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;

        /// <summary>
        /// Progress goes to stderr so that stdout stays a clean artefact that can be redirected
        /// to a file and diffed against the last build without any filtering.
        /// </summary>
        private static void Progress(string what) =>
            Console.Error.WriteLine("  ... " + what);

        private static void TryRaisePriority()
        {
            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            }
            catch
            {
                // Not permitted here; the numbers get a little noisier and say so in the spread.
            }
        }

        private static int[] ParseInts(string csv)
        {
            string[] parts = csv.Split(',');
            var values = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                values[i] = int.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
            return values;
        }

        private static (int w, int h)[] ParseSizes(string csv)
        {
            string[] parts = csv.Split(',');
            var sizes = new (int, int)[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                string[] wh = parts[i].Trim().Split('x', 'X');
                if (wh.Length != 2) throw new FormatException("map sizes look like 680x480");
                sizes[i] = (int.Parse(wh[0], CultureInfo.InvariantCulture),
                            int.Parse(wh[1], CultureInfo.InvariantCulture));
            }
            return sizes;
        }

        private static void Usage()
        {
            Console.Error.WriteLine(
                "usage: Noir.Bench [section ...] [options]\n" +
                "\n" +
                "sections (default: all of them)\n" +
                "  tick        throughput against the 6,000 ticks/s budget, by population\n" +
                "  systems     where a tick goes, and which part grows worst\n" +
                "  path        A* cost per query, on the real map and on synthetic grids\n" +
                "  build       WorldBuilder.Build stage by stage, against area and place count\n" +
                "  mem         bytes per tile, per citizen, per place, projected to city scale\n" +
                "\n" +
                "options\n" +
                "  --pops a,b,c   populations for the sweeps (default 109,250,600,1200,2500,5000)\n" +
                "  --maps WxH,..  grid sizes for pathfinding and world build\n" +
                "                 (default 170x120,340x240,680x480,1360x960,2720x1920)\n" +
                "  --seed n       master seed (default 1979)\n" +
                "  --quick        fewer sizes and fewer repetitions\n" +
                "  --no-real      ignore Content/ and measure synthetic worlds only\n" +
                "  --no-machine   omit the trailing #M key=value lines\n" +
                "\n" +
                "Results go to stdout, progress to stderr, so `> run.txt` gives a diffable artefact.");
        }
    }
}
