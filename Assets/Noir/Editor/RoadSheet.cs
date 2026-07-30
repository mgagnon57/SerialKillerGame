using System.IO;
using UnityEditor;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// Photographs the road kit from directly overhead, one tile per slot, so the PAINTED LANES
    /// can be counted rather than inferred from a bounding box.
    ///
    /// The measurement says the City mainroad carries 12m of asphalt. Twelve metres is four 3m
    /// lanes or three 4m ones, and a bounding box cannot tell you which the artist painted. This
    /// can.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.RoadSheet.Shoot -logFile &lt;log&gt;
    /// </summary>
    public static class RoadSheet
    {
        private const string City = "Assets/polyperfect/Poly Universal Pack/Prefabs/City/Roads City/";
        private const int Width = 1800, Height = 620;

        /// <summary>Left to right, and named in the log so the picture can be read.</summary>
        private static readonly string[] Top =
        {
            "Freeway_Straight_30x30_City",
            "Freeway_Crosswalk_30x30_City",
            "Freeway_Cross_30x30_City",
            "Freeway_T_30x30_City",
            "FreewayToMainroad_30x30_City",
            "FreewayToMainroad_Exit_30x30_City",
        };

        private static readonly string[] Bottom =
        {
            "FreewayToMainroad_Cross_30x30_City",
            "FreewayToMainroad_Cross_B_30x30_City",
            "FreewayToMainroad_T_30x30_City",
            "Mainroad_Cross_Crosswalk_30x30_City",
            "MainroadToRoad_Exit_30x30_City",
            "Mainroad_Stop_30x30_City",
        };

        [MenuItem("Noir/Photograph Road Kit")]
        public static void Shoot()
        {
            var root = new GameObject("RoadSheet");
            var camGo = new GameObject("SheetCam");
            var sunGo = new GameObject("SheetSun");

            try
            {
                for (int i = 0; i < Top.Length; i++) Put(root.transform, Top[i], i * 40f + 30f, 0f);
                for (int i = 0; i < Bottom.Length; i++) Put(root.transform, Bottom[i], i * 40f + 30f, -40f);

                Debug.Log("[sheet] TOP ROW:    " + string.Join(" | ", Top));
                Debug.Log("[sheet] BOTTOM ROW: " + string.Join(" | ", Bottom));

                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.1f;
                sunGo.transform.rotation = Quaternion.Euler(75f, 20f, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.62f);

                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 42f;                     // half the vertical span, in metres
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.10f, 0.11f, 0.13f);
                cam.nearClipPlane = 1f;
                cam.farClipPlane = 500f;
                camGo.transform.position = new Vector3(125f, 100f, -35f);
                camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

                var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "snapshots"));
                Directory.CreateDirectory(dir);
                Capture(cam, Path.Combine(dir, "road-kit.png"));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(sunGo);
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Put(Transform parent, string name, float x, float z)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(City + name + ".prefab");
            if (prefab == null) { Debug.LogWarning("[sheet] missing " + name); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, 0f, z);
        }

        private static void Capture(Camera cam, string path)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;

            var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            shot.Apply();
            File.WriteAllBytes(path, shot.EncodeToPNG());

            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(shot);
            rt.Release();
            Object.DestroyImmediate(rt);
            Debug.Log("[sheet] wrote " + path);
        }
    }
}
