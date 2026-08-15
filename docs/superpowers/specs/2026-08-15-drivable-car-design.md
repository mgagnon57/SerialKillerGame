# A drivable car, and a town that can testify about what you did with it

**The ask.** "I want to be able to drive a car around." Confirmed in brainstorming, with five
rulings from the owner on 2026-08-15, all recorded here so no later session re-litigates them:

1. **Any standing car** can be entered — not a single owned car, not a spawned one.
2. **Harm is real from day one.** Hitting a person is a recorded game event, not a pass-through.
3. **The body stays where it fell.** Witnesses in sight record it; the town's *reaction* is later.
4. **Police respond and an ambulance removes the body after the investigation completes — Phase 2**,
   not this pass (captured in `docs/IDEAS.md` under Story). Phase 1's event record is Phase 2's
   input contract: victim, place, minute, car identity.
5. **Full testimony in Phase 1.** A witness can say "about half past four, I saw a car hit
   somebody" with a coarse car description. Not deferred with the police.

## What exists today (measured, with anchors)

**Standing cars.** `CityDriveways` stands 547 cars at 539 houses. Each is one flattened
GameObject — single `MeshFilter`/`MeshRenderer`, **no collider, deliberately** (a box was tried
2026-08-08 and reverted: 611 solid objects in front gardens; `CarMesh.cs:107`) — named
`Parked_{home}_{unit}`, linked to a *household* via parallel lists (`_cars`, `_homeOf`, `_byHome`;
`CityDriveways.cs:55`). The colour-variant prefab name is **discarded** by that rename
(`CityDriveways.cs:122`). `Refresh` runs once per sim minute and only ever calls `SetActive` in
place, keyed on the household's `AwayFromTown` count — it never teleports, destroys, or respawns,
and it tolerates a null slot (`CityDriveways.cs:146-175`). Lot cars (`CityParking`) are **baked
into chunks** at startup and cannot be individually entered. Everything that draws a car is
`#if UNITY_EDITOR`.

**Moving cars.** `CityTraffic` is pure transform kinematics in fixed 1/30 s real-time slices
(`CityTraffic.cs:963`), no Rigidbody — a kinematic Rigidbody was tried and **measured at half the
frame rate**, then reverted (`CarMesh.cs:44-55`). All five separation queries (`Blocked`,
`TurnFree`, `RoomBeyond`, `NothingCrossing`, `NothingComing`) iterate `_movers` only: nothing
outside that list is ever an obstacle to an NPC car (`CityTraffic.cs:1541`).

**The player rig.** `Player.Enter/Leave` hand the camera between `OrbitCamera` (an enabled flag)
and a follow block (pivot + back vector + pull-in SphereCast, `Player.cs:142-151`). `Where` is a
`Vector3?` the witness recorder consumes. Every control reads the new Input System behind the
`VillageUI.KeyboardCaptured` guard; the cursor stays locked for the whole walk (the E-verb
ruling, `PlayerInteraction.cs` header).

**The collision shell.** Ground mesh + ~342 wall colliders + building boxes, built for "a person
needs walls" (`CityCollision.SolidifyWalls`). Figures and parked cars have **no colliders**;
car-vs-anything must be analytic.

**The crowd.** `Simulation` owns every agent; `GetAgent` returns struct copies, every mutator is
private — **nothing outside the sim can stop one person** (`Simulation.cs:373`). The `Stranded`
flag is the precedent for live state outranking the plan (`if (!_agents[i].Stranded)`,
`Simulation.cs:446`). Figures are re-posed from sim state every frame (`AgentMeshView.cs:293`),
so a body placed view-side would be snapped back — the body must be a sim fact. **No
fallen/lying/death clip exists among all 88** (`animations.txt`'s `asleep` row is deliberately
empty; the lowest pose is Kneeling Inspecting). A new `Activity` member requires an
`animations.txt` row in the same commit (`EveryActivityHasARowInTheRealFile`).

**The witness layer.** `PlayerTrack` is "the simulation's one piece of genuine history" — one
`Step {Tile, Visibly}` per sim minute, forward-only; its own header bans folding anything else
into it (`PlayerTrack.cs:36-52`). `Visibly` is a flags byte: `Carrying=1, Quickly=2, InCompany=4`.
The firewall pins all of it: exactly one file may name `Noir.Core.Witness`
(`WitnessFirewallTests`, `TheCaller = VillageHost.cs`), and no citizen/place id may appear in
`Noir.Core.Observation`'s public surface (`ObservationFirewallTests`). `Recollection` answers
"what did you see" by replaying the witness's pure-function DayPlan and checking each minute
against the track through `Sightlines` (stationary witnesses only, distance/light bands,
`BlurredMinute` by clarity) and `Degradation` (seeded band-shuffle, wrong-able at a glimpse,
the memory IS the seed). The player's physical description is hardcoded at the ask site
(`Recollection.cs:111`). **Nothing anywhere knows vehicles**: no description type, no `Visibly`
bit, no event verb — a witness today would describe a driven car as a 178 cm man moving Quickly.

## Out of scope for this pass

- **Police and the ambulance** — Phase 2 (IDEAS.md, Story). This pass only guarantees their
  input: the event record below.
- **NPC traffic seeing the player's car.** The five separation queries walk `_movers` only, so an
  NPC will drive through a stopped player car. The seam is identified for later — a second
  obstacle source those five queries consult, or a guarded not-driven-by-me `Mover` — and naive
  `_movers` injection is *known wrong*: `Drive()` would steer the car and `Retime()` would
  `SetActive(false)` it with the player inside (`CityTraffic.cs:983, 331-340`).
- **Solid parked cars** (the 611-solid-objects regression stands), **enterable lot cars** (baked),
  **standalone builds** (the whole car estate is editor-only today), **engine audio**, **damage**,
  and **the stolen car ever returning to its owner** — once taken, a car is loose for good in v1.
- **Sim-speed pinning while driving.** Driving runs on real time like all traffic; the hit sweep
  below is segment-vs-segment precisely so a fast sim clock cannot tunnel a pedestrian through
  the car's path.

## The change

### 1. `Player` gains a mode

`Player` grows `enum Mode { Afoot, Driving }` so `Where`, the P key, and the track recorder keep
one owner. Entering a car: deactivate the armature body, keep `OrbitCamera` disabled, keep the
cursor locked, drive `Camera.main` from the same pivot/back/SphereCast follow block with a longer
tether (~7 m). `Where` returns the car's position while driving, so the witness pipeline runs
unmodified. **P is ignored while driving** — E gets you out first; one exit path, not two.

### 2. `PlayerInteraction` grows the provider registry

The day the header scheduled has arrived. The door-specific lines in `Update` become a loop over
providers, each answering "nearest candidate within range, squared distance, and a factory."
Doors and cars are the two providers; closest wins; the cache key widens from `int` to
`(provider, index)`. `CarInteractable` copies `DoorInteractable`'s stateless `(owner, index)`
shape; its verb is `Drive`, rendered "E — Drive" by the existing prompt with zero UI work. While
driving, the prompt shows "E — Get out" (offered by the mode itself, not by proximity).

### 3. `CityDriveways` learns to give a car up

- `NearestCar(Vector3 from, float within)` → car index or -1, the exact mirror of
  `CityDoors.NearestDoor`, skipping inactive cars (their owners drove them to work). The provider
  calls this every frame with **no side effects**.
- `Take(int index)` → the car GameObject plus its captured appearance, **removing it from
  `_cars`/`_byHome`** so `Refresh` and the layer toggle stop owning it — called once, from
  `CarInteractable.Perform`, and safe by construction (`Refresh` skips null/missing slots).
- Appearance capture at `Create` time: the tone band (Dark/Mid/Light, read from the prefab's
  body material) and shape band (Car/Pickup/Van, from the prefab name), stored beside `_homeOf` —
  today that identity is discarded by the rename, and the witness layer needs it.
- The car keeps no collider (`CarMesh.Flatten`'s reserved `moving` parameter stays reserved —
  v1 collision is all analytic sweeps).

### 4. `PlayerCar` — the driving model

Kinematic and arcade, matching the fleet's own idiom: no Rigidbody, no WheelColliders (both
measured wrong for this town). Per frame on `Time.deltaTime`:

- throttle/brake-reverse on W/S, cap **12 m/s (~27 mph)** forward — NPC traffic does 8 — and
  4 m/s reverse; steer on A/D with yaw rate scaled by speed over cap (no pivoting in place);
- `y = ElevationGrid.HeightAt(x, villageY)` — the exact call parked and moving cars already use;
- a swept `Physics.BoxCast` (car half-extents) along the frame's travel against the static shell:
  walls, buildings, ground. Contact stops the car at the hit distance. Stop, not slide — v1.
- every key behind `VillageUI.KeyboardCaptured`.

### 5. The hit

Per frame, sweep the car's travel segment against each agent's movement segment (previous sim
position → current, cached across frames), closest-approach under car-half-width + a person
radius. **Sim positions, never figure transforms** (`AgentMeshView.Pick`'s own doc comment is the
design argument). Filters, each load-bearing: skip `Doing == AwayFromTown` (sim-anchored at their
own door while invisible), skip indoor tiles (or the car kills through walls), skip the already
`Downed`. ~1,380 iterations of 2D math per frame is microseconds. On contact the car hands
`VillageHost` plain data — `CitizenId` victim, `Vector3` impact — exactly the `Player.Where`
pattern; the car controller never names the witness assembly.

### 6. Core: `Activity.Downed` and `Simulation.Down`

- `Activity.Downed` joins the enum, **with its `animations.txt` row in the same commit** (the
  Core gate enforces the pairing). The row needs one imported hold-last-frame lying clip
  (Mixamo; non-looping — a looping Dying re-dies forever) and a `Noir > Build The Townsfolk
  Animator` rebuild.
- `public void Down(CitizenId who)` on `Simulation` — it must live inside because agent state
  leaves only as struct copies. Entry cleanup mirrors `Arrive` + `StandStill`: `Travelling=false`,
  release the path, zero `Heading`, clear talk state, `QueueSlot=-1` (queue eviction then happens
  by the existing slot-mismatch check). The tick loop's plan-overwrite guard extends the
  `Stranded` condition at `Simulation.cs:446`: a Downed agent's live state outranks the plan
  forever, so `Doing` stays `Downed`, the renderer freezes the body at the frozen position with
  held yaw, and "stays where it fell" is the pipeline's default behavior. The guard is a true
  no-op when nobody is downed — the fixture-village replays behind `watched.floor`'s ratchets
  must stay byte-identical.
- Three display sites learn the state in the same change: `VillageUI.Census` (or a downed agent
  reads "out" forever), the inspector's `Verb()` sentence, and `AgentMeshView.Report`'s clip
  census (its Stateless counter is the built-in alarm if the clip import is forgotten).

### 7. The record — Phase 2's contract

- `HitEvents`, a second forward-only store in `Noir.Core.Witness` beside `PlayerTrack` — the
  second legitimate "genuine history" (player-caused, underivable from the seed). A *list* of
  `{minute, Tile, CarTone, CarShape}` — multiple events in one minute are legal, which is why it
  is not a minute-keyed dictionary. Written only by `VillageHost.CarStruckSomebody(...)`, beside
  `RecordWhereThePlayerWas` — the firewall's one caller.
- **The victim's identity stays Sim-side** (the downed flag itself, plus a host-side
  victim-to-event pairing kept for Phase 2's police) — `ObservationFirewallTests` forbids ids in
  evidence, and a witness saw a figure go down, not a name.
- `Visibly` gains `InAVehicle = 8`. While driving, the track recorder sets it, and
  `Degradation.WhatRegistered` suppresses the person bands (height/build/age/clothing) when it is
  set — a witness saw a car, not a tall man running. The hit minute lands in the player's own
  track automatically, `Quickly` already tripping at any driving speed.

### 8. The testimony

- `CarDescription` in `Noir.Core.Observation`, under the vagueness doctrine: `ToneBand`
  (Unnoticed/Dark/Mid/Light — tone, never colour, never a make) and `ShapeBand`
  (Unnoticed/Car/Pickup/Van). Wide bands, `Unnoticed = 0` defaults, no ids anywhere.
- `EventSighting` beside `Sighting`: `{Minute, Tile, SightingClarity, EventAct, CarDescription}`
  where `EventAct` is a coarse stranger-nameable verb enum in `ObservedAct`'s house style —
  `CarStruckSomebody` is its first member. Legal by construction: Contracts types only.
- `Recollection` gains an event overlay: for each recorded event, the existing witness-gate
  arithmetic runs verbatim — replayed stationary witness, `Sightlines.HowGoodALook` at the event
  tile and minute, `SawAnythingAtAll`, `BlurredMinute` — so an event is witnessed by exactly the
  people the existing rules say could see that tile at that minute. Car bands degrade through
  the same seeded `(witness, minute)` rolls: two witnesses remember different halves, and a
  glimpse can be wrong.
- A new `IInterruptions`-style interface (the `INightWitnesses` pattern: Core states the
  question, the Unity layer answers from Sim) tells the replay a citizen is down from minute M —
  so a dead citizen neither testifies from their planned afternoon nor gets placed by any replay
  consumer after the hit.
- `Testimony` gets a second grammar arm — `"16:30, I saw a car hit somebody."` — same
  clock-prefix, clarity verbs, and under-claiming rules; delivered as extra lines in the same
  `string[]` from `AskWhatTheySaw`, so `VillageUI`'s ask panel changes not at all.

### 9. Testing

**Core (dotnet, Release):** `Down` semantics — a downed agent never moves, never has `Doing`
overwritten, is evicted from queues and conversations; determinism — the guard with an empty
downed set leaves the fixture villages byte-identical; `HitEvents` forward-only and
multiple-per-minute; `CarDescription`/`EventSighting` band rules and id-freedom (the firewall
tests extend automatically); the new `Testimony` arm's sentence shape; `Activity.Downed`'s
animations row (existing gate).

**PlayMode:** enter-drive-exit round trip (provider offers Drive, `PerformOffered` enters
Driving, scripted throttle moves the car, E exits and restores Afoot — with teardown restoring
the mode on every path, the shared-city trap); a scripted hit downs an agent and the body is
still there a sim-hour later; asking a nearby witness yields a line matching the extended
testimony shape. The two-clock sweep gets a test that runs the sim fast while the car crosses a
crowd tile.

**Look at it:** drive down Second Street, hit somebody, walk back, look at the body, knock on a
door and ask. The tests cannot see ugly, and none of this has ever been drawn.

## Decisions here that must not be quietly reversed

- **No physics vehicle.** The kinematic-Rigidbody halving is measured (`CarMesh.cs:44-55`).
- **Sweeps against sim positions, never figure transforms or colliders.**
- **The firewall stands.** Only `VillageHost` names the witness assembly; the car controller and
  everything else hand over plain data. If a better caller emerges, the test's name MOVES.
- **The vagueness doctrine covers cars.** Tone, never colour; shape band, never a model; a
  description that identifies one specific car is a design bug.
- **The victim stays in the population.** 1,300 people is load-bearing; every consumer indexes
  citizens by ordinal. Nobody is removed — they are frozen.
