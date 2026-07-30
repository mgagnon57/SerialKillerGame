using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.World;
// UnityEngine has its own Terrain (the 3D heightmap kind), so name ours explicitly.
using Terrain = Noir.Core.World.Terrain;

namespace Noir.Unity
{
    /// <summary>
    /// The sun, the sky, and the lights people leave on.
    ///
    /// In 3D this is mostly Unity doing the work: one directional light whose angle is driven
    /// by the clock, and real shadows fall out of it. Long raking shadows at eight in the
    /// morning, short ones at noon, and the whole village going blue as the light drops below
    /// the horizon - none of that is drawn, it is just geometry lit properly.
    ///
    /// The part worth caring about is still the windows: a dwelling lights up when somebody is
    /// actually home and awake. A street going dark one window at a time between ten and
    /// midnight IS the village being alive, and the data is free - the simulation already knows
    /// where everyone is.
    ///
    /// Every window and every lamp in the town glows; only a few dozen of them get a real point
    /// light on top of it, and which few is decided by LightPool. That is not a compromise but
    /// the actual physics of the thing: the glass is what you see, and the spill on the wall
    /// beneath it only reads from close by.
    /// </summary>
    public sealed class SunRig : MonoBehaviour
    {
        private VillageHost _host;
        private Light _sun;
        private Material _sky;

        private Fixtures _fixtures;
        private MaterialPropertyBlock _paneBlock;
        private MaterialPropertyBlock _lanternBlock;

        /// <summary>
        /// Every light and pane of glass in the village, each paired with the place it belongs
        /// to. Parallel lists rather than a list of small objects because the update loop walks
        /// them by index.
        ///
        /// Lamps and Windows are NOT Light components any more - they are handles into Lights,
        /// which owns the whole village's supply of real point lights and hands out a few dozen
        /// of them to whatever is nearest. Panes and Lanterns are emissive surfaces and are not
        /// pooled at all: every one of them in the town is driven, every time, because that is
        /// what a lit window actually looks like from anywhere but arm's length. See LightPool.
        /// </summary>
        public sealed class Fixtures
        {
            public readonly LightPool Lights;

            public readonly List<int> Lamps = new List<int>();
            public readonly List<MeshRenderer> Lanterns = new List<MeshRenderer>();
            public readonly List<int> Windows = new List<int>();
            public readonly List<PlaceId> WindowPlaces = new List<PlaceId>();
            public readonly List<MeshRenderer> Panes = new List<MeshRenderer>();
            public readonly List<PlaceId> PanePlaces = new List<PlaceId>();

            /// <summary>Glass that came with a bought building, lit by swapping its material.</summary>
            public readonly List<MeshRenderer> Glazing = new List<MeshRenderer>();
            public readonly List<PlaceId> GlazingPlaces = new List<PlaceId>();

            public Fixtures(Transform parent) { Lights = new LightPool(parent); }
        }

        public static SunRig Create(VillageHost host, Transform parent)
        {
            var go = new GameObject("Sun & Lights");
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<SunRig>();
            rig.Build(host);
            return rig;
        }

        private void Build(VillageHost host)
        {
            _host = host;

            // Any directional light already in the scene would fight ours.
            foreach (var existing in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (existing.type == LightType.Directional && !existing.transform.IsChildOf(transform))
                    existing.enabled = false;

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.shadows = LightShadows.Soft;
            _sun.shadowStrength = 0.7f;
            _sun.intensity = 1f;

            BuildSky();

            _fixtures = BuildFixtures(host.World, transform);

            Debug.Log($"Lighting: {_fixtures.Windows.Count} window lights, {_fixtures.Panes.Count} panes, "
                    + $"{_fixtures.Lamps.Count} street lamps, sharing "
                    + $"{_fixtures.Lights.SlotCount} real lights.");
        }

        /// <summary>
        /// A real sky and distance fog.
        ///
        /// Cheapest atmosphere in the project by a wide margin. A procedural skybox gives an
        /// actual horizon and a sun disc that tracks the directional light, and fog does the
        /// thing every outdoor scene needs - it puts air between you and the far end of the
        /// village, so distance reads as distance instead of as a flat backdrop.
        /// </summary>
        private void BuildSky()
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader != null)
            {
                _sky = new Material(shader) { name = "AshcombeSky" };
                if (_sky.HasProperty("_SunSize")) _sky.SetFloat("_SunSize", 0.04f);
                if (_sky.HasProperty("_AtmosphereThickness")) _sky.SetFloat("_AtmosphereThickness", 1.2f);
                RenderSettings.skybox = _sky;
            }
            else
            {
                Debug.LogWarning("Skybox/Procedural not found; falling back to a flat background.");
            }

            RenderSettings.sun = _sun;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0012f;

            // URP's shadow distance defaults to 50 metres. The village is 170 across and the
            // camera sits about 90 out, so at the default almost nothing was within shadow
            // range - which reads as "the lighting is broken" when in fact the sun was working
            // perfectly and simply not casting past the end of your nose.
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                urp.shadowDistance = 320f;
                urp.shadowCascadeCount = 4;   // keeps near shadows sharp over that range
            }
            else
            {
                Debug.LogWarning("Not running on URP - shadows will use whatever the "
                               + "pipeline defaults to.");
            }
        }

        /// <summary>
        /// Every lamp, window light and pane in the village, built under one parent.
        ///
        /// Static and fed only a world and a transform, because the offline snapshot renderer
        /// has neither a host nor a simulation behind it and still has to produce the same
        /// fixtures. Without this it built the village and none of its lights, so the two night
        /// shots - the pictures most worth having, and the hardest to tune by eye - came out as
        /// a black rectangle.
        /// </summary>
        public static Fixtures BuildFixtures(WorldModel world, Transform parent)
        {
            var fixtures = new Fixtures(parent);

            // Every place that HAS walls gets glass in them.
            //
            // This was a hand-written list of six kinds, and the five it left out - the school,
            // the post office, the village hall, the surgery and the garage - were the only
            // walled buildings in Ashcombe with no windows at all. Not dark windows: none. A
            // front door, a sign, and thirty metres of unbroken stone, in a street where every
            // cottage has a row of them. The hall is authored open until ten at night and stood
            // next to the lit mill as a solid black box.
            //
            // Asking the roof builder settles it, because a building that is worth putting a
            // roof over is a building that is worth putting windows in, and the two lists can
            // no longer drift apart.
            foreach (var place in world.AllPlaces)
                if (RoofBuilder.IsRoofed(place.Kind))
                    AddWindow(world, place, parent, fixtures);

            BuildStreetLamps(world, parent, fixtures);
            return fixtures;
        }

        private static void AddWindow(WorldModel world, Place place, Transform parent, Fixtures into)
        {
            if (place == null) return;

            // No GameObject and no Light: a building registers a CLAIM on one of the pool's few
            // dozen real lights and gets one only while it is among the nearest things that are
            // actually on. The place id is the tiebreak, and it has to be something the village
            // decides rather than something the frame does - see LightPool.Compare.
            into.Windows.Add(into.Lights.Add(
                Space3D.ToWorld(place.Bounds.Centre) + Vector3.up * 1.8f,
                new Color(1.00f, 0.80f, 0.50f),   // tungsten, not daylight
                Mathf.Max(6f, Mathf.Max(place.Bounds.W, place.Bounds.H) * 1.4f),
                place.Id.Value));
            into.WindowPlaces.Add(place.Id);

            // THE POINT LIGHT STAYS, THE PANES DO NOT. A bought building has its own glass in
            // its own walls; these panes are quads positioned against the PROCEDURAL walls,
            // which for these places are no longer built. They were left hanging in the air in
            // front of the terraces like lit paintings.
            if (CityBuildings.Handles(place))
            {
                CityBuildings.CollectGlazing(place, into.Glazing, into.GlazingPlaces);
                return;
            }

            BuildWindowPanes(world, place, parent, into.Panes, into.PanePlaces);
        }

        /// <summary>
        /// Actual windows on the outside walls, which glow when somebody is in.
        ///
        /// The point light above lights the room; this is the thing you SEE from the street,
        /// and it is the defining image of a village at night. Without it, light spills from
        /// buildings that visibly have no windows in them.
        ///
        /// Placed on wall tiles that have open ground outside, every third tile, so a cottage
        /// gets two or three and the mill gets a row.
        /// </summary>
        public static void BuildWindowPanes(WorldModel world, Place place, Transform parent,
                                            List<MeshRenderer> panes, List<PlaceId> panePlaces)
        {
            var grid = world.Grid;
            var b = place.Bounds;
            int placed = 0;

            for (int y = b.Y; y <= b.Bottom; y++)
            for (int x = b.X; x <= b.Right; x++)
            {
                bool onPerimeter = x == b.X || x == b.Right || y == b.Y || y == b.Bottom;
                if (!onPerimeter) continue;
                if (grid.TerrainAt(x, y) != Terrain.Wall) continue;

                // Which way does this wall face? Only where there is outdoors to look at.
                Vector3 outward = Vector3.zero;
                if (y == b.Y && Outdoors(grid, x, y - 1)) outward = new Vector3(0f, 0f, 1f);
                else if (y == b.Bottom && Outdoors(grid, x, y + 1)) outward = new Vector3(0f, 0f, -1f);
                else if (x == b.X && Outdoors(grid, x - 1, y)) outward = new Vector3(-1f, 0f, 0f);
                else if (x == b.Right && Outdoors(grid, x + 1, y)) outward = new Vector3(1f, 0f, 0f);
                else continue;

                if (placed++ % 3 != 0) continue;   // not every tile; a wall is mostly wall

                var pane = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pane.name = "window";
                pane.transform.SetParent(parent, false);

                var centre = Space3D.ToWorld(new Tile(x, y)) + Vector3.up * 1.55f;
                pane.transform.position = centre + outward * 0.52f;

                bool facingZ = Mathf.Abs(outward.z) > 0.5f;
                pane.transform.localScale = facingZ
                    ? new Vector3(0.85f, 0.95f, 0.08f)
                    : new Vector3(0.08f, 0.95f, 0.85f);

                Discard(pane.GetComponent<Collider>());

                var mr = pane.GetComponent<MeshRenderer>();
                mr.sharedMaterial = Materials3D.WindowGlass;
                mr.shadowCastingMode = ShadowCastingMode.Off;

                panes.Add(mr);
                panePlaces.Add(place.Id);
            }
        }

        /// <summary>A road tile with something that is not road beside it - i.e. the edge.</summary>
        private static bool NextToVerge(WorldModel world, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (!world.Grid.InBounds(nx, ny)) continue;
                if ((world.Grid.FlagsAt(nx, ny) & TileFlags.Road) == 0) return true;
            }
            return false;
        }

        private static bool Outdoors(TileGrid grid, int x, int y)
        {
            if (!grid.InBounds(x, y)) return false;
            var t = grid.TerrainAt(x, y);
            return t != Terrain.Wall && t != Terrain.Floor;
        }

        /// <summary>
        /// Throw away a component. Object.Destroy is a no-op outside play mode - it logs
        /// "Destroy may not be called from edit mode" and leaves the object alone - so the
        /// snapshot renderer, which builds the village without ever pressing Play, produced
        /// hundreds of error lines and kept every collider it asked to be rid of.
        /// </summary>
        private static void Discard(Object doomed)
        {
            if (doomed == null) return;
            if (Application.isPlaying) Destroy(doomed);
            else DestroyImmediate(doomed);
        }

        /// <summary>
        /// Lamps along the kerb, evenly spaced.
        ///
        /// The previous rule required a road tile whose x AND y were both multiples of 16.
        /// On this map no tile satisfies both, so the village had exactly zero street lamps
        /// and the nights were pitch dark for a reason that looked like a lighting bug.
        /// Spacing is now measured against the lamps already placed, which works on any
        /// street layout rather than only on ones that happen to land on a grid.
        /// </summary>
        public static void BuildStreetLamps(WorldModel world, Transform parent, Fixtures into)
        {
            const float spacing = 13f;
            var placed = new List<Vector2>();

            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                if ((world.Grid.FlagsAt(x, y) & TileFlags.Road) == 0) continue;

                // On the kerb, not in the carriageway.
                if (!NextToVerge(world, x, y)) continue;

                bool tooClose = false;
                for (int i = 0; i < placed.Count; i++)
                {
                    float dx = placed[i].x - x, dy = placed[i].y - y;
                    if (dx * dx + dy * dy < spacing * spacing) { tooClose = true; break; }
                }
                if (tooClose) continue;
                placed.Add(new Vector2(x, y));

                var ground = Space3D.ToWorld(new Tile(x, y));

                // A lamp needs a lamp POST. Light appearing from nothing five metres up reads
                // as a bug in daylight, and at night you lose the thing that makes a lit street
                // feel like a street: the silhouette of the column under the glow.
                var column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                column.name = $"lamppost:{x},{y}";
                column.transform.SetParent(parent, false);
                column.transform.position = ground + Vector3.up * 2.1f;
                column.transform.localScale = new Vector3(0.16f, 2.1f, 0.16f);
                Discard(column.GetComponent<Collider>());
                column.GetComponent<MeshRenderer>().sharedMaterial = Materials3D.Ironwork;

                var lantern = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lantern.name = "lantern";
                lantern.transform.SetParent(parent, false);
                lantern.transform.position = ground + Vector3.up * 4.35f;
                lantern.transform.localScale = new Vector3(0.42f, 0.5f, 0.42f);
                Discard(lantern.GetComponent<Collider>());

                var glass = lantern.GetComponent<MeshRenderer>();
                glass.sharedMaterial = Materials3D.LampGlass;
                glass.shadowCastingMode = ShadowCastingMode.Off;
                into.Lanterns.Add(glass);

                into.Lamps.Add(into.Lights.Add(ground + Vector3.up * 4.2f,
                                               new Color(1.00f, 0.85f, 0.60f), 14f,
                                               LampKey(x, y)));
            }
        }

        /// <summary>
        /// A lamp's tiebreak in the pool's ordering. Negative so it can never collide with a
        /// place id, and derived from the tile so it stays the same whatever order the lamps
        /// happen to be placed in. Ashcombe is 170 tiles across; 4096 is room to spare.
        /// </summary>
        private static int LampKey(int x, int y) => -(1 + y * 4096 + x);

        private void Update()
        {
            if (_host?.Sim == null) return;

            float hour = _host.Sim.Clock.MinuteOfDay / 60f;
            _sun.transform.rotation = SunRotation(hour);

            var (colour, intensity, ambient) = SkyAt(hour);
            _sun.color = colour;
            _sun.intensity = intensity;
            _sun.enabled = intensity > 0.01f;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;

            RenderSettings.fogColor = FogAt(colour, intensity, ambient);

            // Density is an order of magnitude lower than it was. Fog should say "there is air
            // between us", not "somebody has put tracing paper over the far half of the map" -
            // and at the old values the whole top of the frame was washed to grey.
            RenderSettings.fogDensity = Mathf.Lerp(0.0022f, 0.0010f, Mathf.Clamp01(intensity));

            // Dim the whole sky at night rather than leaving a bright daylight dome overhead.
            if (_sky != null && _sky.HasProperty("_Exposure"))
                _sky.SetFloat("_Exposure", Mathf.Lerp(0.18f, 1.15f, Mathf.Clamp01(intensity)));

            float level = NightLevel(intensity);

            RefreshOccupancy();
            UpdateGlow(level);
            UpdateLightPool();

            // The fades, and the only part of the lighting that is per-frame work: it walks
            // thirty-two slots rather than every window in the town.
            _fixtures.Lights.Tick(Time.unscaledDeltaTime);

            UpdateShutters();
        }

        private int _poolMinute = -1;
        private float _poolAt = -999f;
        private Vector3 _poolEye;
        private Transform _eye;

        /// <summary>
        /// Deal the pool's real lights out to whatever is nearest and lit.
        ///
        /// Once a minute of village time, on the same reasoning as UpdateShutters - who has a
        /// light on is a fact about the clock, and a fixed cadence is what makes the assignment
        /// reproducible. But the clock is only half of it: WHERE YOU ARE changes the answer too
        /// and does not move with the clock at all. At 10x a village minute is six real seconds,
        /// and six seconds at walking pace is twenty-five metres of street whose lights had not
        /// been dealt yet, so a distance trigger sits alongside the minute.
        ///
        /// The real-time floor underneath both is what keeps a handover able to finish; see
        /// LightPool.ReassignInterval.
        /// </summary>
        private void UpdateLightPool()
        {
            var eye = Viewpoint();
            int minute = _host.Sim.Clock.MinuteOfDay;

            bool due = minute != _poolMinute || (eye - _poolEye).sqrMagnitude > 16f;
            if (!due) return;
            if (Time.unscaledTime - _poolAt < LightPool.ReassignInterval) return;

            _poolMinute = minute;
            _poolEye = eye;
            _poolAt = Time.unscaledTime;
            _fixtures.Lights.Reassign(eye);
        }

        /// <summary>
        /// Where the pool measures distance from.
        ///
        /// Not Camera.main outright: nothing guarantees anything in the scene carries the tag,
        /// and the same fixtures are built by the offline renderer, which has its own camera and
        /// never presses Play. With no camera at all the pool lights the middle of the village,
        /// which is arbitrary but bounded - the alternative is a null dereference in Update.
        /// </summary>
        private Vector3 Viewpoint()
        {
            if (_eye == null)
            {
                var cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
                if (cam != null) _eye = cam.transform;
            }
            return _eye != null
                ? _eye.position
                : Space3D.Centre(_host.World.Width, _host.World.Height);
        }

        private int _shutterMinute = -1;

        /// <summary>
        /// Put the shutters up when a place closes and take them down when it opens.
        ///
        /// Frontage builds the boarding for all eleven trading frontages and then hides it, so
        /// until something called in the whole system was geometry that had never rendered a
        /// pixel - while the console reported "11 shuttered frontages" as though it worked.
        /// Opening hours are already authored data; this is the cheapest signal in the project
        /// that the village runs on something rather than merely containing buildings.
        ///
        /// Once a minute of village time, not once a frame: at 300x that is still only five
        /// times a second, and nothing about a shutter needs to be smoother than the clock.
        /// </summary>
        private void UpdateShutters()
        {
            var clock = _host.Sim.Clock;
            if (clock.MinuteOfDay == _shutterMinute) return;
            _shutterMinute = clock.MinuteOfDay;

            foreach (var place in _host.World.AllPlaces)
                if (Frontage.HasShutter(place.Id))
                    Frontage.SetOpen(place.Id, place.AlwaysOpen || place.IsOpenAt(clock));
        }

        /// <summary>
        /// How dark it is as far as the lights are concerned: 0 in daylight, 1 in the dead of
        /// night, ramping over the last of the dusk.
        ///
        /// Public alongside the two intensities below because the offline snapshot renderer has
        /// to reach the same brightness at the same hour. A second copy of these numbers would
        /// mean the night pictures showed a village that does not exist.
        /// </summary>
        public static float NightLevel(float sunIntensity) =>
            Mathf.InverseLerp(0.35f, 0f, sunIntensity);

        public const float LampIntensity = 2.2f;
        public const float WindowIntensity = 2.6f;

        private int _glowMinute = -1;

        /// <summary>
        /// The glow in the glass and in the lanterns, and what each pooled light is being asked
        /// for. Every pane and every lantern in the village, pooled or not - this is the thing
        /// you actually SEE, and it is the cheapest signal in the project.
        ///
        /// Once a minute of village time, or whenever somebody comes or goes, on the same
        /// reasoning as UpdateShutters. It used to run over every pane every frame for nothing:
        /// the brightness here is a function of the hour ALONE - Update derives it from
        /// Clock.MinuteOfDay - and occupancy is the only other input, so between those two
        /// events every one of several hundred panes was being written the value it already
        /// had. Nothing is lost by skipping: at 1x the level moves by half a percent per village
        /// minute, and by construction it cannot move at all between them.
        /// </summary>
        private void UpdateGlow(float level)
        {
            int minute = _host.Sim.Clock.MinuteOfDay;
            if (minute == _glowMinute && !_occupancyChanged) return;
            _glowMinute = minute;

            if (_lanternBlock == null) _lanternBlock = new MaterialPropertyBlock();
            foreach (var lantern in _fixtures.Lanterns)
                SetLanternGlow(lantern, _lanternBlock, level);

            if (_paneBlock == null) _paneBlock = new MaterialPropertyBlock();
            for (int i = 0; i < _fixtures.Panes.Count; i++)
            {
                bool lit = _occupied.Contains(_fixtures.PanePlaces[i].Value);
                SetPaneGlow(_fixtures.Panes[i], _paneBlock, lit ? level : 0f);
            }

            var lights = _fixtures.Lights;
            // A bought building's own glass, switched between the pack's day and night
            // materials. Same occupancy test as a pane: the window is lit because somebody is
            // in and awake, which is the only reason a window is ever lit in this game.
            for (int i = 0; i < _fixtures.Glazing.Count; i++)
            {
                bool on = level > 0.02f && _occupied.Contains(_fixtures.GlazingPlaces[i].Value);
                CityBuildings.SetGlazing(_fixtures.Glazing[i], on);
            }

            for (int i = 0; i < _fixtures.Lamps.Count; i++)
                lights.SetIntensity(_fixtures.Lamps[i], level * LampIntensity);

            for (int i = 0; i < _fixtures.Windows.Count; i++)
                lights.SetIntensity(_fixtures.Windows[i],
                    _occupied.Contains(_fixtures.WindowPlaces[i].Value)
                        ? level * WindowIntensity : 0f);
        }

        private static readonly int PaneEmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int PaneBaseId = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// One pane, lit or not: warm at full glow, a cold dark rectangle at none, so an empty
        /// house is visibly empty from the street.
        ///
        /// Driven through a property block rather than a material so all several hundred panes
        /// stay one batch, and public so the snapshot renderer glows its windows with these
        /// exact colours rather than its own guess at them.
        /// </summary>
        /// <summary>
        /// The lantern itself, lit or dark.
        ///
        /// This has to be driven rather than baked into the material, which is how it was: a
        /// permanently emissive lantern is a small white rectangle at noon, and once bloom was
        /// added to the pipeline every lamp post in the village had a glare around it in full
        /// sunlight. Glass is only bright when there is something behind it.
        /// </summary>
        public static void SetLanternGlow(MeshRenderer lantern, MaterialPropertyBlock block, float glow)
        {
            // Fixtures are collected BEFORE CityChunker bakes and used after it, and the bake
            // combines away most of what it finds - so by the time this runs a lantern may be a
            // destroyed object that a stale reference still points at. Touching one throws, and
            // because that throw happened inside the render it took the whole photograph with
            // it: every city still came out as whatever had been on the buffer before.
            if (lantern == null) return;

            var warm = new Color(1.00f, 0.82f, 0.50f);

            lantern.GetPropertyBlock(block);
            block.SetColor(PaneEmissionId, warm * (glow * 1.35f));
            block.SetColor(PaneBaseId, Color.Lerp(new Color(0.34f, 0.35f, 0.33f), warm, glow));
            lantern.SetPropertyBlock(block);
        }

        public static void SetPaneGlow(MeshRenderer pane, MaterialPropertyBlock block, float glow)
        {
            if (pane == null) return;      // may have been baked away; see SetLanternGlow

            var warm = new Color(1.00f, 0.80f, 0.48f);
            var cold = new Color(0.16f, 0.19f, 0.24f);

            // 1.5 rather than 3.2. Above about 1.8 even a tonemapper has nothing left to work
            // with and the pane goes to flat paper white - all the warmth that makes a lit
            // window read as a lamp behind a curtain is in the range just above 1, not far
            // above it. Bloom does the rest of the job of looking bright.
            pane.GetPropertyBlock(block);
            block.SetColor(PaneEmissionId, warm * (glow * 1.5f));
            block.SetColor(PaneBaseId, Color.Lerp(cold, warm, glow * 0.7f));
            pane.SetPropertyBlock(block);
        }

        private HashSet<int> _occupied = new HashSet<int>();
        private HashSet<int> _spare = new HashSet<int>();
        private bool _occupancyChanged;

        /// <summary>
        /// Which places hold somebody awake. Built once per frame by walking the agents rather
        /// than having each of sixty windows search all 105 people for the same answer.
        ///
        /// It also reports whether the answer CHANGED, which is what lets the glow skip the
        /// frames where there is nothing to say. Compared as a set against the previous one
        /// rather than by a hash of it: this is a few hundred integers, and a hash collision
        /// would show up as a house that stayed dark with somebody in it - which is precisely
        /// the failure the pooling work was about in the first place.
        /// </summary>
        private void RefreshOccupancy()
        {
            var next = _spare;
            next.Clear();

            var sim = _host.Sim;
            for (int i = 0; i < sim.AgentCount; i++)
            {
                var a = sim.GetAgent(i);
                if (a.Travelling || !a.At.IsValid || a.Doing == Activity.Asleep) continue;
                next.Add(a.At.Value);
            }

            _occupancyChanged = !next.SetEquals(_occupied);
            _spare = _occupied;
            _occupied = next;
        }

        /// <summary>
        /// The colour the distance fades to. Fog takes the sky's colour, so the horizon and the
        /// far end of the village agree with each other instead of the fog reading as a wall.
        ///
        /// Both terms are scaled by how much sun there actually is, and that is the whole point
        /// of the method. The previous version boosted ambient by a flat 1.6 and leant a flat
        /// 35% towards the sun's colour at every hour of the day: at dusk that came out at about
        /// (0.46, 0.41, 0.54) over an ambient of (0.19, 0.19, 0.28) and a sky dimmed to a third
        /// of its daylight exposure - fog BRIGHTER than both the village and the sky above it.
        /// The horizon became a pale band across a darkening scene and the evening read as a
        /// bug. In full daylight the scaling is 1 and the numbers are exactly as they were.
        ///
        /// Public for the offline snapshot renderer, which has to fade its distance to the same
        /// colour the game does or the pictures are of somewhere else.
        /// </summary>
        public static Color FogAt(Color sun, float intensity, Color ambient)
        {
            float daylight = Mathf.Clamp01(intensity);
            return Color.Lerp(ambient * Mathf.Lerp(1f, 1.6f, daylight), sun, 0.35f * daylight);
        }

        /// <summary>
        /// Where the sun is at a given hour. Elevation sweeps -90 (midnight, below the
        /// horizon) through 0 at sunrise to 90 overhead at noon and on to 180.
        /// </summary>
        public static Quaternion SunRotation(float hour)
        {
            // Sunrise at five, sunset at nine: a summer day, and sixteen hours of it.
            //
            // The old mapping spread the hours evenly around the whole circle, which puts
            // sunrise at exactly 06:00 and sunset at exactly 18:00. The intensity curve below
            // disagrees - it still calls for 0.45 at seven in the evening and does not reach
            // zero until ten - so for the whole of the golden hour the sun was UNDERGROUND.
            // Dusk rendered with no cast shadows at all and no lit side to anything, and the
            // obvious read was "the evening is too dark, raise the ambient", which would have
            // been the wrong fix for the second time in this file.
            const float Sunrise = 5f, Sunset = 21f;

            float h = Mathf.Repeat(hour, 24f);
            float elevation = h >= Sunrise && h <= Sunset
                ? (h - Sunrise) / (Sunset - Sunrise) * 180f
                : 180f + Mathf.Repeat(h - Sunset, 24f) / (24f - (Sunset - Sunrise)) * 180f;

            // X sweeps 0 at sunrise through 90 overhead to 180 at sunset; Y is the compass
            // bearing of the arc, which is what makes shadows fall across the streets rather
            // than along them.
            return Quaternion.Euler(elevation, 150f, 0f);
        }

        /// <summary>
        /// Sun colour, sun intensity, and ambient fill, keyframed across the day.
        ///
        /// Public so the offline snapshot renderer can light a scene with exactly the same
        /// curve. If it had its own copy, the pictures would be of a village that does not
        /// exist and tuning against them would be worthless.
        /// </summary>
        public static (Color sun, float intensity, Color ambient) SkyAt(float hour)
        {
            var keys = new (float h, Color sun, float i, Color amb)[]
            {
                (0f,  new Color(0.55f, 0.62f, 0.95f), 0.00f, new Color(0.10f, 0.13f, 0.22f)),
                (5f,  new Color(0.70f, 0.62f, 0.80f), 0.06f, new Color(0.14f, 0.16f, 0.26f)),
                (6.5f,new Color(1.00f, 0.72f, 0.48f), 0.55f, new Color(0.26f, 0.25f, 0.30f)),
                (9f,  new Color(1.00f, 0.94f, 0.84f), 1.05f, new Color(0.38f, 0.40f, 0.44f)),
                (13f, new Color(1.00f, 0.98f, 0.94f), 1.25f, new Color(0.44f, 0.46f, 0.50f)),
                (17f, new Color(1.00f, 0.90f, 0.76f), 1.00f, new Color(0.38f, 0.38f, 0.42f)),
                (19f, new Color(1.00f, 0.62f, 0.40f), 0.45f, new Color(0.26f, 0.24f, 0.30f)),
                (20.5f,new Color(0.62f, 0.58f, 0.86f), 0.08f, new Color(0.15f, 0.17f, 0.27f)),
                (22f, new Color(0.55f, 0.62f, 0.95f), 0.00f, new Color(0.10f, 0.13f, 0.22f)),
                (24f, new Color(0.55f, 0.62f, 0.95f), 0.00f, new Color(0.10f, 0.13f, 0.22f)),
            };

            for (int i = 0; i < keys.Length - 1; i++)
            {
                if (hour < keys[i].h || hour > keys[i + 1].h) continue;
                float t = Mathf.InverseLerp(keys[i].h, keys[i + 1].h, hour);
                return (Color.Lerp(keys[i].sun, keys[i + 1].sun, t),
                        Mathf.Lerp(keys[i].i, keys[i + 1].i, t),
                        Color.Lerp(keys[i].amb, keys[i + 1].amb, t));
            }
            return (keys[0].sun, keys[0].i, keys[0].amb);
        }
    }
}
