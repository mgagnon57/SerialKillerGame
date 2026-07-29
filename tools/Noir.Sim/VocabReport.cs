using System;
using System.Collections.Generic;
using System.Text;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.World;

namespace Noir.Sim
{
    /// <summary>
    /// The content ceiling nobody can see.
    ///
    /// Every authored table has a silent population limit, and crossing it throws no exception,
    /// logs no line and drops no frame. The only symptom is a town where everybody starts
    /// sounding the same, and by the time that is noticeable in play it has been true for
    /// months. So it gets counted, at a population that has not been built yet.
    ///
    /// The measurement is the real generator against the real tables — not a model of it. That
    /// matters because the draws are not uniform: PickSurname prefers a surname nobody has yet,
    /// and a person's two or three particulars must differ from each other. Both of those
    /// flatten the distribution while the table is roomy and give up quietly once it is not,
    /// which is exactly the behaviour a closed-form estimate would miss.
    /// </summary>
    public static class VocabReport
    {
        /// <summary>
        /// The default bar: how many people one authored string may describe.
        ///
        /// A POLICY, not a measurement, and the one number here that is somebody's judgement
        /// rather than the village's. Four is chosen so that the ladder below straddles it —
        /// three men who whistle badly is a village, ten is a generator. Move it with --k and
        /// read the ladder to see what the move costs.
        /// </summary>
        public const int DefaultK = 4;

        /// <summary>How many independent resamples the bar must hold across. Content has to hold
        /// for the seed that ships, not the seed it was measured on.</summary>
        public const int DefaultSamples = 8;

        private const int SearchCap = 200_000;

        private enum Kind { Particulars, Male, Female, Surname }

        public sealed class Outcome
        {
            public string Text;
            public bool Failed;
        }

        /// <summary>
        /// The bench this command works on: the tagged tables it has built so far, and a count of
        /// the populations it has generated.
        ///
        /// The cache is not an optimisation for its own sake. The bisection asks for the same
        /// table size once per resample, so without it every search rebuilds a hundred thousand
        /// lines of text eight times over. The counter exists so the report can say what it cost
        /// in work rather than in seconds — a stopwatch reading would make two runs of the same
        /// seed differ, which is the one thing these reports may not do.
        /// </summary>
        private sealed class Lab
        {
            public readonly Dictionary<long, (NameTable names, ParticularsTable parts)> Tables =
                new Dictionary<long, (NameTable, ParticularsTable)>();

            public int Populations;

            public Population Generate(WorldModel world, NameTable names,
                                       ParticularsTable particulars, ulong seed)
            {
                Populations++;
                return PopulationGenerator.Generate(world, names, particulars, seed);
            }
        }

        public static Outcome Run(int targetPopulation, int k, int samples, ulong seed)
        {
            if (k < 1) k = DefaultK;
            if (samples < 1) samples = DefaultSamples;

            var realNames = NameTable.Parse(ContentPath.ReadAll("names.txt"));
            var realParticulars = ParticularsTable.Parse(ContentPath.ReadAll("particulars.txt"));
            var layout = VillageParser.Parse(ContentPath.ReadAll("village.txt"));

            var tables = new Sizes(realNames, realParticulars);
            var lab = new Lab();

            // ---- the world the population is drawn into ----
            //
            // The authored village is built either way. When a target is given it is not what is
            // measured, but the projection at the end scales places per head off it, and a
            // projection anchored to the sample town rather than to Ashcombe would be scaling
            // the target against itself and reporting no work to do.
            var authoredWorld = WorldBuilder.Build(layout);
            int authoredPeople = lab.Generate(authoredWorld, realNames, realParticulars, seed).Count;

            WorldModel world = authoredWorld;
            string worldNote = "Ashcombe exactly as authored — this is a measurement, not a sample.";

            if (targetPopulation > 0)
            {
                var (n0, p0) = Tagged(lab, tables, realNames, realParticulars, Kind.Particulars,
                                      tables.Particulars);
                world = SampleTown.ForPopulation(targetPopulation, seed, n0, p0,
                                                 out int homes, out int trials);
                lab.Populations += trials;
                worldNote = $"a sample town of {homes} homes, sized in {trials} trials until its "
                          + $"generated population landed on the target. Ashcombe holds {authoredPeople}.";
            }

            // ---- the run everything is reported from ----
            var tagged = Tagged(lab, tables, realNames, realParticulars, Kind.Particulars,
                                tables.Particulars);
            var people = lab.Generate(world, tagged.names, tagged.parts, seed);
            var real = lab.Generate(world, realNames, realParticulars, seed);
            bool agrees = SameVillage(people, real, realNames, realParticulars);

            var r = new Instrument();
            r.Line($"vocab — the content ceiling at {people.Count} people, seed {seed}");
            r.Line($"        {worldNote}");
            r.Line($"        bar: no authored string may describe more than {k} "
                 + $"{(k == 1 ? "person" : "people")}.");
            r.Line("        The bar is a policy and the one number here that is somebody's judgement.");
            r.Line("        Everything else is measured. Exits non-zero if this seed breaks the bar.");
            r.Line();
            Inventory(r);
            r.Line(agrees
                ? "  Counting is done on a run with every entry replaced by a tag, which is what makes"
                : "  WARNING: counting is done on a tagged run, and it did NOT reproduce the untagged");
            r.Line(agrees
                ? "  a draw attributable to one table and one line. It reproduced the untagged village"
                : "  village person for person — a table almost certainly holds the same string twice.");
            r.Line(agrees
                ? "  person for person, so nothing below is an artefact of the tagging."
                : "  Read the duplicate column before anything else.");
            r.Line();

            // ---- the drawn tables ----
            var drawn = new[] { Kind.Particulars, Kind.Male, Kind.Female, Kind.Surname };
            var counts = new Dictionary<Kind, int[]>();
            foreach (var kind in drawn)
                counts[kind] = Tally(people, kind, tables.Size(kind));

            var main = new Table("DRAWN TABLES — one row per authored table, at this population",
                new Col("table", 17, right: false), new Col("entries", 7), new Col("dupes", 5),
                new Col("draws", 7), new Col("per entry", 9), new Col("max", 5),
                new Col($"over {k}", 6), new Col("+need", 7),
                new Col($"worst/{samples}", 8), new Col("+need", 7));

            bool failed = false;
            var needed = new Dictionary<Kind, int>();
            var floors = new Dictionary<Kind, int>();

            foreach (var kind in drawn)
            {
                int size = tables.Size(kind);
                var byEntry = counts[kind];
                var byString = Aggregate(byEntry, tables.Entries(kind));

                long draws = 0;
                foreach (int c in byEntry) draws += c;

                int maxHere = 0;
                foreach (var kv in byString) if (kv.Value > maxHere) maxHere = kv.Value;

                int over = 0;
                foreach (var kv in byString) if (kv.Value > k) over++;

                int worst = Worst(world, lab, tables, realNames, realParticulars, kind, size,
                                  samples, seed);

                int needHere = Needed(world, lab, tables, realNames, realParticulars, kind, size,
                                      k, 1, seed);
                int needAny = Needed(world, lab, tables, realNames, realParticulars, kind, size,
                                     k, samples, seed);

                needed[kind] = needHere;
                floors[kind] = Math.Max(0, (int)Math.Ceiling(draws / (double)k) - size);
                if (maxHere > k) failed = true;

                main.Row(tables.Label(kind), size, size - byString.Count, draws,
                         size > 0 ? draws / (double)size : 0, maxHere, over,
                         Grow(needHere), worst, Grow(needAny));

                r.M("vocab_table", ("pop", people.Count), ("table", tables.Key(kind)),
                    ("entries", size), ("dupes", size - byString.Count), ("draws", draws),
                    ("per_entry", size > 0 ? draws / (double)size : 0),
                    ("per_head", people.Count > 0 ? draws / (double)people.Count : 0),
                    ("max", maxHere), ("k", k), ("over_k", over), ("need", needHere),
                    ("worst_seeds", worst), ("need_any_seed", needAny),
                    ("mean_floor", floors[kind]));
            }

            main.Note("'draws' is how many times the generator reached into the table, and 'per entry'");
            main.Note("is that divided by its length. 'max' and 'over' are for THIS seed — the village");
            main.Note("that actually ships, and what the exit code is decided on. The last two columns");
            main.Note($"are the worst of {samples} resamples on other seeds and what it would cost to hold the");
            main.Note("bar on any of them; the gap between the two '+need' columns is how much of the");
            main.Note("headroom is luck.");
            r.Add(main);

            // The tail is the whole difficulty and it is not obvious, so it is shown rather than
            // explained: the bar is on the busiest entry, and the busiest entry carries several
            // times the average however evenly the draws are spread.
            var floor = new Table($"THE FLOOR OF THE SAME JOB — entries to bring the AVERAGE to {k}",
                new Col("table", 17, right: false), new Col("have", 7), new Col("average", 8),
                new Col("+floor", 8), new Col("+need", 8), new Col("tail costs", 11));

            foreach (var kind in drawn)
            {
                int size = tables.Size(kind);
                long draws = 0;
                foreach (int c in counts[kind]) draws += c;

                floor.Row(tables.Label(kind), size, size > 0 ? draws / (double)size : 0,
                          Grow(floors[kind]), Grow(needed[kind]),
                          needed[kind] <= 0 ? "—"
                              : floors[kind] > 0 ? $"x{needed[kind] / (double)floors[kind]:0.0}"
                              : "ALL of it");
            }
            floor.Note("DERIVED, not measured: +floor is draws/k rounded up, less what the table already");
            floor.Note("holds. It is the least the job could possibly be. +need is the measured answer to");
            floor.Note("the real bar, and the last column is what the busiest entry costs over the");
            floor.Note("average one. Read the two as the ends of the range for the content task.");
            floor.Note("'ALL of it' means the AVERAGE is already inside the bar and every entry in +need");
            floor.Note("is bought purely to pull the busiest line down — which is the usual case, and the");
            floor.Note("reason a table can look roomy and read as repetitive at the same time.");
            r.Add(floor);

            // ---- who is over, by name ----
            var offenders = new Table("THE STRINGS DOING THE MOST WORK",
                new Col("table", 17, right: false), new Col("uses", 6),
                new Col("entry", 62, right: false));

            foreach (var kind in drawn)
            {
                var byString = Aggregate(counts[kind], tables.Entries(kind));
                foreach (var (text, uses) in Top(byString, 2))
                    offenders.Row(tables.Label(kind), uses, Clip(text, 62));
            }
            offenders.Note("Two per table, worst first. These are the lines a player meets most often.");
            r.Add(offenders);

            // ---- the ladder ----
            var ladder = new Table("ENTRIES NEEDED TO HOLD A GIVEN BAR, at this population",
                new Col("table", 17, right: false), new Col("have", 7),
                new Col("k=2", 9), new Col("k=3", 9), new Col("k=4", 9),
                new Col("k=6", 9), new Col("k=10", 9));

            int[] rungs = { 2, 3, 4, 6, 10 };
            foreach (var kind in drawn)
            {
                int size = tables.Size(kind);
                var cells = new object[rungs.Length + 2];
                cells[0] = tables.Label(kind);
                cells[1] = size;
                for (int i = 0; i < rungs.Length; i++)
                {
                    int need = Needed(world, lab, tables, realNames, realParticulars, kind, size,
                                      rungs[i], 1, seed);
                    cells[i + 2] = Grow(need);
                    r.M("vocab_ladder", ("pop", people.Count), ("table", tables.Key(kind)),
                        ("k", rungs[i]), ("need", need));
                }
                ladder.Row(cells);
            }
            ladder.Note("On this seed, matching the '+need' column above. The smallest table size the");
            ladder.Note("search could confirm holds the bar: found by bisection, and the busiest entry");
            ladder.Note("does not fall monotonically as a table grows, so a few entries either way are");
            ladder.Note("noise. '—' means no size under " + SearchCap + " held it.");
            ladder.Note("This is the table to read if the bar is the argument. It costs nothing to change");
            ladder.Note("k and everything to change it after the town is written.");
            r.Add(ladder);

            // ---- village.txt: authored once each, and therefore a different kind of ceiling ----
            AuthoredPlaces(r, layout, authoredPeople, targetPopulation);

            // ---- findings ----
            foreach (var kind in drawn)
            {
                int need = needed[kind];
                if (need > 0)
                    r.Finding($"{tables.Label(kind)}: {tables.Size(kind)} entries today, "
                            + $"{tables.Size(kind) + need} needed to hold k={k} at {people.Count} "
                            + $"people — {need} more to write. "
                            + (floors[kind] > 0
                                ? $"{floors[kind]} of those only bring the average inside the bar; the "
                                  + "rest are the busiest line."
                                : $"The average is already inside the bar at {k}; all {need} are bought "
                                  + "to pull the busiest line down."));
            }

            if (!failed)
            {
                r.Finding($"Every drawn table holds k={k} at {people.Count} people on this seed.");

                var marginal = new List<string>();
                foreach (var kind in drawn)
                {
                    int worst = Worst(world, lab, tables, realNames, realParticulars, kind,
                                      tables.Size(kind), samples, seed);
                    if (worst > k) marginal.Add($"{tables.Label(kind)} reaches {worst}");
                }
                if (marginal.Count > 0)
                    r.Finding("It holds on THIS seed and not on every seed: across " + samples
                            + " resamples, " + string.Join(", ", marginal) + ". The tables are at "
                            + "their limit at this population, not comfortably inside it.");
            }

            r.Finding("Run this with --pop before the town is authored, not after. The tables are the "
                    + "cheapest thing in the project to grow and the most expensive to notice you "
                    + "should have grown.");

            // What it cost, in work rather than in seconds — see Lab.
            r.Line($"  COST — {lab.Populations:#,0} populations generated, "
                 + $"{(targetPopulation > 0 ? "Ashcombe plus one sample town" : "one world")} built, "
                 + "no simulation ticked.");
            r.Line($"         {lab.Tables.Count} distinct table sizes were built and cached. Almost all "
                 + "of that is the");
            r.Line("         bisection: --samples 1 makes it eight times cheaper and the "
                 + $"worst/{samples} column");
            r.Line("         meaningless.");
            r.Line();
            r.M("vocab_cost", ("populations", lab.Populations), ("table_builds", lab.Tables.Count),
                ("samples", samples));

            return new Outcome { Text = r.ToString(), Failed = failed };
        }

        // ------------------------------------------------------------------
        //  what is in Content/ at all
        // ------------------------------------------------------------------

        /// <summary>
        /// Every text file in Content/, and whether this command knows how it is drawn.
        ///
        /// Listed rather than assumed, because the whole failure this command exists to catch is
        /// a silent one. A table added next month that nothing here samples would otherwise be
        /// invisible in exactly the way the particulars ceiling was: no exception, no log line,
        /// and a report that looks complete. An unmeasured file is named and called unmeasured,
        /// which is the least this can do about a table it has never heard of.
        /// </summary>
        private static void Inventory(Instrument r)
        {
            var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "names.txt", "drawn — male forenames, female forenames, surnames" },
                { "particulars.txt", "drawn — one clause per line" },
                { "village.txt", "authored once each — place names and `human` lines" },
            };

            var table = new Table("CONTENT/ — read off the folder, not off a list in this file",
                new Col("file", 20, right: false), new Col("lines", 7), new Col("bytes", 9),
                new Col("", 52, right: false));

            var files = new List<string>(System.IO.Directory.GetFiles(ContentPath.Root, "*.txt"));
            files.Sort(StringComparer.Ordinal);

            int unmeasured = 0;
            foreach (string path in files)
            {
                string name = System.IO.Path.GetFileName(path);
                string text = System.IO.File.ReadAllText(path);

                bool measured = known.TryGetValue(name, out string what);
                if (!measured) unmeasured++;

                table.Row(name, MeaningfulLines(text), text.Length,
                          measured ? what : "NOT MEASURED — this command does not know how it is drawn");
            }

            table.Note("'lines' counts what the parsers count: comments and blanks stripped.");
            table.Note("Content/tiles, Content/audio and Content/textures hold PNG and WAV that the");
            table.Note("`tiles` and `audio` commands regenerate from code — no authored strings in them.");
            if (unmeasured > 0)
                table.Note($"{unmeasured} file(s) above are NOT measured. Teach this command how they are "
                         + "drawn, or the ceiling on them stays invisible.");
            r.Add(table);

            r.M("vocab_inventory", ("files", files.Count), ("unmeasured", unmeasured));
        }

        // ------------------------------------------------------------------
        //  village.txt — authored once each rather than drawn
        // ------------------------------------------------------------------

        private static void AuthoredPlaces(Instrument r, VillageLayout layout, int population,
                                           int target)
        {
            var names = new Dictionary<string, int>(StringComparer.Ordinal);
            var humans = new Dictionary<string, int>(StringComparer.Ordinal);
            int dwellings = 0, needHuman = 0, missingHuman = 0, units = 0;

            foreach (var place in layout.Places)
            {
                Bump(names, place.Name);
                if (place.Kind == PlaceKind.Dwelling)
                {
                    dwellings++;
                    units += Math.Max(1, place.Units);
                }
                else
                {
                    // Dwellings take their character from the household inside, so only the rest
                    // are expected to carry a line — see WorldValidator.
                    needHuman++;
                    if (string.IsNullOrEmpty(place.Human)) missingHuman++;
                    else Bump(humans, place.Human);
                }
            }

            var table = new Table("VILLAGE.TXT — authored once each, so the ceiling is a different shape",
                new Col("table", 17, right: false), new Col("entries", 8), new Col("dupes", 6),
                new Col("used", 8), new Col("per entry", 10), new Col("missing", 8));

            table.Row("place name", layout.Places.Count, layout.Places.Count - names.Count,
                      layout.Places.Count, 1.0, 0);
            table.Row("human line", needHuman - missingHuman, needHuman - missingHuman - humans.Count,
                      needHuman - missingHuman, 1.0, missingHuman);
            table.Note("Nothing is drawn here: each string is written for one place and used by that");
            table.Note("place. A duplicate is a straight authoring mistake — WorldBuilder refuses two");
            table.Note("places with the same name outright — so 'dupes' should read zero forever.");
            r.Add(table);

            r.M("vocab_authored", ("places", layout.Places.Count), ("dwellings", dwellings),
                ("units", units), ("name_dupes", layout.Places.Count - names.Count),
                ("human_lines", needHuman - missingHuman),
                ("human_dupes", needHuman - missingHuman - humans.Count),
                ("human_missing", missingHuman));

            if (target <= 0) return;

            // DERIVED, and the only derived figure in this command. It assumes the town keeps
            // Ashcombe's places-per-head, which a town will not do exactly — a settlement of six
            // hundred gets a second shop before it gets six times the churches.
            double scale = target / (double)population;
            int placesAt = (int)Math.Ceiling(layout.Places.Count * scale);
            int humansAt = (int)Math.Ceiling(needHuman * scale);

            r.Line($"    DERIVED, not measured — at {target} people, holding Ashcombe's {population} people");
            r.Line($"    across {layout.Places.Count} places:");
            r.Line($"      places        {layout.Places.Count,6}  ->{placesAt,7}   "
                 + $"{placesAt - layout.Places.Count} more building names to invent, all distinct");
            r.Line($"      human lines   {needHuman,6}  ->{humansAt,7}   "
                 + $"{humansAt - needHuman} more sentences to write");
            r.Line("      A town does not scale its amenities linearly, so read these as the order of");
            r.Line("      the job and not the job. The place-name figure is the firm one: WorldBuilder");
            r.Line("      rejects a layout with two places called the same thing, so every one of them");
            r.Line("      has to be different.");
            r.Line();
            r.M("vocab_authored_projected", ("target", target), ("places", placesAt),
                ("human_lines", humansAt));
        }

        // ------------------------------------------------------------------
        //  measurement
        // ------------------------------------------------------------------

        /// <summary>The real length of each table, and the strings in it.</summary>
        private sealed class Sizes
        {
            private readonly NameTable _names;
            private readonly ParticularsTable _particulars;

            public Sizes(NameTable names, ParticularsTable particulars)
            {
                _names = names;
                _particulars = particulars;
            }

            public int Particulars => _particulars.Count;

            public int Size(Kind kind)
            {
                switch (kind)
                {
                    case Kind.Particulars: return _particulars.Count;
                    case Kind.Male: return _names.Male.Count;
                    case Kind.Female: return _names.Female.Count;
                    default: return _names.Surnames.Count;
                }
            }

            public IReadOnlyList<string> Entries(Kind kind)
            {
                switch (kind)
                {
                    case Kind.Particulars: return _particulars.Clauses;
                    case Kind.Male: return _names.Male;
                    case Kind.Female: return _names.Female;
                    default: return _names.Surnames;
                }
            }

            public string Label(Kind kind)
            {
                switch (kind)
                {
                    case Kind.Particulars: return "particulars";
                    case Kind.Male: return "forename (male)";
                    case Kind.Female: return "forename (female)";
                    default: return "surname";
                }
            }

            public string Key(Kind kind) => Label(kind).Replace(" (", "_").Replace(")", "");
        }

        /// <summary>
        /// The content tables with every entry replaced by a tag.
        ///
        /// Tagging is what makes a draw attributable. A citizen holds a forename, not a table and
        /// an index, so counting real names cannot tell a name that appears in two lists from one
        /// that appears twice in one, and cannot tell either from a name used twice. Tags remove
        /// the ambiguity without changing a single roll: the generator picks by index and never
        /// looks at what it drew, except when PickSurname asks whether a surname is already
        /// taken — which behaves identically as long as the real table has no duplicates. That is
        /// checked, and reported, rather than assumed.
        /// </summary>
        private static (NameTable names, ParticularsTable parts) Tagged(
            Lab lab, Sizes tables,
            NameTable realNames, ParticularsTable realParticulars, Kind kind, int size)
        {
            long key = (long)kind * 1_000_000L + size;
            if (lab.Tables.TryGetValue(key, out var hit)) return hit;

            int male = kind == Kind.Male ? size : tables.Size(Kind.Male);
            int female = kind == Kind.Female ? size : tables.Size(Kind.Female);
            int surname = kind == Kind.Surname ? size : tables.Size(Kind.Surname);
            int clauses = kind == Kind.Particulars ? size : tables.Size(Kind.Particulars);

            var sb = new StringBuilder(male * 8 + female * 8 + surname * 10);
            for (int i = 0; i < male; i++) sb.Append("male m").Append(i).Append('\n');
            for (int i = 0; i < female; i++) sb.Append("female f").Append(i).Append('\n');
            for (int i = 0; i < surname; i++) sb.Append("surname s").Append(i).Append('\n');
            var names = NameTable.Parse(sb.ToString());

            sb.Clear();
            for (int i = 0; i < clauses; i++) sb.Append('p').Append(i).Append('\n');
            var parts = ParticularsTable.Parse(sb.ToString());

            var made = (names, parts);
            lab.Tables[key] = made;
            return made;
        }

        /// <summary>How many times each entry of one table was used, by entry index.</summary>
        private static int[] Tally(Population people, Kind kind, int size)
        {
            var counts = new int[size];

            if (kind == Kind.Particulars)
            {
                foreach (var c in people.Citizens)
                    foreach (int p in c.Particulars)
                        if (p >= 0 && p < size) counts[p]++;
                return counts;
            }

            if (kind == Kind.Surname)
            {
                // One use per DRAW, which is one per household plus one per lodger. A family of
                // four called Pethick is one Pethick in the village, not four — what a player
                // meets is households — and PopulationGenerator draws it exactly once for them.
                foreach (var h in people.Households)
                {
                    Add(counts, h.Surname, 's', size);
                    if (h.Shape != HouseholdShape.Sharers) continue;

                    foreach (var id in h.Members)
                        Add(counts, people.Get(id).Surname, 's', size);
                }
                return counts;
            }

            char want = kind == Kind.Male ? 'm' : 'f';
            foreach (var c in people.Citizens) Add(counts, c.Forename, want, size);
            return counts;
        }

        private static void Add(int[] counts, string tag, char prefix, int size)
        {
            if (string.IsNullOrEmpty(tag) || tag[0] != prefix) return;
            if (!int.TryParse(tag.Substring(1), out int index)) return;
            if (index >= 0 && index < size) counts[index]++;
        }

        /// <summary>
        /// Per-entry counts folded onto the strings themselves.
        ///
        /// The bar is about a STRING — the thing a player reads — so two table lines holding the
        /// same words are one offender with the sum of their uses, not two innocents.
        /// </summary>
        private static Dictionary<string, int> Aggregate(int[] byEntry, IReadOnlyList<string> entries)
        {
            var byString = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < byEntry.Length && i < entries.Count; i++)
            {
                byString.TryGetValue(entries[i], out int had);
                byString[entries[i]] = had + byEntry[i];
            }
            return byString;
        }

        /// <summary>
        /// The most any one authored string is used, over several independent resamples.
        ///
        /// Sample zero is the seed being reported on, so this can never come back lower than the
        /// village's own figure and the two columns are always readable together.
        /// </summary>
        private static int Worst(WorldModel world,
                                 Lab lab, Sizes tables,
                                 NameTable realNames, ParticularsTable realParticulars,
                                 Kind kind, int size, int samples, ulong seed)
        {
            var entries = tables.Entries(kind);
            int worst = 0;

            for (int s = 0; s < samples; s++)
            {
                ulong trial = s == 0 ? seed : seed ^ Keys.Of("vocab-resample-" + s);
                var (names, parts) = Tagged(lab, tables, realNames, realParticulars, kind, size);
                var people = lab.Generate(world, names, parts, trial);

                int here = MaxUses(Tally(people, kind, size), entries);
                if (here > worst) worst = here;
            }
            return worst;
        }

        /// <summary>
        /// The busiest STRING, not the busiest table row. They differ only when a table holds the
        /// same words twice, which is itself worth catching — two lines reading the same thing
        /// are one line a player meets twice as often.
        /// </summary>
        private static int MaxUses(int[] byEntry, IReadOnlyList<string> entries)
        {
            int worst = 0;
            var byString = Aggregate(byEntry, entries);
            foreach (var kv in byString) if (kv.Value > worst) worst = kv.Value;

            // Padding beyond the authored table has no string of its own; each entry is its own.
            for (int i = entries.Count; i < byEntry.Length; i++)
                if (byEntry[i] > worst) worst = byEntry[i];

            return worst;
        }

        private static string Grow(int need) =>
            need == 0 ? "ok" : need < 0 ? "—" : "+" + need;

        /// <summary>
        /// How many entries must be ADDED before every resample holds the bar. Zero if it
        /// already does; negative if no table under the cap did.
        /// </summary>
        private static int Needed(WorldModel world,
                                  Lab lab, Sizes tables,
                                  NameTable realNames, ParticularsTable realParticulars,
                                  Kind kind, int size, int k, int samples, ulong seed)
        {
            if (Worst(world, lab, tables, realNames, realParticulars, kind, size, samples, seed) <= k)
                return 0;

            int low = size, high = size;
            while (true)
            {
                low = high;
                high *= 2;
                if (high > SearchCap) return -1;
                if (Worst(world, lab, tables, realNames, realParticulars, kind, high, samples, seed) <= k)
                    break;
            }

            while (low + 1 < high)
            {
                int mid = low + (high - low) / 2;
                if (Worst(world, lab, tables, realNames, realParticulars, kind, mid, samples, seed) <= k)
                    high = mid;
                else low = mid;
            }
            return high - size;
        }

        /// <summary>Does the tagged village name the same people the untagged one does?</summary>
        private static bool SameVillage(Population tagged, Population real,
                                        NameTable names, ParticularsTable particulars)
        {
            if (tagged.Count != real.Count) return false;

            for (int i = 0; i < tagged.Count; i++)
            {
                var a = tagged.Get(new CitizenId(i));
                var b = real.Get(new CitizenId(i));
                if (a.Age != b.Age || a.Stage != b.Stage || a.Household != b.Household) return false;
                if (a.Particulars.Length != b.Particulars.Length) return false;

                string tag = a.Forename;
                if (tag.Length < 2 || !int.TryParse(tag.Substring(1), out int index)) return false;

                var list = tag[0] == 'm' ? names.Male : names.Female;
                if (index >= list.Count || list[index] != b.Forename) return false;
            }
            return true;
        }

        private static void Bump(Dictionary<string, int> counts, string text)
        {
            if (text == null) text = "";
            counts.TryGetValue(text, out int had);
            counts[text] = had + 1;
        }

        /// <summary>The most-used entries, worst first, ties broken by the string so that two runs
        /// of the same seed print the same order.</summary>
        private static List<(string text, int uses)> Top(Dictionary<string, int> byString, int howMany)
        {
            var all = new List<(string text, int uses)>();
            foreach (var kv in byString) all.Add((kv.Key, kv.Value));
            all.Sort((x, y) => x.uses != y.uses
                ? y.uses.CompareTo(x.uses)
                : string.CompareOrdinal(x.text, y.text));

            if (all.Count > howMany) all.RemoveRange(howMany, all.Count - howMany);
            return all;
        }

        private static string Clip(string text, int width) =>
            text.Length <= width ? text : text.Substring(0, width - 1) + "…";

        /// <summary>
        /// Lines a parser would keep: comment stripped at the first '#', trimmed, non-empty.
        ///
        /// The same rule NameTable and ParticularsTable use, restated here rather than called,
        /// because theirs is internal to Core and this is a harness. It is used only for the
        /// inventory line count of files nothing has taught this command to parse.
        /// </summary>
        private static int MeaningfulLines(string text)
        {
            int lines = 0;
            foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                int hash = raw.IndexOf('#');
                if ((hash >= 0 ? raw.Substring(0, hash) : raw).Trim().Length > 0) lines++;
            }
            return lines;
        }
    }
}
