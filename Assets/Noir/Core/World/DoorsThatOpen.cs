using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// MOVES A DOOR THAT OPENS INTO A NEIGHBOUR'S WALL ROUND TO A SIDE OF THE BUILDING THAT HAS
    /// GROUND OUTSIDE IT.
    ///
    /// Nine doors in Rossville could not be walked through on 2026-08-09, and the validator could
    /// only say so - "is not walkable", "is cut off from the rest of the village" - which reads
    /// like nine separate faults and is one. Every one of them had ANOTHER BUILDING standing flat
    /// against its outside face, and they came in mutual pairs, each sealing the other:
    ///
    ///     501 Mckibben Ave   1327,1813 13x7    ends at y 1819
    ///     316 Mc Kibben St   1322,1820 13x20   begins at y 1820
    ///
    /// They touch exactly. There is no gap and no doorstep, so 501's door opens into 316's back
    /// wall and 316's opens into 501's. The same shape at 401/403 Dale, 502 Stewart against 400
    /// South Grove, 502 Mckibben against 315 Mc Kibben, and 201 against 109 Perry.
    ///
    /// WHY THE DOOR MOVES AND NOT THE BUILDING. Where a house stands is survey; which wall its
    /// front door is in is not. `SeatOnSurvey` places a building on the county's own lot and
    /// `ClearOfRoads` may then shove it off a corridor, and both carry the door along at its
    /// authored offset - which is right, because a door is part of a building. What neither can
    /// carry is the ground outside the wall. Moving the building to free its door would be moving
    /// a measured thing to satisfy an authored one, and SOURCES-OF-TRUTH.md is explicit about
    /// which of those wins.
    ///
    /// TOWARDS THE ROAD, because that is what a front door is. Of the faces that have open ground
    /// outside them, this takes the one whose outside is nearest a road, and the tile on that face
    /// nearest the road. A house whose authored door faced a neighbour ends up facing the street,
    /// which is where it should have faced.
    ///
    /// IT TOUCHES NOTHING THAT WORKS. A door with any open tile beside it is left exactly where it
    /// was, so this is a no-op on the six hundred doors that are fine and on every map that has no
    /// abutting buildings at all.
    /// </summary>
    public static class DoorsThatOpen
    {
        /// <summary>
        /// Re-faces every door the BUILT WORLD says is broken. Returns how many moved, and how
        /// many could not be helped because the building has no wall with reachable ground
        /// outside it.
        ///
        /// IT TAKES THE WORLD AND NOT JUST THE LAYOUT, and that is the whole correctness of it.
        /// A PlaceSpec's Bounds is its LOT, not its masonry - the walls are inset - so asking the
        /// layout "is another place's rectangle over my doorstep" says yes for NINETY doors on
        /// this map while only NINE are actually bricked up. Both readings drive the validator to
        /// zero; only one of them leaves the other eighty-one front doors where their owners put
        /// them. Ask the grid what is really there.
        ///
        /// The caller has to rebuild afterwards - a door is carved into the grid when the world is
        /// built, so a door that has moved is not in the grid this was handed.
        /// </summary>
        public static int Apply(VillageLayout layout, WorldModel world, out int walledIn)
        {
            walledIn = 0;
            if (layout == null || world == null) return 0;

            var grid = world.Grid;

            // THE VALIDATOR'S OWN LABELLING, not a second opinion. A pass that fixes doors and a
            // gate that fails them have to agree on what "reachable" means.
            var region = WorldValidator.Regions(grid, out _, out _, out _);
            int main = WorldValidator.MainRegion(grid, region);

            var table = PlaceKindTable.Current;
            var roads = new RoadCorridor.Corridors(layout);

            int moved = 0;
            foreach (var p in layout.Places)
            {
                if (!p.Door.IsValid) continue;
                if (!table.Row(p.Kind).IsBuilding) continue;
                if (Works(grid, region, main, p.Door)) continue;

                if (TryFace(grid, region, main, roads, p, out var door))
                {
                    p.Door = door;
                    moved++;
                }
                else walledIn++;
            }
            return moved;
        }

        /// <summary>
        /// The same two questions `WorldValidator` asks of a door, and no others: can it be stood
        /// on, and is it joined to the rest of the town.
        /// </summary>
        private static bool Works(TileGrid grid, int[] region, int main, Tile door)
        {
            if (!grid.InBounds(door)) return false;
            if (!grid.IsWalkable(door)) return false;
            return region[grid.Index(door)] == main;
        }


        /// <summary>The eight neighbours WalkableRegions floods on.</summary>
        private static readonly (int dx, int dy)[] Sides =
            { (0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1) };

        /// <summary>
        /// The tile on this building's perimeter with the town on the other side of it, nearest a
        /// road. Walks the whole perimeter rather than the four midpoints, so a door can land at
        /// the end of a wall a neighbour only partly covers.
        ///
        /// TOWARDS THE ROAD, because that is what a front door is. A house whose authored door
        /// faced a neighbour's back wall ends up facing the street, which is where it should have
        /// faced.
        ///
        /// It cannot check the new door tile itself: that tile is masonry until the world is
        /// rebuilt with the door on it. What it can check, and what actually matters, is that the
        /// ground OUTSIDE is the town.
        /// </summary>
        private static bool TryFace(TileGrid grid, int[] region, int main,
                                    RoadCorridor.Corridors roads, PlaceSpec p, out Tile best)
        {
            best = p.Door;
            float nearest = float.MaxValue;
            bool found = false;

            var b = p.Bounds;
            for (int y = b.Y; y < b.Y + b.H; y++)
            for (int x = b.X; x < b.X + b.W; x++)
            {
                bool edge = x == b.X || x == b.X + b.W - 1 || y == b.Y || y == b.Y + b.H - 1;
                if (!edge) continue;

                bool onTheTown = false;
                foreach (var (dx, dy) in Sides)
                {
                    var t = new Tile(x + dx, y + dy);
                    if (b.Contains(t)) continue;                 // its own inside is not outside
                    if (!grid.InBounds(t)) continue;
                    if (!grid.IsWalkable(t)) continue;
                    if (region[grid.Index(t)] != main) continue;
                    onTheTown = true;
                    break;
                }
                if (!onTheTown) continue;

                float d = roads.DistanceTo(x + 0.5f, y + 0.5f);
                if (d >= nearest) continue;

                nearest = d;
                best = new Tile(x, y);
                found = true;
            }

            return found;
        }
    }
}
