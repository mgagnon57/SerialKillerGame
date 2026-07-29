using System;
using System.Diagnostics;

namespace Noir.Bench
{
    /// <summary>
    /// The distribution of one measurement, not a single number.
    ///
    /// A benchmark that prints one figure is asking to be believed on faith. Every number this
    /// harness reports carries the spread it was drawn from, so a reader can tell a real 3%
    /// regression from a machine that had a browser open.
    /// </summary>
    public readonly struct Stat
    {
        public readonly double Median;
        public readonly double Min;
        public readonly double Max;
        public readonly double Mean;
        public readonly int Samples;

        public Stat(double[] values)
        {
            var sorted = (double[])values.Clone();
            Array.Sort(sorted);

            Samples = sorted.Length;
            Min = sorted[0];
            Max = sorted[sorted.Length - 1];
            Median = sorted.Length % 2 == 1
                ? sorted[sorted.Length / 2]
                : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) * 0.5;

            double total = 0;
            foreach (double v in sorted) total += v;
            Mean = total / sorted.Length;
        }

        /// <summary>Full range as a percentage of the median. Worst case, not a standard deviation.</summary>
        public double SpreadPercent => Median > 0 ? (Max - Min) / Median * 100.0 : 0.0;

        /// <summary>
        /// Above this the number is a mood rather than a measurement, and the tables say so
        /// with a "?" instead of quietly printing a clean-looking median.
        /// </summary>
        public bool Noisy => SpreadPercent > 15.0;

        public string SpreadCell => Noisy
            ? "?" + SpreadPercent.ToString("0") + "%"
            : SpreadPercent.ToString("0.0") + "%";
    }

    public static class Measure
    {
        private static readonly double TicksToSeconds = 1.0 / Stopwatch.Frequency;

        /// <summary>
        /// Time a body that performs <paramref name="units"/> units of work, and return the
        /// per-unit cost in seconds.
        ///
        /// Warm-up runs are thrown away rather than folded in. The first pass through any of
        /// this code is paying for JIT compilation and cold caches, and averaging that in is
        /// the single most common way a benchmark lies about steady-state cost.
        /// </summary>
        public static Stat PerUnit(Action<int> body, int units, int reps = 7, int warmups = 2)
        {
            for (int w = 0; w < warmups; w++) body(units);

            var values = new double[reps];
            for (int r = 0; r < reps; r++)
            {
                long start = Stopwatch.GetTimestamp();
                body(units);
                long end = Stopwatch.GetTimestamp();
                values[r] = (end - start) * TicksToSeconds / units;
            }
            return new Stat(values);
        }

        /// <summary>Time whole runs of <paramref name="body"/>, returning seconds per run.</summary>
        public static Stat PerRun(Action body, int reps = 7, int warmups = 2)
        {
            for (int w = 0; w < warmups; w++) body();

            var values = new double[reps];
            for (int r = 0; r < reps; r++)
            {
                long start = Stopwatch.GetTimestamp();
                body();
                long end = Stopwatch.GetTimestamp();
                values[r] = (end - start) * TicksToSeconds;
            }
            return new Stat(values);
        }

        /// <summary>
        /// How many units of work take roughly <paramref name="targetSeconds"/>.
        ///
        /// Fixing the iteration count instead would mean the fast configurations finish inside
        /// the timer's noise floor while the slow ones take a minute each. Calibrating keeps
        /// every row of a sweep equally trustworthy.
        /// </summary>
        public static int Calibrate(Action<int> body, double targetSeconds, int min, int max)
        {
            body(min);

            long start = Stopwatch.GetTimestamp();
            body(min);
            double perUnit = (Stopwatch.GetTimestamp() - start) * TicksToSeconds / min;
            if (perUnit <= 0) return max;

            double wanted = targetSeconds / perUnit;
            if (wanted < min) return min;
            if (wanted > max) return max;
            return (int)wanted;
        }

        /// <summary>
        /// Median and 95th percentile of a set of individually-timed events, in seconds.
        /// Used where the interesting thing is the tail — one tick in twenty costing ten times
        /// the others is invisible in a mean and is exactly what a frame budget cares about.
        /// </summary>
        public static (double median, double p95, double max) Percentiles(double[] values, int count)
        {
            var slice = new double[count];
            Array.Copy(values, slice, count);
            Array.Sort(slice);
            return (slice[count / 2], slice[(int)(count * 0.95)], slice[count - 1]);
        }

        /// <summary>
        /// A fixed unit of work, so two runs on two days can be compared.
        ///
        /// Within-run spread does not capture everything that moves a benchmark. A machine that
        /// is busy for a whole run, or that has dropped a turbo bin, reports a tight spread
        /// around a number that is uniformly wrong — which is indistinguishable from a genuine
        /// regression and is the failure this harness exists to avoid. Timing one deterministic
        /// workload at both ends of the run gives a divisor: if the anchor moved by a third,
        /// every absolute figure in the run moved by about a third with it, and the ratios
        /// between them did not.
        /// </summary>
        public static double MachineAnchor()
        {
            const int rounds = 40;
            double total = 0;

            // Deliberately arithmetic rather than simulation: no allocation, no cache effects
            // of its own, nothing that a change to Noir.Core could ever alter. The anchor has
            // to measure the machine and only the machine.
            for (int r = 0; r < rounds; r++)
            {
                long t0 = Now();
                double acc = 0;
                for (int i = 1; i <= 400000; i++) acc += 1.0 / i - 0.5 / (i + 1);
                double seconds = Since(t0);
                if (acc != 0 && (r == 0 || seconds < total)) total = seconds;
            }
            return total;
        }

        public static long Now() => Stopwatch.GetTimestamp();
        public static double Since(long start) => (Stopwatch.GetTimestamp() - start) * TicksToSeconds;

        /// <summary>
        /// Settle the heap so a memory reading measures what was allocated rather than what has
        /// not been swept yet. Two passes because finalisers can resurrect work for the first.
        /// </summary>
        public static long SettledHeap()
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            return GC.GetTotalMemory(forceFullCollection: true);
        }
    }
}
