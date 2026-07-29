using System.Collections.Generic;
using System.Globalization;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// The syntax the authored content files have in common: tokens, whole numbers, coordinates,
    /// clock times, day masks and terrain names.
    ///
    /// Shared rather than written once per file. village.txt and kinds.txt are different formats,
    /// but they spell half past eight and a Tuesday the same way, and two parsers that agree only
    /// by coincidence stop agreeing the first time one of them is corrected.
    /// </summary>
    internal static class ContentText
    {
        public static string[] SplitLines(string text) =>
            text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        /// <summary>Whitespace-separated, except that a double-quoted run is one token.</summary>
        public static List<string> Tokenise(string line)
        {
            var result = new List<string>();
            int i = 0;
            while (i < line.Length)
            {
                while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
                if (i >= line.Length) break;

                if (line[i] == '"')
                {
                    i++;
                    int start = i;
                    while (i < line.Length && line[i] != '"') i++;
                    result.Add(line.Substring(start, i - start));
                    if (i < line.Length) i++;
                }
                else
                {
                    int start = i;
                    while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
                    result.Add(line.Substring(start, i - start));
                }
            }
            return result;
        }

        public static void Require(List<string> tokens, int count, string file, int lineNo, string usage)
        {
            if (tokens.Count < count) throw new VillageParseException(file, lineNo, "expected: " + usage);
        }

        public static int Int(string s, string file, int lineNo)
        {
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                throw new VillageParseException(file, lineNo, $"'{s}' is not a whole number");
            return v;
        }

        /// <summary>"12,34" -> Tile(12,34)</summary>
        public static Tile Point(string s, string file, int lineNo)
        {
            int comma = s.IndexOf(',');
            if (comma < 0) throw new VillageParseException(file, lineNo, $"'{s}' should look like x,y");
            return new Tile(Int(s.Substring(0, comma), file, lineNo),
                            Int(s.Substring(comma + 1), file, lineNo));
        }

        /// <summary>"12,34" + "8x6" -> TileRect</summary>
        public static TileRect RectAt(string origin, string size, string file, int lineNo)
        {
            var o = Point(origin, file, lineNo);
            int x = size.IndexOf('x');
            if (x < 0) throw new VillageParseException(file, lineNo, $"'{size}' should look like WxH");
            return new TileRect(o.X, o.Y,
                                Int(size.Substring(0, x), file, lineNo),
                                Int(size.Substring(x + 1), file, lineNo));
        }

        /// <summary>"11:00-14:30" -> (660, 870)</summary>
        public static (int, int) TimeRange(string s, string file, int lineNo)
        {
            int dash = s.IndexOf('-');
            if (dash < 0) throw new VillageParseException(file, lineNo, $"'{s}' should look like hh:mm-hh:mm");
            return (Minutes(s.Substring(0, dash), file, lineNo),
                    Minutes(s.Substring(dash + 1), file, lineNo));
        }

        public static int Minutes(string s, string file, int lineNo)
        {
            int colon = s.IndexOf(':');
            if (colon < 0) throw new VillageParseException(file, lineNo, $"'{s}' should look like hh:mm");
            int h = Int(s.Substring(0, colon), file, lineNo);
            int m = Int(s.Substring(colon + 1), file, lineNo);
            if (h < 0 || h > 24 || m < 0 || m > 59)
                throw new VillageParseException(file, lineNo, $"'{s}' is not a valid time");
            return h * 60 + m;
        }

        private static readonly string[] DayNames = { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };

        /// <summary>"Mon-Fri", "Sun", "mon,tue,thu", "all"</summary>
        public static byte DaysMask(string s, string file, int lineNo)
        {
            string t = s.ToLowerInvariant();
            if (t == "all") return OpenWindow.AllDays;

            if (t.IndexOf(',') >= 0)
            {
                byte listMask = 0;
                foreach (string part in t.Split(','))
                    if (part.Length > 0) listMask |= DaysMask(part, file, lineNo);
                return listMask;
            }

            int dash = t.IndexOf('-');
            if (dash < 0) return (byte)(1 << DayIndex(t, file, lineNo));

            int from = DayIndex(t.Substring(0, dash), file, lineNo);
            int to = DayIndex(t.Substring(dash + 1), file, lineNo);
            byte mask = 0;
            for (int d = from; ; d = (d + 1) % 7)
            {
                mask |= (byte)(1 << d);
                if (d == to) break;
            }
            return mask;
        }

        private static int DayIndex(string s, string file, int lineNo)
        {
            for (int i = 0; i < DayNames.Length; i++)
                if (DayNames[i] == s) return i;
            throw new VillageParseException(file, lineNo, $"'{s}' is not a day name");
        }

        public static Terrain TerrainKind(string s, string file, int lineNo)
        {
            switch (s.ToLowerInvariant())
            {
                case "grass": return Terrain.Grass;
                case "field": return Terrain.Field;
                case "road": return Terrain.Road;
                case "path": return Terrain.Path;
                case "water": return Terrain.Water;
                case "wall": return Terrain.Wall;
                case "wood": return Terrain.Wood;
                case "churchyard": return Terrain.Churchyard;
                default: throw new VillageParseException(file, lineNo, $"unknown terrain '{s}'");
            }
        }

        public static bool YesNo(string s, string file, int lineNo)
        {
            switch (s.ToLowerInvariant())
            {
                case "yes": case "y": case "true": return true;
                case "no": case "n": case "false": return false;
                default: throw new VillageParseException(file, lineNo, $"'{s}' should be yes or no");
            }
        }
    }
}
