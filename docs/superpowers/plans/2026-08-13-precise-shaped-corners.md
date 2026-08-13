# Precise Shaped-Building Corners Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop a downtown terrace's front wall from kinking several degrees between neighbouring
storefronts by carrying each corner's true, continuous position through to rendering instead of
only the version already rounded to the nearest tile.

**Architecture:** `Place.Outline` is `Tile[]` — necessary for the tile grid (`WorldBuilder.MaskToOutline`
needs integer coordinates to rasterise `Terrain.Wall`), but lossy for rendering: a narrow storefront's
own edge can swing several degrees once both its corners are independently rounded to the nearest
metre, which reads as a visible gap once you look down the row rather than across it. This plan adds
`OutlinePrecise` — the same ring, in continuous `Vec2` metres, alongside the existing tile-rounded
`Outline` — threaded from `PlaceSpec` through `Place` exactly the way `Outline` already is. Only
`DowntownFromSanborn` populates it in this plan (the terrace generator, and the one place tonight's
symptom was found); `DrawShapedPerimeters` (`Assets/Noir/Unity/VillageMesh.cs`) prefers it when
present and falls back to the tile-rounded `Outline` otherwise, so every other shaped building
(`SeatOnSurvey`'s measured footprints) keeps rendering exactly as it does today — nothing about this
plan changes their output. The tile grid itself is untouched throughout: pathfinding, room stamping,
`WorldBuilder.MaskToOutline` and everything else that reads `Outline` keeps reading the same
tile-rounded ring it always has.

**Tech Stack:** C# / .NET 9 (Core, `dotnet test`), Unity 6000.3.20f1 (Unity + PlayMode).

**Spec:** No separate spec document. Diagnosed directly in conversation with the owner: he pointed at
a specific live gap in Play mode that survived the shaped-perimeter-walls plan's door and winding
fixes; measured front-edge direction differs by up to 15° between some neighbouring 112 S Chicago
storefronts (confirmed with the same winding-normalization `DrawShapedPerimeters` actually uses, and
confirmed the gap disappears entirely from a broadside camera angle on the same stretch — it is a
real geometric kink, not a rendering artifact of viewing angle alone, but its VISIBILITY is angle-dependent).

## Global Constraints

- Core baseline before this plan starts: 501 pass, 0 fail (confirmed 2026-08-13, tip of `main` after
  the shaped-perimeter-walls PR merged). Expect 503 after Task 1's two new tests land, 0 fail
  throughout.
- **Scope is `DowntownFromSanborn` only.** `SeatOnSurvey` (measured real footprints, e.g. the
  L-shaped high school) is NOT touched by this plan and will not gain precise corners — it keeps
  producing `Outline` with no `OutlinePrecise`, and `DrawShapedPerimeters` falls back to the existing
  tile-rounded behaviour for it, unchanged. If the same kink is ever spotted on a `SeatOnSurvey`
  building, that is a follow-up plan, not scope creep here.
- No `UnityEngine` reference is available to anything under `Assets/Noir/Core` — Task 1's new field
  on `PlaceSpec`/`Place` must use `Noir.Core.Contracts.Vec2`, never `UnityEngine.Vector2`.
- The two existing call sites that construct a `Place` (`Assets/Noir/Core/World/WorldBuilder.cs` and
  `tools/Noir.Sim/SampleTown.cs`) must both keep compiling unmodified except where this plan
  explicitly touches `WorldBuilder.cs` — `SampleTown.cs` uses a shorter constructor overload this
  plan does not change, and Task 1's new parameter is optional (defaults to `null`) precisely so nothing
  else has to change.

---

### Task 1: Core — `PlaceSpec` and `Place` carry an optional precise outline

**Files:**
- Modify: `Assets/Noir/Core/World/VillageLayout.cs` (`PlaceSpec`)
- Modify: `Assets/Noir/Core/World/Place.cs` (`Place`)
- Modify: `Assets/Noir/Core/World/WorldBuilder.cs` (threads the new field through)
- Test: `tools/Noir.Core.Tests/ShapedBuildingTests.cs`

**Interfaces:**
- Produces: `PlaceSpec.OutlinePrecise` (`public Vec2[] OutlinePrecise;`, default `null`) and
  `Place.OutlinePrecise` (`public readonly Vec2[] OutlinePrecise;`, threaded through a new optional
  constructor parameter `Vec2[] outlinePrecise = null` on `Place`'s fullest constructor). Consumed by
  Task 2 (`DowntownFromSanborn`, which populates it) and Task 3 (`DrawShapedPerimeters`, which reads
  it).

- [ ] **Step 1: Write the failing tests**

Add to `tools/Noir.Core.Tests/ShapedBuildingTests.cs`, inside the `ShapedBuildingTests` class. First,
replace the existing `Build` helper (it needs an optional second parameter) — find:

```csharp
        private static WorldModel Build(Tile[] outline)
        {
            TestContent.EnsureKinds();
            var layout = VillageParser.Parse(Header + OneHouse);
            layout.Places[0].Outline = outline;
            return WorldBuilder.Build(layout, 1234UL);
        }
```

Replace with:

```csharp
        private static WorldModel Build(Tile[] outline, Vec2[] outlinePrecise = null)
        {
            TestContent.EnsureKinds();
            var layout = VillageParser.Parse(Header + OneHouse);
            layout.Places[0].Outline = outline;
            layout.Places[0].OutlinePrecise = outlinePrecise;
            return WorldBuilder.Build(layout, 1234UL);
        }
```

Then add two new tests, after `AnOutlineTheDoorIsNotInsideIsIgnoredRatherThanObeyed`:

```csharp
        [Test]
        public void OutlinePreciseIsNullWhenNeverSet()
        {
            var world = Build(Ell());
            Assert.That(world.AllPlaces[0].OutlinePrecise, Is.Null,
                        "nothing set a precise ring, so the built Place should not invent one");
        }

        [Test]
        public void OutlinePreciseSurvivesToTheBuiltPlaceUnchanged()
        {
            var precise = new[]
            {
                new Vec2(20.3f, 20.7f), new Vec2(30.1f, 20.4f), new Vec2(30.6f, 30.2f),
                new Vec2(40.4f, 30.9f), new Vec2(40.2f, 40.1f), new Vec2(20.8f, 40.3f),
            };
            var world = Build(Ell(), precise);

            Assert.That(world.AllPlaces[0].OutlinePrecise, Is.EqualTo(precise),
                        "the precise ring is carried through PlaceSpec -> Place unchanged - it is "
                      + "not rounded, resized or reordered anywhere in WorldBuilder");
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~ShapedBuildingTests.OutlinePrecise"`

Expected: build error — `PlaceSpec` has no member `OutlinePrecise`, `Place` has no member
`OutlinePrecise`.

- [ ] **Step 3: Add the field to `PlaceSpec`**

In `Assets/Noir/Core/World/VillageLayout.cs`, find:

```csharp
        public Tile[] Outline;
```

Replace with:

```csharp
        public Tile[] Outline;

        /// <summary>
        /// The same ring as <see cref="Outline"/>, in continuous metres rather than tiles - or
        /// null to say nothing more precise than the tile-rounded ring was ever computed, which is
        /// what every caller except <c>DowntownFromSanborn</c> does today. See
        /// <see cref="Place.OutlinePrecise"/> for why this exists and where it actually changes
        /// what gets drawn.
        /// </summary>
        public Vec2[] OutlinePrecise;
```

- [ ] **Step 4: Add the field and constructor parameter to `Place`**

In `Assets/Noir/Core/World/Place.cs`, find:

```csharp
        public readonly Tile[] Outline;
```

Replace with (keeping the existing doc comment on `Outline` exactly as it is, only adding the new
field and its own doc comment after it):

```csharp
        public readonly Tile[] Outline;

        /// <summary>
        /// The same corners as <see cref="Outline"/>, before they were rounded to the nearest
        /// tile - or null when nothing more precise than the tile-rounded ring was ever computed.
        /// Rendering the wrong precision costs nothing in the grid: pathfinding, room stamping and
        /// every other tile-based system still reads <see cref="Outline"/>, unchanged. It costs a
        /// visible kink between two adjacent buildings' walls once a unit is narrow enough that a
        /// single tile of rounding on nearby corners swings its own edge several degrees off its
        /// neighbour's - see <c>DrawShapedPerimeters</c> (Assets/Noir/Unity/VillageMesh.cs) for
        /// where this actually gets used, and <c>DowntownFromSanborn</c> for the one caller that
        /// populates it today.
        /// </summary>
        public readonly Vec2[] OutlinePrecise;
```

Then find the fullest constructor:

```csharp
        public Place(PlaceId id, PlaceKind kind, string name, string human,
                     TileRect bounds, Tile door, OpenWindow[] hours, int jobSlots, int units,
                     string keySource, Tile[] outline)
        {
            Outline = outline;
            Units = units < 1 ? 1 : units;
```

Replace with:

```csharp
        public Place(PlaceId id, PlaceKind kind, string name, string human,
                     TileRect bounds, Tile door, OpenWindow[] hours, int jobSlots, int units,
                     string keySource, Tile[] outline, Vec2[] outlinePrecise = null)
        {
            Outline = outline;
            OutlinePrecise = outlinePrecise;
            Units = units < 1 ? 1 : units;
```

- [ ] **Step 5: Thread it through `WorldBuilder`**

In `Assets/Noir/Core/World/WorldBuilder.cs`, find:

```csharp
                var place = new Place(id, spec.Kind, spec.Name, spec.Human,
                                      spec.Bounds, spec.Door, spec.Hours.ToArray(),
                                      spec.JobSlots, spec.Units, spec.Key,
                                      shaped ? spec.Outline : null);
```

Replace with:

```csharp
                var place = new Place(id, spec.Kind, spec.Name, spec.Human,
                                      spec.Bounds, spec.Door, spec.Hours.ToArray(),
                                      spec.JobSlots, spec.Units, spec.Key,
                                      shaped ? spec.Outline : null,
                                      shaped ? spec.OutlinePrecise : null);
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~ShapedBuildingTests"`

Expected: PASS, 7 of 7 (the 5 existing tests in this file plus the 2 new ones).

- [ ] **Step 7: Run the full Core suite to confirm nothing else moved**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj`

Expected: 503 pass, 0 fail (501 baseline + 2).

- [ ] **Step 8: Commit**

```bash
git add Assets/Noir/Core/World/VillageLayout.cs Assets/Noir/Core/World/Place.cs Assets/Noir/Core/World/WorldBuilder.cs tools/Noir.Core.Tests/ShapedBuildingTests.cs
git commit -m "Place carries its precise corners alongside the tile-rounded ones, when it has them"
```

---

### Task 2: `DowntownFromSanborn` populates `OutlinePrecise`

**Files:**
- Modify: `Assets/Noir/Unity/DowntownFromSanborn.cs`

**Interfaces:**
- Consumes: `PlaceSpec.OutlinePrecise` (Task 1), `Noir.Core.Contracts.Vec2` (existing — this file
  already has `using Noir.Core.Contracts;`, no new `using` needed).
- Produces: every `PlaceSpec` this file emits now also carries `OutlinePrecise`, the same four
  corners as `Outline` (plus the closing repeat) in continuous metres, taken before `ToTile` ever
  rounds them. Consumed by Task 3 (`DrawShapedPerimeters`).

No Core test is possible for this file (`Assets/Noir/Unity` cannot compile under `dotnet test` —
`UnityEngine.Vector2` is not available there). Verified by `dotnet build` and the PlayMode/visual
check in Task 4, matching this file's own existing convention (see its class doc header).

- [ ] **Step 1: Build the precise ring alongside the tile-rounded one**

In `Assets/Noir/Unity/DowntownFromSanborn.cs`, find:

```csharp
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
```

Replace with:

```csharp
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

                    // THE SAME FOUR CORNERS, BEFORE ToTile TOUCHES THEM. A unit's front edge is
                    // as short as four or five metres on this row, where rounding both its
                    // corners to the nearest tile can swing the wall's own direction several
                    // degrees off its neighbour's - invisible face-on, and a visible gap the
                    // moment you look down the row instead of across it. DrawShapedPerimeters
                    // (Assets/Noir/Unity/VillageMesh.cs) prefers this ring when it is present;
                    // Outline above still exists and is still what the tile grid stamps from.
                    var precise = new[]
                    {
                        new Vec2(a0.x, a0.y), new Vec2(a1.x, a1.y), new Vec2(b1.x, b1.y),
                        new Vec2(b0.x, b0.y), new Vec2(a0.x, a0.y),   // closed, matching corners above
                    };
```

- [ ] **Step 2: Attach it to the `PlaceSpec`**

Find:

```csharp
                    var spec = new PlaceSpec
                    {
                        Kind = PlaceKind.Shop,
                        Bounds = new TileRect(minX, minY, w, h),
                        Outline = outline,
                        Door = door,
                        Name = CommercialRow.HandleFor(address, lot.Id, index),
                    };
```

Replace with:

```csharp
                    var spec = new PlaceSpec
                    {
                        Kind = PlaceKind.Shop,
                        Bounds = new TileRect(minX, minY, w, h),
                        Outline = outline,
                        OutlinePrecise = precise,
                        Door = door,
                        Name = CommercialRow.HandleFor(address, lot.Id, index),
                    };
```

- [ ] **Step 3: Verify it compiles**

Close the Unity editor first if it is open (per `CLAUDE.md`'s standing precondition), or verify via
the live MCP tools if someone is using it.

Run: `dotnet build Noir.Unity.csproj -c Debug`

Expected: build succeeds, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Noir/Unity/DowntownFromSanborn.cs
git commit -m "DowntownFromSanborn hands over the corner it actually computed, not just the rounded one"
```

---

### Task 3: `DrawShapedPerimeters` prefers the precise ring when it has one

**Files:**
- Modify: `Assets/Noir/Unity/VillageMesh.cs`

**Interfaces:**
- Consumes: `Place.OutlinePrecise` (Task 1), populated for terrace units by Task 2.
- Produces: `DrawShapedPerimeters`'s geometry now comes from continuous corners when
  `OutlinePrecise` is present and matches `Outline`'s length; otherwise behaviour is byte-identical
  to before this task (tile-rounded `Outline`, converted to `Vector2` exactly as it already was).

- [ ] **Step 1: Replace the ring-building section**

In `Assets/Noir/Unity/VillageMesh.cs`, find `DrawShapedPerimeters` and replace its body from the
start of the method through the ring-reversal block — find:

```csharp
        private static void DrawShapedPerimeters(WorldModel world, MeshChunks chunks, ref int count)
        {
            foreach (var place in world.AllPlaces)
            {
                if (place == null || place.Outline == null || place.Outline.Length < 3) continue;
                if (!PlaceKindTable.Current.Row(place.Kind).IsBuilding) continue;
                if (CityBuildings.Handles(place)) continue;   // a bought model brings its own walls

                float depth = Materials3D.WallDepthFor(place);
                int submesh = Materials3D.WallingFor(place);
                float bottom = Space3D.GroundUnder(place.Bounds);
                float top = bottom + MassingGrammars.Of(place).Eaves;

                var outline = place.Outline;
                int n = outline.Length;

                // Signed area, shoelace formula: sum of (x_i * y_(i+1) - x_(i+1) * y_i) over
                // every edge (the formula's usual halving does not matter here - only the sign
                // is used). Positive means this ring already winds the way the axis-aligned
                // AddWall callers in BuildWalls wind a rectangle's corners; negative means the
                // other way. Rather than carry that sign through every computation below, the
                // ring is reversed ONCE, here, whenever it does not already match - so the
                // inward normal and AddWall's corner order can both be written as if every ring
                // that reaches them always wound the same way, because now it does.
                float signedArea = 0f;
                for (int i = 0; i < n; i++)
                {
                    var a = outline[i];
                    var b = outline[(i + 1) % n];
                    signedArea += (float)a.X * b.Y - (float)b.X * a.Y;
                }

                var ring = outline;
                if (signedArea < 0f)
                {
                    ring = new Tile[n];
                    for (int i = 0; i < n; i++) ring[i] = outline[n - 1 - i];
                }
```

Replace with:

```csharp
        private static void DrawShapedPerimeters(WorldModel world, MeshChunks chunks, ref int count)
        {
            foreach (var place in world.AllPlaces)
            {
                if (place == null || place.Outline == null || place.Outline.Length < 3) continue;
                if (!PlaceKindTable.Current.Row(place.Kind).IsBuilding) continue;
                if (CityBuildings.Handles(place)) continue;   // a bought model brings its own walls

                float depth = Materials3D.WallDepthFor(place);
                int submesh = Materials3D.WallingFor(place);
                float bottom = Space3D.GroundUnder(place.Bounds);
                float top = bottom + MassingGrammars.Of(place).Eaves;

                // THE TRUE CORNER WHEN ONE WAS MEASURED, the tile-rounded one otherwise. Rounding
                // every corner to the nearest metre is fine for a wide unit - the direction error
                // it introduces is a fraction of a degree - but a downtown storefront can be four
                // or five metres across, where the SAME one-tile rounding on two corners just a
                // few metres apart can swing the wall's own direction several degrees off its
                // neighbour's. Two flat panels meeting at that angle look solid face-on and open
                // up the moment you look down the row rather than across it. See
                // Place.OutlinePrecise for where this comes from and who populates it.
                int n = place.Outline.Length;
                var pts = new Vector2[n];
                if (place.OutlinePrecise != null && place.OutlinePrecise.Length == n)
                {
                    for (int i = 0; i < n; i++)
                        pts[i] = new Vector2(place.OutlinePrecise[i].X, place.OutlinePrecise[i].Y);
                }
                else
                {
                    for (int i = 0; i < n; i++)
                        pts[i] = new Vector2(place.Outline[i].X, place.Outline[i].Y);
                }

                // Signed area, shoelace formula: sum of (x_i * y_(i+1) - x_(i+1) * y_i) over
                // every edge (the formula's usual halving does not matter here - only the sign
                // is used). Positive means this ring already winds the way the axis-aligned
                // AddWall callers in BuildWalls wind a rectangle's corners; negative means the
                // other way. Rather than carry that sign through every computation below, the
                // ring is reversed ONCE, here, whenever it does not already match - so the
                // inward normal and AddWall's corner order can both be written as if every ring
                // that reaches them always wound the same way, because now it does.
                float signedArea = 0f;
                for (int i = 0; i < n; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % n];
                    signedArea += a.x * b.y - b.x * a.y;
                }

                var ring = pts;
                if (signedArea < 0f)
                {
                    ring = new Vector2[n];
                    for (int i = 0; i < n; i++) ring[i] = pts[n - 1 - i];
                }
```

- [ ] **Step 2: Remove the now-redundant per-point `Vector2` conversions**

`ring[i]` is now already a `Vector2` (built in Step 1), where before it was a `Tile` that had to be
converted at every point of use. Find the door-edge search:

```csharp
                    float bestDist = float.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        var p0 = new Vector2(ring[i].X, ring[i].Y);
                        var p1 = new Vector2(ring[(i + 1) % n].X, ring[(i + 1) % n].Y);
                        float len = Vector2.Distance(p0, p1);
```

Replace with:

```csharp
                    float bestDist = float.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        var p0 = ring[i];
                        var p1 = ring[(i + 1) % n];
                        float len = Vector2.Distance(p0, p1);
```

Then find the slab-emitting loop, right after the door-edge search:

```csharp
                for (int i = 0; i < n; i++)
                {
                    var p0 = new Vector2(ring[i].X, ring[i].Y);
                    var p1 = new Vector2(ring[(i + 1) % n].X, ring[(i + 1) % n].Y);
                    var edge = p1 - p0;
```

Replace with:

```csharp
                for (int i = 0; i < n; i++)
                {
                    var p0 = ring[i];
                    var p1 = ring[(i + 1) % n];
                    var edge = p1 - p0;
```

Everything else in `DrawShapedPerimeters` (the door-gap math, the `Slab` local function, the
`AddWall` call) is unchanged — it already operated on `Vector2`, and `ring[i]` now simply IS one
directly instead of needing the conversion at every use.

- [ ] **Step 3: Verify it compiles**

Close the Unity editor first if it is open, or verify via the live MCP tools if someone is using it.

Run: `dotnet build Noir.Unity.csproj -c Debug`

Expected: build succeeds, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Noir/Unity/VillageMesh.cs
git commit -m "DrawShapedPerimeters draws from the corner that was actually measured, when it has one"
```

---

### Task 4: Verification — build, and look at 112 S Chicago from the same angle that showed the kink

No new code — confirms Tasks 1-3 actually closed what the owner pointed at.

- [ ] **Step 1: Full Unity build**

Close the Unity editor first if it is open.

Run, in order:

```
dotnet build Noir.Unity.csproj -c Debug
dotnet build Noir.Editor.csproj -c Debug
dotnet build Noir.PlayTests.csproj -c Debug
```

Expected: all three succeed, 0 errors.

- [ ] **Step 2: Confirm the Core baseline**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj`

Expected: 503 pass, 0 fail (unchanged from Task 1's own check — this task adds no new Core code).

- [ ] **Step 3: Run the PlayMode gate**

If the editor is closed:

```
Unity.exe -batchmode -projectPath C:\SerialKillerGame -runTests -testPlatform PlayMode -assemblyNames Noir.PlayTests -testCategory "!Diagnostic" -testResults <xml> -logFile <log>
```

Expected: 20 of 20 pass, 0 fail, 1 skipped (unchanged baseline).

- [ ] **Step 4: Look at 112 S Chicago from the grazing angle, not just face-on**

This is the step a face-on screenshot cannot substitute for — the whole point of this plan is that
the kink is invisible from most angles and only opens up looking down the row. If the editor is
open and in use, drive it live: find `Camera.main`'s instance ID via `Unity_RunCommand`, and either
capture from the owner's own current view, or reproduce the earlier diagnostic angle directly by
positioning a temporary camera at approximately `(789, 15-19, -1374)` looking toward roughly
`(261° yaw, 12-23° pitch down)` — the exact transform the owner's own camera was at when the kink
was first reported live. Compare against what was seen before this plan: the pale slivers between
narrower storefronts partway down the row should be gone, and the row should read as one continuous
wall from this angle too, not just broadside.

Cross-check numerically, the same way the kink was originally measured: re-run the front-edge angle
comparison across `112 S Chicago #1` through `#17` (project each unit's door onto its own
winding-normalized ring, find the closest edge, compare its direction to the previous unit's) - the
angle between consecutive units' front edges should now be near 0° (a fraction of a degree, from
`CommercialRow`'s own frontage-length division, not the several-degree jumps tile-rounding caused),
wherever `OutlinePrecise` is populated.

- [ ] **Step 5: Report to the owner**

Tell him it's ready to look at, with the before/after angle numbers from Step 4 if he wants them,
and remind him `SeatOnSurvey`-sourced buildings (the school, and any other measured real footprint)
were deliberately left out of this plan's scope per the Global Constraints note.
