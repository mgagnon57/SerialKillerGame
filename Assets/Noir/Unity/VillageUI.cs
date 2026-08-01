using System.Collections.Generic;
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
        private Vector2 _scroll;
        private bool _showPlan = true;

        // ---- the note editor, shared by the place panel and the bare-parcel panel ----
        private int _noteDraftFor = int.MinValue;
        private string _noteDraft = "";

        private const float PanelWidth = 340f;
        private const float BarHeight = 48f;

        /// <summary>
        /// Keep the whole top bar reachable. The skip buttons sit a long way right, and on a
        /// narrow window they used to slide off the edge with no indication they existed.
        /// </summary>
        private const float MinBarWidth = 900f;

        private void Awake() => _host = GetComponent<VillageHost>();

        private void BuildStyles()
        {
            _panel = new GUIStyle(GUI.skin.box);
            _panel.normal.background = SolidTexture(new Color(0.06f, 0.07f, 0.08f, 0.92f));
            _panel.border = new RectOffset(2, 2, 2, 2);
            _panel.padding = new RectOffset(14, 14, 12, 12);

            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = true,
                wordWrap = true
            };
            _label.normal.textColor = new Color(0.85f, 0.84f, 0.80f);

            _title = new GUIStyle(_label) { fontSize = 17, fontStyle = FontStyle.Bold };
            _title.normal.textColor = new Color(0.96f, 0.94f, 0.88f);

            _small = new GUIStyle(_label) { fontSize = 11 };
            _small.normal.textColor = new Color(0.60f, 0.60f, 0.58f);

            _button = new GUIStyle(GUI.skin.button) { fontSize = 13 };

            // The clock is the one thing you should never have to hunt for.
            _clock = new GUIStyle(_label) { fontSize = 26, fontStyle = FontStyle.Bold };
            _clock.normal.textColor = new Color(0.98f, 0.96f, 0.90f);

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
            if (!_stylesReady) BuildStyles();

            if (_host.LoadError != null)
            {
                GUI.Box(new Rect(20, 20, 720, 110), GUIContent.none, _panel);
                GUI.Label(new Rect(36, 34, 690, 90),
                    "<b>The village could not be loaded.</b>\n\n" + _host.LoadError, _label);
                return;
            }
            if (_host.Sim == null) return;

            DrawTopBar();
            DrawInspector();
            DrawHelp();

            // Let the camera know not to treat a click on the panel as a click on the village.
            var mouse = Event.current.mousePosition;
            PointerOverUI = mouse.y < BarHeight ||
                            ((_host.Selected.IsValid || _host.SelectedPlace.IsValid
                              || _host.SelectedParcel.HasValue)
                             && mouse.x > Screen.width - PanelWidth);
        }

        private void DrawTopBar()
        {
            GUI.Box(new Rect(0, 0, Screen.width, BarHeight), GUIContent.none, _panel);

            var clock = _host.Sim.Clock;
            bool paused = _host.SpeedIndex == 0;

            // ---- the clock, big enough to read without looking for it ----
            GUI.Label(new Rect(18, 6, 200, 30),
                $"{clock.HourOfDay:00}:{clock.MinuteOfHour:00}", _clock);

            GUI.Label(new Rect(112, 8, 160, 18),
                $"<b>{GameClock.DayNames[clock.DayOfWeek]}</b>", _label);
            GUI.Label(new Rect(112, 26, 160, 16),
                $"<color=#8a8a86>day {clock.Day}</color>", _small);

            // ---- what speed, in words, not just a highlighted button ----
            string speedText = paused
                ? "<color=#ff8a5c><b>PAUSED</b></color>"
                : $"<b>{VillageHost.SpeedLabels[_host.SpeedIndex]}</b>";

            GUI.Label(new Rect(182, 6, 90, 18), "<color=#8a8a86>speed</color>", _small);
            GUI.Label(new Rect(182, 22, 90, 22), speedText, _label);

            if (_host.Skipping)
                GUI.Label(new Rect(182, 22, 200, 22), "<color=#ff8a5c><b>skipping…</b></color>", _label);

            float x = 250f;
            for (int i = 0; i < VillageHost.Speeds.Length; i++)
            {
                bool active = _host.SpeedIndex == i;
                var old = GUI.backgroundColor;
                if (active) GUI.backgroundColor = new Color(0.90f, 0.48f, 0.30f);
                if (GUI.Button(new Rect(x, 12, 42, 24), VillageHost.SpeedLabels[i], _button))
                    _host.SpeedIndex = i;
                GUI.backgroundColor = old;
                x += 45f;
            }

            x += 14f;
            GUI.Label(new Rect(x, 16, 40, 20), "skip", _small);
            x += 36f;
            for (int i = 0; i < VillageHost.SkipHours.Length; i++)
            {
                int hour = VillageHost.SkipHours[i];
                // The digit that does the same thing, so the shortcut is discoverable rather
                // than something you have to be told about.
                if (GUI.Button(new Rect(x, 12, 52, 24), $"{hour:00}:00  <color=#8a8a86>{i + 1}</color>", _button))
                    _host.SkipToHour(hour);
                x += 56f;
            }

            // The census is the first thing to go on a narrow window - the controls matter
            // more than the readout, and overlapping text is worse than absent text.
            if (Screen.width >= MinBarWidth + 440f)
                GUI.Label(new Rect(Screen.width - 430, 16, 420, 20), Census(), _small);

            // Which view you are in, far right. In street mode you can lose track of whether
            // WASD is going to pan the camera or walk you into a hedge.
            string mode = _host.ViewName;
            GUI.Label(new Rect(Screen.width - 150, 2, 140, 16),
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

            const float w = 620f, h = 470f;
            var rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(rect, GUIContent.none, _panel);

            GUILayout.BeginArea(new Rect(rect.x + 26, rect.y + 20, rect.width - 52, rect.height - 40));

            GUILayout.Label("Ashcombe", _title);
            GUILayout.Label("<color=#8a8a86>press H to close</color>", _small);
            GUILayout.Space(14);

            Row("Space", "pause and resume");
            Row("[  ]", "slower / faster  —  ❚❚ ¼ ½ 1 3 10 60 300");
            Row("1 – 6", "skip to 06:00 08:00 12:00 17:00 20:00 23:00");
            GUILayout.Space(10);

            Row("Tab", "<b>overview ⇄ street level</b>");
            Row("WASD", "pan, or walk when at street level");
            Row("Shift", "jog");
            Row("right-drag", "orbit, or look around in the street");
            Row("Q  E", "rotate");
            Row("R  Shift+F", "tilt up / down");
            Row("wheel", "zoom");
            GUILayout.Space(10);

            Row("click", "select somebody");
            Row("F", "follow them");
            GUILayout.Space(14);

            GUILayout.Label(
                "<color=#c9b98a>Roofs lift off as you zoom in from above, so you can watch "
              + "people indoors. They stay on at street level.</color>", _label);
            GUILayout.Space(8);
            GUILayout.Label(
                "<color=#8a8a86>Try: press 5 for 21:00, then Tab, and walk down Back Lane. "
              + "Lit windows are houses where somebody is home and awake.</color>", _label);

            GUILayout.EndArea();
        }

        private void Row(string key, string what)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{key}</b>", _label, GUILayout.Width(110));
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
            var rect = new Rect(Screen.width - PanelWidth, BarHeight + 8, PanelWidth - 12,
                                Screen.height - BarHeight - 20);
            GUI.Box(rect, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(rect.x + 14, rect.y + 12, rect.width - 28, rect.height - 24));

            var sim = _host.Sim;
            var kind = PlaceKindTable.Current.Row(place.Kind);

            // The name IS the address - see relay-rossville.py and Content/parcels.txt.
            GUILayout.Label(place.Name, _title);
            GUILayout.Label($"{Article(kind.Name)}   ·   {LotSize(place)}", _small);
            GUILayout.Space(10);

            // The line somebody wrote about this building when they put it in the map.
            if (!string.IsNullOrWhiteSpace(place.Human))
            {
                GUILayout.Label($"<i>{place.Human}</i>", _label);
                GUILayout.Space(10);
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
                GUILayout.Space(10);
            }

            if (place.Units > 1)
                GUILayout.Label($"<color=#8a8a86>{place.Units} separate homes</color>", _small);
            if (place.JobSlots > 0)
                GUILayout.Label($"<color=#8a8a86>{place.JobSlots} job slots</color>", _small);

            int parcelId = ParcelIndex.FindFor(place)?.Id ?? -1;
            DrawNoteEditor(parcelId);

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("close", _button, GUILayout.Width(70), GUILayout.Height(26)))
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
        private int _editingNoteFor = int.MinValue;
        private string _draftCharacter = "", _draftNames = "";
        private int _draftAdults, _draftKids;
        private ParcelNotes.Zoning _draftZoning;
        private ParcelNotes.HousingType _draftHousing;
        private int _draftStories;
        private bool _draftBasement;

        private void DrawNoteEditor(int parcelId)
        {
            if (parcelId < 0) return;
            GUILayout.Space(10);

            var drawer = _host.Footprint;
            bool drawingHere = drawer != null && drawer.Active && drawer.TargetParcelId == parcelId;
            var saved = ParcelNotes.For(parcelId);
            bool editing = _editingNoteFor == parcelId;

            GUILayout.Label("<color=#8a8a86>household</color>", _small);

            if (!editing)
            {
                if (saved == null || (saved.Adults == 0 && saved.Kids == 0
                                       && string.IsNullOrWhiteSpace(saved.Names)))
                {
                    GUILayout.Label("<color=#75736e>nobody on file</color>", _label);
                }
                else
                {
                    GUILayout.Label(HouseholdSummary(saved.Adults, saved.Kids), _label);
                    if (!string.IsNullOrWhiteSpace(saved.Names))
                        GUILayout.Label($"<color=#8a8a86>{saved.Names.Replace("\n", ", ")}</color>", _small);
                }
                if (saved != null && !string.IsNullOrWhiteSpace(saved.Character))
                {
                    GUILayout.Space(4);
                    GUILayout.Label(saved.Character, _label);
                }

                if (saved != null && (saved.Zoning != ParcelNotes.Zoning.Unset || saved.Stories != 0
                                       || saved.Basement || saved.Housing != ParcelNotes.HousingType.Unset))
                {
                    GUILayout.Space(6);
                    var bits = new List<string> { Pretty(saved.Zoning) };
                    if (saved.Zoning == ParcelNotes.Zoning.Residential
                        && saved.Housing != ParcelNotes.HousingType.Unset)
                        bits.Add(Pretty(saved.Housing));
                    if (saved.Stories > 0)
                        bits.Add(saved.Stories == 1 ? "1 story" : $"{saved.Stories} stories");
                    if (saved.Basement) bits.Add("basement");
                    GUILayout.Label($"<color=#8a8a86>{string.Join(" · ", bits)}</color>", _small);
                }

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("edit", _button, GUILayout.Width(70), GUILayout.Height(24)))
                {
                    _editingNoteFor = parcelId;
                    _draftAdults = saved?.Adults ?? 0;
                    _draftKids = saved?.Kids ?? 0;
                    _draftNames = saved?.Names ?? "";
                    _draftCharacter = saved?.Character ?? "";
                    _draftZoning = saved?.Zoning ?? ParcelNotes.Zoning.Unset;
                    _draftHousing = saved?.Housing ?? ParcelNotes.HousingType.Unset;
                    _draftStories = saved?.Stories ?? 0;
                    _draftBasement = saved?.Basement ?? false;
                }
                if (GUILayout.Button("randomize", _button, GUILayout.Width(90), GUILayout.Height(24)))
                {
                    RandomizeHousehold(out _draftAdults, out _draftKids, out _draftNames, out _draftCharacter);
                    ParcelNotes.Save(parcelId, new ParcelNotes.Note
                    {
                        Adults = _draftAdults, Kids = _draftKids, Names = _draftNames,
                        Character = _draftCharacter, Footprint = saved?.Footprint,
                        Zoning = saved?.Zoning ?? ParcelNotes.Zoning.Unset,
                        Housing = saved?.Housing ?? ParcelNotes.HousingType.Unset,
                        Stories = saved?.Stories ?? 0, Basement = saved?.Basement ?? false
                    });
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("adults", _small, GUILayout.Width(50));
                if (GUILayout.Button("-", _button, GUILayout.Width(28))) _draftAdults = Mathf.Max(0, _draftAdults - 1);
                GUILayout.Label(_draftAdults.ToString(), _label, GUILayout.Width(20));
                if (GUILayout.Button("+", _button, GUILayout.Width(28))) _draftAdults++;
                GUILayout.Space(10);
                GUILayout.Label("kids", _small, GUILayout.Width(34));
                if (GUILayout.Button("-", _button, GUILayout.Width(28))) _draftKids = Mathf.Max(0, _draftKids - 1);
                GUILayout.Label(_draftKids.ToString(), _label, GUILayout.Width(20));
                if (GUILayout.Button("+", _button, GUILayout.Width(28))) _draftKids++;
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.Label("<color=#8a8a86>names, one per line</color>", _small);
                _draftNames = GUILayout.TextArea(_draftNames, GUILayout.Height(50));

                GUILayout.Space(4);
                GUILayout.Label("<color=#8a8a86>what they're like - the seed for behaviour</color>", _small);
                _draftCharacter = GUILayout.TextArea(_draftCharacter, GUILayout.Height(60));

                GUILayout.Space(8);
                GUILayout.Label("<color=#8a8a86>zoning</color>", _small);
                if (GUILayout.Button(Pretty(_draftZoning), _button, GUILayout.Height(24)))
                    _draftZoning = Cycle(_draftZoning);

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label("stories", _small, GUILayout.Width(50));
                if (GUILayout.Button("-", _button, GUILayout.Width(28))) _draftStories = Mathf.Max(0, _draftStories - 1);
                GUILayout.Label(_draftStories.ToString(), _label, GUILayout.Width(20));
                if (GUILayout.Button("+", _button, GUILayout.Width(28))) _draftStories++;
                GUILayout.Space(10);
                if (GUILayout.Button(_draftBasement ? "basement: yes" : "basement: no", _button))
                    _draftBasement = !_draftBasement;
                GUILayout.EndHorizontal();

                if (_draftZoning == ParcelNotes.Zoning.Residential)
                {
                    GUILayout.Space(4);
                    GUILayout.Label("<color=#8a8a86>housing type</color>", _small);
                    if (GUILayout.Button(Pretty(_draftHousing), _button, GUILayout.Height(24)))
                        _draftHousing = Cycle(_draftHousing);
                }

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("save", _button, GUILayout.Height(24)))
                {
                    ParcelNotes.Save(parcelId, new ParcelNotes.Note
                    {
                        Adults = _draftAdults, Kids = _draftKids, Names = _draftNames,
                        Character = _draftCharacter, Footprint = saved?.Footprint,
                        Zoning = _draftZoning, Housing = _draftHousing,
                        Stories = _draftStories, Basement = _draftBasement
                    });
                    _editingNoteFor = int.MinValue;
                }
                if (GUILayout.Button("randomize", _button, GUILayout.Height(24)))
                    RandomizeHousehold(out _draftAdults, out _draftKids, out _draftNames, out _draftCharacter);
                if (GUILayout.Button("cancel", _button, GUILayout.Height(24)))
                    _editingNoteFor = int.MinValue;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);

            if (!drawingHere)
            {
                bool hasShape = saved?.Footprint != null;
                if (GUILayout.Button(hasShape ? "redraw house" : "draw house", _button, GUILayout.Height(24)))
                    drawer?.Begin(parcelId, saved?.Footprint);
            }
            else
            {
                GUILayout.Label($"<color=#c9b98a>drawing - click the ground to place a corner "
                               + $"({drawer.Points.Count} so far)</color>", _small);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("undo point", _button, GUILayout.Height(24)))
                    drawer.UndoLast();
                if (GUILayout.Button("finish", _button, GUILayout.Height(24)))
                    drawer.Finish(parcelId);
                if (GUILayout.Button("cancel", _button, GUILayout.Height(24)))
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

        private static string HouseholdSummary(int adults, int kids)
        {
            if (adults == 0 && kids == 0) return "<color=#75736e>nobody on file</color>";
            string a = adults == 1 ? "1 adult" : $"{adults} adults";
            if (kids == 0) return a;
            string k = kids == 1 ? "1 kid" : $"{kids} kids";
            return $"{a}, {k}";
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
        /// </summary>
        private void DrawParcelInspector(ParcelIndex.Parcel parcel)
        {
            var rect = new Rect(Screen.width - PanelWidth, BarHeight + 8, PanelWidth - 12,
                                Screen.height - BarHeight - 20);
            GUI.Box(rect, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(rect.x + 14, rect.y + 12, rect.width - 28, rect.height - 24));

            float wFt = parcel.Bounds.width * MetresToFeet;
            float hFt = parcel.Bounds.height * MetresToFeet;
            var centre = new Vector2(parcel.Bounds.x + parcel.Bounds.width / 2f,
                                     parcel.Bounds.y + parcel.Bounds.height / 2f);
            string approx = StreetAddressing.Estimate(_host.World, centre);

            GUILayout.Label(approx ?? "Undeveloped lot", _title);
            GUILayout.Label($"{(approx != null ? "no house built" : "no address on file")}   ·   "
                          + $"{Mathf.RoundToInt(wFt)} x {Mathf.RoundToInt(hFt)} ft", _small);
            GUILayout.Space(10);
            GUILayout.Label("<color=#8a8a86>A real surveyed parcel with no house or business "
                           + "built on it.</color>", _label);

            DrawNoteEditor(parcel.Id);

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("close", _button, GUILayout.Width(70), GUILayout.Height(26)))
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
                GUI.Label(new Rect(16, Screen.height - 62, 900, 22),
                    "<b>Tab</b> street level   ·   right-drag or <b>Q</b>/<b>E</b> orbit   ·   "
                  + "<b>R</b>/<b>Shift+F</b> tilt   ·   <b>WASD</b> move   ·   wheel zoom", _small);
                GUI.Label(new Rect(16, Screen.height - 40, 900, 22),
                    "<b>Space</b> pause   ·   <b>[</b> <b>]</b> speed   ·   <b>1</b>–<b>6</b> skip to hour   ·   "
                  + "click anyone, any building, or any lot   ·   <b>F</b> follow   ·   <b>H</b> for help", _small);
                return;
            }

            var rect = new Rect(Screen.width - PanelWidth, BarHeight + 8, PanelWidth - 12, Screen.height - BarHeight - 20);
            GUI.Box(rect, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(rect.x + 14, rect.y + 12, rect.width - 28, rect.height - 24));

            var sim = _host.Sim;
            var agent = sim.GetAgent(citizen.Id);
            var household = _host.People.HouseholdOf(citizen);

            GUILayout.Label(citizen.FullName, _title);
            GUILayout.Label($"{citizen.Age}   ·   {Stage(citizen)}", _small);
            GUILayout.Space(10);

            // ---- what they are doing, right now ----
            string doing = agent.Travelling
                ? $"walking to <b>{_host.World.GetPlace(sim.CurrentBlock(citizen.Id).Where)?.Name}</b>"
                : $"{Verb(agent.Doing)} <b>{_host.World.GetPlace(agent.At)?.Name}</b>";
            GUILayout.Label(doing, _label);
            GUILayout.Space(10);

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
            else if (citizen.IsChild) GUILayout.Label("<color=#8a8a86>at school</color>", _label);
            else if (citizen.Stage == LifeStage.Elder) GUILayout.Label("<color=#8a8a86>retired</color>", _label);

            GUILayout.Space(12);

            // ---- the particulars: the whole reason this is worth watching ----
            foreach (int p in citizen.Particulars)
                GUILayout.Label("<color=#c9b98a>" + _host.Particulars.Sentence(citizen.Forename, p) + "</color>", _label);

            GUILayout.Space(12);
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
            if (GUILayout.Button(_host.Following ? "stop following" : "follow  (F)", _button, GUILayout.Height(26)))
                _host.Following = !_host.Following;
            if (GUILayout.Button("close", _button, GUILayout.Width(70), GUILayout.Height(26)))
            {
                _host.Selected = CitizenId.None;
                _host.Following = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private static string Stage(Citizen c)
        {
            switch (c.Stage)
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
