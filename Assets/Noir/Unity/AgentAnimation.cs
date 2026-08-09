using System.Collections.Generic;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.People;

namespace Noir.Unity
{
    /// <summary>
    /// Which clip a person should be playing, given what the simulation says they are doing.
    ///
    /// THE MAPPING IS CONTENT, NOT CODE. It used to be a switch in this file, which meant every
    /// animation downloaded afterwards needed a recompile to be used at all - and an animation
    /// set is exactly the sort of thing that arrives a few files at a time over weeks. It now
    /// reads `Content/animations.txt`, in the same shape and for the same reason as `kinds.txt`:
    /// adding a clip is a line of text, and the file can be read by anything that wants to check
    /// it, including a test.
    ///
    /// MORE THAN ONE CLIP PER SITUATION, chosen per person off their citizen key. A row that
    /// names three ways of drinking gives a bar three sorts of drinker, stably - the same person
    /// drinks the same way every time you look at them, because the choice is a hash of who they
    /// are rather than a roll.
    ///
    /// INERT UNTIL THE CLIPS EXIST. Nothing here loads or asserts an animation. Ask for a clip
    /// and it answers a name; <see cref="Drive"/> does nothing when handed a null Animator or a
    /// controller with no such state. That is what makes a half-imported set safe to run.
    ///
    /// IN PLACE, ALWAYS. Every locomotive clip must be downloaded with Mixamo's "In Place" box
    /// ticked. `Simulation` decides where everybody is by pathfinding and `AgentMeshView` draws
    /// them at the position it computed; a clip carrying root motion drives the transform ITSELF,
    /// so the two would fight over the same number and people would walk off their own paths.
    /// `Noir/Check The Animations` measures it. See docs/ASSETS.md.
    /// </summary>
    public static class AgentAnimation
    {
        /// <summary>The row every situation falls back to.</summary>
        public const string Default = "default";

        /// <summary>The two rows that answer "on the move", which beats whatever they are doing.</summary>
        public const string Moving = "moving", Hurrying = "hurrying";

        private static Dictionary<string, string[]> _rows;
        private static Dictionary<string, float> _paces;

        /// <summary>
        /// How far above the speed it was animated at a clip may be pushed.
        ///
        /// Beyond this the legs blur and the eye stops resolving a stride at all, which is worse
        /// than the sliding the match was meant to cure. The town's clock runs to 300x, so this is
        /// reached routinely and ON PURPOSE: the honest answer at sixty times speed is that no clip
        /// can show it, and the same admission is already made for the primitive figures' swing.
        ///
        /// THERE IS DELIBERATELY NO FLOOR. A slow walk played slowly is correct - that is a person
        /// dawdling, and clamping it up would put the skate back in for exactly the people it is
        /// easiest to watch. Zero is correct too: a paused town should freeze, not treadmill.
        /// </summary>
        private const float Fastest = 2f;

        /// <summary>
        /// The table, read once.
        ///
        /// A missing or unreadable file is not an error worth stopping for: it leaves every
        /// situation empty, every lookup null, and every figure standing still - which is exactly
        /// what the city looked like before any of this existed.
        /// </summary>
        public static IReadOnlyDictionary<string, string[]> Rows
        {
            get
            {
                if (_rows != null) return _rows;

                // PARSED IN CORE NOW - see AnimationTable. This method used to hold the parser,
                // which meant the only way to test it was a PlayMode run, which meant in practice
                // it was never tested at all: the dotted-clip fault lived in exactly that gap.
                // What is left here is the two things Core cannot do - find the file, and log.
                string text = null;
                try { text = Content.Read("animations.txt"); }
                catch { /* see the summary: no table is a survivable state */ }

                var table = AnimationTable.Parse(text);

                foreach (var warning in table.Warnings) Debug.LogWarning("[anim] " + warning);

                _rows = new Dictionary<string, string[]>(table.Rows,
                                                         System.StringComparer.OrdinalIgnoreCase);
                _paces = new Dictionary<string, float>(table.Paces,
                                                       System.StringComparer.OrdinalIgnoreCase);
                return _rows;
            }
        }

        /// <summary>Read the table again. For the editor checks, which change the file.</summary>
        public static void Reload() => _rows = null;

        /// <summary>
        /// The clip this person should play, or null for "animate nothing".
        ///
        /// Null is a real answer rather than a failure: it is what somebody asleep behind a wall
        /// should be given, and it is what everybody gets before any clips are imported.
        /// </summary>
        /// <param name="who">
        /// Which person, so a row naming several clips always gives THIS person the same one.
        /// </param>
        public static string ClipFor(Activity doing, bool moving, bool hurrying = false,
                                     ulong who = 0)
        {
            var clips = Pick(Resolve(doing, moving, hurrying));
            if (clips == null || clips.Length == 0) return null;

            if (clips.Length == 1) return clips[0];

            // Stable per person: a hash of who they are, never a roll, so nobody changes the way
            // they stand between one look and the next.
            return clips[(int)(Mix(who) % (ulong)clips.Length)];
        }

        /// <summary>
        /// Which row this situation actually lands on, once falling through is taken into account.
        ///
        /// A situation with no row of its own falls through to `default` - but a row that EXISTS
        /// and names nothing, like `asleep`, means nothing on purpose and must not. The distinction
        /// is the whole reason this is a lookup rather than a null check.
        /// </summary>
        private static string Resolve(Activity doing, bool moving, bool hurrying)
        {
            string row = moving ? (hurrying ? Hurrying : Moving)
                                : doing.ToString().ToLowerInvariant();
            return Rows.ContainsKey(row) ? row : Default;
        }

        private static string[] Pick(string row) =>
            Rows.TryGetValue(row, out var clips) ? clips : null;

        /// <summary>
        /// The speed the clips on a row were animated at, in metres a second, or 0 for "this is
        /// not locomotion - play it at the rate it was made".
        /// </summary>
        public static float PaceOf(string row)
        {
            _ = Rows;                                    // the table builds both dictionaries
            return _paces.TryGetValue(row, out float p) ? p : 0f;
        }

        private static ulong Mix(ulong v)
        {
            v ^= v >> 33; v *= 0xFF51AFD7ED558CCDUL;
            v ^= v >> 33; v *= 0xC4CEB9FE1A85EC53UL;
            return v ^ (v >> 33);
        }

        /// <summary>
        /// Put an Animator into the right state, and do nothing if it cannot be.
        ///
        /// Crossfaded rather than snapped, because a figure changing gait between two frames is
        /// what makes a crowd read as puppets. A quarter second is under the threshold at which
        /// anybody notices a transition and over the one at which they notice a pop.
        ///
        /// Every reason this can fail is silent by design: no Animator, no controller, a
        /// controller with no such state. The city has to keep running while the animation set is
        /// half imported, or nobody will ever import it a few clips at a time.
        /// </summary>
        public static void Drive(Animator animator, Activity doing, bool moving,
                                 bool hurrying = false, ulong who = 0, float pace = -1f)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;

            string row = Resolve(doing, moving, hurrying);
            string clip = ClipFor(doing, moving, hurrying, who);
            if (string.IsNullOrEmpty(clip)) return;

            // THE CURE FOR SLIDING FEET, and it is arithmetic rather than an asset. A walk cycle
            // is animated at one speed; the simulation moves people at another, and at 1x the gap
            // between the two IS the skate. Playing the clip at the ratio closes it exactly.
            //
            // Set before the early return below, because the state does not change while somebody
            // walks the length of a street and their pace does.
            float made = PaceOf(row);
            animator.speed = made > 0f && pace >= 0f
                ? Mathf.Min(pace / made, Fastest)
                : 1f;

            int state = Animator.StringToHash(clip);
            if (!animator.HasState(0, state)) return;

            var now = animator.GetCurrentAnimatorStateInfo(0);
            if (now.shortNameHash == state) return;          // already there; do not restart it

            animator.CrossFadeInFixedTime(state, 0.25f);
        }
    }
}
