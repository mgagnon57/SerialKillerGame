using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// What the pack can actually make a crowd out of.
    ///
    /// The simulation already describes people it has never drawn: `PersonDescription` carries
    /// ApparentSex, HeightBand, BuildBand, AgeBand, ClothingTone and CarriedThing, and STATE.md
    /// records that not one of them is ever constructed - the running game observes nothing. This
    /// asks the other half of that question: of those six, which can be EXPRESSED with the models
    /// we own, and which cannot.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.PeopleProbe.Run
    /// </summary>
    public static class PeopleProbe
    {
        private const string Folk = "Assets/polyperfect/Poly Universal Pack/Prefabs/People";

        [MenuItem("Noir/Probe The People")]
        public static void Run()
        {
            var sets = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Folk }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string set = Path.GetFileName(Path.GetDirectoryName(path));
                if (!sets.TryGetValue(set, out var list)) sets[set] = list = new List<string>();
                list.Add(path);
            }

            int rigged = 0, coloured = 0, multiMaterial = 0;
            var heights = new List<float>();

            foreach (var set in sets)
            {
                Debug.Log($"[folk] === {set.Key} ({set.Value.Count})");
                set.Value.Sort(StringComparer.Ordinal);

                foreach (var path in set.Value)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;

                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    go.transform.position = Vector3.zero;

                    var skin = go.GetComponentInChildren<SkinnedMeshRenderer>();
                    var mesh = skin != null ? skin.sharedMesh : null;
                    var rends = go.GetComponentsInChildren<Renderer>();

                    float height = 0f;
                    if (rends.Length > 0)
                    {
                        var b = rends[0].bounds;
                        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                        height = b.size.y;
                    }

                    int slots = skin != null ? skin.sharedMaterials.Length : 0;
                    int subs = mesh != null ? mesh.subMeshCount : 0;
                    bool vcol = mesh != null && mesh.colors != null && mesh.colors.Length > 0;
                    bool hasHands = go.GetComponentsInChildren<Transform>().Length > 20;

                    if (skin != null) rigged++;
                    if (vcol) coloured++;
                    if (slots > 1) multiMaterial++;
                    if (height > 0.2f) heights.Add(height);

                    Debug.Log($"[folk]   {Path.GetFileNameWithoutExtension(path),-42} "
                            + $"{height:0.00}m  {(skin != null ? "rigged" : "STATIC")}  "
                            + $"{slots} material{(slots == 1 ? "" : "s")}, {subs} submesh"
                            + $"{(subs == 1 ? "" : "es")}"
                            + $"{(vcol ? ", vertex colours" : "")}"
                            + $"{(hasHands ? "" : ", NO SKELETON")}");

                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            heights.Sort();
            Debug.Log($"[folk] ---- {heights.Count} figures: rigged {rigged}, "
                    + $"more than one material {multiMaterial}, with vertex colours {coloured}.");
            if (heights.Count > 0)
                Debug.Log($"[folk] heights {heights[0]:0.00}m to {heights[heights.Count - 1]:0.00}m, "
                        + $"median {heights[heights.Count / 2]:0.00}m");

            // What a person could be seen CARRYING. The observation system has a CarriedThing and
            // the rigs have hand bones, so this is the one description that needs props rather
            // than character variants.
            foreach (var where in new[]
                     { "Prefabs/Survival", "Prefabs/City/Props City", "Prefabs/Food", "Prefabs/Tools" })
            {
                int n = 0;
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[]
                         { "Assets/polyperfect/Poly Universal Pack/" + where }))
                {
                    string f = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                    if (f.StartsWith("Bag") || f.StartsWith("Backpack") || f.StartsWith("Suitcase")
                        || f.StartsWith("Box") || f.StartsWith("Case") || f.StartsWith("Umbrella")
                        || f.StartsWith("Basket") || f.StartsWith("Bucket") || f.StartsWith("Bottle")) n++;
                }
                Debug.Log($"[folk] carryable in {where}: {n}");
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
