using System;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    [Flags]
    public enum TileFlags : byte
    {
        None = 0,
        Walkable = 1 << 0,
        BlocksSight = 1 << 1,
        Indoor = 1 << 2,
        Road = 1 << 3,   // carriageway — vehicles later, and where people walk fastest
        Path = 1 << 4,   // footpath, verge, yard
        Water = 1 << 5,
        Rough = 1 << 6,  // field, scrub — passable but slow
    }

    public enum Terrain : byte
    {
        Grass = 0,
        Field = 1,
        Road = 2,
        Path = 3,
        Water = 4,
        Wall = 5,
        Floor = 6,      // building interior
        Churchyard = 7,
        Wood = 8,
    }

    /// <summary>
    /// The village, as a grid. One tile is one metre.
    ///
    /// Struct-of-arrays rather than an array of Tile objects: a game touches one field across
    /// many tiles far more often than many fields of one tile, so keeping each field in its own
    /// flat array is dramatically more cache-friendly. It is also trivially serialisable.
    /// </summary>
    public sealed class TileGrid
    {
        public readonly int Width;
        public readonly int Height;

        private readonly TileFlags[] _flags;
        private readonly Terrain[] _terrain;

        /// <summary>
        /// Which place and which room owns each tile. -1 for neither.
        ///
        /// These were short[], which cost four bytes a tile less and silently stopped working at
        /// 32,767 rooms — about 7,700 homes, a city of seventeen thousand. The cast in
        /// <see cref="SetRoom"/> wrapped negative and <see cref="RoomAt"/> then reported "no
        /// room" for real rooms, for the tiles inside the affected buildings only, without
        /// throwing. A silent wrong answer at a size we intend to reach is not a saving.
        /// </summary>
        private readonly int[] _placeId;
        private readonly int[] _roomId;

        /// <summary>
        /// The largest id these arrays can hold. Nothing is close to it; the point is that the
        /// ceiling is now a number somebody can be told about rather than one they find out
        /// about from a bug report.
        /// </summary>
        public const int MaxId = int.MaxValue - 1;

        public TileGrid(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("grid must be non-empty");
            Width = width;
            Height = height;
            int n = width * height;
            _flags = new TileFlags[n];
            _terrain = new Terrain[n];
            _placeId = new int[n];
            _roomId = new int[n];
            for (int i = 0; i < n; i++)
            {
                _terrain[i] = Terrain.Grass;
                _flags[i] = TileFlags.Walkable;
                _placeId[i] = -1;
                _roomId[i] = -1;
            }
        }

        public int Count => Width * Height;

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public bool InBounds(Tile t) => InBounds(t.X, t.Y);

        public int Index(int x, int y) => y * Width + x;
        public int Index(Tile t) => t.Y * Width + t.X;
        public Tile TileAt(int index) => new Tile(index % Width, index / Width);

        public TileFlags FlagsAt(int x, int y) => InBounds(x, y) ? _flags[Index(x, y)] : TileFlags.BlocksSight;
        public TileFlags FlagsAt(Tile t) => FlagsAt(t.X, t.Y);

        public Terrain TerrainAt(int x, int y) => InBounds(x, y) ? _terrain[Index(x, y)] : Terrain.Wall;
        public Terrain TerrainAt(Tile t) => TerrainAt(t.X, t.Y);

        public PlaceId PlaceAt(int x, int y)
        {
            if (!InBounds(x, y)) return PlaceId.None;
            int p = _placeId[Index(x, y)];
            return p < 0 ? PlaceId.None : new PlaceId(p);
        }
        public PlaceId PlaceAt(Tile t) => PlaceAt(t.X, t.Y);

        public bool IsWalkable(int x, int y) => InBounds(x, y) && (_flags[Index(x, y)] & TileFlags.Walkable) != 0;
        public bool IsWalkable(Tile t) => IsWalkable(t.X, t.Y);

        public bool BlocksSight(int x, int y) => !InBounds(x, y) || (_flags[Index(x, y)] & TileFlags.BlocksSight) != 0;
        public bool BlocksSight(Tile t) => BlocksSight(t.X, t.Y);

        /// <summary>Movement cost multiplier. Roads and paths are quick; rough ground is slow.</summary>
        public float MoveCost(int x, int y)
        {
            if (!InBounds(x, y)) return float.MaxValue;
            var f = _flags[Index(x, y)];
            if ((f & TileFlags.Walkable) == 0) return float.MaxValue;
            if ((f & TileFlags.Road) != 0) return 1.0f;
            if ((f & TileFlags.Path) != 0) return 1.05f;
            if ((f & TileFlags.Indoor) != 0) return 1.2f;
            if ((f & TileFlags.Rough) != 0) return 1.7f;
            return 1.3f; // open grass
        }

        // ---- writers, used only by the builder ----

        public void Set(int x, int y, Terrain terrain, TileFlags flags)
        {
            if (!InBounds(x, y)) return;
            int i = Index(x, y);
            _terrain[i] = terrain;
            _flags[i] = flags;
        }

        public void SetPlace(int x, int y, PlaceId place)
        {
            if (place.Value > MaxId) throw new ArgumentOutOfRangeException(nameof(place),
                $"place id {place.Value} is past what a tile can record ({MaxId})");
            if (!InBounds(x, y)) return;
            _placeId[Index(x, y)] = place.Value;
        }

        /// <summary>Which room this tile is in, if any. Rooms only exist inside buildings.</summary>
        public RoomId RoomAt(int x, int y)
        {
            if (!InBounds(x, y)) return RoomId.None;
            int r = _roomId[Index(x, y)];
            return r < 0 ? RoomId.None : new RoomId(r);
        }
        public RoomId RoomAt(Tile t) => RoomAt(t.X, t.Y);

        public void SetRoom(int x, int y, RoomId room)
        {
            if (room.Value > MaxId) throw new ArgumentOutOfRangeException(nameof(room),
                $"room id {room.Value} is past what a tile can record ({MaxId})");
            if (!InBounds(x, y)) return;
            _roomId[Index(x, y)] = room.Value;
        }

        /// <summary>Standard flags for a terrain kind, so the builder stays declarative.</summary>
        public static TileFlags FlagsFor(Terrain t)
        {
            switch (t)
            {
                case Terrain.Wall: return TileFlags.BlocksSight;
                case Terrain.Water: return TileFlags.Water | TileFlags.BlocksSight;
                case Terrain.Road: return TileFlags.Walkable | TileFlags.Road;
                case Terrain.Path: return TileFlags.Walkable | TileFlags.Path;
                case Terrain.Floor: return TileFlags.Walkable | TileFlags.Indoor;
                case Terrain.Field: return TileFlags.Walkable | TileFlags.Rough;
                case Terrain.Wood: return TileFlags.Walkable | TileFlags.Rough | TileFlags.BlocksSight;
                case Terrain.Churchyard: return TileFlags.Walkable | TileFlags.Path;
                default: return TileFlags.Walkable;
            }
        }
    }
}
