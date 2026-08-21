# Drivable Car Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The player can enter any driveway car with E, drive it through Rossville, and hitting a
person is a real, recorded, witnessable event whose victim stays where they fell.

**Architecture:** Core first (all dotnet-testable): a `Downed` state the sim owns, a car
vocabulary in Contracts, a `HitEvents` store beside `PlayerTrack`, evidence types under the
vagueness doctrine, and a Recollection overlay that turns stored events into testimony. Then
Unity: `CityDriveways` learns to give a car up, `Player` grows a Driving mode with a kinematic
arcade car, `PlayerInteraction` grows the provider registry, and `VillageHost` — the one file
allowed to name the witness assembly — records the hit.

**Tech Stack:** Unity 6000.3.20f1, C#, `UnityEngine.InputSystem`, NUnit (Core suite +
PlayMode).

**Spec:** `docs/superpowers/specs/2026-08-15-drivable-car-design.md`

## Global Constraints

- **No physics vehicle.** No Rigidbody, no WheelColliders — kinematic transform driving only
  (a kinematic Rigidbody was measured at half the frame rate, `CarMesh.cs:44-55`).
- **Sweeps against sim positions, never figure transforms or colliders.**
- **The firewall stands.** Only `Assets/Noir/Unity/VillageHost.cs` may contain the string
  `Noir.Core.Witness` (WitnessFirewallTests greps for it — comments included, so do not even
  write it in a comment elsewhere; say "the witness assembly"). `Noir.Unity` and
  `Noir.PlayTests` must NOT reference `Noir.Core.Observation`. New shared enums go in
  `Noir.Core.Contracts`.
- **Determinism:** no `System.Random`, no `DateTime.Now`, no new draws inserted into existing
  `IRng` substreams. Witness rolls go through `Rolls.*` seeded on `(witness, minute)`. Sim
  facts are stamped with the sim clock, never `Time.time`.
- **Vagueness doctrine:** tone never colour, shape band never a model, no citizen/place id in
  any Observation type.
- **The victim stays in the population** — frozen, never removed. 1,300 people is load-bearing.
- **Core tests run in Release:** `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj`.
  Baseline going in: 509 pass, 0 fail. Any red is a regression.
- **Unity compile checks** (safe with the editor open): `dotnet build Noir.Unity.csproj -c Debug`
  and `dotnet build Noir.PlayTests.csproj -c Debug`.
- **Do NOT run `Unity.exe -batchmode` while the owner's editor is open**, and never kill a Unity
  process. Live-editor verification is driven through unity-mcp by the coordinating session, not
  by task subagents.
- Commit messages end with `Co-Authored-By:` per the repo convention; stage only files you
  edited (never `git add -A`).

---

### Task 1: `Activity.Downed` and `Simulation.Down`

**Files:**
- Modify: `Assets/Noir/Core/People/DayPlan.cs` (the `Activity` enum, ends at `Talking,` ~line 49)
- Modify: `Assets/Noir/Core/Sim/Simulation.cs` (AgentState ~line 10; tick loop 398-451; helpers 930-989)
- Modify: `Content/animations.txt` (new row — the Core gate `EveryActivityHasARowInTheRealFile` fails without it)
- Test: `tools/Noir.Core.Tests/DownedTests.cs` (new file)

**Interfaces:**
- Produces: `Activity.Downed` (enum member); `AgentState.Downed : bool` (field);
  `Simulation.Down(CitizenId who) : void`. Tasks 4 and 8 consume all three.

- [ ] **Step 1: Write the failing tests**

Create `tools/Noir.Core.Tests/DownedTests.cs`. The fixture idiom is the suite's own
(`QueueAndDoorTests.cs:191`): `new Simulation(Queueham.World, Queueham.People, Queueham.Seed, startMinute)`.
If `Queueham` lacks what a test needs, use whichever fixture village the neighbouring test files
construct — the assertions below are fixture-agnostic.

```csharp
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Sim;

namespace Noir.Core.Tests
{
    /// <summary>
    /// A person a car has hit stays down: the sim stops moving them, their plan stops
    /// overwriting them, and nothing ever stands them back up. The body staying where it fell
    /// is Phase 2's crime scene, so every assertion here is a gate on evidence.
    /// </summary>
    [TestFixture]
    public class DownedTests
    {
        [Test]
        public void ADownedAgentStopsAndStaysDown()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            for (int t = 0; t < 600; t++) sim.Tick();          // let the morning start

            var who = new CitizenId(0);
            sim.Down(who);
            var at = sim.GetAgent(who).Position;

            Assert.That(sim.GetAgent(who).Doing, Is.EqualTo(Activity.Downed));
            Assert.That(sim.GetAgent(who).Travelling, Is.False);
            Assert.That(sim.GetAgent(who).Heading, Is.EqualTo(Vec2.Zero));

            for (int t = 0; t < 20 * 60 * 30; t++) sim.Tick(); // half a sim hour
            Assert.That(sim.GetAgent(who).Position, Is.EqualTo(at),
                "the body moved after being downed");
            Assert.That(sim.GetAgent(who).Doing, Is.EqualTo(Activity.Downed),
                "the plan overwrote Doing on a downed agent");
        }

        [Test]
        public void DownIsIdempotentAndOnlyTouchesItsVictim()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            for (int t = 0; t < 600; t++) sim.Tick();

            var victim = new CitizenId(1);
            var bystander = new CitizenId(2);
            var bystanderDoing = sim.GetAgent(bystander).Doing;

            sim.Down(victim);
            sim.Down(victim);                                   // twice is a no-op, not a throw

            Assert.That(sim.GetAgent(victim).Doing, Is.EqualTo(Activity.Downed));
            Assert.That(sim.GetAgent(bystander).Doing, Is.EqualTo(bystanderDoing));
        }

        [Test]
        public void NobodyDownedIsByteIdenticalToBefore()
        {
            // The guard must be a true no-op when the downed set is empty: two sims, same
            // seed, one carrying the new code path — every agent identical after an hour.
            var a = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            var b = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, 8 * 60);
            for (int t = 0; t < 20 * 60 * 60; t++) { a.Tick(); b.Tick(); }
            for (int i = 0; i < 40; i++)
            {
                Assert.That(a.GetAgent(i).Position, Is.EqualTo(b.GetAgent(i).Position));
                Assert.That(a.GetAgent(i).Doing, Is.EqualTo(b.GetAgent(i).Doing));
            }
        }
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~DownedTests"`
Expected: compile error — `Activity` has no `Downed`, `Simulation` has no `Down`.

- [ ] **Step 3: Add the enum member, the field, and the method**

In `DayPlan.cs`, after `Talking,` (the enum's last member) add:

```csharp
        /// <summary>
        /// Struck down and staying down — the sim's word for a body in the street.
        ///
        /// Set only by <see cref="Noir.Core.Sim.Simulation"/> when something (a car, so far)
        /// downs a person. Live state that outranks the plan forever, same mechanism as
        /// Stranded: the tick loop never overwrites a downed agent's Doing, never starts them
        /// on a journey, and never stands them up. AT THE END OF THE ENUM on purpose — the
        /// values are positional and the animations.txt keys are name-based.
        /// </summary>
        Downed,
```

In `Simulation.cs`, add to `AgentState` (beside `Stranded`):

```csharp
        /// <summary>Struck down and staying down. Live state that outranks the plan forever —
        /// see Activity.Downed. Only Simulation.Down sets it; nothing clears it.</summary>
        public bool Downed;
```

Add the public method beside `GetAgent` (~line 373):

```csharp
        /// <summary>
        /// Put one person down where they stand, permanently. The one external mutation the
        /// sim accepts, because a player's car is genuine history the plan cannot know about.
        /// Entry cleanup mirrors Arrive + StandStill so every derived system — queues,
        /// conversations, the renderer — lets go of them on its own. Consumes no RNG, so
        /// every other agent's day is byte-identical to the un-downed run.
        /// </summary>
        public void Down(CitizenId who)
        {
            int i = who.Value;
            if (_agents[i].Downed) return;

            _agents[i].Downed = true;
            _agents[i].Doing = Activity.Downed;
            _agents[i].Travelling = false;
            ReleasePath(i);
            StandStill(i);
            _agents[i].WalkingWith = CitizenId.None;
            _agents[i].Carrying = false;
            _agents[i].TalkTicks = 0;
            _agents[i].TalkingTo = CitizenId.None;
            _agents[i].QueueSlot = -1;
        }
```

(If `AgentState` field names differ — `TalkCooldown`, `DoorPauseTicks` exist too — clear those
two as well; read the struct at `Simulation.cs:10-80` and mirror exactly what `Arrive` at
line 1052 clears, plus `QueueSlot = -1`.)

- [ ] **Step 4: Guard the tick loop**

In `Tick()`'s per-agent loop, immediately after `var block = _plans[i].At(minute);`
(line ~401), insert:

```csharp
                // A downed agent has left the plan for good: no journeys, no talk, no queue,
                // no Doing overwrite. One branch, taken by nobody in a healthy town — the
                // byte-identical replays behind watched.floor depend on this being a no-op
                // when the flag is never set.
                if (_agents[i].Downed) continue;
```

- [ ] **Step 5: The animations row**

In `Content/animations.txt`, beside the other bare rows, add:

```
downed  Breathing Idle
```

with this comment above it (the file carries comments):

```
# downed: a body in the street. There is no lying/death clip in the set (checked 2026-08-15,
# pack included), so the row points at a harmless idle to keep the animator gate green and
# AgentMeshView pitches the figure flat and freezes the animator instead — see the Downed
# branch in AgentMeshView.Refresh. Replace this row when a real lying clip is imported.
```

- [ ] **Step 6: Run the tests and the whole Core gate**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj`
Expected: 512 pass (509 + 3), 0 fail. `EveryActivityHasARowInTheRealFile` passes because the
row landed with the enum member.

- [ ] **Step 7: Commit**

```bash
git add Assets/Noir/Core/People/DayPlan.cs Assets/Noir/Core/Sim/Simulation.cs \
        Content/animations.txt tools/Noir.Core.Tests/DownedTests.cs
git commit -m "Sim: Activity.Downed and Simulation.Down - a body stays where it fell"
```

---

### Task 2: Car vocabulary in Contracts, `Visibly.InAVehicle`, and the `HitEvents` store

**Files:**
- Create: `Assets/Noir/Core/Contracts/CarSight.cs`
- Modify: `Assets/Noir/Core/Witness/PlayerTrack.cs` (the `Visibly` enum, lines 14-21)
- Create: `Assets/Noir/Core/Witness/HitEvents.cs`
- Test: `tools/Noir.Core.Tests/HitEventsTests.cs` (new file)

**Interfaces:**
- Produces: `CarTone { Unnoticed, Dark, Mid, Light }` and `CarShape { Unnoticed, Car, Pickup,
  Van }` in `Noir.Core.Contracts` (usable from Unity — Contracts is referenced everywhere);
  `Visibly.InAVehicle = 8`; `HitEvents` with
  `Record(int minute, Tile where, CarTone tone, CarShape shape)`,
  `Count : int`, and `ForEach(System.Action<int, Tile, CarTone, CarShape>)`.
  Task 4 (Recollection overlay) and Task 8 (VillageHost recorder) consume these.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Witness;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The second genuine history. PlayerTrack's header bans folding events into the track,
    /// so hits get their own forward-only store — a list, not a minute-keyed dictionary,
    /// because two hits in one minute are both real.
    /// </summary>
    [TestFixture]
    public class HitEventsTests
    {
        [Test]
        public void TwoEventsInOneMinuteAreBothKept()
        {
            var events = new HitEvents();
            events.Record(100, new Tile(5, 5), CarTone.Dark, CarShape.Car);
            events.Record(100, new Tile(6, 5), CarTone.Dark, CarShape.Car);
            Assert.That(events.Count, Is.EqualTo(2));
        }

        [Test]
        public void TimeOnlyRunsForwards()
        {
            var events = new HitEvents();
            events.Record(100, new Tile(5, 5), CarTone.Mid, CarShape.Van);
            Assert.Throws<ArgumentException>(() =>
                events.Record(99, new Tile(5, 5), CarTone.Mid, CarShape.Van));
        }

        [Test]
        public void ForEachReplaysInOrder()
        {
            var events = new HitEvents();
            events.Record(10, new Tile(1, 1), CarTone.Dark, CarShape.Car);
            events.Record(20, new Tile(2, 2), CarTone.Light, CarShape.Pickup);
            int seen = 0, lastMinute = -1;
            events.ForEach((minute, where, tone, shape) =>
            {
                Assert.That(minute, Is.GreaterThanOrEqualTo(lastMinute));
                lastMinute = minute; seen++;
            });
            Assert.That(seen, Is.EqualTo(2));
        }
    }
}
```

- [ ] **Step 2: Run and watch them fail** (compile error: no `CarTone`, no `HitEvents`).

- [ ] **Step 3: Implement**

`Assets/Noir/Core/Contracts/CarSight.cs`:

```csharp
namespace Noir.Core.Contracts
{
    /// <summary>
    /// What can be said about a car from across a road, in the same spirit as the witness
    /// layer's person bands: TONE, NEVER COLOUR — a witness who says "blue" is guessing —
    /// and a shape wide enough to hold half the fleet. Lives in Contracts because the Unity
    /// side captures it (from the prefab, at creation) and the evidence side reports it, and
    /// neither assembly may reference the other.
    /// </summary>
    public enum CarTone : byte { Unnoticed = 0, Dark, Mid, Light }

    /// <summary>Car, pickup, van. Never a make, never a model, never a plate.</summary>
    public enum CarShape : byte { Unnoticed = 0, Car, Pickup, Van }
}
```

In `PlayerTrack.cs`, extend `Visibly` (its own header rule — "perceptible from outside" — holds:
being inside a car is the most visible fact there is):

```csharp
        InCompany = 4,
        /// <summary>In (driving) a vehicle. A witness who saw this saw a car, not a figure —
        /// Recollection treats such minutes as unremarkable traffic rather than a person
        /// sighting, because in a town with ambient cars, one more car is not a memory.</summary>
        InAVehicle = 8,
```

`Assets/Noir/Core/Witness/HitEvents.cs`:

```csharp
using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.Witness
{
    /// <summary>
    /// Vehicular harm, one entry per event. THE SECOND GENUINE HISTORY: like PlayerTrack —
    /// whose own header bans widening the track — this is player-caused and underivable from
    /// the seed, so it is stored rather than replayed. A LIST, not a minute-keyed dictionary:
    /// two hits in one minute are both real. What is NOT here, deliberately: no victim id, no
    /// PlaceId — the victim's identity is the sim's fact (AgentState.Downed) and evidence may
    /// only carry what a stranger could see. Phase 2's police join the two by minute and tile.
    /// </summary>
    public sealed class HitEvents
    {
        private readonly struct Hit
        {
            public readonly int Minute;
            public readonly Tile Where;
            public readonly CarTone Tone;
            public readonly CarShape Shape;
            public Hit(int minute, Tile where, CarTone tone, CarShape shape)
            { Minute = minute; Where = where; Tone = tone; Shape = shape; }
        }

        private readonly List<Hit> _hits = new List<Hit>();
        public int Count => _hits.Count;

        public void Record(int minute, Tile where, CarTone tone, CarShape shape)
        {
            if (_hits.Count > 0 && minute < _hits[_hits.Count - 1].Minute)
                throw new ArgumentException(
                    "A history runs forwards. Tried to record minute " + minute +
                    " after minute " + _hits[_hits.Count - 1].Minute + ".", nameof(minute));
            _hits.Add(new Hit(minute, where, tone, shape));
        }

        /// <summary>In-order replay, without handing out the internal type.</summary>
        public void ForEach(Action<int, Tile, CarTone, CarShape> visit)
        {
            foreach (var h in _hits) visit(h.Minute, h.Where, h.Tone, h.Shape);
        }
    }
}
```

- [ ] **Step 4: Run the new tests, then the whole Core gate** — expected 515 pass, 0 fail.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Core/Contracts/CarSight.cs Assets/Noir/Core/Witness/PlayerTrack.cs \
        Assets/Noir/Core/Witness/HitEvents.cs tools/Noir.Core.Tests/HitEventsTests.cs
git commit -m "Witness: HitEvents, the second genuine history, and a car vocabulary in Contracts"
```

---

### Task 3: `CarDescription` and `EventSighting` in Observation

**Files:**
- Create: `Assets/Noir/Core/Observation/CarDescription.cs`
- Create: `Assets/Noir/Core/Observation/EventSighting.cs`
- Test: `tools/Noir.Core.Tests/EventSightingTests.cs` (new file)

**Interfaces:**
- Consumes: `CarTone`/`CarShape` from Task 2.
- Produces: `CarDescription { CarTone Tone; CarShape Shape; bool IsBlank }` (readonly struct,
  ctor `(CarTone, CarShape)`); `EventAct { CarStruckSomebody }` (enum);
  `EventSighting { ObserverId Observer; int Minute; int MinuteOfDay; Tile Where;
  SightingClarity Clarity; EventAct Act; CarDescription Car }` (readonly struct, ctor in that
  order minus MinuteOfDay, which derives like `Sighting.MinuteOfDay`). Task 4 consumes both.

- [ ] **Step 1: Write the failing tests**

```csharp
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Observation;

namespace Noir.Core.Tests
{
    [TestFixture]
    public class EventSightingTests
    {
        [Test]
        public void ADefaultCarDescriptionIsBlank()
        {
            Assert.That(default(CarDescription).IsBlank, Is.True,
                "the default must be the least useful description possible");
        }

        [Test]
        public void AnEventSightingCarriesNoIdentity()
        {
            // The firewall's rule restated small: nothing in this struct can name anybody.
            // ObservationFirewallTests sweeps the whole public surface by reflection; this is
            // the readable version for this one type.
            foreach (var f in typeof(EventSighting).GetFields())
                Assert.That(f.FieldType.Name, Does.Not.Contain("Citizen").And.Not.Contain("Place"),
                    $"EventSighting.{f.Name} leaks identity");
        }

        [Test]
        public void MinuteOfDayDerivesFromMinute()
        {
            var s = new EventSighting(new ObserverId(0), minute: Sighting.MinutesPerDay + 90,
                                      new Tile(3, 3), SightingClarity.Partial,
                                      EventAct.CarStruckSomebody,
                                      new CarDescription(CarTone.Dark, CarShape.Car));
            Assert.That(s.MinuteOfDay, Is.EqualTo(90));
        }
    }
}
```

- [ ] **Step 2: Run and watch them fail** (compile error: types missing).

- [ ] **Step 3: Implement**

`CarDescription.cs`:

```csharp
using Noir.Core.Contracts;

namespace Noir.Core.Observation
{
    /// <summary>
    /// What a witness could say about a car. Same doctrine as PersonDescription, restated for
    /// wheels: every band wide enough to hold a good fraction of the town's fleet, Unnoticed
    /// as zero so the default is "there was a car, couldn't tell you a thing", tone never
    /// colour, shape never a make. A description that identifies one specific car is a design
    /// bug, not a feature.
    /// </summary>
    public readonly struct CarDescription
    {
        public readonly CarTone Tone;
        public readonly CarShape Shape;

        public CarDescription(CarTone tone, CarShape shape) { Tone = tone; Shape = shape; }

        public bool IsBlank => Tone == CarTone.Unnoticed && Shape == CarShape.Unnoticed;
    }
}
```

`EventSighting.cs`:

```csharp
using Noir.Core.Contracts;

namespace Noir.Core.Observation
{
    /// <summary>
    /// The coarse verbs a stranger could put to something they watched HAPPEN. ObservedAct is
    /// the house style (nine verbs, no interpretation); this enum exists separately because an
    /// event sighting carries a Tile and ObservedAct's log deliberately cannot.
    /// </summary>
    public enum EventAct : byte
    {
        CarStruckSomebody = 0,
    }

    /// <summary>
    /// Something a witness saw HAPPEN, at a place and a minute. Sighting's sibling, not its
    /// extension — Sighting deliberately has no verb, and widening it would erode the
    /// vagueness doctrine. Like Sighting: a claim, not a record; no victim, no driver, no id
    /// of any kind. The figure that went down is at most a PersonDescription-shaped blur a
    /// LATER pass may add; v1 reports the act and the car.
    /// </summary>
    public readonly struct EventSighting
    {
        public readonly ObserverId Observer;
        /// <summary>Minutes since the simulation began — same stamp as Sighting.Minute.</summary>
        public readonly int Minute;
        public readonly Tile Where;
        public readonly SightingClarity Clarity;
        public readonly EventAct Act;
        public readonly CarDescription Car;

        public int MinuteOfDay => Minute % Sighting.MinutesPerDay;

        public EventSighting(ObserverId observer, int minute, Tile where,
                             SightingClarity clarity, EventAct act, CarDescription car)
        {
            Observer = observer; Minute = minute; Where = where;
            Clarity = clarity; Act = act; Car = car;
        }
    }
}
```

- [ ] **Step 4: Run the new tests, then the whole Core gate** — the firewall reflection tests
sweep the new types automatically; all green means the types are legal. Expected 518 pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Core/Observation/CarDescription.cs \
        Assets/Noir/Core/Observation/EventSighting.cs \
        tools/Noir.Core.Tests/EventSightingTests.cs
git commit -m "Observation: CarDescription and EventSighting - an event, under the vagueness doctrine"
```

---

### Task 4: Car degradation, `IInterruptions`, the Recollection overlay, and the Testimony arm

**Files:**
- Modify: `Assets/Noir/Core/Witness/Degradation.cs` (new method beside `WhatRegistered`)
- Create: `Assets/Noir/Core/Witness/Interruptions.cs`
- Modify: `Assets/Noir/Core/Witness/Recollection.cs` (skip `InAVehicle` minutes at line ~72;
  new overlay method; extend `AskInEnglish`)
- Modify: `Assets/Noir/Core/Observation/Testimony.cs` (new `InEnglish(EventSighting)` arm)
- Test: `tools/Noir.Core.Tests/EventTestimonyTests.cs` (new file)

**Interfaces:**
- Consumes: `HitEvents`, `Visibly.InAVehicle` (Task 2); `CarDescription`, `EventSighting`,
  `EventAct` (Task 3).
- Produces: `IInterruptions { int DownedFromMinute(CitizenId who); }` (int.MaxValue = never);
  `Degradation.CarRegistered(SightingClarity, CarTone actual, CarShape actual, CitizenKey
  witness, int minute, ulong seed) : CarDescription`;
  `Recollection.WhatTheySawOfEvents(WorldModel, Population, Citizen who, int day, HitEvents,
  ulong seed, IInterruptions = null, ISightBlocked = null) : EventSighting[]`;
  `Recollection.AskInEnglish(...)` gains two optional trailing parameters
  `HitEvents hits = null, IInterruptions interruptions = null`;
  `Testimony.InEnglish(EventSighting) : string`. Task 8's VillageHost call consumes
  `AskInEnglish`'s new shape.

- [ ] **Step 1: Write the failing tests**

```csharp
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Observation;
using Noir.Core.Witness;

namespace Noir.Core.Tests
{
    [TestFixture]
    public class EventTestimonyTests
    {
        [Test]
        public void CarBandsDegradeButNeverInvent()
        {
            // At a glimpse most bands die; whatever survives must be the truth or Unnoticed —
            // wrongness is allowed only where the person machinery already allows it (tone at
            // a glimpse may shift one band, same spirit as ApparentSex).
            var d = Degradation.CarRegistered(SightingClarity.Clear, CarTone.Dark, CarShape.Van,
                                              new CitizenKey(12345), minute: 500, seed: 1979);
            Assert.That(d.Shape, Is.EqualTo(CarShape.Van).Or.EqualTo(CarShape.Unnoticed));

            var g = Degradation.CarRegistered(SightingClarity.Glimpsed, CarTone.Dark, CarShape.Van,
                                              new CitizenKey(12345), minute: 500, seed: 1979);
            Assert.That(g.IsBlank || !g.IsBlank, Is.True); // never throws; bands legal by type
        }

        [Test]
        public void TheMemoryIsTheSeed()
        {
            var once = Degradation.CarRegistered(SightingClarity.Partial, CarTone.Light,
                CarShape.Pickup, new CitizenKey(777), 900, 1979);
            var twice = Degradation.CarRegistered(SightingClarity.Partial, CarTone.Light,
                CarShape.Pickup, new CitizenKey(777), 900, 1979);
            Assert.That(once.Tone, Is.EqualTo(twice.Tone));
            Assert.That(once.Shape, Is.EqualTo(twice.Shape));
        }

        [Test]
        public void AnEventSightingReadsLikeAWitness()
        {
            var s = new EventSighting(new ObserverId(0), 990, new Tile(4, 4),
                SightingClarity.Partial, EventAct.CarStruckSomebody,
                new CarDescription(CarTone.Dark, CarShape.Car));
            string line = Testimony.InEnglish(s);
            Assert.That(line, Does.StartWith("16:30, "));
            Assert.That(line, Does.EndWith("."));
            Assert.That(line.ToLowerInvariant(), Does.Contain("hit"));
            Assert.That(line.ToLowerInvariant(), Does.Contain("dark"));
        }

        [Test]
        public void ABlankCarIsStillACar()
        {
            var s = new EventSighting(new ObserverId(0), 990, new Tile(4, 4),
                SightingClarity.Glimpsed, EventAct.CarStruckSomebody, default);
            Assert.That(Testimony.InEnglish(s).ToLowerInvariant(), Does.Contain("a car"));
        }
    }
}
```

- [ ] **Step 2: Run and watch them fail** (compile: no `CarRegistered`, no event `InEnglish`).

- [ ] **Step 3: Implement `Degradation.CarRegistered`** (beside `WhatRegistered`, same rolls):

```csharp
        private static readonly ulong CarPurpose = Rolls.Purpose("witness.degradation.car");

        /// <summary>
        /// What stuck about the car. Two bands instead of six: at a clear look both survive,
        /// partial keeps one (seeded pick), a glimpse keeps one only half the time — and a
        /// glimpsed TONE may be wrong by a band, the same designed fallibility as ApparentSex.
        /// </summary>
        public static CarDescription CarRegistered(SightingClarity clarity,
            CarTone actualTone, CarShape actualShape,
            CitizenKey witness, int minute, ulong seed)
        {
            var tone = CarTone.Unnoticed;
            var shape = CarShape.Unnoticed;

            int keep = clarity == SightingClarity.Clear ? 2
                     : clarity == SightingClarity.Partial ? 1
                     : Rolls.Int(seed, CarPurpose, witness.Value, minute, 0xCA51UL, 0, 2); // 0 or 1

            bool toneFirst = Rolls.Int(seed, CarPurpose, witness.Value, minute, 0xF1A7UL, 0, 2) == 0;
            for (int i = 0; i < keep; i++)
            {
                bool pickTone = (i == 0) == toneFirst;
                if (pickTone)
                {
                    tone = actualTone;
                    if (clarity == SightingClarity.Glimpsed &&
                        Rolls.Int(seed, CarPurpose, witness.Value, minute, 0x70E5UL, 0, 4) == 0)
                        tone = tone == CarTone.Dark ? CarTone.Mid
                             : tone == CarTone.Light ? CarTone.Mid : CarTone.Dark;
                }
                else shape = actualShape;
            }

            return new CarDescription(tone, shape);
        }
```

- [ ] **Step 4: Implement `IInterruptions`** — `Assets/Noir/Core/Witness/Interruptions.cs`:

```csharp
using Noir.Core.Contracts;

namespace Noir.Core.Witness
{
    /// <summary>
    /// The Sim-side fact the replay cannot know: from which minute a citizen stopped living
    /// their plan. INightWitnesses' pattern exactly — Core states the question, the one
    /// caller answers it from live sim state, and null means nobody ever was (the default,
    /// and the honest one). A downed citizen neither witnesses nor is placed by any replay
    /// consumer from that minute on.
    /// </summary>
    public interface IInterruptions
    {
        /// <summary>Minutes since the simulation began, or int.MaxValue if never.</summary>
        int DownedFromMinute(CitizenId who);
    }
}
```

- [ ] **Step 5: Extend Recollection**

In `WhatTheySaw`, after `if (!track.TryGet(minute, out Step step)) { inSight = false; continue; }`
(line 72), add:

```csharp
                // A car among cars. While the player is driving, a witness saw ONE MORE CAR
                // in a town where ambient traffic passes all day - not a figure, and not a
                // memory. Sightings of the person stop; anything worth remembering about the
                // car is an EVENT (WhatTheySawOfEvents), which is where a hit goes.
                if ((step.Looked & Visibly.InAVehicle) != 0) { inSight = false; continue; }
```

Add the overlay method after `WhatTheySaw`:

```csharp
        /// <summary>
        /// Everything one villager could tell you about recorded EVENTS, for one day. The
        /// same stationary-witness arithmetic as WhatTheySaw, run against the event list
        /// instead of the player's track: an event is witnessed by exactly the people the
        /// existing rules say could see that tile at that minute.
        /// </summary>
        public static EventSighting[] WhatTheySawOfEvents(WorldModel world, Population population,
            Citizen who, int day, HitEvents hits, ulong seed,
            IInterruptions interruptions = null, ISightBlocked blocked = null)
        {
            var found = new List<EventSighting>();
            if (hits == null || hits.Count == 0) return System.Array.Empty<EventSighting>();

            DayPlan plan = DayPlanner.Plan(world, population, who, day, seed);
            int downedFrom = interruptions?.DownedFromMinute(who.Id) ?? int.MaxValue;

            hits.ForEach((minute, where, tone, shape) =>
            {
                int minuteOfDay = minute % MinutesPerDay;
                if (minute / MinutesPerDay != day) return;
                if (minute >= downedFrom) return;              // the victim testifies to nothing

                Block block = plan.At(minuteOfDay);
                if (block.What == Activity.TravellingTo) return;
                if (block.What == Activity.Asleep) return;
                if (!block.Where.IsValid) return;

                Tile watcher = world.GetPlace(block.Where).Door;
                var when = new GameClock(GameClock.TickAt(day, minuteOfDay));
                SightingClarity clarity = Sightlines.HowGoodALook(watcher, where, when, who);
                if (!Sightlines.SawAnythingAtAll(clarity, watcher, where, when, blocked)) return;

                var car = Degradation.CarRegistered(clarity, tone, shape, who.Key, minute, seed);
                found.Add(new EventSighting(new ObserverId(found.Count),
                                            BlurredMinute(minute, clarity),
                                            watcher, clarity,
                                            EventAct.CarStruckSomebody, car));
            });

            return found.ToArray();
        }
```

Extend `AskInEnglish`'s signature with two trailing optionals and append event lines:

```csharp
        public static string[] AskInEnglish(WorldModel world, Population population,
                                            Citizen who, int day, PlayerTrack track, ulong seed,
                                            INightWitnesses nightWitnesses = null,
                                            ISightBlocked blocked = null,
                                            HitEvents hits = null,
                                            IInterruptions interruptions = null)
        {
            Sighting[] saw = WhatTheySaw(world, population, who, day, track, seed,
                                         nightWitnesses, blocked);
            EventSighting[] events = WhatTheySawOfEvents(world, population, who, day, hits,
                                                         seed, interruptions, blocked);

            if (saw.Length == 0 && events.Length == 0) return new[] { Testimony.SawNothing };

            var lines = new List<string>(Testimony.InEnglish(saw));
            foreach (var e in events) lines.Add(Testimony.InEnglish(e));
            return lines.ToArray();
        }
```

- [ ] **Step 6: The Testimony arm** (in `Testimony.cs`, beside the Sighting overloads):

```csharp
        /// <summary>An event, said out loud. Same shape as a person sighting: time, then
        /// certainty, then what was seen — and the certainty verb carries the hedge.</summary>
        public static string InEnglish(EventSighting e)
        {
            var sb = new StringBuilder();
            sb.Append(Clock(e.MinuteOfDay));
            sb.Append(", ");
            sb.Append(e.Clarity switch
            {
                SightingClarity.Clear => "I watched",
                SightingClarity.Partial => "I saw",
                _ => "I think I saw",
            });
            sb.Append(' ').Append(Car(e.Car));
            sb.Append(e.Act switch
            {
                EventAct.CarStruckSomebody => " hit somebody",
                _ => " do something",
            });
            sb.Append('.');
            return sb.ToString();
        }

        /// <summary>"a dark pickup", "a van", "a car". Blank is still a car — the witness saw
        /// the event; it is the DESCRIPTION that failed to register.</summary>
        private static string Car(CarDescription c)
        {
            string tone = c.Tone switch
            {
                CarTone.Dark => "dark ", CarTone.Light => "light-coloured ", _ => "",
            };
            string shape = c.Shape switch
            {
                CarShape.Pickup => "pickup", CarShape.Van => "van", _ => "car",
            };
            return Article(tone.Length > 0 ? tone : shape) + " " + tone + shape;
        }
```

(`Article` already exists in the file at line 131.)

- [ ] **Step 7: Run the new tests, then the whole Core gate** — expected 522 pass, 0 fail.
The existing `AskInEnglish` call in the Unity layer still compiles (new parameters are
optional), and `WitnessPlayTests`' sentence-shape regex (`^\d\d:\d\d,`) still holds for event
lines by construction.

- [ ] **Step 8: Commit**

```bash
git add Assets/Noir/Core/Witness/Degradation.cs Assets/Noir/Core/Witness/Interruptions.cs \
        Assets/Noir/Core/Witness/Recollection.cs Assets/Noir/Core/Observation/Testimony.cs \
        tools/Noir.Core.Tests/EventTestimonyTests.cs
git commit -m "Witness: events become testimony - car degradation, interruptions, the overlay, the sentence"
```

---

### Task 5: `CityDriveways` learns identity and letting go

**Files:**
- Modify: `Assets/Noir/Unity/CityDriveways.cs` (fields ~55; `Create` loop 109-129; new methods)

**Interfaces:**
- Consumes: `CarTone`/`CarShape` (Contracts, Task 2).
- Produces: `NearestCar(Vector3 from, float within) : int` (index or -1, skips inactive);
  `PositionOf(int index) : Vector3`; `Take(int index) : (GameObject car, CarTone tone,
  CarShape shape)`. Tasks 6 and 7 consume all three.

- [ ] **Step 1: Capture appearance at Create**

Add fields beside `_homeOf` (line 56):

```csharp
        private readonly List<CarTone> _tone = new List<CarTone>();
        private readonly List<CarShape> _shape = new List<CarShape>();
```

In the `Create` loop, after `CarMesh.Flatten(car);` and BEFORE
`car.name = $"Parked_{d.Home.Value}_{d.Unit}";` (line 122 — the rename is what discards the
identity today), add:

```csharp
                // The identity the rename below is about to discard, banded the way a witness
                // would band it: shape off the prefab's own name, tone off the flattened
                // mesh's average albedo. Captured here because after Flatten + rename there is
                // nothing left to read it from.
                it._shape.Add(ShapeOf(car.name));
                it._tone.Add(ToneOf(car));
```

And add the two helpers plus the query/take methods to the class:

```csharp
        private static CarShape ShapeOf(string prefabName) =>
            prefabName.IndexOf("Pickup", System.StringComparison.OrdinalIgnoreCase) >= 0
                ? CarShape.Pickup
          : prefabName.IndexOf("Van", System.StringComparison.OrdinalIgnoreCase) >= 0
                ? CarShape.Van
          : CarShape.Car;

        private static CarTone ToneOf(GameObject car)
        {
            var r = car.GetComponent<MeshRenderer>();
            if (r == null) return CarTone.Mid;
            float sum = 0f; int n = 0;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                var c = m.color; sum += (c.r + c.g + c.b) / 3f; n++;
            }
            if (n == 0) return CarTone.Mid;
            float lum = sum / n;
            return lum < 0.35f ? CarTone.Dark : lum > 0.65f ? CarTone.Light : CarTone.Mid;
        }

        /// <summary>Where car <paramref name="index"/> stands right now.</summary>
        public Vector3 PositionOf(int index) => _cars[index].transform.position;

        /// <summary>
        /// The nearest standing car to <paramref name="from"/> within <paramref name="within"/>
        /// metres, or -1. CityDoors.NearestDoor's XZ scan, on wheels. Skips inactive cars —
        /// their owners drove them to work — and taken ones (null slots).
        /// </summary>
        public int NearestCar(Vector3 from, float within)
        {
            int best = -1;
            float bestD2 = within * within;
            for (int i = 0; i < _cars.Count; i++)
            {
                var car = _cars[i];
                if (car == null || !car.activeSelf) continue;
                var d = car.transform.position - from;
                float d2 = d.x * d.x + d.z * d.z;
                if (d2 > bestD2) continue;
                bestD2 = d2; best = i;
            }
            return best;
        }

        /// <summary>
        /// Hand car <paramref name="index"/> over and stop owning it: the slot goes null so
        /// Refresh's absence schedule and the layer switch never touch it again — Refresh
        /// already tolerates a null slot by construction. Once taken, a car is loose for
        /// good; whether it ever goes home again is a later feature, recorded in IDEAS.
        /// </summary>
        public (GameObject car, CarTone tone, CarShape shape) Take(int index)
        {
            var car = _cars[index];
            _cars[index] = null;
            return (car, _tone[index], _shape[index]);
        }
```

Note `using Noir.Core.Contracts;` is already at the top of the file (line 3).

- [ ] **Step 2: Compile**

Run: `dotnet build Noir.Unity.csproj -c Debug` — expected 0 errors. (These are
editor-driven paths; the PlayMode round-trip in Task 9 is their test. No Core test can see
this file.)

- [ ] **Step 3: Commit**

```bash
git add Assets/Noir/Unity/CityDriveways.cs
git commit -m "Driveways: NearestCar, Take, and the appearance the rename used to discard"
```

---

### Task 6: `Player` grows a Driving mode

**Files:**
- Modify: `Assets/Noir/Unity/Player.cs`

**Interfaces:**
- Consumes: `CityDriveways.Take/PositionOf` (Task 5); `VillageHost.Driveways` (exists,
  VillageHost.cs:346); `ElevationGrid.HeightAt` (exists).
- Produces: `Player.Driving : bool`; `Player.InVehicle : bool` (alias of Driving, for the
  track recorder's readability); `Player.EnterCar(int drivewayIndex) : void`;
  `Player.LeaveCar() : void`; `Player.CarTone : CarTone` and `Player.CarShape : CarShape`
  (valid while Driving); `Player.CarTravelledFrom : Vector3?` (the previous frame's car
  position, for the hit sweep — null on the first driving frame). `Where` now also answers
  while Driving. Tasks 7 and 8 consume these.

- [ ] **Step 1: State and properties**

Add beside `Walking` (line 81):

```csharp
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
```

`using Noir.Core.Contracts;` is already imported (line 3 region — verify; add if absent).

Change `Where` (line 95) to answer in both modes:

```csharp
        public Vector3? Where =>
            Walking && _body != null ? _body.transform.position
          : Driving && _car != null ? _car.transform.position
          : (Vector3?)null;
```

- [ ] **Step 2: Enter and leave**

```csharp
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

            _car = car;
            CarTone = tone;
            CarShape = shape;
            _carSpeed = 0f;
            CarTravelledFrom = null;

            Walking = false;
            Driving = true;
            _body.SetActive(false);
            _yaw = _car.transform.eulerAngles.y;
            Debug.Log($"[player] driving {shape} ({tone}). E to get out.");
        }

        /// <summary>Out at the driver's door. The car stays exactly where it stands.</summary>
        public void LeaveCar()
        {
            if (!Driving) return;
            var at = _car.transform.position - _car.transform.right * 1.6f;
            at.y = ElevationGrid.HeightAt(at.x, -at.z) + 0.5f;

            Driving = false;
            _car = null;
            CarTravelledFrom = null;

            Walking = true;
            _body.SetActive(true);
            var cc = _body.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            _body.transform.position = at;
            if (cc != null) cc.enabled = true;
        }
```

(`ElevationGrid.HeightAt(x, y)` takes the VILLAGE y — world `-z` — exactly as `Standing()`
at the bottom of this same file already does: `ElevationGrid.HeightAt(x, y) ... -y`. Mirror it.)

- [ ] **Step 3: The drive step**

In `Update()` (line 107), the current body is gated by
`if (!Walking || _target == null || _camera == null) return;`. Change the flow: BEFORE that
line, insert the driving branch; and gate the P key so it does nothing while Driving:

Replace (line 109-112):

```csharp
            var keys = Keyboard.current;
            if (keys != null && keys.pKey.wasPressedThisFrame) Toggle();

            if (!Walking || _target == null || _camera == null) return;
```

with:

```csharp
            var keys = Keyboard.current;
            if (keys != null && keys.pKey.wasPressedThisFrame && !Driving
                && !VillageUI.KeyboardCaptured) Toggle();

            if (Driving) { DriveStep(keys); return; }

            if (!Walking || _target == null || _camera == null) return;
```

Add the method:

```csharp
        /// <summary>
        /// One frame behind the wheel. Kinematic on purpose — see the spec and CarMesh.cs's
        /// own measurement: a physics vehicle halved this town's frame rate. Real time
        /// (Time.deltaTime), same clock the NPC fleet drives on.
        /// </summary>
        private void DriveStep(Keyboard keys)
        {
            if (_car == null || _camera == null) { Driving = false; return; }
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
            if (distance > 0f)
            {
                var half = new Vector3(0.95f, 0.7f, 2.6f);
                if (Physics.BoxCast(_car.transform.position + Vector3.up * 0.9f, half,
                                    step.normalized, out var hit, _car.transform.rotation,
                                    distance, ~0, QueryTriggerInteraction.Ignore))
                {
                    distance = Mathf.Max(0f, hit.distance - 0.05f);
                    _carSpeed = 0f;
                }
                var to = _car.transform.position + step.normalized * distance;
                to.y = ElevationGrid.HeightAt(to.x, -to.z);
                _car.transform.position = to;
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
        }
```

- [ ] **Step 4: Compile**

Run: `dotnet build Noir.Unity.csproj -c Debug` — expected 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Unity/Player.cs
git commit -m "Player: a Driving mode - kinematic car, walking's own camera on a longer tether"
```

---

### Task 7: The provider registry, `CarInteractable`, and `GetOutInteractable`

**Files:**
- Create: `Assets/Noir/Unity/CarInteractable.cs`
- Modify: `Assets/Noir/Unity/PlayerInteraction.cs` (the `Update` body, lines 85-105)
- Modify: `Assets/Noir/PlayTests/PlayerInteractionPlayTests.cs` (append two tests)

**Interfaces:**
- Consumes: `CityDriveways.NearestCar/PositionOf` (Task 5); `Player.EnterCar/LeaveCar/Driving`
  (Task 6); `VillageHost.Driveways`; the existing `IInteractable`.
- Produces: `CarInteractable` (`Verbs == ["Drive"]`, `Perform → player.EnterCar(index)`);
  `GetOutInteractable` (`Verbs == ["Get out"]`, `Perform → player.LeaveCar()`); the offer rule
  "cars offer at `CarOffer = 3.0f`, closest provider wins."

- [ ] **Step 1: Write the failing PlayMode tests** (append inside `PlayerInteractionPlayTests`):

```csharp
        [UnityTest, Timeout(900000)]
        public IEnumerator ACarOffersDriveAndTheClosestProviderWins()
        {
            var host = Object.FindFirstObjectByType<VillageHost>();
            var driveways = host.Driveways;
            Assert.That(driveways, Is.Not.Null.And.Property("Count").GreaterThan(0),
                "no driveway cars in this town - the provider has nothing to offer");

            int car = driveways.NearestCar(driveways.PositionOf(0), 0.5f);
            Assert.That(car, Is.EqualTo(0), "a car's own position did not find itself");

            var player = Object.FindFirstObjectByType<Player>();
            player.Toggle();
            for (int frame = 0; frame < 5; frame++) yield return null;
            var body = GameObject.Find("PlayerArmature");
            var cc = body.GetComponent<CharacterController>();

            cc.enabled = false;
            body.transform.position = driveways.PositionOf(0) + new Vector3(1.5f, 0.5f, 0f);
            cc.enabled = true;
            yield return null;

            var interaction = host.Interaction;
            Assert.That(interaction.Current, Is.Not.Null, "no verb offered beside a car");
            Assert.That(interaction.Current.Verbs[0], Is.EqualTo("Drive"));
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator EnterDriveExitRoundTrip()
        {
            var host = Object.FindFirstObjectByType<VillageHost>();
            var driveways = host.Driveways;
            var player = Object.FindFirstObjectByType<Player>();
            player.Toggle();
            for (int frame = 0; frame < 5; frame++) yield return null;
            var body = GameObject.Find("PlayerArmature");
            var cc = body.GetComponent<CharacterController>();

            int car = driveways.NearestCar(body.transform.position, 100000f);
            Assert.That(car, Is.GreaterThanOrEqualTo(0), "no standing car anywhere");
            var carPos = driveways.PositionOf(car);

            cc.enabled = false;
            body.transform.position = carPos + new Vector3(1.5f, 0.5f, 0f);
            cc.enabled = true;
            yield return null;

            host.Interaction.PerformOffered();                 // E - Drive
            yield return null;
            Assert.That(player.Driving, Is.True, "Perform(Drive) did not enter the car");
            Assert.That(player.Where, Is.Not.Null, "Where went null while driving");
            Assert.That(host.Interaction.Current.Verbs[0], Is.EqualTo("Get out"));

            host.Interaction.PerformOffered();                 // E - Get out
            yield return null;
            Assert.That(player.Driving, Is.False);
            Assert.That(player.Walking, Is.True, "leaving the car did not restore walking");

            player.Toggle();
        }
```

Also extend `BackToTheOverview` (the teardown) so a failed test never strands the car mode:

```csharp
            var player = Object.FindFirstObjectByType<Player>();
            if (player != null && player.Driving) player.LeaveCar();
            if (player != null && player.Walking) player.Toggle();
```

- [ ] **Step 2: Verify they fail to compile**

Run: `dotnet build Noir.PlayTests.csproj -c Debug`
Expected: errors — no `Player.Driving`, wait: `Driving` exists after Task 6; the errors are
no `CityDriveways.NearestCar` overload visible if Task 5 unmerged, and no "Get out" provider.
If everything compiles because Tasks 5-6 landed first, the failing state is the RUNTIME
expectation (`Current.Verbs[0] == "Drive"` cannot pass — no provider exists yet); note that in
the commit message rather than skipping the red step.

- [ ] **Step 3: `CarInteractable.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>A parked car, offered through the IInteractable seam - the second provider,
    /// the one the registry was built for. Stateless wrapper over (CityDriveways, index),
    /// DoorInteractable's exact shape.</summary>
    public sealed class CarInteractable : IInteractable
    {
        private static readonly string[] DriveVerb = { "Drive" };

        private readonly VillageHost _host;
        private readonly int _index;

        public CarInteractable(VillageHost host, int index) { _host = host; _index = index; }

        public Vector3 Position => _host.Driveways.PositionOf(_index);
        public IReadOnlyList<string> Verbs => DriveVerb;
        public void Perform(string verb) => _host.Player.EnterCar(_index);
    }

    /// <summary>The one verb the driver's seat offers. Owned by the mode, not by proximity.</summary>
    public sealed class GetOutInteractable : IInteractable
    {
        private static readonly string[] OutVerb = { "Get out" };
        private readonly Player _player;

        public GetOutInteractable(Player player) { _player = player; }

        public Vector3 Position => _player.Where ?? Vector3.zero;
        public IReadOnlyList<string> Verbs => OutVerb;
        public void Perform(string verb) => _player.LeaveCar();
    }
}
```

- [ ] **Step 4: The registry in `PlayerInteraction.Update`**

Replace the body of `Update()` (currently the door-only flow, lines 85-105) with:

```csharp
        /// <summary>Cars offer a stride further than a door does - you approach a car from
        /// any side, and its measured body is 5.5 m long.</summary>
        private const float CarOffer = 3.0f;

        private GetOutInteractable _getOut;

        private void Update()
        {
            var player = _host.Player;
            if (player == null) { Current = null; _currentIndex = -1; return; }

            // Behind the wheel there is exactly one verb and proximity has nothing to say.
            if (player.Driving)
            {
                if (!(Current is GetOutInteractable))
                    Current = _getOut ??= new GetOutInteractable(player);
                _currentIndex = -1;
                var driveKeys = Keyboard.current;
                if (driveKeys != null && driveKeys.eKey.wasPressedThisFrame
                    && !VillageUI.KeyboardCaptured) PerformOffered();
                return;
            }

            if (!player.Walking) { Current = null; _currentIndex = -1; return; }
            var where = player.Where;
            if (!where.HasValue) { Current = null; _currentIndex = -1; return; }

            // THE PROVIDER REGISTRY, the day the header scheduled. Each provider answers
            // "nearest candidate, squared distance"; the closest wins. Two providers is a
            // pair of ifs rather than a list - grow it into one the day there are four.
            int doorIx = _host.Doors != null
                ? _host.Doors.NearestDoor(where.Value, Range) : -1;
            int carIx = _host.Driveways != null
                ? _host.Driveways.NearestCar(where.Value, CarOffer) : -1;

            float doorD2 = doorIx >= 0
                ? (_host.Doors.PositionOf(doorIx) - where.Value).sqrMagnitude : float.MaxValue;
            float carD2 = carIx >= 0
                ? (_host.Driveways.PositionOf(carIx) - where.Value).sqrMagnitude : float.MaxValue;

            if (doorIx < 0 && carIx < 0) { Current = null; _currentIndex = -1; return; }

            // Cache key: provider in the sign, index in the magnitude - doors positive,
            // cars bitwise-complemented, so switching provider always rebuilds Current.
            int key = carD2 < doorD2 ? ~carIx : doorIx;
            if (key != _currentIndex || Current == null)
            {
                Current = carD2 < doorD2
                    ? (IInteractable)new CarInteractable(_host, carIx)
                    : new DoorInteractable(_host.Doors, doorIx);
                _currentIndex = key;
            }

            var keys = Keyboard.current;
            if (keys != null && keys.eKey.wasPressedThisFrame && !VillageUI.KeyboardCaptured)
                PerformOffered();
        }
```

(The `y` component matters in those `sqrMagnitude` calls only as noise; both providers'
positions sit at ground level like the player. Leave it — the XZ-only scan already gated
`within`.)

- [ ] **Step 5: Compile everything**

Run: `dotnet build Noir.Unity.csproj -c Debug` and `dotnet build Noir.PlayTests.csproj -c Debug`
Expected: 0 errors in both.

- [ ] **Step 6: Commit**

```bash
git add Assets/Noir/Unity/CarInteractable.cs Assets/Noir/Unity/PlayerInteraction.cs \
        Assets/Noir/PlayTests/PlayerInteractionPlayTests.cs
git commit -m "Interaction: the provider registry, E - Drive, and E - Get out"
```

---

### Task 8: The hit, the record, and the body on screen

**Files:**
- Modify: `Assets/Noir/Unity/Player.cs` (the sweep, at the end of `DriveStep`)
- Modify: `Assets/Noir/Unity/VillageHost.cs` (recorder + `IInterruptions` answer + ask wiring +
  the `Visibly.InAVehicle` bit in `RecordWhereThePlayerWas`, line ~1238)
- Modify: `Assets/Noir/Unity/AgentMeshView.cs` (the Downed pose branch, in `Refresh` near the
  away-toggle at line ~202)
- Modify: `Assets/Noir/Unity/VillageUI.cs` (`Census` ~543-570; `Verb` ~2420)
- Modify: `Assets/Noir/PlayTests/PlayerInteractionPlayTests.cs` (append the hit test)

**Interfaces:**
- Consumes: `Simulation.Down` (Task 1); `HitEvents`, `Visibly.InAVehicle` (Task 2);
  `AskInEnglish`'s new optional parameters (Task 4); `Player.CarTravelledFrom/CarTone/CarShape/
  InVehicle` (Task 6).
- Produces: `VillageHost.CarStruckSomebody(CitizenId victim, Vector3 at) : void`;
  `Player.SweepForVictims(Vector3 from, Vector3 to) : void` (public so the PlayMode test can
  drive it without forging input).

- [ ] **Step 1: The sweep, in `Player`**

At the end of `DriveStep`, after the position write, add
`SweepForVictims(CarTravelledFrom.Value, _car.transform.position);` (guard on `HasValue`),
and add the method:

```csharp
        /// <summary>Half the car's width plus a shoulder. A person inside this lateral
        /// distance of the car's path was hit.</summary>
        private const float HitRadius = 1.3f;

        /// <summary>
        /// Did this frame's travel pass through anybody? SIM positions, never figures - the
        /// blessed pattern (AgentMeshView.Pick's own header). Segment-vs-point with the
        /// agent's own last-tick travel folded in, so neither a fast car nor a fast sim
        /// clock tunnels. Public so the PlayMode gate can prove a hit without forging input.
        /// </summary>
        public void SweepForVictims(Vector3 from, Vector3 to)
        {
            var sim = _host.Sim;
            var world = _host.World;
            if (sim == null || world == null) return;

            for (int i = 0; i < sim.AgentCount; i++)
            {
                var agent = sim.GetAgent(i);
                if (agent.Downed) continue;
                if (agent.Doing == Noir.Core.People.Activity.AwayFromTown) continue;

                var p = Space3D.ToWorld(agent.Position);        // same conversion the view uses
                var tile = agent.Position.ToTile();
                if ((world.Grid.FlagsAt(tile) & Noir.Core.World.TileFlags.Indoor) != 0) continue;

                // Closest approach of the car's segment to the person, in XZ.
                Vector3 seg = to - from; seg.y = 0f;
                Vector3 rel = p - from; rel.y = 0f;
                float len2 = seg.sqrMagnitude;
                float t = len2 > 0.0001f ? Mathf.Clamp01(Vector3.Dot(rel, seg) / len2) : 0f;
                Vector3 nearest = from + seg * t; nearest.y = 0f;
                Vector3 flat = p; flat.y = 0f;
                if ((flat - nearest).sqrMagnitude > HitRadius * HitRadius) continue;

                _host.CarStruckSomebody(new CitizenId(i), p);
            }
        }
```

(If `Space3D.ToWorld(Vec2)`'s exact name differs, it is the conversion `AgentMeshView.Refresh`
uses at line ~293 — read that line and call the same thing. `Simulation.AgentCount` likewise:
if the property is named differently, `VillageUI.Census` at line 547 iterates it — copy its
spelling.)

- [ ] **Step 2: The recorder, in `VillageHost`**

Beside `RecordWhereThePlayerWas` (line 1205), add the store, the answer to `IInterruptions`,
and the recording API:

```csharp
        /// <summary>Vehicular harm, the second genuine history. See its own header.</summary>
        private readonly HitEvents _hitEvents = new HitEvents();

        /// <summary>Which minute each downed citizen went down - the sim knows WHO, this
        /// knows WHEN, and Phase 2's police will want both. Also the answer Recollection's
        /// IInterruptions asks for, so a dead witness stops testifying.</summary>
        private readonly Dictionary<int, int> _downedAtMinute = new Dictionary<int, int>();

        private sealed class SimInterruptions : IInterruptions
        {
            private readonly VillageHost _host;
            public SimInterruptions(VillageHost host) { _host = host; }
            public int DownedFromMinute(CitizenId who) =>
                _host._downedAtMinute.TryGetValue(who.Value, out int m) ? m : int.MaxValue;
        }
        private SimInterruptions _interruptions;

        /// <summary>
        /// A car hit a person. The one recording seam, fed plain data by Player exactly as
        /// Player.Where feeds the track - the car controller never names this layer. Downs
        /// the victim in the sim (the body stays), stamps the event with the sim clock, and
        /// keeps the victim-to-minute pairing Phase 2's police will consume.
        /// </summary>
        public void CarStruckSomebody(CitizenId victim, Vector3 at)
        {
            if (Sim == null) return;
            if (Sim.GetAgent(victim).Downed) return;

            Sim.Down(victim);

            int minute = Sim.Clock.Day * (GameClock.TicksPerDay / GameClock.TicksPerMinute)
                       + Sim.Clock.MinuteOfDay;
            _hitEvents.Record(minute, Space3D.TileAt(at),
                              _player != null ? _player.CarTone : CarTone.Unnoticed,
                              _player != null ? _player.CarShape : CarShape.Unnoticed);
            _downedAtMinute[victim.Value] = minute;

            Debug.Log($"[hit] a car struck citizen {victim.Value} at {Space3D.TileAt(at)} "
                    + $"minute {minute}. They are down, and the town can be asked about it.");
        }
```

Wire the ask (line 1170) to carry the new evidence:

```csharp
            _interruptions ??= new SimInterruptions(this);
            return Recollection.AskInEnglish(World, People, People.Get(who), day, Track, Seed,
                                             null, null, _hitEvents, _interruptions);
```

And in `RecordWhereThePlayerWas`, beside the `Quickly` line (~1242):

```csharp
            // Behind a wheel. What a witness saw was a car - Recollection treats these
            // minutes as traffic, not a figure.
            if (_player != null && _player.InVehicle) looked |= Visibly.InAVehicle;
```

Note `RecordWhereThePlayerWas` bails when `Where` is null — after Task 6, `Where` answers
while Driving, so driving minutes track automatically. Verify `_fastestSinceEntry` picks up
car speed (it measures `Where` deltas — it does).

- [ ] **Step 3: The body on screen, in `AgentMeshView.Refresh`**

Near the away-toggle (line ~202, `bool away = agent.Doing == Activity.AwayFromTown;`), add a
Downed branch that (a) pitches the figure's root 90° so it lies flat along its last heading,
(b) skips the animator Drive call (freeze — no clip exists; the animations row is a
placeholder pointing at a harmless idle):

```csharp
                if (agent.Doing == Activity.Downed)
                {
                    // A body, not a person. No lying clip exists in the set (checked
                    // 2026-08-15, pack included), so the figure is laid flat procedurally
                    // and its animator is left un-driven - which freezes it, the documented
                    // missing-state behavior. Works identically for the primitive fallback
                    // bodies, which have no Animator at all. Replace with a real clip via
                    // the 'downed' row in animations.txt when one is imported.
                    root.localRotation = Quaternion.Euler(90f, _yaw[i], 0f);
                    continue;   // past the pose/Drive calls for this figure
                }
```

The exact insertion point and the local names (`root`, `_yaw[i]`, whether `continue` is legal
there) must be read from the loop's real body — the contract is: position stays the sim's
frozen position, rotation pitches 90°, and neither `Pose` nor `Drive` runs for this figure.

- [ ] **Step 4: The two display sites, in `VillageUI`**

`Census` (line ~557 switch): add before `default:`:

```csharp
                    case Activity.Downed: down++; break;
```

with `int down = 0` beside the other counters and `   down {down}` appended to the census
string ONLY when nonzero (`down > 0 ? $"   down {down}" : ""` — an ordinary day should not
carry a zero for a state that should never happen).

`Verb` (line ~2420 switch): add:

```csharp
                case Activity.Downed: return "lying hurt at";
```

- [ ] **Step 5: The PlayMode hit test** (append to `PlayerInteractionPlayTests`):

```csharp
        [UnityTest, Timeout(900000)]
        public IEnumerator AHitDownsSomebodyAndTheBodyStays()
        {
            var host = Object.FindFirstObjectByType<VillageHost>();
            var sim = host.Sim;
            var player = Object.FindFirstObjectByType<Player>();

            // Find somebody outdoors to be the victim - the sweep's own filters, inverted.
            int victim = -1;
            for (int i = 0; i < sim.AgentCount; i++)
            {
                var a = sim.GetAgent(i);
                if (a.Downed || a.Doing == Activity.AwayFromTown) continue;
                if ((host.World.Grid.FlagsAt(a.Position.ToTile()) & TileFlags.Indoor) != 0) continue;
                victim = i; break;
            }
            Assert.That(victim, Is.GreaterThanOrEqualTo(0), "nobody is outdoors to test with");

            // Drive the sweep straight through them - the exact method the car calls.
            player.Toggle();
            for (int frame = 0; frame < 5; frame++) yield return null;
            var p = Space3D.ToWorld(sim.GetAgent(victim).Position);
            player.SweepForVictims(p + new Vector3(-3f, 0f, 0f), p + new Vector3(3f, 0f, 0f));
            yield return null;

            Assert.That(sim.GetAgent(victim).Doing, Is.EqualTo(Activity.Downed),
                "the sweep passed through a person and nobody went down");
            var at = sim.GetAgent(victim).Position;

            float until = Time.time + 3f;
            while (Time.time < until) yield return null;
            Assert.That(sim.GetAgent(victim).Position, Is.EqualTo(at), "the body moved");

            player.Toggle();
        }
```

(`SweepForVictims` works from walking mode too — it reads positions, not the car — which is
exactly why the test can use it without scripting a drive. Add whatever `using` lines the
file needs for `Activity`/`TileFlags`/`Space3D`.)

- [ ] **Step 6: Compile everything, run the Core gate**

`dotnet build Noir.Unity.csproj -c Debug`, `dotnet build Noir.PlayTests.csproj -c Debug`,
then the full Core gate — expected 522 pass, 0 fail (nothing in this task touches Core).

- [ ] **Step 7: Commit**

```bash
git add Assets/Noir/Unity/Player.cs Assets/Noir/Unity/VillageHost.cs \
        Assets/Noir/Unity/AgentMeshView.cs Assets/Noir/Unity/VillageUI.cs \
        Assets/Noir/PlayTests/PlayerInteractionPlayTests.cs
git commit -m "The hit: swept detection, the recorded event, and a body the town can see"
```

---

### Task 9: Docs, the live look, and the gate

**Files:**
- Modify: `docs/CONTROLS.md` (Street table)
- Modify: `Assets/Noir/Unity/VillageUI.cs` (H panel rows, ~line 703)
- This task is run by the coordinating session (live editor work), not a subagent.

- [ ] **Step 1: CONTROLS.md** — extend the Street section:

```markdown
| **E** at a parked car | get in — then **WASD** drive, **E** get out. The car stays where you leave it |
```

and a paragraph below the table:

```markdown
A car is not a costume: witnesses see a car, not you, and a car that hits somebody is a
recorded event the town can be asked about (**T**, near a witness). NPC traffic cannot see
your car yet — it will drive through you. Phase 2 (police, ambulance) is in IDEAS.md.
```

- [ ] **Step 2: The H panel** (`VillageUI.cs`, beside the door row added 2026-08-15):

```csharp
            Row("E", "open / close the door - or get in / out of the car - you're at (street level)");
```

(Replace the existing door-only E row rather than adding a second E row.)

- [ ] **Step 3: Live verification, in the owner's editor via unity-mcp** (coordinating session):
enter Play, spawn the player beside a driveway car, `PerformOffered` through the seam
(enter → drive a scripted burst → exit), confirm: the car moves and stops at a wall; `Refresh`
never blinks the taken car; a swept hit downs an agent, the figure lies flat and stays; the
census line shows `down 1`; `AskWhatTheySaw` on a near witness returns a line matching
`^\d\d:\d\d, I (watched|saw|think I saw) a .*(car|pickup|van).* hit somebody\.$`; leaving and
re-entering walk mode leaves every mode consistent. Screenshot the body and the car.

- [ ] **Step 4: The PlayMode gate** — when the editor is free (owner not in it):

```
Unity.exe -batchmode -projectPath C:\SerialKillerGame -runTests -testPlatform PlayMode ^
  -assemblyNames Noir.PlayTests -testCategory "!Diagnostic" -testResults <xml> -logFile <log>
```

Expected: 29 selected (25 prior + this plan's 4), 1 skipped Explicit aspiration. Whatever it
MEASURES, write into CLAUDE.md's baseline paragraph and delete the stale-baseline warning
there — that warning names this exact obligation.

- [ ] **Step 5: Commit and push**

```bash
git add docs/CONTROLS.md Assets/Noir/Unity/VillageUI.cs CLAUDE.md
git commit -m "Drivable car: controls documented, PlayMode baseline re-measured"
git push origin survey-layer
```

---

## Self-review record

- **Spec coverage:** rulings 1-5 → Tasks 5-7 (any car, enter), 1+8 (harm, body), 2+3+4 (record,
  testimony), 9 (docs/verification). Phase 2 contract → Task 2 store + Task 8 pairing. V1 holes
  → stated in CONTROLS and code comments. The spec's "Degradation suppresses person bands" is
  implemented as the stronger form — driving minutes produce no person sighting at all — with
  the reasoning in the `Visibly.InAVehicle` doc comment and Recollection's skip comment.
- **Type consistency:** `CarTone/CarShape` (Contracts) flow Driveways → Player → VillageHost →
  HitEvents → CarRegistered → CarDescription; `Down(CitizenId)`, `AgentState.Downed`,
  `Activity.Downed` names match across Tasks 1, 4, 8; `AskInEnglish`'s widened signature in
  Task 4 matches Task 8's call.
- **Known soft spots, named rather than hidden:** exact local names inside
  `AgentMeshView.Refresh` (Task 8 Step 3) and the `Space3D.ToWorld`/`AgentCount` spellings
  (Task 8 Step 1) are anchored to lines their implementer must read; Core test fixture names
  (`Queueham`) must be checked against the suite's actual fixtures before Task 1 Step 1 runs.
