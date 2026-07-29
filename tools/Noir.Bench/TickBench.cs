using System;
using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Sim;

namespace Noir.Bench
{
    /// <summary>
    /// How many ticks per real second the simulation sustains, and how far that is from the
    /// budget.
    ///
    /// The budget is 6,000 ticks per real second: 20 Hz at 300x, which is the speed at which a
    /// whole day passes in five minutes. Everything else in this file exists to answer one
    /// question — at what population does that stop being possible.
    /// </summary>
    public static class TickBench
    {
        public const double Budget = 6000.0;

        /// <summary>
        /// Twenty past eight, plus ten game-minutes of settling, so the timed window opens
        /// exactly on the half-past bell — the busiest minute the day plans contain.
        ///
        /// The settling matters more than the hour. The constructor places everybody exactly
        /// where their plan says they should be at that minute, so a run timed straight out of
        /// it measures a village where nobody is walking anywhere, which is not the village and
        /// is not the expensive case. Five minutes later than this and it is the wrong window
        /// in the other direction: most of the school run has already arrived, and the sweep
        /// ends up measuring the tail of the peak rather than the peak.
        /// </summary>
        public const int PeakMinute = 8 * 60 + 20;

        /// <summary>Ten game-minutes. Long enough to have journeys in progress, short enough to run.</summary>
        public const int SettleTicks = 12000;

        /// <summary>
        /// Five game-minutes, the same five for every row of every sweep.
        ///
        /// Timing to a wall-clock target instead — which is the obvious thing to do — quietly
        /// destroys the comparison. A fast configuration ends up covering an hour and a half of
        /// game time and a slow one covers twenty seconds, so the fast row is averaging over
        /// quiet stretches the slow row never reaches, and the two are no longer measurements
        /// of the same thing. A fixed window of ticks costs some precision on the small sizes
        /// and buys a sweep whose rows can be divided by one another.
        /// </summary>
        public const int TimedTicks = 6000;

        public sealed class Result
        {
            public Settlement Settlement;
            public int StartMinute;
            public Stat SecondsPerTick;
            public int TicksTimed;
            public double AllocBytesPerTick;
            public int Walking;
            public double TicksPerSecond => 1.0 / SecondsPerTick.Median;
            public double MicrosPerTick => SecondsPerTick.Median * 1e6;
            public double Headroom => TicksPerSecond / Budget;
        }

        /// <summary>
        /// More repetitions where they are cheap.
        ///
        /// The median of five is fragile when one run in five is interrupted, and at village
        /// scale a repetition costs ten milliseconds — there is no reason to be stingy. At city
        /// scale each one costs seconds, so five it is, and the spread column says so.
        /// </summary>
        public static int RepsFor(Settlement s) => s.People.Count <= 1500 ? 9 : 5;

        public static Result Measure1(Settlement s, int startMinute, int reps = 0,
                                      int ticks = TimedTicks)
        {
            if (reps <= 0) reps = RepsFor(s);
            var values = new double[reps];

            double alloc = 0;
            int walking = 0;

            for (int r = -1; r < reps; r++)
            {
                var sim = new Simulation(s.World, s.People, s.Seed, startMinute);
                sim.Tick(SettleTicks);
                Measure.SettledHeap();

                if (r == 0)
                {
                    for (int i = 0; i < sim.AgentCount; i++)
                        if (sim.GetAgent(i).Travelling) walking++;
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                long t0 = Measure.Now();
                sim.Tick(ticks);
                double seconds = Measure.Since(t0);
                long after = GC.GetAllocatedBytesForCurrentThread();

                if (r >= 0)
                {
                    values[r] = seconds / ticks;
                    if (r == 0) alloc = (after - before) / (double)ticks;
                }
            }

            return new Result
            {
                Settlement = s,
                StartMinute = startMinute,
                SecondsPerTick = new Stat(values),
                TicksTimed = ticks,
                AllocBytesPerTick = alloc,
                Walking = walking,
            };
        }

        // ---- how much pathfinding the tick is being asked for ----

        /// <summary>
        /// Journeys demanded per minute of the day, taken from the day plans themselves.
        ///
        /// A journey begins on the tick where a citizen's current block changes, and blocks
        /// change on minute boundaries — so every journey a day contains lands on one of 1,440
        /// ticks out of 1,728,000, and they land in clumps. Reading it off the plans rather
        /// than by watching the simulation is both exact and cheap; watching would need a whole
        /// simulated day per row.
        ///
        /// This is the number that decides whether <c>PathBudgetPerTick</c> is a smoothing
        /// device or a bottleneck. Twelve a tick clears a clump of forty in four ticks, which
        /// nobody sees. It does not clear a clump of four hundred.
        /// </summary>
        public static int[] DemandByMinute(Settlement s, int day = 0)
        {
            var histogram = new int[1440];

            for (int i = 0; i < s.People.Count; i++)
            {
                var plan = DayPlanner.Plan(s.World, s.People, s.People.Get(new CitizenId(i)), day, s.Seed);
                var blocks = plan.Blocks;

                // The first block starts at midnight and is where they already are, so it is
                // not a journey. Every other block start is one.
                for (int b = 1; b < blocks.Count; b++)
                {
                    if (blocks[b].Where == blocks[b - 1].Where) continue;
                    histogram[blocks[b].StartMinute]++;
                }
            }
            return histogram;
        }

        public static void Demand(Report r, Settlement s)
        {
            var histogram = DemandByMinute(s);

            var table = new Table("Journey demand through the day — " + s.Label +
                                  " (" + s.People.Count + " people), from the day plans",
                new Col("hour", 6, right: false), new Col("journeys", 9),
                new Col("busiest minute", 15, right: false), new Col("in it", 7),
                new Col("ticks to clear", 15), new Col("last one leaves", 16));

            int dayPeak = 0, dayPeakMinute = 0, dayTotal = 0;

            for (int hour = 0; hour < 24; hour++)
            {
                int total = 0, peak = 0, peakMinute = hour * 60;
                for (int m = hour * 60; m < hour * 60 + 60; m++)
                {
                    total += histogram[m];
                    if (histogram[m] > peak) { peak = histogram[m]; peakMinute = m; }
                }

                dayTotal += total;
                if (peak > dayPeak) { dayPeak = peak; dayPeakMinute = peakMinute; }
                if (total == 0) continue;

                int ticks = (peak + 11) / 12;
                table.Row(hour.ToString("00") + ":00", total,
                          $"{peakMinute / 60:00}:{peakMinute % 60:00}", peak,
                          ticks, (ticks / 20.0).ToString("0.00") + " s late");
            }

            table.Note("ticks to clear = ceil(peak / PathBudgetPerTick), at the shipping budget of 12.");
            table.Note("'last one leaves' is how far behind their own schedule the final citizen in the");
            table.Note("clump sets off. Under a second is invisible; several seconds is a visible stagger.");
            r.Add(table);

            r.M("demand", ("pop", s.People.Count), ("journeys_per_day", dayTotal),
                ("peak_minute", dayPeakMinute), ("peak", dayPeak),
                ("ticks_to_clear", (dayPeak + 11) / 12));

            double late = ((dayPeak + 11) / 12) / 20.0;
            r.Line($"  Busiest minute of the day is {dayPeakMinute / 60:00}:{dayPeakMinute % 60:00} " +
                   $"with {dayPeak:#,0} journeys starting at once; at PathBudgetPerTick = 12 the last " +
                   $"of them sets off {late:0.00} s late.");
            r.Line();

            if (late > 1.0)
                r.Finding("clump-latency", $"At {s.People.Count:#,0} people the busiest minute of the day demands {dayPeak:#,0} " +
                          $"journeys on a single tick. PathBudgetPerTick = 12 spreads that over " +
                          $"{(dayPeak + 11) / 12:#,0} ticks, so the last citizen sets off {late:0.0} s after " +
                          "the first — the school run visibly dribbles out instead of leaving together.");
        }

        // ---- the sweep ----

        public static List<Result> Sweep(Report r, int[] populations, ulong seed,
                                         IReadOnlyList<Settlement> settlements)
        {
            var table = new Table("Simulation throughput, 08:30-08:35 — the school run, as it happens",
                new Col("target", 6), new Col("pop", 6), new Col("map", 11, right: false),
                new Col("places", 7), new Col("walking", 8), new Col("ticks/s", 11),
                new Col("us/tick", 9), new Col("spread", 7), new Col("x budget", 9),
                new Col("max speed", 10), new Col("B/tick", 8));

            var results = new List<Result>();

            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                var result = Measure1(s, PeakMinute);
                results.Add(result);

                table.Row(populations[i], s.People.Count, s.MapSize, s.World.PlaceCount,
                          result.Walking, result.TicksPerSecond.ToString("#,0"),
                          result.MicrosPerTick, result.SecondsPerTick.SpreadCell,
                          result.Headroom.ToString("0.00") + "x",
                          (result.TicksPerSecond / 20.0).ToString("#,0") + "x",
                          result.AllocBytesPerTick);

                r.M("tick", ("target", populations[i]), ("pop", s.People.Count),
                    ("w", s.World.Width), ("h", s.World.Height), ("places", s.World.PlaceCount),
                    ("walking", result.Walking),
                    ("minute", PeakMinute), ("tps", result.TicksPerSecond),
                    ("us_tick", result.MicrosPerTick), ("spread_pct", result.SecondsPerTick.SpreadPercent),
                    ("headroom", result.Headroom), ("alloc_b_tick", result.AllocBytesPerTick));
            }

            table.Note("budget = 6,000 ticks/s (20 Hz at 300x). max speed = ticks/s / 20.");
            table.Note("B/tick = bytes allocated per tick; anything above zero is GC pressure in Unity.");
            r.Add(table);

            return results;
        }

        public static void TimeOfDay(Report r, Settlement s)
        {
            var table = new Table("Cost through the day — " + s.Label + " (" + s.People.Count + " people)",
                new Col("window", 13, right: false), new Col("ticks/s", 11), new Col("us/tick", 9),
                new Col("spread", 7), new Col("x budget", 9), new Col("walking", 8),
                new Col("of pop", 8));

            // Chosen from the demand histogram rather than on the hour: the interesting minutes
            // of the day are the ones a whole settlement moves on, and a table sampled at
            // o'clock manages to miss every single one of them.
            int[] minutes = { 0, 3 * 60, 6 * 60, 8 * 60 + 30, 12 * 60,
                              14 * 60, 15 * 60 + 30, 17 * 60 + 30, 19 * 60, 22 * 60 };

            foreach (int minute in minutes)
            {
                int start = minute - 10;
                if (start < 0) start += 1440;

                var result = Measure1(s, start);

                table.Row($"{minute / 60:00}:{minute % 60:00}-{(minute + 5) / 60:00}:{(minute + 5) % 60:00}",
                          result.TicksPerSecond.ToString("#,0"),
                          result.MicrosPerTick, result.SecondsPerTick.SpreadCell,
                          result.Headroom.ToString("0.00") + "x", result.Walking,
                          (result.Walking * 100.0 / s.People.Count).ToString("0.0") + "%");

                r.M("hour", ("pop", s.People.Count), ("minute", minute),
                    ("tps", result.TicksPerSecond), ("us_tick", result.MicrosPerTick),
                    ("walking", result.Walking));
            }

            table.Note("Each row starts ten game-minutes early and settles, so the window shown is the");
            table.Note("part that is timed. 'walking' is how many agents are on a path when it opens —");
            table.Note("the only part of the population paying for movement and pathfinding rather than");
            table.Note("a plan lookup.");
            r.Add(table);
        }

        /// <summary>
        /// The single most expensive minute in the day, timed tick by tick.
        ///
        /// A mean over five game-minutes is the wrong number for a frame budget, and the sweep
        /// found out why by accident: a run that happened to start at ten in the evening came
        /// out thirty times more expensive than one at eight in the morning, because that is
        /// when a whole settlement decides to go home to bed at once. The average hides that;
        /// a player would not.
        /// </summary>
        public static void WorstMinute(Report r, IReadOnlyList<Settlement> settlements, double overhead)
        {
            var table = new Table("The worst minute of the day, timed tick by tick",
                new Col("pop", 7), new Col("minute", 8, right: false), new Col("journeys", 9),
                new Col("median us", 10), new Col("p95 us", 9), new Col("worst us", 9),
                new Col("worst / budget", 14), new Col("worst / median", 14));

            foreach (var s in settlements)
            {
                var histogram = DemandByMinute(s);
                int peak = 0, peakMinute = 0;
                for (int m = 0; m < histogram.Length; m++)
                    if (histogram[m] > peak) { peak = histogram[m]; peakMinute = m; }

                const int reps = 3;
                var samples = new double[GameClock.TicksPerMinute * reps];
                var worsts = new double[reps];
                int n = 0;

                // Three runs of the same minute, and the reported worst is the MEDIAN of the
                // three worsts. A single run's maximum is as likely to be a garbage collection
                // landing on an arbitrary tick as it is to be the tick that started a hundred
                // and sixty journeys, and those two want telling apart.
                for (int rep = 0; rep < reps; rep++)
                {
                    int start = peakMinute >= 2 ? peakMinute - 2 : 0;
                    var sim = new Simulation(s.World, s.People, s.Seed, start);
                    sim.Tick((peakMinute - start) * GameClock.TicksPerMinute);
                    Measure.SettledHeap();

                    double worst = 0;
                    for (int t = 0; t < GameClock.TicksPerMinute; t++)
                    {
                        long t0 = Measure.Now();
                        sim.Tick();
                        double seconds = Measure.Since(t0) - overhead;
                        if (seconds < 0) seconds = 0;
                        samples[n++] = seconds;
                        if (seconds > worst) worst = seconds;
                    }
                    worsts[rep] = worst;
                }

                var p = Measure.Percentiles(samples, n);
                double worstMedian = new Stat(worsts).Median;
                double budgetUs = 1e6 / Budget;

                table.Row(s.People.Count, $"{peakMinute / 60:00}:{peakMinute % 60:00}", peak,
                          p.median * 1e6, p.p95 * 1e6, worstMedian * 1e6,
                          (worstMedian * 1e6 / budgetUs).ToString("0.0") + "x",
                          (worstMedian / Math.Max(p.median, 1e-12)).ToString("#,0") + "x");

                r.M("worst_minute", ("pop", s.People.Count), ("minute", peakMinute),
                    ("journeys", peak), ("us_median", p.median * 1e6),
                    ("us_p95", p.p95 * 1e6), ("us_worst", worstMedian * 1e6));

                if (worstMedian * 1e6 > 1e6 / Budget * 20)
                    r.Finding("worst-tick", $"At {s.People.Count:#,0} people the worst single tick of the day costs " +
                              $"{worstMedian * 1000:0.0} ms — {worstMedian * Budget:0} times its share of the " +
                              $"budget and {worstMedian / Math.Max(p.median, 1e-12):#,0} times an ordinary " +
                              "tick. It is the tick that starts a journey clump, and it is entirely " +
                              "pathfinding.");
            }

            table.Note("One game-minute, 1,200 ticks, each timed separately, three times over; the worst");
            table.Note("column is the median of the three worsts. 'budget' is the 166.7 us a tick gets at");
            table.Note("6,000 ticks/s. One tick over budget is a stutter; a run of them is where");
            table.Note("fast-forward visibly slows down.");
            r.Add(table);
        }

        /// <summary>
        /// Where the budget runs out, read off the sweep rather than guessed.
        ///
        /// Reported as a bracket when the sweep contains one and as an extrapolation when it
        /// does not, and labelled as such — an extrapolated ceiling from a power-law fit is a
        /// useful number and a bad promise.
        /// </summary>
        public static void Ceiling(Report r, List<Result> results)
        {
            if (results.Count < 2) return;

            double budgetSeconds = 1.0 / Budget;

            for (int i = 1; i < results.Count; i++)
            {
                double lo = results[i - 1].SecondsPerTick.Median;
                double hi = results[i].SecondsPerTick.Median;
                if (lo > budgetSeconds || hi < budgetSeconds) continue;

                double pLo = results[i - 1].Settlement.People.Count;
                double pHi = results[i].Settlement.People.Count;
                double at = Interpolate(pLo, lo, pHi, hi, budgetSeconds);

                r.Line($"  The 6,000 ticks/s budget is exhausted at about {at:#,0} citizens " +
                       $"(bracketed by the {pLo:#,0} and {pHi:#,0} rows).");
                r.Finding($"The 6,000 ticks/s budget runs out at about {at:#,0} citizens — measured, " +
                          $"bracketed by the {pLo:#,0} and {pHi:#,0} rows, not extrapolated.");
                r.M("ceiling", ("pop", at), ("kind", "bracketed"));
                r.Line();
                return;
            }

            var a = results[results.Count - 2];
            var b = results[results.Count - 1];
            double extrapolated = Interpolate(a.Settlement.People.Count, a.SecondsPerTick.Median,
                                              b.Settlement.People.Count, b.SecondsPerTick.Median,
                                              budgetSeconds);

            double exponent = Exponent(a.Settlement.People.Count, a.SecondsPerTick.Median,
                                       b.Settlement.People.Count, b.SecondsPerTick.Median);

            if (b.SecondsPerTick.Median < budgetSeconds)
            {
                r.Line($"  The budget is not exhausted anywhere in this sweep. Extrapolating the top two " +
                       $"rows (cost grows as pop^{exponent:0.00}) puts it at about {extrapolated:#,0} citizens.");
                r.Finding($"The 6,000 ticks/s budget survives the whole sweep. Extrapolating the top two " +
                          $"rows (pop^{exponent:0.00}) puts the wall at roughly {extrapolated:#,0} citizens — " +
                          "an extrapolation, so treat it as an order of magnitude, not a number.");
            }
            else
            {
                r.Line("  The budget is already exhausted at the smallest size measured.");
                r.Finding("The 6,000 ticks/s budget is already exhausted at the smallest population measured.");
            }

            r.M("ceiling", ("pop", extrapolated), ("kind", "extrapolated"), ("exponent", exponent));
            r.Line();
        }

        /// <summary>Log-log interpolation: cost against population is a power law, not a line.</summary>
        private static double Interpolate(double x0, double y0, double x1, double y1, double y)
        {
            double k = Exponent(x0, y0, x1, y1);
            if (Math.Abs(k) < 1e-9) return x1;
            return x0 * Math.Pow(y / y0, 1.0 / k);
        }

        public static double Exponent(double x0, double y0, double x1, double y1)
        {
            if (x0 <= 0 || x1 <= 0 || y0 <= 0 || y1 <= 0 || Math.Abs(x1 - x0) < 1e-9) return 0;
            return Math.Log(y1 / y0) / Math.Log(x1 / x0);
        }
    }
}
