using UnityEngine;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// Fills a downtown block.
    ///
    /// Northgate was nine sixty-metre blocks alone in the middle of five hundred metres of
    /// arterial road, and the pack has no more landmarks to add - Buildings City holds thirty
    /// prefabs, twelve of them skyscraper modules, and the city already places fourteen of the
    /// seventeen distinct buildings in it. There is no version of a downtown here that is
    /// thirty-six different buildings.
    ///
    /// WHAT MAKES A DOWNTOWN IS THE BLOCK, NOT THE BUILDING. A block is a perimeter of ordinary
    /// buildings standing shoulder to shoulder with their backs to each other, shops at the
    /// bottom where the street is worth a shop, and something in the middle. That is buildable
    /// out of the modular kit forever, and it is what the eye actually reads.
    ///
    /// Three things decide what a given block becomes, all of them from where it is:
    ///
    ///   HOW FAR IN IT IS. Buildings get taller towards the middle of town, because that is
    ///   what land costs do. A block on the ring road is three storeys; one beside the arterials
    ///   is six, and may be a tower instead.
    ///
    ///   WHICH WAY IT FACES. The shopfronts go on the sides facing IN, towards the middle of
    ///   town, and the backs face the ring. Retail follows footfall, and the footfall is inward.
    ///
    ///   WHAT THE DICE SAY, seeded off the block's own coordinates, so a block is the same
    ///   block every time the city is built and nothing here ever moves under you.
    ///
    /// Static, so this goes inside the node CityChunker bakes.
    /// </summary>
    public static class CityDistrict
    {
        /// <summary>
        /// The frontage a single building takes along a street.
        ///
        /// The modules measure 6.1-6.3m across, so a six-metre pitch overlaps its neighbour by
        /// a couple of hundred millimetres - which is what a terrace IS. Spacing them at their
        /// full measured width instead leaves a hairline of daylight between every pair, and a
        /// terrace with light through the joints does not read as a terrace.
        /// </summary>
        private const int Pitch = 6;

        /// <summary>Building depth, and so how far the back of a frontage reaches into the block.</summary>
        private const int Depth = 9;

        /// <summary>Where the middle of downtown is, in village coordinates.</summary>
        private const float TownX = 525f, TownY = 525f;

        public static GameObject Build(WorldModel world, Transform parent)
        {
            var root = new GameObject("CityDistrict");
            root.transform.SetParent(parent, false);

#if UNITY_EDITOR
            int blocks = 0, buildings = 0, towers = 0, pieces = 0;

            foreach (var place in world.AllPlaces)
            {
                if (PlaceKindTable.Current.Row(place.Kind).Name != "district") continue;
                blocks++;

                var lot = place.Bounds;
                float rank = RankOf(lot);

                if (IsTower(world, lot, rank))
                {
                    pieces += Tower(root.transform, place, lot, rank);
                    towers++;
                    continue;
                }

                int built = Perimeter(root.transform, place, lot, rank, out int p);
                buildings += built;
                pieces += p;
            }

            Debug.Log($"[district] {blocks} blocks: {towers} towers, {buildings} buildings, "
                    + $"{pieces} sections, {root.GetComponentsInChildren<Renderer>().Length} renderers.");
#endif
            return root;
        }

#if UNITY_EDITOR

        /// <summary>
        /// How far out of town this block is, in blocks. Zero in the middle, and about two and
        /// a half out at the ring road.
        /// </summary>
        private static float RankOf(TileRect lot)
        {
            float cx = lot.X + lot.W * 0.5f, cy = lot.Y + lot.H * 0.5f;
            return Mathf.Max(Mathf.Abs(cx - TownX), Mathf.Abs(cy - TownY)) / 90f;
        }

        /// <summary>
        /// How tall this block builds, in storeys above the entrance floor.
        ///
        /// Six near the middle down to two at the edge, plus a metre or two of variation per
        /// lot so a run of frontage is not a single flat line. The variation is deliberately
        /// small: a terrace where every unit is a different height is not a terrace either.
        /// </summary>
        private static int StoreysFor(TileRect lot, float rank, int index)
        {
            int tall = Mathf.RoundToInt(Mathf.Lerp(6f, 2f, Mathf.InverseLerp(1f, 2.6f, rank)));
            return Mathf.Max(1, tall + (int)(Materials3D.Scatter(lot.X + index, lot.Y, 5309) % 3) - 1);
        }

        /// <summary>
        /// Is this block a tower rather than a terrace?
        ///
        /// THE TALL THINGS FRONT THE BIG STREETS. This was a die roll on the block's own
        /// coordinates, which is the pattern used everywhere else here and was wrong for this
        /// one thing twice over: the coordinates are all multiples of ninety, so the hash
        /// correlated and the same two blocks came up however the threshold was set - and a
        /// skyline that lands where a hash happens to fall is not a skyline, it is noise.
        ///
        /// A block gets a tower if it is in the inner ring AND it abuts one of the two
        /// arterials. That is why downtown towers stand where they do: the frontage on the
        /// widest street is the frontage worth building high on. It also means the answer
        /// follows the map - reclassify a street and the skyline moves with it.
        /// </summary>
        private static bool IsTower(WorldModel world, TileRect lot, float rank)
        {
            if (rank >= 2.0f) return false;

            foreach (var line in world.Roads.Lines)
            {
                if (!line.IsStraight || line.Class != RoadClass.Freeway) continue;

                // Does the block's edge meet this corridor's edge, and is it alongside the
                // stretch of road that actually exists?
                float lo = line.Centre - line.HalfWidth, hi = line.Centre + line.HalfWidth;
                float a = line.IsNorthSouth ? lot.X : lot.Y;
                float b = a + (line.IsNorthSouth ? lot.W : lot.H);
                if (b < lo - 1f || a > hi + 1f) continue;

                float c = line.IsNorthSouth ? lot.Y : lot.X;
                float d = c + (line.IsNorthSouth ? lot.H : lot.W);
                if (d < line.From || c > line.To) continue;

                return true;
            }
            return false;
        }

        /// <summary>
        /// One skyscraper, standing in the middle of its block.
        ///
        /// Which of the three families is chosen by whether it FITS: A is 47m across the base
        /// and C is 36, and a sixty-metre block with pavement round it cannot take every one of
        /// them everywhere. Height comes from how far in the block is.
        /// </summary>
        private static int Tower(Transform parent, Place place, TileRect lot, float rank)
        {
            uint roll = Materials3D.Scatter(lot.X, lot.Y, 2437);
            char family = "ABC"[(int)(roll % 3)];

            // 17 + 15n metres for family A: four floors is the whole prefab's own height, so
            // this spans from a good deal shorter than the bought tower to a good deal taller.
            int floors = 2 + (int)(Materials3D.Scatter(lot.X, lot.Y, 2441) % 4);
            if (rank < 1.6f) floors += 2;

            // Centred on the block, and inset so the base does not overhang the pavement.
            var pad = new TileRect(lot.X + 4, lot.Y + 4, lot.W - 8, lot.H - 8);
            return CityBuildings.Tower(parent, place, pad, family, floors);
        }

        /// <summary>
        /// A run of buildings along each of the four sides, facing out.
        ///
        /// Corners belong to the north and south runs, which take the full width; the east and
        /// west runs stop short of them. Otherwise two buildings stand on the same ground at
        /// every corner of every block.
        /// </summary>
        private static int Perimeter(Transform parent, Place place, TileRect lot,
                                     float rank, out int pieces)
        {
            pieces = 0;
            int built = 0;

            // Which sides face the middle of town, and so get the shopfronts.
            float cx = lot.X + lot.W * 0.5f, cy = lot.Y + lot.H * 0.5f;
            bool shopNorth = cy > TownY, shopSouth = cy < TownY;
            bool shopWest = cx > TownX, shopEast = cx < TownX;

            // North and south: full width, facing -y and +y.
            for (int x = lot.X; x + Pitch <= lot.X + lot.W; x += Pitch)
            {
                int i = (x - lot.X) / Pitch;

                built += One(parent, place, lot, rank, i,
                             new TileRect(x, lot.Y, Pitch, Depth), 180f, shopNorth, ref pieces);

                built += One(parent, place, lot, rank, i + 40,
                             new TileRect(x, lot.Y + lot.H - Depth, Pitch, Depth), 0f,
                             shopSouth, ref pieces);
            }

            // East and west: what is left between the two corner runs.
            for (int y = lot.Y + Depth; y + Pitch <= lot.Y + lot.H - Depth; y += Pitch)
            {
                int i = (y - lot.Y) / Pitch;

                built += One(parent, place, lot, rank, i + 80,
                             new TileRect(lot.X, y, Depth, Pitch), 90f, shopWest, ref pieces);

                built += One(parent, place, lot, rank, i + 120,
                             new TileRect(lot.X + lot.W - Depth, y, Depth, Pitch), 270f,
                             shopEast, ref pieces);
            }

            return built;
        }

        /// <summary>
        /// One frontage, or a gap where the dice say so.
        ///
        /// ABOUT ONE LOT IN SIX IS EMPTY, and that is the difference between a block and a wall.
        /// A perimeter with no breaks in it has no yards, no side entrances, no way through and
        /// nowhere a car could be left out of sight - which in this game is not a cosmetic loss.
        /// </summary>
        private static int One(Transform parent, Place place, TileRect block, float rank,
                               int index, TileRect lot, float yaw, bool market, ref int pieces)
        {
            if (Materials3D.Scatter(block.X + index, block.Y + index, 4157) % 6 == 0) return 0;

            // Shops only where there are people to walk past them.
            bool shop = market && rank < 2.3f
                     && Materials3D.Scatter(block.X + index, block.Y, 4159) % 3 != 0;

            pieces += CityBuildings.Stack(parent, place, lot, yaw,
                                          StoreysFor(block, rank, index), shop, out _);
            return 1;
        }
#endif
    }
}
