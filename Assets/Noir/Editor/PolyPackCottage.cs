using System;
using System.Collections.Generic;
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
    /// Photographs the Poly Universal Pack prototype cottage beside a real Ashcombe cottage under
    /// the same sun, sky and fog.
    ///
    /// The question this answers is not "does the pack import" - it does - but "does a house
    /// built from it belong in this village". Both frames use SunRig's own curve and the same
    /// camera geometry, so the only thing differing between them is the building.
    ///
    /// The cottage itself is assembled by PolyPackCottageBuilder, which lives in the runtime
    /// assembly so the same building can also be stood up in play mode by PolyPackTrial. There is
    /// one copy of the placement rules and this is not it.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.PolyPackCottage.Render -logFile &lt;log&gt;
    /// </summary>
    public static class PolyPackCottage
    {
        private const int Width = 1600, Height = 900;

        private static string OutputDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "snapshots"));

        [MenuItem("Noir/Render Poly Pack Cottage")]
        public static void Render()
        {
            GameObject root = null, cottage = null, camGo = null, sunGo = null;
            Volume fx = null;
            Material sky = null;
            int written = 0;

            var wasSky = RenderSettings.skybox;
            var wasSun = RenderSettings.sun;
            bool wasFog = RenderSettings.fog;
            var wasFogMode = RenderSettings.fogMode;
            var wasFogColour = RenderSettings.fogColor;
            float wasFogDensity = RenderSettings.fogDensity;
            var wasAmbientMode = RenderSettings.ambientMode;
            var wasAmbient = RenderSettings.ambientLight;

            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float wasShadowDistance = pipeline != null ? pipeline.shadowDistance : 0f;
            int wasCascades = pipeline != null ? pipeline.shadowCascadeCount : 0;

            try
            {
                Directory.CreateDirectory(OutputDir);

                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
                var layout = VillageParser.Parse(ContentLoader.Read("village.txt"));
                var world = WorldBuilder.Build(layout, VillageHost.Seed);

                root = new GameObject("CompareVillage");
                VillageMesh.Build(world, root.transform);

                if (pipeline != null) { pipeline.shadowDistance = 320f; pipeline.shadowCascadeCount = 4; }

                sunGo = new GameObject("CompareSun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.7f;

                camGo = new GameObject("CompareCamera");
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
                sun.enabled = intensity > 0.01f;
                sunGo.transform.rotation = SunRig.SunRotation(hour);
                RenderSettings.ambientLight = ambient;
                RenderSettings.fogColor = SunRig.FogAt(colour, intensity, ambient);
                RenderSettings.fogDensity = Mathf.Lerp(0.0022f, 0.0010f, Mathf.Clamp01(intensity));
                if (sky != null && sky.HasProperty("_Exposure"))
                    sky.SetFloat("_Exposure", Mathf.Lerp(0.18f, 1.15f, Mathf.Clamp01(intensity)));

                // ---- shot A: a real Ashcombe cottage ----
                var subject = FindCottage(world);
                var at = new Vector3(subject.Centre.X, 0f, -subject.Centre.Y);
                Debug.Log($"[cottage] Ashcombe subject {subject}");
                Frame(camGo, at, 26f, 30f);
                Capture(cam, Path.Combine(OutputDir, "compare-ashcombe.png"));
                written++;

                // ---- shot B: the same view of a pack cottage on open ground ----
                // Out past the village edge rather than on the green: the green has trees on it
                // that sit between the camera and anything standing there.
                var pad = new Vector3(world.Width + 40f, 0f, -world.Height / 2f);
                var origin = pad - new Vector3(PolyPackCottageBuilder.W / 2f, 0f,
                                               PolyPackCottageBuilder.D / 2f);
                cottage = PolyPackCottageBuilder.Assemble(origin);
                Report(cottage);
                Frame(camGo, pad, 26f, 30f);
                Capture(cam, Path.Combine(OutputDir, "compare-polypack.png"));
                written++;

                // Close enough to read the walls. At two dozen metres it is not possible to tell
                // a plaster panel from a hole where one should be.
                Frame(camGo, pad + Vector3.up * 1.5f, 13f, 10f);
                Capture(cam, Path.Combine(OutputDir, "compare-polypack-close.png"));

                // Straight on to the front, where the door and windows are.
                Frame(camGo, pad + Vector3.up * 1.5f, 15f, 6f, yaw: 0f);
                Capture(cam, Path.Combine(OutputDir, "compare-polypack-front.png"));
            }
            catch (Exception ex)
            {
                Debug.LogError("[cottage] FAILED: " + ex);
            }
            finally
            {
                if (cottage != null) UnityEngine.Object.DestroyImmediate(cottage);
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
                RenderSettings.fogMode = wasFogMode;
                RenderSettings.fogColor = wasFogColour;
                RenderSettings.fogDensity = wasFogDensity;
                RenderSettings.ambientMode = wasAmbientMode;
                RenderSettings.ambientLight = wasAmbient;

                if (pipeline != null)
                {
                    pipeline.shadowDistance = wasShadowDistance;
                    pipeline.shadowCascadeCount = wasCascades;
                }
            }

            Debug.Log($"[cottage] wrote {written} of 2");
            if (Application.isBatchMode) EditorApplication.Exit(written == 2 ? 0 : 1);
        }

        /// <summary>Same camera geometry for both frames, so only the building differs.</summary>
        private static void Frame(GameObject camGo, Vector3 target, float dist, float pitch,
                                  float yaw = 35f)
        {
            // Well back and pitched up: at seventeen metres and twenty degrees the camera stood
            // inside the neighbouring cottage and photographed its back wall.
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            camGo.transform.position = target + Vector3.up * 2f - rotation * Vector3.forward * dist;
            camGo.transform.rotation = rotation;
        }

        /// <summary>
        /// What actually got built and what it is painted with. A cottage that comes out one flat
        /// colour is either the wrong material or the wrong UVs, and the render alone cannot say
        /// which.
        /// </summary>
        private static void Report(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { Debug.LogWarning("[cottage] assembled nothing"); return; }

            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            Debug.Log($"[cottage] {rends.Length} pieces, bounds min({b.min.x:0.##},{b.min.y:0.##},{b.min.z:0.##}) "
                    + $"size({b.size.x:0.##},{b.size.y:0.##},{b.size.z:0.##})");

            var seen = new HashSet<string>();
            foreach (var r in rends)
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) { Debug.LogWarning($"[cottage] {r.name}: NULL material"); continue; }
                    if (!seen.Add(m.name)) continue;
                    var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                    Debug.Log($"[cottage]   mat {m.name} shader={m.shader.name} "
                            + $"baseMap={(tex ? tex.name : "NONE")} "
                            + $"colour={(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : "-")}");
                }
        }

        private static TileRect FindCottage(WorldModel world)
        {
            foreach (var place in world.AllPlaces)
                if (place.Kind == PlaceKind.Dwelling && place.Bounds.W <= 8 && place.Bounds.H <= 8)
                    return place.Bounds;
            throw new Exception("no small dwelling found");
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
            Debug.Log("[cottage] wrote " + path);
        }
    }
}
