# Multiple Businesses Per Terrace Lot Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a `footprint later` downtown lot (currently just 112 S Chicago / parcel 237) carry several independently-ruled businesses instead of exactly one, by making `DowntownFromSanborn` emit one simulated Place per storefront instead of one Place for the whole row.

**Architecture:** `CommercialRow.Lay()` (Core) already computes the individual storefronts (offset, width, storeys, construction) correctly — nothing there changes. `DowntownFromSanborn.Apply` (Unity) currently folds that array into one `PlaceSpec`; it changes to emit one `PlaceSpec` per storefront, contiguous, each with a stable address-based handle. `BusinessFromRulings`/`business-1991.txt`/the in-game panel already key on a Place's name and need no format changes — they just get more names to work with. A new Core-testable naming helper (`CommercialRow.HandleFor`) and a new Core-testable orphan-ruling check (`BusinessRulings.Unmatched`) are added; the two Unity-layer edits that consume them (`DowntownFromSanborn.Apply`, `BusinessFromRulings.Apply`) are Unity-layer code with no `dotnet test` coverage, verified instead by `dotnet build` and the PlayMode gate, matching this codebase's existing Core/Unity test split.

**Tech Stack:** C# / .NET 9 (Core, `dotnet test`), Unity 6000.3.20f1 (Unity + PlayMode, NUnit via Unity Test Runner).

## Global Constraints

- No `UnityEngine` reference is available to anything under `Assets/Noir/Core` — the `Noir.Core.csproj` gate enforces this at compile time. Both new pure-logic additions in this plan (`CommercialRow.HandleFor`, `BusinessRulings.Unmatched`) must compile there.
- Everything routes through `IRng`/`Content` (the seam), never `System.Random` or `Application.dataPath` — not touched by this plan, but do not introduce either while editing these files.
- `Content/business-1991.txt` and `Content/parcel-1991.txt` are hand-authored, owner-facing files. Nothing in this plan writes to them — the format is unchanged and the owner rules new storefronts through the in-game panel after this ships, the same way "301 W Benton Ave" was ruled.
- Core suite baseline before this work starts: confirm with `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj` and expect the count recorded in `CLAUDE.md` (486 pass, 0 fail, 8 skipped, as of 2026-08-11) before adding anything. Any pre-existing red is not this plan's to fix.

---

### Task 1: `CommercialRow.HandleFor` — the storefront naming rule

**Files:**
- Modify: `Assets/Noir/Core/World/CommercialRow.cs`
- Test: `tools/Noir.Core.Tests/CommercialRowTests.cs`

**Interfaces:**
- Produces: `public static string CommercialRow.HandleFor(string address, int parcelId, int index)` — `index` is 1-based. Returns `"{address} #{index}"` when `address` is non-null/non-empty, else `"parcel {parcelId} unit {index}"`. Consumed by Task 3.

- [ ] **Step 1: Write the failing tests**

Add to `tools/Noir.Core.Tests/CommercialRowTests.cs`, inside the `CommercialRowTests` class, after `SnapPicksTheNearestSurveyedFrontage`:

```csharp
        [Test]
        public void HandleUsesTheAddressWhenThereIsOne()
        {
            Assert.That(CommercialRow.HandleFor("112 S Chicago", 237, 1),
                        Is.EqualTo("112 S Chicago #1"));
            Assert.That(CommercialRow.HandleFor("112 S Chicago", 237, 3),
                        Is.EqualTo("112 S Chicago #3"));
        }

        [Test]
        public void HandleFallsBackToTheParcelIdWithNoAddress()
        {
            Assert.That(CommercialRow.HandleFor(null, 501, 2), Is.EqualTo("parcel 501 unit 2"));
            Assert.That(CommercialRow.HandleFor("", 501, 2), Is.EqualTo("parcel 501 unit 2"));
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~CommercialRowTests.HandleUsesTheAddressWhenThereIsOne|FullyQualifiedName~CommercialRowTests.HandleFallsBackToTheParcelIdWithNoAddress"`

Expected: build error — `CommercialRow` has no method `HandleFor`.

- [ ] **Step 3: Add the method**

In `Assets/Noir/Core/World/CommercialRow.cs`, add after `Snap` (before the closing brace of the class, after the `private static float Abs` helper is fine too — place it directly after `Snap`):

```csharp
        /// <summary>
        /// The handle a generated storefront is known by before anyone has ruled a business onto
        /// it — what `BusinessRulings`/`business-1991.txt` and the in-game panel key on.
        /// Address-based so it reads the way the owner already writes rulings by hand
        /// ("112 S Chicago #1"), falling back to the parcel id for a footprint-later lot with no
        /// resolvable street address. 1-based index, left to right from the crossing.
        /// </summary>
        public static string HandleFor(string address, int parcelId, int index) =>
            string.IsNullOrEmpty(address)
                ? $"parcel {parcelId} unit {index}"
                : $"{address} #{index}";
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~CommercialRowTests.HandleUsesTheAddressWhenThereIsOne|FullyQualifiedName~CommercialRowTests.HandleFallsBackToTheParcelIdWithNoAddress"`

Expected: PASS, 2 of 2.

- [ ] **Step 5: Run the full Core suite to confirm nothing else moved**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj`

Expected: same pass count as the Global Constraints baseline, plus 2.

- [ ] **Step 6: Commit**

```bash
git add Assets/Noir/Core/World/CommercialRow.cs tools/Noir.Core.Tests/CommercialRowTests.cs
git commit -m "CommercialRow gets a naming rule for storefronts that don't exist yet"
```

---

### Task 2: `BusinessRulings.Unmatched` — catching a ruling that points at nothing

**Files:**
- Modify: `Assets/Noir/Core/Survey/BusinessRulings.cs`
- Test: `tools/Noir.Core.Tests/BusinessRulingsTests.cs` (new file)

**Interfaces:**
- Consumes: `BusinessRulings.All` (existing, `IReadOnlyDictionary<string, Ruling>`), `Content`/`IContentSource` from `Noir.Core.Contracts` (existing seam, same pattern as `RulingsTests.cs`).
- Produces: `public static IReadOnlyList<string> BusinessRulings.Unmatched(IEnumerable<string> placeNames)` — every key in `business-1991.txt` that is not present in `placeNames`, sorted ordinally. Consumed by Task 4.

- [ ] **Step 1: Write the failing tests**

Create `tools/Noir.Core.Tests/BusinessRulingsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Survey;

namespace Noir.Core.Tests
{
    /// <summary>
    /// A ruling in business-1991.txt is keyed on a Place's handle, and a terrace lot now hands
    /// out several of those instead of one. If the frontage or the RNG sequencing upstream of
    /// DowntownFromSanborn ever shifts, an old ruling can point at a storefront that no longer
    /// exists — silently, unless something says so. This is that something.
    /// </summary>
    [TestFixture]
    public class BusinessRulingsTests
    {
        /// <summary>Same double RulingsTests.cs uses: a content source made of strings, with a
        /// fresh timestamp per instance so a new Given() always forces a reparse.</summary>
        private sealed class Fake : IContentSource
        {
            private readonly Dictionary<string, string> _files = new Dictionary<string, string>();
            private static int _tick;
            private readonly DateTime _stamp = new DateTime(2026, 8, 12).AddSeconds(++_tick);

            public Fake Put(string name, string text) { _files[name] = text; return this; }
            public string Read(string name) =>
                _files.TryGetValue(name, out var t) ? t : throw new System.IO.FileNotFoundException(name);
            public DateTime WrittenAt(string name) => _files.ContainsKey(name) ? _stamp : default;
        }

        private static void Given(string body) =>
            Content.Install(new Fake().Put(BusinessRulings.FileName, body));

        [Test]
        public void ARuledUnitThatMatchesAPlaceIsNotUnmatched()
        {
            Given("unit \"112 S Chicago #1\" kind shop\n"
                + "unit \"112 S Chicago #1\" business \"Ryan's Antiques\"\n");

            var unmatched = BusinessRulings.Unmatched(
                new[] { "112 S Chicago #1", "112 S Chicago #2" });

            Assert.That(unmatched, Is.Empty);
        }

        [Test]
        public void ARuledUnitThatMatchesNoPlaceIsReportedUnmatched()
        {
            Given("unit \"112 S Chicago #3\" business \"The Old Diner\"\n");

            var unmatched = BusinessRulings.Unmatched(
                new[] { "112 S Chicago #1", "112 S Chicago #2" });

            Assert.That(unmatched, Is.EquivalentTo(new[] { "112 S Chicago #3" }));
        }

        [Test]
        public void NoRulingsMeansNothingUnmatched()
        {
            Given("");

            var unmatched = BusinessRulings.Unmatched(new[] { "112 S Chicago #1" });

            Assert.That(unmatched, Is.Empty);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~BusinessRulingsTests"`

Expected: build error — `BusinessRulings` has no method `Unmatched`.

- [ ] **Step 3: Add the method**

In `Assets/Noir/Core/Survey/BusinessRulings.cs`, add after `All` (before `For`):

```csharp
        /// <summary>
        /// Ruled units that matched none of the Places actually built this run — the handle
        /// drifted, or the lot's layout changed underneath it. A terrace's storefronts are
        /// numbered in order (see DowntownFromSanborn), so a shifted frontage or a changed RNG
        /// sequence silently points an old ruling at the wrong door, or at nothing at all.
        /// </summary>
        public static IReadOnlyList<string> Unmatched(IEnumerable<string> placeNames)
        {
            Load();
            var present = new HashSet<string>(placeNames);
            var missing = new List<string>();
            foreach (var unit in _byUnit.Keys)
                if (!present.Contains(unit)) missing.Add(unit);
            missing.Sort(System.StringComparer.Ordinal);
            return missing;
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~BusinessRulingsTests"`

Expected: PASS, 3 of 3.

- [ ] **Step 5: Run the full Core suite to confirm nothing else moved**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj`

Expected: same pass count as Task 1's Step 5, plus 3.

- [ ] **Step 6: Commit**

```bash
git add Assets/Noir/Core/Survey/BusinessRulings.cs tools/Noir.Core.Tests/BusinessRulingsTests.cs
git commit -m "BusinessRulings can say when a ruling points at nothing this build"
```

---

### Task 3: `DowntownFromSanborn.Apply` — one Place per storefront

**Files:**
- Modify: `Assets/Noir/Unity/DowntownFromSanborn.cs`

**Interfaces:**
- Consumes: `CommercialRow.HandleFor(string, int, int)` (Task 1), `CountyRecord.For(int).Address` (existing, `Assets/Noir/Unity/CountyRecord.cs`, same `Noir.Unity` namespace so no new `using`), `CommercialRow.Lay(float, IRng)` → `Storefront[]` (existing, unchanged).
- Produces: `DowntownFromSanborn.Apply(VillageLayout, IRng)` now adds one `PlaceSpec` per `Storefront` to `layout.Places` instead of one per lot. Each spec's `Name` is `CommercialRow.HandleFor(address, lot.Id, index)`. Consumed by `BusinessFromRulings.Apply` (Task 4, unchanged call site — it already iterates `layout.Places`) and by the PlayMode test in Task 5.

No Core test is possible for this file — it is `Assets/Noir/Unity`, which `dotnet test` cannot compile (`UnityEngine.Vector2`, `UnityEngine.Debug`, `Mathf`). Verified by `dotnet build` (compiles) and the PlayMode test in Task 5 (behavior).

- [ ] **Step 1: Update the class doc comment**

In `Assets/Noir/Unity/DowntownFromSanborn.cs`, the class doc (top of file) currently ends:

```csharp
    /// ONE BUILDING, SEVERAL NARROW SHOPS. The owner, on 112 South Chicago: "they were several
    /// narrow shops... but in the same building". That is what a terrace is and what this lays -
    /// a single continuous structure along the frontage, subdivided by party walls, every unit
    /// square to the pavement. Not a row of detached boxes with gaps between them.
    /// </summary>
```

Replace the last sentence so it describes the real mechanism rather than the old one:

```csharp
    /// ONE BUILDING, SEVERAL NARROW SHOPS. The owner, on 112 South Chicago: "they were several
    /// narrow shops... but in the same building". That is what a terrace is and what this lays -
    /// a continuous run along the frontage, subdivided by party walls, every unit square to the
    /// pavement. Not a row of detached boxes with gaps between them - each storefront is its own
    /// Place so it can carry its own business, but they are laid edge to edge with zero gap and
    /// left to the massing grammars to render as one row, the same way the 41 hand-placed
    /// downtown units already do.
    /// </summary>
```

- [ ] **Step 2: Replace the single-Place block with a per-storefront loop**

Inside `Apply`, find this block (the comment header through the end of the `foreach (var lot in ...)` loop body):

```csharp
                // ONE building, its outline running the whole terrace. The units are its rooms and
                // WorldBuilder divides them; what this decides is the fabric - where the wall is,
                // how far back it runs, and where the party walls fall.
                var corners = new List<Tile>();
                var a0 = front.Start;
                var a1 = front.Start + alongDir * front.Length;
                var b1 = a1 + backDir * DepthMetres;
                var b0 = a0 + backDir * DepthMetres;
                corners.Add(ToTile(a0));
                corners.Add(ToTile(a1));
                corners.Add(ToTile(b1));
                corners.Add(ToTile(b0));
                corners.Add(ToTile(a0));               // closed

                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                foreach (var t in corners)
                {
                    if (t.X < minX) minX = t.X; if (t.X > maxX) maxX = t.X;
                    if (t.Y < minY) minY = t.Y; if (t.Y > maxY) maxY = t.Y;
                }
                int w = maxX - minX, h = maxY - minY;
                if (w < 3 || h < 3) continue;

                var spec = new PlaceSpec
                {
                    Kind = PlaceKind.Shop,
                    Bounds = new TileRect(minX, minY, w, h),
                    Outline = corners.ToArray(),
                    Name = $"the {front.Street} terrace",
                };
                add.Add(spec);
                rows++;
                units += laid.Length;
```

Replace it with:

```csharp
                // EACH STOREFRONT ITS OWN PLACE, laid edge to edge along the frontage with zero
                // gap - the fabric is exactly what it was when this was one merged box, just cut
                // at the same seams CommercialRow already computed. Splitting it is what lets each
                // one carry its own business, kind, jobs and hours instead of all of them sharing
                // whatever the single ruling on the row said.
                string address = CountyRecord.For(lot.Id)?.Address;
                int index = 0;
                int laidHere = 0;

                foreach (var unit in laid)
                {
                    index++;
                    var corners = new List<Tile>();
                    var a0 = front.Start + alongDir * unit.Offset;
                    var a1 = front.Start + alongDir * unit.End;
                    var b1 = a1 + backDir * DepthMetres;
                    var b0 = a0 + backDir * DepthMetres;
                    corners.Add(ToTile(a0));
                    corners.Add(ToTile(a1));
                    corners.Add(ToTile(b1));
                    corners.Add(ToTile(b0));
                    corners.Add(ToTile(a0));           // closed

                    int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                    foreach (var t in corners)
                    {
                        if (t.X < minX) minX = t.X; if (t.X > maxX) maxX = t.X;
                        if (t.Y < minY) minY = t.Y; if (t.Y > maxY) maxY = t.Y;
                    }
                    int w = maxX - minX, h = maxY - minY;
                    if (w < 3 || h < 3) continue;

                    var spec = new PlaceSpec
                    {
                        Kind = PlaceKind.Shop,
                        Bounds = new TileRect(minX, minY, w, h),
                        Outline = corners.ToArray(),
                        Name = CommercialRow.HandleFor(address, lot.Id, index),
                    };
                    add.Add(spec);
                    laidHere++;
                }

                if (laidHere == 0) continue;
                rows++;
                units += laidHere;
```

- [ ] **Step 3: Update the summary log line**

Find:

```csharp
            if (rows > 0)
                Debug.Log($"[survey] {rows} downtown terrace(s) laid from the 1913 survey - "
                        + $"{units} shop units in {rows} building(s), replacing "
                        + $"{drop.Count} raised from post-2000 sources.");
```

Replace `"{units} shop units in {rows} building(s), replacing "` with wording that no longer claims one Place per row:

```csharp
            if (rows > 0)
                Debug.Log($"[survey] {rows} downtown terrace(s) laid from the 1913 survey - "
                        + $"{units} independently-rulable shop units across them, replacing "
                        + $"{drop.Count} raised from post-2000 sources.");
```

- [ ] **Step 4: Verify it compiles**

Close the Unity editor first if it is open (per `CLAUDE.md`'s standing precondition for any `Unity.exe -batchmode` command).

Run: `dotnet build Noir.Unity.csproj -c Debug`

Expected: build succeeds, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Unity/DowntownFromSanborn.cs
git commit -m "DowntownFromSanborn lays one Place per storefront, not one for the whole row"
```

---

### Task 4: `BusinessFromRulings.Apply` — report rulings that matched nothing

**Files:**
- Modify: `Assets/Noir/Unity/BusinessFromRulings.cs`

**Interfaces:**
- Consumes: `BusinessRulings.Unmatched(IEnumerable<string>)` (Task 2).
- Produces: `BusinessFromRulings.Apply(VillageLayout)` return type and existing behavior unchanged; the `[business]` log line gains a reported "matched nothing" count and list.

No Core test is possible for this file (same reason as Task 3). Verified by `dotnet build` and by reading the PlayMode log in Task 5/6.

- [ ] **Step 1: Capture original names before any renaming, and check for unmatched rulings**

In `Assets/Noir/Unity/BusinessFromRulings.cs`, `Apply` currently starts:

```csharp
        public static int Apply(VillageLayout layout)
        {
            _handles.Clear();
            if (layout == null) return 0;

            var table = PlaceKindTable.Current;
            int named = 0, retyped = 0, unknown = 0;

            for (int i = 0; i < layout.Places.Count; i++)
            {
                var spec = layout.Places[i];
                var ruling = BusinessRulings.For(spec.Name);
                if (ruling == null) continue;
```

Replace with (the `for` loop body below this point is unchanged — only the lines before it change, adding an `originalNames` snapshot and switching the lookup to read from it):

```csharp
        public static int Apply(VillageLayout layout)
        {
            _handles.Clear();
            if (layout == null) return 0;

            var table = PlaceKindTable.Current;
            int named = 0, retyped = 0, unknown = 0;

            // A SNAPSHOT, TAKEN BEFORE ANY RENAMING. A ruled place's Name is about to become its
            // business name - "Rossville unit 12" becomes "Shorty's" a few lines down - so asking
            // BusinessRulings.Unmatched afterwards would see every ruled handle as if it had
            // matched nothing. Captured once, up front, so it always reflects the handles this
            // build actually produced.
            var originalNames = new List<string>(layout.Places.Count);
            for (int i = 0; i < layout.Places.Count; i++)
                originalNames.Add(layout.Places[i].Name);

            for (int i = 0; i < layout.Places.Count; i++)
            {
                var spec = layout.Places[i];
                var ruling = BusinessRulings.For(originalNames[i]);
                if (ruling == null) continue;
```

- [ ] **Step 2: Report unmatched rulings in the summary log**

Find the closing log line of `Apply`:

```csharp
            // Greppable, and it prints even at zero - the whole point of this file is that the
            // owner can write a ruling the game never reads, and a silent pass is how that
            // happens. Same reasoning as [lots], [walks] and [roads].
            Debug.Log($"[business] {BusinessRulings.Count} unit(s) ruled in "
                    + $"{BusinessRulings.FileName}: {named} named, {retyped} re-typed"
                    + (unknown > 0 ? $", {unknown} WITH A KIND NOTHING ANSWERS TO" : "")
                    + ".");

            return named + retyped;
```

Replace with:

```csharp
            var unmatched = BusinessRulings.Unmatched(originalNames);

            // Greppable, and it prints even at zero - the whole point of this file is that the
            // owner can write a ruling the game never reads, and a silent pass is how that
            // happens. Same reasoning as [lots], [walks] and [roads].
            Debug.Log($"[business] {BusinessRulings.Count} unit(s) ruled in "
                    + $"{BusinessRulings.FileName}: {named} named, {retyped} re-typed"
                    + (unknown > 0 ? $", {unknown} WITH A KIND NOTHING ANSWERS TO" : "")
                    + (unmatched.Count > 0
                        ? $", {unmatched.Count} MATCHED NOTHING THIS BUILD "
                          + $"({string.Join(", ", unmatched)})"
                        : "")
                    + ".");

            return named + retyped;
```

- [ ] **Step 3: Verify it compiles**

Close the Unity editor first if it is open.

Run: `dotnet build Noir.Unity.csproj -c Debug`

Expected: build succeeds, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Noir/Unity/BusinessFromRulings.cs
git commit -m "BusinessFromRulings says out loud when a ruling matched nothing this build"
```

---

### Task 5: PlayMode test — the built town actually has several storefronts

**Files:**
- Modify: `Assets/Noir/PlayTests/TownGeometryPlayTests.cs`

**Interfaces:**
- Consumes: `CityUnderTest.WaitUntilBuilt()`, `CityUnderTest.World` (existing, used by every other test in this file), `world.AllPlaces` (existing, `Noir.Core.World.WorldModel`), `BusinessFromRulings.HandleOf(string)` (existing, `Assets/Noir/Unity/BusinessFromRulings.cs`) — used rather than reading `place.Name` directly, because a storefront the owner has since ruled through the panel has had its `Name` replaced with the business name; `HandleOf` maps back to the original handle regardless.

- [ ] **Step 1: Add the test**

In `Assets/Noir/PlayTests/TownGeometryPlayTests.cs`, add inside the `TownGeometryPlayTests` class, after `EveryRoadIsDrawable` (or any other existing `[UnityTest]` — placement within the class doesn't matter, this project's convention keeps them ungrouped by category, only by the `[UnityTest]` attribute):

```csharp
        /// <summary>
        /// 112 S Chicago (parcel 237) is ruled `footprint later` with a note naming several
        /// narrow shops and a restaurant. Before this feature it built as exactly one Place named
        /// "the Chicago Street terrace" - one name, one business, no matter how many trades the
        /// note said stood there. This asserts the row is now several independently-named Places.
        ///
        /// Reads through BusinessFromRulings.HandleOf rather than Place.Name directly: once the
        /// owner rules a storefront through the panel its Name becomes the business name, and a
        /// test matching on the handle text would start under-counting the moment that happens -
        /// which is the normal, intended outcome of ruling it, not a regression.
        /// </summary>
        [UnityTest]
        public IEnumerator ATerraceLotProducesMoreThanOneIndependentlyNamedStorefront()
        {
            yield return CityUnderTest.WaitUntilBuilt();
            var world = CityUnderTest.World;
            Assert.That(world, Is.Not.Null, "the town did not build");

            var units = new List<Place>();
            foreach (var place in world.AllPlaces)
            {
                var handle = BusinessFromRulings.HandleOf(place.Name);
                if (handle.StartsWith("112 S Chicago #")) units.Add(place);
            }

            Debug.Log($"[geometry] 112 S Chicago: {units.Count} independently-named storefront(s)");

            Assert.That(units.Count, Is.GreaterThan(1),
                "112 S Chicago is ruled footprint later with several narrow shops named in its "
              + "own note - it should not still be one Place wearing one name.");

            foreach (var u in units)
            {
                Assert.That(u.Bounds.W, Is.GreaterThan(0), $"{u.Name} has zero width");
                Assert.That(u.Bounds.H, Is.GreaterThan(0), $"{u.Name} has zero depth");
            }
        }
```

- [ ] **Step 2: Verify the PlayTests assembly compiles**

Close the Unity editor first if it is open.

Run: `dotnet build Noir.PlayTests.csproj -c Debug`

Expected: build succeeds, 0 errors. (Per `CLAUDE.md`: this is the cheapest check that catches a compile error before an 18-minute PlayMode run finds it instead.)

- [ ] **Step 3: Run the PlayMode gate**

Run:

```
Unity.exe -batchmode -projectPath C:\SerialKillerGame -runTests -testPlatform PlayMode -assemblyNames Noir.PlayTests -testCategory "!Diagnostic" -testResults <xml> -logFile <log>
```

Expected: the new test appears in `<xml>` as passed, alongside the existing baseline (19 of 19 as of 2026-08-11, now 20 of 20). Read `<log>` for the `[geometry] 112 S Chicago: N independently-named storefront(s)` line and the `[survey]`/`[business]` lines from Tasks 3–4 to confirm the count and see whether anything reported as unmatched (expected: nothing, since `business-1991.txt` has no rulings on this address yet).

- [ ] **Step 4: Commit**

```bash
git add Assets/Noir/PlayTests/TownGeometryPlayTests.cs
git commit -m "PlayMode proves 112 S Chicago is several storefronts now, not one"
```

---

### Task 6: Full verification pass

No new code — this is the standing gate from `CLAUDE.md`, run once at the end so nothing from Tasks 1–5 is taken on faith.

- [ ] **Step 1: Full Core suite**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj`

Expected: baseline count (Global Constraints) + 5 (2 from Task 1, 3 from Task 2), 0 fail.

- [ ] **Step 2: All three Unity assemblies compile**

Close the Unity editor first if it is open.

Run, in order:

```
dotnet build Noir.Unity.csproj -c Debug
dotnet build Noir.Editor.csproj -c Debug
dotnet build Noir.PlayTests.csproj -c Debug
```

Expected: all three succeed, 0 errors.

- [ ] **Step 3: Full PlayMode gate**

Run:

```
Unity.exe -batchmode -projectPath C:\SerialKillerGame -runTests -testPlatform PlayMode -assemblyNames Noir.PlayTests -testCategory "!Diagnostic" -testResults <xml> -logFile <log>
```

Expected: 20 of 20 pass, 1 skipped (the standing `[Explicit]` aspiration test, unchanged from baseline).

- [ ] **Step 4: Hand off for the visual check**

This plan does not render a still or claim the row looks like one continuous building — that was explicitly left to the owner ("I will check once done") because it isn't provable from source or from a `Bounds`-only PlayMode assertion. Once Steps 1–3 are green, tell the owner it's ready to look at: `Noir → Render Interiors (thin walls)` or a `CityShot`-style still centred on 112 S Chicago is the fastest way to see it, and then rule the individual storefronts through the in-game panel in Play mode, the same way "301 W Benton Ave" was ruled.
