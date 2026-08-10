using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// The ground, DRESSED, in daylight - the one thing the committed snapshot set cannot show.
    ///
    /// WHY IT IS NOT JUST ANOTHER CityShot CAMERA. That set is the dark SURVEY PLAN:
    /// VillageHost.ShowBuildings is false and Materials3D.Plan dims the ground to near-black
    /// behind it. Worse, Materials3D.ShowGroundColour is `!Application.isBatchMode &amp;&amp; ...`, so it
    /// is hard-false in every headless run there is. A batch-mode plan render therefore CANNOT
    /// show what colour the ground is or what stands on it, which makes it exactly the wrong
    /// instrument for checking that a new terrain kind or a new ground-level structure reads
    /// correctly - it would verify through the one path that bypasses the thing being verified.
    ///
    /// So this forces ShowBuildings on for the duration and puts it back afterwards. It writes
    /// its own files and never touches the plan set.
    ///
    /// Ground, greenery and the rail bed only. The buildings, traffic and street furniture are a
    /// long way from the North Fork and would add minutes to a render that is about a river.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.GroundShot.Water
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.GroundShot.Rail
    /// </summary>
    public static class GroundShot
    {
        /// <summary>One camera: where it looks, how far back, and what to call the file.</summary>
        private readonly struct Shot
        {
            public readonly string Name;
            public readonly Vector2 At;          // map coordinates, straight off features.txt
            public readonly float Eye, Dist, Pitch, Yaw;

            /// <summary>Snap to the railroad: At is treated as "near here", and both the exact
            /// spot and the yaw are taken from the real polyline instead of from Yaw. See
            /// SnapToRail - hand-computed bearings put the first three rail shots in a wood.</summary>
            public readonly bool AlongRail;
            public readonly float Beside;        // metres to the right of the track

            public Shot(string name, float mx, float my, float dist, float pitch, float yaw,
                        float eye = 0f, bool alongRail = false, float beside = 0f)
            {
                Name = name; At = new Vector2(mx, my);
                Dist = dist; Pitch = pitch; Yaw = yaw; Eye = eye;
                AlongRail = alongRail; Beside = beside;
            }
        }

        /// <summary>
        /// The point on the real CSX polyline nearest `near`, and the bearing of the track there
        /// as a Unity yaw.
        ///
        /// WHY THIS EXISTS. The first three rail cameras were aimed by reading coordinates out of
        /// features.txt and working the bearing out on paper, and two of them photographed a wood
        /// forty metres from anything. The alignment is data; asking it where it is costs eight
        /// lines and cannot be off by a hundred metres. Same reasoning as CityStory asking the
        /// road network which way a roadside cross should face rather than authoring the angle.
        /// </summary>
        private static (Vector2 at, float yaw) SnapToRail(Vector2 near, float beside)
        {
            Vector2 best = near, tangent = Vector2.right;
            float bestD2 = float.MaxValue;

            foreach (var feature in MapFeatures.All())
            {
                if (feature.Kind != "rail") continue;
                var pts = MapFeatures.Smoothed(feature.Points);
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    Vector2 a = pts[i], d = pts[i + 1] - a;
                    float len2 = d.sqrMagnitude;
                    if (len2 < 1e-6f) continue;

                    float t = Mathf.Clamp01(Vector2.Dot(near - a, d) / len2);
                    var p = a + d * t;
                    float d2 = (p - near).sqrMagnitude;
                    if (d2 >= bestD2) continue;
                    bestD2 = d2; best = p; tangent = d.normalized;
                }
            }

            // Map space is x east, y south; the right-hand side of a walk is (-dy, dx), the same
            // derivation LaneGraph uses for which side of a road to drive on.
            best += new Vector2(-tangent.y, tangent.x) * beside;

            // Unity yaw is clockwise from +Z, and +Z is north while map y counts southward - so
            // the world direction of a map tangent (tx, ty) is (tx, 0, -ty).
            float yaw = Mathf.Atan2(tangent.x, -tangent.y) * Mathf.Rad2Deg;
            return (best, yaw);
        }

        [MenuItem("Noir/Render The Water")]
        public static void Water() => Run("water", new[]
        {
            // THE NORTH FORK from the air, mid-course. Framed ALONG the water, not across it:
            // the question is whether it reads as a river running through farmland rather than
            // as a blue rectangle.
            new Shot("water-river", 150f, 1500f, 420f, 38f, 15f),

            // WHERE ATTICA CROSSES IT - the one place the river meets the town's own grid, and
            // the case that decides whether a road over water reads as a crossing or as a hole
            // in the carriageway.
            new Shot("water-crossing", 96f, 1335f, 70f, 12f, 90f, 1.6f),

            // THE PONDS BEHIND THE SCHOOL: closed shapes rather than a corridor, and the ones
            // somebody who grew up here would place instantly.
            new Shot("water-ponds", 300f, 1120f, 260f, 40f, 200f),

            // THE BANK at eye height. Water sits 0.35m below its neighbours and the ground mesh
            // closes that step with a riser; this is the only view at which a missing riser
            // shows, as a band of sky lying along the far bank.
            new Shot("water-bank", 360f, 1530f, 55f, 3f, 200f, 1.6f),
        });

        [MenuItem("Noir/Render The Railroad")]
        public static void Rail() => Run("rail", new[]
        {
            // EVERY ONE OF THESE SNAPS TO THE REAL POLYLINE - the coordinate is only "near here"
            // and SnapToRail supplies the exact spot and the bearing. A railroad photographed
            // across itself is a line; photographed along itself it is a railroad, so the yaw
            // matters as much as the position, and both are data rather than arithmetic I did
            // on paper and got wrong twice.

            // FROM BESIDE THE TRACK at eye height, looking down the line, standing nine metres
            // off it. If the ballast, the ties and the rails do not read as a railroad from
            // here they do not read at all.
            //
            // OUT IN THE COUNTRY, at the south-east end past the last of the town lots, rather
            // than the middle of town where this was first aimed: in among the blocks the frame
            // fills with paving and hedges before the eye ever reaches the ballast, and the shot
            // is meant to be about the track.
            new Shot("rail-track", 1803f, 2095f, 30f, 2f, 0f, 1.6f, true, 9f),

            // A LEVEL CROSSING. Attica is one of the four real OSM crossings; the bed drops and
            // the rails run flush through the carriageway. This is the shot that says whether a
            // crossing reads as a crossing or as a railway that stops at the kerb - so it looks
            // ACROSS the line from the road, which is the one rail view that should not be
            // along the track.
            new Shot("rail-crossing", 1291f, 1335f, 38f, 8f, 250f, 1.6f),

            // FROM THE AIR, along the real diagonal. The alignment is the whole point of the
            // feature - this is where it can be checked against the plan.
            new Shot("rail-alignment", 1230f, 1240f, 420f, 42f, 0f, 0f, true),

            // WHERE IT PASSES THE TOWN, which is what makes it Rossville's railroad rather than
            // a line through a field.
            new Shot("rail-town", 1100f, 1050f, 240f, 30f, 0f, 0f, true),
        });

        /// <summary>
        /// THE WHOLE DRESSED TOWN, exactly what pressing Play builds with Noir > Show The Built
        /// Town ticked - buildings, streets, district, suburb, parking, signs, story props, both
        /// railways, the farm, the powerlines and the greenery.
        ///
        /// It exists because the other two sets build ground and greenery only, so nothing had
        /// actually stood the new water and the new rail bed up alongside the four thousand
        /// renderers of brick town. "The parts I changed render fine on their own" is not the
        /// same claim as "the thing the user is about to press Play on comes up".
        /// </summary>
        /// <summary>
        /// THE JUNCTIONS THAT ARE NOT CROSSROADS, from straight above, one frame each.
        ///
        /// Every other set here is about atmosphere. This one is a measurement: the kit's turn and
        /// end pieces each have a built-in orientation that is written down nowhere in the pack, so
        /// the yaw CityStreets seats them at is a guess until somebody looks. A corner laid a
        /// quarter turn out does not change any count, does not fail any test, and is obvious in
        /// one picture - the asphalt simply runs off into the grass and the two roads it was meant
        /// to join arrive at a kerb.
        ///
        /// The coordinates are read off the build's own log, not authored: `[streets] corner at
        /// x,y yaw n - arms N E, road x road`. Re-read them if the network changes.
        ///
        /// STRAIGHT DOWN, deliberately. A corner seen at an angle is ambiguous about which way it
        /// turns; from directly overhead the tile's own painted markings settle it.
        /// </summary>
        [MenuItem("Noir/Render The Odd Junctions")]
        public static void Junctions() => Run("junction", new[]
        {
            // One of each yaw the corner case produces, so a systematic quarter-turn error cannot
            // hide behind a case that happens to be symmetric.
            new Shot("junction-corner-ne", 511f, 1590f, 55f, 89f, 0f),     // abner x perry,     N E
            new Shot("junction-corner-se", 510f, 1484f, 55f, 89f, 0f),     // abner x park,      S E
            new Shot("junction-corner-sw", 833f, 725f, 55f, 89f, 0f),      // harrison x york,   S W
            new Shot("junction-corner-nw", 1352f, 2103f, 55f, 89f, 0f),    // grove x thompson,  N W

            // The one dead end, which is an alley, so the piece is the small kit's.
            new Shot("junction-dead-end", 1168f, 2159f, 45f, 89f, 0f),     // alley8 x alley12,  E

            // A straight-through node, where JUNC-6 lays no junction tile at all and the
            // carriageway walk has to pave over it instead. If that handoff is wrong there is a
            // hole in the road here, which is the one failure mode of laying nothing.
            new Shot("junction-straight-through", 455f, 1324f, 60f, 89f, 0f),   // attica x 3550north

            // And a plain crossroads for comparison, so "the corner looks odd" can be judged
            // against what this kit's junctions normally look like from the same height.
            new Shot("junction-crossroads", 750f, 1335f, 60f, 89f, 0f),
        });

        [MenuItem("Noir/Render The Built Town")]
        public static void Town() => Run("town", new[]
        {
            // The whole place from above, which is the view Play opens on.
            new Shot("town-overview", 750f, 1335f, 1150f, 42f, 20f),

            // Chicago and Attica from the middle of the road - the one signalised junction, and
            // street level, where ground texture is actually resolvable.
            new Shot("town-street", 750f, 1335f, 40f, 4f, 0f, 1.6f),

            // 408 Holmes Ave, the first fixed story anchor - the CENTRE of the lot city.txt gives
            // it (1163,1242, 13x7), looking SOUTH from Holmes Avenue at its front door. It was
            // aimed at (1175,1218), 27 m off the lot with the house behind the camera, and had
            // been since the address was reset to the county. See CityShot's twin.
            new Shot("town-holmes", 1169.5f, 1245.5f, 26f, 4f, 180f, 1.6f),
        }, dressed: true);

        /// <summary>
        /// THE COUNTRY, WHICH NOTHING IN THIS PROJECT HAS EVER PHOTOGRAPHED.
        ///
        /// The PlayMode gate switches Trees, Farm and Powerlines OFF before the town builds -
        /// deliberately, see CityUnderTest, because building four thousand renderers under a
        /// traffic suite slows every run for nothing. The consequence is that a green gate has
        /// never once seen the hedges, the props, the field boundaries or the country ring, and
        /// the nine existing camera sets all point at the town. So 17,849 English hedges - 44.9%
        /// of every prop in Rossville - survived every gate this project has, and were found by
        /// counting rather than by looking, because there was nothing to look at.
        ///
        /// `dressed: true` is the whole point: it forces the built town on, which is what brings
        /// Countryside and CityGreenery into the frame at all.
        ///
        /// THE YAWS ARE A FIRST GUESS AND MUST BE CORRECTED BY LOOKING. Three hand-computed
        /// bearings once photographed a wood forty metres from anything; see SnapToRail above.
        /// </summary>
        [MenuItem("Noir/Render The Country")]
        public static void Country() => Run("country", new[]
        {
            // Standing at the west edge looking out: the last houses, the field boundary and the
            // fields behind it in one frame. If a hedge ever comes back, it comes back here.
            new Shot("country-edge", 40f, 1200f, 90f, 8f, 270f, 1.6f),

            // From above, where the patchwork either reads as Illinois farmland or does not.
            new Shot("country-pattern", 0f, 1200f, 420f, 34f, 250f),

            // The north edge, so the answer is not one accident of one side of town.
            new Shot("country-north", 1050f, 20f, 320f, 22f, 180f),
        }, dressed: true);

        /// <summary>
        /// THE ROOF, CLOSE ENOUGH TO SETTLE IT. Nothing in this project could judge a roof.
        ///
        /// The overview is 1,150 m up, where a shingle course is far below one pixel and every
        /// covering reads as a flat colour whatever it actually is. The two street shots are at
        /// eye height on the downtown block, where the buildings are FLAT-ROOFED commercial and
        /// the pitched roofs are above the frame entirely. So "the roofs look flat" was not a
        /// finding, it was the only thing any existing camera could have reported.
        ///
        /// These stand about twenty metres off a residential block and look DOWN the slope at a
        /// shallow angle, which is the one view where an asphalt course line is resolvable and
        /// where its direction is unambiguous - the courses must run ALONG the ridge, not up the
        /// slope. Three shots because the covering is a property of the building, so one frame
        /// might be all grey by luck.
        /// </summary>
        [MenuItem("Noir/Render The Roofs")]
        public static void RoofFrames() => Run("roof", new[]
        {
            // Down a residential street, high enough to see over the near roof onto the far ones.
            new Shot("roof-block", 1175f, 1240f, 34f, 22f, 0f, 9f),

            // ONE HOUSE, FILLING THE FRAME, FROM FOURTEEN METRES. The shot that answers whether
            // there is shingle at all, and it has to be this close: at 35 m the courses are
            // already below what the frame can resolve, which is how "the roofs look flat" gets
            // reported when the only thing actually measured is the camera.
            //
            // AIMED AT A BUILDING WHOSE COORDINATES WERE MEASURED, not at a street name. 401 Dale
            // Ave is 1177,1943 13x7 off the door audit, so its middle is 1183,1946 - three
            // earlier attempts at this frame were aimed by guessing where houses were and
            // photographed grass, a railway and a field in turn.
            new Shot("roof-close", 1183f, 1946f, 14f, 24f, 0f, 4f),

            // A second block, so a run of grey roofs cannot pass as the whole mix.
            new Shot("roof-mix", 900f, 1560f, 40f, 26f, 20f, 11f),

            // THE DOWNTOWN FROM ABOVE, which is the only place a FLAT roof can be judged. Every
            // other frame in this project looks at Main Street from the street, where a flat roof
            // is a parapet edge and nothing else - which is how the whole block came to be
            // covered in three-tab shingle without anybody seeing it.
            new Shot("roof-downtown", 750f, 1335f, 55f, 42f, 20f, 14f),
        }, dressed: true);

        private static void Run(string label, Shot[] shots, bool dressed = false)
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

                // TownPipeline guarantees the survey layer, so what is photographed here really is
                // what Play builds - which is the entire claim "Render The Built Town" makes.
                var built = TownPipeline.Build();
                var world = built.World;

                int wet = 0;
                for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                    if (world.Grid.TerrainAt(x, y) == Noir.Core.World.Terrain.Water) wet++;
                Debug.Log($"[groundshot:{label}] {world.Width}x{world.Height}, {wet:N0} water tiles "
                        + $"({100f * wet / (world.Width * world.Height):F3}% of the map).");

                // Split the same way CityShot splits it: the generated ground and rail bed are
                // already chunked meshes and are left alone, and only the bought greenery goes
                // through the chunker. Baking the ground a second time would work and would be
                // a different pipeline from the one the game runs, which is the whole point of
                // photographing it.
                root = new GameObject("GroundShot");
                VillageMesh.Build(world, root.transform, true);

                var dressing = new GameObject("GroundShotDressing");
                dressing.transform.SetParent(root.transform, false);

                if (dressed)
                {
                    // The same list, in the same order, as VillageHost's own ShowBuildings block.
                    // If these two ever drift, this stops being a preview of what Play does.
                    CityStreets.Build(world, dressing.transform);
                    CityParking.Build(world, dressing.transform);
                    CitySigns.Build(world, dressing.transform);
                    CityBuildings.Build(world, dressing.transform);
                    CityDistrict.Build(world, dressing.transform);
                    CitySuburb.Build(world, dressing.transform);
                    CityStory.Build(world, dressing.transform);
                    CityRail.Build(world, dressing.transform);
                    CityFarm.Build(world, dressing.transform);
                    CityPowerlines.Build(world, dressing.transform);
                }

                CityRailBed.Build(world, root.transform);
                CityGreenery.Build(world, dressing.transform);
                CityChunker.Bake(dressing);

                sunGo = new GameObject("GroundShotSun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.7f;

                camGo = new GameObject("GroundShotCamera");
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

                foreach (var shot in shots)
                {
                    var at = shot.At;
                    float yaw = shot.Yaw;
                    if (shot.AlongRail)
                    {
                        (at, yaw) = SnapToRail(shot.At, shot.Beside);
                        Debug.Log($"[groundshot] {shot.Name}: snapped to the rail at "
                                + $"{at.x:F0},{at.y:F0}, bearing {yaw:F0} degrees.");
                    }

                    // World space is x east, -z south (Space3D), so a map point is (mx, 0, -my).
                    //
                    // EYE HEIGHT IS ABOVE THE GROUND, NOT ABOVE SEA LEVEL, and it was not. This
                    // read `new Vector3(at.x, shot.Eye, -at.y)` - an ABSOLUTE Y - so every
                    // eye-height camera in this file was standing 1.6 m above world zero while
                    // Rossville sits on ground several metres higher. The camera was underground,
                    // aiming into the hill.
                    //
                    // `town-holmes` is the one that proves it. Its own comment calls 408 Holmes
                    // "the first fixed story anchor" and the frame has never shown the house: it
                    // showed the UNDERSIDES of roofs against a bright band of sky, with hedge
                    // blocks floating in a grey plane, because that is what the world looks like
                    // from below the ground. It was also the smallest PNG in its set, which is
                    // what a frame full of flat ground weighs.
                    float ground = ElevationGrid.HeightAt(at.x, at.y);
                    var target = new Vector3(at.x, ground + shot.Eye, -at.y);
                    CityShot.Frame(camGo, target, shot.Dist, shot.Pitch, yaw);
                    CityShot.Capture(cam, Path.Combine(CityShot.OutputDir, shot.Name + ".png"));
                }

                ShotLog.Stamp(label, CityShot.OutputDir, CityShot.TakeWritten());
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
