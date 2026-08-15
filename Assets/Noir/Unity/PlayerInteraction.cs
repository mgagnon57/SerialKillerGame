using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// Finds the nearest thing the player can act on and offers its menu.
    ///
    /// ONE PROVIDER FOR NOW, ASKED DIRECTLY. CityDoors is the only source of interactables this
    /// pass builds, so this asks it directly rather than through a registry - a registry earns
    /// its keep the day a second provider exists, and guessing its shape before that day arrives
    /// is more likely to be wrong than useful.
    ///
    /// ONLY LIVE WHILE THE PLAYER IS IN THE STREET. Interaction is a first-person mechanic; there
    /// is nothing to act on from the overview camera, and Player.Where is null there anyway.
    /// </summary>
    public sealed class PlayerInteraction : MonoBehaviour
    {
        /// <summary>How close the player must stand for a door to offer its menu - the exact
        /// distance CityDoors itself swings a door open at, so the menu is never offered at a
        /// range where the door is not already reacting to the player being there.</summary>
        private const float Range = CityDoors.Reach;

        private VillageHost _host;
        private CityDoors _doors;
        private GUIStyle _button;

        /// <summary>The interactable currently offering its menu, or null.</summary>
        public IInteractable Current { get; private set; }

        public static PlayerInteraction Create(VillageHost host, Transform parent)
        {
            var go = new GameObject("PlayerInteraction");
            go.transform.SetParent(parent, false);
            var it = go.AddComponent<PlayerInteraction>();
            it._host = host;
            return it;
        }

        private void Update()
        {
            Current = null;

            var player = _host.Player;
            if (player == null || !player.Walking) return;
            var where = player.Where;
            if (!where.HasValue) return;

            if (_doors == null) _doors = Object.FindFirstObjectByType<CityDoors>();
            if (_doors == null) return;

            int nearest = _doors.NearestDoor(where.Value, Range);
            if (nearest < 0) return;

            Current = new DoorInteractable(_doors, nearest);
        }

        private void OnGUI()
        {
            if (Current == null) return;
            var cam = Camera.main;
            if (cam == null) return;

            var screen = cam.WorldToScreenPoint(Current.Position);
            if (screen.z <= 0f) return;                    // behind the camera
            float x = screen.x, y = Screen.height - screen.y;
            if (x < 0f || x > Screen.width || y < 0f || y > Screen.height) return;

            if (_button == null) _button = new GUIStyle(GUI.skin.button) { fontSize = 16 };

            var verbs = Current.Verbs;
            const float w = 90f, h = 32f, gap = 4f;
            for (int i = 0; i < verbs.Count; i++)
            {
                var rect = new Rect(x - w * 0.5f, y - (verbs.Count - i) * (h + gap), w, h);
                if (GUI.Button(rect, verbs[i], _button))
                    Current.Perform(verbs[i]);
            }
        }
    }
}
