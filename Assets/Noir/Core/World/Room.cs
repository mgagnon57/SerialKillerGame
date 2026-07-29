using System;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// What a room is for. This is simulation data, not decoration: it is the difference
    /// between "she is at home" and "she is in the kitchen at seven and the bedroom at eleven".
    /// </summary>
    public enum RoomKind : byte
    {
        Hall = 0,       // inside the front door; connects everything
        Living,         // the front room
        Kitchen,
        Bedroom,
        Bathroom,
        Scullery,       // back kitchen, utility, pantry
        Workroom,       // office, workshop, whatever a given house has instead

        // ---- everything that is not a house ----
        //
        // These earn their place by being furnished differently, and only that. A kind that
        // would hold the same things as an existing one in the same arrangement is a synonym,
        // and synonyms are how an enum turns into a vocabulary list. If two rooms cannot be
        // told apart from above with the roof off, they are the same room.

        Corridor,       // the spine of an institution: circulation, never a destination
        Foyer,          // the public front of a public building; a counter and somewhere to stand
        Waiting,        // benches round the wall, and you are called from somewhere else
        Ward,           // beds in a row against both long walls
        Consulting,     // a desk on one side of it and an examination couch on the other
        Classroom,      // ranks of desks facing a board
        StaffRoom,      // a table, chairs and a kettle, for the people who work here
        Office,
        ShopFloor,      // shelves round the walls, a counter across the back
        StockRoom,      // shelving and crates; the part of a trade nobody sees
        Salon,          // chairs facing a mirror: the barber, the hairdresser
        Bar,            // the servery and the tables in front of it
        Auditorium,     // rows facing a screen
        Nave,           // rows facing an altar
        Assembly,       // the village hall: a stage at one end and chairs set out
        Workshop,       // benches and what is being worked on
        Washroom,       // the public lavatories: a row of basins, not a bathroom
    }

    public readonly struct RoomId : IEquatable<RoomId>
    {
        public readonly int Value;
        public RoomId(int value) { Value = value; }

        public static readonly RoomId None = new RoomId(-1);
        public bool IsValid => Value >= 0;

        public bool Equals(RoomId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is RoomId o && Equals(o);
        public override int GetHashCode() => Value;
        public override string ToString() => IsValid ? $"R{Value}" : "R-none";
        public static bool operator ==(RoomId a, RoomId b) => a.Value == b.Value;
        public static bool operator !=(RoomId a, RoomId b) => a.Value != b.Value;
    }

    /// <summary>One room inside one building.</summary>
    public sealed class Room
    {
        public readonly RoomId Id;
        public readonly PlaceId Building;
        public readonly RoomKind Kind;
        public readonly TileRect Bounds;

        /// <summary>Where somebody stands when they are "in" this room.</summary>
        public readonly Tile Anchor;

        public Room(RoomId id, PlaceId building, RoomKind kind, TileRect bounds, Tile anchor)
        {
            Id = id;
            Building = building;
            Kind = kind;
            Bounds = bounds;
            Anchor = anchor;
        }

        public int Area => Bounds.W * Bounds.H;

        /// <summary>How a room is described to the player. The only register the UI may use.</summary>
        public string Describe()
        {
            switch (Kind)
            {
                case RoomKind.Hall: return "the hall";
                case RoomKind.Living: return "the front room";
                case RoomKind.Kitchen: return "the kitchen";
                case RoomKind.Bedroom: return "a bedroom";
                case RoomKind.Bathroom: return "the bathroom";
                case RoomKind.Scullery: return "the scullery";
                case RoomKind.Workroom: return "the back room";
                case RoomKind.Corridor: return "the corridor";
                case RoomKind.Foyer: return "the entrance";
                case RoomKind.Waiting: return "the waiting room";
                case RoomKind.Ward: return "the ward";
                case RoomKind.Consulting: return "the consulting room";
                case RoomKind.Classroom: return "the classroom";
                case RoomKind.StaffRoom: return "the staff room";
                case RoomKind.Office: return "the office";
                case RoomKind.ShopFloor: return "the shop";
                case RoomKind.StockRoom: return "the store room";
                case RoomKind.Salon: return "the barber's";
                case RoomKind.Bar: return "the bar";
                case RoomKind.Auditorium: return "the auditorium";
                case RoomKind.Nave: return "the nave";
                case RoomKind.Assembly: return "the hall";
                case RoomKind.Workshop: return "the workshop";
                case RoomKind.Washroom: return "the lavatories";
                default: return "a room";
            }
        }

        public override string ToString() => $"{Kind} {Bounds}";
    }
}
