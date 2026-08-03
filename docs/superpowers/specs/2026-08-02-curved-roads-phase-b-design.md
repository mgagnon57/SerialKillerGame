# Curved roads — Phase B, the consumers

**Date:** 2026-08-02
**Status:** approved, ready for planning
**Parent:** `docs/superpowers/specs/2026-08-02-curved-roads-design.md`
**Phase A:** complete and merged to `main` (16 commits, `d66c974`..`66ae7c7`)

## What this is for

Phase A gave `Noir.Core` a road that can bend, and deliberately shipped **no curve in the map** —
because 22 files still skip any road where `!IsStraight`, so a curved Route 1 would have carried
traffic on asphalt nobody drew.

Phase B migrates those consumers and **puts the real road on the ground.** At the end of it,
Illinois Route 1 through Rossville follows its surveyed alignment: the curve that runs down the
corridor the county's own lots leave for it, where the straight `x=750` we ship today puts 85% of
its length inside somebody's back garden.

**Real survey data only.** No straight version of the road is preserved anywhere — not in the map,
not as a test fixture, not as a fallback. Straightness was never a decision anybody made; it was an
artefact of storing each street as a single scalar. Keeping a straight copy for convenience would
re-introduce the fiction this work exists to remove.

## What's already there

**From Phase A, in Core and merged:**

- `RoadPath` (`Assets/Noir/Core/World/RoadPath.cs`) — `Length`, `PointAt(s)`, `TangentAt(s)`,
  `NormalAt(s)`, `Project(Vec2) → (S, Lateral)`, `IsStraightAxisAligned`. Exact linear arithmetic
  for a two-point axis-aligned declaration; Catmull-Rom + 1m resampling + an arc-length table
  otherwise. Trig-free; one `Math.Sqrt`, in `Distance`.
- `RoadLine.Path`, alongside the unchanged `Centre`, `IsNorthSouth`, `IsStraight`, `From`, `To`.
- `Junction` carrying `SNorthSouth`/`SEastWest` (arc length along each road) and
  `TangentNorthSouth`/`TangentEastWest`, found by real path intersection. A pair of roads may cross
  more than once.
- `RoadNetwork.At` answers through `Path.Project`, including at a curved road's ends.
- `LaneGraph` cuts lanes at junction arc lengths and classifies turns from tangents
  (cross-product sign for left/right, dot for straight versus U-turn).
- `MapFeatures.Smoothed` delegates to `RoadPath.Smooth`, so the railway and the roads bend along
  one implementation — verified by byte-identical rail snapshots.
- `CoreDeterminismTests` enforces the transcendental ban that `Vec2` had only ever documented.

**The surveyed alignment**, already used as a Phase A test fixture and the coordinates Phase B
declares in the map — OSM way 22037977, `ref=IL 1`, projected into village metres and rotated into
the parcels' frame:

```
371,177  466,470  593,855  675,1109  747,1332  776,1423
799,1491 839,1607 857,1687 863,1740  872,1876  881,2049
```

**Three defects Phase A found and deliberately did not finish**, because each needs a decision that
belongs here rather than in the geometry:

- `Heading` is still derived from `line.IsNorthSouth` — the whole road's dominant axis — not from
  the segment's own tangent, which the parent spec §5 asked for.
- A curve declared in decreasing coordinate order inverts its segments' travel direction. Phase A
  fixed the *tangent* flip; the `Way` label and the `From + s` mapping were not fixed, and the test
  guarding it compares a sorted multiset that is invariant under exactly that inversion.
- An oblique crossing near a sharp bend can land in the Straight dead-zone (`|cross|` inside ±0.3)
  and then be dropped by the same-line `legal` check.

**The 22 consuming files**, by how many times each reads the scalar geometry:

```
18  Unity/CitySigns.cs         7  Unity/CityStreets.cs      4  Unity/CityOutlines.cs
14  Editor/MapAudit.cs         7  Unity/CityStory.cs        3  Unity/Player.cs
 7  Unity/StreetAddressing.cs  6  Unity/CityPowerlines.cs   3  Editor/TrafficCheck.cs
 7  Unity/PlanLabels.cs        6  Unity/CityDistrict.cs     2  PlayTests/CityUnderTest.cs
 7  Unity/CityTraffic.cs       1  Unity/SunRig.cs           1  PlayTests/TrafficPlayTests.cs
                                                            1  PlayTests/TrafficDiagnostics.cs
```
plus the Core files and test files Phase A already migrated.

## Architecture

### 1. `Way` becomes a stream label, and geometry always comes from the path

This is the blocker the parent spec flagged twice, and it is smaller than it looked. `CityTraffic`
uses `Heading` for exactly three things:

| use | today | after |
|---|---|---|
| which side of the centre line to drive | `Headings.Side(Way)` × lane offset | `Path.NormalAt(s)` × lane offset |
| position along the road | `LaneGraph.AlongOf(Way, s)` | `Path.PointAt(s)` |
| which streams conflict, and which is oncoming | `Headings.IsNorthSouth`, `Headings.Back` | unchanged — still `Way` |

So `Way` stops being the source of geometry and keeps only its classification job: grouping signal
phases and deciding which streams cross each other. It is **redefined as the cardinal nearest the
segment's own tangent at its midpoint**, which is what the parent spec asked for, and which also
disposes of the declaration-order defect — a tangent does not care what order the points were
written in.

### 2. The migration, in four groups

Twenty-two files, but only four distinct questions being asked of a road. Grouping by the question
keeps each unit reviewable and means a reviewer can reject one group while approving its neighbour.

- **"Where is the road at this point?"** — `StreetAddressing`, `CityStory`, `SunRig`, `Player`,
  `CityDistrict`. All of these compare a point against `Centre`; all become `Path.Project`.
- **"Lay something along it."** — `CityStreets`, `CityPowerlines`, `CityOutlines`, `PlanLabels`.
  These walk a road placing tiles, poles, lines or labels; all become a walk in arc length with
  per-item yaw from `TangentAt`.
- **"Where do streams conflict?"** — `CityTraffic`, `CitySignals`, `CitySigns`. These keep `Way`
  for classification and take every metre from the path.
- **"Audit it."** — `MapAudit`, `TrafficCheck`, `CityUnderTest`, `TrafficPlayTests`,
  `TrafficDiagnostics`. These must learn to measure a curve before they can be trusted to grade
  one — they are the instruments the rest of the phase is judged by, so they move first.

### 3. Laying prefab tiles along a curve

`CityStreets` places 30m (and 10m) road tiles with a yaw of 0 or 90. It becomes a walk along the
path at the tile's own pitch, each tile yawed to the local tangent. Route 1 turns about 15° over
2.2km — roughly 0.2° per 30m tile — so the wedge gaps on the outside of the curve are sub-centimetre
and need no special handling. **A sharper road would show them**, and that is a real limit of laying
straight prefabs along a curve rather than a defect to chase.

**Junction visuals snap to 90°**, as the parent spec decided: the bought kit has no oblique junction
piece. The true crossing angle stays in Core and drives the traffic; only the tile is square.

### 4. The golden baseline is re-recorded, not preserved

Bending Route 1 legitimately changes the counts `RoadGeometryBaselineTests` pins (today
roads=27, junctions=142, segments=620, turns=1692, entries=54, plus a SHA-256 per-segment
checksum). The alarm's value is that it holds a **known-good recorded state**, not that the roads
are straight.

So: bend the road, let the numbers move once, then **re-record them against the curved map and
hand-verify the new set** — the junction count in particular must be checked against the roads the
curve genuinely crosses, exactly as Phase A verified 142 by computing the overlap predicate rather
than trusting the number that fell out. From that point the alarm works normally again.

**Until the re-record lands, the golden baseline is not a safety net**, and the phase is running
without it. That is the accepted cost of putting the curve down first, and it is the reason the
audit instruments move before anything else.

### 5. Snapshots

Every committed render containing Route 1 or anything sited off it legitimately changes. During
this phase **a snapshot diff is not evidence of a defect** — the usual regression signal is
unavailable, deliberately. Snapshots are re-baselined once, at the end, as a single reviewed
commit rather than drifting task by task.

## 408 Holmes Ave

The killer's address is the first fixed story anchor and it must not move.

`StreetAddressing.BlockNumber` does not measure distance from Chicago Street — it **counts how many
cross-street centres a point passes**. For 408 Holmes at village (1175, 1218), Chicago sits at
index 3 in the sorted list of north-south centres and the point at index 7, giving
`|7 − 3| × 100` = the 400 block.

With a curved Chicago at that latitude (x ≈ 708 rather than 750) the *ordering* is unchanged, so
the answer is still 400. **The address survives — but by luck of ordering, not by construction.**
Two requirements follow:

1. A test pinning `StreetAddressing` for 408 Holmes Ave lands **before** the curve does, so it can
   be seen to hold across the change rather than asserted afterwards.
2. `BlockNumber` stops reading `chicago.Centre` — meaningless for a road whose centre varies — and
   asks where Chicago is *at the point's own latitude*, through `Path.Project`.

## Testing

- **The audit instruments first.** `MapAudit` and `TrafficCheck` must measure a curve correctly
  before they can grade one. Until they do, a clean audit means nothing.
- **408 Holmes**, pinned before the curve lands (above).
- **The re-recorded baseline**, hand-verified once against the curved map.
- **PlayMode**, `-assemblyNames Noir.PlayTests` — 13 tests including `NoVehicleEverLeavesTheRoad`,
  which is the single most valuable test in this phase: it caught a van driving through a field on
  its first ever run, and a curve is exactly the thing that could put one there again. Do **not**
  pass `-nographics`; two tests render.
- **Look at it.** `MapAudit` and the tests cannot see ugly. A render at street level and from above
  is part of acceptance, not a nicety.

## Risks

- **The tail of the curve leaves the platted grid.** Route 1 runs from x≈299 at the north edge to
  x≈881 at the south — 589m of east-west travel. Places, parcels and block numbering were all laid
  out against a straight road at x=750. Things sited near the road's ends may now sit a long way
  from it.
- **`Junction.Reach` is half the wider corridor**, which under-estimates an oblique crossing: two
  30m corridors meeting at 45° overlap further than 15m along each. Phase A left this deliberately;
  an oblique junction is a Phase B render.
- **The Straight dead-zone** (±0.3 on the cross product) can silently drop a shallow turn. It needs
  either a tighter band or an explicit same-road rule.
- **Both-curved crossings remain O(len × dense)** in `RoadNetwork.Crossings`. Only matters if two
  curved roads ever cross; not the case for Route 1 against a rectilinear grid.

## Decisions taken

| Question | Decision |
|---|---|
| Sequencing | Curve first — bend Route 1 early, then chase the breakage |
| The straight map | **Not preserved anywhere.** Real survey data only |
| The golden baseline | Re-recorded against the curved map and hand-verified, not frozen |
| `Way` on a curve | A stream label only; the cardinal nearest the segment's own tangent |
| Geometry source | Always the path — `PointAt`, `TangentAt`, `NormalAt`, `Project` |
| Junction visuals | Snap to 90°; the true angle stays in Core |
| Migration unit | Four groups by the question asked, audit instruments first |
| 408 Holmes | Pinned by test before the curve lands |
| Acceptance | Curved asphalt, traffic on it, signals and signs sited, suite green, and it looks right |
