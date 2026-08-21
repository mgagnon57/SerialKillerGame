# The response — police, a county car, and the ambulance that takes the body away

**The ask.** Drivable-car Phase 2, scoped by the owner 2026-08-15 (IDEAS.md, Story): "the police
respond to a vehicular accident, and an ambulance takes the body away after the investigation
completes." Confirmed in brainstorming the same day, with six rulings recorded here so no later
session re-litigates them:

1. **Discovery by sight starts the clock.** The response begins the first minute any citizen has
   line of sight to the hit or the downed body, under the same optics testimony runs on — a hit's
   witnesses raise it immediately, and a body on an empty street at 2 AM lies there until somebody
   passes. The player never triggers discovery.
2. **Local first, county investigates.** A Rossville officer reaches the scene first and holds it;
   a county sheriff's car then arrives from off-map up Route 1 (Danville is fifteen miles south)
   and runs the investigation; the ambulance comes last, from off-map north (Hoopeston — the town
   has no hospital, and the map already agrees). This is `POLICE-AND-INCIDENT.md`'s doctrine made
   mechanical: a four-officer department "does not patrol, it responds", and a body brings the
   county.
3. **Fully visible travel.** The officer walks from wherever his day has him; the county car and
   ambulance drive the lane graph in from the map edges and park at the scene. An arrival you can
   watch from a porch is the content.
4. **The investigation is a canvass that is recorded.** The county officer visits the doors of
   exactly the citizens who hold a sighting of this hit, and what each would answer — the same
   sentences the player gets from T — is recorded into a case file. Phase 2 does nothing with the
   findings; the file just becomes real.
5. **Severity decides the victim's fate.** At or above a speed threshold the victim is dead —
   absent forever, never deleted (1,300 is load-bearing). Below it they are hospitalized off-map
   and walk back into their plan days later, able to testify about life after their return.
6. **No player consequence in Phase 2.** The police never look for, at, or about the player. The
   scene, the canvass, the removal — the world reacting, nothing more. Consequences are their own
   phase, built on a case file that already exists.

**Architecture ruling, same session: Core decides, Unity draws.** The case state machine is
deterministic Core code ticked per sim minute, fed plain Contracts data by `VillageHost`, tested
by the standing Core gate. Unity renders its orders. This is how the whole town is built.

## What exists today (measured, with anchors)

**The input contract, ready.** `HitEvents` is a forward-only list of `{minute, Tile, CarTone,
CarShape}` — deliberately no victim id, no `PlaceId`; its own header says "Phase 2's police join
the two by minute and tile" (`HitEvents.cs:15`). The victim join is host-side:
`VillageHost._downedAtMinute` (`CitizenId.Value` → absolute minute), whose comment says "the sim
knows WHO, this knows WHEN, and Phase 2's police will want both" (`VillageHost.cs:1213`). Both are
private; `VillageHost.CarStruckSomebody` (`VillageHost.cs:1230`) is the one writer, and
`WitnessFirewallTests` pins `VillageHost.cs` as the only file that may name `Noir.Core.Witness`.
All minute stamps are ABSOLUTE minutes (day × 1440 + minute-of-day); mixing in minute-of-day
misfilters by whole days with no error.

**The downed state.** `Simulation.Down` freezes a citizen where they fell, consumes no RNG, and
the tick loop skips them wholesale (`Simulation.cs:388, 476`). `Simulation.Revive`
(`Simulation.cs:436`) clears the flag and resets `Destination` so the citizen departs within the
minute — its doc names "the ambulance that will one day take the body away", but its semantic is
recovery, not removal: the person stands up and resumes their errands. Nothing in Unity calls it,
and nothing ever clears `_downedAtMinute`, so a downed citizen currently testifies to nothing
forever (`IInterruptions.DownedFromMinute`, `Interruptions.cs:16`, consulted by both
`Recollection` arms).

**No trigger exists.** Witnesses only speak when asked (T); nothing watches `HitEvents`; no NPC
ever reports anything to anyone. **No investigation exists.** `AskWhatTheySaw`
(`VillageHost.cs:1167`) is the entire investigative surface of the game. **No NPC vehicle can
drive to a destination.** `CityTraffic` movers are a memoryless weighted random walk over the
Core `LaneGraph` — `Choose` draws a turn per junction off IDOT weights (`CityTraffic.cs:1374`);
there is no A→B planner, no tile→segment inverse, no stop-at-point (a `Hold.Arriving` member sits
reserved and never assigned, `CityTraffic.cs:272`). The only steered car ever built is the
player's, kinematic and outside the graph. **No off-plan tasking exists.** DayPlan is a pure
function of (seed, citizen, day); the sanctioned override is live state that outranks the plan —
exactly how `Downed` works — and the sim's agent array is fixed at construction
(`Simulation.cs:274`): no new person can be spawned at runtime.

**The standing assets.** A real precinct place at 660,1166 ("Rossville unit 2", the 1913 village
office; `city.txt:1327`), staffed today with a known-wrong 12 officers on one 24-hour window
against the owner's 2026-08-09 ruling — "four officers, TWO watches, and one on call from home
overnight… Build the wakeable state now even though nothing calls him yet" (`SIM-FIXES.md:440`).
`Car_Police_Modern` and `Car_Ambulance_Modern` prefabs exist in the pack, used only as parked
props and ~1-in-90 ambient scenery, all `#if UNITY_EDITOR`. Route 1 (Chicago St) reaches both map
edges, so the lane graph gives it off-map entry segments at both ends (`roads.txt:211`,
`LaneGraph.cs:293`) — arriving from off-stage already exists mechanically, in ambient form only
(`Reintroduce`, `CityTraffic.cs:1451`). `Pathfinder` is a public class over `world.Grid` that
paths between any two walkable tiles (`Pathfinder.cs:231`) — a Unity-side walker can own one.
"Officer" is not an enum member: precinct staff carry runtime-minted `Occupations.Of("officer")`
bytes; the stable handle is the kind's roles column, never a hardcoded byte.

## Out of scope for this pass

- **Any player consequence** — no suspicion, no arrest, no game-over, no officer perceiving the
  player. Ruling 6.
- **Town reaction beyond the response** — no gawkers, no talk of the accident, no mood. The
  "twelve hundred people talking" is its own future phase.
- **New testimony kinds.** Seeing the body, the cordon, or the ambulance is not evidence;
  `EventAct` keeps its one member. The canvass gathers testimony about the hit that already
  exists.
- **Moving-obstacle awareness.** Ambient traffic stays blind to response cars in motion, and
  response cars do not weave. Only the stationary seam lands (below).
- **Sirens, lights, audio** (headless runs must stay silent), **scene dressing** (pack cones are
  a polish item), **multiple simultaneous responses** (cases queue — a four-officer town runs one
  at a time), and **the county actors as sim citizens** (the agent array is fixed; they are
  Unity-side actors and cannot be struck by the car in v1).

## The change

### 1. `ResponseCases` — the case machine (Core)

A new Core assembly `Noir.Core.Response` referencing **Contracts only** (Observation's own
discipline). It owns every case, is ticked once per sim minute, and never predicts travel: it
emits orders, receives arrival reports, and runs its own dwell timers. Sketch of the seam:

```csharp
var cases = new ResponseCases();
cases.Open(victim, minute, tile, tone, shape, fatal);   // host, from CarStruckSomebody
cases.BodySeen(caseId, minute, discoverer);             // host's discovery scan
cases.OfficerArrived(caseId, minute); /* CountyArrived, WitnessVisited, AmbulanceArrived… */
foreach (var order in cases.Tick(minute)) { /* host executes */ }
```

States, with the v1 constants (named, tuned later by watching): **Open** (undiscovered) →
**Alarm** (discovery + 4 min — somebody gets to a phone) → **OfficerEnRoute** (order: dispatch an
officer to the scene tile; the machine knows Contracts only, so the HOST selects who per §6 and
reports it) → **SceneHeld** (officer arrival triggers: county dispatched, 18 min off-map)
→ **CountyEnRoute** (order: car from south entry to scene) → **Canvassing** (orders: walk the
county actor to each canvass door in turn, 5 min dwell each) → **InvestigationComplete** →
**AmbulanceEnRoute** (10 min off-map, then north entry to scene) → **Loading** (3 min) →
**BodyRemoved** (order: take the victim away; ambulance departs north, county car departs south,
officer released) → **Closed**. Every transition returns a `[case]` log line through the host —
the greppable record of the whole response.

The case file lives in the machine as plain data: hit minute/tile/tone/shape, victim, fatal,
discovery minute and discoverer, each canvassed witness with their recorded lines (fed in by the
host as strings), and every transition minute. Nothing reads it in Phase 2; it is the deliverable.

Resilience rules: cases queue (one active response; the next opens when the last closes). The
responders are citizens and can themselves be struck — a downed officer's case re-dispatches the
next available officer, and his own hit opens a case that queues like any other. A case whose
victim state the sim can no longer justify closes loudly rather than wedging.

### 2. Discovery (Core, Witness assembly)

A pure function beside `Recollection`: given the world, the population, a snapshot of live
citizen positions (plain `(CitizenId, Tile)` data — Witness cannot and must not reference Sim),
and the body's tile, return who — if anyone — can see it this minute, using the **same
`Sightlines` gates testimony uses** (distance and light bands, indoor, asleep, downed). One
optics, two consumers: if a witness could testify to the hit, the same person discovers the body.
`VillageHost` runs the scan once per sim minute for each undiscovered case, because it is the one
legal Witness caller.

### 3. Sim mutations: `Respond`/`Release` and `TakeAway` (Core, Simulation)

Built exactly like `Down`/`Revive` — live state that outranks the plan, no RNG consumed,
byte-identical town when never used (the determinism guards get siblings):

- **`Respond(CitizenId officer, Tile scene)`** — the first off-plan journey in the project. A live
  assignment routes the officer to a **raw tile** (the journey machinery today targets `PlaceId`s;
  the pathfinder already takes tiles — this adds the live-state tile-journey path), then stands
  him there with a new **`Activity.Responding`**, appended at the enum's end with its
  `animations.txt` row in the same commit and a `Noir > Build The Townsfolk Animator` rebuild
  (the Core gate enforces the pairing). Works from any activity — `Asleep` included: the overnight
  on-call officer wakes at home and goes. **`Release(officer)`** clears the state and resets
  `Destination`, Revive's own trick, so he resumes his interrupted day within the minute.
- **`TakeAway(CitizenId victim, int returnMinute)`** — clears `Downed`, sets an absent live state
  the renderer treats like `AwayFromTown` (figure root deactivated; `AgentMeshView.cs:227`'s
  downed arm gets a sibling). `int.MaxValue` means dead: in the population forever, in the world
  never — which honors "nobody is removed, they are frozen"
  (`2026-08-15-drivable-car-design.md:221`). A survivor's return minute — v1 constant **3 days
  after the hit** — places them at home with `Destination = None`, and the plan machinery walks
  them back into their life. The hit sweep and the discovery scan both skip taken-away citizens.

**Severity** rides the existing seam: `CarStruckSomebody` gains the impact speed (`DriveStep`
already knows `_carSpeed`), and a named Core constant — v1 **8 m/s (~18 mph)** — decides fatal.
`HitEvents` is **not** widened: whether the victim died is not something a stranger saw from the
kerb; fatality is sim-side fact, stored with the victim pairing.

### 4. The testimony interval (Core, Witness)

`IInterruptions` grows a second question: when did this citizen come back (`int.MaxValue` =
never). Both `Recollection` arms treat the silenced window as `[downedFrom, backFrom)`: the dead
case is the current behavior, kept — dead men tell no tales, and the never-cleared
`_downedAtMinute` was that design by accident. A survivor testifies about their day up to the hit
minute (not the hit itself — it came out of nowhere), nothing while absent, and everything after
their return. The host's pairing becomes interval-shaped and supports a citizen hit twice.
Core tests pin both edges of the window.

### 5. `LaneRoutes` — the route planner (Core, World)

Beside `LaneGraph`: Dijkstra over the directed segments and turns (a few hundred nodes — cost is
nothing), plus a nearest-segment-to-tile inverse built on the existing `RoadPath.Project`. Pure,
Core-tested. This is the piece `CityTraffic.Graph`'s "public because the bus routes will want the
same one" comment (`CityTraffic.cs:366`) has been waiting for. `Math.Sqrt` only — the
transcendental scan applies.

### 6. The precinct rota (Content + People) — a standing ruling lands

`kinds.txt`'s precinct row becomes the ruled reality: **4 jobs, two watch windows** (so
`shifts split` finally has two windows to split across), roles unchanged. The overnight answer is
the on-call officer at home, reachable because `Respond` works from `Asleep`. Selection order for
a dispatch: an on-watch officer (at the precinct or anywhere in town), else the on-call officer
woken at home. Building Phase 2 atop the known-wrong 12-officer staffing would bake in a fault
with a standing decision against it (`SIM-FIXES.md:440`).

### 7. `CityResponse` — the renderer (Unity)

A new MonoBehaviour beside `CityTraffic`, executing the machine's orders:

- Spawns the county car at Route 1's **south** entry segment and the ambulance at the **north**
  (Danville and Hoopeston are real directions), drives each along its `LaneRoutes` plan using the
  arc math `CityTraffic` already exposes for outsiders (`PointOn`/`TurnArc`/`PointInTurn`,
  `CityTraffic.cs:419`), eases to a stop beside the scene tile, parks offset from the lane, and
  reports arrival. Departure is the route reversed, despawn at the edge.
- Response vehicles advance on **sim time** — metres per sim second, so fast-forward compresses
  the response like everything else and the case machine's minute world stays the only clock.
  They are **never in `_movers`**: `Retime` cannot garage a cruiser mid-response and the wander
  logic never steers them.
- While driving, a response car eases behind anything registered in the stationary-obstacle seam
  (below) and any mover ahead on its own segment — it does not weave, and ambient traffic stays
  blind to it in motion (documented hole, same class as Phase 1's).
- The county officer is a Unity-side actor — a pack figure walking scene→door→door via its own
  `new Pathfinder(world.Grid)` at citizen walk speed on sim time. The local officer needs no
  rendering: he is a real citizen, and `AgentMeshView` draws whatever the sim says he is doing.
- At `BodyRemoved` the victim's figure root deactivates via the `TakeAway` state — the loading
  dwell is the theatre, the state change is the mechanism.
- Prefabs follow the estate's current editor-only pattern rather than dragging the cast-manifest
  fix (PB-6/PB-7) into scope — but a player build prints
  `[response] N actors drawn, M invisible (editor-only prefabs)` so the gap is audible, never
  silent.

### 8. The stationary-obstacle seam (Unity, CityTraffic)

Phase 1 named the seam and deferred it ("a second obstacle source those five queries consult",
`2026-08-15-drivable-car-design.md:70`). Phase 2 lands the minimal version because the scene
otherwise reads as a ghost: one registry of **stationary** obstacles — the player's parked car,
parked response vehicles — consulted by `Blocked` (`CityTraffic.cs:1541`) so ambient cars queue
behind the scene instead of driving through it. Moving obstacles stay out of scope. Run the
traffic gate twice before believing any number it moves (the standing rule).

### 9. `VillageHost` glue

The host stays the one seam: `CarStruckSomebody` additionally opens a case (with speed → fatal);
the once-per-minute slot that already drives `Retime` and driveway refresh runs the discovery
scan, ticks `ResponseCases`, executes its orders (`Sim.Respond`/`Release`/`TakeAway`,
`CityResponse` drives and walks), and reports arrivals and canvass completions back. The canvass
list and its recorded answers route through the host because only it may name Witness: who holds
an `EventSighting` of this hit, and what `AskWhatTheySaw` returns for each. `_downedAtMinute`
grows into the interval-shaped case pairing of §4. No new file touches `Noir.Core.Witness`;
`WitnessFirewallTests`' caller list does not move.

### 10. Testing

**Core (dotnet, Release):** the transition table with its minute arithmetic — discovery→alarm
delay, dispatch on arrival not on schedule, dwell timers, queueing, the downed-officer
re-dispatch, the loud-close path; `LaneRoutes` (route found, nearest segment, no route);
`Respond`/`Release`/`TakeAway` semantics and their byte-identical-when-unused guards; the
testimony interval's two edges and the twice-hit citizen; discovery optics agreeing with
testimony optics (same witness set for the same scene); the severity threshold; `Responding`'s
animations row (existing gate enforces).

**PlayMode (one scenario, observe-don't-assume, teardown on every exit path via static fields):**
at high sim speed, hit a citizen in view of a witness; assert the `[case]` lines appear in order
— discovered, officer arrived, county arrived, canvass visited N doors, ambulance arrived, body
removed — and that the victim's figure is inactive at the end. Restore everything: revive or
return the victim, release the officer, despawn response actors.

**Look at it:** hit somebody on Chicago Street in the afternoon, then stand on a porch and watch
the whole thing arrive, work, and leave. Screenshot the held scene. The tests cannot see ugly,
and none of this has ever been drawn.

## Decisions here that must not be quietly reversed

- **Discovery and testimony share one optics.** If the sightline rules change, both move
  together; a body discoverable by someone who could not have testified to it is a bug.
- **The case machine never predicts travel.** Orders out, arrivals in, dwells timed internally.
  A duration constant hiding a travel assumption is how the minute arithmetic rots.
- **`HitEvents` stays exactly as Phase 1 left it.** No victim id, no fatality, no `PlaceId` —
  evidence carries what a stranger could see. Fatality and identity are sim-side facts.
- **The firewall stands.** `VillageHost.cs` remains the only file naming `Noir.Core.Witness`;
  `Noir.Core.Response` references Contracts only.
- **Nobody is removed from the population.** Dead means frozen and absent forever — the 1,300 is
  load-bearing and every consumer indexes by ordinal.
- **Response vehicles are not movers.** They run on sim time outside `_movers`; putting one in
  the ambient list hands it to `Retime` and `Choose`, both of which are wrong for it.
- **No RNG in the response path.** Down, Respond, TakeAway, the machine itself — deterministic
  from the hit and the town. If randomness is ever wanted (a slow dispatcher), it gets its own
  named substream, never a shared stream.
