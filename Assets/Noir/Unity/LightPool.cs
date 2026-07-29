using System;
using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// A fixed number of real lights, lent out to whichever windows and lamps are worth
    /// spending them on.
    ///
    /// URP stops adding lights at MAX_VISIBLE_LIGHT_COUNT_DESKTOP - 256 - and when it does it
    /// simply drops the rest. No error, no warning, nothing in the log. Ashcombe runs 84 point
    /// lights at 109 people, one per roofed building and one per lamp post, which crosses 256
    /// at somewhere around 330 people - well inside the six hundred this village is being built
    /// towards. It would have looked like a tuning problem rather than a ceiling, and the thing
    /// that quietly stopped working would have been the best thing in the project.
    ///
    /// So the count stops growing with the town. A few dozen Light components exist for ever,
    /// and they are dealt out on a cadence to the nearest emitters that are actually on.
    ///
    /// THE GLOW IS NOT THE LIGHT, and that split is the whole reason this works. Every window
    /// pane and every lantern is an emissive surface driven through a MaterialPropertyBlock;
    /// they cost nothing per-emitter and none of them is pooled, so all several thousand of
    /// them still light up when somebody is home. What a real Light adds on top is the spill
    /// onto the wall under the window and the road under the lamp, and that only reads from
    /// close by. A lit window at the far end of the village still reads as lit, because the
    /// thing you are actually looking at from there is the pane.
    /// </summary>
    public sealed class LightPool
    {
        /// <summary>
        /// How many real lights exist. Thirty-two covers a radius of sixty-odd metres at
        /// Ashcombe's density, which is most of what fog leaves visible at street level, and it
        /// is an eighth of URP's ceiling - so the headroom is there if the spill radius turns
        /// out to read short once there is a six hundred person town to walk around in.
        /// </summary>
        public const int DefaultSize = 32;

        /// <summary>
        /// The shortest gap between reassignments, in real seconds. SunRig gates on this.
        ///
        /// A slot handing over fades the old emitter out before it adopts the new one, and at
        /// 300x a village minute is a fifth of a second - shorter than that fade. Without a
        /// real-time floor under the clock cadence, slots at the churn boundary would never
        /// finish fading and would sit permanently dark.
        /// </summary>
        public const float ReassignInterval = 0.35f;

        /// <summary>
        /// Intensity units per second when a light is following the occupancy behind it. This
        /// is the ease the window lights already had before they were pooled, kept deliberately:
        /// at 300x a house going dark is otherwise a hard edge.
        /// </summary>
        private const float SwitchRate = 6f;

        /// <summary>
        /// Faster, because this fade covers a mechanical event rather than a dramatic one. The
        /// slot has to reach zero before it can move to a different building, and every
        /// millisecond of that is a light which ought to be on and is not.
        /// </summary>
        private const float HandoverRate = 12f;

        /// <summary>
        /// How many metres of head start an emitter already holding a slot gets when the pool
        /// is dealt again.
        ///
        /// Without it, two emitters a hair apart at the edge of the pool trade the slot back and
        /// forth every cadence tick as the camera drifts, and every trade costs a fade out and a
        /// fade in - so the pair would blink at each other for as long as you stood there.
        ///
        /// It is inert for the offline snapshot renderer, which calls Reset before every shot
        /// and therefore never has an incumbent to favour.
        /// </summary>
        public float HoldBonus = 6f;

        // ---- the emitters: every window and every lamp in the village, however many that is ----
        //
        // Parallel lists rather than a list of small objects, matching SunRig.Fixtures: the only
        // thing ever done with them is a walk by index.
        private readonly List<Vector3> _at = new List<Vector3>();
        private readonly List<Color> _colour = new List<Color>();
        private readonly List<float> _range = new List<float>();
        private readonly List<int> _key = new List<int>();
        private readonly List<float> _want = new List<float>();
        private readonly List<float> _score = new List<float>();
        private readonly List<int> _slotOf = new List<int>();
        private readonly List<int> _stamp = new List<int>();

        // ---- the slots: the only Light components in the village bar the sun ----
        private readonly Light[] _light;

        /// <summary>Which emitter each slot's Light is currently CONFIGURED for.</summary>
        private readonly int[] _holder;

        /// <summary>Which emitter each slot has been PROMISED to. Differs from the holder only
        /// while a handover is fading out.</summary>
        private readonly int[] _target;

        private readonly float[] _level;
        private readonly bool[] _on;

        private readonly List<int> _shortlist = new List<int>();
        private readonly Comparison<int> _byImportance;
        private int _round;

        public LightPool(Transform parent, int size = DefaultSize)
        {
            if (size < 1) size = 1;

            var root = new GameObject("Light Pool");
            root.transform.SetParent(parent, false);

            _light = new Light[size];
            _holder = new int[size];
            _target = new int[size];
            _level = new float[size];
            _on = new bool[size];

            for (int s = 0; s < size; s++)
            {
                var go = new GameObject($"pooled light {s}");
                go.transform.SetParent(root.transform, false);

                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;   // even thirty-two shadowed point lights is too many
                light.intensity = 0f;
                light.enabled = false;

                _light[s] = light;
                _holder[s] = -1;
                _target[s] = -1;
            }

            _byImportance = Compare;
        }

        public int EmitterCount => _at.Count;
        public int SlotCount => _light.Length;

        /// <summary>
        /// Register something that can be lit, and get back the handle used to drive it.
        ///
        /// No GameObject and no Light is created here - that is the point. The key is the
        /// tiebreak when two emitters are the same distance away, and it has to be a property
        /// of the village rather than of the frame: see Compare.
        /// </summary>
        public int Add(Vector3 at, Color colour, float range, int key)
        {
            _at.Add(at);
            _colour.Add(colour);
            _range.Add(range);
            _key.Add(key);
            _want.Add(0f);
            _score.Add(0f);
            _slotOf.Add(-1);
            _stamp.Add(0);
            return _at.Count - 1;
        }

        /// <summary>
        /// What this emitter would be shining at if it had a light. Cheap enough to call for
        /// every window in the town every frame: it is one array write and nothing else.
        /// </summary>
        public void SetIntensity(int emitter, float intensity)
        {
            _want[emitter] = intensity > 0f ? intensity : 0f;
        }

        /// <summary>
        /// Deal the slots out to the nearest emitters that are lit.
        ///
        /// Given the same viewpoint, the same set of lit emitters and the same previous
        /// assignment, this produces exactly the same answer every time. That matters because
        /// the offline snapshot renderer runs it, and two runs of that at the same hour have to
        /// be the same picture or the whole set stops being a regression test.
        /// </summary>
        public void Reassign(Vector3 viewpoint)
        {
            _round++;

            _shortlist.Clear();
            for (int e = 0; e < _at.Count; e++)
            {
                // A dark window is not competing for anything.
                if (_want[e] <= 0f) continue;

                float d = Vector3.Distance(viewpoint, _at[e]);
                if (_slotOf[e] >= 0) d -= HoldBonus;
                _score[e] = d;
                _shortlist.Add(e);
            }

            _shortlist.Sort(_byImportance);

            int lit = Mathf.Min(_shortlist.Count, _light.Length);
            for (int i = 0; i < lit; i++) _stamp[_shortlist[i]] = _round;

            // Anything holding a slot that did not make the cut gives up its CLAIM here. The
            // slot goes on showing it until Tick has faded it out - releasing the claim and
            // snapping the light to somewhere else in the same frame is exactly the pop this
            // whole arrangement exists to avoid.
            for (int s = 0; s < _light.Length; s++)
            {
                int held = _target[s];
                if (held < 0 || _stamp[held] == _round) continue;
                _slotOf[held] = -1;
                _target[s] = -1;
            }

            int free = 0;
            for (int i = 0; i < lit; i++)
            {
                int e = _shortlist[i];
                if (_slotOf[e] >= 0) continue;

                // Prefer the slot that is still fading this emitter out, if there is one. A
                // window that goes dark for a minute and comes back on would otherwise be
                // adopted by a second slot while the first was still letting go of it, and for
                // a fraction of a second there would be two point lights inside one cottage.
                int slot = -1;
                for (int s = 0; s < _light.Length; s++)
                    if (_target[s] < 0 && _holder[s] == e) { slot = s; break; }

                if (slot < 0)
                {
                    while (free < _light.Length && _target[free] >= 0) free++;
                    if (free >= _light.Length) break;   // cannot happen: lit <= SlotCount
                    slot = free;
                }

                _target[slot] = e;
                _slotOf[e] = slot;
            }
        }

        /// <summary>
        /// A total order, and it has to be total. List.Sort is an introsort and therefore not
        /// stable, so two emitters the sort cannot tell apart would come out in whatever order
        /// the partitioning happened to leave them - which is the sort of thing that produces
        /// two different night snapshots from the same village and no way to see why.
        ///
        /// Distance decides it; the village-derived key decides ties; the registration index,
        /// which is unique by construction, decides the rest. Nothing here can vary between
        /// runs, and in particular none of it is Unity's per-frame visible-light ordering.
        /// </summary>
        private int Compare(int a, int b)
        {
            int byDistance = _score[a].CompareTo(_score[b]);
            if (byDistance != 0) return byDistance;

            int byKey = _key[a].CompareTo(_key[b]);
            return byKey != 0 ? byKey : a.CompareTo(b);
        }

        /// <summary>Move every fade along. Call it every frame; it walks the slots, not the town.</summary>
        public void Tick(float deltaTime)
        {
            for (int s = 0; s < _light.Length; s++)
            {
                if (_holder[s] != _target[s])
                {
                    _level[s] = Mathf.MoveTowards(_level[s], 0f, HandoverRate * deltaTime);
                    if (_level[s] > 0f) { Apply(s); continue; }

                    _holder[s] = _target[s];
                    Configure(s);
                }

                int e = _holder[s];
                _level[s] = Mathf.MoveTowards(_level[s], e >= 0 ? _want[e] : 0f,
                                              SwitchRate * deltaTime);
                Apply(s);
            }
        }

        /// <summary>
        /// Finish every handover and every fade outright. A still has no frames to ease over,
        /// so the offline renderer settles the pool after it has placed its camera.
        /// </summary>
        public void Settle()
        {
            for (int s = 0; s < _light.Length; s++)
            {
                if (_holder[s] != _target[s]) { _holder[s] = _target[s]; Configure(s); }

                int e = _holder[s];
                _level[s] = e >= 0 ? _want[e] : 0f;
                Apply(s);
            }
        }

        /// <summary>
        /// Forget every assignment and go dark, as though nothing had ever been lit.
        ///
        /// The offline snapshot renderer calls this before each shot. Reassign is deterministic
        /// on its own, but WHICH slot an emitter lands in depends on what the pool was holding
        /// beforehand, and lights accumulate additively - a different slot order is a different
        /// order of floating-point additions, so two otherwise identical pictures could disagree
        /// in the last bit of a handful of pixels, which is exactly the kind of difference that
        /// wastes an afternoon. From nothing, the slots are a pure function of where the camera
        /// is and who is lit, and reordering the shot table cannot change a single one.
        ///
        /// Never called in play mode: there, the previous assignment is the whole point.
        /// </summary>
        public void Reset()
        {
            for (int e = 0; e < _slotOf.Count; e++) _slotOf[e] = -1;

            for (int s = 0; s < _light.Length; s++)
            {
                _holder[s] = -1;
                _target[s] = -1;
                _level[s] = 0f;
                _on[s] = false;
                _light[s].intensity = 0f;
                _light[s].enabled = false;
            }
        }

        private void Configure(int s)
        {
            int e = _holder[s];
            if (e < 0) return;

            var light = _light[s];
            light.transform.position = _at[e];
            light.color = _colour[e];
            light.range = _range[e];
        }

        private void Apply(int s)
        {
            bool on = _level[s] > 0.001f;

            // Disabled rather than merely dark. A Light at zero intensity is still a light as
            // far as culling is concerned, and the one thing this class has to guarantee is
            // that the number URP has to think about is a constant.
            if (_on[s] != on) { _on[s] = on; _light[s].enabled = on; }
            if (on) _light[s].intensity = _level[s];
        }
    }
}
