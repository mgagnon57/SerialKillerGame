using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.Survey
{
    /// <summary>
    /// What the owner says stood on each lot in 1991. Read from Content/parcel-1991.txt.
    ///
    /// WHY THIS IS ITS OWN FILE AND ITS OWN LOADER. Everything else the game knows about a lot is
    /// either measured or generated: the county's tax roll, the federal footprints, the parcel
    /// geometry, the households. This is the only source that is a person remembering, and it is
    /// the only one that cannot be rebuilt by running something. So it gets its own file, its own
    /// commit, and its own reader - and in particular it is NOT folded into ParcelNotes, which the
    /// game rewrites whole every time anybody edits a parcel. One shared file would mean the game
    /// could overwrite the rulings, and there is no script that could put them back.
    ///
    /// WHO WRITES IT. The browser map - docs/rossville-buildings.html, served by
    /// tools/serve-viewer.py - where clicking a lot and saying what was there lands a line here.
    /// Hand-editing is fine. This side only ever reads.
    ///
    /// ABSENT IS NOT VACANT. A vacant lot existed in 1991 with nothing on it. An ABSENT lot did
    /// not exist: the parcel boundaries in Content/parcels.txt are TODAY'S, and ground subdivided
    /// out of a field in 1998 has no business on a map of 1991. So absent lots stop being lots -
    /// see <see cref="ParcelIndex.In1991"/>, which is where that takes effect for every renderer
    /// and for the click test at once.
    ///
    /// THE NAME IS THE GROUPING. The county splits ground for its own reasons, and one building
    /// commonly stands across several of its lots - the grade school is three parcels. Lots
    /// carrying the same `property` name are one property. There is no group id to keep in step,
    /// which means a grouping can be made or broken in the browser by typing, and nothing here
    /// needs migrating when it is.
    /// </summary>
    public static class Rulings
    {
        public const string FileName = "parcel-1991.txt";

        /// <summary>
        /// The verbs this understands, for the smoke test to check the authored file against.
        ///
        /// THE BROWSER MAP IS THE SOURCE and the game is meant to obey all of it. A ruling the map
        /// learns to write and this never learns to read is the failure worth guarding: nothing
        /// breaks, nothing is logged, the owner rules a hundred lots and the town quietly ignores
        /// them. Naming the vocabulary here lets that be caught the day it lands.
        /// </summary>
        public static readonly string[] KnownVerbs = { "was", "kind", "property", "note", "footprint" };

        /// <summary>What was on the lot. <see cref="Unruled"/> means nobody has looked at it yet,
        /// which is different from having looked and not settled it (<see cref="Unsure"/>).</summary>
        public enum Stood { Unruled, Built, Vacant, Unsure, Absent }

        public sealed class Ruling
        {
            public int ParcelId;
            public Stood Was;

            /// <summary>What it was, in the game's own place words where one fits - see
            /// Content/kinds.txt - and free text where none does. Empty if unsaid.</summary>
            public string Kind = "";

            /// <summary>Which property this lot is part of, or empty. Lots sharing a name are
            /// one property.</summary>
            public string Property = "";

            /// <summary>Anything else worth saying. Empty if unsaid.</summary>
            public string Note = "";

            /// <summary>
            /// TRUE WHERE A BUILDING STOOD IN 1991 BUT THE MEASURED FOOTPRINT IS NOT IT.
            ///
            /// The gap this closes was found at 112 South Chicago Street. A block-long run of
            /// antique shops and a restaurant stood there in 1991; it burned in February 2004 and
            /// a Casey's was built on the cleared ground. So the lot is emphatically `built` - and
            /// seating the measured footprint on it puts a set-back convenience store where a row
            /// of storefronts met the pavement. "Nowhere close", as the owner put it.
            ///
            /// `vacant` is the wrong word for this and was tried first: it says nothing stood
            /// there, which erases the row instead of correcting it. The lot was built on. It is
            /// the SHAPE that postdates 1991, and the two facts need saying separately.
            ///
            ///     parcel 237 footprint later
            ///
            /// The footprint is then skipped by SeatOnSurvey and the lot falls through to
            /// FillFromSurvey, which raises a building by rule - the right era and the right
            /// posture, even if it is not the right building. A rule is a better guess about 1991
            /// than a photograph of 2016.
            /// </summary>
            public bool FootprintIsLater;
        }

        private static readonly Ruling Nothing = new Ruling { ParcelId = -1 };

        private static Dictionary<int, Ruling> _byParcel;
        private static Dictionary<string, List<int>> _byProperty;
        private static System.DateTime _stamp;
        private static int _checkedAt;
        private static int _revision;

        /// <summary>
        /// Bumped every time the file is reparsed, so anything caching work derived from these
        /// rulings can ask one int whether it is stale instead of stat'ing the file itself.
        /// <see cref="ParcelIndex.In1991"/> and its interior-edge set are both built this way.
        /// </summary>
        public static int Revision { get { Load(); return _revision; } }

        /// <summary>The ruling on one lot. Never null - an unruled lot hands back a blank one, so
        /// callers can read .Was and .Kind without a null check at every site.</summary>
        public static Ruling For(int parcelId)
        {
            Load();
            return _byParcel.TryGetValue(parcelId, out var r) ? r : Nothing;
        }

        /// <summary>Whether this lot did not exist in 1991 and should not be drawn, clicked or
        /// counted as a lot. False for a lot nobody has ruled on - silence is not a verdict.</summary>
        public static bool Absent(int parcelId) => For(parcelId).Was == Stood.Absent;

        /// <summary>The property name this lot belongs to, or empty.</summary>
        public static string PropertyOf(int parcelId) => For(parcelId).Property;

        /// <summary>
        /// Every lot making up the same property as this one, ascending, including this one.
        /// A lot with no property name is its own property, so this is never empty and callers
        /// can treat the single-lot case and the grade school identically.
        /// </summary>
        public static IReadOnlyList<int> OneProperty(int parcelId)
        {
            Load();
            var name = PropertyOf(parcelId).Trim();
            if (name.Length > 0 && _byProperty.TryGetValue(name.ToLowerInvariant(), out var lots))
                return lots;
            return new[] { parcelId };
        }

        /// <summary>The lot that speaks for the property - its lowest id. Selecting any lot of a
        /// property opens this one's editor, so three clicks on the school give one panel rather
        /// than three that disagree.</summary>
        public static int SpokesmanFor(int parcelId) => OneProperty(parcelId)[0];

        /// <summary>Whether this lot is part of a property made of more than one lot.</summary>
        public static bool IsGrouped(int parcelId) => OneProperty(parcelId).Count > 1;

        /// <summary>Every property made of more than one lot, by name.</summary>
        public static IReadOnlyDictionary<string, List<int>> Properties { get { Load(); return _byProperty; } }

        /// <summary>How many lots carry a ruling of any kind.</summary>
        public static int Count { get { Load(); return _byParcel.Count; } }

        /// <summary>
        /// Throw the cache away and reparse on the next question.
        ///
        /// FOR CALLERS THAT KNOW THE FILE CHANGED, which the throttle below cannot. Load() only
        /// asks the filesystem once a second - deliberately, because GroundZoning.ZoningAt calls
        /// through here once per TILE and Rossville is five million of them - so anything that
        /// rewrites the file and then reads it back inside that second gets the old answer.
        ///
        /// That is not a hypothetical about tests. tools/merge-back-strips.py and
        /// group-terraces.py both rewrite Content/parcel-1991.txt, and the browser map writes it
        /// on every click; a tool that wrote and then re-read would have been reading its own
        /// stale copy. The tests that finally exercise this parser hit it immediately - every one
        /// of them after the first read the FIRST one's data.
        /// </summary>
        public static void Forget()
        {
            _byParcel = null;
            _byProperty = null;
            _stamp = default;
            _checkedAt = 0;
        }

        private static void Load()
        {
            // Reparse when the file moves under us. The browser map writes it while the editor is
            // open - that is the whole point of the browser map - and a static cache outlives a
            // domain reload, so without this the game would show the rulings as they were when it
            // started and nothing would explain why.
            //
            // THROTTLED, because asking the filesystem is a syscall and this is now on the ground
            // build's inner loop: GroundZoning.ZoningAt calls SpokesmanFor once per TILE, and
            // Rossville is five million of them. Stat'ing per tile turned a one-second build into
            // one that never finished. A second's lag on noticing a save is imperceptible next to
            // the browser map's own two-second reload poll.
            if (_byParcel != null)
            {
                int now = System.Environment.TickCount;
                if (unchecked(now - _checkedAt) < 1000) return;
                _checkedAt = now;
                if (Content.WrittenAt(FileName) == _stamp) return;
            }

            _stamp = Content.WrittenAt(FileName);
            _checkedAt = System.Environment.TickCount;
            _revision++;
            _byParcel = new Dictionary<int, Ruling>();
            _byProperty = new Dictionary<string, List<int>>();

            string text;
            try { text = Content.Read(FileName); }
            catch { return; }

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                // parcel <id> <verb> <rest>, where rest is one bare word or one quoted string.
                var parts = line.Split(new[] { ' ' }, 4);
                if (parts.Length < 4 || parts[0] != "parcel") continue;
                if (!int.TryParse(parts[1], out int id)) continue;

                if (!_byParcel.TryGetValue(id, out var r))
                    _byParcel[id] = r = new Ruling { ParcelId = id };

                var rest = parts[3].Trim();
                switch (parts[2])
                {
                    case "was":      r.Was = StoodOf(rest);   break;
                    case "kind":     r.Kind = Unquote(rest);  break;
                    case "note":     r.Note = Unquote(rest);  break;
                    case "property": r.Property = Unquote(rest); break;
                    // `footprint later` - the lot was built on, but the measured shape postdates
                    // 1991 and must not be seated. Any other value is left alone rather than
                    // guessed at, so a future word cannot silently mean this one.
                    case "footprint": r.FootprintIsLater = Unquote(rest).Trim().ToLowerInvariant() == "later"; break;
                }
            }

            // KEYED CASE-INSENSITIVELY AND TRIMMED, because the browser map joins them that way
            // and the two must agree about what is one property. Typing "grade school" on a
            // fourth lot has to join the school, not start a second one that looks identical in
            // the file and behaves differently on the map.
            foreach (var kv in _byParcel)
            {
                var name = kv.Value.Property.Trim();
                if (name.Length == 0) continue;
                var key = name.ToLowerInvariant();
                if (!_byProperty.TryGetValue(key, out var lots))
                    _byProperty[key] = lots = new List<int>();
                lots.Add(kv.Key);
            }
            // Ascending, so SpokesmanFor is the same lot every load. Dictionary order is not.
            foreach (var kv in _byProperty) kv.Value.Sort();
        }

        private static Stood StoodOf(string word)
        {
            switch (word)
            {
                case "built":  return Stood.Built;
                case "vacant": return Stood.Vacant;
                case "unsure": return Stood.Unsure;
                case "absent": return Stood.Absent;
                default:       return Stood.Unruled;
            }
        }

        private static string Unquote(string s)
        {
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
            return s;
        }
    }
}
