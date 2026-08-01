using System;
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
        /// Off while the city is being built out. Turning it back on is this one flag: nothing
        /// downstream of it was deleted, and Sim, Population, the lit windows and the "who is
        /// inside this building" panel all keep working with it off, because none of them ever
        /// went through the figures.
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
        /// Set this true before Play to raise the models again. It is deliberately NOT a key:
        /// showing both means building both, and the brick town is four thousand renderers to
        /// keep hidden in case somebody wants to look at it.
        /// </summary>
        public static bool ShowBuildings = false;

        private GameObject _village;
        private XRay _xray;
        private AgentMeshView _agentView;
        private OrbitCamera _rig;
        private SunRig _lighting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("Ashcombe");
            DontDestroyOnLoad(go);
            go.AddComponent<VillageHost>();
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

                var layout = VillageParser.Parse(ContentLoader.Read(MapFile));
                World = WorldBuilder.Build(layout);

                var report = WorldValidator.Validate(World);
                foreach (var problem in report.Errors) Debug.LogError("village.txt: " + problem);
                foreach (var warning in report.Warnings) Debug.LogWarning("village.txt: " + warning);

                var names = NameTable.Parse(ContentLoader.Read("names.txt"));
                Particulars = ParticularsTable.Parse(ContentLoader.Read("particulars.txt"));
                People = PopulationGenerator.Generate(World, names, Particulars, Seed);

                // Start at six in the morning: the village is about to wake up, which is the
                // most interesting minute of the day to arrive at.
                Sim = new Simulation(World, People, Seed, startMinuteOfDay: 6 * 60);

                Debug.Log($"Ashcombe: {World.Width}×{World.Height}, {World.PlaceCount} places, "
                        + $"{People.Count} people in {People.HouseholdCount} households.");
            }
            catch (Exception ex)
            {
                LoadError = ex.Message;
                Debug.LogError("Failed to load the village: " + ex);
                return;
            }

            _village = VillageMesh.Build(World, transform);

            // The ground, roads and props are still drawn by the village renderer; only the
            // BUILDINGS are bought models. Nothing happens here for a map that has no city
            // kinds in it, so Ashcombe still builds exactly as it did.
            var city = new GameObject("City");
            city.transform.SetParent(_village.transform, false);
            CityStreets.Build(World, city.transform);
            CityParking.Build(World, city.transform);
            CitySigns.Build(World, city.transform);
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
            }
            // Built AFTER the bake and outside it, further down - CityChunker combines every
            // renderer under `city` into a handful of meshes and destroys the originals, and a
            // plan that has been merged into the terrain is a plan you cannot see.
            CityStory.Build(World, city.transform);
            CityRail.Build(World, city.transform);
            CityFarm.Build(World, city.transform);
            CityPowerlines.Build(World, city.transform);
            CityGreenery.Build(World, city.transform);

            // BEFORE the bake, because it measures the buildings the bake is about to destroy,
            // and parented outside the node the bake touches so it survives. Nothing else in the
            // project raycasts against this - picking still walks the world model - it exists so
            // a person has a floor and the bank has walls.
            CityCollision.Build(World, transform, authored, blocks, estates);

            // Assembled out of pieces, drawn as a handful of meshes.
            CityChunker.Bake(city);

            // AFTER the bake and OUTSIDE the node it bakes: a combined mesh cannot move or
            // change colour, so anything that drives - or that goes red and green - has to be
            // built once the static city is already frozen.
            if (!ShowBuildings) CityOutlines.Build(World, transform);

            var signals = CitySignals.Create(World, transform);
            CityTraffic.Create(World, transform, signals);
            _xray = XRay.Create(World, _village);

            // The people are SIMULATED either way - Sim ticks, they go to work, windows light
            // from who is behind them, and clicking a building still says who is in it. This
            // only decides whether they are DRAWN. Turned off while the city itself is being
            // built out: a few hundred figures walking through a downtown that is still being
            // laid is noise over the thing actually being looked at.
            if (ShowPeople) _agentView = AgentMeshView.Create(this, transform);
            _rig = OrbitCamera.Create(this);

            // P drops you into the town at eye height with a body, and P again lifts you back
            // out. Nothing is spawned until the first press: a rigged character standing in the
            // street costs nothing to nobody who never asks for it.
            Player.Create(this, transform);
            _lighting = SunRig.Create(this, transform);

            PostFx.Create(transform);
            PostFx.EnableOn(Camera.main);

            // The church bell, the ambience beds and footsteps by surface. Atmosphere is half
            // sound, and until now the village made none at all.
            VillageAudio.Create(this, transform);
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
