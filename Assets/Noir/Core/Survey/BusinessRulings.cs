using System.Collections.Generic;
using System.Text;
using Noir.Core.Contracts;

namespace Noir.Core.Survey
{
    /// <summary>
    /// WHAT TRADED IN EACH DOWNTOWN UNIT IN 1991 - THE OWNER'S OWN RULING.
    ///
    /// Authored, not derived, and the same kind of fact as Content/parcel-1991.txt and
    /// Content/roads-1991.txt: no source in this project records what was open in Rossville in
    /// 1991. The tax roll starts in 2007. The federal imagery is 2016. The only thing anybody had
    /// was the 1913 Sanborn sheet, and on 2026-08-11 the town was still trading under it - a
    /// jeweller, a meat market, a G.A.R. hall, seventy-eight years out of date. Those names were
    /// cleared to numbered handles ("Rossville unit 12") precisely so this file could fill them.
    ///
    /// KEYED BY THE UNIT HANDLE FROM city.txt, NEVER BY THE DISPLAY NAME. The build replaces a
    /// unit's name with the business ruled onto it, so keying on what the player sees would break
    /// the ruling the moment it took effect - the second build would look up "Shorty's", find
    /// nothing, and quietly drop back to the handle. city.txt's name is the stable one.
    ///
    ///     unit "Rossville unit 12" kind tavern
    ///     unit "Rossville unit 12" business "Shorty's"
    ///     unit "Rossville unit 12" trade "beer and a grill"
    ///
    /// THE KIND IS THE LOAD-BEARING HALF. `business` and `trade` are prose - they are shown and
    /// nothing else reads them. `kind` is a word out of kinds.txt and it decides the interior,
    /// the jobs, the opening hours and what animations.txt gives the people inside
    /// (`atwork@tavern` keys on the KIND, never on a name). A kind nothing answers to does not
    /// throw; it falls through to a default and the building merely looks wrong - which is why
    /// the panel that writes this offers a list rather than a text box.
    /// </summary>
    public static class BusinessRulings
    {
        public const string FileName = "business-1991.txt";

        /// <summary>The verbs this understands. SmokeTest reads it, so a ruling the panel learns
        /// to write and this never learns to read is caught the day it lands rather than the day
        /// somebody notices the game ignoring it. Same contract as RoadRulings.KnownVerbs.</summary>
        public static readonly string[] KnownVerbs = { "unit" };

        public sealed class Ruling
        {
            /// <summary>A word from kinds.txt, or empty to leave the unit's kind alone.</summary>
            public string Kind = "";

            /// <summary>What it was called. Becomes the place's display name when set.</summary>
            public string Business = "";

            /// <summary>What it sold, in the owner's words. Shown; nothing simulates it.</summary>
            public string Trade = "";

            public bool IsEmpty => Kind.Length == 0 && Business.Length == 0 && Trade.Length == 0;
        }

        private static Dictionary<string, Ruling> _byUnit;
        private static System.DateTime _stamp = System.DateTime.MinValue;

        public static int Count { get { Load(); return _byUnit.Count; } }

        public static IReadOnlyDictionary<string, Ruling> All { get { Load(); return _byUnit; } }

        /// <summary>The ruling for a unit, or null where nobody has said. Absent is not the same
        /// as "there was nothing there".</summary>
        public static Ruling For(string unit)
        {
            if (string.IsNullOrEmpty(unit)) return null;
            Load();
            return _byUnit.TryGetValue(unit, out var r) ? r : null;
        }

        private static void Load()
        {
            var written = Content.WrittenAt(FileName);
            if (_byUnit != null && written == _stamp) return;
            _stamp = written;
            _byUnit = new Dictionary<string, Ruling>();

            string text;
            try { text = Content.Read(FileName); }
            catch { return; }   // absent is a town nobody has ruled on yet, not an error

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                var p = Tokens(line);
                if (p.Count < 4 || p[0] != "unit") continue;

                string unit = p[1], verb = p[2], value = p[3];
                if (!_byUnit.TryGetValue(unit, out var r)) _byUnit[unit] = r = new Ruling();

                if (verb == "kind") r.Kind = value;
                else if (verb == "business") r.Business = value;
                else if (verb == "trade") r.Trade = value;
            }
        }

        /// <summary>
        /// Split on spaces, except inside double quotes - a unit handle and a business name both
        /// have spaces in them, so a plain Split(' ') cannot read this file at all.
        /// </summary>
        private static List<string> Tokens(string line)
        {
            var outv = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;

            foreach (char c in line)
            {
                if (c == '"') { quoted = !quoted; continue; }
                if (c == ' ' && !quoted)
                {
                    if (sb.Length > 0) { outv.Add(sb.ToString()); sb.Length = 0; }
                    continue;
                }
                sb.Append(c);
            }
            if (sb.Length > 0) outv.Add(sb.ToString());
            return outv;
        }

        /// <summary>
        /// The whole file as it should be written back. The panel rewrites all of it rather than
        /// patching a line, the way ParcelNotes does, so there is one code path and no chance of
        /// a half-updated file.
        /// </summary>
        public static string Serialise(IReadOnlyDictionary<string, Ruling> rulings)
        {
            var sb = new StringBuilder();
            sb.Append("# ============================================================================\n");
            sb.Append("#  WHAT TRADED HERE IN 1991 - THE OWNER'S OWN RULING\n");
            sb.Append("#\n");
            sb.Append("#  Authored, not derived. Nothing in this project records what was open in\n");
            sb.Append("#  Rossville in 1991: the tax roll starts in 2007 and the imagery is 2016. The\n");
            sb.Append("#  downtown used to carry the 1913 Sanborn sheet's trades - a jeweller, a meat\n");
            sb.Append("#  market, a G.A.R. hall - which is seventy-eight years out and is why they were\n");
            sb.Append("#  cleared to numbered handles for this file to fill.\n");
            sb.Append("#\n");
            sb.Append("#  Keyed by the unit handle in city.txt, NOT by the business name: the build\n");
            sb.Append("#  replaces the name with whatever is ruled here, so keying on the display name\n");
            sb.Append("#  would break the ruling the moment it took effect.\n");
            sb.Append("#\n");
            sb.Append("#    unit \"Rossville unit 12\" kind tavern\n");
            sb.Append("#    unit \"Rossville unit 12\" business \"Shorty's\"\n");
            sb.Append("#    unit \"Rossville unit 12\" trade \"beer and a grill\"\n");
            sb.Append("#\n");
            sb.Append("#  `kind` must be a word kinds.txt answers to - it drives the interior, the jobs,\n");
            sb.Append("#  the hours and the animations. `business` and `trade` are prose and are shown.\n");
            sb.Append("#\n");
            sb.Append("#  Written by the game itself (the place panel). Hand-editing is fine; the next\n");
            sb.Append("#  save from the game rewrites the whole file.\n");
            sb.Append("# ============================================================================\n");
            sb.Append("\n");

            var keys = new List<string>(rulings.Keys);
            keys.Sort(System.StringComparer.Ordinal);

            foreach (var unit in keys)
            {
                var r = rulings[unit];
                if (r == null || r.IsEmpty) continue;
                if (r.Kind.Length > 0) sb.Append($"unit \"{unit}\" kind {r.Kind}\n");
                if (r.Business.Length > 0) sb.Append($"unit \"{unit}\" business \"{r.Business}\"\n");
                if (r.Trade.Length > 0) sb.Append($"unit \"{unit}\" trade \"{r.Trade}\"\n");
            }

            return sb.ToString();
        }
    }
}
