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
        public const string Moving = "moving";

        // `hurrying` IS GONE, AND THE CLIP BEHIND IT WAS NEVER PLAYED ONCE.
        //
        // It required somebody MOVING while AtThePlayground, which the simulation never produces:
        // a child at the playground is AT the playground, not travelling to it. Measured before
        // deleting - `Running=` appears zero times across all three recorded runs, against
        // censuses reporting up to 317 people on the move. The simulation's ceiling is about
        // 3.4 mph and the clip is authored for 8 mph, so it could not have looked right either.
        //
        // If running belongs in this game it belongs to a story event - somebody fleeing - and it
        // will want its own channel rather than a flag on a commute. Owner's decision, 2026-08-08.

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
        public static void Reload()
        {
            _rows = null;
            _missing.Clear();       // so a name that is still wrong after a rebuild says so again
        }

        /// <summary>
        /// The clip this person should play, or null for "animate nothing".
        ///
        /// Null is a real answer rather than a failure: it is what somebody asleep behind a wall
        /// should be given, and it is what everybody gets before any clips are imported.
        /// </summary>
        /// <param name="who">
        /// Which person, so a row naming several clips always gives THIS person the same one.
        /// </param>
        public static string ClipFor(Activity doing, bool moving, ulong who = 0)
        {
            var clips = Pick(Resolve(doing, moving));
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
        private static string Resolve(Activity doing, bool moving) =>
            Resolve(new Situation(doing, moving));

        /// <summary>
        /// Everything the table is allowed to know about a person, and nothing else.
        ///
        /// `Drive` used to receive the Activity alone, so WHERE somebody was and WHO they were got
        /// thrown away before the lookup. Eight faults came out of that one omission: a farmer
        /// typed and faxed, four in ten lunchtime diner customers played `Drunk Idle` because the
        /// diner and the tavern share `atthepub`, adults on a dinner break at the playground
        /// cheered like children, and children smoked at home.
        ///
        /// A struct rather than four more parameters because `Drive` already had five, and because
        /// this is one idea - the situation a person is in - rather than four unrelated facts.
        /// </summary>
        public readonly struct Situation
        {
            public readonly Activity Doing;
            public readonly bool Moving;

            /// <summary>The kind of place they are at, lowercase, or null. From `kinds.txt`.</summary>
            public readonly string Place;

            /// <summary>`child`, `adult` or `elder`, or null.</summary>
            public readonly string Stage;

            /// <summary>`male` or `female`, or null.</summary>
            public readonly string Sex;

            public Situation(Activity doing, bool moving,
                             string place = null, string stage = null, string sex = null)
            {
                Doing = doing; Moving = moving;
                Place = place; Stage = stage; Sex = sex;
            }
        }

        /// <summary>
        /// Which row this situation actually lands on, once falling through is taken into account.
        ///
        /// A situation with no row of its own falls through to `default` - but a row that EXISTS
        /// and names nothing, like `asleep`, means nothing on purpose and must not. The distinction
        /// is the whole reason this is a lookup rather than a null check.
        ///
        /// THE LADDER, MOST SPECIFIC FIRST. Place beats person, and stage beats sex:
        ///
        ///     atwork@tavern:elder     everything known
        ///     atwork@tavern:male
        ///     atwork@tavern           the place
        ///     atwork:elder            the person
        ///     atwork:male
        ///     atwork                  the bare activity
        ///     default                 the net
        ///
        /// Stage before sex because a child is a child before he is a boy: `athome:child` should
        /// beat `athome:male` for a nine-year-old, or SIM-7's whole point - children not smoking -
        /// is lost the moment somebody adds a row for men.
        ///
        /// A QUALIFIED KEY THAT MATCHES NOTHING IS SILENT BY DESIGN, which is what makes a typo
        /// dangerous: `atwork@tavren` costs nothing, warns nobody and simply never fires. Take the
        /// place names from `Content/kinds.txt`. `AnimationCheck` measures the fall-through so a
        /// row nothing ever reaches can at least be counted.
        /// </summary>
        private static string Resolve(Situation what)
        {
            string bare = what.Moving ? Moving : what.Doing.ToString().ToLowerInvariant();

            if (what.Place != null)
            {
                string at = bare + "@" + what.Place;
                if (what.Stage != null && Rows.ContainsKey(at + ":" + what.Stage)) return at + ":" + what.Stage;
                if (what.Sex != null && Rows.ContainsKey(at + ":" + what.Sex)) return at + ":" + what.Sex;
                if (Rows.ContainsKey(at)) return at;
            }

            if (what.Stage != null && Rows.ContainsKey(bare + ":" + what.Stage)) return bare + ":" + what.Stage;
            if (what.Sex != null && Rows.ContainsKey(bare + ":" + what.Sex)) return bare + ":" + what.Sex;

            return Rows.ContainsKey(bare) ? bare : Default;
        }

        /// <summary>The clip for a full situation. See <see cref="ClipFor(Activity,bool,ulong)"/>.</summary>
        public static string ClipFor(Situation what, ulong who = 0)
        {
            var clips = Pick(Resolve(what));
            if (clips == null || clips.Length == 0) return null;
            if (clips.Length == 1) return clips[0];
            return clips[(int)(Mix(who) % (ulong)clips.Length)];
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
        /// <summary>
        /// THE STRIDE A CLIP WAS ANIMATED FOR, in metres. `AgentLook.Stride` is `Height * 0.82`,
        /// and the adult baseline height is 1.71 m - so this is the stride of the average adult
        /// the walk cycles were authored against.
        ///
        /// Pinned deliberately: an ordinary adult divides by itself, gets exactly 1, and NOTHING
        /// about the town's walk changes. The term only bites where it should - on the short and
        /// the tall, and hardest on children, whose legs are a metre and who were covering the
        /// simulation's ground with an adult's stride length.
        /// </summary>
        private const float AdultStride = 1.71f * 0.82f;

        public static void Drive(Animator animator, Situation what,
                                 ulong who = 0, float pace = -1f, float stride = 0f)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;

            // ONE Resolve, NOT TWO. `ClipFor` resolves the row again internally, so every figure
            // paid for the lookup and the `doing.ToString().ToLowerInvariant()` allocation twice
            // per frame - 1,385 people times two strings times sixty frames, for one answer.
            string row = Resolve(what);
            string clip = PickFor(row, who);

            // THE CURE FOR SLIDING FEET, and it is arithmetic rather than an asset. A walk cycle
            // is animated at one speed; the simulation moves people at another, and at 1x the gap
            // between the two IS the skate. Playing the clip at the ratio closes it exactly.
            //
            // ABOVE THE EMPTY-ROW RETURN, WHICH IS WHERE IT WAS NOT. A row that names nothing -
            // `asleep` - used to return before this line, leaving the animator running at whatever
            // CLAMPED ratio the person's last walk had set. So somebody who went to bed mid-stride
            // kept their walk speed forever, and it was the clamped one at that.
            float made = PaceOf(row);
            float wanted = made > 0f && pace >= 0f ? Mathf.Min(pace / made, Fastest) : 1f;

            // AND A TERM FOR THE LEGS THAT ARE ACTUALLY DOING IT. `pace / made` closes the gap
            // between the speed the clip was animated at and the speed the simulation is moving
            // somebody - which is right for the body the clip was animated FOR, and wrong for
            // every other body in town.
            //
            // A short person covers the same ground with more steps: their stride is shorter, so
            // to walk at the sim's speed without skating their cycle has to run FASTER. Linear in
            // height, because stride is - `AgentLook.Stride` is `Height * 0.82`. An average adult
            // divides by itself and gets 1, so the town's walk is unchanged for the majority;
            // a ten-year-old at 1.35 m gets 1.27x and stops gliding.
            //
            // Clamped by the same `Fastest` ceiling as the pace ratio, and only where a stride was
            // actually passed - `stride <= 0` is the caller saying "I do not know", and inventing
            // a body for it would be worse than leaving the clip alone.
            if (stride > 0.01f)
                wanted = Mathf.Min(wanted * (AdultStride / stride), Fastest);

            if (string.IsNullOrEmpty(clip)) { animator.speed = wanted; return; }

            int state = Animator.StringToHash(clip);

            // FREEZE, DO NOT TREADMILL - AND THIS IS THE WHOLE DOTTED-CLIP FAULT.
            //
            // The speed write used to happen ABOVE this check, so a clip whose state does not
            // exist left the animator running the state it was already in, at walking speed, and
            // returned in silence. A person standing at a door played the walk cycle on the spot.
            // Speed 0 holds the pose instead, which is visibly a person standing still rather
            // than a person jogging nowhere - and it is the honest thing to show when the game
            // does not have the clip it wanted.
            if (!animator.HasState(0, state))
            {
                animator.speed = 0f;
                Missing(clip);
                return;
            }

            animator.speed = wanted;

            var now = animator.GetCurrentAnimatorStateInfo(0);
            if (now.shortNameHash == state) return;          // already there; do not restart it

            // WHERE IN THE CYCLE THEY START, so a street is not a chorus line. Two people given
            // the same idle on the same frame otherwise breathe in perfect unison, which reads as
            // machinery. Hashed off the citizen rather than rolled, so the same person always
            // enters the same clip at the same point and a seed still reproduces the village.
            float offset = who == 0 ? 0f : (Mix(who) % 1000UL) / 1000f;

            animator.CrossFadeInFixedTime(state, 0.25f, 0, offset);
        }

        /// <summary>The clip for an already-resolved row. See <see cref="ClipFor"/>.</summary>
        private static string PickFor(string row, ulong who)
        {
            var clips = Pick(row);
            if (clips == null || clips.Length == 0) return null;
            if (clips.Length == 1) return clips[0];
            return clips[(int)(Mix(who) % (ulong)clips.Length)];
        }

        /// <summary>
        /// Names a clip with no state, once, and never again until the table is reloaded.
        ///
        /// Once because this is called per figure per frame: a missing state would otherwise print
        /// eighty thousand identical lines a second and take the editor with it. The set is
        /// cleared by <see cref="Reload"/>, so fixing the table and rebuilding says it again if it
        /// is still wrong.
        /// </summary>
        private static void Missing(string clip)
        {
            if (_missing.Contains(clip)) return;
            _missing.Add(clip);
            Debug.LogWarning($"[anim] no state named '{clip}' in the controller, so everybody who "
                           + "wants it is frozen. Rebuild with Noir/Build The Townsfolk Animator, "
                           + "and check the name has no period in it.");
        }

        private static readonly HashSet<string> _missing =
            new HashSet<string>(System.StringComparer.Ordinal);
    }
}
