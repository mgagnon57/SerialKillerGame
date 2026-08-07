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
    /// The name-that-building test, made repeatable.
    ///
    /// One straight-on render per building kind, with NO SIGN, NO FRONTAGE, NO PROPS and no
    /// label in the picture. You look at eleven images, you name each one, and anything you
    /// cannot name has failed to read.
    ///
    /// WHY IT STRIPS THE SIGNS. Frontage.cs already tells buildings apart well - a hanging board
    /// for the pub, a brass plate for the surgery, a name cut in stone for the school - and that
    /// is exactly why it has to come off for this test. A picture with a pub sign in it does not
    /// answer whether the pub reads as a pub; it answers whether you can read. The thing on trial
    /// here is the SILHOUETTE, which is the only signal that survives the distance the overview
    /// camera watches from.
    ///
    /// This grades the village rather than proving the code runs, which puts it with `ratio` and
    /// `street` rather than with the smoke test. It has no pass mark and it never fails a build:
    /// its output is eleven pictures and a person.
    ///
    /// Run headlessly:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.Elevations.Render
    /// </summary>
    public static class Elevations
    {
        private const int Width = 900;
        private const int Height = 700;

        /// <summary>Everything between the camera and the shape it is there to judge.</summary>
        private static readonly string[] Stripped = { "Frontage", "Props", "Countryside" };

        private static string OutputDir =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "docs", "elevations");

        [MenuItem("Noir/Render Elevations")]
        public static void Render()
        {
            GameObject root = null, camGo = null, sunGo = null;
            int written = 0;

            var wasSky = RenderSettings.skybox;
            var wasSun = RenderSettings.sun;
            bool wasFog = RenderSettings.fog;
            var wasAmbientMode = RenderSettings.ambientMode;
            var wasAmbient = RenderSettings.ambientLight;

            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float wasShadowDistance = pipeline != null ? pipeline.shadowDistance : 0f;

            try
            {
                Directory.CreateDirectory(OutputDir);

                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
                var layout = VillageParser.Parse(ContentLoader.Read("fixture-village.txt"));

                // Deliberately UNSURVEYED. This is the fixture village, not Rossville: the survey
                // passes are keyed to the real town's parcel ids and would silently do nothing
                // here. Do not "fix" this into TownPipeline.Build().
                var built = TownPipeline.BuildUnsurveyed(layout, VillageHost.Seed, "fixture-village.txt");
                var world = built.World;

                root = new GameObject("ElevationVillage");
                VillageMesh.Build(world, root.transform);

                foreach (string name in Stripped)
                {
                    var part = root.transform.Find("Village/" + name);
                    if (part != null) part.gameObject.SetActive(false);
                }

                if (pipeline != null) pipeline.shadowDistance = 200f;

                // Flat, even light from over the camera's shoulder. A raking sun would carve the
                // shape with shadow, which flatters it - the question is whether the OUTLINE
                // carries, so the lighting must not do the outline's job for it.
                sunGo = new GameObject("ElevationSun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.35f;
                sun.intensity = 1.1f;
                sunGo.transform.rotation = Quaternion.Euler(38f, 150f, 0f);

                RenderSettings.skybox = null;
                RenderSettings.sun = sun;
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.42f, 0.45f, 0.50f);

                camGo = new GameObject("ElevationCamera");
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 35f;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 1000f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.62f, 0.67f, 0.74f);

                // One building per kind, and the FIRST of that kind in table order rather than
                // the biggest or the prettiest - the test is worthless if the specimen is chosen
                // to flatter the result.
                var seen = new HashSet<PlaceKind>();
                var subjects = new List<Place>();
                foreach (var place in world.AllPlaces)
                {
                    if (!PlaceKindTable.Current.Row(place.Kind).IsBuilding) continue;
                    if (!seen.Add(place.Kind)) continue;
                    subjects.Add(place);
                }

                foreach (var place in subjects)
                {
                    var b = place.Bounds;
                    var m = MassingGrammars.Of(place);

                    float top = m.Eaves + m.Pitch;
                    float centreX = b.X + b.W * 0.5f;
                    float centreZ = -(b.Y + b.H * 0.5f);

                    // Far enough back that the tallest thing in the picture - a church spire is
                    // more than three times its own walls - still fits.
                    float span = Mathf.Max(Mathf.Max(b.W, b.H), top * 2.2f);
                    float distance = span * 1.9f + 8f;

                    var target = new Vector3(centreX, top * 0.45f, centreZ);
                    var eye = target + new Vector3(distance * 0.42f, distance * 0.34f, distance * 0.84f);

                    camGo.transform.position = eye;
                    camGo.transform.LookAt(target);

                    string file = PlaceKindTable.Current.Row(place.Kind).Name.ToLowerInvariant();
                    string path = Path.Combine(OutputDir, file + ".png");
                    Capture(cam, path);
                    written++;

                    Debug.Log($"[elevation] {file,-12} eaves {m.Eaves:0.0}  {m.Roof}  "
                            + $"pitch {m.Pitch:0.0}  -> {path}");
                }

                Debug.Log($"[elevation] {written} kinds rendered to {OutputDir}");
            }
            catch (Exception ex)
            {
                Debug.LogError("Elevations failed: " + ex);
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
                if (sunGo != null) UnityEngine.Object.DestroyImmediate(sunGo);

                // As in Snapshot: rendering a set of stills must not leave the open scene's sky,
                // fog and ambient at whatever the last shot happened to want.
                RenderSettings.skybox = wasSky;
                RenderSettings.sun = wasSun;
                RenderSettings.fog = wasFog;
                RenderSettings.ambientMode = wasAmbientMode;
                RenderSettings.ambientLight = wasAmbient;
                if (pipeline != null) pipeline.shadowDistance = wasShadowDistance;
            }

            if (Application.isBatchMode) EditorApplication.Exit(written > 0 ? 0 : 1);
        }

        private static void Capture(Camera cam, string path)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 2
            };

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
        }
    }
}
