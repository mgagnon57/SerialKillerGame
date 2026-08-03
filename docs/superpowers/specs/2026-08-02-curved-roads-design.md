# Curved roads — Phase A, the core geometry

**Date:** 2026-08-02
**Status:** approved, ready for planning
**Scope of this spec:** Phase A only. Phases B and C are sketched at the end and get their own specs.

## What this is for

Illinois Route 1 — Chicago Street, the town's spine — is drawn as a straight line at `x=750` and
the real road is a curve. It is not a small error and it is not confined to one end of the town:

```
        real centreline        y = -47   x = 299
        (game metres)          y = 1332  x = 747     <- the Attica crossing, the one place we are right
                               y = 2049  x = 881
```

589 metres of east-west travel over the map's height, and **100m of deviation from its own straight
chord**, so it is a genuine curve rather than a tilted line.

**The evidence, from two independent sources.** The county parcels are the Vermilion County
assessor's own cadastral boundaries and know nothing about OpenStreetMap:

| line sampled every 12m, y 760..2100 | points falling INSIDE a county lot |
|---|---|
| the real curved centreline | **0 of 112** |
| our declared straight `x=750` | **95 of 112 (85%)** |

The real road runs down a corridor the lots leave for it. Ours is drawn through the middle of the
town's back gardens. Separately, 14 east-west streets terminate exactly on that curve — including
every street that changes name across Route 1 (Maple/Park Place at x=796, Perry/Gilbert at 822,
Stewart/Stufflebeam at 863, McKibben/McKibbin at 872), which is what pins the road's true position
at each latitude independently of any single measurement.

### Why it was straight, which is the part worth understanding

Not a decision anybody made. `tools/relay-rossville.py` stores every street as **one scalar**:

```python
NS = [                             # north-south streets: (east offset, name, alias)
    (   0, "chicago",    "Illinois Route 1, the Dixie Highway"),
```

A single number cannot curve. The generator drew exactly what it was given. The same file holds the
railway as a real 23-point polyline (`RAIL`), and `tools/rossville-parcels.geojson` holds 811
parcels as 11,758 boundary points — so **three fidelity levels coexist in one generator**, and the
streets got the worst one.

The generator's own comment says why, and names the cause exactly:

> *"A single vertical 'Railroad Avenue' can never be correct everywhere — **the engine's roads are
> axis-aligned only** — so RAILROAD below is a single representative crossing (at Attica's own
> latitude) for siding/elevator placement, and RAIL\_X below is the real thing every BLOCK gets
> measured against so nothing is platted across it."*

The author hit this engine limitation, kept the real polyline as a *placement constraint*, and never
drew it as a road. **This spec removes the limitation.**

**It has already cost the project once.** The comment immediately below records it: *"120 of 469
houses — a quarter of the town — turned out to be beyond the real track for their row, '408 Holmes
Ave' among them, 61m past it. The houses were never wrong; the assumed 900m of clearance was."*
Same failure mode — a real diagonal approximated as a straight one — different feature.

## What's already there

- **`RoadLine`** (`Assets/Noir/Core/World/RoadNetwork.cs`) holds `Centre`, **a single float**: "the x
  of a north-south road, the y of an east-west one". Also `IsNorthSouth`, `IsStraight` (true only
  when every point shares the first point's cross-axis coordinate), and `From`/`To`.
- **`RoadNetwork.Junctions`** pairs lines gated on `ns.IsStraight && ns.IsNorthSouth` against
  `ew.IsStraight && !ew.IsNorthSouth`, and takes the crossing as `(ns.Centre, ew.Centre)`. A bent
  road therefore forms **zero junctions with anything**. `RoadNetwork.At(x,y)` likewise skips any
  line where `!IsStraight`.
- **`LaneGraph`** (`Assets/Noir/Core/World/LaneGraph.cs`) opens its per-line loop with
  `if (!line.IsStraight) continue;` — a bent road gets **zero lane segments**, so no traffic can
  ever be placed on it. Its `LaneSegment.FromS`/`ToS` are already a 1-D "travel coordinate" that
  rises along the direction of travel; only the mapping back to a village coordinate
  (`AlongOf`, plus `line.Centre`) assumes axis alignment.
- **`Heading`** is a 4-value enum (North/South/East/West). Turn legality goes through
  `Headings.Between(from, to)`, which is pure enum arithmetic.
- **`Vec2`** (`Assets/Noir/Core/Contracts/Vec2.cs`) is Core's float 2-vector, with only `+ - *`,
  `LengthSquared` and `Lerp`. Its header states the constraint this whole design must respect:
  *"Transcendentals (Sin/Cos/Exp/Log) are banned in Core because their results are
  implementation-defined and have changed between .NET runtimes — that would silently break replay
  on a runtime upgrade. **A build-time test greps for them.**"*
- **`MapFeatures.Smoothed`** (`Assets/Noir/Unity/MapFeatures.cs`) is already the curve this project
  draws real survey data with — Catmull-Rom, `SmoothSteps = 4`, endpoints clamped to themselves,
  *"every original point is still on the curve exactly where it was"*. It is used for the railway by
  `CityRailBed`, `CityOutlines` and `GroundShot`. It lives on the Unity side and uses
  `UnityEngine.Vector2`/`Mathf`, so Core cannot reach it and `dotnet test` cannot exercise it.
- **The content format already supports a curve.** `VillageParser` parses
  `road <name> <width> <x,y> <x,y> ...` over arbitrarily many points and only refuses fewer than
  two. No content-format change is needed at any point in this work.
- **13 files** read `IsStraight` or `IsNorthSouth`: `RoadNetwork`, `LaneGraph`, `CityTraffic`,
  `CityStreets`, `CitySignals`, `CitySigns`, `CityOutlines`, `CityPowerlines`, `CityStory`,
  `CityDistrict`, `CityUnderTest`, `TrafficPlayTests`, `TrafficDiagnostics`.

## Architecture

Five pieces. All of it in Core, all of it exercised by `dotnet test` with no editor involved.

### 1. `RoadPath` — the centreline primitive

New: `Assets/Noir/Core/World/RoadPath.cs`. It is the generalisation of the scalar `Centre` from
"a constant cross-coordinate" to "a position that varies along the road".

```
float Length                            arc length of the whole centreline
Vec2  PointAt(float s)                  position at arc length s
Vec2  TangentAt(float s)                unit direction of travel at s
Vec2  NormalAt(float s)                 right-hand normal, (-ty, tx)
(float s, float lateral) Project(Vec2)  inverse: nearest point on the path
bool  IsStraightAxisAligned             the exact fast path — see §2
```

Built by smoothing the declared polyline (§3) and resampling it to a dense polyline with a
cumulative arc-length table, so `PointAt`/`Project` are a binary search plus a lerp. **Resampling
pitch is 1 metre**, matching what `CityRailBed` already resamples the rail bed at — chosen so a long
straight and a tight bend are built to the same resolution. Only curved roads pay for it; straight
ones never reach the resampler (§2).

A lane's centre at arc length `s` and lateral offset `d` is `PointAt(s) + NormalAt(s) * d`. That one
expression replaces every `line.Centre ± offset` in the codebase.

### 2. The zero-regression guarantee

**`RoadPath` short-circuits.** When the declared polyline is exactly two points sharing an axis —
which is all 27 roads in the current `city.txt` — it does no smoothing, no resampling and no table
lookup. `PointAt` is exact linear interpolation on the declared coordinates and `TangentAt` returns
a cardinal unit vector, so the numbers are the ones the current code produces, not merely close to
them.

This is the property that protects the existing city, the traffic tests and the committed snapshots,
and it is directly assertable — see Testing, test 1. `RoadLine` therefore **keeps** `Centre`, `IsNorthSouth`,
`IsStraight`, `From` and `To`, meaning exactly what they mean today, and *gains* `Path`.

`RoadPath.IsStraightAxisAligned` and `RoadLine.IsStraight` are the same predicate — declared as two
points sharing an axis — evaluated in two places. `RoadLine.IsStraight` becomes a delegation to the
path so the two can never disagree; it keeps its name because 13 files read it.

**Blast radius in Phase A: Core only, plus one line of Unity.** Every changed file is under
`Assets/Noir/Core/World/`, with the single exception of `MapFeatures.Smoothed` becoming a delegation
(§3). None of the 13 consumers is touched.

### 3. One Catmull-Rom, in Core

The spline moves from `MapFeatures.Smoothed` into Core as `RoadPath`'s smoothing step, rewritten
against `Vec2` instead of `UnityEngine.Vector2`. `MapFeatures.Smoothed` becomes a thin delegation so
the railway keeps drawing the identical curve it draws today.

Two reasons beyond tidiness. It gets the project's curve arithmetic under `dotnet test`, where it has
never been. And it stops a **third** copy of real-geometry handling appearing — there are already two
(`MapFeatures.Smoothed` for drawing, the `RAIL` polyline in `relay-rossville.py` for placement) and
they are the reason Route 1's curve went missing in the first place.

Catmull-Rom is `+ - * /` only. It cannot violate the transcendental ban.

### 4. Junctions become real crossings

`Junction` today is `(ns.Centre, ew.Centre)` — the only crossing two axis-aligned lines can have.
It becomes the actual intersection of two paths, found by segment-against-segment intersection over
the dense polylines, carrying:

- the crossing point,
- **the arc length along each road** (`SA`, `SB`) — what `LaneGraph` needs to cut lanes,
- the tangents at the crossing, for classifying the turns through it.

The `IsStraight`/`IsNorthSouth` gating is removed, so curved roads form junctions at all. For two
axis-aligned straights this reduces to exactly `(ns.Centre, ew.Centre)`.

**Two real generalisations fall out and must be handled, not assumed away.** A pair of roads may now
cross more than once, so `Junctions` is a list per pair rather than a single entry; and a crossing
may be oblique, so `Junction` cannot assume a square reach. Junction *visuals* still snap to 90°
(the bought road kit has no oblique junction piece) — that is a Phase B rendering concern, and the
true angle stays in Core regardless.

**`RoadNetwork.At(x, y)`** — "which road covers this point", asked by the zoning and lighting passes —
is the other casualty of the straight-line assumption, and it is fixed by the same primitive. It
becomes `Path.Project(point)`: a point is on the road when its `lateral` is within `HalfWidth` and
its `s` lies inside the run. For an axis-aligned straight this is arithmetically the current
`Math.Abs(across - line.Centre) > line.HalfWidth` test, so it too is covered by the equivalence
assertion in Testing, test 1.

### 5. `LaneGraph` in arc length

`LaneSegment.FromS`/`ToS` stop being "travel coordinate derived from a village coordinate" and become
**arc length along the path**, direction-signed exactly as now. Junction stops come from
`Junction.SA`/`SB` instead of `junction.X`/`Y`. The `if (!line.IsStraight) continue` guard goes.

`Heading` survives as a **coarse cardinal classification**, derived from the tangent at the segment's
midpoint — exact for a straight road, and the nearest cardinal for a curved one. It is still what
`CitySignals` groups phases by and what `CitySigns` faces signs with, and those remain meaningful for
a gently curving road.

Turn legality stops going through the enum and is computed from the tangents at the crossing:

- **sign of the cross product** `tIn × tOut` → left or right,
- **dot product** `tIn · tOut` → straight (near +1) or a U-turn (near −1, not offered).

For axis-aligned roads these yield precisely the results `Headings.Between` yields today. The
existing lane rules on top of that classification — left turns leave from lane 0, right turns from
the outermost, straight stays in its lane — are unchanged.

### Determinism

Everything above is `+ - * /` and `sqrt`. No `Sin`, `Cos`, `Atan2`, `Exp` or `Log` enters Core, so the
build-time grep test keeps passing and replay is unaffected. `sqrt` is admissible and is not a
transcendental: IEEE-754 requires it to be correctly rounded, so it is bit-identical across runtimes
in a way `Sin` is not. Arc length needs it; nothing else does. **Angles are never materialised** —
left/right/straight is a cross and a dot, never an `Atan2`.

## Sequencing constraint — Phase A must not put a curve in the map

Phase A delivers the *capability* and its tests. It must **not** change `Content/city.txt` to declare
a curved Route 1.

The reason is the `IsStraight` guard. Until Phase B migrates the 13 consumers, `CityStreets`,
`CitySigns`, `CityStory`, `CityPowerlines`, `CityOutlines` and `CityDistrict` all still skip any line
where `!IsStraight` — so a curved Route 1 introduced during Phase A would rasterise into the terrain
and form junctions and lanes, while being invisible to half the renderers. A road with traffic on it
and no asphalt drawn under it is a worse state than the straight road we have now.

Curved geometry enters the map in Phase C, after Phase B has migrated the consumers.

## Testing

Core tests, `dotnet test`, no Unity. Run in Release — see the CPU note in `docs/STATE.md` before
believing any intermittent Debug failure.

1. **Equivalence, per road.** For all 27 roads in the real `Content/city.txt`, assert
   `Path.PointAt`/`TangentAt` reproduce the existing `Centre` + `AlongOf` results exactly, and that
   `IsStraightAxisAligned` is true for every one of them. This is the guarantee in §2, asserted
   against real content rather than a fixture.
2. **Golden baseline.** Record the current lane-segment count, turn count, entry count and junction
   count for the real city, and assert the rewritten `LaneGraph` and `RoadNetwork` produce the same
   values and the same per-segment geometry. The counts are *recorded from the current build* as the
   first step of implementation, not quoted from documentation — `docs/STATE.md`'s figures predate
   the present map.
3. **Curve fixtures.** A synthetic curved road: junctions found at the right arc lengths, lane
   centres exactly `HalfWidth` from the centreline measured *perpendicular* to it, turns classified
   correctly, no segment of negative length.
4. **The real Route 1 polyline** as a fixture, asserted to produce a sane lane graph — every segment
   positive length, every junction on the road, every arrival having a legal way out.
5. **Two roads crossing twice**, which the current model cannot represent at all.
6. **The transcendental grep test must still pass**, and should be extended to cover the new file.

## Risks

- **`Heading` becoming approximate.** For a road curving through more than ~45° the cardinal
  classification stops being meaningful and signal phasing would group approaches oddly. Route 1
  turns ~15° over its whole length, so this is not a live problem, but it is a real limit of keeping
  the enum and should be recorded rather than discovered later.
- **Multiple crossings per road pair** changes a shape several call sites assume is single-valued.
  Phase A must return a list; Phase B has to handle it in the renderers.
- **The dense resampling pitch** is a cost/accuracy trade (`CityRailBed` already resamples at 1m for
  the rail bed). It needs choosing deliberately and stating, not inheriting by accident.
- **Junction reach on an oblique crossing** is no longer simply half the wider corridor. Phase A
  should define it honestly even though only Phase B renders it.

## Phases B and C — sketch only

**Phase B — the consumers.** Migrate the consuming files from `Centre`/`IsNorthSouth` to `Path`.

> **CORRECTION, from Phase A's final review: it is 24 files, not 13.** The count in "What's
> already there" above was taken from a grep that missed the editor tools and several renderers.
> Also on the list: `Editor/MapAudit.cs`, `Editor/TrafficCheck.cs`, `Editor/Snapshot.cs`,
> `Unity/PlanLabels.cs`, `Unity/Player.cs`, `Unity/SunRig.cs`, `Unity/VillageAudio.cs`,
> `Unity/OrbitCamera.cs`, `Unity/Massing/MassingExtras.cs`, and — most consequentially —
> **`Unity/StreetAddressing.cs`, which derives house numbers from `IsNorthSouth`/`Centre`.**
> That is the file that assigns **408 Holmes Ave**. Migrating it is the step where the killer's
> address can silently move, so it wants its own verification, not a sweep.

**Three curve-only defects Phase A found, fixed, and deliberately did NOT finish** — all invisible
to the golden baseline, because Phase A ships no curve and the baseline only sees straight roads:

- **`Heading` is still derived from `line.IsNorthSouth`, not from the tangent.** §5 above says it
  should come from the segment's local tangent; `LaneGraph` still labels segments from the whole
  road's dominant axis. Harmless for a 15° Route 1, wrong for any segment whose local tangent
  leaves its road's dominant axis.
- **A curve declared in decreasing coordinate order inverts its segments' travel direction.**
  Phase A fixed the *tangent* flip for this case, but the `Way` label and the `From + s` mapping
  were not fixed, and the test written to guard it compares only a sorted multiset of
  `Way→Way:Kind`, which is invariant under exactly that inversion — so it passes while blind.
  In Phase B this feeds `Headings.Side` and would put lanes in the oncoming carriageway.
- **An oblique crossing near a sharp bend can land in the Straight dead-zone** (`|cross|` inside
  the ±0.3 band) and then be dropped by the same-line `legal` check.

Both of the first two need the same decision — *what does `Way` mean on a curve?* — which is
renderer/traffic work, not geometry work, and so belongs here rather than in Phase A.

`CityStreets` lays its 30m/10m prefab tiles along the path at arc-length intervals, each yawed to the
local tangent (Route 1 turns ~15° over ~2.2km, so roughly 0.2° per 30m tile — the wedge gaps on the
outside of the curve are negligible). Junction tiles snap to 90°. `CityTraffic` takes vehicle
positions from `PointAt`/`NormalAt`.

**Phase C — one generator, all layers, re-calibrated.** Two blockers found while investigating, both
of which belong to C and neither of which blocks A:

- **`Content/parcels.txt` cannot be regenerated.** No script in the repository writes it — the
  geojson is committed, the converter never was. It has to be written before parcels can participate
  in a re-derivation.
- **The coordinate frame is not actually shared.** `Content/parcels.txt` records a **+1.81°**
  rotation; a least-squares fit of the OSM street grid gives **+2.28°** — about 8m per kilometre.
  Worse, the +1.81° was derived by rotating the parcels *to match the assumed-straight street grid*,
  i.e. calibrated against the model this spec exists to correct. It must be re-derived against real
  road geometry.

C then moves the street tables from scalars to real OSM polylines and regenerates `city.txt`, with
the corridor-versus-lots test (0 of 112) as the acceptance gate. **`408 Holmes Ave` must survive the
rebuild as a real addressed place** — it is the first fixed story anchor, and it is one of the
addresses the last geometry correction moved.

## Decisions taken

| Question | Decision |
|---|---|
| Curve model | True smooth curve (spline), not a chain of straight segments |
| Generality | A general engine capability — any road may curve, not a Route 1 special case |
| Curve source | Re-queried from OpenStreetMap (way 22037977, `ref=IL 1`), cross-checked against county parcels |
| Oblique junctions | Visuals snap to 90°; the true angle is kept in Core |
| Structure | Three phases, each with its own spec, plan and implementation cycle |
| Data scope (Phase C) | Full re-derivation from source, one generator, all layers, re-calibrated |
| The rail spline | Moved into Core and shared, rather than duplicated |
