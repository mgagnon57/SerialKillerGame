using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Sim;

// THE ONE FILE IN THE GAME THAT NAMES THIS ASSEMBLY. WitnessFirewallTests holds the line and
// this is the exception it was written to expect: the caller that asks the observation system a
// question. It records where the player was; nothing else in Assets may reference Witness, and
// the test names this file rather than allowing a pattern.
using Noir.Core.Witness;
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

        /// <summary>
        /// WHERE THE PLAYER HAS BEEN, one entry a minute. The simulation's only genuine history.
        ///
        /// Everybody else is recomputable - DayPlanner.Plan is a pure function of (seed, citizen,
        /// day), so any villager's past can be replayed and none of it needs storing. The player
        /// has no plan and no key, so this is the one thing that has to be written down, and
        /// Recollection.WhatTheySaw cannot answer anything without it.
        ///
        /// It is the missing link the observation system has been waiting on: the machinery is
        /// built, tested and firewalled, and has never run because nothing recorded this.
        /// </summary>
        public PlayerTrack Track { get; } = new PlayerTrack();

        private Player _player;

        /// <summary>The last minute written to the track, so a frame does not write it twice.</summary>
        private int _lastTrackedMinute = -1;

        /// <summary>Where the body was last frame, and the fastest it has moved since the last
        /// entry - both only so `Visibly.Quickly` can be answered from something real.</summary>
        private Vector3? _trackedFrom;
        private float _fastestSinceEntry;
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

        /// <summary>
        /// The lot under the cursor right now, or null when the cursor is off the map or over a
        /// panel. Rewritten every frame by OrbitCamera; nothing else should set it.
        ///
        /// Separate from SelectedParcel on purpose. Hovering is a question - "what is this one?"
        /// - and clicking is a commitment. A hover that quietly became a selection would replace
        /// whatever you had open just by moving the mouse across the map to reach it.
        /// </summary>
        public ParcelIndex.Parcel? HoveredParcel { get; set; }

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
        /// How long the simulation is allowed to hold the frame, in milliseconds.
        ///
        /// A tick over the whole town costs about 5 ms under Mono in the editor, and the
        /// accumulator asks for speed x 20 of them per second of real time. Multiply those out
        /// and the feedback of one frame on the next is exactly speed x 0.1: harmless at 1x,
        /// EXACTLY ONE at the default 10x, and explosive at 60x. Unity gain is the worst place
        /// to sit - a stall neither grows nor decays, so a single hiccup sustains itself for as
        /// long as it likes. Measured: 38 stalls in a minute at 10x, the worst 3.5 seconds,
        /// every one of them frame time = ticks x 5 ms to within one percent.
        ///
        /// MaxTicksPerFrame cannot prevent this. At 24,000 it is sized for fast-forwarding a
        /// whole day, and the worst stall measured ran 696 ticks - nowhere near it. A count
        /// cannot bound a frame when the cost per tick is what varies; only time can.
        ///
        /// So the frame gets a slice and the simulation keeps whatever it can finish inside it.
        /// Six milliseconds leaves room for a 60 fps frame with the renderer's share intact.
        /// </summary>
        private const double SimBudgetMs = 6.0;

        /// <summary>
        /// Ticks between budget checks. The clock is not free to read, and checking it after
        /// every single tick would cost more than the tick. Eight is about 40 ms of overshoot
        /// in the worst case, which is a dropped frame rather than a freeze.
        /// </summary>
        private const int TickChunk = 8;

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

        /// <summary>
        /// Whether the GROUND paints a building's slab and walls, or just the land underneath.
        ///
        /// WHY IT IS OFF. Buildings are drawn twice: once as objects, which the layer panel can
        /// switch off, and once as `Wall` and `Floor` TERRAIN baked into the ground mesh - 19,722
        /// and 23,151 tiles of it. The second copy has no switch, so asking for "roads and lot
        /// lines only" still produced a map covered in building-coloured rectangles, and there was
        /// no way to see whether a street was on its right of way underneath them.
        ///
        /// It is off while the ROADS and PARCELS are being brought into agreement, on the owner's
        /// direction: houses are authored at positions inherited from the old wrong roads, so they
        /// are not evidence about anything until they are re-derived. Turn it back on then.
        ///
        /// Read at BUILD time, like ShowBuildings - the ground is one baked mesh, so this cannot
        /// be a runtime layer toggle. It takes effect on the next Play.
        /// </summary>
        public static bool ShowBuildingFootprints = false;

        /// <summary>
        /// Paint the whole map one green, water excepted.
        ///
        /// FLAT COLOUR, NOT FLAT LAND. The elevation grid is untouched and the ground keeps every
        /// foot of its relief - the owner was explicit: "when I say flat I do not want to lose the
        /// elevation". What goes is the PAINT: field against town grass, the GroundZoning scatter
        /// that reads as bushes strewn over the map, wooded ground, the dark paved yards inside
        /// blocks, the churchyard, and road terrain.
        ///
        /// This is a drawing to JUDGE STREET ALIGNMENT by. Six kinds of ground texture and a
        /// scatter of detail are honest about what the land is and are noise against the one
        /// question being asked, which is whether a centreline sits on its right of way.
        /// </summary>
        public static bool FlatGroundColour = true;

        /// <summary>
        /// Draw each road as a thin line down its middle instead of laying real road tiles.
        ///
        /// A 33 ft band of textured asphalt covers the lot lines it is supposed to be judged
        /// against, and CityStreets' junction pieces snap square, so a road that is slightly off
        /// its right of way reads as a road that is fine. The centreline hides nothing.
        ///
        /// Turn it off to get the built streets back - see RoadCentrelines.
        /// </summary>
        public static bool RoadsAsCentrelines = true;

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

                // The roads in city.txt are DERIVED, not authored - SOURCES-OF-TRUTH.md is
                // explicit that when they disagree with the parcels the parcels win and the
                // road gets moved. Content/roads.txt is that move, done from survey rather than
                // by sliding a ruled line sideways. No-ops if the file is not there.
                SurveyRoads.Apply(layout);

                // The buildings on lots the owner ruled had none in 1991. Same reasoning as the
                // roads above and the same seam: decided on the layout, once, so everything that
                // walks AllPlaces afterwards sees one town.
                RuledAway.Apply(layout);

                // And what is left stands where it was measured standing, at the size it was
                // measured being - rather than on the generator's 13x7 box. After RuledAway, so
                // nothing is carefully seated and then taken down again.
                SeatOnSurvey.Apply(layout);

                // And the buildings the survey found that the map never had. Last, so it can see
                // everything already standing and put nothing up on top of it.
                FillFromSurvey.Apply(layout);

                World = WorldBuilder.Build(layout);

                var report = WorldValidator.Validate(World);
                // NAMED FROM MapFile, not written out. These said "village.txt:" while the game
                // has loaded city.txt for months, and an error that names the wrong file sends
                // whoever reads it to go and look at the wrong one - which it did.
                foreach (var problem in report.Errors) Debug.LogError(MapFile + ": " + problem);
                foreach (var warning in report.Warnings) Debug.LogWarning(MapFile + ": " + warning);

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
            // TRUE, NOT ShowBuildings. That flag is about the BOUGHT prefabs - the Universal Pack's
            // Chicago brownstones, which are the wrong country for an Illinois village and are off
            // for that reason. The generated massing is the town this project actually draws, and
            // tying it to the same flag meant the Generated massing switch had nothing behind it
            // and could never light a single house however many times it was clicked. What gets
            // built is the layer's decision now, and it is made lazily.
            _village = VillageMesh.Build(World, transform, showDressing: true);
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
                // Alleys come back on their own root either way, so they can be switched off
                // while the streets are being judged - see Layers.Kind.Alleys.
                GameObject alleyRoot;
                if (RoadsAsCentrelines)
                {
                    Layers.Register(Layers.Kind.Streets,
                                    RoadCentrelines.Build(World, city.transform, out alleyRoot));
                    Layers.Register(Layers.Kind.Alleys, alleyRoot);
                    profile.Done("RoadCentrelines");
                }
                else
                {
                    Layers.Register(Layers.Kind.Streets,
                                    CityStreets.Build(World, city.transform, out alleyRoot));
                    Layers.Register(Layers.Kind.Alleys, alleyRoot);
                    profile.Done("CityStreets");
                }

                // PARKING AND SIGNS EITHER WAY. They were built only alongside the street kit, so
                // in centreline mode - which is how Rossville is drawn - both switches sat in the
                // panel looking exactly like the working ones and did nothing when clicked.
                // Neither needs the asphalt: both are placed from the road network, which is there
                // in both modes. Lazy, so the centreline view does not pay for them unless asked.
                var roads = city.transform;
                Layers.RegisterLazy(Layers.Kind.Parking, () => CityParking.Build(World, roads));
                profile.Done("CityParking");
                Layers.RegisterLazy(Layers.Kind.Signs, () => CitySigns.Build(World, roads));
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
            // NOT GATED ON ShowBuildings, ANY OF THEM. That flag is about the bought prefabs -
            // the Universal Pack's Chicago brownstones, which are the wrong country for an
            // Illinois village and are off for that reason. None of the six below is a bought
            // house: the CSX line follows the survey, the story props are the game's own, and the
            // farm, poles and trees are generated. Tying them to the same flag left six switches
            // in the panel that ticked and did nothing, forever, and no way to find out why.
            //
            // SKIPPED WHEN SWITCHED OFF, AND BUILT THE MOMENT ONE IS ASKED FOR. Building them to
            // hide them cost 17,405 farm pieces, 12,804 trees and 789 poles nothing would ever
            // see, which on a survey view is the whole frame budget. Skipping them used to mean
            // switching one back on did nothing until you left Play and came back - a switch that
            // needs the game restarted is not a switch. See Layers.RegisterLazy: the saving stays
            // and the restart goes.
            var cityRoot = city.transform;
            Layers.RegisterLazy(Layers.Kind.Story, () => CityStory.Build(World, cityRoot));
            profile.Done("CityStory");

            // The real CSX line, at grade. Separate from Kind.Rail, which is the elevated El and
            // needs a `place railway` city.txt does not have - that one is off the panel entirely
            // now, see Layers.All. This one needs no place: it follows the surveyed alignment in
            // features.txt, the same line the survey plan draws.
            Layers.RegisterLazy(Layers.Kind.RailBed, () => CityRailBed.Build(World, cityRoot));
            profile.Done("CityRailBed");

            Layers.RegisterLazy(Layers.Kind.Farm, () => CityFarm.Build(World, cityRoot));
            profile.Done("CityFarm");
            Layers.RegisterLazy(Layers.Kind.Powerlines, () => CityPowerlines.Build(World, cityRoot));
            profile.Done("CityPowerlines");
            Layers.RegisterLazy(Layers.Kind.Trees, () => CityGreenery.Build(World, cityRoot));
            profile.Done("CityGreenery");

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
            // A dead root is SKIPPED, not walked into. Bake destroys the originals it consumes,
            // so a root that carried its own renderer destroys itself - and one throw here aborts
            // Awake half way and leaves a black screen with the town built but no camera. The
            // renderer-on-a-child rule is the real fix (see RoadCentrelines); this is so the next
            // one costs a log line instead of the whole session.
            foreach (var kind in Layers.All)
                foreach (var root in Layers.RootsOf(kind))
                {
                    if (root == null)
                    {
                        Debug.LogWarning($"[layers] the '{Layers.Label(kind)}' root was destroyed "
                                       + "before the bake reached it - its renderer is on the root "
                                       + "itself, and CityChunker.Bake removes what it consumes.");
                        continue;
                    }
                    CityChunker.Bake(root);
                }
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
                            CityOutlines.Build(World, transform, ShowPlanRoads));

            // ON ITS OWN ROOT AND ITS OWN SWITCH. The lot lines say where the boundary is and
            // these say where the house is; the useful view is the second over the first, and
            // while they shared a mesh there was one switch for both.
            Layers.Register(Layers.Kind.Footprints,
                            CityOutlines.BuildFootprints(World, transform));

            // THE NAMES GO ON EITHER WAY. Like the parcel lines above, these were built only in
            // survey-plan mode - so the built town, which is what anybody actually looks at, was
            // the one view with no street names in it. That is backwards: a name is more useful
            // over a town than over a drawing, because the drawing has nothing else to confuse it
            // with. Switched with Layers.Kind.Labels.
            Layers.Register(Layers.Kind.Labels, PlanLabels.Create(this, transform).gameObject);

            // THE MARKS GO ON EITHER WAY, for exactly the reason the lot lines and the street
            // names above do. These lived inside `if (!ShowBuildings)` and so did not exist in
            // the built town - which is the view most of the time is spent in, and the view where
            // the buildings can be switched off by LAYER while ShowBuildings is still true. In
            // that state the map looks like a survey plan, hovering a lot did nothing at all, and
            // there was no error to explain why. Pointing at a lot is a question worth answering
            // over a town as much as over a drawing.
            //
            // The one thing a baked mesh cannot do for itself: a selection changes on every
            // click, and CityOutlines is built once and frozen.
            SelectionHighlight.Create(this, transform);

            // AFTER THE GROUND IS BAKED, and that matters: it snapshots what the baked mesh is
            // showing per lot, so it can draw only the ones you have re-zoned since.
            ZoningPatch.Create(this, transform);
            HoverHighlight.Create(this, transform);

            if (!ShowBuildings)
            {
                // What somebody who grew up here can add: who lived on a lot, and the shape of
                // the house if they can still picture it. AuthoredFootprints shows every one
                // that has been drawn; Footprint is the pen for drawing the next.
                AuthoredFootprints.Create(transform);
                Footprint = FootprintDrawer.Create(transform);
            }

            // SKIPPED WHEN BOTH LAYERS ARE OFF, and this is a deliberate reversal of the note
            // below - which is kept because its reasoning was sound for the case it was written
            // about, and this is a different case.
            //
            // The owner's report was "runs smooth for the first 5 seconds then goes to shit",
            // and that shape is not a startup cost - it is something RAMPING UP. 88 vehicles
            // begin mid-block, where they are cheap, and within a few seconds they are all at
            // junctions, where the solver has 2,766 conflicting turn pairs over 1,218 turns to
            // resolve. CityTraffic.Update drives up to twelve 1/30 s slices per frame off
            // Time.deltaTime, and it does that whether or not a single car is drawn.
            //
            // Gating it on the LAYERS rather than on a plan flag is what makes this safe where
            // the earlier attempt was not: the layer state is read from PlayerPrefs before Awake
            // runs, so a test or a preset can set it and be obeyed.
            CitySignals signals = null;
            CityTraffic traffic = null;

            // ALWAYS BUILT. Not lazily, and not skipped - and this is the one place in the panel
            // where that is the right answer.
            //
            // These two were skipped when both layers were off, which saved solving 88 vehicles
            // and 2,766 junction conflict pairs a frame, and cost a restart to undo. Making them
            // lazy like the trees looked obvious and is wrong twice: the traffic is a SIMULATION,
            // not scenery - signals cycle, lanes are walked, jams happen whether or not anybody
            // is drawing a van - and building it on a switch means the town's history depends on
            // when somebody clicked. It also breaks every test that waits for a CityTraffic to
            // exist, which is most of them.
            //
            // So the layer decides what is DRAWN and nothing else, exactly as the paragraph below
            // has always said it should, and the switch works without a restart because there is
            // nothing left to build.
            signals = CitySignals.Create(World, transform);
            profile.Done("CitySignals");
            traffic = CityTraffic.Create(World, transform, signals);
            profile.Done("CityTraffic");

            // The two that MOVE. Registered like the rest, and switching them off hides them
            // exactly as HideActors always did. Not baked: a combined mesh cannot move or change
            // colour.
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
            // The People LAYER is honoured here too, for the same reason the scenery builders
            // are: 763 bought, animated figures are built and skinned every frame whether or not
            // anything draws them, and on a survey view that is a large slice of the budget spent
            // on people who are switched off. The simulation is untouched either way.
            // Lazily, so switching the people on is a click rather than a restart. The simulation
            // is untouched either way - this is the 763 bought, animated, skinned figures, not the
            // citizens, who go to work whether or not anybody is drawing them.
            if (ShowPeople)
                Layers.RegisterLazy(Layers.Kind.People, () =>
                {
                    _agentView = AgentMeshView.Create(this, transform);
                    return _agentView != null ? _agentView.gameObject : null;
                });
            profile.Done("AgentMeshView (the people)");

            // THE CURTAIN. Up before anything can be mistaken for the game running, down only
            // when the frames have actually settled - and it holds the clock while it is up, so
            // nothing has quietly started behind it. See BootScreen.
            BootScreen.Phase = "Loading assets and shaders";
            BootScreen.Create(this, transform);

            // EVERY SHADER THE TOWN NEEDS, COMPILED NOW, before a single frame is presented.
            // Otherwise each variant compiles the first time something needs to draw with it,
            // which stops the frame dead - and lands as a freeze three seconds into looking at
            // something rather than as an honest pause at startup. See ShaderWarmup.
            ShaderWarmup.Run(_village);

            // THE MAP OF WHO CAN REACH WHOM, worked out now for the same reason as the shaders.
            //
            // It is a flood fill of five million tiles and it costs about a third of a second.
            // Built lazily it lands on whichever frame happens to contain the first journey of
            // the day, which measured as a single 314 ms stall a minute into play - the one and
            // only stall left in a sixteen thousand frame run. Behind the curtain it costs
            // nothing anybody can see.
            if (Sim != null)
            {
                BootScreen.Phase = "Working out the walkable town";
                var regions = Sim.Regions;
                if (regions.RegionCount > 1)
                    Debug.LogWarning($"[regions] the walkable map is in {regions.RegionCount} "
                        + "pieces - somewhere is sealed off. Noir > Report Stranded People "
                        + "says where and who it strands.");
            }

            // Built before the camera, so a build that goes wrong further down still leaves a
            // frame counter on screen to diagnose it with.
            PerfHud.Create(this, transform);

            _rig = OrbitCamera.Create(this);

            // P drops you into the town at eye height with a body, and P again lifts you back
            // out. Nothing is spawned until the first press: a rigged character standing in the
            // street costs nothing to nobody who never asks for it.
            _player = Player.Create(this, transform);
            profile.Done("Player");
            _lighting = SunRig.Create(this, transform);
            profile.Done("SunRig");

            // A callback rather than a root: the lamps and glazing are renderers scattered
            // through the town's own meshes, so there is no node to switch off. The lights
            // themselves keep burning either way, which is the same distinction everywhere else
            // here draws - this decides what is DRAWN.
            if (_lighting != null)
            {
                Layers.Register(Layers.Kind.Lamps, _lighting.SetFixtureRenderers);

                // The houses' own windows follow the houses, not the street lighting - see
                // SunRig.SetWindowPanes. The glazing inside a bought building needs no switch of
                // its own at all: it is a child of the building, so the building's layer already
                // takes it away.
                Layers.Register(Layers.Kind.Massing, _lighting.SetWindowPanes);
            }

            // The switches, on screen. L opens them.
            LayerPanel.Create(transform);

            // Everything that is going to register has registered by here, so this is where a
            // switch with nothing behind it can be named. The panel marks the same ones.
            Layers.Audit();

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
            // NOT WHILE SOMEBODY IS TYPING. Space, x, the digits and the brackets are all bound
            // here, and 1-6 skip the clock to an hour - so entering a house number used to time
            // travel. See VillageUI.KeyboardCaptured.
            if (VillageUI.KeyboardCaptured) return;

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

            // Z paints the lots by what they are zoned for, and takes the legend with it.
            //
            // A RE-TINT, NOT A REBUILD. The ground is one baked mesh with a submesh per zoning,
            // so the geometry is already sorted by what each lot is for whether or not anybody is
            // looking at the colours - switching them off is six SetColor calls. Rebuilding five
            // million tiles to change a colour would stall the frame for seconds.
            if (keyboard.zKey.wasPressedThisFrame)
            {
                Materials3D.ShowZoningColours = !Materials3D.ShowZoningColours;
                Materials3D.RetintZoning();
            }

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

        /// <summary>
        /// Write down where the player was, once a sim minute, while they are in the body.
        ///
        /// ONCE A MINUTE, NOT ONCE A FRAME. Sighting.Minute is stamped in minutes and the sim
        /// runs at 20 Hz, so sampling per frame would make a track 1,200 times bigger than it
        /// needs to be and stamp most of it with the same number - entries fighting each other
        /// for one slot, and only the last surviving.
        ///
        /// NOTHING IS RECORDED WHILE ORBITING. A camera is not a body: nobody can see it, and a
        /// track of where the view was pointed is not evidence of anywhere a person stood.
        ///
        /// A skipped hour leaves a GAP rather than a run of identical entries. The player did not
        /// walk anywhere during it, and inventing minutes they were not present for is exactly
        /// what Recollection refuses to do for travelling villagers.
        /// </summary>
        private void RecordWhereThePlayerWas()
        {
            var at = _player == null ? null : _player.Where;
            if (at == null)
            {
                _lastTrackedMinute = -1;
                _trackedFrom = null;
                _fastestSinceEntry = 0f;
                return;
            }

            // How fast they are actually moving, watched every frame and remembered at its
            // highest until the minute is written. A single sample on the minute boundary would
            // catch whatever they happened to be doing at that instant and call it the minute.
            if (_trackedFrom.HasValue && Time.unscaledDeltaTime > 0f)
            {
                float metresPerSecond =
                    Vector3.Distance(at.Value, _trackedFrom.Value) / Time.unscaledDeltaTime;
                if (metresPerSecond > _fastestSinceEntry) _fastestSinceEntry = metresPerSecond;
            }
            _trackedFrom = at;

            int minute = Sim.Clock.Day * (GameClock.TicksPerDay / GameClock.TicksPerMinute)
                       + Sim.Clock.MinuteOfDay;
            if (minute == _lastTrackedMinute) return;
            if (minute < _lastTrackedMinute) return;   // a track runs forwards; PlayerTrack throws

            // ---- WHAT WAS VISIBLY TRUE, from things that are actually known ----
            //
            // Every flag has to be perceptible from across a road - that is Visibly's own rule.
            // Filled from real facts rather than passed as a constant, because this is the
            // player's own contribution to being seen, and a constant would make every walk
            // identical to every other.
            var looked = Visibly.Nothing;

            // Running rather than walking. A body moving at a pace is the thing a witness
            // notices first and remembers longest.
            if (_fastestSinceEntry > RunningPace) looked |= Visibly.Quickly;

            // Somebody else within a few metres. Not who - Visibly cannot say who, and neither
            // can the witness - only that they were not on their own.
            if (_agentView != null && _agentView.Pick(at.Value, InCompanyMetres).IsValid)
                looked |= Visibly.InCompany;

            // Visibly.Carrying is deliberately never set. There is nothing in the game to carry
            // yet, and a flag guessed at is worse than one left honest - this is evidence.

            Track.Record(minute, Space3D.TileAt(at.Value), looked);
            _lastTrackedMinute = minute;
            _fastestSinceEntry = 0f;
        }

        /// <summary>Above a walk. Starter Assets walks at 2 m/s and sprints at 5.3.</summary>
        private const float RunningPace = 3.2f;

        /// <summary>Near enough that a witness would say they were with somebody.</summary>
        private const float InCompanyMetres = 6f;

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

            // Timed in three parts because inference has repeatedly got this wrong. Capping the
            // tick count bounded the simulation and left the frame at 418 ms anyway, so the cost
            // is in one of the other two calls - and guessing which has not worked. These read
            // the clock three times a frame, which is nothing against what they are measuring.
            var slice = System.Diagnostics.Stopwatch.StartNew();

            float speed = Speeds[Mathf.Clamp(SpeedIndex, 0, Speeds.Length - 1)];
            if (speed > 0f)
            {
                _tickAccumulator += Time.unscaledDeltaTime * speed * GameClock.TicksPerSecond;
                int ticks = (int)_tickAccumulator;
                if (ticks > 0)
                {
                    _tickAccumulator -= ticks;
                    if (ticks > MaxTicksPerFrame) ticks = MaxTicksPerFrame;
                    RunWithinBudget(ticks);
                }
            }
            SimMs = slice.Elapsed.TotalMilliseconds;

            RecordWhereThePlayerWas();

            if (_agentView != null) _agentView.Refresh();
            RefreshMs = slice.Elapsed.TotalMilliseconds - SimMs;

            _rig.Tick();
            RigMs = slice.Elapsed.TotalMilliseconds - SimMs - RefreshMs;
        }

        /// <summary>What the last frame's simulation ticks cost, in milliseconds.</summary>
        public double SimMs { get; private set; }

        /// <summary>What the last frame's figure refresh cost, in milliseconds.</summary>
        public double RefreshMs { get; private set; }

        /// <summary>What the last frame's rig tick cost, in milliseconds.</summary>
        public double RigMs { get; private set; }

        /// <summary>
        /// Run up to <paramref name="ticks"/> simulation ticks, stopping when the frame's slice
        /// is spent, and report what could not be afforded.
        ///
        /// The unaffordable remainder is DROPPED, not carried: the accumulator was already
        /// drained by the caller. That is the same bargain the old tick ceiling struck - when
        /// the machine cannot keep up, the town's clock runs slow rather than the window
        /// freezing - and it is the right way round. A stopped picture with a correct clock is
        /// worth nothing to somebody trying to watch the place.
        /// </summary>
        private void RunWithinBudget(int ticks)
        {
            var slice = System.Diagnostics.Stopwatch.StartNew();
            int done = 0;

            // At least one chunk always runs, whatever the clock says. A budget that can starve
            // the simulation to a standstill would stop the town dead on a slow machine, which
            // is a worse bug than the one being fixed.
            do
            {
                int chunk = Mathf.Min(TickChunk, ticks - done);
                Sim.Tick(chunk);
                done += chunk;
            }
            while (done < ticks && slice.Elapsed.TotalMilliseconds < SimBudgetMs);

            TicksDropped = ticks - done;
            TicksRun = done;
        }

        /// <summary>Ticks the last frame asked for and could not afford. Zero when keeping up.</summary>
        public int TicksDropped { get; private set; }

        /// <summary>Ticks the last frame actually ran.</summary>
        public int TicksRun { get; private set; }

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
