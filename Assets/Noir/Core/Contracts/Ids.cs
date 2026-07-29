using System;

namespace Noir.Core.Contracts
{
    /// <summary>
    /// Where one citizen sits in the population arrays. A SLOT, not a name.
    ///
    /// It is load-bearing for the hot loop — every per-tick lookup is an array index — and it is
    /// worthless the moment the population changes shape, because the slot a person occupies is
    /// a fact about the array rather than a fact about them. A game whose central event removes
    /// somebody from the village cannot answer "who was that" with an array offset. Anything
    /// that outlives one run of the simulation, or that is shown to a person, uses
    /// <see cref="CitizenKey"/>.
    /// </summary>
    public readonly struct CitizenId : IEquatable<CitizenId>
    {
        public readonly int Value;
        public CitizenId(int value) { Value = value; }

        public static readonly CitizenId None = new CitizenId(-1);
        public bool IsValid => Value >= 0;

        public bool Equals(CitizenId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CitizenId o && Equals(o);
        public override int GetHashCode() => Value;
        public override string ToString() => IsValid ? $"C{Value}" : "C-none";
        public static bool operator ==(CitizenId a, CitizenId b) => a.Value == b.Value;
        public static bool operator !=(CitizenId a, CitizenId b) => a.Value != b.Value;
    }

    /// <summary>Identifies a Place: an institution with hours, a roster and a purpose.</summary>
    public readonly struct PlaceId : IEquatable<PlaceId>
    {
        public readonly int Value;
        public PlaceId(int value) { Value = value; }

        public static readonly PlaceId None = new PlaceId(-1);
        public bool IsValid => Value >= 0;

        public bool Equals(PlaceId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is PlaceId o && Equals(o);
        public override int GetHashCode() => Value;
        public override string ToString() => IsValid ? $"P{Value}" : "P-none";
        public static bool operator ==(PlaceId a, PlaceId b) => a.Value == b.Value;
        public static bool operator !=(PlaceId a, PlaceId b) => a.Value != b.Value;
    }

    /// <summary>
    /// Who somebody IS, as opposed to where they are stored.
    ///
    /// Derived from the home they were born into and their place in it, so it survives the one
    /// thing an array index cannot: the population changing shape underneath it. Two runs of the
    /// same seed give the same person the same key, a saved game can name them, and a village
    /// that has lost somebody still knows which somebody it lost.
    ///
    /// Sixty-four bits rather than an incrementing counter because a counter is only unique
    /// within whatever issued it, and the thing that issues these is a generator that must be
    /// able to run twice and agree with itself.
    /// </summary>
    public readonly struct CitizenKey : IEquatable<CitizenKey>
    {
        public readonly ulong Value;
        public CitizenKey(ulong value) { Value = value; }

        public static readonly CitizenKey None = new CitizenKey(0);
        public bool IsValid => Value != 0;

        public bool Equals(CitizenKey other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CitizenKey o && Equals(o);
        public override int GetHashCode() => (int)(Value ^ (Value >> 32));
        public override string ToString() => IsValid ? Value.ToString("x16") : "c-none";
        public static bool operator ==(CitizenKey a, CitizenKey b) => a.Value == b.Value;
        public static bool operator !=(CitizenKey a, CitizenKey b) => a.Value != b.Value;
    }

    /// <summary>Identifies a household: the people who share a dwelling.</summary>
    public readonly struct HouseholdId : IEquatable<HouseholdId>
    {
        public readonly int Value;
        public HouseholdId(int value) { Value = value; }

        public static readonly HouseholdId None = new HouseholdId(-1);
        public bool IsValid => Value >= 0;

        public bool Equals(HouseholdId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is HouseholdId o && Equals(o);
        public override int GetHashCode() => Value;
        public override string ToString() => IsValid ? $"H{Value}" : "H-none";
        public static bool operator ==(HouseholdId a, HouseholdId b) => a.Value == b.Value;
        public static bool operator !=(HouseholdId a, HouseholdId b) => a.Value != b.Value;
    }
}
