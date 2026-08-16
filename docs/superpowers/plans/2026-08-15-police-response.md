# Police Response (Drivable-Car Phase 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A hit body is discovered by sight, a Rossville officer walks over and holds the scene, a county car drives in from Route 1's south edge and canvasses the witnesses into a recorded case file, and an ambulance from the north takes the victim away — dead above ~18 mph, back on their plan three days later below it.

**Architecture:** Core decides, Unity draws. A deterministic `ResponseCases` state machine (new Contracts-only Core assembly) is ticked once per sim minute by `VillageHost`; it emits orders and receives arrival reports, never predicting travel. Two new sim mutations (`Respond`/`Release`, `TakeAway`/`Return`) copy the `Down`/`Revive` live-state pattern. `CityResponse` renders vehicles and the county actor on sim time, outside `_movers`.

**Tech Stack:** C# 9 / netstandard2.1 (Core, tested by `dotnet test -c Release`), Unity 6000.3.20f1 (Unity layer, tested by PlayMode).

**Spec:** `docs/superpowers/specs/2026-08-15-police-response-design.md` — read it first; the six owner rulings and the "Decisions that must not be quietly reversed" list bind every task.

## Global Constraints

- **Core gate:** `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj` — baseline **526 pass, 0 fail, 8 skipped**. Never run it in Debug (unstable on this machine). Any red is a regression.
- **Unity compiles are separate evidence:** `dotnet build Noir.Unity.csproj -c Debug` (and `Noir.PlayTests.csproj` before any PlayMode run). These csprojs are **gitignored and Unity-generated** — after adding the new asmdef or any new file, Unity must open once (editor or batchmode) before `dotnet build` sees them. **The editor must be CLOSED before any `Unity.exe -batchmode` command** — check for a running `Unity.exe` first, and if the owner left it open deliberately, say so rather than killing it.
- **Core determinism:** no `Math.Sin/Cos/Atan/Pow` (a source-scanning test enforces; `Math.Sqrt` is allowed), no `System.Random`, no `System.DateTime`. The response path consumes **no RNG at all** (spec ruling).
- **Minutes:** absolute minutes = `Sim.Clock.Day * (GameClock.TicksPerDay / GameClock.TicksPerMinute) + Sim.Clock.MinuteOfDay` (the exact expression `CarStruckSomebody` already uses). `HitEvents`, `_downedAtMinute`, `ResponseCases` all speak absolute minutes; `DayPlan` blocks and `Clock.MinuteOfDay` are minute-of-day. Mixing them misfilters by whole days silently.
- **Firewalls:** only `Assets/Noir/Unity/VillageHost.cs` may contain the string `Noir.Core.Witness` in code (comments are stripped by the grep test). `Noir.Core.Witness.asmdef`'s references array is pinned exactly to `[Contracts, World, People, Observation]`. The new `Noir.Core.Response.asmdef` references `["Noir.Core.Contracts"]` only. `HitEvents` is frozen — no victim id, no fatality, no PlaceId.
- **Activity enum:** values are positional; new members append at the END (after `Downed`), with a lowercase row in `Content/animations.txt` **in the same commit** (`EveryActivityHasARowInTheRealFile` gates it). A row naming only clips other rows already use needs no controller rebuild.
- **Git:** never `git add -A` or `git add .` — stage only what you edited. New `.cs`/`.asmdef` files under `Assets/` need Unity-generated `.meta` files: let an editor/batchmode run generate them, then commit them (the `5f34ba7` convention).
- **Shared PlayMode town:** the city is built once per run, opens at NOON, survey plan unless `NOIR_BUILT_TOWN=1`. Anything a test changes must be restored in a `[UnityTearDown]` that runs on every exit path, communicating via STATIC fields.

---

## File Structure

| File | Role |
|---|---|
| `Assets/Noir/Core/World/LaneRoutes.cs` (create) | Dijkstra over `LaneGraph` + tile→segment inverse |
| `Assets/Noir/Core/Response/ResponseCases.cs` (create) | The case state machine, orders, case file, constants |
| `Assets/Noir/Core/Response/Noir.Core.Response.asmdef` (create) | Contracts-only assembly |
| `Assets/Noir/Core/Sim/Simulation.cs` (modify) | `TakeAway`/`Return`, `Respond`/`Release`, tick-loop gates, AgentState fields |
| `Assets/Noir/Core/People/DayPlan.cs` (modify) | `Activity.Responding` appended after `Downed` |
| `Assets/Noir/Core/Witness/Interruptions.cs` (modify) | `BackFromMinute` second question |
| `Assets/Noir/Core/Witness/Recollection.cs` (modify) | Both arms gate on the `[downedFrom, backFrom)` window |
| `Assets/Noir/Core/Witness/Discovery.cs` (create) | Who can see the body this minute, testimony's own optics |
| `Content/animations.txt` (modify) | `responding` row |
| `Content/kinds.txt` (modify) | Precinct rota: 4 jobs, two watch windows; fix the stale "Only the mill." header |
| `Assets/Noir/Unity/VillageHost.cs` (modify) | Victim records, severity, discovery scan, machine tick, order execution, `[case]` log, `Cases` accessor |
| `Assets/Noir/Unity/Player.cs` (modify) | Impact speed into `CarStruckSomebody`; obstacle registration on leave/enter |
| `Assets/Noir/Unity/CityTraffic.cs` (modify) | Stationary-obstacle registry consulted by `Blocked` |
| `Assets/Noir/Unity/CityResponse.cs` (create) | Response vehicles + county officer actor, sim-time movement |
| `Assets/Noir/Unity/VillageUI.cs` (modify) | `Verb()` case for `Responding` |
| `tools/Noir.Core.Tests/LaneRoutesTests.cs` (create) | |
| `tools/Noir.Core.Tests/ResponseCasesTests.cs` (create) | |
| `tools/Noir.Core.Tests/TakeAwayTests.cs` (create) | |
| `tools/Noir.Core.Tests/RespondTests.cs` (create) | |
| `tools/Noir.Core.Tests/DiscoveryTests.cs` (create) | |
| `tools/Noir.Core.Tests/ResponseFirewallTests.cs` (create) | Pins the Response asmdef reference list |
| `tools/Noir.Core.Tests/EventTestimonyTests.cs` (modify) | Interval-window tests join the existing file |
| `tools/Noir.Core.Tests/PrecinctRotaTests.cs` (create) | Reads the real kinds.txt |
| `Assets/Noir/PlayTests/ResponsePlayTests.cs` (create) | The one end-to-end scenario |

Task order: 1→15 below. Tasks 1–8 are Core/Content and independent of Unity; 9–13 are Unity; 14 is PlayMode; 15 is gates/docs/push.

---

### Task 1: `LaneRoutes` — the A→B route planner (Core, World)

**Files:**
- Create: `Assets/Noir/Core/World/LaneRoutes.cs`
- Test: `tools/Noir.Core.Tests/LaneRoutesTests.cs`

**Interfaces:**
- Consumes: `LaneGraph` (`Segments`, `Turns`, `TurnsFrom(int)`, `Entries`, `AlongOf`/`TravelOf`), `RoadNetwork.Lines[i].Path` (`Project`, `PointAt`, `ArcAt`), `RoadLine.IsNorthSouth`.
- Produces: `LaneRoutes.Plan(LaneGraph graph, int fromSegment, int toSegment, List<int> turnsOut) -> bool` (turnsOut holds LaneTurn indices in driving order; empty when from==to), and `LaneRoutes.NearestSegment(LaneGraph graph, RoadNetwork roads, Vec2 point, out int segment, out float s) -> bool`. Task 11 drives cars along `turnsOut`; Task 13 aims them with `NearestSegment`.

- [ ] **Step 1: Write the failing tests**

Copy `LaneGraphTests`' fixture pattern verbatim (`tools/Noir.Core.Tests/LaneGraphTests.cs:17-33` — map string → `TestContent.EnsureKinds()` → `WorldBuilder.Build(VillageParser.Parse(map), 1234UL)` → `new LaneGraph(world.Roads, world.Width, world.Height)`; legal in tools/, the `TownPipelineTests` ban covers only Assets/Noir/Unity|Editor). Tests:

```csharp
[Test]
public void ARouteExistsFromEveryEntryToEverySegment()
{
    var graph = FixtureVillage();
    var turns = new List<int>();
    foreach (int entry in graph.Entries)
        for (int to = 0; to < graph.Segments.Count; to++)
            Assert.That(LaneRoutes.Plan(graph, entry, to, turns), Is.True,
                $"no route from entry {entry} to segment {to}, but LaneGraphTests proves all are reachable");
}

[Test]
public void ThePlannedTurnsChainLegally()
{
    var graph = FixtureVillage();
    var turns = new List<int>();
    int from = graph.Entries[0];
    int to = graph.Segments.Count - 1;
    Assert.That(LaneRoutes.Plan(graph, from, to, turns), Is.True);
    int at = from;
    foreach (int t in turns)
    {
        Assert.That(graph.Turns[t].From, Is.EqualTo(at), "a turn departs a segment the car is not on");
        at = graph.Turns[t].To;
    }
    Assert.That(at, Is.EqualTo(to), "the chain does not end at the destination");
}

[Test]
public void NearestSegmentFindsTheLaneBesideAPoint()
{
    var graph = Build(Header + Grid, out var world);
    // On the fixture, road "first" runs x=75 the full height; a point just right of its
    // centre at y=120 must land on one of its lanes with s inside the segment.
    Assert.That(LaneRoutes.NearestSegment(graph, world.Roads, new Vec2(77f, 120f),
                                          out int seg, out float s), Is.True);
    var found = graph.Segments[seg];
    Assert.That(world.Roads.Lines[found.Line].Name, Is.EqualTo("first"));
    Assert.That(s, Is.GreaterThanOrEqualTo(found.FromS).And.LessThanOrEqualTo(found.ToS));
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~LaneRoutesTests"`. Expected: compile error, `LaneRoutes` not defined.

- [ ] **Step 3: Implement `LaneRoutes`**

```csharp
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// A drive route over the lane graph — the planner CityTraffic.Graph's "the bus routes
    /// will want the same one" comment has been waiting for. Ambient traffic stays a
    /// memoryless wander; this answers the one question it cannot: how to get from THIS
    /// lane to THAT one. Deterministic, no RNG, Sqrt only.
    /// </summary>
    public static class LaneRoutes
    {
        /// <summary>
        /// Dijkstra over segments; cost is segment length (turn arcs are a junction wide and
        /// near-constant, so they cancel). On success `turnsOut` holds LaneTurn indices in
        /// driving order — empty when from == to. False when no chain of legal turns joins them.
        /// </summary>
        public static bool Plan(LaneGraph graph, int fromSegment, int toSegment, List<int> turnsOut)
        {
            turnsOut.Clear();
            if (fromSegment == toSegment) return true;

            int n = graph.Segments.Count;
            var best = new float[n];
            var via = new int[n];              // the turn that reached each segment, -1 unreached
            for (int i = 0; i < n; i++) { best[i] = float.MaxValue; via[i] = -1; }
            best[fromSegment] = 0f;

            // A few hundred segments: a plain scan-for-minimum is simpler than a heap and
            // costs nothing at this size.
            var done = new bool[n];
            for (int round = 0; round < n; round++)
            {
                int at = -1; float low = float.MaxValue;
                for (int i = 0; i < n; i++)
                    if (!done[i] && best[i] < low) { low = best[i]; at = i; }
                if (at < 0) break;
                done[at] = true;
                if (at == toSegment) break;

                foreach (int t in graph.TurnsFrom(at))
                {
                    int next = graph.Turns[t].To;
                    float cost = best[at] + graph.Segments[next].Length;
                    if (cost < best[next]) { best[next] = cost; via[next] = t; }
                }
            }

            if (via[toSegment] < 0) return false;
            for (int at = toSegment; at != fromSegment; at = graph.Turns[via[at]].From)
                turnsOut.Add(via[at]);
            turnsOut.Reverse();
            return true;
        }

        /// <summary>
        /// The lane segment beside a village-space point, and the travel coordinate of the
        /// nearest spot on it. FromS/ToS are travel-signed AXIS coordinates while
        /// RoadPath.Project returns ARC length — the conversion goes arc → point → axis
        /// coordinate → TravelOf, or the two disagree on every curve.
        /// </summary>
        public static bool NearestSegment(LaneGraph graph, RoadNetwork roads, Vec2 point,
                                          out int segment, out float s)
        {
            segment = -1; s = 0f;
            float nearest = float.MaxValue;
            for (int i = 0; i < graph.Segments.Count; i++)
            {
                var seg = graph.Segments[i];
                var line = roads.Lines[seg.Line];
                if (line.Path == null) continue;

                var (arc, lateral) = line.Path.Project(point);
                var on = line.Path.PointAt(arc);
                float axis = line.IsNorthSouth ? on.Y : on.X;
                float travel = LaneGraph.TravelOf(seg.Way, axis);
                if (travel < seg.FromS || travel > seg.ToS) continue;

                float d = lateral < 0f ? -lateral : lateral;
                if (d >= nearest) continue;
                nearest = d; segment = i; s = travel;
            }
            return segment >= 0;
        }
    }
}
```

- [ ] **Step 4: Run the tests** — same filter. Expected: 3 PASS. Then run the full Core gate once — expected 529/0 (526 + 3).

- [ ] **Step 5: Commit** — `git add Assets/Noir/Core/World/LaneRoutes.cs tools/Noir.Core.Tests/LaneRoutesTests.cs` then commit: `LaneRoutes: the first A-to-B plan over the lane graph, with the tile inverse`.

---

### Task 2: `ResponseCases` — the case machine, happy path (Core, new assembly)

**Files:**
- Create: `Assets/Noir/Core/Response/ResponseCases.cs`, `Assets/Noir/Core/Response/Noir.Core.Response.asmdef`
- Test: `tools/Noir.Core.Tests/ResponseCasesTests.cs`, `tools/Noir.Core.Tests/ResponseFirewallTests.cs`

**Interfaces:**
- Consumes: `CitizenId`, `Tile`, `CarTone`, `CarShape` (all Noir.Core.Contracts).
- Produces (Tasks 9/13/14 depend on these exact names):

```csharp
public enum CaseState : byte
{ Undiscovered, Alarm, OfficerEnRoute, SceneHeld, CountyEnRoute, Canvassing,
  AmbulanceEnRoute, Loading, Closed }

public enum OrderKind : byte
{ DispatchOfficer, CountyCarIn, CanvassNext, AmbulanceIn, TakeBodyAway,
  ReleaseOfficer, VehiclesLeave }

public readonly struct CaseOrder
{
    public readonly OrderKind Kind;
    public readonly int Case;
    public readonly Tile Scene;
    public readonly CitizenId Who;   // witness for CanvassNext, officer for ReleaseOfficer,
                                     // victim for TakeBodyAway; CitizenId.None otherwise
}

public sealed class ResponseCases
{
    public const int AlarmDelayMinutes = 4;
    public const int CountyOffMapMinutes = 18;
    public const int CanvassMinutesPerDoor = 5;
    public const int NoWitnessSceneMinutes = 10;   // zero-canvass cases still get worked
    public const int AmbulanceOffMapMinutes = 10;
    public const int LoadingMinutes = 3;
    public const float FatalSpeed = 8f;            // m/s at impact, ~18 mph
    public const int SurvivorAwayDays = 3;

    public int Open(CitizenId victim, int minute, Tile scene, CarTone tone, CarShape shape, bool fatal);
    public int Count { get; }
    public int ClosedCount { get; }
    public CaseState StateOf(int caseId);
    public CitizenId VictimOf(int caseId);
    public Tile SceneOf(int caseId);
    public int MinuteOf(int caseId);                // the hit's absolute minute
    public bool FatalOf(int caseId);
    public int ReturnMinuteOf(int caseId);          // hit + SurvivorAwayDays*1440, or int.MaxValue when fatal
    public CitizenId OfficerOf(int caseId);         // CitizenId.None until dispatched

    public void BodySeen(int caseId, int minute, CitizenId discoverer);
    public void OfficerDispatched(int caseId, CitizenId officer);
    public void OfficerArrived(int caseId, int minute);
    public void CountyArrived(int caseId, int minute);
    public void CanvassBegins(int caseId, CitizenId[] witnesses);
    public void CountyReachedDoor(int caseId, int minute, CitizenId witness, string[] lines);
    public void AmbulanceArrived(int caseId, int minute);

    public void Tick(int minute, List<CaseOrder> orders);
    public void DrainLog(List<string> into);        // one line per transition since last drain
    public IReadOnlyList<string> FileOf(int caseId); // the case file: transitions + canvass answers
}
```

**Machine rules (implement exactly):**
- `Open` appends a case in `Undiscovered` and returns its index. Two hits in one minute are two cases.
- Only the ACTIVE case advances: the lowest-id non-`Closed` case that has been `BodySeen`. Queued discovered cases wait; their alarm clock starts at `max(discoveredAt, previousCaseClosedAt)`.
- Transitions (each appends a `[case]`-shaped line to the log AND to the case's file, e.g. `case 0: discovered at minute 723 by citizen 41`):
  - `Undiscovered` --BodySeen--> `Alarm` (record discoveredAt).
  - `Alarm`: `Tick` emits `DispatchOfficer` once when `minute >= alarmStart + AlarmDelayMinutes` → `OfficerEnRoute`. (The HOST selects who and reports `OfficerDispatched`; the machine knows Contracts only.)
  - `OfficerEnRoute` --OfficerArrived--> `SceneHeld` (record countyDueAt = minute + CountyOffMapMinutes).
  - `SceneHeld`: `Tick` emits `CountyCarIn` once when `minute >= countyDueAt` → `CountyEnRoute`.
  - `CountyEnRoute` --CountyArrived--> `Canvassing`. The host calls `CanvassBegins` with the witness list in the same minute; an empty list sets a dwell of `NoWitnessSceneMinutes` instead.
  - `Canvassing`: emit `CanvassNext(witness[i])` for the first unvisited witness; on `CountyReachedDoor`, record the lines into the file and dwell `CanvassMinutesPerDoor` before the next emit. When all are visited (or the empty-list dwell expires): record ambulanceDueAt = minute + AmbulanceOffMapMinutes; `Tick` emits `AmbulanceIn` at that minute → `AmbulanceEnRoute`.
  - `AmbulanceEnRoute` --AmbulanceArrived--> `Loading` (dwell `LoadingMinutes`).
  - `Loading` expiry: `Tick` emits, in order, `TakeBodyAway`, `VehiclesLeave`, `ReleaseOfficer` → `Closed`.
- Every emitted order is emitted exactly ONCE (guard with a per-case emitted-flags set; `Tick` is called every minute and must be idempotent between transitions).
- No RNG, no DateTime, forward minutes only (`Tick` may assert `minute` never decreases).

- [ ] **Step 1: Write the asmdef** — copy `Noir.Core.Observation.asmdef`'s exact shape (excerpted in the spec's research; references `["Noir.Core.Contracts"]`, `"autoReferenced": false`, `"noEngineReferences": true`, `"rootNamespace": "Noir.Core.Response"`, `"name": "Noir.Core.Response"`). The dotnet gate compiles the new folder automatically (the `Compile Include="..\..\Assets\Noir\Core\**\*.cs"` glob in `tools/Noir.Core/Noir.Core.csproj`).

- [ ] **Step 2: Write the failing tests** — `ResponseCasesTests` covering the happy path end to end with hand-fed minutes:

```csharp
[Test]
public void AWitnessedHitRunsTheWholeResponse()
{
    var cases = new ResponseCases();
    var orders = new List<CaseOrder>();
    int id = cases.Open(new CitizenId(7), 1000, new Tile(50, 50),
                        CarTone.Dark, CarShape.Pickup, fatal: false);
    Assert.That(cases.StateOf(id), Is.EqualTo(CaseState.Undiscovered));

    cases.BodySeen(id, 1000, new CitizenId(3));
    cases.Tick(1000, orders);
    Assert.That(orders, Is.Empty, "the alarm takes AlarmDelayMinutes to raise");

    cases.Tick(1004, orders);
    Assert.That(orders.Count, Is.EqualTo(1));
    Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.DispatchOfficer));
    orders.Clear();
    cases.Tick(1005, orders);
    Assert.That(orders, Is.Empty, "an order is emitted exactly once");

    cases.OfficerDispatched(id, new CitizenId(12));
    cases.OfficerArrived(id, 1010);
    cases.Tick(1027, orders);   // 1010 + 18 = 1028: not yet
    Assert.That(orders, Is.Empty);
    cases.Tick(1028, orders);
    Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.CountyCarIn));
    orders.Clear();

    cases.CountyArrived(id, 1033);
    cases.CanvassBegins(id, new[] { new CitizenId(3), new CitizenId(9) });
    cases.Tick(1033, orders);
    Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.CanvassNext));
    Assert.That(orders[0].Who, Is.EqualTo(new CitizenId(3)));
    orders.Clear();

    cases.CountyReachedDoor(id, 1036, new CitizenId(3), new[] { "16:40, I saw a dark pickup hit somebody." });
    cases.Tick(1040, orders);   // 1036 + 5 = 1041: still dwelling
    Assert.That(orders, Is.Empty);
    cases.Tick(1041, orders);
    Assert.That(orders[0].Who, Is.EqualTo(new CitizenId(9)));
    orders.Clear();

    cases.CountyReachedDoor(id, 1044, new CitizenId(9), new[] { "Nothing. I never saw anybody." });
    cases.Tick(1049, orders);   // canvass done at 1044+5=1049 → ambulance due 1049+10=1059
    Assert.That(orders, Is.Empty);
    cases.Tick(1059, orders);
    Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.AmbulanceIn));
    orders.Clear();

    cases.AmbulanceArrived(id, 1065);
    cases.Tick(1068, orders);   // 1065 + 3 loading
    Assert.That(orders.Select(o => o.Kind), Is.EqualTo(new[]
        { OrderKind.TakeBodyAway, OrderKind.VehiclesLeave, OrderKind.ReleaseOfficer }));
    Assert.That(orders[0].Who, Is.EqualTo(new CitizenId(7)), "the body order names the victim");
    Assert.That(orders[2].Who, Is.EqualTo(new CitizenId(12)), "the release order names the officer");
    Assert.That(cases.StateOf(id), Is.EqualTo(CaseState.Closed));
    Assert.That(cases.ReturnMinuteOf(id), Is.EqualTo(1000 + 3 * 1440));

    var file = cases.FileOf(id);
    Assert.That(file.Any(l => l.Contains("dark pickup")), "the canvass answer is in the file");
}

[Test]
public void AFatalCaseNeverReturns()
{
    var cases = new ResponseCases();
    int id = cases.Open(new CitizenId(1), 500, new Tile(10, 10),
                        CarTone.Mid, CarShape.Car, fatal: true);
    Assert.That(cases.ReturnMinuteOf(id), Is.EqualTo(int.MaxValue));
}

[Test]
public void AnEmptyCanvassStillWorksTheScene()
{
    // Drive to Canvassing as above with CanvassBegins(id, new CitizenId[0]);
    // assert AmbulanceIn is emitted at countyArrivedMinute + NoWitnessSceneMinutes
    //        + AmbulanceOffMapMinutes and not a minute before.
}
```

`ResponseFirewallTests`: clone `ObservationFirewallTests`' second (asmdef-pinning) test verbatim — regex out the `references` array of `Assets/Noir/Core/Response/Noir.Core.Response.asmdef` and assert it equals `["Noir.Core.Contracts"]`, plus the `noEngineReferences: true` regex assert. (Do NOT clone the reflection identity-type walk: the case file legitimately carries `CitizenId` — the spec says so.)

- [ ] **Step 3: Run to verify failure** — filter `FullyQualifiedName~ResponseCases`. Expected: compile error.

- [ ] **Step 4: Implement `ResponseCases`** to the rules above. One file; a private `sealed class Case` holding state, stamps, the witness queue, emitted-flags, and its file `List<string>`; the public API delegating to the active case where relevant. No LINQ in Tick (allocation-free per minute is easy here and matches the Core idiom).

- [ ] **Step 5: Run the tests, then the full gate** — expected all new green, total 526 + 3 (Task 1) + ~5 here, 0 fail.

- [ ] **Step 6: Commit** — stage the two new source files and two new test files only. Message: `ResponseCases: the case machine, orders out and arrivals in, minute-driven`.

---

### Task 3: `ResponseCases` — queueing and resilience

**Files:**
- Modify: `Assets/Noir/Core/Response/ResponseCases.cs`
- Test: `tools/Noir.Core.Tests/ResponseCasesTests.cs` (extend)

**Interfaces:**
- Produces: `public void OfficerLost(int caseId)` — the officer went down; the case re-emits `DispatchOfficer` on the next `Tick` (host reports it when the responding officer is struck). Task 13 calls it.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public void ASecondCaseWaitsForTheFirstToClose()
{
    // Open case A at minute 100, BodySeen(100); open case B at 110, BodySeen(110).
    // Drive A through to Closed at minute ~170 feeding arrivals as in the happy path.
    // Assert: between 114 and A's close, no Tick ever emits an order carrying Case == B.
    // Assert: B's DispatchOfficer is emitted at (A's close minute) + AlarmDelayMinutes,
    //         not at 114 — the alarm clock starts when the town's one response frees up.
}

[Test]
public void ADownedOfficerIsReplaced()
{
    // Drive a case to OfficerEnRoute, OfficerDispatched(officer 12), then OfficerLost(id).
    // Assert the next Tick emits DispatchOfficer again, and OfficerOf(id) is None until
    // the host reports the replacement.
}

[Test]
public void TheLogRunsForwardAndDrains()
{
    // After the happy path, DrainLog returns > 0 lines in transition order (discovered
    // before officer, officer before county...), and a second DrainLog returns none.
}
```

- [ ] **Step 2: Run to verify failure.**
- [ ] **Step 3: Implement** — `OfficerLost` clears the officer and the DispatchOfficer emitted-flag; the queue rule from Task 2 already holds `activeSince`, so B's alarm base is `max(discoveredAt, activeSince)`. Also the spec's loud-close rule: `public void CloseLoudly(int caseId, string why)` — any state → `Closed`, one log line naming why, orders `VehiclesLeave` + `ReleaseOfficer` if anything was out. The host calls it if a case's victim state ever stops making sense (defensive; nothing should reach it, and the log line is the alarm if something does).
- [ ] **Step 4: Run the tests, then the full gate.**
- [ ] **Step 5: Commit** — `ResponseCases: cases queue, a downed officer is replaced, the log drains`.

---

### Task 4: `Simulation.TakeAway` / `Return` — the absent victim (Core, Sim)

**Files:**
- Modify: `Assets/Noir/Core/Sim/Simulation.cs`
- Test: `tools/Noir.Core.Tests/TakeAwayTests.cs`

**Interfaces:**
- Consumes: the `Down`/`Revive` pattern (`Simulation.cs:381-443`), the tick loop's Downed skip (`Simulation.cs:476`), `Vec2.CentreOf`, `World.GetPlace(...).Door`.
- Produces: `public void TakeAway(CitizenId who, int returnAbsoluteMinute)` (requires the citizen be Downed; `int.MaxValue` = dead) and `public void Return(CitizenId who)` (also called internally when the clock reaches the return minute; public so the PlayMode teardown can restore the shared town). `AgentState` gains `public int AwayUntilMinute;` (0 = not taken away). Tasks 9/13/14 call these.

**Design decisions locked here:**
- `Doing` becomes `Activity.AwayFromTown` while away — this reuses, with ZERO Unity edits, `AgentMeshView`'s root-deactivation arm, `TakeCensus`'s away skip, and `EveryActivityHasARowInTheRealFile`'s existing exemption. No new enum member for absence.
- The tick loop gates taken-away agents exactly where Downed gates (after `block` is computed, before the wants-check), checking the clock for the return:

```csharp
// Splice DIRECTLY AFTER the Downed skip at Simulation.cs:476:
if (_agents[i].AwayUntilMinute != 0)
{
    int now = _clock.Day * 1440 + minute;
    if (now < _agents[i].AwayUntilMinute) continue;
    Return(new CitizenId(i));      // falls through: they rejoin the plan this tick
}
```

- `TakeAway` body (mirror `Down`'s shape; the victim is already Downed so the entry cleanup has run — assert it):

```csharp
/// <summary>
/// The ambulance leaves with them. Requires Downed (Phase 2's ambulance only ever
/// collects a body) — clears it and replaces it with an absent state the renderer
/// already knows how to not-draw: Doing becomes AwayFromTown, the same value the
/// out-of-town commuters carry, so no display site needed teaching. int.MaxValue
/// never returns — dead, and still in the population: 1,300 is load-bearing and
/// nobody is ever removed, they are frozen out of the world. Consumes no RNG.
/// </summary>
public void TakeAway(CitizenId who, int returnAbsoluteMinute)
{
    int i = who.Value;
    if (!_agents[i].Downed) return;
    _agents[i].Downed = false;
    _agents[i].Doing = Activity.AwayFromTown;
    _agents[i].AwayUntilMinute = returnAbsoluteMinute;
}

/// <summary>
/// Back from the hospital, at their own front door, and the plan takes them from
/// there — Destination = None is Revive's own trick to fire the departure within
/// the minute. Public for the same second consumer Revive has: a test handing a
/// shared town back the way it found it.
/// </summary>
public void Return(CitizenId who)
{
    int i = who.Value;
    if (_agents[i].AwayUntilMinute == 0) return;
    _agents[i].AwayUntilMinute = 0;

    var home = World.GetPlace(People.Get(who).Home);
    if (home != null)
    {
        _agents[i].Position = Vec2.CentreOf(home.Door);
        _agents[i].PreviousPosition = _agents[i].Position;
        _agents[i].At = People.Get(who).Home;
    }
    _agents[i].Destination = PlaceId.None;
}
```

- Also: `Down(who)` on a taken-away citizen must be impossible by construction — `Player.SweepForVictims` already skips `Doing == AwayFromTown` agents, and `CarStruckSomebody` guards `Downed`; add `if (_agents[i].AwayUntilMinute != 0) return;` at the top of `Down` anyway (a body off-map cannot be hit again).
- Update `Down`'s doc comment: it is no longer "the one external mutation" — say "one of the sim's four external mutations (Down, Revive, TakeAway/Return, Respond/Release)". Do it in THIS task; Task 5 adds the last pair.

- [ ] **Step 1: Write the failing tests** (on the Queueham fixture, seed 1979, start 8*60, copying `DownedTests`' shapes):

```csharp
[Test] public void ATakenAwayVictimIsNotDrawnAndNotHit()
    // Down citizen 1, TakeAway(who, int.MaxValue). Assert Doing == AwayFromTown,
    // Downed == false, and an hour of ticks later Position has not moved.

[Test] public void ASurvivorReturnsHomeAndRejoinsTheirPlan()
    // Down citizen 1 mid-errand (wait for Travelling, per ARevivedAgentDepartsPromptlyEvenMidBlock's
    // own comment), TakeAway with returnMinute = current absolute minute + 30.
    // Tick 31 sim minutes: assert AwayUntilMinute == 0, Position started at their home door,
    // and within ten further sim minutes they are Travelling or Doing != AwayFromTown.

[Test] public void ADeadVictimNeverComesBack()
    // TakeAway(..., int.MaxValue); tick a full sim day; Doing still AwayFromTown.

[Test] public void NobodyTakenAwayIsByteIdenticalToBefore()
    // Clone NobodyDownedIsByteIdenticalToBefore verbatim (two same-seed sims, one hour
    // lockstep, compare Position + Doing per agent) — the new tick-loop gate is a true
    // no-op when AwayUntilMinute is never set.

[Test] public void ReturnIsIdempotentAndTakeAwayRequiresDowned()
    // TakeAway on an un-downed citizen is a no-op; double Return is a no-op.
```

- [ ] **Step 2: Run to verify failure.** — filter `FullyQualifiedName~TakeAwayTests`.
- [ ] **Step 3: Implement** per the code above (field, tick gate, two methods, Down guard, doc edit).
- [ ] **Step 4: Run the new tests, then the FULL gate** — `DownedTests` must stay green untouched.
- [ ] **Step 5: Commit** — `Sim.TakeAway/Return: the ambulance's verb — absent like a commuter, dead means forever`.

---

### Task 5: `Simulation.Respond` / `Release` — the off-plan walk (Core, Sim + Content + UI verb)

**Files:**
- Modify: `Assets/Noir/Core/Sim/Simulation.cs`, `Assets/Noir/Core/People/DayPlan.cs` (Activity enum), `Content/animations.txt`, `Assets/Noir/Unity/VillageUI.cs` (Verb switch)
- Test: `tools/Noir.Core.Tests/RespondTests.cs`

**Interfaces:**
- Consumes: `StartJourney`'s shape (`Simulation.cs:906-994`), `Advance`/`Arrive`/`StandStill`, `Down`'s cleanup list, `Pathfinder.FindPath(Tile, Tile, List<Tile>)`.
- Produces: `public void Respond(CitizenId who, Tile scene)` and `public void Release(CitizenId who)`; `AgentState` gains `public bool Responding; public Tile RespondTarget;`; `Activity.Responding` appended AFTER `Downed`. Task 13 calls these; the host detects arrival by polling `GetAgent(officer)` (`Responding && !Travelling && Doing == Activity.Responding`).

**Design decisions locked here:**
- A responding agent leaves the plan the way a downed one does: the tick loop gates them right after the TakeAway gate from Task 4 and runs a dedicated per-tick body instead of `continue`:

```csharp
// Splice AFTER Task 4's AwayUntilMinute gate:
if (_agents[i].Responding) { RespondTick(i, citizen, dt); continue; }
```

- `RespondTick`: if `Travelling`, call `Advance(i, citizen, dt)` — BUT `Arrive` unconditionally rewrites `Doing` from the plan (`Simulation.cs:1137`), so `Arrive` gains one guard line, the same shape as the Stranded guard in the tick loop:

```csharp
// In Arrive, replace the final line
//     _agents[index].Doing = _plans[index].At(_clock.MinuteOfDay).What;
// with:
_agents[index].Doing = _agents[index].Responding
    ? Activity.Responding
    : _plans[index].At(_clock.MinuteOfDay).What;
```

If NOT travelling and not yet standing at the target tile, start the tile journey (a trimmed `StartJourney` — no companions, no Carrying, no TargetIn):

```csharp
private void RespondTick(int index, Citizen citizen, float dt)
{
    if (_agents[index].Travelling) { Advance(index, citizen, dt); return; }

    var from = _agents[index].Position.ToTile();
    if (from == _agents[index].RespondTarget)
    {
        if (_agents[index].Doing != Activity.Responding)
        { _agents[index].Doing = Activity.Responding; StandStill(index); }
        return;
    }
    // Not there and not walking: (re)start the journey. Retrying every tick is fine —
    // the Regions pre-check answers an impossible route in O(1), and a GaveUp is worth
    // asking again (PathOutcome's own doc). Budgeted like every other journey.
    if (!CanAffordAPath()) return;
    _scratchPath.Clear();
    var outcome = _pathfinder.FindPath(from, _agents[index].RespondTarget, _scratchPath);
    _pathNodesThisTick += _pathfinder.LastNodesExamined;
    _pathsThisTick++;
    if (outcome != PathOutcome.Found || _scratchPath.Count == 0)
    {
        if (_agents[index].Doing != Activity.Responding)
        { _agents[index].Doing = Activity.Responding; StandStill(index); }
        return;   // stand where they are; the host's per-minute poll sees no arrival and waits
    }
    var route = RentPath(index);
    route.Clear();
    route.AddRange(_scratchPath);
    _agents[index].PathIndex = 0;
    _agents[index].Travelling = true;
    _agents[index].At = PlaceId.None;
    _agents[index].Doing = Activity.TravellingTo;
}
```

- `Respond(who, scene)`: mirror `Down`'s entry cleanup (Travelling=false, ReleasePath, StandStill, WalkingWith/Carrying/Talk*/DoorPause/QueueSlot resets, unconditional ClearStranded), then `Responding = true; RespondTarget = scene; Destination = PlaceId.None;`. Works from `Asleep` — the plan is simply no longer consulted. No-op if already Responding or Downed or AwayUntilMinute != 0.
- `Release(who)`: `Responding = false; Destination = PlaceId.None;` (Revive's trick — the wants-check fires within the minute). No-op if not Responding.
- `Down(who)` on a responding officer must clear the assignment: add `_agents[i].Responding = false;` to `Down`'s cleanup list (the host sees the officer's `Responding` drop / `Downed` rise and reports `OfficerLost`).
- `Activity.Responding` appends AFTER `Downed` in `DayPlan.cs` with this doc: `/// Standing over the scene, or walking to it off-plan. Live state set only by Simulation.Respond; the tick loop never overwrites it. At the end of the enum — values are positional.`
- `Content/animations.txt` gains, next to the `downed` row, using ONLY clips already named by other rows (so no controller rebuild is needed — `AnimatorContractTests` stays green because every named state already exists):

```
# responding: an officer standing over a scene, or holding it. Clips are all ones other
# rows already carry, so the controller needs no rebuild for this row.
responding      Standing Idle, Looking Around, Weight Shift
```

  ONE ROW IS ONE LINE — no wrapping (the file's own header rule).
- `VillageUI.Verb` gains `case Activity.Responding: return "standing over";` immediately after the `Downed` case. (Census's `default: outside++` absorbing Responding is correct — an officer at a scene is out.)

- [ ] **Step 1: Write the failing tests** (Queueham fixture):

```csharp
[Test] public void ARespondingCitizenWalksToTheTileAndStandsThere()
    // Respond(citizen 1, a road tile ~30 tiles from their position). Tick up to 20 sim
    // minutes; assert Position.ToTile() == target, Doing == Activity.Responding,
    // Heading == Vec2.Zero, Travelling == false.

[Test] public void ReleaseHandsThemBackToThePlanPromptly()
    // After arrival, Release; within ten sim minutes Doing != Activity.Responding
    // (the ARevivedAgentDepartsPromptlyEvenMidBlock assertion pair, reused).

[Test] public void RespondWorksFromAsleep()
    // Construct the sim at startMinuteOfDay = 2*60 (02:00). Find an agent whose
    // Doing == Activity.Asleep. Respond them to a nearby road tile; assert they arrive.

[Test] public void DownClearsAResponse()
    // Respond, then Down the same citizen mid-walk: Responding is false, Doing is Downed.

[Test] public void NobodyRespondingIsByteIdenticalToBefore()
    // The lockstep clone again, for the new tick-loop gate.
```

- [ ] **Step 2: Run to verify failure.**
- [ ] **Step 3: Implement** — enum member + animations row FIRST (the gate pairs them), then the sim code, then the VillageUI case.
- [ ] **Step 4: Run the new tests, `EveryActivityHasARowInTheRealFile`, `AnimatorContractTests`, then the FULL gate.**
- [ ] **Step 5: Commit** (one commit — the enum and the row must land together): `Sim.Respond/Release: the first off-plan walk — an officer goes where no plan sends him`.

---

### Task 6: The testimony interval — `[downedFrom, backFrom)` (Core, Witness)

**Files:**
- Modify: `Assets/Noir/Core/Witness/Interruptions.cs`, `Assets/Noir/Core/Witness/Recollection.cs`
- Test: `tools/Noir.Core.Tests/EventTestimonyTests.cs` (extend — the downed-witness tests live here and in the person-arm's test file; put the new ones beside `ADownedWitnessTestifiesToNothingFromThatMinuteOn`)

**Interfaces:**
- Consumes: the two identical gates — person arm `Recollection.cs:79` (`if (minute >= downedFrom) { inSight = false; continue; }`), event arm `Recollection.cs:164` (`if (minute >= downedFrom) return;`).
- Produces: `IInterruptions` gains `int BackFromMinute(CitizenId who)` (`int.MaxValue` = never came back). The silenced window is `downedFrom <= minute < backFrom`. Task 9's `SimInterruptions` implements both.

**Contract (state it in the interface doc):** the host answers with ONE window per citizen. On a re-hit of a returned survivor the host widens it — earliest down-minute, latest back-minute (or MaxValue) — which silences the between-hits stretch too. Conservative and honest: a twice-struck witness says less, never more.

- [ ] **Step 1: Write the failing tests** — beside the existing downed-witness test, using its same fixture and a stub:

```csharp
private sealed class Interval : IInterruptions
{
    public int From = int.MaxValue, Back = int.MaxValue;
    public int DownedFromMinute(CitizenId who) => From;
    public int BackFromMinute(CitizenId who) => Back;
}

[Test] public void AReturnedSurvivorTestifiesAboutLifeAfterTheirReturn()
    // Arrange the existing downed-witness scenario, but Back = downMinute + 60.
    // Track minutes AFTER Back produce sightings again; minutes inside the window do not;
    // minutes before From still do.

[Test] public void ADeadWitnessStaysSilentForever()
    // Back = int.MaxValue reproduces the existing behavior exactly — assert the same
    // outcome the current ADownedWitnessTestifiesToNothingFromThatMinuteOn asserts.

[Test] public void TheEventArmHonoursTheSameWindow()
    // A hit recorded after the witness's Back minute IS witnessed; one inside the
    // window is not; one before From is (the victim's own pre-hit day still testifies).
```

- [ ] **Step 2: Run to verify failure** — the stub won't compile until the interface grows.
- [ ] **Step 3: Implement** — in `Interruptions.cs` add below `DownedFromMinute`:

```csharp
/// <summary>Minutes since the simulation began when they came back — the ambulance's
/// survivor walking home — or int.MaxValue if they never did (the dead, and everyone
/// never taken). The silenced window is [DownedFromMinute, BackFromMinute): one window
/// per citizen; a re-hit widens it (earliest down, latest back), which silences the
/// between-hits stretch too — a twice-struck witness says less, never more.</summary>
int BackFromMinute(CitizenId who);
```

In `Recollection.cs`, both arms compute one more value beside `downedFrom`:

```csharp
int backFrom = interruptions?.BackFromMinute(who.Id) ?? int.MaxValue;
```

and the two gates become (person arm / event arm respectively):

```csharp
if (minute >= downedFrom && minute < backFrom) { inSight = false; continue; }
...
if (minute >= downedFrom && minute < backFrom) return;   // silenced while down or away
```

- [ ] **Step 4: Run the new tests, then the FULL gate** — every existing interruption test must stay green (they pass `null` or a From-only... note: any existing test stub implementing `IInterruptions` gains the new member — implement it as `=> int.MaxValue` there; the compiler will list them).
- [ ] **Step 5: Commit** — `Testimony interval: a survivor testifies again after they walk home; the dead stay silent`.

---

### Task 7: `Discovery` — who can see the body (Core, Witness)

**Files:**
- Create: `Assets/Noir/Core/Witness/Discovery.cs`
- Test: `tools/Noir.Core.Tests/DiscoveryTests.cs`

**Interfaces:**
- Consumes: `DayPlanner.Plan`, `Sightlines.HowGoodALook`/`SawAnythingAtAll`, `GameClock.TickAt`, `IInterruptions` (Task 6's shape), `INightWitnesses`, `ISightBlocked`.
- Produces: a small class the host owns one of per case (it caches plans per day — 1,300 `DayPlanner.Plan` calls per scanned minute would not be free, once per day is):

```csharp
public sealed class Discovery
{
    /// <summary>The first citizen who can see this tile at this minute, by the SAME optics
    /// testimony runs on, or CitizenId.None. One optics, two consumers: whoever discovers
    /// the body is exactly somebody who could testify to the scene — a stationary witness
    /// at the door of the place their plan has them, in this light, at this range. A
    /// passer-by mid-walk does NOT discover it, because a passer-by cannot testify either
    /// (Recollection skips TravellingTo) — the deliberate consequence of one optics.</summary>
    public CitizenId WhoSees(WorldModel world, Population population, int day, int minuteOfDay,
                             Tile body, ulong seed,
                             INightWitnesses nightWitnesses = null,
                             IInterruptions interruptions = null,
                             ISightBlocked blocked = null);
}
```

**Implement as the event arm's gate sequence, verbatim order** (`Recollection.cs:146-184` is the reference): per citizen — skip the interruption window (`downedFrom <= minute < backFrom` — the victim and any taken-away citizen never discover anything); `plan.At(minuteOfDay)`; skip `TravellingTo`; skip `Asleep` unless `nightWitnesses.AwakeEnough`; skip invalid `Where`; `watcher = world.GetPlace(block.Where).Door`; `when = new GameClock(GameClock.TickAt(day, minuteOfDay))`; `clarity = Sightlines.HowGoodALook(watcher, body, when, who)`; `if (!Sightlines.SawAnythingAtAll(clarity, watcher, body, when, blocked)) continue;` — first pass wins, lowest citizen index (deterministic). The per-day plan cache: `Dictionary<int, DayPlan>` keyed by citizen index, cleared when `day` changes.

- [ ] **Step 1: Write the failing tests** — reuse whatever fixture the existing `EventSightingTests` build (world + population + a hit tile) and assert:

```csharp
[Test] public void TheDiscovererIsExactlyAWitnessTheEventArmWouldCredit()
    // For a body tile near a house at noon: WhoSees returns some citizen C; then run
    // Recollection.WhatTheySawOfEvents for C against a HitEvents holding a hit at that
    // tile/minute and assert C holds a sighting. And for a citizen WhoSees skipped
    // (asleep, or beyond NeverBeyond), assert the event arm returns nothing either.
    // One optics, proven both directions.

[Test] public void ABodyOnAnEmptyRangeIsNotDiscovered()
    // A tile > Sightlines.NeverBeyond (60) from every occupied door at that minute
    // returns CitizenId.None.

[Test] public void TheVictimNeverDiscoversTheirOwnBody()
    // interruptions stub with From = the scanned minute: that citizen is skipped even
    // if their door overlooks the tile.
```

- [ ] **Step 2: Run to verify failure.**
- [ ] **Step 3: Implement** per above. Lives in `Assets/Noir/Core/Witness/` — inside the firewall (the grep exempts `/Core/Witness/` paths; the asmdef needs no new references).
- [ ] **Step 4: Run the new tests, then the FULL gate.**
- [ ] **Step 5: Commit** — `Discovery: a body is found by exactly whoever could testify to it`.

---

### Task 8: The precinct rota — a standing ruling lands (Content + Core test)

**Files:**
- Modify: `Content/kinds.txt` (the precinct row at :481-497, and the stale `Only the mill.` header comment at :55)
- Test: `tools/Noir.Core.Tests/PrecinctRotaTests.cs` (create)

**Interfaces:**
- Consumes: `DayPlan.ShiftFor`'s split branch (`SplitShifts && todays.Count > 1`, `DayPlan.cs:734`), `Citizen.Shift` (byte window index), the kinds hours syntax (`06:00-14:00@mon-fri 14:00-22:00@mon-fri` — the mill at kinds.txt:226 is the model).
- Produces: a precinct that staffs 4 across two real watches; Task 13's officer selection depends on `People.WorkersAt(precinct)` returning 4 and on off-watch officers being at home overnight.

**The edit** (owner ruling, `SIM-FIXES.md:440`: "four officers, TWO watches, and one on call from home overnight"). Replace the precinct row's three lines:

```
  hours     00:00-23:59
  jobs      12
```
becomes
```
  hours     06:00-18:00 18:00-23:59
  jobs      4
```
(`roles sergeant officer` and `shifts split` stay — slot 0 is the sergeant, the rest officers; two windows make the split branch fire for the first time at this place.) **The midnight trap is real:** a `Block` may not cross midnight (`DayPlan.cs:78-80` throws on `end > 1440`), so the evening watch ends 23:59 and the OVERNIGHT is nobody-on-duty — the on-call man asleep at home, woken by `Respond` (Task 5 proved Respond works from Asleep). That is the ruling, mechanized. Also fix the header comment at kinds.txt:55: `shifts ... Only the mill.` → `... The mill, the factory and the precinct.`

- [ ] **Step 1: Write the failing test** — the `EveryActivityHasARowInTheRealFile` pattern (read the real file from `RepoRoot()`):

```csharp
[Test]
public void ThePrecinctRunsFourOfficersOnTwoWatches()
{
    string kinds = File.ReadAllText(Path.Combine(RepoRoot(), "Content", "kinds.txt"));
    // Cut the precinct block: from "kind precinct" to the next "kind ".
    var block = ...;
    StringAssert.Contains("jobs      4", block,
        "the owner ruled four officers (SIM-FIXES.md:440); 12 on one 24h window is the known-wrong staffing");
    Assert.That(Regex.Matches(Regex.Match(block, @"hours\s+(.*)").Groups[1].Value,
                              @"\d\d:\d\d-\d\d:\d\d").Count, Is.EqualTo(2),
        "two watch windows, so ShiftFor's split branch actually fires");
    StringAssert.Contains("shifts    split", block);
}
```

- [ ] **Step 2: Run to verify failure** (the file still says 12).
- [ ] **Step 3: Make the edit.** Content is hand-edited by design here — this row IS the ruling's home.
- [ ] **Step 4: Run the new test, then the FULL gate** — population generation and any staffing-sensitive test must stay green; if a fixture-count test moves, read it before touching it (the fixtures declare their own places and may not contain a precinct at all).
- [ ] **Step 5: Commit** — `Precinct rota: four officers, two watches, the overnight man on call at home (ruled 2026-08-09)`.

---

### Task 9: Host victim records + severity (Unity, VillageHost + Player)

**Files:**
- Modify: `Assets/Noir/Unity/VillageHost.cs` (`_downedAtMinute` → records; `SimInterruptions`; `CarStruckSomebody`), `Assets/Noir/Unity/Player.cs` (pass impact speed), `Assets/Noir/Unity/Noir.Unity.asmdef` (add `"Noir.Core.Response"` to references — nothing pins this list)
- Test: compile-level only in this task (`dotnet build Noir.Unity.csproj -c Debug` — after opening Unity once so the generated csproj sees the new asmdef); behavior lands under the Core-tested machine and Task 14's scenario.

**Interfaces:**
- Consumes: `ResponseCases.FatalSpeed` (Task 2), `IInterruptions.BackFromMinute` (Task 6).
- Produces: `CarStruckSomebody(CitizenId victim, Vector3 at, float speed)` (signature change — Player is the only caller); a private `struct VictimRecord { public int DownedFrom; public int BackFrom; public bool Fatal; }` in `Dictionary<int, VictimRecord> _victims` replacing `_downedAtMinute`; `public void VictimReturned(CitizenId who, int minute)` setting `BackFrom` (Task 13 calls it when ordering `TakeBodyAway` for a survivor — the return minute is known at order time).

**The edits:**

1. Replace the `_downedAtMinute` field (VillageHost.cs:1213) with:

```csharp
/// <summary>Which minute each downed citizen went down, whether it killed them, and when
/// they came back — the sim knows WHO, this knows WHEN. Phase 2's police consume it, and
/// Recollection's IInterruptions answers from it: silenced while [DownedFrom, BackFrom).
/// A re-hit of a returned survivor widens the window (earliest down, latest back).</summary>
private struct VictimRecord { public int DownedFrom; public int BackFrom; public bool Fatal; }
private readonly Dictionary<int, VictimRecord> _victims = new Dictionary<int, VictimRecord>();
```

2. `SimInterruptions` implements both members:

```csharp
public int DownedFromMinute(CitizenId who) =>
    _host._victims.TryGetValue(who.Value, out var r) ? r.DownedFrom : int.MaxValue;
public int BackFromMinute(CitizenId who) =>
    _host._victims.TryGetValue(who.Value, out var r) ? r.BackFrom : int.MaxValue;
```

3. `CarStruckSomebody` gains the speed parameter and the record write (everything else — the guards, `Sim.Down`, the `_hitEvents.Record` line, the log — stays byte-identical):

```csharp
public void CarStruckSomebody(CitizenId victim, Vector3 at, float speed)
{
    ...existing guards, Sim.Down, minute computation, _hitEvents.Record...
    bool fatal = speed >= ResponseCases.FatalSpeed;
    _victims.TryGetValue(victim.Value, out var was);   // default: DownedFrom=0 → treat absent
    _victims[victim.Value] = new VictimRecord
    {
        DownedFrom = _victims.ContainsKey(victim.Value) ? Math.Min(was.DownedFrom, minute) : minute,
        BackFrom   = int.MaxValue,
        Fatal      = fatal,
    };
    ...existing Debug.Log, extended with fatal/speed...
}
```

4. `VictimReturned(CitizenId who, int minute)`: fetch, set `BackFrom = minute`, store back.
5. Player's call site (`Player.cs:390` area, inside `SweepForVictims`): `_host.CarStruckSomebody(new CitizenId(i), p, Mathf.Abs(_carSpeed));` — a direct PlayMode test call has `_carSpeed == 0`, lands as non-fatal, which is what the existing teardown's `Revive` expects.
6. `Noir.Unity.asmdef` references gain `"Noir.Core.Response"`.

- [ ] **Step 1: Make the edits** (no failing-test-first here — this task is glue whose behavior is pinned by Task 2's Core tests and Task 14's scenario; the compile is the check).
- [ ] **Step 2: Open Unity once (or run any `-batchmode -quit` command, editor closed) so the asmdef lands and csprojs regenerate; commit the generated `.meta` files for `Response/` alongside.**
- [ ] **Step 3: Verify** — `dotnet build Noir.Unity.csproj -c Debug` green; FULL Core gate still green (the WitnessFirewall grep must still find VillageHost as the one caller — nothing about that changed).
- [ ] **Step 4: Commit** — `VillageHost: victim records with fatality and return, severity on the one recording seam`.

---

### Task 10: The stationary-obstacle seam (Unity, CityTraffic + Player)

**Files:**
- Modify: `Assets/Noir/Unity/CityTraffic.cs` (registry + `Blocked`), `Assets/Noir/Unity/Player.cs` (register on leave, unregister on enter)

**Interfaces:**
- Produces: on `CityTraffic`:

```csharp
/// <summary>Stationary things ambient cars must not drive through — the player's parked
/// car, a response vehicle standing at a scene. Phase 1 named this seam and deferred it
/// ("a second obstacle source those five queries consult"); this is the minimal landing:
/// FOLLOWING only. Moving obstacles stay invisible, documented as ever.</summary>
public readonly List<Transform> Obstacles = new List<Transform>();
```

Task 11's `CityResponse` adds its parked vehicles here; `Player` adds `_lastCar` on `LeaveCar` and removes it on `EnterCar`/`ReenterLastCar`.

**The `Blocked` edit** — append a second loop after the `_movers` loop (before `return false`), treating each obstacle as a stationary pseudo-car with the default `Reach` (2.2f). No heading dot-test: a stationary obstacle blocks whatever way it faces.

```csharp
for (int j = 0; j < Obstacles.Count; j++)
{
    var what = Obstacles[j];
    if (what == null) continue;
    var gap = what.position - here;
    float ahead = Vector3.Dot(gap, me.Forward);
    if (ahead <= 0f) continue;
    if (ahead > me.Reach + 2.2f + Headway) continue;
    if (Vector3.Cross(me.Forward, gap).magnitude > LookWide) continue;
    return true;
}
```

Player edits: in `LeaveCar` (after `_lastCar = _car;`): `_host.Traffic?.Obstacles.Add(_lastCar.transform);` and in `SitIn` (the shared enter tail): `_host.Traffic?.Obstacles.Remove(car.transform);` — check whether `VillageHost` exposes `Traffic`; if it does not, add `public CityTraffic Traffic => _traffic;` beside `Driveways`.

- [ ] **Step 1: Make the edits.**
- [ ] **Step 2: Verify compile** (`dotnet build Noir.Unity.csproj -c Debug`).
- [ ] **Step 3: Commit** — `Stationary obstacles: ambient traffic queues behind a parked player car or a held scene (Phase 1's named seam, minimal landing)`. NOTE in the commit body: the traffic PlayMode gates must be run TWICE before believing any number they move (standing rule), done at Task 15.

---

### Task 11: `CityResponse` — the vehicles (Unity)

**Files:**
- Create: `Assets/Noir/Unity/CityResponse.cs`
- Modify: `Assets/Noir/Unity/VillageHost.cs` (create it in the build path beside `CityTraffic.Create`, expose `public CityResponse Response { get; private set; }`)

**Interfaces:**
- Consumes: `CityTraffic.Graph`, `PointOn(int, float)` / `TurnArc(int, ...)` / `PointInTurn(int, float)` (the public wrappers), `LaneRoutes.Plan`/`NearestSegment` (Task 1), `CityTraffic.Obstacles` (Task 10).
- Produces (Task 13 drives it; Task 12 adds the officer actor):

```csharp
public sealed class CityResponse : MonoBehaviour
{
    public static CityResponse Create(WorldModel world, Transform parent, CityTraffic traffic);
    public enum Rig { County, Ambulance }
    /// <summary>Spawn (editor-only prefab; audible when not drawable) at the named map
    /// edge of Route 1 and drive the lane graph to the scene. edgeSouth: county true,
    /// ambulance false. Fires onArrived once, parked beside the scene.</summary>
    public void DriveIn(Rig rig, bool edgeSouth, Tile scene, System.Action onArrived);
    /// <summary>Reverse: drive from the scene back off the same edge and despawn.</summary>
    public void Depart(Rig rig, bool edgeSouth);
    public bool Arrived(Rig rig);
}
```

**Design decisions locked here:**
- **Sim time.** The component tracks `_lastTick = host.Sim.Clock.Tick` and advances every `Update` by `dtSim = (nowTick - _lastTick) / (float)GameClock.TicksPerSecond`, so fast-forward compresses the drive and pause stops it. Speed const `ResponseSpeed = 10f` m/s (a shade over ambient's 8 — an official car moving with purpose, under the player's 12).
- **Never in `_movers`.** Each rig is a private struct `{ Transform what; List<int> turns; int leg; int segment; float s; float t; bool inTurn; float stopAtS; bool arrived; }` mirroring `Mover`'s shape. Movement mirrors `RunSegment`/`CrossJunction`'s arithmetic against the PUBLIC wrappers: advance `s` by `ResponseSpeed * dtSim`; at `segment.ToS` enter the next planned turn (`t` 0→1, arc length approximated as the chord `|a-c|` via `TurnArc` — good enough at junction scale); seat via `PointOn`/`PointInTurn` exactly as `Seat` does (position + `LookRotation` on the 1 m-ahead delta).
- **Spawn edge:** scan `Graph.Entries` for the entry whose `PointOn(entry, FromS)` has the largest world `-z` (village y — south) or smallest (north), restricted to entries whose road's `Aadt` is the highest at that edge (Route 1 carries the county's 5,200 — in practice: pick the southmost/northmost entry outright; Route 1 owns both edges per `roads.txt:211`).
- **Stop at the scene:** destination segment+`stopAtS` from `LaneRoutes.NearestSegment(graph, world.Roads, Vec2.CentreOf(scene) …)`; ease the nose to `stopAtS` with `RunSegment`'s own arithmetic (`allowed = max(0, toStop - Reach - 0.4)`); on stop, offset the parked transform half a lane width toward the verge, add it to `traffic.Obstacles`, set `arrived`, fire the callback once.
- **Ease behind traffic:** before advancing, run the same box test `Blocked` uses against `traffic`'s public state — CityTraffic gains ONE small accessor for this (`public bool AnyMoverWithin(Vector3 pos, Vector3 forward, float reach)` wrapping the mover loop's gap arithmetic), rather than exposing `_movers`. Response cars also test `Obstacles` (skipping themselves).
- **Prefabs:** `#if UNITY_EDITOR` `AssetDatabase.LoadAssetAtPath<GameObject>` with the exact `CityBuildings.Fleet` constants — `"Assets/polyperfect/Poly Universal Pack/Prefabs/Cars/Cars City/Car_Police_Modern.prefab"` and `...Car_Ambulance_Modern.prefab` (the county sheriff wears the pack's one cruiser; a bespoke county livery is not in the pack and not worth chasing). `#else` the rig runs INVISIBLE but real (a bare `new GameObject`), and `Create` logs once: `[response] N actors drawn, M invisible (editor-only prefabs)` — audible, never silent (spec §7).
- `Depart`: plan from the parked segment to the same edge's EXIT segment (scan `Segments` for `IsExit` at that edge — there is no precomputed exit list), remove from `Obstacles`, destroy at the end.

- [ ] **Step 1: Write `CityResponse`** per above (no Core test can see it; Task 14 is its test, plus looking at it).
- [ ] **Step 2: Wire creation** in `VillageHost`'s build path directly after `CityTraffic.Create(...)`: `Response = CityResponse.Create(World, transform, _traffic);`.
- [ ] **Step 3: Verify compile**, editor-open once for metas, commit metas too.
- [ ] **Step 4: Commit** — `CityResponse: a county car and an ambulance that drive the lane graph on sim time, outside _movers`.

---

### Task 12: `CityResponse` — the county officer actor and the canvass walk (Unity)

**Files:**
- Modify: `Assets/Noir/Unity/CityResponse.cs`

**Interfaces:**
- Consumes: `Pathfinder` (`new Pathfinder(world.Grid)` — the FIRST Unity-side owner; hold ONE instance for the component's lifetime, its scratch buffers are the point), `Space3D.ToWorld(Vec2)`, `TileGrid.MoveCost`, `CityTraffic` unused here.
- Produces (Task 13 drives it):

```csharp
/// <summary>Walk the county officer from wherever he stands to this tile; fires
/// onArrived once. He exists only while his car is at a scene.</summary>
public void WalkCountyTo(Tile door, System.Action onArrived);
public Vector3? CountyOfficerAt { get; }   // null when not spawned
```

**Design decisions locked here:**
- The officer is a Unity-side actor, NOT a sim citizen (the agent array is fixed at construction — `Simulation.cs:274`). He spawns beside the parked county car when `DriveIn(County, ...)` completes and despawns with `Depart(County, ...)`.
- **Figure:** `#if UNITY_EDITOR` load one adult-male pack prefab by direct path (pick one from `AgentBody`'s Men cast folder, `Assets/polyperfect/Poly Universal Pack/Prefabs/People/...` — read `AgentBody.cs:204`'s list and take the first name that resolves); `#else` invisible-but-real, counted in the `[response]` audibility line.
- **Walk:** sim-time, the sim's own formula reproduced (`Simulation.cs:1080-1097`): `speed = 1.35f / cost` where `cost = grid.MoveCost(currentTile)` (clamped as the sim clamps), stepped along the `Pathfinder.FindPath` tile list (result runs from just-after-from through to inclusive), positioned via `Space3D.ToWorld(pos)` each frame. Plain `FindPath`, never the alley variant. `PathOutcome.NoRouteExists` → report arrival anyway after a 2-sim-minute stand (a door the grid cannot reach must not wedge the whole case — the machine's dwell then runs; log `[response] no walkable route to door (x,y), canvassing from the kerb`).
- Trespass pricing already favors this walk: FindPath exempts the two ENDPOINT lots, so a door-targeted walk enters that yard lawfully with zero extra plumbing.

- [ ] **Step 1: Implement** per above.
- [ ] **Step 2: Verify compile; commit** — `CityResponse: the county officer walks the real grid, door to door`.

---

### Task 13: Host glue — discovery scan, machine tick, order execution (Unity, VillageHost)

**Files:**
- Modify: `Assets/Noir/Unity/VillageHost.cs`

**Interfaces:**
- Consumes: everything — `ResponseCases` (Tasks 2/3), `Discovery` (7), `Sim.Respond/Release/TakeAway` (4/5), `VictimRecord`/`VictimReturned` (9), `CityResponse.DriveIn/Depart/WalkCountyTo` (11/12), `People.WorkersAt`, `PlaceKindTable.Current.TryKindOf("precinct", out var kind)` + `World.PlacesOfKind(kind)` (NEVER a hardcoded PlaceKind — precinct is table-declared, `Place.cs:7-26` proves it is not an enum member).
- Produces: `public ResponseCases Cases => _cases;` (Task 14 asserts on it) and the `[case]` log lines.

**The edits, in `VillageHost.cs` (the one legal Witness caller — everything here may name `Recollection`, `Discovery`, `HitEvents`):**

1. Fields: `_cases = new ResponseCases()`, `_discovery = new Discovery()`, `_caseOrders = new List<CaseOrder>()`, `_caseLog = new List<string>()`, `_responseAt = -1` (minute guard), plus per-case bookkeeping the machine cannot hold: `_officerWalking` (caseId → officer being polled for arrival).

2. `CarStruckSomebody` additionally opens the case (after the record write):

```csharp
int caseId = _cases.Open(victim, minute, Space3D.TileAt(at),
                         _player != null ? _player.CarTone : CarTone.Unnoticed,
                         _player != null ? _player.CarShape : CarShape.Unnoticed,
                         speed >= ResponseCases.FatalSpeed);
```

3. A `RunResponse()` called from `Update`'s once-per-minute slot, the `_drivewaysAt` stash pattern exactly (`VillageHost.cs:1329-1342` is the model — its own guard field `_responseAt`, keyed on `Sim.Clock.MinuteOfDay`):

```csharp
private void RunResponse()
{
    int minuteOfDay = Sim.Clock.MinuteOfDay;
    int minute = Sim.Clock.Day * (GameClock.TicksPerDay / GameClock.TicksPerMinute) + minuteOfDay;

    // 1. Discovery, for every undiscovered case, testimony's own optics.
    for (int c = 0; c < _cases.Count; c++)
    {
        if (_cases.StateOf(c) != CaseState.Undiscovered) continue;
        _interruptions ??= new SimInterruptions(this);
        var saw = _discovery.WhoSees(World, People, Sim.Clock.Day, minuteOfDay,
                                     _cases.SceneOf(c), Seed, null, _interruptions, null);
        if (saw.IsValid) _cases.BodySeen(c, minute, saw);
    }

    // 2. Arrival polling for the walking officer (the machine never predicts travel).
    // _officerWalking is Dictionary<int, CitizenId> (caseId → officer); copy keys before
    // mutating. NowMinute() is the absolute-minute expression, extracted once as a helper:
    //   private int NowMinute() =>
    //       Sim.Clock.Day * (GameClock.TicksPerDay / GameClock.TicksPerMinute)
    //       + Sim.Clock.MinuteOfDay;
    _walkingKeys.Clear(); _walkingKeys.AddRange(_officerWalking.Keys);
    foreach (int c in _walkingKeys)
    {
        var officer = _officerWalking[c];
        var a = Sim.GetAgent(officer);
        if (a.Downed) { _cases.OfficerLost(c); _officerWalking.Remove(c); continue; }
        if (a.Responding && !a.Travelling && a.Doing == Activity.Responding
            && a.Position.ToTile() == _cases.SceneOf(c))
        { _cases.OfficerArrived(c, minute); _officerWalking.Remove(c); }
    }

    // 3. The machine's own minute.
    _caseOrders.Clear();
    _cases.Tick(minute, _caseOrders);
    foreach (var order in _caseOrders) Execute(order, minute);

    // 4. The greppable record.
    _caseLog.Clear();
    _cases.DrainLog(_caseLog);
    foreach (var line in _caseLog) Debug.Log("[case] " + line);
}
```

4. `Execute(CaseOrder order, int minute)` — a switch:
- `DispatchOfficer`: select the officer — `TryKindOf("precinct")` → `PlacesOfKind` → first place → `People.WorkersAt(placeId)`; among them prefer one whose live `Doing == Activity.AtWork` (on watch), else one `Asleep` (the on-call man, woken), else any not Downed/taken/responding; skip all when none remain (log `[case] no officer left standing`; the machine re-emits next minute because `OfficerDispatched` never arrived — NOTE: guard re-emission by only re-emitting DispatchOfficer when `OfficerOf == None`, which Task 3's OfficerLost path already provides). Then `Sim.Respond(officer, scene)`, `_cases.OfficerDispatched(order.Case, officer)`, remember in `_officerWalking`.
- `CountyCarIn`: `Response.DriveIn(Rig.County, edgeSouth: true, scene, onArrived: () => { _cases.CountyArrived(order.Case, NowMinute()); _cases.CanvassBegins(order.Case, CanvassListFor(order.Case)); });`
- `CanvassNext`: `Response.WalkCountyTo(DoorOf(order.Who), onArrived: () => _cases.CountyReachedDoor(order.Case, NowMinute(), order.Who, AskWhatTheySaw(order.Who, DayOfHit(order.Case))));` — the canvass records the SAME strings the player's T key gets, for the hit's day.
- `AmbulanceIn`: `Response.DriveIn(Rig.Ambulance, edgeSouth: false, scene, onArrived: () => _cases.AmbulanceArrived(order.Case, NowMinute()));`
- `TakeBodyAway`: `int back = _cases.ReturnMinuteOf(order.Case); Sim.TakeAway(order.Who, back); if (back != int.MaxValue) VictimReturned(order.Who, back);` (the record's BackFrom is the return minute — testimony resumes only after they are actually home, which the sim's Return enforces at that same minute).
- `ReleaseOfficer`: `Sim.Release(order.Who);`
- `VehiclesLeave`: `Response.Depart(Rig.County, true); Response.Depart(Rig.Ambulance, false);`

5. `CanvassListFor(int caseId)` — who holds an event sighting of the hit's day (the one legal caller doing the one legal thing):

```csharp
private CitizenId[] CanvassListFor(int caseId)
{
    _interruptions ??= new SimInterruptions(this);
    int day = _cases.MinuteOf(caseId) / 1440;      // MinuteOf is on Task 2's read surface
    var list = new List<CitizenId>();
    for (int i = 0; i < People.Count; i++)
    {
        var id = new CitizenId(i);
        if (id.Value == _cases.VictimOf(caseId).Value) continue;
        var events = Recollection.WhatTheySawOfEvents(World, People, People.Get(id), day,
                                                      _hitEvents, Seed, null, _interruptions, null);
        if (events.Length > 0) list.Add(id);
    }
    return list.ToArray();
}
```

(`DayOfHit(caseId)` in the Execute switch is `_cases.MinuteOf(caseId) / 1440`; `DoorOf(who)` is `World.GetPlace(People.Get(who).Home).Door`.)

- [ ] **Step 1: Make the edits.** All inside `VillageHost.cs` — the firewall's caller list does not move.
- [ ] **Step 2: Verify compile + FULL Core gate** (WitnessFirewall's grep must still pass: `CityResponse.cs` and `ResponseCases.cs` must not contain `Noir.Core.Witness` in code).
- [ ] **Step 3: Commit** — `VillageHost: the response loop — discovery, dispatch, canvass, removal, all in the one legal seam`.

---

### Task 14: The PlayMode scenario (Noir.PlayTests)

**Files:**
- Create: `Assets/Noir/PlayTests/ResponsePlayTests.cs`

**Interfaces:**
- Consumes: `host.Cases` (Task 13), `host.Sim`, `player.SweepForVictims` (two-call seeding), `CityUnderTest.WaitUntilBuilt`, the `WitnessPlayTests` SpeedIndex save/boost/restore pattern.

**Conventions that bind this test** (all measured, all already paid for): the city is built once and shared — restore EVERYTHING via static fields in a `[UnityTearDown]`; observe-don't-assume (never hardcode "citizen 0", find an outdoor victim near an occupied door the way `AHitDownsSomebodyAndTheBodyStays` finds one); assert on ACCESSOR STATE, never on log text (no `LogAssert` precedent in this suite — the `[case]` lines are echoed for humans, the assertions read `host.Cases`); `[UnityTest, Timeout(900000)]`; sim time is unscaled real time — run at 300× (`host.SpeedIndex = VillageHost.Speeds.Length - 1`).

- [ ] **Step 1: Write the test**

```csharp
public class ResponsePlayTests
{
    private static int _victim = -1;
    private static int _caseId = -1;
    private static int _wasSpeed = -1;

    [UnitySetUp]
    public IEnumerator Ready() { Time.timeScale = 1f; yield return CityUnderTest.WaitUntilBuilt(); }

    [UnityTearDown]
    public IEnumerator EverythingBack()
    {
        var host = Object.FindFirstObjectByType<VillageHost>();
        var player = Object.FindFirstObjectByType<Player>();
        if (player != null && player.Walking) player.Toggle();
        if (host != null && host.Sim != null)
        {
            if (_caseId >= 0)
            {
                var officer = host.Cases.OfficerOf(_caseId);
                if (officer.IsValid) host.Sim.Release(officer);
            }
            if (_victim >= 0)
            {
                host.Sim.Return(new CitizenId(_victim));   // no-op unless taken away
                host.Sim.Revive(new CitizenId(_victim));   // no-op unless still downed
            }
            if (_wasSpeed >= 0) host.SpeedIndex = _wasSpeed;
        }
        _victim = -1; _caseId = -1; _wasSpeed = -1;
        yield break;
    }

    /// <summary>The whole response, compressed: a hit in view of a door runs discovery →
    /// officer → county → canvass → ambulance → removal, and the case file is real.</summary>
    [UnityTest, Timeout(900000)]
    public IEnumerator AWitnessedHitBringsTheTownsWholeResponse()
    {
        var host = Object.FindFirstObjectByType<VillageHost>();
        var sim = host.Sim;
        var player = Object.FindFirstObjectByType<Player>();

        // A victim who is outdoors AND within testimony range (< 55 tiles, inside
        // Sightlines.NeverBeyond = 60 with margin) of some other citizen's occupied
        // door — otherwise discovery legitimately never fires. Walk the census for a
        // candidate; skip Downed/AwayFromTown/Indoor exactly as the sweep does.
        int victim = -1;
        for (int i = 0; i < sim.AgentCount && victim < 0; i++)
        {
            var a = sim.GetAgent(i);
            if (a.Downed || a.Doing == Activity.AwayFromTown) continue;
            if ((host.World.Grid.FlagsAt(a.Position.ToTile()) & TileFlags.Indoor) != 0) continue;
            // near an occupied door: any OTHER agent At a place whose door is close.
            for (int j = 0; j < sim.AgentCount; j++)
            {
                if (j == i) continue;
                var b = sim.GetAgent(j);
                if (b.Travelling || !b.At.IsValid) continue;
                if (b.Doing == Activity.Asleep) continue;
                var door = host.World.GetPlace(b.At).Door;
                if (Tile.ChebyshevDistance(door, a.Position.ToTile()) < 55) { victim = i; break; }
            }
        }
        Assert.That(victim, Is.GreaterThanOrEqualTo(0), "no witnessable victim in the noon town");

        int casesBefore = host.Cases.Count;
        player.Toggle();
        for (int f = 0; f < 5; f++) yield return null;
        var p = Space3D.ToWorld(sim.GetAgent(victim).Position);
        player.SweepForVictims(p + new Vector3(-3f, 0f, 0f), p + new Vector3(3f, 0f, 0f));
        player.SweepForVictims(p + new Vector3(-3f, 0f, 0f), p + new Vector3(3f, 0f, 0f));
        yield return null;

        Assert.That(sim.GetAgent(victim).Doing, Is.EqualTo(Activity.Downed));
        _victim = victim;
        Assert.That(host.Cases.Count, Is.EqualTo(casesBefore + 1), "the hit opened a case");
        _caseId = casesBefore;
        player.Toggle();

        _wasSpeed = host.SpeedIndex;
        host.SpeedIndex = VillageHost.Speeds.Length - 1;   // 300x

        // The full sequence is ~50-70 sim minutes ≈ 15-25 real seconds at 300x plus
        // walking/driving; poll state, not time. Give it four real minutes.
        float deadline = Time.time + 240f;
        var seen = new List<CaseState>();
        while (Time.time < deadline && host.Cases.StateOf(_caseId) != CaseState.Closed)
        {
            var s = host.Cases.StateOf(_caseId);
            if (seen.Count == 0 || seen[seen.Count - 1] != s) seen.Add(s);
            yield return null;
        }
        host.SpeedIndex = _wasSpeed; _wasSpeed = -1;

        Assert.That(host.Cases.StateOf(_caseId), Is.EqualTo(CaseState.Closed),
            "the case never closed; states seen: " + string.Join(" → ", seen));
        // The order of what we saw is the order the machine promises.
        CollectionAssert.IsOrdered(seen.Select(s => (int)s), "states ran out of order");

        // The body is gone: the victim is away, not downed, and their figure is not drawn.
        var after = sim.GetAgent(victim);
        Assert.That(after.Downed, Is.False);
        Assert.That(after.Doing, Is.EqualTo(Activity.AwayFromTown));

        // The case file is real.
        Assert.That(host.Cases.FileOf(_caseId).Count, Is.GreaterThan(3),
            "a worked case files its transitions and its canvass");
        foreach (var line in host.Cases.FileOf(_caseId)) Debug.Log("[case-file] " + line);
    }
}
```

- [ ] **Step 2: Build the test assembly FIRST** — `dotnet build Noir.PlayTests.csproj -c Debug` (the cheapest four seconds in this project).
- [ ] **Step 3: Run the suite** (editor CLOSED; check for `Unity.exe` first):

```
Unity.exe -batchmode -projectPath C:\SerialKillerGame -runTests -testPlatform PlayMode ^
  -assemblyNames Noir.PlayTests -testCategory "!Diagnostic" -testResults <xml> -logFile <log>
```

Expected: 31 of 31 (the 30 baseline + this one), 1 skipped, budget ~3 extra minutes over the 172 s baseline. If red: read the states-seen message before touching anything — the machine names where it stopped.
- [ ] **Step 4: Commit** — `ResponsePlayTests: the whole response, watched once by the gate`.

---

### Task 15: Gates, look at it, docs, push

- [ ] **Step 1: Full Core gate in Release**, record the exact final count (526 + Tasks 1-8's additions; expect roughly +20).
- [ ] **Step 2: The three Unity builds** — `dotnet build Noir.Unity.csproj / Noir.Editor.csproj / Noir.PlayTests.csproj -c Debug` all green.
- [ ] **Step 3: The full PlayMode gate**, run TWICE if any traffic number moved (Task 10 touched `Blocked` — the standing rule says never believe a traffic number from one run).
- [ ] **Step 4: LOOK AT IT.** Drive Unity yourself (the editor workflow memory): enter Play, drive a car into somebody on Chicago Street mid-afternoon in view of houses, then stand on a porch at 10× and watch the whole thing: officer walks up, county car in from the south, door-knocks, ambulance from the north, body gone, everyone leaves. Multi-angle scene captures at the held scene → `docs/snapshots/response-*.png`. The tests cannot see ugly and none of this has ever been drawn.
- [ ] **Step 5: Docs, one home per fact:**
  - `CLAUDE.md`: new Core baseline count + PlayMode 31-of-31 baseline (replace, don't append); a one-line entry for the response under the load-bearing facts if it earns one.
  - `docs/CONTROLS.md`: under the car lines — "hit somebody where a window can see it and the town responds: an officer, the county, an ambulance. Phase 2 delivered; the police never look for you (yet)."
  - `docs/IDEAS.md`: tick the Story item at :923 with a pointer to the spec.
  - `docs/SIM-FIXES.md`: mark the precinct-staffing item landed (the :440 ruling).
  - Memory: update `drivable-car-landed.md` (Phase 2 landed) rather than a new file.
- [ ] **Step 6: Commit docs; push the branch** (the end-of-session rule).

---

## Execution notes for whoever runs this

- Tasks 1–8 need no Unity at all — pure `dotnet test` loops. Task 9 is the first that needs the editor opened once (asmdef + csproj regeneration + metas).
- **Do not edit any `.cs` while a batch run is going** (the 18-minutes-for-nothing trap). Queue edits.
- If the PlayMode scenario needs traffic and finds none: the sim opens at NOON (≈24 ambient cars) — that is the documented trap; observe the town, never assume.
- The response consumes no RNG anywhere. If a reviewer sees a `Substream` or `Rolls` call in response code, that is a finding.






