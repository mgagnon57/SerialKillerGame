# Car Collisions Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** About once a town-day, two of Rossville's own drivers crash — witnesses can testify
to it, the response machine runs a collision case with roadside interviews instead of a
canvass, and the law's verdict (DUI arrest and tow / ticket / let go) is a pure function of
the drivers' own day plans.

**Architecture:** A deterministic `CrashPlanner` in Core.People computes at most one crash per
day from the seed and the day plans — minute, scene, two drivers, severity, and the VERDICT,
stamped at plan time because the adjudication needs day-plan knowledge the Contracts-only
response machine must never see. `ResponseCases` gains a `CaseKind` and a collision arc
(interviews for canvass, adjudication orders, no ambulance/body for a bender). Witnesses hear
about it through a `CrashEvents` store — the third event kind, the `AskEvents` pattern
exactly. The host stages the crash with the drivers' own parked cars (`CityDriveways.Take`),
downs the at-fault driver on the rare injury crash, and executes the new orders with the
existing rigs (the arrest ride is the cruiser that already exists).

**Tech Stack:** C# — Noir.Core (Contracts/People/Witness/Observation/Response), Noir.Unity
(VillageHost, VillageUI, CityDriveways consumer), NUnit Core tests, one PlayMode scenario.

**Spec:** `docs/superpowers/specs/2026-08-18-car-collisions-design.md`

## Global Constraints

- **Planning-time randomness is hash-of-seed only** — `CrashPlanner` follows `DayPlanner`'s
  own idiom (`new Xoshiro256ss(Mix(seed, …, day))`, DayPlan.cs:172) and is a pure function of
  (seed, day, world, population): same inputs, same crash, any number of calls. **The
  response machine itself stays RNG-free** and Contracts-only — which is WHY the verdict is
  computed by the planner and handed to `OpenCollision` as data (`CrashVerdict` lives in
  Contracts so Response may carry it).
- **The firewall holds.** `Noir.Core.Response` references Contracts ONLY (`ResponseFirewallTests`
  pins it). New Contracts types: `CaseKind`, `CrashVerdict`. `CrashPlanner` lives in
  `Noir.Core.People` (which already references World and Contracts). `EventSighting` gains no
  fields; witnesses name nobody.
- **Histories run forwards**; `CrashEvents.Record` throws on a backwards minute exactly as
  `HitEvents`/`AskEvents` do.
- **Hit-line and ask-line testimony wording is pinned by tests and must not change.**
- **Orders are emitted exactly once; state checks are `>=` never `==`** (ResponseCases' own
  header invariants). `CaseOrder` keeps its single `Who` — the collision arc uses per-driver
  orders, never a second field.
- **An injured driver must be silenced as a witness**: the host writes the same
  `_victims`/`VictimRecord` interruption window `CarStruckSomebody` writes, or the downed
  driver keeps testifying (the corpse-testifies lesson).
- Core tests in Release: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj`.
  Baseline going in: **582 pass, 0 fail, 8 skipped.** Any red is a regression.
- **The generated csprojs are stale for NEW files** — before trusting any `dotnet build` of a
  task that CREATES a .cs file, hand-add its `<Compile Include>` to the relevant gitignored
  csproj (never commit the csproj) and verify with a grep. Unity regenerates them at next open.
- Do not edit any `.cs` while a batch Unity run is going. The year is 1991.

## File Structure

- Create: `Assets/Noir/Core/Contracts/Crash.cs` — `CaseKind`, `CrashVerdict`
- Create: `Assets/Noir/Core/Witness/CrashEvents.cs` — the third event store
- Modify: `Assets/Noir/Core/Observation/EventSighting.cs` — `EventAct.CarsCollided = 2`
- Modify: `Assets/Noir/Core/Observation/Testimony.cs` — the collision sentence
- Modify: `Assets/Noir/Core/Witness/Recollection.cs` — thread `CrashEvents` (the asks pattern)
- Create: `Assets/Noir/Core/People/CrashPlanner.cs` — the planner + adjudication
- Modify: `Assets/Noir/Core/Response/ResponseCases.cs` — `CaseKind`, `OpenCollision`, the
  collision arc (interviews, verdict orders, tow), `KindOf`
- Modify: `Assets/Noir/Unity/VillageHost.cs` — crash staging + new order arms
- Modify: `Assets/Noir/Unity/VillageUI.cs` — kind-aware ticker prose
- Tests: `tools/Noir.Core.Tests/` — `CrashEventsTests.cs` (new), `CrashPlannerTests.cs` (new),
  additions to `EventTestimonyTests.cs`, `ResponseCasesTests.cs`

---

### Task 1: The words in Contracts, the store in Witness, the sentence in Observation

**Files:**
- Create: `Assets/Noir/Core/Contracts/Crash.cs`
- Create: `Assets/Noir/Core/Witness/CrashEvents.cs`
- Modify: `Assets/Noir/Core/Observation/EventSighting.cs` (enum tail)
- Modify: `Assets/Noir/Core/Observation/Testimony.cs` (`InEnglish(EventSighting)`)
- Test: create `tools/Noir.Core.Tests/CrashEventsTests.cs`; add to `EventTestimonyTests.cs`

**Interfaces:**
- Produces: `Noir.Core.Contracts.CaseKind { PersonDown = 0, Collision = 1 }` and
  `Noir.Core.Contracts.CrashVerdict { LetGo = 0, Ticket = 1, Dui = 2 }` (both `: byte`, one
  file, doc comments in the house voice — Tasks 3, 4, 5 consume the exact names);
  `Noir.Core.Witness.CrashEvents` with `int Count`,
  `void Record(int minute, Tile where, CarTone tone, CarShape shape)`,
  `void ForEach(Action<int, Tile, CarTone, CarShape> visit)` (the `HitEvents` shape — Task 2
  and Task 5 rely on it); `EventAct.CarsCollided = 2`; the sentence
  `"16:30, I saw a dark van and another car come together."`

- [x] **Step 1: Write the failing tests.** `CrashEventsTests.cs` mirrors `AskEventsTests.cs`
  exactly (same three tests: two-in-one-minute kept, `TimeOnlyRunsForwards` throws
  `ArgumentException`, `ForEachReplaysInOrder`) with the four-argument `Record` — read
  `HitEventsTests.cs` and `AskEventsTests.cs` first and follow their fixture shape. In
  `EventTestimonyTests.cs`:

```csharp
/// <summary>
/// The collision spec's sentence: the at-fault car described through the same degradation
/// the hit line uses, the other car nameless — a witness saw an accident, not a report.
/// </summary>
[Test]
public void ACollisionSightingReadsLikeAWitness()
{
    var s = new EventSighting(new ObserverId(0), 990, new Tile(4, 4),
        SightingClarity.Partial, EventAct.CarsCollided,
        new CarDescription(CarTone.Dark, CarShape.Van));
    Assert.That(Testimony.InEnglish(s),
        Is.EqualTo("16:30, I saw a dark van and another car come together."));
}
```

- [x] **Step 2: Run to verify they fail** — filter
  `"FullyQualifiedName~CrashEventsTests|FullyQualifiedName~ACollisionSightingReadsLikeAWitness"`,
  expect compile errors (missing types).

- [x] **Step 3: Implement.** `Crash.cs` (namespace `Noir.Core.Contracts`): the two byte enums
  with doc comments saying what each value means and that Response carries them as data,
  never computes them. `CrashEvents.cs`: transcribe `HitEvents.cs` with the class renamed,
  the doc comment retold for collisions (no identity, forward-only, the third event kind),
  and the same rich exception message (`"A history runs forwards. Tried to record minute " +
  minute + " after minute " + last + "."` with `nameof(minute)` — HitEvents' actual form, not
  AskEvents' short one). `EventSighting.cs`: `CarsCollided = 2` appended after
  `SomebodyAskedQuestions`. `Testimony.cs`: the act switch gains
  `EventAct.CarsCollided => " and another car come together"` and the SUBJECT switch routes
  `CarsCollided` through `Car(e.Car)` exactly like `CarStruckSomebody` (read the
  subject-by-act structure Phase 2 left; hit and ask lines must come out byte-identical).

- [x] **Step 4: Run the testimony surface** — filter
  `"FullyQualifiedName~CrashEventsTests|FullyQualifiedName~EventTestimonyTests|FullyQualifiedName~TestimonyTests|FullyQualifiedName~EventSightingTests"` — ALL PASS.

- [x] **Step 5: Commit** (stage the five files):
  `The third kind of event: two cars come together`

---

### Task 2: `Recollection` carries crashes — the three-store merge

**Files:**
- Modify: `Assets/Noir/Core/Witness/Recollection.cs`
- Test: `tools/Noir.Core.Tests/EventTestimonyTests.cs`

**Interfaces:**
- Consumes: `CrashEvents` (Task 1); the existing two-store `WhatTheySawOfEvents` (hits + asks,
  `Look` local function, true-minute merge with `Stamp`).
- Produces: `WhatTheySawOfEvents(..., AskEvents asks = null, CrashEvents crashes = null)` and
  `AskInEnglish(..., AskEvents asks = null, CrashEvents crashes = null)` — one more trailing
  optional each; every existing call site compiles unchanged. Task 5 relies on `AskInEnglish`'s
  signature.

- [x] **Step 1: Write the failing tests** (in `EventTestimonyTests.cs`, the fixture-village
  conventions of `AWitnessRemembersSomebodyAskingQuestions` — read it first):

```csharp
[Test]
public void AWitnessRemembersTwoCarsComingTogether()
{
    // Same skeleton as AWitnessRemembersSomebodyAskingQuestions: find a stationary
    // citizen, record a crash at their own door tile (distance zero, on purpose), ask.
    // Assert exactly one line, containing "come together".
}

[Test]
public void CrashesMergeWithHitsAndAsksInMinuteOrder()
{
    // The three-kind merge: hit at m, ask at m+60, crash at m+120 (same stationary-twice
    // search widened to three stationary minutes, 60 apart — mirror the existing
    // stationary-pair scan). Assert 3 lines: "hit" first, "asking questions" second,
    // "come together" third.
}
```

Write these as REAL tests with the full search/record/assert code copied from the two
existing fixture tests' idiom — the skeleton comments above name the shape; the committed
tests contain no skeleton comments, only working code with the house-style doc comments.

- [x] **Step 2: Run to verify they fail** (compile error: no `crashes` parameter).

- [x] **Step 3: Implement.** In `WhatTheySawOfEvents`: a third collect pass
  (`crashes?.ForEach((minute, where, tone, shape) => …)`) using the SAME `Look` gate and the
  SAME `Degradation.CarRegistered(clarity, tone, shape, who.Key, minute, seed)` call the hit
  pass uses (a crash's at-fault car degrades like a hit's car — same purpose constant, same
  memory-is-the-seed property), collecting into `crashSeen`/`crashTrue`; the two-run merge
  becomes a three-run merge on true minutes — implement as two successive two-run merges
  (hits+asks first as today, then that result merged with crashes, `Stamp` re-numbering at
  the final add as it already does). Ties: hits before asks before crashes (the graver first).
  Early-out updated: return empty only when ALL THREE stores are null/empty. `AskInEnglish`:
  add the trailing param, pass through; nothing else changes.

- [x] **Step 4: Run the full Witness surface** — filter
  `"FullyQualifiedName~EventTestimonyTests|FullyQualifiedName~WitnessTests|FullyQualifiedName~WitnessFirewallTests|FullyQualifiedName~DownedTests|FullyQualifiedName~EventSightingTests"` — ALL PASS.

- [x] **Step 5: Commit**: `Recollection: witnesses remember the crash`

---

### Task 3: `CrashPlanner` — the town's one crash a day, verdict included

**Files:**
- Create: `Assets/Noir/Core/People/CrashPlanner.cs`
- Test: create `tools/Noir.Core.Tests/CrashPlannerTests.cs`

**Interfaces:**
- Consumes: `DayPlanner.Plan(world, population, who, day, seed)`, `DayPlan.At/Blocks`,
  `Activity.AwayFromTown/AtThePub`, `world.Grid.TerrainAt`, `Xoshiro256ss`,
  `Contracts.CaseKind/CrashVerdict/CarTone/CarShape` (Task 1).
- Produces (Tasks 4-note and 5 rely on the exact shape):

```csharp
namespace Noir.Core.People
{
    /// <summary>One planned crash: everything the town needs to stage it and judge it,
    /// fixed at plan time. No identity leaks to witnesses — the tones/shapes here are what
    /// the event store records.</summary>
    public readonly struct CrashPlan
    {
        public readonly int MinuteOfDay;
        public readonly Tile Scene;
        public readonly CitizenId AtFault;
        public readonly CitizenId Other;
        public readonly bool Injury;            // the rare bad one: AtFault goes down
        public readonly CrashVerdict Verdict;
        public readonly CarTone Tone;           // the at-fault car, as witnesses may see it
        public readonly CarShape Shape;
        // constructor assigning all fields
    }

    public static class CrashPlanner
    {
        /// <summary>The day's crash, or null — a day with no qualifying pair of drivers is
        /// a day with no crash, and that is honest. Pure in (seed, day, world, population):
        /// recomputed on demand, stored nowhere, identical every call.</summary>
        public static CrashPlan? PlanFor(WorldModel world, Population population, int day, ulong seed)
    }
}
```

**The rules, stated exactly (the implementer transcribes these into code and doc comments):**

1. **Drivers.** A citizen is *driving* at minute m when either (a) their plan's block list
   contains an `Activity.AwayFromTown` block and m is within ±6 minutes of that block's
   `StartMinute` (the morning departure) or its `EndMinute` (the evening return) — the
   out-of-town commute is the one drive Core already knows about; or (b) they are an adult
   whose plan has an `AtThePub` block ENDING at or after 20:00, m IS that block's `EndMinute`, and the per-citizen coin `rng.Chance(0.5f)` says they drove to
   the pub — the planner's own deeming, stated in a doc comment as the deliberate fiction it
   is (Core has no car ownership; half the evening pub crowd drove, decided by seed).
2. **The pick.** One RNG stream for the whole day: `new Xoshiro256ss(Mix(seed, 0x0C0111DE, day))`
   (follow `DayPlanner`'s `Mix` idiom — read how DayPlan.cs:172 builds its stream and use the
   same helper; if `Mix` is private, replicate its arithmetic with a distinct salt constant).
   Collect every (citizen, driving-minute) pair for the day in citizen-id order. Group into
   candidate PAIRS: two distinct citizens whose driving minutes are ≤ 4 apart. If none: no
   crash (return null). Otherwise weight each pair's minute by the fixed table — minutes in
   [06:00,08:00) or [16:30,18:00) weight 3, minutes at or after 20:00 weight 4, all else
   weight 1 — and pick one pair by a single weighted draw from the stream. The EARLIER
   citizen id of the pair is `Other`; the LATER is `AtFault` — then flip at-fault to the
   pub-driver when exactly one of the two is driving off a pub block (drink outranks id
   order, and the doc comment says so).
3. **Scene.** From the at-fault driver's origin place for that block (`Block.Where` of the
   block whose edge minute matched — home for a morning departure, the pub for a pub-close
   drive), take the place's door tile and walk outward in the `Driveways` idiom (increasing
   ring, deterministic order) to the first tile with `TerrainAt == Terrain.Road`; that tile
   is the scene. If no road tile within 12 tiles, return null (no crash that day — stated in
   a comment as the honest degenerate case).
4. **Severity.** `Injury = rng.Chance(1f / 6f)` — about one in six, the spec's number.
5. **Verdict.** DUI when the at-fault driver's plan has any `AtThePub` block whose
   `EndMinute` is within 180 minutes before the crash minute. Else Ticket when
   `rng.Chance(0.6f)` (most sober at-fault crashes earn paper). Else LetGo.
6. **Tones.** `Tone`/`Shape` by two draws from the stream over the non-Unnoticed values —
   what witnesses may degrade from; the host may override with the staged car's real
   tone/shape when it has one (Task 5).

- [x] **Step 1: Write the failing tests** (`CrashPlannerTests.cs`, `VillageContext.Load()`
  fixture like `EventTestimonyTests` — read its usage first):

```csharp
[Test] // same seed, same day, twice -> byte-identical plan (or both null)
public void TheSameDayCrashesTheSameWay() { /* PlanFor twice; assert all fields equal */ }

[Test] // every field the plan stamps is internally consistent
public void ACrashHappensWhereSomebodyWasActuallyDriving()
{ /* if plan != null: assert Scene is Terrain.Road on the fixture grid; assert AtFault != Other;
     assert the at-fault citizen's DayPlan has an AwayFromTown or qualifying AtThePub block
     whose edge is within the pairing window of plan.MinuteOfDay */ }

[Test] // the verdict table, driven from the plan's own data
public void DrinkConvicts()
{ /* if plan != null && at-fault's plan has AtThePub ending within 180 min before the crash:
     assert Verdict == CrashVerdict.Dui; if no such block: assert Verdict != CrashVerdict.Dui */ }

[Test] // several days scanned: at most one crash per day, and some day in a fortnight crashes
public void TheTownCrashesAboutDaily()
{ /* days 0..13: collect PlanFor results; assert at least 3 non-null (the fixture has commuters
     and a tavern; if this proves flaky against the fixture, tighten to >= 1 and note it) */ }
```

Written as real tests, full code, house-style doc comments explaining each gate.

- [x] **Step 2: Run to verify they fail** (no `CrashPlanner` — compile error). Filter
  `"FullyQualifiedName~CrashPlannerTests"`.

- [x] **Step 3: Implement** per the six rules. NOTE the csproj-staleness constraint: hand-add
  `CrashPlanner.cs` (and Task 1's new files if not yet listed) to the gitignored test csproj
  only if the test build fails to see them — the TEST csproj (`tools/Noir.Core.Tests`) is
  hand-maintained/SDK-style and may glob; check before patching.

- [x] **Step 4: Run** the filter — ALL PASS; then the neighbouring People surface:
  `"FullyQualifiedName~PeopleTests|FullyQualifiedName~CrashPlannerTests"` — ALL PASS.

- [x] **Step 5: Commit**: `CrashPlanner: one crash a day, and the day plan is the breathalyzer`

---

### Task 4: The collision arc in `ResponseCases`

**Files:**
- Modify: `Assets/Noir/Core/Response/ResponseCases.cs`
- Test: `tools/Noir.Core.Tests/ResponseCasesTests.cs`

**Interfaces:**
- Consumes: `CaseKind`/`CrashVerdict` from Contracts (Task 1).
- Produces (Task 5 relies on):

```csharp
public int OpenCollision(CitizenId atFault, CitizenId other, int minute, Tile scene,
                         CarTone tone, CarShape shape, bool injury, CrashVerdict verdict)
public CaseKind KindOf(int caseId)
// OrderKind gains: InterviewDriver, ArrestDriver, TicketDriver, ReleaseDrivers, TowVehicle
// (appended AFTER VehiclesLeave — order emission code compares by name, but append anyway;
// a comment states the append-only rule for the enum)
```

**The arc, stated exactly:**

- `OpenCollision` stamps a `Case` with `Kind = Collision`, `Victim = atFault` (the existing
  slot, doc-commented as "the at-fault driver for a collision"), a new readonly
  `CitizenId Other`, `Fatal = false` always (a collision injury is never fatal in phase 1),
  new readonly `bool Injury`, new readonly `CrashVerdict Verdict`. `PersonDown` cases set
  `Other = CitizenId.None`, `Injury = false`, `Verdict = LetGo` via the existing constructor
  defaults.
- Discovery, alarm, officer dispatch, scene-held, county-in: UNCHANGED — the same states and
  ticks serve both kinds (BodySeen's log line is already kind-neutral).
- `TickCanvassing` branches on kind at its top: for `Collision`, the "witness queue" is
  exactly `[Victim, Other]` minus any downed driver (an injured at-fault driver cannot be
  interviewed at the roadside) — emitted as `InterviewDriver` orders with the same
  once-per-index/`NextDoorReadyAt` pacing the canvass uses (`CanvassMinutesPerDoor` shared).
  The host answers through `CountyReachedDoor` unchanged (its file prefix "said:" reads
  correctly for a driver's statement).
- When the interviews are exhausted: for `Injury` cases the ambulance arc runs exactly as
  today (AmbulanceIn → Loading; `TakeBodyAway` names the downed at-fault driver); for benders
  it is SKIPPED. Then — new state `CaseState.Adjudicating` INSERTED between `Loading` and
  `Closed` (safe: the enum is never persisted, every `>=`/`<` comparison recompiles against
  the new values; the ticker's `_ => state.ToString()` fallback covers the new name until
  Task 6) —
  `TickAdjudicating` emits, once, in this order: the verdict order (`ArrestDriver` naming
  `Victim` for Dui / `TicketDriver` naming `Victim` for Ticket / `ReleaseDrivers` with
  `CitizenId.None` for LetGo), then `TowVehicle` naming `Victim` (Dui only — the arrested
  man's car is towed; ticketed and released drivers drive their own cars off), then
  `VehiclesLeave` + `ReleaseOfficer`, state → `Closed`, the same close bookkeeping
  `TickLoading` does (`_lastActiveClosedAt`, `_activeCaseId = -1`, `ActivateNextIfNeeded()`).
  Each emission writes the file in the house voice ("case N: citizen W is under arrest — the
  drink decided it", "case N: citizen W is ticketed at the roadside", "case N: both drivers
  released", "case N: the car is called in for tow").
- `ReturnMinuteOf` for a Dui collision returns `HitMinute + SurvivorAwayDays * 1440` (reuse
  the constant; the doc comment says the county lockup and the hospital cost the same days in
  phase 1).

- [x] **Step 1: Write the failing tests** (in `ResponseCasesTests.cs`, driving the machine
  exactly as `AWitnessedHitRunsTheWholeResponse` does — read it first; four tests):

```csharp
[Test] public void ABenderRunsInterviewsAndAVerdictAndNoAmbulance()
{ /* OpenCollision(letgo, injury:false) -> BodySeen -> tick to officer/county ->
     assert two InterviewDriver orders (Victim then Other), answer each via CountyReachedDoor,
     then assert ReleaseDrivers, then VehiclesLeave+ReleaseOfficer, state Closed,
     and NO AmbulanceIn/TakeBodyAway ever appeared in any orders list. */ }

[Test] public void DrinkEndsInArrestAndATow()
{ /* verdict: Dui -> after interviews assert ArrestDriver(Victim) then TowVehicle(Victim)
     then VehiclesLeave; file contains "under arrest" and "tow". */ }

[Test] public void AnInjuredDriverIsNotInterviewedButIsTakenAway()
{ /* injury:true -> assert exactly ONE InterviewDriver (Other only), then AmbulanceIn,
     Loading, TakeBodyAway names Victim, then the verdict order still emits. */ }

[Test] public void KindOfTellsTheTwoCasesApart()
{ /* Open a person-hit and a collision; assert KindOf. */ }
```

Full code, the happy-path test's minute arithmetic reused.

- [x] **Step 2: Run to verify they fail** — filter `"FullyQualifiedName~ResponseCasesTests"`.

- [x] **Step 3: Implement** per the arc above. The class's own invariants (once-only
  emission flags, `>=` state checks, `Emit` writing log + file) are the pattern for every new
  emission; `TickAdjudicating` mirrors `TickLoading`'s close bookkeeping byte-for-byte.

- [x] **Step 4: Run** `"FullyQualifiedName~ResponseCasesTests|FullyQualifiedName~ResponseFirewallTests"` — ALL PASS.

- [x] **Step 5: Commit**: `The collision case: interviews at the kerb, and the law decides`

---

### Task 5: The host stages the crash and executes the verdict

**Files:**
- Modify: `Assets/Noir/Unity/VillageHost.cs`

**Interfaces:**
- Consumes: `CrashPlanner.PlanFor` (Task 3), `CrashEvents` (Task 1),
  `AskInEnglish(..., crashes:)` via `AskWhatTheySaw` (Task 2),
  `OpenCollision`/`KindOf`/new orders (Task 4), `Sim.Down/Respond/Release/Board/Alight/TakeAway`
  (whichever away-pair the file already uses — read the arrest-adjacent code), `Driveways.Take/
  NearestCar/PositionOf` (`VillageHost.Driveways` accessor), `Response.CruiserHome`, `_victims`
  record, `_voices`, the cordon (rises on OfficerArrived for ANY kind — free).
- Produces: the running feature.

**The steps, stated exactly (identifiers indicative — the implementer uses the file's own):**

- [x] **Step 1: Fields + the minute check.** `private readonly CrashEvents _crashEvents = new
  CrashEvents();` beside `_askEvents`; `private int _crashStagedForDay = -1;`. In
  `RunResponse` (the once-a-sim-minute driver), before the discovery scan: if
  `_crashStagedForDay != Sim.Clock.Day`, compute `var plan = CrashPlanner.PlanFor(World,
  People, Sim.Clock.Day, Seed)` (cheap enough per-minute? NO — compute once per day: cache
  plan+day in fields when the day changes); when a cached plan exists and
  `Sim.Clock.MinuteOfDay >= plan.MinuteOfDay` and the crash has not staged today, call
  `StageCollision(plan.Value)` and mark the day staged. (`>=` not `==` — the response loop's
  own idiom; a skipped minute still crashes.)

- [x] **Step 2: `StageCollision(CrashPlan plan)` — public, so the PlayMode scenario can call
  it with a manufactured plan.** In order: (a) resolve tones — try
  `Driveways.NearestCar(Space3D.ToWorld(home-of-at-fault door), 20f)`; if found, `Take` it,
  move it to the scene (angled ~30° off the road axis, the `Put` idiom's position/rotation
  form with `ElevationGrid.HeightAt`), and use its real tone/shape; else keep the plan's.
  Same for `Other`'s car, angled the other way, nose-to-fender (offset one car-length). Cars
  taken this way are recorded in a per-case list so the Closed arm can destroy them (phase
  1's "tow" — the driveway slot is already permanently empty, which is true: the car is
  gone). (b) `_crashEvents.Record(NowMinute(), plan.Scene, tone, shape)` with the resolved
  at-fault tone. (c) if `plan.Injury`: `Sim.Down(plan.AtFault)` + write the `_victims`
  record exactly as `CarStruckSomebody` does (same DownedFrom/BackFrom arithmetic — the
  downed driver must stop testifying). (d) stand both un-downed drivers at the scene:
  `Sim.Respond(driver, plan.Scene)` (they wait, Responding, until released/arrested/taken).
  (e) `_cases.OpenCollision(...)` with the resolved tone/shape and the plan's verdict.
  (f) a `Debug.Log` `[crash]` line in the `[hit]` line's voice, and a `_voices.Say` for the
  at-fault driver ("he came out of nowhere—" / for the other: variant by id, no RNG).

- [x] **Step 3: `AskWhatTheySaw` threads `_crashEvents`** as the new trailing argument
  (canvass, interviews, and the T key all hear about crashes). `SawTheHit` stays untouched
  (its two-call comparison still passes only `hits` — a collision discovery is driven by the
  same BodySeen-style sighting scan… READ how discovery currently finds a downed body and
  extend the scan: a collision case in `Undiscovered` is discovered when any citizen's
  sight-line reaches the scene tile — reuse the existing discovery scan by treating the
  crash scene like a body tile for kind=Collision cases; follow the scan's existing shape).

- [x] **Step 4: Execute arms** for the new orders in the host's order switch:
  - `InterviewDriver`: file the driver's own statement via
    `_cases.CountyReachedDoor(order.Case, NowMinute(), order.Who, AskWhatTheySaw(order.Who, day))`
    — the county asks the driver what they saw, same seam, same pacing (mirror the canvass
    arm's kerb/no-door handling).
  - `ArrestDriver`: `_voices.Say(order.Who, "…")`, `Sim.Release(order.Who)` then the away
    pair the codebase uses for multi-day absence (READ how the ambulance's TakeBodyAway arm
    sends the victim away and mirror it — same call, `SurvivorAwayDays` semantics), and the
    cruiser departs via the existing `ReleaseOfficer`/`CruiserHome` flow (no new rig motion
    in phase 1 — the log and file carry the ride).
  - `TicketDriver` / `ReleaseDrivers`: `Sim.Release` the named / both drivers, a voices line
    each, nothing else.
  - `TowVehicle`: destroy the staged at-fault car object now (visual), log
    `[case] … the car goes on the hook`.
  - The `Closed` arm additionally destroys any remaining staged cars for that case and
    releases any driver still Responding (mirror the gawker-release loop).

- [x] **Step 5: Build all three csprojs** (hand-patch the gitignored Noir.Unity.csproj with
  any new Core file includes if the Core assembly csprojs are stale — verify with greps),
  exit 0.

- [x] **Step 6: Commit**: `The town crashes on schedule, and the host runs the scene`

---

### Task 6: The ticker tells collisions honestly

**Files:**
- Modify: `Assets/Noir/Unity/VillageUI.cs` (`DrawCaseTicker`)

**Interfaces:**
- Consumes: `Cases.KindOf(c)` (Task 4).

- [x] **Step 1:** In `DrawCaseTicker`, branch the prose on `KindOf`: collision arms —
  Undiscovered: `"two cars sit tangled in the street — nobody has called it in"`; Canvassing:
  `"the county officer is taking the drivers' statements"`; AmbulanceEnRoute/Loading keep the
  existing lines when `Injury`-cased victims exist (`FatalOf` is false for collisions; use
  the existing `them` machinery with "the driver"); Adjudicating (new state, both kinds):
  `"the verdict is coming"` — and the shared states keep their existing lines. Read the
  existing switch and extend minimally; every new string in the file's lowercase deadpan
  voice.
- [x] **Step 2:** `dotnet build Noir.Unity.csproj -c Debug` exit 0.
- [x] **Step 3: Commit**: `The ticker learns to say what a crash is`

---

### Task 7: The PlayMode scenario stages a collision

**Files:**
- Modify: `Assets/Noir/PlayTests/ResponsePlayTests.cs`

**Interfaces:**
- Consumes: `VillageHost.StageCollision(CrashPlan)` (Task 5, public), `CrashPlan`'s
  constructor (Task 3), the suite's teardown conventions.

- [x] **Step 1:** A new `[UnityTest]` `AStagedCollisionRunsToItsVerdict`, mirroring the
  existing scenario's shape: find two outdoor adult citizens near an occupied door (the
  existing victim-search idiom), construct a `CrashPlan` with the CURRENT minute, a road
  tile near them (search `TerrainAt == Terrain.Road` outward — the planner's own idiom),
  verdict `CrashVerdict.Dui`, `Injury = false`; call `host.StageCollision(plan)`; assert
  `Cases.Count` grew and `KindOf` says Collision; ride the existing 300x poll-loop shape to
  `Closed` (reuse the state-latch style); assert the file contains "under arrest" and
  "tow"; assert both drivers are no longer Responding after close, and the at-fault driver
  is `AwayFromTown`. Teardown: the suite's existing `EverythingBack` already releases
  Responding agents, revives the downed, and `CloseLoudly`s stragglers — read it and add a
  `Return` for the arrested driver's away-state mirroring the victim-return handling.
- [x] **Step 2:** `dotnet build Noir.PlayTests.csproj -c Debug` exit 0. The gate itself waits
  for the next editor-closed window.
- [x] **Step 3: Commit**: `The scenario suite stages a crash and reads the verdict`

---

### Task 8: Land it

- [x] **Step 1:** Full Core gate in Release, bare. Expect roughly 582 + 14 (3 CrashEvents,
  1 collision sentence, 2 Recollection, 4 CrashPlanner, 4 ResponseCases) = **~596 — USE THE
  MEASURED NUMBER**; 0 fail, 8 skipped.
- [x] **Step 2:** CLAUDE.md: new Core baseline entry above the 582 one, house style, naming
  the new suites and this plan's path.
- [x] **Step 3:** The spec gets its "Landed" line (date + plan path + measured gate).
- [x] **Step 4:** Commit docs, push.
- [x] **Step 5:** Named leftovers: the PlayMode gate at the next editor-closed window; the
  live look (stand at the tavern at closing on a crash day); phase 2 by name (wrecker rig,
  crumple, cuffed walk, crash sound); the planner's pub-driver coin and 180-minute DUI
  window are tuning knobs the owner may re-rule after watching.
