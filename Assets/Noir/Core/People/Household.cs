using System;
using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.People
{
    public enum HouseholdShape : byte
    {
        Solitary = 0,       // one person
        Couple,             // two adults
        Family,             // adults with children
        SingleParent,
        Sharers,            // unrelated adults under one roof
        Extended,           // a couple with an elder
    }

    /// <summary>
    /// The people who share a dwelling. This is the unit that makes the school run, the family
    /// walk to church, and the empty house at eight in the morning all fall out for free rather
    /// than being scripted as special cases.
    /// </summary>
    public sealed class Household
    {
        public readonly HouseholdId Id;
        public readonly PlaceId Dwelling;
        public readonly string Surname;
        public readonly HouseholdShape Shape;

        /// <summary>Which home within the building. 0 for a house; 0..n for a terrace or flats.</summary>
        public readonly int Unit;

        /// <summary>
        /// The address, as a number: the dwelling's own stable key crossed with which home in it
        /// this is. Nothing about the population arrays goes into it, so it means the same thing
        /// after somebody has moved out.
        /// </summary>
        public readonly ulong Key;

        // ---- the catchment: which school, which church, which surgery ----
        //
        // Settled once, by the generator, from where the house is. It lives on the HOUSEHOLD
        // and not on the citizen because two children under one roof have to leave for the same
        // gate at the same minute or there is nothing for the companion pairing to pair — a
        // brother and sister walking to different schools is not a village, it is a bug you can
        // see from the camera.
        //
        // Storing it also keeps DayPlanner a pure function of what it is handed: the planner
        // reads a decision already made rather than reaching for the world's first school.

        // Kept as a pair of parallel arrays keyed on kind rather than as three named fields,
        // because which institutions have a catchment is the `catchment` column of kinds.txt.
        // A parish with a second school and no surgery is a content edit, not a new field here.
        private readonly PlaceKind[] _catchmentKinds;
        private readonly PlaceId[] _catchments;

        public PlaceId School => Catchment(PlaceKind.School);
        public PlaceId Church => Catchment(PlaceKind.Church);
        public PlaceId Surgery => Catchment(PlaceKind.Surgery);

        private readonly CitizenId[] _members;

        public Household(HouseholdId id, PlaceId dwelling, string surname,
                         HouseholdShape shape, CitizenId[] members)
            : this(id, dwelling, surname, shape, members, 0) { }

        public Household(HouseholdId id, PlaceId dwelling, string surname,
                         HouseholdShape shape, CitizenId[] members, int unit)
            : this(id, dwelling, surname, shape, members, unit,
                   PlaceId.None, PlaceId.None, PlaceId.None) { }

        public Household(HouseholdId id, PlaceId dwelling, string surname,
                         HouseholdShape shape, CitizenId[] members, int unit,
                         PlaceId school, PlaceId church, PlaceId surgery)
            : this(id, dwelling, surname, shape, members, unit, school, church, surgery,
                   // A hand-built fixture that has no world to ask for the dwelling's key falls
                   // back to the dwelling's index. Good enough to keep keys distinct in a test;
                   // not stable against content edits, which is why the generator passes a real
                   // one.
                   Keys.Combine((ulong)dwelling.Value + 1UL, (ulong)unit)) { }

        public Household(HouseholdId id, PlaceId dwelling, string surname,
                         HouseholdShape shape, CitizenId[] members, int unit,
                         PlaceId school, PlaceId church, PlaceId surgery, ulong key)
            : this(id, dwelling, surname, shape, members, unit,
                   new[] { PlaceKind.School, PlaceKind.Church, PlaceKind.Surgery },
                   new[] { school, church, surgery }, key) { }

        public Household(HouseholdId id, PlaceId dwelling, string surname,
                         HouseholdShape shape, CitizenId[] members, int unit,
                         PlaceKind[] catchmentKinds, PlaceId[] catchments, ulong key)
        {
            Key = key;
            Id = id;
            Dwelling = dwelling;
            Surname = surname;
            Shape = shape;
            Unit = unit;
            _catchmentKinds = catchmentKinds ?? Array.Empty<PlaceKind>();
            _catchments = catchments ?? Array.Empty<PlaceId>();
            _members = members ?? Array.Empty<CitizenId>();
        }

        /// <summary>The address as a number: which building, and which home inside it.</summary>
        public static ulong KeyFor(ulong dwellingKey, int unit) =>
            Keys.Combine(dwellingKey, (ulong)unit);

        /// <summary>
        /// The institution of this kind that this household belongs to, or None if the kind is
        /// not one anybody has a catchment for. None also means "not decided" — a household
        /// built without catchments, which the planner falls back from rather than crashing on.
        /// </summary>
        public PlaceId Catchment(PlaceKind kind)
        {
            for (int i = 0; i < _catchmentKinds.Length && i < _catchments.Length; i++)
                if (_catchmentKinds[i] == kind) return _catchments[i];
            return PlaceId.None;
        }

        public IReadOnlyList<CitizenId> Members => _members;
        public int Size => _members.Length;

        public override string ToString() => $"the {Surname}s ({Shape}, {Size})";
    }
}
