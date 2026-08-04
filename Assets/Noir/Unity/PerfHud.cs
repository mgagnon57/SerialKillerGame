using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Noir.Unity
{
    /// <summary>
    /// Frames per second, frame time, and what the frame is actually spending itself on.
    ///
    /// WHY IT EXISTS. Three rounds of "still choppy as hell" were answered by guessing - build
    /// timings, renderer counts, layers skipped - and a build-time number says nothing about a
    /// frame. Renderer count is not draw calls, draw calls are not milliseconds, and none of them
    /// tell you whether the cost is the GPU drawing or the CPU simulating. This measures the
    /// thing being complained about.
    ///
    /// WORST FRAME IS THE NUMBER THAT MATTERS. Chop is not a low average - it is a good average
    /// with spikes in it, and an average happily hides one 200 ms frame a second inside a
    /// perfectly respectable 60 fps. The 1% worst over the last two seconds is what you feel.
    ///
    /// It costs one GUI.Label block and a handful of counters, and the expensive lookups - the
    /// renderer census - are refreshed once a second rather than per frame, because an
    /// instrument that changes what it measures is worse than no instrument.
    /// </summary>
    public sealed class PerfHud : MonoBehaviour
    {
        private VillageHost _host;

        // A ring of recent frame times, for the percentile. Two seconds at 60 fps.
        private readonly float[] _frames = new float[120];
        private int _at;
        private int _seen;

        private float _smoothMs = 16f;
        private float _nextCensus;
        private int _renderers, _lights, _active;
        private bool _open = true;

        private GUIStyle _box, _text, _head;
        private bool _styled;

        public static PerfHud Create(VillageHost host, Transform parent)
        {
            var go = new GameObject("PerfHud");
            go.transform.SetParent(parent, false);
            var hud = go.AddComponent<PerfHud>();
            hud._host = host;
            return hud;
        }

        private void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            _frames[_at] = ms;
            _at = (_at + 1) % _frames.Length;
            if (_seen < _frames.Length) _seen++;

            // Exponential smoothing for the headline, so it is readable rather than flickering.
            _smoothMs = Mathf.Lerp(_smoothMs, ms, 0.08f);

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame) _open = !_open;

            if (Time.unscaledTime >= _nextCensus)
            {
                _nextCensus = Time.unscaledTime + 1f;
                RecomputeWorst();
                Census();
            }
        }

        /// <summary>
        /// Counted once a second, not per frame. FindObjectsByType is not cheap and an instrument
        /// that shows up in its own measurement is useless.
        /// </summary>
        private void Census()
        {
            var all = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            _renderers = all.Length;
            _active = 0;
            foreach (var r in all)
                if (r.enabled && r.gameObject.activeInHierarchy) _active++;

            _lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length;
        }

        // Sorted in place in a buffer that is allocated ONCE. The first version built a fresh
        // List and sorted it inside OnGUI, which Unity calls several times per frame for layout
        // and repaint - so the instrument was allocating a few hundred KB a second and adding to
        // the garbage collections it existed to report. Recomputed with the census, once a
        // second, because a percentile over a two-second window does not change faster than that.
        private readonly float[] _sortBuffer = new float[120];
        private float _worstCached;

        private void RecomputeWorst()
        {
            if (_seen == 0) { _worstCached = 0f; return; }
            System.Array.Copy(_frames, _sortBuffer, _seen);
            System.Array.Sort(_sortBuffer, 0, _seen);
            _worstCached = _sortBuffer[Mathf.Clamp(Mathf.RoundToInt(_seen * 0.99f) - 1, 0, _seen - 1)];
        }

        /// <summary>The frame time only 1 in 100 frames is worse than - the chop you feel.</summary>
        private float Worst() => _worstCached;

        private static int F(int at1080) =>
            Mathf.RoundToInt(at1080 * Mathf.Max(1f, Screen.height / 1080f));

        private void Style()
        {
            if (_styled) return;
            _styled = true;

            var bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.07f, 0.85f));
            bg.Apply();

            _box = new GUIStyle { padding = new RectOffset(F(10), F(10), F(8), F(8)) };
            _box.normal.background = bg;

            _text = new GUIStyle
            {
                fontSize = F(13),
                richText = true,
                font = Font.CreateDynamicFontFromOSFont("Consolas", F(13)),
            };
            _text.normal.textColor = new Color(0.86f, 0.87f, 0.84f);

            _head = new GUIStyle(_text) { fontSize = F(20), fontStyle = FontStyle.Bold };
        }

        private void OnGUI()
        {
            Style();

            if (!_open)
            {
                GUI.Label(new Rect(Screen.width - F(150), Screen.height - F(28), F(140), F(22)),
                          "<color=#9aa>F3 — perf</color>", _text);
                return;
            }

            float fps = _smoothMs > 0.001f ? 1000f / _smoothMs : 0f;
            float worst = Worst();

            // Green above 50, amber down to 25, red below - the thresholds at which panning a
            // map stops feeling attached to the mouse.
            string colour = fps >= 50f ? "#8c8" : fps >= 25f ? "#dc8" : "#e77";

            // Under the clock bar, at whatever height the bar currently is - VillageUI's scale is
            // tunable at runtime with Ctrl+= and Ctrl+-, so a fixed offset gets covered up the
            // moment the owner makes the UI bigger. Which is exactly what happened.
            float w = F(230);
            var area = new Rect(Screen.width - w - F(12),
                                VillageUI.BarHeight + F(8), w, F(276));
            Bounds = area;
            GUILayout.BeginArea(area, _box);

            GUILayout.Label($"<color={colour}>{fps,5:0} fps</color>   {_smoothMs,5:0.1} ms", _head);
            GUILayout.Space(F(4));
            Graph(GUILayoutUtility.GetRect(w - F(22), F(58)));
            GUILayout.Space(F(4));
            GUILayout.Label($"worst 1%   <b>{worst,6:0.1} ms</b>", _text);
            GUILayout.Label($"renderers  {_active:N0} on / {_renderers:N0}", _text);
            GUILayout.Label($"lights     {_lights:N0}", _text);

#if UNITY_EDITOR
            GUILayout.Space(F(4));
            GUILayout.Label($"draw calls {UnityEditor.UnityStats.drawCalls:N0}", _text);
            GUILayout.Label($"batches    {UnityEditor.UnityStats.batches:N0}", _text);
            GUILayout.Label($"triangles  {UnityEditor.UnityStats.triangles:N0}", _text);
            GUILayout.Label($"set-pass   {UnityEditor.UnityStats.setPassCalls:N0}", _text);
#endif
            if (_host != null && _host.Sim != null)
            {
                GUILayout.Space(F(4));
                GUILayout.Label($"people     {_host.Sim.AgentCount:N0} simulated", _text);
            }

            GUILayout.Space(F(4));
            GUILayout.Label("<color=#889>F3 hides this</color>", _text);
            GUILayout.EndArea();
        }

        /// <summary>
        /// The last two seconds of frame times, oldest on the left, one column per frame.
        ///
        /// THE NUMBER CANNOT SHOW THIS. "Jumps all over the place when moving around" is a shape,
        /// not a value - a steady 40 fps and a 70 fps average with a 200 ms hitch every half
        /// second read almost the same on a counter and feel nothing alike. The bars show which
        /// one it is, and whether the spikes line up with what you were doing at the time.
        ///
        /// Auto-scaled to the worst frame in the window, with rules at 60 and 30 fps, so a spike
        /// is always visible instead of clipping off the top and looking like a plateau.
        /// </summary>
        private void Graph(Rect area)
        {
            if (Event.current.type != EventType.Repaint || _seen < 2) return;

            var px = Texture2D.whiteTexture;
            var was = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(area, px);

            float peak = 0f;
            for (int i = 0; i < _seen; i++) peak = Mathf.Max(peak, _frames[i]);
            float ceiling = Mathf.Max(33.4f, peak * 1.1f);

            // 60 fps and 30 fps rules, so a bar's height means something without a legend.
            foreach (var (ms, tint) in new[]
            {
                (16.7f, new Color(0.55f, 0.75f, 0.55f, 0.45f)),
                (33.3f, new Color(0.85f, 0.75f, 0.45f, 0.40f)),
            })
            {
                if (ms > ceiling) continue;
                float y = area.yMax - (ms / ceiling) * area.height;
                GUI.color = tint;
                GUI.DrawTexture(new Rect(area.x, y, area.width, 1f), px);
            }

            float bar = Mathf.Max(1f, area.width / _frames.Length);
            for (int i = 0; i < _seen; i++)
            {
                // Oldest on the left: _at is where the NEXT sample goes, so it is also the oldest.
                int idx = (_at + (_frames.Length - _seen) + i) % _frames.Length;
                float ms = _frames[idx];
                float h = Mathf.Clamp01(ms / ceiling) * area.height;

                GUI.color = ms <= 20f ? new Color(0.53f, 0.78f, 0.53f, 0.95f)
                          : ms <= 40f ? new Color(0.87f, 0.78f, 0.47f, 0.95f)
                                      : new Color(0.90f, 0.45f, 0.45f, 0.95f);
                GUI.DrawTexture(new Rect(area.x + i * bar, area.yMax - h, bar, h), px);
            }

            GUI.color = new Color(0.55f, 0.57f, 0.60f, 1f);
            GUI.Label(new Rect(area.x + F(3), area.y, area.width, F(16)),
                      $"<size={F(10)}>{ceiling:0} ms</size>", _text);
            GUI.color = was;
        }

        /// <summary>Where it sits, so VillageUI can stop a click on it reaching the town.</summary>
        public static Rect Bounds { get; private set; }

        private void OnDisable() => Bounds = new Rect();
    }
}
