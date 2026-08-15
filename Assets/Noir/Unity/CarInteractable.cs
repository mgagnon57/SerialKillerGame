using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>A parked car, offered through the IInteractable seam - the second provider,
    /// the one the registry was built for. Stateless wrapper over (CityDriveways, index),
    /// DoorInteractable's exact shape.</summary>
    public sealed class CarInteractable : IInteractable
    {
        private static readonly string[] DriveVerb = { "Drive" };

        private readonly VillageHost _host;
        private readonly int _index;

        public CarInteractable(VillageHost host, int index) { _host = host; _index = index; }

        public Vector3 Position => _host.Driveways.PositionOf(_index);
        public IReadOnlyList<string> Verbs => DriveVerb;
        public void Perform(string verb) => _host.Player.EnterCar(_index);
    }

    /// <summary>The one verb the driver's seat offers. Owned by the mode, not by proximity.</summary>
    public sealed class GetOutInteractable : IInteractable
    {
        private static readonly string[] OutVerb = { "Get out" };
        private readonly Player _player;

        public GetOutInteractable(Player player) { _player = player; }

        public Vector3 Position => _player.Where ?? Vector3.zero;
        public IReadOnlyList<string> Verbs => OutVerb;
        public void Perform(string verb) => _player.LeaveCar();
    }
}
