using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Measures the pack's whole-building prefabs against Ashcombe's actual lots.
    ///
    /// A complete building is not placed, it is FITTED: its footprint is fixed, so either the
    /// village has lots that size or it does not. This answers that with numbers before anybody
    /// designs around the idea.
    /// </summary>
    public static class PolyPackFootprints
    {
        [MenuItem("Noir/Measure Pack Footprints")]
        public static void Measure()
        {
            try
            {
                foreach (var folder in new[] { "City/Buildings City", "Farm", "Survival" })
                {
                    string dir = "Assets/polyperfect/Poly Universal Pack/Prefabs/" + folder;
                    var guids = AssetDatabase.FindAssets("t:Prefab", new[] { dir });
                    if (guids.Length == 0) { Debug.Log($"[fit] {folder}: nothing"); continue; }

                    Debug.Log($"[fit] ---- {folder}: {guids.Length} prefabs ----");
                    var sizes = new List<float>();

                    foreach (var guid in guids)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (prefab == null) continue;

                        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                        go.transform.position = Vector3.zero;
                        go.transform.rotation = Quaternion.identity;

                        var rends = go.GetComponentsInChildren<Renderer>();
                        if (rends.Length > 0)
                        {
                            var b = rends[0].bounds;
                            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                            sizes.Add(Mathf.Max(b.size.x, b.size.z));
                            Debug.Log($"[fit] {prefab.name}  {b.size.x:0.#} x {b.size.z:0.#} m, "
                                    + $"h {b.size.y:0.#}, {rends.Length} renderers");
                        }
                        UnityEngine.Object.DestroyImmediate(go);
                    }

                    sizes.Sort();
                    if (sizes.Count > 0)
                        Debug.Log($"[fit] {folder} longest side: min {sizes[0]:0.#}m, "
                                + $"median {sizes[sizes.Count / 2]:0.#}m, max {sizes[sizes.Count - 1]:0.#}m");
                }

                // ---- what Ashcombe actually has to offer ----
                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
                var layout = VillageParser.Parse(ContentLoader.Read("village.txt"));
                var world = WorldBuilder.Build(layout, VillageHost.Seed);

                var byKind = new Dictionary<PlaceKind, List<string>>();
                int buildings = 0;
                var longest = new List<float>();

                foreach (var place in world.AllPlaces)
                {
                    if (!PlaceKindTable.Current.Row(place.Kind).IsBuilding) continue;
                    buildings++;
                    longest.Add(Mathf.Max(place.Bounds.W, place.Bounds.H));
                    if (!byKind.TryGetValue(place.Kind, out var list))
                        byKind[place.Kind] = list = new List<string>();
                    list.Add($"{place.Bounds.W}x{place.Bounds.H}");
                }

                longest.Sort();
                Debug.Log($"[fit] ---- Ashcombe: {buildings} buildings ----");
                Debug.Log($"[fit] longest side: min {longest[0]:0.#}m, "
                        + $"median {longest[longest.Count / 2]:0.#}m, max {longest[longest.Count - 1]:0.#}m");
                foreach (var kv in byKind)
                    Debug.Log($"[fit] {kv.Key} x{kv.Value.Count}: {string.Join(" ", kv.Value.ToArray())}");
            }
            catch (Exception ex)
            {
                Debug.LogError("[fit] FAILED: " + ex);
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
