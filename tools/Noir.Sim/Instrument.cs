using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Noir.Sim
{
    public readonly struct Col
    {
        public readonly string Header;
        public readonly int Width;
        public readonly bool Right;

        public Col(string header, int width, bool right = true)
        {
            Header = header;
            Width = width;
            Right = right;
        }
    }

    /// <summary>
    /// A table whose column positions are declared, not derived from the widest cell.
    ///
    /// Deliberately the same shape as Noir.Bench's, and deliberately a second copy of it: the
    /// two harnesses share no assembly, and a report writer has no business in Core, which is
    /// the only thing they both reference. The duplication is four dozen lines and it keeps
    /// Core free of anything that exists to be printed.
    ///
    /// Fixed widths are the point. These reports get diffed build against build, and a column
    /// that silently widens because one number gained a digit marks every line of the run as
    /// changed with the actual regression buried in it.
    /// </summary>
    public sealed class Table
    {
        private readonly Col[] _cols;
        private readonly List<string[]> _rows = new List<string[]>();
        private readonly List<string> _notes = new List<string>();
        public readonly string Title;

        public Table(string title, params Col[] cols)
        {
            Title = title;
            _cols = cols;
        }

        public void Row(params object[] cells)
        {
            var text = new string[_cols.Length];
            for (int i = 0; i < _cols.Length; i++)
                text[i] = i < cells.Length ? Format(cells[i]) : "";
            _rows.Add(text);
        }

        /// <summary>A blank line inside the table, to group rows.</summary>
        public void Gap() => _rows.Add(null);

        public void Note(string text) => _notes.Add(text);

        public int RowCount => _rows.Count;

        private static string Format(object cell)
        {
            switch (cell)
            {
                case null: return "";
                case double d: return d.ToString("#,0.##", CultureInfo.InvariantCulture);
                case float f: return f.ToString("#,0.##", CultureInfo.InvariantCulture);
                case int i: return i.ToString("#,0", CultureInfo.InvariantCulture);
                case long l: return l.ToString("#,0", CultureInfo.InvariantCulture);
                default: return cell.ToString();
            }
        }

        public void WriteTo(StringBuilder sb)
        {
            if (!string.IsNullOrEmpty(Title)) sb.Append("  ").Append(Title).Append('\n');

            var header = new StringBuilder("  ");
            var rule = new StringBuilder("  ");
            for (int i = 0; i < _cols.Length; i++)
            {
                header.Append(Pad(_cols[i].Header, _cols[i].Width, _cols[i].Right));
                rule.Append(new string('-', _cols[i].Width));
                if (i < _cols.Length - 1) { header.Append("  "); rule.Append("  "); }
            }
            sb.Append(header.ToString().TrimEnd()).Append('\n');
            sb.Append(rule).Append('\n');

            foreach (var row in _rows)
            {
                if (row == null) { sb.Append('\n'); continue; }

                var line = new StringBuilder("  ");
                for (int i = 0; i < _cols.Length; i++)
                {
                    line.Append(Pad(row[i], _cols[i].Width, _cols[i].Right));
                    if (i < _cols.Length - 1) line.Append("  ");
                }
                sb.Append(line.ToString().TrimEnd()).Append('\n');
            }

            foreach (string note in _notes) sb.Append("  ").Append(note).Append('\n');
            sb.Append('\n');
        }

        private static string Pad(string text, int width, bool right)
        {
            if (text.Length > width) text = text.Substring(0, width);
            return right ? text.PadLeft(width) : text.PadRight(width);
        }
    }

    /// <summary>
    /// What one measurement command prints, in two registers at once.
    ///
    /// The tables are for a person. The "#M" lines are for `diff` and `awk`: one flat
    /// key=value record per measurement, so comparing this build against the last one is a
    /// shell one-liner rather than a careful read of two screenfuls of columns. Same
    /// convention as Noir.Bench, so the same tooling reads both.
    ///
    /// Nothing here may consult a clock or a stopwatch. These reports are compared byte for
    /// byte between runs, and one timestamp would make every diff non-empty.
    /// </summary>
    public sealed class Instrument
    {
        private readonly StringBuilder _human = new StringBuilder();
        private readonly StringBuilder _machine = new StringBuilder();
        private readonly List<string> _findings = new List<string>();

        public bool EmitMachineLines = true;

        public void Heading(string text)
        {
            _human.Append('\n').Append("  ").Append(text).Append('\n');
            _human.Append("  ").Append(new string('-', text.Length)).Append('\n');
        }

        public void Line(string text = "") => _human.Append(text).Append('\n');

        public void Add(Table table) => table.WriteTo(_human);

        /// <summary>One machine-readable measurement: `#M group key=value ...`.</summary>
        public void M(string group, params (string key, object value)[] fields)
        {
            if (!EmitMachineLines) return;

            _machine.Append("#M ").Append(group);
            foreach (var (key, value) in fields)
            {
                _machine.Append(' ').Append(key).Append('=');
                _machine.Append(value is IFormattable f
                    ? f.ToString(value is double || value is float ? "0.####" : null,
                                 CultureInfo.InvariantCulture)
                    : value?.ToString() ?? "");
            }
            _machine.Append('\n');
        }

        /// <summary>Something the numbers say that the code does not. Printed last, in one place.</summary>
        public void Finding(string text) => _findings.Add(text);

        public override string ToString()
        {
            var sb = new StringBuilder(_human.ToString());

            if (_findings.Count > 0)
            {
                sb.Append('\n').Append("  WHAT THE NUMBERS SAY\n");
                sb.Append("  ").Append(new string('-', 20)).Append('\n');
                foreach (string finding in _findings)
                    sb.Append("    * ").Append(finding).Append('\n');
                sb.Append('\n');
            }

            if (EmitMachineLines && _machine.Length > 0)
            {
                sb.Append("  MACHINE-READABLE  (grep '^#M')\n");
                sb.Append("  ").Append(new string('-', 30)).Append('\n').Append('\n');
                sb.Append(_machine);
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// The shape of a set of numbers, not its average.
    ///
    /// Every measurement in these three commands is a per-citizen or per-household figure, and
    /// for all of them the mean is the least informative thing that can be said. Four people
    /// who meet everybody and a hundred who meet nobody have the same mean as a healthy
    /// village; the percentiles are what tell them apart, and the count of zeroes is what
    /// tells you whether anybody was left out altogether.
    /// </summary>
    public static class Spread
    {
        public static readonly Col[] Columns =
        {
            new Col("", 26, right: false), new Col("mean", 9), new Col("min", 8),
            new Col("p10", 8), new Col("p25", 8), new Col("median", 8), new Col("p75", 8),
            new Col("p90", 8), new Col("max", 8), new Col("zero", 6),
        };

        /// <summary>Nearest-rank, on a copy sorted here. Integer ranks only, so two runs of the
        /// same seed cannot disagree about an interpolation.</summary>
        public static double Percentile(double[] sorted, int percent)
        {
            if (sorted.Length == 0) return 0;
            int rank = (int)Math.Ceiling(percent / 100.0 * sorted.Length) - 1;
            if (rank < 0) rank = 0;
            if (rank >= sorted.Length) rank = sorted.Length - 1;
            return sorted[rank];
        }

        public static double Mean(double[] values)
        {
            if (values.Length == 0) return 0;
            double total = 0;
            foreach (double v in values) total += v;
            return total / values.Length;
        }

        public static int Zeroes(double[] values)
        {
            int n = 0;
            foreach (double v in values) if (v <= 0) n++;
            return n;
        }

        /// <summary>One row of <see cref="Columns"/>: mean, the spread, and how many got none.</summary>
        public static void Describe(Table table, string label, double[] values)
        {
            var sorted = (double[])values.Clone();
            Array.Sort(sorted);
            table.Row(label, Mean(values), Percentile(sorted, 0), Percentile(sorted, 10),
                      Percentile(sorted, 25), Percentile(sorted, 50), Percentile(sorted, 75),
                      Percentile(sorted, 90), Percentile(sorted, 100), Zeroes(values));
        }

        /// <summary>The same numbers as one `#M` record, so a spread can be diffed as easily as
        /// a mean.</summary>
        public static void Machine(Instrument r, string group, string what, double[] values)
        {
            var sorted = (double[])values.Clone();
            Array.Sort(sorted);
            r.M(group, ("what", what), ("n", values.Length), ("mean", Mean(values)),
                ("min", Percentile(sorted, 0)), ("p10", Percentile(sorted, 10)),
                ("p25", Percentile(sorted, 25)), ("median", Percentile(sorted, 50)),
                ("p75", Percentile(sorted, 75)), ("p90", Percentile(sorted, 90)),
                ("max", Percentile(sorted, 100)), ("zero", Zeroes(values)));
        }

        /// <summary>
        /// A histogram with declared bucket edges rather than derived ones.
        ///
        /// Edges from the data would move between builds and make two runs incomparable, which
        /// is the one thing these reports may not do.
        /// </summary>
        public static void Histogram(Table table, double[] values, int[] edges, int barWidth = 34)
        {
            var counts = new int[edges.Length + 1];
            foreach (double v in values)
            {
                int bucket = edges.Length;
                for (int i = 0; i < edges.Length; i++)
                    if (v < edges[i]) { bucket = i; break; }
                counts[bucket]++;
            }

            int most = 1;
            foreach (int c in counts) if (c > most) most = c;

            for (int i = 0; i < counts.Length; i++)
            {
                string band = i == 0 ? "< " + edges[0]
                            : i == edges.Length ? edges[edges.Length - 1] + " +"
                            : edges[i - 1] + " – " + (edges[i] - 1);
                int bar = counts[i] * barWidth / most;
                table.Row(band, counts[i], new string('#', bar));
            }
        }
    }
}
