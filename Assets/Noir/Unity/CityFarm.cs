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
        /// A field under crop.
        ///
        /// Wheat tiles at one metre, which would be forty thousand objects on a two-hundred-metre
        /// field, so it is laid on a coarser grid and the tile scaled up to cover it. Low-poly
        /// wheat is a handful of crossed quads and stretching it reads as denser planting rather
        /// than as a stretched model.
        /// </summary>
        private static int Crop(Transform parent, Place place)
        {
            var lot = place.Bounds;

            // One stage for the whole field. A field is sown on a day, not a square at a time.
            string[] stages =
            {
                "Wheat_Seedling_Square_1x1m_A", "Wheat_Sprout_Square_1x1m_A",
                "Wheat_Ripening_Square_1x1m_A", "Wheat_Mature_Square_1x1m_A",
            };
            string stage = stages[Materials3D.Scatter(lot.X, lot.Y, 1301) % (uint)stages.Length];
            string path = Farm + "Crops Farm/" + stage + ".prefab";

            const int Patch = 4;               // metres to a tile, after scaling
            int n = 0;

            for (int y = lot.Y; y + Patch <= lot.Y + lot.H; y += Patch)
            for (int x = lot.X; x + Patch <= lot.X + lot.W; x += Patch)
            {
                var go = Put(parent, path, x + Patch / 2f, y + Patch / 2f, 0f);
                if (go == null) continue;

                go.transform.localScale = new Vector3(Patch, 1f, Patch);
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
            go.transform.position = new Vector3(vx, 0f, -vy);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }
#endif
    }
}
