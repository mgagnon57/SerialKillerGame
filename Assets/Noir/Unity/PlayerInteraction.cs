using UnityEngine;
using UnityEngine.InputSystem;

namespace Noir.Unity
{
    /// <summary>
    /// Finds the nearest thing the player can act on, offers its verb beside the crosshair -
    /// "E — Open" - and performs it when E is pressed.
    ///
    /// A KEY, NOT A CLICKABLE BUTTON, AND THAT IS A MEASURED DECISION. The first version drew a
    /// GUI.Button and it could not be clicked, for three independent reasons, each found the hard
    /// way on 2026-08-15:
    ///
    ///  - Under Active Input Handling = Input System Package only (activeInputHandler: 1, which
    ///    this project uses), runtime IMGUI receives NO input events in a built player at all -
    ///    Unity's own UISupport manual says so outright; the InputForUI bridge feeds UI Toolkit
    ///    and uGUI, never OnGUI. The editor's Game view forwards the EDITOR's IMGUI events, which
    ///    is why a button half-works in Play and silently dies in a build. This covers every
    ///    OnGUI button in the project, VillageUI's top bar included.
    ///  - While Cursor.lockState is Locked - and Player.Enter() locks it for the whole walk - the
    ///    IMGUI mouse position is pinned (measured as a hard (-10000,-10000) in a build, driven
    ///    with genuine SendInput), so no Rect on screen can ever be hit.
    ///  - Unlocking the cursor while the menu showed lost a fight it could not see:
    ///    StarterAssetsInputs.OnApplicationFocus re-locks the cursor on EVERY focus change in
    ///    either direction (it ignores its own hasFocus argument), and an edge-triggered unlock
    ///    cannot notice. Caught live doing exactly that under an open menu.
    ///
    /// Keyboard.current is immune to all three: it reads the device, not an event queue, and it
    /// works identically under cursor lock in the editor and in a shipped build - P itself is the
    /// standing proof (Player.Update). So the cursor now simply STAYS LOCKED for the whole walk,
    /// and this file owns no cursor state at all - the old SyncCursor and its two desync bugs are
    /// deleted rather than repaired. If a future interactable ever offers more than one verb at
    /// once, that is the day this needs more keys (or a Mouse.current hit-test over the drawn
    /// rects, the OrbitCamera.HandleSelection pattern - never a GUI.Button).
    ///
    /// TWO PROVIDERS NOW, A PAIR OF IFS RATHER THAN A LIST. CityDoors and CityDriveways each
    /// answer "nearest candidate, squared distance" and the closer one wins; see the registry
    /// comment inside Update. That was written the day a second provider actually existed rather
    /// than guessed at in advance - grow it into a real list the day there are four.
    ///
    /// ONLY LIVE WHILE THE PLAYER IS IN THE STREET. Interaction is a first-person mechanic; there
    /// is nothing to act on from the overview camera, and Player.Where is null there anyway. E is
    /// free while walking: OrbitCamera (whose E rotates the overview) is disabled for the whole
    /// walk, and nothing else reads it.
    /// </summary>
    public sealed class PlayerInteraction : MonoBehaviour
    {
        /// <summary>How close the player must stand for a door to offer its verb. The value and
        /// its reasoning live in <see cref="CityDoors.Offer"/>, beside Reach and Hold, so the
        /// three door distances stay one file's to keep consistent.</summary>
        private const float Range = CityDoors.Offer;

        /// <summary>How close the player must stand for a car to offer Drive. Cars offer a
        /// stride further than a door does - you approach a car from any side, and its measured
        /// body is 5.5 m long.</summary>
        private const float CarOffer = 3.0f;

        private VillageHost _host;
        private GUIStyle _prompt;

        /// <summary>The one GetOutInteractable this component ever needs, built once and reused
        /// for as long as the player stays behind the wheel.</summary>
        private GetOutInteractable _getOut;

        /// <summary>The provider-tagged index the prompt is currently built for, or -1 - so a
        /// new interactable is only allocated when the nearest one actually changes, not once a
        /// frame for whichever candidate happens to still be nearest. See the cache-key comment
        /// in Update for how a door index and a car index share this one int.</summary>
        private int _currentIndex = -1;

        /// <summary>The interactable currently offering its verb, or null.</summary>
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
            var player = _host.Player;
            if (player == null) { Current = null; _currentIndex = -1; return; }

            // Behind the wheel there is exactly one verb and proximity has nothing to say.
            if (player.Driving)
            {
                if (!(Current is GetOutInteractable))
                    Current = _getOut ??= new GetOutInteractable(player);
                _currentIndex = -1;
                var driveKeys = Keyboard.current;
                if (driveKeys != null && driveKeys.eKey.wasPressedThisFrame
                    && !VillageUI.KeyboardCaptured) PerformOffered();
                return;
            }

            if (!player.Walking) { Current = null; _currentIndex = -1; return; }
            var where = player.Where;
            if (!where.HasValue) { Current = null; _currentIndex = -1; return; }

            // THE PROVIDER REGISTRY, the day the header scheduled. Each provider answers
            // "nearest candidate, squared distance"; the closest wins. Two providers is a
            // pair of ifs rather than a list - grow it into one the day there are four.
            int doorIx = _host.Doors != null
                ? _host.Doors.NearestDoor(where.Value, Range) : -1;
            int carIx = _host.Driveways != null
                ? _host.Driveways.NearestCar(where.Value, CarOffer) : -1;

            float doorD2 = doorIx >= 0
                ? (_host.Doors.PositionOf(doorIx) - where.Value).sqrMagnitude : float.MaxValue;
            float carD2 = carIx >= 0
                ? (_host.Driveways.PositionOf(carIx) - where.Value).sqrMagnitude : float.MaxValue;

            if (doorIx < 0 && carIx < 0) { Current = null; _currentIndex = -1; return; }

            // Cache key: provider in the sign, index in the magnitude - doors positive,
            // cars bitwise-complemented, so switching provider always rebuilds Current.
            int key = carD2 < doorD2 ? ~carIx : doorIx;
            if (key != _currentIndex || Current == null)
            {
                Current = carD2 < doorD2
                    ? (IInteractable)new CarInteractable(_host, carIx)
                    : new DoorInteractable(_host.Doors, doorIx);
                _currentIndex = key;
            }

            var keys = Keyboard.current;
            if (keys != null && keys.eKey.wasPressedThisFrame && !VillageUI.KeyboardCaptured)
                PerformOffered();
        }

        /// <summary>Carry out the offered verb - exactly what pressing E does, public so the
        /// PlayMode gate can prove the action without forging a keyboard. Today's interactables
        /// offer one verb at a time, so "the offered verb" is Verbs[0]; see the class header for
        /// what changes the day that stops being true.</summary>
        public void PerformOffered()
        {
            if (Current == null) return;
            Current.Perform(Current.Verbs[0]);
        }

        /// <summary>
        /// Drawn at a FIXED SCREEN POSITION, just under the crosshair, because that is where the
        /// player is already looking - a prompt, not a target. Nothing here is hit-tested, so the
        /// locked cursor's pinned IMGUI position (see the class header) costs nothing.
        ///
        /// This leaves IInteractable.Position unused here; it stays on the interface for a
        /// future, differently-shaped prompt that might still want it.
        /// </summary>
        private void OnGUI()
        {
            if (Current == null) return;

            // Rebuilt whenever the UI scale moves (Ctrl+= / Ctrl+-), not cached once - the rects
            // below read S() live every frame, and a style caught at the old scale leaves wrong-
            // sized text in a right-sized box for the rest of the session.
            int wantFont = VillageUI.F(16);
            if (_prompt == null || _prompt.fontSize != wantFont)
                _prompt = new GUIStyle(GUI.skin.box)
                {
                    fontSize = wantFont,
                    alignment = TextAnchor.MiddleCenter,
                };

            var verbs = Current.Verbs;
            float w = VillageUI.S(140f), h = VillageUI.S(40f), gap = VillageUI.S(6f);
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f + VillageUI.S(40f);

            for (int i = 0; i < verbs.Count; i++)
                GUI.Box(new Rect(cx - w * 0.5f, cy + i * (h + gap), w, h),
                        $"E — {verbs[i]}", _prompt);
        }
    }
}
