using System;
using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.People
{
    /// <summary>
    /// Fills the village with people. A pure function of (world, content, seed): the same seed
    /// always produces the same hundred lives, which is what makes "why did he do that" a
    /// question with an answer.
    ///
    /// Households first, then jobs. That order matters — a household is the unit that produces
    /// the school run and the empty house at nine, and those have to fall out of who lives with
    /// whom rather than being scripted afterwards.
    /// </summary>
    public static class PopulationGenerator
    {
        private struct ShapeWeight
        {
            public HouseholdShape Shape;
            public int Weight;
            public ShapeWeight(HouseholdShape s, int w) { Shape = s; Weight = w; }
        }

        // Tuned so ~44 dwellings hold ~100 people: an average household of about 2.2.
        // Rural 1979 skews to small households — grown children have left, and a lot of
        // cottages hold one widow or one couple.
        private static readonly ShapeWeight[] Shapes =
        {
            new ShapeWeight(HouseholdShape.Solitary,     28),
            new ShapeWeight(HouseholdShape.Couple,       30),
            new ShapeWeight(HouseholdShape.Family,       20),
            new ShapeWeight(HouseholdShape.SingleParent,  7),
            new ShapeWeight(HouseholdShape.Sharers,       6),
            new ShapeWeight(HouseholdShape.Extended,      9),
        };

        public static Population Generate(WorldModel world, NameTable names,
                                          ParticularsTable particulars, ulong seed)
        {
            var rng = Xoshiro256ss.Substream(seed, "population");
            var kinds = PlaceKindTable.Current;

            // Every trade the table names, given its number now, in kind order. Doing it here
            // rather than as each job is handed out is what keeps a content-authored trade's
            // number the same from one run to the next: the shuffle below decides who gets
            // which job, and it must not also decide what the job is called.
            for (int k = 0; k < kinds.Count; k++)
                foreach (string role in kinds.Row((PlaceKind)k).Roles) Occupations.Of(role);

            var citizens = new List<Citizen>();
            var households = new List<Household>();

            var dwellings = world.PlacesOfKind(PlaceKind.Dwelling);
            var usedSurnames = new HashSet<string>();

            // ---- 1. households, one per home ----
            //
            // A building may hold several homes: a cottage has one, a terrace four, a block of
            // flats a dozen. Iterating units rather than buildings is what makes neighbours a
            // real thing - people who share a roof and a front path, and who notice each other
            // in a way that detached householders never do.
            for (int d = 0; d < dwellings.Count; d++)
            {
                var dwelling = dwellings[d];
                var building = world.GetPlace(dwelling);
                int units = building?.Units ?? 1;

                for (int u = 0; u < units; u++)
                {
                    var shape = PickShape(rng);
                    string surname = PickSurname(names, usedSurnames, rng);

                    var householdId = new HouseholdId(households.Count);
                    ulong householdKey = Household.KeyFor(building?.Key ?? 0UL, u);
                    var members = new List<CitizenId>();

                    BuildHousehold(shape, surname, householdId, householdKey, dwelling, names,
                                   particulars, rng, citizens, members);

                    // The catchment is pure geometry, so it takes no draws at all — which is
                    // why it can be settled here without shifting a single number the rest of
                    // the village depends on, and why the table is free to decide how many
                    // institutions a household belongs to.
                    var front = Locality.AnchorOf(building);
                    var catchmentKinds = kinds.CatchmentKinds;
                    var catchments = new PlaceId[catchmentKinds.Count];
                    for (int c = 0; c < catchmentKinds.Count; c++)
                        catchments[c] = Locality.NearestOfKind(world, front, catchmentKinds[c]);

                    var kindsForHousehold = new PlaceKind[catchmentKinds.Count];
                    for (int c = 0; c < catchmentKinds.Count; c++)
                        kindsForHousehold[c] = catchmentKinds[c];

                    households.Add(new Household(householdId, dwelling, surname, shape,
                                                 members.ToArray(), u,
                                                 kindsForHousehold, catchments,
                                                 householdKey));
                }
            }

            // ---- 2. jobs ----
            AssignJobs(world, citizens, rng);

            return new Population(citizens.ToArray(), households.ToArray());
        }

        private static void BuildHousehold(HouseholdShape shape, string surname,
                                           HouseholdId householdId, ulong householdKey,
                                           PlaceId dwelling,
                                           NameTable names, ParticularsTable particulars,
                                           IRng rng, List<Citizen> citizens, List<CitizenId> members)
        {
            switch (shape)
            {
                case HouseholdShape.Solitary:
                {
                    // Solitary households skew older: this is a village, and people stay.
                    var stage = rng.Chance(0.45f) ? LifeStage.Elder : LifeStage.Adult;
                    Add(citizens, members, householdId, householdKey, dwelling, surname, stage,
                        rng.Chance(0.5f), names, particulars, rng);
                    break;
                }

                case HouseholdShape.Couple:
                {
                    var stage = rng.Chance(0.3f) ? LifeStage.Elder : LifeStage.Adult;
                    Add(citizens, members, householdId, householdKey, dwelling, surname, stage, true, names, particulars, rng);
                    Add(citizens, members, householdId, householdKey, dwelling, surname, stage, false, names, particulars, rng);
                    break;
                }

                case HouseholdShape.Family:
                {
                    Add(citizens, members, householdId, householdKey, dwelling, surname, LifeStage.Adult, true, names, particulars, rng);
                    Add(citizens, members, householdId, householdKey, dwelling, surname, LifeStage.Adult, false, names, particulars, rng);
                    int kids = 1 + rng.NextInt(2);
                    for (int k = 0; k < kids; k++)
                        Add(citizens, members, householdId, householdKey, dwelling, surname, LifeStage.Child,
                            rng.Chance(0.5f), names, particulars, rng);
                    break;
                }

                case HouseholdShape.SingleParent:
                {
                    Add(citizens, members, householdId, householdKey, dwelling, surname, LifeStage.Adult,
                        rng.Chance(0.25f), names, particulars, rng);
                    int kids = 1 + rng.NextInt(2);
                    for (int k = 0; k < kids; k++)
                        Add(citizens, members, householdId, householdKey, dwelling, surname, LifeStage.Child,
                            rng.Chance(0.5f), names, particulars, rng);
                    break;
                }

                case HouseholdShape.Sharers:
                {
                    // Unrelated — so they do NOT share a surname. Lodgers, farm men, mill men.
                    int n = 2 + rng.NextInt(2);
                    for (int i = 0; i < n; i++)
                        Add(citizens, members, householdId, householdKey, dwelling,
                            names.Surnames[rng.NextInt(names.Surnames.Count)],
                            LifeStage.Adult, rng.Chance(0.7f), names, particulars, rng);
                    break;
                }

                case HouseholdShape.Extended:
                {
                    Add(citizens, members, householdId, householdKey, dwelling, surname, LifeStage.Adult, true, names, particulars, rng);
                    Add(citizens, members, householdId, householdKey, dwelling, surname, LifeStage.Adult, false, names, particulars, rng);
                    Add(citizens, members, householdId, householdKey, dwelling, surname, LifeStage.Elder,
                        rng.Chance(0.6f), names, particulars, rng);
                    if (rng.Chance(0.5f))
                        Add(citizens, members, householdId, householdKey, dwelling, surname, LifeStage.Child,
                            rng.Chance(0.5f), names, particulars, rng);
                    break;
                }
            }
        }

        private static void Add(List<Citizen> citizens, List<CitizenId> members,
                                HouseholdId household, ulong householdKey, PlaceId dwelling,
                                string surname, LifeStage stage, bool male,
                                NameTable names, ParticularsTable particulars, IRng rng)
        {
            var id = new CitizenId(citizens.Count);

            // Their place in the house, in the order it filled: parents, then children, then
            // whoever else came. It is fixed at generation and never renumbered, which is what
            // makes the key it feeds outlive the arrays.
            int birthOrder = members.Count;
            var key = Citizen.KeyFor(householdKey, birthOrder);

            string forename = male
                ? names.Male[rng.NextInt(names.Male.Count)]
                : names.Female[rng.NextInt(names.Female.Count)];

            int age;
            switch (stage)
            {
                case LifeStage.Child: age = 5 + rng.NextInt(11); break;   // 5..15
                case LifeStage.Elder: age = 65 + rng.NextInt(24); break;  // 65..88
                default: age = 21 + rng.NextInt(44); break;               // 21..64
            }

            // Two or three particulars each, distinct.
            int wanted = 2 + (rng.Chance(0.4f) ? 1 : 0);
            var chosen = new List<int>(wanted);
            int guard = 0;
            while (chosen.Count < wanted && guard++ < 32)
            {
                int p = rng.NextInt(particulars.Count);
                if (!chosen.Contains(p)) chosen.Add(p);
            }

            // Whatever the clauses they drew make them DO. Folded here rather than rolled for,
            // so a habit is always the consequence of something written about them.
            var beats = Beat.None;
            foreach (int p in chosen) beats |= particulars.BeatAt(p);

            citizens.Add(new Citizen(
                id, key, birthOrder, forename, surname, age, stage,
                Occupation.None, household, dwelling, PlaceId.None, 0,
                punctuality: (sbyte)(rng.NextInt(-15, 16)),
                pace: (byte)rng.NextInt(256),
                sociability: (byte)rng.NextInt(256),
                particulars: chosen.ToArray(),
                beats: beats));

            members.Add(id);
        }

        /// <summary>
        /// One job in five is handed out with no regard whatever to where the person lives.
        ///
        /// The temptation is to set this to zero, and it is the wrong instinct. A settlement
        /// where everybody works next door has no morning flow across it, no reason for a bus
        /// route or a lift to work, and no occasion on which somebody from the north end is
        /// ever seen at the south end. The cross-district commute is what carries people past
        /// each other, and half the village's chance encounters are paid for by it. So a
        /// deliberate minority walk a long way to work, and they are a feature.
        ///
        /// Kept low enough that it is a minority: at a fifth the far commutes read as the mill
        /// drawing hands from the whole parish, which is what a mill does.
        /// </summary>
        private const float LongCommuteShare = 0.20f;

        /// <summary>
        /// Hand out the village's jobs, nearest first. Slots come from the world, so employment
        /// can never exceed what the village can actually support — the mill has 22 places and
        /// that is simply how many mill hands there are.
        ///
        /// Candidate-first rather than workplace-first: each person in turn takes the nearest
        /// job still going. Walking the world's places in declaration order instead — which is
        /// what this did — meant the job someone got depended on where their employer happened
        /// to appear in village.txt, and since nothing in the loop looked at distance at all,
        /// home and work were independent draws across the whole map. Two of a person's three
        /// or four daily journeys are the commute, so that one omission put most of the
        /// village's traffic on the diagonal.
        /// </summary>
        private static void AssignJobs(WorldModel world, List<Citizen> citizens, IRng rng)
        {
            // Working-age adults, shuffled so the same people are not always employed.
            var candidates = new List<int>();
            for (int i = 0; i < citizens.Count; i++)
                if (citizens[i].Stage == LifeStage.Adult) candidates.Add(i);

            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            var workplaces = new List<Place>();
            foreach (var place in world.AllPlaces)
                if (place.JobSlots > 0) workplaces.Add(place);
            if (workplaces.Count == 0) return;

            var doors = new Tile[workplaces.Count];
            var filled = new int[workplaces.Count];
            int vacancies = 0;
            for (int i = 0; i < workplaces.Count; i++)
            {
                doors[i] = Locality.AnchorOf(workplaces[i]);
                vacancies += workplaces[i].JobSlots;
            }

            var far = new List<int>(workplaces.Count);

            for (int n = 0; n < candidates.Count && vacancies > 0; n++)
            {
                var old = citizens[candidates[n]];
                var home = Locality.AnchorOf(world.GetPlace(old.Home));

                // Both draws happen for everybody, whichever branch is taken. Drawing only on
                // the branch that needs it would make the share above a number that reshuffles
                // the whole village every time it is tuned, which is exactly the sort of thing
                // that makes a seed stop meaning anything.
                bool commutes = rng.Chance(LongCommuteShare);
                float spin = rng.NextFloat();

                int pick = commutes
                    ? PickFarVacancy(workplaces, filled, doors, home, spin, far)
                    : PickNearestVacancy(workplaces, filled, doors, home);
                if (pick < 0) break;

                var place = workplaces[pick];
                int slot = filled[pick];
                var job = OccupationFor(place.Kind, slot);

                // Somewhere that runs two shifts puts half its hands on nights.
                byte shift = 0;
                if (PlaceKindTable.Current.Row(place.Kind).SplitShifts && place.Hours.Count > 1)
                    shift = (byte)(slot % 2);

                citizens[candidates[n]] = new Citizen(
                    old.Id, old.Key, old.BirthOrder, old.Forename, old.Surname, old.Age, old.Stage,
                    job, old.Household, old.Home, place.Id, shift,
                    old.Punctuality, old.Pace, old.Sociability, old.Particulars, old.Beats);

                filled[pick]++;
                vacancies--;
            }
        }

        /// <summary>Ties go to the earlier workplace, so the result never depends on anything
        /// but the layout.</summary>
        private static int PickNearestVacancy(List<Place> workplaces, int[] filled, Tile[] doors,
                                              Tile home)
        {
            int best = -1, bestDistance = 0;
            for (int i = 0; i < workplaces.Count; i++)
            {
                if (filled[i] >= workplaces[i].JobSlots) continue;

                // `best < 0` first, so that a vacancy is always returned when one exists. A
                // plain "closer than the best so far" would return nothing at all if every
                // distance came back unmeasurable, and the rest of the village would then go
                // unemployed without anything having reported a fault.
                int d = Locality.Distance(home, doors[i]);
                if (best < 0 || d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Somewhere in the farther half of what is still going. Uniform over every vacancy
        /// would mostly land nearby anyway and the long commutes would never actually appear;
        /// the point of this branch is that they do.
        /// </summary>
        private static int PickFarVacancy(List<Place> workplaces, int[] filled, Tile[] doors,
                                          Tile home, float spin, List<int> scratch)
        {
            scratch.Clear();
            for (int i = 0; i < workplaces.Count; i++)
            {
                if (filled[i] >= workplaces[i].JobSlots) continue;

                // Insertion sort by distance: at most a dozen workplaces, and it keeps ties in
                // declaration order.
                int d = Locality.Distance(home, doors[i]);
                int at = scratch.Count;
                scratch.Add(i);
                while (at > 0 && Locality.Distance(home, doors[scratch[at - 1]]) > d)
                {
                    scratch[at] = scratch[at - 1];
                    scratch[at - 1] = i;
                    at--;
                }
            }

            if (scratch.Count == 0) return -1;

            int half = scratch.Count / 2;
            int among = scratch.Count - half;
            int chosen = half + (int)(spin * among);
            if (chosen >= scratch.Count) chosen = scratch.Count - 1;
            return scratch[chosen];
        }

        /// <summary>
        /// What the person in this slot is. Read off the kind's `roles` column, where the last
        /// name covers every slot after it — so a farm is one farmer and then hands, and a
        /// surgery is one doctor and then nurses.
        ///
        /// The old switch ended `default: return Occupation.None`, which meant a new kind of
        /// place was staffed by nobody: no windows lit by occupancy, nothing in the density
        /// curve, and no complaint from anywhere. The table refuses to load a kind that names a
        /// trade and employs no one, or employs people and names no trade for them.
        /// </summary>
        private static Occupation OccupationFor(PlaceKind kind, int slot) =>
            Occupations.Of(PlaceKindTable.Current.Row(kind).RoleAt(slot));

        private static HouseholdShape PickShape(IRng rng)
        {
            int total = 0;
            foreach (var s in Shapes) total += s.Weight;
            int roll = rng.NextInt(total);
            foreach (var s in Shapes)
            {
                roll -= s.Weight;
                if (roll < 0) return s.Shape;
            }
            return HouseholdShape.Couple;
        }

        private static string PickSurname(NameTable names, HashSet<string> used, IRng rng)
        {
            // Prefer an unused surname, but a village repeats names and that is realistic.
            for (int attempt = 0; attempt < 12; attempt++)
            {
                string s = names.Surnames[rng.NextInt(names.Surnames.Count)];
                if (used.Add(s)) return s;
            }
            return names.Surnames[rng.NextInt(names.Surnames.Count)];
        }
    }

    /// <summary>
    /// How far apart things are, in the only terms the People layer has.
    ///
    /// This is straight-line tile distance between the spots people actually walk to, not a
    /// route — the People layer cannot see the pathfinder and should not want to. It is used
    /// for choosing BETWEEN places, where the ordering is what matters and a wall between two
    /// of them changes very little; the real cost of the walk is the simulation's business.
    ///
    /// Integer throughout, deliberately. Everything a seed has to reproduce is decided with
    /// these numbers, and integers have no rounding to diverge on between one runtime and the
    /// next.
    /// </summary>
    internal static class Locality
    {
        /// <summary>
        /// The tile somebody heads for. Open ground — the green, the allotments, the churchyard
        /// — has no door at all, so the middle of it stands in for one.
        /// </summary>
        public static Tile AnchorOf(Place place)
        {
            if (place == null) return Tile.None;
            return place.Door.IsValid ? place.Door : place.Bounds.Centre;
        }

        /// <summary>Straight-line distance in whole tiles. int.MaxValue if either end is nowhere,
        /// so an unplaceable candidate sorts last instead of sorting first.</summary>
        public static int Distance(Tile a, Tile b)
        {
            if (!a.IsValid || !b.IsValid) return int.MaxValue;
            return Sqrt(Tile.DistanceSquared(a, b));
        }

        public static PlaceId NearestOfKind(WorldModel world, Tile from, PlaceKind kind)
        {
            var ids = world.PlacesOfKind(kind);
            var best = PlaceId.None;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < ids.Count; i++)
            {
                int d = Distance(from, AnchorOf(world.GetPlace(ids[i])));
                if (d < bestDistance) { bestDistance = d; best = ids[i]; }
            }
            return best;
        }

        /// <summary>
        /// The distance at which a place is picked half as often as one on the doorstep.
        ///
        /// Fifty metres, which on a village street is four or five houses along. Small enough
        /// that the shop at the end of the lane wins most of the time; large enough that the
        /// one on the far side of the green is still a real possibility, which is the whole
        /// point — see <see cref="ChooseNearby"/>.
        /// </summary>
        private const int HalfDistance = 50;

        /// <summary>Fixed-point weights, so the choice is made in integers.</summary>
        private const int WeightScale = 256;

        private static int WeightAt(int distance) =>
            distance >= int.MaxValue ? 0 : (HalfDistance * WeightScale) / (HalfDistance + distance);

        /// <summary>
        /// Pick one of the <paramref name="k"/> nearest places that are open for the whole
        /// visit, weighted towards the near ones by 1/(1 + d/50).
        ///
        /// NOT strict nearest, and that is the important part. Strict nearest roughly halves
        /// the time people spend on the street, and the street is where they see each other —
        /// it would buy a performance number by quietly deleting the chance encounters that
        /// this whole village exists to produce. Weighted keeps people out walking while
        /// stopping the twenty-minute trek to a shop chosen by dice.
        ///
        /// <paramref name="roll"/> is a single draw in [0,1) taken by the caller BEFORE any of
        /// the filtering below, so that a place being shut never changes how many numbers the
        /// day consumes.
        ///
        /// Worth knowing before anyone tunes <see cref="HalfDistance"/>: this weighting falls
        /// off like 1/d, so once every candidate is well beyond fifty tiles the odds between
        /// two of them settle at the ratio of their distances and the constant stops mattering.
        /// Measured on a town four times Ashcombe's area, dropping it from 50 to 12 moved the
        /// mean errand by four tiles. If errands ever need to be genuinely local at town scale
        /// the answer is a shorter shortlist or a steeper curve, not a smaller number here.
        /// </summary>
        public static PlaceId ChooseNearby(WorldModel world, Tile from, IReadOnlyList<PlaceId> ids,
                                           int dayOfWeek, int arrive, int minutes, int k, float roll)
        {
            if (world == null || ids == null || ids.Count == 0 || k <= 0) return PlaceId.None;

            var shortlist = new PlaceId[k];
            var distances = new int[k];
            int have = 0;

            for (int i = 0; i < ids.Count; i++)
            {
                var place = world.GetPlace(ids[i]);
                if (place == null) continue;

                // Open when they get there AND still open when they leave. Checking the last
                // minute they are present is enough: opening windows are contiguous, so if both
                // ends are inside one window the middle is too.
                if (!place.IsOpenAt(arrive % 1440, dayOfWeek)) continue;
                if (!place.IsOpenAt((arrive + minutes - 1) % 1440, dayOfWeek)) continue;

                int d = Distance(from, AnchorOf(place));
                if (d == int.MaxValue) continue;
                if (have == k && d >= distances[k - 1]) continue;

                if (have < k) have++;
                int at = have - 1;
                while (at > 0 && distances[at - 1] > d)
                {
                    distances[at] = distances[at - 1];
                    shortlist[at] = shortlist[at - 1];
                    at--;
                }
                distances[at] = d;
                shortlist[at] = ids[i];
            }

            if (have == 0) return PlaceId.None;

            int total = 0;
            for (int i = 0; i < have; i++) total += WeightAt(distances[i]);
            if (total <= 0) return shortlist[0];

            int target = (int)(roll * total);
            if (target >= total) target = total - 1;
            if (target < 0) target = 0;

            for (int i = 0; i < have; i++)
            {
                target -= WeightAt(distances[i]);
                if (target < 0) return shortlist[i];
            }
            return shortlist[0];
        }

        /// <summary>Integer square root by Newton's method. Core owns this rather than calling
        /// System.Math, whose floating-point results are not guaranteed identical across
        /// runtimes — and a village that differs by one tile between two machines is a village
        /// whose seed has stopped meaning anything.</summary>
        private static int Sqrt(int value)
        {
            if (value <= 0) return 0;
            int x = value;
            int next = (x + 1) / 2;
            while (next < x)
            {
                x = next;
                next = (x + value / x) / 2;
            }
            return x;
        }
    }
}
