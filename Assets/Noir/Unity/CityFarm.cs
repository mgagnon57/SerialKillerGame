using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// The country: what stands in a field rather than on a street.
    ///
    /// The pack has 486 farm prefabs and the city had used none of them. This is the renderer
    /// for the ones that are not simply buildings - the crops, the fencing, the machinery left
    /// standing in a yard - because those are not placed on a lot, they FILL one.
    ///
    /// THREE THINGS FILL AN AREA RATHER THAN SIT ON A POINT:
    ///
    ///   A crop is a tiled square. Wheat ships as Wheat_*_Square_1x1m_A at five growth stages,
    ///   which is exactly a floor tile for a field; corn is individual plants and gets scattered.
    ///   Which stage a field is at is decided per FIELD, not per square, or a single field comes
    ///   out with seedlings next to a ready harvest.
    ///
    ///   A pen is a fence walked round a rectangle, with a gate on one side. The kit is 1m, 2m
    ///   and 3m pieces plus corners, gates and posts, so the walk is greedy: take the longest
    ///   piece that still fits.
    ///
    ///   An orchard is a lattice, because that is how trees are planted.
    ///
    /// THERE ARE NO ANIMALS IN THIS PACK. Not a cow, pig, chicken or sheep anywhere in four
    /// thousand prefabs - the only hits are a butcher's props in the fantasy set and a
    /// cattle-crossing road sign. So a pen here is fenced, gated, troughed and empty, and that
    /// is a purchase away from being fixed rather than a bug.
    ///
    /// Editor-only in practice - the pieces load through AssetDatabase.
    /// </summary>
    public static class CityFarm
    {
        private const string Farm = "Assets/polyperfect/Poly Universal Pack/Prefabs/Farm/";
        private const string Fences = "Assets/polyperfect/Poly Universal Pack/Prefabs/Modular Parts/Fences/";
        private const string Nature = "Assets/polyperfect/Poly Universal Pack/Prefabs/Nature/";

        public static GameObject Build(WorldModel world, Transform parent)
        {
            var root = new GameObject("CityFarm");
            root.transform.SetParent(parent, false);

#if UNITY_EDITOR
            int pieces = 0;

            foreach (var place in world.AllPlaces)
            {
                switch (PlaceKindTable.Current.Row(place.Kind).Name)
                {
                    case "cornfield": pieces += Crop(root.transform, place); break;
                    case "paddock":   pieces += Pen(root.transform, place); break;
                    case "orchard":   pieces += Orchard(root.transform, place); break;
                    case "farmyard":  pieces += Yard(root.transform, place); break;
                }
            }

            if (pieces > 0)
                Debug.Log($"[farm] {pieces} pieces of country, "
                        + $"{root.GetComponentsInChildren<Renderer>().Length} renderers.");
#endif
            return root;
        }

#if UNITY_EDITOR
        /// <summary>
        /// What a field is under, and how it is planted.
        ///
        /// Wheat is the only crop the pack tiles - Wheat_*_Square_1x1m covers ground and nothing
        /// else does. Everything else is an individual plant, and an individual plant is only
        /// worth having IF IT IS PLANTED IN ROWS. That is the whole difference between a field
        /// and a green rectangle: rows are the thing the eye reads as farmed, and they are what
        /// makes one field look like somebody's work rather than like terrain.
        /// </summary>
        private readonly struct Planting
        {
            public readonly string Name;
            public readonly string[] Plants;   // under Crops Farm/, without the extension
            public readonly float Row, Along;  // metres between rows, and along one
            public readonly float Scale;
            public readonly bool Tiled;

            public Planting(string name, float row, float along, float scale, bool tiled,
                            params string[] plants)
            {
                Name = name; Row = row; Along = along; Scale = scale; Tiled = tiled; Plants = plants;
            }
        }

        /// <summary>
        /// The crops a field round here might be under.
        ///
        /// Spacings are wide and the models scaled up to meet: a potato at its true 0.4m spacing
        /// is nine thousand objects on one field, and at three metres and scaled it covers the
        /// same ground for six hundred. On flat-shaded low-poly that trade is invisible and it is
        /// the only reason a field can be planted individually at all.
        /// </summary>
        private static readonly Planting[] Crops =
        {
            new Planting("wheat", 4f, 4f, 4f, true,
                         "Wheat_Seedling_Square_1x1m_A", "Wheat_Sprout_Square_1x1m_A",
                         "Wheat_Ripening_Square_1x1m_A", "Wheat_Mature_Square_1x1m_A"),

            new Planting("field corn", 2.6f, 2.0f, 1.7f, false,
                         "Corn_Ripe_Fieldcorn_A", "Corn_Ripe_Fieldcorn_B", "Corn_Ripe_Fieldcorn_C"),

            new Planting("sunflowers", 3.0f, 2.4f, 1.5f, false,
                         "Sunflower_Plant_Mature_A", "Sunflower_Plant_Mature_B",
                         "Sunflower_Plant_Ripe_A", "Sunflower_Plant_Ripe_B",
                         "Sunflower_Plant_Ripe_C", "Sunflower_Plant_Ripe_D"),

            new Planting("potatoes", 3.0f, 2.4f, 1.8f, false,
                         "Potato_Plant_Flowering_A", "Potato_Plant_Flowering_B",
                         "Potato_Plant_Ripe_A", "Potato_Plant_Ripe_B"),

            new Planting("beet", 2.8f, 2.2f, 1.9f, false,
                         "Beetroot_Plant_Ripe_A", "Beetroot_Plant_Ripe_B",
                         "Beetroot_Plant_Young", "Beetroot_Plant_Ripe_Root_A"),

            new Planting("pumpkins", 3.4f, 3.0f, 1.6f, false,
                         "Pumpkin_Ripe_A", "Pumpkin_B", "Pumpkin_C",
                         "Pumpkin_D", "Pumpkin_Flowering_A"),
        };

        /// <summary>
        /// A field under crop, planted in rows along its own long axis.
        ///
        /// ONE CROP TO A FIELD, and one growth stage. A field is sown on a day, not a square at a
        /// time, and a field carrying seedlings next to a ready harvest reads as a bug.
        /// </summary>
        private static int Crop(Transform parent, Place place)
        {
            var lot = place.Bounds;
            var crop = Crops[Materials3D.Scatter(lot.X, lot.Y, 1301) % (uint)Crops.Length];

            // One plant model for the whole field, for the same reason.
            string path = Farm + "Crops Farm/"
                        + crop.Plants[Materials3D.Scatter(lot.Y, lot.X, 1303) % (uint)crop.Plants.Length]
                        + ".prefab";

            // Rows run the long way, as they are ploughed.
            bool alongX = lot.W >= lot.H;
            float step = crop.Along, gap = crop.Row;
            int n = 0;

            for (float across = gap * 0.5f; across < (alongX ? lot.H : lot.W); across += gap)
            for (float down = step * 0.5f; down < (alongX ? lot.W : lot.H); down += step)
            {
                float x = lot.X + (alongX ? down : across);
                float y = lot.Y + (alongX ? across : down);

                // Jitter ALONG the row and barely across it. A row that wanders is a row nobody
                // drilled; a row that is perfectly straight is a texture.
                if (!crop.Tiled)
                {
                    uint r = Materials3D.Scatter(Mathf.RoundToInt(x), Mathf.RoundToInt(y), 1305);
                    float wobble = (r % 100 / 100f - 0.5f) * step * 0.35f;
                    if (alongX) x += wobble; else y += wobble;
                }

                var go = Put(parent, path, x, y, crop.Tiled ? 0f : Materials3D.Scatter(
                                 Mathf.RoundToInt(x), Mathf.RoundToInt(y), 1309) % 360);
                if (go == null) continue;

                go.transform.localScale = crop.Tiled
                    ? new Vector3(crop.Scale, 1f, crop.Scale)
                    : Vector3.one * crop.Scale;
                n++;
            }

            // A tractor and its implement standing at the field edge, and a few bales.
            var kit = new[]
            {
                "Vehicles Farm/Tractor_Big", "Vehicles Farm/Tractor_Old",
                "Vehicles Farm/Combine_Harvester", "Vehicles Farm/Plow_Small",
                "Vehicles Farm/Seeder", "Vehicles Farm/Hay_Baler",
            };
            uint roll = Materials3D.Scatter(lot.X, lot.Y, 1307);
            if (Put(parent, Farm + kit[roll % (uint)kit.Length] + ".prefab",
                    lot.X + 3f, lot.Y + 3f, roll % 4 * 90f) != null) n++;

            for (int b = 0; b < 3; b++)
            {
                float bx = lot.X + 6f + Materials3D.Scatter(lot.X + b, lot.Y, 1311) % (uint)Mathf.Max(1, lot.W - 12);
                float by = lot.Y + 6f + Materials3D.Scatter(lot.X, lot.Y + b, 1313) % (uint)Mathf.Max(1, lot.H - 12);
                if (Put(parent, Farm + "Crops Farm/Haybale_Square_Big.prefab", bx, by,
                        Materials3D.Scatter(b, lot.X, 1317) % 4 * 90f) != null) n++;
            }

            return n;
        }

        /// <summary>
        /// A fenced paddock: a fence walked round the boundary, a gate on one side, and the
        /// troughs and feed that would be in it if the pack had anything to put in it.
        /// </summary>
        private static int Pen(Transform parent, Place place)
        {
            var lot = place.Bounds;

            // Horse fencing, because it is the only kit in the pack drawn as field fencing
            // rather than as a garden or an industrial boundary.
            string post = Fences + "Fence_Horse_Pole.prefab";
            string gate = Fences + "Fence_Horse_Gate.prefab";
            var runs = new (string path, int span)[]
            {
                (Fences + "Fence_Horse_3m.prefab", 3),
                (Fences + "Fence_Horse_2m.prefab", 2),
                (Fences + "Fence_Horse_1m.prefab", 1),
            };

            int n = 0;

            // The gate goes in the middle of the north side, facing the track.
            int gateAt = lot.X + lot.W / 2 - 1;

            for (int side = 0; side < 4; side++)
            {
                bool horizontal = side < 2;
                float fixedAt = side switch
                {
                    0 => lot.Y,                       // north
                    1 => lot.Y + lot.H,               // south
                    2 => lot.X,                       // west
                    _ => lot.X + lot.W,               // east
                };
                float yaw = horizontal ? 0f : 90f;
                int from = horizontal ? lot.X : lot.Y;
                int length = horizontal ? lot.W : lot.H;

                int at = from;
                while (at < from + length)
                {
                    // Leave a two-metre gap for the gate.
                    if (side == 0 && at >= gateAt && at < gateAt + 2)
                    {
                        if (Put(parent, gate, at + 1f, fixedAt, yaw) != null) n++;
                        at += 2;
                        continue;
                    }

                    bool placed = false;
                    foreach (var (path, span) in runs)
                    {
                        if (at + span > from + length) continue;

                        float px = horizontal ? at + span / 2f : fixedAt;
                        float py = horizontal ? fixedAt : at + span / 2f;
                        if (Put(parent, path, px, py, yaw) != null) n++;
                        at += span;
                        placed = true;
                        break;
                    }
                    if (!placed) break;
                }

                // A post on the corner, so the two runs meet in something.
                float cx = side == 2 || side == 0 ? lot.X : lot.X + lot.W;
                float cy = side < 2 ? fixedAt : lot.Y;
                if (Put(parent, post, cx, cy, 0f) != null) n++;
            }

            // What is in a pen, minus the animals.
            var troughs = new[] { "Trough_A_Full", "Trough_B_Full", "Trough_C_Full" };
            for (int t = 0; t < 2; t++)
            {
                uint roll = Materials3D.Scatter(lot.X + t, lot.Y, 1319);
                float tx = lot.X + 3f + roll % (uint)Mathf.Max(1, lot.W - 6);
                float ty = lot.Y + 3f + Materials3D.Scatter(lot.X, lot.Y + t, 1321) % (uint)Mathf.Max(1, lot.H - 6);
                if (Put(parent, Farm + troughs[roll % 3] + ".prefab", tx, ty, roll % 4 * 90f) != null) n++;
            }

            return n;
        }

        /// <summary>Trees in rows, because that is what an orchard is.</summary>
        private static int Orchard(Transform parent, Place place)
        {
            var lot = place.Bounds;
            var trees = Catalogue(Farm + "Trees Farm");
            if (trees.Count == 0) trees = Catalogue(Nature + "Trees");
            if (trees.Count == 0) return 0;

            const int Spacing = 8;
            int n = 0;

            for (int y = lot.Y + 4; y < lot.Y + lot.H - 2; y += Spacing)
            for (int x = lot.X + 4; x < lot.X + lot.W - 2; x += Spacing)
            {
                // A tree planted by a person is in a row, but not to the centimetre.
                float jx = x + (Materials3D.Scatter(x, y, 1327) % 100) / 100f - 0.5f;
                float jy = y + (Materials3D.Scatter(y, x, 1331) % 100) / 100f - 0.5f;
                string pick = trees[(int)(Materials3D.Scatter(x, y, 1333) % (uint)trees.Count)];
                if (Put(parent, pick, jx, jy, Materials3D.Scatter(x, y, 1337) % 4 * 90f) != null) n++;
            }
            return n;
        }

        /// <summary>
        /// The yard between the buildings: what a working farm leaves lying about.
        ///
        /// Scattered from whole folders rather than a list of names, so the pack's 68 tools, 66
        /// crates and 21 bags actually get used instead of the four somebody typed out.
        /// </summary>
        private static int Yard(Transform parent, Place place)
        {
            var lot = place.Bounds;

            var kit = new List<List<string>>
            {
                Catalogue(Farm + "Tools Farm"),
                Catalogue(Farm + "Crates Farm"),
                Catalogue(Farm + "Bags Farm"),
                Catalogue(Farm + "Planters Farm"),
                Catalogue(Farm + "Vehicles Farm"),
            };
            kit.RemoveAll(c => c.Count == 0);
            if (kit.Count == 0) return 0;

            int n = 0;

            // ---- what STANDS in a yard, placed rather than scattered ---------------------
            //
            // A water tower and a grain bin are structures. Rolling for them the way a crate is
            // rolled for puts a twelve-metre tower at a jaunty angle in the middle of the mud,
            // and the yard stops reading as somewhere that was laid out by anybody. These go on
            // the boundary, square to it, like everything else on a farm that had to be poured.
            var standing = new (string path, float fx, float fy)[]
            {
                (Farm + "Buildings Farm/Tower_Water_Farm.prefab",  0.10f, 0.14f),
                (Farm + "Buildings Farm/Windmill_Pump.prefab",     0.90f, 0.16f),
                (Farm + "Buildings Farm/Bin_Grain_Old.prefab",     0.14f, 0.86f),
                (Farm + "Buildings Farm/Bin_Grain_New.prefab",     0.34f, 0.88f),
                (Farm + "Buildings Farm/Greenhouse_Modern_A.prefab", 0.86f, 0.84f),
            };
            foreach (var (path, fx, fy) in standing)
                if (Put(parent, path, lot.X + lot.W * fx, lot.Y + lot.H * fy, 0f) != null) n++;

            // ---- the kitchen garden ------------------------------------------------------
            //
            // A run of planter boxes along one edge, because a farm feeds itself first. The
            // pack has seventeen of these and the city had used none.
            var boxes = Catalogue(Farm + "Planters Farm");
            for (int b = 0; b < 4 && boxes.Count > 0; b++)
            {
                string box = boxes[(int)(Materials3D.Scatter(lot.X + b, lot.Y, 1371) % (uint)boxes.Count)];
                if (Put(parent, box, lot.X + 3f + b * 3.2f, lot.Y + lot.H - 3f, 0f) != null) n++;
            }

            // ---- somewhere to sit, and the hay ------------------------------------------
            var sitting = Catalogue(Farm + "Furniture Farm");
            if (sitting.Count > 0)
            {
                uint s = Materials3D.Scatter(lot.X, lot.Y, 1373);
                if (Put(parent, sitting[(int)(s % (uint)sitting.Count)],
                        lot.X + lot.W * 0.5f, lot.Y + 2.5f, s % 4 * 90f) != null) n++;
            }

            string[] hay =
            {
                "Crops Farm/Haybale_Round", "Crops Farm/Haybale_Square_Big",
                "Crops Farm/Hay_Pile_Dry_A", "Crops Farm/Hay_Wheelbarrow_Dry",
                "Crops Farm/Hay_Wheelbarrow_Fresh",
            };
            for (int h = 0; h < 5; h++)
            {
                uint r = Materials3D.Scatter(lot.X + h * 3, lot.Y, 1377);
                float hx = lot.X + 4f + r % (uint)Mathf.Max(1, lot.W - 8);
                float hy = lot.Y + 4f + Materials3D.Scatter(lot.X, lot.Y + h * 3, 1379)
                                        % (uint)Mathf.Max(1, lot.H - 8);
                if (Put(parent, Farm + hay[r % (uint)hay.Length] + ".prefab",
                        hx, hy, r % 4 * 90f) != null) n++;
            }
            for (int i = 0; i < lot.W * lot.H / 45; i++)
            {
                var role = kit[(int)(Materials3D.Scatter(lot.X + i, lot.Y, 1339) % (uint)kit.Count)];
                float x = lot.X + 2f + Materials3D.Scatter(lot.X * 7 + i, lot.Y, 1343) % (uint)Mathf.Max(1, lot.W - 4);
                float y = lot.Y + 2f + Materials3D.Scatter(lot.X, lot.Y * 7 + i, 1349) % (uint)Mathf.Max(1, lot.H - 4);
                string pick = role[(int)(Materials3D.Scatter(i, lot.Y, 1351) % (uint)role.Count)];
                if (Put(parent, pick, x, y, Materials3D.Scatter(i, lot.X, 1361) % 4 * 90f) != null) n++;
            }
            return n;
        }

        private static List<string> Catalogue(string folder)
        {
            var found = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf("Collider", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                found.Add(path);
            }
            found.Sort(System.StringComparer.Ordinal);
            return found;
        }

        /// <summary>A prop at a point in village coordinates. Village y runs into Unity -z.</summary>
        private static GameObject Put(Transform parent, string path, float vx, float vy, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(vx, ElevationGrid.HeightAt(vx, vy), -vy);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }
#endif
    }
}
