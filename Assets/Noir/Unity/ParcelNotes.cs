using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// What a person knows about a real parcel that the county's own data cannot: who lived
    /// there, how many of them, what they were like, the shape of the house if it's still clear
    /// in memory, and what the lot itself is - zoned residential or otherwise, how many stories,
    /// a basement or not, and for a house, what form it takes. Keyed by ParcelIndex's stable
    /// per-parcel Id rather than by address - most of these 794 lots have no address at all, and
    /// the whole point is that somebody who grew up here knows who lived on a lot whether or not
    /// it was ever numbered.
    ///
    /// Content/parcel-notes.txt is the one Content file this project writes to at runtime rather
    /// than only reading - hand-editable like every other, but also a save file. Rewritten whole
    /// on every save rather than appended to, so a corrected entry replaces the old one instead of
    /// leaving it behind for the parser to find first.
    /// </summary>
    public static class ParcelNotes
    {
        /// <summary>What the lot is zoned for. Independent of whether a Place was ever generated
        /// on it - most of Rossville's 794 parcels never got one, and a lot can be categorized
        /// long before (or without) anything being built there.</summary>
        public enum Zoning { Unset, Residential, Commercial, Industrial, Civic, Agricultural, Vacant }

        /// <summary>The housing FORM, distinct from how tall it is or whether it has a basement
        /// - those are Stories/Basement below, and apply to any zoning, not just this one. Only
        /// meaningful when Zoning is Residential.</summary>
        public enum HousingType { Unset, SingleFamily, Duplex, Apartment, ApartmentComplex }

        /// <summary>What condition the building is in - kept up, let go, or somewhere between.
        /// Applies to any zoning, not just housing: a shut-up storefront is as much a fact about
        /// a street as a derelict house is.</summary>
        public enum Quality { Unset, Derelict, Poor, Fair, Good, Excellent }

        /// <summary>
        /// One person in a household.
        ///
        /// Age rather than a birth year, because what the author remembers is "he'd have been
        /// about sixty" and not a date. Zero means not recorded.
        /// </summary>
        public sealed class Person
        {
            public string First = "";
            public string Last = "";
            public int Age;

            /// <summary>A child of the household rather than an adult in it. Not derived from
            /// Age: a nineteen-year-old still at home is somebody's kid in a village, and a
            /// twelve-year-old in a household of one is not a child of anybody here.</summary>
            public bool Child;

            /// <summary>From VillageTraits, or typed. See the picker in VillageUI.</summary>
            public readonly List<string> Traits = new List<string>();

            public string FullName =>
                string.IsNullOrWhiteSpace(Last) ? First.Trim() : (First + " " + Last).Trim();

            public Person Copy()
            {
                var c = new Person { First = First, Last = Last, Age = Age, Child = Child };
                c.Traits.AddRange(Traits);
                return c;
            }
        }

        public sealed class Note
        {
            /// <summary>What the family is like - the seed for behaviour, not just flavour text.
            /// See VillageUI's household editor and Noir.Core.People.ParticularsTable for the
            /// existing "useless human detail" system this is meant to feed the same way.</summary>
            public string Character = "";

            /// <summary>
            /// What TRADED on this lot in the build year, and what it sold.
            ///
            /// The one thing about the downtown that no source has. Everything written about
            /// Rossville's commercial row was written after the February 2004 fire and mourns
            /// the block rather than naming what was in it - see docs/research/DOWNTOWN-1991.md,
            /// where exactly one 1991 business could be identified from the whole open web.
            ///
            /// So it is captured HERE instead, by the one person who was standing in it: click
            /// the lot, type the shop. The owner asked for it in those words - "if there is a
            /// way for me to add that when I click the shop then yes".
            ///
            /// Business is the sign over the door, Trade is what it actually was. Both, because
            /// a name alone does not tell a generator what to build and a trade alone does not
            /// tell a player what they are looking at.
            /// </summary>
            public string Business = "";
            public string Trade = "";

            public int Adults;
            public int Kids;

            /// <summary>One name per line, adults then kids, in no particular enforced count -
            /// entering three names for two adults and one kid is the author's business, not a
            /// rule this file polices.
            ///
            /// SUPERSEDED BY <see cref="People"/>. Kept so notes written before the structured
            /// editor existed still load and still show something; migrated into People on read,
            /// and never written back out. Do not add to it.</summary>
            public string Names = "";

            /// <summary>
            /// Who lives here, one entry each, with the name split.
            ///
            /// Replaces the free-text Names blob. A blob was fine when the only question was
            /// "who lived on this lot" and hopeless the moment anything wanted to KNOW something
            /// about a person - their age, whether they are a child, what they are like. Every
            /// one of those was a parsing guess against a line somebody typed.
            ///
            /// First and last separately because a household is not a list of full names: a wife
            /// may not share the surname, a lodger certainly does not, and a child defaults to
            /// its mother's - which the editor can only do for you if it can see the parts.
            /// </summary>
            public readonly List<Person> People = new List<Person>();

            public Vector2[] Footprint;    // null if nobody has drawn one

            /// <summary>What kind of plot this is, and - for Residential - what form the housing
            /// takes. See Zoning and HousingType above.</summary>
            public Zoning Zoning;
            public HousingType Housing;

            /// <summary>0 means not recorded, not "no stories" - a real building always has at
            /// least one.</summary>
            public int Stories;
            public bool Basement;

            /// <summary>What condition it is in. See Quality above.</summary>
            public Quality Condition;

            // ---- the house itself ----
            //
            // ZERO IS "NOT RECORDED" THROUGHOUT, not a real measurement of zero. Vermilion
            // County publishes none of this - no square footage, no bedroom or bathroom count,
            // no year built, anywhere online; its tax card carries billing and assessment only.
            // So unlike the address and the class code these are AUTHORED, and the county's
            // assessed dwelling value (see CountyRecord.MarketValue) is the only public check
            // on whether a number entered here is plausible.

            public int Bedrooms;

            /// <summary>Full bathrooms - a toilet, a basin and a bath or shower.</summary>
            public int Baths;

            /// <summary>Half bathrooms: a toilet and a basin, no bath. Counted separately
            /// rather than as "2.5 baths" because which one a house has matters when somebody
            /// is moving through it - a half bath is usually downstairs and off a hall.</summary>
            public int HalfBaths;

            /// <summary>Finished living area in square FEET. This is America.</summary>
            public int SquareFeet;

            public int YearBuilt;

            /// <summary>
            /// Fill People from a draft list, COPYING each person.
            ///
            /// Copied rather than referenced because the editor holds live objects the user is
            /// typing into. Handing those same objects to the saved note would mean the "saved"
            /// state and the draft are one thing - so the dirty check could never see a
            /// difference, the save button would never light, and `revert` would revert to the
            /// edits it was meant to discard.
            /// </summary>
            public Note WithPeople(System.Collections.Generic.IEnumerable<Person> people)
            {
                People.Clear();
                foreach (var who in people)
                    if (!string.IsNullOrWhiteSpace(who.First) || !string.IsNullOrWhiteSpace(who.Last))
                        People.Add(who.Copy());
                return this;
            }
        }

        private static Dictionary<int, Note> _byId;

        public static Note For(int parcelId)
        {
            Load();
            return _byId.TryGetValue(parcelId, out var n) ? n : null;
        }

        public static IReadOnlyDictionary<int, Note> All { get { Load(); return _byId; } }

        /// <summary>Fired after every save, so a renderer showing every authored footprint at
        /// once (see AuthoredFootprints) knows to rebuild rather than polling.</summary>
        public static event System.Action Changed;

        public static void Save(int parcelId, Note note)
        {
            Load();
            bool empty = note == null
                      || (string.IsNullOrWhiteSpace(note.Character)
                       && string.IsNullOrWhiteSpace(note.Business)
                       && string.IsNullOrWhiteSpace(note.Trade)
                       && string.IsNullOrWhiteSpace(note.Names)
                       && note.People.Count == 0
                       && note.Adults == 0 && note.Kids == 0
                       && note.Zoning == Zoning.Unset && note.Housing == HousingType.Unset
                       && note.Stories == 0 && !note.Basement && note.Condition == Quality.Unset
                       && note.Bedrooms == 0 && note.Baths == 0 && note.HalfBaths == 0
                       && note.SquareFeet == 0 && note.YearBuilt == 0
                       && (note.Footprint == null || note.Footprint.Length < 3));

            if (empty) _byId.Remove(parcelId);
            else _byId[parcelId] = note;

            Write();
            Changed?.Invoke();
        }

        /// <summary>Convenience for callers that only ever touch the footprint (FootprintDrawer)
        /// - keeps whatever household data already exists untouched.</summary>
        public static void SaveFootprint(int parcelId, Vector2[] footprint)
        {
            var note = For(parcelId) ?? new Note();
            note.Footprint = footprint;
            Save(parcelId, note);
        }

        private static void Load()
        {
            if (_byId != null) return;
            _byId = new Dictionary<int, Note>();

            string text;
            try { text = ContentLoader.Read("parcel-notes.txt"); }
            catch { return; }

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                var parts = line.Split(new[] { ' ' }, 3);
                if (parts.Length < 3 || parts[0] != "parcel") continue;
                if (!int.TryParse(parts[1], out int id)) continue;

                var note = _byId.TryGetValue(id, out var existing) ? existing : (_byId[id] = new Note());
                var rest = parts[2];

                if (rest.StartsWith("character "))
                    note.Character = Unquote(rest.Substring(10));
                else if (rest.StartsWith("business "))
                    note.Business = Unquote(rest.Substring(9));
                else if (rest.StartsWith("trade "))
                    note.Trade = Unquote(rest.Substring(6));
                else if (rest.StartsWith("names "))
                    note.Names = Unquote(rest.Substring(6)).Replace("|", "\n");
                else if (rest.StartsWith("household "))
                {
                    var nums = rest.Substring(10).Split(' ');
                    if (nums.Length >= 2 && int.TryParse(nums[0], out int a) && int.TryParse(nums[1], out int k))
                    {
                        note.Adults = a;
                        note.Kids = k;
                    }
                }
                else if (rest.StartsWith("zoning "))
                {
                    if (System.Enum.TryParse(rest.Substring(7), true, out Zoning z)) note.Zoning = z;
                }
                else if (rest.StartsWith("rooms "))
                {
                    var nums = rest.Substring(6).Split(' ');
                    if (nums.Length >= 3 && int.TryParse(nums[0], out int bed)
                                         && int.TryParse(nums[1], out int bath)
                                         && int.TryParse(nums[2], out int half))
                    {
                        note.Bedrooms = bed; note.Baths = bath; note.HalfBaths = half;
                    }
                }
                else if (rest.StartsWith("size "))
                {
                    var nums = rest.Substring(5).Split(' ');
                    if (nums.Length >= 2 && int.TryParse(nums[0], out int sqft)
                                         && int.TryParse(nums[1], out int year))
                    {
                        note.SquareFeet = sqft; note.YearBuilt = year;
                    }
                }
                else if (rest.StartsWith("quality "))
                {
                    if (System.Enum.TryParse(rest.Substring(8), true, out Quality q)) note.Condition = q;
                }
                else if (rest.StartsWith("building "))
                {
                    var nums = rest.Substring(9).Split(' ');
                    if (nums.Length >= 3 && int.TryParse(nums[0], out int st))
                    {
                        note.Stories = st;
                        note.Basement = nums[1] == "1";
                        if (System.Enum.TryParse(nums[2], true, out HousingType h)) note.Housing = h;
                    }
                }
                else if (rest.StartsWith("shape "))
                {
                    var pts = new List<Vector2>();
                    foreach (var piece in rest.Substring(6).Split(' '))
                    {
                        int comma = piece.IndexOf(',');
                        if (comma <= 0) continue;
                        if (float.TryParse(piece.Substring(0, comma), NumberStyles.Float,
                                           CultureInfo.InvariantCulture, out float x)
                         && float.TryParse(piece.Substring(comma + 1), NumberStyles.Float,
                                           CultureInfo.InvariantCulture, out float y))
                            pts.Add(new Vector2(x, y));
                    }
                    if (pts.Count >= 3) note.Footprint = pts.ToArray();
                }
            }

            // Everything authored before the structured editor existed becomes People here, once,
            // after the whole file is in hand. Done at the end rather than per line because a
            // note's `names` and its `person` lines can appear in either order.
            foreach (var note in _byId.Values) MigrateNames(note);
        }

        /// <summary>
        /// `adult|child "first" "last" age "trait|trait"` - the tail of a person line.
        ///
        /// Hand-tolerant on purpose. This file is meant to be editable in a text editor, so a
        /// line missing its age or its traits still yields a person rather than nothing; only
        /// the kind and the first name are really required.
        /// </summary>
        private static void ReadPerson(Note note, string rest)
        {
            var who = new Person();
            int at = rest.IndexOf(' ');
            if (at < 0) return;

            who.Child = rest.Substring(0, at).Trim().Equals("child", System.StringComparison.OrdinalIgnoreCase);
            rest = rest.Substring(at + 1).Trim();

            who.First = Unquote(NextField(ref rest));
            who.Last = Unquote(NextField(ref rest));

            string age = NextField(ref rest);
            if (int.TryParse(age, out int years)) who.Age = years;

            string traits = Unquote(NextField(ref rest));
            if (!string.IsNullOrWhiteSpace(traits))
                foreach (var t in traits.Split('|'))
                    if (!string.IsNullOrWhiteSpace(t)) who.Traits.Add(t.Trim());

            if (!string.IsNullOrWhiteSpace(who.First) || !string.IsNullOrWhiteSpace(who.Last))
                note.People.Add(who);
        }

        /// <summary>Take the next `"quoted field"` or bare token off the front of a line.</summary>
        private static string NextField(ref string rest)
        {
            rest = rest.TrimStart();
            if (rest.Length == 0) return "";

            if (rest[0] == '"')
            {
                // SKIP ESCAPED QUOTES. Quote() writes a quote inside a field as \" and a plain
                // IndexOf finds that one first - so a nickname like Bob "Red" Fuller truncated
                // the field and ate every field after it on the line. Villages are full of
                // nicknames; this will be typed.
                int close = -1;
                for (int i = 1; i < rest.Length; i++)
                {
                    if (rest[i] == '\\') { i++; continue; }
                    if (rest[i] == '"') { close = i; break; }
                }
                if (close < 0) { var whole = rest; rest = ""; return whole; }
                var field = rest.Substring(0, close + 1);
                rest = rest.Substring(close + 1);
                return field;
            }

            int space = rest.IndexOf(' ');
            if (space < 0) { var whole = rest; rest = ""; return whole; }
            var token = rest.Substring(0, space);
            rest = rest.Substring(space + 1);
            return token;
        }

        /// <summary>
        /// Turn a pre-structured-editor `names` blob into People, once, on load.
        ///
        /// Everything authored before the editor could tell a first name from a last one is a
        /// list of lines like "Russell Fuller". Splitting on the LAST space is the least wrong
        /// rule: "Mary Ellen Stufflebeam" gives Mary Ellen / Stufflebeam, which is right, and a
        /// single word gives a first name and no surname, which is also right. Nobody's typing
        /// is thrown away and nothing is invented - no ages, no traits, no guesses about who is
        /// a child.
        /// </summary>
        private static void MigrateNames(Note note)
        {
            if (note.People.Count > 0 || string.IsNullOrWhiteSpace(note.Names)) return;

            foreach (var line in note.Names.Split('\n'))
            {
                var name = line.Trim();
                if (name.Length == 0) continue;

                int space = name.LastIndexOf(' ');
                note.People.Add(new Person
                {
                    First = space > 0 ? name.Substring(0, space) : name,
                    Last = space > 0 ? name.Substring(space + 1) : "",
                });
            }
        }

        private static void Write()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ============================================================================");
            sb.AppendLine("#  AUTHORED PARCEL NOTES - who lived here, the household, the house shape if");
            sb.AppendLine("#  it's known, and what the lot is - zoning, stories, basement, housing type");
            sb.AppendLine("#  and what condition the building is in.");
            sb.AppendLine("#");
            sb.AppendLine("#  Keyed by parcel id, which is that parcel's line number in parcels.txt (0-");
            sb.AppendLine("#  based, comments and blanks not counted) - NOT an address, because most of");
            sb.AppendLine("#  these lots never had a house number generated on them at all.");
            sb.AppendLine("#");
            sb.AppendLine("#  Written by the game itself (VillageUI's household editor). Hand-editing is");
            sb.AppendLine("#  fine; the next save from the game rewrites the whole file, so keep a copy");
            sb.AppendLine("#  of any edit you care about outside a Play session.");
            sb.AppendLine("# ============================================================================");
            sb.AppendLine();

            var ids = new List<int>(_byId.Keys);
            ids.Sort();
            foreach (int id in ids)
            {
                var note = _byId[id];
                if (!string.IsNullOrWhiteSpace(note.Business))
                    sb.AppendLine($"parcel {id} business \"{Quote(note.Business)}\"");
                if (!string.IsNullOrWhiteSpace(note.Trade))
                    sb.AppendLine($"parcel {id} trade \"{Quote(note.Trade)}\"");
                if (!string.IsNullOrWhiteSpace(note.Character))
                    sb.AppendLine($"parcel {id} character \"{Quote(note.Character)}\"");
                if (note.Adults != 0 || note.Kids != 0)
                    sb.AppendLine($"parcel {id} household {note.Adults} {note.Kids}");
                if (!string.IsNullOrWhiteSpace(note.Names))
                    sb.AppendLine($"parcel {id} names \"{Quote(note.Names.Replace("\n", "|"))}\"");
                if (note.Zoning != Zoning.Unset)
                    sb.AppendLine($"parcel {id} zoning {note.Zoning.ToString().ToLowerInvariant()}");
                if (note.Stories != 0 || note.Basement || note.Housing != HousingType.Unset)
                    sb.AppendLine($"parcel {id} building {note.Stories} {(note.Basement ? 1 : 0)} "
                                + $"{note.Housing.ToString().ToLowerInvariant()}");
                if (note.Condition != Quality.Unset)
                    sb.AppendLine($"parcel {id} quality {note.Condition.ToString().ToLowerInvariant()}");
                if (note.Bedrooms != 0 || note.Baths != 0 || note.HalfBaths != 0)
                    sb.AppendLine($"parcel {id} rooms {note.Bedrooms} {note.Baths} {note.HalfBaths}");
                if (note.SquareFeet != 0 || note.YearBuilt != 0)
                    sb.AppendLine($"parcel {id} size {note.SquareFeet} {note.YearBuilt}");
                if (note.Footprint != null && note.Footprint.Length >= 3)
                {
                    sb.Append($"parcel {id} shape");
                    foreach (var p in note.Footprint)
                        sb.Append(' ').Append(p.x.ToString("0.0", CultureInfo.InvariantCulture))
                          .Append(',').Append(p.y.ToString("0.0", CultureInfo.InvariantCulture));
                    sb.AppendLine();
                }
            }

            // WRITE BESIDE IT, THEN SWAP. This file is the only Content file the game writes
            // rather than reads, it is rewritten WHOLE on every save, and it is the sole copy of
            // everything anybody has authored about the town - who lived on a lot, what traded
            // there, the house shapes drawn by hand. A plain WriteAllText truncates the real file
            // first and fills it afterwards, so a crash, a full disk or Unity being killed
            // mid-save leaves a half file and takes the lot with it.
            //
            // File.Replace keeps the previous contents as parcel-notes.bak, which costs nothing
            // and means even a failure between the two steps leaves a complete file to go back to.
            string path = Path.Combine(ContentLoader.Root, "parcel-notes.txt");
            string temp = path + ".tmp";
            string backup = path + ".bak";

            File.WriteAllText(temp, sb.ToString());
            if (File.Exists(path)) File.Replace(temp, path, backup);
            else File.Move(temp, path);
        }

        private static string Quote(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                                                   .Replace("\n", "\\n");

        private static string Unquote(string s)
        {
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
            return s.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }
}
