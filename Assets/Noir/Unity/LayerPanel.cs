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
                // which is exactly how the built-town switch went unfound twice.
                GUI.Label(new Rect(F(12), Screen.height - F(30), F(260), F(24)),
                          "<color=#9aa>L — layers (" + Layers.CountOn() + "/" + Layers.All.Length + ")</color>",
                          _head);
                return;
            }

            float w = F(240);
            float h = F(96) + Layers.All.Length * F(30);
            GUILayout.BeginArea(new Rect(F(12), F(12), w, h), _panel);

            GUILayout.Label("<color=#d8d4c8>Layers</color>  <color=#8a8>"
                          + Layers.CountOn() + "/" + Layers.All.Length + "</color>", _head);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("All", _preset)) Layers.SetAll(true);
            if (GUILayout.Button("None", _preset)) Layers.SetAll(false);
            if (GUILayout.Button("Ground + roads", _preset)) Layers.GroundAndRoadsOnly();
            GUILayout.EndHorizontal();

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
