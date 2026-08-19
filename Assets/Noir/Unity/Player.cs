using UnityEngine;
using UnityEngine.InputSystem;
using Noir.Core.Contracts;
using Noir.Core.People;
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

        /// <summary>Behind a wheel rather than on foot. Walking and Driving are exclusive.</summary>
        public bool Driving { get; private set; }

        /// <summary>The track recorder's question, by its own name.</summary>
        public bool InVehicle => Driving;

        /// <summary>The taken car's witness-facing identity. Valid while Driving.</summary>
        public CarTone CarTone { get; private set; }
        public CarShape CarShape { get; private set; }

        /// <summary>The car's position at the START of this frame's drive step, or null on
        /// the first frame — the other end of the hit sweep's segment.</summary>
        public Vector3? CarTravelledFrom { get; private set; }

        private GameObject _car;
        private float _carSpeed;                       // m/s, signed (negative = reverse)

        /// <summary>The last car the player got out of, remembered so the interaction seam can
        /// offer it back - PlayerInteraction's own-car candidate, CarInteractable.
        /// OwnCarInteractable. Only one car is remembered, the most recent: taking a different
        /// car forgets the old one where it stands (v1 - the town's other 546 cars are still
        /// there). Null before any car has ever been taken. SitIn itself never touches this
        /// field - the two callers that put the player back behind a wheel do: ReenterLastCar
        /// clears it (it IS the remembered car, taken back), and EnterCar leaves it stale (a
        /// DIFFERENT car was taken, and LastCarPosition's own `!Driving` guard is what keeps the
        /// stale value from being offered again while driving).</summary>
        private GameObject _lastCar;

        /// <summary>
        /// Where the body is standing, in world space, or null when nobody is in it. Answers for
        /// the car as well while Driving — same witness-facing question, different vehicle.
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
            Walking && _body != null ? _body.transform.position
          : Driving && _car != null ? _car.transform.position
          : (Vector3?)null;

        /// <summary>Where the player's own abandoned car stands, for the interaction seam -
        /// null while driving (nothing is "abandoned" mid-drive) or before any car was ever
        /// taken.</summary>
        public Vector3? LastCarPosition =>
            _lastCar != null && !Driving ? _lastCar.transform.position : (Vector3?)null;

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
            if (keys != null && keys.pKey.wasPressedThisFrame && !Driving
                && !VillageUI.KeyboardCaptured) Toggle();

            if (Driving) { DriveStep(keys); return; }

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

        /// <summary>
        /// One frame behind the wheel. Kinematic on purpose — see the spec and CarMesh.cs's
        /// own measurement: a physics vehicle halved this town's frame rate. Real time
        /// (Time.deltaTime), same clock the NPC fleet drives on.
        /// </summary>
        private void DriveStep(Keyboard keys)
        {
            if (_car == null || _camera == null)
            {
                // Torn out of the seat rather than stepping out of it - most plausibly the car
                // was destroyed out from under the driver (the town tore the driveway down mid-
                // drive). LeaveCar's own exit math needs the car's transform, which is gone, so
                // this is LeaveCar without a car: stand the body back up where it was left - its
                // position was never touched while driving - rather than leaving Driving false
                // with Walking also false and the player nowhere at all.
                //
                // THE CAR ITSELF MAY STILL BE STANDING even though this branch fired - the other
                // half of the guard is _camera, and losing the camera says nothing about the
                // car. Stash it into _lastCar exactly as LeaveCar would, but only "if the
                // reference is still alive": Unity's overloaded == already reports a destroyed
                // car as null, so a car that really was torn out from under the driver leaves
                // nothing worth remembering.
                if (_car != null) _lastCar = _car;
                Driving = false;
                _car = null;
                CarTravelledFrom = null;
                _sweepPrev = null;
                Walking = true;
                if (_body != null) _body.SetActive(true);
                return;
            }
            float dt = Time.deltaTime;
            bool typing = VillageUI.KeyboardCaptured;

            // ---- throttle ----
            float want = 0f;
            if (!typing && keys != null)
            {
                if (keys.wKey.isPressed || keys.upArrowKey.isPressed) want = TopSpeed;
                if (keys.sKey.isPressed || keys.downArrowKey.isPressed) want = -ReverseSpeed;
            }
            float rate = Mathf.Abs(want) > Mathf.Abs(_carSpeed) ? Accelerate : Brake;
            _carSpeed = Mathf.MoveTowards(_carSpeed, want, rate * dt);

            // ---- steering, scaled by speed so the car cannot pivot on a point ----
            if (!typing && keys != null && Mathf.Abs(_carSpeed) > 0.2f)
            {
                float steer = 0f;
                if (keys.aKey.isPressed || keys.leftArrowKey.isPressed) steer -= 1f;
                if (keys.dKey.isPressed || keys.rightArrowKey.isPressed) steer += 1f;
                float sign = _carSpeed < 0f ? -1f : 1f;      // reversing steers the other way
                _car.transform.Rotate(0f,
                    steer * sign * TurnRate * (Mathf.Abs(_carSpeed) / TopSpeed) * dt, 0f);
            }

            // ---- move, stopped by the same walls that stop a person ----
            CarTravelledFrom = _car.transform.position;
            Vector3 step = _car.transform.forward * _carSpeed * dt;
            float distance = step.magnitude;

            // The officer's hold line binds the player exactly as it binds the fleet —
            // the barricade collider stops the closed half physically; this stops the
            // open lane until the wave. Traffic answers the same question RunSegment
            // asks for the ambient cars. Only FORWARD motion is held: `_carSpeed > 0f`
            // keeps this from also zeroing reverse and steering (which scales with
            // |speed|) — without it a held player was trapped, unable to back off the
            // line they were stopped at.
            if (_carSpeed > 0f && _host != null && _host.Traffic != null
                && _host.Traffic.CordonHolds(_car.transform.position, _car.transform.forward))
            {
                _carSpeed = 0f;
                distance = 0f;
            }

            if (distance > 0f)
            {
                // The box floats above the road - bottom at +0.4m, raised from an earlier
                // +0.9/0.7 - so neither this cast nor the destination checks below ever pick up
                // the ground collider itself, on any slope the elevation grid can produce.
                var half = new Vector3(0.95f, 0.6f, 2.6f);
                var origin = _car.transform.position + Vector3.up * 1.0f;
                if (Physics.BoxCast(origin, half, step.normalized, out var hit,
                                    _car.transform.rotation, distance, ~0,
                                    QueryTriggerInteraction.Ignore))
                {
                    distance = Mathf.Max(0f, hit.distance - 0.05f);
                    _carSpeed = 0f;
                }
                var to = _car.transform.position + step.normalized * distance;
                to.y = ElevationGrid.HeightAt(to.x, -to.z);

                // A cast is blind to a collider it STARTS inside of, and steering is not swept -
                // a rotation can put the nose into a wall, and the next frame's cast sees
                // nothing. So the destination is checked too, with an escape rule: a move that
                // would take a CLEAN car into overlap is refused; a car already overlapping may
                // move (that is how it backs out of the wall it was steered into).
                bool cleanNow = !Physics.CheckBox(origin, half, _car.transform.rotation,
                                                  ~0, QueryTriggerInteraction.Ignore);
                bool cleanThere = !Physics.CheckBox(to + Vector3.up * 1.0f, half,
                                                    _car.transform.rotation,
                                                    ~0, QueryTriggerInteraction.Ignore);
                if (cleanNow && !cleanThere) _carSpeed = 0f;
                else _car.transform.position = to;
            }

            // ---- camera: the walking follow block, on a longer tether ----
            var mouse = Mouse.current;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                var d = mouse.delta.ReadValue() * (LookSpeed * 0.0006f);
                _yaw += d.x;
                _pitch = Mathf.Clamp(_pitch - d.y, MinPitch, MaxPitch);
            }
            var pivot = _car.transform.position + Vector3.up * 1.2f;
            var back = Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.back;
            float reach = DriveCamDistance;
            if (Physics.SphereCast(pivot, 0.25f, back, out var wall, DriveCamDistance,
                                   ~0, QueryTriggerInteraction.Ignore))
                reach = Mathf.Max(0.6f, wall.distance - 0.15f);
            _camera.transform.position = pivot + back * reach;
            _camera.transform.rotation = Quaternion.LookRotation(pivot - _camera.transform.position);

            // ---- did this frame's travel go through anybody? ----
            //
            // AFTER the camera, not before: a refused move (the BoxCast/CheckBox pair above
            // held the car at its old spot) or a zero-throttle frame leaves CarTravelledFrom
            // equal to the car's current position, and the closest-approach math in
            // SweepForVictims handles that segment fine - t clamps to 0 and the test becomes a
            // plain point-distance check - so a stalled car standing on somebody still hits
            // them rather than getting a free pass because it never moved.
            //
            // GATED ON SPEED, NOT ON WHETHER IT CALLS AT ALL - SweepForVictims itself stays
            // ungated (the PlayMode gate drives it directly, without throttle input), only this
            // call site is. A pedestrian brushing a parked or crawling car is a bump, not a
            // casualty; the harm threshold is a walking pace, roughly the 1.5 m/s a person on
            // foot moves at, so a car below that is not travelling fast enough to be the one
            // doing the hitting.
            if (CarTravelledFrom.HasValue && Mathf.Abs(_carSpeed) >= 1.5f)
                SweepForVictims(CarTravelledFrom.Value, _car.transform.position);
        }

        /// <summary>Half the car's width plus a shoulder. A person inside this lateral
        /// distance of the car's path was hit.</summary>
        private const float HitRadius = 1.3f;

        /// <summary>
        /// Where each sim agent stood at the last SweepForVictims call, world space, indexed the
        /// same way sim.GetAgent(i) is. Null whenever there is no "last sweep" to compare
        /// against: fresh out of Awake, and again every time a driving session ends or begins -
        /// LeaveCar, the torn-out-of-the-seat bailout in DriveStep, and EnterCar itself, so a
        /// walking-mode caller (the PlayMode gate calls this directly, with no car at all) can
        /// never leave a stale cache for the next real drive to inherit.
        /// </summary>
        private Vector3[] _sweepPrev;

        /// <summary>
        /// Did this frame's travel pass through anybody? SIM positions, never figures - the
        /// blessed pattern (AgentMeshView.Pick's own header).
        ///
        /// BOTH ENDS ARE MOVING, NOT JUST THE CAR. Checking the car's segment against a person's
        /// CURRENT point treats them as standing still, and at a fast sim clock - 300x batches
        /// thousands of ticks into one Update - a person's own travel across that one frame can
        /// be metres, which is exactly the tunnelling this method exists to prevent.
        /// AgentState.PreviousPosition cannot fix this: it is one sim TICK back, nothing next to
        /// a frame spanning thousands of them. So this keeps its OWN cache of where everybody
        /// stood at the last sweep and finds the closest approach between two moving points over
        /// the frame - the car travelling from -> to, the person travelling _sweepPrev[i] -> now
        /// - both parameterised by the same t in [0,1]: with A = _sweepPrev[i] - from and
        /// B = (now - _sweepPrev[i]) - (to - from), the squared distance is |A + B*t|, minimised
        /// at t* = clamp01(-Dot(A,B) / Dot(B,B)).
        ///
        /// THE CACHE HAS TO BE SEEDED BEFORE IT MEANS ANYTHING. The first sweep after EnterCar,
        /// or the very first call from a walking-mode caller, has no "last sweep" to compare
        /// against, so it only records where everybody stood and reports no hits - the
        /// alternative is inventing a "previous" position out of nothing and calling whatever
        /// that invents a hit.
        ///
        /// Public so the PlayMode gate can prove a hit without forging input.
        /// </summary>
        public void SweepForVictims(Vector3 from, Vector3 to)
        {
            var sim = _host.Sim;
            var world = _host.World;
            if (sim == null || world == null) return;

            bool seeding = _sweepPrev == null;
            if (seeding) _sweepPrev = new Vector3[sim.AgentCount];

            Vector3 segCar = to - from; segCar.y = 0f;

            for (int i = 0; i < sim.AgentCount; i++)
            {
                var agent = sim.GetAgent(i);
                var p = Space3D.ToWorld(agent.Position);        // same conversion the view uses

                if (seeding) { _sweepPrev[i] = p; continue; }    // no "last sweep" yet - seed only

                // ALWAYS refreshed, even for an agent the filters below skip - a stale prev on a
                // filtered agent (indoors, away, already down) must not read as a sudden jump the
                // instant the filter stops applying to them.
                Vector3 prev = _sweepPrev[i];
                _sweepPrev[i] = p;

                if (agent.Downed) continue;
                if (agent.Doing == Activity.AwayFromTown) continue;

                var tile = agent.Position.ToTile();
                if ((world.Grid.FlagsAt(tile) & TileFlags.Indoor) != 0) continue;

                // Closest approach of two moving points over the frame - see the method header.
                Vector3 a = prev - from; a.y = 0f;
                Vector3 b = (p - prev) - segCar; b.y = 0f;
                float bb = Vector3.Dot(b, b);
                float t = bb < 1e-6f ? 0f : Mathf.Clamp01(-Vector3.Dot(a, b) / bb);
                Vector3 rel = a + b * t;
                if (rel.sqrMagnitude > HitRadius * HitRadius) continue;

                _host.CarStruckSomebody(new CitizenId(i), p, Mathf.Abs(_carSpeed));
            }
        }

        /// <summary>In or out of the body.</summary>
        public void Toggle()
        {
            // Guarded here too, not only at the P-key call site: a public caller (a PlayMode
            // test, in particular) can reach this directly, and Toggle while Driving would run
            // Enter() with Driving still true - Where left answering with the stale body
            // position, and the witness track recording garbage, mid-drive.
            if (Driving) return;
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
            // Where the player was standing, captured before the body deactivates: the orbit
            // camera opens over the scene just left, not wherever the overview last sat.
            var at = _body != null ? _body.transform.position : transform.position;

            Walking = false;
            if (_body != null) _body.SetActive(false);
            if (_orbit != null)
            {
                _orbit.enabled = true;
                _orbit.ArriveOver(at);
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>Speeds: the county's own scale. NPC traffic runs 8 m/s; the player may
        /// hurry a little, and 12 m/s is 27 mph — a lot, on a street where people walk.</summary>
        private const float TopSpeed = 12f, ReverseSpeed = 4f, Accelerate = 8f, Brake = 16f;
        private const float TurnRate = 90f;            // degrees/second at full speed
        private const float DriveCamDistance = 7f;

        /// <summary>Into the driver's seat of driveway car <paramref name="index"/> — called
        /// by the interaction seam's Perform. Takes the car out of CityDriveways' ownership
        /// (its old owner's schedule would otherwise blink it invisible mid-drive).</summary>
        public void EnterCar(int index)
        {
            if (Driving || !Walking) return;
            var driveways = _host.Driveways;
            if (driveways == null) return;

            var (car, tone, shape) = driveways.Take(index);
            if (car == null) return;

            CarTone = tone;
            CarShape = shape;
            SitIn(car);
            Debug.Log($"[player] driving {shape} ({tone}). E to get out.");
        }

        /// <summary>Back into the car just stepped out of - the interaction seam's own-car
        /// candidate, CarInteractable.OwnCarInteractable, offered wherever LastCarPosition says
        /// the abandoned car stands. Guarded the same way EnterCar is (not already driving, on
        /// foot), plus the obvious third: there has to BE a remembered car. CarTone/CarShape are
        /// already sitting from the drive that put the car there in the first place, so unlike
        /// EnterCar there is nothing here to take or re-derive.</summary>
        public void ReenterLastCar()
        {
            if (Driving || !Walking || _lastCar == null) return;
            var car = _lastCar;
            _lastCar = null;
            SitIn(car);
            Debug.Log($"[player] back in the {CarShape} ({CarTone}). E to get out.");
        }

        /// <summary>The mode-entry tail EnterCar and ReenterLastCar share, so the two paths
        /// cannot drift apart: body off, Driving true, yaw taken from the car's own facing,
        /// speed zeroed, and CarTravelledFrom/_sweepPrev cleared. THE CACHE HAS TO BE FRESH
        /// EVERY TIME, even if a walking-mode caller (the PlayMode gate) left one behind - "the
        /// first sweep after entering a car" has to mean exactly that, not "the first sweep
        /// since whenever this field last happened to be null".</summary>
        private void SitIn(GameObject car)
        {
            _car = car;
            _carSpeed = 0f;
            CarTravelledFrom = null;
            _sweepPrev = null;

            Walking = false;
            Driving = true;
            _body.SetActive(false);
            _yaw = _car.transform.eulerAngles.y;

            _host.Traffic?.Obstacles.Remove(car.transform);
        }

        /// <summary>Out at the driver's door. The car stays exactly where it stands, remembered
        /// as _lastCar so the interaction seam can offer it back - see ReenterLastCar.</summary>
        public void LeaveCar()
        {
            if (!Driving) return;
            var at = _car.transform.position - _car.transform.right * 1.6f;
            at.y = ElevationGrid.HeightAt(at.x, -at.z) + 0.5f;

            Driving = false;
            _lastCar = _car;
            _host.Traffic?.Obstacles.Add(_lastCar.transform);
            _car = null;
            CarTravelledFrom = null;
            _sweepPrev = null;

            Walking = true;
            _body.SetActive(true);
            var cc = _body.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            _body.transform.position = at;
            if (cc != null) cc.enabled = true;
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
            // HOME FIRST (owner's ruling, 2026-08-18): when the town has 408 Holmes Street -
            // the survey-seated lot, his own hand-made house since the same night - P stands
            // you on its front walk, one stride out from the door, facing the house. The
            // road-centre fallback below still serves every fixture town and any map without
            // the address, so no test moves.
            const string HomeAddress = "408 Holmes Street";
            foreach (var place in world.AllPlaces)
                if (place != null && place.Name == HomeAddress)
                {
                    var w = Space3D.ToWorld(place.Door);
                    return new Vector3(w.x, w.y, w.z - 2f);   // village +y is toward Holmes
                }

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
