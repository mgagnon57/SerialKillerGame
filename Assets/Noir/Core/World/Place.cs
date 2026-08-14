using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    public enum PlaceKind : byte
    {
        Dwelling = 0,
        Tavern,
        Shop,
        PostOffice,
        Church,
        School,
        Clinic,
        Garage,
        Mill,
        Farm,
        VillageHall,
        Playground,
        BusStop,
        PhoneBooth,
        Green,
        Churchyard,
        Gardens,
    }

    public readonly struct TileRect
    {
        public readonly int X, Y, W, H;
        public TileRect(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }

        public int Left => X;
        public int Top => Y;
        public int Right => X + W - 1;
        public int Bottom => Y + H - 1;
        public int Area => W * H;
        public Tile Centre => new Tile(X + W / 2, Y + H / 2);

        public bool Contains(int x, int y) => x >= X && y >= Y && x < X + W && y < Y + H;
        public bool Contains(Tile t) => Contains(t.X, t.Y);

        public bool Overlaps(TileRect o) => X < o.X + o.W && o.X < X + W && Y < o.Y + o.H && o.Y < Y + H;

        public override string ToString() => $"{X},{Y} {W}x{H}";
    }

    /// <summary>One opening window. DaysMask bit 0 = Monday .. bit 6 = Sunday.</summary>
    public readonly struct OpenWindow
    {
        public readonly short StartMinute;
        public readonly short EndMinute;
        public readonly byte DaysMask;

        public const byte AllDays = 0b0111_1111;
        public const byte Weekdays = 0b0001_1111;
        public const byte Weekend = 0b0110_0000;
        public const byte SundayOnly = 0b0100_0000;

        public OpenWindow(int startMinute, int endMinute, byte daysMask = AllDays)
        {
            StartMinute = (short)startMinute;
            EndMinute = (short)endMinute;
            DaysMask = daysMask;
        }

        public bool Covers(int minuteOfDay, int dayOfWeek) =>
            (DaysMask & (1 << dayOfWeek)) != 0 &&
            minuteOfDay >= StartMinute && minuteOfDay < EndMinute;

        public override string ToString() =>
            $"{StartMinute / 60:00}:{StartMinute % 60:00}-{EndMinute / 60:00}:{EndMinute % 60:00}";
    }

    /// <summary>
    /// A Place is an institution, not just geometry: it has hours, a purpose, and people who
    /// belong to it. Rooms are walked by pathfinding; Places are what a life is organised around.
    ///
    /// TWO REGISTERS, and the split is enforced by use rather than by convention:
    ///   - the TACTICAL register is numeric (rect, door, capacity, hours) and is never rendered;
    ///   - the HUMAN register is one sentence about what this place is for the person who goes
    ///     there, and it is the ONLY thing the UI is allowed to show.
    /// The barber is not "high-dwell interior cover". It is where a man goes fortnightly to be
    /// talked to. Content that describes places tactically leaks that framing into everything
    /// downstream, so the tactical text simply does not exist.
    /// </summary>
    public sealed class Place
    {
        public readonly PlaceId Id;
        public readonly PlaceKind Kind;
        public readonly string Name;
        public readonly string Human;
        public readonly TileRect Bounds;
        public readonly Tile Door;
        public readonly int JobSlots;

        /// <summary>
        /// The building's real outline in tiles, or null for a plain rectangle - carried through
        /// from PlaceSpec.Outline so anything DRAWING a place can trace the building rather than
        /// the box around it.
        ///
        /// <see cref="Bounds"/> stays the rectangle everything measures by, and for a shaped
        /// building it is deliberately the full bounding box: the outline is what removes the
        /// parts that are not the building. Drawing the box instead put a house's plan line
        /// across the street it fronts, on every lot where the building sits at an angle to the
        /// grid - which along a road that runs diagonally is most of them.
        /// </summary>
        public readonly Tile[] Outline;

        /// <summary>
        /// The same corners as <see cref="Outline"/>, before they were rounded to the nearest
        /// tile - or null when nothing more precise than the tile-rounded ring was ever computed.
        /// Rendering the wrong precision costs nothing in the grid: pathfinding, room stamping and
        /// every other tile-based system still reads <see cref="Outline"/>, unchanged. It costs a
        /// visible kink between two adjacent buildings' walls once a unit is narrow enough that a
        /// single tile of rounding on nearby corners swings its own edge several degrees off its
        /// neighbour's - see <c>DrawShapedPerimeters</c> (Assets/Noir/Unity/VillageMesh.cs) for
        /// where this actually gets used, and <c>DowntownFromSanborn</c> for the one caller that
        /// populates it today.
        ///
        /// Must be the same length as <see cref="Outline"/>, corner for corner in the same order
        /// and winding, including the same closing repeat — a mismatch is silently discarded
        /// (see <c>DrawShapedPerimeters</c>, which now logs when that happens).
        /// </summary>
        public readonly Vec2[] OutlinePrecise;

        /// <summary>
        /// The wall's base height in metres, or null when nothing more than the per-place
        /// default (<c>Space3D.GroundUnder</c>, sampled at the middle of THIS place's own
        /// <see cref="Bounds"/>) was ever computed.
        ///
        /// A terrace row's units are narrow enough that neighbouring bounds centres land on
        /// measurably different points of the elevation grid, so two adjacent units seated
        /// independently can differ by a few centimetres at the party wall they are meant to
        /// share - invisible face-on, and a visible sliver of sky between two storefronts once
        /// you look down the row at a raking angle, for the same reason a few degrees of
        /// direction error was: see <see cref="OutlinePrecise"/>. This field lets a generator
        /// that knows it is laying out several attached units say so once, for the whole row,
        /// instead of leaving each unit to measure its own ground independently.
        /// </summary>
        public readonly float? GroundHeight;

        /// <summary>
        /// What this building is called, in the only sense the generators care about: a stable
        /// 64-bit name that does not move when the file around it does.
        ///
        /// <see cref="Id"/> is a line number in all but spirit, and everything generated from it
        /// inherits that. Inserting one building at the top of a 345-place file used to reshuffle
        /// fifty-four other buildings' interiors — buildings with byte-identical footprints,
        /// nowhere near the edit, that simply came later in the walk. Content has to be additive
        /// or it stops being possible to add any: a shop must not rewrite parts of the village
        /// that have nothing to do with it.
        ///
        /// Hashed from <see cref="KeySource"/>, which is the authored `key` line if there is one
        /// and the name otherwise. Two places may not share it — see WorldBuilder.Build.
        /// </summary>
        public readonly ulong Key;

        /// <summary>The text <see cref="Key"/> was hashed from, kept for error messages.</summary>
        public readonly string KeySource;

        /// <summary>
        /// How many separate homes are inside this building. One for a cottage; four for a
        /// terrace; a dozen for a block of flats.
        ///
        /// This is what makes the village stop being forty-five detached houses. A terrace is
        /// one building with one roof and several front doors, and the people in it are
        /// neighbours in a way that detached householders are not - which matters enormously
        /// for who notices what.
        /// </summary>
        public readonly int Units;

        private readonly OpenWindow[] _hours;

        public Place(PlaceId id, PlaceKind kind, string name, string human,
                     TileRect bounds, Tile door, OpenWindow[] hours, int jobSlots)
            : this(id, kind, name, human, bounds, door, hours, jobSlots, 1) { }

        public Place(PlaceId id, PlaceKind kind, string name, string human,
                     TileRect bounds, Tile door, OpenWindow[] hours, int jobSlots, int units)
            : this(id, kind, name, human, bounds, door, hours, jobSlots, units, null) { }

        public Place(PlaceId id, PlaceKind kind, string name, string human,
                     TileRect bounds, Tile door, OpenWindow[] hours, int jobSlots, int units,
                     string keySource)
            : this(id, kind, name, human, bounds, door, hours, jobSlots, units, keySource, null) { }

        public Place(PlaceId id, PlaceKind kind, string name, string human,
                     TileRect bounds, Tile door, OpenWindow[] hours, int jobSlots, int units,
                     string keySource, Tile[] outline, Vec2[] outlinePrecise = null,
                     float? groundHeight = null)
        {
            Outline = outline;
            OutlinePrecise = outlinePrecise;
            GroundHeight = groundHeight;
            Units = units < 1 ? 1 : units;
            Id = id;
            Kind = kind;
            Name = name ?? string.Empty;
            Human = human ?? string.Empty;
            Bounds = bounds;
            Door = door;
            _hours = hours ?? Array.Empty<OpenWindow>();
            JobSlots = jobSlots;
            KeySource = string.IsNullOrEmpty(keySource) ? Name : keySource;
            Key = Keys.Of(KeySource);
        }

        public IReadOnlyList<OpenWindow> Hours => _hours;

        /// <summary>Places with no declared hours (dwellings, the green) are always accessible.</summary>
        public bool AlwaysOpen => _hours.Length == 0;

        public bool IsOpenAt(int minuteOfDay, int dayOfWeek)
        {
            if (_hours.Length == 0) return true;
            for (int i = 0; i < _hours.Length; i++)
                if (_hours[i].Covers(minuteOfDay, dayOfWeek)) return true;
            return false;
        }

        public bool IsOpenAt(GameClock clock) => IsOpenAt(clock.MinuteOfDay, clock.DayOfWeek);

        /// <summary>Somewhere you are served one at a time, and therefore somewhere you queue.</summary>
        public bool HasACounter => CounterLine.Serves(Kind);

        /// <summary>Minutes until this place next opens, or 0 if open now. -1 if it never opens.</summary>
        public int MinutesUntilOpen(int minuteOfDay, int dayOfWeek)
        {
            if (IsOpenAt(minuteOfDay, dayOfWeek)) return 0;
            for (int ahead = 1; ahead <= 1440 * 7; ahead++)
            {
                int m = (minuteOfDay + ahead) % 1440;
                int d = (dayOfWeek + (minuteOfDay + ahead) / 1440) % 7;
                if (IsOpenAt(m, d)) return ahead;
            }
            return -1;
        }

        public override string ToString() => $"{Kind} \"{Name}\" @{Bounds}";
    }

    /// <summary>
    /// Where people stand to wait their turn: an ordered line of standing positions running
    /// back from the counter to the door. Slot 0 is at the counter, and the last slot is one
    /// step inside the doorway.
    ///
    /// SLOT-RESERVED rather than emergent. Somebody arriving takes the first free position and
    /// walks to it; when the person at the front is served, everyone moves up one. That needs
    /// no steering, no collision avoidance and no negotiation between agents, it comes out
    /// identical on every run from the same seed, and it looks better than emergent queuing —
    /// because real queues are not emergent either. People look for the back of the line and
    /// stand in it.
    ///
    /// The line is fixed geometry, decided once when the village is built, so nothing about it
    /// can drift as the day runs.
    /// </summary>
    public sealed class CounterLine
    {
        /// <summary>How long a line can get before the rest have to wait their turn to join it.</summary>
        public const int MaxSlots = 6;

        public readonly PlaceId Place;

        /// <summary>The doorway the line is fed from. Not a standing position — nobody queues in a doorway.</summary>
        public readonly Tile Entry;

        private readonly Tile[] _slots;

        public CounterLine(PlaceId place, Tile entry, Tile[] slots)
        {
            Place = place;
            Entry = entry;
            _slots = slots ?? Array.Empty<Tile>();
        }

        /// <summary>Ordered from the counter backwards. Index 0 is being served.</summary>
        public IReadOnlyList<Tile> Slots => _slots;

        public int Count => _slots.Length;
        public Tile Counter => _slots[0];

        /// <summary>Which position in the line this tile is, or -1 if it is not on the line at all.</summary>
        public int IndexOf(Tile t)
        {
            for (int i = 0; i < _slots.Length; i++) if (_slots[i] == t) return i;
            return -1;
        }

        /// <summary>Tiles the line needs left clear: its standing positions and the door feeding them.</summary>
        public bool Claims(Tile t) => t == Entry || IndexOf(t) >= 0;

        /// <summary>Places where you are served one at a time. A pub is not one: you stand at the bar.</summary>
        public static bool Serves(PlaceKind kind) => PlaceKindTable.Current.Row(kind).HasCounter;

        private static readonly int[] Dx = { 0, 1, 0, -1 };
        private static readonly int[] Dy = { -1, 0, 1, 0 };

        /// <summary>
        /// Lay out the line for one building.
        ///
        /// Breadth-first from the doorway, four-directionally and confined to the building's own
        /// footprint, then the counter is put on the deepest route in — capped, so that the tail
        /// of the queue always ends up just inside the door where it can be seen from the street
        /// rather than tucked away in a back room.
        ///
        /// Four directions rather than eight on purpose: it guarantees consecutive positions are
        /// orthogonally adjacent, which is what lets somebody shuffle forward in a straight line
        /// without any chance of clipping the corner of a doorway.
        /// </summary>
        public static CounterLine Build(TileGrid grid, Place place, int maxSlots = MaxSlots)
        {
            if (grid == null || place == null || maxSlots < 1) return null;

            var b = place.Bounds;
            var door = place.Door;
            if (b.W <= 0 || b.H <= 0) return null;
            if (!door.IsValid || !b.Contains(door) || !grid.IsWalkable(door)) return null;

            int w = b.W;
            var depth = new int[b.Area];
            var from = new int[b.Area];
            for (int i = 0; i < depth.Length; i++) { depth[i] = -1; from[i] = -1; }

            var frontier = new List<int>();
            int start = (door.Y - b.Y) * w + (door.X - b.X);
            depth[start] = 0;
            frontier.Add(start);

            int deepest = start;
            for (int head = 0; head < frontier.Count; head++)
            {
                int current = frontier[head];
                if (depth[current] > depth[deepest]) deepest = current;

                int cx = b.X + current % w;
                int cy = b.Y + current / w;

                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + Dx[d], ny = cy + Dy[d];
                    if (!b.Contains(nx, ny) || !grid.IsWalkable(nx, ny)) continue;

                    int next = (ny - b.Y) * w + (nx - b.X);
                    if (depth[next] >= 0) continue;

                    depth[next] = depth[current] + 1;
                    from[next] = current;
                    frontier.Add(next);
                }
            }

            // A doorway that opens straight onto a wall has nowhere to queue.
            if (depth[deepest] < 1) return null;

            int counterAt = depth[deepest] < maxSlots ? depth[deepest] : maxSlots;

            int node = deepest;
            while (depth[node] > counterAt) node = from[node];

            var slots = new Tile[counterAt];
            for (int k = 0; k < counterAt; k++)
            {
                slots[k] = new Tile(b.X + node % w, b.Y + node / w);
                node = from[node];
            }

            return new CounterLine(place.Id, door, slots);
        }

        public override string ToString() => $"{Count} deep at {(Count > 0 ? Counter.ToString() : "nowhere")}";
    }
}
