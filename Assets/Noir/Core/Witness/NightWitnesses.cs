using Noir.Core.Contracts;

namespace Noir.Core.Witness
{
    /// <summary>
    /// Who can still see the street while their plan has them asleep.
    ///
    /// THIS IS AN INTERFACE BECAUSE THE ANSWER LIVES SOMEWHERE CORE CANNOT LOOK. Whether a
    /// particular villager is a light sleeper, keeps a scanner on, or walks the dog at midnight
    /// is AUTHORED - it is typed into the parcel editor and stored in Content/parcel-notes.txt,
    /// both of which are the Unity layer. Core has no business referencing that and the firewall
    /// would not allow it (see ObservationFirewallTests).
    ///
    /// So Core states the question and the Unity layer answers it. Pass null and nobody sees
    /// anything while asleep, which is the honest default: a village asleep is a village that
    /// witnesses nothing, and a night witness should be somebody who was CHOSEN to be one.
    ///
    /// The alternative considered and rejected was gating on the trait strings directly, which
    /// would have put a list of English phrases in the middle of the observation model and made
    /// Core depend on the spelling of an authoring convenience.
    /// </summary>
    public interface INightWitnesses
    {
        /// <summary>
        /// True if this citizen is awake enough to see something while their plan says asleep.
        ///
        /// Asked once per citizen per query, not per minute - the answer is a fact about the
        /// person, not about the moment.
        /// </summary>
        bool AwakeEnough(CitizenId who);
    }
}
