using UnityEditor;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// Measures where the CARRIAGEWAY actually is inside a road tile.
    ///
    /// The tile is 10m square and carries three materials - M_Road_Paved, M_Sidewalk_Paved and
    /// M_Universal_A - which means the ten metres is tarmac AND pavement, not tarmac. Lanes were
    /// placed by assuming the whole tile was road and offsetting by eye, which is how a car ends
    /// up with two wheels on the kerb.
    ///
    /// This reports the extent of each submesh separately, so the lane offset can come from the
    /// mesh rather than from a guess.
    /// </summary>
    public static class RoadProbe
    {
        private const string Roads =
            "Assets/polyperfect/Poly Universal Pack/Prefabs/Modular Parts/Roads/";

        [MenuItem("Noir/Probe Road Tile")]
        public static void Probe()
        {
            foreach (var name in new[] { "Road_Paved_Straight_10x10m", "Mainroad_Paved_Straight_10x10m", "Mainroad_To_Road_Paved_Straight_10x10m" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Roads + name + ".prefab");
                if (prefab == null) { Debug.Log($"[road] MISSING {name}"); continue; }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.identity;

                var mf = go.GetComponentInChildren<MeshFilter>();
                var mr = go.GetComponentInChildren<MeshRenderer>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var mesh = mf.sharedMesh;
                    var verts = mesh.vertices;

                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        var tris = mesh.GetTriangles(s);
                        if (tris.Length == 0) continue;

                        float minX = float.MaxValue, maxX = float.MinValue;
                        float minZ = float.MaxValue, maxZ = float.MinValue;
                        foreach (var t in tris)
                        {
                            var v = verts[t];
                            if (v.x < minX) minX = v.x;
                            if (v.x > maxX) maxX = v.x;
                            if (v.z < minZ) minZ = v.z;
                            if (v.z > maxZ) maxZ = v.z;
                        }

                        string mat = s < mr.sharedMaterials.Length && mr.sharedMaterials[s] != null
                            ? mr.sharedMaterials[s].name : "?";
                        Debug.Log($"[road] {name} submesh {s} = {mat}: "
                                + $"x {minX:0.##}..{maxX:0.##}  z {minZ:0.##}..{maxZ:0.##}");
                    }
                }
                UnityEngine.Object.DestroyImmediate(go);
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
