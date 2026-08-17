using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Response;
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
        private PlayerInteraction _interaction;

        /// <summary>Whoever is walking about, if anybody is. Read by the testimony panel, which
        /// asks about where the player is STANDING - the answer is a function of where they have
        /// been, so it has to know where that is.</summary>
        public Player Player => _player;

        /// <summary>
        /// Whether the player is asking questions with a badge or as a passer-by. A TOGGLE
        /// (B) rather than a character, deliberately: how the badge is EARNED is a story
        /// decision the spec leaves open. Badge on = full testimony; off = the thinned
        /// stranger's version. Phase 2 makes the difference cut deeper — a civilian who asks
        /// gets REMEMBERED. Spec: docs/superpowers/specs/2026-08-17-witness-voices-design.md.
        /// </summary>
        public bool Badge;

        /// <summary>The street's floating one-liners; created in Awake beside the UI.</summary>
        private StreetVoices _voices;

        /// <summary>A gawker's line, keyed on the citizen id — the response path consumes no
        /// RNG, so the same person wonders the same thing every run of the same seed.</summary>
        private static string GawkLine(int who) => (who % 3) switch
        {
            0 => "what happened here?",
            1 => "is that— oh no.",
            _ => "who is it? can you see?",
        };

        /// <summary>The verb-menu framework that offers doors (and, later, whatever else)
        /// while the player is walking. Was write-only from here until now - nothing outside
        /// this file needed to read it yet, but a field nothing exposes is a field nothing can
        /// be tested or driven against from outside without a scene search.</summary>
        public PlayerInteraction Interaction => _interaction;

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
        /// Rossville is built from bought models placed on authored lots; the old village generated its
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
        /// <remarks>
        /// A PROPERTY RATHER THAN A FIELD ONLY SO THE GROUND CAN HEAR IT CHANGE. Materials3D bakes
        /// the plan-dimming decision when it builds a material and caches the material for the
        /// life of the domain, so an editor tool that sets this AFTER the palette exists - which
        /// is most of them, CityShot and GroundShot and HouseProto and LayerShot all do it - was
        /// rendering with the previous answer. Every one of the twelve assignments in the tree is
        /// a plain `=` and compiles unchanged; there is no `ref`, `out` or Interlocked use.
        /// </remarks>
        public static bool ShowBuildings
        {
            get => _showBuildings;
            set { _showBuildings = value; Materials3D.RefreshPlan(); }
        }

        /// <summary>
        /// The minute of the day the town wakes up at when Play begins. 20:00 - THE HOUR THE
        /// STREET LAMPS ARE ON, asked for by the owner so the lighting is what he lands in
        /// rather than something he has to go looking for. Not a guess: SunRig.NightLevel is
        /// InverseLerp(0.35, 0, sunIntensity) and SkyAt's curve crosses 0.35 at about 19:25, so
        /// at eight the lamps are lit and the sky still has colour in it - the only hour that
        /// shows BOTH the lamps and the town they stand in. The digit keys skip to 06:00, 08:00,
        /// 12:00, 17:00, 20:00 and 23:00, so noon is one keypress away.
        ///
        /// A FIELD AND NOT A LITERAL, BECAUSE THE OPENING HOUR BROKE THE GATE THE NIGHT IT
        /// MOVED. The commit that set 20:00 said "nothing in the simulation depends on the
        /// opening hour" - and nothing does, but four TESTS did, and the first PlayMode run
        /// afterwards went 4 red with every one tracing back to the clock. The animation sweep
        /// starts at the next whole hour and cannot rewind, so from a 20:00 start its range came
        /// out "21:00 to 17:00" and the loop body never ran once - "nobody walked all day". The
        /// driveways test wants the commuters still OUT, and DayPlan brought them home at 17:10.
        /// The two traffic gates ran against an evening fleet the suite had never once been
        /// measured on. CityUnderTest sets this back to noon for the batch suite - the town the
        /// 19-of-19 baseline was measured on - and the [gate] line says which hour it opened at.
        /// </summary>
        public static int OpeningMinuteOfDay = 20 * 60;

        private static bool _showBuildings;

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
        /// TURNED OFF 2026-08-08. This is a survey instrument and it was left switched on, so
        /// pressing Play showed a flat green sheet with line-art roads on it rather than the town.
        /// It is the default a demo runs on. See RoadCentrelines below - the same fault.
        /// <remarks>
        /// Same treatment as <see cref="ShowBuildings"/>, with one caveat worth stating: only the
        /// MATERIAL half follows a live change. The MESH half is read at build time (VillageMesh
        /// chooses which submesh a tile goes in), so flipping this after a build recolours the
        /// ground without re-zoning it.
        /// </remarks>
        public static bool FlatGroundColour
        {
            get => _flatGroundColour;
            set { _flatGroundColour = value; Materials3D.RefreshPlan(); }
        }

        private static bool _flatGroundColour;

        /// <summary>
        /// Draw each road as a thin line down its middle instead of laying real road tiles.
        ///
        /// A 33 ft band of textured asphalt covers the lot lines it is supposed to be judged
        /// against, and CityStreets' junction pieces snap square, so a road that is slightly off
        /// its right of way reads as a road that is fine. The centreline hides nothing.
        ///
        /// Turn it off to get the built streets back - see RoadCentrelines.
        /// </summary>
        /// TURNED OFF 2026-08-08, with FlatGroundColour. Between them these two were the reason
        /// the game did not look like Rossville on Play: no paved streets, no ground texture, no
        /// junction pieces. Neither had a key, a menu tick or a preference - the only way to find
        /// them switched on was to read this file, which is why they stayed on for weeks.
        public static bool RoadsAsCentrelines = false;

        private GameObject _village;
        private XRay _xray;

        /// <summary>The cars standing at houses. See <see cref="CityDriveways"/>.</summary>
        private CityDriveways _driveways;

        /// <summary>
        /// The cars standing at houses, for anything that needs to count them. Read-only: the
        /// presence of a car is decided by whether its owner is out of town and by nothing else.
        /// </summary>
        public CityDriveways Driveways => _driveways;

        /// <summary>
        /// When the driveways were last reconciled against who is out of town, in sim minutes.
        ///
        /// Not every frame: the answer only changes when somebody's day-plan block changes, and
        /// the two that matter are twenty past six and ten past five. A sweep of the citizen list
        /// once a sim-minute is far cheaper than 286 cars asking every frame, and no player can
        /// see the difference between a car leaving at 06:20 and one leaving at 06:20:59.
        /// </summary>
        private int _drivewaysAt = -1;

        /// <summary>
        /// The moving fleet, kept so Update can hold it to the clock. `CityTraffic.Create`'s result
        /// was a local, which was fine while the fleet was a constant and is not now - see
        /// CityTraffic.CarsOutByHour.
        /// </summary>
        private CityTraffic _traffic;

        /// <summary>The moving fleet, for anything that needs to register a stationary obstacle
        /// against it (see <see cref="CityTraffic.Obstacles"/>). May be null: not every scene or
        /// test builds one.</summary>
        public CityTraffic Traffic => _traffic;

        /// <summary>The lights, kept so Update can hand them the town's clock - they cycle in
        /// SIM seconds now (one-clock ruling, 2026-08-16). May be null like the fleet.</summary>
        private CitySignals _signals;

        /// <summary>The county car and the ambulance. Built beside the traffic because it drives
        /// the traffic's own lane graph; may be null for the same reason <see cref="Traffic"/>
        /// may be.</summary>
        public CityResponse Response { get; private set; }

        /// <summary>The town's front doors, and the one thing that swings them.</summary>
        private CityDoors _doors;

        public CityDoors Doors => _doors;
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

            var go = new GameObject("Rossville");
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

            // The street's own voice — floating lines over the heads the response path points
            // at. Created beside the UI because it is UI, just world-anchored; its OnGUI guards
            // against the world not existing yet the same way VillageUI does.
            _voices = StreetVoices.Create(this, transform);

            // A camera has to exist even in the failure case, or there is nothing to draw the
            // error onto. An empty scene has none.
            EnsureCamera();

            // THE CURTAIN GOES UP BEFORE THE WORK, NOT AFTER IT.
            //
            // This used to be created two thirds of the way down this method, and so it could not
            // possibly do its job: UNITY DRAWS NO FRAME UNTIL Awake RETURNS, so a loading screen
            // built during Awake first appears once the loading it exists to cover has finished.
            // The owner pressed Play, watched Unity's default blue for over a minute with no sign
            // of life, and reasonably concluded it had frozen - which is precisely the failure
            // EnsureCamera's own comment warns about a few lines further down.
            //
            // So the curtain is raised here, and `Build` below is deferred to the first Update.
            // One frame is presented with the screen on it, and only then does the six seconds of
            // town-building start - behind something that says so.
            BootScreen.Phase = "Building the town";
            BootScreen.Create(this, transform);
        }

        /// <summary>Set once the town has been built, so <see cref="Update"/> only does it once.</summary>
        private bool _built;

        /// <summary>
        /// Everything that was in <see cref="Awake"/> until 2026-08-08, run one frame later.
        ///
        /// The move is the whole point: see the note in Awake. Nothing here changed except when it
        /// happens, and the only thing that can notice is code assuming <see cref="World"/> exists
        /// the instant Awake returns. Nothing in the tree does - the PlayMode suite polls through
        /// `CityUnderTest.WaitUntilBuilt`, and every system the town needs is created BY this
        /// method rather than racing it.
        /// </summary>
        private void Build()
        {
            try
            {
                // ONE CALL. The kind table, the era table, the parse, all five survey passes in
                // their load-bearing order, and the validation now live in TownPipeline - so the
                // eighteen editor tools that render and audit this town build the SAME town the
                // player walks around in. Until 2026-08-07 they did not: fourteen of them ran no
                // survey passes at all, and the one called "Render The Built Town" was among them.
                var built = TownPipeline.Build(MapFile, Seed);
                World = built.World;

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
                // The hour itself lives on OpeningMinuteOfDay, beside ShowBuildings: 20:00 for
                // the owner, noon for the batch gate. The story of why - and of the night the
                // literal that used to sit here turned four tests red - is written on the field.
                Sim = new Simulation(World, People, Seed, startMinuteOfDay: OpeningMinuteOfDay);

                Debug.Log($"Rossville: {World.Width}×{World.Height}, {World.PlaceCount} places, "
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
            // BEFORE the massing, because the frontage registers its hinges as it draws them and
            // the massing is built LAZILY - the first time the layer comes on, which may be now or
            // may be twenty minutes into a session. A registry that arrived afterwards would take
            // the doors of whichever build happened to be second.
            _doors = CityDoors.Create(transform);
            Frontage.Doors = _doors;

            _village = VillageMesh.Build(World, transform, showDressing: true);
            profile.Done("VillageMesh (ground, walls, roofs, frontage, furniture)");

            // The ground, roads and props are still drawn by the village renderer; only the
            // BUILDINGS are bought models. Nothing happens here for a map that has no city
            // kinds in it, so Rossville still builds exactly as it did.
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
                                    CityStreets.Build(World, city.transform,
                                                      out alleyRoot, out var streetPlanting));
                    Layers.Register(Layers.Kind.Alleys, alleyRoot);

                    // THE STREET KIT'S PLANTING GOES ON THE TREES SWITCH, not on Streets. A kind
                    // can hold more than one root, so this sits beside CityGreenery's lazy
                    // registration below and both come and go together - which is what somebody
                    // clicking "Trees" means.
                    Layers.Register(Layers.Kind.Trees, streetPlanting);
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

            // GREPPABLE, AND ONLY MEANINGFUL HERE, AFTER THE BAKE: a hinge with no leaf under it
            // is a door CityDoors will swing invisibly forever, and for a week that was all 589
            // of them - Combinable's hinge exemption is what keeps it zero now. Zero is the only
            // healthy number; the PlayMode gate EveryDoorHingeKeepsItsLeafThroughTheBake asserts
            // the same thing where a red can stop a merge.
            if (_doors != null && _doors.Count > 0)
            {
                int leafless = _doors.Leafless();
                if (leafless == 0)
                    Debug.Log($"[doors] all {_doors.Count} hinges kept their leaves through the bake.");
                else
                    Debug.LogWarning($"[doors] {leafless} of {_doors.Count} hinges have NO LEAF "
                                   + "after the bake - those doors swing invisibly. CityChunker's "
                                   + "hinge exemption has stopped matching what Frontage builds.");
            }

            // THE HOUSES GET THEIR SURFACE HERE, AFTER THE BAKE, AND NOT IN CityCollision.Build.
            // The bake destroys the GameObjects it consumed, so a collider added before it is a
            // collider thrown away by it - which is half of why the town was walk-through. The
            // other half is the massing being a lazy layer, and CityCollision.Ready is how the
            // late build knows to surface itself. Both are argued out in SolidifyWalls.
            CityCollision.SolidifyWalls(transform);
            profile.Done("CityCollision.SolidifyWalls");

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
            _signals = signals;
            profile.Done("CitySignals");
            traffic = CityTraffic.Create(World, transform, signals);
            _traffic = traffic;
            profile.Done("CityTraffic");

            // THE TWO THAT COME WHEN SOMEBODY IS HURT. Built here rather than lazily and for the
            // same reason the traffic above it is: it drives the traffic's own lane graph, it runs
            // on the SIM clock rather than on frames, and a case the machine has opened must be
            // answerable whether or not anybody has the Traffic layer switched on.
            Response = CityResponse.Create(World, transform, traffic);
            profile.Done("CityResponse");

            // THE OTHER 98% OF THE TOWN'S CARS, WHICH ARE NOT GOING ANYWHERE.
            //
            // IDOT counts Rossville at ~19 vehicles moving at an average instant and the village
            // owns something like two per household, so the cars standing at houses outnumber the
            // ones being driven by a factor of about sixty - and the game drew none of them until
            // now. Built HERE, beside the traffic and after CityChunker.Bake, for exactly the
            // reason the traffic is: a combined mesh cannot be taken away when its owner drives it
            // to Hoopeston. See docs/research/TRAFFIC-COUNTS.md.
            _driveways = CityDriveways.Create(World, Seed, transform);
            profile.Done("CityDriveways");

            // The two that MOVE. Registered like the rest, and switching them off hides them
            // exactly as HideActors always did. Not baked: a combined mesh cannot move or change
            // colour.
            // HIDDEN BY SWITCHING OFF THE RENDERERS, NOT THE GAME OBJECT - and the difference is
            // the whole reason the paragraph above exists.
            //
            // These were registered by root, and Layers.Register(kind, root) does
            // root.SetActive(IsOn(kind)). An inactive GameObject does not get Update(), so
            // switching the traffic layer off did not hide the traffic - IT STOPPED THE TRAFFIC.
            // The comment above says in as many words that the layer must decide what is DRAWN and
            // nothing else; the mechanism it used could not honour that.
            //
            // It was not theoretical and it was not visible either. Layers persist in PlayerPrefs,
            // so `noir.layer.Traffic = 0` - set once in the editor, months ago, for a survey view -
            // silently froze all 160 vehicles in EVERY run afterwards, including every headless
            // test. TrafficMovesAndStopsAtRedLights reported total distance travelled 0.0 and
            // NoCarWaitsForeverAtTheHeadOfAClearQueue reported 120-second waits, and the traffic
            // diagnostic said "160 of 160 stopped: None=160" - None being the default, because no
            // car had been given a reason to stop by code that had never run.
            if (signals != null) Layers.Register(Layers.Kind.Signals, on => ShowRenderers(signals.gameObject, on));
            if (traffic != null) Layers.Register(Layers.Kind.Traffic, on => ShowRenderers(traffic.gameObject, on));

            // Renderers, not the root, for the reason spelled out above - and it composes with the
            // absence: a car whose owner is at work is an INACTIVE GameObject, so turning the layer
            // back on re-enables the renderers of the cars that are actually there and leaves the
            // ones that drove away hidden.
            if (_driveways != null)
                Layers.Register(Layers.Kind.Driveways,
                                on => ShowRenderers(_driveways.gameObject, on));

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

            // The curtain itself is raised in Awake now, a frame before any of this ran - see the
            // note there. All that is left here is to say what the wait is FOR, which changes at
            // this point from building the town to the shader compile below.
            BootScreen.Phase = "Loading assets and shaders";

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
            _interaction = PlayerInteraction.Create(this, transform);
            profile.Done("PlayerInteraction");
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

            // AFTER EVERY LAYER, NOT DURING ONE. VillageMesh calls SurfaceTextures.ReportOnce
            // partway through its own build, before the lazily-registered Massing, Trees and Farm
            // layers have realised, so that line can only ever say "so far". This one runs when
            // the town is finished and is the complete census: which names came off the pack,
            // which off Content/textures/, and which got nothing at all.
            SurfaceTextures.Report();

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
        /// Hide something without stopping it: every renderer under <paramref name="root"/> off,
        /// the GameObject left active so its Update still runs.
        ///
        /// Used for the two layers that are SIMULATIONS rather than scenery. Deactivating those
        /// roots is what froze the traffic for months - see the note where they are registered.
        /// Includes inactive children on purpose: a car parked inside a disabled parent still has
        /// to come back when the layer is switched on.
        /// </summary>
        private static void ShowRenderers(GameObject root, bool on)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        }

        /// <summary>
        /// Turn off every renderer on the things that move, leaving them running underneath.
        ///
        /// Cheaper and far safer than not building them: a hidden car still drives its lane, is
        /// still counted by the jam instrument, and still occupies the space in front of the car
        /// behind it, so nothing about the town's behaviour changes when you switch the drawing
        /// off. A plan is a way of LOOKING at the town, not a different town.
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
        /// <summary>
        /// Ask one neighbour what they saw, and get back what they would actually say.
        ///
        /// THE OTHER HALF OF THE FIREWALL, and the reason this file is allowed to name the
        /// Witness assembly at all. RecordWhereThePlayerWas has been feeding Track since
        /// 2026-08-05 and NOTHING HAS EVER ASKED IT A QUESTION - 4,852 lines of finished,
        /// tested, firewalled investigation machinery with no consumer. This is the question.
        ///
        /// Strings out, not Sighting[]. Noir.Unity does not reference Noir.Core.Observation and
        /// must not start: handing the game evidence objects would put the investigation's own
        /// types in reach of any MonoBehaviour that wanted one, and the whole point of the
        /// firewall is that convenience is exactly how ground truth leaks into testimony. The UI
        /// gets sentences. It cannot reason backwards from a sentence.
        /// </summary>
        public string[] AskWhatTheySaw(CitizenId who, int day)
        {
            if (People == null || World == null) return System.Array.Empty<string>();
            _interruptions ??= new SimInterruptions(this);
            return Recollection.AskInEnglish(World, People, People.Get(who), day, Track, Seed,
                                             null, null, _hitEvents, _interruptions, _askEvents);
        }

        /// <summary>
        /// The T key's ask, with Phase 2's consequences. A CIVILIAN who questions a witness is
        /// REMEMBERED — the ask joins AskEvents at the witness's own tile, so anybody who could
        /// see that doorstep at that minute can later say somebody was going around asking
        /// questions; the killer's own canvass becomes a sighting. A BADGE ask instead lands the
        /// witness's words in every case the town knows about and has not shut, through
        /// ResponseCases.BadgeAsked — the county canvass's sibling seam. Recorded AFTER the
        /// answer is taken, so the testimony handed back never contains the ask that produced it.
        /// The county's own canvass does not come through here and is neither remembered nor
        /// double-filed — CountyReachedDoor already files it.
        ///
        /// IDEMPOTENT WITHIN A MINUTE: the same witness asked again before the clock ticks
        /// skips both consequence arms below, though the testimony is still handed back every
        /// press. The case file is the permanent record and the ask-event tile is a fact about
        /// a doorstep, not about a keypress — a witness questioned twice in the same minute
        /// said the same thing once, and T-key spam should not file it twice or plant two
        /// sightings of the same visit.
        /// </summary>
        public string[] PlayerAsks(CitizenId who, int day)
        {
            string[] lines = AskWhatTheySaw(who, day);
            if (Sim == null || People == null) return lines;

            int minute = NowMinute();
            bool alreadyAsked = minute == _lastAskMinute && who.Equals(_lastAskWho);
            _lastAskMinute = minute;
            _lastAskWho = who;
            if (alreadyAsked) return lines;

            if (Badge)
            {
                // A badge ask when no case is known-open files nothing and records nothing —
                // deliberate today, a Phase 3 seam (see docs/IDEAS.md).
                for (int i = 0; i < _cases.Count; i++)
                {
                    CaseState s = _cases.StateOf(i);
                    if (s == CaseState.Undiscovered || s == CaseState.Closed) continue;
                    _cases.BadgeAsked(i, who, lines);
                }
            }
            else
            {
                _askEvents.Record(minute, Sim.GetAgent(who).Position.ToTile());
            }
            return lines;
        }

        /// <summary>
        /// The nearest person who lives near a point - the neighbour worth asking.
        ///
        /// Home rather than current position on purpose: Recollection only answers for stationary
        /// witnesses, so the useful witness is the one whose day is spent somewhere, and where
        /// they LIVE is the part of that this can know without replaying anybody's plan.
        /// Returns an invalid id when the town is empty.
        /// </summary>
        public CitizenId NearestNeighbour(Tile to)
        {
            var best = default(CitizenId);
            long nearest = long.MaxValue;
            if (People == null || World == null) return best;

            for (int i = 0; i < People.Count; i++)
            {
                var id = new CitizenId(i);
                var home = World.GetPlace(People.Get(id).Home);
                if (home == null) continue;

                // Tiles throughout, and squared, so this is integer arithmetic with no root and
                // no float comparison. The track records tiles; the places are measured in tiles.
                var at = home.Bounds.Centre;
                long dx = at.X - to.X, dy = at.Y - to.Y;
                long d = dx * dx + dy * dy;
                if (d >= nearest) continue;
                nearest = d;
                best = id;
            }
            return best;
        }

        /// <summary>Vehicular harm, the second genuine history. See its own header.</summary>
        private readonly HitEvents _hitEvents = new HitEvents();

        /// <summary>Every ask a witness was ever put, so a doorstep canvass can itself be seen.</summary>
        private readonly AskEvents _askEvents = new AskEvents();

        /// <summary>The witness and minute PlayerAsks last acted on, so T-key spam does not
        /// re-file or re-record the same doorstep visit. The case file is the permanent
        /// record; a witness asked twice in the same minute said the same thing once.</summary>
        private int _lastAskMinute = -1;
        private CitizenId _lastAskWho;

        /// <summary>Which minute each downed citizen went down, whether it killed them, and when
        /// they came back — the sim knows WHO, this knows WHEN. Phase 2's police consume it, and
        /// Recollection's IInterruptions answers from it: silenced while [DownedFrom, BackFrom).
        /// A re-hit of a returned survivor widens the window (earliest down, latest back).</summary>
        private struct VictimRecord { public int DownedFrom; public int BackFrom; public bool Fatal; }
        private readonly Dictionary<int, VictimRecord> _victims = new Dictionary<int, VictimRecord>();

        private sealed class SimInterruptions : IInterruptions
        {
            private readonly VillageHost _host;
            public SimInterruptions(VillageHost host) { _host = host; }
            public int DownedFromMinute(CitizenId who) =>
                _host._victims.TryGetValue(who.Value, out var r) ? r.DownedFrom : int.MaxValue;
            public int BackFromMinute(CitizenId who) =>
                _host._victims.TryGetValue(who.Value, out var r) ? r.BackFrom : int.MaxValue;
        }
        private SimInterruptions _interruptions;

        /// <summary>
        /// A car hit a person. The one recording seam, fed plain data by Player exactly as
        /// Player.Where feeds the track - the car controller never names this layer. Downs
        /// the victim in the sim (the body stays), stamps the event with the sim clock, and
        /// keeps the victim record Phase 2's police will consume.
        /// </summary>
        public void CarStruckSomebody(CitizenId victim, Vector3 at, float speed)
        {
            if (Sim == null) return;
            if (Sim.GetAgent(victim).Downed) return;

            Sim.Down(victim);

            int minute = Sim.Clock.Day * (GameClock.TicksPerDay / GameClock.TicksPerMinute)
                       + Sim.Clock.MinuteOfDay;
            _hitEvents.Record(minute, Space3D.TileAt(at),
                              _player != null ? _player.CarTone : CarTone.Unnoticed,
                              _player != null ? _player.CarShape : CarShape.Unnoticed);

            bool fatal = speed >= ResponseCases.FatalSpeed;
            _victims.TryGetValue(victim.Value, out var was);   // default: DownedFrom=0 → treat absent
            _victims[victim.Value] = new VictimRecord
            {
                DownedFrom = _victims.ContainsKey(victim.Value) ? Math.Min(was.DownedFrom, minute) : minute,
                BackFrom   = int.MaxValue,
                Fatal      = fatal,
            };

            // AND THE TOWN OPENS A CASE ON IT. Here rather than wherever the body is later
            // found, because everything Open fixes is only knowable at the moment of impact:
            // who, when, which tile, what the car looked like from outside, and whether the
            // speed was lethal. Nothing happens on the strength of it yet - the case sits
            // Undiscovered until somebody with a line of sight to that tile actually sees it,
            // which is RunResponse's discovery scan and not this method's business.
            int caseId = _cases.Open(victim, minute, Space3D.TileAt(at),
                                     _player != null ? _player.CarTone : CarTone.Unnoticed,
                                     _player != null ? _player.CarShape : CarShape.Unnoticed,
                                     speed >= ResponseCases.FatalSpeed);

            Debug.Log($"[hit] a car struck citizen {victim.Value} at {Space3D.TileAt(at)} "
                    + $"minute {minute}, speed {speed:0.0} m/s, fatal={fatal}. "
                    + $"They are down, case {caseId} is open, and the town can be asked about it.");
        }

        /// <summary>Task 13 calls this when ordering TakeBodyAway for a survivor - the return
        /// minute is known at order time. Widens the record rather than replacing it, so a
        /// re-hit of somebody already on their way back does not lose the earlier DownedFrom.</summary>
        public void VictimReturned(CitizenId who, int minute)
        {
            _victims.TryGetValue(who.Value, out var r);
            r.BackFrom = minute;
            _victims[who.Value] = r;
        }

        // ---- the response -----------------------------------------------------------------
        //
        // THE MACHINE SAYS WHAT, THIS FILE SAYS WHO. ResponseCases can see minutes, tiles and
        // ids and nothing else - its asmdef references Noir.Core.Contracts alone - so it cannot
        // pick an officer, cannot know which citizens were at their windows, and has no car. It
        // emits ORDERS; everything below carries one out and reports the result back through the
        // machine's arrival methods. That split is the whole design and it is why this loop is
        // four lines of its own logic wrapped round somebody else's.
        //
        // It also has to live HERE, in the one file the witness firewall names, because two of
        // the three answers it needs - who can see the body, and who has anything to say about
        // the hit - come out of Noir.Core.Witness, which nothing else in Assets may consume.

        /// <summary>The open cases and their files. Read-only from outside: everything that
        /// advances a case goes through an order and a report, above.</summary>
        public ResponseCases Cases => _cases;

        private readonly ResponseCases _cases = new ResponseCases();

        /// <summary>One instance, not one per case: its cache is keyed on the DAY and dropped
        /// when the day moves, so two cases scanning the same minute share 1,300 day plans
        /// instead of building them twice.</summary>
        private readonly Discovery _discovery = new Discovery();

        // Scratch, reused rather than allocated every sim minute.
        private readonly List<CaseOrder> _caseOrders = new List<CaseOrder>();
        private readonly List<string> _caseLog = new List<string>();
        private readonly List<int> _walkingKeys = new List<int>();
        private readonly List<CitizenId> _canvassList = new List<CitizenId>();

        /// <summary>
        /// Which officer is walking to which case's scene, for the arrival poll below.
        ///
        /// THE MACHINE CANNOT HOLD THIS AND MUST NOT LEARN TO. It has no idea how far a man has
        /// to walk, whether the ground between him and the scene is walkable, or that he might
        /// be run over on the way - so it waits to be told, and this is the only place that
        /// knows enough to tell it.
        /// </summary>
        private readonly Dictionary<int, CitizenId> _officerWalking = new Dictionary<int, CitizenId>();

        /// <summary>Which citizens are standing in each case's ring. The machine cannot hold
        /// this for <see cref="_officerWalking"/>'s own reason: it has no idea who is nearby.</summary>
        private readonly Dictionary<int, List<CitizenId>> _gawkers =
            new Dictionary<int, List<CitizenId>>();

        /// <summary>
        /// The fixed ring: offsets at Chebyshev radius 2-3 in one declared order, so the crowd's
        /// shape is deterministic per scene. Slot N is the (N+1)th WALKABLE spot — a ring tile
        /// inside a wall is skipped, not assigned. Reachability is left to RespondTick: a sealed
        /// but walkable tile strands its gawker standing wherever the path gave out, which reads
        /// as somebody craning from a distance and costs nothing.
        /// </summary>
        private static readonly (int dx, int dy)[] RingSpots =
        {
            (2, 0), (-2, 0), (0, 2), (0, -2), (2, 2), (-2, -2), (2, -2), (-2, 2),
            (3, 1), (-3, -1), (1, 3), (-1, -3),
        };

        private Tile? RingTileFor(Tile scene, int taken)
        {
            int placed = 0;
            for (int i = 0; i < RingSpots.Length; i++)
            {
                var t = new Tile(scene.X + RingSpots[i].dx, scene.Y + RingSpots[i].dy);
                if (!World.Grid.IsWalkable(t)) continue;
                if (placed++ == taken) return t;
            }
            return null;
        }

        /// <summary>When the response last ran, in minutes of the day. The
        /// <see cref="_drivewaysAt"/> pattern, for the same reason: the machine reasons in whole
        /// minutes and running it per frame would ask the same question sixty times.</summary>
        private int _responseAt = -1;

        /// <summary>
        /// The absolute minute, as every stamp in the response is measured.
        ///
        /// EXTRACTED BECAUSE IT IS READ FROM TWO CLOCKS. RunResponse reads it once a sim minute;
        /// the arrival callbacks handed to <see cref="CityResponse"/> read it whenever a vehicle
        /// or an officer actually gets somewhere, which is a Unity frame in the middle of that
        /// minute. Two copies of this expression would eventually disagree by a day.
        /// </summary>
        private int NowMinute() =>
            Sim.Clock.Day * (GameClock.TicksPerDay / GameClock.TicksPerMinute) + Sim.Clock.MinuteOfDay;

        /// <summary>
        /// One sim minute of the town's response: who found a body, who has arrived, what the
        /// machine wants next, and the greppable record of all of it.
        ///
        /// The four steps are in this order on purpose. Discovery first, so a body found this
        /// minute starts its alarm this minute rather than next. Arrivals second, so the machine
        /// ticks knowing where everybody actually is. Then its own minute, then the log - which
        /// is drained LAST so that the transitions this minute's orders caused are printed with
        /// it rather than a minute late.
        /// </summary>
        private void RunResponse()
        {
            int minuteOfDay = Sim.Clock.MinuteOfDay;
            int minute = NowMinute();

            // ---- 1. Somebody looks out of a window ----
            //
            // TESTIMONY'S OWN OPTICS, not a proximity check: Discovery walks the identical gate
            // sequence Recollection does, so whoever finds the body is exactly somebody who
            // could later be asked about the scene. The victim cannot find their own body
            // because _interruptions silences them from the minute they went down.
            for (int c = 0; c < _cases.Count; c++)
            {
                if (_cases.StateOf(c) != CaseState.Undiscovered) continue;
                _interruptions ??= new SimInterruptions(this);
                CitizenId saw = _discovery.WhoSees(World, People, Sim.Clock.Day, minuteOfDay,
                                                   _cases.SceneOf(c), Seed, null, _interruptions, null);
                if (saw.IsValid)
                {
                    _cases.BodySeen(c, minute, saw);
                    _voices?.Say(saw, _cases.FatalOf(c)
                        ? "oh god — somebody get help!"
                        : "hey! are you alright?! somebody call it in!", 6f);
                }
            }

            // ---- 2. Is the officer there yet? ----
            //
            // The machine never predicts travel, so somebody has to look. Keys are copied first
            // because both arms below remove from the dictionary they would otherwise be
            // enumerating. Each arm re-checks the state before reporting: a case force-closed
            // while its officer was still walking is gone from the machine's point of view, and
            // OfficerArrived/OfficerLost both throw rather than forgive a stale report.
            _walkingKeys.Clear();
            _walkingKeys.AddRange(_officerWalking.Keys);
            for (int i = 0; i < _walkingKeys.Count; i++)
            {
                int c = _walkingKeys[i];
                CitizenId officer = _officerWalking[c];
                AgentState a = Sim.GetAgent(officer);

                // Struck down en route - the case's own victim's fate, visited on the response.
                if (a.Downed)
                {
                    _officerWalking.Remove(c);
                    if (_cases.StateOf(c) == CaseState.OfficerEnRoute) _cases.OfficerLost(c);
                    continue;
                }

                // Standing on the scene tile, off his plan, not walking: that is the sim's own
                // definition of a responder having arrived (Simulation.RespondTick).
                if (a.Responding && !a.Travelling && a.Doing == Activity.Responding
                    && a.Position.ToTile() == _cases.SceneOf(c))
                {
                    _officerWalking.Remove(c);
                    if (_cases.StateOf(c) == CaseState.OfficerEnRoute)
                    {
                        _cases.OfficerArrived(c, minute);
                        _voices?.Say(officer, "step back, please — all of you.");
                    }
                }
            }

            // ---- 3. The machine's own minute ----
            _caseOrders.Clear();
            _cases.Tick(minute, _caseOrders);
            for (int i = 0; i < _caseOrders.Count; i++) Execute(_caseOrders[i], minute);

            // ---- 3½. The crowd ----
            //
            // THE MACHINE NEVER HEARS OF THEM. Gawkers are pure choreography: once a case is
            // discovered, one nearby citizen a minute drifts over to a ring tile — nearest
            // eligible first, citizen-index tiebreak, NO RNG (the response path's standing
            // rule) — up to six, and the whole ring is released the minute the case closes,
            // however it closed. Keying dispersal on Closed rather than on VehiclesLeave means
            // CloseLoudly and every teardown path disperse the crowd for free.
            for (int c = 0; c < _cases.Count; c++)
            {
                var state = _cases.StateOf(c);
                if (state == CaseState.Closed)
                {
                    if (_gawkers.TryGetValue(c, out var crowd))
                    {
                        for (int g = 0; g < crowd.Count; g++) Sim.Release(crowd[g]);
                        _gawkers.Remove(c);
                        Debug.Log($"[case] case {c}: the crowd loses interest");
                    }
                    continue;
                }
                if (state == CaseState.Undiscovered) continue;

                if (!_gawkers.TryGetValue(c, out var ring))
                    _gawkers[c] = ring = new List<CitizenId>();
                if (ring.Count >= 6) continue;

                var sceneTile = _cases.SceneOf(c);
                var sceneV = Vec2.CentreOf(sceneTile);
                int best = -1;
                float bestD = 80f * 80f;   // nobody drifts over from more than 80 m
                for (int i = 0; i < Sim.AgentCount; i++)
                {
                    if (i == _cases.VictimOf(c).Value) continue;
                    var a = Sim.GetAgent(i);
                    if (a.Downed || a.Responding || a.AwayUntilMinute != 0) continue;
                    if (a.Doing == Activity.Asleep) continue;
                    float dx = a.Position.X - sceneV.X, dy = a.Position.Y - sceneV.Y;
                    float d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best < 0) continue;

                var spot = RingTileFor(sceneTile, ring.Count);
                if (!spot.HasValue) continue;

                var who = new CitizenId(best);
                Sim.Respond(who, spot.Value, Activity.Gawking);
                ring.Add(who);
                _voices?.Say(who, GawkLine(best));
                Debug.Log($"[case] case {c}: citizen {best} drifts over");
            }

            // ---- 4. The record ----
            _caseLog.Clear();
            _cases.DrainLog(_caseLog);
            for (int i = 0; i < _caseLog.Count; i++) Debug.Log("[case] " + _caseLog[i]);
        }

        /// <summary>
        /// Carry out one order. Everything here is the half the machine cannot do: pick a man,
        /// send a car, knock on a door, take a body away.
        ///
        /// TWO OF THESE ARMS FIRE THEIR REPORT LATER, FROM A CALLBACK. DriveIn and WalkCountyTo
        /// each promise exactly one invocation, so the arrival methods they call are reached
        /// exactly once per order - which is what those methods' state guards require. They fire
        /// on a Unity frame rather than inside this loop, hence <see cref="NowMinute"/>.
        /// </summary>
        private void Execute(CaseOrder order, int minute)
        {
            switch (order.Kind)
            {
                case OrderKind.DispatchOfficer:
                {
                    CitizenId officer = WhoIsOnDuty();
                    if (!officer.IsValid)
                    {
                        // THE SANCTIONED WAY TO SAY "NOBODY". OfficerLost is not only for an
                        // officer struck en route - it is the machine's own flag-reset, so the
                        // case falls back to Alarm and asks again next minute. A town whose
                        // whole precinct is downed, away or already out keeps asking rather
                        // than silently never being answered.
                        Debug.Log("[case] no officer left standing");
                        _cases.OfficerLost(order.Case);
                        return;
                    }
                    // Read BEFORE Respond: its entry cleanup leaves Doing alone, but the watch
                    // question belongs to the moment of dispatch, not to whatever RespondTick
                    // writes a tick later.
                    bool onWatch = Sim.GetAgent(officer).Doing == Activity.AtWork;

                    Sim.Respond(officer, order.Scene);
                    _cases.OfficerDispatched(order.Case, officer);
                    _officerWalking[order.Case] = officer;

                    // ON WATCH, HE TAKES THE CRUISER; the overnight on-call man still comes on
                    // foot from his bed — no cruiser at his house. Boarding hides him from the
                    // world; the arrival callback sets him down at the kerb and RespondTick
                    // walks the last few metres. Off-stage fallback included, the callback
                    // always fires, so he can never be sealed inside a cruiser.
                    if (onWatch && Response != null && Response.CruiserAvailable
                        && PrecinctSpot().HasValue)
                    {
                        Sim.Board(officer);
                        var scene = order.Scene;
                        var who = officer;
                        Response.CruiserOut(PrecinctSpot().Value, scene,
                            onArrived: at => Sim.Alight(who, KerbTileNear(at, scene)));
                        Debug.Log($"[case] case {order.Case}: citizen {officer.Value} takes "
                                + $"the cruiser to ({order.Scene.X},{order.Scene.Y})");
                    }
                    else
                        Debug.Log($"[case] case {order.Case}: citizen {officer.Value} sent to "
                                + $"({order.Scene.X},{order.Scene.Y})");
                    return;
                }

                case OrderKind.CountyCarIn:
                {
                    int id = order.Case;
                    if (Response == null)
                    {
                        Debug.Log($"[case] case {id}: no vehicles in this scene - "
                                + "the county car arrives off-stage");
                        CountyIsHere(id, minute);
                        return;
                    }
                    Response.DriveIn(CityResponse.Rig.County, edgeSouth: true, order.Scene,
                                     onArrived: () => CountyIsHere(id, NowMinute()));
                    return;
                }

                case OrderKind.CanvassNext:
                {
                    int id = order.Case;
                    CitizenId who = order.Who;
                    int day = DayOfHit(id);

                    // NO FRONT DOOR IS NOT A REASON TO STOP CANVASSING. A witness whose home
                    // place cannot be resolved has nowhere to be knocked on, and a canvass that
                    // waited for a callback nobody could fire would hold the scene, the two
                    // vehicles and the officer for the rest of the game. Their answer is taken
                    // at the kerb instead - the same words, filed the same way, at this minute.
                    if (!TryDoorOf(who, out Tile door) || Response == null)
                    {
                        Debug.Log($"[case] case {id}: citizen {who.Value} has no door to knock "
                                + "on - their answer is taken at the kerb");
                        _cases.CountyReachedDoor(id, minute, who, AskWhatTheySaw(who, day));
                        _voices?.Say(who, "…and that's everything I saw.");
                        return;
                    }

                    Response.WalkCountyTo(door, onArrived: () =>
                    {
                        _cases.CountyReachedDoor(id, NowMinute(), who, AskWhatTheySaw(who, day));
                        _voices?.Say(who, "…and that's everything I saw.");
                    });
                    return;
                }

                case OrderKind.AmbulanceIn:
                {
                    int id = order.Case;
                    if (Response == null)
                    {
                        Debug.Log($"[case] case {id}: no vehicles in this scene - "
                                + "the ambulance arrives off-stage");
                        _cases.AmbulanceArrived(id, minute);
                        return;
                    }
                    Response.DriveIn(CityResponse.Rig.Ambulance, edgeSouth: false, order.Scene,
                                     onArrived: () => _cases.AmbulanceArrived(id, NowMinute()));
                    return;
                }

                case OrderKind.TakeBodyAway:
                {
                    // THE ONLY CALL SITE OF VictimReturned, and deliberately: the record's
                    // BackFrom is what un-silences a witness, so writing it anywhere else -
                    // speculatively, on an officer, on a guess - would give somebody their
                    // testimony back while they were still lying in the street.
                    int back = _cases.ReturnMinuteOf(order.Case);
                    Sim.TakeAway(order.Who, back);
                    if (back != int.MaxValue) VictimReturned(order.Who, back);
                    return;
                }

                case OrderKind.ReleaseOfficer:
                {
                    // MAY CARRY CitizenId.None. A case closed loudly in the window between
                    // DispatchOfficer being emitted and an officer being reported has no officer
                    // to release, and the machine queues the pair regardless rather than growing
                    // a special case for it. Nothing to do is a legitimate outcome here.
                    _officerWalking.Remove(order.Case);
                    if (order.Who.IsValid) Sim.Release(order.Who);

                    // His ride goes home when he does — Release cleared any Aboard, so even a
                    // case closed mid-drive leaves nobody sealed in the car it recalls.
                    if (Response != null && PrecinctSpot().HasValue)
                        Response.CruiserHome(PrecinctSpot().Value);
                    return;
                }

                case OrderKind.VehiclesLeave:
                {
                    if (Response == null) return;
                    Response.Depart(CityResponse.Rig.County, edgeSouth: true);
                    Response.Depart(CityResponse.Rig.Ambulance, edgeSouth: false);
                    return;
                }
            }
        }

        /// <summary>The county car's arrival and the witness list it starts work on, which are
        /// one moment and are reported as one - <see cref="ResponseCases.CanvassBegins"/> reads
        /// the arrival minute it was just given, so anything between them would be a gap in the
        /// scene's own clock.</summary>
        private void CountyIsHere(int caseId, int minute)
        {
            _cases.CountyArrived(caseId, minute);
            _cases.CanvassBegins(caseId, CanvassListFor(caseId));
        }

        /// <summary>
        /// Who the town sends.
        ///
        /// THE PRECINCT IS A TABLE-DECLARED KIND, NOT AN ENUM MEMBER - `Content/kinds.txt` says
        /// `kind precinct / words precinct police station`, and PlaceKind carries no such value.
        /// So the word is resolved through the kind table exactly as the map parser resolves it,
        /// and a town whose content stops declaring one gets no officer rather than a compile
        /// error somewhere else.
        ///
        /// The order of preference is a small town's night desk: the man actually on watch, then
        /// the man asleep who is on call, then anybody on the roster who is on their feet. Nobody
        /// downed, nobody the ambulance already has, and nobody already standing at another
        /// scene - Rossville does not have two response teams.
        /// </summary>
        /// <summary>The precinct's own kerb, cached after the first ask: where the cruiser
        /// starts from and returns to. Null in a town whose kind table has no precinct.</summary>
        private Vec2? _precinctSpot;
        private bool _precinctLooked;

        private Vec2? PrecinctSpot()
        {
            if (_precinctLooked) return _precinctSpot;
            _precinctLooked = true;
            if (World == null || !PlaceKindTable.IsInstalled) return null;
            if (!PlaceKindTable.Current.TryKindOf("precinct", out PlaceKind kind)) return null;
            IReadOnlyList<PlaceId> stations = World.PlacesOfKind(kind);
            if (stations == null || stations.Count == 0) return null;
            var place = World.GetPlace(stations[0]);
            if (place != null) _precinctSpot = Vec2.CentreOf(place.Door);
            return _precinctSpot;
        }

        /// <summary>The tile the officer steps out onto: the nearest walkable tile to where the
        /// cruiser actually stopped, spiralling out a few tiles, with the scene itself as the
        /// fallback — always walkable, because somebody was standing on it when they were hit.</summary>
        private Tile KerbTileNear(Vector3 world, Tile fallback)
        {
            var centre = Space3D.TileAt(world);
            for (int r = 0; r <= 4; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;   // ring only
                        var t = new Tile(centre.X + dx, centre.Y + dy);
                        if (World.Grid.IsWalkable(t)) return t;
                    }
            return fallback;
        }

        private CitizenId WhoIsOnDuty()
        {
            if (World == null || People == null || Sim == null) return CitizenId.None;
            if (!PlaceKindTable.IsInstalled) return CitizenId.None;
            if (!PlaceKindTable.Current.TryKindOf("precinct", out PlaceKind kind)) return CitizenId.None;

            IReadOnlyList<PlaceId> stations = World.PlacesOfKind(kind);
            if (stations == null || stations.Count == 0) return CitizenId.None;

            IReadOnlyList<CitizenId> roster = People.WorkersAt(stations[0]);
            CitizenId asleep = CitizenId.None, anybody = CitizenId.None;

            for (int i = 0; i < roster.Count; i++)
            {
                CitizenId id = roster[i];
                AgentState a = Sim.GetAgent(id);
                if (a.Downed || a.Responding || a.AwayUntilMinute != 0) continue;

                if (a.Doing == Activity.AtWork) return id;                     // on watch
                if (a.Doing == Activity.Asleep) { if (!asleep.IsValid) asleep = id; continue; }
                if (!anybody.IsValid) anybody = id;
            }

            return asleep.IsValid ? asleep : anybody;
        }

        /// <summary>
        /// Everybody with something to say about the hit, for the county car to work through.
        ///
        /// THE ONE LEGAL CALLER DOING THE ONE LEGAL THING. This asks the witness layer the same
        /// question the player's T key asks, over the whole census, for the day the hit happened
        /// - and keeps nothing but the names, which is the point: the canvass list is who to
        /// knock on, and the words are collected later, at the door, by AskWhatTheySaw.
        ///
        /// The victim is skipped outright. Everybody else who was down at that minute is already
        /// silent - the same IInterruptions window the event arm honours - so there is nothing to
        /// filter for them here.
        /// </summary>
        private CitizenId[] CanvassListFor(int caseId)
        {
            if (World == null || People == null) return System.Array.Empty<CitizenId>();
            _interruptions ??= new SimInterruptions(this);

            int day = DayOfHit(caseId);
            CitizenId victim = _cases.VictimOf(caseId);

            _canvassList.Clear();
            for (int i = 0; i < People.Count; i++)
            {
                var id = new CitizenId(i);
                if (id.Value == victim.Value) continue;
                if (SawTheHit(id, day)) _canvassList.Add(id);
            }
            return _canvassList.ToArray();
        }

        /// <summary>
        /// Did this citizen witness the HIT ITSELF that day - not the player wandering past,
        /// the hit.
        ///
        /// THE OBVIOUS WAY TO ASK THIS DOES NOT COMPILE, AND THE REASON IS THE FIREWALL WORKING.
        /// <c>Recollection.WhatTheySawOfEvents</c> answers exactly this question and returns an
        /// <c>EventSighting[]</c>, which lives in Noir.Core.Observation - an assembly Noir.Unity
        /// deliberately does not reference (see AskWhatTheySaw's own header: the game gets
        /// sentences, never evidence). Naming that method here is CS0012, and the fix is NOT to
        /// add the reference: a MonoBehaviour holding a Sighting is precisely the leak the whole
        /// arrangement exists to prevent.
        ///
        /// ⚠ AND `dotnet build Noir.Unity.csproj` WILL NOT TELL YOU. That project is SDK-style,
        /// so MSBuild passes transitive ProjectReferences to the compiler and the call builds
        /// clean; Unity's own asmdef references are NOT transitive, so the same source is a red
        /// editor console. Verify a change to this method with
        /// `-p:DisableTransitiveProjectReferences=true`, which is what Unity actually does.
        ///
        /// So it is asked through the one seam that hands out strings. An EMPTY track means
        /// WhatTheySaw contributes nothing, so every line that comes back is an EVENT line - and
        /// the answer is "they saw something" unless it is the layer's own I-saw-nothing
        /// sentence, which is asked for once, from the same method with no events to see, rather
        /// than copied across the wall as a literal that could drift.
        /// </summary>
        private bool SawTheHit(CitizenId id, int day)
        {
            Citizen who = People.Get(id);

            _sawNothing ??= Recollection.AskInEnglish(World, People, who, day, _noTrack, Seed,
                                                      null, null, null, _interruptions)[0];

            string[] said = Recollection.AskInEnglish(World, People, who, day, _noTrack, Seed,
                                                      null, null, _hitEvents, _interruptions);
            return said.Length != 1 || said[0] != _sawNothing;
        }

        /// <summary>A track with nothing in it, so the question above is about events alone.
        /// Never written to - the real one is <see cref="Track"/>.</summary>
        private readonly PlayerTrack _noTrack = new PlayerTrack();

        /// <summary>What the witness layer says when there is nothing to say, asked of it once
        /// rather than restated here. See <see cref="SawTheHit"/>.</summary>
        private string _sawNothing;

        /// <summary>The day the hit happened, which is the day a canvass asks about - not
        /// today, which can be later if the body lay unfound overnight.</summary>
        private int DayOfHit(int caseId) =>
            _cases.MinuteOf(caseId) / (GameClock.TicksPerDay / GameClock.TicksPerMinute);

        /// <summary>Where to knock. False when the citizen's home does not resolve to a place,
        /// which is the one case the canvass has to report its way around.</summary>
        private bool TryDoorOf(CitizenId who, out Tile door)
        {
            door = default;
            if (World == null || People == null) return false;
            Place home = World.GetPlace(People.Get(who).Home);
            if (home == null) return false;
            door = home.Door;
            return true;
        }

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

            // Behind a wheel. What a witness saw was a car - Recollection treats these
            // minutes as traffic, not a figure.
            if (_player != null && _player.InVehicle) looked |= Visibly.InAVehicle;

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
            // ONE FRAME LATE, AND DELIBERATELY. Awake raised the boot screen and returned without
            // building anything, so Unity has now presented a frame with the curtain on it. The
            // six seconds of town-building happen behind that instead of behind Unity's default
            // blue, which is what "I pressed Play and it froze" actually was.
            //
            // Before the Sim guard below, because the whole point is that there is no Sim yet.
            if (!_built)
            {
                _built = true;
                Build();
                return;
            }

            if (Sim == null) return;

            HandleHotkeys();

            // The town takes its cars to work. Once a sim-minute, not once a frame - see
            // _drivewaysAt. This is the one thing in the game whose CHANGE is the content: the
            // driveway that is empty this morning and was not yesterday.
            if (_driveways != null && Sim.Clock.MinuteOfDay != _drivewaysAt)
            {
                _drivewaysAt = Sim.Clock.MinuteOfDay;
                _driveways.Refresh(Sim);
            }

            // And the same clock decides how many are actually MOVING. The fleet is built once at
            // the peak hour and garaged; this is what holds it to IDOT's curve - ~19 cars at an
            // average instant, ~46 at the commute - instead of the flat 159 it ran for months.
            // Retime is idempotent within a minute, so calling it every frame costs a compare.
            if (_traffic != null)
            {
                _traffic.Retime(Sim.Clock.MinuteOfDay);
                _traffic.TownTick = Sim.Clock.Tick;
            }
            if (_signals != null)
                _signals.TownSeconds = Sim.Clock.Tick / (double)GameClock.TicksPerSecond;

            // And the town answers for somebody lying in the street. Once a sim-minute for the
            // same reason the driveways are: the case machine reasons in whole minutes, and the
            // discovery scan behind it replays a day plan per citizen. See RunResponse.
            if (Sim.Clock.MinuteOfDay != _responseAt)
            {
                _responseAt = Sim.Clock.MinuteOfDay;
                RunResponse();
            }

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
        /// <summary>
        /// Milliseconds the CAMERA took this frame. `_rig` is an <see cref="OrbitCamera"/>.
        ///
        /// NOT THE CHARACTER RIGS, WHICH IS WHAT EVERYONE READS IT AS. The perf report printed
        /// this in a column headed `rig.ms` next to `sim.ms` and `refresh.ms`, in a town with
        /// 1,385 rigged people in it — so the one number a reader would take as "what the skinned
        /// figures cost" was the orbit camera, and the animators, which really were half the
        /// frame, appeared nowhere. The column says `camera.ms` now.
        /// </summary>
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
