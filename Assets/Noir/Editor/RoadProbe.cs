using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// Measures where the CARRIAGEWAY actually is inside a road tile.
    ///
    /// The first version of this probed the Modular Parts kit and found the lesson that a 10m
    /// tile is 6m of tarmac and 4m of pavement. It is kept and widened because the pack turns
    /// out to have a SECOND road kit - Prefabs/City/Roads City - built at 30m with painted lanes,
    /// stop lines, crosswalks and parking bays, which is the kit a city actually wants.
    ///
    /// Reports per-submesh extents via SubMeshDescriptor.bounds, which is importer metadata and
    /// so works whether or not Read/Write is enabled on the model.
    /// </summary>
    public static class RoadProbe
    {
        private const string Parts = "Assets/polyperfect/Poly Universal Pack/Prefabs/Modular Parts/Roads/";
        private const string City  = "Assets/polyperfect/Poly Universal Pack/Prefabs/City/Roads City/";

        [MenuItem("Noir/Probe Road Tile")]
        public static void Probe()
        {
            Debug.Log("=== MODULAR PARTS (the 10m kit the city is built from today) ===");
            foreach (var n in new[]
            {
                "Road_Paved_Straight_10x10m", "Mainroad_Paved_Straight_10x10m", "Road_Paved_X_10x10m",
            })
                One(Parts + n + ".prefab", n);

            Debug.Log("=== ROADS CITY (the 30m kit, never used) ===");
            foreach (var n in new[]
            {
                "Mainroad_Straight_30x30_City", "Mainroad_Cross_30x30_City",
                "Mainroad_Cross_Crosswalk_30x30_City", "Mainroad_T_Crosswalk_30x30_City",
                "Mainroad_Crosswalk_City", "Mainroad_Stop_30x30_City",
                "Mainroad_Stop_Middle_30x30_City", "MainRoad_Stop_Start_30x30_City",
                "Mainroad_Stop_Start_L_30x30_City", "Mainroad_Stop_Start_R_30x30_City",
                "Mainroad_Turn_30x30_City", "Mainroad_End_30x30_City",
                "Road_Straight_10x10_City", "Road_Cross_10x10_City", "Road_Crosswalk_10x10_City",
                "Road_Stop_10x10_City", "Road_T_10x10_City", "Road_Turn_10x10_City",
                "Road_Parking_10x10_City", "Road_Parking_Side_10x10_City",
                "MainroadToRoad_30x30_City", "MainroadToParking_30x30_City",
                "Sidewalk_30x30_City", "Sidewalk_10x10_City",
                "Freeway_Straight_30x30_City",
            })
                One(City + n + ".prefab", n);

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void One(string path, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.Log($"[road] MISSING {name}"); return; }

            var lines = new List<string>();
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mesh == null || mr == null) continue;

                // Where this piece sits relative to the prefab root, so multi-part prefabs read
                // in one coordinate system rather than each in its own.
                var off = mf.transform.position - prefab.transform.position;

                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var b = mesh.GetSubMesh(s).bounds;
                    string mat = s < mr.sharedMaterials.Length && mr.sharedMaterials[s] != null
                        ? mr.sharedMaterials[s].name : "?";
                    lines.Add($"    {mat,-24} "
                            + $"x {b.min.x + off.x,7:0.##}..{b.max.x + off.x,-7:0.##} "
                            + $"y {b.min.y + off.y,6:0.##}..{b.max.y + off.y,-6:0.##} "
                            + $"z {b.min.z + off.z,7:0.##}..{b.max.z + off.z,-7:0.##}");
                }
            }

            Debug.Log($"[road] {name}\n{string.Join("\n", lines)}");
        }
    }
}
