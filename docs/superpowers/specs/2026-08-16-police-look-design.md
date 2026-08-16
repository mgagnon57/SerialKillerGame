# The Response Looks Like One: uniforms, the cruiser, the crowd, and the camera

**Owner asks (2026-08-16):** the investigating officer should look like police; villagers
should stand around an accident instead of walking over the body; the officer should arrive
in his squad car with the lights going; and leaving third person should keep the camera over
where the player was standing, not snap back across town.

**Approved design (owner, same day):** navy recolor for the uniform (no purchase);
the cruiser as a third response rig with a signal-pattern light bar; a deterministic gawker
ring on the existing `Respond` mechanism; `Player.Leave()` hands the orbit camera its pivot.

## Facts this design stands on (measured 2026-08-16)

- **No police figure exists in the pack.** All 81 people prefabs enumerated; the sets are
  fantasy/farm/Slavic/steampunk. The county officer is hardcoded to
  `Man_Slavic_Summer_Hair.prefab` (`CityResponse.cs:331-333`); the Rossville officer is
  whatever citizen the precinct roster supplies, dressed by `AgentBody.Build`'s hash pick
  (`AgentBody.cs:206-212`) — no pinning mechanism exists.
- **The police car exists and is already in use:** `Car_Police_Modern.prefab`
  (`Prefabs/Cars/Cars City/`) is the county rig (`CityResponse.PrefabOf`, :497-508). Its
  light bar is baked into `car-police-modern-base` — NOT a separate child; flashing must be
  added geometry, not a toggled part. A police car is also parked at the precinct as set
  dressing (`CityBuildings.cs:540-541`).
- **The blinking-light precedent is complete:** `CitySignals` lights the pack's own lens
  submeshes with one URP-Unlit material instance and per-renderer `MaterialPropertyBlock`
  HDR `_BaseColor` writes (change-only), phased off the town clock (`TownSeconds`), plus one
  small distance-gated point `Light` per head for the spill (`CitySignals.cs:510-529,
  555-560, 632-670`). Anything that lights up stays its own renderer — the chunker spares
  materials named `Night`/`Emission` (`CityChunker.cs:281-293`); response cars are outside
  the bake anyway.
- **The garment recolor is documented tech:** `Universal_A_Alb` is a 32×32 swatch atlas,
  one garment role per row, columns 0-19 a shade ramp; shifting a garment's UVs along its
  row recolors that garment only, and `AgentBody` already recolors per citizen
  (`docs/ASSETS.md:274-287`).
- **`Respond` generalizes per agent:** `Simulation.Respond/RespondTick/Release`
  (`Simulation.cs:526-562, 612, 1148-1195`) walks any citizen to an arbitrary tile off-plan
  and stands them there; `RespondTarget` is per-agent. The Activity enum ends
  `..., Talking, Downed, Responding` — positional, append-only — and
  `EveryActivityHasARowInTheRealFile` gates any new member on a same-commit
  `Content/animations.txt` row.
- **Nothing reacts to a body today:** passersby path straight through its tile; no
  avoidance exists anywhere in `Pathfinder`/`Advance`.
- **The camera snap:** `Player.Leave()` (`Player.cs:428-436`) re-enables `OrbitCamera`
  wherever it last sat; nothing hands it the player's street position.

## 1. The uniform — a navy recolor, both officers

- A `PoliceBlue` garment treatment: shirt/coat and trousers shifted to navy on the
  officer's figure via the atlas row ramp, riding `AgentBody`'s existing per-citizen
  recolor seam.
- **Every precinct worker gets it at body-build time** — the officer is uniformed all day
  (at work, walking, standing over a body), not just while responding. The seam is
  `AgentBody.Build`: when the citizen's workplace is the precinct, apply the treatment
  after the hash pick. The hash pick itself is untouched — same figure, navy clothes —
  so no other citizen's casting shuffles.
- **The county officer actor** gets the same treatment, and his prefab constant moves off
  `Man_Slavic_Summer_Hair` to a plainer adult male figure. Constraint (documented at
  `CityResponse.cs:337-345`): the replacement prefab must ship its own Animator with a
  bound avatar, or he slides in bind pose.
- Editor-only, like all casting (`AssetDatabase`); a shipped build still gets capsules.
  That is the standing PB-6/PB-7 gap, not this feature's problem.

## 2. The cruiser — a third rig, lights going

- **`Rig.Cruiser`** joins County and Ambulance in `CityResponse`, wearing the same
  `Car_Police_Modern` prefab. It lives parked at the precinct (the set-dressing police car
  becomes the real one, or stands beside it).
- **On `DispatchOfficer` during watch hours** (the picked officer's live
  `Doing == AtWork`): the officer citizen **boards** — a new sim pair, `Board(who)` /
  `Alight(who, Tile at)`. `Board` requires `Responding`, presents the agent as
  `AwayFromTown` (reusing every existing not-drawn/census/sweep-skip arm) behind a new
  `Aboard` flag; `Alight` places them at the given tile (the `Return`-at-the-door
  precedent), restores `Doing = Responding`, and keeps `RespondTarget` so `RespondTick`
  walks the last few metres from the curb to the body. No RNG, forward minutes only,
  lockstep-neutral when unused — the `TakeAway` test suite's shapes apply.
- The cruiser drives precinct → scene on the lane graph exactly as the county car does
  (same Patience, same obstacle registration); at its stop the host calls `Alight` at the
  nearest walkable curb tile. The case machine is untouched — `OfficerArrived` still
  means "the officer citizen stands at the scene tile," however he got near it.
- **The overnight on-call man still walks** from his bed — no cruiser at his house, and a
  man hurrying up a dark street is worth keeping.
- **The light bar**: two small box meshes on the cab roof (added geometry — the baked bar
  cannot be toggled), each its own renderer wearing the signals' unlit material; a blinker
  phased off the town clock alternates HDR red `(3.0, 0.10, 0.06)` and HDR blue
  `(0.1, 0.4, 3.0)` between the two boxes, `DeadLens`-dark when off; one distance-gated
  point light per bar carries the spill. Bars run from dispatch until the rig leaves or
  parks back home. **The county car and the ambulance get the same bar** — same prefab
  family, same mechanism, one implementation.

## 3. The gawkers — a ring on the Respond mechanism

- `Respond` grows an optional stand-as parameter: `Respond(who, tile, Activity standAs =
  Activity.Responding)`, stored per-agent (`AgentState.StandAs`), used by `RespondTick` and
  the `Arrive` guard where they currently hardcode `Responding`.
- New Activity **`Gawking`**, appended after `Responding` (positional rule), with a
  same-commit `animations.txt` row built ONLY from clips the controller already carries
  (`Standing Idle`, `Looking Around`, `Bored`, `Talking`, `Weight Shift`) — no animator
  rebuild. `VillageUI.Verb`: "watching". Census: absorbed by `out`, correctly.
- **The host picks the crowd, deterministically, RNG-free** (the response path consumes no
  RNG — standing ruling): from case discovery until Loading, once a sim-minute, if the
  ring has vacancies, take the nearest eligible citizens — within ~80 m, not the victim,
  not Downed/Aboard/Away/Responding/Gawking, not Asleep; adults and children alike — up to
  **six**, one or two per minute so the crowd accretes rather than teleports.
- **Ring tiles**: distinct walkable tiles 2-4 tiles out from the scene, reachability
  answered by `Regions` before dispatch (exact-tile arrival strands nobody).
- **Dispersal**: on `VehiclesLeave` (the ambulance pulling away) every gawker is
  `Release`d back to their plan. `CloseLoudly` and the PlayMode teardowns release them the
  same way they release the officer.
- Out of scope, recorded: passers-through still path over the body tile (avoidance is new
  Pathfinder mechanics); a `Watching` moment kind for the texture economy (adding an
  `ObservedAct` verb voids every `watched.floor` line and forces the full three-seed
  re-measure — its own decision another day).

## 4. The camera — leave third person over the scene

- `OrbitCamera` gains `ArriveOver(Vector3 world)`: pivot moves to the given point; zoom
  clamps into a readable band (roughly 40-120 m — close enough to see figures, far enough
  to frame the street) only when outside it; pitch untouched.
- `Player.Leave()` calls it with the body's position (on foot) or the car's (if the player
  steps straight out of driving to overview). Entering third person is unchanged.

## Testing

- **Core**: `BoardTests` cloned from `TakeAwayTests`' shapes (round trip, byte-identical
  lockstep when unused, alight-places-at-tile, a boarded citizen cannot be hit);
  `RespondTests` additions for the stand-as parameter and `Gawking`'s row gate.
- **PlayMode**: the response scenario grows assertions — during OfficerEnRoute on watch
  the officer's `Doing` passes through `AwayFromTown` (aboard) and ends `Responding` at
  the scene; during Canvassing at least one citizen is `Gawking`; after close, nobody is.
  A camera test: walk, teleport, `Leave()`, assert the orbit pivot lands within a few
  metres of where the body stood. Traffic numbers untouched, but the standing two-run rule
  applies to the full gate regardless.
- **Look at it**: one staged dusk hit watched end to end — navy officer, lit cruiser
  pulling up, the ring of neighbours, stills into `docs/snapshots/`.

## Out of scope

Body-avoidance pathing; the `Watching` observed-verb; the ~$50 Modular City People
purchase (backlogged in `docs/ASSETS.md` as the uniform upgrade path); shipped-build
figure casting (PB-6/PB-7 owns it).
