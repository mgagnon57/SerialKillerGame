using System;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// WIDENED FROM byte TO ushort 2026-08-11, because all eight bits were spoken for and the
    /// verge needed one. Nothing casts these to a byte and nothing serialises them — there is no
    /// save system — so the widening is invisible outside this file. It costs one extra byte on
    /// each of 5,040,000 tiles, 5 MB, against the ~50 MB this grid already holds.
    /// </summary>
    [Flags]
    public enum TileFlags : ushort
    {
        None = 0,
        Walkable = 1 << 0,
        BlocksSight = 1 << 1,
        Indoor = 1 << 2,
        Road = 1 << 3,   // carriageway — vehicles later, and where people walk fastest
        Path = 1 << 4,   // footpath, verge, yard
        // Set from Terrain.Water, and read by NOTHING — every water check in the game asks
        // Terrain.Water directly. Kept because it is produced and removing it would rewrite the
        // stored flags of every water tile for no behavioural gain, but do not assume testing
        // this bit does anything: Terrain is the source of truth. Audited 2026-07-29.
        Water = 1 << 5,
        Rough = 1 << 6,  // field, scrub — passable but slow

        /// <summary>
        /// The back lane, and it is a different KIND of way through town rather than a narrow
        /// street. Owner's standing fact 4 in docs/SOURCES-OF-TRUTH.md, from having grown up
        /// here: an alley is for the bins and for the garage that faces it, cars do not use it
        /// to get anywhere, and a boy on a bike takes it in preference to the street.
        ///
        /// The survey agrees without being asked: a Rossville house sits a median 81 ft from
        /// its street and 129 ft from its alley, and its outbuilding is the other way round at
        /// 125 ft and 84 ft. Fronts face the street, garages face the lane.
        ///
        /// Also set on Road tiles - an alley IS a carriageway, it is just nobody's route - so
        /// anything asking "can I walk here quickly" keeps the answer it had.
        /// </summary>
        Alley = 1 << 7,

        /// <summary>
        /// THE PUBLIC GROUND BESIDE THE CARRIAGEWAY, and the reason people stop walking down the
        /// middle of the street.
        ///
        /// A Rossville street paves a 10 m corridor down the middle of a 20 m easement, so five
        /// metres each side is public ground that is NOT road — the county measured the right of
        /// way, and `Content/roads-1991.txt` names that gap as where a walk went on the streets
        /// that had one. Until 2026-08-11 those tiles were indistinguishable from open country,
        /// so the cheapest way along a street was the asphalt itself and everybody walked in the
        /// road.
        ///
        /// It is grass and it renders as grass: this is a bit, not a Terrain, precisely so that
        /// no renderer, material or texture has to learn a new ground kind to make people walk
        /// somewhere sensible.
        /// </summary>
        Verge = 1 << 8,
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

        /// <summary>Whose lot each tile is part of. See <see cref="LotAt"/>.</summary>
        private readonly int[] _lot;

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
            _lot = new int[n];
            for (int i = 0; i < n; i++)
            {
                _terrain[i] = Terrain.Grass;
                _flags[i] = TileFlags.Walkable;
                _placeId[i] = -1;
                _roomId[i] = -1;
                _lot[i] = NoLot;
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

        /// <summary>Is this the back lane rather than the street? See <see cref="TileFlags.Alley"/>.</summary>
        public bool IsAlley(int x, int y) => InBounds(x, y) && (_flags[Index(x, y)] & TileFlags.Alley) != 0;
        public bool IsAlley(Tile t) => IsAlley(t.X, t.Y);

        public bool BlocksSight(int x, int y) => !InBounds(x, y) || (_flags[Index(x, y)] & TileFlags.BlocksSight) != 0;
        public bool BlocksSight(Tile t) => BlocksSight(t.X, t.Y);

        /// <summary>Movement cost multiplier. Roads and paths are quick; rough ground is slow.</summary>
        /// <summary>
        /// What a step across ordinary ground costs, which is what a heuristic has to be scaled
        /// against to mean anything.
        ///
        /// The cheapest terrain is a road at 1.0, but almost none of a journey between two
        /// distant places is on one - the countryside is grass at 1.3, and the map is nearly all
        /// countryside. A heuristic built on the CHEAPEST cost is therefore barely a heuristic at
        /// all out there, and A* degenerates towards Dijkstra exactly where the distances are
        /// longest. Scaling to the typical cost instead keeps the search pointed at the goal on
        /// the ground people actually cross.
        /// </summary>
        public const float TypicalMoveCost = 1.3f;   // open grass, below

        public float MoveCost(int x, int y)
        {
            if (!InBounds(x, y)) return float.MaxValue;
            var f = _flags[Index(x, y)];
            if ((f & TileFlags.Walkable) == 0) return float.MaxValue;

            // THE ORDER IS THE RANKING, AND IT WAS THE OTHER WAY ROUND UNTIL 2026-08-11.
            //
            // A road used to be the cheapest ground in town at 1.00 against a footpath's 1.05,
            // and the enum above called a road "where people walk fastest". So A* did what it was
            // asked and walked the whole village down the centre of the carriageway. A made walk
            // now beats the verge, the verge beats the asphalt, and the asphalt is only what you
            // use to cross the street.
            if ((f & TileFlags.Path) != 0) return 0.90f;   // a laid sidewalk — best there is
            if ((f & TileFlags.Verge) != 0) return 0.95f;  // public ground beside the kerb
            if ((f & TileFlags.Road) != 0) return 1.00f;   // the carriageway itself
            if ((f & TileFlags.Indoor) != 0) return 1.2f;
            if ((f & TileFlags.Rough) != 0) return 1.7f;
            return 1.3f; // open grass, and see LotAt — whose grass it is costs extra
        }

        /// <summary>
        /// WHOSE LOT THIS TILE IS PART OF, or <see cref="NoLot"/> for public ground.
        ///
        /// Distinct from <see cref="PlaceAt"/>, which claims only the tiles inside a building's
        /// own outline: the yard around the house was nobody's, so cutting the corner across it
        /// cost a walker 1.30 against a road's 1.00 and A* took it whenever going round was more
        /// than about a third further. That is the whole of "people walk through other people's
        /// yards" — there was no such thing as somebody's yard.
        ///
        /// Stamped from the county's parcel polygons by the survey layer, which is also why this
        /// is a bare int rather than a PlaceId: a lot exists whether or not anything stands on it.
        /// int[] and not short[] for the reason given against <see cref="_placeId"/> above — a
        /// silent wrong answer at a size we intend to reach is not a saving.
        /// </summary>
        public int LotAt(int x, int y) => InBounds(x, y) ? _lot[Index(x, y)] : NoLot;
        public int LotAt(Tile t) => LotAt(t.X, t.Y);

        /// <summary>Public ground — a street, a verge, a field, the churchyard.</summary>
        public const int NoLot = -1;

        public void SetLot(int x, int y, int lot)
        {
            if (!InBounds(x, y)) return;
            _lot[Index(x, y)] = lot;
        }

        // ---- writers, used only by the builder ----

        public void Set(int x, int y, Terrain terrain, TileFlags flags)
        {
            if (!InBounds(x, y)) return;
            int i = Index(x, y);

            // Only a change to WALKABILITY moves the revision. Repainting grass as gravel does
            // not alter who can reach whom, and bumping the stamp for it would throw away a
            // region map that is still perfectly true.
            if (((_flags[i] ^ flags) & TileFlags.Walkable) != 0) WalkabilityRevision++;

            _terrain[i] = terrain;
            _flags[i] = flags;
        }

        /// <summary>
        /// Bumped whenever a tile's walkability changes, so anything derived from the shape of
        /// the walkable space can tell that it has gone stale.
        ///
        /// The builder writes this grid tile by tile long before anybody paths on it, and a
        /// derived map that silently outlived an edit would be worse than no map at all: it
        /// would refuse journeys that had just become possible, and nothing would say why.
        /// </summary>
        public int WalkabilityRevision { get; private set; }

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
