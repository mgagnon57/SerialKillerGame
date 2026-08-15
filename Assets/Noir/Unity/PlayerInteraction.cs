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
    /// ONE PROVIDER FOR NOW, ASKED DIRECTLY. CityDoors is the only source of interactables this
    /// pass builds, so this asks it directly rather than through a registry - a registry earns
    /// its keep the day a second provider exists, and guessing its shape before that day arrives
    /// is more likely to be wrong than useful.
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

        private VillageHost _host;
        private GUIStyle _prompt;

        /// <summary>The hinge index the prompt is currently built for, or -1 - so a new
        /// DoorInteractable is only allocated when the nearest door actually changes, not once a
        /// frame for whichever door happens to still be nearest.</summary>
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
            if (player == null || !player.Walking) { Current = null; _currentIndex = -1; return; }
            var where = player.Where;
            if (!where.HasValue) { Current = null; _currentIndex = -1; return; }

            var doors = _host.Doors;
            if (doors == null) { Current = null; _currentIndex = -1; return; }

            int nearest = doors.NearestDoor(where.Value, Range);
            if (nearest < 0) { Current = null; _currentIndex = -1; return; }

            if (nearest != _currentIndex || Current == null)
            {
                Current = new DoorInteractable(doors, nearest);
                _currentIndex = nearest;
            }

            // Guarded like every other gameplay key reader (OrbitCamera, VillageHost's travel
            // hotkeys): an 'e' typed into a focused panel text field is a letter, not a verb.
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
