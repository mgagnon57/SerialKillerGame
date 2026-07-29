using System;
using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Sim;
using Noir.Core.World;

namespace Noir.Sim
{
    /// <summary>
    /// Whether the acquaintance graph is real.
    ///
    /// A settlement can pass every test this project already has — it compiles, nobody is
    /// stranded, the density curve empties at nine and fills at six — and still be a hundred
    /// people who have never met. Volume alone will not show it: a village where four people
    /// meet everybody and a hundred meet nobody has the same encounters-per-head as a healthy
    /// one, which is why nothing below is reported as a mean on its own.
    ///
    /// It matters most for the feature it is nearest to. "Stop-and-face conversation" is one of
    /// the twelve aliveness features, and its MEANING inverts with scale: in a village, two
    /// figures stopping in the lane read as two people who know each other, and on a town's high
    /// street the same rule produces strangers greeting strangers every few seconds, which reads
    /// as a bug. The rate per head does not change between those two settlements. What changes
    /// is the repeat rate and the share of the town each person has met, so those are measured
    /// here rather than inferred later.
    /// </summary>
    public static class EncounterReport
    {
        /// <summary>
        /// How close two people must pass to count as having met, in tiles.
        ///
        /// 2.4, which is Simulation's own TalkRange. It is a private constant there and a
        /// duplicate here, deliberately: Core is not going to grow a public field so that a
        /// harness can read it. The consequence is that if that constant ever moves, this one
        /// has to be moved with it — hence the flag, and hence the header printing the value it
        /// used.
        /// </summary>
        public const float DefaultRange = 2.4f;

        /// <summary>
        /// Seconds two people must be apart before passing each other counts again.
        ///
        /// Without it, two men who stand talking for half a minute produce thirty encounters,
        /// and a village whose people stand about would out-score one whose people meet. A
        /// minute is roughly the simulation's own talk cooldown, which is the same judgement
        /// made for the same reason.
        /// </summary>
        public const int DefaultRegapSeconds = 60;

        /// <summary>The population the projection at the end is aimed at: the town this project
        /// is about to become. Nothing above the projection depends on it.</summary>
        private const int Target = 600;

        private const byte MetOutdoors = 1;
        private const byte SharedARoom = 2;
        private const byte Spoke = 4;

        public static string Run(VillageContext ctx, int days, int startHour,
                                 float range, int regapSeconds)
        {
            if (days < 1) days = 1;
            if (range <= 0f) range = DefaultRange;
            if (regapSeconds < 0) regapSeconds = 0;

            var world = ctx.World;
            var grid = world.Grid;
            var sim = new Simulation(world, ctx.People, ctx.Seed, startHour * 60);

            int n = sim.AgentCount;
            long ticks = (long)days * 1440 * GameClock.TicksPerMinute;

            // ---- what is remembered about every pair ----
            //
            // One byte and two counters per ordered pair, upper triangle only. At village scale
            // that is 60 KB; at a town of six hundred it is 2 MB, which is worth spending to get
            // an exact answer rather than a sampled one. Anything much bigger than a town would
            // need this reworked, and the header says so.
            var flags = new byte[n * n];
            var lastSweep = new int[n * n];
            var lastDay = new ushort[n * n];
            for (int i = 0; i < lastSweep.Length; i++) lastSweep[i] = int.MinValue / 2;

            var householdOf = new int[n];
            var homes = new Tile[n];
            for (int i = 0; i < n; i++)
            {
                var c = ctx.People.Get(new CitizenId(i));
                householdOf[i] = c.Household.Value;
                homes[i] = AnchorOf(world.GetPlace(c.Home));
            }

            var isDwelling = new bool[world.PlaceCount];
            for (int p = 0; p < world.PlaceCount; p++)
                isDwelling[p] = world.GetPlace(new PlaceId(p)).Kind == PlaceKind.Dwelling;

            // ---- per person ----
            var streetEvents = new long[n];
            var repeatEvents = new long[n];
            var distinctDaySum = new long[n];
            var degreeStreet = new int[n];
            var degreeRoom = new int[n];
            var degreeTalk = new int[n];
            var conversations = new long[n];
            var outdoorSeconds = new long[n];
            var wasTalking = new bool[n];

            // ---- per hour of the day, summed over every day run ----
            var eventsByHour = new long[24];
            var talksByHour = new long[24];
            var outdoorSecondsByHour = new long[24];

            long totalEvents = 0, totalRepeats = 0, totalConversations = 0;
            long metPairs = 0, metHomeDistance = 0;

            // ---- the sweep's scratch ----
            int cellSize = (int)Math.Ceiling(range);
            if (cellSize < 1) cellSize = 1;
            int cols = (grid.Width + cellSize - 1) / cellSize;
            int rows = (grid.Height + cellSize - 1) / cellSize;
            var head = new int[cols * rows];
            for (int i = 0; i < head.Length; i++) head[i] = -1;
            var next = new int[n];
            var touchedCells = new List<int>();

            var roomOccupants = new List<int>[world.PlaceCount];
            var touchedRooms = new List<int>();

            var outdoors = new List<int>();
            float rangeSquared = range * range;

            for (long t = 0; t < ticks; t++)
            {
                sim.Tick();

                // Once a game-second, on exactly the ticks Simulation.LookForConversations runs.
                // Sweeping more often would count passes the simulation could not have acted on;
                // sweeping less often would miss some it did.
                if (sim.Clock.Tick % GameClock.TicksPerSecond != 0) continue;

                int sweep = (int)(sim.Clock.Tick / GameClock.TicksPerSecond);
                int day = sim.Clock.Day;
                int hour = sim.Clock.HourOfDay;
                ushort dayMark = (ushort)(day + 1);

                outdoors.Clear();

                for (int i = 0; i < n; i++)
                {
                    var a = sim.GetAgent(i);

                    // A conversation the simulation itself started. Detected as an edge rather
                    // than a state, and safe at this sampling rate because the shortest talk is
                    // twelve seconds and the cooldown after one is ninety.
                    bool talking = a.TalkTicks > 0;
                    if (talking && !wasTalking[i])
                    {
                        conversations[i]++;
                        var partner = a.TalkingTo;
                        if (partner.IsValid)
                        {
                            // Once per conversation, not once per participant: both sides are
                            // begun on the same tick, so the higher-numbered one is arbitrary
                            // and stable.
                            if (partner.Value > i) { totalConversations++; talksByHour[hour]++; }
                            Mark(flags, n, i, partner.Value, Spoke, degreeTalk);
                        }
                    }
                    wasTalking[i] = talking;

                    var tile = a.Position.ToTile();
                    if (!grid.InBounds(tile)) continue;

                    if ((grid.FlagsAt(tile) & TileFlags.Indoor) == 0)
                    {
                        outdoorSeconds[i]++;
                        outdoorSecondsByHour[hour]++;
                        outdoors.Add(i);

                        int cell = (tile.Y / cellSize) * cols + (tile.X / cellSize);
                        if (head[cell] < 0) touchedCells.Add(cell);
                        next[i] = head[cell];
                        head[cell] = i;
                    }
                    else if (a.At.IsValid && !isDwelling[a.At.Value])
                    {
                        // Indoors somewhere that is not their own home: the mill floor, a
                        // classroom, the bar. Different from passing in the street and worth
                        // keeping apart from it — a village whose street graph is thin but whose
                        // workplace graph is thick is not the same failure as one with neither.
                        int p = a.At.Value;
                        if (roomOccupants[p] == null) roomOccupants[p] = new List<int>();
                        if (roomOccupants[p].Count == 0) touchedRooms.Add(p);
                        roomOccupants[p].Add(i);
                    }
                }

                // ---- who passed whom, outdoors ----
                for (int k = 0; k < outdoors.Count; k++)
                {
                    int i = outdoors[k];
                    var a = sim.GetAgent(i);
                    var tile = a.Position.ToTile();
                    int cx = tile.X / cellSize, cy = tile.Y / cellSize;

                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;

                        for (int j = head[ny * cols + nx]; j >= 0; j = next[j])
                        {
                            if (j <= i) continue;
                            if (householdOf[j] == householdOf[i]) continue;   // not an acquaintance

                            var delta = sim.GetAgent(j).Position - a.Position;
                            if (delta.LengthSquared > rangeSquared) continue;

                            int pair = i * n + j;
                            int since = sweep - lastSweep[pair];
                            lastSweep[pair] = sweep;
                            if (since <= regapSeconds) continue;   // still the same pass

                            totalEvents++;
                            eventsByHour[hour]++;
                            streetEvents[i]++;
                            streetEvents[j]++;

                            if ((flags[pair] & MetOutdoors) != 0)
                            {
                                totalRepeats++;
                                repeatEvents[i]++;
                                repeatEvents[j]++;
                            }
                            else
                            {
                                flags[pair] |= MetOutdoors;
                                degreeStreet[i]++;
                                degreeStreet[j]++;
                                metPairs++;
                                metHomeDistance += Distance(homes[i], homes[j]);
                            }

                            if (lastDay[pair] != dayMark)
                            {
                                lastDay[pair] = dayMark;
                                distinctDaySum[i]++;
                                distinctDaySum[j]++;
                            }
                        }
                    }
                }

                foreach (int cell in touchedCells) head[cell] = -1;
                touchedCells.Clear();

                // ---- who was in the same building ----
                foreach (int p in touchedRooms)
                {
                    var list = roomOccupants[p];
                    for (int x = 0; x < list.Count; x++)
                    for (int y = x + 1; y < list.Count; y++)
                    {
                        if (householdOf[list[x]] == householdOf[list[y]]) continue;
                        Mark(flags, n, list[x], list[y], SharedARoom, degreeRoom);
                    }
                    list.Clear();
                }
                touchedRooms.Clear();
            }

            return Describe(ctx, sim, days, startHour, range, regapSeconds, ticks,
                            flags, streetEvents, repeatEvents, distinctDaySum, degreeStreet,
                            degreeRoom, degreeTalk, conversations, outdoorSeconds,
                            eventsByHour, talksByHour, outdoorSecondsByHour,
                            totalEvents, totalRepeats, totalConversations,
                            metPairs, metHomeDistance, homes, householdOf);
        }

        private static void Mark(byte[] flags, int n, int a, int b, byte bit, int[] degree)
        {
            int i = a < b ? a : b;
            int j = a < b ? b : a;
            if (i == j) return;

            int pair = i * n + j;
            if ((flags[pair] & bit) != 0) return;
            flags[pair] |= bit;
            degree[i]++;
            degree[j]++;
        }

        // ------------------------------------------------------------------
        //  the report
        // ------------------------------------------------------------------

        private static string Describe(VillageContext ctx, Simulation sim, int days, int startHour,
                                       float range, int regapSeconds, long ticks,
                                       byte[] flags, long[] streetEvents, long[] repeatEvents,
                                       long[] distinctDaySum, int[] degreeStreet, int[] degreeRoom,
                                       int[] degreeTalk, long[] conversations, long[] outdoorSeconds,
                                       long[] eventsByHour, long[] talksByHour,
                                       long[] outdoorSecondsByHour,
                                       long totalEvents, long totalRepeats, long totalConversations,
                                       long metPairs, long metHomeDistance,
                                       Tile[] homes, int[] householdOf)
        {
            int n = sim.AgentCount;
            var r = new Instrument();

            r.Line($"encounters — {ctx.World.Name}, {n} people, {ctx.World.Width}x{ctx.World.Height}, "
                 + $"{days} day(s) from {startHour:00}:00, seed {ctx.Seed}");
            r.Line($"             COST — {ticks:#,0} ticks simulated and "
                 + $"{ticks / GameClock.TicksPerSecond:#,0} proximity sweeps.");
            r.Line("             The sweeps are bucketed by tile and cost almost nothing; the ticks are");
            r.Line("             the whole bill, and `--days 1` divides it by seven. This is the only");
            r.Line("             one of the three instruments that runs the simulation at all.");
            r.Line();
            r.Line("             Proximity is swept once a game-second, on exactly the ticks the");
            r.Line("             simulation itself looks for a conversation — so this sees the passes it");
            r.Line("             could have acted on, and no others.");
            r.Line();
            r.Line("  WHAT COUNTS AS AN ENCOUNTER");
            r.Line($"    street        both outdoors and within {range:0.0} tiles. One pass counts once: they");
            r.Line($"                  must be more than {regapSeconds}s apart before it counts again.");
            r.Line("    building      both indoors in the same building, excluding their own homes.");
            r.Line("                  Never counted as a street encounter; the two are disjoint.");
            r.Line("                  A building, not a room: two people at opposite ends of the mill");
            r.Line("                  floor count, which flatters this column and is said so here");
            r.Line("                  rather than quietly.");
            r.Line("    conversation  the simulation actually stopped them and turned them to face");
            r.Line("                  each other. A strict subset of street encounters.");
            r.Line("    Anyone sharing a roof is excluded throughout. Knowing your own household is");
            r.Line("    not an acquaintance, and counting it would flatter every figure below by one.");
            r.Line();

            // ---- volume ----
            var encountersPerDay = Per(streetEvents, days);
            var distinctPerDay = Per(distinctDaySum, days);
            var distinctOverRun = ToDouble(degreeStreet);
            var roomOverRun = ToDouble(degreeRoom);
            var talkedOverRun = ToDouble(degreeTalk);
            var talkPerDay = Per(conversations, days);
            var outdoorMinutesPerDay = new double[n];
            for (int i = 0; i < n; i++) outdoorMinutesPerDay[i] = outdoorSeconds[i] / 60.0 / days;

            var anyDegree = new double[n];
            var repeatShare = new double[n];
            for (int i = 0; i < n; i++)
            {
                int any = 0;
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    if ((flags[i < j ? i * n + j : j * n + i] & (MetOutdoors | SharedARoom)) != 0) any++;
                }
                anyDegree[i] = any;
                repeatShare[i] = streetEvents[i] > 0 ? 100.0 * repeatEvents[i] / streetEvents[i] : 0;
            }

            r.Heading("PER CITIZEN — the distribution, because the mean hides the whole question");

            var spread = new Table("", Spread.Columns);
            Spread.Describe(spread, "street encounters/day", encountersPerDay);
            Spread.Describe(spread, "conversations/day", talkPerDay);
            Spread.Describe(spread, "minutes outdoors/day", outdoorMinutesPerDay);
            Spread.Describe(spread, "repeat rate, %", repeatShare);
            spread.Gap();
            Spread.Describe(spread, "distinct met, in a day", distinctPerDay);
            Spread.Describe(spread, "distinct met, whole run", distinctOverRun);
            Spread.Describe(spread, "distinct spoken to", talkedOverRun);
            Spread.Describe(spread, "shared a building with", roomOverRun);
            Spread.Describe(spread, "either, whole run", anyDegree);
            spread.Note($"'zero' is how many of the {n} got none at all — the column that says whether");
            spread.Note("anybody has been left out of the village entirely.");
            spread.Note("'in a day' is a mean over the days run: a Sunday is not a Tuesday, and one");
            spread.Note("day's figure on its own would be whichever day it happened to be.");
            r.Add(spread);

            Spread.Machine(r, "enc_spread", "encounters_per_day", encountersPerDay);
            Spread.Machine(r, "enc_spread", "conversations_per_day", talkPerDay);
            Spread.Machine(r, "enc_spread", "outdoor_minutes_per_day", outdoorMinutesPerDay);
            Spread.Machine(r, "enc_spread", "repeat_pct", repeatShare);
            Spread.Machine(r, "enc_spread", "distinct_one_day", distinctPerDay);
            Spread.Machine(r, "enc_spread", "distinct_run", distinctOverRun);
            Spread.Machine(r, "enc_spread", "spoken_to_run", talkedOverRun);
            Spread.Machine(r, "enc_spread", "room_run", roomOverRun);
            Spread.Machine(r, "enc_spread", "either_run", anyDegree);

            // ---- repeats ----
            double repeatRate = totalEvents > 0 ? 100.0 * totalRepeats / totalEvents : 0;

            r.Heading("REPEATS — is it the same faces, or a crowd");
            r.Line($"    street encounters        {totalEvents,10:#,0}   over {days} day(s)");
            r.Line($"    with somebody new        {totalEvents - totalRepeats,10:#,0}   "
                 + $"({(totalEvents > 0 ? 100.0 - repeatRate : 0):0.0}%)");
            r.Line($"    with somebody already met{totalRepeats,10:#,0}   ({repeatRate:0.0}%)");
            r.Line();
            r.Line("    A low repeat rate is not a good number and not a bad one on its own. In a");
            r.Line("    village it means the graph has not closed yet and more days would raise it;");
            r.Line("    in a town it means the street is full of strangers, and a rule that stops");
            r.Line("    two of them to talk is producing the wrong thing however good the mean is.");
            r.Line();
            r.M("enc_repeat", ("events", totalEvents), ("repeats", totalRepeats),
                ("repeat_pct", repeatRate), ("days", days));

            // ---- reach ----
            var sortedReach = (double[])distinctOverRun.Clone();
            Array.Sort(sortedReach);
            double medianReach = Spread.Percentile(sortedReach, 50);
            double medianReachPct = n > 1 ? 100.0 * medianReach / (n - 1) : 0;

            var sortedAny = (double[])anyDegree.Clone();
            Array.Sort(sortedAny);
            double medianAnyPct = n > 1 ? 100.0 * Spread.Percentile(sortedAny, 50) / (n - 1) : 0;

            r.Heading($"REACH — how much of the village one person has met in {days} day(s)");

            var reach = new Table("", new Col("distinct met", 14, right: false),
                                      new Col("people", 8), new Col("", 36, right: false));
            Spread.Histogram(reach, distinctOverRun, new[] { 1, 5, 10, 20, 30, 45, 60, 80 });
            reach.Note($"Median citizen has met {medianReach:0} of the other {n - 1} on the street "
                     + $"({medianReachPct:0.0}%),");
            reach.Note($"and {medianAnyPct:0.0}% counting people they only ever shared a building with.");
            r.Add(reach);

            r.M("enc_reach", ("median_street", medianReach), ("median_street_pct", medianReachPct),
                ("median_any_pct", medianAnyPct), ("population", n), ("days", days));

            // ---- the shape of the graph ----
            int components = Components(flags, n, (byte)(MetOutdoors | SharedARoom));
            int streetComponents = Components(flags, n, MetOutdoors);
            double overlap = Overlap(flags, n, MetOutdoors);

            long allPairs = 0, allDistance = 0;
            for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                if (householdOf[i] == householdOf[j]) continue;
                allPairs++;
                allDistance += Distance(homes[i], homes[j]);
            }

            double metMean = metPairs > 0 ? metHomeDistance / (double)metPairs : 0;
            double allMean = allPairs > 0 ? allDistance / (double)allPairs : 0;
            double mixing = allMean > 0 ? metMean / allMean : 0;

            r.Heading("SHAPE — is this one village, or several that share a map");
            r.Line($"    islands, street graph    {streetComponents,10}   groups with nobody in common");
            r.Line($"    islands, street or room  {components,10}");
            r.Line($"    shared acquaintances     {overlap * 100,10:0.0} %  of two people who have met, the");
            r.Line("                                          share of their acquaintances in common");
            r.Line();
            r.Line($"    mean home-to-home, pairs who met   {metMean,8:0} m");
            r.Line($"    mean home-to-home, all pairs       {allMean,8:0} m");
            r.Line($"    mixing ratio                       {mixing,8:0.00}   1.00 = you are as likely to");
            r.Line("                                                  meet the far end of the village as");
            r.Line("                                                  your own street");
            r.Line();
            r.M("enc_shape", ("islands_street", streetComponents), ("islands_any", components),
                ("overlap_pct", overlap * 100), ("met_home_m", metMean), ("all_home_m", allMean),
                ("mixing", mixing));

            // ---- the street, hour by hour ----
            r.Heading("THE STREET, HOUR BY HOUR — where the aliveness rule actually fires");

            var byHour = new Table("", new Col("hour", 5, right: false), new Col("outdoors", 9),
                                       new Col("passes", 8), new Col("talks", 7),
                                       new Col("one pass per", 13), new Col("", 26, right: false));
            long secondsPerHour = 3600L * days;
            long peak = 1;
            foreach (long e in eventsByHour) if (e > peak) peak = e;

            for (int h = 0; h < 24; h++)
            {
                double meanOutdoors = outdoorSecondsByHour[h] / (double)secondsPerHour;
                double perPass = eventsByHour[h] > 0
                    ? outdoorSecondsByHour[h] / (2.0 * eventsByHour[h]) : 0;
                byHour.Row($"{h:00}:00", meanOutdoors, eventsByHour[h], talksByHour[h],
                           eventsByHour[h] > 0 ? $"{perPass:#,0} s out" : "—",
                           new string('#', (int)(eventsByHour[h] * 26 / peak)));
            }
            byHour.Note("'outdoors' is the mean number of people on the street during that hour.");
            byHour.Note("'one pass per N s out' is how long one person spends outdoors between passing");
            byHour.Note("somebody. THIS is the number that inverts at town scale: it is a property of");
            byHour.Note("street density, not of population, and it decides whether stopping two people");
            byHour.Note("to talk reads as a village or as a fault.");
            r.Add(byHour);

            for (int h = 0; h < 24; h++)
                r.M("enc_hour", ("hour", h),
                    ("outdoors", outdoorSecondsByHour[h] / (double)secondsPerHour),
                    ("passes", eventsByHour[h]), ("talks", talksByHour[h]),
                    ("s_out_per_pass", eventsByHour[h] > 0
                        ? outdoorSecondsByHour[h] / (2.0 * eventsByHour[h]) : 0));

            // ---- what this says about a town ----
            //
            // Everything above is measured. This is not, and the split is kept visible: a
            // projection that reads like a measurement is worse than no projection, because
            // somebody will plan against it.
            int busiest = 0;
            for (int h = 1; h < 24; h++)
                if (eventsByHour[h] > eventsByHour[busiest]) busiest = h;

            double busiestRate = eventsByHour[busiest] > 0
                ? outdoorSecondsByHour[busiest] / (2.0 * eventsByHour[busiest]) : 0;
            double tilesPerHead = n > 0 ? ctx.World.Width * (double)ctx.World.Height / n : 0;
            double ratio = n > 0 ? Target / (double)n : 1;
            double passesPerHead = n > 0 ? totalEvents * 2.0 / n / days : 0;

            r.Heading($"AT {Target} — DERIVED, NOT MEASURED");
            r.Line("    Two of these are properties of street DENSITY and do not move when the");
            r.Line("    population does; one is a share of the population and moves inversely with it.");
            r.Line("    That is the whole prediction, and it is arithmetic on the rows above rather");
            r.Line("    than a second measurement.");
            r.Line();
            r.Line($"      tiles per head, here             {tilesPerHead,8:#,0}    hold this and the first two stand");
            r.Line($"      passes per head per day          {passesPerHead,8:0.0}    unchanged");
            r.Line($"      distinct met in {days} days, median   {medianReach,8:0}    unchanged");
            r.Line($"      as a share of the settlement     {medianReachPct,7:0.0}% ->{100.0 * medianReach / (Target - 1),6:0.0}%");
            r.Line($"      busiest hour ({busiest:00}:00), one pass per {busiestRate,7:#,0} s outdoors, unchanged at this");
            r.Line($"                                                 density — and {busiestRate / ratio:#,0} s if the town's");
            r.Line("                                                 people go on one high street instead");
            r.Line();
            r.Line("    Falsify it rather than believe it. `encounters --tile 2` runs this same content");
            r.Line("    on four Ashcombes and about four times the people at the same density, which is");
            r.Line("    the only preview of the town this harness can build before one is authored.");
            r.Line();
            r.M("enc_projection", ("from", n), ("to", Target), ("tiles_per_head", tilesPerHead),
                ("passes_per_head", passesPerHead), ("median_reach", medianReach),
                ("share_now_pct", medianReachPct),
                ("share_then_pct", 100.0 * medianReach / (Target - 1)),
                ("busiest_hour", busiest), ("s_out_per_pass", busiestRate));

            // ---- named extremes ----
            r.Heading("THE EXTREMES — the people the mean is made of");
            r.Line(Named(ctx, "met most", Argmax(distinctOverRun), distinctOverRun, $"of {n - 1}"));
            r.Line(Named(ctx, "met fewest", Argmin(distinctOverRun), distinctOverRun, $"of {n - 1}"));
            r.Line(Named(ctx, "out longest", Argmax(outdoorMinutesPerDay), outdoorMinutesPerDay,
                         "minutes outdoors a day"));
            r.Line(Named(ctx, "out least", Argmin(outdoorMinutesPerDay), outdoorMinutesPerDay,
                         "minutes outdoors a day"));
            r.Line();

            // ---- findings ----
            double talkShare = totalEvents > 0 ? 100.0 * totalConversations / totalEvents : 0;

            r.Finding($"{passesPerHead:0.0} street encounters per citizen per day, and "
                    + $"{Spread.Mean(talkPerDay):0.00} of them become a conversation "
                    + $"({talkShare:0.0}% of passes).");

            if (Spread.Zeroes(distinctOverRun) > 0)
                r.Finding($"{Spread.Zeroes(distinctOverRun)} of {n} people met nobody at all on the "
                        + $"street in {days} days. Check who they are before reading anything else here.");
            else
                r.Finding($"Everybody met somebody: no citizen is outside the graph.");

            if (streetComponents > 1)
                r.Finding($"The street graph is in {streetComponents} pieces — there are groups here with "
                        + "no acquaintance in common at all.");

            if (mixing > 0.85)
                r.Finding($"Mixing ratio {mixing:0.00}: encounters are almost independent of where people "
                        + "live, so the graph has NOT fragmented by street. The long commute is paying "
                        + "for that; watch this number when the town gets a second employer.");
            else if (mixing < 0.6)
                r.Finding($"Mixing ratio {mixing:0.00}: people overwhelmingly meet their neighbours. This "
                        + "is the 'everybody knows their own street' failure, and it is here now.");

            r.Finding($"Median citizen has met {medianReachPct:0}% of the village in {days} days. Reach is "
                    + "the figure that falls with population if the encounter rate per head holds, and "
                    + "it is the one to watch at 600.");

            return r.ToString();
        }

        private static double[] Per(long[] totals, int days)
        {
            var result = new double[totals.Length];
            for (int i = 0; i < totals.Length; i++) result[i] = totals[i] / (double)days;
            return result;
        }

        private static double[] ToDouble(int[] values)
        {
            var result = new double[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = values[i];
            return result;
        }

        private static int Argmax(double[] values)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++) if (values[i] > values[best]) best = i;
            return best;
        }

        private static int Argmin(double[] values)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++) if (values[i] < values[best]) best = i;
            return best;
        }

        private static string Named(VillageContext ctx, string label, int index,
                                    double[] values, string unit)
        {
            var who = ctx.People.Get(new CitizenId(index));
            var home = ctx.World.GetPlace(who.Home);
            string of = who.FullName + ", of " + (home == null ? "nowhere" : home.Name);
            return $"    {label,-12} {of,-44} {values[index],7:0.#} {unit}";
        }

        /// <summary>How many groups of people have nobody in common. One is the answer a
        /// settlement wants; anything else is two villages sharing a map.</summary>
        private static int Components(byte[] flags, int n, byte mask)
        {
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if ((flags[i * n + j] & mask) != 0) Union(parent, i, j);

            int roots = 0;
            for (int i = 0; i < n; i++) if (Find(parent, i) == i) roots++;
            return roots;
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a), rb = Find(parent, b);
            if (ra != rb) parent[ra] = rb;
        }

        /// <summary>
        /// Mean Jaccard overlap of the acquaintance sets of two people who have met.
        ///
        /// This is the cliquiness measure. Near 1 means everyone you know knows everyone else
        /// you know, which is exactly what "everybody knows their own street and nobody else"
        /// looks like from inside. Near 0 means the graph is a mesh.
        /// </summary>
        private static double Overlap(byte[] flags, int n, byte mask)
        {
            int words = (n + 63) / 64;
            var bits = new ulong[n * words];
            for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                if ((flags[i * n + j] & mask) == 0) continue;
                bits[i * words + (j >> 6)] |= 1UL << (j & 63);
                bits[j * words + (i >> 6)] |= 1UL << (i & 63);
            }

            double total = 0;
            long pairs = 0;
            for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                if ((flags[i * n + j] & mask) == 0) continue;

                int shared = 0, union = 0;
                for (int w = 0; w < words; w++)
                {
                    ulong a = bits[i * words + w], b = bits[j * words + w];
                    shared += PopCount(a & b);
                    union += PopCount(a | b);
                }
                if (union > 0) total += shared / (double)union;
                pairs++;
            }
            return pairs > 0 ? total / pairs : 0;
        }

        private static int PopCount(ulong v)
        {
            int n = 0;
            while (v != 0) { v &= v - 1; n++; }
            return n;
        }

        private static Tile AnchorOf(Place place)
        {
            if (place == null) return Tile.None;
            return place.Door.IsValid ? place.Door : place.Bounds.Centre;
        }

        /// <summary>Straight-line tile distance, in integers, so two runs cannot disagree about a
        /// rounding. One tile is one metre — see the header of village.txt.</summary>
        private static int Distance(Tile a, Tile b)
        {
            if (!a.IsValid || !b.IsValid) return 0;
            return IntSqrt(Tile.DistanceSquared(a, b));
        }

        private static int IntSqrt(int value)
        {
            if (value <= 0) return 0;
            int x = value;
            int next = (x + 1) / 2;
            while (next < x) { x = next; next = (x + value / x) / 2; }
            return x;
        }
    }
}
