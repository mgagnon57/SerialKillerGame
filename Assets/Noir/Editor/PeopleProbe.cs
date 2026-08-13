using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Noir.Unity;

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
        /// <summary>
        /// Where the cast lives — <see cref="AgentBody.Folk"/> and not a copy of it.
        ///
        /// This was a private `const` holding the same string, which is the failure mode the
        /// public one exists to prevent: `AgentBody.Folk` was MADE public precisely so nobody
        /// would keep a second copy, and then a second copy was kept anyway. Two identical string
        /// literals in different assemblies do not drift on the day they are written, they drift
        /// on the day somebody moves the folder — and then this probe reports an empty cast while
        /// the game draws people perfectly well, which reads as a broken probe rather than a
        /// stale path.
        /// </summary>
        private const string Folk = AgentBody.Folk;

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

            // WHERE A CHARACTER'S UVs LAND. Universal_A_Alb is not a texture, it is a labelled
            // swatch grid: each ROW is a role - primary, secondary, tertiary, hair, skin, hide,
            // wood, metal - and each row is a ramp of shades across its columns. If a character's
            // vertices sit on discrete points of that grid then recolouring one is a matter of
            // moving a garment along its own row, which is the whole question behind wanting no
            // two people to look alike.
            foreach (var who in new[] { "Man_Slavic_Summer_Hair", "Woman_Slavic_Winter" })
            {
                string path = null;
                foreach (var guid in AssetDatabase.FindAssets($"{who} t:Prefab", new[] { Folk }))
                    path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == null) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var skin = go.GetComponentInChildren<SkinnedMeshRenderer>();
                var mesh = skin != null ? skin.sharedMesh : null;

                if (mesh != null)
                {
                    var uv = mesh.uv;
                    var cells = new SortedDictionary<string, int>(StringComparer.Ordinal);
                    foreach (var t in uv)
                    {
                        // Quantise to a 64 x 64 grid of the 4096 atlas, which is 64px a cell.
                        int cu = Mathf.Clamp((int)(t.x * 64f), 0, 63);
                        int cv = Mathf.Clamp((int)(t.y * 64f), 0, 63);
                        string key = $"{cu},{cv}";
                        cells[key] = cells.TryGetValue(key, out int had) ? had + 1 : 1;
                    }

                    var rows = new SortedSet<int>();
                    foreach (var c in cells.Keys) rows.Add(int.Parse(c.Split(',')[1]));

                    Debug.Log($"[folk] {who}: {uv.Length} vertices land on {cells.Count} distinct "
                            + $"atlas cells, across {rows.Count} rows (roles).");
                    foreach (var c in cells)
                        Debug.Log($"[folk]      cell {c.Key,-8} {c.Value,4} vertices");
                }

                UnityEngine.Object.DestroyImmediate(go);
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
