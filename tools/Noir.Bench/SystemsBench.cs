using System;
using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Sim;

namespace Noir.Bench
{
    /// <summary>
    /// What the tick is actually spending its time on.
    ///
    /// Simulation exposes no counters and this harness is not allowed to add any, so the
    /// breakdown is built out of three things that need no instrumentation: timing individual
    /// ticks and bucketing them by which work they were due to do, ablating a system through a
    /// public knob and taking the difference, and running a public routine the tick calls
    /// directly. Every row below says which of the three it is, because a differenced number
    /// and a directly measured one do not deserve the same confidence.
    /// </summary>
    public static class SystemsBench
    {
        /// <summary>
        /// What one Stopwatch reading costs, so per-tick timings can be corrected for it.
        /// At village scale a tick is a few microseconds and the timer is tens of nanoseconds;
        /// left uncorrected that is a percent or two of pure invention.
        /// </summary>
        public static double TimerOverheadSeconds()
        {
            const int n = 200000;
            for (int i = 0; i < 1000; i++) { Measure.Now(); Measure.Now(); }

            long t0 = Measure.Now();
            for (int i = 0; i < n; i++) { Measure.Now(); Measure.Now(); }
            return Measure.Since(t0) / n;
        }

        public readonly struct Buckets
        {
            public readonly double PlainMedian;
            public readonly double ScanMedian;
            public readonly double PlainP95;
            public readonly double ScanP95;
            public readonly int PlainCount;
            public readonly int ScanCount;

            public Buckets(double plainMedian, double plainP95, int plainCount,
                           double scanMedian, double scanP95, int scanCount)
            {
                PlainMedian = plainMedian;
                PlainP95 = plainP95;
                PlainCount = plainCount;
                ScanMedian = scanMedian;
                ScanP95 = scanP95;
                ScanCount = scanCount;
            }

            /// <summary>What the conversation sweep costs on the one tick in twenty that runs it.</summary>
            public double ScanCost => ScanMedian - PlainMedian;
        }

        /// <summary>
        /// Time every tick separately and sort them into the nineteen that do not run the
        /// conversation sweep and the one that does.
        ///
        /// The sweep is gated to once a game second precisely so it stays off the per-tick
        /// path, so the only honest way to price it is to look at the tick it lands on.
        /// </summary>
        public static Buckets Bucketed(Settlement s, int startMinute, int ticks, double overhead,
                                       int reps = 3)
        {
            var plain = new double[ticks * reps];
            var scan = new double[(ticks / GameClock.TicksPerSecond + 2) * reps];
            int plainCount = 0, scanCount = 0;

            // Samples from every repetition are pooled rather than each run being summarised
            // and the summaries averaged. The distribution is what is interesting here — one
            // tick in twenty is genuinely a different tick — and pooling keeps it intact.
            for (int rep = 0; rep < reps; rep++)
            {
                var sim = new Simulation(s.World, s.People, s.Seed, startMinute);
                sim.Tick(TickBench.SettleTicks);

                for (int t = 0; t < ticks; t++)
                {
                    long t0 = Measure.Now();
                    sim.Tick();
                    double seconds = Measure.Since(t0) - overhead;
                    if (seconds < 0) seconds = 0;

                    if (sim.Clock.Tick % GameClock.TicksPerSecond == 0)
                    {
                        if (scanCount < scan.Length) scan[scanCount++] = seconds;
                    }
                    else if (plainCount < plain.Length)
                    {
                        plain[plainCount++] = seconds;
                    }
                }
            }

            var p = Measure.Percentiles(plain, plainCount);
            var c = Measure.Percentiles(scan, scanCount);
            return new Buckets(p.median, p.p95, plainCount, c.median, c.p95, scanCount);
        }

        /// <summary>Median seconds per tick with the path budget forced to a given value.</summary>
        private static double AtBudget(Settlement s, int startMinute, int budget, int ticks, int reps = 3)
        {
            var values = new double[reps];
            for (int r = -1; r < reps; r++)
            {
                var sim = new Simulation(s.World, s.People, s.Seed, startMinute)
                {
                    PathBudgetPerTick = budget
                };

                // The same settle as every other measurement, so the only difference between
                // the three budgets is the budget. With it at zero nobody ever sets off, which
                // is exactly the ablation wanted: a tick with the journeys taken out of it.
                sim.Tick(TickBench.SettleTicks);
                Measure.SettledHeap();

                long t0 = Measure.Now();
                sim.Tick(ticks);
                double seconds = Measure.Since(t0) / ticks;
                if (r >= 0) values[r] = seconds;
            }
            return new Stat(values).Median;
        }

        public static void Breakdown(Report r, Settlement s, double overhead)
        {
            var buckets = Bucketed(s, TickBench.PeakMinute, TickBench.TimedTicks, overhead);

            double full = AtBudget(s, TickBench.PeakMinute, 12, TickBench.TimedTicks);
            double frozen = AtBudget(s, TickBench.PeakMinute, 0, TickBench.TimedTicks);
            double unlimited = AtBudget(s, TickBench.PeakMinute, int.MaxValue, TickBench.TimedTicks);

            double planAll = PlanWholePopulation(s);

            int n = s.People.Count;
            var table = new Table(
                "Where a tick goes — " + s.Label + " (" + n + " people, " +
                s.World.PlaceCount + " places, " + s.MapSize + "), 08:00",
                new Col("system", 34, right: false), new Col("us", 10),
                new Col("how often", 20, right: false), new Col("scans all agents", 17, right: false),
                new Col("measured by", 13, right: false));

            // A difference of two noisy measurements can come out below zero, and printing
            // "-0.01 us" as though it were a cost is worse than saying nothing. Anything under
            // two percent of the whole tick is reported as what it is: too small to resolve.
            string Delta(double seconds) =>
                seconds <= buckets.PlainMedian * 0.02
                    ? "< noise"
                    : (seconds * 1e6).ToString("#,0.00");

            table.Row("MEDIAN tick, no conversation sweep", buckets.PlainMedian * 1e6, "19 ticks in 20",
                      "yes, once", "direct");
            table.Row("MEDIAN tick, conversation second", buckets.ScanMedian * 1e6,
                      "1 tick in 20", "yes, pairwise", "direct");
            table.Row("  of which the conversation sweep", Delta(buckets.ScanCost),
                      "1 tick in 20", "O(n^2) pairs", "difference");
            table.Gap();
            table.Row("MEAN tick over the window", full * 1e6, "every tick",
                      "", "direct");
            table.Row("  journeys: movement + A*", Delta(full - frozen), "every tick",
                      "no, travellers", "ablation");
            table.Row("  everything else: plans, queues", frozen * 1e6, "every tick",
                      "yes, once", "ablation");
            table.Row("MEAN tick, path budget removed", unlimited * 1e6, "every tick",
                      "no, travellers", "ablation");
            table.Gap();
            table.Row("day rollover: all DayPlans", planAll * 1e6, "1 tick in 1,728,000",
                      "yes, once", "direct");

            table.Note("us = microseconds. The medians describe a typical tick; the means cover the same");
            table.Note("five game-minutes including the ticks that start a journey clump. Where the mean");
            table.Note($"is far above the median — here {full / Math.Max(buckets.PlainMedian, 1e-12):0.0}x — " +
                       "the cost is not spread across the window, it is");
            table.Note("piled onto a handful of ticks. Ablation rows are differences of two runs.");
            table.Note($"One tick of budget at 6,000 ticks/s = {1e6 / TickBench.Budget:0.0} us.");
            r.Add(table);

            if (full > buckets.PlainMedian * 4)
                r.Finding("mean-vs-median", $"At {n:#,0} people the MEAN tick over the school run ({full * 1e6:#,0} us) is " +
                          $"{full / Math.Max(buckets.PlainMedian, 1e-12):0} times the MEDIAN tick " +
                          $"({buckets.PlainMedian * 1e6:#,0.0} us). The simulation is not steadily " +
                          "expensive; it is cheap with spikes, and the spikes are pathfinding.");

            r.M("breakdown", ("pop", n), ("places", s.World.PlaceCount),
                ("us_plain", buckets.PlainMedian * 1e6), ("us_scan_tick", buckets.ScanMedian * 1e6),
                ("us_conversation", buckets.ScanCost * 1e6),
                ("us_journeys", (full - frozen) * 1e6), ("us_frozen", frozen * 1e6),
                ("us_unbudgeted", unlimited * 1e6), ("us_day_rollover", planAll * 1e6),
                ("p95_plain_us", buckets.PlainP95 * 1e6), ("p95_scan_us", buckets.ScanP95 * 1e6));

            double rolloverTicks = planAll / buckets.PlainMedian;
            r.Line($"  The day rollover costs {planAll * 1000:0.0} ms on one tick — " +
                   $"{rolloverTicks:#,0} ordinary ticks' worth, on the single tick where midnight lands.");
            r.Line();

            r.Finding("day-rollover", $"Midnight is a hitch, not a smooth boundary: Simulation.EnsurePlans rebuilds every " +
                      $"DayPlan on one tick, {planAll * 1000:0.0} ms at {n:#,0} people. The plan document " +
                      "claims the pure-function DayPlan means \"no day-boundary hitch where a hundred " +
                      "schedules regenerate on one tick\"; the simulation regenerates all of them anyway.");

            if (buckets.ScanCost > 0)
                r.Finding("conversation", $"The conversation sweep costs {buckets.ScanCost * 1e6:#,0.0} us on the one tick in " +
                          $"twenty that runs it, against {buckets.PlainMedian * 1e6:0.00} us for an ordinary " +
                          "tick. It is the only all-pairs scan in the tick and the only thing here that " +
                          "grows quadratically.");
        }

        /// <summary>
        /// Every DayPlan in the settlement, which is exactly what Simulation.EnsurePlans does
        /// on the first tick of each day.
        /// </summary>
        public static double PlanWholePopulation(Settlement s)
        {
            var stat = Measure.PerRun(() =>
            {
                for (int i = 0; i < s.People.Count; i++)
                    DayPlanner.Plan(s.World, s.People, s.People.Get(new CitizenId(i)), 1, s.Seed);
            }, reps: 5, warmups: 2);
            return stat.Median;
        }

        // ---- what a place costs, separately from what a person costs ----

        /// <summary>
        /// Price the per-place work in a tick by adding places and nothing else.
        ///
        /// ServeCounters walks every place in the world on every tick and skips the ones with
        /// no counter, so its cost is a function of place count rather than of how many people
        /// are queuing. That is invisible in a population sweep, where places and people grow
        /// together; it needs one variable held still.
        /// </summary>
        public static void PerPlaceCost(Report r, int population, ulong seed, int extra = 400)
        {
            // Both worlds are built on one map, sized up front to hold the settlement AND the
            // extra places. Dwellings stop as soon as the unit target is met and the unit draws
            // happen in the same order either way, so the two populations are identical by
            // construction — and the table checks that rather than assuming it.
            var (w, h) = Synthetic.MapFor(population);
            int rows = Synthetic.RowsIn(h);
            w += ((extra + rows - 1) / rows) * Synthetic.PitchX;

            var lean = Synthetic.Build("lean", w, h, population, seed);
            var fat = Synthetic.Build("fat", w, h, population, seed, extraPlaces: extra);

            var table = new Table("Cost of a place, holding population and map still — 03:00, nobody moving",
                new Col("world", 8, right: false), new Col("pop", 7), new Col("places", 8),
                new Col("us/tick", 10), new Col("spread", 7));

            // Three in the morning, deliberately. The per-place loop runs on every tick whatever
            // else is happening, and measuring it during the school run means looking for a
            // thirty-microsecond difference underneath a thousand microseconds of pathfinding.
            // In the dead hour the tick is nothing but the two per-thing loops.
            const int quietMinute = 3 * 60;
            var a = TickBench.Measure1(lean, quietMinute, reps: 15);
            var b = TickBench.Measure1(fat, quietMinute, reps: 15);

            table.Row("lean", lean.People.Count, lean.World.PlaceCount,
                      a.MicrosPerTick, a.SecondsPerTick.SpreadCell);
            table.Row("+" + extra, fat.People.Count, fat.World.PlaceCount,
                      b.MicrosPerTick, b.SecondsPerTick.SpreadCell);

            if (lean.People.Count == fat.People.Count && fat.World.PlaceCount > lean.World.PlaceCount)
            {
                double per = (b.MicrosPerTick - a.MicrosPerTick) * 1000.0 /
                             (fat.World.PlaceCount - lean.World.PlaceCount);
                table.Note($"marginal cost per place per tick: {per:0.00} ns. Extra places are phone boothes:");
                table.Note("no jobs, no counter, nobody ever goes to one, one tile each.");

                if (a.SecondsPerTick.Noisy || b.SecondsPerTick.Noisy)
                {
                    table.Note("CAUTION: at least one of the two rows is noisy and this is their difference.");
                    table.Note("Read the per-place figure as an order of magnitude, not a measurement.");
                }
                r.M("perplace", ("pop", lean.People.Count),
                    ("places_lean", lean.World.PlaceCount), ("places_fat", fat.World.PlaceCount),
                    ("ns_per_place_tick", per));
            }
            else
            {
                table.Note("populations differ between the two worlds — this comparison is void.");
            }

            r.Add(table);
        }

        // ---- growth orders ----

        public static void Growth(Report r, IReadOnlyList<Settlement> settlements, double overhead)
        {
            var table = new Table("Growth order, measured — the same five game-minutes at every size",
                new Col("pop", 7), new Col("plain tick us", 14), new Col("^", 6),
                new Col("conversation us", 16), new Col("^", 6),
                new Col("day rollover ms", 16), new Col("^", 6));

            double lastPop = 0, lastPlain = 0, lastScan = 0, lastPlan = 0;

            foreach (var s in settlements)
            {
                var b = Bucketed(s, TickBench.PeakMinute, TickBench.TimedTicks, overhead);
                double plan = PlanWholePopulation(s);
                double pop = s.People.Count;
                double scan = Math.Max(b.ScanCost, 1e-9);

                table.Row(s.People.Count,
                          b.PlainMedian * 1e6,
                          lastPop > 0 ? TickBench.Exponent(lastPop, lastPlain, pop, b.PlainMedian).ToString("0.00") : "",
                          scan * 1e6,
                          lastPop > 0 ? TickBench.Exponent(lastPop, lastScan, pop, scan).ToString("0.00") : "",
                          plan * 1000,
                          lastPop > 0 ? TickBench.Exponent(lastPop, lastPlan, pop, plan).ToString("0.00") : "");

                r.M("growth", ("pop", s.People.Count), ("us_plain", b.PlainMedian * 1e6),
                    ("us_conversation", scan * 1e6), ("ms_day_rollover", plan * 1000));

                lastPop = pop;
                lastPlain = b.PlainMedian;
                lastScan = scan;
                lastPlan = plan;
            }

            table.Note("'^' is the exponent k in cost ~ pop^k, fitted against the row above.");
            table.Note("1.0 = linear (a pass over all agents). 2.0 = every pair of agents.");
            r.Add(table);
        }
    }
}
