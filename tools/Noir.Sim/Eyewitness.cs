using System;
using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.Observation;
using Noir.Core.Sim;
using Noir.Core.World;

namespace Noir.Sim
{
    /// <summary>
    /// Somebody on the wall opposite, for a fortnight, with a notebook.
    ///
    /// This is the half of the 2:1 instrument that is allowed to read the simulation, and it
    /// lives in tools/ rather than in Assets/ for that reason: Unity never imports this folder,
    /// so no inspector, no case board and no future UI can instantiate a watcher. That is the
    /// backstop rather than the argument — "it's only a dev tool" is precisely how such a UI
    /// gets built and then demoed — but it is worth having.
    ///
    /// ---- THE EYE'S DISCIPLINE, AND IT IS A RULE RATHER THAN A STYLE ---------------------------
    ///
    /// This class may read where a figure is, whether the tile it stands on is under a roof,
    /// what a building is FOR only in as much as you can see into it, and which front door is
    /// the subject's — a watcher of a fortnight knows that much.
    ///
    /// It may not read how sociable, punctual or quick somebody is, what their trade is, how old
    /// they are, who they live with, or one word of their plan for the day. Those are things the
    /// author put INTO a person; this instrument exists to measure what a fortnight got OUT of
    /// one, and reading them would be the village grading its own homework.
    /// TwoToOneTests.TheEyeReadsOnlyWhatAWatcherCouldSee greps this file for them. That grep is
    /// crude; the real guarantee is downstream, because Salience structurally cannot see any of
    /// them, so the worst an over-reaching eye could do is emit a manner it should not have known.
    ///
    /// Emission is on a change in something PERCEPTIBLE, and never on a change in
    /// AgentState.Doing. Doing is the simulation's word for what a man is really doing, and a
    /// watcher across the road has no access to it.
    /// </summary>
    public static class Eyewitness
    {
        /// <summary>
        /// How far away another figure can be and still count as being with the subject.
        ///
        /// Treat this as part of the instrument's pinned geometry even though it lives in the
        /// eye rather than in Salience: widening it makes everybody lonelier and the ratio
        /// worse, narrowing it flatters the village. Ten metres with a clear line is roughly
        /// "near enough that a passer-by would say they were together".
        /// </summary>
        public const int SightRange = 10;

        /// <summary>
        /// How coarse "somewhere" is, in tiles. Eight is about a frontage and its gate.
        ///
        /// This is the ONLY spatial quantity that survives the watcher, it survives as a single
        /// bit, and it is thrown away with this class. Nothing downstream can name a cell.
        /// </summary>
        public const int CoarseCell = 8;

        /// <summary>Lamps are on between these; outside them a window tells you nothing.</summary>
        private const int Dusk = 19 * 60;
        private const int Dawn = 7 * 60;

        /// <summary>
        /// Samples of the subject's own walking before Quickly/Slowly means anything.
        ///
        /// "Quick for her" is the only honest form of the judgement — a watcher does not know
        /// anybody's declared walking speed, only that this evening she is going faster than she
        /// usually does. Four is enough for "usually" to be a word.
        /// </summary>
        private const int PaceSamplesNeeded = 4;

        /// <summary>
        /// One fortnight, every villager, one simulation pass.
        ///
        /// A hundred and eighteen watchers cost the same as one, because the expensive part is
        /// the village running and not the looking: Simulation.LookForConversations already
        /// sweeps every pair once per simulated SECOND, and this samples once per simulated
        /// MINUTE. The eye really is free.
        /// </summary>
        public static ObservationLog[] WatchAll(VillageContext ctx, int days)
        {
            if (days < 1) days = 1;

            var world = ctx.World;
            var grid = world.Grid;
            var sim = new Simulation(world, ctx.People, ctx.Seed, 0);

            int n = sim.AgentCount;
            int places = world.PlaceCount;
            int minutes = days * 1440;

            // ---- what the watcher already knows before the first morning ----
            var home = new PlaceId[n];
            for (int i = 0; i < n; i++) home[i] = ctx.People.Get(new CitizenId(i)).Home;

            var isDwelling = new bool[places];
            var seeInside = new bool[places];
            for (int p = 0; p < places; p++)
            {
                var place = world.GetPlace(new PlaceId(p));
                if (place == null) continue;
                isDwelling[p] = place.Kind == PlaceKind.Dwelling;

                // You can see a queue in a lit shop from the street, and you can see the bar of
                // a pub through its window. Their house, the mill floor and a barn you cannot,
                // and the watcher records only what those buildings do: the door, and the light.
                seeInside[p] = place.HasACounter || place.Kind == PlaceKind.Tavern;
            }

            var logs = new ObservationLog[n];
            for (int i = 0; i < n; i++) logs[i] = new ObservationLog();

            // ---- scratch that lives for one minute ----
            var pos = new Tile[n];
            var inSight = new bool[n];
            var others = new int[n];
            var occupants = new int[places];
            var wasOccupied = new bool[places];
            var callerToday = new bool[places];

            // ---- what each watch remembers between minutes ----
            var wasInSight = new bool[n];
            var lastPos = new Tile[n];
            var lastCell = new int[n];
            var lastManner = new ObservedManner[n];
            var lastOthers = new int[n];

            // Where she was standing this minute, carried out of the visibility pass so the acts
            // pass can tell going home from calling on somebody. It holds a PlaceId and never
            // leaves this file: nothing identifying reaches the log, only which VERB was written.
            var lastPlace = new PlaceId[n];
            var lastClarity = new SightingClarity[n];
            var seenCells = new HashSet<int>[n];
            var stillRun = new int[n];
            var wasTalking = new bool[n];
            var wasWalkingWith = new bool[n];
            var wasQueueing = new bool[n];

            // A histogram rather than a list, because the median has to be exact and cheap and
            // a sorted insert per person per minute is neither. Tiles per minute is a small
            // integer; anything past the top bucket is already "as fast as this person goes".
            const int PaceBuckets = 64;
            var paceCounts = new int[n * PaceBuckets];
            var paceSamples = new int[n];

            for (int i = 0; i < n; i++)
            {
                lastCell[i] = -1;
                seenCells[i] = new HashSet<int>();
                lastClarity[i] = SightingClarity.Glimpsed;
            }

            // ---- the proximity sweep's buckets ----
            int cols = (grid.Width + SightRange - 1) / SightRange;
            int rows = (grid.Height + SightRange - 1) / SightRange;
            var head = new int[cols * rows];
            var next = new int[n];
            var touched = new List<int>();

            for (int m = 0; m < minutes; m++)
            {
                int minuteOfDay = m % 1440;
                if (minuteOfDay == 0) Array.Clear(callerToday, 0, callerToday.Length);

                // ---- pass A: where everybody is, and which houses have somebody in them ----
                Array.Clear(occupants, 0, occupants.Length);

                for (int i = 0; i < n; i++)
                {
                    var a = sim.GetAgent(i);
                    pos[i] = a.Position.ToTile();

                    bool onTheMap = grid.InBounds(pos[i]);
                    bool outdoors = onTheMap && (grid.FlagsAt(pos[i]) & TileFlags.Indoor) == 0;

                    // The tile they are STANDING on, not the place the simulation has filed them
                    // in. AgentState.At is None for the whole of a journey, including the part
                    // of it spent walking across a shop floor towards the door — believing it
                    // would have the watcher lose sight of a woman who is still in the window.
                    var standingIn = onTheMap ? grid.PlaceAt(pos[i]) : PlaceId.None;
                    lastPlace[i] = standingIn;

                    inSight[i] = outdoors ||
                                 (standingIn.IsValid && seeInside[standingIn.Value]);

                    if (standingIn.IsValid && isDwelling[standingIn.Value])
                    {
                        occupants[standingIn.Value]++;
                        if (standingIn != home[i]) callerToday[standingIn.Value] = true;
                    }
                }

                // ---- who is within sight of whom ----
                CountCompany(grid, pos, inSight, others, head, next, touched, cols, rows, n);

                // ---- pass B: one watch each ----
                for (int i = 0; i < n; i++)
                {
                    var a = sim.GetAgent(i);
                    var log = logs[i];
                    bool sight = inSight[i];
                    log.AMinutePassed(sight);

                    int cell = (pos[i].Y / CoarseCell) * 100_000 + pos[i].X / CoarseCell;
                    var clarity = sight ? ClarityAt(pos[i], grid, minuteOfDay)
                                        : SightingClarity.Glimpsed;
                    // AgentState.Heading is the sim's own answer to "is this person walking",
                    // and it is the only one — Travelling stays true through a conversation and
                    // through a hand on a door handle. A watcher's eye reads exactly this.
                    bool onTheMove = a.Heading.LengthSquared > 0f;

                    // ---- the circumstances, as they read from across the road ----
                    var manner = ObservedManner.None;
                    if (sight)
                    {
                        manner |= others[i] == 0 ? ObservedManner.Alone : ObservedManner.InCompany;

                        if (a.Carrying) manner |= ObservedManner.Carrying;
                        if (a.DoorPauseTicks > 0) manner |= ObservedManner.Lingering;

                        if (wasInSight[i])
                        {
                            int moved = IntSqrt(Tile.DistanceSquared(lastPos[i], pos[i]));
                            if (moved > 0)
                            {
                                // Against her own usual, never against anything the author
                                // wrote down about how fast she walks.
                                if (paceSamples[i] >= PaceSamplesNeeded)
                                {
                                    int usual = MedianPace(paceCounts, i, paceSamples[i], PaceBuckets);
                                    if (moved > usual) manner |= ObservedManner.Quickly;
                                    else if (moved < usual) manner |= ObservedManner.Slowly;
                                }

                                paceCounts[i * PaceBuckets + (moved < PaceBuckets ? moved : PaceBuckets - 1)]++;
                                paceSamples[i]++;
                            }
                        }

                        if (seenCells[i].Add(cell)) manner |= ObservedManner.SomewhereNew;

                        // Both halves required. Being able to see her SOMEWHERE ELSE is what
                        // turns "nobody is at home" from an inference into an observation.
                        if (home[i].IsValid && occupants[home[i].Value] == 0)
                            manner |= ObservedManner.TheHouseIsDark;
                    }

                    // Nothing is an edge on the first minute of a watch. Somebody who has just
                    // arrived opposite a house has not seen anybody come out of it.
                    if (m > 0)
                    {
                        // ---- acts, in enum order, because which one confirms first decides
                        //      which column both of them land in ----

                        if (sight && !wasInSight[i])
                            Write(log, m, ObservedAct.CameOut, manner, others[i], 1, clarity);

                        // Stamped with the last minute she was visible, not with this one: what
                        // the watcher writes down is the woman who walked up the path with a bag
                        // and shut the door, and by this minute there is nothing to see.
                        if (!sight && wasInSight[i])
                            Write(log, m,
                                  WentSomewhereNotHers(lastPlace[i], home[i], isDwelling)
                                      ? ObservedAct.CalledOnSomebody
                                      : ObservedAct.WentIn,
                                  lastManner[i], lastOthers[i], 1, lastClarity[i]);

                        if (sight && onTheMove && lastCell[i] >= 0 && cell != lastCell[i])
                            Write(log, m, ObservedAct.WalkedPast, manner, others[i], 1, clarity);

                        // Two minutes is where standing about starts being worth a line. It is
                        // written at the two-minute mark and never revised, so ForMinutes is the
                        // run SO FAR — a watcher does not know how long the man is going to go
                        // on standing there, and an entry that did would be one written by
                        // somebody who had read his day.
                        //
                        // A stranded villager lands here too, because Simulation.Strand stands
                        // them still. A watcher cannot tell a stranded man from a man waiting;
                        // that is honest, and it is free.
                        if (sight && !onTheMove)
                        {
                            stillRun[i]++;
                            if (stillRun[i] == 2)
                                Write(log, m, ObservedAct.StoodAbout, manner, others[i], 2, clarity);
                        }
                        else stillRun[i] = 0;

                        if (sight && a.TalkingTo.IsValid && !wasTalking[i])
                            Write(log, m, ObservedAct.StoppedToSpeak, manner, others[i], 1, clarity);

                        if (sight && a.WalkingWith.IsValid && !wasWalkingWith[i])
                            Write(log, m, ObservedAct.FellInWithSomebody, manner, others[i], 1, clarity);

                        bool queueing = a.QueueSlot >= 0;
                        if (sight && queueing && !wasQueueing[i])
                            Write(log, m, ObservedAct.StoodInALine, manner, others[i], 1, clarity);

                        // ---- the window ----
                        //
                        // A house is the one thing a watcher can read without seeing anybody,
                        // and lamp hours are the only hours in which reading it means anything.
                        if (home[i].IsValid && (minuteOfDay >= Dusk || minuteOfDay < Dawn))
                        {
                            bool lit = occupants[home[i].Value] > 0;
                            if (lit && !wasOccupied[home[i].Value])
                                Write(log, m, ObservedAct.ALightCameOn, manner, others[i], 1, clarity);
                            else if (!lit && wasOccupied[home[i].Value])
                                Write(log, m, ObservedAct.ALightWentOut, manner, others[i], 1, clarity);
                        }

                        // ---- and the day's verdict on the doorstep ----
                        if (minuteOfDay == 1439 && home[i].IsValid && !callerToday[home[i].Value])
                            Write(log, m, ObservedAct.NobodyCameOrWent, ObservedManner.None, 0,
                                  1440, SightingClarity.Clear);
                    }
                    else if (sight && !onTheMove) stillRun[i] = 1;

                    // ---- remember, for the edges ----
                    wasInSight[i] = sight;
                    lastPos[i] = pos[i];
                    lastCell[i] = sight ? cell : -1;

                    if (sight)
                    {
                        lastManner[i] = manner;
                        lastOthers[i] = others[i];
                        lastClarity[i] = clarity;

                        // Only updated while she is visible, so a pair who set off together
                        // from indoors are seen to fall in with each other the moment they come
                        // out — which is when the watcher would have written it.
                        wasTalking[i] = a.TalkingTo.IsValid;
                        wasWalkingWith[i] = a.WalkingWith.IsValid;
                        wasQueueing[i] = a.QueueSlot >= 0;
                    }
                }

                for (int p = 0; p < places; p++) wasOccupied[p] = occupants[p] > 0;

                sim.Tick(GameClock.TicksPerMinute);
            }

            return logs;
        }

        /// <summary>
        /// Whether the door she just went through was somebody else's.
        ///
        /// A watcher opposite cannot tell you whose house it is, and this must never try - that
        /// would be an identity, and the observation assembly is built so it cannot hold one.
        /// What is genuinely visible from across the road is that she went up a path and was let
        /// in somewhere that is not where she lives, which after a fortnight of watching her is
        /// a thing you would know.
        ///
        /// Only dwellings count. Going into the shop is not calling on anybody.
        /// </summary>
        private static bool WentSomewhereNotHers(PlaceId went, PlaceId home, bool[] isDwelling) =>
            went.IsValid && isDwelling[went.Value] && went.Value != home.Value;

        private static void Write(ObservationLog log, int minute, ObservedAct act,
                                  ObservedManner manner, int others, int forMinutes,
                                  SightingClarity clarity) =>
            log.Add(new Observed(minute, act, manner, others, forMinutes, clarity));

        /// <summary>
        /// How good a look the watcher got. Light, and whether there is glass in the way.
        ///
        /// Indoors is always Glimpsed however bright the shop is: through a window, at an
        /// angle, past a display. Clarity plays no part in the classification — an unsure
        /// sighting still counts, and counts USEFUL — so this is description rather than
        /// weighting, and it must stay that way or the instrument acquires a dial.
        /// </summary>
        private static SightingClarity ClarityAt(Tile where, TileGrid grid, int minuteOfDay)
        {
            if ((grid.FlagsAt(where) & TileFlags.Indoor) != 0) return SightingClarity.Glimpsed;
            if (minuteOfDay >= 7 * 60 && minuteOfDay < 19 * 60) return SightingClarity.Clear;
            if (minuteOfDay >= 6 * 60 && minuteOfDay < 21 * 60) return SightingClarity.Partial;
            return SightingClarity.Glimpsed;
        }

        /// <summary>
        /// For every visible figure, how many other visible figures are near enough and in
        /// clear enough line to read as being with them.
        ///
        /// Bucketed by tile rather than swept as n squared, the same way EncounterReport does
        /// it, so this stays a rounding error next to Simulation.Tick.
        /// </summary>
        private static void CountCompany(TileGrid grid, Tile[] pos, bool[] inSight, int[] others,
                                         int[] head, int[] next, List<int> touched,
                                         int cols, int rows, int n)
        {
            for (int i = 0; i < n; i++)
            {
                others[i] = 0;
                if (!inSight[i]) continue;

                int cell = (pos[i].Y / SightRange) * cols + pos[i].X / SightRange;
                if (cell < 0 || cell >= head.Length) continue;
                if (head[cell] == 0) touched.Add(cell);
                next[i] = head[cell] - 1;
                head[cell] = i + 1;      // one-based, so zero means empty and no clear pass is needed
            }

            for (int i = 0; i < n; i++)
            {
                if (!inSight[i]) continue;
                int cx = pos[i].X / SightRange, cy = pos[i].Y / SightRange;

                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;

                    for (int j = head[ny * cols + nx] - 1; j >= 0; j = next[j])
                    {
                        if (j == i) continue;
                        if (Tile.DistanceSquared(pos[i], pos[j]) > SightRange * SightRange) continue;
                        if (!ClearLine(grid, pos[i], pos[j])) continue;
                        others[i]++;
                    }
                }
            }

            foreach (int cell in touched) head[cell] = 0;
            touched.Clear();
        }

        /// <summary>Bresenham, checking for anything that blocks a view. Endpoints excluded —
        /// standing in a doorway does not hide you from the person beside you.</summary>
        private static bool ClearLine(TileGrid grid, Tile a, Tile b)
        {
            int x = a.X, y = a.Y;
            int dx = Math.Abs(b.X - x), dy = -Math.Abs(b.Y - y);
            int sx = x < b.X ? 1 : -1, sy = y < b.Y ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                if (x == b.X && y == b.Y) return true;

                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }

                if (x == b.X && y == b.Y) return true;
                if (grid.BlocksSight(x, y)) return false;
            }
        }

        /// <summary>The middle sample of everything this subject has been seen to do, read off
        /// the histogram. Integer ranks only, so two runs cannot disagree.</summary>
        private static int MedianPace(int[] counts, int who, int samples, int buckets)
        {
            int wanted = (samples + 1) / 2;
            int running = 0;
            int at = who * buckets;
            for (int v = 0; v < buckets; v++)
            {
                running += counts[at + v];
                if (running >= wanted) return v;
            }
            return 0;
        }

        private static int IntSqrt(int value)
        {
            if (value <= 0) return 0;
            int x = value;
            int step = (x + 1) / 2;
            while (step < x) { x = step; step = (x + value / x) / 2; }
            return x;
        }
    }
}
