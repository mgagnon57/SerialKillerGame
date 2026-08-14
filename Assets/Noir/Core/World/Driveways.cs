using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>Where one household's car stands when nobody is driving it.</summary>
    public readonly struct Driveway
    {
        /// <summary>The building it belongs to.</summary>
        public readonly PlaceId Home;

        /// <summary>Which household inside that building — 0 for a cottage, 0..3 for a terrace.</summary>
        public readonly int Unit;

        /// <summary>The tile the car stands on. One tile is one metre.</summary>
        public readonly Tile Spot;

        /// <summary>
        /// True when the car's long axis runs east–west. A car sits nose-on to the street it
        /// serves, so this follows the front of the house and nothing else.
        /// </summary>
        public readonly bool AlongX;

        public Driveway(PlaceId home, int unit, Tile spot, bool alongX)
        {
            Home = home; Unit = unit; Spot = spot; AlongX = alongX;
        }

        public override string ToString() => $"{Home} unit {Unit} at {Spot}";
    }

    /// <summary>
    /// THE CARS THAT ARE NOT GOING ANYWHERE, WHICH IS NEARLY ALL OF THEM.
    ///
    /// IDOT counts Rossville's own streets by name: Route 1 at 5,200 AADT, Attica at 1,100, a
    /// side street at ~200. Total vehicle-miles over average speed puts about **nineteen cars
    /// moving at an average instant**. Data USA puts the village at **two cars per household**,
    /// and the map declares 624 households. So the town owns something like twelve hundred cars
    /// and is driving nineteen of them: **around 98% of Rossville's vehicles are standing at a
    /// house right now**, and the game drew exactly none of them.
    /// See docs/research/TRAFFIC-COUNTS.md for the counts and the working.
    ///
    /// WHY THIS IS IN CORE AND NOT IN THE RENDERER. `CityParking` — the only thing that had ever
    /// parked a car — is `#if UNITY_EDITOR` from its first line, because it reads the asset
    /// database. So a standalone player has no parked cars at all, and never could have. Deciding
    /// WHERE a car stands is geometry and a seed: it needs no prefab, it belongs in the 27% of
    /// this tree that has automated cover, and a player build gets it for nothing. Only the
    /// drawing is Unity's problem.
    ///
    /// AND IT IS CONTENT, NOT SCENERY. `CityParking`'s own docstring makes the argument and then
    /// stops short of the houses: *"A PARKED CAR IS CONTENT IN THIS GAME. It is a thing that was
    /// somewhere at a time and can be looked at, remembered, and found to have moved."* A car that
    /// sat in a driveway on Tuesday and is gone on Wednesday is worth more to this game than any
    /// number of cars driving past — which is why the spot is stable for a seed and the PRESENCE
    /// of the car is not decided here. Presence is a question for the clock and for whether the
    /// household's driver is away; see the away-work curve.
    /// </summary>
    public static class Driveways
    {
        /// <summary>
        /// How far out from the front wall the search will look for standing room, in metres.
        ///
        /// Rossville's lots are deep and its houses are set back — the parcel statistics put the
        /// front yard at a good deal more than a car length — but a spot further out than this is
        /// either in the road or on somebody else's lot, and neither is a driveway.
        /// </summary>
        public const int Reach = 12;

        /// <summary>
        /// How near the wall a car's SPOT - its centre, not its bumper - may stand.
        ///
        /// A car parks nose-on to the wall it faces (see <see cref="Driveway.AlongX"/>), so half
        /// its own length reaches back toward the house from this point. CityParking's own
        /// measured docstring puts the fleet at 5.0 to 5.5m nose to tail, so anything under 2.75m
        /// backs a car's tail into the siding it is supposedly standing clear of - which is
        /// exactly what a Clearance of 2 did, and it is the deciding half of "vehicles parking
        /// inside the homes". 4 leaves a working margin against a car this project has not
        /// measured yet, the same reason CityParking measures Width/Long off the mesh rather than
        /// trusting a guessed number - and Rossville's lots are deep enough that it costs nothing.
        /// </summary>
        private const int Clearance = 4;

        /// <summary>
        /// Metres between two cars on the same frontage. A car is about 1.8 m wide, so this is a
        /// door's width of daylight between neighbours — which is what a terrace's parking looks
        /// like and, more usefully, keeps two of them from being the same tile.
        /// </summary>
        private const int Apart = 3;

        /// <summary>
        /// One spot per household, for every home on the map that can find room for one.
        ///
        /// Deterministic for a seed: the substream is its own, so adding a system elsewhere does
        /// not move anybody's car. A household that cannot find standing room is simply absent
        /// from the result rather than being given a bad spot — a car in a hedge is worse than no
        /// car, and the count is reported by the tests.
        /// </summary>
        public static Driveway[] Plan(WorldModel world, ulong seed)
        {
            if (world == null) return Array.Empty<Driveway>();

            var rng = Xoshiro256ss.Substream(seed, "driveways");
            var found = new List<Driveway>();
            var taken = new HashSet<Tile>();

            foreach (var id in world.Homes)
            {
                var place = world.GetPlace(id);
                if (place == null) continue;

                Facing(world, place, out int nx, out int ny);
                int units = Math.Max(1, place.Units);

                for (int unit = 0; unit < units; unit++)
                {
                    var spot = Standing(world, place, nx, ny, unit, units, taken, rng);
                    if (!spot.IsValid) continue;

                    taken.Add(spot);
                    found.Add(new Driveway(id, unit, spot, nx != 0));
                }
            }

            return found.ToArray();
        }

        /// <summary>
        /// Which way this building faces, as an outward unit normal.
        ///
        /// The authored door decides it where there is one — that is the door on the street even
        /// in a building that turned out to have several, which is the same ruling `Frontage`
        /// makes. Where there is none, the front is whichever way finds a road first, because a
        /// house with no recorded door still has a street.
        /// </summary>
        private static void Facing(WorldModel world, Place place, out int nx, out int ny)
        {
            var b = place.Bounds;

            if (place.Door.IsValid)
            {
                int left = Math.Abs(place.Door.X - b.Left);
                int right = Math.Abs(b.Right - place.Door.X);
                int top = Math.Abs(place.Door.Y - b.Top);
                int bottom = Math.Abs(b.Bottom - place.Door.Y);

                int best = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
                if (best == top) { nx = 0; ny = -1; return; }
                if (best == bottom) { nx = 0; ny = 1; return; }
                if (best == left) { nx = -1; ny = 0; return; }
                nx = 1; ny = 0; return;
            }

            // No door: face the nearest road. Ties break in a fixed order, so this is still
            // reproducible for a seed.
            int bestReach = int.MaxValue;
            nx = 0; ny = -1;

            var ways = new[] { (0, -1), (0, 1), (-1, 0), (1, 0) };
            foreach (var (dx, dy) in ways)
            {
                int reach = ToRoad(world, b, dx, dy);
                if (reach >= bestReach) continue;
                bestReach = reach; nx = dx; ny = dy;
            }
        }

        /// <summary>How many metres from this face to the first road tile, or int.MaxValue.</summary>
        private static int ToRoad(WorldModel world, TileRect b, int dx, int dy)
        {
            var from = Edge(b, Tile.None, dx, dy, 0, 1);
            for (int step = 1; step <= Reach * 2; step++)
            {
                int x = from.X + dx * step, y = from.Y + dy * step;
                if (!world.Grid.InBounds(x, y)) return int.MaxValue;
                if (world.Grid.TerrainAt(x, y) == Terrain.Road) return step;
            }
            return int.MaxValue;
        }

        /// <summary>
        /// The point on the given face where household <paramref name="unit"/> of
        /// <paramref name="units"/> sits, spread along the frontage so a terrace's cars stand
        /// side by side rather than on top of each other.
        ///
        /// A ONE-CAR HOUSE USED TO BE CENTRED ON THE WHOLE FACE, and an ordinary frame house's
        /// door is itself roughly in the middle of its own frontage - so the search line this
        /// starts from ran straight out of the doorway, and 555 of 599 single-unit homes in
        /// Rossville ended up with their one car parked 2-4 tiles dead ahead of their own front
        /// door. A single household is walked off the DOOR's own line by half a terrace gap
        /// instead, toward whichever end of the face leaves more room - the multi-unit case is
        /// untouched, because a terrace's own doors are already spread along the face and Apart
        /// already keeps their cars apart.
        /// </summary>
        private static Tile Edge(TileRect b, Tile door, int nx, int ny, int unit, int units)
        {
            if (units == 1 && door.IsValid)
            {
                if (ny != 0)
                {
                    int y = ny < 0 ? b.Top : b.Bottom;
                    return new Tile(OffDoor(door.X, b.Left, b.Right), y);
                }
                int atX = nx < 0 ? b.Left : b.Right;
                return new Tile(atX, OffDoor(door.Y, b.Top, b.Bottom));
            }

            // Along the face, centred: unit 0 of 1 is the middle of the frontage.
            float middle = (units - 1) / 2f;
            int slide = (int)Math.Round((unit - middle) * Apart);

            if (ny != 0)
            {
                int y = ny < 0 ? b.Top : b.Bottom;
                return new Tile(b.X + b.W / 2 + slide, y);
            }

            int x = nx < 0 ? b.Left : b.Right;
            return new Tile(x, b.Y + b.H / 2 + slide);
        }

        /// <summary>Along the face, half a terrace gap clear of the door - toward whichever end
        /// of the face (<paramref name="lo"/>..<paramref name="hi"/>) leaves the most room,
        /// clamped so a narrow frontage never pushes the spot past its own wall.</summary>
        private static int OffDoor(int door, int lo, int hi)
        {
            int shift = (hi - door) >= (door - lo) ? Apart : -Apart;
            int at = door + shift;
            return Math.Max(lo, Math.Min(hi, at));
        }

        /// <summary>
        /// The first tile out from the front wall a car could actually stand on.
        ///
        /// Walks outward and takes the first square that is on the map, outside every building,
        /// off the carriageway, and not already somebody else's. It gives up rather than settle:
        /// a house whose frontage is entirely road, water or wall keeps no car at the front, which
        /// is true of the ones on the square.
        /// </summary>
        private static Tile Standing(WorldModel world, Place place, int nx, int ny,
                                     int unit, int units, HashSet<Tile> taken, IRng rng)
        {
            var from = Edge(place.Bounds, place.Door, nx, ny, unit, units);

            // A little jitter along the frontage so a street of identical lots does not read as a
            // car showroom. One metre either way, which is inside the gap Apart already leaves.
            int wobble = rng.NextInt(-1, 2);

            for (int step = Clearance; step <= Reach; step++)
            {
                int x = from.X + nx * step + (nx == 0 ? wobble : 0);
                int y = from.Y + ny * step + (ny == 0 ? wobble : 0);

                if (!world.Grid.InBounds(x, y)) return Tile.None;

                var ground = world.Grid.TerrainAt(x, y);

                // Reached the street without finding room - there is no driveway here.
                if (ground == Terrain.Road) return Tile.None;
                if (ground == Terrain.Water || ground == Terrain.Wall || ground == Terrain.Floor)
                    continue;

                var spot = new Tile(x, y);
                if (world.Grid.PlaceAt(x, y).IsValid) continue;   // inside somebody's building
                if (taken.Contains(spot)) continue;

                return spot;
            }

            return Tile.None;
        }
    }
}
