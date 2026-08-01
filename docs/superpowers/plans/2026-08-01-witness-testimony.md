# Witness Testimony Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reconstruct what any townsperson could have seen of the player on any past day, as
`Sighting`s, and measure whether that evidence is interesting enough to interrogate.

**Architecture:** A new Unity assembly `Noir.Core.Witness` knows everything — day plans, the
world, the player's track — and narrows all of it through a return type (`Sighting[]`) that
structurally cannot hold an identity. `Noir.Core.Observation` is not touched and stays blind.
Citizens' days replay deterministically from `DayPlanner.Plan`; only the player's movement is
stored.

**Tech Stack:** C# 9 / netstandard2.1 (Unity 6 profile). NUnit 4 under `dotnet test`. No new
dependencies.

## Global Constraints

- **Core is netstandard2.1, C# 9.** `tools/Noir.Core/Noir.Core.csproj` compiles every file under
  `Assets/Noir/Core/**` at `LangVersion 9.0`. No records with positional syntax, no file-scoped
  namespaces, no `init` accessors beyond what C# 9 allows, no .NET 9 BCL APIs.
- **No `UnityEngine` in Core.** The gate project has no Unity reference; a `using UnityEngine;`
  fails the build. New asmdefs set `"noEngineReferences": true`.
- **`Noir.Core.Observation` is not modified by this plan.** Not one file, not the asmdef.
  `ObservationFirewallTests` must stay green untouched.
- **Determinism is a hard requirement, not a quality.** Every random choice derives from
  `Rolls`/`Keys` seeded on `Citizen.Key` and the minute. No `System.Random`, no `DateTime`, no
  iteration over a `Dictionary` where order affects output.
- **Spec:** `docs/superpowers/specs/2026-08-01-witness-testimony-design.md`.

### Scope limit, decided and deliberate

**Only stationary citizens witness anything in this plan.** A citizen's position comes from
`DayPlan.At(minute).Where` → `world.GetPlace(id).Door`. During `Activity.TravellingTo` their
position is genuinely unknown — there is no stored route — and interpolating between two doors
would be inventing a path and then treating it as evidence.

So v1 is the man at his gate and the woman behind the counter. Walking witnesses need routing
and are a later plan. Say so in the code, or somebody will read the gap as a bug.

## File Structure

| File | Responsibility |
|---|---|
| `Assets/Noir/Core/Witness/Noir.Core.Witness.asmdef` | The new assembly and its reference list |
| `Assets/Noir/Core/Witness/PlayerTrack.cs` | The one stored history: where the player's body was, per minute |
| `Assets/Noir/Core/Witness/Sightlines.cs` | Could this citizen have seen that tile at that minute — geometry and light only |
| `Assets/Noir/Core/Witness/Degradation.cs` | A candidate look → a `PersonDescription`, seeded and coarse |
| `Assets/Noir/Core/Witness/Recollection.cs` | The one public entry point: `WhatTheySaw` |
| `tools/Noir.Core.Tests/WitnessFirewallTests.cs` | Pins the new assembly's reference list and its callers |
| `tools/Noir.Core.Tests/WitnessTests.cs` | Determinism, no-leakage, and the behaviour of each piece |
| `tools/Noir.Sim/TestimonyReport.cs` | The statement census — the thing that answers "is this interesting" |
| `tools/Noir.Sim/Program.cs` | One new `testimony` command |

Split by responsibility rather than by layer: geometry, degradation and orchestration each fail
for different reasons and are each testable alone.

---

### Task 1: The assembly, and the rule that guards it

**Files:**
- Create: `Assets/Noir/Core/Witness/Noir.Core.Witness.asmdef`
- Create: `Assets/Noir/Core/Witness/Recollection.cs` (header comment + empty class)
- Test: `tools/Noir.Core.Tests/WitnessFirewallTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: the assembly `Noir.Core.Witness`, namespace `Noir.Core.Witness`.

- [ ] **Step 1: Write the failing test**

Create `tools/Noir.Core.Tests/WitnessFirewallTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Noir.Core.Tests
{
    /// <summary>
    /// Noir.Core.Witness is the PRODUCER side of the firewall, and it is dangerous in the exact
    /// opposite way to Noir.Core.Observation. Observation is safe because it can see almost
    /// nothing. Witness can see everything — day plans, the world, the player's track — and is
    /// safe only because the one thing it hands out is a Sighting, which cannot hold an identity.
    ///
    /// That makes its CALLERS the thing to police. The moment one scope holds a Sighting[] and a
    /// DayPlan at once, the narrowing is decorative: whoever wrote it can simply look up the
    /// answer. So the second test below is the important one, and it is a grep, because the
    /// property it defends is about who references the assembly rather than about any type in it.
    /// </summary>
    [TestFixture]
    public class WitnessFirewallTests
    {
        [Test]
        public void WitnessAsmdefReferencesExactlyTheProducerSet()
        {
            string path = Path.Combine(RepoRoot(), "Assets", "Noir", "Core", "Witness",
                                       "Noir.Core.Witness.asmdef");
            Assert.That(File.Exists(path), Is.True, "Missing asmdef at " + path);

            string json = File.ReadAllText(path);

            var refs = new List<string>();
            Match block = Regex.Match(json, "\"references\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            Assert.That(block.Success, Is.True, "Could not find a \"references\" array in " + path);
            foreach (Match m in Regex.Matches(block.Groups[1].Value, "\"([^\"]*)\""))
                refs.Add(m.Groups[1].Value);

            Assert.That(refs, Is.EqualTo(new[]
            {
                "Noir.Core.Contracts",
                "Noir.Core.World",
                "Noir.Core.People",
                "Noir.Core.Observation",
            }), "Noir.Core.Witness.asmdef now references [" + string.Join(", ", refs) + "].\n\n" +
                "This assembly is allowed to see ground truth — that is its job. What it is NOT\n" +
                "allowed to do is grow a second way out. Adding Noir.Core.Sim here would let a\n" +
                "reconstruction read live agent state instead of replaying a plan, which is the\n" +
                "same cheat as reading the day plan from Observation, wearing a different hat.");

            Assert.That(Regex.IsMatch(json, "\"noEngineReferences\"\\s*:\\s*true"), Is.True,
                "Noir.Core.Witness.asmdef must keep \"noEngineReferences\": true. Core runs headless\n" +
                "under dotnet test; a UnityEngine reference ends that.");
        }

        [Test]
        public void NothingInTheGameReferencesWitnessYet()
        {
            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles(Path.Combine(RepoRoot(), "Assets", "Noir"),
                                                       "*.cs", SearchOption.AllDirectories))
            {
                if (file.Replace('\\', '/').Contains("/Core/Witness/")) continue;
                string text = File.ReadAllText(file);
                if (text.Contains("Noir.Core.Witness")) offenders.Add(file);
            }

            Assert.That(offenders, Is.Empty,
                "These files reference Noir.Core.Witness:\n  " + string.Join("\n  ", offenders) + "\n\n" +
                "Nothing may consume this assembly except the caller that asks it a question, and\n" +
                "that caller does not exist yet. When it does, this test changes to name it — one\n" +
                "file, deliberately, with a reason in the commit. It must never become a list.");
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "Noir", "Core", "Observation")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not find Assets/Noir/Core/Observation above " + AppContext.BaseDirectory);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tools/Noir.Core.Tests --filter WitnessFirewallTests`
Expected: FAIL — `Missing asmdef at .../Noir.Core.Witness.asmdef`

- [ ] **Step 3: Create the assembly**

Create `Assets/Noir/Core/Witness/Noir.Core.Witness.asmdef`:

```json
{
    "name": "Noir.Core.Witness",
    "rootNamespace": "Noir.Core.Witness",
    "references": [
        "Noir.Core.Contracts",
        "Noir.Core.World",
        "Noir.Core.People",
        "Noir.Core.Observation"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}
```

Create `Assets/Noir/Core/Witness/Recollection.cs`:

```csharp
using Noir.Core.Observation;

// ---------------------------------------------------------------------------------------------
//  THE PRODUCER SIDE OF THE FIREWALL.
//
//  Noir.Core.Observation is safe because it can see almost nothing: its asmdef names
//  Noir.Core.Contracts and stops, so a Sighting cannot hold a Citizen. This assembly is the
//  opposite. It sees the world, the population, every day plan and the player's whole track,
//  and it is safe for one reason only:
//
//      THE ONLY THING IT HANDS OUT IS A Sighting[], AND A Sighting CANNOT NAME ANYBODY.
//
//  Everything this assembly knows is narrowed through that return type. The compiler does the
//  narrowing, which is why the boundary is worth anything.
//
//  THE RULE, and it is about callers rather than about code in here:
//
//      NOTHING MAY REFERENCE Noir.Core.Witness EXCEPT THE ONE CALLER THAT ASKS IT A QUESTION.
//
//  The instant a single scope holds a Sighting[] and a DayPlan together, the narrowing is
//  decorative — whoever wrote it can just look the answer up, and will. WitnessFirewallTests
//  pins the reference list and greps for callers.
//
//  Do not add a method here that returns anything richer than a Sighting. Not "which citizen
//  was nearest", not a debug overload that returns the candidate before degradation, not a
//  bool "did anyone see him". Each is one line and each is the whole game.
// ---------------------------------------------------------------------------------------------

namespace Noir.Core.Witness
{
    public static class Recollection
    {
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tools/Noir.Core.Tests --filter WitnessFirewallTests`
Expected: PASS, 2 tests.

Then confirm nothing else broke: `dotnet build tools/Noir.Core/Noir.Core.csproj`
Expected: build succeeded (this proves the new file is netstandard2.1/C# 9 clean).

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Core/Witness tools/Noir.Core.Tests/WitnessFirewallTests.cs
git commit -m "The producer side of the firewall, with the rule that guards it"
```

---

### Task 2: `PlayerTrack` — the only thing stored

**Files:**
- Create: `Assets/Noir/Core/Witness/PlayerTrack.cs`
- Test: `tools/Noir.Core.Tests/WitnessTests.cs`

**Interfaces:**
- Consumes: `Noir.Core.Contracts.Tile`.
- Produces:
  - `enum Noir.Core.Witness.Visibly : byte { Nothing=0, Carrying=1, Quickly=2, InCompany=4 }` (`[Flags]`)
  - `readonly struct Noir.Core.Witness.Step { Tile Where; Visibly Looked; }`
  - `sealed class PlayerTrack` with `void Record(int minute, Tile where, Visibly looked)`,
    `bool TryGet(int minute, out Step step)`, `int FirstMinute`, `int LastMinute`, `int Count`.

- [ ] **Step 1: Write the failing test**

Create `tools/Noir.Core.Tests/WitnessTests.cs`:

```csharp
using System;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Witness;

namespace Noir.Core.Tests
{
    [TestFixture]
    public class WitnessTests
    {
        [Test]
        public void ATrackRemembersWhereABodyWasEachMinute()
        {
            var track = new PlayerTrack();
            track.Record(100, new Tile(10, 20), Visibly.Carrying);
            track.Record(101, new Tile(11, 20), Visibly.Carrying | Visibly.Quickly);

            Assert.That(track.Count, Is.EqualTo(2));
            Assert.That(track.FirstMinute, Is.EqualTo(100));
            Assert.That(track.LastMinute, Is.EqualTo(101));

            Assert.That(track.TryGet(100, out Step first), Is.True);
            Assert.That(first.Where, Is.EqualTo(new Tile(10, 20)));
            Assert.That(first.Looked, Is.EqualTo(Visibly.Carrying));

            Assert.That(track.TryGet(999, out _), Is.False);
        }

        [Test]
        public void ATrackRunsForwardsOnly()
        {
            var track = new PlayerTrack();
            track.Record(100, new Tile(10, 20), Visibly.Nothing);

            var ex = Assert.Throws<ArgumentException>(
                () => track.Record(99, new Tile(10, 20), Visibly.Nothing));
            Assert.That(ex.Message, Does.Contain("forwards"));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tools/Noir.Core.Tests --filter WitnessTests`
Expected: FAIL — `PlayerTrack` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Assets/Noir/Core/Witness/PlayerTrack.cs`:

```csharp
using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.Witness
{
    /// <summary>
    /// What was visibly true of a body, at a hundred feet, in 1979 street lighting.
    ///
    /// Every flag here must be perceptible from outside — the same test ObservedManner applies.
    /// A flag that could only be known by reading the simulation does not belong, however
    /// convenient it would be later.
    /// </summary>
    [Flags]
    public enum Visibly : byte
    {
        Nothing = 0,
        Carrying = 1,
        Quickly = 2,
        InCompany = 4,
    }

    /// <summary>One minute of a body's history: where it was, and how it looked.</summary>
    public readonly struct Step
    {
        public readonly Tile Where;
        public readonly Visibly Looked;

        public Step(Tile where, Visibly looked)
        {
            Where = where;
            Looked = looked;
        }
    }

    /// <summary>
    /// The player's movement, one entry a minute. THE ONLY THING THIS LAYER STORES.
    ///
    /// Everyone else replays: DayPlanner.Plan is a pure function of (seed, citizen, day), so any
    /// villager's past is recomputable and nothing about them needs keeping. The player has no
    /// plan and no key, so their track is the simulation's one piece of genuine history.
    ///
    /// READ THE LIST OF WHAT IS NOT HERE BEFORE ADDING A FIELD. There is no PlaceId, no activity,
    /// no intent, and no record of anything done — only where a body was and what it looked like
    /// from across a road. A PlaceId here would put the answer to the whole investigation one
    /// dereference away from the witness, and it is exactly what somebody will reach for the
    /// first time a report wants to say WHERE HE WENT.
    ///
    /// A fortnight is about twenty thousand entries at a handful of bytes. Cost is not the reason
    /// for any decision in this file.
    /// </summary>
    public sealed class PlayerTrack
    {
        private readonly Dictionary<int, Step> _steps = new Dictionary<int, Step>();
        private int _first = -1;
        private int _last = -1;

        /// <summary>Minutes since the simulation began — the same stamp Sighting.Minute uses.</summary>
        public int FirstMinute => _first;
        public int LastMinute => _last;
        public int Count => _steps.Count;

        /// <summary>
        /// Write down one minute. Time only ever moves forward, for the same reason it does in
        /// ObservationLog: a history assembled out of order is not a slightly wrong history, it
        /// is a different one wearing the same name.
        /// </summary>
        public void Record(int minute, Tile where, Visibly looked)
        {
            if (minute < _last)
                throw new ArgumentException(
                    "A track runs forwards. Tried to record minute " + minute +
                    " after minute " + _last + ".", nameof(minute));

            if (_first < 0) _first = minute;
            _last = minute;
            _steps[minute] = new Step(where, looked);
        }

        public bool TryGet(int minute, out Step step) => _steps.TryGetValue(minute, out step);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tools/Noir.Core.Tests --filter WitnessTests`
Expected: PASS, 2 tests.

Run: `dotnet build tools/Noir.Core/Noir.Core.csproj`
Expected: build succeeded.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Core/Witness/PlayerTrack.cs tools/Noir.Core.Tests/WitnessTests.cs
git commit -m "A player's track: where a body was, and nothing about what it meant"
```

---

### Task 3: `Sightlines` — could they have seen it

**Files:**
- Create: `Assets/Noir/Core/Witness/Sightlines.cs`
- Modify: `tools/Noir.Core.Tests/WitnessTests.cs` (add tests)

**Interfaces:**
- Consumes: `Tile`, `Citizen`, `Beat`, `SightingClarity` (from `Noir.Core.Observation`).
- Produces:
  - `static SightingClarity Sightlines.HowGoodALook(Tile watcher, Tile subject, int minuteOfDay, Citizen who)`
  - `static bool Sightlines.SawAnythingAtAll(SightingClarity clarity, Tile watcher, Tile subject)` — false when out of range entirely.
  - `const int Sightlines.NeverBeyond = 60;`

Clarity bands, decided here so later tasks can rely on the numbers:

| Chebyshev distance | daylight (07:00–19:00) | dusk (05:00–07:00, 19:00–21:00) | night |
|---|---|---|---|
| ≤ 12 | `Clear` | `Clear` | `Partial` |
| ≤ 30 | `Partial` | `Partial` | `Glimpsed` |
| ≤ 60 | `Glimpsed` | `Glimpsed` | *nothing* |
| > 60 | *nothing* | *nothing* | *nothing* |

An attentive citizen (`Beat.Lingers`, or `Sociability >= 192`) is bumped one band up; a
`Sociability < 64` citizen is bumped one band down, to a floor of nothing.

- [ ] **Step 1: Write the failing tests**

Add to `tools/Noir.Core.Tests/WitnessTests.cs` (inside the fixture, and add
`using Noir.Core.Observation;` and `using Noir.Core.People;` at the top):

```csharp
        private static Citizen Villager(byte sociability, Beat beats = Beat.None) =>
            new Citizen(new CitizenId(7), "Ada", "Reed", 44, LifeStage.Adult, Occupation.Shopkeeper,
                        new HouseholdId(3), new PlaceId(1), new PlaceId(2), 0,
                        0, 128, sociability, new int[0], beats, male: false);

        [Test]
        public void CloseAndInDaylightIsAClearLook()
        {
            var look = Sightlines.HowGoodALook(new Tile(0, 0), new Tile(5, 0), 12 * 60,
                                               Villager(128));
            Assert.That(look, Is.EqualTo(SightingClarity.Clear));
        }

        [Test]
        public void TheSameLookAtNightIsWorse()
        {
            var look = Sightlines.HowGoodALook(new Tile(0, 0), new Tile(5, 0), 2 * 60,
                                               Villager(128));
            Assert.That(look, Is.EqualTo(SightingClarity.Partial));
        }

        [Test]
        public void BeyondSixtyTilesNobodySeesAnything()
        {
            var watcher = new Tile(0, 0);
            var subject = new Tile(61, 0);
            var look = Sightlines.HowGoodALook(watcher, subject, 12 * 60, Villager(128));
            Assert.That(Sightlines.SawAnythingAtAll(look, watcher, subject), Is.False);
        }

        [Test]
        public void TheManWhoLingersSeesMoreThanTheManWhoDoesNot()
        {
            var watcher = new Tile(0, 0);
            var subject = new Tile(20, 0);   // the Partial band in daylight

            var ordinary = Sightlines.HowGoodALook(watcher, subject, 12 * 60, Villager(128));
            var lingerer = Sightlines.HowGoodALook(watcher, subject, 12 * 60,
                                                   Villager(128, Beat.Lingers));

            Assert.That(ordinary, Is.EqualTo(SightingClarity.Partial));
            Assert.That(lingerer, Is.EqualTo(SightingClarity.Clear));
        }

        [Test]
        public void SomebodyWhoKeepsHisHeadDownSeesLess()
        {
            var watcher = new Tile(0, 0);
            var subject = new Tile(20, 0);

            var withdrawn = Sightlines.HowGoodALook(watcher, subject, 12 * 60, Villager(32));
            Assert.That(withdrawn, Is.EqualTo(SightingClarity.Glimpsed));
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tools/Noir.Core.Tests --filter WitnessTests`
Expected: FAIL — `Sightlines` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Assets/Noir/Core/Witness/Sightlines.cs`:

```csharp
using Noir.Core.Contracts;
using Noir.Core.Observation;
using Noir.Core.People;

namespace Noir.Core.Witness
{
    /// <summary>
    /// Whether one person could have got a look at another: how far, how lit, how attentive.
    ///
    /// Distance is Chebyshev and there is no wall test, which is a deliberate simplification and
    /// not an oversight. A line-of-sight raycast through the tile grid would be more accurate and
    /// would make every number below untestable in isolation, because a test would have to build
    /// a world first. When somebody adds occlusion it belongs behind this same signature.
    ///
    /// Attention comes from the person's own particulars rather than from a separate stat. A
    /// villager who lingers is one who drew a sentence about lingering, so what he notices and
    /// why he notices it can never disagree.
    /// </summary>
    public static class Sightlines
    {
        /// <summary>Past this, in the best light there is, nobody has seen a thing.</summary>
        public const int NeverBeyond = 60;

        private const int Near = 12;
        private const int Middling = 30;

        public static SightingClarity HowGoodALook(Tile watcher, Tile subject, int minuteOfDay,
                                                   Citizen who)
        {
            int distance = Tile.ChebyshevDistance(watcher, subject);
            if (distance > NeverBeyond) return SightingClarity.Glimpsed;   // caller gates on range

            int light = LightAt(minuteOfDay);   // 2 day, 1 dusk, 0 night

            // The table in the plan, as arithmetic. Band 2 is Clear, 1 Partial, 0 Glimpsed,
            // below 0 is nothing at all — which the caller detects via SawAnythingAtAll.
            int band = distance <= Near ? 2 : distance <= Middling ? 1 : 0;
            if (light == 0) band -= 1;

            band += Attention(who);

            if (band > 2) band = 2;
            if (band < 0) band = 0;
            return (SightingClarity)band;
        }

        /// <summary>
        /// False when there was nothing to remember. Separate from the clarity itself because
        /// Glimpsed is a real, useful, common statement and "out of range" is not a statement.
        /// </summary>
        public static bool SawAnythingAtAll(SightingClarity clarity, Tile watcher, Tile subject) =>
            Tile.ChebyshevDistance(watcher, subject) <= NeverBeyond;

        /// <summary>2 in daylight, 1 at dusk, 0 at night. 1979 street lighting is not lighting.</summary>
        private static int LightAt(int minuteOfDay)
        {
            int hour = minuteOfDay / 60;
            if (hour >= 7 && hour < 19) return 2;
            if ((hour >= 5 && hour < 7) || (hour >= 19 && hour < 21)) return 1;
            return 0;
        }

        /// <summary>+1 for somebody who watches the street, -1 for somebody who does not.</summary>
        private static int Attention(Citizen who)
        {
            if ((who.Beats & Beat.Lingers) != 0 || who.Sociability >= 192) return 1;
            if (who.Sociability < 64) return -1;
            return 0;
        }
    }
}
```

Note the interaction the test `SomebodyWhoKeepsHisHeadDownSeesLess` pins: at 20 tiles in
daylight the band is 1, minus 1 for withdrawal is 0 — `Glimpsed`, not nothing. Range alone
decides whether there was a statement at all.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tools/Noir.Core.Tests --filter WitnessTests`
Expected: PASS, 7 tests (2 from Task 2, 5 new).

Run: `dotnet build tools/Noir.Core/Noir.Core.csproj`
Expected: build succeeded.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Core/Witness/Sightlines.cs tools/Noir.Core.Tests/WitnessTests.cs
git commit -m "How good a look somebody got: distance, light, and whether they were looking"
```

---

### Task 4: `Degradation` — a look becomes a description

**Files:**
- Create: `Assets/Noir/Core/Witness/Degradation.cs`
- Modify: `tools/Noir.Core.Tests/WitnessTests.cs` (add tests)

**Interfaces:**
- Consumes: `SightingClarity`, `PersonDescription`, `Visibly`, `CitizenKey`.
- Produces:
  - `static PersonDescription Degradation.WhatRegistered(SightingClarity clarity, Visibly looked, bool subjectIsMale, int subjectAge, int heightCm, int buildIndex, CitizenKey witness, int minute, ulong seed)`

How many bands survive, by clarity: `Clear` 5, `Partial` 3, `Glimpsed` 1. Which bands survive is
a seeded shuffle over the six, so two witnesses at the same clarity remember different things.
`ApparentSex` is wrong 1 time in 5 at `Glimpsed`, 1 in 12 at `Partial`, never at `Clear`.

- [ ] **Step 1: Write the failing tests**

Add to `tools/Noir.Core.Tests/WitnessTests.cs`:

```csharp
        private const ulong TestSeed = 1979UL;

        private static PersonDescription Describe(SightingClarity clarity, CitizenKey witness,
                                                  int minute = 500) =>
            Degradation.WhatRegistered(clarity, Visibly.Carrying, true, 40, 185, 2,
                                       witness, minute, TestSeed);

        [Test]
        public void AGlimpseRegistersAlmostNothing()
        {
            var seen = Describe(SightingClarity.Glimpsed, new CitizenKey(11));
            Assert.That(seen.NoticedCount, Is.EqualTo(1));
        }

        [Test]
        public void AClearLookRegistersMost()
        {
            var seen = Describe(SightingClarity.Clear, new CitizenKey(11));
            Assert.That(seen.NoticedCount, Is.EqualTo(5));
        }

        [Test]
        public void TheSameWitnessRemembersTheSameThingTwice()
        {
            var once = Describe(SightingClarity.Partial, new CitizenKey(11));
            var twice = Describe(SightingClarity.Partial, new CitizenKey(11));
            Assert.That(once, Is.EqualTo(twice));
        }

        [Test]
        public void TwoWitnessesToTheSameMomentRememberDifferently()
        {
            var one = Describe(SightingClarity.Partial, new CitizenKey(11));
            var other = Describe(SightingClarity.Partial, new CitizenKey(9999));
            Assert.That(one, Is.Not.EqualTo(other),
                "Two witnesses at the same clarity registering identical bands means the shuffle " +
                "is not keyed on the witness, and every statement in the village will be a copy.");
        }

        [Test]
        public void AClearLookNeverGetsTheSexWrong()
        {
            for (int minute = 0; minute < 400; minute++)
            {
                var seen = Degradation.WhatRegistered(SightingClarity.Clear, Visibly.Nothing,
                                                      true, 40, 185, 2,
                                                      new CitizenKey(11), minute, TestSeed);
                if (seen.Sex != ApparentSex.Unnoticed)
                    Assert.That(seen.Sex, Is.EqualTo(ApparentSex.Man),
                                "wrong at minute " + minute + ", in a clear look");
            }
        }

        [Test]
        public void AGlimpseSometimesGetsTheSexWrong()
        {
            int wrong = 0;
            for (int minute = 0; minute < 2000; minute++)
            {
                var seen = Degradation.WhatRegistered(SightingClarity.Glimpsed, Visibly.Nothing,
                                                      true, 40, 185, 2,
                                                      new CitizenKey(11), minute, TestSeed);
                if (seen.Sex == ApparentSex.Woman) wrong++;
            }
            Assert.That(wrong, Is.GreaterThan(0),
                "A witness who never mistakes a man for a woman in the dark is not a witness.");
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tools/Noir.Core.Tests --filter WitnessTests`
Expected: FAIL — `Degradation` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Assets/Noir/Core/Witness/Degradation.cs`:

```csharp
using Noir.Core.Contracts;
using Noir.Core.Observation;

namespace Noir.Core.Witness
{
    /// <summary>
    /// What actually stuck. The narrowing this whole assembly exists to perform.
    ///
    /// Everything above this call knows a height in centimetres and a person's real sex. What
    /// comes out is bands, most of them Unnoticed, and there is no way back. The bands are wide
    /// on purpose — see the header of PersonDescription — because a description that narrows to
    /// one villager is an identity with extra steps.
    ///
    /// EVERY ROLL IS SEEDED ON THE WITNESS AND THE MINUTE, never on a running RNG. Ask the same
    /// question twice and you get the same answer, which is what separates a fallible witness
    /// from a slot machine the player pulls until the answer suits them. It also means testimony
    /// needs no storage at all: the memory IS the seed.
    /// </summary>
    public static class Degradation
    {
        private static readonly ulong Purpose = Rolls.Purpose("witness.degradation");

        /// <summary>How many of the six bands survive, by how good the look was.</summary>
        private static int BandsThatSurvive(SightingClarity clarity) =>
            clarity == SightingClarity.Clear ? 5 : clarity == SightingClarity.Partial ? 3 : 1;

        public static PersonDescription WhatRegistered(
            SightingClarity clarity, Visibly looked,
            bool subjectIsMale, int subjectAge, int heightCm, int buildIndex,
            CitizenKey witness, int minute, ulong seed)
        {
            int surviving = BandsThatSurvive(clarity);

            // Which bands stuck. A seeded shuffle of the six, then take the first N — so two
            // people who got an equally good look still remember different halves of it, which
            // is the whole reason multiple witnesses are worth interviewing.
            var order = new int[] { 0, 1, 2, 3, 4, 5 };
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = Rolls.Int(seed, Purpose, witness.Value, minute, (ulong)i, 0, i + 1);
                int swap = order[i]; order[i] = order[j]; order[j] = swap;
            }

            var sex = ApparentSex.Unnoticed;
            var height = HeightBand.Unnoticed;
            var build = BuildBand.Unnoticed;
            var age = AgeBand.Unnoticed;
            var clothing = ClothingTone.Unnoticed;
            var carrying = CarriedThing.Unnoticed;

            for (int i = 0; i < surviving; i++)
            {
                switch (order[i])
                {
                    case 0: sex = ApparentSexOf(subjectIsMale, clarity, witness, minute, seed); break;
                    case 1: height = HeightBandOf(heightCm); break;
                    case 2: build = BuildBandOf(buildIndex); break;
                    case 3: age = AgeBandOf(subjectAge); break;

                    // Tone rather than colour, and rolled rather than read: nothing upstream
                    // models what the player is wearing yet. When it does, pass it in — the
                    // signature is where that belongs, not a lookup from in here.
                    case 4:
                        clothing = (ClothingTone)(1 + Rolls.Int(seed, Purpose, witness.Value,
                                                                minute, 0xC10DUL, 0, 3));
                        break;

                    case 5: carrying = CarriedOf(looked); break;
                }
            }

            return new PersonDescription(sex, height, build, age, clothing, carrying);
        }

        /// <summary>
        /// Wrong 1 in 5 at a glimpse, 1 in 12 at partial, never in a clear look. A coat and a bad
        /// street lamp is all it takes, and the enum's own comment promises this happens.
        /// </summary>
        private static ApparentSex ApparentSexOf(bool male, SightingClarity clarity,
                                                 CitizenKey witness, int minute, ulong seed)
        {
            int oddsAgainst = clarity == SightingClarity.Clear ? 0
                            : clarity == SightingClarity.Partial ? 12 : 5;

            bool mistaken = oddsAgainst > 0 &&
                Rolls.Int(seed, Purpose, witness.Value, minute, 0x5E11UL, 0, oddsAgainst) == 0;

            bool reported = mistaken ? !male : male;
            return reported ? ApparentSex.Man : ApparentSex.Woman;
        }

        private static HeightBand HeightBandOf(int cm) =>
            cm < 165 ? HeightBand.Short : cm > 180 ? HeightBand.Tall : HeightBand.Average;

        private static BuildBand BuildBandOf(int index) =>
            index <= 0 ? BuildBand.Slight : index >= 2 ? BuildBand.Heavy : BuildBand.Average;

        private static AgeBand AgeBandOf(int years) =>
            years < 16 ? AgeBand.Child : years < 30 ? AgeBand.Young
                                       : years < 60 ? AgeBand.MiddleAged : AgeBand.Old;

        /// <summary>
        /// NothingSeen is not the same as not having looked, so this is only ever reached when
        /// the carrying band survived — an absence somebody checked.
        /// </summary>
        private static CarriedThing CarriedOf(Visibly looked) =>
            (looked & Visibly.Carrying) != 0 ? CarriedThing.Bag : CarriedThing.NothingSeen;
    }
}
```

**Note on `Rolls.Int`:** verified against `Assets/Noir/Core/Contracts/Rng.cs:208` — the overload
used above is
`Rolls.Int(ulong seed, ulong purpose, ulong subject, long tick, ulong salt, int inclusiveMin, int exclusiveMax)`.
There is also a five-argument form ending in `exclusiveMax` alone (`Rng.cs:187`); either is fine
as long as the seeding inputs stay `seed, purpose, witness.Value, minute, salt`. Those five are
the determinism guarantee — change what they are and the memory changes with them.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tools/Noir.Core.Tests --filter WitnessTests`
Expected: PASS, 13 tests.

If `TwoWitnessesToTheSameMomentRememberDifferently` fails, the shuffle is not varying with the
witness — check that `witness.Value` is reaching `Rolls.Int` as the subject rather than a
constant. Do not fix it by changing the test.

Run: `dotnet build tools/Noir.Core/Noir.Core.csproj`
Expected: build succeeded.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Core/Witness/Degradation.cs tools/Noir.Core.Tests/WitnessTests.cs
git commit -m "What stuck: six bands, most of them Unnoticed, seeded on the witness"
```

---

### Task 5: `Recollection.WhatTheySaw` — the one public entry point

**Files:**
- Modify: `Assets/Noir/Core/Witness/Recollection.cs`
- Modify: `tools/Noir.Core.Tests/WitnessTests.cs` (add tests)

**Interfaces:**
- Consumes: `PlayerTrack`, `Sightlines`, `Degradation`, `DayPlanner.Plan`, `WorldModel.GetPlace`.
- Produces:
  - `static Sighting[] Recollection.WhatTheySaw(WorldModel world, Population population, Citizen who, int day, PlayerTrack track, ulong seed)`
  - `const int Recollection.MinutesPerDay` (= `Sighting.MinutesPerDay`)

Rules, and they are the spec:
- The citizen's position at a minute is `world.GetPlace(plan.At(minuteOfDay).Where).Door`.
- **`Activity.TravellingTo` produces nothing.** Their position is genuinely unknown.
- A sighting exists only where `SawAnythingAtAll` is true.
- **Consecutive minutes of the same figure collapse into one sighting**, stamped at the first
  minute. A man who walks past a shop over four minutes is one thing remembered, not four.
- The reported minute is blurred: rounded to 15 minutes at `Glimpsed`, 5 at `Partial`, exact at
  `Clear`.
- `ObserverId` is *not* the citizen id. It is `new ObserverId(index within this call)` — the
  register mapping an observer back to a person lives with the caller, as `Sighting.cs` says.

- [ ] **Step 1: Write the failing tests**

Add to `tools/Noir.Core.Tests/WitnessTests.cs` (add `using Noir.Sim;` and
`using Noir.Core.World;`):

```csharp
        /// <summary>The authored village, once, so these tests see the same world as the harness.</summary>
        private static VillageContext Village() => VillageContext.Load();

        /// <summary>A track that parks the player on one citizen's doorstep all afternoon.</summary>
        private static PlayerTrack TrackOutside(VillageContext v, Citizen who, int day,
                                                Visibly looked = Visibly.Carrying)
        {
            var plan = DayPlanner.Plan(v.World, v.People, who, day, v.Seed);
            var track = new PlayerTrack();
            for (int m = 0; m < Sighting.MinutesPerDay; m++)
            {
                Tile door = v.World.GetPlace(plan.At(m).Where).Door;
                track.Record(day * Sighting.MinutesPerDay + m, door, looked);
            }
            return track;
        }

        [Test]
        public void AManStoodOnYourDoorstepAllDayIsRemembered()
        {
            var v = Village();
            Citizen who = v.People.Citizens[0];
            var track = TrackOutside(v, who, 3);

            Sighting[] said = Recollection.WhatTheySaw(v.World, v.People, who, 3, track, v.Seed);

            Assert.That(said.Length, Is.GreaterThan(0),
                "Somebody standing on the doorstep for a whole day and being remembered by " +
                "nobody means no sighting is ever produced, and the census will be empty.");
        }

        [Test]
        public void ARunOfMinutesIsOneThingRemembered()
        {
            var v = Village();
            Citizen who = v.People.Citizens[0];
            var track = TrackOutside(v, who, 3);

            Sighting[] said = Recollection.WhatTheySaw(v.World, v.People, who, 3, track, v.Seed);

            Assert.That(said.Length, Is.LessThan(60),
                "A figure in sight for hours produced one sighting a minute. A witness remembers " +
                "a visit, not a frame count.");
        }

        [Test]
        public void TheSameQuestionTwiceGetsTheSameAnswer()
        {
            var v = Village();
            Citizen who = v.People.Citizens[0];
            var track = TrackOutside(v, who, 3);

            Sighting[] once = Recollection.WhatTheySaw(v.World, v.People, who, 3, track, v.Seed);
            Sighting[] twice = Recollection.WhatTheySaw(v.World, v.People, who, 3, track, v.Seed);

            Assert.That(twice.Length, Is.EqualTo(once.Length));
            for (int i = 0; i < once.Length; i++)
            {
                Assert.That(twice[i].Minute, Is.EqualTo(once[i].Minute), "minute at " + i);
                Assert.That(twice[i].Clarity, Is.EqualTo(once[i].Clarity), "clarity at " + i);
                Assert.That(twice[i].Description, Is.EqualTo(once[i].Description), "description at " + i);
                Assert.That(twice[i].Where, Is.EqualTo(once[i].Where), "place at " + i);
            }
        }

        [Test]
        public void TestimonyCannotTellWhyHeWasThere()
        {
            var v = Village();
            Citizen who = v.People.Citizens[0];

            // The same movements, twice. Whatever the player was "doing" is not an input to any
            // of this, so if these two ever differ, something is reading intent.
            var innocent = TrackOutside(v, who, 3, Visibly.Carrying);
            var guilty = TrackOutside(v, who, 3, Visibly.Carrying);

            Sighting[] a = Recollection.WhatTheySaw(v.World, v.People, who, 3, innocent, v.Seed);
            Sighting[] b = Recollection.WhatTheySaw(v.World, v.People, who, 3, guilty, v.Seed);

            Assert.That(b.Length, Is.EqualTo(a.Length));
            for (int i = 0; i < a.Length; i++)
                Assert.That(b[i].Description, Is.EqualTo(a[i].Description), "at " + i);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tools/Noir.Core.Tests --filter WitnessTests`
Expected: FAIL — `WhatTheySaw` does not exist.

- [ ] **Step 3: Write the implementation**

Replace the body of `Assets/Noir/Core/Witness/Recollection.cs`'s class (keep the whole header
comment from Task 1 exactly as it is):

```csharp
using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.Observation;
using Noir.Core.People;
using Noir.Core.World;

    public static class Recollection
    {
        public const int MinutesPerDay = Sighting.MinutesPerDay;

        /// <summary>
        /// Everything one villager could tell you about the player, for one day.
        ///
        /// Nothing is stored and nothing is stepped: the citizen's day is replayed from
        /// DayPlanner, which is a pure function of (seed, citizen, day), and the player's track
        /// is the only history consulted. Ask about a day from a fortnight ago and it costs the
        /// same as asking about yesterday.
        ///
        /// STATIONARY WITNESSES ONLY, and this is the honest limit of the first version. A
        /// citizen's position comes from the door of the place their plan has them at. While
        /// they are TravellingTo, nobody knows where they are — there is no stored route — and
        /// interpolating between two doors would invent a path and then treat it as evidence.
        /// So the witnesses here are the man at his gate and the woman behind the counter.
        /// Walking witnesses need routing and are a later piece of work; the gap is deliberate.
        /// </summary>
        public static Sighting[] WhatTheySaw(WorldModel world, Population population,
                                             Citizen who, int day, PlayerTrack track, ulong seed)
        {
            DayPlan plan = DayPlanner.Plan(world, population, who, day, seed);
            var found = new List<Sighting>();

            bool inSight = false;

            for (int minuteOfDay = 0; minuteOfDay < MinutesPerDay; minuteOfDay++)
            {
                int minute = day * MinutesPerDay + minuteOfDay;

                if (!track.TryGet(minute, out Step step)) { inSight = false; continue; }

                Block block = plan.At(minuteOfDay);
                if (block.What == Activity.TravellingTo) { inSight = false; continue; }
                if (!block.Where.IsValid) { inSight = false; continue; }

                Tile watcher = world.GetPlace(block.Where).Door;

                SightingClarity clarity = Sightlines.HowGoodALook(watcher, step.Where,
                                                                  minuteOfDay, who);
                if (!Sightlines.SawAnythingAtAll(clarity, watcher, step.Where))
                {
                    inSight = false;
                    continue;
                }

                // One visit, not one frame a minute. A figure who stays in sight is a single
                // thing a witness remembers, stamped at the moment they first noticed him.
                if (inSight) continue;
                inSight = true;

                PersonDescription seen = Degradation.WhatRegistered(
                    clarity, step.Looked,
                    subjectIsMale: true, subjectAge: 35, heightCm: 178, buildIndex: 1,
                    who.Key, minute, seed);

                found.Add(new Sighting(new ObserverId(found.Count),
                                       BlurredMinute(minute, clarity),
                                       watcher, clarity, seen));
            }

            return found.ToArray();
        }

        /// <summary>
        /// "About half seven." Nobody reports a minute, and a consumer given one would trust it.
        /// The worse the look, the coarser the memory of when it was.
        /// </summary>
        private static int BlurredMinute(int minute, SightingClarity clarity)
        {
            int to = clarity == SightingClarity.Clear ? 1
                   : clarity == SightingClarity.Partial ? 5 : 15;
            return minute / to * to;
        }
    }
```

**On the player's own description:** `subjectIsMale: true, subjectAge: 35, heightCm: 178,
buildIndex: 1` are placeholders for a player character that does not exist yet, and they are
constants at ONE call site so there is exactly one line to change. Do not thread a player-model
type through this plan — that is a different piece of work, and inventing its shape here would
be guessing.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tools/Noir.Core.Tests --filter WitnessTests`
Expected: PASS, 17 tests.

Run: `dotnet test tools/Noir.Core.Tests`
Expected: PASS, every fixture — `ObservationFirewallTests` included and unchanged.

Run: `dotnet build tools/Noir.Core/Noir.Core.csproj`
Expected: build succeeded.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Core/Witness/Recollection.cs tools/Noir.Core.Tests/WitnessTests.cs
git commit -m "What one villager could tell you about a day, replayed rather than stored"
```

---

### Task 6: The statement census — does any of this hold up

**Files:**
- Create: `tools/Noir.Sim/TestimonyReport.cs`
- Modify: `tools/Noir.Sim/Program.cs` (add one `case` and one method, alongside `case "ratio"`)

**Interfaces:**
- Consumes: `Recollection.WhatTheySaw`, `VillageContext.Load`.
- Produces: `dotnet run --project tools/Noir.Sim -- testimony [days]` (default 14).

This task has no unit test. Its output *is* the test, and it is the question the whole spec
exists to answer: **is a reconstructed sighting interesting enough to interrogate?**

- [ ] **Step 1: Write the report**

Create `tools/Noir.Sim/TestimonyReport.cs`:

```csharp
using System;
using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.Observation;
using Noir.Core.People;
using Noir.Core.Witness;
using Noir.Core.World;

namespace Noir.Sim
{
    /// <summary>
    /// A fortnight of the player walking the village, and everything anybody could say about it.
    ///
    /// THIS IS THE SUCCESS TEST FOR THE WHOLE WITNESS LAYER, and it is a histogram rather than an
    /// assertion because the property it measures is "is this interesting", which no assert can
    /// hold. If nearly every statement is a bare figure, the tuning in Sightlines and Degradation
    /// is wrong and no amount of dialogue on top will save it. What we want is a scatter of
    /// witnesses each holding one narrow fragment, where the fragments only mean something
    /// assembled.
    /// </summary>
    public static class TestimonyReport
    {
        public static int Run(int days)
        {
            VillageContext v = VillageContext.Load();

            // The player walks the village: a lap of the road network, one tile a minute, from
            // early morning to late at night. Crude on purpose — this measures what the town
            // notices, so the movement only has to be movement.
            var track = new PlayerTrack();
            var roadTiles = new List<Tile>();
            foreach (Place p in v.World.AllPlaces) roadTiles.Add(p.Door);

            for (int day = 0; day < days; day++)
            for (int m = 0; m < Sighting.MinutesPerDay; m++)
            {
                int minute = day * Sighting.MinutesPerDay + m;
                Tile at = roadTiles[(minute / 3) % roadTiles.Count];
                track.Record(minute, at, (minute % 7 == 0) ? Visibly.Carrying : Visibly.Nothing);
            }

            int statements = 0, blank = 0;
            var byClarity = new int[3];
            var byBandCount = new int[7];
            var witnesses = new HashSet<int>();
            var samples = new List<string>();

            for (int day = 0; day < days; day++)
            foreach (Citizen who in v.People.Citizens)
            {
                Sighting[] said = Recollection.WhatTheySaw(v.World, v.People, who, day, track, v.Seed);
                if (said.Length > 0) witnesses.Add(who.Id.Value);

                foreach (Sighting s in said)
                {
                    statements++;
                    byClarity[(int)s.Clarity]++;
                    byBandCount[s.Description.NoticedCount]++;
                    if (s.Description.IsBlank) blank++;
                    else if (samples.Count < 25 && statements % 17 == 0)
                        samples.Add("  d" + s.Day + " " + s.MinuteOfDay / 60 + ":" +
                                    (s.MinuteOfDay % 60).ToString("00") + "  " +
                                    who.FullName + ": \"" + s.Description + "\"");
                }
            }

            Console.WriteLine("TESTIMONY over " + days + " days, " + v.People.Count + " people");
            Console.WriteLine();
            Console.WriteLine("  statements      " + statements);
            Console.WriteLine("  witnesses       " + witnesses.Count + " of " + v.People.Count);
            Console.WriteLine("  blank ('a figure') " + blank + "  (" +
                              (statements == 0 ? 0 : blank * 100 / statements) + "%)");
            Console.WriteLine();
            Console.WriteLine("  by clarity      glimpsed " + byClarity[0] +
                              "   partial " + byClarity[1] + "   clear " + byClarity[2]);
            Console.Write("  bands noticed   ");
            for (int i = 0; i < byBandCount.Length; i++) Console.Write(i + ":" + byBandCount[i] + "  ");
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("A sample of what the town would say:");
            foreach (string line in samples) Console.WriteLine(line);

            Console.WriteLine();
            Console.WriteLine(blank * 2 > statements
                ? "MOSTLY BLANK. Over half of all testimony is 'a figure'. The evidence is too thin\n" +
                  "to interrogate — retune Sightlines (range/light bands) or Degradation (surviving\n" +
                  "band counts) before building anything on top of it."
                : "Usable. Most statements carry at least one band worth asking about.");

            return 0;
        }
    }
}
```

- [ ] **Step 2: Wire up the command**

In `tools/Noir.Sim/Program.cs`, add alongside `case "ratio": return CmdRatio(args);`:

```csharp
                    case "testimony": return CmdTestimony(args);
```

and add the method next to the other `Cmd*` methods:

```csharp
        private static int CmdTestimony(string[] args)
        {
            int days = args.Length > 1 && int.TryParse(args[1], out int d) ? d : 14;
            return TestimonyReport.Run(days);
        }
```

If `Program.cs` prints a usage/help list of commands, add `testimony [days]` to it in the same
style as the neighbouring entries.

- [ ] **Step 3: Run the census**

Run: `dotnet run --project tools/Noir.Sim -- testimony 14`
Expected: a table, a sample of statements, and a verdict line.

**This step is a decision point, not a checkbox.** Read the output:

- If it prints `MOSTLY BLANK`, retune and run again. The two levers are `Sightlines`'s distance
  bands and `Degradation.BandsThatSurvive`. Do not proceed to a dialogue layer until this reads
  `Usable`.
- If `witnesses` is a tiny fraction of the population, the stationary-witness limit is biting
  harder than expected — worth reporting before tuning around it.
- Read the sampled statements as English. They are what a detective would repeat aloud. If they
  are all *"a man in dark clothing"*, the bands are too coarse even though the counts look fine.

- [ ] **Step 4: Record what it said**

Append the census output and a one-paragraph reading of it to
`docs/superpowers/specs/2026-08-01-witness-testimony-design.md` under a new
`## What the census said` heading. The spec claimed a histogram would answer the question; this
is the answer, and it is the input to every decision after it.

- [ ] **Step 5: Commit**

```bash
git add tools/Noir.Sim/TestimonyReport.cs tools/Noir.Sim/Program.cs docs/superpowers/specs/2026-08-01-witness-testimony-design.md
git commit -m "The statement census: what the whole town could say about a fortnight"
```

---

## Self-Review

**Spec coverage:**

| Spec section | Task |
|---|---|
| §1 `Noir.Core.Witness`, reference list, caller rule | Task 1 |
| §2 `PlayerTrack`, per-minute, no PlaceId or activity | Task 2 |
| §3 clarity from distance and light | Task 3 |
| §3 attention from `Sociability` and `Beats` | Task 3 |
| §3 `ApparentSex` wrong at night | Task 4 |
| §3 seeded on `Citizen.Key` + minute | Task 4 |
| §3 bands gated by clarity | Task 4 |
| §3 minutes blur | Task 5 |
| §3 `WhatTheySaw` signature and replay | Task 5 |
| §4 statement census | Task 6 |
| §4 determinism test | Task 5 (`TheSameQuestionTwiceGetsTheSameAnswer`) |
| §4 no-leakage test | Task 5 (`TestimonyCannotTellWhyHeWasThere`) |

**Known gaps, named rather than hidden:**

- The no-leakage test is weaker than the spec's wording. The spec imagines a guilty day and an
  innocent day; nothing in the codebase models guilt yet, so the test asserts the weaker property
  that identical movement yields identical testimony. Strengthen it when the player has acts.
- Walking witnesses are out of scope (see Global Constraints). Task 6 will show the cost of that
  as a low witness count, which is the right place to find out.
- The player's physical description is four constants at one call site in Task 5. There is no
  player model to read them from.
