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
    /// The village waking up, one frame a minute.
    ///
    /// The twelve snapshots are stills, and a still cannot show the thing this village is
    /// actually for: nobody is anywhere in particular at a given instant, but over four hours the
    /// farms get up, the lamps go off, the lights come on in kitchens and go out again, and at
    /// twenty to nine a school gate collects twenty-seven children. That is a sequence, and it
    /// wants a sequence to look at.
    ///
    /// Frames land in docs/film/ as PNGs and are encoded outside Unity. Nothing here writes video.
    ///
    /// ONE SIMULATION RUN, FORWARDS. Crowd.PoseAt ticks the sim up to the minute asked for and
    /// restarts the whole village if asked to go backwards, so the loop below must only ever
    /// count up. That is also why this is cheap: four and a half hours of village is 5,400 ticks
    /// total, not 5,400 ticks per frame.
    ///
    /// Run headlessly:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.Film.Render
    /// </summary>
    public static class Film
    {
        private const int Width = 960;
        private const int Height = 540;

        /// <summary>05:00 to 09:30 — dark, first light, lamps off, the school run at 08:35.</summary>
        private const float FromHour = 5.0f;
        private const float ToHour = 9.5f;

        /// <summary>One frame a village minute. At 24 fps that is 11 seconds for the morning.</summary>
        private const float StepHours = 1f / 60f;

        private static string OutputDir =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "docs", "film");

        private static bool _xray;

        /// <summary>
        /// The same morning with the buildings taken away.
        ///
        /// The ordinary film shows the LIGHT, because at 112 people over a 170x120 map any
        /// framing close enough to see a person covers too little ground to contain one. X-ray is
        /// the one view where that stops being true: nobody is hidden, so all 112 are on screen
        /// at once and the morning reads as a hundred people leaving their houses rather than as
        /// a sunrise.
        /// </summary>
        [MenuItem("Noir/Render Film (X-Ray)")]
        public static void RenderXRay()
        {
            _xray = true;
            try { Render(); }
            finally { _xray = false; }
        }

        [MenuItem("Noir/Render Film")]
        public static void Render()
        {
            GameObject root = null, camGo = null, sunGo = null;
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
                if (Directory.Exists(OutputDir)) Directory.Delete(OutputDir, true);
                Directory.CreateDirectory(OutputDir);

                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
                var layout = VillageParser.Parse(ContentLoader.Read("village.txt"));
                var world = WorldBuilder.Build(layout, VillageHost.Seed);

                root = new GameObject("FilmVillage");
                var village = VillageMesh.Build(world, root.transform);
                if (_xray) XRay.Create(world, village).Set(true);

                var fixtures = SunRig.BuildFixtures(world, root.transform);
                var paneBlock = new MaterialPropertyBlock();
                var crowd = Crowd.Assemble(world, root.transform);

                if (pipeline != null) { pipeline.shadowDistance = 320f; pipeline.shadowCascadeCount = 4; }

                sunGo = new GameObject("FilmSun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.7f;

                camGo = new GameObject("FilmCamera");
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

                // Framed on the school and the street below it, at 52 metres.
                //
                // The wide `morning-long` view was tried first and is the wrong shot for this:
                // at 95 metres a person is two pixels, so it produced a very handsome village
                // with no life in it, which is the one thing the film exists to show. The
                // headless density table says where to point a camera at this hour - 08:35 is
                // the peak of the whole day, with the school gate at 80,43 taking twenty-seven
                // children and their parents walking back.
                // The `school-run` framing exactly, because it is the one shot in the set that
                // was chosen by measurement rather than by eye: the density table says 08:35 is
                // the busiest minute of the whole day, the gate at 80,43 is the south face of
                // Ashcombe Primary, and it is watched from further south looking back at it.
                //
                // Two wider framings were tried first and both failed the same way. At 95 m a
                // person is two pixels; at 52 m and 24 degrees you are looking down onto roofs
                // and the streets disappear underneath them. The pitch has to stay well up - at
                // a shallow angle the camera ends up behind a garden wall - but the distance has
                // to come right in, and 34 m is where those two meet.
                // X-ray wants the opposite framing to the lit film. With the buildings gone
                // nobody is hidden, so the shot should be as wide as the map allows and pitched
                // well down onto it - the subject is a hundred and twelve dots moving at once,
                // and any one of them individually is not the point.
                // Centred on the middle of the map rather than on the old parish. When the east
                // quarter was authored the map went 170 -> 210 wide, and a camera still framed
                // on the old village simply left the new half out of shot.
                // Far enough back to hold the WHOLE map, which is the only framing that shows the
                // village growing. 165 m covered about 170 of the 210 metres and quietly cropped
                // the east quarter and the works off the right-hand edge - the two things the
                // shot existed to show. Sized against the map rather than against the old
                // village, so it survives the town being authored out further.
                var target = _xray ? new Vector3(105f, 0f, -60f) : new Vector3(80f, 0f, -46f);
                if (_xray) PlaceCamera(camGo.transform, target, 235f, 44f, 18f);
                else       PlaceCamera(camGo.transform, target, 34f, 34f, 0f);

                int frame = 0;
                for (float hour = FromHour; hour <= ToHour + 1e-4f; hour += StepHours)
                {
                    var (colour, intensity, ambient) = SunRig.SkyAt(hour);

                    sun.color = colour;
                    sun.intensity = intensity;
                    sunGo.transform.rotation = SunRig.SunRotation(hour);

                    RenderSettings.ambientLight = ambient;
                    RenderSettings.fogColor = SunRig.FogAt(colour, intensity, ambient);
                    RenderSettings.fogDensity = 0.0016f;
                    if (sky != null && sky.HasProperty("_SunDisk")) sky.SetFloat("_SunDisk", 2f);

                    Snapshot.LightUp(world, fixtures, paneBlock, hour, intensity);
                    crowd.PoseAt(hour);

                    Capture(cam, Path.Combine(OutputDir, $"f{frame:0000}.png"));
                    frame++;
                    written++;

                    if (frame % 30 == 0)
                        Debug.Log($"[film] {frame} frames, at "
                                + $"{(int)hour:00}:{Mathf.RoundToInt(hour % 1f * 60f):00}");
                }

                Debug.Log($"[film] {written} frames -> {OutputDir}");
            }
            catch (Exception ex)
            {
                Debug.LogError("Film failed: " + ex);
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
                if (sunGo != null) UnityEngine.Object.DestroyImmediate(sunGo);
                if (fx != null && fx.gameObject != null) UnityEngine.Object.DestroyImmediate(fx.gameObject);
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

            if (Application.isBatchMode) EditorApplication.Exit(written > 0 ? 0 : 1);
        }

        private static void PlaceCamera(Transform t, Vector3 target, float dist, float pitch, float yaw)
        {
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            t.position = target - rot * Vector3.forward * dist;
            t.rotation = rot;
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
