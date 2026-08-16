using System;
using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.People
{
    public enum Activity : byte
    {
        Asleep = 0,
        AtHome,
        TravellingTo,
        AtWork,
        AtSchool,
        Shopping,
        AtThePub,
        AtChurch,
        Visiting,
        Walking,
        AtThePlayground,
        InTheGarden,

        /// <summary>
        /// Out of Rossville altogether, at a job the map does not contain.
        ///
        /// Anchored at the person's HOME rather than at nowhere, because every consumer of
        /// Block.Where - Simulation's agent destinations, the counter queues, the companion
        /// search - assumes a real place, and a PlaceId.None would be a null with extra steps.
        /// AgentMeshView reads the activity and simply does not draw them, which is what being
        /// out of town looks like from the street: an empty house and an empty pavement.
        /// </summary>
        AwayFromTown,

        // There was a WaitingForTheBus here. Nothing ever assigned it: village.txt places a bus
        // stop and says "two buses a day to Marlbury, and everyone knows both times", but no
        // DayPlanner path has ever sent anybody to wait at one, so the value could not occur and
        // Reports carried a display branch for a state no villager could be in. The bus stop
        // stays as authored scenery — its kinds.txt row is hours none, jobs 0, which is what
        // scenery looks like. Reinstate this only alongside a planner that actually queues
        // somebody at the kerb.

        /// <summary>
        /// Stopped in the street, talking to somebody.
        ///
        /// Not a planned activity - it happens to people on the way to somewhere else, which
        /// is exactly why it makes a village feel inhabited. Two figures standing still and
        /// facing each other reads as conversation with no dialogue written at all.
        /// </summary>
        Talking,

        /// <summary>
        /// Struck down and staying down — the sim's word for a body in the street.
        ///
        /// Set only by <see cref="Noir.Core.Sim.Simulation"/> when something (a car, so far)
        /// downs a person. Live state that outranks the plan forever, same mechanism as
        /// Stranded: the tick loop never overwrites a downed agent's Doing, never starts them
        /// on a journey, and never stands them up. AT THE END OF THE ENUM on purpose — the
        /// values are positional and the animations.txt keys are name-based.
        /// </summary>
        Downed,

        /// <summary>
        /// Standing over the scene, or walking to it off-plan. Live state set only by
        /// Simulation.Respond; the tick loop never overwrites it. At the end of the enum —
        /// values are positional.
        /// </summary>
        Responding,

        /// <summary>
        /// Standing in the loose ring around somebody else's misfortune. Live state set only
        /// by Simulation.Respond's standAs parameter; the tick loop never writes it. At the
        /// end of the enum — values are positional.
        /// </summary>
        Gawking,
    }

    /// <summary>One stretch of a person's day: be at this place, from this minute to that one.</summary>
    public readonly struct Block
    {
        public readonly short StartMinute;
        public readonly short EndMinute;
        public readonly PlaceId Where;
        public readonly Activity What;

        public Block(int start, int end, PlaceId where, Activity what)
        {
            // A block that runs past midnight, or backwards, is always a planner bug. Failing
            // here is far better than the alternative: DayPlan.At returns the FIRST covering
            // block, so an overlap silently makes the later one unreachable and the person
            // simply never does that thing. That is invisible in the game and maddening to
            // track down.
            if (end <= start || start < 0 || end > 1440)
                throw new ArgumentOutOfRangeException(nameof(start),
                    $"block {start}..{end} ({what}) is not inside a single day");

            StartMinute = (short)start;
            EndMinute = (short)end;
            Where = where;
            What = what;
        }

        public bool Covers(int minuteOfDay) => minuteOfDay >= StartMinute && minuteOfDay < EndMinute;
        public int Length => EndMinute - StartMinute;

        public override string ToString() =>
            $"{StartMinute / 60:00}:{StartMinute % 60:00}-{EndMinute / 60:00}:{EndMinute % 60:00} {What}";
    }

    /// <summary>
    /// A person's day, as a list of blocks.
    ///
    /// THE KEY DESIGN DECISION: this is a PURE FUNCTION of (seed, citizen, dayIndex). Nothing is
    /// stepped, accumulated or stored. That buys four things at once —
    ///   * no day-boundary hitch where a hundred schedules regenerate on one tick;
    ///   * the same day always replays identically, so "why was he there" has an answer;
    ///   * we can ask what anyone will be doing next Tuesday, or was doing last Friday,
    ///     without having simulated it;
    ///   * memory is a small cache rather than a hundred stored timetables.
    ///
    /// Structure is a hard skeleton of obligations (sleep, work, school) with discretionary time
    /// filled by weighted choice. Pure schedules feel robotic; pure utility feels random. The
    /// skeleton is what makes someone learnable, and the discretion is what stops them being a
    /// metronome.
    /// </summary>
    public sealed class DayPlan
    {
        public readonly CitizenId Who;
        public readonly int Day;
        private readonly Block[] _blocks;

        public DayPlan(CitizenId who, int day, Block[] blocks)
        {
            Who = who;
            Day = day;
            _blocks = blocks;
        }

        public IReadOnlyList<Block> Blocks => _blocks;

        public Block At(int minuteOfDay)
        {
            for (int i = 0; i < _blocks.Length; i++)
                if (_blocks[i].Covers(minuteOfDay)) return _blocks[i];
            return _blocks.Length > 0 ? _blocks[_blocks.Length - 1] : default;
        }

        public Block? Next(int minuteOfDay)
        {
            for (int i = 0; i < _blocks.Length; i++)
                if (_blocks[i].StartMinute > minuteOfDay) return _blocks[i];
            return null;
        }
    }

    public static class DayPlanner
    {
        /// <summary>
        /// Build one person's day. Deterministic in (seed, citizen, day) — no hidden state.
        /// </summary>
        public static DayPlan Plan(WorldModel world, Population population, Citizen who,
                                   int day, ulong seed)
        {
            // A stream unique to this person AND this day, so yesterday does not shift today.
            var rng = new Xoshiro256ss(Mix(seed, (ulong)who.Id.Value, (ulong)day));
            int dayOfWeek = day % 7;

            // WHICH YEAR IT IS, because how old somebody is decides most of what follows and a
            // fifteen-year game moves people between stages. A child planned in 1991 is planned as
            // an adult in 2005 - they leave school, stop being walked there, and start keeping
            // adult hours. Derived from `day` rather than passed in, so there is no second way to
            // be wrong about it.
            int year = new GameClock(GameClock.TickAt(day, 0)).Year;

            bool weekend = dayOfWeek >= 5;
            bool sunday = dayOfWeek == 6;

            var blocks = new List<Block>();
            int jitter = who.Punctuality;

            // ---- sleep: everyone starts the day in bed ----
            int wake = WakeTime(who, year, weekend) + jitter;
            int bed = BedTime(who, year, weekend) + jitter / 2;
            blocks.Add(new Block(0, wake, who.Home, Activity.Asleep));

            int cursor = wake;

            // ---- the school run ----
            //
            // An adult in a house with a small child walks them to the school gate and comes
            // back. It departs at exactly the same minute as the child's own school block, so
            // the two of them leave the house together and the simulation pairs them up on the
            // way - which is the whole point. A parent arriving separately is just two people
            // who happened to go to the same place.
            //
            // This is also the first thing that uses the `population` argument, which the
            // signature has always taken and the body never touched.
            if (!weekend && !who.IsChildIn(year) && HasInfant(population, who, year))
            {
                var school = Catchment(world, population, who, PlaceKind.School);
                if (school.IsValid)
                {
                    var sp = world.GetPlace(school);
                    int bell = OpeningOf(sp, dayOfWeek, 8 * 60 + 30);
                    int back = bell + 14;

                    // Only if it does not collide with their own shift.
                    bool clashesWithWork = who.Works && WorkStartsBefore(world, who, dayOfWeek, back + 10);

                    if (!clashesWithWork && bell >= cursor && back < bed)
                    {
                        if (bell > cursor) blocks.Add(new Block(cursor, bell, who.Home, Activity.AtHome));
                        blocks.Add(new Block(bell, back, school, Activity.Visiting));
                        cursor = back;
                    }
                }
            }

            // ---- the obligation: work or school ----
            if (who.IsChildIn(year) && !weekend)
            {
                var school = Catchment(world, population, who, PlaceKind.School);
                if (school.IsValid)
                {
                    var p = world.GetPlace(school);
                    int start = OpeningOf(p, dayOfWeek, 8 * 60 + 30);
                    int end = ClosingOf(p, dayOfWeek, 15 * 60 + 30);
                    if (start > cursor) blocks.Add(new Block(cursor, start, who.Home, Activity.AtHome));

                    // GUARDED THE SAME WAY THE WORK BRANCH IS, and for a reason the work branch
                    // only half-covers: Block's constructor THROWS on end <= start, and `in_` is
                    // the cursor when the morning has already run past the bell. A child whose
                    // earlier blocks overran school closing crashed population generation for the
                    // whole town, from one lot's data.
                    //
                    // Not advancing the cursor matters as much as not adding the block. `cursor`
                    // is already at or past `end` in this case, so `cursor = end` would move it
                    // BACKWARDS and every block placed afterwards would overlap - which DayPlan.At
                    // resolves by returning the first covering block, so the person silently never
                    // does the later thing. That is the failure the constructor's own comment says
                    // it exists to prevent.
                    int in_ = Math.Max(cursor, start);
                    if (end > in_)
                    {
                        AddObligation(blocks, world, who, year, school, Activity.AtSchool,
                                      in_, end, jitter, dayOfWeek, rng);
                        cursor = end;
                    }
                }
            }
            else if (who.Works)
            {
                var p = world.GetPlace(who.Work);
                if (p != null && WorksToday(p, dayOfWeek))
                {
                    var (start, end) = ShiftFor(p, dayOfWeek, who.Shift);
                    start += jitter;
                    if (start > cursor) blocks.Add(new Block(cursor, start, who.Home, Activity.AtHome));

                    // `end > start` was not enough. The value actually handed to Block is
                    // Math.Max(cursor, start), so a shift that ends before the cursor already
                    // stands still built a backwards block and threw - the guard checked one
                    // number and passed a different one.
                    int from = Math.Max(cursor, start);
                    if (end > from)
                    {
                        AddObligation(blocks, world, who, year, who.Work, Activity.AtWork,
                                      from, end, jitter, dayOfWeek, rng);
                        cursor = end;
                    }
                }
            }
            else if (!weekend && who.WorksAwayIn(year))
            {
                // GONE BY SIX. Rossville employs about 168 people and holds roughly 450 of
                // working age; the rest worked out of town - Hoopeston's canneries five miles
                // north, Danville twenty south, the elevators, the railroad, the ground. Until
                // this branch existed every one of them was planned as a person with a free
                // weekday and sent out to do five errands at ten in the morning, which is why the
                // streets read as a retirement village rather than a farm town.
                //
                // Anchored at home and drawn nowhere - see Activity.AwayFromTown. The commute
                // itself is not simulated: there is no road off this map yet, and inventing one
                // to drive down would be a worse lie than simply not being here.
                int out_ = 6 * 60 + 20 + jitter;          // on the road before half past six
                int back = 17 * 60 + 10 + jitter;

                if (out_ > cursor) blocks.Add(new Block(cursor, out_, who.Home, Activity.AtHome));
                int from = Math.Max(cursor, out_);
                if (back > from)
                {
                    blocks.Add(new Block(from, back, who.Home, Activity.AwayFromTown));
                    cursor = back;
                }
            }

            // ---- church, on a Sunday morning ----
            if (sunday)
            {
                var church = Catchment(world, population, who, PlaceKind.Church);

                // The draw happens unconditionally, before any test that could skip it, so that
                // adding or changing conditions here never shifts the RNG stream for everything
                // downstream. Cheap discipline; it is what keeps a seed reproducible.
                bool attends = rng.Chance(ChurchChance(who, year));

                if (church.IsValid && attends)
                {
                    var cp = world.GetPlace(church);
                    int svcStart = OpeningOf(cp, dayOfWeek, 9 * 60 + 30);
                    int svcEnd = ClosingOf(cp, dayOfWeek, 12 * 60);

                    int start = Math.Max(cursor, svcStart);

                    // Someone still on shift when the service ends simply misses it. Previously
                    // start was only clamped UPWARD to cursor, so a publican clocking off at
                    // 23:00 went to a locked church and stood in it until ten past midnight.
                    if (start + 70 <= Math.Min(svcEnd, bed))
                    {
                        if (start > cursor) blocks.Add(new Block(cursor, start, who.Home, Activity.AtHome));
                        blocks.Add(new Block(start, start + 70, church, Activity.AtChurch));
                        cursor = start + 70;
                    }
                }
            }

            // ---- discretionary time, until bed ----
            int errands = ErrandCount(who, year, weekend, bed - cursor, rng);
            for (int e = 0; e < errands && cursor < bed - 40; e++)
            {
                // SPREAD ACROSS WHAT IS LEFT, NOT STACKED AT THE FRONT.
                //
                // The gap was a flat 15-90 minutes, which meant every errand was taken as early
                // as it possibly could be: somebody with no job woke at seven, did both errands
                // before eleven, and then sat indoors for eleven hours. A census through a driven
                // day caught it exactly - 51 people out at nine in the morning, nine at eleven,
                // and at three in the afternoon NOBODY on the street in a town of 970.
                //
                // Dividing the remaining window by the errands remaining puts each one somewhere
                // in its own share of the day, so a free afternoon has something in it.
                int slice = (bed - 40 - cursor) / Math.Max(1, errands - e);
                // The gap is drawn BEFORE choosing where to go, so the place can be tested
                // against the hour they will actually arrive. Previously it was tested at a
                // fixed cursor+30 while the real start landed anywhere from +15 to +104, so
                // the tested instant matched the real one about one draw in ninety - people
                // walked to shops that had shut an hour earlier.
                int gap = 15 + rng.NextInt(Math.Max(1, slice));
                int start = cursor + gap;
                if (start > bed - 40) break;

                // ONE REFUSAL NO LONGER ENDS THE DAY. This was `break`, so a single errand that
                // could not be placed - everything of that kind shut at that hour, or the map
                // having none at all - sent the person home until bedtime. That is how a city
                // with two greens and no shops came out with 238 of 365 indoors at three in the
                // afternoon: the first empty list stopped everything behind it. Trying the next
                // slot instead costs one wasted draw and the loop still ends, because `e` counts.
                var chosen = ChooseErrand(world, who, year, dayOfWeek, start, bed, rng);
                if (!chosen.HasValue) continue;

                var (place, activity, duration) = chosen.Value;
                int end = start + duration;
                if (end > bed - 20) break;

                blocks.Add(new Block(cursor, start, who.Home, Activity.AtHome));
                blocks.Add(new Block(start, end, place, activity));
                cursor = end;
            }

            // ---- home, then bed ----
            // A late shift can run past the nominal bedtime; without this clamp the Asleep
            // block starts before the work block ends and the two overlap. At() returns the
            // FIRST covering block, so the later one becomes silently unreachable - the person
            // never goes to bed at all.
            if (bed < cursor) bed = cursor;

            if (cursor < bed) blocks.Add(new Block(cursor, bed, who.Home, Activity.AtHome));
            if (bed < 1440) blocks.Add(new Block(bed, 1440, who.Home, Activity.Asleep));

            return new DayPlan(who.Id, day, blocks.ToArray());
        }

        // ---- the dinner hour ----

        /// <summary>
        /// The middle of the day. 12:00, shifted by the same punctuality that moves somebody's
        /// shift, so a person who is habitually ten minutes late is habitually ten minutes late
        /// to their dinner too and the village does not empty onto the street in lockstep.
        /// </summary>
        private const int DinnerStart = 12 * 60;

        private const int DinnerLength = 45;

        /// <summary>
        /// A block of work or school, with the dinner hour cut out of the middle of it.
        ///
        /// WHY THIS EXISTS. A shift used to be one unbroken block — 09:00 to 17:00, or 08:30 to
        /// 15:30 for a child — so for the whole of the best light in the day, thirty-seven adults
        /// and twenty children were sealed inside buildings. The `street` instrument measured the
        /// result: 0.2 of 112 people visible at half past twelve, against 13.6 at half past
        /// eight. The village was not still, it was empty at the hours anyone actually looked at
        /// it, and the default clock is noon.
        ///
        /// The break is spent OUT, deliberately — on the green, in the churchyard, at the
        /// playground, or at the pub. Sending people home for their dinner would satisfy any
        /// reasonable description of a lunch break and buy almost nothing you can see, because
        /// home is another roof. The pub's 11:00-14:30 opening was already authored in
        /// village.txt and had no trade in it at all; a dinner hour is what that window was
        /// always for.
        ///
        /// If nothing suitable is open and near, there is no break and the block stays whole.
        /// A farm hand at the far end of the fields genuinely does eat where he is standing.
        /// </summary>
        private static void AddObligation(List<Block> blocks, WorldModel world, Citizen who, int year,
                                          PlaceId where, Activity what,
                                          int from, int until, int jitter, int dayOfWeek, IRng rng)
        {
            // One draw, unconditionally, before any test that could skip it — the same discipline
            // as the church draw and ChooseNearby's roll. A filter that sometimes skips the roll
            // would shift every decision later in the day.
            float roll = rng.NextFloat();

            // NO ROOM, NO OBLIGATION. Both callers check this now, and it is checked again here
            // because this is the choke point every one of them goes through and the failure it
            // prevents is a thrown exception out of population generation for the whole town. The
            // draw above still happens, so guarding here cannot shift anybody else's numbers.
            if (until <= from) return;

            int start = DinnerStart + jitter;
            int end = start + DinnerLength;

            // Work either side of it, or it is not a break in a shift — it is a short shift.
            bool straddles = from <= start - 15 && until >= end + 15;

            if (straddles)
            {
                var (place, activity) = DinnerPlace(world, who, year, where, dayOfWeek, start, roll);
                if (place.IsValid)
                {
                    blocks.Add(new Block(from, start, where, what));
                    blocks.Add(new Block(start, end, place, activity));
                    blocks.Add(new Block(end, until, where, what));
                    return;
                }
            }

            blocks.Add(new Block(from, until, where, what));
        }

        /// <summary>
        /// Where the dinner hour is spent. Measured from the workplace door, not the house —
        /// you go out from where you already are.
        /// </summary>
        private static (PlaceId, Activity) DinnerPlace(WorldModel world, Citizen who, int year, PlaceId workplace,
                                                       int dayOfWeek, int from, float roll)
        {
            var door = Locality.AnchorOf(world.GetPlace(workplace));
            var candidates = new List<PlaceId>();

            // Never the building they are trying to get out of. The tavern is the obvious
            // candidate for a dinner hour and it is also where three publicans work, so without
            // this a publican's break is a shift.
            void Offer(PlaceKind kind)
            {
                var ids = world.PlacesOfKind(kind);
                for (int i = 0; i < ids.Count; i++)
                    if (ids[i] != workplace) candidates.Add(ids[i]);
            }

            Offer(PlaceKind.Green);
            Offer(PlaceKind.Churchyard);

            if (who.IsChildIn(year))
            {
                Offer(PlaceKind.Playground);
            }
            else
            {
                Offer(PlaceKind.Playground);
                Offer(PlaceKind.Gardens);
                Offer(PlaceKind.Tavern);
            }

            var chosen = Locality.ChooseNearby(world, door, candidates, dayOfWeek, from,
                                               DinnerLength, NearbyChoices, roll);
            if (!chosen.IsValid) return (PlaceId.None, Activity.AtHome);

            switch (world.GetPlace(chosen).Kind)
            {
                case PlaceKind.Tavern: return (chosen, Activity.AtThePub);
                case PlaceKind.Playground: return (chosen, Activity.AtThePlayground);
                case PlaceKind.Gardens: return (chosen, Activity.InTheGarden);
                default: return (chosen, Activity.Walking);
            }
        }

        // ---- the shape of a day ----

        private static int WakeTime(Citizen who, int year, bool weekend)
        {
            switch (who.Job)
            {
                case Occupation.Farmer:
                case Occupation.FarmHand: return 5 * 60;                      // milking
                case Occupation.MillHand: return who.Shift == 0 ? 5 * 60 + 15 : 8 * 60;
                default:
                    if (who.StageIn(year) == LifeStage.Elder) return 6 * 60 + 30;
                    return weekend ? 8 * 60 : 7 * 60;
            }
        }

        private static int BedTime(Citizen who, int year, bool weekend)
        {
            if (who.IsChildIn(year)) return 20 * 60 + 30;
            if (who.StageIn(year) == LifeStage.Elder) return 22 * 60;
            if (who.Job == Occupation.MillHand && who.Shift == 1) return 23 * 60 + 30;
            return weekend ? 23 * 60 + 30 : 22 * 60 + 45;
        }

        private static float ChurchChance(Citizen who, int year) =>
            who.StageIn(year) == LifeStage.Elder ? 0.7f : who.IsChildIn(year) ? 0.35f : 0.3f;

        /// <summary>
        /// How many errands this person has in them today.
        ///
        /// <paramref name="free"/> is the discretionary window in minutes - what is left after
        /// work, school and church. IT HAS TO COUNT, and it did not: the answer was one or two
        /// whether the person had two hours to fill or fourteen, so the retired and the
        /// unemployed - the large majority of Rossville, and the only people about during the
        /// working day - did their shopping before eleven and then sat at home until bed. A town
        /// whose streets are empty every afternoon is not a quiet town, it is an unfinished one.
        ///
        /// One more errand per four free hours, so a working day out ends up with four or five
        /// things in it and a short evening still has one.
        /// </summary>
        private static int ErrandCount(Citizen who, int year, bool weekend, int free, IRng rng)
        {
            float social = who.Sociability / 255f;
            int n = rng.Chance(0.35f + social * 0.4f) ? 2 : 1;
            if (weekend && rng.Chance(0.4f)) n++;
            if (who.IsChildIn(year)) n = rng.Chance(0.7f) ? 1 : 0;

            // Drawn AFTER the rolls above so none of them shift, for the same reason the errand
            // list is appended to rather than inserted into.
            if (!who.IsChildIn(year) && free > 0) n += Math.Min(4, free / 240);
            return n;
        }

        /// <summary>
        /// How many of the nearest candidates of a kind are worth considering.
        ///
        /// Six is enough that a village with three shops still has a choice and a town with
        /// thirty is not choosing between all thirty. It is a shortlist, not a decision — what
        /// happens to the six is in <see cref="Locality.ChooseNearby"/>.
        /// </summary>
        private const int NearbyChoices = 6;

        /// <summary>
        /// Weighted pick over what is actually open and plausible for this person.
        /// <paramref name="from"/> is the real arrival minute, not an estimate.
        /// </summary>
        private static (PlaceId, Activity, int)? ChooseErrand(WorldModel world, Citizen who, int year,
                                                              int dayOfWeek, int from, int until, IRng rng)
        {
            var options = new List<(PlaceId place, Activity act, int minutes, int weight)>();
            int evening = 18 * 60;

            // Errands start from the front door: the planner always puts an at-home block in
            // front of one.
            var doorstep = Locality.AnchorOf(world.GetPlace(who.Home));

            void Consider(PlaceKind kind, Activity act, int minutes, int weight) =>
                ConsiderThese(world.PlacesOfKind(kind), act, minutes, weight);

            // The same thing for a kind only kinds.txt knows. A city kind is numbered past the
            // enum's members, so it has no C# name to pass to the line above, and every one of
            // Rossville's own amenities was therefore unreachable from here.
            void ConsiderNamed(string name, Activity act, int minutes, int weight)
            {
                if (!PlaceKindTable.Current.TryNamed(name, out var kind)) return;
                ConsiderThese(world.PlacesOfKind(kind), act, minutes, weight);
            }

            void ConsiderThese(IReadOnlyList<PlaceId> ids, Activity act, int minutes, int weight)
            {
                if (ids.Count == 0 || weight <= 0) return;

                // One draw whatever happens below, for the same reason the church draw is
                // unconditional: a filter that sometimes skips the roll would shift every
                // decision left in the day.
                float roll = rng.NextFloat();

                if (from + minutes > until) return;

                // Which of the several shops, and it is not the one the dice land on: it used
                // to be `ids[rng.NextInt(ids.Count)]`, an index into a positional list, so a
                // person's errand depended on the order places happen to appear in village.txt
                // and half of them were a walk across the whole map.
                var id = Locality.ChooseNearby(world, doorstep, ids, dayOfWeek, from, minutes,
                                               NearbyChoices, roll);
                if (!id.IsValid) return;

                options.Add((id, act, minutes, weight));
            }

            float social = who.Sociability / 255f;

            if (who.IsChildIn(year))
            {
                Consider(PlaceKind.Playground, Activity.AtThePlayground, 60, 60);
                Consider(PlaceKind.Green, Activity.Walking, 45, 30);
                Consider(PlaceKind.Shop, Activity.Shopping, 15, 10);
            }
            else
            {
                Consider(PlaceKind.Shop, Activity.Shopping, 25, 45);
                Consider(PlaceKind.PostOffice, Activity.Shopping, 20, 18);
                Consider(PlaceKind.Green, Activity.Walking, 40, 20);
                Consider(PlaceKind.Gardens, Activity.InTheGarden, 90,
                         who.StageIn(year) == LifeStage.Elder ? 45 : 22);
                Consider(PlaceKind.VillageHall, Activity.Visiting, 100, (int)(18 * social));

                if (from >= evening - 120)
                    Consider(PlaceKind.Tavern, Activity.AtThePub, 90 + rng.NextInt(60),
                             (int)(20 + 70 * social));

                if (who.StageIn(year) == LifeStage.Elder)
                    Consider(PlaceKind.Churchyard, Activity.Visiting, 30, 20);
            }

            // Calling on somebody.
            //
            // Last in the list on purpose: every Consider draws once, so anything inserted above
            // shifts the draws of everything below it. Added here, the existing errands keep the
            // rolls they have always had.
            //
            // This was missing entirely, and it was the largest single hole in the village. The
            // 2:1 instrument watched a hundred and twelve people for a fortnight and recorded
            // ZERO visits in 1,568 household-days: nobody in Rossville had ever once knocked on
            // anybody's door, because the errand list was a list of AMENITIES and a neighbour is
            // not an amenity. It matters past the ratio, too - "who would notice her gone, and
            // how fast" is the question the whole eventual game turns on, and it has no answer
            // in a village where nobody calls.
            // Weighted below shopping on purpose. The first pass had it at 10 + 45*social, which
            // peaks at 55 against the shop's 45 - so the most sociable half of the village called
            // on neighbours more often than it bought food, and a test whose whole job was to put
            // three people at a counter at once stopped being able to.
            // Half an hour to an hour: calling round is a cup of tea. The first pass allowed up
            // to an hour and a half, which is a different social occasion - and it showed up in
            // the instrument as the village going quiet, because a visit trades forty minutes on
            // the green for an hour indoors where nobody can see you.
            ConsiderThese(NeighboursOf(world, who), Activity.Visiting,
                          25 + rng.NextInt(35),
                          who.IsChildIn(year) ? (int)(3 + 9 * social)
                                      : (int)(3 + 12 * social) + (who.StageIn(year) == LifeStage.Elder ? 6 : 0));

            // ---- what the town has that the old village never did ----
            //
            // A cinema, a casino, a diner and a newspaper shop, all standing open, all lit, and
            // before this NOBODY HAD EVER WALKED INTO ONE. They are kinds the enum has never
            // heard of, so nothing above could name them, and the errand list was a village's
            // list running in a city.
            //
            // Added below the neighbour draw rather than beside their nearest village equivalent
            // so that every roll above keeps the value it has always had - the same reason the
            // neighbour draw itself went last.
            ConsiderNamed("diner", Activity.AtThePub, 40, 26);
            ConsiderNamed("newsstand", Activity.Shopping, 10, 20);

            if (!who.IsChildIn(year))
            {
                // Both are evening places, and the casino is the one that is open when nothing
                // else is - which is the whole reason to have one on a map about a killer.
                ConsiderNamed("cinema", Activity.Visiting, 110,
                              from >= evening - 180 ? (int)(12 + 26 * social) : 4);
                ConsiderNamed("casino", Activity.AtThePub, 80 + rng.NextInt(70),
                              from >= evening ? (int)(8 + 22 * social) : 3);
            }

            if (options.Count == 0) return null;

            int total = 0;
            foreach (var o in options) total += o.weight;
            if (total <= 0) return null;

            int roll = rng.NextInt(total);
            foreach (var o in options)
            {
                roll -= o.weight;
                if (roll < 0) return (o.place, o.act, o.minutes);
            }
            return (options[0].place, options[0].act, options[0].minutes);
        }

        // ---- helpers ----

        /// <summary>
        /// The houses somebody might call at: every dwelling but their own.
        ///
        /// Deliberately not "their friends". There is no acquaintance graph in the world model
        /// and inventing one here would be a second, quieter population generator - the nearness
        /// weighting in ChooseNearby already produces the right shape, because who you drop in on
        /// is mostly a question of who is on your way home. The people you visit repeatedly fall
        /// out of that rather than being written down anywhere, which is the same trick the
        /// errand picker uses for shops.
        ///
        /// Allocates a list per call. ChooseErrand already allocates one, and Plan is memoised
        /// per citizen per day, so this costs a few dozen small lists a day and nothing per tick.
        /// </summary>
        private static IReadOnlyList<PlaceId> NeighboursOf(WorldModel world, Citizen who)
        {
            var all = world.Homes;
            var others = new List<PlaceId>(all.Count);
            foreach (var id in all)
                if (id.Value != who.Home.Value) others.Add(id);
            return others;
        }

        private static bool WorksToday(Place p, int dayOfWeek) =>
            p.MinutesUntilOpen(0, dayOfWeek) == 0 || OpeningToday(p, dayOfWeek) >= 0;

        private static int OpeningToday(Place p, int dayOfWeek)
        {
            int best = -1;
            foreach (var w in p.Hours)
                if ((w.DaysMask & (1 << dayOfWeek)) != 0)
                    if (best < 0 || w.StartMinute < best) best = w.StartMinute;
            return best;
        }

        /// <summary>
        /// The working day at a place.
        ///
        /// Two windows usually means a lunch break (the shop shuts 13:00–14:00), not two shifts —
        /// so most people work from first opening to last closing and the place stays staffed.
        /// The mill is the exception: it genuinely runs an early and a late shift, and half its
        /// hands are on each. Getting this wrong left the shop unmanned every afternoon.
        ///
        /// Which places are the exception is the `shifts` column of kinds.txt rather than the
        /// mill by name, so the next thing that runs nights does not need this method edited.
        /// </summary>
        private static (int, int) ShiftFor(Place p, int dayOfWeek, byte shift)
        {
            var todays = new List<OpenWindow>();
            foreach (var w in p.Hours)
                if ((w.DaysMask & (1 << dayOfWeek)) != 0) todays.Add(w);

            if (todays.Count == 0) return (0, 0);

            if (PlaceKindTable.Current.Row(p.Kind).SplitShifts && todays.Count > 1)
            {
                var chosen = todays[Math.Min(shift, todays.Count - 1)];
                return (chosen.StartMinute, chosen.EndMinute);
            }

            int open = int.MaxValue, close = int.MinValue;
            foreach (var w in todays)
            {
                if (w.StartMinute < open) open = w.StartMinute;
                if (w.EndMinute > close) close = w.EndMinute;
            }
            return (open, close);
        }

        private static int OpeningOf(Place p, int dayOfWeek, int fallback)
        {
            int o = OpeningToday(p, dayOfWeek);
            return o >= 0 ? o : fallback;
        }

        private static int ClosingOf(Place p, int dayOfWeek, int fallback)
        {
            int best = -1;
            foreach (var w in p.Hours)
                if ((w.DaysMask & (1 << dayOfWeek)) != 0 && w.EndMinute > best) best = w.EndMinute;
            return best >= 0 ? best : fallback;
        }

        /// <summary>
        /// Is this the person who takes the children to school?
        ///
        /// One adult per household, not both. An earlier version had every adult in the house
        /// walking to the school gate, so the Dallimores turned out en masse every weekday
        /// morning. The escort is simply the first adult in the household - deterministic, and
        /// it means the same parent does it every day, which is also what happens.
        /// </summary>
        private static bool HasInfant(Population population, Citizen who, int year)
        {
            if (population == null) return false;

            var household = population.HouseholdMembers(who);
            bool anyInfant = false;
            var escort = CitizenId.None;

            for (int i = 0; i < household.Count; i++)
            {
                var member = population.Get(household[i]);
                if (member == null) continue;

                if (member.IsChildIn(year))
                {
                    if (member.AgeIn(year) <= 9) anyInfant = true;
                    continue;
                }
                if (!escort.IsValid) escort = member.Id;
            }

            return anyInfant && escort == who.Id;
        }

        private static bool WorkStartsBefore(WorldModel world, Citizen who, int dayOfWeek, int minute)
        {
            var place = world.GetPlace(who.Work);
            if (place == null) return false;
            var (start, _) = ShiftFor(place, dayOfWeek, who.Shift);
            return start > 0 && start < minute;
        }

        /// <summary>
        /// Which school, which church, which surgery this person belongs to.
        ///
        /// Read off the household, where the generator settled it once from where the house is.
        /// It used to be the world's first place of that kind, so every child in the parish
        /// walked to the same gate however far away it was — which does not even show up at
        /// village scale, because there is only one school.
        ///
        /// Reading it rather than deciding it here is what keeps Plan a pure function of what
        /// it was handed. <see cref="FirstOfKind"/> remains as the fallback for a household
        /// built without a catchment.
        /// </summary>
        private static PlaceId Catchment(WorldModel world, Population population, Citizen who,
                                         PlaceKind kind)
        {
            var household = population?.HouseholdOf(who);
            if (household != null)
            {
                var chosen = household.Catchment(kind);
                if (chosen.IsValid) return chosen;
            }
            return FirstOfKind(world, kind);
        }

        private static PlaceId FirstOfKind(WorldModel world, PlaceKind kind)
        {
            var ids = world.PlacesOfKind(kind);
            return ids.Count > 0 ? ids[0] : PlaceId.None;
        }

        private static ulong Mix(ulong seed, ulong a, ulong b)
        {
            ulong h = seed ^ 0x9E3779B97F4A7C15UL;
            h = (h ^ a) * 0xBF58476D1CE4E5B9UL;
            h = (h ^ (h >> 27) ^ b) * 0x94D049BB133111EBUL;
            return h ^ (h >> 31);
        }
    }
}
