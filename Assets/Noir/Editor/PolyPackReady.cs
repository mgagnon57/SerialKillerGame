using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Photographs the pack's READY-MADE buildings - the ones that arrive whole rather than as
    /// parts - under Ashcombe's own sun, at Ashcombe's own scale.
    ///
    /// The City set is the wrong shelf for this village twice over: its median building is 34.5m
    /// on the long side against Ashcombe's 11m, and it is a modern downtown - a casino, a
    /// hospital, four skyscrapers up to 111m. The Farm set is the right shelf, and it is already
    /// British: House_Farm_British at 13.8x15.1, Barn_Farm_British, Mill_Old. Those numbers sit
    /// inside the village's existing lots without anything being rescaled.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.PolyPackReady.Render -logFile &lt;log&gt;
    /// </summary>
    public static class PolyPackReady
    {
        private const int Width = 1600, Height = 900;
        private const string Farm = "Assets/polyperfect/Poly Universal Pack/Prefabs/Farm/";

        private static readonly (string path, string label, float gap)[] Subjects =
        {
            (Farm + "Buildings Farm/House_Farm_British.prefab", "house-british", 0f),
            (Farm + "Buildings Farm/Barn_Farm_British.prefab",  "barn-british",  26f),
            (Farm + "Buildings Farm/Mill_Old.prefab",           "mill-old",      50f),
        };

        private static string OutputDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "snapshots"));

        [MenuItem("Noir/Render Ready-Made Buildings")]
        public static void Render()
        {
            GameObject root = null, row = null, camGo = null, sunGo = null;
            Volume fx = null;
            Material sky = null;

            var wasSky = RenderSettings.skybox;
            var wasSun = RenderSettings.sun;
            bool wasFog = RenderSettings.fog;
            var wasFogColour = RenderSettings.fogColor;
            float wasFogDensity = RenderSettings.fogDensity;
            var wasAmbientMode = RenderSettings.ambientMode;
            var wasAmbient = RenderSettings.ambientLight;

            try
            {
                Directory.CreateDirectory(OutputDir);

                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
                var layout = VillageParser.Parse(ContentLoader.Read("village.txt"));
                var world = WorldBuilder.Build(layout, VillageHost.Seed);

                root = new GameObject("ReadyVillage");
                VillageMesh.Build(world, root.transform);

                sunGo = new GameObject("ReadySun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.7f;

                camGo = new GameObject("ReadyCamera");
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 45f;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 3000f;
                cam.clearFlags = CameraClearFlags.Skybox;

                fx = PostFx.Create(null);
                PostFx.EnableOn(cam);

                sky = Snapshot.MakeSky();
                RenderSettings.skybox = sky;
                RenderSettings.sun = sun;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.ambientMode = AmbientMode.Flat;

                const float hour = 13f;
                var (colour, intensity, ambient) = SunRig.SkyAt(hour);
                sun.color = colour;
                sun.intensity = intensity;
                sunGo.transform.rotation = SunRig.SunRotation(hour);
                RenderSettings.ambientLight = ambient;
                RenderSettings.fogColor = SunRig.FogAt(colour, intensity, ambient);
                RenderSettings.fogDensity = 0.0010f;
                if (sky != null && sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", 1.15f);

                // Out past the village edge on open ground, in a row.
                var pad = new Vector3(world.Width + 45f, 0f, -world.Height / 2f);
                row = new GameObject("ReadyRow");

                foreach (var (path, label, gap) in Subjects)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) { Debug.LogWarning("[ready] missing " + path); continue; }

                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    go.transform.SetParent(row.transform, false);
                    go.transform.position = pad + new Vector3(gap, 0f, 0f);

                    var rends = go.GetComponentsInChildren<Renderer>();
                    if (rends.Length > 0)
                    {
                        var b = rends[0].bounds;
                        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                        Debug.Log($"[ready] {label} {b.size.x:0.#}x{b.size.z:0.#}m h{b.size.y:0.#} "
                                + $"{rends.Length} renderers");
                    }
                }

                // One frame with all three, then a close on the British farmhouse.
                Frame(camGo, pad + new Vector3(25f, 0f, 0f), 62f, 24f);
                Capture(cam, Path.Combine(OutputDir, "ready-row.png"));

                Frame(camGo, pad + new Vector3(0f, 1.5f, 0f), 22f, 12f);
                Capture(cam, Path.Combine(OutputDir, "ready-house-british.png"));
            }
            catch (Exception ex)
            {
                Debug.LogError("[ready] FAILED: " + ex);
            }
            finally
            {
                if (row != null) UnityEngine.Object.DestroyImmediate(row);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
                if (sunGo != null) UnityEngine.Object.DestroyImmediate(sunGo);
                if (fx != null)
                {
                    UnityEngine.Object.DestroyImmediate(fx.sharedProfile);
                    UnityEngine.Object.DestroyImmediate(fx.gameObject);
                }
                if (sky != null) UnityEngine.Object.DestroyImmediate(sky);

                RenderSettings.skybox = wasSky;
                RenderSettings.sun = wasSun;
                RenderSettings.fog = wasFog;
                RenderSettings.fogColor = wasFogColour;
                RenderSettings.fogDensity = wasFogDensity;
                RenderSettings.ambientMode = wasAmbientMode;
                RenderSettings.ambientLight = wasAmbient;
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Frame(GameObject camGo, Vector3 target, float dist, float pitch)
        {
            var rotation = Quaternion.Euler(pitch, 35f, 0f);
            camGo.transform.position = target + Vector3.up * 2f - rotation * Vector3.forward * dist;
            camGo.transform.rotation = rotation;
        }

        private static void Capture(Camera cam, string path)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            var previousTarget = cam.targetTexture;
            var previousActive = RenderTexture.active;

            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;

            var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            shot.Apply();
            File.WriteAllBytes(path, shot.EncodeToPNG());

            cam.targetTexture = previousTarget;
            RenderTexture.active = previousActive;

            UnityEngine.Object.DestroyImmediate(shot);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            Debug.Log("[ready] wrote " + path);
        }
    }
}
