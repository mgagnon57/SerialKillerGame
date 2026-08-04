using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Sim;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// The one load-bearing MonoBehaviour.
    ///
    /// It bootstraps itself when you press Play — there is no scene to set up, no prefab to
    /// drag, no component to add. That is deliberate: every step that has to happen inside the
    /// Unity editor is a step I cannot do for you, so the number of them is kept at zero.
    ///
    /// It contains no game rules whatsoever. It reads input, advances the simulation, and hands
    /// an immutable view to the renderers. Everything that decides anything lives in Core.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class VillageHost : MonoBehaviour
    {
        public static VillageHost Instance { get; private set; }

        public WorldModel World { get; private set; }
        public Population People { get; private set; }
        public ParticularsTable Particulars { get; private set; }
        public Simulation Sim { get; private set; }
        public string LoadError { get; private set; }

        /// <summary>
        /// The year the simulation is in. THE ONLY ONE - there used to be a second, a frozen
        /// `Households.Year = 1991` const that ages were worked against because the clock had no
        /// calendar when it was written. It has one now, so this is it.
        ///
        /// Presentation code needs this because how old somebody is decides how tall they are
        /// drawn, what the panel prints, and whether they are called a child. Falls back to the
        /// epoch before the host exists, which is what a prototype scene or an editor tool that
        /// never pressed Play is looking at anyway.
        /// </summary>
        public static int Year =>
            Instance != null && Instance.Sim != null ? Instance.Sim.Clock.Year : GameClock.EpochYear;

        public CitizenId Selected { get; set; } = CitizenId.None;
        public bool Following { get; set; }

        /// <summary>
        /// The building the player has clicked on, if any.
        ///
        /// Kept separate from <see cref="Selected"/> rather than folded into one "selection"
        /// because the two answer different questions and the inspector shows different things
        /// for each. Only one is ever set at a time; selecting either clears the other.
        /// </summary>
        public PlaceId SelectedPlace { get; set; } = PlaceId.None;

        public Place SelectedPlaceModel =>
            SelectedPlace.IsValid ? World?.GetPlace(SelectedPlace) : null;

        /// <summary>
        /// A real county parcel with no place on it, if that is what got clicked.
        ///
        /// Only 326 of the town's 794 real lots have a house or a business generated on them -
        /// the rest are simply land nobody built on, which is most of what a plan actually shows.
        /// Before this, a click that missed every generated place found nothing at all: most of
        /// the visible town was not clickable, which reads as the picker being broken rather
        /// than as most of the plan being undeveloped. Set by OrbitCamera as the last resort
        /// after a person and a place have both failed to match; cleared by selecting either.
        /// </summary>
        public ParcelIndex.Parcel? SelectedParcel { get; set; }

        /// <summary>Null outside plan mode - there is nothing to draw a house shape onto when
        /// the real building models are up. See VillageHost.Awake.</summary>
        public FootprintDrawer Footprint { get; private set; }

        /// <summary>
        /// One seed for the whole village. It was previously written as two separate literals -
        /// one for the population, one for the simulation - which meant a typo in either would
        /// give you people generated from one world and day plans from another. Nothing would
        /// detect it: the village still builds and everyone still has a job, but "same seed,
        /// same village" quietly stops being true.
        /// </summary>
        public const ulong Seed = 1979;

        /// <summary>
        /// Which map the game loads.
        ///
        /// Northgate is built from bought models placed on authored lots; Ashcombe generated its
        /// geometry to fill lots instead, and is kept in the tree because it is the only thing
        /// the two can be compared against. Point this at "village.txt" to get it back.
        /// </summary>
        public const string MapFile = "city.txt";

        // ---- time ----
        //
        // Slow motion matters as much as fast-forward. At 1x a person crosses a room in real
        // time, which is too quick to follow a specific thing happening; a quarter speed lets
        // you actually watch somebody arrive, stop, and go in. The steps are roughly
        // logarithmic so each press is a noticeable change rather than a nudge.
        public static readonly float[] Speeds = { 0f, 0.25f, 0.5f, 1f, 3f, 10f, 60f, 300f };
        public static readonly string[] SpeedLabels =
            { "❚❚", "¼×", "½×", "1×", "3×", "10×", "60×", "300×" };

        /// <summary>Starts at 10x: fast enough to see the day move, slow enough to follow.</summary>
        public int SpeedIndex = 5;

        private double _tickAccumulator;
        private const int MaxTicksPerFrame = 24000;   // ~20 game minutes; stops a death spiral

        /// <summary>
        /// Whether the citizens are DRAWN. They are simulated regardless.
        ///
        /// Off while the city is being built out, and off again now while the focus is the town
        /// layout and the lot data rather than who lives where - turning it back on is this one
        /// flag: nothing downstream of it was deleted, and Sim, Population, the lit windows and
        /// the "who is inside this building" panel all keep working with it off, because none of
        /// them ever went through the figures.
        /// </summary>
        public static bool ShowPeople = true;

        /// <summary>
        /// Whether to raise the bought building models, or draw the town as a survey plan.
        ///
        /// OFF, on purpose, and not as a stopgap to be embarrassed about. The pack has two house
        /// families and both are Chicago brownstones; a Rossville street built from them is not a
        /// near miss. The plan draws what we actually know - the county's own 794 lot boundaries
        /// and the real street grid - and says nothing it cannot back up, which is the honest
        /// position until there is a kit that can build an Illinois frame house.
        ///
        /// It is deliberately NOT a live key: showing both means building both, and the brick
        /// town is four thousand renderers to keep hidden in case somebody wants to look at it.
        /// Chosen BEFORE Play, though, it costs nothing - only one of the two is ever built - so
        /// Bootstrap reads it from PlayerPrefs and Noir > Show The Built Town sets it. That
        /// matters because everything that dresses the town is behind this flag: the buildings,
        /// the streets, the greenery, the farm, the powerlines and the railroad. Somebody who
        /// pressed Play to look at the new rail bed and found a dark survey plan had no way to
        /// turn it on short of editing this line, which is what this key is for.
        /// </summary>
        public static bool ShowBuildings = false;

        /// <summary>Where the built-town switch is remembered between sessions. Read once in
        /// Bootstrap, before anything makes a material or a mesh - Materials3D.Plan asks
        /// ShowBuildings while it is building the ground palette, so setting it later would dim
        /// a ground that is about to be shown in full.</summary>
        public const string BuiltTownKey = "noir.town.built";

        /// <summary>
        /// The road corridors and the place-footprint rectangles CityOutlines draws on top of
        /// the county parcels. Off by default: the parcels read as a town on their own, and the
        /// road grid and a redundant footprint on every lot the parcel already outlines competed
        /// with the very thing they were meant to support. Set true for the comparison, same as
        /// ShowBuildings.
        /// </summary>
        public static bool ShowPlanRoads = false;
        public static bool ShowPlanFootprints = false;

        private GameObject _village;
        private XRay _xray;
        private AgentMeshView _agentView;
        private OrbitCamera _rig;
        private SunRig _lighting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;

            // Before the GameObject, and so before any mesh or material exists. The offline
            // renderers (CityShot, GroundShot) never come through here - they set the field
            // directly and put it back - so this cannot overwrite what a batch render asked for.
            //
            // NOT IN BATCH MODE, which is the same guard Materials3D.ShowGroundColour carries and
            // for a sharper reason: the PlayMode suite bootstraps this host, so without it one
            // developer's local tick would quietly decide whether the tests build a survey plan
            // or four thousand renderers of brick town. A headless run is the same run on every
            // machine or it is not a gate.
            // DEFAULTS ON NOW. It defaulted to the survey plan for as long as the pack could not
            // build an Illinois frame house, and the cost was that pressing Play showed a dark
            // drawing with no hint that a town existed behind it - which was reported as the
            // town being missing, twice. The answer is no longer a different default but the
            // LAYER SWITCHES: the town comes up whole and anything in the way of what you are
            // looking at comes off with one click. See Layers.
            if (!Application.isBatchMode)
                ShowBuildings = PlayerPrefs.GetInt(BuiltTownKey, 1) == 1;
            Debug.Log(ShowBuildings
                ? "[host] The built town is ON - buildings, streets, greenery, the farm and the "
                + "railroad. Turn it off again in Noir > Show The Built Town."
                : "[host] Survey plan (the default). Noir > Show The Built Town raises the "
                + "buildings, the greenery and the CSX line instead.");

            var go = new GameObject("Ashcombe");
            DontDestroyOnLoad(go);
            go.AddComponent<VillageHost>();
        }


        /// <summary>
        /// Where the two minutes go.
        ///
        /// Pressing Play on Rossville spends its whole build inside one synchronous Awake, and
        /// Unity reports that as a single "Integration: 121428 ms" line with no breakdown - which
        /// is exactly as useful as "slow". This times each stage and prints them worst-first, so
        /// the next person to ask why it takes so long gets an answer instead of a stopwatch.
        ///
        /// Costs one Stopwatch and a few dozen strings, once, on a path that already takes two
        /// minutes. It is not gated behind a flag because a profile nobody switches on is a
        /// profile nobody reads.
        /// </summary>
        private sealed class BuildProfile
        {
            private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();
            private readonly List<KeyValuePair<string, long>> _stages = new List<KeyValuePair<string, long>>();
            private long _mark;

            /// <summary>Close off the stage that just ran and name it.</summary>
            public void Done(string stage)
            {
                long now = _watch.ElapsedMilliseconds;
                _stages.Add(new KeyValuePair<string, long>(stage, now - _mark));
                _mark = now;
            }

            public void Report()
            {
                _stages.Sort((a, b) => b.Value.CompareTo(a.Value));

                var sb = new System.Text.StringBuilder();
                sb.Append("[build] ").Append(_watch.ElapsedMilliseconds).Append(" ms total, worst first:");
                foreach (var stage in _stages)
                {
                    if (stage.Value < 50) continue;          // noise
                    sb.AppendLine().Append("    ")
                      .Append(stage.Value.ToString().PadLeft(7)).Append(" ms  ")
                      .Append(stage.Key);
                }
                Debug.Log(sb.ToString());
            }
        }

        private void Awake()
        {
            Instance = this;

            // The UI goes on FIRST, before anything that can fail. It is what draws the error
            // if loading throws — added afterwards, a failure would be invisible and the screen
            // would just be empty, which is exactly the least useful thing it could do.
            gameObject.AddComponent<VillageUI>();

            // A camera has to exist even in the failure case, or there is nothing to draw the
            // error onto. An empty scene has none.
            EnsureCamera();

            try
            {
                if (!ContentLoader.Exists)
                    throw new Exception($"Content not found at {ContentLoader.Root}");

                // Before anything reads a kind. Core cannot open a file, so without this it
                // falls back to a copy of this table compiled into PlaceKindTable - which
                // covers the enum but not the content, so a barber authored in village.txt
                // would fail here as an unknown kind while working perfectly in the tools.
                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));

                // What the town has, and when it gets it. Without this line every citizen lives in
                // 1991 for the whole fifteen years.
                //
                // CAUGHT, NOT FATAL, and the asymmetry is the design: a technology with no row is
                // a technology the town does not have, so a missing file means the opening world
                // rather than a village that will not load. The kind table one line up is the
                // opposite and throws, because a kind with no row is a building with no rooms.
                // A MALFORMED file still stops here, though - Parse throws, and an authoring
                // mistake is a different thing from an absence.
                try
                {
                    TechnologyTable.Install(TechnologyTable.Parse(ContentLoader.Read("technology.txt")));
                }
                catch (FileNotFoundException)
                {
                    Debug.LogWarning("[era] no Content/technology.txt - the town stays in 1991.");
                }

                var layout = VillageParser.Parse(ContentLoader.Read(MapFile));
                World = WorldBuilder.Build(layout);

                var report = WorldValidator.Validate(World);
                foreach (var problem in report.Errors) Debug.LogError("village.txt: " + problem);
                foreach (var warning in report.Warnings) Debug.LogWarning("village.txt: " + warning);

                var names = NameTable.Parse(ContentLoader.Read("names.txt"));
                Particulars = ParticularsTable.Parse(ContentLoader.Read("particulars.txt"));
                People = PopulationGenerator.Generate(World, names, Particulars, Seed);

                // WAS SIX IN THE MORNING - "the village is about to wake up, the most
                // interesting minute of the day to arrive at" - and that is a good instinct for a
                // finished game and a bad one for a project being looked at fifty times a day.
                // Six is BEFORE SUNRISE. Pressing Play showed a black screen with nothing on it,
                // which is not a bug and looks exactly like one: the ground, the terrain work and
                // the zoning textures were all there and all unlit, and the honest report was "I
                // clicked Play and saw black".
                //
                // Noon while the world is being built. Nothing about the simulation depends on
                // the opening hour - the digit keys already skip to 06:00, 08:00, 12:00, 17:00,
                // 20:00 and 23:00, so arriving at dawn is one keypress away and arriving at a lit
                // world is the default. Put it back to 6 * 60 when the game is being played
                // rather than inspected.
                Sim = new Simulation(World, People, Seed, startMinuteOfDay: 12 * 60);

                Debug.Log($"Ashcombe: {World.Width}×{World.Height}, {World.PlaceCount} places, "
                        + $"{People.Count} people in {People.HouseholdCount} households.");
            }
            catch (Exception ex)
            {
                LoadError = ex.Message;
                Debug.LogError("Failed to load the village: " + ex);
                return;
            }

            // Before anything registers: the dictionaries would otherwise still hold roots from
            // the last build, all of them destroyed, and the first toggle would walk a list of
            // dead pointers.
            Layers.Clear();

            var profile = new BuildProfile();
            _village = VillageMesh.Build(World, transform, ShowBuildings);
            profile.Done("VillageMesh (ground, walls, roofs, frontage, furniture)");

            // The ground, roads and props are still drawn by the village renderer; only the
            // BUILDINGS are bought models. Nothing happens here for a map that has no city
            // kinds in it, so Ashcombe still builds exactly as it did.
            var city = new GameObject("City");
            city.transform.SetParent(_village.transform, false);
            // THE ROADS COME OUT TOO. Asphalt, kerbs, painted lanes, crossings, lay-bys and
            // the parking bays are all statements about how a road was BUILT, and this drawing
            // does not make those - it draws the corridor, which is the right of way, and lets
            // that be the road. Half a plan and half a model reads as neither.
            if (ShowBuildings)
            {
                // EACH ONE REGISTERED AS ITS OWN LAYER, and each baked separately further down.
                // The switch is on the root, so a layer can be taken away at runtime without a
                // rebuild - see Layers, which explains why the bake had to be split to allow it.
                Layers.Register(Layers.Kind.Streets, CityStreets.Build(World, city.transform));
                profile.Done("CityStreets");
                Layers.Register(Layers.Kind.Parking, CityParking.Build(World, city.transform));
                profile.Done("CityParking");
                Layers.Register(Layers.Kind.Signs, CitySigns.Build(World, city.transform));
                profile.Done("CitySigns");
            }
            // BUILDINGS OFF, LOT LINES ON. The Universal Pack holds exactly two house families
            // and both are Chicago brownstones - bay fronts, stoops, fire escapes - so a village
            // street built out of it is a street of the wrong country, not a rough approximation.
            // Until there is a kit that can build an Illinois frame house, the town draws its
            // FOOTPRINTS instead: 794 cadastral parcels from Vermilion County's own records, the
            // real lot lines, which is the half of the information we actually have.
            //
            // It is also the only way to judge the geometry. Whether the blocks are the right
            // size and the setbacks read as a street is answerable from a plan and unanswerable
            // from behind a wall of the wrong building.
            GameObject authored = null, blocks = null, estates = null;
            if (ShowBuildings)
            {
                authored = CityBuildings.Build(World, city.transform);
                blocks = CityDistrict.Build(World, city.transform);
                estates = CitySuburb.Build(World, city.transform);

                Layers.Register(Layers.Kind.Buildings, authored);
                Layers.Register(Layers.Kind.Districts, blocks);
                Layers.Register(Layers.Kind.Houses, estates);
            }
            // Built AFTER the bake and outside it, further down - CityChunker combines every
            // renderer under `city` into a handful of meshes and destroys the originals, and a
            // plan that has been merged into the terrain is a plan you cannot see.
            // EVERYTHING THAT DRESSES THE TOWN comes out with the buildings. Trees, poles,
            // farm clutter and the story props are all scenery, and scenery on a plan is
            // scenery ON TOP OF the thing you are trying to read - a block of forty trees
            // hides forty lot boundaries.
            if (ShowBuildings)
            {
                Layers.Register(Layers.Kind.Story, CityStory.Build(World, city.transform));
                profile.Done("CityStory");
                Layers.Register(Layers.Kind.Rail, CityRail.Build(World, city.transform));
                profile.Done("CityRail");

                // The real CSX line, at grade. Separate from CityRail above, which is the
                // elevated El and is gated on a `place railway` city.txt does not have - see
                // CityRailBed. This one needs no place: it follows the surveyed alignment in
                // features.txt, which is the same line the survey plan draws.
                Layers.Register(Layers.Kind.RailBed, CityRailBed.Build(World, city.transform));
                profile.Done("CityRailBed");
                Layers.Register(Layers.Kind.Farm, CityFarm.Build(World, city.transform));
                profile.Done("CityFarm");
                Layers.Register(Layers.Kind.Powerlines, CityPowerlines.Build(World, city.transform));
                profile.Done("CityPowerlines");
                Layers.Register(Layers.Kind.Trees, CityGreenery.Build(World, city.transform));
                profile.Done("CityGreenery");
            }

            // BEFORE the bake, because it measures the buildings the bake is about to destroy,
            // and parented outside the node the bake touches so it survives. Nothing else in the
            // project raycasts against this - picking still walks the world model - it exists so
            // a person has a floor and the bank has walls.
            CityCollision.Build(World, transform, authored, blocks, estates);
            profile.Done("CityCollision");

            // Assembled out of pieces, drawn as a handful of meshes.
            //
            // PER LAYER, NOT OVER THE WHOLE CITY, and that is the whole reason the layer
            // switches can exist. CityChunker combines every renderer under the node it is
            // given and DestroyImmediates the originals - so one bake over `city` turned the
            // trees and the walls into the same mesh and left nothing to switch off. Baking
            // each layer's own root keeps that root alive with its own Baked child underneath.
            //
            // It costs draw calls: two layers sharing a material no longer merge, so the same
            // brick appears in the buildings' chunk and again in the districts'. Chunking WITHIN
            // a layer is where nearly all of the win was - 18,059 renderers to 7,635 - and that
            // is untouched. Anything left unregistered is baked with the city as before.
            // AND NOTHING BAKES `city` ITSELF AFTERWARDS. That was the first version and it
            // silently undid the whole thing: `city` is the PARENT of every layer root, so
            // baking it walked back over the meshes the layer bakes had just made, combined
            // them into one node and destroyed the roots - leaving the switches pointing at
            // nothing. The renders proved it: with the trees switched off, the picture came
            // back byte-identical.
            //
            // It was also slower. Re-combining already-combined meshes by chunk and material
            // turned 10,226 layer meshes into 7,796 worse ones.
            foreach (var kind in Layers.All)
                foreach (var root in Layers.RootsOf(kind))
                    CityChunker.Bake(root);
            profile.Done("CityChunker.Bake (all layers)");

            // Anything parented to `city` that no layer claimed is left unbaked on purpose - it
            // would be invisible to the switches, and a renderer nobody can turn off is worth
            // knowing about rather than quietly merging away.
            int orphans = 0;
            foreach (var r in city.GetComponentsInChildren<MeshRenderer>(true))
                if (r.transform.parent == city.transform) orphans++;
            if (orphans > 0)
                Debug.LogWarning($"[layers] {orphans} renderers sit directly under the city and "
                               + "belong to no layer, so nothing can switch them off.");

            // AFTER the bake and OUTSIDE the node it bakes: a combined mesh cannot move or
            // change colour, so anything that drives - or that goes red and green - has to be
            // built once the static city is already frozen.
            // THE PARCEL LINES ARE AN OVERLAY, NOT AN ALTERNATIVE. This used to sit inside the
            // `if (!ShowBuildings)` below, so the county's own lot lines and the town that stands
            // on them could never be on screen together - which is backwards for the one question
            // they answer best: IS THIS HOUSE WHERE THE PARCEL SAYS IT IS. Now it is built either
            // way and switched with Layers.Kind.Plan like everything else.
            Layers.Register(Layers.Kind.Plan,
                            CityOutlines.Build(World, transform, ShowPlanRoads, ShowPlanFootprints));

            if (!ShowBuildings)
            {
                // The names, without which the drawing is anonymous: every line in it is right
                // and none of it is legible to somebody standing in the street.
                PlanLabels.Create(this, transform);

                // The one thing a baked mesh cannot do for itself - a selection changes on
                // every click, and CityOutlines is built once and frozen.
                SelectionHighlight.Create(this, transform);

                // What somebody who grew up here can add: who lived on a lot, and the shape of
                // the house if they can still picture it. AuthoredFootprints shows every one
                // that has been drawn; Footprint is the pen for drawing the next.
                AuthoredFootprints.Create(transform);
                Footprint = FootprintDrawer.Create(transform);
            }

            var signals = CitySignals.Create(World, transform);
            profile.Done("CitySignals");
            var traffic = CityTraffic.Create(World, transform, signals);
            profile.Done("CityTraffic");

            // The two that MOVE. Registered like the rest, and switching them off hides them
            // exactly as HideActors always did - the cars keep driving their lanes and the
            // signals keep cycling underneath, because this decides what is drawn and nothing
            // else. Not baked: a combined mesh cannot move or change colour.
            if (signals != null) Layers.Register(Layers.Kind.Signals, signals.gameObject);
            if (traffic != null) Layers.Register(Layers.Kind.Traffic, traffic.gameObject);

            // DRAWN OR NOT, THEY STILL EXIST. Gating CityTraffic.Create itself on the plan flag
            // was the obvious way to keep cars off a survey drawing and it was wrong twice over:
            // the flag is read inside Awake, so a test cannot set it in time and eight of the
            // thirteen simply hung waiting for a CityTraffic that was never coming - and more
            // importantly the traffic is a SIMULATION. Signals cycle, lanes are walked, jams
            // happen, whether or not anybody is drawing a van.
            //
            // So everything is built exactly as it always was and the renderers are switched
            // off. Same distinction ShowPeople has always drawn: simulated either way, this only
            // decides whether they are on screen.
            _xray = XRay.Create(World, _village);
            profile.Done("XRay");

            // The people are SIMULATED either way - Sim ticks, they go to work, windows light
            // from who is behind them, and clicking a building still says who is in it. This
            // only decides whether they are DRAWN. Turned off while the city itself is being
            // built out: a few hundred figures walking through a downtown that is still being
            // laid is noise over the thing actually being looked at.
            if (ShowPeople) _agentView = AgentMeshView.Create(this, transform);
            profile.Done("AgentMeshView (the people)");
            if (_agentView != null) Layers.Register(Layers.Kind.People, _agentView.gameObject);

            _rig = OrbitCamera.Create(this);

            // P drops you into the town at eye height with a body, and P again lifts you back
            // out. Nothing is spawned until the first press: a rigged character standing in the
            // street costs nothing to nobody who never asks for it.
            Player.Create(this, transform);
            profile.Done("Player");
            _lighting = SunRig.Create(this, transform);
            profile.Done("SunRig");

            // A callback rather than a root: the lamps and glazing are renderers scattered
            // through the town's own meshes, so there is no node to switch off. The lights
            // themselves keep burning either way, which is the same distinction everywhere else
            // here draws - this decides what is DRAWN.
            if (_lighting != null)
                Layers.Register(Layers.Kind.Lamps, _lighting.SetFixtureRenderers);

            // The switches, on screen. L opens them.
            LayerPanel.Create(transform);

            // AFTER the people AND the lights exist, which is the whole of the bug this line
            // used to have. It sat above CityTraffic.Create's block, where _agentView was still
            // null, and hid nothing of SunRig's lamp posts because SunRig did not exist yet
            // either - so the plan had a crowd on it once, and lamp posts standing over an
            // empty road corridor after that.
            if (!ShowBuildings) HideActors();

            PostFx.Create(transform);
            profile.Done("PostFx");
            PostFx.EnableOn(Camera.main);

            // The church bell, the ambience beds and footsteps by surface. Atmosphere is half
            // sound, and until now the village made none at all.
            VillageAudio.Create(this, transform);
            profile.Done("VillageAudio");

            profile.Report();
        }

        /// <summary>
        /// Guarantees there is a camera, whatever scene we were launched into. Pressing Play in
        /// an empty untitled scene is the normal case here, and an empty scene has no camera —
        /// which renders as Unity's default blue and looks identical to "nothing worked".
        /// </summary>
        private static void EnsureCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var existing = FindFirstObjectByType<Camera>();
                if (existing != null) { cam = existing; cam.tag = "MainCamera"; }
            }
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(0x11, 0x14, 0x1A, 0xFF);
        }

        /// <summary>The hours the skip buttons and the number keys jump to.</summary>
        public static readonly int[] SkipHours = { 6, 8, 12, 17, 20, 23 };

        /// <summary>
        /// Keyboard for everything the top bar does.
        ///
        /// Hitting a small button with the mouse is a poor way to drive time, especially while
        /// the other hand is on WASD walking down a street.
        /// </summary>
        private void HandleHotkeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // 1-6 jump to the skip hours, in the same order as the buttons.
            var digits = new[]
            {
                keyboard.digit1Key, keyboard.digit2Key, keyboard.digit3Key,
                keyboard.digit4Key, keyboard.digit5Key, keyboard.digit6Key,
            };
            for (int i = 0; i < digits.Length && i < SkipHours.Length; i++)
                if (digits[i].wasPressedThisFrame) { SkipToHour(SkipHours[i]); return; }

            // [ and ] step the speed; space pauses and resumes.
            if (keyboard.leftBracketKey.wasPressedThisFrame)
                SpeedIndex = Mathf.Max(0, SpeedIndex - 1);
            if (keyboard.rightBracketKey.wasPressedThisFrame)
                SpeedIndex = Mathf.Min(Speeds.Length - 1, SpeedIndex + 1);

            // X takes the buildings away, so you can watch all hundred and twelve at once
            // instead of the four who happen to be outdoors. A looking tool; it changes nothing
            // the simulation can see.
            if (keyboard.xKey.wasPressedThisFrame && _xray != null) _xray.Toggle();

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                if (SpeedIndex == 0) SpeedIndex = _speedBeforePause;
                else { _speedBeforePause = SpeedIndex; SpeedIndex = 0; }
            }
        }

        private int _speedBeforePause = 5;

        /// <summary>
        /// Turn off every renderer on the things that move, leaving them running underneath.
        ///
        /// Cheaper and far safer than not building them: a hidden car still drives its lane, is
        /// still counted by the jam instrument, and still occupies the space in front of the car
        /// behind it, so nothing about the town's behaviour changes when you switch the drawing
        /// off. A plan is a way of LOOKING at Northgate, not a different Northgate.
        /// </summary>
        private void HideActors()
        {
            // FOUND BY TYPE, not off fields, so this cannot silently half-work again if the
            // build order moves. A field that happens to be null at the moment this is called
            // hides nothing and says nothing about it.
            int off = 0;
            foreach (var view in GetComponentsInChildren<AgentMeshView>(true))
                foreach (var r in view.GetComponentsInChildren<Renderer>(true)) { r.enabled = false; off++; }

            foreach (var traffic in GetComponentsInChildren<CityTraffic>(true))
                foreach (var r in traffic.GetComponentsInChildren<Renderer>(true)) { r.enabled = false; off++; }

            // The signal heads and their posts. Simulated either way - the lights still cycle,
            // which is what CityTraffic reads to decide who has right of way - but a lamp post
            // standing over an empty road corridor is exactly the kind of thing this plan is
            // supposed to have removed.
            foreach (var signals in GetComponentsInChildren<CitySignals>(true))
            {
                foreach (var r in signals.GetComponentsInChildren<Renderer>(true)) { r.enabled = false; off++; }

                // The "Lamp_Emission" point light at each head, for the pool of colour it throws
                // on the tarmac - a renderer disables the mesh but not this, so the red and
                // green pools kept glowing over an otherwise empty road corridor.
                foreach (var light in signals.GetComponentsInChildren<Light>(true)) { light.enabled = false; off++; }
            }

            // Lamp posts, window panes and glazing - SunRig's own fixtures, which draw
            // regardless of ShowBuildings because the night-lighting test needs them whether or
            // not anybody is looking at a built town. A plan has no use for a lamp post.
            if (_lighting != null) _lighting.HideFixtureRenderers();

            if (off == 0)
                Debug.LogWarning("[plan] nothing was hidden - people and traffic will be drawn "
                               + "on the plan. Has the build order moved?");
            Debug.Log($"[plan] {off} renderers hidden - people and traffic still running.");
        }

        private void Update()
        {
            if (Sim == null) return;

            HandleHotkeys();

            // Drain a queued skip first, a frame's worth at a time.
            if (_skipTicksRemaining > 0)
            {
                int chunk = (int)Mathf.Min(_skipTicksRemaining, MaxTicksPerFrame);
                Sim.Tick(chunk);
                _skipTicksRemaining -= chunk;
                if (_agentView != null) _agentView.Refresh();
                _rig.Tick();
                return;
            }

            float speed = Speeds[Mathf.Clamp(SpeedIndex, 0, Speeds.Length - 1)];
            if (speed > 0f)
            {
                _tickAccumulator += Time.unscaledDeltaTime * speed * GameClock.TicksPerSecond;
                int ticks = (int)_tickAccumulator;
                if (ticks > 0)
                {
                    _tickAccumulator -= ticks;
                    if (ticks > MaxTicksPerFrame) ticks = MaxTicksPerFrame;
                    Sim.Tick(ticks);
                }
            }

            if (_agentView != null) _agentView.Refresh();
            _rig.Tick();
        }

        private long _skipTicksRemaining;

        /// <summary>
        /// Jump the clock forward to the next occurrence of an hour.
        ///
        /// Queued rather than run inline: skipping a whole day is 1,728,000 ticks, which took
        /// the better part of a second in a Release build and several times that under Mono.
        /// Running it inside one Update would freeze the window and look like a crash. The
        /// queue is drained through the same per-frame ceiling everything else uses, so a skip
        /// costs a few dropped frames instead of a hang.
        /// </summary>
        public void SkipToHour(int hour)
        {
            if (Sim == null) return;
            int minutes = Sim.Clock.MinutesUntil(hour * 60);
            if (minutes == 0) minutes = 1440;
            _skipTicksRemaining = (long)minutes * GameClock.TicksPerMinute;
            _tickAccumulator = 0;
        }

        public bool Skipping => _skipTicksRemaining > 0;

        public Citizen SelectedCitizen => Selected.IsValid ? People.Get(Selected) : null;

        /// <summary>Which camera mode is active, for the HUD.</summary>
        public string ViewName =>
            _rig != null && _rig.Mode == OrbitCamera.ViewMode.Street ? "street level" : "overview";
    }
}
