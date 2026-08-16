using Noir.Core.Contracts;

namespace Noir.Core.Witness
{
    /// <summary>
    /// The Sim-side fact the replay cannot know: from which minute a citizen stopped living
    /// their plan. INightWitnesses' pattern exactly — Core states the question, the one
    /// caller answers it from live sim state, and null means nobody ever was (the default,
    /// and the honest one). A downed citizen neither witnesses nor is placed by any replay
    /// consumer from that minute on — <see cref="Recollection.WhatTheySaw"/> enforces the
    /// witness half (a downed citizen stops describing the player's own afternoon) and
    /// <see cref="Recollection.WhatTheySawOfEvents"/> enforces the event half (a downed
    /// citizen stops being placed at, or testifying about, a recorded event), both against
    /// the same DownedFromMinute.
    /// </summary>
    public interface IInterruptions
    {
        /// <summary>Minutes since the simulation began, or int.MaxValue if never.</summary>
        int DownedFromMinute(CitizenId who);

        /// <summary>Minutes since the simulation began when they came back — the ambulance's
        /// survivor walking home — or int.MaxValue if they never did (the dead, and everyone
        /// never taken). The silenced window is [DownedFromMinute, BackFromMinute): one window
        /// per citizen; a re-hit widens it (earliest down, latest back), which silences the
        /// between-hits stretch too — a twice-struck witness says less, never more.</summary>
        int BackFromMinute(CitizenId who);
    }
}
