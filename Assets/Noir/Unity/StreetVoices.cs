using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.People;

namespace Noir.Unity
{
    /// <summary>
    /// One-line thoughts floating over citizens' heads — the street visibly reacting.
    ///
    /// The response machine always knew who noticed what, and the first evening anybody
    /// played a hit-and-run he stood in a street full of people who had plainly seen it and
    /// gave no sign (2026-08-17, the same session as the case ticker). The knowledge was
    /// there; the PERFORMANCE of it was not. This is the performance: the discoverer cries
    /// out, a gawker wonders aloud, the canvassed witness closes her door on a last word.
    ///
    /// DISPLAY-ONLY IMGUI, WHICH IS THE HALF OF IMGUI THAT WORKS. PlayerInteraction's header
    /// documents the measured lesson: under this project's input settings, runtime OnGUI
    /// receives no INPUT in a build — but it draws fine, and these labels take no input.
    ///
    /// NO RNG, like everything on the response path: a citizen's variant line keys on their
    /// own id, so the same person says the same thing every run of the same seed.
    ///
    /// Spec: docs/superpowers/specs/2026-08-17-witness-voices-design.md
    /// </summary>
    public sealed class StreetVoices : MonoBehaviour
    {
        private const int MaxBubbles = 8;
        private const float MaxDistance = 80f;      // metres from the camera before a line is not drawn
        private const float HeadHeight = 2.1f;      // above the feet, which is above the hat
        private const float DefaultSeconds = 4.5f;  // real seconds - a read, not a subtitle track

        private struct Bubble
        {
            public CitizenId Who;
            public string Line;
            public float Until;    // Time.unscaledTime - real seconds, same clock the fades use
        }

        private readonly Bubble[] _bubbles = new Bubble[MaxBubbles];
        private VillageHost _host;
        private GUIStyle _style;

        public static StreetVoices Create(VillageHost host, Transform parent)
        {
            var go = new GameObject("StreetVoices");
            go.transform.SetParent(parent, false);
            var voices = go.AddComponent<StreetVoices>();
            voices._host = host;
            return voices;
        }

        /// <summary>
        /// Put a line over somebody's head. A second line for the same person replaces the
        /// first - one head, one thought - and when the buffer is full the soonest-to-expire
        /// bubble is evicted: eight people talking at once is already a wall of text.
        /// </summary>
        public void Say(CitizenId who, string line, float seconds = DefaultSeconds)
        {
            if (!who.IsValid || string.IsNullOrEmpty(line)) return;
            float until = Time.unscaledTime + seconds;

            int slot = -1;
            float soonest = float.MaxValue;
            for (int i = 0; i < _bubbles.Length; i++)
            {
                if (_bubbles[i].Who.Equals(who) || _bubbles[i].Line == null || _bubbles[i].Until <= Time.unscaledTime)
                {
                    slot = i;
                    break;
                }
                if (_bubbles[i].Until < soonest) { soonest = _bubbles[i].Until; slot = slot < 0 ? i : slot; }
            }
            _bubbles[slot < 0 ? 0 : slot] = new Bubble { Who = who, Line = line, Until = until };
        }

        private const float BadgeHeight = 2.7f;     // a notch above the bubbles, so the two never collide
        private const string BadgeLine = "★ OFFICER";
        private GUIStyle _badgeStyle;

        private void OnGUI()
        {
            if (Application.isBatchMode) return;
            var cam = Camera.main;
            if (cam == null || _host == null || _host.Sim == null) return;

            if (_style == null)
                _style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 15,
                    fontStyle = FontStyle.Italic,
                    wordWrap = false,
                };
            if (_badgeStyle == null)
                _badgeStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    wordWrap = false,
                };

            DrawBadges(cam);

            float now = Time.unscaledTime;
            var oldColor = GUI.color;
            for (int i = 0; i < _bubbles.Length; i++)
            {
                if (_bubbles[i].Line == null || _bubbles[i].Until <= now) continue;

                var agent = _host.Sim.GetAgent(_bubbles[i].Who);
                var world = Space3D.ToWorld(agent.Position, HeadHeight);
                if ((world - cam.transform.position).sqrMagnitude > MaxDistance * MaxDistance) continue;

                var screen = cam.WorldToScreenPoint(world);
                if (screen.z <= 0f) continue;   // behind the camera

                float alpha = Mathf.Clamp01(_bubbles[i].Until - now);   // fade over the last second
                var size = _style.CalcSize(new GUIContent(_bubbles[i].Line));
                var rect = new Rect(screen.x - size.x / 2f - 8f,
                                    Screen.height - screen.y - size.y - 6f,
                                    size.x + 16f, size.y + 8f);

                GUI.color = new Color(0f, 0f, 0f, 0.55f * alpha);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = new Color(0.96f, 0.94f, 0.86f, alpha);
                GUI.Label(rect, _bubbles[i].Line, _style);
            }
            GUI.color = oldColor;
        }

        /// <summary>
        /// A gold tag over every police-active head, so the player can tell the officer
        /// from the crowd at a glance (owner request 2026-08-18: "a cop badge or something
        /// over their head so we know who they are"). Reads what the sim already says —
        /// Responding, or standing as DirectingTraffic at a cordon pinch — so it appears
        /// with the dispatch and vanishes with the release, no state of its own. Distance
        /// fade over the far half of the bubble range rather than a hard pop.
        /// </summary>
        private void DrawBadges(Camera cam)
        {
            var oldColor = GUI.color;
            int count = _host.Sim.AgentCount;
            for (int i = 0; i < count; i++)
            {
                var agent = _host.Sim.GetAgent(i);
                if (!agent.Responding && agent.Doing != Activity.DirectingTraffic) continue;

                var world = Space3D.ToWorld(agent.Position, BadgeHeight);
                float distance = (world - cam.transform.position).magnitude;
                if (distance > MaxDistance) continue;

                var screen = cam.WorldToScreenPoint(world);
                if (screen.z <= 0f) continue;

                float alpha = Mathf.Clamp01((MaxDistance - distance) / (MaxDistance * 0.5f));
                var size = _badgeStyle.CalcSize(new GUIContent(BadgeLine));
                var rect = new Rect(screen.x - size.x / 2f - 6f,
                                    Screen.height - screen.y - size.y - 4f,
                                    size.x + 12f, size.y + 6f);

                GUI.color = new Color(0f, 0f, 0f, 0.6f * alpha);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = new Color(0.94f, 0.78f, 0.2f, alpha);   // badge gold
                GUI.Label(rect, BadgeLine, _badgeStyle);
            }
            GUI.color = oldColor;
        }
    }
}
