using Noir.Core.Contracts;

namespace Noir.Core.Witness
{
    /// <summary>
    /// The Sim-side fact the replay cannot know: from which minute a citizen stopped living
    /// their plan. INightWitnesses' pattern exactly — Core states the question, the one
    /// caller answers it from live sim state, and null means nobody ever was (the default,
    /// and the honest one). A downed citizen neither witnesses nor is placed by any replay
    /// consumer from that minute on.
    /// </summary>
    public interface IInterruptions
    {
        /// <summary>Minutes since the simulation began, or int.MaxValue if never.</summary>
        int DownedFromMinute(CitizenId who);
    }
}
