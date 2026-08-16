# The Response Looks Like One — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Uniformed officers, a cruiser that arrives with its light bar going, a ring of
gawkers around the scene, and a camera that stays over the scene when the player leaves
third person.

**Architecture:** Core grows one small pair (`Board`/`Alight`) and one parameter
(`Respond`'s stand-as activity, backing the new `Gawking` member); everything else is
Unity-side choreography on existing seams — `AgentBody`'s cast/dress path, `CityResponse`'s
rig machinery, `CitySignals`' lens-lighting pattern, `OrbitCamera`'s `_target`. The case
machine is untouched.

**Tech Stack:** C# 9 / netstandard2.1 Core (dotnet test -c Release), Unity 6000.3.20f1.

**Spec:** `docs/superpowers/specs/2026-08-16-police-look-design.md` — the measured facts
(no police figure in the pack; `Car_Police_Modern` already in use; the signals' blink
pattern; `Respond`'s per-agent generality) all live there.

## Global Constraints

- **Core gate:** `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj`,
  baseline **561/0, 8 skipped**. Never Debug.
- **PlayMode gate:** 32 of 32, 1 skipped, ~596 s, editor CLOSED first (kill only your own
  instance; if the owner's is open, say so). Run TWICE at the end (traffic discipline).
- **Builds:** `dotnet build Noir.Unity.csproj / Noir.PlayTests.csproj -c Debug` after each
  Unity-side task. New `.cs` files under Assets need a Unity open to generate `.meta` +
  regenerate csprojs before `dotnet build` sees them.
- **Activity enum is positional, append-only**; a new member needs its lowercase row in
  `Content/animations.txt` in the SAME commit (`EveryActivityHasARowInTheRealFile`), one
  row = one line, and needs no controller rebuild if the row only names clips other rows
  carry.
- **The response path consumes no RNG** — the gawker picker must be deterministic
  (nearest-first, citizen-index tiebreak).
- **No `git add -A`;** commit messages end with the Claude co-author line.
- The sim runs on the town clock (one-clock ruling); any new blink phases off
  `TownSeconds`/`Sim.Clock.Tick`, never `Time.time`.

## File Structure

| File | Change |
|---|---|
| `Assets/Noir/Core/Sim/Simulation.cs` | `Board`/`Alight`, `AgentState.Aboard`+`StandAs`, `Respond(who, tile, standAs)`, gates |
| `Assets/Noir/Core/People/DayPlan.cs` | `Activity.Gawking` appended after `Responding` |
| `Content/animations.txt` | `gawking` row (existing clips only) |
| `Assets/Noir/Unity/VillageUI.cs` | `Verb`: `Gawking` → "watching" |
| `Assets/Noir/Unity/AgentBody.cs` | `PoliceCells` remap + pinned officer figure for precinct workers |
| `Assets/Noir/Unity/AgentMeshView.cs` | passes `uniformed` into `AgentBody.Build` |
| `Assets/Noir/Unity/CityResponse.cs` | `Rig.Cruiser`, `PlanRouteFrom`, `CruiserOut/CruiserHome`, light bars on all rigs, county officer prefab swap |
| `Assets/Noir/Unity/VillageHost.cs` | DispatchOfficer boards/alights; gawker picker + release; precinct spot |
| `Assets/Noir/Unity/OrbitCamera.cs` | `ArriveOver(Vector3)`, `public Vector3 Target` |
| `Assets/Noir/Unity/Player.cs` | `Leave()` calls `ArriveOver` |
| `tools/Noir.Core.Tests/BoardTests.cs` | create |
| `tools/Noir.Core.Tests/RespondTests.cs` | stand-as additions |
| `Assets/Noir/PlayTests/ResponsePlayTests.cs` | cruiser/gawker assertions + teardown |
| `Assets/Noir/PlayTests/PlayerPlayTests.cs` | camera-handoff test |

Task order: 1→8. Tasks 1–2 are Core-only; 3–7 Unity; 8 gates/look/docs/push.

---

### Task 1: `Board` / `Alight` — the officer vanishes into the car (Core)

**Files:**
- Modify: `Assets/Noir/Core/Sim/Simulation.cs`
- Test: `tools/Noir.Core.Tests/BoardTests.cs` (create, cloning `TakeAwayTests`' fixture shapes)

**Interfaces:**
- Produces: `public void Board(CitizenId who)` (requires `Responding`, not already aboard),
  `public void Alight(CitizenId who, Tile at)`; `AgentState` gains `public bool Aboard;`.
  Task 5 calls both from the host. `Release` learns to clear `Aboard` (the teardown/
  CloseLoudly path).

- [ ] **Step 1: Write the failing tests** (Queueham fixture, seed 1979, `TakeAwayTests`' shapes):

```csharp
[Test] public void ABoardedOfficerIsNotDrawnNotHitAndDoesNotWalk()
    // Respond(citizen 1, tile); Board(1). Assert Doing == AwayFromTown, Aboard true;
    // tick 10 sim minutes: Position unmoved (RespondTick never ran).

[Test] public void AlightPlacesThemAtTheKerbAndTheyWalkOn()
    // Board as above; Alight(1, kerbTile ~10 tiles from scene). Assert Position ==
    // CentreOf(kerbTile), Doing == Responding, Aboard false; within 15 sim minutes
    // Position.ToTile() == RespondTarget (RespondTick resumed and finished the walk).

[Test] public void ReleaseWhileAboardClearsEverything()
    // Board, then Release. Assert Aboard false, Responding false; within ten sim
    // minutes Doing is a plan activity again (the teardown path can never leak a ghost).

[Test] public void BoardRequiresRespondingAndIsIdempotent()
    // Board on an un-Responding citizen is a no-op; double Board is a no-op;
    // Alight on the un-boarded is a no-op.

[Test] public void NobodyBoardedIsByteIdenticalToBefore()
    // The lockstep clone (two same-seed sims, one hour, compare Position+Doing per
    // agent) — the new tick-loop gate is a true no-op when Aboard is never set.
```

- [ ] **Step 2: Run to verify failure** — filter `FullyQualifiedName~BoardTests`. Expected:
  compile error, `Board` not defined.

- [ ] **Step 3: Implement.** In `AgentState`, after `RespondTarget` (`Simulation.cs:126`):

```csharp
        /// <summary>Riding a response vehicle: present in the population, absent from the
        /// world, exactly as AwayFromTown draws — set only by Simulation.Board, cleared by
        /// Alight or Release. Meaningless unless Responding.</summary>
        public bool Aboard;
```

In the tick loop, DIRECTLY BEFORE the `Responding` gate (`Simulation.cs:612`):

```csharp
            if (_agents[i].Aboard) continue;   // riding: the car moves, they do not
```

Beside `Release` (`Simulation.cs:556`):

```csharp
        /// <summary>Into the cruiser. Requires Responding (only a dispatched officer ever
        /// rides) — the walk stops where it stands and the agent presents as AwayFromTown,
        /// reusing every not-drawn/census/sweep-skip arm the commuters already exercise.
        /// Consumes no RNG.</summary>
        public void Board(CitizenId who)
        {
            int i = who.Value;
            if (!_agents[i].Responding || _agents[i].Aboard) return;
            _agents[i].Travelling = false;
            ReleasePath(i);
            StandStill(i);
            _agents[i].Aboard = true;
            _agents[i].Doing = Activity.AwayFromTown;
        }

        /// <summary>Out at the kerb. Places them at the tile (Return's own re-entry
        /// precedent) and hands them back to RespondTick, which walks the last stretch to
        /// RespondTarget. Consumes no RNG.</summary>
        public void Alight(CitizenId who, Tile at)
        {
            int i = who.Value;
            if (!_agents[i].Aboard) return;
            _agents[i].Aboard = false;
            _agents[i].Position = Vec2.CentreOf(at);
            _agents[i].PreviousPosition = _agents[i].Position;
            _agents[i].At = PlaceId.None;
            _agents[i].Doing = Activity.Responding;
        }
```

`Release` gains, before its existing body's `Responding` check returns: clear the ride too —

```csharp
        public void Release(CitizenId who)
        {
            int i = who.Value;
            if (!_agents[i].Responding) return;
            _agents[i].Aboard = false;           // a released rider reappears where boarded
            _agents[i].Responding = false;
            _agents[i].Destination = PlaceId.None;
        }
```

And `Down` gains a guard at its top: `if (_agents[i].Aboard) return;` (a body inside a car
cannot be struck on the street). Update `Down`'s "four external mutations" doc sentence to
count Board/Alight.

- [ ] **Step 4: Run the new tests, then the FULL Core gate** — `TakeAwayTests`,
  `RespondTests`, `DownedTests` all stay green. Expected total 561 + 5.
- [ ] **Step 5: Commit** — `Sim.Board/Alight: the officer rides — present in the town, absent from the street`.

---

### Task 2: `Respond` stands you as anything — and `Gawking` exists (Core + Content + UI)

**Files:**
- Modify: `Assets/Noir/Core/Sim/Simulation.cs`, `Assets/Noir/Core/People/DayPlan.cs`,
  `Content/animations.txt`, `Assets/Noir/Unity/VillageUI.cs`
- Test: `tools/Noir.Core.Tests/RespondTests.cs` (extend)

**Interfaces:**
- Produces: `public void Respond(CitizenId who, Tile scene, Activity standAs =
  Activity.Responding)`; `AgentState` gains `public Activity StandAs;`;
  `Activity.Gawking` appended after `Responding`. Task 6's picker calls
  `Respond(who, ringTile, Activity.Gawking)`.

- [ ] **Step 1: Write the failing tests** (in `RespondTests`):

```csharp
[Test] public void AStandAsActivityIsWornOnArrival()
    // Respond(citizen 1, tile, Activity.Gawking); tick until arrival; assert
    // Doing == Activity.Gawking, not Responding.

[Test] public void TheDefaultStandAsIsRespondingUnchanged()
    // Respond(citizen 2, tile); arrival wears Activity.Responding — the officer's
    // behavior does not move.

[Test] public void GawkingHasARowInTheRealFile()
    // EveryActivityHasARowInTheRealFile covers this globally; this pins it locally the
    // day the row is touched: read Content/animations.txt from RepoRoot(), assert a line
    // starts "gawking" and every clip it names also appears on some OTHER row (the
    // no-controller-rebuild guarantee, asserted rather than promised).
```

- [ ] **Step 2: Run to verify failure.**
- [ ] **Step 3: Implement.**
  - `DayPlan.cs`: append after `Responding`:

```csharp
        /// <summary>Standing in the loose ring around somebody else's misfortune. Live
        /// state set only by Simulation.Respond's standAs parameter; the tick loop never
        /// writes it. At the end of the enum — values are positional.</summary>
        Gawking,
```

  - `Content/animations.txt`, beside the `responding` row (one line, clips all carried by
    other rows already):

```
gawking      Standing Idle, Looking Around, Weight Shift, Bored, Talking
```

  - `Simulation.cs`: `AgentState` gains `public Activity StandAs;`; `Respond` signature
    grows the default parameter and sets `_agents[i].StandAs = standAs;`; the three places
    that hardcode the stand (`RespondTick` at :1155-1156, :1167-1168, :1183-1184 — the
    `Doing != Activity.Responding` pattern) and the `Arrive` guard compare/write
    `_agents[index].StandAs` instead. `Alight` (Task 1) writes `StandAs` too, not the
    literal.
  - `VillageUI.cs` `Verb`: `case Activity.Gawking: return "watching";` after the
    `Responding` case.
- [ ] **Step 4: Run the new tests + `EveryActivityHasARowInTheRealFile` + the FULL gate.**
- [ ] **Step 5: Commit** (enum + row together) — `Sim.Respond stands you as anything; Gawking is what a crowd does`.

---

### Task 3: The uniform — pinned figure, navy cells (Unity)

**Files:**
- Modify: `Assets/Noir/Unity/AgentBody.cs`, `Assets/Noir/Unity/AgentMeshView.cs`,
  `Assets/Noir/Unity/CityResponse.cs:331-333`

**Interfaces:**
- Produces: `AgentBody.Build(parent, who, look, bool uniformed = false)`;
  `AgentMeshView.Build` passes `uniformed: true` for citizens whose workplace is the
  precinct. `CityResponse.OfficerPrefab` repoints to the pinned officer figure.

**Design locked here:** a uniform means police look ALIKE — precinct workers bypass the
hash pick and all wear ONE pinned figure per sex, with shirt+trouser atlas cells remapped
to navy. The garment cells are found empirically once and hardcoded.

- [ ] **Step 1: Find the cells.** Editor probe (RunCommand or a throwaway editor script —
  not committed): for the candidate figures (start `Man_Slavic_Summer_Hair`, and the
  plainest of `Women()`), dump each SkinnedMesh's DISTINCT uv cells (`floor(uv * 32)`) with
  vertex counts, and sample `Universal_A_Alb` at each cell centre for its color. Choose the
  shirt and trouser cells (largest non-skin-toned clusters); choose one navy cell from the
  atlas's blue swatches. Record all of it in the `PoliceCells` doc comment — the probe is
  disposable, the numbers are not.
- [ ] **Step 2: Implement the remap.** In `AgentBody`, beside `Dressed`:

```csharp
        /// <summary>Atlas cells (x,y in the 32-grid) that make this figure a uniform:
        /// source garment cell -> the navy cell. Found empirically 2026-08-16 (probe in
        /// the plan, Task 3 Step 1); per pinned figure, not per cast. A cell pair here is
        /// a FACT about one mesh — do not reuse across models.</summary>
        private static readonly (Vector2Int from, Vector2Int to)[] PoliceCells = { /* Step 1's numbers */ };

        private static Mesh Uniformed(Mesh source, CitizenKey key)
        {
            // Dressed()'s clone-and-shift shape, but a targeted cell REMAP and no
            // per-citizen shift: a uniform is the same coat on every officer.
            // For each vertex: cell = floor(uv * 32); if cell matches a PoliceCells.from,
            // uv = (to + fract(uv * 32)) / 32. Cache per (model, "police") like _dressed.
        }
```

  `Build` gains `bool uniformed = false`; when set: skip the hash pick for the pinned
  figure path (`Men()[0]`-style named constant per sex, chosen in Step 1), and use
  `Uniformed(...)` instead of `Dressed(...)`.
- [ ] **Step 3: The caller decides who is police.** In `AgentMeshView.Build`'s citizen
  loop, resolve the precinct once (`PlaceKindTable.Current.TryKindOf("precinct")` →
  `World.PlacesOfKind` → first id, the `WhoIsOnDuty` pattern at `VillageHost.cs:1581`) and
  pass `uniformed: who.Work == precinctId`.
- [ ] **Step 4: The county man matches.** `CityResponse.OfficerPrefab` repoints to the
  pinned male figure's prefab path, and `SendHimOut`'s instantiation applies the same
  `Uniformed` mesh swap (extract a small `AgentBody.UniformThisInstance(GameObject)` helper
  so the logic exists once). Constraint check from `CityResponse.cs:337-345`: the pinned
  prefab must ship Animator+avatar — verify in Step 1's probe, or keep a figure that does.
- [ ] **Step 5: Compile + look.** `dotnet build Noir.Unity.csproj -c Debug`; then a Play
  probe: photograph a precinct worker and the county man, confirm navy reads and skin/hair
  did not turn blue.
- [ ] **Step 6: Commit** — `AgentBody: the precinct wears navy — one pinned figure, two remapped cells`.

---

### Task 4: `Rig.Cruiser` — the third vehicle, from the precinct (Unity)

**Files:**
- Modify: `Assets/Noir/Unity/CityResponse.cs`

**Interfaces:**
- Consumes: `PlanRoute`'s candidate machinery (`CityResponse.cs:674` — edge entries swap
  for a nearest-lane start), `LaneRoutes.NearestSegment`, the `Car` class, `Dress`, `Seat`,
  `Park`, `Despawn`.
- Produces: `Rig.Cruiser` (enum grows a third member — `_cars` sizes off the enum, verify
  and fix any literal `2`), `public bool CruiserAvailable` (not currently out),
  `public void CruiserOut(Vec2 fromNear, Tile scene, System.Action<Vector3> onArrived)`
  (arrival passes the car's stop position so the host can pick the kerb tile), and
  `public void CruiserHome(Vec2 toNear)` (drives back toward the precinct and despawns at
  its stop — the set-dressing car at the precinct never moved, so nothing needs re-parking).

- [ ] **Step 1: `PlanRouteFrom`.** Clone `PlanRoute`'s shape with the edge-entry loop
  replaced by one start: `LaneRoutes.NearestSegment(graph, world.Roads, fromNear, out seg,
  out s)`; candidates near the destination unchanged. Reuse `MostPlans` and the arc-cost
  pick as-is.
- [ ] **Step 2: `CruiserOut`/`CruiserHome`** — `DriveIn`'s body with `PlanRouteFrom`, the
  cruiser slot, `PrefabOf(Rig.Cruiser)` = the same `Car_Police_Modern.prefab`, `NameOf` =
  "cruiser", OffStage fallback intact (an un-routable cruiser still "arrives" and the
  officer alights at the scene). `onArrived` passes `car.What != null ? car.What.position
  : SceneAt`.
- [ ] **Step 3: Compile; commit** — `CityResponse: the cruiser — a third rig that starts at the precinct, not the map edge`.

---

### Task 5: Dispatch boards the officer; the light bars go on (Unity)

**Files:**
- Modify: `Assets/Noir/Unity/VillageHost.cs` (DispatchOfficer arm, :1448-1467),
  `Assets/Noir/Unity/CityResponse.cs` (light bar)

**Interfaces:**
- Consumes: Task 1's `Board`/`Alight`, Task 4's `CruiserOut`/`CruiserHome`/`CruiserAvailable`.
- Produces: the choreography. The case machine sees nothing new — `OfficerArrived` still
  fires from the arrival poll when the officer citizen stands at the scene tile.

- [ ] **Step 1: The dispatch arm.** In `Execute`'s `DispatchOfficer` case, after
  `Sim.Respond(officer, order.Scene)` — reading `Doing` BEFORE `Respond` (its cleanup does
  not touch `Doing`, but read first anyway, beside `WhoIsOnDuty`'s own read):

```csharp
                    bool onWatch = wasDoing == Activity.AtWork;   // read before Respond
                    if (onWatch && Response != null && Response.CruiserAvailable)
                    {
                        Sim.Board(officer);
                        int caseId = order.Case;
                        Response.CruiserOut(PrecinctSpot(), order.Scene, onArrived: at =>
                        {
                            // Kerbside: the nearest walkable tile to where the car stopped;
                            // fall back to the scene tile itself (always walkable - somebody
                            // was standing on it when they were hit).
                            Sim.Alight(_officerWalking.TryGetValue(caseId, out var who) ? who : officer,
                                       KerbTileNear(at, _cases.SceneOf(caseId)));
                        });
                    }
```

  `PrecinctSpot()`: the precinct place's door as `Vec2` (the `WhoIsOnDuty` resolution,
  cached). `KerbTileNear(Vector3 world, Tile fallback)`: `Space3D.TileAt` + spiral out to
  the first walkable tile within ~4 tiles, else the fallback. The `ReleaseOfficer` arm
  gains `Response?.CruiserHome(PrecinctSpot())`; `VehiclesLeave` does not touch the
  cruiser (it leaves when its officer is released, not when the scene's rigs do).
- [ ] **Step 2: The light bar.** In `CityResponse.Dress`, after instantiation, attach to
  every rig: two small cubes (`GameObject.CreatePrimitive(PrimitiveType.Cube)`, colliders
  destroyed, ~0.28 × 0.09 × 0.12 m) side by side on the cab roof (position from the
  prefab's bounds top, found in the same Dress pass), each wearing ONE shared instance of
  the signals' unlit material shape (`Shader.Find("Universal Render Pipeline/Unlit")`,
  named `M_Response_Bar_Emission` — "Emission" in the name keeps it chunker-exempt by
  convention, though these cars never meet the chunker). In `CityResponse.Update`, beside
  the slicing, a blink pass: phase = `(_host.Sim.Clock.Tick / (double)GameClock.TicksPerSecond) % 1.0`;
  first half: box A wears HDR red `(3.0, 0.10, 0.06)`, box B the signals' `DeadLens`
  dark; second half swapped to HDR blue `(0.1, 0.4, 3.0)` on B. MaterialPropertyBlock
  `_BaseColor`, written on change only (the signals' Refresh discipline). One point light
  per car (`range 11, intensity 2.2, no shadows` — the head-lamp numbers), color following
  the lit side. Lights exist while the rig exists — dispatch to despawn.
- [ ] **Step 3: Compile; commit** — `The officer rides the cruiser in, and every response vehicle runs its bar`.

---

### Task 6: The gawkers (Unity, VillageHost)

**Files:**
- Modify: `Assets/Noir/Unity/VillageHost.cs` (RunResponse grows step 5), 
  `Assets/Noir/PlayTests/ResponsePlayTests.cs` (teardown)

**Interfaces:**
- Consumes: Task 2's `Respond(who, tile, Activity.Gawking)` and `Release`.
- Produces: `_gawkers` (`Dictionary<int, List<CitizenId>>` per case), released on close.

- [ ] **Step 1: The picker, in `RunResponse` after the machine's minute.** For every case
  in a state past `Undiscovered` and before `Closed`: if `_gawkers[c].Count < 6`, scan all
  agents for the nearest eligible one — within 80 m of the scene, not the victim, not the
  case's officer, not Downed/Aboard/Away/Responding/Gawking/Asleep — nearest-first,
  citizen-index tiebreak, ONE per sim-minute so the crowd accretes. Ring tile: from a
  fixed offset table (the 12 tiles at Chebyshev radius 2-3, in one declared order), the
  first walkable one not already taken by this case's ring; skip the minute if none.
  `Sim.Respond(who, ringTile, Activity.Gawking)`. Log once per pick:
  `[case] case N: citizen X drifts over`.
- [ ] **Step 2: Dispersal.** In `Execute`'s `VehiclesLeave` arm and in the `CloseLoudly`
  path: `Release` every id in `_gawkers[case]`, clear the list, log
  `[case] case N: the crowd loses interest`.
- [ ] **Step 3: Teardown honesty.** `ResponsePlayTests.EverythingBack` grows one loop:
  every agent whose `Responding` is true gets `host.Sim.Release(id)` — officers and
  gawkers alike, whatever the exit path.
- [ ] **Step 4: Compile; commit** — `The town gathers: six gawkers on the Respond mechanism, dispersing with the ambulance`.

---

### Task 7: The camera stays at the scene (Unity)

**Files:**
- Modify: `Assets/Noir/Unity/OrbitCamera.cs`, `Assets/Noir/Unity/Player.cs:428-436`

- [ ] **Step 1: `ArriveOver`.** In `OrbitCamera`:

```csharp
        /// <summary>Somewhere worth hovering: pivot to this point, zoom clamped into a
        /// band that frames a street (close enough for figures, far enough for context),
        /// pitch floored so the first frame looks DOWN at the scene rather than along the
        /// grass. Player.Leave hands us the body's position so stepping out of third
        /// person stays over whatever just happened there.</summary>
        public void ArriveOver(Vector3 world)
        {
            world.x = Mathf.Clamp(world.x, 1f, _host.World.Width - 1f);
            world.z = Mathf.Clamp(world.z, -(_host.World.Height - 1f), -1f);
            world.y = 0f;
            _target = world;
            _distance = Mathf.Clamp(_distance, 40f, 120f);
            _pitch = Mathf.Max(_pitch, 35f);
            Mode = ViewMode.Overview;
        }

        /// <summary>Read-only for the PlayMode gate: where the orbit is looking.</summary>
        public Vector3 Target => _target;
```

  Locate the follow mechanism (the lerp at `OrbitCamera.cs:486` — `want` comes from a
  follow field; grep `_follow`) and clear it in `ArriveOver`, or the next frame lerps the
  pivot straight back to whoever was being followed.
- [ ] **Step 2: The handoff.** `Player.Leave()` (`Player.cs:428-436`): before
  `_orbit.enabled = true`, capture `var at = _body != null ? _body.transform.position :
  transform.position;` and after enabling call `_orbit.ArriveOver(at);`.
- [ ] **Step 3: Compile; commit** — `Player.Leave hands the orbit camera the scene — stepping out stays over what you did`.

---

### Task 8: PlayMode, gates, look, docs, push

- [ ] **Step 1: PlayMode additions.**
  - `ResponsePlayTests.AWitnessedHitBringsTheTownsWholeResponse` grows three assertions,
    each observed rather than assumed: while the case is in `OfficerEnRoute`, poll the
    officer id (the machine's `OfficerOf`) — if their `Doing` ever reads `AwayFromTown`,
    the ride happened (record a bool; do NOT require it — the noon watch drives, but an
    off-watch fallback walking is not a failure); during `Canvassing`, assert at least one
    agent's `Doing == Activity.Gawking`; after `Closed`, assert none is.
  - New in `PlayerPlayTests`: `LeavingThirdPersonStaysOverTheScene` — toggle in, teleport
    the body 300 m from the current orbit target (the `ForceCloseBeatsProximityUntilItExpires`
    teleport pattern), toggle out, assert `Vector3.Distance(orbit.Target, bodyPos) < 10f`.
- [ ] **Step 2: Unity opens once** (metas + csprojs for `BoardTests.cs` etc.), then the
  three builds green.
- [ ] **Step 3: Core gate** — expect 561 + Task 1-2's additions.
- [ ] **Step 4: PlayMode gate TWICE**, editor closed. Expect 33 of 33 + 1 skipped + the
  camera test = 34 total, 1 skipped.
- [ ] **Step 5: LOOK AT IT.** One staged dusk hit, watched: navy officer, the cruiser
  pulling up with the bar going, the ring of neighbours, dispersal. Stills →
  `docs/snapshots/response-look-*.png`. The tests cannot see navy, a blink, or a crowd's
  shape — only this can.
- [ ] **Step 6: Docs + push.** CLAUDE.md baselines (Core count, PlayMode count) replaced;
  CONTROLS.md's response line mentions the cruiser and the crowd;
  `drivable-car-landed.md` memory updated. Push the branch.
