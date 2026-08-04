using System.Collections.Generic;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// The trees and bushes, as bought models instead of drawn ones.
    ///
    /// VillageMesh draws a tree as a cylinder and three overlapping spheres, which was a good
    /// answer when there was nothing else - the lobes give a broken outline and at a distance
    /// that is all the eye needs. But the pack holds 216 tree models and 33 bushes, and outside
    /// the street verges and the orchard NOT ONE OF THEM was on screen. Everything green in the
    /// countryside was four primitives.
    ///
    /// WHERE A PROP STANDS DECIDES WHAT IT IS. That is the whole reason this is worth doing
    /// properly rather than swapping one model for another:
    ///
    ///   in a park          broadleaf and the occasional piece of topiary - a park is gardened
    ///   in a wood          spruce and pine, close and mature, because that is what a wood is
    ///   at a derelict      the fallen, the broken and the stumps. The pack has 35 such pieces
    ///                      and they say more about a place than any amount of standing timber
    ///   anywhere else      hedgerow species: oak, beech, birch, willow
    ///
    /// AND THE CLIMATE IS CURATED. The Trees folder is the whole world - baobab, mangrove,
    /// acacia, jungle, six kinds of palm. Asking it for "a tree" is how a savanna species ends
    /// up on a northern high street, which is the same fault as asking Nature for "a rock" and
    /// being handed one with snow on it.
    ///
    /// Editor-only in practice - the models load through AssetDatabase.
    /// </summary>
    public static class CityGreenery
    {
        private const string Nature = "Assets/polyperfect/Poly Universal Pack/Prefabs/Nature/";

        /// <summary>
        /// Does a bought model stand here instead of a drawn one?
        ///
        /// A single answer, consumed by VillageMesh, exactly as CityBuildings.Handles is. It is a
        /// pure function of the prop so it does not matter which renderer runs first - the
        /// mistake that once left every apartment with a grey procedural box inside it.
        ///
        /// Hedges are NOT included and stay drawn. VillageMesh lays them as continuous RUNS
        /// rather than one object per tile, which is what makes the hedgerow lattice across the
        /// fields read as boundaries instead of as a line of shrubs.
        /// </summary>
        public static bool Handles(PropKind kind) =>
            kind == PropKind.Tree || kind == PropKind.Bush;

        public static GameObject Build(WorldModel world, Transform parent)
        {
            var root = new GameObject("CityGreenery");
            root.transform.SetParent(parent, false);

#if UNITY_EDITOR
            // BEECH AND BIRCH ARE NOT ILLINOIS TREES and were quietly making the wrong forest.
            // American beech is an eastern/Appalachian species and birch is northern; neither
            // belongs on the east-central Illinois prairie edge. Oak, poplar and willow do - the
            // willow along the North Fork especially, which the 1940 aerial shows as a wide dark
            // wooded ribbon down the west side.
            //
            // WHAT THE TOWN ACTUALLY HAD IS NOT IN THIS PACK, and that is worth writing down
            // rather than pretending. The 1940 aerial shows Rossville almost entirely under
            // canopy and in 1940 that canopy was AMERICAN ELM - the Midwestern street tree,
            // planted in arching avenues. Dutch elm disease took them through the 1950s-70s and
            // the towns that replanted used silver maple, hackberry, honey locust and enormous
            // quantities of green ash. The emerald ash borer did not reach Illinois until about
            // 2006, so IN 1991 THE ASH ARE ALIVE and the canopy is thicker than today's.
            //
            // The pack ships none of elm, ash, maple or hackberry. So this is the closest
            // honest set, not the right one, and the gap is a content-pack problem rather than
            // a code one. See docs/research/THE-YEAR.md.
            var broadleaf = Species("Trees", "Oak", "Willow_Weeping", "Poplar");
            var conifer = Species("Trees", "Spruce", "Pine", "Juniper", "Cypress");
            var deadwood = Fallen();
            var topiary = All(Nature + "Hedges");
            var bushes = All(Nature + "Bushes");

            if (broadleaf.Count == 0 && conifer.Count == 0)
            {
                Debug.LogError("[greenery] no tree models found, and VillageMesh has stood down "
                             + "for them - the map will have no trees at all.");
                return root;
            }

            int trees = 0, shrubs = 0;

            foreach (var prop in world.AllProps)
            {
                if (!Handles(prop.Kind)) continue;

                // A railroad keeps its right of way cleared, and the first render of the rail
                // bed had trees standing between the rails. Scatter is decided in Core off the
                // terrain, and the corridor is not a terrain kind - it comes from the surveyed
                // polyline in features.txt - so the one place that knows where the track runs
                // has to be asked. See CityRailBed.OnRightOfWay.
                if (CityRailBed.OnRightOfWay(world, prop.At.X, prop.At.Y)) continue;

                var tile = prop.At;
                var terrain = world.Grid.TerrainAt(tile.X, tile.Y);
                string kindHere = KindAt(world, tile);

                // Off the tile centre, or a scatter reads as the grid it was generated on.
                float jx = (prop.Variant * 37 % 100 / 100f - 0.5f) * 0.8f;
                float jz = (prop.Variant * 61 % 100 / 100f - 0.5f) * 0.8f;
                var at = Space3D.ToWorld(tile) + new Vector3(jx, 0f, jz);

                List<string> from;
                if (prop.Kind == PropKind.Bush)
                {
                    from = bushes;
                }
                else if (Derelict(kindHere) && deadwood.Count > 0
                         && Materials3D.Scatter(tile.X, tile.Y, 1451) % 5 < 3)
                {
                    // Three in five at a place nobody keeps up. Not all of them: a ruin with no
                    // standing trees at all reads as a clearing rather than as neglect.
                    from = deadwood;
                }
                else if (terrain == Noir.Core.World.Terrain.Wood)
                {
                    from = conifer.Count > 0 ? conifer : broadleaf;
                }
                else if (kindHere == "green" && topiary.Count > 0
                         && Materials3D.Scatter(tile.X, tile.Y, 1453) % 7 == 0)
                {
                    from = topiary;                    // one tree in seven in a park is clipped
                }
                else
                {
                    from = broadleaf;
                }

                if (from.Count == 0) continue;

                string path = from[(int)(Materials3D.Scatter(tile.X, tile.Y, 1457) % (uint)from.Count)];
                var go = Put(root.transform, path, at,
                             Materials3D.Scatter(tile.X, tile.Y, 1459) % 360);
                if (go == null) continue;

                // A little variety in size, keyed off the prop's own variant so it is stable.
                float scale = 0.82f + prop.Variant % 45 / 100f;
                go.transform.localScale = Vector3.one * scale;

                if (prop.Kind == PropKind.Bush) shrubs++; else trees++;
            }

            // ---- and the near band of the countryside ------------------------------------
            //
            // Countryside dresses the five hundred metres around the map, and inside its first
            // hundred and twenty it drew the same four green lobes the village used to. Against
            // bought trees on the other side of the map edge that read as a line on the ground.
            // It now hands those positions over rather than building them, and this plants them
            // from the same hedgerow set the fields use - so the two sides of the boundary are
            // the same wood.
            //
            // Only the near band. Past it Countryside's canopy LOD has already taken the trunks
            // off and is building crowns from the coarsest shape whose outline holds, which is
            // the right answer for a green mass seen through fog.
            int outfield = 0;
            foreach (var at in Countryside.NearTrees)
            {
                int hx = Mathf.RoundToInt(at.x), hy = Mathf.RoundToInt(-at.z);
                string path = broadleaf[(int)(Materials3D.Scatter(hx, hy, 1461) % (uint)broadleaf.Count)];

                var tree = Put(root.transform, path, at, Materials3D.Scatter(hx, hy, 1463) % 360);
                if (tree == null) continue;

                tree.transform.localScale = Vector3.one * (0.85f + Materials3D.Scatter(hx, hy, 1467) % 40 / 100f);
                outfield++;
            }

            Debug.Log($"[greenery] {outfield} of them beyond the map edge, in the near band "
                    + "Countryside handed over.");

            Debug.Log($"[greenery] {trees} trees and {shrubs} bushes as bought models "
                    + $"({broadleaf.Count} broadleaf, {conifer.Count} conifer, "
                    + $"{deadwood.Count} fallen, {topiary.Count} topiary, {bushes.Count} shrub), "
                    + $"{root.GetComponentsInChildren<Renderer>().Length} renderers.");
#endif
            return root;
        }

#if UNITY_EDITOR
        /// <summary>Somewhere nobody has kept up, where the fallen timber goes.</summary>
        private static bool Derelict(string kind) => kind == "orchard" || kind == "barn";

        private static string KindAt(WorldModel world, Tile tile)
        {
            var id = world.Grid.PlaceAt(tile.X, tile.Y);
            if (!id.IsValid) return "";
            var place = world.GetPlace(id);
            return place == null ? "" : PlaceKindTable.Current.Row(place.Kind).Name;
        }

        /// <summary>
        /// Standing trees of the named species, at whatever growth stages exist.
        ///
        /// Matched on the species word rather than the whole filename so Tree_Oak_Mature,
        /// Tree_Oak_Young and Tree_Oak_Sapling all come through - a wood of identical mature
        /// trees is a wallpaper pattern.
        /// </summary>
        private static List<string> Species(string folder, params string[] wanted)
        {
            var found = new List<string>();
            foreach (var path in All(Nature + folder))
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(path);

                // The fallen and the dead are placed deliberately, not mixed into the standing.
                if (name.IndexOf("Dead", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (IsFallen(name)) continue;

                foreach (var species in wanted)
                    if (name.IndexOf(species, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    { found.Add(path); break; }
            }
            return found;
        }

        /// <summary>What is left of a tree: stumps, logs, fallen trunks, broken branches.</summary>
        private static List<string> Fallen()
        {
            var found = new List<string>();
            foreach (var path in All(Nature + "Trees"))
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (IsFallen(name) || name.IndexOf("Dead", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    found.Add(path);
            }
            return found;
        }

        private static bool IsFallen(string name) =>
            name.IndexOf("Fallen", System.StringComparison.OrdinalIgnoreCase) >= 0
         || name.IndexOf("Stump", System.StringComparison.OrdinalIgnoreCase) >= 0
         || name.IndexOf("Log", System.StringComparison.OrdinalIgnoreCase) >= 0
         || name.IndexOf("Broken", System.StringComparison.OrdinalIgnoreCase) >= 0
         || name.IndexOf("Branch", System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Every prefab in a folder, less the ones that belong to another climate or season.
        ///
        /// FindAssets searches recursively, so Trees also returns Trees Winter and Nature/Rocks
        /// returns Rocks Winter. And the pack's Trees folder is the whole world in one drawer:
        /// baobab, mangrove, acacia, jungle and six palms, none of which grow here.
        /// </summary>
        /// <summary>
        /// Does this prefab's geometry reach the ground it would be planted on?
        ///
        /// A quarter of a metre of clearance is modelling; more than that is a mounting point,
        /// and a thing with a mounting point is a component rather than an object.
        /// </summary>
        private static bool Grounded(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return false;

            float lo = float.MaxValue;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                float off = mf.transform.position.y - prefab.transform.position.y;
                lo = Mathf.Min(lo, mf.sharedMesh.bounds.min.y + off);
            }
            return lo == float.MaxValue || lo <= 0.25f;
        }

        private static List<string> All(string folder)
        {
            string[] wrong =
            {
                "Winter", "Snow", "Ice", "Frozen",
                "Palm", "Cactus", "Baobab", "Mangrove", "Jungle", "Acacia",
                "Fantasy", "Collider",
            };

            var found = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                bool skip = false;
                foreach (var word in wrong)
                    if (path.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    { skip = true; break; }
                if (skip) continue;

                // AND ANYTHING THAT DOES NOT REACH THE GROUND, which is a measurement rather
                // than another word on the list above. Nature/Hedges holds two ARCHES that
                // begin two metres up because they are meant to span a gateway; planted as
                // topiary they would hang in the air over the lawn. Asking the mesh catches
                // those and every other piece of the same sort without anybody having to
                // notice it first. See Noir/Audit Prefab Mounting.
                if (!Grounded(path)) continue;

                found.Add(path);
            }
            found.Sort(System.StringComparer.Ordinal);   // stable, so the map looks the same twice
            return found;
        }

        private static GameObject Put(Transform parent, string path, Vector3 at, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }
#endif
    }
}
