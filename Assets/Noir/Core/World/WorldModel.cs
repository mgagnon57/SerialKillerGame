using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// The immutable village: geometry plus institutions. Built once from an authored layout,
    /// then never mutated. Everything that changes over time lives in the simulation, not here.
    /// </summary>
    public sealed class WorldModel
    {
        public readonly string Name;
        public readonly TileGrid Grid;

        private readonly Place[] _places;
        private readonly List<PlaceId>[] _byKind;
        private readonly Room[] _rooms;
        private readonly Dictionary<int, List<RoomId>> _roomsByPlace;

        private readonly Furniture[] _furniture;

        public WorldModel(string name, TileGrid grid, Place[] places)
            : this(name, grid, places, Array.Empty<Room>(), Array.Empty<Furniture>()) { }

        public WorldModel(string name, TileGrid grid, Place[] places, Room[] rooms)
            : this(name, grid, places, rooms, Array.Empty<Furniture>()) { }

        private readonly Prop[] _props;

        public WorldModel(string name, TileGrid grid, Place[] places, Room[] rooms,
                          Furniture[] furniture)
            : this(name, grid, places, rooms, furniture, Array.Empty<Prop>()) { }

        public WorldModel(string name, TileGrid grid, Place[] places, Room[] rooms,
                          Furniture[] furniture, Prop[] props)
        {
            _props = props ?? Array.Empty<Prop>();
            Name = name;
            Grid = grid;
            _places = places;
            _rooms = rooms ?? Array.Empty<Room>();
            _furniture = furniture ?? Array.Empty<Furniture>();

            _roomsByPlace = new Dictionary<int, List<RoomId>>();
            foreach (var room in _rooms)
            {
                if (!_roomsByPlace.TryGetValue(room.Building.Value, out var list))
                    _roomsByPlace[room.Building.Value] = list = new List<RoomId>();
                list.Add(room.Id);
            }

            // Sized from the table, not from the enum: a kind that exists only in kinds.txt has a
            // value past the last enum member, and a place of that kind still has to be findable.
            int kindCount = PlaceKindTable.Current.Count;
            for (int i = 0; i < places.Length; i++)
                if ((int)places[i].Kind >= kindCount) kindCount = (int)places[i].Kind + 1;

            _byKind = new List<PlaceId>[kindCount];
            for (int i = 0; i < kindCount; i++) _byKind[i] = new List<PlaceId>();
            for (int i = 0; i < places.Length; i++)
                _byKind[(int)places[i].Kind].Add(places[i].Id);

            // Queues are worked out here rather than in the Place constructor because a place is
            // created before its own interior has been stamped into the grid — at that point
            // there is nothing to walk through yet.
            _counters = new CounterLine[places.Length];
            for (int i = 0; i < places.Length; i++)
                if (places[i].HasACounter) _counters[i] = CounterLine.Build(grid, places[i]);
        }

        private readonly CounterLine[] _counters;

        /// <summary>Where people queue in this place, or null if it is not somewhere you queue.</summary>
        public CounterLine CounterAt(PlaceId id) =>
            id.IsValid && id.Value < _counters.Length ? _counters[id.Value] : null;

        public int Width => Grid.Width;
        public int Height => Grid.Height;
        public int PlaceCount => _places.Length;

        public Place GetPlace(PlaceId id) =>
            id.IsValid && id.Value < _places.Length ? _places[id.Value] : null;

        public IReadOnlyList<Place> AllPlaces => _places;

        private static readonly List<PlaceId> NoPlaces = new List<PlaceId>();

        public IReadOnlyList<PlaceId> PlacesOfKind(PlaceKind kind) =>
            (int)kind < _byKind.Length ? _byKind[(int)kind] : NoPlaces;

        public int RoomCount => _rooms.Length;
        public IReadOnlyList<Room> AllRooms => _rooms;

        public int FurnitureCount => _furniture.Length;
        public IReadOnlyList<Furniture> AllFurniture => _furniture;

        public int PropCount => _props.Length;
        public IReadOnlyList<Prop> AllProps => _props;

        public Room GetRoom(RoomId id) =>
            id.IsValid && id.Value < _rooms.Length ? _rooms[id.Value] : null;

        private static readonly List<RoomId> NoRooms = new List<RoomId>();

        /// <summary>The rooms inside a building, in the order they were generated.</summary>
        public IReadOnlyList<RoomId> RoomsIn(PlaceId place) =>
            place.IsValid && _roomsByPlace.TryGetValue(place.Value, out var list) ? list : NoRooms;

        /// <summary>The first room of a given kind in a building, if it has one.</summary>
        public RoomId RoomOfKind(PlaceId place, RoomKind kind)
        {
            foreach (var id in RoomsIn(place))
                if (_rooms[id.Value].Kind == kind) return id;
            return RoomId.None;
        }

        /// <summary>Total job slots across every workplace — the village's employment capacity.</summary>
        public int TotalJobSlots
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _places.Length; i++) n += _places[i].JobSlots;
                return n;
            }
        }

        /// <summary>The place whose door is nearest the given tile. Used for "where did they go in".</summary>
        public PlaceId NearestPlaceOfKind(Tile from, PlaceKind kind)
        {
            var candidates = PlacesOfKind(kind);
            PlaceId best = PlaceId.None;
            int bestDist = int.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                var p = _places[candidates[i].Value];
                int d = Tile.DistanceSquared(from, p.Door);
                if (d < bestDist) { bestDist = d; best = p.Id; }
            }
            return best;
        }
    }
}
