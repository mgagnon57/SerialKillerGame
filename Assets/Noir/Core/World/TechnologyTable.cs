using System;
using System.Collections.Generic;
using System.Globalization;

namespace Noir.Core.World
{
    /// <summary>Who a technology is counted per. See the header of Content/technology.txt.</summary>
    public enum TechScope : byte
    {
        /// <summary>
        /// A fact about the place rather than about anybody in it. The key is ignored entirely
        /// and <c>Has</c> asks whether the curve is past half.
        ///
        /// Most town rows are 0-or-100 - CODIS goes live in 1998 for everybody at once, and no
        /// household is an early adopter of the FBI's database. But a FRACTIONAL town curve is
        /// legitimate and means a share of the town's STOCK: <c>payphone</c> runs 100 to 60 to 10
        /// as the boxes came out one by one, and past-half then reads as "there is still one to
        /// use", which is the question anybody actually asks about a payphone.
        /// </summary>
        Town = 0,
        Household = 1,
        Person = 2,
        Farm = 3,
        Business = 4,
    }

    /// <summary>One authored row: what it is, who it is counted per, and its curve.</summary>
    public sealed class TechnologyRow
    {
        public readonly string Name;
        public readonly TechScope Scope;
        public readonly Adoption Curve;

        /// <summary>
        /// What this cannot exist without, or null. See <see cref="Queue"/> - a row that needs
        /// another one ranks off ITS queue, which is what makes the containment exact.
        /// </summary>
        public readonly string Needs;

        public TechnologyRow(string name, TechScope scope, Adoption curve, string needs = null)
        {
            Name = name; Scope = scope; Curve = curve; Needs = needs;
        }

        /// <summary>
        /// Which queue a key takes its place in. Its own, unless the row declares a dependency -
        /// then the parent's, so that everybody who has the child necessarily has the parent.
        /// </summary>
        public string Queue => Needs ?? Name;

        public override string ToString() => Needs == null ? $"{Name} ({Scope})"
                                                           : $"{Name} ({Scope}, needs {Needs})";
    }

    /// <summary>
    /// What the town has, and when it gets it.
    ///
    /// The game opens in 1991 and runs to about 2006, and until this existed nothing but the crops
    /// and the daylight knew the difference: a citizen in 2005 lived in exactly the 1991 world,
    /// with the same objects and the same reachability. The sharpest instance is
    /// WHO-SEES-WHOM.md section 5 - in 1991 you cannot reach a person who is not at home and an
    /// unanswered phone means nothing; by 2006 a person is reachable and there is a log. THE SAME
    /// DISAPPEARANCE IS A DIFFERENT EVENT AT THE TWO ENDS OF THIS GAME.
    ///
    /// FACTS, NOT PROPS. This layer answers "does this household have a mobile phone in 1999?" so
    /// the day plan, the dialogue prompt and eventually the investigation can consult it. No art
    /// is involved and none is waited for - <c>Fields</c> already proved the point by shipping six
    /// crop states while the asset pack can render one.
    ///
    /// INERT WHEN ABSENT, AND THAT IS THE OPPOSITE OF <see cref="PlaceKindTable"/> ON PURPOSE.
    /// A kind with no row is a building with no rooms and no staff, so that table refuses to load.
    /// A technology with no row is a technology the town does not have, which is a perfectly good
    /// answer and is exactly the 1991 world. So an unknown name returns false and a missing
    /// content file makes every query false - the failure mode is the opening state of the game
    /// rather than a crash. Authoring mistakes are still loud: <see cref="Parse"/> throws.
    ///
    /// DO NOT LET THIS LEAK INTO THE WITNESS LAYER. <c>PersonDescription.CarriedThing</c> is
    /// deliberately vague - Bag, Case, Bundle, LongObject - because a witness says "something in
    /// his hand", not "a Nokia". That vagueness is the type's whole design and knowing what year
    /// it is must not sharpen it.
    /// </summary>
    public sealed class TechnologyTable
    {
        private const string File = "technology.txt";

        private readonly Dictionary<string, TechnologyRow> _rows;
        private readonly string[] _names;                     // authored order, for the diagnostic

        private TechnologyTable(Dictionary<string, TechnologyRow> rows, string[] names)
        {
            _rows = rows; _names = names;
        }

        /// <summary>An empty table: everything false, nothing throws. The 1991 world.</summary>
        public static readonly TechnologyTable Empty =
            new TechnologyTable(new Dictionary<string, TechnologyRow>(), new string[0]);

        public int Count => _names.Length;

        /// <summary>Every technology the table names, in the order they were authored.</summary>
        public IReadOnlyList<string> Names => _names;

        public bool TryRow(string name, out TechnologyRow row)
        {
            row = null;
            return name != null && _rows.TryGetValue(name.ToLowerInvariant(), out row);
        }

        // ---- the ambient table -------------------------------------------------------------

        private static TechnologyTable _current = Empty;

        /// <summary>
        /// The table in force. Empty until a host installs one - Core cannot read files, so the
        /// Unity caller does
        /// <c>TechnologyTable.Install(TechnologyTable.Parse(ContentLoader.Read("technology.txt")))</c>.
        /// </summary>
        public static TechnologyTable Current => _current;

        /// <summary>Install a parsed table. Null resets to <see cref="Empty"/> rather than throwing.</summary>
        public static void Install(TechnologyTable table) => _current = table ?? Empty;

        // ---- what the simulation asks ------------------------------------------------------

        /// <summary>
        /// Does this key have this technology in this year?
        ///
        /// LEAVING THE KEY OUT ASKS A DIFFERENT AND USEFUL QUESTION: is this ORDINARY this year -
        /// does more than half the town have it. That is what a <c>town</c> row always answers,
        /// and it is what the dialogue prompt wants when it is describing the period rather than a
        /// person. It also removes a footgun: a caller who forgets the key gets "is this normal
        /// in 1998" rather than a confident answer about whichever household hashes to zero.
        ///
        /// False for a name the table has never heard of.
        /// </summary>
        public static bool Has(string name, int year, ulong key = 0)
        {
            if (!Current.TryRow(name, out var row)) return false;
            if (row.Scope == TechScope.Town || key == 0) return row.Curve.PercentIn(year) >= 50f;
            return row.Curve.HeldIn(year, Era.RankOf(key, row.Queue));
        }

        /// <summary>Whether more than half the town has it - the period question, with no subject.</summary>
        public static bool IsOrdinary(string name, int year) => Has(name, year);

        /// <summary>
        /// The year this key gets it, or <see cref="Era.Never"/>. One crossing, computed from the
        /// curve, never stored - which is why nobody can acquire a computer twice.
        /// </summary>
        public static int AdoptsIn(string name, ulong key)
        {
            if (!Current.TryRow(name, out var row)) return Era.Never;
            if (row.Scope == TechScope.Town || key == 0) return TownYear(row);
            return row.Curve.YearWhen(Era.RankOf(key, row.Queue));
        }

        /// <summary>
        /// The year this key loses it again, or <see cref="Era.Never"/>. Only a falling curve ever
        /// answers anything else - <c>payphone</c> and the tail of <c>dialup</c>.
        /// </summary>
        public static int LosesIn(string name, ulong key)
        {
            if (!Current.TryRow(name, out var row)) return Era.Never;
            if (row.Scope == TechScope.Town || key == 0) return Era.Never;
            return row.Curve.YearLost(Era.RankOf(key, row.Queue));
        }

        /// <summary>What share of the town has it in a given year, 0..100. Zero if unknown.</summary>
        public static float Adopted(string name, int year) =>
            Current.TryRow(name, out var row) ? row.Curve.PercentIn(year) : 0f;

        /// <summary>The scope of a row, and false if the table does not name it.</summary>
        public static bool TryScope(string name, out TechScope scope)
        {
            if (Current.TryRow(name, out var row)) { scope = row.Scope; return true; }
            scope = default;
            return false;
        }

        /// <summary>The first year a town-scope curve is past half. Used when there is no subject.</summary>
        private static int TownYear(TechnologyRow row)
        {
            if (row.Curve.IsEmpty) return Era.Never;
            for (int y = row.Curve.FirstYear; y <= row.Curve.LastYear; y++)
                if (row.Curve.PercentIn(y) >= 50f) return y;
            return row.Curve.PercentIn(row.Curve.LastYear) >= 50f ? row.Curve.LastYear : Era.Never;
        }

        // ---- parsing -----------------------------------------------------------------------

        /// <summary>
        /// Read the authored table. Throws on anything malformed: a row nobody can read is an
        /// authoring mistake and should stop the load, which is a different thing from a row that
        /// does not exist.
        /// </summary>
        public static TechnologyTable Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            var rows = new Dictionary<string, TechnologyRow>();
            var order = new List<string>();

            string[] lines = ContentText.SplitLines(text);
            for (int i = 0; i < lines.Length; i++)
            {
                int lineNo = i + 1;
                string raw = lines[i];

                int hash = raw.IndexOf('#');
                if (hash >= 0) raw = raw.Substring(0, hash);
                if (raw.Trim().Length == 0) continue;

                var tokens = ContentText.Tokenise(raw.Trim());
                ContentText.Require(tokens, 3, File, lineNo,
                                    "<name> <town|household|person|farm|business> <year:percent> ...");

                string name = tokens[0].ToLowerInvariant();
                if (rows.ContainsKey(name))
                    throw new VillageParseException(File, lineNo, $"'{name}' is written down twice");

                TechScope scope = ParseScope(tokens[1], lineNo);

                var years = new List<int>();
                var percents = new List<float>();
                string needs = null;

                for (int t = 2; t < tokens.Count; t++)
                {
                    if (tokens[t].ToLowerInvariant() == "needs")
                    {
                        if (t + 1 >= tokens.Count)
                            throw new VillageParseException(File, lineNo, "`needs` wants a name after it");
                        needs = tokens[t + 1].ToLowerInvariant();
                        break;
                    }

                    var (year, percent) = ParseWaypoint(tokens[t], lineNo);
                    if (years.Count > 0 && year <= years[years.Count - 1])
                        throw new VillageParseException(File, lineNo,
                            $"waypoint years must ascend - {years[years.Count - 1]} then {year}");
                    years.Add(year);
                    percents.Add(percent);
                }

                if (years.Count == 0)
                    throw new VillageParseException(File, lineNo, "a row needs at least one year:percent");

                rows[name] = new TechnologyRow(name, scope,
                                               new Adoption(years.ToArray(), percents.ToArray()), needs);
                order.Add(name);
            }

            CheckDependencies(rows);
            return new TechnologyTable(rows, order.ToArray());
        }

        /// <summary>
        /// A row that needs another one must never be commoner than what it needs.
        ///
        /// THIS EXISTS BECAUSE THE DIAGNOSTIC CAUGHT IT. With every row ranked off its own name,
        /// `dialup` and `computer` were independent draws, so about a sixth of the town had the
        /// internet and no machine to read it on - and every test in the file passed, because each
        /// curve was individually perfect. Sharing the parent's queue makes the containment exact
        /// rather than probable: the households that bought a computer first are the ones that
        /// went online first, which is both true and free.
        ///
        /// It only works while the child curve stays under the parent's, so that is checked here
        /// rather than left as a thing somebody has to remember when editing the file.
        /// </summary>
        private static void CheckDependencies(Dictionary<string, TechnologyRow> rows)
        {
            foreach (var pair in rows)
            {
                var row = pair.Value;
                if (row.Needs == null) continue;

                if (!rows.TryGetValue(row.Needs, out var parent))
                    throw new VillageParseException(File, 0,
                        $"'{row.Name}' needs '{row.Needs}', and there is no such row");

                if (parent.Needs != null)
                    throw new VillageParseException(File, 0,
                        $"'{row.Name}' needs '{parent.Name}', which needs '{parent.Needs}'. " +
                        "Chains are not supported - say what it directly depends on.");

                if (row.Scope != parent.Scope)
                    throw new VillageParseException(File, 0,
                        $"'{row.Name}' is {row.Scope} and needs '{parent.Name}', which is " +
                        $"{parent.Scope}. A row can only share the queue of one counted the same way.");

                for (int year = Math.Min(row.Curve.FirstYear, parent.Curve.FirstYear);
                     year <= Math.Max(row.Curve.LastYear, parent.Curve.LastYear); year++)
                {
                    float child = row.Curve.PercentIn(year), have = parent.Curve.PercentIn(year);
                    if (child > have + 0.001f)
                        throw new VillageParseException(File, 0,
                            $"in {year}, {child:0.#}% have '{row.Name}' but only {have:0.#}% have the " +
                            $"'{parent.Name}' it needs. A row that needs another cannot be commoner " +
                            "than it, or the containment stops meaning anything.");
                }
            }
        }

        private static TechScope ParseScope(string s, int lineNo)
        {
            switch (s.ToLowerInvariant())
            {
                case "town": return TechScope.Town;
                case "household": return TechScope.Household;
                case "person": return TechScope.Person;
                case "farm": return TechScope.Farm;
                case "business": return TechScope.Business;
                default:
                    throw new VillageParseException(File, lineNo,
                        $"'{s}' is not a scope - use town, household, person, farm or business");
            }
        }

        /// <summary>"1998:22" -> (1998, 22).</summary>
        private static (int, float) ParseWaypoint(string s, int lineNo)
        {
            int colon = s.IndexOf(':');
            if (colon < 0)
                throw new VillageParseException(File, lineNo, $"'{s}' should look like year:percent");

            int year = ContentText.Int(s.Substring(0, colon), File, lineNo);
            if (!float.TryParse(s.Substring(colon + 1), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out float percent))
                throw new VillageParseException(File, lineNo,
                    $"'{s.Substring(colon + 1)}' is not a percentage");

            if (percent < 0f || percent > 100f)
                throw new VillageParseException(File, lineNo,
                    $"{percent:0.#} is not a share of the population");

            return (year, percent);
        }
    }
}
