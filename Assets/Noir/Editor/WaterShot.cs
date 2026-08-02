using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// The real water, photographed on the ground rather than on the plan.
    ///
    /// WHY THIS IS NOT JUST ANOTHER CityShot CAMERA. The committed snapshot set is the dark
    /// SURVEY PLAN - VillageHost.ShowBuildings is false, and Materials3D.Plan dims the ground to
    /// near-black behind it. Worse, Materials3D.ShowGroundColour is `!Application.isBatchMode &amp;&amp;
    /// ...`, so it is hard-false in every headless run there is. A batch-mode plan render
    /// therefore CANNOT show what colour the ground is, which makes it exactly the wrong
    /// instrument for checking that a new terrain kind reads correctly - it would verify through
    /// the one path that bypasses the thing being verified.
    ///
    /// So this forces ShowBuildings on for the duration, which takes Plan() out of the way, and
    /// puts it back afterwards. It writes its own files and does not touch the plan set.
    ///
    /// Ground and greenery only. The buildings, traffic and street furniture are all a long way
    /// east of the North Fork and would add several minutes to a render that is about a river.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.WaterShot.Run -logFile &lt;log&gt;
    /// </summary>
    public static class WaterShot
    {
        [MenuItem("Noir/Render The Water")]
        public static void Run()
        {
            GameObject root = null, camGo = null, sunGo = null;
            Material sky = null;

            bool wasShowBuildings = VillageHost.ShowBuildings;
            var wasSky = RenderSettings.skybox;
            var wasSun = RenderSettings.sun;
            bool wasFog = RenderSettings.fog;
            var wasFogColour = RenderSettings.fogColor;
            float wasFogDensity = RenderSettings.fogDensity;
            var wasAmbientMode = RenderSettings.ambientMode;
            var wasAmbient = RenderSettings.ambientLight;

            try
            {
                Directory.CreateDirectory(CityShot.OutputDir);

                // Before anything is built: VillageMesh reads it while it is making materials.
                VillageHost.ShowBuildings = true;

                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
                var layout = VillageParser.Parse(ContentLoader.Read(VillageHost.MapFile));
                var world = WorldBuilder.Build(layout, VillageHost.Seed);

                int wet = 0;
                for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                    if (world.Grid.TerrainAt(x, y) == Noir.Core.World.Terrain.Water) wet++;
                Debug.Log($"[watershot] {world.Width}x{world.Height}, {wet:N0} water tiles "
                        + $"({100f * wet / (world.Width * world.Height):F3}% of the map).");

                root = new GameObject("WaterShot");
                VillageMesh.Build(world, root.transform, true);
                CityGreenery.Build(world, root.transform);
                CityChunker.Bake(root);

                sunGo = new GameObject("WaterSun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.7f;

                camGo = new GameObject("WaterCamera");
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 45f;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 3000f;
                cam.clearFlags = CameraClearFlags.Skybox;

                sky = Snapshot.MakeSky();
                RenderSettings.skybox = sky;
                RenderSettings.sun = sun;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.ambientMode = AmbientMode.Flat;

                // One o'clock, not the six the game opens at: this is a shot to READ the ground
                // by, and at 06:00 SunRig's fog is at its thickest and everything is a smudge.
                const float hour = 13f;
                var (colour, intensity, ambient) = SunRig.SkyAt(hour);
                sun.color = colour;
                sun.intensity = intensity;
                sunGo.transform.rotation = SunRig.SunRotation(hour);
                RenderSettings.ambientLight = ambient;
                RenderSettings.fogColor = SunRig.FogAt(colour, intensity, ambient);
                RenderSettings.fogDensity = Mathf.Lerp(2.1f, 0.95f, Mathf.Clamp01(intensity))
                                          / Mathf.Max(600f, Mathf.Max(world.Width, world.Height));
                if (sky != null && sky.HasProperty("_Exposure"))
                    sky.SetFloat("_Exposure", Mathf.Lerp(0.18f, 1.15f, Mathf.Clamp01(intensity)));

                // World space is x east, -z south (Space3D), so a map point (mx, my) is
                // (mx, 0, -my). These are map coordinates straight off features.txt.
                Vector3 At(float mx, float my) => new Vector3(mx, 0f, -my);

                // THE NORTH FORK, from the air, mid-course. The whole point of the shot is
                // whether it reads as a river running through farmland rather than a blue
                // rectangle - so it is framed along the water, not across it.
                CityShot.Frame(camGo, At(150f, 1500f), 420f, 38f, 15f);
                CityShot.Capture(cam, Path.Combine(CityShot.OutputDir, "water-river.png"));

                // WHERE ATTICA CROSSES IT. The one place the river meets the town's own grid,
                // and the case that decides whether a road over water reads as a crossing or as
                // a hole in the carriageway. Attica runs the full width at y=1335; the river
                // crosses it once, for 25m.
                CityShot.Frame(camGo, At(96f, 1335f), 70f, 12f, 90f);
                CityShot.Capture(cam, Path.Combine(CityShot.OutputDir, "water-crossing.png"));

                // THE PONDS BEHIND THE SCHOOL. Two closed shapes rather than a corridor, and the
                // ones somebody who grew up here would place instantly.
                CityShot.Frame(camGo, At(300f, 1120f), 260f, 40f, 200f);
                CityShot.Capture(cam, Path.Combine(CityShot.OutputDir, "water-ponds.png"));

                // THE BANK, at eye height on the edge of the southern pond. Water sits 0.35m
                // below its neighbours and the ground mesh closes that step with a riser; this
                // is the only view at which a missing riser shows, as a band of sky lying along
                // the far bank.
                CityShot.Frame(camGo, At(360f, 1530f) + Vector3.up * 1.6f, 55f, 3f, 200f);
                CityShot.Capture(cam, Path.Combine(CityShot.OutputDir, "water-bank.png"));
            }
            finally
            {
                VillageHost.ShowBuildings = wasShowBuildings;
                RenderSettings.skybox = wasSky;
                RenderSettings.sun = wasSun;
                RenderSettings.fog = wasFog;
                RenderSettings.fogColor = wasFogColour;
                RenderSettings.fogDensity = wasFogDensity;
                RenderSettings.ambientMode = wasAmbientMode;
                RenderSettings.ambientLight = wasAmbient;

                if (camGo != null) Object.DestroyImmediate(camGo);
                if (sunGo != null) Object.DestroyImmediate(sunGo);
                if (root != null) Object.DestroyImmediate(root);
                if (sky != null) Object.DestroyImmediate(sky);
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
