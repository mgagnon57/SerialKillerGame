using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

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

        private const string City = "Assets/polyperfect/Poly Universal Pack/Prefabs/City/";
        private const string CityProps = City + "Props City/";
        private const string Cars = "Assets/polyperfect/Poly Universal Pack/Prefabs/Cars/Cars City";

        /// <summary>
        /// Where the middle of downtown is, in village coordinates - ASKED OF THE MAP, not typed.
        ///
        /// This was `const float TownX = 525f` and it was silently wrong the moment the map was
        /// re-laid at 1290 and the town moved to 645. Nothing failed. The blocks still built; they
        /// just ranked themselves against a point a hundred and twenty metres away, so storeys
        /// fell off towards the wrong edge and the shopfronts faced the wrong way - 662 buildings
        /// became 580 and four towers became seven, with no error anywhere. That is the whole
        /// argument for deriving it: a wrong constant here does not break, it just quietly makes a
        /// worse city, which is the kind of fault that survives for months.
        ///
        /// The town is centred on the map by construction - that is what the +120 re-lay was for -
        /// so the map's own middle is the answer and it stays the answer whatever the map does
        /// next.
        /// </summary>
        private static float _townX = 525f, _townY = 525f;

        public static GameObject Build(WorldModel world, Transform parent)
        {
            var root = new GameObject("CityDistrict");
            root.transform.SetParent(parent, false);

            _townX = world.Width * 0.5f;
            _townY = world.Height * 0.5f;

#if UNITY_EDITOR
            int blocks = 0, buildings = 0, towers = 0, pieces = 0, yard = 0;

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
                yard += Interior(root.transform, lot);
            }

            Debug.Log($"[district] {blocks} blocks: {towers} towers, {buildings} buildings, "
                    + $"{pieces} sections, {yard} things in the back yards, "
                    + $"{root.GetComponentsInChildren<Renderer>().Length} renderers.");
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
            return Mathf.Max(Mathf.Abs(cx - _townX), Mathf.Abs(cy - _townY)) / 90f;
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
            bool shopNorth = cy > _townY, shopSouth = cy < _townY;
            bool shopWest = cx > _townX, shopEast = cx < _townX;

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

            // East and west: what is left between the two corner runs, AND A METRE CLEAR OF THEM.
            //
            // This used to run from exactly lot.Y + Depth to exactly lot.Y + lot.H - Depth, which
            // is right to the millimetre in LOT terms and wrong in geometry: the modules are 6.10
            // across on a 6m pitch, so a building overhangs its own lot by 5cm at each end, and
            // the bottom section carries its tail back the full Depth. Abutting the end run
            // therefore meant overlapping it, and the last house of the west run stood through the
            // first house of the south run on every block. One metre is more than both.
            const int Clear = 1;
            for (int y = lot.Y + Depth + Clear; y + Pitch <= lot.Y + lot.H - Depth - Clear; y += Pitch)
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
        /// What is behind the frontage.
        ///
        /// A block was a ring of buildings round a rectangle of fresh paving, which is the one
        /// view of a city nobody ever has from the street and every view has from above - and
        /// this game is played from above as often as not. The middle of a real block is where
        /// the bins go, where the delivery van reverses in, where somebody keeps a lock-up: the
        /// part of a city that is not for show, which is exactly the part a game about what
        /// people do when they think nobody is looking wants.
        ///
        /// LAID ON A LATTICE, one thing to a cell, and every piece measured to fit inside one:
        /// the garage is 6.10 x 6.66 and a car is 5.5 at its longest, so nothing in a seven-metre
        /// cell can reach its neighbour. That is what makes this safe to scatter without a
        /// clearance check - the same guarantee the parking bays now get, arrived at the same
        /// way, by knowing how big the thing actually is before deciding where it goes.
        ///
        /// One column is left clear the whole way through, because a yard nothing can drive into
        /// is a courtyard, and these have to be reachable from the street.
        /// </summary>
        private static int Interior(Transform parent, TileRect lot)
        {
            const int Cell = 7;

            int span = Mathf.Min(lot.W, lot.H) - Depth * 2;
            int cells = span / Cell;
            if (cells < 2) return 0;                 // nothing worth calling a yard

            // NO LOCK-UPS, and it was tried. `Squarehouse_Garage_City` is the pack's only low
            // outbuilding, and almost all of it is on the universal atlas - the same sand
            // colour behind the tan slab that used to face every street - so from above, which
            // is how this game is looked at half the time, a yard of them is a yard of
            // featureless cream boxes. Blank placeholder geometry is a worse answer than the
            // bare paving it was meant to fix, so the yards are bins, boxes and vehicles: all
            // things that read as themselves from directly overhead.
            _skips ??= Catalogue(CityProps + "Garbage Props", "Container_");
            _boxes ??= Catalogue(CityProps + "Garbage Props", "Box_");
            _yardCars ??= Catalogue(Cars, "Car_");

            int lane = cells / 2;                    // the way in, kept clear end to end
            int n = 0;

            for (int i = 0; i < cells; i++)
            for (int j = 0; j < cells; j++)
            {
                if (i == lane) continue;

                float cx = lot.X + Depth + i * Cell + Cell * 0.5f;
                float cy = lot.Y + Depth + j * Cell + Cell * 0.5f;

                uint roll = Materials3D.Scatter(lot.X + i * 13, lot.Y + j * 17, 3301) % 100;

                // Empty more often than not. A yard packed to its edges reads as a car park.
                if (roll < 44) continue;

                if (roll < 64 && _yardCars.Count > 0)
                {
                    // Left facing up or down the lane, as anything reversed into a yard is.
                    float yaw = (roll & 1) == 0 ? 0f : 180f;
                    if (Put(parent, Pick(_yardCars, lot.X + i, lot.Y + j, 3313), cx, cy, yaw) != null) n++;
                }
                else if (roll < 84 && _skips.Count > 0)
                {
                    // Bins and skips come in twos and threes against a wall, not one to a yard.
                    int many = 1 + (int)(Materials3D.Scatter(lot.X + j, lot.Y + i, 3319) % 3);
                    for (int k = 0; k < many; k++)
                    {
                        float ox = (k - 1) * 1.9f;
                        if (Put(parent, Pick(_skips, lot.X + i + k, lot.Y + j, 3323),
                                cx + ox, cy, 90f) != null) n++;
                    }
                }
                else if (_boxes.Count > 0)
                {
                    // A stack of boxes nobody has taken in yet.
                    int many = 2 + (int)(Materials3D.Scatter(lot.X + i, lot.Y + j * 3, 3329) % 3);
                    for (int k = 0; k < many; k++)
                    {
                        float ox = (Materials3D.Scatter(lot.X + i + k, lot.Y + j, 3331) % 30) / 10f - 1.5f;
                        float oz = (Materials3D.Scatter(lot.X + i, lot.Y + j + k, 3337) % 30) / 10f - 1.5f;
                        if (Put(parent, Pick(_boxes, lot.X + i + k * 3, lot.Y + j, 3343),
                                cx + ox, cy + oz,
                                Materials3D.Scatter(lot.X + k, lot.Y + j, 3347) % 4 * 90f) != null) n++;
                    }
                }
            }

            return n;
        }

        private static List<string> _skips, _boxes, _yardCars;

        private static List<string> Catalogue(string folder, string prefix)
        {
            var found = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf("Collider", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                if (prefix != null && !file.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
                found.Add(path);
            }
            found.Sort(System.StringComparer.Ordinal);   // stable, so a yard looks the same twice
            return found;
        }

        private static string Pick(List<string> from, int x, int y, int salt) =>
            from[(int)(Materials3D.Scatter(x, y, salt) % (uint)from.Count)];

        /// <summary>A yard piece, by its own base, at a point in village coordinates.</summary>
        private static GameObject Put(Transform parent, string path, float vx, float vy, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            // On the paving rather than in it: CityStreets lays the block's ground at 0.12.
            go.transform.position = new Vector3(vx, 0.12f, -vy);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
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
