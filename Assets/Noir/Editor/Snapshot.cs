using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Sim;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Renders the village to PNG files without play mode, an editor window, or a human.
    ///
    /// This exists to close a feedback loop. Every visual question - is the fog too heavy, are
    /// the shadows working, is the ground too dark - previously cost a round trip: ask, wait,
    /// receive a screenshot, interpret. Now the same question is answered by rendering a frame
    /// and looking at it.
    ///
    /// It lights the scene with the SAME curve the game uses (SunRig.SkyAt, SunRotation, FogAt)
    /// and builds its lamps and windows with the same code (SunRig.BuildFixtures) rather than a
    /// copy of either, because a picture of a village that does not exist is worse than no
    /// picture at all. The one thing it cannot share is occupancy - there is nobody here - so
    /// that is faked from a stable hash and nothing else is.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.Snapshot.Render
    ///
    /// Note: NO -nographics. Rendering needs a graphics device.
    /// </summary>
    public static class Snapshot
    {
        private const int Width = 1600;
        private const int Height = 900;

        private static string OutputDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "snapshots"));

        /// <summary>Camera setups worth looking at, and the hour to light each one.</summary>
        private static readonly (string name, float hour, Vector3 target, float dist, float pitch, float yaw)[] Shots =
        {
            ("noon-overview",  13f,  new Vector3(85f, 0f, -60f), 120f, 46f,  40f),
            ("morning-long",    8f,  new Vector3(85f, 0f, -60f), 120f, 30f,  40f),
            ("dusk",           20f,  new Vector3(85f, 0f, -60f), 120f, 34f,  40f),
            ("night",          23f,  new Vector3(85f, 0f, -60f), 110f, 38f,  40f),
            // Low sun still below the horizon, lamps not yet off and the farms already up.
            // The one hour of the day where every light source in the rig is doing something.
            ("first-light",     5.5f, new Vector3(85f, 0f, -60f), 150f, 26f,  40f),
            ("street-noon",    13f,  new Vector3(60f, 0f, -58f),   0f,  4f,  95f),
            ("street-night",   22f,  new Vector3(60f, 0f, -58f),   0f,  4f,  95f),
            // Down Back Lane from the verge, four metres up: cottages on both sides at an
            // angle rather than head-on, which is the view that shows lit windows AS windows.
            ("back-lane-night", 21f, new Vector3(100f, 0f, -80f),  39f,  6f,  80f),
            ("close-terrace",  13f,  new Vector3(18f, 0f, -66f),  34f, 34f,  20f),

            // Two shots aimed at PEOPLE rather than at buildings, because the rest of this
            // table turned out to be a set of photographs of an empty village. The headless
            // density table settles when to point a camera: at 13:30 exactly one person in
            // a hundred and sixteen is outdoors, so the quiet street at noon was never a
            // rendering fault - it is the middle of a working day.
            //
            // 08:35 is the peak: seventeen people out, the school gate at 80,43 taking
            // twenty-seven children, and parents walking back. 17:30 is the second peak, at
            // the mill door on Mill Lane as the shift comes off.
            // Yaw 0 looks north (+Z), 180 south. Ashcombe Primary is 70,32 18x12 with its door
            // at 80,43, which is the SOUTH face, so the gate is watched from further south
            // looking back at it. The mill is 118,98 24x14 with its door at 130,98 - the NORTH
            // face - so that one is watched from the north looking south.
            // Pitched well up rather than shot from head height: at a shallow angle the camera
            // sits behind whatever garden wall happens to be in the way, and a three-metre wall
            // twenty metres out fills the frame.
            ("school-run",      8.58f, new Vector3(80f, 0f, -46f),  34f, 34f,   0f),
            ("mill-gate",      17.5f,  new Vector3(130f, 0f, -96f), 34f, 34f, 180f),

            // Camera position is ignored for this one - it goes to whatever cluster of people
            // is largest at that minute. See the "the-crowd" branch below.
            ("the-crowd",       8.58f, Vector3.zero, 0f, 0f, 0f),
        };

        [MenuItem("Noir/Render Snapshots")]
        public static void Render()
        {
            GameObject root = null, camGo = null, sunGo = null;
            Volume fx = null;
            Material sky = null;
            int written = 0;

            // Everything this method is about to scribble on, kept so it can be put back.
            //
            // Rendering a set of stills should not change the project. It was overwriting the
            // open scene's sky, fog and ambient and leaving them at whatever the last shot
            // wanted, and writing shadow settings straight onto UniversalRP.asset - so running
            // the renderer once left the Scene view mysteriously foggy for the rest of the
            // session and dirtied an asset that would then get committed by accident, after
            // which the game starts up with shadow settings its own file does not admit to.
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

                // As in SmokeTest and VillageHost: Core cannot read a file, so whichever entry
                // point gets there first has to install the kinds table before a `place` line is
                // parsed. There is no fallback on purpose - a compiled-in copy silently
                // disagreed with Content/kinds.txt the moment anybody edited it.
                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));

                var layout = VillageParser.Parse(ContentLoader.Read("fixture-village.txt"));

                // Deliberately UNSURVEYED. This is the fixture village, not Rossville: the survey
                // passes are keyed to the real town's parcel ids and would silently do nothing
                // here. Do not "fix" this into TownPipeline.Build().
                var built = TownPipeline.BuildUnsurveyed(layout, VillageHost.Seed, "fixture-village.txt");
                var world = built.World;

                root = new GameObject("SnapshotVillage");
                VillageMesh.Build(world, root.transform);

                // The street lamps, the window lights and the panes themselves. Built here
                // because the mesh does not own them: without this the two night shots were a
                // black rectangle, which is the picture in this set that most needs looking at.
                var fixtures = SunRig.BuildFixtures(world, root.transform);
                var paneBlock = new MaterialPropertyBlock();

                var crowd = Crowd.Assemble(world, root.transform);

                // Shadows have to reach across the village, exactly as in the game.
                var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (urp != null) { urp.shadowDistance = 320f; urp.shadowCascadeCount = 4; }

                sunGo = new GameObject("SnapshotSun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.7f;

                camGo = new GameObject("SnapshotCamera");
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 45f;
                cam.nearClipPlane = 0.3f;
                // The ground now runs two kilometres past the map edge in every direction, so
                // the village stands in countryside. At the old 700 the far land was clipped
                // and the horizon came back as a hard circular edge.
                cam.farClipPlane = 3000f;
                cam.clearFlags = CameraClearFlags.Skybox;

                // Tonemapping and bloom. Both halves are needed and neither is visible on its
                // own: with no volume every lit window clipped to a flat white rectangle, and
                // with no EnableOn the volume sits in the scene doing nothing and produces
                // exactly the same picture as having no volume at all.
                fx = PostFx.Create(null);
                PostFx.EnableOn(cam);

                sky = MakeSky();
                RenderSettings.skybox = sky;
                RenderSettings.sun = sun;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.ambientMode = AmbientMode.Flat;

                // Shots in clock order, not table order. The crowd is one simulation run
                // forward across the day, and stepping it backwards would mean restarting the
                // whole village for every frame that happens to sit earlier in the table.
                var byClock = new List<(string name, float hour, Vector3 target,
                                        float dist, float pitch, float yaw)>(Shots);
                byClock.Sort((a, b) => a.hour.CompareTo(b.hour));

                foreach (var shot in byClock)
                {
                    var (colour, intensity, ambient) = SunRig.SkyAt(shot.hour);
                    sun.color = colour;
                    sun.intensity = intensity;
                    sun.enabled = intensity > 0.01f;
                    sunGo.transform.rotation = SunRig.SunRotation(shot.hour);

                    RenderSettings.ambientLight = ambient;
                    RenderSettings.fogColor = SunRig.FogAt(colour, intensity, ambient);
                    RenderSettings.fogDensity = Mathf.Lerp(0.0022f, 0.0010f, Mathf.Clamp01(intensity));
                    if (sky != null && sky.HasProperty("_Exposure"))
                        sky.SetFloat("_Exposure", Mathf.Lerp(0.18f, 1.15f, Mathf.Clamp01(intensity)));

                    LightUp(world, fixtures, paneBlock, shot.hour, intensity);
                    crowd.PoseAt(shot.hour);

                    var target = shot.target;
                    float dist = shot.dist, pitch = shot.pitch, yaw = shot.yaw;

                    // One shot in the set goes wherever the people are rather than where I
                    // guessed they would be. Every fixed camera I placed by hand came back
                    // showing an empty village, which says nothing about the figures.
                    if (shot.name == "the-crowd")
                    {
                        if (!crowd.TryFocus(out target, out int howMany)) continue;
                        Debug.Log($"[snap] the-crowd found {howMany} together at "
                                + $"{target.x:0}, {-target.z:0}.");
                        target.y = 0f;
                        dist = 22f; pitch = 24f; yaw = 35f;
                    }

                    var rotation = Quaternion.Euler(pitch, yaw, 0f);
                    camGo.transform.position = dist <= 0.01f
                        ? target + Vector3.up * 1.7f
                        : target - rotation * Vector3.forward * dist;
                    camGo.transform.rotation = rotation;

                    // Only now. The pool lends its real lights to whatever is nearest the
                    // camera, so it cannot be dealt until the camera is where the shot wants it
                    // - and then settled outright, because a still has no frames to ease over.
                    //
                    // From scratch every time, rather than carrying the last shot's hand: these
                    // pictures are a regression test, and a picture that depends on which other
                    // pictures were taken first is no use as one. See LightPool.Reset.
                    fixtures.Lights.Reset();
                    fixtures.Lights.Reassign(camGo.transform.position);
                    fixtures.Lights.Settle();

                    string path = Path.Combine(OutputDir, shot.name + ".png");
                    Capture(cam, path);
                    written++;

                    // Printed to the minute rather than the hour, because the half-past shots
                    // otherwise logged themselves as the wrong hour entirely.
                    string clock = $"{(int)shot.hour:00}:{Mathf.RoundToInt(shot.hour % 1f * 60f):00}";
                    Debug.Log($"[snap] {shot.name}  {clock}  sun {intensity:0.00}  -> {path}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[snap] FAILED: " + ex);
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
                if (sunGo != null) UnityEngine.Object.DestroyImmediate(sunGo);
                if (fx != null)
                {
                    // The profile is a ScriptableObject rather than a component, so destroying
                    // only the GameObject would leave it in the editor for the rest of the
                    // session. Profile first: it cannot be reached once the volume is gone.
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

            Debug.Log($"[snap] wrote {written} of {Shots.Length}");
            if (Application.isBatchMode) EditorApplication.Exit(written == Shots.Length ? 0 : 1);
        }

        /// <summary>
        /// Turn the village's lights on for one shot's hour, at the brightness the game would
        /// use at that hour.
        ///
        /// The panes and lanterns are set outright here - the live rig eases them and a still
        /// has no frames to ease over. The lamps and window lights only say what they WANT;
        /// which of them gets one of the pool's real lights depends on where the camera ends up,
        /// so that is settled back in Render once the camera has been placed.
        /// </summary>
        internal static void LightUp(WorldModel world, SunRig.Fixtures fixtures,
                                    MaterialPropertyBlock paneBlock, float hour, float sunIntensity)
        {
            float level = SunRig.NightLevel(sunIntensity);
            var lit = LitPlaces(world, hour);
            var lights = fixtures.Lights;

            // Lamps are on a switch on a wall somewhere, not on who is at home: all of them.
            for (int i = 0; i < fixtures.Lamps.Count; i++)
                lights.SetIntensity(fixtures.Lamps[i], level * SunRig.LampIntensity);
            foreach (var lantern in fixtures.Lanterns)
                SunRig.SetLanternGlow(lantern, paneBlock, level);

            for (int i = 0; i < fixtures.Windows.Count; i++)
                lights.SetIntensity(fixtures.Windows[i],
                    lit.Contains(fixtures.WindowPlaces[i].Value) ? level * SunRig.WindowIntensity : 0f);

            for (int i = 0; i < fixtures.Panes.Count; i++)
                SunRig.SetPaneGlow(fixtures.Panes[i], paneBlock,
                                   lit.Contains(fixtures.PanePlaces[i].Value) ? level : 0f);
        }

        /// <summary>
        /// Who has a light on, with no simulation to ask.
        ///
        /// The rig lights a building when somebody is inside it and awake. There is nobody here
        /// - no agents, no clock, nothing has ever walked anywhere - so a dwelling is lit on a
        /// stable hash of where it stands, which puts roughly six houses in ten on and keeps
        /// the SAME six on for ever. That is the whole point: two night shots taken a week
        /// apart have to differ only where the rendering differs, and a fresh random sixty
        /// percent each run would make every comparison between them worthless.
        ///
        /// Everything that is not a dwelling goes on its authored opening hours instead, which
        /// is free and happens to be true: the tavern is lit at nine and dark at eleven,
        /// the mill's late shift is still on at nine, and the farms are up before anyone.
        /// </summary>
        private static HashSet<int> LitPlaces(WorldModel world, float hour)
        {
            const int Wednesday = 2;   // an ordinary weekday; none of these shots is about a weekend
            int minute = Mathf.RoundToInt(hour * 60f) % 1440;

            var lit = new HashSet<int>();
            foreach (var place in world.AllPlaces)
            {
                var at = place.Bounds.Centre;
                bool on = place.Kind == PlaceKind.Dwelling
                    ? Materials3D.Scatter(at.X, at.Y, 4211) % 100 < 60
                    : !place.AlwaysOpen && place.IsOpenAt(minute, Wednesday);
                if (on) lit.Add(place.Id.Value);

                // Shutters, on the same clock. A shuttered shop at eleven at night is one of
                // the few things in these stills that says the village runs on something, and
                // the whole system had never been switched on by anybody.
                if (Frontage.HasShutter(place.Id))
                    Frontage.SetOpen(place.Id, place.AlwaysOpen || place.IsOpenAt(minute, Wednesday));
            }
            return lit;
        }

        internal static Material MakeSky()
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null) return null;
            var m = new Material(shader) { name = "SnapshotSky" };
            if (m.HasProperty("_SunSize")) m.SetFloat("_SunSize", 0.04f);
            if (m.HasProperty("_AtmosphereThickness")) m.SetFloat("_AtmosphereThickness", 1.2f);
            return m;
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
