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

        /// <summary>
        /// Straight down, orthographic, the whole map in one frame - no perspective to make an
        /// aligned grid look skewed or a skewed one look aligned. This is the shot to audit the
        /// plan by; the angled cameras below are for atmosphere, not for measuring against.
        /// </summary>
        [MenuItem("Noir/Render Plan (top-down, orthographic)")]
        public static void RenderPlanTopDown()
        {
            RenderPlanTopDownCore();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>The render itself, without leaving afterwards, so it can be one step of a
        /// longer batch pass rather than the last thing that ever happens. See Preflight.</summary>
        public static void RenderPlanTopDownCore()
        {
            GameObject root = null, camGo = null;
            try
            {
                Directory.CreateDirectory(OutputDir);
                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
                var layout = VillageParser.Parse(ContentLoader.Read("city.txt"));
                var world = WorldBuilder.Build(layout, VillageHost.Seed);

                root = new GameObject("CityGround");
                VillageMesh.Build(world, root.transform, showDressing: false);
                CityOutlines.Build(world, root.transform,
                                   VillageHost.ShowPlanRoads, VillageHost.ShowPlanFootprints);

                camGo = new GameObject("PlanCam");
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = Mathf.Max(world.Width, world.Height) / 2f + 20f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.nearClipPlane = 1f;
                cam.farClipPlane = 5000f;
                camGo.transform.position = new Vector3(world.Width / 2f, 500f, -world.Height / 2f);
                camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

                var rt = new RenderTexture(2048, 2048, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var shot = new Texture2D(2048, 2048, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, 2048, 2048), 0, 0);
                shot.Apply();
                File.WriteAllBytes(Path.Combine(OutputDir, "plan-top-down.png"), shot.EncodeToPNG());
                cam.targetTexture = null;
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(shot);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                Debug.Log("[cityshot] wrote plan-top-down.png");
            }
            catch (Exception ex) { Debug.LogError("[cityshot] plan top-down FAILED: " + ex); }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
            }
        }

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
                // BUILDINGS are bought models. That is the point of the slice. Dressing off in
                // plan mode for the same reason CityOutlines drops roads and footprints: trees,
                // hedges and street furniture are scenery ON TOP of the thing a plan is meant to
                // let you read.
                root = new GameObject("CityGround");
                VillageMesh.Build(world, root.transform, VillageHost.ShowBuildings);

                city = new GameObject("CityAll");
                if (VillageHost.ShowBuildings)
                {
                    CityStreets.Build(world, city.transform);
                    CityParking.Build(world, city.transform);
                    CitySigns.Build(world, city.transform);
                }
                // The still follows the game: plan by default, models only if asked.
                if (VillageHost.ShowBuildings)
                {
                    CityBuildings.Build(world, city.transform);
                    CityDistrict.Build(world, city.transform);
                    CitySuburb.Build(world, city.transform);
                }

                if (VillageHost.ShowBuildings)
                {
                    CityStory.Build(world, city.transform);
                    CityRail.Build(world, city.transform);
                    CityFarm.Build(world, city.transform);
                    CityPowerlines.Build(world, city.transform);
                    CityGreenery.Build(world, city.transform);
                }

                // The lighting rig's own fixtures, exactly as Snapshot does it. Without these
                // the still has no window panes, no lamps and no lit glass - which is to say it
                // cannot show whether the night lighting works at all. Skipped on a plan: there
                // are no walls for a window to sit in without ShowBuildings, and a lamp post is
                // exactly the standing 3D clutter the plan is meant to have none of.
                var fixtures = VillageHost.ShowBuildings ? SunRig.BuildFixtures(world, city.transform) : null;
                var paneBlock = new MaterialPropertyBlock();
                CityChunker.Bake(city);

                // After the bake, for the same reason the signals are: the chunker destroys what
                // it combines, and the plan has to survive it.
                if (!VillageHost.ShowBuildings)
                    CityOutlines.Build(world, root.transform,
                                       VillageHost.ShowPlanRoads, VillageHost.ShowPlanFootprints);

                // Outside the baked node, as in the game: these move and change colour, and a
                // combined mesh can do neither. Without them the junctions photograph unlit and
                // the streets photograph empty - which is exactly the pair of things a still is
                // worth taking to check.
                var signals = CitySignals.Create(world, root.transform);
                if (VillageHost.ShowBuildings) CityTraffic.Create(world, root.transform, signals);

                // A signal head standing over an otherwise empty road corridor is exactly the
                // kind of thing a plan is supposed to have removed - see VillageHost.HideActors,
                // which does the same thing for the game itself. CityShot builds its own scene
                // and never runs that method, so the still needs its own copy of the hide -
                // renderers for the mesh, and the "Lamp_Emission" point lights too, or the red
                // and green pools of colour keep glowing on the tarmac with nothing above them.
                if (!VillageHost.ShowBuildings)
                {
                    foreach (var r in signals.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                    foreach (var l in signals.GetComponentsInChildren<Light>(true)) l.enabled = false;
                }

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
                // Scaled to the map, exactly as SunRig does it in game - a still taken at a
                // fixed density on a 2,400m map photographs a wall of haze.
                RenderSettings.fogDensity = Mathf.Lerp(2.1f, 0.95f, Mathf.Clamp01(intensity))
                                          / Mathf.Max(600f, Mathf.Max(world.Width, world.Height));
                sun.enabled = intensity > 0.01f;
                if (sky != null && sky.HasProperty("_Exposure"))
                    sky.SetFloat("_Exposure", Mathf.Lerp(0.18f, 1.15f, Mathf.Clamp01(intensity)));

                if (fixtures != null)
                {
                    Snapshot.LightUp(world, fixtures, paneBlock, hour, intensity);
                    fixtures.Lights.Reset();
                }

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

                // ---- ROSSVILLE. Every camera is placed off the real street grid ----
                //
                // These used to be hardcoded points that had to be walked forward by hand every
                // time the map moved - +120 for the 1290 re-lay, and wrong again the moment the
                // town was re-laid on Rossville's own grid. They are now derived from the one
                // fact the map is built about: Chicago Street crosses Attica Street at (750,
                // 1335), and every street is a known offset from it. Move the town and the
                // cameras follow.
                const float cx = 750f, cy = 1335f;
                Vector3 At(float east, float north) => new Vector3(cx + east, 0f, -(cy - north));

                // THE CROSSROADS. Chicago and Attica, the only signalised junction in the
                // village, from the middle of the road looking north up Route 1.
                Frame(camGo, At(0f, 0f) + Vector3.up * 1.6f, 40f, 4f, 0f);
                Capture(cam, Path.Combine(OutputDir, "city-street.png"));

                // THE WHOLE VILLAGE from above, to read the grid: nine streets by fourteen,
                // mostly east of Route 1, with the country round it.
                Frame(camGo, At(280f, -130f), 1150f, 42f, 20f);
                Capture(cam, Path.Combine(OutputDir, "city-block.png"));

                // THE STOREFRONTS - the 100 block of North Chicago, from the pavement.
                Frame(camGo, At(0f, 60f) + Vector3.up * 1.5f, 34f, 5f, 180f);
                Capture(cam, Path.Combine(OutputDir, "city-terrace.png"));

                // 408 HOLMES AVE. The killer's house, from the middle of Holmes Avenue looking
                // at its front door. If this view is wrong nothing else matters.
                //
                // Moved with the address: the 400 block of Holmes ran almost onto the real CSX
                // line once the town was checked against it (see relay-rossville.py), so the
                // house itself moved to the block's one safe lot. Same relative framing - door
                // plus 6m east, 7m north - just off the corrected door position.
                Frame(camGo, new Vector3(1175f, 1.6f, -1218f), 26f, 4f, 0f);
                Capture(cam, Path.Combine(OutputDir, "city-corner.png"));

                // Straight down on the one signal, to read lanes, stop lines and heads.
                Frame(camGo, At(0f, 0f), 70f, 55f, 90f);
                Capture(cam, Path.Combine(OutputDir, "city-junction.png"));

                // From the driver's seat at the northbound stop line on Route 1.
                Frame(camGo, At(0f, -30f) + Vector3.up * 1.2f, 30f, -1f, 0f);
                Capture(cam, Path.Combine(OutputDir, "city-stopline.png"));

                // A RESIDENTIAL STREET - the 400 block of Holmes Ave from the kerb. The view
                // that says whether this reads as somewhere people live.
                Frame(camGo, new Vector3(1200f, 1.6f, -1209f), 55f, 5f, 90f);
                Capture(cam, Path.Combine(OutputDir, "suburb-street.png"));

                // The houses from above, to see the lots, the gaps and the setbacks.
                Frame(camGo, At(500f, 126f), 240f, 50f, 30f);
                Capture(cam, Path.Combine(OutputDir, "suburb-block.png"));

                // Where the village stops and the corn starts.
                Frame(camGo, At(780f, -400f), 190f, 22f, 250f);
                Capture(cam, Path.Combine(OutputDir, "suburb-edge.png"));

                // The water tower, which is the skyline now.
                Frame(camGo, At(120f, 353f), 120f, 12f, 200f);
                Capture(cam, Path.Combine(OutputDir, "country-junction.png"));

                // The grain elevator, sited near the real CSX line (no simulated road for the
                // tracks any more - see VillageHost.ShowPlanRoads and relay-rossville.py).
                Frame(camGo, At(860f, -100f), 130f, 14f, 250f);
                Capture(cam, Path.Combine(OutputDir, "country-stop.png"));

                // Out on the section road, looking back at the town.
                Frame(camGo, At(-500f, -100f), 220f, 10f, 90f);
                Capture(cam, Path.Combine(OutputDir, "country-poles.png"));

                // A back yard on a residential block.
                Frame(camGo, At(300f, 60f), 90f, 40f, 20f);
                Capture(cam, Path.Combine(OutputDir, "block-yard.png"));

                // The precinct car park, which is the biggest of the five and the one with the
                // cruisers in it.
                Frame(camGo, new Vector3(587f, 0f, -700f), 75f, 32f, 45f);
                Capture(cam, Path.Combine(OutputDir, "city-carpark.png"));

                // Down among the bays, which is the only distance at which two cars sharing a
                // bay can be told from two cars merely close together.
                Frame(camGo, new Vector3(585f, 0f, -698f), 26f, 22f, 40f);
                Capture(cam, Path.Combine(OutputDir, "city-carpark-close.png"));

                // Where the farm track meets the ring road at the corner of the map. THREE arms,
                // not four: this junction used to be laid as a full crossroads with a fourth arm,
                // kerbs and a stop line painted straight into the paddock.
                Frame(camGo, new Vector3(270f, 0f, -165f), 80f, 50f, 270f);
                Capture(cam, Path.Combine(OutputDir, "farm-track.png"));

                // Home Farm in the north-west corner, looking west across the yard.
                Frame(camGo, new Vector3(190f, 0f, -145f), 70f, 22f, 270f);
                Capture(cam, Path.Combine(OutputDir, "farm-yard.png"));

                // The whole map: town in the middle, suburbs round it, 270 metres of country on
                // every side. This is the shot the map-size decision was taken for.
                Frame(camGo, new Vector3(645f, 0f, -645f), 950f, 42f, 30f);
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
