using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.People
{
    public enum LifeStage : byte
    {
        Child = 0,      // at school
        Adult = 1,
        Elder = 2,      // retired
    }

    public enum Occupation : byte
    {
        None = 0,       // too young, retired, or works away
        MillHand,
        Farmer,
        FarmHand,
        Teacher,
        Shopkeeper,
        Postmaster,
        Publican,
        Mechanic,
        Doctor,
        Nurse,
        Verger,
        Caretaker,
    }

    /// <summary>
    /// Trades, by the name Content/kinds.txt gives them.
    ///
    /// The table names a place's roles in words rather than in enum members because
    /// Noir.Core.World cannot see this enum, and should not: what a barber is called is content.
    /// A word the enum already has keeps its value, so nothing that switches on Occupation moves;
    /// a word it has never heard of is given the next value along, which is what lets an amenity
    /// authored in content be staffed by somebody with a real trade instead of by nobody.
    ///
    /// Names are interned once, in kind order, before any job is handed out — see
    /// PopulationGenerator.Generate. Interning them as they came up would make a new trade's
    /// number depend on the order the shuffle happened to reach its workplaces, and a number that
    /// moves between runs is a number that cannot be written down.
    /// </summary>
    public static class Occupations
    {
        private static readonly Dictionary<string, Occupation> ByName =
            new Dictionary<string, Occupation>();
        private static readonly List<string> ByValue = new List<string>();
        private static int _next;

        static Occupations()
        {
            foreach (Occupation job in Enum.GetValues(typeof(Occupation)))
            {
                while (ByValue.Count <= (int)job) ByValue.Add(null);
                ByValue[(int)job] = job.ToString();
                ByName[job.ToString().ToLowerInvariant()] = job;
                if ((int)job >= _next) _next = (int)job + 1;
            }
        }

        /// <summary>The trade called this, minting one if the enum has never heard of it.</summary>
        public static Occupation Of(string name)
        {
            if (string.IsNullOrEmpty(name)) return Occupation.None;

            string key = name.ToLowerInvariant();
            if (ByName.TryGetValue(key, out var known)) return known;

            if (_next > byte.MaxValue)
                throw new InvalidOperationException(
                    "there is no room left for another trade: Occupation is a byte");

            var minted = (Occupation)_next++;
            ByName[key] = minted;
            while (ByValue.Count <= (int)minted) ByValue.Add(null);
            ByValue[(int)minted] = name;
            return minted;
        }

        /// <summary>What to call this trade. The authored word for one the enum does not name.</summary>
        public static string NameOf(Occupation job)
        {
            int i = (int)job;
            return i >= 0 && i < ByValue.Count && ByValue[i] != null ? ByValue[i] : job.ToString();
        }
    }

    /// <summary>
    /// One person's identity: fixed for the life of the simulation. Everything that changes —
    /// where they are, what they are doing right now — lives in the simulation state, not here.
    ///
    /// Note what this type does NOT have: any score, rating, or measure of a person. There is
    /// deliberately nowhere to put "how isolated is she" or "how long until anyone notices".
    /// Those questions are answered by watching, or not at all.
    /// </summary>
    public sealed class Citizen
    {
        public readonly CitizenId Id;

        /// <summary>
        /// Who they are, as against <see cref="Id"/>, which is only where they are kept.
        ///
        /// Every roll the simulation makes about one person is keyed on this rather than on the
        /// array slot, so nobody's day changes because somebody else's did. It is also the only
        /// thing here fit to be written down: a save file, a case note or an observation that
        /// says "citizen 41" is saying something about an array.
        /// </summary>
        public readonly CitizenKey Key;

        /// <summary>Which of this household's people they are, in the order the house filled.
        /// The other half of <see cref="Key"/>, kept so it can be recomputed and checked.</summary>
        public readonly int BirthOrder;

        public readonly string Forename;
        public readonly string Surname;
        public readonly int Age;

        /// <summary>
        /// Which forename list they were drawn from.
        ///
        /// THE GENERATOR ALWAYS KNEW THIS AND THREW IT AWAY. `PopulationGenerator` decides it to
        /// pick between the male and female name tables and then kept nothing, so from that point
        /// on the only trace of it was the forename itself - which means anything downstream had
        /// to guess, and a rigged figure would have rendered half the Margarets as men.
        ///
        /// It is also one of the six things `PersonDescription` describes and has never once been
        /// able to fill in: a witness who saw somebody in the street saw a man or a woman before
        /// they saw anything else about them, and until now nothing in the running game could say
        /// which.
        ///
        /// Named for what it IS - which list the name came from - rather than for a claim about
        /// the person. What an observer reports is `ApparentSex`, and the two are deliberately
        /// different words.
        /// </summary>
        public readonly bool Male;
        public readonly LifeStage Stage;
        public readonly Occupation Job;

        public readonly HouseholdId Household;
        public readonly PlaceId Home;
        public readonly PlaceId Work;       // None if they do not work in the village

        /// <summary>Which shift, for places that run more than one. 0 = only/early, 1 = late.</summary>
        public readonly byte Shift;

        // ---- the small variations that stop everyone moving on the same tick ----

        /// <summary>-15..+15 minutes. Some people are always early; some are always late.</summary>
        public readonly sbyte Punctuality;

        /// <summary>0..255, scales walking pace by roughly ±12%.</summary>
        public readonly byte Pace;

        /// <summary>0..255. How much discretionary time is spent around other people.</summary>
        public readonly byte Sociability;

        /// <summary>Indices into the particulars table — the true, useless, human details.</summary>
        public readonly int[] Particulars;

        /// <summary>
        /// The habits among those particulars that can actually be SEEN, folded together.
        ///
        /// Derived at generation from the clauses this person drew, not stored in the content and
        /// not chosen separately - so a villager who carries something is carrying it because of
        /// a sentence somebody wrote about them, and the two can never disagree.
        /// </summary>
        public readonly Beat Beats;

        public Citizen(CitizenId id, string forename, string surname, int age, LifeStage stage,
                       Occupation job, HouseholdId household, PlaceId home, PlaceId work, byte shift,
                       sbyte punctuality, byte pace, byte sociability, int[] particulars,
                       Beat beats = Beat.None, bool? male = null)
            : this(id, CitizenKey.None, 0, forename, surname, age, stage, job, household, home,
                   work, shift, punctuality, pace, sociability, particulars, beats, male) { }

        public Citizen(CitizenId id, CitizenKey key, int birthOrder,
                       string forename, string surname, int age, LifeStage stage,
                       Occupation job, HouseholdId household, PlaceId home, PlaceId work, byte shift,
                       sbyte punctuality, byte pace, byte sociability, int[] particulars,
                       Beat beats = Beat.None, bool? male = null)
        {
            // A citizen built without one — a hand-made test fixture — still needs a key that is
            // theirs alone, or every roll about them would be a roll about everybody.
            Key = key.IsValid ? key : KeyFor(Keys.Combine((ulong)household.Value + 1UL, 0x51ED), id.Value);
            BirthOrder = birthOrder;
            Id = id;
            Forename = forename;
            Surname = surname;
            Age = age;
            Stage = stage;
            Job = job;
            Household = household;
            Home = home;
            Work = work;
            Shift = shift;
            Punctuality = punctuality;
            Pace = pace;
            Sociability = sociability;
            Particulars = particulars ?? Array.Empty<int>();
            Beats = beats;

            // A hand-made fixture that does not say gets one off its own key rather than a
            // constant, so a test village is not a village of men.
            Male = male ?? ((Keys.Combine(Key.Value, 0xB1A5UL) & 1UL) != 0UL);
        }

        /// <summary>A person's name, in the sense a generator means it: the home they were born
        /// into, and their place in it.</summary>
        public static CitizenKey KeyFor(ulong householdKey, int birthOrder)
        {
            ulong v = Keys.Combine(householdKey, (ulong)birthOrder + 0x9E37UL);
            return new CitizenKey(v == 0 ? 1UL : v);   // zero is reserved for nobody
        }

        public string FullName => Forename + " " + Surname;

        public bool IsChild => Stage == LifeStage.Child;
        public bool Works => Work.IsValid;

        /// <summary>Walking speed in tiles per second, before terrain.</summary>
        public float BaseSpeed
        {
            get
            {
                float baseline = Stage == LifeStage.Child ? 1.15f
                               : Stage == LifeStage.Elder ? 0.95f
                               : 1.35f;
                float variation = 0.88f + (Pace / 255f) * 0.24f;
                return baseline * variation;
            }
        }

        public override string ToString() => $"{FullName} ({Age})";
    }
}
