using System;

// ---------------------------------------------------------------------------------------------
//  A FORTNIGHT ON THE PAVEMENT.
//
//  Sighting.cs is what one person says about another afterwards. This is the other half: what a
//  watcher stood opposite one house for a fortnight could have written down at the time. Same
//  assembly, same firewall, same reason — see the header of Sighting.cs before adding anything.
//
//  Read the list of what is NOT here before adding a field. There is no Tile, no PlaceId, no
//  identity, and no position of any kind, and their absence is the design rather than an
//  omission: a log with no place in it cannot become a track, and a log that cannot become a
//  track cannot become a shopping list. The only spatial content that survives the eye is one
//  bit, SomewhereNew, computed outside this assembly and thrown away with the watcher.
// ---------------------------------------------------------------------------------------------

namespace Noir.Core.Observation
{
    /// <summary>
    /// The thing a watcher across the road saw happen. Nine verbs and a silence.
    ///
    /// Deliberately NOT Activity. Activity is the simulation's word for what a man is really
    /// doing — GoingToWork, OnTheAllotment — and a watcher sees a figure come out of a door with
    /// a bag, which is a different and much poorer claim. Every value below is something a
    /// stranger could name without knowing whose house it is or where the figure is going.
    ///
    /// NothingInParticular is zero so that a default Observed is inert: an entry that asserts
    /// nothing is the safest default for a type whose whole job is to under-claim.
    /// </summary>
    public enum ObservedAct : byte
    {
        /// <summary>
        /// The zero value, so a default-constructed Observed is inert rather than a lie.
        ///
        /// NOTE, because an audit had to establish it: nothing ever produces this. Every call
        /// site passes an explicit act. It is here to make `default(Observed)` harmless, not
        /// because any watcher writes it down — so a reader of the log will never encounter it,
        /// and code branching on it is unreachable rather than merely rare.
        /// </summary>
        NothingInParticular = 0,

        CameOut,            // a door, from outside. Which door is not recorded, and cannot be.
        WentIn,

        // Up a path that is not her own. WHOSE is not recorded and must not be - that would be
        // an identity, and this assembly cannot hold one. What a watcher opposite can see is
        // only that she did not let herself in at home, which is a different kind of moment
        // from going indoors and is why it gets its own verb rather than folding into WentIn.
        CalledOnSomebody,

        WalkedPast,         // somewhere they had not been in this stretch of road, in sight and moving
        StoodAbout,         // in sight, not moving, two minutes or more

        StoppedToSpeak,
        FellInWithSomebody,
        StoodInALine,       // a queue, seen through glass

        ALightCameOn,
        ALightWentOut,

        /// <summary>
        /// A whole day, and nobody came to the door.
        ///
        /// The one entry that is about an absence rather than an event, and the only one a
        /// watcher may write without seeing anybody, because it is a fact about the doorstep
        /// they watched all day rather than an inference about the person inside. "She was
        /// home alone" is the inference, it is not in this enum, and it never will be.
        /// </summary>
        NobodyCameOrWent,
    }

    /// <summary>
    /// The circumstances of a moment, as they read from across the road.
    ///
    /// Flags rather than a value, because a woman can come out carrying something, quickly, and
    /// alone, and a watcher would write all three down. Every bit here must be perceptible from
    /// outside a building at a hundred feet — if a bit could only be known by reading the
    /// simulation, it does not belong, however convenient it would be for the classifier.
    /// </summary>
    [Flags]
    public enum ObservedManner : byte
    {
        None = 0,
        Carrying = 1,
        Alone = 2,
        InCompany = 4,
        Quickly = 8,
        Slowly = 16,
        Lingering = 32,

        /// <summary>A stretch of the village this watch has not seen this figure in before.</summary>
        SomewhereNew = 64,

        /// <summary>
        /// Nobody at home, and the figure is in sight somewhere else.
        ///
        /// Both halves are required, and the second one is the whole discipline of this file.
        /// A watcher who cannot see the subject does not get to write down "and so the house
        /// stood empty"; that is an inference dressed as an observation, and recording it is
        /// exactly the cheat the assembly exists to prevent.
        /// </summary>
        TheHouseIsDark = 128,
    }

    /// <summary>
    /// One line of a watcher's notebook.
    ///
    /// Sixteen bytes and no way to say who, where or what for. The classifier that reads these
    /// (see Salience) cannot join one to a person because there is nothing in here to join with,
    /// and the assembly it lives in contains no identity type at all.
    /// </summary>
    public readonly struct Observed
    {
        /// <summary>
        /// Minutes since the watch began, not since the simulation did.
        ///
        /// Minutes for the same reason Sighting uses them: a tick offers a fiftieth of a second
        /// of precision no watcher has, and a consumer would start trusting it.
        /// </summary>
        public readonly int Minute;

        public readonly ObservedAct Act;
        public readonly ObservedManner Manner;

        /// <summary>
        /// Figures in sight of the subject, saturating at 255.
        ///
        /// A count, never a list. Who else was there is another person's business and would be
        /// an identity by the back door — twelve of them, at that.
        /// </summary>
        public readonly byte Others;

        /// <summary>
        /// How long it had been going on when the watcher wrote it down.
        ///
        /// The run SO FAR, never the run entire. A man standing at a gate has not finished
        /// standing there, and an entry that knew how long he was going to go on standing would
        /// be a note written by somebody who had read the day plan.
        /// </summary>
        public readonly ushort ForMinutes;

        public readonly SightingClarity Clarity;

        public Observed(int minute, ObservedAct act, ObservedManner manner,
                        int others, int forMinutes, SightingClarity clarity)
        {
            Minute = minute;
            Act = act;
            Manner = manner;
            Others = others < 0 ? (byte)0 : others > 255 ? (byte)255 : (byte)others;
            ForMinutes = forMinutes < 0 ? (ushort)0
                       : forMinutes > ushort.MaxValue ? ushort.MaxValue : (ushort)forMinutes;
            Clarity = clarity;
        }

        public override string ToString() =>
            Manner == ObservedManner.None ? Act.ToString() : Act + " (" + Manner + ")";
    }
}
