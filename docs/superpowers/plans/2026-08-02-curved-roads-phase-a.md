# Curved Roads — Phase A Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `Noir.Core` the ability to describe a road whose centreline curves, without changing a single number for the 27 straight roads the town is built from today.

**Architecture:** A new `RoadPath` generalises `RoadLine.Centre` from a scalar cross-coordinate ("the x of a north-south road") into a position, tangent and normal that vary along the road. `RoadPath` short-circuits to exact linear arithmetic when the declared polyline is two points on one axis, which is every current road — that is the property that protects the existing city. Junctions become real path intersections; `LaneGraph` works in arc length and classifies turns from tangents rather than a four-value enum.

**Tech Stack:** C# targeting **netstandard2.1, LangVersion 9.0** (Core), NUnit 4.2.2 on net9.0 (tests), `dotnet test`.

## Global Constraints

- **Core is netstandard2.1 / C# 9.0.** No file-scoped namespaces, no `global using`, no C# 10+ syntax. `tools/Noir.Core/Noir.Core.csproj` is the gate and compiles the same files Unity does.
- **No `UnityEngine` in Core.** There is no reference available; a stray `using UnityEngine;` will not compile.
- **No transcendentals in Core.** `Sin`, `Cos`, `Tan`, `Atan`, `Atan2`, `Exp`, `Log`, `Pow`. Their results are implementation-defined and have changed between .NET runtimes, which would silently break replay. `Sqrt` **is** permitted — IEEE-754 requires it to be correctly rounded, so it is bit-identical across runtimes.
- **Angles are never materialised.** Left/right is the sign of a cross product; straight-versus-U-turn is a dot product. Never an `Atan2`.
- **Phase A ships no curve in `Content/city.txt`.** Do not add a curved road to the map. Until Phase B migrates the 13 files still gated on `IsStraight`, a curved road would get lanes and junctions while half the renderers ignored it — traffic on asphalt nobody drew.
- **Run tests in Release:** `dotnet test -c Release`. Debug crashes the test host on this machine for hardware reasons — see the CPU note in `docs/STATE.md`. Do not read a Debug crash as a defect.
- **Two tests fail before you start and must still fail after:** `TwoToOneTests.TheMedianVillagerYieldsTwiceAsMuchTextureAsUse` and `TheTenthPercentileIsNotALock`. They fail by design. Any *other* failure is yours.
- **Spec:** `docs/superpowers/specs/2026-08-02-curved-roads-design.md`.

---

## File Structure

| File | Responsibility |
|---|---|
| `Assets/Noir/Core/World/RoadPath.cs` | **new.** The centreline primitive: arc length, `PointAt`, `TangentAt`, `NormalAt`, `Project`. Owns the Catmull-Rom and the resampling. |
| `Assets/Noir/Core/World/RoadNetwork.cs` | **modify.** `RoadLine` gains `Path`; `Junction` becomes a real crossing carrying arc lengths and tangents; `RoadNetwork.At` goes through `Project`. |
| `Assets/Noir/Core/World/LaneGraph.cs` | **modify.** Segments cut at junction arc lengths; turns classified from tangents. |
| `Assets/Noir/Unity/MapFeatures.cs` | **modify, one method.** `Smoothed` delegates to Core so rail and roads share one curve. |
| `tools/Noir.Core.Tests/CoreDeterminismTests.cs` | **new.** Enforces the transcendental ban that `Vec2` documents but nothing checks. |
| `tools/Noir.Core.Tests/RoadPathTests.cs` | **new.** The primitive, straight and curved. |
| `tools/Noir.Core.Tests/RoadGeometryBaselineTests.cs` | **new.** Golden baseline over the real `Content/city.txt`, plus per-road equivalence. |
| `tools/Noir.Core.Tests/RoadNetworkTests.cs` | **modify.** Add oblique and double-crossing cases. |
| `tools/Noir.Core.Tests/LaneGraphTests.cs` | **modify.** Add curve fixtures. |

---

### Task 1: Enforce the transcendental ban

`Vec2.cs` states *"A build-time test greps for them."* **That test does not exist.** Core today calls no math functions at all, so the ban is currently held up by nothing but a comment — and Task 3 is about to introduce Core's first `Sqrt`. Build the guard before the thing it guards.

**Files:**
- Create: `tools/Noir.Core.Tests/CoreDeterminismTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing consumed by later tasks. It is a standing guard that every later task must keep green.

- [ ] **Step 1: Write the test**

Create `tools/Noir.Core.Tests/CoreDeterminismTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The ban Vec2 has always claimed was enforced, actually enforced.
    ///
    /// Transcendentals are implementation-defined: Math.Sin has changed result in the last bit
    /// between .NET runtimes before, and Core is the half of this project whose whole value is
    /// that the same seed replays the same village. A drifting sine would not fail loudly - it
    /// would move one villager one tile, two years from now, on somebody else's machine.
    ///
    /// SQRT IS DELIBERATELY ALLOWED. It is not a transcendental: IEEE-754 requires it to be
    /// correctly rounded, so it is bit-identical wherever it runs. RoadPath needs it for arc
    /// length and nothing else in Core needs it at all.
    /// </summary>
    [TestFixture]
    public class CoreDeterminismTests
    {
        private static readonly string[] Banned =
        {
            "Sin", "Cos", "Tan", "Asin", "Acos", "Atan", "Atan2", "Exp", "Log", "Log10", "Pow",
        };

        [Test]
        public void NoCoreFileCallsATranscendental()
        {
            var offenders = new List<string>();

            foreach (string file in Directory.GetFiles(
                         Path.Combine(RepoRoot(), "Assets", "Noir", "Core"), "*.cs",
                         SearchOption.AllDirectories))
            {
                // Comments stripped first, the same way TwoToOneTests strips them: a file must be
                // able to say "no Cos in here" in its own header without tripping its own guard.
                string source = Regex.Replace(File.ReadAllText(file), @"/\*.*?\*/", "",
                                              RegexOptions.Singleline);
                source = Regex.Replace(source, @"//[^\n]*", "");

                foreach (string name in Banned)
                {
                    // Match a CALL through Math/MathF only. A bare word would fire on any
                    // identifier containing it - Cost, Single, Login - and a guard that cries
                    // wolf is a guard somebody switches off.
                    if (Regex.IsMatch(source, @"\bMathF?\." + name + @"\s*\("))
                        offenders.Add(Path.GetFileName(file) + " -> Math." + name);
                }
            }

            Assert.That(offenders, Is.Empty,
                "Transcendentals in Core:\n  " + string.Join("\n  ", offenders) + "\n\n" +
                "Their results are implementation-defined and have changed between .NET\n" +
                "runtimes, which would silently break replay. See Vec2.cs. Sqrt is allowed.");
        }

        [Test]
        public void SqrtIsAllowedSoTheBanIsAboutDeterminismAndNotAboutMath()
        {
            // Falsification: the matcher must NOT fire on the one function RoadPath relies on.
            // Without this, a lazier regex that banned everything on Math would pass the test
            // above for the wrong reason and block Task 3 for no reason.
            const string sample = "var d = MathF.Sqrt(dx * dx + dy * dy);";
            foreach (string name in Banned)
                Assert.That(Regex.IsMatch(sample, @"\bMathF?\." + name + @"\s*\("), Is.False,
                            "Sqrt must not be caught by the " + name + " matcher");
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "Noir", "Core", "World")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not find Assets/Noir/Core/World above " + AppContext.BaseDirectory);
        }
    }
}
```

- [ ] **Step 2: Run it and watch it pass on today's Core**

Run: `dotnet test -c Release tools/Noir.Core.Tests --filter "FullyQualifiedName~CoreDeterminismTests"`
Expected: **2 passed.** Core calls no math functions today, so a green result here is correct and is the point — the guard is now in place before anything can violate it.

- [ ] **Step 3: Falsify it**

Temporarily add this line inside any method in `Assets/Noir/Core/World/RoadNetwork.cs`:

```csharp
            float bogus = (float)Math.Cos(1.0);
```

Run: `dotnet test -c Release tools/Noir.Core.Tests --filter "FullyQualifiedName~CoreDeterminismTests"`
Expected: **FAIL**, naming `RoadNetwork.cs -> Math.Cos`. A guard nobody has seen fail is a guard nobody knows works. **Delete the line again** and re-run to confirm green.

- [ ] **Step 4: Commit**

```bash
git add tools/Noir.Core.Tests/CoreDeterminismTests.cs
git commit -m "The transcendental ban Vec2 always claimed was enforced

Vec2's header says 'A build-time test greps for them'. It did not exist. Core
calls no math function at all today, so the ban has been resting on a comment -
and RoadPath is about to introduce Core's first Sqrt.

Sqrt is deliberately allowed and there is a falsification test to keep it that
way: IEEE-754 requires it correctly rounded, so unlike Sin it is bit-identical
wherever it runs. The matcher only fires on a call through Math/MathF, because
a bare word search hits Cost, Single and Login, and a guard that cries wolf is
a guard somebody switches off."
```

---

### Task 2: Record the golden baseline

Everything after this changes how junctions and lanes are computed. This records what they compute **now**, over the real map, so any drift is caught by number rather than by eye.

The counts are recorded by running the current build. **Do not copy figures from `docs/STATE.md`** — they predate the present 2100×2400 map.

**Files:**
- Create: `tools/Noir.Core.Tests/RoadGeometryBaselineTests.cs`

**Interfaces:**
- Consumes: `TestContent.EnsureKinds()` and `TestContent.ReadRaw(string)` from `WorldTests.cs`; `VillageParser.Parse`, `WorldBuilder.Build`, `WorldModel.Roads`, `LaneGraph`.
- Produces: `RoadGeometryBaseline.RealCity()` — returns `WorldModel` for the real `Content/city.txt`, reused by Tasks 6 and 7.

- [ ] **Step 1: Write the baseline test with the numbers left to be filled by the run**

Create `tools/Noir.Core.Tests/RoadGeometryBaselineTests.cs`:

```csharp
using NUnit.Framework;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// What the road network and the lane graph produce for the REAL town, pinned.
    ///
    /// Phase A rewrites how both are computed - junctions become path intersections, lanes
    /// become arc length - and the whole safety argument is that the 27 straight roads
    /// Rossville is built from come out unchanged. That claim is only worth anything if it is
    /// a number somebody recorded before the rewrite started.
    ///
    /// These figures were READ OFF THE BUILD, not off any document. docs/STATE.md quotes
    /// counts from a 960x960 map that no longer exists.
    /// </summary>
    [TestFixture]
    public class RoadGeometryBaselineTests
    {
        public static WorldModel RealCity()
        {
            TestContent.EnsureKinds();
            return WorldBuilder.Build(
                VillageParser.Parse(TestContent.ReadRaw("city.txt")), 1234UL);
        }

        [Test]
        public void TheRealCityHasTheRoadsAndJunctionsItHadBeforePhaseA()
        {
            var world = RealCity();
            var graph = new LaneGraph(world.Roads, world.Width, world.Height);

            TestContext.Out.WriteLine($"roads      = {world.Roads.Lines.Count}");
            TestContext.Out.WriteLine($"junctions  = {world.Roads.Junctions.Count}");
            TestContext.Out.WriteLine($"segments   = {graph.Segments.Count}");
            TestContext.Out.WriteLine($"turns      = {graph.Turns.Count}");
            TestContext.Out.WriteLine($"entries    = {graph.Entries.Count}");

            Assert.That(world.Roads.Lines.Count, Is.EqualTo(27), "roads in city.txt");
            Assert.That(world.Roads.Junctions.Count, Is.EqualTo(BaselineJunctions));
            Assert.That(graph.Segments.Count, Is.EqualTo(BaselineSegments));
            Assert.That(graph.Turns.Count, Is.EqualTo(BaselineTurns));
            Assert.That(graph.Entries.Count, Is.EqualTo(BaselineEntries));
        }

        [Test]
        public void EveryRealRoadIsStraightAndAxisAligned()
        {
            // The premise of the whole zero-regression argument. If this ever fails, a curve
            // has entered the map and the equivalence tests below stopped meaning what they say.
            foreach (var line in RealCity().Roads.Lines)
                Assert.That(line.IsStraight, Is.True, line.Name + " is not straight");
        }

        // Filled in at Step 3 from the run in Step 2.
        private const int BaselineJunctions = -1;
        private const int BaselineSegments = -1;
        private const int BaselineTurns = -1;
        private const int BaselineEntries = -1;
    }
}
```

- [ ] **Step 2: Run it to harvest the real numbers**

Run: `dotnet test -c Release tools/Noir.Core.Tests --filter "FullyQualifiedName~RoadGeometryBaselineTests" -l "console;verbosity=detailed"`
Expected: `TheRealCityHasTheRoadsAndJunctionsItHadBeforePhaseA` **FAILS** on the first `-1` constant, and the console shows the five real numbers printed above the failure. `EveryRealRoadIsStraightAndAxisAligned` passes.

- [ ] **Step 3: Bake the harvested numbers in**

Replace the four `-1` constants with the values printed in Step 2. If `roads` printed anything other than `27`, stop and report — `Content/city.txt` has changed since this plan was written and the rest of the plan's assumptions need rechecking.

- [ ] **Step 4: Run the whole Core suite and record the pre-existing state**

Run: `dotnet test -c Release tools/Noir.Core.Tests`
Expected: all green **except** `TheMedianVillagerYieldsTwiceAsMuchTextureAsUse` and `TheTenthPercentileIsNotALock`, which fail by design. Write the exact pass/fail totals into the commit message — this is the line every later task compares against.

- [ ] **Step 5: Commit**

```bash
git add tools/Noir.Core.Tests/RoadGeometryBaselineTests.cs
git commit -m "Record what the road network computes now, before changing how

Phase A rewrites junction-finding and lane-cutting, and its entire safety
argument is that the 27 straight roads Rossville is built from come out
unchanged. That is only worth something as a number recorded before the
rewrite, so this reads the counts off the current build and pins them.

Deliberately not taken from docs/STATE.md, whose figures describe a 960x960
map that no longer exists."
```

---

### Task 3: `RoadPath` — the exact straight case

Build the primitive with **only** the short-circuit. No spline yet: the straight case is the one that has to be exact, it covers every road in the map, and getting it alone under test first means Task 4's curve cannot quietly perturb it.

**Files:**
- Create: `Assets/Noir/Core/World/RoadPath.cs`
- Create: `tools/Noir.Core.Tests/RoadPathTests.cs`

**Interfaces:**
- Consumes: `Noir.Core.Contracts.Vec2`.
- Produces, relied on by Tasks 4–9:
  - `RoadPath.Straight(Vec2 from, Vec2 to)` → `RoadPath`
  - `float RoadPath.Length { get; }`
  - `bool RoadPath.IsStraightAxisAligned { get; }`
  - `Vec2 RoadPath.PointAt(float s)`
  - `Vec2 RoadPath.TangentAt(float s)` — unit
  - `Vec2 RoadPath.NormalAt(float s)` — right-hand, `(-t.Y, t.X)`
  - `(float S, float Lateral) RoadPath.Project(Vec2 p)`

- [ ] **Step 1: Write the failing tests**

Create `tools/Noir.Core.Tests/RoadPathTests.cs`:

```csharp
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The centreline primitive. Straight first, and exactly - every road in the real town is
    /// straight, so this is the case that must not move by a millimetre.
    /// </summary>
    [TestFixture]
    public class RoadPathTests
    {
        // Chicago Street as the map declares it today: 30m wide, x=750, the full height.
        private static RoadPath Chicago() =>
            RoadPath.Straight(new Vec2(750f, 0f), new Vec2(750f, 2400f));

        [Test]
        public void AStraightRunKnowsItIsStraight()
        {
            Assert.That(Chicago().IsStraightAxisAligned, Is.True);
            Assert.That(Chicago().Length, Is.EqualTo(2400f));
        }

        [Test]
        public void PointAtIsExactAlongAnAxis()
        {
            // EXACT, not approximately. A straight axis-aligned road must return the declared
            // coordinate bit for bit, because that is what the existing city is built from and
            // what every snapshot was rendered against.
            var path = Chicago();
            Assert.That(path.PointAt(0f).X, Is.EqualTo(750f));
            Assert.That(path.PointAt(0f).Y, Is.EqualTo(0f));
            Assert.That(path.PointAt(1335f).X, Is.EqualTo(750f));
            Assert.That(path.PointAt(1335f).Y, Is.EqualTo(1335f));
            Assert.That(path.PointAt(2400f).Y, Is.EqualTo(2400f));
        }

        [Test]
        public void PointAtClampsRatherThanRunningOffTheEnd()
        {
            var path = Chicago();
            Assert.That(path.PointAt(-50f).Y, Is.EqualTo(0f));
            Assert.That(path.PointAt(9999f).Y, Is.EqualTo(2400f));
        }

        [Test]
        public void TangentPointsTheWayTheRoadWasDeclared()
        {
            var t = Chicago().TangentAt(500f);
            Assert.That(t.X, Is.EqualTo(0f));
            Assert.That(t.Y, Is.EqualTo(1f), "declared north to south, so travel is +y");
        }

        [Test]
        public void NormalIsTheRightHandSideTheRestOfCoreAlreadyMeansByIt()
        {
            // Headings.Side derives the right of travel (dx,dy) as (-dy,dx): village coordinates
            // are x east, y south, the same handedness as a screen. Facing south, right is west.
            var n = Chicago().NormalAt(500f);
            Assert.That(n.X, Is.EqualTo(-1f));
            Assert.That(n.Y, Is.EqualTo(0f));
        }

        [Test]
        public void AnEastWestRunNormalsSouth()
        {
            // Facing east, right is south - the greater y. This is the pairing Headings.Side
            // spells out, and getting it backwards would put every lane on the wrong side.
            var attica = RoadPath.Straight(new Vec2(0f, 1335f), new Vec2(2100f, 1335f));
            var n = attica.NormalAt(100f);
            Assert.That(n.X, Is.EqualTo(0f));
            Assert.That(n.Y, Is.EqualTo(1f));
        }

        [Test]
        public void ALaneOffsetIsTheNormalTimesTheDistance()
        {
            // The expression that replaces every `line.Centre +/- offset` in the codebase.
            var path = Chicago();
            var lane = path.PointAt(1000f) + path.NormalAt(1000f) * 6f;
            Assert.That(lane.X, Is.EqualTo(744f), "6m to the right of a southbound road is west");
            Assert.That(lane.Y, Is.EqualTo(1000f));
        }

        [Test]
        public void ProjectFindsHowFarAlongAndHowFarAside()
        {
            var (s, lateral) = Chicago().Project(new Vec2(760f, 400f));
            Assert.That(s, Is.EqualTo(400f));
            // The point is 10m EAST of a southbound road, and east is its left, so lateral is
            // negative. Signed, not absolute - RoadNetwork.At needs the magnitude but a lane
            // needs the side.
            Assert.That(lateral, Is.EqualTo(-10f));
        }

        [Test]
        public void ProjectClampsToTheEndsOfTheRun()
        {
            var (s, _) = Chicago().Project(new Vec2(750f, -200f));
            Assert.That(s, Is.EqualTo(0f));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test -c Release tools/Noir.Core.Tests --filter "FullyQualifiedName~RoadPathTests"`
Expected: **compile error** — `RoadPath` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Assets/Noir/Core/World/RoadPath.cs`:

```csharp
using System;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// Where a road's centre line actually runs.
    ///
    /// This is the generalisation of RoadLine.Centre, which is A SINGLE FLOAT - "the x of a
    /// north-south road, the y of an east-west one" - and therefore cannot describe a road that
    /// bends. Illinois Route 1 through Rossville bends 100m off its own chord, and the town is
    /// built with it drawn as a straight line, which puts 85% of its length inside the county's
    /// own lot boundaries.
    ///
    /// THE STRAIGHT CASE IS EXACT AND THAT IS THE WHOLE SAFETY ARGUMENT. Every one of the 27
    /// roads in Content/city.txt is two points on one axis, and for those this class does no
    /// smoothing, no resampling and no square roots - PointAt is the declared coordinate,
    /// returned bit for bit. A curve costs what a curve costs; a straight road costs nothing
    /// and changes nothing.
    ///
    /// NO TRANSCENDENTALS. Tangents are differences, normals are (-y, x), and which way a turn
    /// goes is the sign of a cross product. Sqrt appears once, for arc length, and is allowed
    /// because IEEE-754 requires it correctly rounded - see CoreDeterminismTests.
    /// </summary>
    public sealed class RoadPath
    {
        private readonly Vec2 _from;
        private readonly Vec2 _to;
        private readonly Vec2 _tangent;      // unit, exact for an axis-aligned run

        public bool IsStraightAxisAligned { get; }
        public float Length { get; }

        private RoadPath(Vec2 from, Vec2 to)
        {
            _from = from;
            _to = to;
            IsStraightAxisAligned = true;

            float dx = to.X - from.X, dy = to.Y - from.Y;

            // Exactly one axis moves, so the length is that difference and the tangent is a
            // cardinal unit vector. Deliberately NOT sqrt(dx*dx+dy*dy): for dy=2400 that is
            // sqrt(5760000), which is 2400 to the last bit but arrives there through a rounding
            // nobody needs to trust.
            if (dx == 0f)
            {
                Length = dy < 0f ? -dy : dy;
                _tangent = new Vec2(0f, dy < 0f ? -1f : 1f);
            }
            else
            {
                Length = dx < 0f ? -dx : dx;
                _tangent = new Vec2(dx < 0f ? -1f : 1f, 0f);
            }
        }

        /// <summary>
        /// A straight run between two points on one axis. Throws if they are not: this
        /// constructor's promise is exactness, and it cannot keep it off-axis.
        /// </summary>
        public static RoadPath Straight(Vec2 from, Vec2 to)
        {
            if (from.X != to.X && from.Y != to.Y)
                throw new ArgumentException(
                    "RoadPath.Straight needs two points sharing an axis; got "
                    + from + " and " + to + ". A road that bends is built with RoadPath.Through.");
            if (from.X == to.X && from.Y == to.Y)
                throw new ArgumentException("RoadPath.Straight needs two distinct points; got " + from);

            return new RoadPath(from, to);
        }

        private float Clamp(float s) => s < 0f ? 0f : (s > Length ? Length : s);

        public Vec2 PointAt(float s)
        {
            s = Clamp(s);
            // One of these two terms is always zero, so the surviving coordinate is the
            // declared one untouched: no drift on the cross axis, ever.
            return new Vec2(_from.X + _tangent.X * s, _from.Y + _tangent.Y * s);
        }

        public Vec2 TangentAt(float s) => _tangent;

        /// <summary>
        /// The right-hand side of travel, which is what a lane offset is measured along.
        ///
        /// (-y, x) rather than (y, -x), and it is not a convention picked here: Headings.Side
        /// already derives it that way from village coordinates being x east, y south. Facing
        /// north (0,-1) the right is east (1,0). Getting it backwards puts every lane in the
        /// oncoming carriageway.
        /// </summary>
        public Vec2 NormalAt(float s)
        {
            var t = TangentAt(s);
            return new Vec2(-t.Y, t.X);
        }

        /// <summary>
        /// The nearest point on the centre line: how far along, and how far to the side.
        ///
        /// Lateral is SIGNED - positive on the road's right - because a lane needs the side and
        /// RoadNetwork.At only needs the magnitude. Returning the absolute value would serve the
        /// caller that matters least.
        /// </summary>
        public (float S, float Lateral) Project(Vec2 p)
        {
            var d = p - _from;
            float s = Clamp(d.X * _tangent.X + d.Y * _tangent.Y);

            var on = PointAt(s);
            var off = p - on;
            var n = NormalAt(s);
            return (s, off.X * n.X + off.Y * n.Y);
        }

        public override string ToString() =>
            (IsStraightAxisAligned ? "straight " : "curved ") + _from + ".." + _to
            + " (" + Length + "m)";
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test -c Release tools/Noir.Core.Tests --filter "FullyQualifiedName~RoadPathTests"`
Expected: **9 passed.**

- [ ] **Step 5: Confirm the ban still holds and Core still compiles to netstandard2.1**

Run: `dotnet build -c Release tools/Noir.Core && dotnet test -c Release tools/Noir.Core.Tests --filter "FullyQualifiedName~CoreDeterminismTests"`
Expected: build succeeds (this is the C# 9 / no-UnityEngine gate), 2 passed.

- [ ] **Step 6: Commit**

```bash
git add Assets/Noir/Core/World/RoadPath.cs tools/Noir.Core.Tests/RoadPathTests.cs
git commit -m "RoadPath, straight and exact

The generalisation of RoadLine.Centre, which is a single float and therefore
cannot describe a road that bends. Straight case only for now, and exact: no
smoothing, no resampling, no sqrt. PointAt returns the declared coordinate bit
for bit, because all 27 roads in city.txt are straight and every committed
snapshot was rendered against those numbers.

The normal is (-y, x) and that is not a convention invented here - Headings.Side
already derives the right-hand side that way from village coordinates being x
east, y south. Backwards would put every lane in the oncoming carriageway, so
there is a test for the east-west pairing as well as the north-south one."
```

---

### Task 4: `RoadPath` — the curved case

Now the spline, moved into Core from `MapFeatures.Smoothed`. It must reproduce that method's output **exactly**, because Task 5 hands the railway over to it and the rail bed is already built and committed against it.

**Files:**
- Modify: `Assets/Noir/Core/World/RoadPath.cs`
- Modify: `tools/Noir.Core.Tests/RoadPathTests.cs`

**Interfaces:**
- Consumes: everything from Task 3.
- Produces, relied on by Tasks 5–9:
  - `RoadPath.Through(IReadOnlyList<Vec2> points)` → `RoadPath` (straight-shortcircuits when given two axis-aligned points)
  - `RoadPath.Smooth(IReadOnlyList<Vec2> points)` → `Vec2[]` — the Catmull-Rom, exposed for `MapFeatures` in Task 5
  - `const int RoadPath.SmoothSteps = 4`
  - `const float RoadPath.ResamplePitch = 1f`

- [ ] **Step 1: Write the failing tests**

Append inside the `RoadPathTests` class in `tools/Noir.Core.Tests/RoadPathTests.cs`:

```csharp
        // ---- the curve ----------------------------------------------------------------

        /// <summary>A quarter-circle-ish bend, declared coarsely the way a survey way is.</summary>
        private static RoadPath Bend() => RoadPath.Through(new[]
        {
            new Vec2(0f, 0f), new Vec2(0f, 100f), new Vec2(20f, 180f), new Vec2(60f, 240f),
        });

        [Test]
        public void ThroughTwoAxisAlignedPointsIsStillTheExactStraightCase()
        {
            // The short circuit is the whole zero-regression argument, so it is asserted at the
            // door RoadLine will actually come in through, not only at RoadPath.Straight.
            var path = RoadPath.Through(new[] { new Vec2(750f, 0f), new Vec2(750f, 2400f) });
            Assert.That(path.IsStraightAxisAligned, Is.True);
            Assert.That(path.Length, Is.EqualTo(2400f));
            Assert.That(path.PointAt(1335f).X, Is.EqualTo(750f));
        }

        [Test]
        public void ACurveKnowsItIsNotStraight()
        {
            Assert.That(Bend().IsStraightAxisAligned, Is.False);
        }

        [Test]
        public void TheCurveStartsAndEndsOnItsDeclaredEndPoints()
        {
            var path = Bend();
            Assert.That(path.PointAt(0f).X, Is.EqualTo(0f).Within(0.001f));
            Assert.That(path.PointAt(0f).Y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(path.PointAt(path.Length).X, Is.EqualTo(60f).Within(0.5f));
            Assert.That(path.PointAt(path.Length).Y, Is.EqualTo(240f).Within(0.5f));
        }

        [Test]
        public void ArcLengthIsAtLeastTheStraightLineAndNotAbsurdlyMore()
        {
            // A curve between the same endpoints is longer than the chord and, for a bend this
            // gentle, not dramatically so. Pins the arc-length table against both the classic
            // failures: summing nothing, and summing the resample points twice.
            var path = Bend();
            Assert.That(path.Length, Is.GreaterThan(240f));
            Assert.That(path.Length, Is.LessThan(330f));
        }

        [Test]
        public void WalkingTheCurveMovesAtOneMetrePerMetre()
        {
            // What arc-length parameterisation MEANS, and the thing a naive t-in-0..1 spline
            // gets wrong: equal steps of s must be equal distances on the ground, or a car
            // driving the curve speeds up and slows down through the bend.
            var path = Bend();
            for (float s = 0f; s + 10f <= path.Length; s += 10f)
            {
                var a = path.PointAt(s);
                var b = path.PointAt(s + 10f);
                float step = (b - a).LengthSquared;
                Assert.That(step, Is.EqualTo(100f).Within(6f), "uneven step at s=" + s);
            }
        }

        [Test]
        public void TheTangentIsAlwaysAUnitVector()
        {
            var path = Bend();
            for (float s = 0f; s <= path.Length; s += 5f)
                Assert.That(path.TangentAt(s).LengthSquared, Is.EqualTo(1f).Within(0.001f),
                            "tangent not unit at s=" + s);
        }

        [Test]
        public void TheTangentTurnsThroughTheBendRatherThanJumping()
        {
            // Smoothness is the reason for the spline. Consecutive tangents must stay close;
            // a kink would show as a sudden drop in the dot product.
            var path = Bend();
            for (float s = 0f; s + 2f <= path.Length; s += 2f)
            {
                var a = path.TangentAt(s);
                var b = path.TangentAt(s + 2f);
                Assert.That(a.X * b.X + a.Y * b.Y, Is.GreaterThan(0.99f), "kink at s=" + s);
            }
        }

        [Test]
        public void ProjectRoundTripsAnywhereOnTheCurve()
        {
            var path = Bend();
            for (float s = 0f; s <= path.Length; s += 7f)
            {
                var (back, lateral) = path.Project(path.PointAt(s));
                Assert.That(back, Is.EqualTo(s).Within(1.0f), "s did not round trip at " + s);
                Assert.That(lateral, Is.EqualTo(0f).Within(0.5f), "a point ON the curve is not aside");
            }
        }

        [Test]
        public void ProjectPutsTheRightHandSideOnTheRight()
        {
            var path = Bend();
            var at = path.PointAt(50f);
            var n = path.NormalAt(50f);
            var (_, lateral) = path.Project(at + n * 8f);
            Assert.That(lateral, Is.EqualTo(8f).Within(0.5f), "offset along the normal must read positive");
        }

        [Test]
        public void SmoothKeepsEveryDeclaredPointOnTheCurve()
        {
            // MapFeatures.Smoothed promises exactly this - "every original point is still on the
            // curve exactly where it was" - and Task 5 hands the railway to this code. The rail
            // bed is already built and committed against that promise.
            var declared = new[]
            {
                new Vec2(0f, 0f), new Vec2(0f, 100f), new Vec2(20f, 180f), new Vec2(60f, 240f),
            };
            var smoothed = RoadPath.Smooth(declared);

            Assert.That(smoothed.Length, Is.EqualTo((declared.Length - 1) * RoadPath.SmoothSteps + 1));
            foreach (var p in declared)
            {
                bool found = false;
                foreach (var q in smoothed)
                    if ((q - p).LengthSquared < 1e-6f) { found = true; break; }
                Assert.That(found, Is.True, "declared point " + p + " is not on the smoothed curve");
            }
        }

        [Test]
        public void SmoothReproducesTheRailwaysOwnCatmullRomToTheBit()
        {
            // The one value that proves this is the SAME curve MapFeatures.Smoothed draws, not
            // merely a similar one. Catmull-Rom at t=0.5 on the first span of the fixture above,
            // with the first point clamped as its own neighbour:
            //   0.5 * (2*p1 + (-p0+p2)*t + (2*p0-5*p1+4*p2-p3)*t^2 + (-p0+3*p1-3*p2+p3)*t^3)
            // with p0=p1=(0,0), p2=(0,100), p3=(20,180) at t=0.5:
            //   x = 0.5 * ((-20)*0.25 + 20*0.125)          = 0.5 * -2.5 = -1.25
            //   y = 0.5 * (100*0.5 + 220*0.25 + -120*0.125) = 0.5 * 90   = 45
            var smoothed = RoadPath.Smooth(new[]
            {
                new Vec2(0f, 0f), new Vec2(0f, 100f), new Vec2(20f, 180f), new Vec2(60f, 240f),
            });

            Assert.That(smoothed[2].X, Is.EqualTo(-1.25f).Within(1e-4f));
            Assert.That(smoothed[2].Y, Is.EqualTo(45f).Within(1e-4f));
        }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test -c Release tools/Noir.Core.Tests --filter "FullyQualifiedName~RoadPathTests"`
Expected: **compile error** — `RoadPath.Through` and `RoadPath.Smooth` do not exist.

- [ ] **Step 3: Write the implementation**

In `Assets/Noir/Core/World/RoadPath.cs`, add `using System.Collections.Generic;` at the top, then add these members to the class. Replace the four existing member bodies (`Length`/`PointAt`/`TangentAt`/`Project`) as shown so the curved branch is honoured:

```csharp
        /// <summary>
        /// Sub-divisions inserted between each pair of declared vertices. FOUR, because that is
        /// what MapFeatures.Smoothed has always used and the committed rail bed was built with -
        /// see RoadPath.Smooth.
        /// </summary>
        public const int SmoothSteps = 4;

        /// <summary>
        /// Metres between resampled points. One, matching what CityRailBed already resamples the
        /// rail bed at, so a long straight and a tight bend are built to the same resolution.
        /// Only a curve pays for this; a straight road never reaches the resampler.
        /// </summary>
        public const float ResamplePitch = 1f;

        private readonly Vec2[] _dense;          // null for the straight case
        private readonly float[] _cumulative;    // null for the straight case

        private RoadPath(Vec2[] dense, float[] cumulative)
        {
            _dense = dense;
            _cumulative = cumulative;
            IsStraightAxisAligned = false;
            Length = cumulative[cumulative.Length - 1];
            _from = dense[0];
            _to = dense[dense.Length - 1];
        }

        /// <summary>
        /// A road through the points it was declared with. Two points on one axis short-circuit
        /// to the exact straight case - which is every road in the real map, and the reason
        /// Phase A changes no numbers.
        /// </summary>
        public static RoadPath Through(IReadOnlyList<Vec2> points)
        {
            if (points == null || points.Count < 2)
                throw new ArgumentException("a road path needs at least two points");

            if (points.Count == 2 && (points[0].X == points[1].X || points[0].Y == points[1].Y))
                return Straight(points[0], points[1]);

            var dense = Resample(Smooth(points), ResamplePitch);

            var cumulative = new float[dense.Length];
            for (int i = 1; i < dense.Length; i++)
                cumulative[i] = cumulative[i - 1] + Distance(dense[i - 1], dense[i]);

            return new RoadPath(dense, cumulative);
        }

        /// <summary>
        /// Catmull-Rom through the declared vertices, unchanged - every original point is still
        /// on the curve exactly where it was, only the straight segments between them become an
        /// arc. End points are their own neighbour, the standard clamp for a spline with nothing
        /// before its first control point.
        ///
        /// MOVED HERE FROM MapFeatures.Smoothed, which drew the railway with it. One curve, used
        /// by the rail and the roads alike, and testable under dotnet test - which it never was
        /// on the Unity side. Change nothing about the arithmetic without re-rendering the rail
        /// snapshots: the committed bed was built from these exact numbers.
        /// </summary>
        public static Vec2[] Smooth(IReadOnlyList<Vec2> pts)
        {
            if (pts.Count < 3)
            {
                var copy = new Vec2[pts.Count];
                for (int i = 0; i < pts.Count; i++) copy[i] = pts[i];
                return copy;
            }

            var result = new List<Vec2>((pts.Count - 1) * SmoothSteps + 1) { pts[0] };
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var p0 = pts[i - 1 < 0 ? 0 : i - 1];
                var p1 = pts[i];
                var p2 = pts[i + 1];
                var p3 = pts[i + 2 > pts.Count - 1 ? pts.Count - 1 : i + 2];

                for (int s = 1; s <= SmoothSteps; s++)
                    result.Add(CatmullRom(p0, p1, p2, p3, (float)s / SmoothSteps));
            }
            return result.ToArray();
        }

        private static Vec2 CatmullRom(Vec2 p0, Vec2 p1, Vec2 p2, Vec2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return (p1 * 2f
                  + (p2 - p0) * t
                  + (p0 * 2f - p1 * 5f + p2 * 4f - p3) * t2
                  + (p1 * 3f - p0 - p2 * 3f + p3) * t3) * 0.5f;
        }

        /// <summary>Even spacing along the polyline, so equal steps of s are equal ground.</summary>
        private static Vec2[] Resample(Vec2[] pts, float pitch)
        {
            var result = new List<Vec2> { pts[0] };
            float carried = 0f;

            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vec2 a = pts[i], b = pts[i + 1];
                float span = Distance(a, b);
                if (span <= 1e-6f) continue;

                float travelled = pitch - carried;
                while (travelled <= span)
                {
                    result.Add(Vec2.Lerp(a, b, travelled / span));
                    travelled += pitch;
                }
                carried = span - (travelled - pitch);
            }

            var last = pts[pts.Length - 1];
            if (Distance(result[result.Count - 1], last) > 1e-4f) result.Add(last);
            return result.ToArray();
        }

        /// <summary>
        /// The one square root in Core. Allowed, and not a loophole: IEEE-754 requires Sqrt to
        /// be correctly rounded, so unlike Sin it is bit-identical on every runtime. See
        /// CoreDeterminismTests, which permits it by name and forbids the rest.
        /// </summary>
        private static float Distance(Vec2 a, Vec2 b)
        {
            var d = b - a;
            return (float)Math.Sqrt(d.X * d.X + d.Y * d.Y);
        }

        /// <summary>The dense index at or before arc length s, by bisection.</summary>
        private int IndexAt(float s)
        {
            int lo = 0, hi = _cumulative.Length - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) / 2;
                if (_cumulative[mid] <= s) lo = mid; else hi = mid;
            }
            return lo;
        }
```

Then replace the three straight-only members so each honours the curve:

```csharp
        public Vec2 PointAt(float s)
        {
            s = Clamp(s);
            if (IsStraightAxisAligned)
                return new Vec2(_from.X + _tangent.X * s, _from.Y + _tangent.Y * s);

            int i = IndexAt(s);
            float span = _cumulative[i + 1] - _cumulative[i];
            if (span <= 1e-6f) return _dense[i];
            return Vec2.Lerp(_dense[i], _dense[i + 1], (s - _cumulative[i]) / span);
        }

        public Vec2 TangentAt(float s)
        {
            if (IsStraightAxisAligned) return _tangent;

            int i = IndexAt(Clamp(s));
            var d = _dense[i + 1] - _dense[i];
            float len = Distance(_dense[i], _dense[i + 1]);
            return len <= 1e-6f ? new Vec2(1f, 0f) : new Vec2(d.X / len, d.Y / len);
        }

        public (float S, float Lateral) Project(Vec2 p)
        {
            if (IsStraightAxisAligned)
            {
                var straightD = p - _from;
                float straightS = Clamp(straightD.X * _tangent.X + straightD.Y * _tangent.Y);
                var straightOff = p - PointAt(straightS);
                var straightN = NormalAt(straightS);
                return (straightS, straightOff.X * straightN.X + straightOff.Y * straightN.Y);
            }

            // Nearest over every dense segment. Linear in the number of samples and called only
            // by RoadNetwork.At, which is not on a per-frame path.
            float bestS = 0f, bestD2 = float.MaxValue;
            for (int i = 0; i < _dense.Length - 1; i++)
            {
                Vec2 a = _dense[i], b = _dense[i + 1];
                var ab = b - a;
                float span2 = ab.LengthSquared;
                if (span2 <= 1e-9f) continue;

                var ap = p - a;
                float t = (ap.X * ab.X + ap.Y * ab.Y) / span2;
                t = t < 0f ? 0f : (t > 1f ? 1f : t);

                var on = Vec2.Lerp(a, b, t);
                float d2 = (p - on).LengthSquared;
                if (d2 >= bestD2) continue;

                bestD2 = d2;
                bestS = _cumulative[i] + (_cumulative[i + 1] - _cumulative[i]) * t;
            }

            var offset = p - PointAt(bestS);
            var normal = NormalAt(bestS);
            return (bestS, offset.X * normal.X + offset.Y * normal.Y);
        }
```

Finally, add `_dense = null; _cumulative = null;` as the first two statements of the existing `RoadPath(Vec2 from, Vec2 to)` constructor, so the straight case's curve fields read as deliberate rather than merely defaulted. `Length` and `IsStraightAxisAligned` need no change — a getter-only auto-property is assignable from any constructor of its own class, and both constructors set both.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test -c Release tools/Noir.Core.Tests --filter "FullyQualifiedName~RoadPathTests"`
Expected: **20 passed** (9 from Task 3, 11 new).

- [ ] **Step 5: Confirm the ban still holds**

Run: `dotnet test -c Release tools/Noir.Core.Tests --filter "FullyQualifiedName~CoreDeterminismTests"`
Expected: 2 passed. `Math.Sqrt` is present in Core for the first time and must be permitted; `Math.Cos` and friends must still be absent.

- [ ] **Step 6: Commit**

```bash
git add Assets/Noir/Core/World/RoadPath.cs tools/Noir.Core.Tests/RoadPathTests.cs
git commit -m "RoadPath learns to bend, using the railway's own Catmull-Rom

The spline moves into Core from MapFeatures.Smoothed unchanged - same four
sub-divisions, same clamped end points, same arithmetic - because Task 5 hands
the railway to this code and the committed rail bed was built from those exact
numbers. There is a test pinning one interpolated value to the bit for that
reason, not merely a 'looks smooth' assertion.

Arc-length parameterised, so equal steps of s are equal ground. A spline walked
in its own t speeds up and slows down through a bend, which would have shown up
later as cars accelerating round corners.

Core's first sqrt, for arc length. Permitted by name in CoreDeterminismTests:
IEEE-754 requires it correctly rounded, so unlike Sin it cannot drift between
runtimes."
```

---

### Task 5: Share the curve with the railway

One method, on the Unity side, with a real risk attached: the rail bed and the plan view are already committed and were rendered from `MapFeatures.Smoothed`. If Core's copy differs at all, the railway moves.

**Files:**
- Modify: `Assets/Noir/Unity/MapFeatures.cs` (the `Smoothed` method and its private `CatmullRom`)

**Interfaces:**
- Consumes: `RoadPath.Smooth(IReadOnlyList<Vec2>)`, `RoadPath.SmoothSteps` from Task 4.
- Produces: nothing new. `MapFeatures.Smoothed(List<Vector2>) → List<Vector2>` keeps its exact signature; `CityRailBed`, `CityOutlines` and `GroundShot` are untouched.

- [ ] **Step 1: Replace the body of `Smoothed` and delete the private `CatmullRom`**

In `Assets/Noir/Unity/MapFeatures.cs`, keep the existing doc-comment on `Smoothed`, add a line to it pointing at Core, and replace the method body. Delete the private `CatmullRom` helper entirely — it now lives in Core.

```csharp
        /// (The curve itself is Noir.Core.World.RoadPath.Smooth. It moved to Core so the roads
        /// and the railway bend along the same arithmetic rather than two copies of it, and so
        /// it can be tested under dotnet test - which it never could be here.)
        public static List<Vector2> Smoothed(List<Vector2> pts)
        {
            if (pts.Count < 3) return pts;

            var input = new Noir.Core.Contracts.Vec2[pts.Count];
            for (int i = 0; i < pts.Count; i++) input[i] = new Noir.Core.Contracts.Vec2(pts[i].x, pts[i].y);

            var smoothed = Noir.Core.World.RoadPath.Smooth(input);

            var result = new List<Vector2>(smoothed.Length);
            foreach (var p in smoothed) result.Add(new Vector2(p.X, p.Y));
            return result;
        }
```

- [ ] **Step 2: Compile the project headlessly**

The user must close the Unity editor first — a batch-mode run fails while the project is open.

Run:
```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.20f1/Editor/Unity.exe" \
  -batchmode -quit -nographics -projectPath C:/SerialKillerGame \
  -logFile C:/SerialKillerGame/Logs/compile-task5.log
```
Then wait for the process to actually exit — `Unity.exe` forks a child and the parent returns immediately, so `$LASTEXITCODE` is meaningless:
```bash
until ! tasklist //FI "IMAGENAME eq Unity.exe" | grep -q Unity.exe; do sleep 3; done
grep -n "error CS" C:/SerialKillerGame/Logs/compile-task5.log || echo "clean"
```
Expected: `clean`.

- [ ] **Step 3: Prove the railway did not move**

Run:
```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.20f1/Editor/Unity.exe" \
  -batchmode -quit -projectPath C:/SerialKillerGame \
  -executeMethod Noir.Editor.GroundShot.Rail \
  -logFile C:/SerialKillerGame/Logs/rail-task5.log
until ! tasklist //FI "IMAGENAME eq Unity.exe" | grep -q Unity.exe; do sleep 3; done
git status --short docs/snapshots/
```
Expected: **`docs/snapshots/rail-*.png` come back unmodified.** Byte-identical renders are the proof that Core's Catmull-Rom is the same curve, not a similar one.

If they differ, **stop and report**. Do not re-baseline the snapshots — a changed rail bed means the arithmetic diverged and Task 4 needs fixing, not the pictures.

- [ ] **Step 4: Commit**

```bash
git add Assets/Noir/Unity/MapFeatures.cs
git commit -m "The railway and the roads bend along the same arithmetic

MapFeatures.Smoothed becomes a delegation to RoadPath.Smooth. There were already
two copies of real-geometry handling in this project - this spline for drawing,
and a hardcoded RAIL polyline in relay-rossville.py for placement - and having
them is exactly how Route 1's curve went missing: the streets were flattened to
one scalar each while the rail kept its shape.

Verified by re-rendering the rail snapshots and confirming they come back
byte-identical, which is what proves it is the same curve rather than a similar
one."
```

---

### Task 6: `RoadLine` gains `Path`

**Files:**
- Modify: `Assets/Noir/Core/World/RoadNetwork.cs` (the `RoadLine` class)
- Modify: `tools/Noir.Core.Tests/RoadGeometryBaselineTests.cs`

**Interfaces:**
- Consumes: `RoadPath.Through`, `RoadPath.Straight` from Tasks 3–4; `RoadGeometryBaselineTests.RealCity()` from Task 2.
- Produces, relied on by Tasks 7–9: `RoadPath RoadLine.Path { get; }`, never null for a line with two or more points.

- [ ] **Step 1: Write the failing equivalence test**

Append inside the `RoadGeometryBaselineTests` class:

```csharp
        [Test]
        public void EveryRealRoadsPathReproducesItsOldCentreExactly()
        {
            // The zero-regression guarantee, asserted against real content rather than a
            // fixture. Centre is the single float Phase A is replacing; if Path disagrees with
            // it anywhere on any of the 27 roads, the town has moved.
            foreach (var line in RealCity().Roads.Lines)
            {
                Assert.That(line.Path, Is.Not.Null, line.Name + " has no path");
                Assert.That(line.Path.IsStraightAxisAligned, Is.True, line.Name);
                Assert.That(line.Path.Length, Is.EqualTo(line.To - line.From).Within(0f),
                            line.Name + " length");

                for (float t = 0f; t <= 1f; t += 0.1f)
                {
                    float s = line.Path.Length * t;
                    var p = line.Path.PointAt(s);

                    float across = line.IsNorthSouth ? p.X : p.Y;
                    float along = line.IsNorthSouth ? p.Y : p.X;

                    Assert.That(across, Is.EqualTo(line.Centre),
                                line.Name + " drifted off its centre at s=" + s);
                    Assert.That(along, Is.EqualTo(line.From + s),
                                line.Name + " is not where From+s says at s=" + s);
                }
            }
        }

        [Test]
        public void APathsTangentAgreesWithTheAxisTheLineSaysItRunsOn()
        {
            foreach (var line in RealCity().Roads.Lines)
            {
                var t = line.Path.TangentAt(line.Path.Length * 0.5f);
                if (line.IsNorthSouth)
                    Assert.That(t.X, Is.EqualTo(0f), line.Name + " is N-S but its tangent has x");
                else
                    Assert.That(t.Y, Is.EqualTo(0f), line.Name + " is E-W but its tangent has y");
            }
        }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test -c Release tools/Noir.Core.Tests --filter "FullyQualifiedName~RoadGeometryBaselineTests"`
Expected: **compile error** — `RoadLine.Path` does not exist.

- [ ] **Step 3: Add `Path` to `RoadLine`**

In `Assets/Noir/Core/World/RoadNetwork.cs`, add `using Noir.Core.Contracts;` if absent, add the field to `RoadLine`:

```csharp
        /// <summary>
        /// Where the centre line actually runs.
        ///
        /// Centre above is A SINGLE FLOAT and cannot describe a road that bends, which is why
        /// Illinois Route 1 is drawn straight through 85% of the lots it passes. For a straight
        /// axis-aligned road - which is all 27 in the current map - this is the exact same
        /// geometry Centre/From/To describe, and RoadPath returns it bit for bit.
        /// </summary>
        public readonly RoadPath Path;
```

At the end of the `RoadLine` constructor — after `From` and `To` are assigned — build it:

```csharp
            // Built from the DERIVED continuous centre line, not from the declared tiles: the
            // brush covers -(W/2)..(W/2 + W%2 - 1), so an odd width sits half a tile past the
            // declared coordinate, and a tile's run ends at hi+1 rather than hi. Path has to
            // describe the road WorldBuilder actually strokes.
            if (IsStraight)
            {
                Path = IsNorthSouth
                    ? RoadPath.Straight(new Vec2(Centre, From), new Vec2(Centre, To))
                    : RoadPath.Straight(new Vec2(From, Centre), new Vec2(To, Centre));
            }
            else
            {
                // A declared curve runs through its tile centres - Vec2.CentreOf, the convention
                // the rest of Core already means by "where a tile is". Phase A ships no curved
                // road, so nothing exercises this on real content yet; Phase C revisits it if the
                // half-width parity above turns out to matter on a bend.
                var through = new Vec2[Points.Count];
                for (int i = 0; i < Points.Count; i++) through[i] = Vec2.CentreOf(Points[i]);
                Path = RoadPath.Through(through);
            }
```

Note the early `return` in the existing constructor when `Points.Count < 2` — leave it. A line with fewer than two points keeps a null `Path`, and the `Is.Not.Null` assertion above only covers real roads.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test -c Release tools/Noir.Core.Tests --filter "FullyQualifiedName~RoadGeometryBaselineTests"`
Expected: **4 passed.**

- [ ] **Step 5: Run the whole Core suite**

Run: `dotnet test -c Release tools/Noir.Core.Tests`
Expected: same totals as Task 2 Step 4, plus the new tests. Only the two by-design failures.

- [ ] **Step 6: Commit**

```bash
git add Assets/Noir/Core/World/RoadNetwork.cs tools/Noir.Core.Tests/RoadGeometryBaselineTests.cs
git commit -m "RoadLine carries a path as well as a scalar centre

Centre, IsNorthSouth, IsStraight, From and To all keep their exact meanings -
13 files read them - and Path arrives alongside. For the 27 straight roads in
the real map the two describe identical geometry, asserted at eleven points
along every one of them rather than at the ends.

Built from the derived continuous centre line rather than the declared tiles,
because an odd width sits half a tile past its declared coordinate and a run
ends at hi+1. Path has to describe the road WorldBuilder actually strokes."
```

---

### Task 7: `RoadNetwork.At` through `Project`

**Files:**
- Modify: `Assets/Noir/Core/World/RoadNetwork.cs` (the `At` method)
- Modify: `tools/Noir.Core.Tests/RoadNetworkTests.cs`

**Interfaces:**
- Consumes: `RoadLine.Path`, `RoadPath.Project` from Task 6.
- Produces: `RoadNetwork.At(float, float)` keeps its signature and now answers for curved roads too.

- [ ] **Step 1: Write the failing test**

Append inside `RoadNetworkTests`:

```csharp
        [Test]
        public void AtAnswersForACurvedRoadToo()
        {
            // The old At skipped any line where !IsStraight outright, so a bent road was
            // invisible to zoning and to the lighting pass - they would have called it open
            // ground and planted trees down the carriageway.
            var world = Build(Header
                + "road bend 10 20,20 20,120 60,180 120,200\n  class street\n");

            var line = world.Roads.Lines[0];
            Assert.That(line.IsStraight, Is.False, "the fixture is meant to bend");

            var onIt = line.Path.PointAt(line.Path.Length * 0.5f);
            Assert.That(world.Roads.At(onIt.X, onIt.Y), Is.Not.Null,
                        "a point on the centre line is on the road");

            var beside = onIt + line.Path.NormalAt(line.Path.Length * 0.5f) * 40f;
            Assert.That(world.Roads.At(beside.X, beside.Y), Is.Null,
                        "40m aside from a 10m corridor is not on the road");
        }

        [Test]
        public void AtStillPrefersTheWidestWhereTwoRoadsOverlap()
        {
            var world = Build(Header
                + "road wide 30 0,75 239,75\n  class mainroad\n"
                + "road narrow 10 75,0 75,239\n  class street\n");

            Assert.That(world.Roads.At(75f, 75f).Name, Is.EqualTo("wide"));
        }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test -c Release tools/Noir.Core.Tests --filter "FullyQualifiedName~RoadNetworkTests"`
Expected: `AtAnswersForACurvedRoadToo` **FAILS** — `At` returns null on the centre line, because the `!IsStraight` guard skips the road.

- [ ] **Step 3: Rewrite `At`**

Replace the body of `RoadNetwork.At` in `Assets/Noir/Core/World/RoadNetwork.cs`:

```csharp
        /// <summary>The road covering this point, or null. The widest wins where two overlap.</summary>
        public RoadLine At(float x, float y)
        {
            RoadLine best = null;
            foreach (var line in Lines)
            {
                if (line.Path == null) continue;

                // Through the path rather than against Centre, so this answers for a road that
                // bends. For a straight axis-aligned line Project reduces to exactly the old
                // Math.Abs(across - Centre) test - see RoadGeometryBaselineTests.
                var (s, lateral) = line.Path.Project(new Vec2(x, y));
                if ((lateral < 0f ? -lateral : lateral) > line.HalfWidth) continue;
                if (s <= 0f || s >= line.Path.Length)
                {
                    // Project clamps, so a point off the end reports s at the end with a small
                    // lateral. Reject unless it is genuinely within the run.
                    float along = line.IsNorthSouth ? y : x;
                    if (along < line.From || along > line.To) continue;
                }
                if (best == null || line.Width > best.Width) best = line;
            }
            return best;
        }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test -c Release tools/Noir.Core.Tests --filter "FullyQualifiedName~RoadNetworkTests"`
Expected: all `RoadNetworkTests` pass, including the pre-existing ones.

- [ ] **Step 5: Run the whole Core suite**

Run: `dotnet test -c Release tools/Noir.Core.Tests`
Expected: only the two by-design failures.

- [ ] **Step 6: Commit**

```bash
git add Assets/Noir/Core/World/RoadNetwork.cs tools/Noir.Core.Tests/RoadNetworkTests.cs
git commit -m "RoadNetwork.At asks the path, so a bend is on the road

At skipped any line where !IsStraight, which meant a curved road was invisible
to the zoning and lighting passes - they would have read the carriageway as open
ground and planted trees down it.

For a straight axis-aligned line Project reduces to the same
abs(across - Centre) <= HalfWidth test it always was, which the baseline suite
asserts on all 27 real roads."
```

---

### Task 8: Junctions become real crossings

**Files:**
- Modify: `Assets/Noir/Core/World/RoadNetwork.cs` (the `Junction` struct and the `RoadNetwork` constructor)
- Modify: `tools/Noir.Core.Tests/RoadNetworkTests.cs`

**Interfaces:**
- Consumes: `RoadLine.Path`, `RoadPath.Project`, `RoadPath.PointAt`, `RoadPath.TangentAt`.
- Produces, relied on by Task 9:
  - `float Junction.SNorthSouth`, `float Junction.SEastWest` — arc length of the crossing along each road
  - `Vec2 Junction.TangentNorthSouth`, `Vec2 Junction.TangentEastWest`
  - `Junction.X`, `Junction.Y`, `Junction.NorthSouth`, `Junction.EastWest`, `Junction.Reach` all keep their names and meanings.

- [ ] **Step 1: Write the failing tests**

Append inside `RoadNetworkTests`:

```csharp
        [Test]
        public void AJunctionKnowsHowFarAlongEachRoadItSits()
        {
            var world = Build(Header
                + "road ew 30 0,75 239,75\n  class mainroad\n"
                + "road ns 30 165,0 165,239\n  class mainroad\n");

            Assert.That(world.Roads.Junctions.Count, Is.EqualTo(1));
            var j = world.Roads.Junctions[0];

            Assert.That(j.X, Is.EqualTo(165f), "unchanged: the N-S road's centre");
            Assert.That(j.Y, Is.EqualTo(75f), "unchanged: the E-W road's centre");

            // New, and what LaneGraph needs: where the crossing falls along each road.
            Assert.That(j.SNorthSouth, Is.EqualTo(75f).Within(0.01f));
            Assert.That(j.SEastWest, Is.EqualTo(165f).Within(0.01f));
        }

        [Test]
        public void ACurvedRoadFormsJunctionsAtAll()
        {
            // The old constructor gated on IsStraight && IsNorthSouth, so a bent road crossed
            // nothing: no junctions, and therefore no lanes and no traffic anywhere on it.
            var world = Build(Header
                + "road bend 30 20,20 20,120 60,180 140,200\n  class mainroad\n"
                + "road cross 30 0,190 239,190\n  class mainroad\n");

            Assert.That(world.Roads.Junctions.Count, Is.GreaterThanOrEqualTo(1),
                        "a curve that crosses a straight road must produce a junction");
        }

        [Test]
        public void TwoRoadsMayCrossMoreThanOnce()
        {
            // An S-bend can meet the same straight road twice. The old model held one junction
            // per pair by construction and could not represent it.
            var world = Build("village Test\nsize 300 300\nterrain path 0,0 300x300\n"
                + "road wiggle 30 40,20 40,80 160,120 40,160 40,220\n  class mainroad\n"
                + "road flat 30 0,120 299,120\n  class mainroad\n");

            Assert.That(world.Roads.Junctions.Count, Is.GreaterThanOrEqualTo(2),
                        "the S-bend crosses the straight road on the way out and on the way back");
        }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test -c Release tools/Noir.Core.Tests --filter "FullyQualifiedName~RoadNetworkTests"`
Expected: **compile error** on `j.SNorthSouth`.

- [ ] **Step 3: Rewrite `Junction` and the crossing search**

Replace the `Junction` struct in `Assets/Noir/Core/World/RoadNetwork.cs`:

```csharp
    /// <summary>Where two roads cross. The place a signal goes and a car has to wait.</summary>
    public readonly struct Junction
    {
        /// <summary>The centre of the crossing, in continuous village coordinates.</summary>
        public readonly float X, Y;

        public readonly RoadLine NorthSouth, EastWest;

        /// <summary>
        /// How far along each road the crossing falls. THIS is what LaneGraph cuts lanes at;
        /// before Phase A it inferred the position from the other road's Centre, which only
        /// works while every road is straight.
        /// </summary>
        public readonly float SNorthSouth, SEastWest;

        /// <summary>
        /// The direction each road is heading THROUGH the crossing.
        ///
        /// Recorded here for Phase B, which has to face signal heads and stop lines square to a
        /// road that may no longer be axis-aligned. Task 9 does NOT read these: a turn's
        /// direction depends on which way the segment travels, not only on which way the road
        /// was declared, so LaneGraph asks the path for the tangent and flips it for a segment
        /// running against the declaration.
        /// </summary>
        public readonly Vec2 TangentNorthSouth, TangentEastWest;

        public Junction(RoadLine ns, RoadLine ew, float sNs, float sEw, float x, float y)
        {
            NorthSouth = ns;
            EastWest = ew;
            SNorthSouth = sNs;
            SEastWest = sEw;
            X = x;
            Y = y;
            TangentNorthSouth = ns.Path.TangentAt(sNs);
            TangentEastWest = ew.Path.TangentAt(sEw);
        }

        /// <summary>
        /// How far from the centre the crossing reaches - half the WIDER corridor, because the
        /// junction tile is square and sized to the road that needs the most room.
        ///
        /// Still half the wider corridor on an oblique crossing, which is an UNDER-estimate:
        /// two 30m corridors meeting at 45 degrees overlap further than 15m along each. Left as
        /// it is deliberately - Phase A ships no oblique junction, and widening the reach here
        /// would move every existing junction's lane cuts for no present gain.
        /// </summary>
        public float Reach => NorthSouth.HalfWidth > EastWest.HalfWidth
            ? NorthSouth.HalfWidth : EastWest.HalfWidth;
    }
```

Replace the crossing search in the `RoadNetwork` constructor:

```csharp
            var crossings = new List<Junction>();
            for (int i = 0; i < Lines.Count; i++)
            {
                var ns = Lines[i];
                if (ns.Path == null || !ns.IsNorthSouth) continue;

                for (int j = 0; j < Lines.Count; j++)
                {
                    var ew = Lines[j];
                    if (ew.Path == null || ew.IsNorthSouth) continue;

                    // EVERY crossing, not one. A road that bends can meet the same straight road
                    // twice, and the old model held a single junction per pair by construction.
                    foreach (var hit in Crossings(ns.Path, ew.Path))
                        crossings.Add(new Junction(ns, ew, hit.SA, hit.SB, hit.X, hit.Y));
                }
            }
            Junctions = crossings;
```

Add this private static helper to `RoadNetwork`:

```csharp
        private readonly struct Crossing
        {
            public readonly float SA, SB, X, Y;
            public Crossing(float sa, float sb, float x, float y) { SA = sa; SB = sb; X = x; Y = y; }
        }

        /// <summary>
        /// Where two centre lines actually meet.
        ///
        /// TWO BRANCHES, AND THE SPLIT IS ABOUT COST AS MUCH AS CORRECTNESS. The obvious
        /// implementation - every dense segment of one against every dense segment of the other -
        /// is 2400 x 2100 segment pairs for a full-height road against a full-width one, times
        /// the 160-odd pairs in the real grid. That is hundreds of millions of iterations at
        /// every world build, for a map where the answer is a one-line intersection.
        ///
        /// So: two axis-aligned straights get the closed form, which is every pair in the real
        /// map and is arithmetically the (ns.Centre, ew.Centre) the old constructor asserted
        /// outright. Anything involving a curve WALKS ONE PATH AND WATCHES WHICH SIDE OF THE
        /// OTHER IT IS ON - where the signed lateral flips, the curve has crossed. That is one
        /// Project per sample rather than a nested loop, and Project on a straight road is
        /// constant time, so a curved Route 1 against the whole street grid stays linear.
        /// </summary>
        private static List<Crossing> Crossings(RoadPath a, RoadPath b)
        {
            var found = new List<Crossing>();

            if (a.IsStraightAxisAligned && b.IsStraightAxisAligned)
            {
                var a0 = a.PointAt(0f);
                var b0 = b.PointAt(0f);
                bool aVertical = a0.X == a.PointAt(a.Length).X;
                bool bVertical = b0.X == b.PointAt(b.Length).X;
                if (aVertical == bVertical) return found;              // parallel; never crosses

                float x = aVertical ? a0.X : b0.X;
                float y = aVertical ? b0.Y : a0.Y;

                // Project clamps to the ends of a run, so a crossing that falls off either road
                // comes back with a non-zero lateral. That is the test for "do they actually
                // meet" rather than "would they meet if both were infinite".
                var (sa, latA) = a.Project(new Vec2(x, y));
                var (sb, latB) = b.Project(new Vec2(x, y));
                if (latA > 0.001f || latA < -0.001f) return found;
                if (latB > 0.001f || latB < -0.001f) return found;

                found.Add(new Crossing(sa, sb, x, y));
                return found;
            }

            float pitch = RoadPath.ResamplePitch;
            var previous = a.PointAt(0f);
            var (_, previousLateral) = b.Project(previous);

            for (float s = pitch; s <= a.Length; s += pitch)
            {
                var here = a.PointAt(s);
                var (_, lateral) = b.Project(here);

                if ((previousLateral < 0f) != (lateral < 0f))
                {
                    float t = previousLateral / (previousLateral - lateral);
                    if (t >= 0f && t <= 1f)
                    {
                        float sa = s - pitch + pitch * t;
                        var hit = a.PointAt(sa);
                        var (sb, _) = b.Project(hit);

                        // Beyond B's end, Project clamps and the lateral is measured to the end
                        // point - which can flip sign for a road that merely passes the end
                        // without touching it. Only strictly inside the run is a crossing.
                        if (sb > 0.001f && sb < b.Length - 0.001f)
                        {
                            bool duplicate = false;
                            foreach (var seen in found)
                                if ((seen.SA - sa) * (seen.SA - sa) < 1f) { duplicate = true; break; }
                            if (!duplicate) found.Add(new Crossing(sa, sb, hit.X, hit.Y));
                        }
                    }
                }

                previous = here;
                previousLateral = lateral;
            }
            return found;
        }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test -c Release tools/Noir.Core.Tests --filter "FullyQualifiedName~RoadNetworkTests"`
Expected: all pass.

- [ ] **Step 5: Confirm the real city's junction count is unchanged**

Run: `dotnet test -c Release tools/Noir.Core.Tests --filter "FullyQualifiedName~RoadGeometryBaselineTests"`
Expected: **all pass**, including the baked-in junction count from Task 2. This is the moment the rewrite could silently double-count crossings on the real map; the baseline is what catches it.

- [ ] **Step 6: Commit**

```bash
git add Assets/Noir/Core/World/RoadNetwork.cs tools/Noir.Core.Tests/RoadNetworkTests.cs
git commit -m "Junctions are where the roads actually cross

Junction was (ns.Centre, ew.Centre) - the only crossing two axis-aligned lines
can have - behind a gate that skipped any road that bends. A curved road formed
zero junctions with anything, and therefore had no lanes and no traffic.

It is a real intersection now, carrying the arc length along each road and the
tangent through the crossing, so LaneGraph can cut lanes without inferring a
position from the other road's centre. Two roads may cross more than once,
which an S-bend does and the old one-per-pair model could not represent.

The real map's junction count is unchanged, which is what the baseline recorded
before any of this."
```

---

### Task 9: `LaneGraph` in arc length

**Files:**
- Modify: `Assets/Noir/Core/World/LaneGraph.cs`
- Modify: `tools/Noir.Core.Tests/LaneGraphTests.cs`

**Interfaces:**
- Consumes: `Junction.SNorthSouth`, `Junction.SEastWest`, `Junction.TangentNorthSouth`, `Junction.TangentEastWest` from Task 8; `RoadLine.Path`.
- Produces: `LaneGraph.Segments`, `Turns`, `Entries`, `TurnsFrom` all keep their shapes. `LaneSegment.FromS`/`ToS` are now arc length along the path, direction-signed as before.

- [ ] **Step 1: Write the failing tests**

Append inside `LaneGraphTests`:

```csharp
        [Test]
        public void ACurvedRoadGetsLanes()
        {
            // `if (!line.IsStraight) continue` meant a bent road got no segments at all, so no
            // car could ever be placed on it. This is the line Phase A exists to delete.
            var graph = Build(Header
                + "road bend 30 20,20 20,120 60,180 140,200\n  class mainroad\n"
                + "road cross 30 0,190 239,190\n  class mainroad\n", out var world);

            var bend = world.Roads.Lines[0];
            Assert.That(bend.IsStraight, Is.False, "the fixture is meant to bend");

            int onBend = 0;
            foreach (var seg in graph.Segments) if (seg.Line == 0) onBend++;
            Assert.That(onBend, Is.GreaterThan(0), "a curved road must carry lanes");
        }

        [Test]
        public void NoSegmentAnywhereHasNegativeOrZeroLength()
        {
            var graph = Build(Header
                + "road bend 30 20,20 20,120 60,180 140,200\n  class mainroad\n"
                + "road cross 30 0,190 239,190\n  class mainroad\n", out _);

            foreach (var seg in graph.Segments)
                Assert.That(seg.Length, Is.GreaterThan(0f),
                            "segment " + seg.Index + " on line " + seg.Line + " has no length");
        }

        [Test]
        public void EveryArrivalStillHasALegalWayOut()
        {
            // The invariant that mattered most before Phase A and still does: a car reaching a
            // junction must have somewhere to go, or it vanishes or wedges.
            var graph = Build(Header + Grid, out _);
            foreach (var seg in graph.Segments)
            {
                if (seg.IsExit) continue;
                Assert.That(graph.TurnsFrom(seg.Index).Count, Is.GreaterThan(0),
                            "segment " + seg.Index + " arrives at a junction with no way out");
            }
        }

        [Test]
        public void TurnsAreStillClassifiedTheWayTheEnumClassifiedThem()
        {
            // Tangent-based classification must agree with Headings.Between on axis-aligned
            // roads, or every existing signal phase and give-way rule changes meaning.
            var graph = Build(Header + Grid, out _);
            foreach (var turn in graph.Turns)
            {
                var from = graph.Segments[turn.From];
                var to = graph.Segments[turn.To];
                var expected = Headings.Between(from.Way, to.Way);
                Assert.That(expected, Is.Not.Null, "a U-turn was offered");
                Assert.That(turn.Kind, Is.EqualTo(expected.Value),
                            "turn " + from.Way + "->" + to.Way + " classified differently");
            }
        }

        /// <summary>
        /// The real Illinois Route 1, as surveyed, on a map the size of the real one.
        ///
        /// These twelve points are the OSM centre line (way 22037977, ref IL 1) projected into
        /// village metres and rotated into the parcels' frame - the curve that runs down the
        /// corridor the county's own lots leave for it, where the straight x=750 we ship today
        /// puts 85% of its length inside somebody's back garden.
        ///
        /// It is a FIXTURE, not content. Phase A ships no curve in Content/city.txt, because the
        /// renderers still skip anything where !IsStraight and a road with traffic and no asphalt
        /// under it is worse than the straight one. This proves the geometry works so that Phase C
        /// can lay it down once Phase B has migrated the consumers.
        /// </summary>
        private const string RealRoute1 =
            "road route1 30 371,177 466,470 593,855 675,1109 747,1332 776,1423 "
          + "799,1491 839,1607 857,1687 863,1740 872,1876 881,2049\n  class mainroad\n";

        [Test]
        public void TheRealRoute1ProducesASaneLaneGraph()
        {
            var graph = Build(
                "village Rossville\nsize 2100 2400\nterrain path 0,0 2100x2400\n"
                + RealRoute1
                + "road attica 30 0,1335 2099,1335\n  class mainroad\n"
                + "road benton 10 496,1113 1530,1113\n  class street\n", out var world);

            var route1 = world.Roads.Lines[0];
            Assert.That(route1.IsStraight, Is.False, "the real Route 1 bends");
            Assert.That(route1.Path.Length, Is.GreaterThan(1900f), "it spans most of the map");

            Assert.That(world.Roads.Junctions.Count, Is.GreaterThanOrEqualTo(2),
                        "Route 1 crosses both Attica and Benton");

            int onRoute1 = 0;
            foreach (var seg in graph.Segments)
            {
                Assert.That(seg.Length, Is.GreaterThan(0f), "segment " + seg.Index + " has no length");
                if (seg.Line == 0) onRoute1++;
            }
            Assert.That(onRoute1, Is.GreaterThan(0), "the real curve carries lanes");

            foreach (var seg in graph.Segments)
                if (!seg.IsExit)
                    Assert.That(graph.TurnsFrom(seg.Index).Count, Is.GreaterThan(0),
                                "segment " + seg.Index + " arrives somewhere with no way out");
        }

        [Test]
        public void EveryLaneOnTheRealRoute1StaysWithinItsOwnCorridor()
        {
            // The claim that matters for Phase B: a lane offset from a curved centre line is
            // still ON the road. Measured perpendicular to the curve, which is the only
            // meaningful way to ask it once the road stops being axis-aligned.
            Build("village Rossville\nsize 2100 2400\nterrain path 0,0 2100x2400\n"
                  + RealRoute1
                  + "road attica 30 0,1335 2099,1335\n  class mainroad\n", out var world);

            var path = world.Roads.Lines[0].Path;
            float halfWidth = world.Roads.Lines[0].HalfWidth;

            for (float s = 0f; s <= path.Length; s += 25f)
            {
                var lane = path.PointAt(s) + path.NormalAt(s) * (halfWidth - 2f);
                var (_, lateral) = path.Project(lane);
                Assert.That(lateral, Is.EqualTo(halfWidth - 2f).Within(1.5f),
                            "a lane 2m inside the kerb reads as somewhere else at s=" + s);
            }
        }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test -c Release tools/Noir.Core.Tests --filter "FullyQualifiedName~LaneGraphTests"`
Expected: `ACurvedRoadGetsLanes` **FAILS** with 0 segments on the bend.

- [ ] **Step 3: Rewrite the lane cutting**

In `Assets/Noir/Core/World/LaneGraph.cs`, in the constructor, delete `if (!line.IsStraight) continue;` and replace the stop-gathering and span calculation. Replace this block:

```csharp
                var stops = new List<(float along, float reach, int index)>();
                for (int j = 0; j < roads.Junctions.Count; j++)
                {
                    var junction = roads.Junctions[j];
                    float cross = line.IsNorthSouth ? junction.X : junction.Y;
                    if (Math.Abs(cross - line.Centre) > 0.5f) continue;   // not on this road
                    stops.Add((line.IsNorthSouth ? junction.Y : junction.X, junction.Reach, j));
                }
```

with:

```csharp
                // ARC LENGTH, off the junction itself, rather than reading the crossing's village
                // coordinate back through this road's own Centre. The old way could only work
                // while every road was a constant cross-coordinate.
                var stops = new List<(float along, float reach, int index)>();
                for (int j = 0; j < roads.Junctions.Count; j++)
                {
                    var junction = roads.Junctions[j];
                    float s;
                    if (ReferenceEquals(junction.NorthSouth, line)) s = junction.SNorthSouth;
                    else if (ReferenceEquals(junction.EastWest, line)) s = junction.SEastWest;
                    else continue;                                        // not on this road

                    // From + s, deliberately, rather than raw arc length. For a straight road the
                    // two are identical and FromS/ToS keep the exact values they have today,
                    // which is what the recorded baseline pins. For a curve it is a convenience:
                    // arc length measured from the path start, offset onto the road's declared
                    // axis. Nothing carries traffic on a curve until Phase C, and Phase B should
                    // settle whether segments want a true arc-length origin before it does.
                    stops.Add((line.From + s, junction.Reach, j));
                }
```

Then replace the span calculation, which reads `line.From`/`line.To` against the map edge:

```csharp
                    float span = line.IsNorthSouth ? height : width;
                    float low = line.From <= 0.01f ? line.From - margin : line.From;
                    float high = line.To >= span - 0.01f ? line.To + margin : line.To;
```

with:

```csharp
                    // Unchanged in meaning: the margin is for leaving the MAP, not for leaving
                    // the road, so a farm track that stops at a junction does not send a van out
                    // into a field. Expressed against the path's own extent so it reads the same
                    // for a curve.
                    float span = line.IsNorthSouth ? height : width;
                    float pathEnd = line.From + line.Path.Length;
                    float low = line.From <= 0.01f ? line.From - margin : line.From;
                    float high = pathEnd >= span - 0.01f ? pathEnd + margin : pathEnd;
```

- [ ] **Step 4: Classify turns from the tangents**

Still in the constructor, replace the turn-legality block's `var kind = Headings.Between(into.Way, onward.Way);` with:

```csharp
                    // FROM THE TANGENTS, not the enum, so an oblique crossing classifies too.
                    // For axis-aligned roads this yields precisely what Headings.Between yields:
                    // the cross product's sign is which way the wheel turns, and the dot tells a
                    // straight-on from a U-turn. No angle is taken - see CoreDeterminismTests.
                    var tIn = roads.Lines[into.Line].Path.TangentAt(
                        AlongOf(into.Way, into.ToS) - roads.Lines[into.Line].From);
                    var tOut = roads.Lines[onward.Line].Path.TangentAt(
                        AlongOf(onward.Way, onward.FromS) - roads.Lines[onward.Line].From);

                    // The path's tangent always points the way the road was DECLARED; a segment
                    // running the other way travels against it.
                    if (!Headings.Increasing(into.Way)) tIn = new Vec2(-tIn.X, -tIn.Y);
                    if (!Headings.Increasing(onward.Way)) tOut = new Vec2(-tOut.X, -tOut.Y);

                    float dot = tIn.X * tOut.X + tIn.Y * tOut.Y;
                    float cross = tIn.X * tOut.Y - tIn.Y * tOut.X;

                    TurnKind? kind;
                    if (dot <= -0.5f) kind = null;                       // a U-turn; never offered
                    else if (cross > 0.3f) kind = TurnKind.Right;        // right is (-y, x): +cross
                    else if (cross < -0.3f) kind = TurnKind.Left;
                    else kind = TurnKind.Straight;
```

Add `using Noir.Core.Contracts;` to the file if it is not already present.

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test -c Release tools/Noir.Core.Tests --filter "FullyQualifiedName~LaneGraphTests"`
Expected: all pass, including the 14 pre-existing `LaneGraphTests`.

If `TurnsAreStillClassifiedTheWayTheEnumClassifiedThem` fails on left/right specifically, the cross-product sign is inverted — check it against `Headings.Side`, which derives the right of travel `(dx,dy)` as `(-dy,dx)`. Do not "fix" it by flipping the comparison without confirming which way round the fixture's roads run.

- [ ] **Step 6: Confirm the baseline is still exactly what Task 2 recorded**

Run: `dotnet test -c Release tools/Noir.Core.Tests`
Expected: the full Core suite, only the two by-design failures. **The baked-in segment, turn, entry and junction counts must be untouched** — that is the whole zero-regression claim, now covering every rewritten path.

- [ ] **Step 7: Commit**

```bash
git add Assets/Noir/Core/World/LaneGraph.cs tools/Noir.Core.Tests/LaneGraphTests.cs
git commit -m "Lanes are cut in arc length, and turns come from the tangents

LaneGraph opened its per-road loop with 'if (!line.IsStraight) continue', so a
bent road got no segments and no car could ever be placed on it. It cuts at the
junction's own arc length now instead of reading the crossing's coordinate back
through this road's Centre, which only worked while every road was a constant
cross-coordinate.

Turn legality is the sign of a cross product and a dot, not the four-value enum,
so an oblique crossing classifies too. For axis-aligned roads it agrees with
Headings.Between exactly, and there is a test asserting that over every turn in
the fixture city rather than trusting the algebra.

Segment, turn, entry and junction counts for the real map are all unchanged
against the baseline recorded before any of this started."
```

---

## Done when

- `dotnet test -c Release tools/Noir.Core.Tests` shows only the two by-design failures.
- The baseline counts recorded in Task 2 are still asserted and still passing.
- `docs/snapshots/rail-*.png` are byte-identical to their committed versions.
- `Content/city.txt` is **unmodified** — Phase A ships no curve.
- Unity compiles clean headlessly.

Phase B migrates the 13 consumers off `Centre`/`IsNorthSouth`. Phase C rebuilds the map data. Neither belongs in this plan.
