using UnityEngine;
using UnityEngine.InputSystem;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// Somebody to be in the town, rather than above it.
    ///
    /// Everything in this project is bootstrapped from code and nothing is dragged into a scene -
    /// see VillageHost, which builds the whole city off its own RuntimeInitializeOnLoadMethod.
    /// The Starter Assets package is authored the other way round, as prefabs you place by hand,
    /// so this is the adapter: it loads `PlayerArmature`, stands it in a street, and gives it a
    /// camera. There is still nothing to set up in the editor.
    ///
    /// NO CINEMACHINE. Starter Assets ships a `PlayerFollowCamera` that wants it, and the package
    /// is not in this project - but `ThirdPersonController` itself never references Cinemachine.
    /// It rotates a plain GameObject it calls `CinemachineCameraTarget` and reads the main
    /// camera's yaw to steer by. So the target is all that is actually required, and the fifty
    /// lines below follow it. Adding a package mid-project to get an orbit we already know how to
    /// write is a poor trade; swap in Cinemachine later by pointing a vcam at the same target.
    ///
    /// PRESS P to go from looking at Rossville to standing in it, and P again to come back.
    /// </summary>
    public sealed class Player : MonoBehaviour
    {
        private const string Armature =
            "Assets/StarterAssets/ThirdPersonController/Prefabs/PlayerArmature.prefab";

        /// <summary>How far back and how high the camera sits. A shoulder, not a helicopter.</summary>
        private const float Distance = 4.2f, Height = 1.55f;

        /// <summary>Degrees a second per unit of mouse travel.</summary>
        private const float LookSpeed = 220f;

        /// <summary>How far the camera may be pitched, so it never goes through the pavement.</summary>
        private const float MinPitch = -28f, MaxPitch = 68f;

        private VillageHost _host;
        private GameObject _body;
        private Transform _target;          // the ThirdPersonController's own camera target
        private Camera _camera;
        private OrbitCamera _orbit;
        private float _yaw, _pitch = 14f;

        public bool Walking { get; private set; }

        /// <summary>
        /// Where the body is standing, in world space, or null when nobody is in it.
        ///
        /// DELIBERATELY A UNITY TYPE AND NOTHING MORE. VillageHost writes the observation track
        /// and is the only file in the game allowed to name the witness assembly - see
        /// WitnessFirewallTests. Handing it a Vector3, rather than recording from in here, is
        /// what keeps this file out of that assembly and the exception down to one file.
        ///
        /// (The guard is a plain text scan for the assembly's full name, so this comment cannot
        /// spell it. That bluntness is the point: a check that understood the difference between
        /// code and prose would also be a check somebody could talk their way around.)
        /// </summary>
        public Vector3? Where =>
            Walking && _body != null ? _body.transform.position : (Vector3?)null;

        public static Player Create(VillageHost host, Transform parent)
        {
            var go = new GameObject("Player");
            go.transform.SetParent(parent, false);
            var player = go.AddComponent<Player>();
            player._host = host;
            return player;
        }

        private void Update()
        {
            var keys = Keyboard.current;
            if (keys != null && keys.pKey.wasPressedThisFrame) Toggle();

            if (!Walking || _target == null || _camera == null) return;

            // ---- look ----
            var mouse = Mouse.current;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                var d = mouse.delta.ReadValue() * (LookSpeed * 0.0006f);
                _yaw += d.x;
                _pitch = Mathf.Clamp(_pitch - d.y, MinPitch, MaxPitch);
            }

            // ---- follow ----
            //
            // Behind and above the target, pulled in if a wall is in the way. Without that last
            // part every street corner puts the camera inside the building behind you, and a
            // third-person camera that clips into geometry reads as broken faster than almost
            // anything else on screen.
            var pivot = _target.position + Vector3.up * (Height - 1.375f);
            var back = Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.back;
            float reach = Distance;

            if (Physics.SphereCast(pivot, 0.25f, back, out var hit, Distance,
                                   ~0, QueryTriggerInteraction.Ignore))
                reach = Mathf.Max(0.6f, hit.distance - 0.15f);

            _camera.transform.position = pivot + back * reach;
            _camera.transform.rotation = Quaternion.LookRotation(pivot - _camera.transform.position);
        }

        /// <summary>In or out of the body.</summary>
        public void Toggle()
        {
            if (Walking) { Leave(); return; }
            Enter();
        }

        private void Enter()
        {
            if (_body == null && !Spawn()) return;
            Walking = true;
            _body.SetActive(true);

            _orbit = Object.FindFirstObjectByType<OrbitCamera>();
            if (_orbit != null) _orbit.enabled = false;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Debug.Log("[player] walking. P to come back out.");
        }

        private void Leave()
        {
            Walking = false;
            if (_body != null) _body.SetActive(false);
            if (_orbit != null) _orbit.enabled = true;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>
        /// The name `Resources.Load` is given at runtime. Made by
        /// `Noir.Editor.PlayerArmatureResource`, which must be re-run after Starter Assets is
        /// re-imported.
        /// </summary>
        private const string ArmatureResource = "PlayerArmature";

        /// <summary>
        /// Stand a body up, IN A SHIPPED BUILD AS WELL AS IN THE EDITOR.
        ///
        /// This whole method used to be `#if UNITY_EDITOR`, because it reaches the armature
        /// through `AssetDatabase` - so **pressing P in a shipped Rossville did nothing at all.**
        /// No body, no error, no log. The one control that lets somebody walk down the street they
        /// have just built was editor-only from the day it was written.
        ///
        /// RESOURCES FIRST AND IN BOTH, rather than a `#if` with two branches. A path only a build
        /// takes is a path nobody exercises: this way the editor walks the same load every day,
        /// and the AssetDatabase line below is the FALLBACK - for a machine that has not run
        /// `Noir > Make The Player Shippable` yet.
        /// </summary>
        private bool Spawn()
        {
            var prefab = Resources.Load<GameObject>(ArmatureResource);

#if UNITY_EDITOR
            if (prefab == null)
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Armature);
                if (prefab != null)
                    Debug.LogWarning("[player] the armature came from AssetDatabase, so a SHIPPED "
                        + "build of this tree still cannot stand a body up. Run "
                        + "Noir > Make The Player Shippable.");
            }
#endif

            if (prefab == null)
            {
                Debug.LogWarning($"[player] no {ArmatureResource} in Resources and no {Armature} - "
                    + "run Noir > Make The Player Shippable, or import Starter Assets first.");
                return false;
            }

            // ASSIGN THE FIELD. Instantiating into a new local leaves `_body` null and the very
            // next line NullReferences - which compiles perfectly and takes two PlayMode tests
            // with it. `PrefabUtility.InstantiatePrefab` keeps the prefab link in the editor and
            // does not exist outside it; plain Instantiate is what a build has.
#if UNITY_EDITOR
            _body = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
#else
            _body = Object.Instantiate(prefab);
#endif
            _body.transform.SetParent(transform, false);
            _body.transform.position = Standing(_host.World);
            _body.name = "PlayerArmature";

            // ThirdPersonController wants a camera target to rotate and a MainCamera to steer by.
            // The prefab carries the first; the second is the one OrbitCamera already made.
            _target = FindTarget(_body.transform) ?? _body.transform;
            _camera = Camera.main;

            if (_camera == null)
            {
                Debug.LogWarning("[player] no main camera to follow with.");
                return false;
            }

            _yaw = _body.transform.eulerAngles.y;
            Debug.Log($"[player] stood at {_body.transform.position}.");
            return true;
        }

        private static Transform FindTarget(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (t.name.Contains("CameraTarget") || t.name.Contains("CinemachineTarget")) return t;
            return null;
        }

        /// <summary>
        /// Where a person starts.
        ///
        /// ON THE PAVEMENT OF THE WIDEST STREET IN TOWN, asked of the map rather than typed: of
        /// the north-south freeways, the one whose centre line runs closest to the middle of the
        /// map, offset from it by enough to clear the asphalt. Typing a coordinate here would put
        /// the player in a field the next time the map is re-laid - which has happened twice
        /// already, most recently when everything moved +120.
        ///
        /// NEAREST THE MIDDLE, not the first one found. Taking the first put them on Westway at
        /// 375, which is downtown's western boundary and half a mile of walking from anything;
        /// nearest-the-middle is Second Street, which is the one the town was laid out around.
        ///
        /// AND THE HEIGHT IS ASKED OF THE MAP FOR THE SAME REASON, which it was not until the
        /// people were turned on and the fall stopped being survivable. This was a typed `3f`
        /// meaning "three metres up, and the floor is at nought" - true while the map was one flat
        /// plane, and quietly wrong from the moment ElevationGrid gave it 24m of relief. The
        /// ground under Second Street is +4.2m, so a player dropped at y=3 arrived a metre BELOW
        /// the collision mesh, fell straight through it and kept going.
        ///
        /// IT LOOKED LIKE IT WORKED FOR AS LONG AS THE FRAMES WERE CHEAP. `ThePlayerCanStandInTheStreet`
        /// drops the player and reads the height 240 frames later, asking for something above -1m;
        /// with nobody drawn those frames were short enough that the man was still only just under
        /// the ground when it looked, and falling read as standing. Seven hundred and sixty-three
        /// animators made the frames long enough to finish the fall, and the same bug that had
        /// been there all along finally cleared the bar. A test that passes because the machine is
        /// fast is a test that was not measuring what it thought.
        /// </summary>
        private static Vector3 Standing(WorldModel world)
        {
            float middle = world.Width * 0.5f;
            float x = middle, best = float.MaxValue;

            foreach (var line in world.Roads.Lines)
            {
                if (!line.IsStraight || !line.IsNorthSouth) continue;
                if (line.Class != RoadClass.Freeway) continue;

                float from = Mathf.Abs(line.Centre - middle);
                if (from >= best) continue;

                best = from;
                // Half the corridor out from the centre line, less a stride, which lands on
                // pavement rather than in the gutter or through a shop window.
                x = line.Centre + line.HalfWidth - 2f;
            }

            // Three metres above the ground THERE, not above nought. Still a drop, on purpose:
            // landing is what proves the collision shell holds, and it is the one thing about a
            // player character a still photograph cannot answer.
            float y = world.Height * 0.5f;
            return new Vector3(x, ElevationGrid.HeightAt(x, y) + 3f, -y);
        }
    }
}
