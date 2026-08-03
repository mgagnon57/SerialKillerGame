using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// The same town, photographed with different layers switched off.
    ///
    /// TWO JOBS, and the second is the reason this exists rather than another CityShot camera.
    /// It proves the layer switches actually work - a still where the trees are gone and the
    /// roads are not is evidence in a way that "SetActive returned without throwing" is not.
    /// And it produces the view the ground work is judged on: elevation, terrain textures, road
    /// surfaces and the railroad, with nothing planted or built standing in front of them.
    ///
    /// It drives the REAL builders through the real registry, so if the layer wiring is wrong
    /// this render is wrong too - which is the point. A test that mocked the registry would pass
    /// while the town stayed covered in trees.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.LayerShot.All
    /// </summary>
    public static class LayerShot
    {
        private readonly struct Shot
        {
            public readonly string Name;
            public readonly float X, Y, Dist, Pitch, Yaw, Eye;

            public Shot(string name, float x, float y, float dist, float pitch, float yaw, float eye = 0f)
            {
                Name = name; X = x; Y = y; Dist = dist; Pitch = pitch; Yaw = yaw; Eye = eye;
            }
        }

        /// <summary>
        /// Three views of the same ground, because one is never enough to judge terrain: the
        /// whole town from the air says whether the land reads, a low oblique says whether the
        /// textures hold up at the distance you actually look at them, and street level says
        /// whether they survive being stood on.
        /// </summary>
        private static readonly Shot[] Views =
        {
            new Shot("layers-town", 750f, 1335f, 1150f, 42f, 20f),
            new Shot("layers-country", 520f, 1750f, 420f, 18f, 35f, 2f),
            new Shot("layers-street", 750f, 1335f, 40f, 4f, 0f, 1.6f),
        };

        [MenuItem("Noir/Render The Layers (all, ground-only, no trees)")]
        public static void All()
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
                VillageHost.ShowBuildings = true;

                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
                var layout = VillageParser.Parse(ContentLoader.Read(VillageHost.MapFile));
                var world = WorldBuilder.Build(layout, VillageHost.Seed);

                root = new GameObject("LayerShot");
                VillageMesh.Build(world, root.transform, true);

                var city = new GameObject("City");
                city.transform.SetParent(root.transform, false);

                // THE REAL REGISTRY, in the order VillageHost builds them.
                Layers.Clear();
                Layers.Register(Layers.Kind.Streets, CityStreets.Build(world, city.transform));
                Layers.Register(Layers.Kind.Parking, CityParking.Build(world, city.transform));
                Layers.Register(Layers.Kind.Signs, CitySigns.Build(world, city.transform));
                Layers.Register(Layers.Kind.Buildings, CityBuildings.Build(world, city.transform));
                Layers.Register(Layers.Kind.Districts, CityDistrict.Build(world, city.transform));
                Layers.Register(Layers.Kind.Houses, CitySuburb.Build(world, city.transform));
                Layers.Register(Layers.Kind.Story, CityStory.Build(world, city.transform));
                Layers.Register(Layers.Kind.Rail, CityRail.Build(world, city.transform));
                Layers.Register(Layers.Kind.RailBed, CityRailBed.Build(world, city.transform));
                Layers.Register(Layers.Kind.Farm, CityFarm.Build(world, city.transform));
                Layers.Register(Layers.Kind.Powerlines, CityPowerlines.Build(world, city.transform));
                Layers.Register(Layers.Kind.Trees, CityGreenery.Build(world, city.transform));

                // Per layer, exactly as VillageHost does it - the whole reason a layer survives
                // the bake with a root left to switch. NOTHING bakes `city` itself: it is the
                // parent of every layer root, so baking it destroys them again. See VillageHost.
                foreach (var kind in Layers.All)
                    foreach (var node in Layers.RootsOf(kind))
                        CityChunker.Bake(node);

                sunGo = new GameObject("LayerShotSun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.7f;

                camGo = new GameObject("LayerShotCamera");
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

                Pass(cam, "all", () => Layers.SetAll(true));

                Pass(cam, "ground", Layers.GroundAndRoadsOnly);

                Pass(cam, "notrees", () =>
                {
                    Layers.SetAll(true);
                    Layers.Set(Layers.Kind.Trees, false);
                    Layers.Set(Layers.Kind.Houses, false);
                    Layers.Set(Layers.Kind.Districts, false);
                });

                // Left as it was found, so a person opening the editor next is not handed a
                // town with three layers missing and no idea why.
                Layers.SetAll(true);
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

        /// <summary>One set of switches, photographed from every view.</summary>
        private static void Pass(Camera cam, string suffix, System.Action apply)
        {
            apply();

            int drawn = 0;
            foreach (var kind in Layers.All) if (Layers.IsOn(kind)) drawn++;
            Debug.Log($"[layershot] '{suffix}': {drawn} of {Layers.All.Length} layers drawn.");

            foreach (var view in Views)
            {
                var target = new Vector3(view.X, view.Eye, -view.Y);
                CityShot.Frame(cam.gameObject, target, view.Dist, view.Pitch, view.Yaw);
                CityShot.Capture(cam, Path.Combine(CityShot.OutputDir, view.Name + "-" + suffix + ".png"));
            }
        }
    }
}
