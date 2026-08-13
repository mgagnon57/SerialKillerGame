using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>Something the kinds table says that cannot be true. Thrown at load, never later.</summary>
    public sealed class PlaceKindTableException : Exception
    {
        public PlaceKindTableException(string message) : base("kinds.txt: " + message) { }
    }

    /// <summary>Outdoor furniture that belongs to a place rather than to the ground it stands on.</summary>
    public enum PlacePropStyle : byte
    {
        None = 0,
        Benches,
        Postbox,
        WaterTrough,
    }

    /// <summary>
    /// How many rooms a kind's interior is divided into.
    ///
    /// <see cref="Auto"/> means "however many the grammar thinks a building of this sort needs",
    /// which is what every kind uses: one room per eight square metres is a fact about houses,
    /// and the corridor grammars have their own arithmetic. A stated range is here because a
    /// surgery is a waiting room and two consulting rooms whatever its footprint â€” but no grammar
    /// takes a count yet, so the table refuses one rather than accepting a number nobody reads.
    /// </summary>
    public readonly struct RoomPlan
    {
        public readonly bool Any;
        public readonly int Min;
        public readonly int Max;

        private RoomPlan(bool any, int min, int max) { Any = any; Min = min; Max = max; }

        public static readonly RoomPlan None = new RoomPlan(false, 0, 0);
        public static readonly RoomPlan Auto = new RoomPlan(true, 0, 0);
        public static RoomPlan Between(int min, int max) => new RoomPlan(true, min, max);

        /// <summary>True when the count is left to the grammar rather than stated here.</summary>
        public bool FromArea => Any && Min == 0 && Max == 0;

        public override string ToString() => !Any ? "none" : FromArea ? "auto" : $"{Min}-{Max}";
    }

    /// <summary>
    /// Everything the generators need to know about one kind of place, in one row.
    ///
    /// This used to be thirteen switch statements, each with a silent `default:`. A new kind
    /// compiled cleanly and produced a hollow walled box - no rooms, no staff, no windows lit by
    /// anybody, no contribution to the density curve - and nothing anywhere said so. A row is
    /// either complete or the load fails, which is the entire point of the type.
    /// </summary>
    public sealed class PlaceKindRow
    {
        /// <summary>What the enum calls it, or the next free value for a kind only the table names.</summary>
        public readonly PlaceKind Kind;

        /// <summary>Its canonical name, as written after `kind` in the table.</summary>
        public readonly string Name;

        /// <summary>Every word village.txt may use for it, canonical name included.</summary>
        public readonly IReadOnlyList<string> Words;

        /// <summary>Walls, a floor and a door - as against open ground with a boundary.</summary>
        public readonly bool IsBuilding;

        /// <summary>
        /// Whether people LIVE here, and therefore whether households are made in it.
        ///
        /// This is the column that stops the open-kind promise being half true. Core swore that
        /// a kind the enum never heard of "works everywhere without a line of C#", and it very
        /// nearly did - but PopulationGenerator and DayPlan both asked for PlaceKind.Dwelling by
        /// its ENUM MEMBER, so a city of `apartment` came out with forty homes and nobody in a
        /// single one of them. Being a home is a property of the kind, not of which kinds
        /// happened to be known when the enum was written.
        ///
        /// It defaults to "is this the Dwelling member" when a row does not say, so a table
        /// written before this column existed behaves exactly as it did.
        /// </summary>
        public readonly bool IsHome;

        /// <summary>What open ground of this kind is made of. Grass unless it says otherwise.</summary>
        public readonly Terrain Ground;

        public readonly RoomPlan Rooms;

        /// <summary>Which interior grammar lays this kind out. See <see cref="InteriorGenerator"/>.</summary>
        public readonly string Grammar;

        /// <summary>
        /// Which massing grammar shapes the OUTSIDE of it: how tall the walls stand, what the
        /// roof does above them, and whether it gets a tower. Read by the renderer, not by Core,
        /// the same as <see cref="Roofed"/> and <see cref="Frontage"/>.
        ///
        /// Never null, and "cottage" when the row says nothing. Note the asymmetry with
        /// <see cref="Grammar"/> one line up, which IS refused when it names something nothing
        /// answers to: Core can check that because InteriorGenerator lives in Core, and it cannot
        /// check this one, because the massing grammars live in Assets/Noir/Unity. A typo here
        /// therefore costs a plain building and an editor warning rather than a refusal at load.
        /// That is the right trade for a column that only decides how something looks.
        /// </summary>
        public readonly string Massing;

        /// <summary>
        /// Whether it has a roof over it, which is also the question of whether its windows can
        /// light up. Read by the renderer, not by Core - see the note on RoofBuilder in the
        /// class remarks.
        /// </summary>
        public readonly bool Roofed;

        /// <summary>Which shopfront the renderer hangs on it: a sign, a fascia, a brass plate.</summary>
        public readonly string Frontage;

        public readonly PlacePropStyle Props;

        /// <summary>Somewhere you are served one at a time, and therefore somewhere you queue.</summary>
        public readonly bool HasCounter;

        /// <summary>Minutes at the counter: this many, plus a draw below <see cref="ServiceSpread"/>.</summary>
        public readonly int ServiceMinutes;
        public readonly int ServiceSpread;

        /// <summary>What a place of this kind is open for when it does not say for itself.</summary>
        public readonly IReadOnlyList<OpenWindow> Hours;

        /// <summary>How many work here when the place does not say for itself.</summary>
        public readonly int Jobs;

        /// <summary>
        /// What the people who work here are called, in slot order. The last name covers every
        /// slot after it, so "farmer farmhand" is one farmer and as many hands as there is work.
        /// </summary>
        public readonly IReadOnlyList<string> Roles;

        /// <summary>
        /// Whether several opening windows in a day are separate shifts rather than a lunch break.
        ///
        /// Almost everywhere they are a break - the shop shuts between one and two and the same
        /// shopkeeper unlocks it again - so staff work first opening to last closing. The mill
        /// genuinely runs an early and a late, and half its hands are on each.
        /// </summary>
        public readonly bool SplitShifts;

        /// <summary>Whether a household belongs to one of these: its school, its church, its surgery.</summary>
        public readonly bool IsCatchment;

        /// <summary>Whether a place of this kind is expected to carry its own human sentence.</summary>
        public readonly bool WantsDescription;

        public PlaceKindRow(PlaceKind kind, string name, IReadOnlyList<string> words,
                            bool isBuilding, Terrain ground, RoomPlan rooms, string grammar,
                            string massing, bool roofed, string frontage, PlacePropStyle props,
                            bool hasCounter, int serviceMinutes, int serviceSpread,
                            IReadOnlyList<OpenWindow> hours, int jobs, IReadOnlyList<string> roles,
                            bool splitShifts, bool isCatchment, bool wantsDescription,
                            bool isHome = false)
        {
            Kind = kind;
            Name = name;
            Words = words;
            IsBuilding = isBuilding;
            IsHome = isHome;
            Ground = ground;
            Rooms = rooms;
            Grammar = grammar;
            Massing = massing;
            Roofed = roofed;
            Frontage = frontage;
            Props = props;
            HasCounter = hasCounter;
            ServiceMinutes = serviceMinutes;
            ServiceSpread = serviceSpread;
            Hours = hours;
            Jobs = jobs;
            Roles = roles;
            SplitShifts = splitShifts;
            IsCatchment = isCatchment;
            WantsDescription = wantsDescription;
        }

        /// <summary>What the person filling this slot is called, or null if nobody works here.</summary>
        public string RoleAt(int slot)
        {
            if (Roles.Count == 0) return null;
            return Roles[slot < Roles.Count ? (slot < 0 ? 0 : slot) : Roles.Count - 1];
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Every kind of place there is, and what each one means.
    ///
    /// AUTHORED IN Content/kinds.txt, in the same idiom as village.txt: a `kind` line and a block
    /// of indented attributes under it. A kind whose name matches a <see cref="PlaceKind"/> member
    /// takes that member's value, so the numbering never moves under the switches that other
    /// assemblies still have; a kind the enum has never heard of gets the next value along, which
    /// is why an amenity can be added in content alone.
    ///
    /// THE LOAD-TIME ASSERT IS THE POINT. Every enum member must have a row and every row must be
    /// complete, or Parse throws. Before this, `PlaceKind.Hospital` compiled cleanly, fell through
    /// thirteen `default:` branches, and produced a hollow walled box with nobody in it.
    ///
    /// Two columns - <see cref="PlaceKindRow.Roofed"/> and <see cref="PlaceKindRow.Frontage"/> -
    /// are read by nothing in Core. They describe decisions the renderer makes in
    /// Assets/Noir/Unity, and they are written down here so that the day those switches are
    /// migrated the answers are already authored rather than guessed at.
    ///
    /// Nothing in the village is seeded off a kind's numeric value: every stream is named after a
    /// building's key, not its kind. Adding or reordering rows therefore cannot move a single
    /// tree, room or household.
    /// </summary>
    public sealed class PlaceKindTable
    {
        private const string File = "kinds.txt";

        private readonly PlaceKindRow[] _rows;                    // indexed by (int)PlaceKind
        private readonly Dictionary<string, PlaceKind> _byWord;
        private readonly PlaceKind[] _catchments;

        private PlaceKindTable(PlaceKindRow[] rows, Dictionary<string, PlaceKind> byWord,
                               PlaceKind[] catchments)
        {
            _rows = rows;
            _byWord = byWord;
            _catchments = catchments;
        }

        /// <summary>How many kinds there are, which is also one past the highest kind value.</summary>
        public int Count => _rows.Length;

        /// <summary>
        /// The row for this kind, or null where the table has a gap.
        ///
        /// <see cref="Row"/> throws on a gap, deliberately - asking about a kind that has no row
        /// is a fault at render time. This is for the callers that are WALKING the table rather
        /// than asking about one kind, where a gap is an ordinary answer: SmokeTest diffs every
        /// declared `frontage` and `massing` word against the keys the renderers answer to, which
        /// is the check CLAUDE.md asks for and which nothing did until a bank spent months
        /// wearing an anonymous plank.
        /// </summary>
        public PlaceKindRow RowOrNull(PlaceKind kind)
        {
            int i = (int)kind;
            return i < 0 || i >= _rows.Length ? null : _rows[i];
        }

        /// <summary>
        /// The kind with this canonical name, or false if the table has never heard of it.
        ///
        /// A kind the PlaceKind enum knows can be named in C# as PlaceKind.Tavern. A kind only
        /// kinds.txt knows - which is every city kind, numbered past the enum's members - cannot
        /// be, and that is not a small gap: it meant the day planner's whole errand list was a
        /// VILLAGE's list running in a city, offering shops and greens while the cinema, the
        /// casino and the diner stood open with nobody ever walking into them.
        ///
        /// False rather than an exception, because content decides what exists: asking for a
        /// diner on a map that has none is an ordinary answer, not a fault.
        /// </summary>
        public bool TryNamed(string name, out PlaceKind kind)
        {
            for (int i = 0; i < _rows.Length; i++)
            {
                if (_rows[i] == null) continue;
                if (!string.Equals(_rows[i].Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                kind = (PlaceKind)i;
                return true;
            }

            kind = default;
            return false;
        }

        public PlaceKindRow Row(PlaceKind kind)
        {
            int i = (int)kind;
            if (i < 0 || i >= _rows.Length || _rows[i] == null)
                throw new PlaceKindTableException(
                    $"{kind} has no row. Every kind needs one - a kind without a row is a building " +
                    "with no rooms, no staff and no hours, which is not something to find out at " +
                    "render time.");
            return _rows[i];
        }

        /// <summary>The word village.txt used, resolved to a kind. False if it names nothing.</summary>
        public bool TryKindOf(string word, out PlaceKind kind) =>
            _byWord.TryGetValue(word.ToLowerInvariant(), out kind);

        /// <summary>The institutions a household belongs to one of, in table order.</summary>
        public IReadOnlyList<PlaceKind> CatchmentKinds => _catchments;

        // ---- the ambient table ----

        private static PlaceKindTable _current;

        /// <summary>
        /// The table in force.
        ///
        /// Falls back to <see cref="Bootstrap"/> when no host has installed one. Core may not read
        /// files - the host reads them and hands over the string - and the Unity host does not yet
        /// know that kinds.txt exists. The bootstrap holds exactly the kinds the enum names and
        /// nothing else, so a new amenity still costs one row in kinds.txt and no code at all;
        /// it goes away the day VillageHost loads the file like every other host does.
        /// </summary>
        public static PlaceKindTable Current => _current ?? throw new PlaceKindTableException(
            "No kind table has been installed. Core cannot read files, so whoever owns the "
          + "content has to hand it over: PlaceKindTable.Install(PlaceKindTable.Parse(<kinds.txt>)). "
          + "The Unity host, the tools, the bench and the tests all do this at startup. "
          + "Failing loudly here is deliberate - the alternative was a compiled-in copy of the "
          + "table, which silently disagreed with Content/kinds.txt the moment anybody edited it.");

        public static bool IsInstalled => _current != null;

        public static void Install(PlaceKindTable table) =>
            _current = table ?? throw new ArgumentNullException(nameof(table));

        // ---- parsing ----

        /// <summary>
        /// Read the authored table. Throws rather than returning a partial one: a half-built table
        /// is exactly the silent failure this whole type exists to remove.
        /// </summary>
        public static PlaceKindTable Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            var drafts = new List<Draft>();
            Draft current = null;

            string[] lines = ContentText.SplitLines(text);
            for (int i = 0; i < lines.Length; i++)
            {
                int lineNo = i + 1;
                string raw = lines[i];

                int hash = raw.IndexOf('#');
                if (hash >= 0) raw = raw.Substring(0, hash);
                if (raw.Trim().Length == 0) continue;

                // A semicolon is simply the end of a line. It buys nothing when a row is laid out
                // one attribute per line, which is how the authored file reads best; it is there
                // so that a row can also be written across three lines where the surrounding text
                // matters more than the row does.
                bool indented = raw[0] == ' ' || raw[0] == '\t';
                string[] segments = raw.Split(';');

                for (int s = 0; s < segments.Length; s++)
                {
                    var tokens = ContentText.Tokenise(segments[s].Trim());
                    if (tokens.Count == 0) continue;
                    string cmd = tokens[0].ToLowerInvariant();

                    if (s == 0 && !indented)
                    {
                        if (cmd != "kind")
                            throw new VillageParseException(File, lineNo,
                                $"unknown directive '{tokens[0]}' - a row starts with `kind <name>`");
                        ContentText.Require(tokens, 2, File, lineNo, "kind <name>");
                        current = new Draft(tokens[1].ToLowerInvariant(), lineNo);
                        drafts.Add(current);
                        continue;
                    }

                    if (current == null)
                        throw new VillageParseException(File, lineNo,
                            $"'{tokens[0]}' has no `kind` line above it to belong to");

                    current.Attribute(cmd, tokens, lineNo);
                }
            }

            return Assemble(drafts);
        }

        /// <summary>
        /// One row as read, before anything has been checked. Attributes are kept as nullables so
        /// that "not written down" and "written down as no" stay different things - which is what
        /// lets the completeness check name the attribute that is actually missing.
        /// </summary>
        private sealed class Draft
        {
            public readonly string Name;
            public readonly int LineNo;

            public List<string> Words;
            public bool? IsBuilding;
            public bool? IsHome;
            public Terrain? Ground;
            public RoomPlan? Rooms;
            public string Grammar;
            public string Massing;
            public bool? Roofed;
            public string Frontage;
            public PlacePropStyle? Props;
            public bool? HasCounter;
            public int ServiceMinutes, ServiceSpread;
            public bool HasService;
            public List<OpenWindow> Hours;
            public int? Jobs;
            public List<string> Roles;
            public bool? SplitShifts;
            public bool? IsCatchment;
            public bool? WantsDescription;

            public Draft(string name, int lineNo) { Name = name; LineNo = lineNo; }

            public void Attribute(string cmd, List<string> tokens, int lineNo)
            {
                switch (cmd)
                {
                    case "words":
                        ContentText.Require(tokens, 2, File, lineNo, "words <name> [<name> ...]");
                        Words = new List<string>();
                        for (int t = 1; t < tokens.Count; t++) Words.Add(tokens[t].ToLowerInvariant());
                        break;

                    case "home":
                        ContentText.Require(tokens, 2, File, lineNo, "home yes|no");
                        IsHome = ContentText.YesNo(tokens[1], File, lineNo);
                        break;

                    case "form":
                        ContentText.Require(tokens, 2, File, lineNo, "form building|open");
                        switch (tokens[1].ToLowerInvariant())
                        {
                            case "building": IsBuilding = true; break;
                            case "open": IsBuilding = false; break;
                            default: throw new VillageParseException(File, lineNo,
                                $"'{tokens[1]}' should be building or open");
                        }
                        break;

                    case "ground":
                        ContentText.Require(tokens, 2, File, lineNo, "ground <terrain>");
                        Ground = ContentText.TerrainKind(tokens[1], File, lineNo);
                        break;

                    case "rooms":
                        ContentText.Require(tokens, 2, File, lineNo, "rooms none|auto|<min>-<max>");
                        Rooms = ParseRooms(tokens[1], lineNo);
                        break;

                    case "grammar":
                        ContentText.Require(tokens, 2, File, lineNo, "grammar <name>");
                        Grammar = tokens[1].ToLowerInvariant();
                        break;

                    case "massing":
                        ContentText.Require(tokens, 2, File, lineNo, "massing <name>");
                        Massing = tokens[1].ToLowerInvariant();
                        break;

                    case "roof":
                        ContentText.Require(tokens, 2, File, lineNo, "roof yes|no");
                        Roofed = ContentText.YesNo(tokens[1], File, lineNo);
                        break;

                    case "frontage":
                        ContentText.Require(tokens, 2, File, lineNo, "frontage <style>");
                        Frontage = tokens[1].ToLowerInvariant();
                        break;

                    case "props":
                        ContentText.Require(tokens, 2, File, lineNo, "props none|benches|postbox|trough");
                        Props = ParseProps(tokens[1], lineNo);
                        break;

                    case "counter":
                        ContentText.Require(tokens, 2, File, lineNo, "counter yes|no");
                        HasCounter = ContentText.YesNo(tokens[1], File, lineNo);
                        break;

                    case "service":
                        ContentText.Require(tokens, 3, File, lineNo, "service <minutes> <spread>");
                        ServiceMinutes = ContentText.Int(tokens[1], File, lineNo);
                        ServiceSpread = ContentText.Int(tokens[2], File, lineNo);
                        if (ServiceMinutes < 1 || ServiceSpread < 1)
                            throw new VillageParseException(File, lineNo,
                                "being served takes at least a minute, and the spread at least one more");
                        HasService = true;
                        break;

                    case "hours":
                        ContentText.Require(tokens, 2, File, lineNo, "hours none|<hh:mm-hh:mm[@days] ...>");
                        Hours = new List<OpenWindow>();
                        if (tokens[1].ToLowerInvariant() != "none")
                            for (int t = 1; t < tokens.Count; t++) Hours.Add(ParseWindow(tokens[t], lineNo));
                        break;

                    case "jobs":
                        ContentText.Require(tokens, 2, File, lineNo, "jobs <n>");
                        Jobs = ContentText.Int(tokens[1], File, lineNo);
                        if (Jobs < 0) throw new VillageParseException(File, lineNo,
                            "a place cannot employ fewer than nobody");
                        break;

                    case "roles":
                        ContentText.Require(tokens, 2, File, lineNo, "roles none|<name> [<name> ...]");
                        Roles = new List<string>();
                        if (tokens[1].ToLowerInvariant() != "none")
                            for (int t = 1; t < tokens.Count; t++) Roles.Add(tokens[t].ToLowerInvariant());
                        break;

                    case "shifts":
                        ContentText.Require(tokens, 2, File, lineNo, "shifts one|split");
                        switch (tokens[1].ToLowerInvariant())
                        {
                            case "one": SplitShifts = false; break;
                            case "split": SplitShifts = true; break;
                            default: throw new VillageParseException(File, lineNo,
                                $"'{tokens[1]}' should be one or split");
                        }
                        break;

                    case "catchment":
                        ContentText.Require(tokens, 2, File, lineNo, "catchment yes|no");
                        IsCatchment = ContentText.YesNo(tokens[1], File, lineNo);
                        break;

                    case "describe":
                        ContentText.Require(tokens, 2, File, lineNo, "describe yes|no");
                        WantsDescription = ContentText.YesNo(tokens[1], File, lineNo);
                        break;

                    default:
                        throw new VillageParseException(File, lineNo,
                            $"unknown attribute '{tokens[0]}'");
                }
            }

            private static RoomPlan ParseRooms(string s, int lineNo)
            {
                string t = s.ToLowerInvariant();
                if (t == "none") return RoomPlan.None;
                if (t == "auto") return RoomPlan.Auto;

                int dash = t.IndexOf('-');
                if (dash <= 0) throw new VillageParseException(File, lineNo,
                    $"'{s}' should be none, auto, or a range like 3-5");

                int min = ContentText.Int(t.Substring(0, dash), File, lineNo);
                int max = ContentText.Int(t.Substring(dash + 1), File, lineNo);
                if (min < 1 || max < min) throw new VillageParseException(File, lineNo,
                    $"'{s}' is not a room range");
                return RoomPlan.Between(min, max);
            }

            private static PlacePropStyle ParseProps(string s, int lineNo)
            {
                switch (s.ToLowerInvariant())
                {
                    case "none": return PlacePropStyle.None;
                    case "benches": return PlacePropStyle.Benches;
                    case "postbox": return PlacePropStyle.Postbox;
                    case "trough": return PlacePropStyle.WaterTrough;
                    default: throw new VillageParseException(File, lineNo,
                        $"'{s}' is not a prop style Core knows how to scatter");
                }
            }

            /// <summary>"17:30-23:00" or "09:00-12:00@sun".</summary>
            private static OpenWindow ParseWindow(string s, int lineNo)
            {
                int at = s.IndexOf('@');
                string span = at < 0 ? s : s.Substring(0, at);
                byte days = at < 0 ? OpenWindow.AllDays
                                   : ContentText.DaysMask(s.Substring(at + 1), File, lineNo);
                var (start, end) = ContentText.TimeRange(span, File, lineNo);
                return new OpenWindow(start, end, days);
            }
        }

        // ---- the checks that make a row worth having ----

        private static PlaceKindTable Assemble(List<Draft> drafts)
        {
            if (drafts.Count == 0)
                throw new PlaceKindTableException("there are no kinds in it at all");

            // A kind the enum names keeps the enum's value; anything else takes the next one
            // along. Other assemblies still switch on PlaceKind members, so their numbering has
            // to stay exactly where it is whatever content does.
            var enumValues = new Dictionary<string, PlaceKind>();
            foreach (PlaceKind k in Enum.GetValues(typeof(PlaceKind)))
                enumValues[k.ToString().ToLowerInvariant()] = k;

            int next = 0;
            foreach (PlaceKind k in Enum.GetValues(typeof(PlaceKind)))
                if ((int)k >= next) next = (int)k + 1;

            var assigned = new Dictionary<string, PlaceKind>();
            foreach (var draft in drafts)
            {
                if (assigned.ContainsKey(draft.Name))
                    throw new PlaceKindTableException($"'{draft.Name}' is written down twice");

                assigned[draft.Name] = enumValues.TryGetValue(draft.Name, out var known)
                    ? known
                    : (PlaceKind)next++;
            }

            // THE ASSERT. A member of the enum with no row is a kind that compiles, renders as a
            // walled box, and is staffed by nobody - and says nothing about it anywhere.
            var missing = new List<string>();
            foreach (var pair in enumValues)
                if (!assigned.ContainsKey(pair.Key)) missing.Add(pair.Key);
            if (missing.Count > 0)
            {
                missing.Sort(StringComparer.Ordinal);
                throw new PlaceKindTableException(
                    $"no row for {string.Join(", ", missing.ToArray())}. Every kind PlaceKind names " +
                    "needs one: without it the kind falls through every generator in the world, and " +
                    "the first sign of it is an empty building nobody works in.");
            }

            int size = next;
            foreach (var pair in assigned) if ((int)pair.Value >= size) size = (int)pair.Value + 1;

            var rows = new PlaceKindRow[size];
            var byWord = new Dictionary<string, PlaceKind>();
            var catchments = new List<PlaceKind>();

            foreach (var draft in drafts)
            {
                var kind = assigned[draft.Name];
                var row = Complete(draft, kind);
                rows[(int)kind] = row;

                foreach (string word in row.Words)
                {
                    if (byWord.TryGetValue(word, out var clash))
                        throw new PlaceKindTableException(
                            $"'{word}' names both {rows[(int)clash].Name} and {row.Name}");
                    byWord[word] = kind;
                }

                if (row.IsCatchment) catchments.Add(kind);
            }

            return new PlaceKindTable(rows, byWord, catchments.ToArray());
        }

        private static PlaceKindRow Complete(Draft d, PlaceKind kind)
        {
            Need(d, d.Words != null && d.Words.Count > 0, "words");
            Need(d, d.IsBuilding.HasValue, "form");
            Need(d, d.Rooms.HasValue, "rooms");
            Need(d, d.Roofed.HasValue, "roof");
            Need(d, d.Frontage != null, "frontage");
            Need(d, d.Props.HasValue, "props");
            Need(d, d.HasCounter.HasValue, "counter");
            Need(d, d.Hours != null, "hours");
            Need(d, d.Jobs.HasValue, "jobs");
            Need(d, d.Roles != null, "roles");
            Need(d, d.SplitShifts.HasValue, "shifts");
            Need(d, d.IsCatchment.HasValue, "catchment");
            Need(d, d.WantsDescription.HasValue, "describe");

            // The three attributes that only mean anything in company. Required when they are
            // relevant and refused when they are not, so that a service time on a phone booth is a
            // load failure rather than a line somebody wrote and then wondered about.
            bool open = !d.IsBuilding.Value;
            if (open) Need(d, d.Ground.HasValue, "ground");
            else if (d.Ground.HasValue) Refuse(d, "ground", "only open ground is made of anything");

            if (d.Rooms.Value.Any) Need(d, d.Grammar != null, "grammar");
            else if (d.Grammar != null) Refuse(d, "grammar", "it has no rooms to lay out");

            // A grammar nothing answers to would fall back to the house rules, and a hospital
            // built to the house rules is a 330 m2 kitchen with two bedrooms off it. Better the
            // load stops than that it is discovered by looking at the building.
            if (d.Grammar != null && !InteriorGenerator.IsKnown(d.Grammar))
                throw new PlaceKindTableException(
                    $"'{d.Name}' (line {d.LineNo}) asks for the '{d.Grammar}' interior grammar and " +
                    $"nothing answers to that name. There is: {string.Join(", ", InteriorGenerator.Names())}.");

            // A stated room count has nowhere to go: every grammar decides for itself how many
            // rooms a building of its sort needs, which is the right answer for houses and is
            // why the house rules could not build a school. Saying so beats accepting the number
            // and ignoring it, which is the silent default this table exists to remove.
            if (d.Rooms.Value.Any && !d.Rooms.Value.FromArea)
                throw new PlaceKindTableException(
                    $"'{d.Name}' (line {d.LineNo}) asks for {d.Rooms.Value} rooms, and no grammar " +
                    "takes a room count. Say `rooms auto` until IInteriorGrammar.Generate accepts one.");

            if (d.HasCounter.Value) Need(d, d.HasService, "service");
            else if (d.HasService) Refuse(d, "service", "nobody is served at a counter here");

            // Roles without staff is the failure this table was written to make impossible: an
            // amenity whose windows never light from occupancy and which contributes nothing to
            // the density curve, because the trade it declares is practised by nobody.
            if (d.Roles.Count > 0 && d.Jobs.Value <= 0)
                throw new PlaceKindTableException(
                    $"'{d.Name}' (line {d.LineNo}) names the trades {string.Join(", ", d.Roles.ToArray())} " +
                    "but employs nobody. A kind that declares roles has to staff.");
            if (d.Roles.Count == 0 && d.Jobs.Value > 0)
                throw new PlaceKindTableException(
                    $"'{d.Name}' (line {d.LineNo}) employs {d.Jobs.Value} but names no trade for them.");

            if (!d.IsBuilding.Value && d.Rooms.Value.Any)
                throw new PlaceKindTableException(
                    $"'{d.Name}' (line {d.LineNo}) is open ground with rooms in it.");

            return new PlaceKindRow(kind, d.Name, d.Words.ToArray(),
                                    d.IsBuilding.Value, open ? d.Ground.Value : Terrain.Grass,
                                    d.Rooms.Value, d.Grammar, d.Massing ?? "cottage",
                                    d.Roofed.Value, d.Frontage,
                                    d.Props.Value, d.HasCounter.Value,
                                    d.ServiceMinutes, d.ServiceSpread,
                                    d.Hours.ToArray(), d.Jobs.Value, d.Roles.ToArray(),
                                    d.SplitShifts.Value, d.IsCatchment.Value,
                                    d.WantsDescription.Value,
                                    // Unstated means "the way it was before this column existed",
                                    // which is that Dwelling and nothing else is a home. A table
                                    // written against the old schema therefore behaves identically.
                                    d.IsHome ?? (kind == PlaceKind.Dwelling));
        }

        private static void Need(Draft d, bool present, string attribute)
        {
            if (!present)
                throw new PlaceKindTableException(
                    $"'{d.Name}' (line {d.LineNo}) has no `{attribute}` line. A row is complete or it " +
                    "is not a row.");
        }

        private static void Refuse(Draft d, string attribute, string why)
        {
            throw new PlaceKindTableException(
                $"'{d.Name}' (line {d.LineNo}) has a `{attribute}` line, but {why}.");
        }

        // ---- the bootstrap ----

    }
}
