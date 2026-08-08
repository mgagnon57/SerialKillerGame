using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// Turns an authored layout into a rasterised world. Deterministic and order-dependent:
    /// terrain is painted first, then roads over it, then buildings over those - exactly as
    /// written in the file, so the author can reason about overlaps by reading top to bottom.
    /// </summary>
    public static class WorldBuilder
    {
        public static WorldModel Build(VillageLayout layout) => Build(layout, 1979);

        public static WorldModel Build(VillageLayout layout, ulong seed)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));

            var grid = new TileGrid(layout.Width, layout.Height);
            var rooms = new List<Room>();
            var furniture = new List<Furniture>();

            // 1. terrain patches, in declaration order
            foreach (var patch in layout.Terrain)
                FillRect(grid, patch.Area, patch.Kind);

            // 2. roads and footpaths. Kept as well as painted: rasterising a road into terrain
            //    loses which corridor a tile belonged to and how wide it was, and streets,
            //    traffic and signals all need that back. See RoadNetwork.
            var lines = new List<RoadLine>();
            foreach (var run in layout.Roads)
            {
                StrokePolyline(grid, run);
                if (run.Kind == Terrain.Road)
                    lines.Add(new RoadLine(run.Name, run.EffectiveClass, run.Width, run.Points,
                                           run.EffectiveCarries, run.Easement, run.Aadt));
            }
            var roads = new RoadNetwork(lines);

            // 3. places
            var places = new List<Place>(layout.Places.Count);
            var keys = new Dictionary<ulong, int>(layout.Places.Count);

            for (int i = 0; i < layout.Places.Count; i++)
            {
                var spec = layout.Places[i];
                var id = new PlaceId(i);

                // A SHAPED BUILDING IS STAMPED AS A RECTANGLE AND THEN CUT BACK TO ITS OUTLINE,
                // so every step below - units, interiors, doorways, furniture - goes on working
                // in rectangles and only the silhouette changes. The ground under the rectangle
                // is kept first, because the corners that turn out not to be the building have to
                // go back to being whatever they were.
                bool shaped = spec.Outline != null && spec.Outline.Length >= 3
                              && Inside(spec.Outline, spec.Door);
                Terrain[] under = null; TileFlags[] underFlags = null;
                if (shaped) SnapshotUnder(grid, spec.Bounds, out under, out underFlags);

                if (spec.IsBuilding) StampBuilding(grid, spec);
                else StampOpenPlace(grid, spec);

                // Claim tiles for this place so "what is this person walking into" is answerable.
                for (int y = spec.Bounds.Y; y <= spec.Bounds.Bottom; y++)
                for (int x = spec.Bounds.X; x <= spec.Bounds.Right; x++)
                    if (!shaped || Inside(spec.Outline, x, y))
                        grid.SetPlace(x, y, id);

                var place = new Place(id, spec.Kind, spec.Name, spec.Human,
                                      spec.Bounds, spec.Door, spec.Hours.ToArray(),
                                      spec.JobSlots, spec.Units, spec.Key,
                                      shaped ? spec.Outline : null);
                places.Add(place);

                if (keys.TryGetValue(place.Key, out int clash))
                    throw new InvalidOperationException(
                        $"'{place.KeySource}' names two places ({clash} and {i}). Everything inside " +
                        "a building is generated from its key, so two places sharing one would be " +
                        "two copies of the same interior. Give one of them a `key` line.");
                keys.Add(place.Key, i);

                // A stream per building, named after the building. Threading one stream through
                // the whole file is what made adding a shop rewrite the village.
                if (spec.IsBuilding && PlaceKindTable.Current.Row(spec.Kind).Rooms.Any)
                    StampUnits(grid, spec, place, rooms, furniture,
                               Xoshiro256ss.Substream(seed, "interior:" + place.KeySource));

                // Last, so it cuts the finished building rather than a half-built one.
                if (shaped) MaskToOutline(grid, spec, under, underFlags);
            }

            // Loud rather than silently wrong. Neither ceiling is reachable today; the point is
            // that the build stops if one is ever crossed instead of quietly writing tiles that
            // read back as belonging to nothing.
            if (rooms.Count > TileGrid.MaxId)
                throw new InvalidOperationException(
                    $"{rooms.Count} rooms is more than a tile can record ({TileGrid.MaxId})");
            if (places.Count > TileGrid.MaxId)
                throw new InvalidOperationException(
                    $"{places.Count} places is more than a tile can record ({TileGrid.MaxId})");

            SealWalkablePockets(grid);

            var placeArray = places.ToArray();
            var props = PropGenerator.Generate(grid, placeArray, seed);

            return new WorldModel(layout.Name, grid, placeArray,
                                  rooms.ToArray(), furniture.ToArray(), props.ToArray(), roads);
        }

        /// <summary>
        /// Lay rooms into a building: fill the interior with walls, then carve the rooms out of
        /// it and punch the doorways. Working subtractively means the one-tile gaps the
        /// subdivision leaves BECOME the internal walls, with no separate wall-drawing pass.
        /// </summary>
        /// <summary>
        /// Lay out every home in a building.
        ///
        /// One unit is a house. Several units means a terrace or a block of flats: the
        /// footprint is sliced along its longer axis, each slice gets its own front door onto
        /// the street and its own independent interior. Slicing the long way is what makes a
        /// terrace look like a terrace - narrow and deep, front to back.
        /// </summary>
        private static void StampUnits(TileGrid grid, PlaceSpec spec, Place place,
                                       List<Room> rooms, List<Furniture> furniture, IRng rng)
        {
            var placeId = place.Id;
            int units = spec.Units < 1 ? 1 : spec.Units;
            if (units == 1) { StampInterior(grid, spec, spec.Bounds, spec.Door, placeId, rooms, furniture, rng); return; }

            var b = spec.Bounds;
            bool sliceVertically = b.W >= b.H;
            int span = sliceVertically ? b.W : b.H;

            // Each home needs walls plus somewhere to stand. Below that, quietly build fewer.
            int minSlice = 5;
            if (span / units < minSlice) units = System.Math.Max(1, span / minSlice);

            // Which side the front doors are on: the same side the authored door is on, so a
            // terrace faces the street rather than its own back garden.
            bool doorOnTop = spec.Door.IsValid && spec.Door.Y == b.Y;
            bool doorOnLeft = spec.Door.IsValid && spec.Door.X == b.X;

            int offset = 0;
            for (int u = 0; u < units; u++)
            {
                int size = (span - offset) / (units - u);
                TileRect slice = sliceVertically
                    ? new TileRect(b.X + offset, b.Y, size, b.H)
                    : new TileRect(b.X, b.Y + offset, b.W, size);
                offset += size;

                // A door in the middle of this unit's street frontage - except for the unit
                // that contains the authored door, which keeps it. Otherwise the door written
                // in village.txt gets walled over and the validator quite rightly complains
                // that the building has no way in.
                Tile door;
                if (spec.Door.IsValid && slice.Contains(spec.Door))
                {
                    door = spec.Door;
                }
                else if (sliceVertically)
                {
                    door = new Tile(slice.X + slice.W / 2, doorOnTop ? slice.Y : slice.Bottom);
                }
                else
                {
                    door = new Tile(doorOnLeft ? slice.X : slice.Right, slice.Y + slice.H / 2);
                }

                StampInterior(grid, spec, slice, door, placeId, rooms, furniture, rng);
            }
        }

        private static void StampInterior(TileGrid grid, PlaceSpec spec, TileRect bounds, Tile frontDoor,
                                          PlaceId placeId, List<Room> rooms, List<Furniture> furniture,
                                          IRng rng)
        {
            // The kind names its grammar and InteriorGenerator finds it. Which grammar a shop
            // uses is content; how that grammar works is code; this line is the only place the
            // two have to meet.
            // Grammar is how the building is arranged; the kind's own name is the PROGRAMME,
            // which is what its rooms are for. A hospital and a school are the same corridor
            // with different things off it, so passing the name is what gets wards in one and
            // classrooms in the other without a second column saying so.
            var plan = PlaceKindTable.Current.Row(spec.Kind);
            var interior = InteriorGenerator.Generate(bounds, frontDoor, rng, plan.Grammar, plan.Name);
            if (interior.Rooms.Count == 0) return;

            int firstRoom = rooms.Count;

            var wallFlags = TileGrid.FlagsFor(Terrain.Wall);
            var floorFlags = TileGrid.FlagsFor(Terrain.Floor);

            // The unit starts entirely solid - INCLUDING its own perimeter - and rooms are
            // carved back out of it.
            //
            // Filling the perimeter too is what gives a terrace its party walls. Skipping it
            // left the boundary between adjacent homes as bare floor, so the four houses in
            // Ash Terrace were one open building you could walk straight through. A shared
            // wall you cannot pass is the entire difference between neighbours and flatmates.
            for (int y = bounds.Y; y <= bounds.Bottom; y++)
            for (int x = bounds.X; x <= bounds.Right; x++)
                grid.Set(x, y, Terrain.Wall, wallFlags);

            foreach (var (roomBounds, kind) in interior.Rooms)
            {
                var roomId = new RoomId(rooms.Count);
                for (int y = roomBounds.Y; y <= roomBounds.Bottom; y++)
                for (int x = roomBounds.X; x <= roomBounds.Right; x++)
                {
                    grid.Set(x, y, Terrain.Floor, floorFlags);
                    grid.SetRoom(x, y, roomId);
                }
                rooms.Add(new Room(roomId, placeId, kind, roomBounds, roomBounds.Centre));
            }

            // Doorways between rooms.
            foreach (var door in interior.Doors)
                grid.Set(door.X, door.Y, Terrain.Floor, floorFlags);

            // The front door must still open into something. If the room behind it got walled
            // off by the subdivision, punch straight through to the nearest floor.
            ConnectFrontDoor(grid, bounds, frontDoor, floorFlags);

            // Furnish, once the doorways are known - nothing may be placed against a door.
            var doorKeys = new HashSet<int>();
            foreach (var door in interior.Doors) doorKeys.Add(door.Y * grid.Width + door.X);
            if (frontDoor.IsValid) doorKeys.Add(frontDoor.Y * grid.Width + frontDoor.X);

            for (int i = firstRoom; i < rooms.Count; i++)
                FurniturePlacer.Place(rooms[i], doorKeys, grid.Width, furniture);
        }

        private static void ConnectFrontDoor(TileGrid grid, TileRect bounds, Tile door,
                                             TileFlags floorFlags)
        {
            if (!door.IsValid) return;

            grid.Set(door.X, door.Y, Terrain.Floor, floorFlags);

            // Step inward from the doorway until we reach floor, opening the way as we go.
            int dx = 0, dy = 0;
            if (door.X == bounds.X) dx = 1;
            else if (door.X == bounds.Right) dx = -1;
            else if (door.Y == bounds.Y) dy = 1;
            else if (door.Y == bounds.Bottom) dy = -1;
            else return;   // not on the perimeter; nothing to do

            for (int step = 1; step <= Math.Max(bounds.W, bounds.H); step++)
            {
                int x = door.X + dx * step;
                int y = door.Y + dy * step;
                if (!bounds.Contains(x, y)) break;
                if (grid.TerrainAt(x, y) == Terrain.Floor) break;
                grid.Set(x, y, Terrain.Floor, floorFlags);
            }
        }

        private static void FillRect(TileGrid grid, TileRect r, Terrain kind)
        {
            var flags = TileGrid.FlagsFor(kind);
            for (int y = r.Y; y <= r.Bottom; y++)
            for (int x = r.X; x <= r.Right; x++)
                grid.Set(x, y, kind, flags);
        }

        /// <summary>
        /// A building is a walled perimeter with a floor inside, and one tile of the wall
        /// replaced by a door. Single-tile-thin buildings degrade gracefully to all floor.
        /// </summary>
        private static void StampBuilding(TileGrid grid, PlaceSpec spec)
        {
            var b = spec.Bounds;
            var wallFlags = TileGrid.FlagsFor(Terrain.Wall);
            var floorFlags = TileGrid.FlagsFor(Terrain.Floor);

            for (int y = b.Y; y <= b.Bottom; y++)
            for (int x = b.X; x <= b.Right; x++)
            {
                bool perimeter = (x == b.X || x == b.Right || y == b.Y || y == b.Bottom);
                if (perimeter && b.W > 2 && b.H > 2)
                    grid.Set(x, y, Terrain.Wall, wallFlags);
                else
                    grid.Set(x, y, Terrain.Floor, floorFlags);
            }

            // Carve the doorway.
            if (spec.Door.IsValid)
                grid.Set(spec.Door.X, spec.Door.Y, Terrain.Floor, floorFlags);
        }

        /// <summary>
        /// Ground nobody can reach, closed off.
        ///
        /// A handful of tiles get sealed behind whatever gets stamped over them - a shaped
        /// building's walls closing round a strip of the garden it did not cover, a doorway that
        /// lands against a neighbour. Two tiles of grass behind a house harms nothing on screen
        /// and breaks the one invariant the town's routing rests on: that the walkable map is a
        /// single piece. It was five pieces the first time anybody measured the built town, and
        /// the four strays were 2, 3, 3 and 6 tiles.
        ///
        /// A NET AT THE END, NOT A FIX AT EACH SOURCE. Anything that stamps terrain can strand a
        /// pocket, and the ways to do it are not enumerable - so this catches them all, once,
        /// where the grid is finally complete.
        ///
        /// ONLY SMALL ONES. A big cut-off region is a real fault - a quarter of the town behind an
        /// impassable crossing - and quietly filling it in would hide exactly the thing worth
        /// knowing. Those are left alone for the validator to report.
        /// </summary>
        private const int SealPocketsUpTo = 64;

        private static void SealWalkablePockets(TileGrid grid)
        {
            // The same flood WorldValidator counts regions with - eight ways, and no cutting the
            // diagonal between two blocked corners. Written out here rather than shared, because
            // WalkableRegions lives in Noir.Core.Sim and this assembly cannot see it; the two must
            // agree or this would seal ground the validator still counts, or leave ground it does
            // not, and either way the report would stop matching the map.
            var region = new int[grid.Count];
            for (int i = 0; i < region.Length; i++) region[i] = -1;

            var sizes = new List<int>();
            var stack = new Stack<Tile>();

            for (int start = 0; start < grid.Count; start++)
            {
                var first = grid.TileAt(start);
                if (!grid.IsWalkable(first) || region[start] >= 0) continue;

                int id = sizes.Count, size = 0;
                region[start] = id;
                stack.Push(first);

                while (stack.Count > 0)
                {
                    var t = stack.Pop();
                    size++;
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = t.X + dx, ny = t.Y + dy;
                        if (!grid.InBounds(nx, ny) || !grid.IsWalkable(nx, ny)) continue;
                        if (dx != 0 && dy != 0 &&
                            !grid.IsWalkable(t.X + dx, t.Y) && !grid.IsWalkable(t.X, t.Y + dy))
                            continue;

                        int ni = grid.Index(nx, ny);
                        if (region[ni] >= 0) continue;
                        region[ni] = id;
                        stack.Push(new Tile(nx, ny));
                    }
                }
                sizes.Add(size);
            }

            if (sizes.Count <= 1) return;

            int largest = 0;
            for (int i = 1; i < sizes.Count; i++) if (sizes[i] > sizes[largest]) largest = i;

            var wallFlags = TileGrid.FlagsFor(Terrain.Wall);
            for (int i = 0; i < region.Length; i++)
            {
                int r = region[i];
                if (r < 0 || r == largest || sizes[r] > SealPocketsUpTo) continue;
                var t = grid.TileAt(i);
                grid.Set(t.X, t.Y, Terrain.Wall, wallFlags);
            }
        }

        // ---- shaped buildings ---------------------------------------------------------------
        //
        // See PlaceSpec.Outline. A building whose real footprint has been measured is stamped as
        // its bounding rectangle, in the ordinary way, and then everything outside the outline is
        // put back to the ground it was standing on. That order is deliberate: the interior
        // generator, the unit slicing, the doorways and the furniture all keep working in
        // rectangles, which is what they were written for and what the tests cover.

        private static void SnapshotUnder(TileGrid grid, TileRect b,
                                          out Terrain[] terrain, out TileFlags[] flags)
        {
            terrain = new Terrain[b.W * b.H];
            flags = new TileFlags[b.W * b.H];
            for (int y = 0; y < b.H; y++)
            for (int x = 0; x < b.W; x++)
            {
                terrain[y * b.W + x] = grid.TerrainAt(b.X + x, b.Y + y);
                flags[y * b.W + x] = grid.FlagsAt(b.X + x, b.Y + y);
            }
        }

        private static bool Inside(Tile[] ring, Tile t) => t.IsValid && Inside(ring, t.X, t.Y);

        /// <summary>Even-odd crossings, tested at the tile's own centre so a tile is either in
        /// the building or out of it and never half.</summary>
        private static bool Inside(Tile[] ring, int tx, int ty)
        {
            if (ring == null || ring.Length < 3) return true;
            float px = tx + 0.5f, py = ty + 0.5f;
            bool inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                float ax = ring[i].X, ay = ring[i].Y, bx = ring[j].X, by = ring[j].Y;
                if ((ay > py) != (by > py) && px < (bx - ax) * (py - ay) / (by - ay) + ax)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// Cut a stamped rectangle back to the building's real outline.
        ///
        /// Three passes, and the third is the one that makes this safe to do at all:
        ///
        ///   OUT. Every tile outside the outline goes back to the ground that was under it.
        ///
        ///   SEAL. A room the outline cut through is now open to the street, so any floor that
        ///   has come to touch the outside becomes wall. Without this a house with an L-shaped
        ///   footprint has its back rooms standing in the garden with no wall between.
        ///
        ///   REACH. Cutting the shape can strand part of the interior behind the new wall. Every
        ///   floor tile that cannot be walked to from the front door becomes wall too, so the
        ///   building is always one connected inside with one way in - which is the property the
        ///   town's own walkability test rests on, and it is guaranteed here rather than hoped
        ///   for. Solid is a worse building; disconnected is a broken town.
        /// </summary>
        private static void MaskToOutline(TileGrid grid, PlaceSpec spec,
                                          Terrain[] under, TileFlags[] underFlags)
        {
            var b = spec.Bounds;
            var ring = spec.Outline;
            var wallFlags = TileGrid.FlagsFor(Terrain.Wall);
            var floorFlags = TileGrid.FlagsFor(Terrain.Floor);

            var inside = new bool[b.W * b.H];
            for (int y = 0; y < b.H; y++)
            for (int x = 0; x < b.W; x++)
            {
                bool inb = Inside(ring, b.X + x, b.Y + y);
                inside[y * b.W + x] = inb;
                if (!inb)
                    grid.Set(b.X + x, b.Y + y, under[y * b.W + x], underFlags[y * b.W + x]);
            }

            for (int y = 0; y < b.H; y++)
            for (int x = 0; x < b.W; x++)
            {
                if (!inside[y * b.W + x]) continue;
                if (grid.TerrainAt(b.X + x, b.Y + y) != Terrain.Floor) continue;
                if (Edge(inside, b, x, y))
                    grid.Set(b.X + x, b.Y + y, Terrain.Wall, wallFlags);
            }

            // The door is on the outline - SeatOnSurvey puts it there, and a spec whose door is
            // not inside its own outline never gets here at all. Open it and walk in.
            grid.Set(spec.Door.X, spec.Door.Y, Terrain.Floor, floorFlags);
            ConnectFrontDoor(grid, b, spec.Door, floorFlags);

            var reached = new bool[b.W * b.H];
            var queue = new Queue<int>();
            int start = (spec.Door.Y - b.Y) * b.W + (spec.Door.X - b.X);
            if (start < 0 || start >= reached.Length) return;
            reached[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                int ax = at % b.W, ay = at / b.W;
                for (int d = 0; d < 4; d++)
                {
                    int nx = ax + (d == 0 ? 1 : d == 1 ? -1 : 0);
                    int ny = ay + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= b.W || ny >= b.H) continue;
                    int n = ny * b.W + nx;
                    if (reached[n]) continue;
                    if (grid.TerrainAt(b.X + nx, b.Y + ny) != Terrain.Floor) continue;
                    reached[n] = true;
                    queue.Enqueue(n);
                }
            }

            for (int y = 0; y < b.H; y++)
            for (int x = 0; x < b.W; x++)
            {
                int i = y * b.W + x;
                if (reached[i] || !inside[i]) continue;
                if (grid.TerrainAt(b.X + x, b.Y + y) == Terrain.Floor)
                    grid.Set(b.X + x, b.Y + y, Terrain.Wall, wallFlags);
            }
        }

        /// <summary>Whether this inside-tile has the outside orthogonally next to it.</summary>
        private static bool Edge(bool[] inside, TileRect b, int x, int y)
        {
            if (x == 0 || y == 0 || x == b.W - 1 || y == b.H - 1) return true;
            return !inside[y * b.W + x + 1] || !inside[y * b.W + x - 1]
                || !inside[(y + 1) * b.W + x] || !inside[(y - 1) * b.W + x];
        }

        /// <summary>The green, a playground, a churchyard: bounded but open ground.</summary>
        private static void StampOpenPlace(TileGrid grid, PlaceSpec spec) =>
            FillRect(grid, spec.Bounds, PlaceKindTable.Current.Row(spec.Kind).Ground);

        /// <summary>
        /// Rasterise a polyline with thickness. Uses a simple stepped walk rather than Bresenham
        /// so that a road of even width lands symmetrically and corners fill in without gaps.
        /// </summary>
        private static void StrokePolyline(TileGrid grid, RoadRun run)
        {
            var flags = TileGrid.FlagsFor(run.Kind);

            // An alley is carriageway like any other - same walkability, same speed - but it is
            // marked, because who USES it is not the same. See TileFlags.Alley.
            if (run.Kind == Terrain.Road && run.EffectiveClass == RoadClass.Alley)
                flags |= TileFlags.Alley;

            int half = run.Width / 2;
            int extra = run.Width % 2 == 0 ? 0 : 1;

            for (int seg = 0; seg + 1 < run.Points.Count; seg++)
            {
                Tile a = run.Points[seg];
                Tile b = run.Points[seg + 1];

                int dx = b.X - a.X, dy = b.Y - a.Y;
                int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (steps == 0) steps = 1;

                for (int s = 0; s <= steps; s++)
                {
                    // Integer-rounded interpolation - no floats, so no drift.
                    int px = a.X + (dx * s + (dx >= 0 ? steps / 2 : -steps / 2)) / steps;
                    int py = a.Y + (dy * s + (dy >= 0 ? steps / 2 : -steps / 2)) / steps;

                    for (int oy = -half; oy < half + extra; oy++)
                    for (int ox = -half; ox < half + extra; ox++)
                        grid.Set(px + ox, py + oy, run.Kind, flags);
                }
            }
        }
    }
}
