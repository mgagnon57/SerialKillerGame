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
    /// Loads the city block from Content/city.txt and photographs it under the same sun Ashcombe
    /// uses, so the two can be compared without a variable between them but the buildings.
    ///
    /// It also prints the renderer count, which is the number the whole approach turns on: the
    /// village manages 1,835 renderers for everything, and a city of bought models has to land
    /// somewhere near that after chunking or it does not ship.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.CityShot.Render -logFile &lt;log&gt;
    /// </summary>
    public static class CityShot
    {
        private const int Width = 1600, Height = 900;

        /// <summary>Set by Render06 / Render13; defaults to the hour the game opens at.</summary>
        private static float Hour = 6f;

        [MenuItem("Noir/Render City Block (as the game opens, 06:00)")]
        public static void RenderDawn() { Hour = 6f; Render(); }

        [MenuItem("Noir/Render City Block (noon)")]
        public static void RenderNoon() { Hour = 13f; Render(); }

        [MenuItem("Noir/Render City Block (night, 22:00)")]
        public static void RenderNight() { Hour = 22f; Render(); }

        private static string OutputDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "snapshots"));

        [MenuItem("Noir/Render City Block")]
        public static void Render()
        {
            GameObject root = null, city = null, camGo = null, sunGo = null;
            Volume fx = null;
            Material sky = null;

            var wasSky = RenderSettings.skybox;
            var wasSun = RenderSettings.sun;
            bool wasFog = RenderSettings.fog;
            var wasFogColour = RenderSettings.fogColor;
            float wasFogDensity = RenderSettings.fogDensity;
            var wasAmbientMode = RenderSettings.ambientMode;
            var wasAmbient = RenderSettings.ambientLight;

            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float wasShadowDistance = pipeline != null ? pipeline.shadowDistance : 0f;

            try
            {
                Directory.CreateDirectory(OutputDir);

                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
                var layout = VillageParser.Parse(ContentLoader.Read("city.txt"));
                var world = WorldBuilder.Build(layout, VillageHost.Seed);
                Debug.Log($"[cityshot] loaded {world.Width}x{world.Height}, {world.PlaceCount} places.");

                var report = WorldValidator.Validate(world);
                foreach (var problem in report.Errors) Debug.LogError("city.txt: " + problem);
                foreach (var warning in report.Warnings) Debug.LogWarning("city.txt: " + warning);

                // The ground, roads and props still come from the old renderer - only the
                // BUILDINGS are bought models. That is the point of the slice.
                root = new GameObject("CityGround");
                VillageMesh.Build(world, root.transform);

                city = new GameObject("CityAll");
                CityStreets.Build(world, city.transform);
                CityParking.Build(world, city.transform);
                CitySigns.Build(world, city.transform);
                CityBuildings.Build(world, city.transform);
                CityDistrict.Build(world, city.transform);
                CityRail.Build(world, city.transform);
                CityFarm.Build(world, city.transform);
                CityGreenery.Build(world, city.transform);

                // The lighting rig's own fixtures, exactly as Snapshot does it. Without these
                // the still has no window panes, no lamps and no lit glass - which is to say it
                // cannot show whether the night lighting works at all.
                var fixtures = SunRig.BuildFixtures(world, city.transform);
                var paneBlock = new MaterialPropertyBlock();
                CityChunker.Bake(city);

                // Outside the baked node, as in the game: these move and change colour, and a
                // combined mesh can do neither. Without them the junctions photograph unlit and
                // the streets photograph empty - which is exactly the pair of things a still is
                // worth taking to check.
                var signals = CitySignals.Create(world, root.transform);
                CityTraffic.Create(world, root.transform, signals);

                if (pipeline != null) pipeline.shadowDistance = 320f;

                sunGo = new GameObject("CitySun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.7f;

                camGo = new GameObject("CityCamera");
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

                // The hour the GAME starts at, not a flattering one. Rendering these at noon was
                // honest about the geometry and quietly dishonest about the experience: at 06:00
                // the sun is barely up, SunRig's fog is at its thickest, and the city that
                // photographed well at one o'clock is a brown smudge.
                float hour = Hour;
                var (colour, intensity, ambient) = SunRig.SkyAt(hour);
                sun.color = colour;
                sun.intensity = intensity;
                sunGo.transform.rotation = SunRig.SunRotation(hour);
                RenderSettings.ambientLight = ambient;
                RenderSettings.fogColor = SunRig.FogAt(colour, intensity, ambient);
                RenderSettings.fogDensity = Mathf.Lerp(0.0022f, 0.0010f, Mathf.Clamp01(intensity));
                sun.enabled = intensity > 0.01f;
                if (sky != null && sky.HasProperty("_Exposure"))
                    sky.SetFloat("_Exposure", Mathf.Lerp(0.18f, 1.15f, Mathf.Clamp01(intensity)));

                Snapshot.LightUp(world, fixtures, paneBlock, hour, intensity);
                fixtures.Lights.Reset();

                // THE TOWN MOVED AND THESE DID NOT. Every camera below used to be aimed at the
                // city when it sat in the map's north-west corner, so after Northgate was
                // re-centred on 360..720 the whole set quietly went on photographing empty
                // fields - a street view of a wheatfield, a "junction" with no junction in it.
                // The stills said nothing was wrong because there was nothing in them.
                //
                // The town is now the middle: paved 360..600, with First Street at x=435,
                // Second at x=525, Northgate Avenue at y=435 and Franklin at y=525.
                //
                // EVERY CAMERA STANDS IN A STREET, a car park or open ground. Frame() puts the
                // camera a long way BACK from its target, which is how an earlier set ended up
                // inside a townhouse and inside the Meridian, photographing wallpaper.

                // Standing in Northgate Avenue looking east: the view the game is played from,
                // and the only one that says whether this is a street.
                Frame(camGo, new Vector3(470f, 1.6f, -435f), 36f, 3f, 90f);
                Capture(cam, Path.Combine(OutputDir, "city-street.png"));

                // The whole town, to see the grid: nine blocks, four signalised junctions.
                Frame(camGo, new Vector3(480f, 0f, -480f), 300f, 38f, 30f);
                Capture(cam, Path.Combine(OutputDir, "city-block.png"));

                // The terrace on the north side of Northgate, from the middle of the avenue.
                Frame(camGo, new Vector3(480f, 1.5f, -420f), 30f, 8f, 0f);
                Capture(cam, Path.Combine(OutputDir, "city-terrace.png"));

                // Northgate Avenue meeting Second Street, looking east along the avenue: the
                // only place two four-lane arterials cross.
                Frame(camGo, new Vector3(525f, 0f, -435f), 70f, 10f, 90f);
                Capture(cam, Path.Combine(OutputDir, "city-corner.png"));

                // Straight down on a signalised junction, to read the lanes, the stop lines and
                // the signals rather than admire the skyline.
                Frame(camGo, new Vector3(435f, 0f, -435f), 55f, 45f, 90f);
                Capture(cam, Path.Combine(OutputDir, "city-junction.png"));

                // FROM THE DRIVER'S SEAT, held at the northbound stop line on First Street at
                // Northgate Avenue. This is the only view that says whether a signal is facing
                // the traffic it governs, and it is the view the complaint came from.
                Frame(camGo, new Vector3(441f, 0f, -435f), 34f, -2f, 0f);
                Capture(cam, Path.Combine(OutputDir, "city-stopline.png"));

                // A COUNTRY crossroads - westway meeting northway, and the pair that decides
                // whether taking the lights off the farmland worked. There should be stop signs
                // on the north-south arms, no signal heads, and no zebra painted on any of it.
                Frame(camGo, new Vector3(255f, 0f, -255f), 65f, 35f, 90f);
                Capture(cam, Path.Combine(OutputDir, "country-junction.png"));

                // One stop sign, close, from the north. This is the check that a sign is a sign
                // and not a plate half-buried in the verge, and that it faces the driver it is
                // for rather than away from them.
                Frame(camGo, new Vector3(241f, 1.4f, -238f), 13f, 6f, 180f);
                Capture(cam, Path.Combine(OutputDir, "country-stop.png"));

                // The precinct car park, which is the biggest of the five and the one with the
                // cruisers in it.
                Frame(camGo, new Vector3(467f, 0f, -580f), 75f, 32f, 45f);
                Capture(cam, Path.Combine(OutputDir, "city-carpark.png"));

                // Where the farm track meets First Street. THREE arms, not four: this junction
                // used to be laid as a full crossroads with a fourth arm, kerbs and a stop line
                // painted straight into the paddock.
                Frame(camGo, new Vector3(435f, 0f, -660f), 75f, 50f, 270f);
                Capture(cam, Path.Combine(OutputDir, "farm-track.png"));

                // Home Farm, looking west across the yard from the near paddock.
                Frame(camGo, new Vector3(390f, 0f, -638f), 70f, 22f, 270f);
                Capture(cam, Path.Combine(OutputDir, "farm-yard.png"));

                // The whole map: town in the middle, the ring roads out of it, country beyond.
                Frame(camGo, new Vector3(480f, 0f, -480f), 700f, 42f, 30f);
                Capture(cam, Path.Combine(OutputDir, "farm-country.png"));
            }
            catch (Exception ex)
            {
                Debug.LogError("[cityshot] FAILED: " + ex);
            }
            finally
            {
                if (city != null) UnityEngine.Object.DestroyImmediate(city);
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
                if (pipeline != null) pipeline.shadowDistance = wasShadowDistance;
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Frame(GameObject camGo, Vector3 target, float dist, float pitch, float yaw)
        {
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
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
            Debug.Log("[cityshot] wrote " + path);
        }
    }
}
