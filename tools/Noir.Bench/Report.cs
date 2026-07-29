using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Noir.Bench
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
    /// This harness exists to be diffed between builds. If a column silently widens because
    /// one number gained a digit, every line of the run shows up as changed and the actual
    /// regression is buried. Fixed widths cost an occasional truncation and are worth it.
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

        /// <summary>A blank line inside the table, to group a sweep by scenario.</summary>
        public void Gap() => _rows.Add(null);

        public void Note(string text) => _notes.Add(text);

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
            sb.Append(Title).Append('\n');

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
    /// Everything a run prints, in two registers at once.
    ///
    /// The tables are for a person. The "#M" lines are for `diff` and `awk`: one flat
    /// key=value record per measurement, so a build-over-build comparison is a shell one-liner
    /// rather than a careful read of two screenfuls of columns.
    /// </summary>
    public sealed class Report
    {
        private readonly StringBuilder _human = new StringBuilder();
        private readonly StringBuilder _machine = new StringBuilder();
        private readonly List<string> _findingOrder = new List<string>();
        private readonly Dictionary<string, string> _findings = new Dictionary<string, string>();

        public bool EmitMachineLines = true;

        public void Heading(string text)
        {
            _human.Append('\n').Append(text).Append('\n');
            _human.Append(new string('=', text.Length)).Append('\n').Append('\n');
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

        /// <summary>
        /// Something the numbers say that the code does not. Printed last, in one place.
        ///
        /// Keyed, and a repeat replaces its predecessor. The sections run smallest settlement
        /// first, so the same observation made at every size collapses to the largest one —
        /// which is the interesting one, and a list of six near-identical bullets is a list
        /// nobody reads to the end of.
        /// </summary>
        public void Finding(string key, string text)
        {
            if (!_findings.ContainsKey(key)) _findingOrder.Add(key);
            _findings[key] = text;
        }

        public void Finding(string text) => Finding(text, text);

        public override string ToString()
        {
            var sb = new StringBuilder(_human.ToString());

            if (_findingOrder.Count > 0)
            {
                sb.Append('\n').Append("WHAT THE NUMBERS SAY").Append('\n');
                sb.Append(new string('=', 20)).Append('\n').Append('\n');
                foreach (string key in _findingOrder)
                    sb.Append("  * ").Append(_findings[key]).Append('\n');
                sb.Append('\n');
            }

            if (EmitMachineLines && _machine.Length > 0)
            {
                sb.Append('\n').Append("MACHINE-READABLE  (grep '^#M')").Append('\n');
                sb.Append(new string('=', 30)).Append('\n').Append('\n');
                sb.Append(_machine);
            }

            return sb.ToString();
        }
    }
}
