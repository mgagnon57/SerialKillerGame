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

        /// <summary>Where the camera starts, and how high it looks. A shoulder, not a helicopter.</summary>
        private const float StartDistance = 4.2f, Height = 1.55f;

        /// <summary>
        /// How close and how far the wheel may take it: five feet to eighty.
        ///
        /// THIS USED TO BE ONE `const` AND THERE WAS NO WHEEL AT ALL. `Update` read the mouse for
        /// LOOK and never for zoom, so the camera stood at 4.2 m whatever you did - which is why
        /// the complaint was "I cannot zoom out much if at all" rather than "zoom does nothing".
        /// It was doing nothing, and something ELSE was holding the camera hard against his back:
        /// see FindTarget, which was aiming the whole rig at his feet.
        ///
        /// Eighty feet is the owner's ruling and it is a reading distance, not a helicopter: it
        /// shows the street, both pavements and the block behind, which is what you want when you
        /// are looking at a town you have just built. Five feet is close enough to read a doorway.
        /// </summary>
        private const float MinDistance = 1.5f, MaxDistance = 24.4f;

        /// <summary>
        /// What one notch of wheel is worth, as a FRACTION OF THE CURRENT DISTANCE rather than a
        /// fixed number of metres.
        ///
        /// A fixed step cannot serve both ends of a sixteen-to-one range: 1 m a notch is four
        /// notches to cross the close end and ninety to cross the far one. Proportional means the
        /// wheel moves what you are looking at by the same PROPORTION everywhere, which is what
        /// the hand expects, and it takes about thirteen notches to go from the shoulder to the
        /// full eighty feet. Windows reports 120 per notch, hence the small-looking number.
        /// </summary>
        private const float ZoomPerNotch = 0.0012f;

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

        /// <summary>How far back the wheel has been asked for. Kept across a Leave/Enter, because
        /// somebody who has set the camera where they want it has not changed their mind by
        /// stepping out of the body and back into it.</summary>
        private float _distance = StartDistance;

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

            // ---- zoom ----
            //
            // NOT GATED ON THE CURSOR LOCK, unlike the look above. The wheel is not a pointer and
            // has nothing to do with whether the mouse is captured; gating it would make the one
            // control that is safe to use with a free cursor the one that stops working.
            if (mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f)
                    _distance = Mathf.Clamp(_distance - wheel * ZoomPerNotch * _distance,
                                            MinDistance, MaxDistance);
            }

            // ---- follow ----
            //
            // Behind and above the target, pulled in if a wall is in the way. Without that last
            // part every street corner puts the camera inside the building behind you, and a
            // third-person camera that clips into geometry reads as broken faster than almost
            // anything else on screen.
            var pivot = _target.position + Vector3.up * (Height - 1.375f);
            var back = Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.back;
            float reach = _distance;

            if (Physics.SphereCast(pivot, 0.25f, back, out var hit, _distance,
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

            // Somebody who lives here rather than Unity's grey mannequin. Editor-only, because the
            // pack is; a build keeps the robot and the log below says which one stood up.
            bool dressed = false;
#if UNITY_EDITOR
            dressed = Reskin(_body);
#endif

            // ThirdPersonController wants a camera target to rotate and a MainCamera to steer by.
            // The prefab carries the first; the second is the one OrbitCamera already made.
            //
            // NO SILENT FALLBACK TO THE ROOT. That is what hid the naming mismatch for months -
            // see FindTarget. If the armature has no camera root, make one at head height and SAY
            // SO, rather than orbiting his shoes and leaving somebody to wonder why the camera is
            // stuck to his back.
            _target = FindTarget(_body.transform);
            if (_target == null)
            {
                _target = MakeTarget(_body.transform);
                Debug.LogWarning("[player] the armature has no camera root of its own, so one was "
                    + "made at head height. Check the prefab: aiming at the body root points the "
                    + "camera at the pavement and the pull-in cast then jams it against his back.");
            }

            _camera = Camera.main;

            if (_camera == null)
            {
                Debug.LogWarning("[player] no main camera to follow with.");
                return false;
            }

            _yaw = _body.transform.eulerAngles.y;
            Debug.Log($"[player] stood at {_body.transform.position} as "
                    + (dressed ? Skin : "the Starter Assets mannequin") + ".");
            return true;
        }

        /// <summary>
        /// Which of the town's own people you are.
        ///
        /// FROM THE CAST, NOT FROM THE 79. `AgentBody` keeps a register of about twenty figures
        /// that pass for an ordinary Illinois town and deliberately leaves out the Fantasy
        /// knights, the Primeval tribe and the Seasons costumes; taking the player from outside
        /// that register would put a man in fullplate on Second Street. Owner's pick, 2026-08-11.
        /// </summary>
        private const string Skin = "Man_Slavic_Winter";

#if UNITY_EDITOR
        /// <summary>
        /// Take the grey robot off and put a townsman on, keeping everything that makes him work.
        ///
        /// WHAT WAS WRONG. `PlayerArmature` ships with `Armature_Mesh` on `M_Armature_Body` -
        /// Unity's faceless grey mannequin. The town around him is 1,385 rigged people from the
        /// pack, so the one figure the player actually sees close up was the only one in Rossville
        /// who looked like a crash-test dummy.
        ///
        /// A SWAP, NOT A REPARENT. The obvious thing - hang a pack character under the armature
        /// and hide the robot - gives you two skeletons and only one of them animating, because an
        /// Animator drives the bones its AVATAR names and nothing else. So this moves the pack's
        /// mesh and rig in, throws the mannequin's `Geometry` and `Skeleton` away, and re-points
        /// the Animator's avatar at what is now underneath it. Everything that makes the body work
        /// stays untouched on the root: CharacterController, ThirdPersonController,
        /// StarterAssetsInputs, PlayerInput, and `PlayerCameraRoot`.
        ///
        /// IT RETARGETS BECAUSE BOTH ENDS ARE HUMANOID - measured before this was written, not
        /// assumed: `ArmatureAvatar` is `isHuman`, and so are 70 of the pack's 79 people. The
        /// controller stays `StarterAssetsThirdPerson`, whose clips are humanoid, so Unity maps
        /// them onto the new skeleton on its own. Their heights agree to within four inches
        /// (1.80 m against the CharacterController's 1.8), so nothing is scaled.
        ///
        /// EDITOR ONLY, AND THAT IS A KNOWN HOLE RATHER THAN AN OVERSIGHT. `Assets/polyperfect` is
        /// gitignored and reached through `AssetDatabase`, so a shipped build has no pack to dress
        /// him from and keeps the mannequin. It is the same hole that leaves the crowd as capsules
        /// in a build - `docs/ANIMATION-FIXES.md` PB-6/PB-7, the cast manifest - and it closes for
        /// the player on the same day it closes for everybody else.
        /// </summary>
        private static bool Reskin(GameObject body)
        {
            string path = null;
            foreach (var guid in AssetDatabase.FindAssets($"{Skin} t:Prefab", new[] { AgentBody.Folk }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) != Skin) continue;
                path = p;
                break;
            }

            var prefab = path == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[player] no '{Skin}' under {AgentBody.Folk} - staying as the "
                    + "Starter Assets mannequin. Assets/polyperfect is gitignored, so this is the "
                    + "normal state of a fresh clone.");
                return false;
            }

            // PLAIN Instantiate, NOT PrefabUtility.InstantiatePrefab, AND THE BODY GETS UNPACKED.
            //
            // Measured the hard way, 2026-08-11:
            //
            //   Setting the parent of a transform which resides in a Prefab instance is not
            //   possible (GameObject: 'man-slavic-winter')
            //
            // A linked prefab instance is SEALED: Unity refuses to reparent or delete anything
            // inside one, because the link is what lets an edit to the asset flow back down. This
            // method is a dismantling - it takes two children out of one instance and throws two
            // out of another - so neither end may keep its link. The dressing is instantiated
            // loose, and the armature, which `Spawn` deliberately created linked, is unpacked
            // first. Nothing is lost: the link is an authoring convenience and this body is built
            // fresh from the prefab every time somebody presses P.
            if (PrefabUtility.IsPartOfPrefabInstance(body))
                PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely,
                                                   InteractionMode.AutomatedAction);

            var dressed = Object.Instantiate(prefab);

            // Written out rather than `?.` on purpose: null propagation across a UnityEngine.Object
            // is the one place where `== null` and `?.` disagree, because a destroyed object is
            // only fake-null.
            var theirs = dressed.GetComponent<Animator>();
            var avatar = theirs == null ? null : theirs.avatar;
            if (avatar == null || !avatar.isHuman)
            {
                Object.DestroyImmediate(dressed);
                Debug.LogWarning($"[player] '{Skin}' has no humanoid avatar, so the Starter Assets "
                    + "clips could not retarget onto it. Staying as the mannequin.");
                return false;
            }

            // Their mesh and their rig, in. Collected first: reparenting inside a foreach over
            // the same transform mutates what is being walked and drops every other child.
            var moving = new System.Collections.Generic.List<Transform>();
            foreach (Transform t in dressed.transform) moving.Add(t);
            foreach (var t in moving) t.SetParent(body.transform, false);
            Object.DestroyImmediate(dressed);

            // The mannequin's, out. Geometry first - its SkinnedMeshRenderer is bound to bones in
            // Skeleton, and leaving a skin pointing at destroyed bones is how you get a body
            // smeared across the origin.
            foreach (var name in new[] { "Geometry", "Skeleton" })
            {
                var old = body.transform.Find(name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }

            // Only now, and REBIND, or the Animator keeps driving a hierarchy that has gone.
            var animator = body.GetComponent<Animator>();
            animator.avatar = avatar;
            animator.Rebind();
            return true;
        }
#endif

        /// <summary>
        /// The node the camera orbits, which for months WAS NOT FOUND AND NOBODY NOTICED.
        ///
        /// Starter Assets calls it `PlayerCameraRoot` and sits it at y=1.375 - head height. This
        /// method looked for `CameraTarget` and `CinemachineTarget`, matched neither, returned
        /// null, and the caller quietly fell back to `_body.transform`, WHICH IS HIS FEET. So the
        /// pivot came out at 0.175 m and the whole rig orbited his ankles.
        ///
        /// AND THAT IS WHY THE CAMERA WOULD NOT PULL BACK. It is not only that the picture was
        /// aimed low: the pull-in SphereCast starts at the pivot, the pivot was seven inches off
        /// the pavement, and the ground collider is right there - so almost every frame it hit
        /// immediately and clamped `reach` to its 0.6 m floor. The camera was jammed against his
        /// back by the ground, which reads exactly like "I cannot zoom out much if at all", and
        /// no amount of wheel would have fixed it on its own.
        ///
        /// A silent fallback to the wrong transform is what let a naming mismatch survive this
        /// long - it compiled, it ran, it drew a picture, and the picture was of somebody's shoes.
        /// The caller makes a proper target now and says so out loud.
        /// </summary>
        private static Transform FindTarget(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.Contains("CameraRoot")
                 || t.name.Contains("CameraTarget")
                 || t.name.Contains("CinemachineTarget")) return t;
            return null;
        }

        /// <summary>The camera's pivot, made rather than found, for a body that has no such node
        /// of its own. Head height, so the arithmetic in Update is the same either way.</summary>
        private static Transform MakeTarget(Transform root)
        {
            var made = new GameObject("PlayerCameraRoot");
            made.transform.SetParent(root, false);
            made.transform.localPosition = new Vector3(0f, 1.375f, 0f);
            return made.transform;
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
