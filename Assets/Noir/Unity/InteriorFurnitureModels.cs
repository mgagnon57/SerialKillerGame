using Noir.Core.World;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// The small, deliberately curated bridge between the abstract furniture plan and the
    /// Polyperfect pack.  The plan remains authoritative: a model has to fit its simulated
    /// footprint, never the other way round.
    ///
    /// The installed pack is broad rather than period-specific.  This list therefore contains
    /// only its plain timber, enamel and upholstered pieces that can plausibly belong in a
    /// modest 1991 Rossville home.  Fantasy, holiday and overtly steampunk props are not a
    /// substitute for domestic furniture and are intentionally absent.
    /// </summary>
    internal static class InteriorFurnitureModels
    {
        private const string Farm =
            "Assets/polyperfect/Poly Universal Pack/Prefabs/Farm/Furniture Farm/";
        private const string Victorian =
            "Assets/polyperfect/Poly Universal Pack/Prefabs/Steampunk/Furniture Steampunk/";
        private const string Steampunk =
            "Assets/polyperfect/Poly Universal Pack/Prefabs/Steampunk/";
        private const string Bathroom =
            "Assets/polyperfect/Poly Universal Pack/Prefabs/Steampunk/Bathroom Steampunk/";

        /// <summary>
        /// Installs a real model when the editor has the pack available.  Player builds keep
        /// the generated fallback mesh; the rest of this project's pack dressing is currently
        /// editor-only for the same AssetDatabase reason.
        /// </summary>
        public static bool TryBuild(Furniture furniture, Transform parent, Vector3 centre,
                                    float floor)
        {
#if UNITY_EDITOR
            string path = PathFor(furniture);
            if (path == null) return false;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return false;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = furniture.Kind.ToString();
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(centre.x, floor, centre.z);

            // The plan describes a long object as W-by-H tiles.  Most pack furniture is made
            // long on local X, so turn it only when the planned long side runs north/south.
            if (furniture.Footprint.H > furniture.Footprint.W)
                go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Object.DestroyImmediate(go);
                return false;
            }

            Bounds before = BoundsOf(renderers);
            float targetX = Mathf.Max(0.25f, furniture.Footprint.W - 0.14f);
            float targetZ = Mathf.Max(0.25f, furniture.Footprint.H - 0.14f);
            float scale = Mathf.Min(targetX / Mathf.Max(0.01f, before.size.x),
                                    targetZ / Mathf.Max(0.01f, before.size.z));
            go.transform.localScale *= scale;

            // Pack pivots vary.  Fit each model into the simulation's reserved rectangle and
            // seat it exactly on the building floor, rather than trusting an authoring pivot.
            Bounds fitted = BoundsOf(go.GetComponentsInChildren<Renderer>());
            go.transform.position += new Vector3(centre.x - fitted.center.x,
                                                 floor - fitted.min.y,
                                                 centre.z - fitted.center.z);

            // Interior objects convey what is in a room but are not walls.  Let the wall pass
            // own collision, as the previous primitive furniture did.
            foreach (var collider in go.GetComponentsInChildren<Collider>())
                Object.Destroy(collider);
            return true;
#else
            return false;
#endif
        }

#if UNITY_EDITOR
        private static Bounds BoundsOf(Renderer[] renderers)
        {
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static string PathFor(Furniture furniture)
        {
            // Alternatives are selected from the stable grid coordinate, so rebuilding a town
            // never makes a household's chairs or bedroom arrangement flicker into another one.
            bool alternate = Materials3D.Scatter(furniture.Footprint.X, furniture.Footprint.Y, 9021) % 2 == 0;
            return furniture.Kind switch
            {
                FurnitureKind.Bed       => Steampunk + "Bed_Double_A_Steampunk.prefab",
                FurnitureKind.Wardrobe  => Victorian + "Wardrobe_Victorian_A.prefab",
                FurnitureKind.Dresser   => Victorian + (alternate
                    ? "Chest_Of_Drawers_Victorian_A.prefab" : "Chest_Of_Drawers_Victorian_B.prefab"),
                FurnitureKind.Table     => Farm + "Table_Dinner_Farm_A.prefab",
                FurnitureKind.Chair     => Farm + (alternate
                    ? "Chair_Dinner_Farm_A.prefab" : "Chair_Dinner_Farm_B.prefab"),
                FurnitureKind.Sofa      => Steampunk + "Sofa_Steampunk.prefab",
                FurnitureKind.Bath      => Bathroom + "Bathtub_Victorian.prefab",
                FurnitureKind.Basin     => Bathroom + (alternate
                    ? "Sink_Victorian_A.prefab" : "Sink_Victorian_B.prefab"),
                FurnitureKind.Desk      => Victorian + "Desk_Kneehole.prefab",
                FurnitureKind.Bench     => Farm + "Bench_Garden_Wooden_A.prefab",
                FurnitureKind.Shelf     => Victorian + (alternate
                    ? "Shelf_Wall_Victorian_A.prefab" : "Shelf_Wall_Victorian_B.prefab"),
                FurnitureKind.Cabinet   => Victorian + (alternate
                    ? "Cabinet_Specimen_A.prefab" : "Cabinet_Specimen_B.prefab"),
                FurnitureKind.Couch     => Steampunk + "Sofa_Steampunk.prefab",
                _                       => null,
            };
        }
#endif
    }
}
