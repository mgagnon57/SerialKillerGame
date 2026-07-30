using System.IO;
using UnityEditor;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// Can the pack's traffic light be made to show red, amber and green?
    ///
    /// Its head is one mesh with three materials - M_Universal_A for the body and two shared
    /// "glass night" emissives - and both emissives are warm white. So the lens COLOURS are
    /// either baked into the atlas UVs, in which case nothing can switch them, or they are those
    /// two submeshes, in which case a MaterialPropertyBlock per submesh can.
    ///
    /// Four copies: untouched, submesh 1 forced red, submesh 2 forced green, both forced blue.
    /// Whichever parts change colour are the parts a signal controller can drive.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.SignalProbe.Shoot -logFile &lt;log&gt;
    /// </summary>
    public static class SignalProbe
    {
        private const string Lights = "Assets/polyperfect/Poly Universal Pack/Prefabs/City/TrafficLights City/";
        private const int Width = 1400, Height = 700;

        [MenuItem("Noir/Probe Traffic Light")]
        public static void Shoot()
        {
            var root = new GameObject("SignalProbe");
            var camGo = new GameObject("ProbeCam");
            var sunGo = new GameObject("ProbeSun");

            try
            {
                var tints = new[]
                {
                    (sub: -1, colour: Color.white),
                    (sub:  1, colour: new Color(3f, 0.1f, 0.1f)),
                    (sub:  2, colour: new Color(0.1f, 3f, 0.1f)),
                    (sub: -2, colour: new Color(0.1f, 0.3f, 3f)),
                };

                for (int i = 0; i < tints.Length; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        Lights + "Traffic_Light_A_City.prefab");
                    if (prefab == null) { Debug.LogError("[signal] missing prefab"); return; }

                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = new Vector3(i * 3f, 0f, 0f);

                    var (sub, colour) = tints[i];
                    if (sub == -1) continue;

                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                    {
                        Debug.Log($"[signal] copy {i}: {r.name} has "
                                + $"{r.sharedMaterials.Length} materials");

                        var block = new MaterialPropertyBlock();
                        block.SetColor("_EmissionColor", colour);
                        block.SetColor("_BaseColor", colour);

                        if (sub == -2)
                            for (int s = 0; s < r.sharedMaterials.Length; s++) r.SetPropertyBlock(block, s);
                        else if (sub < r.sharedMaterials.Length)
                            r.SetPropertyBlock(block, sub);
                    }
                }

                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 0.8f;
                sunGo.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.25f, 0.26f, 0.30f);

                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.06f, 0.06f, 0.08f);
                cam.fieldOfView = 30f;
                camGo.transform.position = new Vector3(4.5f, 3.6f, 9f);
                camGo.transform.LookAt(new Vector3(4.5f, 3.2f, 0f));

                var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "snapshots"));
                Directory.CreateDirectory(dir);

                var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                shot.Apply();
                File.WriteAllBytes(Path.Combine(dir, "signal-probe.png"), shot.EncodeToPNG());
                cam.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(shot);
                rt.Release();
                Object.DestroyImmediate(rt);

                Debug.Log("[signal] left to right: untouched | submesh 1 red | submesh 2 green | all blue");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(sunGo);
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
