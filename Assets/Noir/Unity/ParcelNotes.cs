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
        /// <summary>
        /// Man, woman, or nobody wrote it down.
        ///
        /// Unrecorded is a real answer and the default one. Most of what gets authored here is
        /// half-remembered, and a field that forces a guess collects guesses.
        ///
        /// Core already has this as Citizen.Male, a plain bool, because a generated citizen
        /// always has one. An AUTHORED person does not, which is the whole difference between
        /// the two and the reason this is three states rather than a bool.
        /// </summary>
        public enum Sex { Unrecorded, Man, Woman }

        public sealed class Person
        {
            /// <summary>
            /// Stable for the life of this person, and never reused once assigned - see the
            /// `nextperson` line in the file. Zero means "not in the store yet": a row typed
            /// into the editor has no id until it is saved.
            ///
            /// The reason people have ids at all is that they OUTLIVE LOTS. Somebody between
            /// houses cannot be written down by a format that only knows `parcel N person ...`,
            /// and moving a family without an id means retyping them, which is how their traits
            /// and their ages get lost.
            /// </summary>
            public int Id;

            public string First = "";
            public string Last = "";
            public int Age;

            /// <summary>Who the mother is, when a child needs a surname - and what the witness
            /// layer needs to describe somebody as a man, a woman or a figure. See
            /// Noir.Core.Observation.PersonDescription, which already reports apparent sex.</summary>
            public Sex Which = Sex.Unrecorded;

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
                var c = new Person { Id = Id, First = First, Last = Last, Age = Age,
                                     Child = Child, Which = Which };
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
            ///
            /// BY ID, NOT BY VALUE. A lot points AT its residents rather than containing them,
            /// which is the whole thing that lets somebody exist while living nowhere: a person
            /// no lot points at is unhoused, and that needs no flag and no second list. It also
            /// means moving a family is changing which lot points at them, so their ages and
            /// their traits come along instead of being retyped.
            ///
            /// Resolve through <see cref="ParcelNotes.Residents"/>; write through
            /// <see cref="ParcelNotes.SetResidents"/>.
            /// </summary>
            public readonly List<int> Lives = new List<int>();

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

        }

        private static Dictionary<int, Note> _byId;

        /// <summary>Everybody authored, by id, whether or not they live anywhere.</summary>
        private static Dictionary<int, Person> _people;

        /// <summary>
        /// The next id to hand out. STORED in the file rather than derived from what is in it.
        ///
        /// `max(existing) + 1` walks backwards the moment the highest-numbered person is
        /// deleted: delete 19, reload, and the next person created is 19 again - inheriting any
        /// stale `lives 19` sitting in a hand edit or in parcel-notes.bak, which puts a stranger
        /// in somebody's house. So it is written out and only ever increased.
        /// </summary>
        private static int _nextPersonId = 1;

        /// <summary>Everybody, housed or not. See <see cref="Unhoused"/> for the roster.</summary>
        public static IReadOnlyDictionary<int, Person> AllPeople { get { Load(); return _people; } }

        /// <summary>One person by id, or null if no record has that id.</summary>
        public static Person PersonById(int id)
        {
            Load();
            return _people.TryGetValue(id, out var p) ? p : null;
        }

        /// <summary>The people living on this note, resolved. Ids with no record are skipped -
        /// Load has already dropped and reported those, so this only sees them mid-edit.</summary>
        public static List<Person> Residents(Note note)
        {
            Load();
            var list = new List<Person>();
            if (note == null) return list;
            foreach (int id in note.Lives)
                if (_people.TryGetValue(id, out var p)) list.Add(p);
            return list;
        }

        /// <summary>
        /// Put these people in the store and make them this note's residents.
        ///
        /// COPIES them in, for the reason the old WithPeople copied: the editor holds live
        /// objects somebody is typing into, and handing those same objects to the saved state
        /// would make the saved state and the draft one thing - so the dirty check could never
        /// see a difference, the save button would never light, and `revert` would revert to the
        /// edits it was meant to discard.
        ///
        /// Anybody arriving without an id is new and gets one. A row with neither a forename nor
        /// a surname is scaffolding rather than a person and is skipped.
        /// </summary>
        public static void SetResidents(Note note, System.Collections.Generic.IEnumerable<Person> people)
        {
            Load();
            note.Lives.Clear();
            foreach (var who in people)
            {
                if (string.IsNullOrWhiteSpace(who.First) && string.IsNullOrWhiteSpace(who.Last))
                    continue;
                int id = who.Id > 0 ? who.Id : _nextPersonId++;
                var stored = who.Copy();
                stored.Id = id;
                _people[id] = stored;
                note.Lives.Add(id);
            }
        }

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
                       && note.Lives.Count == 0
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
            _people = new Dictionary<int, Person>();
            _nextPersonId = 1;

            string text;
            try { text = ContentLoader.Read("parcel-notes.txt"); }
            catch { return; }

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                // TOP-LEVEL LINES, which are not about any parcel. They have to be handled before
                // the `parcel` guard below, which drops every line that does not start with that
                // word - people are no longer owned by a lot, so their records are not either.
                if (line.StartsWith("nextperson "))
                {
                    if (int.TryParse(line.Substring(11).Trim(), out int next))
                        _nextPersonId = System.Math.Max(_nextPersonId, next);
                    continue;
                }
                if (line.StartsWith("person "))
                {
                    ReadPerson(line.Substring(7));
                    continue;
                }

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
                else if (rest.StartsWith("person "))
                {
                    // LEGACY: a person written UNDERNEATH a lot, from before people had records
                    // of their own. Written by this file only between the morning of 2026-08-05
                    // and the id change the same afternoon, so the window is a few hours - and
                    // dropping it silently would still have cost somebody a household they had
                    // sat and typed. Minted an id and attached to this lot, once, on load; the
                    // next save writes them out in the current form and this stops mattering.
                    ReadPerson(rest.Substring(7), note);
                }
                else if (rest.StartsWith("lives "))
                {
                    // ONE ID PER LINE, so a single corrupt entry costs one resident rather than
                    // a household. Resolved against the person store at the end of Load, not
                    // here - the file does not promise that people come before the lots that
                    // list them, and it should not have to.
                    if (int.TryParse(rest.Substring(6).Trim(), out int personId))
                        note.Lives.Add(personId);
                }
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

            // Everything authored before the structured editor existed becomes people here, once,
            // after the whole file is in hand. Done at the end rather than per line because a
            // note's `names` and its `person` lines can appear in either order.
            foreach (var note in _byId.Values) MigrateNames(note);

            // A `lives` line naming somebody with no record. Costs that one resident and says so,
            // rather than refusing to load a file that is otherwise fine - the lot keeps its
            // zoning, its shape and everybody else. Must run AFTER MigrateNames, which mints ids
            // of its own and would otherwise have its people dropped as ghosts.
            foreach (var pair in _byId)
                for (int i = pair.Value.Lives.Count - 1; i >= 0; i--)
                    if (!_people.ContainsKey(pair.Value.Lives[i]))
                    {
                        Debug.LogWarning($"[notes] parcel {pair.Key} lists person "
                                       + $"{pair.Value.Lives[i]}, who has no record. Dropped.");
                        pair.Value.Lives.RemoveAt(i);
                    }
        }

        /// <summary>
        /// `<id> adult|child "first" "last" age "trait|trait" m|f|-` - the tail of a person line.
        ///
        /// Hand-tolerant on purpose. This file is meant to be editable in a text editor, so a
        /// line missing its age, its traits or its sex still yields a person rather than nothing;
        /// only the kind and a name are really required. A line with no id at all is a pre-id
        /// line and is minted one.
        /// </summary>
        /// <param name="attachTo">
        /// Non-null only for a LEGACY line written underneath a lot, which has no id where one
        /// would now be. The person is minted an id and made a resident of that lot.
        /// </param>
        private static void ReadPerson(string rest, Note attachTo = null)
        {
            var who = new Person();

            if (attachTo == null)
            {
                string idField = NextField(ref rest).Trim();
                if (int.TryParse(idField, out int givenId) && givenId > 0) who.Id = givenId;
            }

            who.Child = NextField(ref rest).Trim()
                            .Equals("child", System.StringComparison.OrdinalIgnoreCase);

            who.First = Unquote(NextField(ref rest));
            who.Last = Unquote(NextField(ref rest));

            string age = NextField(ref rest);
            if (int.TryParse(age, out int years)) who.Age = years;

            string traits = Unquote(NextField(ref rest));
            if (!string.IsNullOrWhiteSpace(traits))
                foreach (var t in traits.Split('|'))
                    if (!string.IsNullOrWhiteSpace(t)) who.Traits.Add(t.Trim());

            // Optional, and absent on anything written before the field existed. Spelled out
            // as well as abbreviated because this file is meant to be edited by hand and "man"
            // is what somebody would type.
            string sex = NextField(ref rest).Trim().ToLowerInvariant();
            if (sex == "m" || sex == "man" || sex == "male") who.Which = Sex.Man;
            else if (sex == "f" || sex == "woman" || sex == "female") who.Which = Sex.Woman;

            if (string.IsNullOrWhiteSpace(who.First) && string.IsNullOrWhiteSpace(who.Last)) return;

            if (who.Id <= 0) who.Id = _nextPersonId++;
            if (_people.ContainsKey(who.Id))
                Debug.LogWarning($"[notes] two person records share id {who.Id}; "
                               + "the later one wins. The next save rewrites the file cleanly.");
            _people[who.Id] = who;

            // The counter never goes backwards, whatever the file said - a `nextperson` line that
            // is missing, or lower than a person actually in the file, would otherwise hand out
            // an id somebody already has.
            _nextPersonId = System.Math.Max(_nextPersonId, who.Id + 1);

            attachTo?.Lives.Add(who.Id);
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
            if (note.Lives.Count > 0 || string.IsNullOrWhiteSpace(note.Names)) return;

            foreach (var line in note.Names.Split('\n'))
            {
                var name = line.Trim();
                if (name.Length == 0) continue;

                int space = name.LastIndexOf(' ');
                var who = new Person
                {
                    Id = _nextPersonId++,
                    First = space > 0 ? name.Substring(0, space) : name,
                    Last = space > 0 ? name.Substring(space + 1) : "",
                };
                _people[who.Id] = who;
                note.Lives.Add(who.Id);
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

            // ---- THE PEOPLE, before any lot, because lots only point at them ----
            //
            // The counter goes first and is never allowed to go backwards. Deriving it as
            // max+1 on load would reissue the id of whoever was highest the moment they were
            // deleted, and any stale `lives` line naming that id - in a hand edit, or in the
            // .bak sitting beside this file - would put a stranger in somebody's house.
            sb.AppendLine($"nextperson {_nextPersonId}");
            sb.AppendLine();

            var personIds = new List<int>(_people.Keys);
            personIds.Sort();
            foreach (int personId in personIds)
            {
                var who = _people[personId];
                if (string.IsNullOrWhiteSpace(who.First) && string.IsNullOrWhiteSpace(who.Last))
                    continue;

                // The traits field is written even when it is empty, so the age token in front
                // of it always has a space behind it and the line keeps one shape whether
                // somebody has traits or not - NextField reads a bare token by looking for that
                // space. Sex goes LAST, so a line written before that field existed, or typed by
                // hand without it, still reads back as a whole person with the sex unrecorded.
                sb.AppendLine($"person {who.Id} {(who.Child ? "child" : "adult")} "
                            + $"\"{Quote(who.First ?? "")}\" \"{Quote(who.Last ?? "")}\" "
                            + $"{who.Age} \"{Quote(string.Join("|", who.Traits))}\" "
                            + $"{(who.Which == Sex.Man ? "m" : who.Which == Sex.Woman ? "f" : "-")}");
            }
            if (personIds.Count > 0) sb.AppendLine();

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

                // WHO LIVES HERE, by id. One per line rather than a list, so a single corrupt
                // entry costs one resident instead of a household. The people themselves were
                // written above, once each, however many lots point at them - which is nothing
                // clever, just what happens when a person is a record rather than a line
                // underneath a lot.
                foreach (int resident in note.Lives)
                    sb.AppendLine($"parcel {id} lives {resident}");

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
