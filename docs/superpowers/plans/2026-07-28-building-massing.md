# Building Massing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give each building kind its own silhouette — wall height, roof form, pitch, and bespoke extras like a church tower — so the church, mill and school are recognisable from the overview camera without reading a sign.

**Architecture:** A `Massing` struct replaces two global constants (`Space3D.WallHeight`, `RoofBuilder.Pitch`) as the source of truth for a building's shape. One `IMassingGrammar` per kind supplies it, selected by a new optional `massing` column in `Content/kinds.txt` defaulting to `cottage`. Grammars — never a `switch (place.Kind)` in geometry code — so a new amenity still costs one content row.

**Tech Stack:** C# / .NET Standard 2.1 (Core), Unity 6000.3.20f1 URP (shell), NUnit (tools/Noir.Core.Tests), headless `Noir.Editor.SmokeTest` for geometry.

## Global Constraints

- **Core must not reference UnityEngine.** `Assets/Noir/Core` has zero Unity references and that is enforced by asmdef. The `massing` *column* lives in Core (`PlaceKindTable`); the `Massing` *struct*, the grammars and all geometry live in `Assets/Noir/Unity`.
- **`Noir.Core.Tests` references Core only.** It can test the column. It cannot test geometry. Geometry is verified by `Noir.Editor.SmokeTest`, run headlessly.
- **Determinism is load-bearing.** The 12 snapshots must come out byte-identical across two separate Unity processes. Any iteration over places or tiles must have a defined order; any tie must have an explicit deterministic break.
- **Do not change `dwelling` or `farm` numbers.** They keep eaves 3.0, hip, pitch 2.2 — today's exact values — so the village people already know does not shift underneath this work.
- **The `massing` column is optional and defaults to `cottage`.** Unlike `rooms`, `roof` and `frontage`, a missing `massing` must NOT refuse to load a village. Follow the existing `grammar` column, which is already optional.
- **Run the headless build with the editor closed.** Unity takes an exclusive project lock.
  `"C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" -batchmode -quit -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.SmokeTest.Run -logFile <log>`

---

## File Structure

| File | Responsibility |
|---|---|
| `Assets/Noir/Core/World/PlaceKindTable.cs` | **Modify.** New optional `Massing` string column. |
| `Content/kinds.txt` | **Modify.** One `massing` line per building kind. |
| `Assets/Noir/Unity/Massing/Massing.cs` | **Create.** The `RoofForm` enum and `Massing` struct. Data only. |
| `Assets/Noir/Unity/Massing/IMassingGrammar.cs` | **Create.** The interface. |
| `Assets/Noir/Unity/Massing/MassingGrammars.cs` | **Create.** The registry and `Massing.Of`. |
| `Assets/Noir/Unity/Massing/Grammars.cs` | **Create.** The eleven grammar implementations. |
| `Assets/Noir/Unity/Massing/Extras.cs` | **Create.** Tower, spire, bell-cote, lucam geometry. |
| `Assets/Noir/Unity/VillageMesh.cs` | **Modify.** `BuildWalls` gains a place-per-tile paint and per-run height. |
| `Assets/Noir/Unity/RoofBuilder.cs` | **Modify.** `AddHipRoof` becomes `AddRoof(form, eaves, pitch)`. |
| `Assets/Noir/Unity/Frontage.cs` | **Modify.** Door head reads eaves from `Massing`. |
| `Assets/Noir/Unity/XRay.cs` | **Modify.** Outline box reads eaves from `Massing`. |
| `Assets/Noir/Editor/Elevations.cs` | **Create.** The name-that-building instrument. |
| `Assets/Noir/Editor/SmokeTest.cs` | **Modify.** Assert every kind resolves and no `Extras` throws. |
| `tools/Noir.Core.Tests/PlaceKindTableTests.cs` | **Modify.** Column tests. |

---

## Task 1: The `massing` column

**Files:**
- Modify: `Assets/Noir/Core/World/PlaceKindTable.cs`
- Modify: `Content/kinds.txt`
- Test: `tools/Noir.Core.Tests/PlaceKindTableTests.cs`

**Interfaces:**
- Produces: `PlaceKindRow.Massing` — a non-null lowercase `string`, `"cottage"` when the row omits it.

- [ ] **Step 1: Write the failing tests**

Add to `tools/Noir.Core.Tests/PlaceKindTableTests.cs`:

```csharp
[Test]
public void MassingDefaultsToCottageWhenTheRowDoesNotSayOtherwise()
{
    var table = PlaceKindTable.Parse(TestContent.Read("kinds.txt"));
    Assert.That(table.Row(PlaceKind.Dwelling).Massing, Is.EqualTo("cottage"));
}

[Test]
public void MassingIsReadFromTheRowWhenPresent()
{
    var table = PlaceKindTable.Parse(TestContent.Read("kinds.txt"));
    Assert.That(table.Row(PlaceKind.Church).Massing, Is.EqualTo("church"));
    Assert.That(table.Row(PlaceKind.Mill).Massing, Is.EqualTo("mill"));
}

/// <summary>
/// Massing is decoration, so a row without it must still load. This is deliberately
/// different from rooms/roof/frontage, which refuse an incomplete row - a village with an
/// unstyled barber is worth having, a village that will not open is not.
/// </summary>
[Test]
public void AKindWithNoMassingLineStillLoads()
{
    const string source = @"
kind shed
  words     shed
  form      building
  ground    grass
  rooms     none
  roof      yes
  frontage  none
  props     none
  counter   no
  hours     none
  jobs      0
  roles     none
  shifts    one
  catchment no
  describe  no
";
    var table = PlaceKindTable.Parse(source);
    var shed = table.KindNamed("shed");
    Assert.That(table.Row(shed).Massing, Is.EqualTo("cottage"));
}
```

If `KindNamed` does not exist, resolve the kind however the existing tests in this file
already do — read them first and follow that pattern rather than inventing an accessor.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd C:\SerialKillerGame\tools
dotnet test Noir.sln --filter "Massing" --nologo
```

Expected: FAIL — `PlaceKindRow` has no definition for `Massing` (compile error is an
acceptable red here; it is the feature being missing, not a typo).

- [ ] **Step 3: Add the column**

In `PlaceKindTable.cs`, follow the **`grammar`** column exactly — it is already optional and
is the precedent. Four edits:

1. Field on `PlaceKindRow`, beside `Grammar`:

```csharp
/// <summary>
/// Which massing grammar shapes the outside of it: how tall the walls are, what the roof
/// does, and whether it gets a tower. Read by Assets/Noir/Unity, not by Core - the same
/// arrangement as Roofed and Frontage.
///
/// Optional, and defaults to "cottage". A missing massing line means a plain building, not
/// a broken village.
/// </summary>
public readonly string Massing;
```

2. Constructor parameter `string massing` immediately after `string grammar`, assigned
   `Massing = massing;`.

3. Parse case, beside `case "grammar":`:

```csharp
case "massing":
    ContentText.Require(tokens, 2, File, lineNo, "massing <grammar>");
    Massing = tokens[1].ToLowerInvariant();
    break;
```

   with `public string Massing;` added to the `Draft` class.

4. In the build call, pass `d.Massing ?? "cottage"`. Do **not** add a `Need(...)` line for it.

- [ ] **Step 4: Add the rows to `Content/kinds.txt`**

Add one `massing` line to each building kind, directly under its `frontage` line:

```
dwelling    -> massing   cottage
farm        -> massing   cottage
shop        -> massing   shopfront
postoffice  -> massing   shopfront
surgery     -> massing   shopfront
pub         -> massing   pub
villagehall -> massing   hall
school      -> massing   school
church      -> massing   church
mill        -> massing   mill
garage      -> massing   garage
```

Leave the six `form open` kinds (playground, busstop, phonebox, green, churchyard,
allotments) alone — they have no walls to shape.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test Noir.sln --filter "Massing" --nologo
dotnet test Noir.sln --nologo
```

Expected: the three new tests PASS. Full suite: **128 passed, 3 failed** — the three
failures must be `NobodyIsOnlyASchedule`, `TheMedianVillagerYieldsTwiceAsMuchTextureAsUse`
and `TheTenthPercentileIsNotALock`, which fail by design. Any *other* failure is yours.

- [ ] **Step 6: Commit**

```bash
git add Assets/Noir/Core/World/PlaceKindTable.cs Content/kinds.txt tools/Noir.Core.Tests/PlaceKindTableTests.cs
git commit -m "Add the massing column to kinds.txt, defaulting to cottage"
```

---

## Task 2: `Massing`, the registry, and the cottage grammar

**Files:**
- Create: `Assets/Noir/Unity/Massing/Massing.cs`
- Create: `Assets/Noir/Unity/Massing/IMassingGrammar.cs`
- Create: `Assets/Noir/Unity/Massing/MassingGrammars.cs`
- Modify: `Assets/Noir/Editor/SmokeTest.cs`

**Interfaces:**
- Consumes: `PlaceKindTable.Current.Row(place.Kind).Massing` from Task 1.
- Produces:
  - `enum RoofForm { Hip, Gable, LeanTo, Flat }`
  - `struct Massing { float Eaves; RoofForm Roof; float Pitch; bool RidgeAcross; }`
  - `static Massing MassingGrammars.Of(Place place)`
  - `interface IMassingGrammar { Massing Profile(Place place); void Extras(Place place, MeshChunk into); }`

- [ ] **Step 1: Create `Massing.cs`**

```csharp
namespace Noir.Unity
{
    public enum RoofForm { Hip, Gable, LeanTo, Flat }

    /// <summary>
    /// The shape of a building from outside: how high the walls go, what the roof does above
    /// them, and which way the ridge runs.
    ///
    /// This exists because every roofed building used to get the identical AddHipRoof at the
    /// identical 2.2 pitch on identical 3.0 walls, so the church, the mill, the school and a
    /// two-up-two-down were the same box differing only in footprint. From the overview camera
    /// nothing told them apart.
    ///
    /// A plain value with no behaviour, resolved once at build time. Nothing here is consulted
    /// per frame.
    /// </summary>
    public readonly struct Massing
    {
        /// <summary>Wall height, where the roof starts. Was the global Space3D.WallHeight.</summary>
        public readonly float Eaves;

        public readonly RoofForm Roof;

        /// <summary>Ridge height above the eaves. Was the global RoofBuilder.Pitch. Zero for Flat.</summary>
        public readonly float Pitch;

        /// <summary>
        /// Run the ridge across the SHORT axis instead of the long one. A church wants this:
        /// the nave's ridge runs the length of the building, but a transept or a tower reads
        /// wrong if the main ridge follows the same rule a cottage does.
        /// </summary>
        public readonly bool RidgeAcross;

        public Massing(float eaves, RoofForm roof, float pitch, bool ridgeAcross = false)
        {
            Eaves = eaves;
            Roof = roof;
            Pitch = pitch;
            RidgeAcross = ridgeAcross;
        }

        /// <summary>Exactly what every building got before this system existed.</summary>
        public static readonly Massing Cottage = new Massing(3.0f, RoofForm.Hip, 2.2f);
    }
}
```

- [ ] **Step 2: Create `IMassingGrammar.cs`**

```csharp
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// One kind of building's outside shape.
    ///
    /// Deliberately an interface with a registry rather than a switch on PlaceKind. Stage 4
    /// made PlaceKind open - a row the enum has never heard of gets the next value along - and
    /// a switch in the mesh builder would quietly close it again, putting a new amenity back to
    /// costing C#. An unregistered kind falls through to cottage and still looks like a
    /// building.
    /// </summary>
    public interface IMassingGrammar
    {
        /// <summary>Wall height, roof form, pitch. Called once per building at build time.</summary>
        Massing Profile(Place place);

        /// <summary>
        /// Bespoke geometry this kind and no other gets: a tower, a bell-cote, a hoist.
        /// Emitted into the same chunked mesh as the roofs, so it inherits the existing
        /// chunking and culling and adds no renderers of its own.
        ///
        /// Most grammars add nothing and should leave this empty.
        /// </summary>
        void Extras(Place place, MeshChunk into);
    }
}
```

- [ ] **Step 3: Create `MassingGrammars.cs` with only the cottage registered**

```csharp
using System.Collections.Generic;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// Which grammar shapes which building, looked up by the massing column in kinds.txt.
    /// </summary>
    public static class MassingGrammars
    {
        private static readonly Dictionary<string, IMassingGrammar> Registry =
            new Dictionary<string, IMassingGrammar>
            {
                { "cottage", new CottageMassing() },
            };

        /// <summary>
        /// The grammar for a place, or the cottage if its column names one that does not exist.
        ///
        /// Falling back rather than throwing is the point: massing is decoration, and a village
        /// that will not render because somebody typed "cotage" is worse than one with a plain
        /// building in it. The name is logged once so the typo is still findable.
        /// </summary>
        public static IMassingGrammar For(Place place)
        {
            string name = PlaceKindTable.Current.Row(place.Kind).Massing;
            if (name != null && Registry.TryGetValue(name, out var grammar)) return grammar;

            if (name != null && !_warned.Contains(name))
            {
                _warned.Add(name);
                UnityEngine.Debug.LogWarning($"kinds.txt: no massing grammar called '{name}'; "
                                           + "using cottage.");
            }
            return Registry["cottage"];
        }

        private static readonly HashSet<string> _warned = new HashSet<string>();

        public static Massing Of(Place place) => For(place).Profile(place);
    }

    /// <summary>
    /// A house. Exactly what every building in the village got before massing existed, kept
    /// byte-for-byte so that adding this system does not move a single cottage.
    /// </summary>
    public sealed class CottageMassing : IMassingGrammar
    {
        public Massing Profile(Place place) => Massing.Cottage;
        public void Extras(Place place, MeshChunk into) { }
    }
}
```

- [ ] **Step 4: Add the smoke-test assertion**

In `SmokeTest.cs`, immediately after `VillageMesh.Build(...)`:

```csharp
// Every building must resolve to a grammar and survive being asked for its Extras. These
// are runtime failures - a null row, a missing registry entry, a bad index in tower
// geometry - and compiling catches none of them.
int shaped = 0;
foreach (var place in world.AllPlaces)
{
    if (!PlaceKindTable.Current.Row(place.Kind).IsBuilding) continue;
    var m = MassingGrammars.Of(place);
    if (m.Eaves <= 0f) { LogError($"massing: '{place.Name}' has eaves {m.Eaves}"); failures++; }
    shaped++;
}
Log($"massing    {shaped} buildings shaped");
```

- [ ] **Step 5: Run the smoke test to verify it passes**

```bash
"C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" -batchmode -quit \
  -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.SmokeTest.Run -logFile smoke.log
```

Expected: `massing    50 buildings shaped` (44 dwellings + the amenities), then
`--- SMOKE TEST PASSED ---`. Read the log with `Select-String`, not `grep` — the log is
UTF-16 and `grep` silently returns nothing.

- [ ] **Step 6: Commit**

```bash
git add Assets/Noir/Unity/Massing Assets/Noir/Editor/SmokeTest.cs
git commit -m "Massing struct, grammar interface, registry, and the cottage grammar"
```

---

## Task 3: Per-building wall heights

This is the surgery. `BuildWalls` scans the tile grid and merges wall tiles into runs without
ever asking which `Place` they belong to, so a run can currently span two buildings.

**Files:**
- Modify: `Assets/Noir/Unity/VillageMesh.cs` (`BuildWalls` ~line 771, `AddWall` ~line 843)

**Interfaces:**
- Consumes: `MassingGrammars.Of(place)` from Task 2.
- Produces: `AddWall(MeshChunk into, int gx, int gy, int w, int h, float top)` — note the new
  final parameter. Task 7 does not call this; nothing else does.

- [ ] **Step 1: Add the place-per-tile paint**

At the top of `BuildWalls`, before the run loops:

```csharp
// Which building owns each wall tile, so a run can carry that building's height.
//
// Walls were merged purely geometrically, which was correct while every building was 3 m
// tall and is wrong the moment they are not: a run could span two buildings and would have
// to pick one of their heights.
//
// Where two buildings share a boundary tile the LOWEST PLACE ID WINS. Any tie-break would
// do; what matters is that it is fixed, because iteration order deciding a wall's height
// would make the mesh differ run to run and the twelve snapshots are asserted byte-identical.
var owner = new int[world.Width * world.Height];
for (int i = 0; i < owner.Length; i++) owner[i] = -1;

foreach (var place in world.AllPlaces)
{
    if (!PlaceKindTable.Current.Row(place.Kind).IsBuilding) continue;
    var b = place.Bounds;
    for (int y = b.Y; y < b.Y + b.H; y++)
    for (int x = b.X; x < b.X + b.W; x++)
    {
        if (x < 0 || y < 0 || x >= world.Width || y >= world.Height) continue;
        int at = y * world.Width + x;
        if (owner[at] < 0 || place.Id.Value < owner[at]) owner[at] = place.Id.Value;
    }
}

float HeightAt(int gx, int gy)
{
    int id = owner[gy * world.Width + gx];
    if (id < 0) return Space3D.WallHeight;          // a garden wall, owned by no building
    var p = world.GetPlace(new PlaceId(id));
    return p == null ? Space3D.WallHeight : MassingGrammars.Of(p).Eaves;
}
```

If `PlaceId`'s constructor differs from `new PlaceId(int)`, read `Assets/Noir/Core/Contracts/Ids.cs`
and use whatever it actually provides.

- [ ] **Step 2: Break runs when the owner changes**

Both run loops currently extend while `IsWall(...) && !used[...]`. Add the owner to that
condition. For the horizontal pass:

```csharp
int start = gx;
int startOwner = owner[gy * world.Width + gx];
while (gx < world.Width && IsWall(world, gx, gy) && !used[gy * world.Width + gx]
       && owner[gy * world.Width + gx] == startOwner)
{
    used[gy * world.Width + gx] = true;
    gx++;
}
int length = gx - start;
if (length >= 2) { AddWall(chunks.At(start, gy), start, gy, length, 1, HeightAt(start, gy)); count++; }
else { used[gy * world.Width + start] = false; }
```

Apply the identical change to the vertical pass, swapping the roles of `gx` and `gy` and
passing `HeightAt(gx, start)`.

- [ ] **Step 3: Give `AddWall` the height**

Change the signature and delete the constant:

```csharp
private static void AddWall(MeshChunk into, int gx, int gy, int w, int h, float top)
{
    var verts = into.Verts;
    var uvs = into.Uvs;
    var tris = into.Tris[0];

    float x0 = gx, x1 = gx + w;
    float z0 = -gy, z1 = -(gy + h);
    // ... body unchanged; `top` is now the parameter, not Space3D.WallHeight
```

Delete the line `float top = Space3D.WallHeight;` from the body.

- [ ] **Step 4: Run the smoke test — geometry must still build**

```bash
"C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" -batchmode -quit \
  -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.SmokeTest.Run -logFile smoke.log
```

Expected: PASSED. At this point **every kind is still cottage**, so the renderer count should
be within a few percent of the 1104 the smoke test reported before this task — the runs now
break at building boundaries, which adds some. A large jump means the owner paint is wrong.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Unity/VillageMesh.cs
git commit -m "Walls carry their building's height; runs break at building boundaries"
```

---

## Task 4: Roof forms

**Files:**
- Modify: `Assets/Noir/Unity/RoofBuilder.cs`

**Interfaces:**
- Consumes: `Massing`, `MassingGrammars.Of` from Task 2.
- Produces: `AddRoof(TileRect bounds, Massing m, MeshChunk into, int submesh)`, replacing
  `AddHipRoof(TileRect, MeshChunk, int)`.

- [ ] **Step 1: Rename and parameterise the hip roof**

`AddHipRoof` becomes the `Hip` branch of `AddRoof`. Replace its two hardcoded reads:

```csharp
float y = Space3D.WallHeight;   ->   float y = m.Eaves;
float ridgeY = y + Pitch;       ->   float ridgeY = y + m.Pitch;
```

and replace the `if (w >= d)` ridge-axis test with `bool alongX = m.RidgeAcross ? d > w : w >= d;`.

- [ ] **Step 2: Add the three new forms**

```csharp
/// <summary>
/// The roof, in whichever form this kind of building wears.
///
/// Hip is what the whole village had and what houses keep. The others exist because a
/// silhouette is the only thing that carries to the overview camera: a gable end reads as
/// a public building, a flat roof reads as a workshop, and neither of them reads as a house.
/// </summary>
private static void AddRoof(TileRect bounds, Massing m, MeshChunk into, int submesh)
{
    switch (m.Roof)
    {
        case RoofForm.Gable:  AddGableRoof(bounds, m, into, submesh); break;
        case RoofForm.LeanTo: AddLeanToRoof(bounds, m, into, submesh); break;
        case RoofForm.Flat:   AddFlatRoof(bounds, m, into, submesh); break;
        default:              AddHipRoof(bounds, m, into, submesh); break;
    }
}
```

**This switch is on `RoofForm`, not on `PlaceKind`.** That distinction is the whole point of
the design — `RoofForm` is a closed set of shapes, `PlaceKind` is open.

A gable roof is the hip roof with the ridge run to the full length of the building instead of
inset by half its depth, and the two end triangles replaced by vertical gable walls up to the
ridge. Build it from the same six vertices: set the ridge ends to the building's own ends
(`r0.x = x0`, `r1.x = x1` for a ridge along X) and emit two quads for the slopes plus two
triangles standing vertically at each end.

Lean-to is a single slope: the eaves edge at `m.Eaves` on one side, rising to `m.Eaves +
m.Pitch` on the other. Flat is one quad at `m.Eaves` with the overhang, and no pitch at all.

- [ ] **Step 3: Wire the build loop to the grammar**

In `Build`, replace the `AddHipRoof` call:

```csharp
var massing = MassingGrammars.Of(place);
AddRoof(place.Bounds, massing, into, Materials3D.RoofingFor(place.Bounds.X, place.Bounds.Y));

int stacks = place.Kind == PlaceKind.Dwelling ? place.Units : 1;
AddChimneys(place.Bounds, stacks, massing, into, Materials3D.ChimneyIndex);

MassingGrammars.For(place).Extras(place, into);
```

`AddChimneys` takes `Massing` too — its `float ridgeY = Space3D.WallHeight + Pitch;` becomes
`float ridgeY = m.Eaves + m.Pitch;`. Leave the inset arithmetic alone; the comment above it
records a real bug that was fixed once and must not be reintroduced.

- [ ] **Step 4: Run the smoke test**

Expected: PASSED, renderer count unchanged from Task 3 (every kind is still cottage, so every
roof is still hip — this task adds code paths nothing takes yet).

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Unity/RoofBuilder.cs
git commit -m "Roofs come in four forms; the roof loop asks the massing grammar"
```

---

## Task 5: The ten remaining grammars, profiles only

**Files:**
- Create: `Assets/Noir/Unity/Massing/Grammars.cs`
- Modify: `Assets/Noir/Unity/Massing/MassingGrammars.cs` (register them)

**Interfaces:**
- Consumes: `IMassingGrammar`, `Massing`, `RoofForm` from Task 2.
- Produces: `ShopfrontMassing`, `PubMassing`, `HallMassing`, `SchoolMassing`, `ChurchMassing`,
  `MillMassing`, `GarageMassing`. All have empty `Extras` in this task; Task 6 fills three of them.

- [ ] **Step 1: Write the grammars**

```csharp
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>A shop, a post office, a surgery: a tall ground floor with room for a fascia.</summary>
    public sealed class ShopfrontMassing : IMassingGrammar
    {
        public Massing Profile(Place place) =>
            new Massing(place.Kind == PlaceKind.Surgery ? 3.4f : 3.6f, RoofForm.Hip, 2.0f);
        public void Extras(Place place, MeshChunk into) { }
    }

    public sealed class PubMassing : IMassingGrammar
    {
        public Massing Profile(Place place) => new Massing(3.4f, RoofForm.Gable, 2.4f);
        public void Extras(Place place, MeshChunk into) { }
    }

    public sealed class HallMassing : IMassingGrammar
    {
        public Massing Profile(Place place) => new Massing(4.2f, RoofForm.Gable, 2.6f);
        public void Extras(Place place, MeshChunk into) { }
    }

    public sealed class SchoolMassing : IMassingGrammar
    {
        public Massing Profile(Place place) => new Massing(4.0f, RoofForm.Gable, 2.6f);
        public void Extras(Place place, MeshChunk into) { }   // bell-cote in Task 6
    }

    /// <summary>
    /// Tall nave walls and a steep roof. Both numbers are doing the same job: a church is the
    /// one building in a village that was built to be seen from the next parish.
    /// </summary>
    public sealed class ChurchMassing : IMassingGrammar
    {
        public Massing Profile(Place place) => new Massing(5.5f, RoofForm.Gable, 4.5f);
        public void Extras(Place place, MeshChunk into) { }   // tower and spire in Task 6
    }

    /// <summary>Three storeys and a shallow roof - the bulk is the signal.</summary>
    public sealed class MillMassing : IMassingGrammar
    {
        public Massing Profile(Place place) => new Massing(6.5f, RoofForm.Gable, 1.6f);
        public void Extras(Place place, MeshChunk into) { }   // lucam in Task 6
    }

    public sealed class GarageMassing : IMassingGrammar
    {
        public Massing Profile(Place place) => new Massing(3.4f, RoofForm.Flat, 0f);
        public void Extras(Place place, MeshChunk into) { }
    }
}
```

- [ ] **Step 2: Register them**

```csharp
{ "cottage",   new CottageMassing() },
{ "shopfront", new ShopfrontMassing() },
{ "pub",       new PubMassing() },
{ "hall",      new HallMassing() },
{ "school",    new SchoolMassing() },
{ "church",    new ChurchMassing() },
{ "mill",      new MillMassing() },
{ "garage",    new GarageMassing() },
```

- [ ] **Step 3: Run the smoke test**

Expected: PASSED. **This is the first task where the village changes shape.** Renderer and
vertex counts will move. Record both numbers from the log in the commit message.

- [ ] **Step 4: Look at it**

Render the snapshots and open `noon-overview` and `mill-gate`:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" -batchmode -quit \
  -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.Snapshot.Render -logFile snap.log
```

The church and the mill should now be obviously taller than the houses. If they are not, the
massing column is not reaching the grammar — check `PlaceKindTable.Current` is installed.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Unity/Massing
git commit -m "Seven more massing grammars: the village stops being one box repeated"
```

---

## Task 6: The bespoke extras

**Files:**
- Create: `Assets/Noir/Unity/Massing/Extras.cs`
- Modify: `Assets/Noir/Unity/Massing/Grammars.cs` (church, school, mill)

**Interfaces:**
- Consumes: `MeshChunk` (`.Verts`, `.Uvs`, `.Tris[submesh]`), `Locality.AnchorOf(Place)`.
- Produces: `Extras.Tower(...)`, `Extras.Spire(...)`, `Extras.BellCote(...)`, `Extras.Lucam(...)`.

- [ ] **Step 1: Write the extras**

All four are boxes and pyramids. Add a shared `Box(MeshChunk, Vector3 centre, Vector3 size, int submesh)`
and `Pyramid(MeshChunk, Vector3 baseCentre, float width, float height, int submesh)` and build
each feature from them.

Placement rules, which are the part that matters:

- **Tower** — square, side `min(bounds.W, bounds.H) * 0.55f`, height `m.Eaves + m.Pitch + 4.0f`.
  Placed at the end of the building **furthest from the front door**, so it reads as a west
  tower. Get the door with `Locality.AnchorOf(place)` — the same call `Frontage` makes — and
  put the tower at whichever of the four bounds-edge midpoints is furthest from it.
- **Spire** — a pyramid on the tower, base = tower side, height = `tower side * 2.2f`.
- **Bell-cote** — a small box `0.9 x 0.5 x 1.4` standing on the ridge, one third along from the
  end furthest from the door.
- **Lucam** — a box `2.0 x 1.6 x 1.4` projecting from the long wall at eaves height, centred.

**The tower is exterior-only.** It does not claim interior floor space and the interior grammar
has already placed rooms across the whole footprint. This is recorded in the spec as a known
simplification — do not try to reconcile it here.

- [ ] **Step 2: Call them from the three grammars**

```csharp
public void Extras(Place place, MeshChunk into)   // ChurchMassing
{
    var m = Profile(place);
    var tower = Massing.Extras.Tower(place, m, into, Materials3D.ChimneyIndex);
    Massing.Extras.Spire(tower, into, Materials3D.ChimneyIndex);
}
```

Use `Materials3D.ChimneyIndex` for tower, spire and bell-cote — it is masonry, which is what
these are, and it is already in the roof material array so no new submesh is needed. The lucam
uses `Materials3D.RoofingFor(place.Bounds.X, place.Bounds.Y)`.

- [ ] **Step 3: Run the smoke test**

Expected: PASSED, `massing    50 buildings shaped`. A throw inside `Extras` is exactly what
this assertion exists to catch — the geometry is index arithmetic and compiling proves nothing.

- [ ] **Step 4: Look at it**

Re-render and open `noon-overview`. St Anne's must have a tower with a spire, visibly taller
than everything else in the village. If the tower is inside the nave rather than at its end,
the door-distance test is inverted.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Unity/Massing
git commit -m "The church gets a tower and spire, the school a bell-cote, the mill a lucam"
```

---

## Task 7: The remaining readers of the old constant

**Files:**
- Modify: `Assets/Noir/Unity/Frontage.cs:277-278`
- Modify: `Assets/Noir/Unity/XRay.cs:108`

- [ ] **Step 1: Fix the door head**

`Frontage.cs` builds the panel above a door from `Space3D.WallHeight`. On a 5.5 m church wall
that leaves a 3.45 m hole above the door. Both lines take the place's eaves instead:

```csharp
float eaves = MassingGrammars.Of(place).Eaves;
// line 277:  f.On((DoorHeight + eaves) * 0.5f, -0.5f),
// line 278:  f.Size(1.0f, eaves - DoorHeight, 0.9f), Materials3D.Wall);
```

- [ ] **Step 2: Fix the X-ray outline**

`XRay.cs:108` boxes every building to `Space3D.WallHeight`. Replace:

```csharp
float top = MassingGrammars.Of(place).Eaves;
```

- [ ] **Step 3: Run the smoke test**

Expected: PASSED, and the x-ray line still reports the same restore count it did before
(`N renderers -> M stripped -> N restored`).

- [ ] **Step 4: Commit**

```bash
git add Assets/Noir/Unity/Frontage.cs Assets/Noir/Unity/XRay.cs
git commit -m "Frontage and X-ray read the building's own eaves, not the global constant"
```

---

## Task 8: The `elevations` instrument

**Files:**
- Create: `Assets/Noir/Editor/Elevations.cs`

- [ ] **Step 1: Write it**

A `[MenuItem("Noir/Elevations")]` plus a `Render()` entry point runnable headlessly, following
`Assets/Noir/Editor/Snapshot.cs` for camera and PNG-writing patterns — read that file first and
match it rather than inventing a second way to render.

For each **building** kind present in the village, pick the first place of that kind, frame it
straight-on from the front at a fixed distance, and write `docs/elevations/<kind>.png`.

**No signs, no frontage, no props, no label in the image.** Disable those subtrees before
rendering, the same way `XRay` does. The whole point is that only the silhouette is on trial —
a picture with a pub sign in it does not test whether the pub reads as a pub.

- [ ] **Step 2: Run it**

```bash
"C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe" -batchmode -quit \
  -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.Elevations.Render -logFile elev.log
```

Expected: eleven PNGs in `docs/elevations/`.

- [ ] **Step 3: Take the test**

Look at the eleven images without their filenames and name each one. Write the result into the
commit message honestly — including any you could not name, because those are the ones that
have not worked yet and the whole instrument is worthless if its result is massaged.

- [ ] **Step 4: Commit**

```bash
git add Assets/Noir/Editor/Elevations.cs docs/elevations
git commit -m "elevations: one unsigned, context-free render per building kind"
```

---

## Task 9: Re-baseline and measure

- [ ] **Step 1: Confirm determinism still holds**

Render the snapshots twice, from two separate Unity processes, into different folders and
compare byte-for-byte. All twelve must be identical between the two runs. If they are not,
the wall-owner tie-break is not deterministic — go back to Task 3 Step 1.

- [ ] **Step 2: Re-baseline the twelve**

They will all have changed against the old baseline, once, deliberately. Commit them on their
own so any later snapshot diff is attributable to the change that caused it and not to this one.

```bash
git add docs/snapshots
git commit -m "Re-baseline the twelve snapshots after massing"
```

- [ ] **Step 3: Measure the cost**

```bash
cd C:\SerialKillerGame\tools
dotnet run --project Noir.Bench -c Release
```

Wall runs break at building boundaries now, so renderer and vertex counts are expected to be
up. Record the numbers. If the increase is more than about 15%, say so rather than absorbing
it — the chunking work that got 5,487 renderers down to 1,835 is worth more than a tower.

- [ ] **Step 4: Write it up in `docs/STATE.md`**

A section at the top, in the style of the existing ones: what landed, the before/after renderer
and vertex counts, the result of the name-that-building test including the failures, and
anything left undone.

- [ ] **Step 5: Commit**

```bash
git add docs/STATE.md
git commit -m "STATE: building massing landed"
```

---

## Self-review notes

Checked against the spec:

- Seam, grammar registry, eleven kinds, the `BuildWalls` surgery, the four constant-readers,
  `elevations`, snapshot determinism, smoke-test coverage, and the bench measurement all have
  tasks. No spec section is unimplemented.
- The spec's `RidgeAcross` field is defined in Task 2 and consumed in Task 4's ridge-axis test.
- The spec lists eleven kinds across eight grammars (dwelling and farm share `cottage`; shop,
  post office and surgery share `shopfront`). Task 5 creates seven and Task 2 creates one.
- `AddWall` gains its `top` parameter in Task 3 and no later task calls it, so the signature
  change is contained.
- `AddChimneys` takes `Massing` from Task 4 onward; its only caller is the roof build loop,
  changed in the same task.
