using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// One door, offered through the general IInteractable seam. Wraps a (CityDoors, hinge index)
    /// pair rather than owning any state of its own - CityDoors already knows everything about
    /// this door; this only translates its questions (position, verbs) and its one command
    /// (perform) into the generic shape PlayerInteraction expects.
    /// </summary>
    public sealed class DoorInteractable : IInteractable
    {
        private static readonly string[] OpenVerb = { "Open" };
        private static readonly string[] CloseVerb = { "Close" };

        private readonly CityDoors _doors;
        private readonly int _index;

        public DoorInteractable(CityDoors doors, int index)
        {
            _doors = doors;
            _index = index;
        }

        public Vector3 Position => _doors.PositionOf(_index);

        public IReadOnlyList<string> Verbs => _doors.IsOpen(_index) ? CloseVerb : OpenVerb;

        public void Perform(string verb) => _doors.Force(_index, verb == "Open");
    }
}
