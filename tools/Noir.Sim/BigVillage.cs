using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Sim
{
    /// <summary>
    /// Ashcombe repeated in a grid, as a stand-in for a map we have not authored yet.
    ///
    /// Costs that are invisible at 170x120 are the ones that decide whether the engine can ever
    /// hold a town, and the only honest way to find them is to run the real content on a bigger
    /// map rather than to reason about it. Tiling the authored village keeps every other
    /// variable fixed — the same buildings, the same hours, the same population density — so a
    /// difference between one copy and four is a difference of scale and nothing else.
    /// </summary>
    public static class BigVillage
    {
        /// <summary>
        /// <paramref name="tiles"/> copies each way: 2 gives four Ashcombes and about four times
        /// the people.
        /// </summary>
        public static VillageLayout Repeat(VillageLayout one, int tiles)
        {
            if (tiles <= 1) return one;

            var big = new VillageLayout
            {
                Name = one.Name + " x" + tiles,
                Width = one.Width * tiles,
                Height = one.Height * tiles,
            };

            for (int j = 0; j < tiles; j++)
            for (int i = 0; i < tiles; i++)
            {
                int ox = i * one.Width, oy = j * one.Height;
                string tag = " [" + i + "," + j + "]";

                foreach (var patch in one.Terrain)
                    big.Terrain.Add(new TerrainPatch { Kind = patch.Kind, Area = Shift(patch.Area, ox, oy) });

                foreach (var road in one.Roads)
                {
                    var run = new RoadRun { Kind = road.Kind, Width = road.Width };
                    foreach (var p in road.Points) run.Points.Add(new Tile(p.X + ox, p.Y + oy));
                    big.Roads.Add(run);
                }

                foreach (var place in one.Places)
                {
                    var copy = new PlaceSpec
                    {
                        Kind = place.Kind,
                        Name = place.Name + tag,
                        Human = place.Human,
                        Bounds = Shift(place.Bounds, ox, oy),
                        Door = place.Door.IsValid ? new Tile(place.Door.X + ox, place.Door.Y + oy) : Tile.None,
                        JobSlots = place.JobSlots,
                        Units = place.Units,
                    };
                    foreach (var h in place.Hours) copy.Hours.Add(h);
                    big.Places.Add(copy);
                }
            }

            Bridge(big, one.Width, one.Height, tiles);
            return big;
        }

        private static TileRect Shift(TileRect r, int dx, int dy) =>
            new TileRect(r.X + dx, r.Y + dy, r.W, r.H);

        /// <summary>
        /// Roads across every seam, because Ashcombe has a river along its southern edge and
        /// four copies stacked without crossings is four villages rather than one town. An
        /// unreachable destination is a different failure from an expensive one, and a map that
        /// mixed them would measure neither.
        /// </summary>
        private static void Bridge(VillageLayout big, int width, int height, int tiles)
        {
            const int Reach = 16;   // far enough either side of a seam to clear a river

            for (int j = 1; j < tiles; j++)
            {
                int seam = j * height;
                foreach (int x in Crossings(big, big.Width, seam - Reach, seam + Reach, vertical: true))
                {
                    var run = new RoadRun { Kind = Terrain.Road, Width = 5 };
                    run.Points.Add(new Tile(x, seam - Reach));
                    run.Points.Add(new Tile(x, seam + Reach));
                    big.Roads.Add(run);
                }
            }

            for (int i = 1; i < tiles; i++)
            {
                int seam = i * width;
                foreach (int y in Crossings(big, big.Height, seam - Reach, seam + Reach, vertical: false))
                {
                    var run = new RoadRun { Kind = Terrain.Road, Width = 5 };
                    run.Points.Add(new Tile(seam - Reach, y));
                    run.Points.Add(new Tile(seam + Reach, y));
                    big.Roads.Add(run);
                }
            }
        }

        /// <summary>
        /// Three evenly spread lanes across a seam, moved aside from anything already built
        /// there. Buildings are stamped after roads, so a crossing under a cottage would be
        /// walled up again and the seam would stay shut.
        /// </summary>
        private static List<int> Crossings(VillageLayout big, int span, int from, int to, bool vertical)
        {
            var found = new List<int>();
            for (int n = 1; n <= 3; n++)
            {
                int at = ClearLaneNear(big, span * n / 4, span, from, to, vertical);
                if (at >= 0) found.Add(at);
            }
            return found;
        }

        /// <summary>The nearest position to <paramref name="wanted"/> where a crossing would not
        /// run under a building, or -1 if the seam is built up for its whole length.</summary>
        private static int ClearLaneNear(VillageLayout big, int wanted, int span,
                                         int from, int to, bool vertical)
        {
            for (int drift = 0; drift < span / 4; drift++)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int at = wanted + sign * drift;
                if (at < 4 || at >= span - 4) continue;

                var lane = vertical
                    ? new TileRect(at - 3, from, 7, to - from + 1)
                    : new TileRect(from, at - 3, to - from + 1, 7);

                bool blocked = false;
                foreach (var place in big.Places)
                    if (place.IsBuilding && place.Bounds.Overlaps(lane)) { blocked = true; break; }

                if (!blocked) return at;
            }
            return -1;
        }
    }
}
