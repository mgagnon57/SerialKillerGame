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
            DrawHoverTip();

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

            // ---- AND THE KEYBOARD, which was not guarded at all ----
            //
            // PointerOverUI is a MOUSE test. It stops a click on the panel falling through to
            // the map and does nothing whatever about typing, so every game shortcut stayed live
            // while a text field had focus. Typing "hardware" into the business field panned the
            // camera left, tilted it, panned right, panned forward and orbited - a, r, d, w, e
            // are all bound - and typing a house number jumped the clock, because 1 to 6 are the
            // skip-to-hour keys. Tab dropped to street level. Space paused the town.
            //
            // GUIUtility.keyboardControl is IMGUI's focused control, and zero when nothing has
            // focus. Everything that reads the keyboard for the GAME now asks this first.
            KeyboardCaptured = GUIUtility.keyboardControl != 0;

            // ---- WAYS OUT, and a sign saying so ----
            //
            // The guard above is correct and was, on its own, a trap: with a field focused the
            // camera and the clock stop answering, nothing explains why, and the only exit was a
            // key nobody had been told about. Reported exactly that way - "I did not know, it
            // never told me, and then I could not click it any more like it locked it."
            //
            // So: Escape releases it, clicking the MAP releases it, and while it is held there
            // is a line on screen saying what is happening. A mode with no indicator is a bug
            // however well it works.
            if (KeyboardCaptured && Event.current.type == EventType.KeyDown
                && Event.current.keyCode == KeyCode.Escape)
            {
                GUIUtility.keyboardControl = 0;
                KeyboardCaptured = false;
                Event.current.Use();
            }

            // Clicking anywhere that is not the panel means you are done typing. This is what
            // most people will reach for before they think of Escape.
            if (KeyboardCaptured && !PointerOverUI
                && Event.current.type == EventType.MouseDown)
            {
                GUIUtility.keyboardControl = 0;
                KeyboardCaptured = false;
            }

            if (KeyboardCaptured) DrawTypingNotice();
        }

        /// <summary>The sign that says why the keys have stopped working.</summary>
        private void DrawTypingNotice()
        {
            const string text = "typing — the camera and clock keys are paused   ·   "
                              + "Esc or click the map to give them back";
            var content = new GUIContent(text);
            var size = _small.CalcSize(content);
            float w = size.x + S(28f), h = size.y + S(14f);
            var rect = new Rect((Screen.width - w) * 0.5f, BarHeight + S(10f), w, h);

            var was = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.90f, 0.62f, 0.25f, 0.95f);
            GUI.Box(rect, GUIContent.none, _panel);
            GUI.backgroundColor = was;
            GUI.Label(new Rect(rect.x + S(14f), rect.y + S(7f), rect.width, rect.height),
                      text, _small);
        }

        /// <summary>
        /// True while a text field has the keyboard, so the game's own shortcuts must not fire.
        ///
        /// Read by VillageHost.HandleHotkeys and by every OrbitCamera handler that reads a key.
        /// Escape releases it.
        /// </summary>
        public static bool KeyboardCaptured { get; private set; }

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
        private string _draftBusiness = "", _draftTrade = "";
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
        /// <summary>
        /// Whether the drafts say something the file does not.
        ///
        /// Pulled out of DrawNoteEditor because SeedDrafts needs the same answer when the
        /// selection moves to another lot - it decides whether the outgoing lot's edits are
        /// carried to disk or thrown away, and that question must be asked with exactly the
        /// comparison the save button uses. Two copies of it would drift, and the failure mode
        /// of drifting is silently losing somebody's typing.
        /// </summary>
        private bool DraftsDifferFromDisk(int parcelId)
        {
            var saved = ParcelNotes.For(parcelId);
            if (saved == null) return DraftIsAnything();

            return PeopleDiffer(saved) || _draftCharacter != saved.Character
                || _draftBusiness != saved.Business || _draftTrade != saved.Trade
                || _draftZoning != saved.Zoning || _draftHousing != saved.Housing
                || _draftQuality != saved.Condition
                || _draftStories != saved.Stories || _draftBasement != saved.Basement
                || _draftBedrooms != saved.Bedrooms || _draftBaths != saved.Baths
                || _draftHalfBaths != saved.HalfBaths || _draftSquareFeet != saved.SquareFeet
                || _draftYearBuilt != saved.YearBuilt;
        }

        /// <summary>Field by field, because a household is the thing most likely to be edited
        /// and most expensive to lose.</summary>
        private bool PeopleDiffer(ParcelNotes.Note saved)
        {
            var live = new System.Collections.Generic.List<ParcelNotes.Person>();
            foreach (var who in _draftPeople)
                if (!string.IsNullOrWhiteSpace(who.First) || !string.IsNullOrWhiteSpace(who.Last))
                    live.Add(who);

            if (live.Count != saved.People.Count) return true;
            for (int i = 0; i < live.Count; i++)
            {
                var a = live[i];
                var b = saved.People[i];
                if (a.First != b.First || a.Last != b.Last || a.Age != b.Age || a.Child != b.Child)
                    return true;
                if (a.Traits.Count != b.Traits.Count) return true;
                for (int t = 0; t < a.Traits.Count; t++)
                    if (a.Traits[t] != b.Traits[t]) return true;
            }
            return false;
        }

        private void SeedDrafts(int parcelId)
        {
            if (_noteDraftFor == parcelId) return;

            // CARRY THE LAST LOT'S EDITS TO DISK BEFORE LOADING THIS ONE.
            //
            // This used to reseed straight over the drafts, so clicking a second lot threw away
            // whatever had been typed into the first with no prompt and no trace. The `dirty`
            // flag existed and was used only to COLOUR the save button. For someone naming a
            // street's worth of shops - click, type, click the next one - that is losing a name
            // every time, and never noticing which.
            //
            // Saved rather than prompted because that is the shape of the job: authoring, not
            // filling in a form. `revert` is still there for a lot, and ParcelNotes.Save treats
            // an all-blank note as a deletion, so an accidental keystroke that gets cleared
            // again does not leave a husk behind.
            if (_noteDraftFor != int.MinValue && DraftsDifferFromDisk(_noteDraftFor))
            {
                ParcelNotes.Save(_noteDraftFor, DraftNote(ParcelNotes.For(_noteDraftFor)));
                Debug.Log($"[notes] carried unsaved edits on parcel {_noteDraftFor} to disk "
                        + $"when the selection moved to {parcelId}.");
            }

            _noteDraftFor = parcelId;
            _noteScroll = Vector2.zero;   // a new lot opens at the top, not where the last one sat

            var saved = ParcelNotes.For(parcelId);
            _draftAdults = saved?.Adults ?? 0;
            _draftKids = saved?.Kids ?? 0;
            _draftNames = saved?.Names ?? "";
            _draftPeople.Clear();
            _traitsOpenFor = -1;
            if (saved != null) foreach (var who in saved.People) _draftPeople.Add(who.Copy());
            _draftCharacter = saved?.Character ?? "";
            _draftBusiness = saved?.Business ?? "";
            _draftTrade = saved?.Trade ?? "";
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
        // ---- the structured household ----
        private readonly System.Collections.Generic.List<ParcelNotes.Person> _draftPeople =
            new System.Collections.Generic.List<ParcelNotes.Person>();

        /// <summary>Which person's trait picker is open, by index. -1 for none.</summary>
        private int _traitsOpenFor = -1;

        /// <summary>
        /// The surname a new member of this household most likely takes.
        ///
        /// The commonest surname already in the house, or the first person's if they all differ.
        /// A guess, and the right kind: it saves typing on the ordinary case - a family - and
        /// costs one correction on the lodger.
        /// </summary>
        private string HouseholdSurname()
        {
            string best = "";
            int bestCount = 0;
            foreach (var a in _draftPeople)
            {
                if (string.IsNullOrWhiteSpace(a.Last)) continue;
                int n = 0;
                foreach (var b in _draftPeople)
                    if (string.Equals(a.Last, b.Last, System.StringComparison.OrdinalIgnoreCase)) n++;
                if (n > bestCount) { bestCount = n; best = a.Last; }
            }
            return best;
        }

        /// <summary>
        /// The trait list, grouped, as toggles.
        ///
        /// Toggles rather than a menu because a person has SEVERAL - the man who sits on the
        /// porch is also the man who knows everyone's business, and that combination is the
        /// character. Drawn inline for the same reason the enum dropdowns are: a floating popup
        /// inside a scroll view is clipped by it.
        /// </summary>
        private void DrawTraitPicker(ParcelNotes.Person who)
        {
            foreach (var group in VillageTraits.All)
            {
                GUILayout.Label($"<color=#75736e>   {group.Name}</color>", _small);

                int perRow = 2, inRow = 0;
                GUILayout.BeginHorizontal();
                foreach (var trait in group.Traits)
                {
                    if (inRow == perRow) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); inRow = 0; }

                    bool has = who.Traits.Contains(trait);
                    var was = GUI.backgroundColor;
                    if (has) GUI.backgroundColor = new Color(0.72f, 0.58f, 0.28f);
                    if (GUILayout.Button((has ? "• " : "") + trait, _button,
                                         GUILayout.Width(S(148f)), GUILayout.Height(S(20f))))
                    {
                        if (has) who.Traits.Remove(trait);
                        else who.Traits.Add(trait);
                    }
                    GUI.backgroundColor = was;
                    inRow++;
                }
                while (inRow++ < perRow) GUILayout.Label("", _small, GUILayout.Width(S(148f)));
                GUILayout.EndHorizontal();
            }
        }

        private ParcelNotes.Note DraftNote(ParcelNotes.Note saved) => new ParcelNotes.Note
        {
            // Adults/Kids stay derived so anything still reading them - Households, the
            // inspector summary - keeps working while People is the real answer.
            Adults = CountPeople(false), Kids = CountPeople(true), Names = "",
            Character = _draftCharacter, Footprint = saved?.Footprint,
            Business = _draftBusiness, Trade = _draftTrade,
            Zoning = _draftZoning, Housing = _draftHousing, Condition = _draftQuality,
            Stories = _draftStories, Basement = _draftBasement,
            Bedrooms = _draftBedrooms, Baths = _draftBaths, HalfBaths = _draftHalfBaths,
            SquareFeet = _draftSquareFeet, YearBuilt = _draftYearBuilt
        }.WithPeople(_draftPeople);

        private int CountPeople(bool children)
        {
            int n = 0;
            foreach (var who in _draftPeople) if (who.Child == children) n++;
            return n;
        }

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
            if (generated != null && _draftPeople.Count == 0)
            {
                GUILayout.Label($"<color=#8a8a86>in {GameClock.EpochYear}, {generated.Family} "
                              + $"{(generated.Rented ? "rented here" : "lived here")}</color>", _small);
                foreach (var person in generated.Members)
                    GUILayout.Label($"<color=#75736e>   {person.Forename} {generated.Surname}, "
                                  + $"{person.Age}</color>", _small);
                if (GUILayout.Button("use this household", _button, GUILayout.Height(S(22f))))
                {
                    _draftPeople.Clear();
                    foreach (var person in generated.Members)
                        _draftPeople.Add(new ParcelNotes.Person
                        {
                            First = person.Forename,
                            Last = generated.Surname,
                            Age = person.Age,
                            Child = person.Age < 18,
                        });
                }
                GUILayout.Space(S(6f));
            }

            // ---- WHO LIVES HERE, one row each ----
            //
            // Was a counter for adults, a counter for kids and a free-text box of names, which is
            // three things that could disagree with each other and did. A row per person cannot:
            // the count IS the number of rows, and every field belongs to somebody.
            GUILayout.Label("<color=#8a8a86>household</color>", _small);

            int removeAt = -1;
            for (int i = 0; i < _draftPeople.Count; i++)
            {
                var who = _draftPeople[i];

                GUILayout.BeginHorizontal();
                who.First = GUILayout.TextField(who.First, GUILayout.Width(S(96f)));
                who.Last = GUILayout.TextField(who.Last, GUILayout.Width(S(96f)));

                string age = GUILayout.TextField(who.Age > 0 ? who.Age.ToString() : "",
                                                 GUILayout.Width(S(34f)));
                who.Age = int.TryParse(age, out int years) ? Mathf.Clamp(years, 0, 120) : 0;

                if (GUILayout.Button(who.Child ? "child" : "adult", _button, GUILayout.Width(S(52f))))
                    who.Child = !who.Child;

                // The remove is last and narrow, so it is never the button you were reaching for.
                if (GUILayout.Button("×", _button, GUILayout.Width(S(24f)))) removeAt = i;
                GUILayout.EndHorizontal();

                // ---- traits ----
                bool traitsOpen = _traitsOpenFor == i;
                GUILayout.BeginHorizontal();
                GUILayout.Label(who.Traits.Count == 0
                        ? "<color=#75736e>   no traits</color>"
                        : $"<color=#b9a87e>   {string.Join(", ", who.Traits)}</color>",
                    _small, GUILayout.Width(S(238f)));
                if (GUILayout.Button(traitsOpen ? "traits ▲" : "traits ▾", _button,
                                     GUILayout.Width(S(66f))))
                    _traitsOpenFor = traitsOpen ? -1 : i;
                GUILayout.EndHorizontal();

                if (traitsOpen) DrawTraitPicker(who);
                GUILayout.Space(S(4f));
            }

            // Removal deferred out of the loop: mutating the list mid-draw changes how many
            // controls IMGUI lays out between its Layout and Repaint passes, and the reward for
            // that is a torn layout rather than a deleted person.
            if (removeAt >= 0)
            {
                _draftPeople.RemoveAt(removeAt);
                _traitsOpenFor = -1;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ adult", _button, GUILayout.Height(S(22f))))
                _draftPeople.Add(new ParcelNotes.Person { Last = HouseholdSurname(), Age = 0 });

            // A CHILD TAKES THE HOUSEHOLD SURNAME by default - in a village that is right far
            // more often than it is wrong, and it is one less thing to type for every kid on
            // every lot. Still editable; it is a default, not a rule.
            if (GUILayout.Button("+ child", _button, GUILayout.Height(S(22f))))
                _draftPeople.Add(new ParcelNotes.Person
                    { Last = HouseholdSurname(), Child = true, Age = 0 });
            GUILayout.EndHorizontal();

            GUILayout.Space(S(4f));
            GUILayout.Label("<color=#8a8a86>what they're like - the seed for behaviour</color>", _small);
            _draftCharacter = GUILayout.TextArea(_draftCharacter, GUILayout.Height(S(60f)));

            // ---- what the lot is: the right-hand column ----
            GUILayout.EndVertical();
            GUILayout.Space(S(24f));
            GUILayout.BeginVertical(GUILayout.Width(S(330f)));

            // ---- WHAT TRADED HERE ----
            //
            // First in this column, above zoning, because for the downtown it is the question.
            // No source names what was in these units in 1991 - everything written about the
            // commercial row post-dates the 2004 fire and mourns it rather than listing it - so
            // the only way it gets recorded is somebody who was there typing it in.
            GUILayout.Label("<color=#8a8a86>business - the sign over the door</color>", _small);
            _draftBusiness = GUILayout.TextField(_draftBusiness, GUILayout.Height(S(22f)));

            GUILayout.Space(S(4f));
            GUILayout.Label("<color=#8a8a86>trade - what it actually was</color>", _small);
            _draftTrade = GUILayout.TextField(_draftTrade, GUILayout.Height(S(22f)));

            GUILayout.Space(S(10f));
            _draftZoning = EnumField("zoning", "zoning", _draftZoning, Pretty);

            if (_draftZoning == ParcelNotes.Zoning.Residential)
            {
                GUILayout.Space(S(4f));
                _draftHousing = EnumField("housing", "housing type", _draftHousing, Pretty);
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
            _draftQuality = EnumField("condition", "condition", _draftQuality, Pretty);

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
            bool dirty = DraftsDifferFromDisk(parcelId);

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

        /// <summary>Which dropdown is showing its options, by field id. One at a time.</summary>
        private string _openDropdown;

        /// <summary>
        /// A labelled enum field that OPENS A LIST, rather than a button you click repeatedly
        /// until the value you wanted comes round again.
        ///
        /// Cycling was fine when zoning had three values and nobody used it. Quality has six and
        /// zoning has seven, so setting one to the value two before the current one meant five
        /// clicks and reading the label after each - and if you overshot, five more. It also
        /// hides the options: there is no way to learn what a field can be except to click
        /// through the whole ring.
        ///
        /// The list is drawn INLINE, pushing the rest of the column down, rather than floating
        /// over it. A floating popup inside a GUILayout scroll view has to be drawn in a second
        /// pass with absolute coordinates or it is clipped by the view it opened in, and that is
        /// a lot of machinery for a form with three of these on it.
        ///
        /// Never returns early out of the option loop. IMGUI runs Layout and Repaint as separate
        /// passes over the same code and requires them to draw the SAME NUMBER of controls;
        /// returning the moment a button reports a click draws fewer, and the reward is a
        /// "Mismatched LayoutGroup" exception rather than a working menu.
        /// </summary>
        private T EnumField<T>(string id, string label, T value, System.Func<T, string> pretty)
            where T : struct, System.Enum
        {
            GUILayout.Label($"<color=#8a8a86>{label}</color>", _small);

            bool open = _openDropdown == id;
            if (GUILayout.Button($"{pretty(value)}    {(open ? "▲" : "▼")}", _button,
                                 GUILayout.Height(S(24f))))
                _openDropdown = open ? null : id;

            if (!open) return value;

            var chosen = value;
            foreach (T option in (T[])System.Enum.GetValues(typeof(T)))
            {
                bool current = option.Equals(value);
                var was = GUI.backgroundColor;
                if (current) GUI.backgroundColor = new Color(0.36f, 0.52f, 0.38f);
                if (GUILayout.Button((current ? "•  " : "    ") + pretty(option), _button,
                                     GUILayout.Height(S(22f))))
                    chosen = option;
                GUI.backgroundColor = was;
            }

            if (!chosen.Equals(value)) _openDropdown = null;
            return chosen;
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
        /// <summary>
        /// Whether the drafts hold ANYTHING, for a lot the file has no note for yet.
        ///
        /// PEOPLE ARE CHECKED HERE and were not, which was the same bug as losing edits on a
        /// lot change, reintroduced by the feature that replaced the thing it was fixed for.
        /// The old counters _draftAdults/_draftKids are DERIVED at save time now and nothing
        /// edits them, so they sit at zero however many people are typed in - and a household
        /// entered on a fresh lot read as "no changes": the save button stayed dark, the
        /// carry-forward declined to fire, and the household went in the bin on the next click.
        /// They are not consulted any more; the list is.
        /// </summary>
        private bool DraftIsAnything() =>
            AnyPersonTyped()
            || !string.IsNullOrWhiteSpace(_draftCharacter) || !string.IsNullOrWhiteSpace(_draftNames)
            || !string.IsNullOrWhiteSpace(_draftBusiness) || !string.IsNullOrWhiteSpace(_draftTrade)
            || _draftZoning != ParcelNotes.Zoning.Unset
            || _draftHousing != ParcelNotes.HousingType.Unset
            || _draftQuality != ParcelNotes.Quality.Unset
            || _draftStories != 0 || _draftBasement
            || _draftBedrooms != 0 || _draftBaths != 0 || _draftHalfBaths != 0
            || _draftSquareFeet != 0 || _draftYearBuilt != 0;

        /// <summary>A row with a name in it. Blank rows are scaffolding, not a household.</summary>
        private bool AnyPersonTyped()
        {
            foreach (var who in _draftPeople)
                if (!string.IsNullOrWhiteSpace(who.First) || !string.IsNullOrWhiteSpace(who.Last))
                    return true;
            return false;
        }

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
        /// <summary>
        /// Two lines beside the cursor: whose address this lot is, and whether anyone is in it.
        ///
        /// Deliberately not the inspector. The inspector is what you get when you commit to a
        /// lot; this is what you get for pointing at one, and it answers only the two questions
        /// worth answering without a click. Everything else - PIN, acreage, assessed value,
        /// notes - stays behind the click, or the map becomes unreadable the moment the mouse
        /// crosses it.
        ///
        /// It follows the cursor and disappears on its own when the lot changes to another or to
        /// none, because it is drawn from HoveredParcel every frame and never cached.
        /// </summary>
        private void DrawHoverTip()
        {
            var hovered = _host.HoveredParcel;
            if (hovered == null) return;

            // Nothing while a panel is open on the SAME lot - the inspector already says all of
            // this and more, an arm's length away, and two copies of one address is clutter.
            if (_host.SelectedParcel.HasValue
                && _host.SelectedParcel.Value.Id == hovered.Value.Id) return;

            var parcel = hovered.Value;
            var county = CountyRecord.For(parcel.Id);
            var centre = new Vector2(parcel.Bounds.x + parcel.Bounds.width / 2f,
                                     parcel.Bounds.y + parcel.Bounds.height / 2f);

            string address = county?.Address
                          ?? StreetAddressing.Estimate(_host.World, centre)
                          ?? "Undeveloped lot";

            // OCCUPIED IS A CLAIM ABOUT PEOPLE, so it says only what the record supports. A lot
            // with no building is empty ground and says so; a lot with a building the county has
            // no occupancy for says the building is there and stops, rather than guessing at
            // somebody living in it.
            string state;
            if (county == null) state = "no county record";
            else if (!county.HasBuilding) state = "vacant - no building assessed";
            else switch (county.Occupied)
            {
                case CountyRecord.Occupancy.Owner:
                    state = "occupied - owner lives here"; break;
                case CountyRecord.Occupancy.Absentee:
                    state = "occupied - tax bill goes elsewhere, likely rented"; break;
                default:
                    state = "building stands here - occupancy not recorded"; break;
            }

            // AUTHORED SHOP FIRST, when there is one. If somebody has typed what traded here,
            // that is the most interesting true thing about the lot and it outranks a class
            // code - the county says "commercial", the author says "Market Place Shoppes".
            var note = ParcelNotes.For(parcel.Id);
            string shop = note != null && !string.IsNullOrWhiteSpace(note.Business)
                ? note.Business + (string.IsNullOrWhiteSpace(note.Trade) ? "" : $" — {note.Trade}")
                : null;

            // EVERYTHING KNOWN, not a summary. Asked for in those words - "I just want all the
            // details popup while I am hovering" - and it is the right call for authoring: the
            // question while sweeping a street is "is this one done yet", and a tip that shows
            // only an address cannot answer it. The panel is for CHANGING things; this is for
            // reading them without losing the one you already have open.
            var body = new StringBuilder();
            body.Append("<b>").Append(address).Append("</b>");

            if (shop != null) body.Append("\n<color=#e8d9a8>").Append(shop).Append("</color>");
            body.Append("\n<color=#b9d8b0>").Append(state).Append("</color>");

            // ---- the lot ----
            float wFt = parcel.Bounds.width * MetresToFeet;
            float hFt = parcel.Bounds.height * MetresToFeet;
            body.Append("\n<color=#8a8a86>").Append(Mathf.RoundToInt(wFt)).Append(" x ")
                .Append(Mathf.RoundToInt(hFt)).Append(" ft");
            if (county != null && county.Acres > 0f) body.Append("   ·   ").Append(county.Acres.ToString("0.00")).Append(" acres");
            if (county?.ClassName != null) body.Append("   ·   ").Append(county.ClassName.ToLowerInvariant());
            body.Append("</color>");

            // ---- what has been authored about it ----
            if (note != null)
            {
                var lot = new StringBuilder();
                if (note.Zoning != ParcelNotes.Zoning.Unset) lot.Append(Pretty(note.Zoning));
                if (note.Housing != ParcelNotes.HousingType.Unset)
                    lot.Append(lot.Length > 0 ? "   ·   " : "").Append(Pretty(note.Housing));
                if (note.Condition != ParcelNotes.Quality.Unset)
                    lot.Append(lot.Length > 0 ? "   ·   " : "").Append(Pretty(note.Condition));
                if (note.Stories > 0)
                    lot.Append(lot.Length > 0 ? "   ·   " : "").Append(note.Stories).Append(" storey");
                if (note.Basement) lot.Append(lot.Length > 0 ? "   ·   " : "").Append("basement");
                if (lot.Length > 0) body.Append("\n<color=#9fb6c8>").Append(lot).Append("</color>");

                var house = new StringBuilder();
                if (note.Bedrooms > 0) house.Append(note.Bedrooms).Append(" bed");
                if (note.Baths > 0 || note.HalfBaths > 0)
                    house.Append(house.Length > 0 ? "   ·   " : "").Append(note.Baths)
                         .Append(note.HalfBaths > 0 ? "." + note.HalfBaths : "").Append(" bath");
                if (note.SquareFeet > 0)
                    house.Append(house.Length > 0 ? "   ·   " : "").Append(note.SquareFeet.ToString("N0")).Append(" sq ft");
                if (note.YearBuilt > 0)
                    house.Append(house.Length > 0 ? "   ·   " : "").Append("built ").Append(note.YearBuilt);
                if (house.Length > 0) body.Append("\n<color=#9fb6c8>").Append(house).Append("</color>");

                // ---- who lives here ----
                if (note.People.Count > 0)
                {
                    body.Append("\n");
                    foreach (var who in note.People)
                    {
                        body.Append("\n<color=#d8cfa8>  ").Append(who.FullName);
                        if (who.Age > 0) body.Append(", ").Append(who.Age);
                        if (who.Child) body.Append("  <i>(child)</i>");
                        body.Append("</color>");
                        if (who.Traits.Count > 0)
                            body.Append("\n<color=#8f8a66>      ")
                                .Append(string.Join(", ", who.Traits)).Append("</color>");
                    }
                }

                if (!string.IsNullOrWhiteSpace(note.Character))
                    body.Append("\n\n<color=#a89f8a><i>").Append(note.Character.Trim()).Append("</i></color>");
            }

            body.Append("\n\n<color=#6f6d68>click to edit</color>");
            var tip = new GUIContent(body.ToString());
            var size = _label.CalcSize(tip);
            float w = Mathf.Min(size.x + S(24f), S(420f));
            float h = _label.CalcHeight(tip, w - S(24f)) + S(18f);

            // Offset off the cursor so the pointer itself never covers the first character, and
            // flipped back inside the screen near the right and bottom edges - a tip that runs
            // off the edge is unreadable exactly where the map's own edge lots are.
            var m = Event.current.mousePosition;
            float x = m.x + S(18f);
            float y = m.y + S(18f);
            if (x + w > Screen.width - S(8f)) x = m.x - w - S(12f);
            if (y + h > Screen.height - S(8f)) y = m.y - h - S(12f);

            var rect = new Rect(x, y, w, h);
            GUI.Box(rect, GUIContent.none, _panel);
            GUI.Label(new Rect(rect.x + S(12f), rect.y + S(9f), rect.width - S(24f),
                               rect.height - S(18f)), tip, _label);
        }

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
