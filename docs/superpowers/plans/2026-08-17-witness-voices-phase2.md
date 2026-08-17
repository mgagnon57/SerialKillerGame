# Witness Voices Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Being asked becomes an event — a civilian who questions a witness is remembered
("I saw somebody going around asking questions"), through the same event-testimony machinery
hits use; a badge-on ask lands the witness's words in every open case's file through a sibling
of the county canvass's own seam.

**Architecture:** One new forward-only history (`AskEvents`, the shape of `HitEvents`), one new
`EventAct` member with its English arm, one extra optional parameter threaded through
`Recollection.WhatTheySawOfEvents`/`AskInEnglish`, one new `ResponseCases` writer beside
`CountyReachedDoor`, and one host method (`PlayerAsks`) that the T key calls instead of
`AskWhatTheySaw`. No new observation machinery, no RNG, no new fields on `EventSighting`.

**Tech Stack:** C# (Noir.Core, NUnit in tools/Noir.Core.Tests), Unity 6000.3.20f1 host wiring.

**Spec:** `docs/superpowers/specs/2026-08-17-witness-voices-design.md` (Phase 2 section)

## Global Constraints

- **No RNG anywhere in this chain.** The witness/response layers are hash-of-seed only
  (`Rolls.Int`) or nothing. Phase 2 degrades nothing new, so it needs no new purpose constant
  and NO randomness of any kind. `ResponseCases` stays a pure minute-driven state machine.
- **The firewall holds.** `Noir.Unity` references Witness and Response, never Observation.
  `EventSighting` must carry no type whose name contains "Citizen" or "Place"
  (`EventSightingTests.AnEventSightingCarriesNoIdentity` reflects and enforces). This plan
  adds NO fields to `EventSighting`.
- **The vagueness is the design.** The new sentence names nobody: "somebody going around
  asking questions." No identity, no description of the asker in v1 (`EventSighting`'s own
  header reserves a `PersonDescription`-shaped blur for a later pass).
- **Histories run forwards.** `AskEvents.Record` throws on a minute below the last, exactly
  as `HitEvents.Record` does.
- **Existing hit-line wording is pinned by tests and must not change byte-for-byte.**
- **Core tests run in Release:** `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj`.
  Baseline going in: **569 pass, 0 fail, 8 skipped** (CLAUDE.md). Any red is a regression.
- **Do not edit any .cs while a batch Unity run is going** (CLAUDE.md trap).
- The year is 1991; seeds are seeds, not dates.

## File Structure

- Create: `Assets/Noir/Core/Witness/AskEvents.cs` — the forward-only ask history
- Create: `tools/Noir.Core.Tests/AskEventsTests.cs`
- Modify: `Assets/Noir/Core/Observation/EventSighting.cs` — `EventAct.SomebodyAskedQuestions = 1`
- Modify: `Assets/Noir/Core/Observation/Testimony.cs:63-87` — subject chosen by act
- Modify: `Assets/Noir/Core/Witness/Recollection.cs:147-240` — `asks` parameter + two-store merge
- Modify: `Assets/Noir/Core/Response/ResponseCases.cs` — `BadgeAsked` beside `CountyReachedDoor` (:278)
- Modify: `Assets/Noir/Unity/VillageHost.cs` — `_askEvents` field, `PlayerAsks`, thread asks into `AskWhatTheySaw` (:1216)
- Modify: `Assets/Noir/Unity/VillageUI.cs:742` — `Ask()` calls `PlayerAsks`
- Test additions: `tools/Noir.Core.Tests/EventTestimonyTests.cs`, `tools/Noir.Core.Tests/ResponseCasesTests.cs`

---

### Task 1: The new act speaks — `EventAct.SomebodyAskedQuestions` and its English

**Files:**
- Modify: `Assets/Noir/Core/Observation/EventSighting.cs` (the `EventAct` enum, ~line 10)
- Modify: `Assets/Noir/Core/Observation/Testimony.cs:63-87` (`InEnglish(EventSighting)`)
- Test: `tools/Noir.Core.Tests/EventTestimonyTests.cs`

**Interfaces:**
- Consumes: existing `EventSighting` ctor `(ObserverId, int minute, Tile, SightingClarity, EventAct, CarDescription)`.
- Produces: `EventAct.SomebodyAskedQuestions = 1`; `Testimony.InEnglish` renders it as
  `"16:30, I saw somebody going around asking questions."` (clarity prefix varies as for hits).
  Task 3 relies on both.

- [ ] **Step 1: Write the failing tests** (append inside the fixture in `EventTestimonyTests.cs`):

```csharp
/// <summary>
/// Phase 2 of witness voices: being asked is an event, and it must read like a witness
/// saying it — same clock, same clarity hedging as the hit line — while naming NOBODY.
/// "Somebody" is the whole description; the vagueness rule is load-bearing here.
/// </summary>
[Test]
public void AnAskSightingReadsLikeAWitness()
{
    var s = new EventSighting(new ObserverId(0), 990, new Tile(4, 4),
        SightingClarity.Partial, EventAct.SomebodyAskedQuestions,
        new CarDescription(CarTone.Unnoticed, CarShape.Unnoticed));
    Assert.That(Testimony.InEnglish(s),
        Is.EqualTo("16:30, I saw somebody going around asking questions."));
}

[Test]
public void AGlimpsedAskSightingHedges()
{
    var s = new EventSighting(new ObserverId(0), 990, new Tile(4, 4),
        SightingClarity.Glimpsed, EventAct.SomebodyAskedQuestions,
        new CarDescription(CarTone.Unnoticed, CarShape.Unnoticed));
    Assert.That(Testimony.InEnglish(s),
        Does.StartWith("16:30, I think I saw somebody"));
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~AnAskSightingReadsLikeAWitness|FullyQualifiedName~AGlimpsedAskSightingHedges"`
Expected: FAIL — `EventAct` has no member `SomebodyAskedQuestions` (compile error is the failure).

- [ ] **Step 3: Implement.** In `EventSighting.cs` extend the enum (keep its existing comment style):

```csharp
public enum EventAct : byte
{
    CarStruckSomebody = 0,
    SomebodyAskedQuestions = 1,
}
```

In `Testimony.cs`, the current event body appends `Car(e.Car)` unconditionally before the act.
Make the SUBJECT act-chosen; the hit line must come out byte-identical to today:

```csharp
sb.Append(' ');
sb.Append(e.Act == EventAct.CarStruckSomebody ? Car(e.Car) : "somebody");
sb.Append(e.Act switch
{
    EventAct.CarStruckSomebody      => " hit somebody",
    EventAct.SomebodyAskedQuestions => " going around asking questions",
    _                               => " do something",
});
sb.Append('.');
```

- [ ] **Step 4: Run the whole Observation/testimony surface to prove the hit line didn't move**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~EventTestimonyTests|FullyQualifiedName~TestimonyTests|FullyQualifiedName~EventSightingTests"`
Expected: ALL PASS (the two new ones and every pinned hit-line test).

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Core/Observation/EventSighting.cs Assets/Noir/Core/Observation/Testimony.cs tools/Noir.Core.Tests/EventTestimonyTests.cs
git commit -m "The second kind of event: somebody going around asking questions"
```

---

### Task 2: `AskEvents` — the history of being questioned

**Files:**
- Create: `Assets/Noir/Core/Witness/AskEvents.cs`
- Test: create `tools/Noir.Core.Tests/AskEventsTests.cs`

**Interfaces:**
- Consumes: `Tile` from `Noir.Core.Contracts` (mirror `HitEvents.cs`'s usings exactly).
- Produces: `public sealed class AskEvents` in `Noir.Core.Witness` with
  `int Count`, `void Record(int minute, Tile where)` (forward-only, throws `ArgumentException`),
  `void ForEach(Action<int, Tile> visit)`. Tasks 3 and 5 rely on these exact names.

- [ ] **Step 1: Write the failing tests** (`AskEventsTests.cs`, mirroring `HitEventsTests.cs`'s
  fixture shape, namespace `Noir.Core.Tests`):

```csharp
[Test]
public void TwoAsksInOneMinuteAreBothKept()
{
    var asks = new AskEvents();
    asks.Record(100, new Tile(5, 5));
    asks.Record(100, new Tile(6, 5));
    Assert.That(asks.Count, Is.EqualTo(2));
}

[Test]
public void TimeOnlyRunsForwards()
{
    var asks = new AskEvents();
    asks.Record(100, new Tile(5, 5));
    Assert.Throws<ArgumentException>(() => asks.Record(99, new Tile(5, 5)));
}

[Test]
public void ForEachReplaysInOrder()
{
    var asks = new AskEvents();
    asks.Record(10, new Tile(1, 1));
    asks.Record(20, new Tile(2, 2));
    var minutes = new List<int>();
    asks.ForEach((minute, where) => minutes.Add(minute));
    Assert.That(minutes, Is.EqualTo(new[] { 10, 20 }));
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~AskEventsTests"`
Expected: FAIL — `AskEvents` does not exist (compile error).

- [ ] **Step 3: Implement** `Assets/Noir/Core/Witness/AskEvents.cs` (usings and doc style
  copied from `HitEvents.cs`; same forward-only exception message):

```csharp
/// <summary>
/// Every time somebody went around a witness's door asking questions, as bare facts: a
/// minute and a tile. The same shape as HitEvents and for the same reason — witnesses
/// testify about it through the identical sighting arithmetic, and the history carries no
/// identity at all: WHO asked is exactly what a witness cannot hand you. Phase 2 of the
/// witness-voices spec: the killer's own canvass becomes a sighting.
/// </summary>
public sealed class AskEvents
{
    private readonly struct Ask
    {
        public readonly int Minute;
        public readonly Tile Where;
        public Ask(int minute, Tile where) { Minute = minute; Where = where; }
    }

    private readonly List<Ask> _asks = new List<Ask>();

    public int Count => _asks.Count;

    public void Record(int minute, Tile where)
    {
        if (_asks.Count > 0 && minute < _asks[_asks.Count - 1].Minute)
            throw new ArgumentException("A history runs forwards.");
        _asks.Add(new Ask(minute, where));
    }

    public void ForEach(Action<int, Tile> visit)
    {
        foreach (var a in _asks) visit(a.Minute, a.Where);
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~AskEventsTests"`
Expected: 3 PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Core/Witness/AskEvents.cs tools/Noir.Core.Tests/AskEventsTests.cs
git commit -m "AskEvents: the history of being questioned, no names kept"
```

---

### Task 3: `Recollection` carries the asks — witnesses testify about the asker

**Files:**
- Modify: `Assets/Noir/Core/Witness/Recollection.cs:147-240`
- Test: `tools/Noir.Core.Tests/EventTestimonyTests.cs`

**Interfaces:**
- Consumes: `AskEvents` (Task 2), `EventAct.SomebodyAskedQuestions` + its English (Task 1).
- Produces: `WhatTheySawOfEvents(..., ISightBlocked blocked = null, AskEvents asks = null)`
  and `AskInEnglish(..., IInterruptions interruptions = null, AskEvents asks = null)` —
  each gains ONE trailing optional parameter, so every existing call site compiles unchanged.
  Task 5 relies on the `AskInEnglish` signature.

- [ ] **Step 1: Write the failing tests** (in `EventTestimonyTests.cs`, using its existing
  `VillageContext`, `IsStationary`, distance-zero conventions — see
  `EventsAndPersonSightingsMergeInMinuteOrder` at :103 for the pattern being mirrored):

```csharp
/// <summary>
/// Phase 2's core promise: a witness remembers being asked, through the SAME sighting
/// arithmetic hits use. Distance zero on purpose (the ask happens at the witness's own
/// door), the same trick the merge test above uses: what is under test is that the ask
/// arrives in testimony at all, not the lighting model.
/// </summary>
[Test]
public void AWitnessRemembersSomebodyAskingQuestions()
{
    const int day = 3;
    var v = VillageContext.Load();

    Citizen who = null;
    int minuteOfDay = -1;
    foreach (Citizen candidate in v.People.Citizens)
    {
        DayPlan plan = DayPlanner.Plan(v.World, v.People, candidate, day, v.Seed);
        for (int m = 0; m < Sighting.MinutesPerDay; m++)
        {
            if (!IsStationary(plan.At(m))) continue;
            who = candidate; minuteOfDay = m; break;
        }
        if (who != null) break;
    }
    Assert.That(who, Is.Not.Null, "no citizen in the fixture village is ever stationary");

    DayPlan whosPlan = DayPlanner.Plan(v.World, v.People, who, day, v.Seed);
    Tile spot = v.World.GetPlace(whosPlan.At(minuteOfDay).Where).Door;

    var asks = new AskEvents();
    asks.Record(day * Sighting.MinutesPerDay + minuteOfDay, spot);

    string[] said = Recollection.AskInEnglish(v.World, v.People, who, day,
                                              new PlayerTrack(), v.Seed, asks: asks);

    Assert.That(said.Length, Is.EqualTo(1), string.Join(" | ", said));
    Assert.That(said[0].ToLowerInvariant(), Does.Contain("asking questions"),
        "the ask should surface as testimony: " + string.Join(" | ", said));
}

/// <summary>
/// A hit and an ask an hour apart must come out in the order they happened — the merge
/// promise holds ACROSS all three kinds of line (person, hit, ask), not just two. Sixty
/// minutes, not ten, for the same blur-proofing reason as the person/event merge test.
/// </summary>
[Test]
public void HitsAndAsksMergeInMinuteOrder()
{
    const int day = 3;
    const int gap = 60;
    var v = VillageContext.Load();

    Citizen who = null;
    int hitMinuteOfDay = -1, askMinuteOfDay = -1;
    foreach (Citizen candidate in v.People.Citizens)
    {
        DayPlan plan = DayPlanner.Plan(v.World, v.People, candidate, day, v.Seed);
        for (int m = 0; m + gap < Sighting.MinutesPerDay; m++)
        {
            if (!IsStationary(plan.At(m)) || !IsStationary(plan.At(m + gap))) continue;
            who = candidate; hitMinuteOfDay = m; askMinuteOfDay = m + gap; break;
        }
        if (who != null) break;
    }
    Assert.That(who, Is.Not.Null,
        "no citizen in the fixture village is ever stationary twice, an hour apart");

    DayPlan whosPlan = DayPlanner.Plan(v.World, v.People, who, day, v.Seed);
    Tile hitSpot = v.World.GetPlace(whosPlan.At(hitMinuteOfDay).Where).Door;
    Tile askSpot = v.World.GetPlace(whosPlan.At(askMinuteOfDay).Where).Door;

    var hits = new HitEvents();
    hits.Record(day * Sighting.MinutesPerDay + hitMinuteOfDay, hitSpot,
                CarTone.Dark, CarShape.Van);
    var asks = new AskEvents();
    asks.Record(day * Sighting.MinutesPerDay + askMinuteOfDay, askSpot);

    string[] said = Recollection.AskInEnglish(v.World, v.People, who, day,
                                              new PlayerTrack(), v.Seed,
                                              hits: hits, asks: asks);

    Assert.That(said.Length, Is.EqualTo(2), string.Join(" | ", said));
    Assert.That(said[0].ToLowerInvariant(), Does.Contain("hit"),
        "the hit happened first and should be told first: " + string.Join(" | ", said));
    Assert.That(said[1].ToLowerInvariant(), Does.Contain("asking questions"),
        "the ask came an hour later and should be told second: " + string.Join(" | ", said));
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~AWitnessRemembersSomebodyAskingQuestions|FullyQualifiedName~HitsAndAsksMergeInMinuteOrder"`
Expected: FAIL — `AskInEnglish` has no parameter `asks` (compile error).

- [ ] **Step 3: Implement.** In `Recollection.cs`, replace `WhatTheySawOfEvents` (:147-186)
  with the two-store version. The witness arithmetic is EXTRACTED, not duplicated; the output
  keeps the documented true-minute order across both kinds so `AskInEnglish`'s merge promise
  (:218-229) still holds; `ObserverId` is stamped by final position exactly as today
  (`found.Count` at add time):

```csharp
public static EventSighting[] WhatTheySawOfEvents(WorldModel world, Population population,
    Citizen who, int day, HitEvents hits, ulong seed,
    INightWitnesses nightWitnesses = null,
    IInterruptions interruptions = null, ISightBlocked blocked = null,
    AskEvents asks = null)
{
    if ((hits == null || hits.Count == 0) && (asks == null || asks.Count == 0))
        return System.Array.Empty<EventSighting>();

    DayPlan plan = DayPlanner.Plan(world, population, who, day, seed);
    int downedFrom = interruptions?.DownedFromMinute(who.Id) ?? int.MaxValue;
    int backFrom = interruptions?.BackFromMinute(who.Id) ?? int.MaxValue;

    // ASKED ONCE, not per event: see the identical comment on WhatTheySaw.
    bool seesWhileAsleep = nightWitnesses != null && nightWitnesses.AwakeEnough(who.Id);

    // The witness arithmetic, shared by every KIND of event: null means "never saw it".
    // One extraction rather than two copies, because the rules gating a hit and gating an
    // ask are the same rules by design — an event is witnessed by exactly the people who
    // could see that tile at that minute, whatever happened on it.
    (SightingClarity clarity, Tile watcher)? Look(int minute, Tile where)
    {
        int minuteOfDay = minute % MinutesPerDay;
        if (minute / MinutesPerDay != day) return null;
        if (minute >= downedFrom && minute < backFrom) return null;   // silenced while down or away

        Block block = plan.At(minuteOfDay);
        if (block.What == Activity.TravellingTo) return null;
        if (block.What == Activity.Asleep && !seesWhileAsleep) return null;
        if (!block.Where.IsValid) return null;

        Tile watcher = world.GetPlace(block.Where).Door;
        var when = new GameClock(GameClock.TickAt(day, minuteOfDay));
        SightingClarity clarity = Sightlines.HowGoodALook(watcher, where, when, who);
        if (!Sightlines.SawAnythingAtAll(clarity, watcher, where, when, blocked)) return null;
        return (clarity, watcher);
    }

    var hitSeen = new List<EventSighting>(); var hitTrue = new List<int>();
    var askSeen = new List<EventSighting>(); var askTrue = new List<int>();

    hits?.ForEach((minute, where, tone, shape) =>
    {
        var look = Look(minute, where);
        if (look == null) return;
        var car = Degradation.CarRegistered(look.Value.clarity, tone, shape, who.Key, minute, seed);
        hitSeen.Add(new EventSighting(default, BlurredMinute(minute, look.Value.clarity),
                                      look.Value.watcher, look.Value.clarity,
                                      EventAct.CarStruckSomebody, car));
        hitTrue.Add(minute);
    });

    asks?.ForEach((minute, where) =>
    {
        var look = Look(minute, where);
        if (look == null) return;
        askSeen.Add(new EventSighting(default, BlurredMinute(minute, look.Value.clarity),
                                      look.Value.watcher, look.Value.clarity,
                                      EventAct.SomebodyAskedQuestions,
                                      new CarDescription(CarTone.Unnoticed, CarShape.Unnoticed)));
        askTrue.Add(minute);
    });

    // TRUE-minute order across BOTH kinds — AskInEnglish's merge documents and relies on
    // it. Two sorted runs (each store is forward-only), ties go to the hit: the graver
    // thing is what a witness reports first. ObserverId is stamped by final position.
    var found = new List<EventSighting>(hitSeen.Count + askSeen.Count);
    EventSighting Stamp(EventSighting s) =>
        new EventSighting(new ObserverId(found.Count), s.Minute, s.Where, s.Clarity, s.Act, s.Car);
    int i = 0, j = 0;
    while (i < hitSeen.Count && j < askSeen.Count)
        found.Add(hitTrue[i] <= askTrue[j] ? Stamp(hitSeen[i++]) : Stamp(askSeen[j++]));
    while (i < hitSeen.Count) found.Add(Stamp(hitSeen[i++]));
    while (j < askSeen.Count) found.Add(Stamp(askSeen[j++]));
    return found.ToArray();
}
```

And in `AskInEnglish` (:201): add the trailing parameter and pass it through —

```csharp
public static string[] AskInEnglish(WorldModel world, Population population,
                                    Citizen who, int day, PlayerTrack track, ulong seed,
                                    INightWitnesses nightWitnesses = null,
                                    ISightBlocked blocked = null,
                                    HitEvents hits = null,
                                    IInterruptions interruptions = null,
                                    AskEvents asks = null)
```

with the `WhatTheySawOfEvents` call becoming:

```csharp
EventSighting[] events = WhatTheySawOfEvents(world, population, who, day, hits,
                                             seed, nightWitnesses, interruptions, blocked,
                                             asks);
```

Nothing else in `AskInEnglish` changes — the person/event merge (:230-239) already handles
whatever `WhatTheySawOfEvents` returns.

- [ ] **Step 4: Run the full Witness surface**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~EventTestimonyTests|FullyQualifiedName~EventSightingTests|FullyQualifiedName~WitnessTests|FullyQualifiedName~WitnessFirewallTests|FullyQualifiedName~DownedTests"`
Expected: ALL PASS — the two new tests green, every existing event/person/downed test
untouched (the extraction must not have changed hit behaviour).

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Core/Witness/Recollection.cs tools/Noir.Core.Tests/EventTestimonyTests.cs
git commit -m "Recollection: witnesses remember somebody going around asking questions"
```

---

### Task 4: `ResponseCases.BadgeAsked` — the badge writes the record

**Files:**
- Modify: `Assets/Noir/Core/Response/ResponseCases.cs` (new method beside `CountyReachedDoor`, :278)
- Test: `tools/Noir.Core.Tests/ResponseCasesTests.cs`

**Interfaces:**
- Consumes: the existing `Case.File` list (:116) and `CaseState`.
- Produces: `public void BadgeAsked(int caseId, CitizenId witness, string[] lines)` —
  appends `"case N: citizen W told the badge: <line>"` per line; throws
  `InvalidOperationException` on a `Closed` or `Undiscovered` case; touches NO canvass
  state (`WitnessIndex`, `NextDoorReadyAt`, `CanvassNextEmittedForIndex` all untouched).
  Task 5 relies on this signature.

- [ ] **Step 1: Write the failing tests** (in `ResponseCasesTests.cs`; the drive-to-Canvassing
  sequence mirrors `AWitnessedHitRunsTheWholeResponse` at :20 — same minutes, same calls):

```csharp
/// <summary>
/// Phase 2 of witness voices: a badge-on ask lands the witness's words in the file
/// through a sibling of the canvass's own seam — verbatim, prefixed so a reader can
/// tell a volunteered county answer from one the player's badge collected.
/// </summary>
[Test]
public void ABadgeAskLandsTheWordsInTheFile()
{
    var cases = new ResponseCases();
    var orders = new List<CaseOrder>();
    int id = cases.Open(new CitizenId(7), 1000, new Tile(50, 50),
                        CarTone.Dark, CarShape.Pickup, fatal: false);
    cases.BodySeen(id, 1000, new CitizenId(3));
    cases.Tick(1004, orders);

    cases.BadgeAsked(id, new CitizenId(41), new[] { "16:30, I saw a dark van hit somebody." });

    var file = cases.FileOf(id);
    Assert.That(file.Any(l => l.Contains("citizen 41 told the badge: 16:30, I saw a dark van hit somebody.")),
        "the badge ask should be in the file verbatim: " + string.Join(" | ", file));
}

[Test]
public void ABadgeAskOnAnUndiscoveredCaseThrows()
{
    var cases = new ResponseCases();
    int id = cases.Open(new CitizenId(7), 1000, new Tile(50, 50),
                        CarTone.Dark, CarShape.Pickup, fatal: false);
    Assert.Throws<InvalidOperationException>(() =>
        cases.BadgeAsked(id, new CitizenId(41), new[] { "anything" }),
        "the town cannot file what it does not know exists");
}

[Test]
public void ABadgeAskOnAClosedCaseThrows()
{
    var cases = new ResponseCases();
    int id = cases.Open(new CitizenId(7), 1000, new Tile(50, 50),
                        CarTone.Dark, CarShape.Pickup, fatal: false);
    cases.CloseLoudly(id, "test: shutting the file");
    Assert.Throws<InvalidOperationException>(() =>
        cases.BadgeAsked(id, new CitizenId(41), new[] { "anything" }));
}

/// <summary>
/// The badge writer must be a BYSTANDER to the canvass state machine: after a badge ask
/// mid-canvass, the outstanding CanvassNext witness is still the one CountyReachedDoor
/// expects, and the next door comes due on the county's own clock, unmoved.
/// </summary>
[Test]
public void ABadgeAskDoesNotAdvanceTheCanvass()
{
    var cases = new ResponseCases();
    var orders = new List<CaseOrder>();
    int id = cases.Open(new CitizenId(7), 1000, new Tile(50, 50),
                        CarTone.Dark, CarShape.Pickup, fatal: false);
    cases.BodySeen(id, 1000, new CitizenId(3));
    cases.Tick(1004, orders); orders.Clear();
    cases.OfficerDispatched(id, new CitizenId(12));
    cases.OfficerArrived(id, 1010);
    cases.Tick(1028, orders); orders.Clear();
    cases.CountyArrived(id, 1033);
    cases.CanvassBegins(id, new[] { new CitizenId(3), new CitizenId(9) });
    cases.Tick(1033, orders);
    Assert.That(orders[0].Who, Is.EqualTo(new CitizenId(3)));
    orders.Clear();

    cases.BadgeAsked(id, new CitizenId(41), new[] { "Nothing. I never saw anybody." });

    // The canvass neither skipped citizen 3 nor reset its clock: their answer still lands,
    // and citizen 9 comes due exactly CanvassMinutesPerDoor after it, as ever.
    cases.CountyReachedDoor(id, 1036, new CitizenId(3), new[] { "Nothing. I never saw anybody." });
    cases.Tick(1040, orders);
    Assert.That(orders, Is.Empty, "the door clock must be the county's own, unmoved by the badge");
    cases.Tick(1041, orders);
    Assert.That(orders[0].Who, Is.EqualTo(new CitizenId(9)));
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~ABadgeAsk"`
Expected: FAIL — `ResponseCases` has no `BadgeAsked` (compile error).

- [ ] **Step 3: Implement** in `ResponseCases.cs`, directly under `CountyReachedDoor` (match
  its exception type — it is the loud-crash convention — and `Case c = _cases[caseId];`
  access pattern):

```csharp
/// <summary>
/// A badge-on ask, filed. The second and only other place a witness's own words land in
/// the file, beside <see cref="CountyReachedDoor"/> — but a BYSTANDER to the canvass:
/// no outstanding-order check, no witness index, no door clock. The county's procedure
/// is not the player's, and a badge ask mid-canvass must not move the county's feet.
/// Undiscovered throws — the town cannot file what it does not know exists — and Closed
/// throws — the file is shut.
/// </summary>
public void BadgeAsked(int caseId, CitizenId witness, string[] lines)
{
    Case c = _cases[caseId];
    if (c.State == CaseState.Undiscovered)
        throw new InvalidOperationException(
            "case " + caseId + " is undiscovered; the town cannot file what it does not know exists.");
    if (c.State == CaseState.Closed)
        throw new InvalidOperationException("case " + caseId + " is closed; the file is shut.");

    if (lines == null) return;
    for (int i = 0; i < lines.Length; i++)
        c.File.Add("case " + caseId + ": citizen " + witness.Value + " told the badge: " + lines[i]);
}
```

(If `CountyReachedDoor` throws a different exception type than `InvalidOperationException`,
match IT and update the two `Assert.Throws` accordingly — the convention outranks this plan.)

- [ ] **Step 4: Run the full Response surface**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~ResponseCasesTests|FullyQualifiedName~ResponseFirewallTests"`
Expected: ALL PASS — four new plus every existing.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Core/Response/ResponseCases.cs tools/Noir.Core.Tests/ResponseCasesTests.cs
git commit -m "BadgeAsked: the badge writes the record, and the canvass never feels it"
```

---

### Task 5: Host and UI wiring — the T key gains consequences

**Files:**
- Modify: `Assets/Noir/Unity/VillageHost.cs` (field near `_hitEvents`; `AskWhatTheySaw` :1216; new `PlayerAsks` beside it)
- Modify: `Assets/Noir/Unity/VillageUI.cs:742` (`Ask()`)

**Interfaces:**
- Consumes: `AskEvents` (Task 2), `AskInEnglish`'s `asks` parameter (Task 3),
  `ResponseCases.BadgeAsked` (Task 4), existing `Badge` (:68), `NowMinute()` (:1414),
  `Sim.GetAgent(id).Position.ToTile()`, `_cases.Count`/`StateOf`.
- Produces: `public string[] PlayerAsks(CitizenId who, int day)` on `VillageHost`.

- [ ] **Step 1: Add the field** beside `_hitEvents` in `VillageHost.cs`:

```csharp
private readonly AskEvents _askEvents = new AskEvents();
```

- [ ] **Step 2: Thread asks into `AskWhatTheySaw`** (:1216) so EVERY ask — the county's
  canvass included — can hear about the asker. This is the line that makes the killer's
  own canvass reach the police:

```csharp
return Recollection.AskInEnglish(World, People, People.Get(who), day, Track, Seed,
                                 null, null, _hitEvents, _interruptions, _askEvents);
```

Deliberately NOT changed: `SawTheHit` (:1847) calls `AskInEnglish` directly without `asks`
— the canvass list stays "who saw the HIT", on purpose; somebody who only saw the asker is
not a hit-witness.

- [ ] **Step 3: Add `PlayerAsks`** directly below `AskWhatTheySaw`:

```csharp
/// <summary>
/// The T key's ask, with Phase 2's consequences. A CIVILIAN who questions a witness is
/// REMEMBERED — the ask joins AskEvents at the witness's own tile, so anybody who could
/// see that doorstep at that minute can later say somebody was going around asking
/// questions; the killer's own canvass becomes a sighting. A BADGE ask instead lands the
/// witness's words in every case the town knows about and has not shut, through
/// ResponseCases.BadgeAsked — the county canvass's sibling seam. Recorded AFTER the
/// answer is taken, so the testimony handed back never contains the ask that produced it.
/// The county's own canvass does not come through here and is neither remembered nor
/// double-filed — CountyReachedDoor already files it.
/// </summary>
public string[] PlayerAsks(CitizenId who, int day)
{
    string[] lines = AskWhatTheySaw(who, day);
    if (Sim == null || People == null) return lines;

    if (Badge)
    {
        for (int i = 0; i < _cases.Count; i++)
        {
            CaseState s = _cases.StateOf(i);
            if (s == CaseState.Undiscovered || s == CaseState.Closed) continue;
            _cases.BadgeAsked(i, who, lines);
        }
    }
    else
    {
        _askEvents.Record(NowMinute(), Sim.GetAgent(who).Position.ToTile());
    }
    return lines;
}
```

- [ ] **Step 4: Point the T key at it.** In `VillageUI.Ask()` (:742), change

```csharp
_said = _host.AskWhatTheySaw(who, day);
```

to

```csharp
_said = _host.PlayerAsks(who, day);
```

- [ ] **Step 5: All three Unity assemblies compile** (check exit codes, not `| tail`):

Run: `dotnet build Noir.Unity.csproj -c Debug` then `dotnet build Noir.Editor.csproj -c Debug` then `dotnet build Noir.PlayTests.csproj -c Debug`
Expected: exit 0, 0 errors, all three.

- [ ] **Step 6: Commit**

```bash
git add Assets/Noir/Unity/VillageHost.cs Assets/Noir/Unity/VillageUI.cs
git commit -m "PlayerAsks: a civilian is remembered, a badge writes the file"
```

---

### Task 6: Land it — the Core gate, the ledgers, the push

- [ ] **Step 1: Run the full Core gate in Release** (bare, never `| tail`):

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj`
Expected: **578 pass, 0 fail, 8 skipped** (569 + 9: 2 testimony, 3 AskEvents, 2 Recollection,
4 ResponseCases — recount against what actually landed and use the MEASURED number).

- [ ] **Step 2: Update CLAUDE.md's Core baseline** — new entry above the 569 one, same style:
  the measured counts, the date, one sentence on what the +9 are, plan path.

- [ ] **Step 3: Update the spec** — `docs/superpowers/specs/2026-08-17-witness-voices-design.md`
  Phase 2 section: "NOT tonight" is now stale; mark Phase 2 landed with the date and this
  plan's path.

- [ ] **Step 4: Commit and push**

```bash
git add CLAUDE.md docs/superpowers/specs/2026-08-17-witness-voices-design.md docs/superpowers/plans/2026-08-17-witness-voices-phase2.md
git commit -m "Witness voices phase 2 lands: asking has a memory, the badge has a file"
git push
```

- [ ] **Step 5: Note what is deliberately left.** PlayMode coverage of `PlayerAsks` is
  additive-safe and waits for the next editor-closed gate window (CLAUDE.md's own rule);
  a bubble or UI acknowledgment of "you were seen asking" is a Phase 3 idea, not built;
  the asker's `PersonDescription` blur stays reserved (`EventSighting`'s header), so
  witnesses say "somebody" and nothing more.
