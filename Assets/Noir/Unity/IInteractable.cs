using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// The seam a player action plugs into: where to draw its menu, what it can be asked to do,
    /// and how to do it. Doors are the first thing behind this interface - see DoorInteractable
    /// for the adapter. Nothing here knows about hinges, angles, or CityDoors.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Where to draw this interactable's menu, in world space.</summary>
        Vector3 Position { get; }

        /// <summary>The verbs available right now - may change between calls (a door offers
        /// "Open" when shut, "Close" when open).</summary>
        IReadOnlyList<string> Verbs { get; }

        /// <summary>Carry out one of the verbs from <see cref="Verbs"/>.</summary>
        void Perform(string verb);
    }
}
