using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Noir.Core.Contracts;
using Noir.Core.Observation;
using Noir.Core.World;

namespace Noir.Sim
{
    /// <summary>
    /// THE 2:1 RULE, measured.
    ///
    /// The design document says it in one sentence: watching someone must yield more useless
    /// information than useful, or the villagers are locks rather than people. Every other
    /// instrument in this harness grades something the village has — jobs, encounters, content
    /// ceilings. This one grades the only thing the project set itself a number for, and it is
    /// the number nobody had ever computed.
    ///
    /// What it does NOT do is worth saying first, because it is most of the design. The census
    /// names nobody, including by index. It prints no time, no day and no place. It renders the
    /// two columns asymmetrically — the texture side in full English, the tactical side as a
    /// bare count by question — and there is no code path in this file that formats a useful
    /// proposition's key. That is a restriction on the OUTPUT rather than on the computation,
    /// and it is chosen deliberately: the count is all the ratio needs and the sentence is all
    /// an audit of the texture needs, so the tactical column's prose is the one part of this
    /// the measurement has no use for. Not producing it costs nothing and removes the artifact.
    ///
    /// The obvious ending for a single-subject report is "she is alone on the lane most weekday
    /// mornings; her house is dark from nine to four; nobody has called in a fortnight". The
    /// instrument holds all three of those at the moment it prints, and prints none of them.
    /// Their absence is the design.
    /// </summary>
    public static class Ratio
    {
        /// <summary>The rule, verbatim. Two of texture for every one of use.</summary>
        public const double Required = 2.00;

        /// <summary>
        /// Below this you have learned more about how to hurt somebody than about who they are —
        /// the design document's definition of a lock. Applied at the tenth percentile and not
        /// at the minimum, because with a hundred and eighteen stochastic day plans a minimum is
        /// one unlucky draw from red on every seed, and a flaky gate is a deleted gate.
        ///
        /// Deliberately 1.00 rather than 2.00. A night-shift mill hand genuinely is more legible
        /// than a publican, and flattening that would fail the plan's other tests in the
        /// opposite direction.
        /// </summary>
        public const double RequiredAtTenth = 1.00;

        /// <summary>
        /// Nobody is only a schedule. Eight is one new kind of thing per weekday of the
        /// fortnight; below it, most days of the watch added nothing at all. That is a content
        /// bug rather than a spread, and it is reported as a count and never as a person.
        /// </summary>
        public const int RequiredTextureFloor = 8;

        /// <summary>The eye has not been narrowed. Guards against making the ratio look good by
        /// seeing less of the village.</summary>
        public const double RequiredSightShare = 0.025;

        public const string FloorFile = "watched.floor";

        // ---------------------------------------------------------------------------------
        //  one seed, measured
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// What a fortnight over one village came to. Arrays are per-person and in agent order,
        /// which is an ordering of a population and not a ranking of one — nothing in this file
        /// ever sorts a person, only their numbers, and only after the identity has been dropped.
        /// </summary>
        public sealed class Watch
        {
            public ulong Seed;
            public int People;
            public int MinutesWatched;

            public double[] Ratios;
            public double[] Useful;
            public double[] Texture;
            public double[] SightShare;
            public double[] Entries;

            public long AloneTotal, HouseEmptyTotal, UncalledTotal;

            /// <summary>How many people had any texture proposition of each act, and of each
            /// act with each manner bit set. Counts of people, never people.</summary>
            public int[] ActPeople;
            public int[,] ActMannerPeople;

            public int ActKindsSeen;
            public long CallerDays;
            public long HouseholdDays;
        }

        public static Watch Measure(VillageContext ctx)
        {
            int days = Salience.DaysWatched;
            var logs = Eyewitness.WatchAll(ctx, days);
            int n = logs.Length;

            var w = new Watch
            {
                Seed = ctx.Seed,
                People = n,
                MinutesWatched = days * 1440,
                Ratios = new double[n],
                Useful = new double[n],
                Texture = new double[n],
                SightShare = new double[n],
                Entries = new double[n],
                ActPeople = new int[ActCount],
                ActMannerPeople = new int[ActCount, MannerBits.Length],
            };

            var everyAct = new HashSet<int>();
            var keys = new HashSet<int>();

            for (int i = 0; i < n; i++)
            {
                keys.Clear();
                WatchTally tally = Salience.Weigh(logs[i], keys);

                w.Ratios[i] = tally.Ratio;
                w.Useful[i] = tally.Useful;
                w.Texture[i] = tally.Texture;
                w.Entries[i] = tally.Entries;
                w.SightShare[i] = tally.MinutesInSight / (double)w.MinutesWatched;

                w.AloneTotal += tally.Alone;
                w.HouseEmptyTotal += tally.HouseStandsEmpty;
                w.UncalledTotal += tally.NobodyCalls;

                // Per PERSON, not per proposition: "how many villagers ever came out carrying
                // something" is a fact about the village's vocabulary. "How many times" is a
                // fact about one person's fortnight, and this file has no use for it.
                var actsHere = new HashSet<int>();
                var pairsHere = new HashSet<int>();
                foreach (int key in keys)
                {
                    int act = (int)Salience.ActOf(key);
                    var manner = Salience.MannerOf(key);
                    if (act >= ActCount) continue;

                    actsHere.Add(act);
                    everyAct.Add(act);

                    for (int b = 0; b < MannerBits.Length; b++)
                        if ((manner & MannerBits[b]) != 0) pairsHere.Add(act * 16 + b);
                }

                foreach (int act in actsHere) w.ActPeople[act]++;
                foreach (int pair in pairsHere) w.ActMannerPeople[pair / 16, pair % 16]++;
            }

            w.ActKindsSeen = everyAct.Count;

            // The one content fact the first run turns on: does anybody ever call on anybody.
            // Counted from the logs rather than from the day plans, because a visit nobody could
            // have seen is not a visit this instrument is entitled to know about.
            w.HouseholdDays = 0;
            w.CallerDays = 0;
            for (int i = 0; i < n; i++)
            {
                int uncalledDays = 0;
                foreach (Observed o in logs[i].Entries)
                    if (o.Act == ObservedAct.NobodyCameOrWent) uncalledDays++;
                w.HouseholdDays += days;
                w.CallerDays += days - uncalledDays;
            }

            return w;
        }

        // ---------------------------------------------------------------------------------
        //  the census
        // ---------------------------------------------------------------------------------

        public static string Census(ulong[] seeds)
        {
            if (seeds == null || seeds.Length == 0) seeds = new ulong[] { 1979 };

            var floor = Floor.Load();
            var r = new Instrument();
            var results = new List<Watch>();

            foreach (ulong seed in seeds)
            {
                var ctx = VillageContext.Load(1, seed);
                var w = Measure(ctx);
                results.Add(w);
                Describe(r, ctx, w, floor, seeds.Length > 1);
            }

            if (results.Count > 1) Combined(r, results, floor);

            Findings(r, results, floor);
            return r.ToString();
        }

        private static void Describe(Instrument r, VillageContext ctx, Watch w, Floor floor,
                                     bool manySeeds)
        {
            int n = w.People;
            var ratios = Sorted(w.Ratios);
            var texture = Sorted(w.Texture);
            var sight = Sorted(w.SightShare);

            double medianRatio = Spread.Percentile(ratios, 50);
            double tenth = Spread.Percentile(ratios, 10);
            double medianTexture = Spread.Percentile(texture, 50);
            double minTexture = Spread.Percentile(texture, 0);
            double medianSight = Spread.Percentile(sight, 50);

            r.Line();
            r.Line($"THE 2:1 RULE — {ctx.World.Name} watched");
            r.Line($"  {n} people, seed {w.Seed} · {Words(Salience.DaysWatched)} days each · "
                 + $"one simulation pass, {n} watchers");
            r.Line($"  sampled once a minute · {w.MinutesWatched:#,0} minutes watched · the median "
                 + "villager was in");
            r.Line($"  sight for {medianSight * w.MinutesWatched:#,0} of them "
                 + $"({medianSight * 100:0.0}%) — everything else was a closed door");

            // ---- the distribution ----
            r.Heading("WHAT A FORTNIGHT ON THE PAVEMENT YIELDS, PER PERSON");

            var hist = new Table("", new Col("ratio", 12, right: false),
                                     new Col("", 38, right: false), new Col("people", 6));
            Histogram(hist, w.Ratios, RatioEdges);
            r.Add(hist);

            // ---- the gates ----
            var gates = new Table("", new Col("", 32, right: false), new Col("measured", 8),
                                      new Col("needs", 7), new Col("floor", 8),
                                      new Col("", 7, right: false));

            Gate(gates, "median ratio", medianRatio, Required, floor.RatioMedian, "0.00");
            Gate(gates, "tenth percentile", tenth, RequiredAtTenth, floor.RatioP10, "0.00");
            Gate(gates, "distinct texture, minimum", minTexture, RequiredTextureFloor,
                 floor.TextureMin, "0");
            Gate(gates, "distinct texture, median", medianTexture, double.NaN,
                 floor.TextureMedian, "0");
            GatePercent(gates, "minutes in sight, median", medianSight, RequiredSightShare,
                        floor.SightMedian);
            r.Add(gates);

            // ---- the sign test ----
            //
            // Printed every run and not yet a gate. A fuller life ought to score BETTER; today
            // it scores worse, because time outdoors alone accrues tactical propositions while
            // the texture vocabulary is closed at nine verbs. Until this flips, the ratio alone
            // is not a measure of "more life", and the median-texture floor is what stops the
            // village being strip-mined for a better number in the meantime.
            var (busiest, quietest) = SignTest(w);

            r.Line($"    the sign test    busiest quarter {busiest:0.00}  ·  "
                 + $"quietest quarter {quietest:0.00}");
            r.Line(busiest >= quietest
                ? "                     a fuller life scores better.  printed, not yet a gate"
                : "                     a fuller life currently scores WORSE.  printed, not yet a gate");
            r.Line();

            var useful = Sorted(w.Useful);
            r.Line("    distinct propositions per person   useful  "
                 + Five(useful));
            r.Line("                                      texture  "
                 + Five(texture));
            r.Line();

            r.M("ratio", ("seed", w.Seed), ("people", n), ("days", Salience.DaysWatched),
                ("median", medianRatio), ("p10", tenth), ("mean", Spread.Mean(w.Ratios)),
                ("texture_median", medianTexture), ("texture_min", minTexture),
                ("useful_median", Spread.Percentile(useful, 50)),
                ("sight_median", medianSight),
                ("sign_busiest", busiest), ("sign_quietest", quietest));

            // ---- where the tactical column comes from ----
            r.Heading("WHERE THE TACTICAL COLUMN COMES FROM — by question, never by person");
            r.Line($"    when she is alone                      {w.AloneTotal,10:#,0}");
            r.Line($"    when the house stands empty            {w.HouseEmptyTotal,10:#,0}");
            r.Line($"    whether anybody would notice her gone  {w.UncalledTotal,10:#,0}");
            r.Line();
            r.Line("    Confirmed propositions, summed over everybody. There is no code in");
            r.Line("    Ratio.cs that can turn one of these back into an hour or a day, and");
            r.Line("    there is no flag that will add one.");
            r.Line();
            r.M("ratio_useful", ("seed", w.Seed), ("alone", w.AloneTotal),
                ("house_empty", w.HouseEmptyTotal), ("uncalled", w.UncalledTotal));

            // ---- where the texture comes from ----
            r.Heading("WHERE THE TEXTURE COMES FROM — by kind of moment, never by person");

            var cols = new List<Col> { new Col("", 24, right: false), new Col("people", 7) };
            foreach (string word in MannerWords) cols.Add(new Col(word, 7));

            var kinds = new Table("", cols.ToArray());
            for (int act = 1; act < ActCount; act++)
            {
                if (w.ActPeople[act] == 0) continue;

                var cells = new object[MannerWords.Length + 2];
                cells[0] = ActWords[act];
                cells[1] = w.ActPeople[act];
                for (int b = 0; b < MannerBits.Length; b++)
                    cells[b + 2] = w.ActMannerPeople[act, b] == 0 ? (object)"" : w.ActMannerPeople[act, b];
                kinds.Row(cells);
            }
            kinds.Note("'people' is how many of the village yielded that kind of moment at all, and");
            kinds.Note("each manner column the same again for that act with that manner. Nothing here");
            kinds.Note("is a count of TIMES: both columns of the ratio are sets of propositions, so a");
            kinds.Note("village cannot raise either of them by doing more of the same.");
            r.Add(kinds);

            for (int act = 1; act < ActCount; act++)
                if (w.ActPeople[act] > 0)
                    r.M("ratio_texture", ("seed", w.Seed), ("act", ((ObservedAct)act).ToString()),
                        ("people", w.ActPeople[act]));

            if (!manySeeds)
            {
                r.Line("  Nobody is named above, and the people below the line are not listed.");
                r.Line("  Run `ratio <n>` on somebody you have chosen yourself.");
                r.Line();
            }
        }

        private static void Combined(Instrument r, List<Watch> results, Floor floor)
        {
            r.Heading("ALL SEEDS — the verdict the gate reads");

            var table = new Table("", new Col("seed", 8, right: false), new Col("median", 8),
                                      new Col("p10", 8), new Col("texture min", 12),
                                      new Col("texture med", 12), new Col("in sight", 9),
                                      new Col("", 7, right: false));

            double worstMedian = double.MaxValue, worstTenth = double.MaxValue;
            double worstTextureMin = double.MaxValue, worstTextureMedian = double.MaxValue;
            double worstSight = double.MaxValue;

            foreach (Watch w in results)
            {
                var ratios = Sorted(w.Ratios);
                var texture = Sorted(w.Texture);
                var sight = Sorted(w.SightShare);

                double median = Spread.Percentile(ratios, 50);
                double tenth = Spread.Percentile(ratios, 10);
                double tmin = Spread.Percentile(texture, 0);
                double tmed = Spread.Percentile(texture, 50);
                double smed = Spread.Percentile(sight, 50);

                worstMedian = Math.Min(worstMedian, median);
                worstTenth = Math.Min(worstTenth, tenth);
                worstTextureMin = Math.Min(worstTextureMin, tmin);
                worstTextureMedian = Math.Min(worstTextureMedian, tmed);
                worstSight = Math.Min(worstSight, smed);

                bool ok = median >= Required && tenth >= RequiredAtTenth &&
                          tmin >= RequiredTextureFloor;
                table.Row(w.Seed.ToString(CultureInfo.InvariantCulture), median.ToString("0.00"),
                          tenth.ToString("0.00"), tmin.ToString("0"), tmed.ToString("0"),
                          (smed * 100).ToString("0.0") + "%", ok ? "holds" : "FAIL");
            }

            table.Note("The gate reads the WORST seed of the three, not their mean. An instrument");
            table.Note("that averages three villages tells you about no village.");
            r.Add(table);

            r.M("ratio_all", ("seeds", results.Count), ("worst_median", worstMedian),
                ("worst_p10", worstTenth), ("worst_texture_min", worstTextureMin),
                ("worst_texture_median", worstTextureMedian), ("worst_sight", worstSight));
        }

        // ---------------------------------------------------------------------------------
        //  the findings — what to fix, not merely that something is wrong
        // ---------------------------------------------------------------------------------

        private static void Findings(Instrument r, List<Watch> results, Floor floor)
        {
            Watch w = results[0];
            int n = w.People;

            var ratios = Sorted(w.Ratios);
            var texture = Sorted(w.Texture);
            var sight = Sorted(w.SightShare);
            double medianRatio = Spread.Percentile(ratios, 50);
            double tenth = Spread.Percentile(ratios, 10);
            double minTexture = Spread.Percentile(texture, 0);
            double medianTexture = Spread.Percentile(texture, 50);
            double medianSight = Spread.Percentile(sight, 50);

            if (medianRatio < Required)
                Say(r, $"THE VILLAGE IS AT {medianRatio:0.00} : 1 AND THE RULE IS 2 : 1. The median "
                        + $"villager yields {Spread.Percentile(texture, 50):0} distinct kinds of moment "
                        + $"against {Spread.Percentile(Sorted(w.Useful), 50):0} confirmed tactical "
                        + "propositions. The fix is not fewer tactical propositions — those are the "
                        + "honest consequence of people having timetables. It is more KINDS of "
                        + $"moment: the observable vocabulary is {w.ActKindsSeen} verbs, and the "
                        + "ratio moves only when that grows.");

            if (w.CallerDays == 0)
                Say(r, $"NOBODY CALLED ON ANYBODY. In {Salience.DaysWatched} simulated days, across "
                        + $"{n} people and {w.HouseholdDays:#,0} household-days, not one villager set "
                        + "foot in a dwelling that was not their own. Zero. \"Who would notice them "
                        + "gone and how fast\" therefore has the same answer for all of them, and the "
                        + "answer is nobody. FIX: DayPlanner.ChooseErrand offers Shop, PostOffice, "
                        + "Green, Allotments, VillageHall, Pub, Churchyard and Playground. It does "
                        + "not offer a dwelling. Expect this number to move sharply — in either "
                        + "direction — the first time it does, and re-measure Content/watched.floor "
                        + "in that same commit rather than assuming the sign.");
            else
                Say(r, $"Somebody called: {w.CallerDays:#,0} of {w.HouseholdDays:#,0} household-days "
                        + "had a visitor. Q3 now discriminates between people, which it could not "
                        + "when the answer was nobody, everywhere, always.");

            Say(r, $"THE OBSERVABLE VOCABULARY IS {Words(w.ActKindsSeen).ToUpperInvariant()} VERBS. "
                    + "Content/particulars.txt holds hundreds of clauses and its own header says "
                    + "\"when we measure whether watching someone yields more useless information "
                    + "than useful, THIS is the numerator\". Citizen.Particulars is read by the "
                    + "inspectors and by no line of simulation code. FIX: the numerator is not in "
                    + "the sum because it is not ENACTED — a particular has to become something a "
                    + "watcher across the road could see happen before it can count here.");

            if (minTexture < RequiredTextureFloor)
                Say(r, $"SOMEBODY YIELDED ONLY {minTexture:0} DISTINCT KINDS OF MOMENT in a "
                        + $"fortnight, against a floor of {RequiredTextureFloor} — one new kind of "
                        + "thing per weekday. That is a content bug rather than a spread: most days "
                        + "of that person's watch added nothing at all. The count is reported and "
                        + "the person is not; find them with `ratio <n>` if you mean to.");

            Say(r, $"THE MEDIAN VILLAGER IS IN SIGHT FOR {medianSight * 1440:0} MINUTES A DAY "
                    + $"({medianSight * 100:0.0}% of the watch). This instrument grades the STREET "
                    + "and nothing else, and it says so rather than pretending otherwise. A village "
                    + "where every citizen gains a rich enacted interior life moves this number by "
                    + "zero. That needs a second instrument, and this one must not be extended to "
                    + "cover it: the moment it can see through a wall it stops measuring what a "
                    + "watcher gets and starts measuring what the author intended.");

            var (busiest, quietest) = SignTest(w);
            if (busiest < quietest)
                Say(r, $"THE SIGN IS INVERTED: the busiest quarter of the village scores "
                        + $"{busiest:0.00} and the quietest {quietest:0.00}. A quiet life scores "
                        + "better today because time outdoors alone accrues tactical propositions "
                        + "while the texture vocabulary is closed. Until this flips, the ratio alone "
                        + "is not a measure of \"more life\", and the median-texture floor is doing "
                        + "the work of stopping the village being strip-mined for a better number.");

            if (medianRatio < floor.RatioMedian - 0.005)
                Say(r, $"THE RATCHET HAS BEEN WALKED BACKWARDS: median {medianRatio:0.00} against a "
                        + $"floor of {floor.RatioMedian:0.00} in Content/{FloorFile}. That file's rule "
                        + "is that ratio.median may be lowered ONLY in the same commit that RAISES "
                        + $"texture.median (now {medianTexture:0}, floor {floor.TextureMedian:0}). "
                        + "You may trade legibility for texture. You may not lose both.");

            if (tenth < RequiredAtTenth)
                Say(r, $"THE TENTH PERCENTILE IS {tenth:0.00}. One in ten villagers is a person "
                        + "about whom a fortnight taught you more about how to hurt them than about "
                        + "who they are. That is the design document's definition of a lock, and it "
                        + "is where the failure this instrument exists to catch actually lives.");
        }

        /// <summary>
        /// A finding, wrapped so that it can be read.
        ///
        /// Every one of these is meant to say WHAT TO FIX rather than that something is wrong,
        /// so they run to a paragraph, and a paragraph on one line is a paragraph nobody reads.
        /// Wrapped here rather than in Instrument, because the other three instruments say
        /// their findings in a sentence and do not need it.
        /// </summary>
        private static void Say(Instrument r, string text)
        {
            const int Width = 86;
            var sb = new StringBuilder();
            int column = 0;

            foreach (string word in text.Split(' '))
            {
                if (column > 0 && column + 1 + word.Length > Width)
                {
                    sb.Append("\n      ");
                    column = 0;
                }
                else if (column > 0) { sb.Append(' '); column++; }

                sb.Append(word);
                column += word.Length;
            }
            r.Finding(sb.ToString());
        }

        /// <summary>
        /// Mean ratio of the quarter of the village with the most observations against the
        /// quarter with the fewest. A fuller life ought to score better.
        /// </summary>
        private static (double busiest, double quietest) SignTest(Watch w)
        {
            int n = w.People;
            if (n < 4) return (0, 0);

            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;

            // Sorted by how much the watch SAW, which is a property of the log and not of the
            // person: there is no name attached to any of these indices by the time the two
            // means come out, and neither mean can be turned back into one.
            var by = (double[])w.Entries.Clone();
            Array.Sort(by, order);

            int quarter = n / 4;
            double quiet = 0, busy = 0;
            for (int k = 0; k < quarter; k++)
            {
                quiet += w.Ratios[order[k]];
                busy += w.Ratios[order[n - 1 - k]];
            }
            return (busy / quarter, quiet / quarter);
        }

        // ---------------------------------------------------------------------------------
        //  one person, chosen by hand
        // ---------------------------------------------------------------------------------

        public static string One(VillageContext ctx, int index)
        {
            var logs = Eyewitness.WatchAll(ctx, Salience.DaysWatched);
            if (index < 0 || index >= logs.Length)
                return $"There is no citizen {index}. The village has {logs.Length}.\n";

            var keys = new HashSet<int>();
            WatchTally tally = Salience.Weigh(logs[index], keys);

            var who = ctx.People.Get(new CitizenId(index));
            var sb = new StringBuilder();

            sb.Append('\n');
            sb.Append(who.FullName).Append(" — a fortnight, from the lane\n");
            sb.Append($"  in sight for {tally.MinutesInSight:#,0} of "
                    + $"{Salience.DaysWatched * 1440:#,0} minutes\n\n");

            // The texture side in full English. This is the half of the log an audit of the
            // village's aliveness needs to read, and it is the half that says nothing about
            // when or where.
            var lines = new List<string>();
            foreach (int key in keys) lines.Add(Prose(Salience.ActOf(key), Salience.MannerOf(key)));
            lines.Sort(StringComparer.Ordinal);

            sb.Append($"  THE TEXTURE OF A LIFE — {Words(tally.Texture)} distinct things\n");
            foreach (string line in lines) sb.Append("    ").Append(line).Append('\n');
            if (lines.Count == 0) sb.Append("    nothing. Not one kind of moment in a fortnight.\n");
            sb.Append('\n');

            // And the tactical side as three numbers. There is no branch below this that
            // formats a proposition, and adding one would be the whole artifact.
            //
            // Pronoun-free throughout, which is Content/particulars.txt's own authoring rule
            // and it is right for the same reason: the population has no sex field, so any
            // pronoun here would be guessed from a forename and would be wrong about somebody
            // in a village this size.
            sb.Append($"  WHAT SOMEBODY WHO MEANT THEM HARM WOULD HAVE GOT — "
                    + $"{Words(tally.Useful)} propositions\n");
            sb.Append($"    when they are alone                 {tally.Alone,5}\n");
            sb.Append($"    when the house stands empty         {tally.HouseStandsEmpty,5}\n");
            sb.Append($"    whether anybody would notice        {tally.NobodyCalls,5}\n\n");

            string verdict = tally.Ratio >= Required ? "CLEARS THE LINE" : "BELOW THE LINE";
            string scoreline = "  " + tally.Ratio.ToString("0.00", CultureInfo.InvariantCulture) + " : 1";
            sb.Append(scoreline.PadRight(64)).Append(verdict).Append('\n');
            sb.Append('\n');

            if (tally.Ratio < Required)
                sb.Append("  This is a fault in the village, not a fact about them.\n");

            sb.Append("  The instrument holds, at this moment, the hours and the day-classes behind\n");
            sb.Append("  every one of those propositions. It prints none of them, and there is no\n");
            sb.Append("  flag that will.\n\n");

            sb.Append("#M ratio_one ")
              .Append("useful=").Append(tally.Useful)
              .Append(" texture=").Append(tally.Texture)
              .Append(" entries=").Append(tally.Entries)
              .Append(" in_sight=").Append(tally.MinutesInSight)
              .Append(" ratio=").Append(tally.Ratio.ToString("0.####", CultureInfo.InvariantCulture))
              .Append('\n');

            return sb.ToString();
        }

        // ---------------------------------------------------------------------------------
        //  rendering
        // ---------------------------------------------------------------------------------

        // Both of these are indexed BY ENUM VALUE, so a verb added anywhere but the end silently
        // relabels every row below it. That is exactly what happened when CalledOnSomebody was
        // slipped in after WentIn: the table went on printing, every number in it was real, and
        // every one of them was against the wrong name.
        private const int ActCount = 12;

        private static readonly string[] ActWords =
        {
            "nothing in particular",
            "came out", "went in", "called on somebody",
            "walked past", "stood about",
            "stopped to speak", "fell in with somebody", "stood in a line",
            "a light came on", "a light went out",
            "nobody came or went",
        };

        private static readonly ObservedManner[] MannerBits =
        {
            ObservedManner.Carrying, ObservedManner.Alone, ObservedManner.InCompany,
            ObservedManner.Quickly, ObservedManner.Slowly, ObservedManner.Lingering,
            ObservedManner.SomewhereNew, ObservedManner.TheHouseIsDark,
        };

        private static readonly string[] MannerWords =
        {
            "carry", "alone", "compy", "quick", "slow", "linger", "new", "empty",
        };

        /// <summary>One texture proposition as a sentence a person would say. No time, no day,
        /// no place — there is none in the key to render.</summary>
        private static string Prose(ObservedAct act, ObservedManner manner)
        {
            var sb = new StringBuilder();
            switch (act)
            {
                case ObservedAct.CameOut: sb.Append("came out"); break;
                case ObservedAct.WentIn: sb.Append("went in"); break;
                case ObservedAct.CalledOnSomebody: sb.Append("called at a house that is not hers"); break;
                case ObservedAct.WalkedPast: sb.Append("walked past"); break;
                case ObservedAct.StoodAbout: sb.Append("stood a while"); break;
                case ObservedAct.StoppedToSpeak: sb.Append("stopped and spoke to somebody"); break;
                case ObservedAct.FellInWithSomebody: sb.Append("fell in with somebody on the way"); break;
                case ObservedAct.StoodInALine: sb.Append("stood in a line, through the glass"); break;
                case ObservedAct.ALightCameOn: sb.Append("a light came on in the house"); break;
                case ObservedAct.ALightWentOut: sb.Append("a light went out in the house"); break;
                default: sb.Append(act.ToString()); break;
            }

            if ((manner & ObservedManner.Carrying) != 0) sb.Append(", carrying something");
            if ((manner & ObservedManner.Lingering) != 0) sb.Append(", and stood a moment on the step");
            if ((manner & ObservedManner.SomewhereNew) != 0) sb.Append(", somewhere we had not seen them go");
            if ((manner & ObservedManner.Quickly) != 0) sb.Append(", quickly for them");
            if ((manner & ObservedManner.Slowly) != 0) sb.Append(", slowly");
            if ((manner & ObservedManner.InCompany) != 0) sb.Append(", with somebody");
            if ((manner & ObservedManner.Alone) != 0) sb.Append(", alone");
            if ((manner & ObservedManner.TheHouseIsDark) != 0) sb.Append(", the house behind them empty");

            return sb.ToString();
        }

        private static readonly double[] RatioEdges = { 0.50, 1.00, 1.50, 2.00, 3.00, 5.00 };

        /// <summary>Declared bucket edges, never derived ones — edges from the data would move
        /// between builds and make two runs incomparable.</summary>
        private static void Histogram(Table table, double[] values, double[] edges)
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
                string band = i == 0 ? "under " + edges[0].ToString("0.00", CultureInfo.InvariantCulture)
                            : i == edges.Length ? edges[edges.Length - 1].ToString("0.00", CultureInfo.InvariantCulture) + " +"
                            : edges[i - 1].ToString("0.00", CultureInfo.InvariantCulture) + " – "
                              + edges[i].ToString("0.00", CultureInfo.InvariantCulture);
                table.Row(band, new string('#', counts[i] * 36 / most), counts[i]);
            }
        }

        private static void Gate(Table table, string label, double measured, double needs,
                                 double floorValue, string format)
        {
            double bar = double.IsNaN(needs) ? floorValue : Math.Max(needs, floorValue);
            table.Row(label,
                      measured.ToString(format, CultureInfo.InvariantCulture),
                      double.IsNaN(needs) ? "–" : needs.ToString(format, CultureInfo.InvariantCulture),
                      floorValue.ToString(format, CultureInfo.InvariantCulture),
                      measured + 1e-9 >= bar ? "holds" : "FAIL");
        }

        private static void GatePercent(Table table, string label, double measured, double needs,
                                        double floorValue)
        {
            double bar = Math.Max(needs, floorValue);
            table.Row(label, (measured * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%",
                      (needs * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%",
                      (floorValue * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%",
                      measured + 1e-9 >= bar ? "holds" : "FAIL");
        }

        private static string Five(double[] sorted) =>
            $"min {Spread.Percentile(sorted, 0),3:0}  p10 {Spread.Percentile(sorted, 10),3:0}  "
          + $"med {Spread.Percentile(sorted, 50),3:0}  p90 {Spread.Percentile(sorted, 90),3:0}  "
          + $"max {Spread.Percentile(sorted, 100),3:0}";

        private static double[] Sorted(double[] values)
        {
            var copy = (double[])values.Clone();
            Array.Sort(copy);
            return copy;
        }

        private static readonly string[] SmallWords =
        {
            "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
            "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen",
            "seventeen", "eighteen", "nineteen", "twenty",
        };

        private static string Words(int n) =>
            n >= 0 && n < SmallWords.Length ? SmallWords[n] : n.ToString(CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------------------------
        //  the floor
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Content/watched.floor, read at run time.
        ///
        /// A one-way ratchet with exactly one trading rule, and the rule is the whole
        /// anti-mechanisation clause: ratio.median may be lowered ONLY in the same commit that
        /// RAISES texture.median. You may trade legibility for texture, and you may not lose
        /// both. Without it, the cheapest path to a better ratio is to strip the village of the
        /// errands that produce standing in a line, carrying something and going somewhere new
        /// — which improves the number while killing the thing it exists to protect.
        /// </summary>
        public sealed class Floor
        {
            public double RatioMedian = 0;
            public double RatioP10 = 0;
            public double TextureMedian = 0;
            public double TextureMin = 0;
            public double SightMedian = 0;

            public static Floor Load()
            {
                var floor = new Floor();
                string path = ContentPath.PathTo(FloorFile);
                if (!File.Exists(path)) return floor;

                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    int gap = line.IndexOf(' ');
                    if (gap < 0) continue;
                    string key = line.Substring(0, gap).Trim();
                    if (!double.TryParse(line.Substring(gap).Trim(), NumberStyles.Float,
                                         CultureInfo.InvariantCulture, out double value)) continue;

                    switch (key)
                    {
                        case "ratio.median": floor.RatioMedian = value; break;
                        case "ratio.p10": floor.RatioP10 = value; break;
                        case "texture.median": floor.TextureMedian = value; break;
                        case "texture.min": floor.TextureMin = value; break;
                        case "sight.median": floor.SightMedian = value; break;
                    }
                }
                return floor;
            }
        }
    }
}
