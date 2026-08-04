using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.People
{
    /// <summary>
    /// Everyone in the village, plus the lookups the simulation needs constantly:
    /// who lives here, who works there, who shares a roof with whom.
    /// </summary>
    public sealed class Population
    {
        private readonly Citizen[] _citizens;
        private readonly Household[] _households;
        private readonly Dictionary<int, List<CitizenId>> _byWorkplace;
        private readonly Dictionary<int, HouseholdId> _householdByDwelling;

        /// <summary>
        /// The way back from a name to a slot.
        ///
        /// Anything that outlived one run of the simulation — a save, a case note, an
        /// observation — holds a <see cref="CitizenKey"/>, because an array index would mean
        /// somebody else by the time it was read. This is where it becomes a person again, and
        /// it is allowed to fail: a key for somebody who is no longer in the village is a
        /// perfectly ordinary thing to be holding.
        /// </summary>
        private readonly Dictionary<ulong, CitizenId> _byKey;

        public Population(Citizen[] citizens, Household[] households)
        {
            _citizens = citizens ?? Array.Empty<Citizen>();
            _households = households ?? Array.Empty<Household>();

            _byKey = new Dictionary<ulong, CitizenId>(_citizens.Length);
            foreach (var c in _citizens)
                if (c.Key.IsValid) _byKey[c.Key.Value] = c.Id;

            _byWorkplace = new Dictionary<int, List<CitizenId>>();
            foreach (var c in _citizens)
            {
                if (!c.Work.IsValid) continue;
                if (!_byWorkplace.TryGetValue(c.Work.Value, out var list))
                    _byWorkplace[c.Work.Value] = list = new List<CitizenId>();
                list.Add(c.Id);
            }

            _householdByDwelling = new Dictionary<int, HouseholdId>();
            foreach (var h in _households)
                if (h.Dwelling.IsValid) _householdByDwelling[h.Dwelling.Value] = h.Id;
        }

        public int Count => _citizens.Length;
        public int HouseholdCount => _households.Length;

        public IReadOnlyList<Citizen> Citizens => _citizens;
        public IReadOnlyList<Household> Households => _households;

        public Citizen Get(CitizenId id) =>
            id.IsValid && id.Value < _citizens.Length ? _citizens[id.Value] : null;

        /// <summary>Which slot this person is in now, or None if they are not in the village.</summary>
        public CitizenId Find(CitizenKey key) =>
            key.IsValid && _byKey.TryGetValue(key.Value, out var id) ? id : CitizenId.None;

        public Citizen Get(CitizenKey key) => Get(Find(key));

        public Household GetHousehold(HouseholdId id) =>
            id.IsValid && id.Value < _households.Length ? _households[id.Value] : null;

        public Household HouseholdOf(Citizen c) => GetHousehold(c.Household);

        private static readonly List<CitizenId> Empty = new List<CitizenId>();

        public IReadOnlyList<CitizenId> WorkersAt(PlaceId place) =>
            place.IsValid && _byWorkplace.TryGetValue(place.Value, out var list) ? list : Empty;

        public HouseholdId HouseholdAt(PlaceId dwelling) =>
            dwelling.IsValid && _householdByDwelling.TryGetValue(dwelling.Value, out var h)
                ? h : HouseholdId.None;

        /// <summary>Everyone this person shares a roof with. The cheapest true social tie there is.</summary>
        public IReadOnlyList<CitizenId> HouseholdMembers(Citizen c)
        {
            var h = GetHousehold(c.Household);
            return h != null ? h.Members : (IReadOnlyList<CitizenId>)Empty;
        }

        /// <summary>
        /// How many are at this stage of life IN A GIVEN YEAR. The year is not optional and used
        /// not to exist: a stage is now derived from a birth year, so "how many children are
        /// there" is a question about a date and the count genuinely changes across the game.
        /// </summary>
        public int CountOfStage(LifeStage stage, int year)
        {
            int n = 0;
            foreach (var c in _citizens) if (c.StageIn(year) == stage) n++;
            return n;
        }

        public int WorkingCount
        {
            get { int n = 0; foreach (var c in _citizens) if (c.Works) n++; return n; }
        }
    }
}
