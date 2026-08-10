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
- **`SEEN-2`** — settle what the brown bands in the owner's screenshot actually are. Two candidates
  and the audit cannot separate them from source: exposed ground where a flat slab cuts a slope
  (`TILT`), or `GroundZoning` painting Agricultural/Vacant parcels with hard edges (`BLEND`).
  **Both are real; which one dominates decides whether W2 or W4 is worth more.**

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

### W4 — The ground stops looking like a diagram · medium

`BLEND-1` `BLEND-2` `ONE-1`

- **`BLEND-1`** — a transition where two ground looks meet: a blend band, a broken edge, or noise on
  the boundary. Cheapest honest version first, measured by looking at it.
- **`BLEND-2`** — `GroundZoning`'s parcel-shaped patches want their edges softened *specifically*,
  because a lot line is a legal fiction and not something you can see from a car.
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
