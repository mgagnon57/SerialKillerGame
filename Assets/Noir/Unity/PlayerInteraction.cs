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
    ///
    /// ⚠ MEASURED 2026-08-15, AND STILL AN OPEN PROBLEM THIS FILE CANNOT SOLVE ALONE: in a
    /// STANDALONE BUILD, once <see cref="Cursor.lockState"/> has been set to Locked even ONCE in
    /// a session, GUI.Button never registers a real click again for the rest of that session -
    /// not just here, but for every OnGUI button in the project, including VillageUI's own top
    /// bar, which was independently confirmed working before a lock/unlock cycle and confirmed
    /// broken by the identical click at the identical screen position immediately after one.
    /// Proven with a real built player (Noir.Editor.BuildPlayer.Windows64) and genuine OS-level
    /// SendInput mouse events - not reasoned about, not an editor artifact. The two fixes below
    /// (fixed screen position, and unlocking the cursor while the menu shows) are both correct
    /// and both necessary, and they DO restore Event.current.mousePosition to a real, live,
    /// correctly-tracked value - but they cannot by themselves restore MouseDown/MouseUp EVENT
    /// TYPES, which simply never arrive at any OnGUI again post-lock in this configuration. The
    /// likely cause is Windows' RIDEV_NOLEGACY-style raw-input capture that Unity's cursor lock
    /// engages and does not appear to release on unlock, which would explain why POSITION
    /// tracking (sourced differently) keeps working while button messages stop - that is a
    /// hypothesis, not confirmed by reading Unity's own source. See the fix-wave report at
    /// .superpowers/sdd/2026-08-15-player-interaction/fix-wave-report.md for the full measurement
    /// trail. Until this is solved - most likely by moving this menu's click handling onto the
    /// New Input System's own UI event path rather than legacy OnGUI - a player who has walked
    /// even once cannot click ANY on-screen button for the rest of that session.
    /// </summary>
    public sealed class PlayerInteraction : MonoBehaviour
    {
        /// <summary>How close the player must stand for a door to offer its menu - the exact
        /// distance CityDoors itself swings a door open at, so the menu is never offered at a
        /// range where the door is not already reacting to the player being there.</summary>
        private const float Range = CityDoors.Reach;

        private VillageHost _host;
        private GUIStyle _button;

        /// <summary>The hinge index the menu is currently built for, or -1 - so a new
        /// DoorInteractable is only allocated when the nearest door actually changes, not once a
        /// frame for whichever door happens to still be nearest.</summary>
        private int _currentIndex = -1;

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
            var player = _host.Player;
            if (player == null || !player.Walking) { Current = null; _currentIndex = -1; return; }
            var where = player.Where;
            if (!where.HasValue) { Current = null; _currentIndex = -1; SyncCursor(); return; }

            var doors = _host.Doors;
            if (doors == null) { Current = null; _currentIndex = -1; SyncCursor(); return; }

            int nearest = doors.NearestDoor(where.Value, Range);
            if (nearest < 0) { Current = null; _currentIndex = -1; SyncCursor(); return; }

            if (nearest != _currentIndex || Current == null)
            {
                Current = new DoorInteractable(doors, nearest);
                _currentIndex = nearest;
            }

            SyncCursor();
        }

        /// <summary>Whether the last call actually applied Locked/hidden (false) or None/shown
        /// (true) - so a steady state does not re-issue the same Cursor call every frame. That
        /// matters, and was measured to matter: setting Cursor.lockState/visible even to their
        /// CURRENT values, every frame, was observed to keep the OS cursor pinned at a single
        /// position - real SendInput movement of up to 200px produced no change at all until this
        /// was made edge-triggered.</summary>
        private bool? _cursorShowing;

        /// <summary>
        /// MEASURED, NOT ASSUMED: Player.Enter() locks and hides the cursor, and under that lock
        /// Event.current.mousePosition never carries a real value at all - not "imprecise", not
        /// "stale", literally the constant (-10000,-10000) on every single frame, proven by
        /// driving a real standalone build with actual SendInput mouse input and grepping tens of
        /// thousands of consecutive log lines without one exception. A fixed-screen-position
        /// button cannot fix that on its own: no Rect on screen ever contains (-10000,-10000), so
        /// GUI.Button could never register a hit no matter where it was drawn. The cause is the
        /// OS-level cursor confinement a locked cursor uses - it re-clips the pointer to
        /// (functionally) the same point every frame, so the legacy, message-based tracking
        /// IMGUI's Event system relies on never sees a net move. The New Input System's raw
        /// deltas (what drives camera look) are unaffected, which is why walking and looking
        /// around work fine regardless.
        ///
        /// So the menu unlocks and shows the cursor for as long as it is offering something, and
        /// hands lock back the instant it is not. The window to click through is small - the
        /// button sits at screen centre, right where the crosshair already is - so the cursor
        /// barely has to move, and a small mouse motion while the menu is up nudging the camera a
        /// touch is a fair trade against a menu that cannot be used at all.
        ///
        /// This restores real, live position TRACKING - measured and confirmed. It does not, by
        /// itself, restore the ability to click - see the class-level warning above.
        /// </summary>
        private void SyncCursor()
        {
            bool showing = Current != null;
            if (_cursorShowing == showing) return;
            Cursor.lockState = showing ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = showing;
            _cursorShowing = showing;
        }

        /// <summary>
        /// Drawn at a FIXED SCREEN POSITION, not projected from the door's world position.
        ///
        /// Player.Enter() locks and hides the cursor while walking, and under a locked cursor
        /// Event.current.mousePosition - what GUI.Button hit-tests against - does not track real
        /// mouse movement at all (see <see cref="SyncCursor"/>). A button positioned by
        /// projecting the door's world position to screen space could therefore sit somewhere
        /// the click point never actually reaches even once tracking is restored. Anchoring the
        /// menu to the centre of the screen instead - a crosshair-prompt, the usual shape for a
        /// locked-cursor first/third-person game - guarantees the button always covers wherever
        /// the click point is, regardless of where the door stands.
        ///
        /// This makes IInteractable.Position unused here; it stays on the interface for a future,
        /// differently-shaped menu that might still want it.
        /// </summary>
        private void OnGUI()
        {
            if (Current == null) return;

            if (_button == null) _button = new GUIStyle(GUI.skin.button) { fontSize = VillageUI.F(16) };

            var verbs = Current.Verbs;
            float w = VillageUI.S(140f), h = VillageUI.S(40f), gap = VillageUI.S(6f);
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f + VillageUI.S(40f);

            for (int i = 0; i < verbs.Count; i++)
            {
                var rect = new Rect(cx - w * 0.5f, cy + i * (h + gap), w, h);
                if (GUI.Button(rect, verbs[i], _button))
                    Current.Perform(verbs[i]);
            }
        }
    }
}
