using System;
using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.People
{
    /// <summary>
    /// One planned crash: everything the town needs to stage it and judge it, fixed at plan
    /// time. No identity leaks to witnesses - the tones/shapes here are what the event store
    /// records, the same coarse vocabulary <see cref="CarTone"/>/<see cref="CarShape"/> already
    /// use for a witness's own account of a car.
    /// </summary>
    public readonly struct CrashPlan
    {
        public readonly int MinuteOfDay;
        public readonly Tile Scene;
        public readonly CitizenId AtFault;
        public readonly CitizenId Other;

        /// <summary>The rare bad one: AtFault goes down.</summary>
        public readonly bool Injury;

        public readonly CrashVerdict Verdict;

        /// <summary>The at-fault car, as witnesses may see it.</summary>
        public readonly CarTone Tone;
        public readonly CarShape Shape;

        public CrashPlan(int minuteOfDay, Tile scene, CitizenId atFault, CitizenId other,
                         bool injury, CrashVerdict verdict, CarTone tone, CarShape shape)
        {
            MinuteOfDay = minuteOfDay;
            Scene = scene;
            AtFault = atFault;
            Other = other;
            Injury = injury;
            Verdict = verdict;
            Tone = tone;
            Shape = shape;
        }
    }

    /// <summary>
    /// The town's one crash a day - or none, which is the honest common case.
    ///
    /// PURE in (world, population, day, seed): nothing here is stepped, cached or stored, in the
    /// same spirit as <see cref="DayPlanner"/> and for the same reasons - the same day always
    /// replays identically, and "was there a crash on Tuesday" can be asked of any day without
    /// having simulated it. Every citizen's <see cref="DayPlan"/> is recomputed on demand from
    /// <see cref="DayPlanner.Plan"/> rather than read off anything stored.
    /// </summary>
    public static class CrashPlanner
    {
        /// <summary>
        /// One (citizen, minute) driving event: a moment Rule 1 says this citizen was behind a
        /// wheel, and the place the drive started from - which is also where <see cref="Scene"/>
        /// search below begins if this citizen ends up at fault.
        /// </summary>
        private readonly struct DrivingEvent
        {
            public readonly CitizenId Who;
            public readonly int Minute;
            public readonly PlaceId Where;

            /// <summary>True when this event is a pub-close drive, not a commute edge - the fact
            /// Rule 2's fault tiebreak (drink outranks id order) needs to know.</summary>
            public readonly bool IsPubDrive;

            public DrivingEvent(CitizenId who, int minute, PlaceId where, bool isPubDrive)
            {
                Who = who; Minute = minute; Where = where; IsPubDrive = isPubDrive;
            }
        }

        /// <summary>A candidate pair, already resolved to who is at fault and where the crash
        /// would stand, with the weight Rule 2's table gives its minute.</summary>
        private readonly struct Candidate
        {
            public readonly CitizenId AtFault;
            public readonly CitizenId Other;
            public readonly int Minute;
            public readonly PlaceId Origin;
            public readonly int Weight;

            public Candidate(CitizenId atFault, CitizenId other, int minute, PlaceId origin, int weight)
            {
                AtFault = atFault; Other = other; Minute = minute; Origin = origin; Weight = weight;
            }
        }

        /// <summary>
        /// Names the per-citizen pub-driving coin so it can be drawn as a stateless function of
        /// WHO rather than of stream position - see the long comment on <see cref="DroveToThePub"/>
        /// for why this is <see cref="Rolls"/> and not a stream draw.
        /// </summary>
        private static readonly ulong PubCoinPurpose = Rolls.Purpose("crash-pub-coin");

        /// <summary>
        /// How far the scene search will walk from the at-fault driver's own doorstep before
        /// giving up. Rule 3's own number - see <see cref="FindRoadOutward"/>.
        /// </summary>
        private const int SceneReach = 12;

        /// <summary>The day's crash, or null - a day with no qualifying pair of drivers is a day
        /// with no crash, and that is honest. Pure in (seed, day, world, population): recomputed
        /// on demand, stored nowhere, identical every call.</summary>
        public static CrashPlan? PlanFor(WorldModel world, Population population, int day, ulong seed)
        {
            if (world == null || population == null) return null;

            // WHICH YEAR IT IS, the same derivation DayPlanner.Plan uses to decide who is a
            // child - needed here only for Rule 1(b)'s "adult" gate on the pub-driving coin.
            int year = new GameClock(GameClock.TickAt(day, 0)).Year;

            // Rule 2: one stream for the whole day, covering the pair pick, the severity draw,
            // the verdict draw and the tone/shape draws below - everything EXCEPT the per-citizen
            // pub coin, which is deliberately not on this stream (see DroveToThePub).
            var rng = new Xoshiro256ss(Mix(seed, 0x0C0111DEUL, (ulong)day));

            // ---- Rule 1: every driving event, citizen-id order, block-plan order within a
            // citizen. Citizens are walked by an explicit ascending CitizenId rather than trusted
            // to arrive in that order from Population, so the "citizens in id order" requirement
            // is true by construction, not by convention. Each citizen's DayPlan is kept for
            // later (Rule 5's verdict needs the at-fault driver's whole day, not just the block
            // that put them on the road at the crash minute) rather than recomputed a second
            // time - which would still be pure, just wasteful.
            //
            // ONE MINUTE PER EVENT, NOT A THIRTEEN-MINUTE WINDOW. Rule 1's own "within ±6
            // minutes of the block's edge" defines when a citizen counts as driving at all - it
            // is what makes the block's own edge minute a legal driving-minute, trivially (an
            // edge is 0 minutes from itself). Rule 2 then gives its own, separate number - "≤ 4
            // apart" - for when two drivers' minutes are close enough to pair. The block's edge
            // minute is recorded as the event, once, and the ≤4 test below is what actually
            // decides pairing; nothing here enumerates the ±6 window as thirteen candidate
            // minutes; that would be a different and much more expensive reading the rules do
            // not ask for.
            var plans = new DayPlan[population.Count];
            var events = new List<DrivingEvent>();

            for (int i = 0; i < population.Count; i++)
            {
                var who = population.Get(new CitizenId(i));
                if (who == null) continue;

                var plan = DayPlanner.Plan(world, population, who, day, seed);
                plans[i] = plan;

                foreach (var block in plan.Blocks)
                {
                    if (block.What == Activity.AwayFromTown)
                    {
                        // The one drive Core already knows about: gone before half seven, back
                        // after five. Both edges are driving events - the morning departure and
                        // the evening return - and both are anchored at Where == who.Home, which
                        // is what DayPlanner puts on an AwayFromTown block regardless of which
                        // edge is in play.
                        events.Add(new DrivingEvent(who.Id, block.StartMinute, block.Where, false));
                        events.Add(new DrivingEvent(who.Id, block.EndMinute, block.Where, false));
                    }
                    else if (block.What == Activity.AtThePub && block.EndMinute >= 20 * 60)
                    {
                        // Rule 1(b): an adult, a pub block closing at or after 20:00, and the
                        // planner's own deeming coin. Core has no car ownership - this is the
                        // deliberate fiction that stands in for it: half the evening pub crowd
                        // drove home, decided by seed, same as DayPlanner deems who escorts a
                        // child to school or who takes the dinner-hour walk.
                        bool isAdult = !who.IsChildIn(year);
                        if (isAdult && DroveToThePub(seed, who, day))
                            events.Add(new DrivingEvent(who.Id, block.EndMinute, block.Where, true));
                    }
                }
            }

            // ---- Rule 2: candidate pairs, in the fixed order the double loop below visits them
            // - citizen-id-and-block order on the outer index, the same on the inner. Two events
            // belonging to the SAME citizen never pair (a lone commuter's own departure and
            // return are hours apart in every real case anyway, but the check is explicit rather
            // than relying on that).
            var candidates = new List<Candidate>();
            for (int i = 0; i < events.Count; i++)
            {
                var a = events[i];
                for (int j = i + 1; j < events.Count; j++)
                {
                    var b = events[j];
                    if (a.Who == b.Who) continue;
                    if (Math.Abs(a.Minute - b.Minute) > 4) continue;

                    candidates.Add(ResolveFault(a, b));
                }
            }

            if (candidates.Count == 0) return null; // no qualifying pair - no crash today

            // A single weighted draw over every candidate, same shape as DayPlanner.ChooseErrand's
            // own weighted pick: sum the table's weights, roll into the sum, walk the list
            // subtracting until the roll lands inside one candidate's share.
            int total = 0;
            foreach (var c in candidates) total += c.Weight;

            int roll = rng.NextInt(total);
            Candidate chosen = candidates[0];
            foreach (var c in candidates)
            {
                roll -= c.Weight;
                if (roll < 0) { chosen = c; break; }
            }

            // ---- Rule 3: the scene. From the at-fault driver's own doorstep for the block that
            // put them on the road, walked outward ring by ring to the first road tile.
            var originPlace = world.GetPlace(chosen.Origin);
            var doorstep = Locality.AnchorOf(originPlace);
            var scene = FindRoadOutward(world, doorstep);
            if (!scene.IsValid) return null; // no road within reach - no crash today, honestly

            // ---- Rule 4: severity.
            bool injury = rng.Chance(1f / 6f);

            // ---- Rule 5: the verdict. The Ticket roll is drawn unconditionally, before the DUI
            // test that might make it irrelevant - the same discipline DayPlanner's own
            // AddObligation and church-attendance draws use, so that a day which happens to earn
            // a DUI does not consume one fewer draw than a day that does not and shift every
            // draw after it.
            bool dui = false;
            var atFaultPlan = plans[chosen.AtFault.Value];
            foreach (var block in atFaultPlan.Blocks)
            {
                if (block.What != Activity.AtThePub) continue;
                int since = chosen.Minute - block.EndMinute;
                if (since >= 0 && since <= 180) { dui = true; break; }
            }

            float ticketRoll = rng.NextFloat();
            CrashVerdict verdict = dui ? CrashVerdict.Dui
                                 : ticketRoll < 0.6f ? CrashVerdict.Ticket
                                 : CrashVerdict.LetGo;

            // ---- Rule 6: tone and shape, two draws over the non-Unnoticed values - what a
            // witness may degrade FROM. Unnoticed (0) is deliberately excluded: the plan records
            // what the car actually was, and it is Degradation's job later to lose detail from
            // it, not this method's job to start blank.
            var tone = (CarTone)(1 + rng.NextInt(3));
            var shape = (CarShape)(1 + rng.NextInt(3));

            return new CrashPlan(chosen.Minute, scene, chosen.AtFault, chosen.Other,
                                 injury, verdict, tone, shape);
        }

        /// <summary>
        /// Resolves one candidate pair to who is at fault, where the drive started, and what
        /// weight Rule 2's table gives its minute.
        ///
        /// Default is id order - the LATER citizen id is AtFault, the EARLIER is Other - flipped
        /// only when exactly one side of the pair is a pub-close drive: drink outranks id order,
        /// per Rule 2's own words. When both or neither side drove from the pub, id order stands.
        /// </summary>
        private static Candidate ResolveFault(DrivingEvent a, DrivingEvent b)
        {
            CitizenId atFault, other;
            int minute; PlaceId origin;

            if (a.IsPubDrive != b.IsPubDrive)
            {
                var drunk = a.IsPubDrive ? a : b;
                var sober = a.IsPubDrive ? b : a;
                atFault = drunk.Who; other = sober.Who; minute = drunk.Minute; origin = drunk.Where;
            }
            else if (a.Who.Value > b.Who.Value)
            {
                atFault = a.Who; other = b.Who; minute = a.Minute; origin = a.Where;
            }
            else
            {
                atFault = b.Who; other = a.Who; minute = b.Minute; origin = b.Where;
            }

            return new Candidate(atFault, other, minute, origin, WeightFor(minute));
        }

        /// <summary>Rule 2's fixed weighting table: the morning and evening peaks, then closing
        /// time heavier still, everything else flat.</summary>
        private static int WeightFor(int minute)
        {
            if (minute >= 6 * 60 && minute < 8 * 60) return 3;          // 06:00-08:00
            if (minute >= 16 * 60 + 30 && minute < 18 * 60) return 3;   // 16:30-18:00
            if (minute >= 20 * 60) return 4;                            // 20:00 on
            return 1;
        }

        /// <summary>
        /// The per-citizen "did they drive to the pub" coin, keyed on WHO rather than on how many
        /// citizens the scan happened to consider before reaching them.
        ///
        /// THIS IS <see cref="Rolls"/>, NOT A SUBSTREAM, and deliberately so. A
        /// <see cref="Xoshiro256ss"/> stream - the day stream this method's caller already
        /// carries, or a fresh Substream(seed, "crash-pub") - is consumed in whatever order the
        /// citizen scan visits people, and Rolls' own docstring names exactly this failure: "a
        /// shared mutable stream consumed in agent-index order gives NO determinism guarantee
        /// worth having... whether agent 5 crosses a doorway this tick decides what number agent
        /// 6 gets." Adding a citizen to the town - or a fixture gaining one more Elder with a
        /// pub habit - would shift every draw the loop reaches afterward, flipping coins that
        /// have nothing to do with the new citizen. <see cref="Rolls.Chance"/> takes the stream
        /// position away entirely: it is a pure function of (seed, purpose, subject, tick, salt),
        /// keyed here on <see cref="Citizen.Key"/> - the identity that survives the population
        /// changing shape, per Citizen.Key's own doc comment - and on the day. Two citizens never
        /// share a subject, so this citizen's coin is the same whichever slot they occupy and
        /// whoever else is or is not in the town that day.
        /// </summary>
        private static bool DroveToThePub(ulong seed, Citizen who, int day) =>
            Rolls.Chance(seed, PubCoinPurpose, who.Key.Value, day, 0, 0.5f);

        /// <summary>
        /// Rule 3's scene search: from <paramref name="from"/>, walk outward ring by ring -
        /// increasing Chebyshev radius, each ring traced in a fixed clockwise order starting at
        /// its own top-left corner - and return the first tile whose terrain is
        /// <see cref="Terrain.Road"/>. <see cref="Tile.None"/> if nothing qualifies within
        /// <see cref="SceneReach"/> tiles, which PlanFor treats as an honest no-crash day rather
        /// than forcing a scene onto a tile that was never a road.
        ///
        /// Same idiom as <see cref="Driveways"/>'s own outward walks (see its Standing and
        /// ToRoad), but a full ring rather than a single directional ray - a doorstep can have
        /// its nearest road in any direction, where a driveway's search already knows which way
        /// the house faces.
        /// </summary>
        private static Tile FindRoadOutward(WorldModel world, Tile from)
        {
            if (!from.IsValid) return Tile.None;
            if (world.Grid.InBounds(from) && world.Grid.TerrainAt(from) == Terrain.Road) return from;

            for (int r = 1; r <= SceneReach; r++)
            {
                int x0 = from.X - r, x1 = from.X + r;
                int y0 = from.Y - r, y1 = from.Y + r;

                // Top edge, left to right; right edge, top+1 to bottom; bottom edge, right-1 to
                // left; left edge, bottom-1 to top+1 - one full clockwise pass with no tile
                // repeated, so the visiting order is fixed for a seed the way Driveways' own
                // stepped search is.
                for (int x = x0; x <= x1; x++)
                    if (IsRoad(world, x, y0)) return new Tile(x, y0);
                for (int y = y0 + 1; y <= y1; y++)
                    if (IsRoad(world, x1, y)) return new Tile(x1, y);
                for (int x = x1 - 1; x >= x0; x--)
                    if (IsRoad(world, x, y1)) return new Tile(x, y1);
                for (int y = y1 - 1; y >= y0 + 1; y--)
                    if (IsRoad(world, x0, y)) return new Tile(x0, y);
            }

            return Tile.None;
        }

        private static bool IsRoad(WorldModel world, int x, int y) =>
            world.Grid.InBounds(x, y) && world.Grid.TerrainAt(x, y) == Terrain.Road;

        /// <summary>
        /// <see cref="DayPlanner"/>'s own stream-seeding arithmetic, replicated rather than
        /// shared because that method's Mix is private. 0x0C0111DE is this file's own salt in
        /// the (seed, a, b) slot DayPlanner.Plan fills with a citizen id - distinct from every
        /// other Mix call in the codebase, so this stream never collides with anyone else's.
        /// </summary>
        private static ulong Mix(ulong seed, ulong a, ulong b)
        {
            ulong h = seed ^ 0x9E3779B97F4A7C15UL;
            h = (h ^ a) * 0xBF58476D1CE4E5B9UL;
            h = (h ^ (h >> 27) ^ b) * 0x94D049BB133111EBUL;
            return h ^ (h >> 31);
        }
    }
}
