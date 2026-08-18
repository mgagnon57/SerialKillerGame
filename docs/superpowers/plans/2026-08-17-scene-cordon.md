# Scene Cordon Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the officer holds a scene, the street visibly closes around it — striped
barricades and tape go up, one lane stays open, and the officer stands at the pinch waving
one direction through at a time, on the sim clock, until the case closes and it all comes
down.

**Architecture:** Entirely host-side off states `ResponseCases` already broadcasts (cordon up
in the `OfficerArrived` arm, down in the `CaseState.Closed` arm, which covers `CloseLoudly`
for free). Traffic control is a per-segment hold-`s` clamp inside `CityTraffic.RunSegment` —
no lane-graph or junction change, no new `Mover` fields — with an alternation phase derived
from the sim tick in `double` seconds. Props are runtime primitives per the
`CityResponse.DressTheBar` precedent. The officer's wave is `Activity.DirectingTraffic`,
following `Activity.Gawking`'s exact enum + `animations.txt` + `RespondTests` pattern.

**Tech Stack:** C# — Noir.Core (People/Sim), Noir.Unity (CityTraffic, VillageHost, Player,
Materials3D, new SceneCordon), NUnit Core tests, one PlayMode assertion pass.

**Spec:** `docs/superpowers/specs/2026-08-17-scene-cordon-design.md`

## Global Constraints

- **No RNG anywhere.** The alternation phase is `TownTick / (double)GameClock.TicksPerSecond`
  — `double`, never `float` (CitySignals' own lesson: a week of sim seconds in a float loses
  the sub-second phase).
- **One clock.** Everything paces off the sim tick (`CityTraffic.TownTick` is already fed from
  `Sim.Clock.Tick`); a paused town has a frozen cordon. Nothing reads `Time.deltaTime` for
  town behavior.
- **The lane graph and junction topology are never mutated.** The PlayMode gate pins
  `2 signalised (8 heads)` — the cordon is not a signal and must not register as one.
- **No new fields on `Mover`.** Nine loops walk `_movers` (CityTraffic.cs:298-308 lists them);
  a held car stays an ordinary member. Cordon state lives beside the list, not on the car.
- **A cordon hold must NOT touch `me.Waited`/`me.Choices`** — those drive re-routing
  (`Rethink`), and a waved-through car must wait for the wave, not re-plan around it.
- **`Activity` is positional** ("values are positional" — DayPlan.cs:74): `DirectingTraffic`
  appends AFTER `Gawking`, at the end, and its `animations.txt` row lands in the SAME commit
  (`EveryActivityHasARowInTheRealFile` fails the Core gate otherwise). The row may only use
  clips other rows already carry (no `Townsfolk.controller` rebuild).
- **Cordon-down keys on `CaseState.Closed`**, in `RunResponse`'s existing arm — never on
  `VehiclesLeave` — so `CloseLoudly` and every teardown path lower it for free. Nothing may
  leak a barricade into the next test.
- Core tests in Release: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj`.
  Baseline going in: **580 pass, 0 fail, 8 skipped.** Unity verified by
  `dotnet build Noir.Unity.csproj -c Debug` (+ Editor, PlayTests) checking exit codes, never
  `| tail`. The PlayMode gate itself runs at the next editor-closed window, not per-task.
- Do not edit any `.cs` while a batch Unity run is going.

## File Structure

- Modify: `Assets/Noir/Core/People/DayPlan.cs` (enum tail, ~:75)
- Modify: `Content/animations.txt` (one row at the end)
- Modify: `tools/Noir.Core.Tests/RespondTests.cs` (two mirrored tests)
- Modify: `Assets/Noir/Unity/CityTraffic.cs` (`Hold.Cordon`; cordon state + `RaiseCordon`/
  `LowerCordon`/`CordonHolds`; `RunSegment` clamp; `Seat` lateral offset)
- Create: `Assets/Noir/Unity/SceneCordon.cs` (props: sawhorses + tape, per-case root, raise/lower)
- Modify: `Assets/Noir/Unity/Materials3D.cs` (three lazy materials)
- Modify: `Assets/Noir/Unity/VillageHost.cs` (`OfficerArrived` arm ~:1536, `Closed` arm ~:1562)
- Modify: `Assets/Noir/Unity/Player.cs` (`DriveStep` hold check, ~:255)
- Modify: `Assets/Noir/PlayTests/ResponsePlayTests.cs` (cordon latches in the scenario)

---

### Task 1: `Activity.DirectingTraffic` — the officer's wave exists in Core

**Files:**
- Modify: `Assets/Noir/Core/People/DayPlan.cs` (~:74, after `Gawking`)
- Modify: `Content/animations.txt` (append after the `gawking` row, ~:184)
- Test: `tools/Noir.Core.Tests/RespondTests.cs`

**Interfaces:**
- Consumes: `Simulation.Respond(CitizenId who, Tile scene, Activity standAs = Activity.Responding)`
  (Simulation.cs:537) — already generic over the stand-as; no Sim change needed.
- Produces: `Activity.DirectingTraffic` as the LAST enum member; row key `directingtraffic`.
  Task 4 relies on the exact member name.

- [ ] **Step 1: Write the failing tests** (in `RespondTests.cs`, mirroring
  `AStandAsActivityIsWornOnArrival` at :37 and
  `GawkingHasARowInTheRealFileUsingOnlyCarriedClips` at :77 — read both first; reuse the
  fixture's `Scene` tile and Queueham context exactly as they do):

```csharp
/// <summary>
/// The scene cordon's officer: Respond with a stand-as of DirectingTraffic must dress him
/// in it on arrival, the same contract Gawking proved. The spec is
/// docs/superpowers/specs/2026-08-17-scene-cordon-design.md.
/// </summary>
[Test]
public void AStandAsOfDirectingTrafficIsWornOnArrival()
{
    var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed);
    var who = new CitizenId(0);
    sim.Respond(who, Scene, Activity.DirectingTraffic);

    for (int i = 0; i < 20_000; i++)
    {
        sim.Tick();
        var a = sim.GetAgent(who);
        if (!a.Travelling && a.Position.ToTile() == Scene) break;
    }

    Assert.That(sim.GetAgent(who).Doing, Is.EqualTo(Activity.DirectingTraffic),
        "a stand-as of DirectingTraffic must be what they wear on arrival, not Responding");
}

/// <summary>
/// The directingtraffic row may only name clips other rows already carry — the same
/// no-controller-rebuild gate Gawking's row passes. A clip nothing else carries would
/// resolve to nothing until somebody reruns Build The Townsfolk Animator.
/// </summary>
[Test]
public void DirectingTrafficHasARowInTheRealFileUsingOnlyCarriedClips()
{
    string[] lines = System.IO.File.ReadAllLines(AnimationTableTests.RealFile());
    string row = null;
    var others = new System.Collections.Generic.List<string>();
    foreach (string line in lines)
    {
        string t = line.Trim();
        if (t.StartsWith("#") || t.Length == 0) continue;
        if (t.StartsWith("directingtraffic")) row = t.Substring("directingtraffic".Length);
        else others.Add(t);
    }
    Assert.That(row, Is.Not.Null, "no directingtraffic row in Content/animations.txt");
    foreach (string name in row.Split(','))
    {
        string clip = name.Trim();
        if (clip.Length == 0) continue;
        Assert.That(others.Exists(o => o.Contains(clip)), Is.True,
            "the directingtraffic row names '" + clip + "', which no other row carries - " +
            "that clip is not in the controller and would resolve to nothing");
    }
}
```

NOTE: mirror the EXACT mechanics of `GawkingHasARowInTheRealFileUsingOnlyCarriedClips`
(:77-105) for locating the file and splitting the row — if it uses a helper other than
`AnimationTableTests.RealFile()`, use what IT uses. The intent above is binding; the file
plumbing is whatever the Gawking test already does.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~DirectingTraffic"`
Expected: FAIL — `Activity` has no member `DirectingTraffic` (compile error).

- [ ] **Step 3: Implement.** In `DayPlan.cs`, append after `Gawking` (keep the doc-comment
  voice; values are positional, so LAST):

```csharp
        /// <summary>
        /// Standing at the pinch of a cordoned scene, waving one direction of traffic
        /// through at a time. Live state set only by Simulation.Respond's standAs
        /// parameter, the same as Gawking; the tick loop never writes it. At the end of
        /// the enum — values are positional.
        /// </summary>
        DirectingTraffic,
```

In `Content/animations.txt`, append after the `gawking` row (every clip below is carried by
the `talking` row at :126, so the carried-clips gate passes):

```
directingtraffic Arm Gesture, Hands Forward Gesture, Strong Gesture, Acknowledging
```

- [ ] **Step 4: Run the Core surface**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~RespondTests|FullyQualifiedName~AnimationTableTests"`
Expected: ALL PASS — the two new tests plus `EveryActivityHasARowInTheRealFile` green over
the new member.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Core/People/DayPlan.cs Content/animations.txt tools/Noir.Core.Tests/RespondTests.cs
git commit -m "Activity.DirectingTraffic: the officer's wave, dressed from carried clips"
```

---

### Task 2: The cordon in `CityTraffic` — hold lines, alternation, the crawl

**Files:**
- Modify: `Assets/Noir/Unity/CityTraffic.cs`

**Interfaces:**
- Consumes: existing `Graph.Segments` (`LaneSegment{Index, Line, Way, Lane, FromS, ToS, ...}`),
  `PointOn(int segment, float s)` (:430), `TownTick` (:983), `Hold` enum (:271),
  `RunSegment` (:1037), `Seat`.
- Produces (Tasks 3-5 rely on these exact members):

```csharp
public struct CordonLayout
{
    public bool TrafficControlled;   // false when no lane passes near the scene (tape-only)
    public Vector3 BarricadeNear;    // closed-lane hold point, one end of the pinch
    public Vector3 BarricadeFar;     // ...and the other end
    public Vector3 RoadAxis;         // unit vector along the road at the scene
}
public CordonLayout RaiseCordon(Vector3 sceneWorld)
public void LowerCordon()
public bool CordonActive { get; }
public bool CordonHolds(Vector3 position, Vector3 forward)   // the player's question
```

- [ ] **Step 1: State and constants.** Beside the `Obstacles` list (:318), add — one cordon at
  a time, matching the town's one response team; `RaiseCordon` replaces any prior one:

```csharp
        // ---- the scene cordon ----
        //
        // One cordon at a time — Rossville has one response team, and the officer who
        // raises it is the same man who directs the pinch. State lives HERE, beside the
        // fleet, never on Mover: nine loops walk _movers and a held car must remain an
        // ordinary member of all of them. See the spec:
        // docs/superpowers/specs/2026-08-17-scene-cordon-design.md
        private struct CordonLine
        {
            public int Segment;      // index into Graph.Segments
            public float HoldS;      // cars stop short of this travel coordinate...
            public float ClearS;     // ...and are through the pinch past this one
            public bool SideA;       // which alternation group this approach belongs to
            public bool Closed;      // the body's half: cars here borrow laterally
        }
        private readonly List<CordonLine> _cordon = new List<CordonLine>();
        private bool _cordonUp;
        private Vector3 _cordonCentre;
        private Vector3 _cordonOffsetDir;          // unit lateral, toward the OPEN half
        private const float CordonReach = 10f;     // metres of lane held either side of the scene
        private const float CordonCrawl = 2.5f;    // m/s through the pinch
        private const double CordonCycle = 36.0;   // the signal cycle's proportions: A 0-14, hold 14-18, B 18-32, hold 32-36
```

- [ ] **Step 2: `RaiseCordon`.** Walk every segment; sample `PointOn` along it at 2 m steps to
  find the closest approach to `sceneWorld` (a one-time cost at raise, not per frame). A
  segment whose closest approach is under `CordonReach` joins `_cordon` with
  `HoldS = closestS - CordonReach` and `ClearS = closestS + CordonReach`, both clamped to
  `[FromS, ToS]`. Group approaches by direction: read the `Heading` enum (the type of
  `segment.Way`; `Headings.IsNorthSouth(...)` at :1170 shows the idiom) and write
  `SideA = ((int)segment.Way & 1) == 0` — or, if the enum's values make that meaningless,
  any pure function of `Way` alone. The BINDING requirement, which the implementer states in
  a comment beside the line: opposing headings on the same road land in OPPOSITE groups, and
  the grouping is deterministic (a pure function of the segment, nothing else).
  The closed half: compute the body's signed lateral offset from the nearest lane's direction
  (`Vector3.Cross(axis, sceneWorld - closestPoint).y`), mark segments whose lane sits on the
  body's side `Closed = true`, and set `_cordonOffsetDir` to the unit lateral pointing the
  other way. Fill and return `CordonLayout`: `TrafficControlled = _cordon.Count > 0`;
  `BarricadeNear/Far = PointOn(closedSegment, HoldS/ClearS)` of the closed-side segment
  nearest the scene (or, when nothing is closed-side, of the nearest cordoned segment);
  `RoadAxis` = normalized `PointOn(seg, closestS + 1) - PointOn(seg, closestS)`. When NO
  segment qualifies, return `default` with `TrafficControlled = false` and do not set
  `_cordonUp`.

- [ ] **Step 3: The alternation.** Phase in `double` seconds off the town tick — the same
  arithmetic the signals are fed (VillageHost.cs:2039):

```csharp
        private bool CordonOpenFor(bool sideA)
        {
            double t = TownTick / (double)GameClock.TicksPerSecond % CordonCycle;
            return sideA ? t < 14.0 : (t >= 18.0 && t < 32.0);
        }
```

- [ ] **Step 4: The clamp in `RunSegment`.** Directly after the `MayCross` block (:1057-1077)
  and BEFORE the `Blocked(index)` check, insert — modelled byte-for-byte on the existing stop
  line arithmetic at :1062, and deliberately NOT touching `Waited`/`Choices`:

```csharp
            // The scene cordon: a hold line short of the pinch, and a crawl through it
            // when this approach has the officer's wave. Waited/Choices are deliberately
            // untouched — a held car waits for the wave; it does not re-plan around a
            // crime scene.
            if (_cordonUp)
            {
                for (int c = 0; c < _cordon.Count; c++)
                {
                    if (_cordon[c].Segment != me.Segment) continue;
                    var line = _cordon[c];
                    if (me.S > line.ClearS) break;                     // already past
                    if (!CordonOpenFor(line.SideA) && me.S < line.HoldS)
                    {
                        float allowed = Mathf.Max(0f, line.HoldS - me.S - me.Reach - 0.4f);
                        step = Mathf.Min(step, allowed);
                        if (allowed <= 0.01f) me.Why = Hold.Cordon;
                    }
                    else
                    {
                        step = Mathf.Min(step, CordonCrawl * dt);      // waved through, at a crawl
                    }
                    break;
                }
            }
```

Add `Cordon` to the `Hold` enum (:271-274) — `Holds()` tallies it for free.

- [ ] **Step 5: The borrowed lane.** In `Seat` (where the car's world pose is written from
  `PointOn`), add a lateral offset for a car inside a CLOSED cordoned window:

```csharp
        /// <summary>Zero except through a cordon's pinch on the closed half, where a car
        /// borrows toward the open lane and back — a render-side shift; S never lies.</summary>
        private Vector3 CordonShift(int segment, float s)
        {
            if (!_cordonUp) return Vector3.zero;
            for (int c = 0; c < _cordon.Count; c++)
            {
                var line = _cordon[c];
                if (line.Segment != segment || !line.Closed) continue;
                if (s < line.HoldS || s > line.ClearS) return Vector3.zero;
                // ease in and out of the borrow over the first and last 4 metres
                float into = Mathf.Min(s - line.HoldS, line.ClearS - s);
                return _cordonOffsetDir * (3.5f * Mathf.Clamp01(into / 4f));
            }
            return Vector3.zero;
        }
```

and apply `+ CordonShift(me.Segment, me.S)` to the seated position in `Seat` (one line —
find where `PointOn`'s result is assigned and add the shift; rotation untouched).

- [ ] **Step 6: `LowerCordon`, `CordonActive`, `CordonHolds`:**

```csharp
        public bool CordonActive => _cordonUp;

        public void LowerCordon()
        {
            _cordon.Clear();
            _cordonUp = false;
        }

        /// <summary>The player's car asks the same question the fleet answers in
        /// RunSegment: is there a hold line within braking distance ahead of this pose,
        /// on an approach the officer has NOT waved through?</summary>
        public bool CordonHolds(Vector3 position, Vector3 forward)
        {
            if (!_cordonUp) return false;
            for (int c = 0; c < _cordon.Count; c++)
            {
                var line = _cordon[c];
                if (CordonOpenFor(line.SideA)) continue;
                Vector3 hold = PointOn(line.Segment, line.HoldS);
                Vector3 gap = hold - position;
                float ahead = Vector3.Dot(gap, forward.normalized);
                if (ahead <= 0f || ahead > 7f) continue;                       // behind, or far
                if (Vector3.Cross(forward.normalized, gap).magnitude > 3.0f) continue;  // not my lane
                return true;
            }
            return false;
        }
```

(`RaiseCordon` sets `_cordonUp = _cordon.Count > 0` and `_cordonCentre = sceneWorld` after
filling the list.)

- [ ] **Step 7: Build**

Run: `dotnet build Noir.Unity.csproj -c Debug` — exit 0, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add Assets/Noir/Unity/CityTraffic.cs
git commit -m "The cordon holds the fleet: a hold line, a wave, and a borrowed lane"
```

---

### Task 3: The props — sawhorses, tape, and three materials

**Files:**
- Create: `Assets/Noir/Unity/SceneCordon.cs`
- Modify: `Assets/Noir/Unity/Materials3D.cs` (three lazy properties beside `Timber`/`Postbox`, ~:943)

**Interfaces:**
- Consumes: `CityTraffic.CordonLayout` (Task 2), `Space3D.ToWorld(Tile)`,
  `ElevationGrid.HeightAt` via the idiom `CitySigns.cs:372` uses.
- Produces (Task 4 relies on):

```csharp
public sealed class SceneCordon
{
    public static SceneCordon Create(Transform parent)
    public void Raise(int caseId, Vector3 sceneWorld, CityTraffic.CordonLayout layout)
    public void Lower(int caseId)
    public bool IsUp(int caseId)
}
```

- [ ] **Step 1: Materials.** In `Materials3D.cs`, beside the other lazy properties, following
  the exact `Timber` idiom (backing field + one-line property; new-thing colors are judgment
  calls in the flat-color style, not pack measurements — say so in the comment):

```csharp
        private static Material _cordonWood, _cordonStripe, _cordonTape;

        /// <summary>The white of a municipal sawhorse — a new thing, not a pack
        /// measurement; judged against the flat-color style like the response bar's
        /// lenses were.</summary>
        public static Material CordonWood =>
            _cordonWood != null ? _cordonWood : (_cordonWood = Make("CordonWood", new Color32(0xE8, 0xE4, 0xDC, 0xFF), 0.10f));
        public static Material CordonStripe =>
            _cordonStripe != null ? _cordonStripe : (_cordonStripe = Make("CordonStripe", new Color32(0xD8, 0x6A, 0x1E, 0xFF), 0.15f));
        public static Material CordonTape =>
            _cordonTape != null ? _cordonTape : (_cordonTape = Make("CordonTape", new Color32(0xE8, 0xC8, 0x1A, 0xFF), 0.20f));
```

- [ ] **Step 2: `SceneCordon.cs`.** Runtime props per the `CityResponse.DressTheBar` precedent
  (CityResponse.cs:1348 — read it first): `CreatePrimitive` pieces under a per-case root in a
  `Dictionary<int, GameObject>`. Structure:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// The dressing of a held scene: two striped sawhorses closing the body's half of the
    /// street, and a ring of tape on posts around the scene itself. Raised when the
    /// officer arrives, lowered when the case closes — the host owns both moments.
    /// Generated primitives in the flat-color style; an owner model replaces them later
    /// without touching this logic, the cruiser's own precedent. Spec:
    /// docs/superpowers/specs/2026-08-17-scene-cordon-design.md
    /// </summary>
    public sealed class SceneCordon
    {
        private readonly Transform _parent;
        private readonly Dictionary<int, GameObject> _roots = new Dictionary<int, GameObject>();

        public static SceneCordon Create(Transform parent) => new SceneCordon(parent);
        private SceneCordon(Transform parent) { _parent = parent; }

        public bool IsUp(int caseId) => _roots.ContainsKey(caseId);

        public void Raise(int caseId, Vector3 sceneWorld, CityTraffic.CordonLayout layout)
        {
            if (_roots.ContainsKey(caseId)) return;
            var root = new GameObject("Scene Cordon " + caseId);
            root.transform.SetParent(_parent, false);
            _roots[caseId] = root;

            if (layout.TrafficControlled)
            {
                var facing = Quaternion.LookRotation(layout.RoadAxis, Vector3.up);
                Sawhorse(root.transform, layout.BarricadeNear, facing);
                Sawhorse(root.transform, layout.BarricadeFar, facing);
            }
            TapeRing(root.transform, sceneWorld);
        }

        public void Lower(int caseId)
        {
            if (!_roots.TryGetValue(caseId, out var root)) return;
            _roots.Remove(caseId);
            if (root != null) Object.Destroy(root);
        }

        /// <summary>A municipal sawhorse: two A-frame legs and a striped crossbar,
        /// 2.0 m wide, bar at 0.8 m. The ROOT carries one box collider so the player's
        /// existing BoxCast (Player.DriveStep) stops at it — the one prop family where
        /// the keep-the-collider rule inverts the Frontage.Piece convention.</summary>
        private static void Sawhorse(Transform parent, Vector3 at, Quaternion facing)
        {
            var horse = new GameObject("Sawhorse");
            horse.transform.SetParent(parent, false);
            horse.transform.position = at;
            horse.transform.rotation = facing;
            var box = horse.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.5f, 0f);
            box.size = new Vector3(2.0f, 1.0f, 0.3f);

            // striped crossbar: orange - white - orange
            Piece(horse.transform, "bar-l", new Vector3(-0.67f, 0.8f, 0f), new Vector3(0.66f, 0.22f, 0.05f), Materials3D.CordonStripe);
            Piece(horse.transform, "bar-m", new Vector3(0f, 0.8f, 0f), new Vector3(0.66f, 0.22f, 0.05f), Materials3D.CordonWood);
            Piece(horse.transform, "bar-r", new Vector3(0.67f, 0.8f, 0f), new Vector3(0.66f, 0.22f, 0.05f), Materials3D.CordonStripe);
            // A-frame legs, splayed in z
            Leg(horse.transform, new Vector3(-0.85f, 0f, 0.18f), -12f);
            Leg(horse.transform, new Vector3(-0.85f, 0f, -0.18f), 12f);
            Leg(horse.transform, new Vector3(0.85f, 0f, 0.18f), -12f);
            Leg(horse.transform, new Vector3(0.85f, 0f, -0.18f), 12f);
        }

        private static void Leg(Transform parent, Vector3 foot, float lean)
        {
            var leg = Piece(parent, "leg", foot + new Vector3(0f, 0.42f, 0f),
                            new Vector3(0.06f, 0.84f, 0.06f), Materials3D.CordonWood);
            leg.transform.localRotation = Quaternion.Euler(lean, 0f, 0f);
        }

        /// <summary>Four posts and four tape runs boxing the scene, 3.2 m half-width —
        /// wide enough to ring the body, narrow enough to stay off the open lane.</summary>
        private static void TapeRing(Transform parent, Vector3 centre)
        {
            var half = 3.2f;
            var corners = new[]
            {
                centre + new Vector3(-half, 0f, -half), centre + new Vector3(half, 0f, -half),
                centre + new Vector3(half, 0f, half), centre + new Vector3(-half, 0f, half),
            };
            foreach (var c in corners)
                Piece(parent, "post", c + new Vector3(0f, 0.5f, 0f),
                      new Vector3(0.05f, 1.0f, 0.05f), Materials3D.CordonWood);
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = corners[i], b = corners[(i + 1) % 4];
                Vector3 mid = (a + b) / 2f + new Vector3(0f, 0.9f, 0f);
                var tape = Piece(parent, "tape", mid,
                                 new Vector3(Vector3.Distance(a, b), 0.08f, 0.01f), Materials3D.CordonTape);
                tape.transform.rotation = Quaternion.LookRotation(Vector3.Cross(b - a, Vector3.up), Vector3.up);
            }
        }

        private static GameObject Piece(Transform parent, string name, Vector3 position,
                                        Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            Object.Destroy(go.GetComponent<Collider>());   // the ROOT's collider is the one that counts
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            return go;
        }
    }
}
```

NOTE on ground height: `Raise` receives `sceneWorld` and barricade points already at ground
level (Task 4 computes them via the elevation idiom); this class does not re-derive terrain.

- [ ] **Step 3: Build**

Run: `dotnet build Noir.Unity.csproj -c Debug` — exit 0.

- [ ] **Step 4: Commit**

```bash
git add Assets/Noir/Unity/SceneCordon.cs Assets/Noir/Unity/Materials3D.cs
git commit -m "Sawhorses and tape: the cordon has something to stand behind"
```

---

### Task 4: Host and player wiring — up on arrival, down on close, the officer at the pinch

**Files:**
- Modify: `Assets/Noir/Unity/VillageHost.cs` (`Build` creates the cordon; `OfficerArrived` arm
  ~:1536; `Closed` arm ~:1562)
- Modify: `Assets/Noir/Unity/Player.cs` (`DriveStep`, beside the BoxCast ~:255)

**Interfaces:**
- Consumes: `Traffic.RaiseCordon/LowerCordon/CordonActive/CordonHolds` (Task 2),
  `SceneCordon.Create/Raise/Lower` (Task 3), `Activity.DirectingTraffic` (Task 1),
  `Sim.Respond(who, tile, standAs)`, `KerbTileNear(Vector3, Tile)` (:1809),
  `Space3D.ToWorld(Tile)`, `_voices?.Say(...)`.
- Produces: a `_cordons` field (`SceneCordon`) and `_cordonCase` int the Closed arm reads.

- [ ] **Step 1: Create the cordon owner.** In `VillageHost.Build`, beside where other city
  systems are created (find `VillageAudio.Create(this, transform)` at ~:1031 and follow its
  placement), add a field and creation:

```csharp
        private SceneCordon _cordons;
        private int _cordonCase = -1;
```
```csharp
            _cordons = SceneCordon.Create(_village.transform);
```

- [ ] **Step 2: Cordon up.** In the `OfficerArrived` arm (:1536-1543), AFTER
  `_cases.OfficerArrived(c, minute)` and the existing `Say` (order matters: the arrival
  detector requires `a.Doing == Activity.Responding`, so the re-stand comes after the report):

```csharp
                        // THE CORDON GOES UP. Traffic first (it computes the geometry),
                        // then the props stand at the points it chose, then the officer
                        // walks to the pinch and starts waving — re-stood AFTER
                        // OfficerArrived is reported, because the arrival detector above
                        // keys on Doing == Responding and must fire exactly once.
                        var sceneWorld = Space3D.ToWorld(_cases.SceneOf(c));
                        var layout = _traffic != null ? _traffic.RaiseCordon(sceneWorld)
                                                      : default;
                        _cordons.Raise(c, sceneWorld, layout);
                        _cordonCase = c;
                        if (layout.TrafficControlled)
                        {
                            Tile pinch = KerbTileNear(layout.BarricadeNear, _cases.SceneOf(c));
                            Sim.Respond(officer, pinch, Activity.DirectingTraffic);
                            _voices?.Say(officer, "keep it moving — one at a time.");
                        }
```

NOTE: `Space3D.ToWorld(Tile)` returns ground-level for the tile; the barricade points come
back from `RaiseCordon` already on the lane (PointOn), which rides the road surface. If a
visual float/sink shows at review, snap prop Y via the `ElevationGrid.HeightAt(x, -z)` idiom
(`CitySigns.cs:372`) inside Task 3's `Raise` — one line, noted here so the reviewer knows
where the fix lives.

- [ ] **Step 3: Cordon down.** In the `CaseState.Closed` arm (:1562-1571), beside the crowd
  dispersal:

```csharp
                    if (_cordonCase == c)
                    {
                        _cordons.Lower(c);
                        _traffic?.LowerCordon();
                        _cordonCase = -1;
                        Debug.Log($"[case] case {c}: the cordon comes down");
                    }
```

- [ ] **Step 4: The player obeys the wave.** In `Player.DriveStep`, immediately before the
  BoxCast obstacle check (~:255):

```csharp
            // The officer's hold line binds the player exactly as it binds the fleet —
            // the barricade collider stops the closed half physically; this stops the
            // open lane until the wave. Traffic answers the same question RunSegment
            // asks for the ambient cars.
            if (_host != null && _host.Traffic != null
                && _host.Traffic.CordonHolds(_car.transform.position, _car.transform.forward))
            {
                _carSpeed = 0f;
                distance = 0f;
            }
```

(Verify the local names — `distance`, `_carSpeed`, `_car`, and how Player reaches the host —
against the surrounding code at :201-330 and use ITS identifiers; the intent is binding, the
spelling is the file's.)

- [ ] **Step 5: Build all three**

Run: `dotnet build Noir.Unity.csproj -c Debug`, `dotnet build Noir.Editor.csproj -c Debug`,
`dotnet build Noir.PlayTests.csproj -c Debug` — each exit 0.

- [ ] **Step 6: Commit**

```bash
git add Assets/Noir/Unity/VillageHost.cs Assets/Noir/Unity/Player.cs
git commit -m "The cordon rises with the officer and falls with the case"
```

---

### Task 5: The PlayMode scenario learns to see the cordon

**Files:**
- Modify: `Assets/Noir/PlayTests/ResponsePlayTests.cs` (the poll loop ~:180, the post-close
  latch ~:194)

**Interfaces:**
- Consumes: `GameObject.Find("Scene Cordon " + _caseId)` (Task 3's root name), the existing
  poll-loop/latch shapes.

- [ ] **Step 1: The latches.** In `AWitnessedHitBringsTheTownsWholeResponse`, add beside the
  `gawked` probe in the poll loop (:180):

```csharp
                if (!cordoned && s >= CaseState.SceneHeld && s < CaseState.Closed)
                    cordoned = GameObject.Find("Scene Cordon " + _caseId) != null;
```

(declare `bool cordoned = false;` beside `rode`/`gawked`), and after the dispersal latch
(:194-203), the mirrored teardown check:

```csharp
            Assert.That(cordoned, Is.True,
                "the cordon never went up while the scene was held - no 'Scene Cordon " + _caseId + "' object appeared");

            // And it comes down with the case: RunResponse lowers it in the same Closed
            // arm that disperses the crowd, so the same few-real-seconds window applies.
            float lowered = Time.time + 5f;
            bool standing = true;
            while (Time.time < lowered && standing)
            {
                standing = GameObject.Find("Scene Cordon " + _caseId) != null;
                if (standing) yield return null;
            }
            Assert.That(standing, Is.False,
                "the cordon outlived its case - the barricades are still standing after Closed");
```

- [ ] **Step 2: Build the test assembly** (the cheapest four seconds in this project):

Run: `dotnet build Noir.PlayTests.csproj -c Debug` — exit 0.

- [ ] **Step 3: Commit**

```bash
git add Assets/Noir/PlayTests/ResponsePlayTests.cs
git commit -m "The response scenario checks the tape goes up and comes down"
```

NOTE: the PlayMode gate itself is NOT run in this task — it needs the editor closed and runs
at the next gate window per the standing rule. The staged live look (drive, hit, watch the
cordon rise) is the acceptance test the tests cannot replace.

---

### Task 6: Land it — the Core gate, the ledgers, the push

- [ ] **Step 1: Full Core gate in Release** (bare, never `| tail`):

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj`
Expected: **582 pass, 0 fail, 8 skipped** (580 + 2 from Task 1 — use the MEASURED number).

- [ ] **Step 2: CLAUDE.md** — new Core baseline entry above the 580 one, same house style:
  measured counts, date, "+2: `RespondTests`' DirectingTraffic pair backing the scene
  cordon", plan path.

- [ ] **Step 3: The spec** — `docs/superpowers/specs/2026-08-17-scene-cordon-design.md`: add a
  line to the header block noting the implementation landed with this plan's path and date.

- [ ] **Step 4: Commit and push**

```bash
git add CLAUDE.md docs/superpowers/specs/2026-08-17-scene-cordon-design.md docs/superpowers/plans/2026-08-17-scene-cordon.md
git commit -m "Scene cordon lands: the street closes around the body, and one lane breathes"
git push
```

- [ ] **Step 5: Named leftovers.** The PlayMode gate (with the new cordon latches and the
  shadow-experiment history) waits for the next editor-closed window; the live look — cordon
  rising over a real case, the officer waving, the fleet crawling the pinch — is the owner's
  acceptance test; owner-modeled sawhorses/tape replace the primitives whenever his pipeline
  produces them; a pinch-wait PlayMode assertion in the `NoCarWaitsForever` style is deferred
  until the cordon has been WATCHED once (per that test's own lesson: never trust a traffic
  number from the same run that changed the topology of behavior).
