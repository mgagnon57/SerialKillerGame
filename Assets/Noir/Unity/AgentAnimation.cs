using UnityEngine;
using Noir.Core.People;

namespace Noir.Unity
{
    /// <summary>
    /// Which clip a person should be playing, given what the simulation says they are doing.
    ///
    /// INERT UNTIL THE CLIPS EXIST. Nothing here loads, requires or asserts an animation: ask it
    /// for a clip name and it answers, and <see cref="Drive"/> does nothing at all when handed a
    /// null Animator or an Animator whose controller has no such state. That is deliberate - the
    /// mapping is the part worth writing down NOW, while the reasoning is fresh, and downloading
    /// thirteen files from Mixamo is the part that cannot be done from here. See docs/ASSETS.md.
    ///
    /// THE NAMES ARE MIXAMO'S OWN, exactly as they appear on the site, so there is no translation
    /// step between what you downloaded and what this asks for. An animation called "Standing
    /// Idle" on mixamo.com is `Standing Idle` here and should be the state name in the controller.
    ///
    /// IN PLACE, ALWAYS. Every locomotive clip must be downloaded with Mixamo's "In Place" box
    /// ticked, and this is not a preference. `Simulation` decides where everybody is by
    /// pathfinding and `AgentMeshView` draws them at the position it computed; a clip carrying
    /// root motion drives the transform ITSELF, so the animation and the simulation would fight
    /// over the same number and people would walk off their own paths. Same reasoning as
    /// CityTraffic moving a car along its lane coordinate rather than letting anything else push
    /// it about.
    /// </summary>
    public static class AgentAnimation
    {
        /// <summary>
        /// The clip for a person who is on their feet and moving.
        ///
        /// Movement beats activity: somebody on the way to the pub is walking, not drinking, and
        /// what the eye reads at fifty metres is the gait rather than the errand. Only when they
        /// have arrived and stopped does <see cref="Resting"/> get a say.
        /// </summary>
        public const string Walking = "Walking";

        /// <summary>Children at the playground, and nobody else. See <see cref="Moving"/>.</summary>
        public const string Running = "Running";

        /// <summary>What to play when there is no clip for the activity. Always safe.</summary>
        public const string Fallback = "Standing Idle";

        /// <summary>
        /// The clip for somebody who is moving, which is almost always the walk.
        /// </summary>
        /// <param name="hurrying">
        /// A child at play. The only case in the whole day plan where somebody runs - an adult
        /// crossing Northgate at a jog would read as fleeing, which is a story event rather than
        /// a commute, and this game should not say that by accident.
        /// </param>
        public static string Moving(bool hurrying) => hurrying ? Running : Walking;

        /// <summary>
        /// The clip for somebody standing still, by what they are there to do.
        ///
        /// SITTING IS A LIE THIS TELLS ON PURPOSE, for now. Church, school and a front room all
        /// map to `Sitting Idle`, and nothing here knows whether there is actually a chair under
        /// the person - the interior generator places furniture but the day plan does not reserve
        /// it. A congregation standing to attention for an hour reads worse than a congregation
        /// sitting on nothing, at the distance this is seen from. Revisit when somebody is
        /// assigned a seat rather than a room.
        /// </summary>
        public static string Resting(Activity doing) => doing switch
        {
            Activity.Talking          => "Talking",
            Activity.AtThePub         => "Drinking",
            Activity.AtWork           => "Standing Idle",
            Activity.Shopping         => "Looking Around",
            Activity.OnTheAllotment   => "Digging",

            Activity.AtChurch         => "Sitting Idle",
            Activity.AtSchool         => "Sitting Idle",
            Activity.AtHome           => "Sitting Idle",
            Activity.Visiting         => "Sitting Idle",

            // Asleep has no clip and wants none: they are indoors, behind a wall, in the dark,
            // and the only thing that ever shows it is the window not being lit.
            Activity.Asleep           => null,

            _                         => Fallback,
        };

        /// <summary>
        /// Everything above in one call: what should this person be playing?
        ///
        /// Null means "draw them, animate nothing" - which is right for somebody asleep and is
        /// also what a caller gets before any clips have been imported.
        /// </summary>
        public static string ClipFor(Activity doing, bool moving, bool hurrying = false) =>
            moving ? Moving(hurrying) : Resting(doing);

        /// <summary>
        /// Put an Animator into the right state, and do nothing if it cannot be.
        ///
        /// Crossfaded rather than snapped, because a figure changing gait between two frames is
        /// the thing that makes a crowd read as puppets. A quarter second is under the threshold
        /// at which anybody notices a transition and over the one at which they notice a pop.
        ///
        /// Every reason this can fail is silent by design: no Animator, no controller, a
        /// controller with no such state. The city has to keep running while the animation set is
        /// half imported, or nobody will ever import it one clip at a time.
        /// </summary>
        public static void Drive(Animator animator, Activity doing, bool moving, bool hurrying = false)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;

            string clip = ClipFor(doing, moving, hurrying);
            if (string.IsNullOrEmpty(clip)) return;

            int state = Animator.StringToHash(clip);
            if (!animator.HasState(0, state)) return;

            var now = animator.GetCurrentAnimatorStateInfo(0);
            if (now.shortNameHash == state) return;          // already there; do not restart it

            animator.CrossFadeInFixedTime(state, 0.25f);
        }
    }
}
