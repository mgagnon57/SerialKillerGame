using UnityEngine;
using UnityEngine.InputSystem;

namespace Noir.Unity
{
    /// <summary>
    /// The switches, on screen, drawn with IMGUI like the rest of this project's overlays.
    ///
    /// L opens and closes it. Every row is a click, and there are two presets at the top,
    /// because the two things anybody actually wants are "show me everything" and "take it all
    /// away and let me look at the ground".
    ///
    /// IMGUI RATHER THAN A CANVAS, for the same reason VillageUI is: there is no scene to author
    /// in this project - VillageHost bootstraps itself from a RuntimeInitializeOnLoadMethod and
    /// builds its own camera - so a prefab-based UI would be the only thing in the game that
    /// needed one.
    /// </summary>
    public sealed class LayerPanel : MonoBehaviour
    {
        private bool _open;
        private GUIStyle _panel, _head, _row, _rowOff, _preset;
        private bool _styled;

        /// <summary>
        /// Where this panel is on screen, in GUI coordinates, or an empty rect when it is shut.
        ///
        /// THE CAMERA HAS TO KNOW. OrbitCamera turns a left click into a selection unless
        /// VillageUI.PointerOverUI says the pointer is over an overlay, and that test only ever
        /// knew about the top bar and the right-hand inspector. This panel is drawn top LEFT, so
        /// every click on a layer row ALSO fell through to the town underneath and opened the
        /// parcel inspector on whatever happened to be behind the button.
        ///
        /// Published rather than tested here, so there is still one place that decides what
        /// counts as UI. Read by VillageUI.
        /// </summary>
        public static Rect Bounds { get; private set; }

        private void OnDisable() => Bounds = new Rect();

        public static LayerPanel Create(Transform parent)
        {
            var go = new GameObject("LayerPanel");
            go.transform.SetParent(parent, false);
            return go.AddComponent<LayerPanel>();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // L for layers. Not one of the digits - those are the hour skips - and not X, which
            // is the x-ray.
            if (keyboard.lKey.wasPressedThisFrame) _open = !_open;
        }

        /// <summary>Scale with the display, the same way VillageUI does, so the panel is legible
        /// on a 5120-wide monitor instead of a postage stamp in the corner.</summary>
        private static int F(int at1080) => Mathf.RoundToInt(at1080 * Mathf.Max(1f, Screen.height / 1080f));

        private void Style()
        {
            if (_styled) return;
            _styled = true;

            _panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(F(10), F(10), F(10), F(10)) };

            _head = new GUIStyle(GUI.skin.label)
            {
                fontSize = F(14), fontStyle = FontStyle.Bold, richText = true,
            };

            _row = new GUIStyle(GUI.skin.button)
            {
                fontSize = F(13), alignment = TextAnchor.MiddleLeft, richText = true,
                padding = new RectOffset(F(8), F(8), F(4), F(4)),
            };

            _rowOff = new GUIStyle(_row);
            _rowOff.normal.textColor = new Color(0.45f, 0.45f, 0.48f);

            _preset = new GUIStyle(GUI.skin.button) { fontSize = F(12), richText = true };
        }

        private void OnGUI()
        {
            Style();

            if (!_open)
            {
                // A hint, always, or the feature is invisible and might as well not exist -
                // which is exactly how the built-town switch went unfound twice. It is a LABEL,
                // nothing to click, so it does not claim the pointer.
                Bounds = new Rect();
                GUI.Label(new Rect(F(12), Screen.height - F(30), F(260), F(24)),
                          "<color=#9aa>L — layers (" + Layers.CountOn() + "/" + Layers.All.Length + ")</color>",
                          _head);
                return;
            }

            float w = F(240);
            float h = F(126) + Layers.All.Length * F(30);   // 126 covers the second preset row
            var area = new Rect(F(12), F(12), w, h);

            // Claimed BEFORE the buttons are drawn, so the click that lands on a row this frame
            // is already known to be a UI click by the time the camera looks.
            Bounds = area;

            GUILayout.BeginArea(area, _panel);

            GUILayout.Label("<color=#d8d4c8>Layers</color>  <color=#8a8>"
                          + Layers.CountOn() + "/" + Layers.All.Length + "</color>", _head);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("All", _preset)) Layers.SetAll(true);
            if (GUILayout.Button("None", _preset)) Layers.SetAll(false);
            if (GUILayout.Button("Ground + roads", _preset)) Layers.GroundAndRoadsOnly();
            GUILayout.EndHorizontal();

            // Its own row rather than a fourth button on the one above: three already crowd 240 px
            // and this is the preset that gets used most while the plat is being checked.
            if (GUILayout.Button("Street layout — roads, rail, parcels, names", _preset))
                Layers.StreetLayoutOnly();

            GUILayout.Space(F(6));

            foreach (var kind in Layers.All)
            {
                bool on = Layers.IsOn(kind);
                string tick = on ? "<color=#8c8>✓</color> " : "<color=#666>·</color> ";
                if (GUILayout.Button(tick + Layers.Label(kind), on ? _row : _rowOff))
                    Layers.Toggle(kind);
            }

            GUILayout.EndArea();
        }
    }
}
