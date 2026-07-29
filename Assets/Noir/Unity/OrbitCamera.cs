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
        private float _yaw = 45f;
        private float _pitch = 50f;
        private float _distance = 90f;

        private const float MinPitch = 12f;    // below this you are looking through the ground
        private const float MaxPitch = 89f;
        private const float MinDistance = 6f;
        private const float MaxDistance = 220f;

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
            rig._target = Space3D.Centre(host.World.Width, host.World.Height);
            rig.Apply();
            return rig;
        }

        public void Tick()
        {
            if (_camera == null) return;

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
            HandleFollow();
            Apply();
        }

        private void HandleModeSwitch()
        {
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

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f) return;

            _distance = Mathf.Clamp(_distance * (scroll > 0 ? 0.88f : 1f / 0.88f),
                                    MinDistance, MaxDistance);
        }

        private void HandleOrbit()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

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

            float speed = Mathf.Lerp(10f, 60f, _distance / MaxDistance);
            _target += (flatForward * move.z + flatRight * move.x).normalized
                     * speed * Time.unscaledDeltaTime;
        }

        private void HandleSelection()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (VillageUI.PointerOverUI) return;

            // Intersect the view ray with the ground plane, then take the nearest person to it.
            var ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float enter)) return;

            var hit = ray.GetPoint(enter);
            var view = _host.GetComponentInChildren<AgentMeshView>();
            if (view == null) return;

            float radius = Mathf.Max(1.5f, _distance * 0.03f);
            var picked = view.Pick(hit, radius);
            _host.Selected = picked;
            if (!picked.IsValid) _host.Following = false;
        }

        private void HandleFollow()
        {
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
