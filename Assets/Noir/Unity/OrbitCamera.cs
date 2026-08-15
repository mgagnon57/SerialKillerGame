using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Noir.Core.Contracts;

namespace Noir.Unity
{
    /// <summary>
    /// Orbit, tilt, zoom, pan and follow.
    ///
    /// This is the thing 3D was chosen for. Rotating to any angle, looking down a street, and
    /// getting real occlusion are all just the camera existing in space - none of them had to
    /// be engineered, which is exactly what they would have been in an isometric renderer.
    ///
    /// Uses the new Input System because the project is configured for it exclusively; legacy
    /// UnityEngine.Input throws at runtime here.
    /// </summary>
    public sealed class OrbitCamera : MonoBehaviour
    {
        private VillageHost _host;
        private Camera _camera;

        private Vector3 _target;

        /// <summary>
        /// THE OPENING SHOT IS SOUTH DOWN CHICAGO STREET, which is the view the town is about.
        ///
        /// 162 degrees, not 180. Space3D maps village (x,y) to world (x, h, -y), so due south is
        /// -Z and yaw 180 - but Chicago Street is not due south. It is the 1829 Hubbard Trail and
        /// it runs about 18 degrees east of south through the crossing and the downtown, which is
        /// the whole reason the grid and the highway disagree. Pointing the camera at 180 would
        /// put the one road the town was built along at a slant across the frame.
        ///
        /// A low pitch on purpose: 50 degrees looks DOWN ON a street, and the ask was to look
        /// ALONG one.
        /// </summary>
        private float _yaw = 162f;
        private float _pitch = 24f;
        private float _distance = 330f;

        private const float MinPitch = 12f;    // below this you are looking through the ground
        private const float MaxPitch = 89f;
        private const float MinDistance = 6f;

        /// <summary>
        /// 220 was sized for a built town seen from a hill nearby - close enough to still read
        /// as buildings. The plan is the point now (see VillageHost.ShowBuildings) and the town
        /// itself is over 2,000m across; 220 could never step back far enough to see more than a
        /// few blocks of it at once. 1,600 clears the whole map - see farClipPlane below - with
        /// room to spare at a steep pitch.
        /// </summary>
        private const float MaxDistance = 1600f;

        private bool _orbiting;
        private Vector2 _lastMouse;

        /// <summary>How far the camera sits from what it is looking at. Drives the roof cutaway.</summary>
        public float Distance => _distance;

        public enum ViewMode { Overview, Street }

        /// <summary>
        /// Overview looks down at the village; Street stands in it at eye height.
        ///
        /// Street mode is not a gimmick - a village you can only look down at is a diagram,
        /// and the whole question this project is trying to answer is whether the place feels
        /// alive. You find that out at eye level, walking past somebody's lit kitchen window.
        /// </summary>
        public ViewMode Mode { get; private set; } = ViewMode.Overview;

        private const float EyeHeight = 1.7f;
        private const float WalkSpeed = 4.2f;
        private const float JogSpeed = 9.0f;

        /// <summary>
        /// How far out the roofs go back on. It lives with the camera rather than with the roof
        /// builder because the camera owns the other half of the answer - street mode - and a
        /// threshold split across two objects is a threshold that will one day disagree with
        /// itself. RoofBuilder used to carry a public CutawayDistance beside this one that
        /// nothing ever read, which is worse than no knob at all - somebody changes it and
        /// reasonably expects something to happen. It has been deleted.
        /// </summary>
        private const float RoofCutaway = 46f;

        /// <summary>Roofs belong on when standing in the street, and off when peering in from above.</summary>
        public bool WantsRoofs => Mode == ViewMode.Street || _distance > RoofCutaway;

        public static OrbitCamera Create(VillageHost host)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }

            cam.orthographic = false;
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.3f;
            // Far enough to reach the end of the surrounding countryside. Clipping it short
            // puts a hard circular edge on the land where the frustum ends, which is exactly
            // the floating-slab look the countryside was added to get rid of. It costs
            // nothing: shadow range is set separately and stays at 320 m.
            cam.farClipPlane = 3000f;
            // Skybox rather than a flat colour: there is a real sky and horizon now.
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.backgroundColor = new Color32(0x11, 0x14, 0x1A, 0xFF);

            var rig = cam.gameObject.AddComponent<OrbitCamera>();
            rig._host = host;
            rig._camera = cam;
            // The Chicago x Attica crossing when the massing layer has worked it out from the
            // road network, and the middle of the map before that - which is what it always was.
            rig._target = HouseLayers.Installed
                ? Space3D.ToWorld(new Vec2(HouseLayers.Centre.x, HouseLayers.Centre.y), 0f)
                : Space3D.Centre(host.World.Width, host.World.Height);
            rig.Apply();
            return rig;
        }

        public void Tick()
        {
            if (_camera == null) return;

            // Player.Enter()/Leave() set `enabled` to hand the camera to the walking body and
            // take it back - but VillageHost calls Tick() directly rather than through Unity's
            // own Update() dispatch, so a plain MonoBehaviour `enabled = false` was silently not
            // stopping anything: this method ran every frame regardless and Apply() kept
            // overwriting whatever Player.Update() had just written, every frame, with this
            // rig's own unchanged orbit transform. Whether that made the walking camera visibly
            // "stuck" depended on the unspecified script-execution-order tie between VillageHost
            // and Player for that process - so it looked correct in some runs and frozen in
            // others with byte-identical code. Honouring the flag here, once, fixes it regardless
            // of ordering and finally makes `enabled` mean what the two call sites already assume
            // it means.
            //
            // SIDE EFFECT, AND ALMOST CERTAINLY THE RIGHT ONE: HandleModeSwitch() below is what
            // Tab drives, so returning here before it runs means Tab does nothing while the
            // player is walking - the rig has been handed off to the player for the duration, and
            // there is no orbit mode to switch between until Player.Leave() hands it back and
            // re-enables this component. Not a bug; stated here because it was not before.
            if (!enabled) return;

            HandleModeSwitch();

            if (Mode == ViewMode.Street)
            {
                HandleLook();
                HandleWalk();
                HandleSelection();
                ApplyStreet();
                return;
            }

            HandleZoom();
            HandleOrbit();
            HandlePan();
            HandleSelection();
            HandleHover();
            HandleFollow();
            Apply();
        }

        private void HandleModeSwitch()
        {
            // Keys belong to the text field while one has focus - see
            // VillageUI.KeyboardCaptured. WASD/QE/R/F/Tab are all bound here.
            if (VillageUI.KeyboardCaptured) return;
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.tabKey.wasPressedThisFrame) return;

            if (Mode == ViewMode.Overview)
            {
                // Drop in where you were looking, at eye height, facing the way you were.
                Mode = ViewMode.Street;
                _pitch = 8f;
                _host.Following = false;
            }
            else
            {
                Mode = ViewMode.Overview;
                _pitch = 50f;
                _distance = Mathf.Max(_distance, 40f);
            }
        }

        private void HandleLook()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            // Right-drag to look, rather than capturing the cursor - you still need the mouse
            // to click on people and to reach the panel.
            var pos = mouse.position.ReadValue();
            if (mouse.rightButton.isPressed)
            {
                if (!_orbiting) { _orbiting = true; _lastMouse = pos; }
                var delta = pos - _lastMouse;
                _lastMouse = pos;
                _yaw += delta.x * 0.16f;
                _pitch = Mathf.Clamp(_pitch - delta.y * 0.14f, -70f, 70f);
            }
            else _orbiting = false;
        }

        private void HandleWalk()
        {
            // Keys belong to the text field while one has focus - see
            // VillageUI.KeyboardCaptured. WASD/QE/R/F/Tab are all bound here.
            if (VillageUI.KeyboardCaptured) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            var move = Vector3.zero;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.z += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.z -= 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1f;
            if (move == Vector3.zero) return;

            var forward = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(_camera.transform.right, Vector3.up).normalized;

            float speed = keyboard.leftShiftKey.isPressed ? JogSpeed : WalkSpeed;
            _target += (forward * move.z + right * move.x).normalized * speed * Time.unscaledDeltaTime;

            // Stay on the map. Walking off the edge into fog is not atmosphere, it is a bug.
            _target.x = Mathf.Clamp(_target.x, 1f, _host.World.Width - 1f);
            _target.z = Mathf.Clamp(_target.z, -(_host.World.Height - 1f), -1f);
        }

        private void ApplyStreet()
        {
            _camera.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            _camera.transform.position = new Vector3(_target.x, EyeHeight, _target.z);
        }

        private void HandleZoom()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            // THE WHEEL BELONGS TO WHATEVER IS UNDER IT. Every other handler in this file asks
            // this and this one never did, so scrolling down a long parcel panel zoomed the town
            // out from under it instead of moving the list. The panel scrolls itself; the map is
            // only the wheel's business when the pointer is actually on the map.
            if (VillageUI.PointerOverUI) return;

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f) return;

            _distance = Mathf.Clamp(_distance * (scroll > 0 ? 0.88f : 1f / 0.88f),
                                    MinDistance, MaxDistance);
        }

        private void HandleOrbit()
        {
            var mouse = Mouse.current;
            // Mouse orbit stays live while typing - you want to look at the lot you
            // are describing. Only the KEY half is withheld.
            var keyboard = VillageUI.KeyboardCaptured ? null : Keyboard.current;

            // Q and E snap-free rotate; right-drag orbits freely.
            if (keyboard != null)
            {
                float turn = 0f;
                if (keyboard.qKey.isPressed) turn -= 1f;
                if (keyboard.eKey.isPressed) turn += 1f;
                // Rotating deliberately KEEPS follow on, where panning drops it. Turning the
                // camera around somebody you are watching is a change of viewpoint on the same
                // subject; moving the target is a change of subject. Said here because what
                // stood on this line was `Following = Following`, which reads as an intention
                // somebody deleted rather than as one anybody decided.
                if (turn != 0f) _yaw += turn * 70f * Time.unscaledDeltaTime;

                if (keyboard.rKey.isPressed) _pitch += 40f * Time.unscaledDeltaTime;
                if (keyboard.fKey.isPressed && keyboard.leftShiftKey.isPressed)
                    _pitch -= 40f * Time.unscaledDeltaTime;
            }

            if (mouse != null)
            {
                var pos = mouse.position.ReadValue();
                if (mouse.rightButton.isPressed)
                {
                    if (!_orbiting) { _orbiting = true; _lastMouse = pos; }
                    var delta = pos - _lastMouse;
                    _lastMouse = pos;
                    _yaw += delta.x * 0.25f;
                    _pitch -= delta.y * 0.20f;
                }
                else _orbiting = false;
            }

            _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);
        }

        private void HandlePan()
        {
            // Keys belong to the text field while one has focus - see
            // VillageUI.KeyboardCaptured. WASD/QE/R/F/Tab are all bound here.
            if (VillageUI.KeyboardCaptured) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            var move = Vector3.zero;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.z += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.z -= 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1f;
            if (move == Vector3.zero) return;

            _host.Following = false;

            // Pan relative to where the camera is looking, not to world axes - anything else
            // feels wrong the moment you have rotated.
            var flatForward = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up).normalized;
            var flatRight = Vector3.ProjectOnPlane(_camera.transform.right, Vector3.up).normalized;

            // The top speed used to be reached at the old MaxDistance (220m) and crossing the
            // whole 2,400m town at it would have taken 40 seconds - fine when 220m was as far
            // out as you could go, not fine now that it is one seventh of the new range.
            float speed = Mathf.Lerp(10f, 300f, _distance / MaxDistance);
            _target += (flatForward * move.z + flatRight * move.x).normalized
                     * speed * Time.unscaledDeltaTime;
        }

        /// <summary>
        /// Which lot the cursor is over, every frame.
        ///
        /// Cheap enough to do per frame and worth checking, given the evening this file has had:
        /// ParcelIndex.Find rejects on a bounding box first, so 794 lots cost 794 rectangle
        /// tests and one polygon test. The ray is the same Space3D.GroundHit the click uses, so
        /// what you hover and what you would select are the same lot - a hover that highlighted
        /// one lot and a click that took its neighbour would be worse than no hover at all.
        ///
        /// Cleared while the pointer is over a panel, so moving the mouse up to the inspector
        /// does not leave a lot lit up behind it.
        /// </summary>
        private void HandleHover()
        {
            var mouse = Mouse.current;
            if (mouse == null || VillageUI.PointerOverUI
                || (_host.Footprint != null && _host.Footprint.Active))
            {
                _host.HoveredParcel = null;
                return;
            }

            var ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
            _host.HoveredParcel = Space3D.GroundHit(ray, out Vector3 hit)
                ? ParcelIndex.Find(GroundPoint(hit))
                : null;
        }

        /// <summary>
        /// How near the cursor a person has to be to win the click, IN PIXELS.
        ///
        /// Pixels rather than metres because that is the tolerance a hand experiences. See
        /// HandleSelection for what it replaced and why that could not hold.
        /// </summary>
        private const float PickPixels = 14f;

        private void HandleSelection()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (VillageUI.PointerOverUI) return;

            // A CLICK THAT LEAVES A TEXT FIELD DOES NOT ALSO SELECT SOMETHING ELSE.
            //
            // VillageUI tells you, on screen, to click the map to stop typing - and that same
            // click was landing on the village, picking whoever was nearby and throwing the lot
            // you were editing off the panel. So the documented way out of the keyboard guard
            // was also the way to lose the thing you were typing into.
            //
            // First click leaves the field, second click selects. That is what every other text
            // field on a computer does, and it costs nothing when no field has focus.
            if (VillageUI.KeyboardCaptured) return;

            var ray = _camera.ScreenPointToRay(mouse.position.ReadValue());

            // DRAWING TAKES EVERY CLICK while it is active - a click is placing the next corner
            // of a house, not selecting whatever happens to be under it. Nothing else below runs.
            if (_host.Footprint != null && _host.Footprint.Active)
            {
                if (Space3D.GroundHit(ray, out Vector3 hit))
                    _host.Footprint.AddPoint(new Vector2(hit.x, -hit.z));
                return;
            }

            // PEOPLE FIRST: they are small, they move, and they are the harder thing to hit, so
            // a click that could mean either should mean the person. They stand on the ground,
            // so the flat projection is the right test for them - against the REAL ground now,
            // not a flat plane a hillside walk would miss by metres.
            var view = _host.GetComponentInChildren<AgentMeshView>();

            if (view != null && Space3D.GroundHit(ray, out Vector3 groundPoint))
            {
                // HOW CLOSE IS CLOSE ENOUGH, MEASURED ON SCREEN RATHER THAN ON THE GROUND.
                //
                // Was `Mathf.Max(1.5f, _distance * 0.03f)`. The camera opens at 330m, so that is
                // a TEN METRE grab radius before you touch anything. A lot in this town is about
                // 20 by 40, which made a click meant for a lot depend on whether any of 148
                // walking citizens happened to be standing within a third of it - reported, and
                // accurately, as "it just changes screens to a person sometimes". Zoomed out it
                // got worse in proportion, which is the tell: the constant was tuned close in and
                // nothing said so.
                //
                // One pixel covers (2 * range * tan(fov/2)) / screenHeight metres at the point
                // under the cursor. Deriving the radius from the camera means it cannot rot when
                // the camera moves, and it is stated in the units the hand actually works in.
                float range = Vector3.Distance(_camera.transform.position, groundPoint);
                float metresPerPixel = 2f * range
                    * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad)
                    / Mathf.Max(1, Screen.height);
                float radius = Mathf.Max(1f, PickPixels * metresPerPixel);

                var picked = view.Pick(groundPoint, radius);
                if (picked.IsValid)
                {
                    _host.Selected = picked;
                    _host.SelectedPlace = Noir.Core.Contracts.PlaceId.None;
                    _host.SelectedParcel = null;
                    return;
                }
            }

            // Otherwise whatever building the ray goes through. Walked rather than flattened
            // onto the ground, so pointing at a wall selects the wall's building - see
            // PlacePicker.
            _host.Selected = Noir.Core.Contracts.CitizenId.None;
            _host.Following = false;

            // BUT NOT WHEN THE BUILDINGS ARE HIDDEN, and they are hidden by default - the survey
            // plan is the view this town is usually looked at in.
            //
            // A place exists in the world model whether or not anything draws it, so PlacePicker
            // walked the ray through houses that were not on screen. Clicking a lot from a
            // shallow camera angle sent that ray clipping through the invisible house on the
            // NEXT lot along, selected that, and the amber outline appeared one plot over -
            // reported exactly that way: "it appears when I click on the lot to the left of it".
            //
            // Worse, a third of those houses sit outside any parcel at all (152 of 477: the
            // generated grid does not line up with the county's real boundaries), so the
            // selection could not even fall back to a lot outline and drew the house's own small
            // footprint instead - a little rectangle floating on a verge, belonging to nothing
            // you had clicked.
            //
            // You cannot mean to click a thing you cannot see. With the plan up, a click is
            // about the LOT, which is the only thing drawn.
            var place = VillageHost.ShowBuildings
                ? PlacePicker.Pick(_host.World, ray)
                : Noir.Core.Contracts.PlaceId.None;
            _host.SelectedPlace = place;

            if (place.IsValid) { _host.SelectedParcel = null; return; }

            // LAST RESORT: no person, no place - most of the real plan is not one. 468 of the
            // 794 real lots never got a house or a business generated on them, and until now a
            // click there found nothing at all, which reads as "clicking is broken" rather than
            // "this lot is undeveloped". Same ground point PlacePicker's own fallback uses.
            _host.SelectedParcel = Space3D.GroundHit(ray, out Vector3 lastResort)
                ? ParcelIndex.Find(GroundPoint(lastResort))
                : null;
        }

        /// <summary>Village space is x,-z of the world point - see Space3D.</summary>
        private static Vector2 GroundPoint(Vector3 worldPoint) =>
            new Vector2(worldPoint.x, -worldPoint.z);

        private void HandleFollow()
        {
            // Keys belong to the text field while one has focus - see
            // VillageUI.KeyboardCaptured. WASD/QE/R/F/Tab are all bound here.
            if (VillageUI.KeyboardCaptured) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.fKey.wasPressedThisFrame
                && !keyboard.leftShiftKey.isPressed && _host.Selected.IsValid)
                _host.Following = !_host.Following;

            if (!_host.Following || !_host.Selected.IsValid) return;

            var agent = _host.Sim.GetAgent(_host.Selected);
            var want = Space3D.ToWorld(agent.Position);
            _target = Vector3.Lerp(_target, want, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
        }

        private void Apply()
        {
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            _camera.transform.position = _target - rotation * Vector3.forward * _distance;
            _camera.transform.rotation = rotation;
        }
    }
}
