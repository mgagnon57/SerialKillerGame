using System;
using UnityEditor;
using UnityEngine;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Scratch tool: prints the pack's pivot conventions and a shortlist of Ashcombe cottages,
    /// so a building can be assembled from pack pieces without guessing where each prefab's
    /// origin sits. Delete once the catalog in PolyPackBuilding is settled.
    /// </summary>
    public static class PolyPackProbe
    {
        private const string Parts = "Assets/polyperfect/Poly Universal Pack/Prefabs/Modular Parts/";

        private static readonly string[] Interesting =
        {
            Parts + "Walls/3m/Walls Plain/Wall_1x3m_Ext.prefab",
            Parts + "Walls/3m/Walls Plain/Wall_5x3m_Ext.prefab",
            Parts + "Walls/3m/Walls Plain/Wall_Door_Regular_2x3m_Ext.prefab",
            Parts + "Walls/3m/Walls Plain/Wall_Window_2x3m_Ext.prefab",
            Parts + "Walls/3m/Walls Plain/Wall_Corner_Out_Basic_3m.prefab",
            Parts + "Walls/3m/Walls Plain/Wall_Roof_Corner_Top_2x2m_Ext.prefab",
            Parts + "Walls/3m/Walls Plain/Wall_Roof_Corner_Down_2x2m_Ext.prefab",
            Parts + "Walls/3m/Walls Plain/Wall_Roof_Center_1x2m_Ext.prefab",
            Parts + "Roofs/Roof Regular/Roof_Regular_1x1m.prefab",
            Parts + "Roofs/Roof Regular/Roof_Regular_2x2m.prefab",
            Parts + "Roofs/Roof Regular/Roof_Regular_1m_Edge.prefab",
            Parts + "Roofs/Roof Regular/Roof_Regular_1m_Front.prefab",
            Parts + "Roofs/Roof Regular/Roof_Regular_Edge_Front.prefab",
            Parts + "Chimneys/Chimney_3m_A_Fantasy.prefab",
            Parts + "Chimneys/Chimney_Top_A_Fantasy.prefab",
            Parts + "Doors/Doors Fantasy/Door_Single_A_Regular_Fantasy.prefab",
            Parts + "Doors/Doors City/Door_Single_A_Regular_City.prefab",
            Parts + "Windows/Windows Fantasy/Window_Wood_Small_A_Fantasy.prefab",
            Parts + "Windows/Windows City/Window_A_City.prefab",
            Parts + "Windows/Windows City/Window_B_City.prefab",

            // The dressing layer: eaves, rakes and gutters, none of which the bare cottage uses.
            Parts + "Roofs/Roof Regular/Roof_Regular_2m_Edge.prefab",
            Parts + "Roofs/Roof Regular/Roof_Regular_2m_Front.prefab",
            Parts + "Roofs/Roof Regular/Roof_Regular_Edge_Corner_A.prefab",
            Parts + "Roofs/Roof Regular/Roof_Regular_Edge_Corner_B.prefab",
            Parts + "Roofs/Gutter/Gutter_Regular_2m.prefab",
            Parts + "Roofs/Gutter/Gutter_Regular_End.prefab",
            Parts + "Roofs/Gutter/Gutter_Regular_Corner.prefab",
            Parts + "Roofs/Gutter/Gutter_Pipe_3m.prefab",
            Parts + "Roofs/Gutter/Gutter_Pipe_Top_Regular.prefab",
            Parts + "Roofs/Gutter/Gutter_Pipe_Bottom.prefab",
        };

        [MenuItem("Noir/Probe Poly Pack")]
        public static void Probe()
        {
            foreach (var path in Interesting)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { Debug.Log($"[probe] MISSING {path}"); continue; }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.identity;

                var rends = go.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) { Debug.Log($"[probe] {System.IO.Path.GetFileName(path)}  NO RENDERER"); }
                else
                {
                    var b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    string mats = "";
                    foreach (var m in rends[0].sharedMaterials) mats += (m ? m.name : "null") + " ";
                    Debug.Log($"[probe] {System.IO.Path.GetFileName(path)}  "
                            + $"min({b.min.x:0.##},{b.min.y:0.##},{b.min.z:0.##}) "
                            + $"max({b.max.x:0.##},{b.max.y:0.##},{b.max.z:0.##}) "
                            + $"size({b.size.x:0.##},{b.size.y:0.##},{b.size.z:0.##}) "
                            + $"rends={rends.Length} mat0={mats.Trim()}");
                }
                Corners(go, System.IO.Path.GetFileName(path));
                UnityEngine.Object.DestroyImmediate(go);
            }

            // A handful of the smallest dwellings, for something to stand the pack house next to.
            try
            {
                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
                var layout = VillageParser.Parse(ContentLoader.Read("village.txt"));
                var world = WorldBuilder.Build(layout, VillageHost.Seed);

                int shown = 0;
                foreach (var place in world.AllPlaces)
                {
                    if (place.Kind != PlaceKind.Dwelling) continue;
                    var r = place.Bounds;
                    if (r.W > 8 || r.H > 8) continue;
                    Debug.Log($"[probe] dwelling '{place.Name}' at {r} centre {r.Centre.X},{r.Centre.Y}");
                    if (++shown >= 12) break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[probe] village read failed: " + ex.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// Which corner of a piece's footprint is high. Bounds alone say a roof tile is 1m tall
        /// but not which edge the ridge is on, and guessing that wrong builds the roof upside
        /// down.
        /// </summary>
        private static void Corners(GameObject go, string label)
        {
            var mf = go.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;

            var verts = mf.sharedMesh.vertices;
            if (verts.Length == 0) return;

            var t = mf.transform;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                var w = t.TransformPoint(verts[i]);
                if (w.x < minX) minX = w.x;
                if (w.x > maxX) maxX = w.x;
                if (w.z < minZ) minZ = w.z;
                if (w.z > maxZ) maxZ = w.z;
            }
            if (maxX - minX < 0.01f || maxZ - minZ < 0.01f) return;

            // Highest vertex within a small radius of each footprint corner.
            var corner = new[]
            {
                (name: "x-z-", x: minX, z: minZ, y: float.MinValue),
                (name: "x+z-", x: maxX, z: minZ, y: float.MinValue),
                (name: "x-z+", x: minX, z: maxZ, y: float.MinValue),
                (name: "x+z+", x: maxX, z: maxZ, y: float.MinValue),
            };
            for (int i = 0; i < verts.Length; i++)
            {
                var w = t.TransformPoint(verts[i]);
                for (int c = 0; c < corner.Length; c++)
                    if (Mathf.Abs(w.x - corner[c].x) < 0.25f && Mathf.Abs(w.z - corner[c].z) < 0.25f
                        && w.y > corner[c].y)
                        corner[c].y = w.y;
            }

            string s = "";
            foreach (var c in corner) s += $"{c.name}={(c.y == float.MinValue ? "-" : c.y.ToString("0.##"))} ";
            Debug.Log($"[probe]   corners {label}: {s.Trim()}");

            // Height profile across x. Corner sampling cannot tell a triangle that peaks in the
            // middle from one that rises to an end, and that is exactly the difference between
            // a gable that fits and one that leaves a hole.
            string prof = "";
            for (int k = 0; k <= 8; k++)
            {
                float px = Mathf.Lerp(minX, maxX, k / 8f);
                float hi = float.MinValue;
                for (int i = 0; i < verts.Length; i++)
                {
                    var w = t.TransformPoint(verts[i]);
                    if (Mathf.Abs(w.x - px) < 0.2f && w.y > hi) hi = w.y;
                }
                prof += (hi == float.MinValue ? "-" : hi.ToString("0.#")) + " ";
            }
            Debug.Log($"[probe]   profile {label} (x {minX:0.#}->{maxX:0.#}): {prof.Trim()}");
        }
    }
}
