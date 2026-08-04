using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// Time controls and the inspector, drawn with IMGUI.
    ///
    /// IMGUI rather than UI Toolkit or uGUI for one reason: it needs no assets, no canvas, no
    /// prefabs — nothing that has to be built by hand inside the editor. It is not the tool you
    /// would ship a game's menus with, and it is exactly the right tool for an instrument panel.
    /// </summary>
    public sealed class VillageUI : MonoBehaviour
    {
        public static bool PointerOverUI { get; private set; }

        private VillageHost _host;
        private GUIStyle _panel, _label, _title, _small, _button, _clock;
        private bool _stylesReady;
        private bool _scaleLoaded;
        private Vector2 _scroll;
        private bool _showPlan = true;

        // ---- the note editor, shared by the place panel and the bare-parcel panel ----
        //
        // Which parcel the drafts below currently hold. Set to int.MinValue to force a reload
        // from disk on the next frame, which is what both `save` and `revert` do.
        private int _noteDraftFor = int.MinValue;
        private Vector2 _noteScroll;


        // ============================ READABILITY ============================
        //
        // EVERY SIZE IN THIS FILE IS MULTIPLIED BY Scale. Nothing here is drawn at a fixed pixel
        // size any more, because a fixed size is a guess about somebody's eyes and this one was
        // wrong: the panel was set at 13px body text and 11px for the grey secondary lines, which
        // is unreadable for a partially sighted player and merely small for everybody else.
        //
        // Scaled rather than simply enlarged, so it is TUNABLE at runtime - Ctrl+= and Ctrl+-
        // while the game is running, saved to PlayerPrefs so it survives a restart. A single
        // hard-coded larger number would just be a different guess, and the person who needs it
        // biggest is exactly the person who cannot try three builds to find out.
        //
        // Applies to fonts, panel size, the top bar, and every control height and width, because
        // scaling text alone gives you big words in boxes that clip them.

        /// <summary>1.0 is the old sizing. Clamped to something still usable at both ends.</summary>
        public static float Scale = 1.6f;

        private const float MinScale = 0.8f, MaxScale = 3.0f;
        private const string ScaleKey = "noir.ui.scale";

        /// <summary>Scale a pixel measurement. `S(24)` is "24 pixels at the default size".</summary>
        private static float S(float px) => px * Scale;

        /// <summary>Scale a font size. Rounded, because a fractional point size renders muddy.</summary>
        private static int F(float px) => Mathf.Max(1, Mathf.RoundToInt(px * Scale));

        private static void LoadScale() =>
            Scale = Mathf.Clamp(PlayerPrefs.GetFloat(ScaleKey, 1.6f), MinScale, MaxScale);

        /// <summary>
        /// Ctrl+= larger, Ctrl+- smaller, Ctrl+0 back to default. Held on Ctrl so it cannot
        /// collide with the single-key game shortcuts (Tab, H, F, the digits).
        /// </summary>
        private void ReadScaleKeys()
        {
            var keys = Keyboard.current;
            if (keys == null) return;
            if (!keys.ctrlKey.isPressed) return;

            float was = Scale;
            if (keys.equalsKey.wasPressedThisFrame || keys.numpadPlusKey.wasPressedThisFrame)
                Scale = Mathf.Min(MaxScale, Scale + 0.1f);
            if (keys.minusKey.wasPressedThisFrame || keys.numpadMinusKey.wasPressedThisFrame)
                Scale = Mathf.Max(MinScale, Scale - 0.1f);
            if (keys.digit0Key.wasPressedThisFrame) Scale = 1.6f;

            if (Mathf.Approximately(was, Scale)) return;

            // The styles cache their font sizes, so they have to be rebuilt at the new scale.
            _stylesReady = false;
            PlayerPrefs.SetFloat(ScaleKey, Scale);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Where an inspector goes: a panel in the MIDDLE of the screen rather than a rail down
        /// the right edge.
        ///
        /// The rail was 340px wide against a 5120px monitor, which put everything you were
        /// reading a metre to the right of the thing you had clicked - and made the parcel
        /// editor, which is now a long two-column form, scroll for no reason other than the
        /// width it had been given. Centred, it lands where your eye already is.
        ///
        /// Sized against the window rather than fixed, and capped so it never fills a big screen
        /// edge to edge. The cap GROWS WITH Scale - at 2x text a 760px panel would scroll for no
        /// reason other than the width it was given, which is the mistake the old 340px rail made.
        ///
        /// A RIGHT-HAND RAIL, AND IT WENT BACK TO BEING ONE. It was centred for a while, on the
        /// reasoning that a dialog you look across beats one that covers the map. That is true of
        /// a DIALOG - see DrawHelp, which is still centred and should be - and false of an
        /// inspector, because an inspector is read WHILE looking at the thing it describes. Centred
        /// it lands on top of whatever was just clicked, and on a 5120px ultrawide it sits in the
        /// middle of the screen with two thousand pixels of empty map either side of it.
        ///
        /// What the centring pass got right is kept: ONE rect for all three inspectors instead of
        /// three hand-rolled ones, hit-testing against the real rectangle rather than "anywhere
        /// right of 340px", and a width that scales with the text.
        /// </summary>
        private static Rect PanelRect()
        {
            float w = Mathf.Min(S(760f), Screen.width - 80f);
            float h = Screen.height - BarHeight - S(20f);
            return new Rect(Screen.width - w - S(12f), BarHeight + S(8f), w, h);
        }

        /// <summary>
        /// How tall the clock bar is, in real pixels.
        ///
        /// PUBLIC because anything else drawing at the top of the screen has to sit under it, and
        /// S() is user-tunable at runtime - a second overlay that guessed a fixed 52 px was
        /// covered by the bar the moment the UI scale went up. One source for the number.
        /// </summary>
        public static float BarHeight => S(48f);

        /// <summary>
        /// Keep the whole top bar reachable. The skip buttons sit a long way right, and on a
        /// narrow window they used to slide off the edge with no indication they existed.
        /// </summary>
        private const float MinBarWidth = 900f;

        private void Awake() => _host = GetComponent<VillageHost>();

        private void BuildStyles()
        {
            int pad = Mathf.RoundToInt(S(14f));
            _panel = new GUIStyle(GUI.skin.box);
            _panel.normal.background = SolidTexture(new Color(0.06f, 0.07f, 0.08f, 0.92f));
            _panel.border = new RectOffset(2, 2, 2, 2);
            _panel.padding = new RectOffset(pad, pad, pad, pad);

            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = F(13),
                richText = true,
                wordWrap = true
            };
            _label.normal.textColor = new Color(0.85f, 0.84f, 0.80f);

            _title = new GUIStyle(_label) { fontSize = F(17), fontStyle = FontStyle.Bold };
            _title.normal.textColor = new Color(0.96f, 0.94f, 0.88f);

            // WAS 11px AND GREY ON DARK, which is the least readable thing on the screen and is
            // used for the addresses, the county record, the household and every field label -
            // i.e. most of the actual content. Lifted to 12 BEFORE scaling and paled up from
            // 0.60 to 0.72 so it still reads as secondary without disappearing.
            _small = new GUIStyle(_label) { fontSize = F(12) };
            _small.normal.textColor = new Color(0.72f, 0.72f, 0.70f);

            _button = new GUIStyle(GUI.skin.button) { fontSize = F(13), richText = true };

            // The clock is the one thing you should never have to hunt for.
            _clock = new GUIStyle(_label) { fontSize = F(26), fontStyle = FontStyle.Bold };
            _clock.normal.textColor = new Color(0.98f, 0.96f, 0.90f);

            // Typed text has to be at least as legible as the labels around it.
            GUI.skin.textArea.fontSize = F(13);
            GUI.skin.textField.fontSize = F(13);
            GUI.skin.label.fontSize = F(13);

            _stylesReady = true;
        }

        private static Texture2D SolidTexture(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void OnGUI()
        {
            if (!_scaleLoaded) { LoadScale(); _scaleLoaded = true; }
            ReadScaleKeys();
            if (!_stylesReady) BuildStyles();

            if (_host.LoadError != null)
            {
                GUI.Box(new Rect(S(20f), S(20f), S(720f), S(110f)), GUIContent.none, _panel);
                GUI.Label(new Rect(S(36f), S(34f), S(690f), S(90f)),
                    "<b>The village could not be loaded.</b>\n\n" + _host.LoadError, _label);
                return;
            }
            if (_host.Sim == null) return;

            DrawTopBar();
            DrawInspector();
            DrawHelp();

            // Let the camera know not to treat a click on the panel as a click on the village.
            // Tested against the panel's REAL rectangle now that it is centred - the old check
            // was "anywhere right of 340px from the edge", which was only ever a stand-in for
            // the rail's own bounds and would swallow half the map if it were left alone.
            var mouse = Event.current.mousePosition;
            bool anyPanel = _host.Selected.IsValid || _host.SelectedPlace.IsValid
                         || _host.SelectedParcel.HasValue;

            // THE LAYER PANEL COUNTS AS UI TOO. It is drawn top left by a different behaviour and
            // was missing from this test, so every click on a layer row fell through and opened
            // the parcel inspector on whatever was behind the button - which is worse than a
            // no-op, because it changes the selection while you are trying to change the view.
            PointerOverUI = mouse.y < BarHeight
                         || (anyPanel && PanelRect().Contains(mouse))
                         || LayerPanel.Bounds.Contains(mouse)
                         || PerfHud.Bounds.Contains(mouse);
        }

        private void DrawTopBar()
        {
            GUI.Box(new Rect(S(0f), S(0f), Screen.width, BarHeight), GUIContent.none, _panel);

            var clock = _host.Sim.Clock;
            bool paused = _host.SpeedIndex == 0;

            // ---- the clock, big enough to read without looking for it ----
            GUI.Label(new Rect(S(18f), S(6f), S(200f), S(30f)),
                $"{clock.HourOfDay:00}:{clock.MinuteOfHour:00}", _clock);

            GUI.Label(new Rect(S(112f), S(8f), S(160f), S(18f)),
                $"<b>{GameClock.DayNames[clock.DayOfWeek]}</b>", _label);
            GUI.Label(new Rect(S(112f), S(26f), S(160f), S(16f)),
                $"<color=#8a8a86>day {clock.Day}</color>", _small);

            // ---- what speed, in words, not just a highlighted button ----
            string speedText = paused
                ? "<color=#ff8a5c><b>PAUSED</b></color>"
                : $"<b>{VillageHost.SpeedLabels[_host.SpeedIndex]}</b>";

            GUI.Label(new Rect(S(182f), S(6f), S(90f), S(18f)), "<color=#8a8a86>speed</color>", _small);
            GUI.Label(new Rect(S(182f), S(22f), S(90f), S(22f)), speedText, _label);

            if (_host.Skipping)
                GUI.Label(new Rect(S(182f), S(22f), S(200f), S(22f)), "<color=#ff8a5c><b>skipping…</b></color>", _label);

            float x = S(250f);
            for (int i = 0; i < VillageHost.Speeds.Length; i++)
            {
                bool active = _host.SpeedIndex == i;
                var old = GUI.backgroundColor;
                if (active) GUI.backgroundColor = new Color(0.90f, 0.48f, 0.30f);
                if (GUI.Button(new Rect(x, S(12f), S(42f), S(24f)), VillageHost.SpeedLabels[i], _button))
                    _host.SpeedIndex = i;
                GUI.backgroundColor = old;
                x += S(45f);
            }

            x += S(14f);
            GUI.Label(new Rect(x, S(16f), S(40f), S(20f)), "skip", _small);
            x += S(36f);
            for (int i = 0; i < VillageHost.SkipHours.Length; i++)
            {
                int hour = VillageHost.SkipHours[i];
                // The digit that does the same thing, so the shortcut is discoverable rather
                // than something you have to be told about.
                if (GUI.Button(new Rect(x, S(12f), S(52f), S(24f)), $"{hour:00}:00  <color=#8a8a86>{i + 1}</color>", _button))
                    _host.SkipToHour(hour);
                x += S(56f);
            }

            // The census is the first thing to go on a narrow window - the controls matter
            // more than the readout, and overlapping text is worse than absent text.
            if (Screen.width >= S(MinBarWidth) + S(440f))
                GUI.Label(new Rect(Screen.width - S(430f), S(16f), S(420f), S(20f)), Census(), _small);

            // Which view you are in, far right. In street mode you can lose track of whether
            // WASD is going to pan the camera or walk you into a hedge.
            string mode = _host.ViewName;
            GUI.Label(new Rect(Screen.width - S(150f), S(2f), S(140f), S(16f)),
                $"<color=#8a8a86>{mode}</color>", _small);
        }

        private string Census()
        {
            int asleep = 0, walking = 0, work = 0, school = 0, pub = 0, outside = 0, talking = 0;
            var sim = _host.Sim;
            for (int i = 0; i < sim.AgentCount; i++)
            {
                var a = sim.GetAgent(i);

                // Talking BEFORE Travelling, and that order is the whole point. Travelling stays
                // true through a conversation — a stopped figure still holds its path — so this
                // census used to count two people standing in the road talking to each other as
                // "walking", and Activity.Talking was set by the simulation and read by nothing.
                if (a.Doing == Activity.Talking) { talking++; continue; }
                if (a.Travelling) { walking++; continue; }
                switch (a.Doing)
                {
                    case Activity.Asleep: asleep++; break;
                    case Activity.AtWork: work++; break;
                    case Activity.AtSchool: school++; break;
                    case Activity.AtThePub: pub++; break;
                    case Activity.AtHome: break;
                    default: outside++; break;
                }
            }
            return $"{_host.People.Count} souls   ·   asleep {asleep}   walking {walking}   "
                 + $"talking {talking}   at work {work}   school {school}   pub {pub}   "
                 + $"out {outside}";
        }

        private bool _showHelp;

        /// <summary>
        /// The controls, on H.
        ///
        /// A doc in a folder is the wrong place for this: the moment you need it is the moment
        /// you are already in the game and do not want to leave it.
        /// </summary>
        private void DrawHelp()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.hKey.wasPressedThisFrame) _showHelp = !_showHelp;
            if (!_showHelp) return;

            float w = S(620f), h = S(470f);
            var rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(rect, GUIContent.none, _panel);

            GUILayout.BeginArea(new Rect(rect.x + S(26f), rect.y + S(20f), rect.width - S(52f), rect.height - S(40f)));

            GUILayout.Label("Ashcombe", _title);
            GUILayout.Label("<color=#8a8a86>press H to close</color>", _small);
            GUILayout.Space(S(14f));

            Row("Space", "pause and resume");
            Row("[  ]", "slower / faster  —  ❚❚ ¼ ½ 1 3 10 60 300");
            Row("1 – 6", "skip to 06:00 08:00 12:00 17:00 20:00 23:00");
            GUILayout.Space(S(10f));

            Row("Tab", "<b>overview ⇄ street level</b>");
            Row("WASD", "pan, or walk when at street level");
            Row("Shift", "jog");
            Row("right-drag", "orbit, or look around in the street");
            Row("Q  E", "rotate");
            Row("R  Shift+F", "tilt up / down");
            Row("wheel", "zoom");
            GUILayout.Space(S(10f));

            Row("click", "select somebody");
            Row("F", "follow them");
            GUILayout.Space(S(14f));

            GUILayout.Label(
                "<color=#c9b98a>Roofs lift off as you zoom in from above, so you can watch "
              + "people indoors. They stay on at street level.</color>", _label);
            GUILayout.Space(S(8f));
            GUILayout.Label(
                "<color=#8a8a86>Try: press 5 for 21:00, then Tab, and walk down Back Lane. "
              + "Lit windows are houses where somebody is home and awake.</color>", _label);

            GUILayout.EndArea();
        }

        private void Row(string key, string what)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{key}</b>", _label, GUILayout.Width(S(110f)));
            GUILayout.Label(what, _label);
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// What a plot is: the address, its size, and whatever was authored about it.
        ///
        /// Deliberately not who lives or works there this round - the household, staffing and
        /// "who is inside" sections all read the simulation, and the current pass is about the
        /// town layout and the lot data, not the population sitting on top of it. Set
        /// VillageHost.ShowPeople back to true and restore those sections together when that
        /// becomes the question again; nothing downstream of either was deleted.
        /// </summary>
        private void DrawPlaceInspector(Place place)
        {
            var rect = PanelRect();
            GUI.Box(rect, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(rect.x + S(20f), rect.y + S(16f), rect.width - S(40f), rect.height - S(32f)));

            var sim = _host.Sim;
            var kind = PlaceKindTable.Current.Row(place.Kind);

            // The name IS the address - see relay-rossville.py and Content/parcels.txt.
            GUILayout.Label(place.Name, _title);
            GUILayout.Label($"{Article(kind.Name)}   ·   {LotSize(place)}", _small);

            // The REAL county record for this lot, when the two disagree. Generated places keep
            // their own synthetic numbering - "408 Holmes Ave" is a fixed story anchor and
            // nothing here should ever move it - but the player asked to see what the tax rolls
            // actually say, and silently hiding a mismatch would be worse than showing one.
            int parcelIdForAddr = ParcelIndex.FindFor(place)?.Id ?? -1;
            var county = parcelIdForAddr >= 0 ? CountyRecord.For(parcelIdForAddr) : null;
            if (county?.Address != null && !SameAddress(county.Address, place.Name))
                GUILayout.Label($"<color=#8a8a86>county record: {county.Address}</color>", _small);

            GUILayout.Space(S(10f));

            // The line somebody wrote about this building when they put it in the map.
            if (!string.IsNullOrWhiteSpace(place.Human))
            {
                GUILayout.Label($"<i>{place.Human}</i>", _label);
                GUILayout.Space(S(10f));
            }

            // ---- when it is open ----
            if (place.Hours.Count > 0)
            {
                var clock = sim.Clock;
                bool open = false;
                foreach (var window in place.Hours)
                    if (window.Covers(clock.MinuteOfDay, clock.DayOfWeek)) { open = true; break; }

                GUILayout.Label(open
                    ? "<color=#9fd08a><b>open now</b></color>"
                    : "<color=#a8817a><b>closed</b></color>", _label);
                foreach (var window in place.Hours)
                    GUILayout.Label($"<color=#8a8a86>{window}</color>", _small);
                GUILayout.Space(S(10f));
            }

            if (place.Units > 1)
                GUILayout.Label($"<color=#8a8a86>{place.Units} separate homes</color>", _small);
            if (place.JobSlots > 0)
                GUILayout.Label($"<color=#8a8a86>{place.JobSlots} job slots</color>", _small);

            int parcelId = ParcelIndex.FindFor(place)?.Id ?? -1;
            // NO FlexibleSpace BEFORE THE BUTTON when there is a note editor above it: the
            // editor's scroll view expands too, and two expanding siblings split the leftover
            // height between them - which is how a form that fits ends up half-scrolled anyway.
            if (parcelId >= 0) DrawNoteEditor(parcelId);
            else GUILayout.FlexibleSpace();

            if (GUILayout.Button("close", _button, GUILayout.Width(S(70f)), GUILayout.Height(S(26f))))
                _host.SelectedPlace = PlaceId.None;

            GUILayout.EndArea();
        }

        /// <summary>
        /// Who lived on a lot, how many of them, what they were like, and the house shape if it
        /// is still clear in memory - everything the county's own data cannot know. Shared by the
        /// place panel and the bare-parcel panel, keyed to the real parcel under either. See
        /// ParcelNotes for why an address is not what this is filed under, and for the file
        /// format a saved household is written as.
        /// </summary>
        private string _draftCharacter = "", _draftNames = "";
        private int _draftAdults, _draftKids;
        private ParcelNotes.Zoning _draftZoning;
        private ParcelNotes.HousingType _draftHousing;
        private ParcelNotes.Quality _draftQuality;
        private int _draftStories;
        private bool _draftBasement;
        private int _draftBedrooms, _draftBaths, _draftHalfBaths, _draftSquareFeet, _draftYearBuilt;

        /// <summary>
        /// Loads the drafts from whatever is on file the first time a given parcel is shown, so
        /// the form below can be LIVE rather than hidden behind an edit button.
        ///
        /// It used to be modal - a read-only summary with `edit` and `randomize` under it, and
        /// every field that was not a name or a household count lived inside the edit branch. The
        /// summary line for zoning only drew when something was already set, so on a fresh parcel
        /// (the overwhelming majority: 468 of 794 have nothing on them) the panel showed no zoning,
        /// no stories, no basement and no housing type at all, and nothing suggesting that clicking
        /// `edit` would reveal them. Everything is on screen now.
        /// </summary>
        private void SeedDrafts(int parcelId)
        {
            if (_noteDraftFor == parcelId) return;
            _noteDraftFor = parcelId;

            var saved = ParcelNotes.For(parcelId);
            _draftAdults = saved?.Adults ?? 0;
            _draftKids = saved?.Kids ?? 0;
            _draftNames = saved?.Names ?? "";
            _draftCharacter = saved?.Character ?? "";
            _draftQuality = saved?.Condition ?? ParcelNotes.Quality.Unset;
            _draftStories = saved?.Stories ?? 0;
            _draftBasement = saved?.Basement ?? false;
            _draftBedrooms = saved?.Bedrooms ?? 0;
            _draftBaths = saved?.Baths ?? 0;
            _draftHalfBaths = saved?.HalfBaths ?? 0;
            _draftSquareFeet = saved?.SquareFeet ?? 0;
            _draftYearBuilt = saved?.YearBuilt ?? 0;

            // THE COUNTY'S ANSWER IS THE STARTING VALUE where nobody has authored one. Its class
            // code says what the assessor thinks the lot is, for all 776 matched parcels, which
            // beats making somebody set `residential` by hand 517 times. An authored value always
            // wins - this only fills a blank.
            var county = CountyRecord.For(parcelId);
            _draftZoning = saved?.Zoning ?? ParcelNotes.Zoning.Unset;
            if (_draftZoning == ParcelNotes.Zoning.Unset && county != null)
                _draftZoning = county.Zoning;

            _draftHousing = saved?.Housing ?? ParcelNotes.HousingType.Unset;
            if (_draftHousing == ParcelNotes.HousingType.Unset && county != null)
                _draftHousing = county.Housing;
        }

        /// <summary>Everything in the drafts, as a note ready to write. The footprint is not a
        /// draft - FootprintDrawer owns it and saves it separately - so it is carried across from
        /// whatever is already on file rather than being overwritten with nothing.</summary>
        private ParcelNotes.Note DraftNote(ParcelNotes.Note saved) => new ParcelNotes.Note
        {
            Adults = _draftAdults, Kids = _draftKids, Names = _draftNames,
            Character = _draftCharacter, Footprint = saved?.Footprint,
            Zoning = _draftZoning, Housing = _draftHousing, Condition = _draftQuality,
            Stories = _draftStories, Basement = _draftBasement,
            Bedrooms = _draftBedrooms, Baths = _draftBaths, HalfBaths = _draftHalfBaths,
            SquareFeet = _draftSquareFeet, YearBuilt = _draftYearBuilt
        };

        private void DrawNoteEditor(int parcelId)
        {
            if (parcelId < 0) return;
            SeedDrafts(parcelId);
            GUILayout.Space(S(10f));

            var drawer = _host.Footprint;
            bool drawingHere = drawer != null && drawer.Active && drawer.TargetParcelId == parcelId;
            var saved = ParcelNotes.For(parcelId);

            _noteScroll = GUILayout.BeginScrollView(_noteScroll);

            // TWO COLUMNS, because the panel is centred and 760 wide now rather than a 340px
            // rail. WHO LIVES HERE on the left and WHAT THE BUILDING IS on the right: they are
            // two separate questions about the same lot, and stacking them made a form you had
            // to scroll past the people to reach the house.
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(S(330f)));

            // ---- who lives here ----
            //
            // WHAT THE GENERATOR SAYS, until somebody authors something better. Households.For
            // works a 1991 family out of the county's own record for this lot - owner-occupied
            // or rented, over-65 exemption or not, how much dwelling is assessed - with the
            // people themselves drawn from names.txt. Shown greyed and above the fields so it
            // reads as a suggestion rather than as saved data; typing anything below replaces
            // it for this parcel and nothing else.
            var generated = Households.For(parcelId);
            if (generated != null && string.IsNullOrWhiteSpace(_draftNames))
            {
                GUILayout.Label($"<color=#8a8a86>in {GameClock.EpochYear}, {generated.Family} "
                              + $"{(generated.Rented ? "rented here" : "lived here")}</color>", _small);
                foreach (var person in generated.Members)
                    GUILayout.Label($"<color=#75736e>   {person.Forename} {generated.Surname}, "
                                  + $"{person.Age}</color>", _small);
                if (GUILayout.Button("use this household", _button, GUILayout.Height(S(22f))))
                {
                    var lines = new System.Text.StringBuilder();
                    foreach (var person in generated.Members)
                        lines.Append(person.Forename).Append(' ').Append(generated.Surname).Append('\n');
                    _draftNames = lines.ToString().TrimEnd('\n');
                    _draftAdults = generated.Adults;
                    _draftKids = generated.Kids;
                }
                GUILayout.Space(S(6f));
            }

            GUILayout.Label("<color=#8a8a86>household</color>", _small);
            GUILayout.BeginHorizontal();
            GUILayout.Label("adults", _small, GUILayout.Width(S(50f)));
            if (GUILayout.Button("-", _button, GUILayout.Width(S(28f)))) _draftAdults = Mathf.Max(0, _draftAdults - 1);
            GUILayout.Label(_draftAdults.ToString(), _label, GUILayout.Width(S(20f)));
            if (GUILayout.Button("+", _button, GUILayout.Width(S(28f)))) _draftAdults++;
            GUILayout.Space(S(10f));
            GUILayout.Label("kids", _small, GUILayout.Width(S(34f)));
            if (GUILayout.Button("-", _button, GUILayout.Width(S(28f)))) _draftKids = Mathf.Max(0, _draftKids - 1);
            GUILayout.Label(_draftKids.ToString(), _label, GUILayout.Width(S(20f)));
            if (GUILayout.Button("+", _button, GUILayout.Width(S(28f)))) _draftKids++;
            GUILayout.EndHorizontal();

            GUILayout.Space(S(4f));
            GUILayout.Label("<color=#8a8a86>names, one per line</color>", _small);
            _draftNames = GUILayout.TextArea(_draftNames, GUILayout.Height(S(50f)));

            GUILayout.Space(S(4f));
            GUILayout.Label("<color=#8a8a86>what they're like - the seed for behaviour</color>", _small);
            _draftCharacter = GUILayout.TextArea(_draftCharacter, GUILayout.Height(S(60f)));

            // ---- what the lot is: the right-hand column ----
            GUILayout.EndVertical();
            GUILayout.Space(S(24f));
            GUILayout.BeginVertical(GUILayout.Width(S(330f)));

            GUILayout.Label("<color=#8a8a86>zoning</color>", _small);
            if (GUILayout.Button(Pretty(_draftZoning), _button, GUILayout.Height(S(24f))))
                _draftZoning = Cycle(_draftZoning);

            if (_draftZoning == ParcelNotes.Zoning.Residential)
            {
                GUILayout.Space(S(4f));
                GUILayout.Label("<color=#8a8a86>housing type</color>", _small);
                if (GUILayout.Button(Pretty(_draftHousing), _button, GUILayout.Height(S(24f))))
                    _draftHousing = Cycle(_draftHousing);
            }

            GUILayout.Space(S(4f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("stories", _small, GUILayout.Width(S(50f)));
            if (GUILayout.Button("-", _button, GUILayout.Width(S(28f)))) _draftStories = Mathf.Max(0, _draftStories - 1);
            GUILayout.Label(_draftStories.ToString(), _label, GUILayout.Width(S(20f)));
            if (GUILayout.Button("+", _button, GUILayout.Width(S(28f)))) _draftStories++;
            GUILayout.Space(S(10f));
            if (GUILayout.Button(_draftBasement ? "basement: yes" : "basement: no", _button))
                _draftBasement = !_draftBasement;
            GUILayout.EndHorizontal();

            GUILayout.Space(S(4f));
            GUILayout.Label("<color=#8a8a86>condition</color>", _small);
            if (GUILayout.Button(Pretty(_draftQuality), _button, GUILayout.Height(S(24f))))
                _draftQuality = Cycle(_draftQuality);

            // ---- the house itself ----
            GUILayout.Space(S(8f));
            GUILayout.Label("<color=#8a8a86>rooms</color>", _small);
            Counter("beds", ref _draftBedrooms, 1);
            Counter("baths", ref _draftBaths, 1);
            Counter("half", ref _draftHalfBaths, 1);

            GUILayout.Space(S(4f));
            Counter("sq ft", ref _draftSquareFeet, 50);
            Counter("built", ref _draftYearBuilt, 1, 1830);

            // WHAT THE COUNTY WOULD SAY ABOUT THAT NUMBER. The assessor publishes no square
            // footage anywhere, but it does publish what the dwelling is assessed at, and
            // Illinois assesses at a third of market - so market value over the entered area is
            // a dollars-per-square-foot that can be sanity-checked by eye. Rossville runs about
            // $55-95/sqft; well outside that means the area or the condition is wrong.
            var countyNow = CountyRecord.For(parcelId);
            if (countyNow != null && countyNow.HasBuilding && _draftSquareFeet > 0)
            {
                int perFoot = countyNow.MarketValue / _draftSquareFeet;
                bool odd = perFoot < 40 || perFoot > 130;
                GUILayout.Label($"<color={(odd ? "#c9a08a" : "#8a8a86")}>${perFoot}/sq ft against the "
                              + $"county's ${countyNow.MarketValue:N0}"
                              + (odd ? " - outside what this town sells for" : "") + "</color>", _small);
            }

            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();

            // ---- committing ----
            //
            // SAVED IS NOT AUTOMATIC. Every control above writes to a draft and nothing else, so
            // cycling zoning to see what the options are does not commit anything until asked.
            bool dirty = saved == null
                ? DraftIsAnything()
                : (_draftAdults != saved.Adults || _draftKids != saved.Kids
                || _draftNames != saved.Names || _draftCharacter != saved.Character
                || _draftZoning != saved.Zoning || _draftHousing != saved.Housing
                || _draftQuality != saved.Condition
                || _draftStories != saved.Stories || _draftBasement != saved.Basement
                || _draftBedrooms != saved.Bedrooms || _draftBaths != saved.Baths
                || _draftHalfBaths != saved.HalfBaths || _draftSquareFeet != saved.SquareFeet
                || _draftYearBuilt != saved.YearBuilt);

            GUILayout.Space(S(4f));
            GUILayout.BeginHorizontal();
            var wasColour = GUI.backgroundColor;
            if (dirty) GUI.backgroundColor = new Color(0.90f, 0.48f, 0.30f);
            if (GUILayout.Button(dirty ? "save *" : "save", _button, GUILayout.Height(S(24f))))
            {
                ParcelNotes.Save(parcelId, DraftNote(saved));
                _noteDraftFor = int.MinValue;     // reload from what actually landed on disk
            }
            GUI.backgroundColor = wasColour;

            if (GUILayout.Button("randomize", _button, GUILayout.Height(S(24f))))
                RandomizeHousehold(out _draftAdults, out _draftKids, out _draftNames, out _draftCharacter);
            if (GUILayout.Button("revert", _button, GUILayout.Height(S(24f))))
                _noteDraftFor = int.MinValue;
            GUILayout.EndHorizontal();

            GUILayout.Space(S(6f));

            if (!drawingHere)
            {
                bool hasShape = saved?.Footprint != null;
                if (GUILayout.Button(hasShape ? "redraw house" : "draw house", _button, GUILayout.Height(S(24f))))
                    drawer?.Begin(parcelId, saved?.Footprint);
            }
            else
            {
                GUILayout.Label($"<color=#c9b98a>drawing - click the ground to place a corner "
                               + $"({drawer.Points.Count} so far)</color>", _small);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("undo point", _button, GUILayout.Height(S(24f))))
                    drawer.UndoLast();
                if (GUILayout.Button("finish", _button, GUILayout.Height(S(24f))))
                    drawer.Finish(parcelId);
                if (GUILayout.Button("cancel", _button, GUILayout.Height(S(24f))))
                    drawer.Cancel();
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>Steps an enum's field on to its next value, wrapping round - a click-to-cycle
        /// button in place of a dropdown IMGUI does not have. Unset is always index 0, so cycling
        /// all the way round doubles as a way to clear the field.</summary>
        private static T Cycle<T>(T current) where T : struct, System.Enum
        {
            var values = (T[])System.Enum.GetValues(typeof(T));
            int idx = System.Array.IndexOf(values, current);
            return values[(idx + 1) % values.Length];
        }

        private static string Pretty(ParcelNotes.Zoning z)
        {
            switch (z)
            {
                case ParcelNotes.Zoning.Residential: return "residential";
                case ParcelNotes.Zoning.Commercial: return "commercial";
                case ParcelNotes.Zoning.Industrial: return "industrial";
                case ParcelNotes.Zoning.Civic: return "civic";
                case ParcelNotes.Zoning.Agricultural: return "agricultural";
                case ParcelNotes.Zoning.Vacant: return "vacant";
                default: return "not zoned";
            }
        }

        /// <summary>A labelled -/+ row. `step` is how much a click moves it and `floor` is the
        /// lowest it will go, which is 0 for a count and a year the town could actually have been
        /// built in for a date - a year field that steps down through 3, 2, 1, 0 is useless.</summary>
        private void Counter(string label, ref int value, int step, int floor = 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _small, GUILayout.Width(S(44f)));
            if (GUILayout.Button("-", _button, GUILayout.Width(S(28f))))
                value = value == 0 ? floor : Mathf.Max(floor, value - step);
            GUILayout.Label(value == 0 ? "<color=#75736e>-</color>" : value.ToString(),
                            _label, GUILayout.Width(S(46f)));
            if (GUILayout.Button("+", _button, GUILayout.Width(S(28f))))
                value = value == 0 ? Mathf.Max(floor, step) : value + step;
            GUILayout.EndHorizontal();
        }

        /// <summary>Whether the drafts hold anything worth saving, for a parcel that has nothing
        /// on file yet - mirrors ParcelNotes.Save's own emptiness test, so `save *` never lights
        /// up for a note that would be discarded as empty the moment it was written.</summary>
        private bool DraftIsAnything() =>
            !string.IsNullOrWhiteSpace(_draftCharacter) || !string.IsNullOrWhiteSpace(_draftNames)
            || _draftAdults != 0 || _draftKids != 0
            || _draftZoning != ParcelNotes.Zoning.Unset
            || _draftHousing != ParcelNotes.HousingType.Unset
            || _draftQuality != ParcelNotes.Quality.Unset
            || _draftStories != 0 || _draftBasement
            || _draftBedrooms != 0 || _draftBaths != 0 || _draftHalfBaths != 0
            || _draftSquareFeet != 0 || _draftYearBuilt != 0;

        private static string Pretty(ParcelNotes.Quality q)
        {
            switch (q)
            {
                case ParcelNotes.Quality.Derelict: return "derelict";
                case ParcelNotes.Quality.Poor: return "poor";
                case ParcelNotes.Quality.Fair: return "fair";
                case ParcelNotes.Quality.Good: return "good";
                case ParcelNotes.Quality.Excellent: return "excellent";
                default: return "condition not recorded";
            }
        }

        private static string Pretty(ParcelNotes.HousingType h)
        {
            switch (h)
            {
                case ParcelNotes.HousingType.SingleFamily: return "single-family";
                case ParcelNotes.HousingType.Duplex: return "duplex";
                case ParcelNotes.HousingType.Apartment: return "apartment";
                case ParcelNotes.HousingType.ApartmentComplex: return "apartment complex";
                default: return "unspecified";
            }
        }

        private static NameTable _names;

        /// <summary>
        /// A plausible household for a lot nobody remembers, using the same names.txt and
        /// particulars.txt every generated citizen already draws from - a randomized family
        /// should read as one more Rossville household, not as a different kind of content.
        /// </summary>
        private void RandomizeHousehold(out int adults, out int kids, out string names, out string character)
        {
            if (_names == null)
            {
                try { _names = NameTable.Parse(ContentLoader.Read("names.txt")); }
                catch { _names = null; }
            }

            var rng = new System.Random();
            adults = rng.NextDouble() < 0.3 ? 1 : 2;
            double kidRoll = rng.NextDouble();
            kids = kidRoll < 0.4 ? 0 : kidRoll < 0.7 ? 1 : kidRoll < 0.9 ? 2 : 3;

            var lines = new System.Collections.Generic.List<string>();
            character = "";
            if (_names != null && _names.Surnames.Count > 0
                && _names.Male.Count > 0 && _names.Female.Count > 0)
            {
                string surname = _names.Surnames[rng.Next(_names.Surnames.Count)];
                string headFirst = null;
                bool headIsMale = rng.NextDouble() < 0.5;
                for (int i = 0; i < adults; i++)
                {
                    // The second adult is the opposite sex of the first - not a rule this town
                    // enforces elsewhere, just the common case for a randomized guess.
                    bool male = i == 0 ? headIsMale : !headIsMale;
                    string first = male ? _names.Male[rng.Next(_names.Male.Count)]
                                        : _names.Female[rng.Next(_names.Female.Count)];
                    if (i == 0) headFirst = first;
                    lines.Add($"{first} {surname}");
                }
                for (int i = 0; i < kids; i++)
                {
                    string first = rng.NextDouble() < 0.5
                        ? _names.Male[rng.Next(_names.Male.Count)]
                        : _names.Female[rng.Next(_names.Female.Count)];
                    lines.Add($"{first} {surname}");
                }

                if (_host?.Particulars != null && _host.Particulars.Count > 0 && lines.Count > 0)
                {
                    int idx = rng.Next(_host.Particulars.Count);
                    character = _host.Particulars.Sentence(headFirst ?? lines[0].Split(' ')[0], idx);
                }
            }
            names = string.Join("\n", lines);
        }

        /// <summary>
        /// A real county lot with no address on it - most of the plan. 468 of Rossville's 794
        /// surveyed parcels never got a house or a business generated on them; before this a
        /// click there found nothing, which is most of what's visible on the plan reading as
        /// unclickable rather than as undeveloped land.
        ///
        /// THE REAL ADDRESS FIRST, THE ESTIMATE ONLY IF THERE IS NONE. CountyRecord is Vermilion
        /// County's own tax record, matched to this exact parcel - see Content/parcel-county.txt.
        /// StreetAddressing.Estimate's "400 block of X Ave" was always a guess for a lot with no
        /// real answer on file, and showing it even where the county DOES have one was
        /// indistinguishable from the guess being wrong.
        /// </summary>
        private void DrawParcelInspector(ParcelIndex.Parcel parcel)
        {
            var rect = PanelRect();
            GUI.Box(rect, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(rect.x + S(20f), rect.y + S(16f), rect.width - S(40f), rect.height - S(32f)));

            float wFt = parcel.Bounds.width * MetresToFeet;
            float hFt = parcel.Bounds.height * MetresToFeet;
            var centre = new Vector2(parcel.Bounds.x + parcel.Bounds.width / 2f,
                                     parcel.Bounds.y + parcel.Bounds.height / 2f);

            var county = CountyRecord.For(parcel.Id);
            string confirmed = county?.Address;
            string approx = confirmed == null ? StreetAddressing.Estimate(_host.World, centre) : null;

            GUILayout.Label(confirmed ?? approx ?? "Undeveloped lot", _title);

            string status = confirmed != null ? "confirmed address" : approx != null
                          ? "estimated address" : "no address on file";
            if (county != null && !county.HasBuilding) status += ", no house built";
            GUILayout.Label($"{status}   ·   {Mathf.RoundToInt(wFt)} x {Mathf.RoundToInt(hFt)} ft", _small);

            if (county != null)
            {
                if (county.Pin != null)
                    GUILayout.Label($"<color=#8a8a86>PIN {county.Pin}"
                                  + (county.Acres > 0f ? $"   ·   {county.Acres:0.00} acres" : "")
                                  + "</color>", _small);
                if (county.ClassName != null)
                    GUILayout.Label($"<color=#8a8a86>assessed as {county.ClassName.ToLowerInvariant()}"
                                  + $" ({county.ClassCode})</color>", _small);
                if (county.HasBuilding)
                    GUILayout.Label($"<color=#8a8a86>building assessed ${county.DwellingValue:N0}"
                                  + $"   ·   about ${county.MarketValue:N0} market</color>", _small);

                string who = county.Occupied == CountyRecord.Occupancy.Owner ? "owner-occupied"
                           : county.Occupied == CountyRecord.Occupancy.Absentee
                             ? "tax bill goes elsewhere - likely rented" : null;
                if (who != null)
                    GUILayout.Label($"<color=#8a8a86>{who}"
                                  + (county.Over65 ? ", over-65 exemption" : "") + "</color>", _small);
            }

            GUILayout.Space(S(10f));
            GUILayout.Label("<color=#8a8a86>A real surveyed parcel with no house or business "
                           + "built on it.</color>", _label);

            // No FlexibleSpace here - the note editor's own scroll view takes the slack. See
            // DrawPlaceInspector for why two expanding siblings is the wrong shape.
            DrawNoteEditor(parcel.Id);

            if (GUILayout.Button("close", _button, GUILayout.Width(S(70f)), GUILayout.Height(S(26f))))
                _host.SelectedParcel = null;

            GUILayout.EndArea();
        }

        private const float MetresToFeet = 3.28084f;

        /// <summary>
        /// The real lot, not the model's footprint. Rossville's houses are cardboard boxes
        /// standing in for an Illinois frame house the asset pack does not own (see
        /// CityOutlines) - their generated Bounds is a placeholder's size, not the address's.
        /// The county's own parcel, underfoot, is the actual answer to "how big is this lot",
        /// so this looks there first and only falls back to the footprint for places the parcel
        /// data does not cover - open ground, the railway corridor, anything off 794 records.
        ///
        /// This is America: feet, not metres, and the player never sees the conversion happen.
        /// </summary>
        private static string LotSize(Place place)
        {
            var b = place.Bounds;
            var parcel = ParcelIndex.FindFor(place);

            float wFt, hFt;
            string what;
            if (parcel.HasValue)
            {
                wFt = parcel.Value.Bounds.width * MetresToFeet;
                hFt = parcel.Value.Bounds.height * MetresToFeet;
                what = "lot";
            }
            else
            {
                wFt = b.W * MetresToFeet;
                hFt = b.H * MetresToFeet;
                what = "footprint";
            }

            return $"{Mathf.RoundToInt(wFt)} x {Mathf.RoundToInt(hFt)} ft {what}";
        }

        private static readonly string[] StreetSuffixes = { "ave", "st", "dr", "ct", "rd", "pl", "place" };

        /// <summary>Whether a generated place's own name and the county's confirmed address are
        /// the same lot under two spellings rather than a genuine disagreement - the county's
        /// PropertyAddress field never carries a street-type suffix (see Content/parcel-
        /// addresses.txt), so a place's own "408 Holmes Ave" needs that word dropped before it is
        /// fair to compare against the record's "408 Holmes".</summary>
        private static bool SameAddress(string county, string generated)
        {
            var words = generated.Trim().Split(' ');
            if (words.Length > 1)
            {
                string last = words[words.Length - 1].ToLowerInvariant();
                foreach (var suffix in StreetSuffixes)
                    if (last == suffix)
                    {
                        generated = string.Join(" ", words, 0, words.Length - 1);
                        break;
                    }
            }
            return string.Equals(county.Trim(), generated.Trim(), System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>"a diner", "an apartment" - the kind's own name, read out loud.</summary>
        private static string Article(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return "a place";
            bool vowel = "aeiou".IndexOf(char.ToLowerInvariant(kind[0])) >= 0;
            return (vowel ? "an " : "a ") + kind;
        }

        private void DrawInspector()
        {
            var place = _host.SelectedPlaceModel;
            if (place != null) { DrawPlaceInspector(place); return; }

            if (_host.SelectedParcel.HasValue) { DrawParcelInspector(_host.SelectedParcel.Value); return; }

            var citizen = _host.SelectedCitizen;
            if (citizen == null)
            {
                GUI.Label(new Rect(S(16f), Screen.height - S(62f), S(900f), S(22f)),
                    "<b>Tab</b> street level   ·   right-drag or <b>Q</b>/<b>E</b> orbit   ·   "
                  + "<b>R</b>/<b>Shift+F</b> tilt   ·   <b>WASD</b> move   ·   wheel zoom", _small);
                GUI.Label(new Rect(S(16f), Screen.height - S(40f), S(900f), S(22f)),
                    "<b>Space</b> pause   ·   <b>[</b> <b>]</b> speed   ·   <b>1</b>–<b>6</b> skip to hour   ·   "
                  + "click anyone, any building, or any lot   ·   <b>F</b> follow   ·   <b>H</b> for help", _small);
                return;
            }

            var rect = PanelRect();
            GUI.Box(rect, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(rect.x + S(20f), rect.y + S(16f), rect.width - S(40f), rect.height - S(32f)));

            var sim = _host.Sim;
            var agent = sim.GetAgent(citizen.Id);
            var household = _host.People.HouseholdOf(citizen);

            GUILayout.Label(citizen.FullName, _title);
            GUILayout.Label($"{citizen.AgeIn(VillageHost.Year)}   ·   {Stage(citizen)}", _small);
            GUILayout.Space(S(10f));

            // ---- what they are doing, right now ----
            string doing = agent.Travelling
                ? $"walking to <b>{_host.World.GetPlace(sim.CurrentBlock(citizen.Id).Where)?.Name}</b>"
                : $"{Verb(agent.Doing)} <b>{_host.World.GetPlace(agent.At)?.Name}</b>";
            GUILayout.Label(doing, _label);
            GUILayout.Space(S(10f));

            // ---- who they are ----
            GUILayout.Label($"<color=#8a8a86>lives at</color>  {_host.World.GetPlace(citizen.Home)?.Name}", _label);
            if (household != null && household.Size > 1)
            {
                var sb = new StringBuilder("<color=#8a8a86>with</color>  ");
                bool first = true;
                foreach (var id in household.Members)
                {
                    if (id == citizen.Id) continue;
                    var other = _host.People.Get(id);
                    if (!first) sb.Append(", ");
                    sb.Append(other.Forename);
                    if (other.Surname != citizen.Surname) sb.Append(' ').Append(other.Surname);
                    first = false;
                }
                GUILayout.Label(sb.ToString(), _label);
            }
            else
            {
                GUILayout.Label("<color=#8a8a86>lives alone</color>", _label);
            }

            if (citizen.Works)
                GUILayout.Label($"<color=#8a8a86>works as</color>  {Pretty(citizen.Job)} "
                              + $"<color=#8a8a86>at</color> {_host.World.GetPlace(citizen.Work)?.Name}"
                              + (citizen.Shift > 0 ? "  <color=#8a8a86>(late shift)</color>" : ""), _label);
            else if (citizen.IsChildIn(VillageHost.Year)) GUILayout.Label("<color=#8a8a86>at school</color>", _label);
            else if (citizen.StageIn(VillageHost.Year) == LifeStage.Elder) GUILayout.Label("<color=#8a8a86>retired</color>", _label);

            GUILayout.Space(S(12f));

            // ---- the particulars: the whole reason this is worth watching ----
            foreach (int p in citizen.Particulars)
                GUILayout.Label("<color=#c9b98a>" + _host.Particulars.Sentence(citizen.Forename, p) + "</color>", _label);

            GUILayout.Space(S(12f));
            _showPlan = GUILayout.Toggle(_showPlan, "  today", _label);

            if (_showPlan)
            {
                _scroll = GUILayout.BeginScrollView(_scroll);
                var plan = sim.PlanFor(citizen.Id);
                int now = sim.Clock.MinuteOfDay;
                foreach (var b in plan.Blocks)
                {
                    bool current = b.Covers(now);
                    string line = $"{b.StartMinute / 60:00}:{b.StartMinute % 60:00}  "
                                + $"{Verb(b.What)} {_host.World.GetPlace(b.Where)?.Name}";
                    GUILayout.Label(current ? $"<b>{line}</b>" : $"<color=#75736e>{line}</color>", _small);
                }
                GUILayout.EndScrollView();
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_host.Following ? "stop following" : "follow  (F)", _button, GUILayout.Height(S(26f))))
                _host.Following = !_host.Following;
            if (GUILayout.Button("close", _button, GUILayout.Width(S(70f)), GUILayout.Height(S(26f))))
            {
                _host.Selected = CitizenId.None;
                _host.Following = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private static string Stage(Citizen c)
        {
            switch (c.StageIn(VillageHost.Year))
            {
                case LifeStage.Child: return "a child";
                case LifeStage.Elder: return "elderly";
                default: return "an adult";
            }
        }

        private static string Pretty(Occupation o)
        {
            switch (o)
            {
                case Occupation.MillHand: return "a mill hand";
                case Occupation.Farmer: return "a farmer";
                case Occupation.FarmHand: return "a farm hand";
                case Occupation.Teacher: return "a teacher";
                case Occupation.Shopkeeper: return "a shopkeeper";
                case Occupation.Postmaster: return "the postmaster";
                case Occupation.Publican: return "a publican";
                case Occupation.Mechanic: return "a mechanic";
                case Occupation.Doctor: return "the doctor";
                case Occupation.Nurse: return "a nurse";
                case Occupation.Verger: return "the verger";
                case Occupation.Caretaker: return "the caretaker";

                // A trade the enum has never heard of is one kinds.txt minted, and the authored
                // word is exactly the right thing to show. This used to return an em-dash, so
                // the works opened and its thirty-three machinists were each described to the
                // player as nothing at all - the largest employer in the village, invisible.
                //
                // The cases above stay because they carry the article and the wording a switch
                // can give and a table cannot: "the postmaster" and "the doctor" are definite
                // because a village has one, and that is a judgement about the place.
                default:
                    string name = Occupations.NameOf(o);
                    if (string.IsNullOrEmpty(name) || name == nameof(Occupation.None)) return "—";
                    name = name.ToLowerInvariant();
                    return ("aeiou".IndexOf(name[0]) >= 0 ? "an " : "a ") + name;
            }
        }

        private static string Verb(Activity a)
        {
            switch (a)
            {
                case Activity.Asleep: return "asleep at";
                case Activity.AtHome: return "at home in";
                case Activity.AtWork: return "at work in";
                case Activity.AtSchool: return "at";
                case Activity.Shopping: return "in";
                case Activity.AtThePub: return "in";
                case Activity.AtChurch: return "at";
                case Activity.Visiting: return "visiting";
                case Activity.Walking: return "walking on";
                case Activity.AtThePlayground: return "playing at";
                case Activity.OnTheAllotment: return "digging at";
                case Activity.TravellingTo: return "on the way to";
                case Activity.Talking: return "stopped to talk on";
                default: return "at";
            }
        }
    }
}
