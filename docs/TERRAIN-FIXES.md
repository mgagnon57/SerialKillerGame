# Terrain fixes — the work list

**This is a work file. Delete it when the last item lands.** A read-only audit on 2026-08-10, after
the owner reported that the roads do not line up with the ground. The facts live in `CLAUDE.md`;
this is a queue.

**Sibling plans:** `docs/ANIMATION-FIXES.md`, `docs/ROAD-FIXES.md`, `docs/SIM-FIXES.md`,
`docs/TEST-FIXES.md`. `docs/TEXTURE-FIXES.md` is finished and deleted (2026-08-09).

**Item IDs:** `TILT` nothing in the town is ever tilted · `GRADE` there is no grading pass ·
`BLEND` the ground has no transitions · `ONE` three places decide one surface · `SEEN` what
sixteen hours of un-hiding means.

---

## ⚠ Two corrections before anything else

**1. The relief is 24.5 m (80 ft), not 195 m.** Commit `f191e75` and `SIM-FIXES.md` both recorded
"5,754 samples, 30.00 m to 225.30 m — 195.3 m of relief". The measuring script ate the data
header. `Content/elevation.txt` opens its grid with `grid 71 81 30`; **71 × 81 = 5,751**, and the
three extra "samples" are the column count, the row count and the 30 m step — which is exactly
where the 30.00 m minimum came from. The file's own header states the real range, **200.8 m to
225.3 m**, and adds: *"it is not flat, but it is close, and anything that looks like a hill here is
wrong."* `ElevationGrid.cs:116` parses the header correctly, so **the game was never wrong — only
the number written about it was.** Both homes are corrected; the commit message cannot be.

**2. This is not a regression, and nothing broke last night.** Every fault below has been in the
code for as long as the code has existed. It was invisible because the ground was a perfect plane:
`elevation.txt` sat behind a bare `catch { return; }` until **01:10 on 2026-08-10**, and every
slope test read zero. The town has had real ground for sixteen hours. **What changed is not the
roads — it is that the ground stopped hiding them.**

---

## The owner's ruling, 2026-08-10

**Engineer the corridor.** Grade the ground to a survivable gradient (cut and fill), *then* pitch
the surface to follow what was graded — rather than only tilting tiles onto raw ground, which still
kinks at every seam, or only flattening the ground, which leaves the surface a decal.

That is `GRADE` then `TILT`, in that order, and it is why `TILT` is not simply done first because
it is cheaper: tilting onto ungraded ground is work that the grading pass then throws away.

---

## The findings

### `TILT` — nothing in the built town is ever tilted, and it never has been

Every object the town is dressed with is placed **flat**, at a **single point sample** of the
ground under its centre:

```
CityStreets.cs:908    go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
CityStreets.cs:920    float groundY = parent.position.y + ElevationGrid.HeightAt(mx, my);
CityStreets.cs:947    Quaternion.Euler(0f, yaw, 0f)      :967   HeightAt(x + w/2, y + h/2)
CityStreets.cs:1294   Quaternion.Euler(0f, yaw, 0f)
CityBuildings.cs:717  pos.y = ElevationGrid.HeightAt(lot.X + lot.W/2f, lot.Y + lot.H/2f);
CityBuildings.cs:972  float groundY = ElevationGrid.HeightAt(lot.X + lot.W/2f, lot.Y + lot.H/2f);
```

**Yaw only. Zero pitch, zero roll.** Twenty-one files under `Assets/Noir/Unity` sample
`ElevationGrid.HeightAt`, and **not one of them ever applies a pitch or a roll**. The only non-yaw
rotations anywhere in the Unity layer are people's limbs (`AgentFigure`), one signal head
(`CitySignals:430`), the camera, the player and the sun.

Meanwhile the ground itself is built properly, from **four corner heights per quad** —
`VillageMesh` samples `h00, h10, h01, h11` and follows the relief. So the ground follows the land
and everything standing on it does not.

**The magnitude, measured against the real grades.** Rossville's median grade is 1.6% and its 90th
percentile is under 4% (`GroundZoning.cs:28-33`, measured against `elevation.txt` on 2026-08-02):

| Grade | Over one 30 m (100 ft) main-road tile | At each seam |
|---|---|---|
| median 1.6% | 0.48 m | **±9 in** |
| 90th pct 4% | 1.2 m | ±2 ft |
| bank 12% | 3.6 m | ±6 ft |
| steepest, 0.1% of the map, 16% | 4.8 m | ±8 ft |

A nine-inch lip at every hundred feet of road is exactly the look reported. The creek banks are
where it becomes a wall.

### `GRADE` — there is no grading pass anywhere in the pipeline

Nothing in `TownPipeline` moves earth. The five survey passes place and remove buildings; none of
them cuts a corridor. A real road is built on an engineered bench — cut into the high side, filled
on the low, held to a gradient a vehicle can climb — and the town has no such stage. This is the
substance of the owner's ruling and the largest item here.

### `BLEND` — the ground has no transitions at all

`VillageMesh.cs:629-638` assigns **one submesh per tile**, hard:

```csharp
submeshGrid[gy, gx] = (terrain == Terrain.Grass || terrain == Terrain.Field)
    ? SubmeshForLook(GroundZoning.LookAt(world, gx, gy, terrain, h00, h10, h01, h11))
    : SubmeshFor(terrain);
```

There is no blend weight, no transition band and no noise on the boundary anywhere in the mesher.
So every change of surface is a **hard polygon edge on a one-metre grid**, and because
`GroundZoning` decides by *parcel polygon*, those edges are lot-shaped: a rectangle of bare brown
earth against turf, with a seam you could cut yourself on.

**`GroundZoning`'s own header names this as the thing it exists to avoid** — *"A flat farm town
with a slope map painted over it would be exactly the 'debug colour' look this stream exists to
avoid."* The slope test is careful. The **edges** are what make it read as a debug overlay.

### `BANK` — every shoreline in Rossville is a 35 cm wall following a staircase

**Found by looking at `city-block.png` on 2026-08-10.** The two ponds on the west side are the most
visible straight lines on the ground.

> ⚠ **That frame PREDATES `6008587` — it was rendered at 21:37, before `GroundBlend` existed, and
> nothing has re-rendered Rossville since.** So "with the parcel patches quietened" is not
> something anybody has seen; it was inferred. **The finding below does not rest on the image**,
> which only pointed at the ponds: the cause is read out of `features.txt`, `relay-rossville.py`
> and `VillageMesh`'s riser pass, and `GroundBlend` excludes Water by design, so the blend could
> not have changed these edges either way. Re-render before quoting the frame for anything else.

The cause is a rasterisation, and it is three steps with nothing wrong at any one of them:

1. `Content/features.txt` carries the **real** North Fork Vermilion and the real school ponds —
   surveyed OpenStreetMap polygons, with curves.
2. `tools/relay-rossville.py` rasterises those polygons into `city.txt` as **1,009 axis-aligned
   `terrain water` rectangles** on the one-metre grid (44,866 tiles, 44,073 surviving into the
   world; the difference is road crossings correctly overwriting it).
3. `VillageMesh` seats water at **−0.35 m** and closes the bank with a **riser** at every
   terrain-type boundary — a vertical face, in the submesh of the higher surface.

So the shoreline the game draws is a 35 cm vertical wall following an axis-aligned staircase,
while `CityOutlines` draws the true curve from the same `features.txt` into the plan view. **The
plan and the ground disagree about the shape of the water**, and the plan is the one that is right.

> ⚠ **DO NOT FIX THIS BY MOVING WATER TILES, and `GroundBlend` must not be pointed at it.** The
> tile grid is what `BlocksSight` and walkability read, and the real-water commit `b9c1271` is
> explicit that build order — terrain, then roads, then place ground — is what makes the four real
> road crossings free. Wandering the water boundary the way the grass now wanders would put open
> water under the path across the school field, which is the exact bug that commit fixed.
> `GroundBlend.Soft` admits Grass, Field and Rough only, and that guard is load-bearing.

### `ONE` — three separate places decide what one lot's surface is

`GroundZoning.cs` says so in its own comment, at the point of decision:

> *"One property, one surface — the third of the three places that make this decision, with
> `VillageMesh.ZoningMask` and `ZoningPatch.ZoningOf`."*

Three implementations of one rule, and the comment exists because they had already disagreed once.
This is the same shape as the `frontage`/`massing` drift in `TEST-FIXES` W1 and wants the same
remedy: one home, and a gate that fails when a second appears.

### `SEEN` — the town you have been looking at was not this town

`elevation.txt` was not alone behind that `catch { return; }`. Five more content loads failed
silently until 01:10 on 2026-08-10:

| File | What was missing until this morning |
|---|---|
| `parcels.txt` | **no lot lines at all** — so nothing downstream could resolve a parcel, including every ruling in `parcel-1991.txt`, which is keyed on one |
| `parcel-buildings.txt` | the 824 measured footprints; every building fell back to a generated box and **the survey layer quietly stopped existing** |
| `parcel-county.txt` | 4,534 lines of zoning; every lot drew as whatever the map fiction said |
| `parcel-notes.txt` | none of the author's notes were in the town |
| `placement-1991.txt` | **every house the owner moved was back where the generator put it** |

**Every visual judgement made about this town before 2026-08-10 01:10 was made about a different
one** — flat, with generated footprints, no real lot lines, no zoning and none of the owner's
placements. That includes every render in `docs/snapshots/`, the texture pass's before-and-afters,
and any "it looks wrong" recorded earlier. **Do not trust an old picture, and do not chase a fault
recorded against one.**

---

## The waves

### W1 — Look at the town that exists now · half a day · one Unity window

`SEEN-1` `SEEN-2`

**Nothing here fixes anything, and it comes first anyway.** Sixteen hours ago the town gained real
ground, real lot lines, 824 measured footprints, real zoning and the owner's own placements, all at
once. No render in the repository shows that town.

- **`SEEN-1`** — re-render the standing snapshot set and diff it against what is committed. The
  render run rewrites `docs/snapshots/**` anyway, which is why the standing rule says never
  `git add -A`.
- ✅ **`SEEN-2` — CLOSED by the owner, 2026-08-10: the brown is un-blended zoning, not floating
  slabs.** So **`BLEND` is the visible fault and it goes first** — the waves below are re-ordered
  for it. `TILT` and `GRADE` are real and stay, but at the town's median grade they are a nine-inch
  lip, not what anybody is looking at.

  **And half of that answer was already fixed the day before, by his own ruling.** `Vacant` does
  **not** draw brown any more. `Materials3D.cs:570`: *"Vacant lots: A LOT MOWED ONCE A SUMMER IS
  GRASS, NOT PLOUGHED EARTH. Owner's ruling 2026-08-09, made on `suburb-block.png`, where almost
  every lot carried a bare red-brown rectangle."* `GroundLook.Rough` is grass at twice the tiling,
  so it reads as rank uncut growth. That was **118 parcels** — by far the larger half.

  **What is still brown, measured from `Content/parcel-county.txt`:**

  | Class | Count | Zoning | Draws as |
  |---|---|---|---|
  | 0040 Improved Residential Lot | 517 | Residential | grass |
  | 0030 Vac Lots-Lands | 106 | Vacant | **grass** — ruled 2026-08-09 |
  | 0060 Commercial | 58 | Commercial | Hard, concrete |
  | 0090 Tax Exempt | 52 | Civic | grass |
  | **0021 Agricultural** | **16** | **Agricultural** | **ploughed earth** |
  | 0032 Vacant | 12 | Vacant | **grass** — ruled 2026-08-09 |
  | 0080 / 5060 Industrial, Railroad | 4 | Industrial | Hard |
  | 0050 Commercial >6 units | 4 | Residential | grass |
  | 0011 Homesite-Dwelling | 3 | Residential | grass |

  So the brown is **16 Agricultural lots**, plus every tile the map fiction calls `Terrain.Field`
  that stands on **no parcel at all** — which is the countryside, and correct.

- ✅ **`SEEN-3` — CLOSED by the owner, 2026-08-10: all 16 stay ploughed.** Asked with the render in
  front of him, on the frames where the orange rectangles are plainly visible (`farm-country`,
  `city-block`), and answered the opposite way to the vacant-lot ruling: county class 0021 draws
  ploughed earth, inside the town as well as at its edge. **So the argument that carried the 106
  vacant lots does not carry these**, and nobody should re-run it — the county says agricultural
  and the ground says agricultural.

  ⚠ **This makes `BLEND-2` load-bearing rather than optional.** The 16 patches are staying brown,
  so the only thing that can stop them reading as a zoning diagram is their EDGE.

> **RE-ORDERED 2026-08-10 after the owner settled `SEEN-2`.** The visible fault is `BLEND`, so
> **W4 below is now the first wave of real work** and W2/W3 follow it. The grading ruling still
> stands and its internal order — grade, then pitch — is unchanged; it is simply not the thing
> anybody is looking at. Read W4 first.

### W2 — Grade the corridor · the ruled approach, step 1 · large

`GRADE-1` `GRADE-2` `GRADE-3`

- **`GRADE-1`** — a grading pass in `TownPipeline`, after `SurveyRoads` and before the world is
  built, that writes a graded surface for every road corridor: cut on the high side, fill on the
  low, held to a maximum gradient.
- **`GRADE-2`** — the gradient ceiling is a **decision, not a constant to invent**. A 1991 Illinois
  county road is not a mountain pass; the town's own steepest ground is 16% and that is a creek
  bank, not a street. Bring a measured proposal rather than a number.
- **`GRADE-3`** — banks and embankments are the visible product of grading and want a surface of
  their own. `GroundLook.Bank` already exists and is already wired.

⚠ **This moves the ground under everything already seated.** It must land before `TILT`, and
anything holding a cached ground height needs re-seating after it — including `CityCollision`,
which now samples the same `ElevationGrid` the visual ground uses and would otherwise disagree
with what you can see.

### W3 — Pitch what stands on it · the ruled approach, step 2 · medium

`TILT-1` `TILT-2` `TILT-3`

- **`TILT-1`** — road tiles take a pitch and roll from the graded surface under their own footprint,
  not from one centre sample. Three seating sites: `CityStreets.cs:908`, `:947`, `:1294`.
- **`TILT-2`** — buildings are the harder call and want the owner. A house does **not** tilt; it
  sits level on a foundation, and the ground is cut to meet it. So `CityBuildings.cs:717/:972`
  wants a **levelled pad**, not a pitch — which is grading again, per building.
- **`TILT-3`** — the rest of the furniture. A lamp standard, a sign and a bin stand plumb whatever
  the ground does; a driveway, a parking bay and a rail bed follow it. **Sort the 21 files into
  those two lists before writing anything** — half of them are already correct by doing nothing.

### W4 — The ground stops looking like a diagram · **DO THIS FIRST** · medium

`BLEND-1` `BLEND-2` `ONE-1`

**The owner settled `SEEN-2` on this wave's side: the brown bands are un-blended zoning.** Every
change of ground surface in Rossville is a hard polygon edge on a one-metre grid, and where that
edge follows a parcel it is a hard-edged rectangle in a green field. A lot line is a legal fiction
— it is not a thing you can see from a car — and drawing one as a visible seam is what makes the
map read as a diagram.

> ⚠ **THE SEAM IS A CHANGE OF TILING, NOT A CHANGE OF COLOUR, AND THIS PARAGRAPH USED TO SAY
> "PLOUGHED EARTH IN A GREEN FIELD".** Measured in `Materials3D` on 2026-08-10:
> `GrassEverywhere` (default **true**) binds the SAME grass sheet to Grass, Field, Wood, Path,
> Hard and Bank, at different tilings — Field at 9f, everything else at 4f, `Rough` at more than
> twice the tile so it reads as rank growth, Churchyard four levels lighter off the same sheet.
> **So there is no brown ground anywhere in Rossville today**, and a lot patch shows as a
> rectangle of differently-scaled grass rather than as a different surface. It is quieter than the
> plan describes and it is still a rectangle with corners.
>
> **The orange rectangles in `farm-country` and `city-block` are NOT ground.** They are wheat:
> `CityFarm` tiles `Wheat_*_Square_1x1m` prefabs across every `cornfield` place. A field of ripe
> wheat with a straight edge is what Illinois looks like — do not "fix" it.

- ✅ **`BLEND-1` and `BLEND-2` — LANDED 2026-08-10, commit `6008587`.** `GroundBlend` asks the
  survey's question a few metres away instead of at the tile itself, so a soft-to-soft boundary
  wanders instead of following the ruled line. The tile, vertex and draw-call counts are all
  unchanged, which is why it prints its own `[blend]` line — nothing else in the ground's output
  moves.

  **Rossville: `[blend] 19,280 tile(s) took a neighbouring surface`**, read out of the running
  editor in play mode on 2026-08-10.

  > ⚠ **`Noir → Render Snapshots` DOES NOT RENDER ROSSVILLE, AND THE FIRST NUMBER WRITTEN HERE WAS
  > THE FIXTURE VILLAGE'S.** This item first recorded **624 tiles**, measured off a
  > `Noir/Render Snapshots` run. `Snapshot.cs:122` reads **`fixture-village.txt`** and builds it
  > with **`TownPipeline.BuildUnsurveyed`** — its own comment says so and says not to "fix" it,
  > because the survey passes are keyed to the real town's parcel ids and would silently do nothing
  > on a fixture. 25,200 tiles against Rossville's map; 624 against 19,280. **The two are not the
  > same town and never were**, which is `SEEN` again, one directory over.
  >
  > **The renderer that builds the real town is `CityShot.cs` — `TownPipeline.Build()` at line 160.**
  > It writes `city-*`, `farm-*`, `country-*`, `suburb-*` and `block-yard`. `Snapshot.cs` writes the
  > other twelve: `back-lane-night`, `close-terrace`, `dusk`, `first-light`, `mill-gate`,
  > `morning-long`, `night`, `noon-overview`, `school-run`, `street-night`, `street-noon`,
  > `the-crowd`. **Those twelve are the fixture village. Do not read a Rossville verdict off any of
  > them.** The scale gap is the tell: Rossville meshes **5,040,000 tiles**, the fixture 25,200.
  >
  > ⚠ **AND FROM BATCH MODE IT MUST BE `CityShot.RenderBuiltNoon`, NOT `RenderNoon`.**
  > `VillageHost.ShowBuildings` defaults OFF, and `CityShot` builds `CityStreets`, `CityBuildings`,
  > `CityFarm` and **`CityGreenery` only when it is true** — so `RenderNoon` headless photographs
  > the survey PLAN: outlines on grey, no ground, no props, no reeds. `RenderBuiltNoon` exists for
  > this and its own comment names it *"exactly the trap that hid the porch"*. It was walked into
  > again on 2026-08-10, one render after the `Snapshot`/`CityShot` mix-up — **the same fault twice
  > in an hour**, and the shape is always: the render succeeded, wrote its files, logged no error,
  > and could not show the thing being verified.
  >
  > One thing the plan render IS good for, and it is where `BANK` was confirmed: `CityOutlines`
  > draws the ponds from `features.txt` as **visibly curved, organic polygons**. Put that frame next
  > to the ground's axis-aligned staircase and the disagreement is the whole finding in one picture.

  > ⚠ **STILL NOT VERIFIED BY EYE ON ROSSVILLE.** No render of the real town has been taken since
  > `6008587` landed. The view that would settle it is an eye-level shot at the **Field↔Grass
  > town-edge boundary** — the longest edge on the map — from `CityShot`, ideally twice with
  > `GroundBlend.Enabled` true and false. Until then, 19,280 is a log line, not a verdict.
- **`BANK-1`** — **shelve the bank instead of walling it.**
  > ⚠ **THIS WAS SCOPED AS "cheapest of the three, touches only `VillageMesh`'s riser pass" AND
  > THAT IS WRONG.** Measured against the mesher on 2026-08-10, before building it:
  >
  > - **A riser is vertical by construction.** `VillageMesh.cs:936-939` emits four verts on ONE
  >   (x,z) footprint — two at `low`, two at `high`. Sloping it means moving geometry in x/z.
  > - **`AssertFootprint` fails when geometry leaves its chunk**, and the riser pass's own comment
  >   warns why it must not: a riser filed one chunk out is *"a strip of river bank that goes
  >   missing from precisely the angles the riser exists to cover."* A shore tile on a chunk
  >   boundary would trip it.
  > - **The other route breaks the merge.** `flatGrid` is ONE float per tile, added uniformly to
  >   all four corners, and the greedy run-merge depends on that. Per-corner terrain offsets are an
  >   architectural change to the ground mesher.
  >
  > So this is a **large** item, not a cheap one. Do `BANK-2` and look at the water before deciding
  > whether the geometry is still worth touching.
- ✅ **`BANK-2` — LANDED 2026-08-10, commit `b253163`.** `PropKind.Reed`, scattered in Core by
  `PropGenerator.NextToWater` and drawn by `CityGreenery` from `Nature/Freshwater/Cattail_*` and
  `Water_Grass_Long_*`. Two shore tiles in three. **`NextToWater` is four-neighbour and not eight
  on purpose** — the water is a raster of axis-aligned rectangles, so a diagonal neighbour is a
  staircase CORNER, and counting it plants a reed on the outside of every step and draws the
  staircase in cattail instead of hiding it. No tile's terrain is touched, so walkability and
  `BlocksSight` are untouched by construction.
  > ✅ **LOOKED AT, 2026-08-10 — `docs/snapshots/pond-bank.png` and `pond-close.png`**, the first
  > two frames in this project ever pointed at water. The cattail reads: a broken, gappy fringe on
  > both banks, brown seed heads on green stems, and it does break the shoreline.
  >
  > ⚠ **The first render of it was a pond ringed with KELP**, and the lesson is not about reeds.
  > `Species("Freshwater", "Cattail", "Water_Grass_Long")` picked the second one **by name, out of
  > the folder that looked right, without opening one.** `Water_Grass_Long` is SUBMERGED weed —
  > filed beside the waterlilies because that is what it grows among — and at native scale standing
  > on a dry bank it is a three-metre black frond. Fixed to `Cattail` only in `6713fdf`. **A pack
  > path that sounds right is not evidence**, and `docs/ASSETS.md` cannot tell you what a prefab
  > looks like standing up.
  >
  > Still true: `Reed` inherits the `#if UNITY_EDITOR` gap every tree and bush has, so no shipped
  > player draws one until the cast manifest lands; and the reed COUNT is still unknown, because
  > `CityGreenery`'s log buckets anything that is not a `Bush` as a tree — worth one line to split.
  > **Open ruling for the owner:** `Reed.BlocksSight` is **false**. Head-high cattail arguably
  > should block a witness's line of sight; that is a sim decision, not a rendering one.

- ⚠ **`BANK-1` IS CONFIRMED NEEDED, BY EYE, FOR THE FIRST TIME.** `pond-close.png` shows the 35 cm
  riser as a continuous dark band at the waterline with the staircase visible in it — the steps
  where the raster turns a corner are plainly countable. Planting the edge softened it and did not
  hide it. So the large item above is real work that wants doing, not a nice-to-have; **and there
  is now a frame that can judge whether any attempt at it worked.**
- **`BANK-3`** — **draw the shoreline from `features.txt`, not from the tile boundary.** The honest
  fix: the grid stays authoritative for walkability and sight at one metre, but the visible edge
  follows the surveyed polygon the plan view already draws. Largest of the three and the only one
  that needs the mesher to know about a non-grid shape. **Do `BANK-1` and `BANK-2` first and look
  at it** — if a shelved, planted bank reads as a pond, this is not worth building.
- **`ONE-1`** — collapse `GroundZoning.ZoningAt`, `VillageMesh.ZoningMask` and
  `ZoningPatch.ZoningOf` to one home, with a Core gate that fails when a fourth appears. Same shape
  as `TEST-FIXES` `KEY-1`; copy `EveryActivityHasARowInTheRealFile`.

### W5 — The gates · small, and it goes last

`GATE-1` `GATE-2`

- **`GATE-1`** — no seated object stands more than *n* centimetres clear of, or buried in, the ground
  under any corner of its own footprint. This is the gate that would have caught all of `TILT` on
  the day the ground arrived, and it is measurable in Core against `ElevationGrid`'s data.
- **`GATE-2`** — the relief itself is ratcheted: assert `elevation.txt` parses to 71 × 81 = **5,751**
  samples in the range 200.8–225.3 m. This is a three-line gate and it is the one that makes the
  correction at the top of this file impossible to re-make.

---

## Cross-plan

| Collision | With | Resolution |
|---|---|---|
| `GRADE-1` moves the ground under the road network | `ROAD-FIXES` W8 (the street layer is actually drawn) | **Sequence them.** Grading changes what the street layer is drawn on; doing W8 first means drawing it twice |
| `GATE-1` and `GATE-2` are Core gates on content | `TEST-FIXES` `KEY-1` | Same pattern, same helper. Land whichever comes first and copy it |
| `SEEN-1` rewrites `docs/snapshots/**` | every plan | Never `git add -A`; the standing rule exists for this directory |
