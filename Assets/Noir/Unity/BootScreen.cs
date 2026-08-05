using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// "Loading assets and shaders" - and it stays up until the game is genuinely ready to run.
    ///
    /// WHY IT EXISTS, in the owner's words: *"If I know it is working on something in the
    /// background that is going to effect gameplay I want to know. Give me a 'Loading
    /// assets/shaders' during start up so I am not expecting it to run until it is all done."*
    ///
    /// That is the right instinct and it is what every shipping game's shader screen is for. The
    /// cost of compiling a shader variant does not go away; what a loading screen buys is that
    /// the cost lands somewhere you EXPECT to wait, instead of arriving as a freeze five seconds
    /// into looking at something and reading as a broken game.
    ///
    /// IT DOES NOT GUESS HOW LONG. It watches actual frame times and lifts when the game is
    /// demonstrably smooth - RequiredGoodFrames consecutive frames under GoodFrameMs. A timer
    /// would be a lie the first time a machine or a material set changed; this is a measurement,
    /// and it is right on a cold cache and on a warm one without being told which it is.
    ///
    /// AND IT HOLDS THE SIMULATION. The clock does not advance and the camera does not accept
    /// input while it is up, so nothing has "already started" behind the curtain. That is the
    /// half that makes it honest rather than decorative.
    /// </summary>
    public sealed class BootScreen : MonoBehaviour
    {
        /// <summary>A frame at or under this is a frame the game is keeping up with, in ms.</summary>
        private const float GoodFrameMs = 25f;

        /// <summary>How many in a row before the curtain lifts. Enough that one lucky frame
        /// between two compiles cannot open it early.</summary>
        private const int RequiredGoodFrames = 12;

        /// <summary>Never hold it longer than this, however bad the frames are - a curtain that
        /// never lifts is worse than a slow game, because at least a slow game can be looked at.
        /// </summary>
        private const float PatienceSeconds = 45f;

        /// <summary>What it is waiting on, set by whoever is doing the work.</summary>
        public static string Phase = "Loading";

        private VillageHost _host;
        private int _good;
        private float _opened;
        private int _wasSpeed;
        private bool _done;

        private GUIStyle _panel, _title, _detail;
        private bool _styled;

        public static BootScreen Create(VillageHost host, Transform parent)
        {
            var go = new GameObject("BootScreen");
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<BootScreen>();
            s._host = host;
            s._opened = Time.realtimeSinceStartup;

            // Hold the clock. Restored the moment the curtain lifts.
            s._wasSpeed = host != null ? host.SpeedIndex : 1;
            if (host != null) host.SpeedIndex = 0;
            return s;
        }

        private void Update()
        {
            if (_done) return;

            float ms = Time.unscaledDeltaTime * 1000f;
            if (ms <= GoodFrameMs) _good++; else _good = 0;

            bool settled = _good >= RequiredGoodFrames;
            bool impatient = Time.realtimeSinceStartup - _opened > PatienceSeconds;
            if (!settled && !impatient) return;

            _done = true;
            if (_host != null) _host.SpeedIndex = _wasSpeed;

            float took = Time.realtimeSinceStartup - _opened;
            Debug.Log(impatient && !settled
                ? $"[boot] gave up waiting after {took:0.0}s - the frames never settled. "
                + "Something is still costing more than 25 ms a frame; press F3."
                : $"[boot] ready after {took:0.0}s - {RequiredGoodFrames} straight frames "
                + $"under {GoodFrameMs:0} ms. Clock running.");

            Destroy(gameObject);
        }

        private static int F(int at1080) =>
            Mathf.RoundToInt(at1080 * Mathf.Max(1f, Screen.height / 1080f));

        private void Style()
        {
            if (_styled) return;
            _styled = true;

            var bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0.06f, 0.07f, 0.08f, 1f));
            bg.Apply();

            _panel = new GUIStyle();
            _panel.normal.background = bg;

            _title = new GUIStyle
            {
                fontSize = F(34),
                alignment = TextAnchor.MiddleCenter,
                richText = true,
            };
            _title.normal.textColor = new Color(0.88f, 0.87f, 0.82f);

            _detail = new GUIStyle(_title) { fontSize = F(16) };
            _detail.normal.textColor = new Color(0.55f, 0.57f, 0.60f);
        }

        private void OnGUI()
        {
            if (_done) return;
            Style();

            // Opaque and full screen on purpose. A translucent overlay over a half-built town
            // invites exactly the misreading this exists to prevent - that it has started.
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none, _panel);

            float y = Screen.height * 0.5f - F(60);
            GUI.Label(new Rect(0, y, Screen.width, F(50)), Phase, _title);

            // A moving mark, so a long compile is visibly WORKING rather than visibly hung -
            // the distinction the owner has had to guess at all afternoon.
            int dots = Mathf.FloorToInt(Time.realtimeSinceStartup * 3f) % 4;
            GUI.Label(new Rect(0, y + F(52), Screen.width, F(24)),
                      new string('.', dots + 1), _detail);

            GUI.Label(new Rect(0, y + F(84), Screen.width, F(24)),
                      "compiling shaders and building the town - it will start on its own",
                      _detail);

            float waited = Time.realtimeSinceStartup - _opened;
            if (waited > 6f)
                GUI.Label(new Rect(0, y + F(112), Screen.width, F(24)),
                          $"<color=#776>{waited:0} seconds - a cold shader cache takes a while, "
                        + "and is cached after this</color>", _detail);
        }
    }
}
