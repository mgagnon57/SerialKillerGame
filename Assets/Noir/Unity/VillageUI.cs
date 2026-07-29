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
                            (_host.Selected.IsValid && mouse.x > Screen.width - PanelWidth);
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
            int asleep = 0, walking = 0, work = 0, school = 0, pub = 0, outside = 0;
            var sim = _host.Sim;
            for (int i = 0; i < sim.AgentCount; i++)
            {
                var a = sim.GetAgent(i);
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
                 + $"at work {work}   school {school}   pub {pub}   out {outside}";
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

        private void DrawInspector()
        {
            var citizen = _host.SelectedCitizen;
            if (citizen == null)
            {
                GUI.Label(new Rect(16, Screen.height - 62, 900, 22),
                    "<b>Tab</b> street level   ·   right-drag or <b>Q</b>/<b>E</b> orbit   ·   "
                  + "<b>R</b>/<b>Shift+F</b> tilt   ·   <b>WASD</b> move   ·   wheel zoom", _small);
                GUI.Label(new Rect(16, Screen.height - 40, 900, 22),
                    "<b>Space</b> pause   ·   <b>[</b> <b>]</b> speed   ·   <b>1</b>–<b>6</b> skip to hour   ·   "
                  + "click anyone   ·   <b>F</b> follow   ·   <b>H</b> for help", _small);
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
                default: return "—";
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
                default: return "at";
            }
        }
    }
}
