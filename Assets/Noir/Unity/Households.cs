using System.Collections.Generic;
using Noir.Core.People;

namespace Noir.Unity
{
    /// <summary>
    /// Who lived on a parcel in 1991, generated rather than looked up.
    ///
    /// WHY THIS IS NOT REAL PEOPLE. Vermilion County publishes the owner of every one of these
    /// lots by name, and the same record flags which owners are over 65 and which live alone.
    /// Rossville is a real town of about 1,300 real people and this is a game about somebody
    /// murdering the residents of it, so real names at real addresses are the one thing this
    /// project will not carry - see the header of Content/parcel-county.txt. The county's own
    /// 1991 rolls are not reachable in any case: its portal starts at 2007 and the older years
    /// exist on paper at the courthouse.
    ///
    /// So the SHAPE of each household is taken from the real record - is it owner-occupied or
    /// rented, is there an over-65 exemption on it, how much dwelling is assessed there - and
    /// the PEOPLE are drawn from Content/names.txt. That gives a town whose demography is
    /// genuinely Rossville's while nobody in it is a real person.
    ///
    /// DETERMINISTIC. Everything is seeded off the parcel id, so the same lot has the same
    /// family every run, in the editor and in a build, without any of it being stored. An
    /// authored household in ParcelNotes always wins over what is generated here.
    /// </summary>
    public static class Households
    {
        public struct Person
        {
            public string Forename;
            public int Age;
            public bool IsChild;
        }

        public sealed class Household
        {
            public string Surname;
            public bool Rented;
            public List<Person> Members = new List<Person>();

            public int Adults
            {
                get { int n = 0; foreach (var p in Members) if (!p.IsChild) n++; return n; }
            }
            public int Kids => Members.Count - Adults;

            /// <summary>"the Hendersons" - how anybody in this town would refer to them.</summary>
            public string Family => Surname.EndsWith("s") ? "the " + Surname + "es"
                                                          : "the " + Surname + "s";
        }

        /// <summary>The year the game is set. Ages are worked out against this, not against
        /// whatever year the machine thinks it is.</summary>
        public const int Year = 1991;

        private static NameTable _names;
        private static Dictionary<int, Household> _cache;

        /// <summary>
        /// The household on a parcel, or null where nobody lives - a vacant lot, farm ground,
        /// the elevator, a church. Nothing is generated for a parcel the county does not assess
        /// a dwelling on, which is the truest "is anything built here" test the record offers.
        /// </summary>
        public static Household For(int parcelId)
        {
            if (parcelId < 0) return null;
            _cache ??= new Dictionary<int, Household>();
            if (_cache.TryGetValue(parcelId, out var got)) return got;

            var built = Build(parcelId);
            _cache[parcelId] = built;
            return built;
        }

        private static Household Build(int parcelId)
        {
            var county = CountyRecord.For(parcelId);
            if (county == null || !county.HasBuilding) return null;
            if (county.Zoning != ParcelNotes.Zoning.Residential) return null;

            if (_names == null)
            {
                try { _names = NameTable.Parse(ContentLoader.Read("names.txt")); }
                catch { return null; }
            }

            // Seeded off the parcel id alone, so the answer never depends on load order or on
            // anything the player has done.
            uint seed = Mix((uint)parcelId * 2654435761u + 12345u);

            var home = new Household
            {
                Surname = Pick(_names.Surnames, ref seed),
                Rented = county.Occupied == CountyRecord.Occupancy.Absentee,
            };

            // HOW BIG A HOUSE IS, from what the county assesses the dwelling at. Illinois
            // assesses at a third of market, so this is real money, and in this town it sorts
            // the two-bedroom bungalows from the farmhouses about as well as anything could.
            int market = county.MarketValue;
            int room = market < 45000 ? 0 : market < 95000 ? 1 : 2;

            if (county.Over65)
            {
                // The over-65 exemption is claimed here, so somebody in the house is past 65.
                // These are the households that are alone the most, which is a fact the story
                // eventually cares about - but it is a RATE here, attached to no real person.
                int age = 66 + (int)(Next(ref seed) % 21);
                bool man = Next(ref seed) % 2 == 0;
                home.Members.Add(Adult(age, man, ref seed));
                if (Next(ref seed) % 100 < 55)
                {
                    // A widow or widower alone is the other 45%, and this town has a lot of
                    // them. One in ten of the pairs is same-sex - two sisters who kept the
                    // family house is an ordinary thing at this age.
                    bool spouseMan = Next(ref seed) % 10 == 0 ? man : !man;
                    home.Members.Add(Adult(age - 4 + (int)(Next(ref seed) % 8), spouseMan, ref seed));
                }
                return home;
            }

            if (home.Rented)
            {
                // The tax bill goes somewhere other than the house. Renters here skew young and
                // the households are smaller - a couple starting out, or one parent and a kid.
                int head = 23 + (int)(Next(ref seed) % 22);
                bool tenantMan = Next(ref seed) % 2 == 0;
                home.Members.Add(Adult(head, tenantMan, ref seed));
                bool partner = Next(ref seed) % 100 < 45;
                if (partner)
                {
                    // Sharing the rent with somebody of the same sex is common at this age in a
                    // way it is not among owner-occupiers - a fifth of these are flatmates.
                    bool otherMan = Next(ref seed) % 5 == 0 ? tenantMan : !tenantMan;
                    home.Members.Add(Adult(head - 3 + (int)(Next(ref seed) % 7), otherMan, ref seed));
                }

                int kids = (int)(Next(ref seed) % 100) < (partner ? 55 : 40)
                    ? 1 + (int)(Next(ref seed) % (room == 0 ? 2 : 3)) : 0;
                AddKids(home, kids, ref seed);
                return home;
            }

            // Owner-occupied, under 65: the ordinary case, and most of the town.
            int parent = 28 + (int)(Next(ref seed) % 34);
            bool headMan = Next(ref seed) % 2 == 0;
            home.Members.Add(Adult(parent, headMan, ref seed));
            bool married = Next(ref seed) % 100 < 78;
            if (married)
                home.Members.Add(Adult(parent - 4 + (int)(Next(ref seed) % 9), !headMan, ref seed));

            // Nobody has children at 61. The gate is the PARENT's age, not the dice.
            int maxKids = room == 0 ? 2 : room == 1 ? 3 : 4;
            int count = parent > 55 || !married && Next(ref seed) % 100 < 45
                ? 0 : (int)(Next(ref seed) % (maxKids + 1));
            AddKids(home, count, ref seed);
            return home;
        }

        /// <summary>
        /// Children, aged so that the YOUNGEST adult in the house was at least 18 when each of
        /// them arrived - which is what stops the generator producing a 31-year-old with a
        /// 17-year-old son.
        ///
        /// Against the youngest and not the head, which was the bug: the partner is drawn within
        /// three years either side of the head, so seeding the gap off the head alone put a
        /// twenty-year-old in the house with a three-year-old, making her a mother at seventeen.
        /// </summary>
        private static void AddKids(Household home, int count, ref uint seed)
        {
            int youngest = int.MaxValue;
            foreach (var p in home.Members) if (!p.IsChild && p.Age < youngest) youngest = p.Age;
            if (youngest == int.MaxValue) return;

            int oldest = System.Math.Min(18, youngest - 18);
            if (oldest < 0) return;

            for (int i = 0; i < count; i++)
            {
                int age = oldest <= 0 ? 0 : (int)(Next(ref seed) % (uint)(oldest + 1));
                bool boy = Next(ref seed) % 2 == 0;
                home.Members.Add(new Person
                {
                    Forename = Pick(boy ? _names.MaleChild : _names.FemaleChild, ref seed),
                    Age = age,
                    IsChild = true,
                });
            }
        }

        /// <summary>
        /// One adult of a stated sex.
        ///
        /// THE SEX IS PASSED IN RATHER THAN ROLLED because the second adult in a household is
        /// the first one's spouse. Rolling independently made half of every married couple in
        /// the village same-sex, which is not what rural Illinois looked like in 1991 - the
        /// sample run had a Beverly living with a Joyce and a Dana with a Dorothy. Households
        /// that are genuinely two adults of the same sex - sisters, an elderly pair of brothers,
        /// two men sharing the rent - are worth having, but they are a deliberate minority and
        /// belong in their own branch rather than falling out of a coin toss.
        /// </summary>
        private static Person Adult(int age, bool man, ref uint seed) => new Person
        {
            Forename = Pick(man ? _names.Male : _names.Female, ref seed),
            Age = age < 18 ? 18 : age,
            IsChild = false,
        };

        private static string Pick(IReadOnlyList<string> from, ref uint seed) =>
            from.Count == 0 ? "" : from[(int)(Next(ref seed) % (uint)from.Count)];

        // xorshift32. Small, fast and - the only property that matters here - identical on every
        // machine, which System.Random is explicitly not promised to be.
        private static uint Next(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        private static uint Mix(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352du;
            x ^= x >> 15; x *= 0x846ca68bu;
            x ^= x >> 16;
            return x == 0 ? 0x9e3779b9u : x;   // xorshift dies on a zero state
        }
    }
}
