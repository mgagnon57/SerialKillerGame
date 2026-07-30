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
                    lines.Add(new RoadLine(run.Name, run.EffectiveClass, run.Width, run.Points));
            }
            var roads = new RoadNetwork(lines);

            // 3. places
            var places = new List<Place>(layout.Places.Count);
            var keys = new Dictionary<ulong, int>(layout.Places.Count);

            for (int i = 0; i < layout.Places.Count; i++)
            {
                var spec = layout.Places[i];
                var id = new PlaceId(i);

                if (spec.IsBuilding) StampBuilding(grid, spec);
                else StampOpenPlace(grid, spec);

                // Claim tiles for this place so "what is this person walking into" is answerable.
                for (int y = spec.Bounds.Y; y <= spec.Bounds.Bottom; y++)
                for (int x = spec.Bounds.X; x <= spec.Bounds.Right; x++)
                    grid.SetPlace(x, y, id);

                var place = new Place(id, spec.Kind, spec.Name, spec.Human,
                                      spec.Bounds, spec.Door, spec.Hours.ToArray(),
                                      spec.JobSlots, spec.Units, spec.Key);
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
