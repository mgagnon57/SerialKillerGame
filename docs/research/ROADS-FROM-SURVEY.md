# The roads, rebuilt from survey instead of from a ruler

Compiled 2026-08-05, after *"roads are way off"* and *"the roads have always been an issue,
do not trust much from the previous"*. Both are right, and the second turns out to be the more
useful instruction: almost nothing about the old road data survives inspection.

At `Content/roads.txt` (derived, re-runnable). Built by `tools/build-roads.py`; alleys by
`tools/derive-alleys.py`; scored by `tools/check-roads.py`.

> **IT IS NO LONGER A PROPOSAL AND HAS NOT BEEN FOR SOME TIME.** This said "nothing reads it yet".
> `SurveyRoads.Apply` replaces `city.txt`'s road block wholesale whenever the file exists, and it
> does: the game, every render and every audit have driven on these roads since the survey layer
> landed. The line below about the junction graph was stale in the same way and is now answered —
> see *What is not settled*.

---

## What was actually there

**32 of the 37 road lines in `city.txt` are two points.** Not a polyline — a straight line
ruled from one side of the town to the other:

```
road attica 30 0,1353 2099,1353
```

That is the whole of Attica Street: one segment, 2,099 m long, dead straight, spanning the
entire map. Every alley is the same, and so is Church, Summit, Grove, Holmes, Maple, Gilbert,
Stewart, McKibben, Dale, Greenwood, Thompson and York. Only five roads in the file have real
shape, and one of those is Chicago Street, which the owner corrected personally.

The 2026-08-04 refit then slid each line bodily onto the nearest parcel-free strip. That is the
most a rigid shift *can* do, and it cannot help a road whose **shape** is wrong. Harrison turns
15.4° at Benton — the owner called that before it was measured — and no amount of sliding a
straight line will put a corner in it.

So the previous road data was never survey data that drifted. It was a sketch.

## The test

A public right of way is a **hole in the parcel coverage**: nobody owns it, so the assessor
draws nothing there. A correct centreline therefore sits in that hole, and a wrong one runs
across somebody's lot. Sample every 4 m through the platted town, count the samples that land
inside a parcel, and the answer needs no interpretation. For a right of way it should be zero.

| | share of centreline on private land |
|---|---|
| `city.txt` as it stands | **9.0%** |
| county centrelines | 2.2% |
| **the proposal in `roads.txt`** | **1.6%** |

Per road, the worst of the old network against its replacement:

| road | city.txt | new |
|---|---|---|
| alley1 | **75.3%** | derived afresh |
| alley12 | 42.2% | derived afresh |
| attica | **33.9%** | 4.7% |
| grove | 13.7% | **0.0%** |
| railroad | 13.6% | **0.0%** |
| ann | 13.3% | **0.0%** |
| maple | 7.8% | **0.0%** |
| holmes | 9.0% | 0.6% |
| all 15 alleys | 13.4% | **0.5%** |

## Where the new geometry comes from

### Streets — the county's own centreline layer

`gis.cityofdanville.org/.../Transportation/Streets` — the same ArcGIS server that supplied the
parcels and the property records. **146 segments over Rossville**, with address ranges, ZIP
60963, speed limits, and each street's own name. Real surveyed geometry.

It also settles some naming the project had already reasoned its way to: the county carries
**Park, Perry, Stufflebeam, Smith and Watson** as real addressed streets, which is exactly the
west-of-Route-1 name split `ROSSVILLE-HISTORY.md` predicted. It spells McKibben **"McKidden"**;
this project's spelling is kept and the county's noted.

The layer is cut at every junction, so a street arrives as six or ten pieces; they are chained
back into continuous runs before use.

### Alleys — derived from the parcels, because nobody maps them

The county maps no alleys at all — counties map what they maintain and address from, and an
alley is neither. OSM has them no better. But an alley does not need downloading: it is already
in the survey **as an absence**. Rasterise every parcel, cut out the street corridors, and what
survives inside a block is the alley and nothing else.

The centreline is taken as the **medial axis** — the point furthest from any parcel at each
step along the run. The first attempt used the midpoint of the gap's extent and scored 8.9% on
private land, because wherever an alley opens out at a junction the extent bulges to one side
and drags the midpoint into a back garden with it. The medial point cannot do that. It scores
**0.5%**.

Fragments split by street crossings are stitched back together when they point the same way and
their ends are within a street's width, so an alley that runs four blocks is one way to walk
down rather than four.

**31 alleys.** The old file had 15. (Two more were dropped as slivers — see the width section.)

### Chicago Street — left exactly as it is

Not out of deference. `SOURCES-OF-TRUTH.md` records it as authored and never to be regenerated,
and the measurement independently agrees with the ruling: **the authored curve runs on private
land 0.0% of the time and the county's own Route 1 line manages 3.4%.** The owner's road is the
better one, and replacing it would be a downgrade. IL Route 1 is dropped as redundant — the
owner's standing fact is that Route 1 and Chicago Street are the same road, and the authored
line already spans the whole map.

This is the one place where "don't trust the previous" and the evidence point opposite ways,
and the evidence wins.

## Widths and classes — measured and sourced, not inherited

The old file called every street `10` and every alley `4`. Those numbers came along with the
ruled lines and are not trusted here.

**Width is measured** off the parcel gaps: twice the distance from the centreline to the
nearest privately-owned ground, taken at the 40th percentile along each road because a junction
is a hole the size of both roads meeting there and drags a plain median upward.

| class | n | measured right of way |
|---|---|---|
| street | 28 | **65.6 ft** |
| alley | 33 | **19.7 ft** |

The street figure independently reproduces the 19.6 m already recorded in
`SOURCES-OF-TRUTH.md` §2, arrived at by a different method. Widths quantise to about 6 ft
because the raster is 1 m.

**Class starts from the county, and is then made to fit.** The county's own fields say what each
road *is*:

| FunctionalClass / RoadType | what it is | roads |
|---|---|---|
| 30 / STP | state route | Chicago Street, IL Route 1 |
| 50 / CTH | county highway | **Attica**, 3550 North |
| 60 / CVS | village street | everything else |

That classification is right about **function** and cannot be right about **width** — see the
next section, where a 98 ft mainroad corridor was laid down a 67 ft easement and put 16 ft of
asphalt on private lots for the length of Attica Street. Width says how much room a road has;
the classification says what the road is; and where they disagree, the ground wins.


## Road width: the corridor has to fit inside the easement

Raised after the first pass looked right in plan and wrong up close — *"roads seem to be running
over onto property."* They were, and it was one class doing it.

**Three widths, nested, and only the middle one is the road:**

| | what it is | where it comes from |
|---|---|---|
| **easement** | the public land — the gap the lots leave | measured off the parcels |
| **corridor** | what the kit paves: asphalt **plus its pavements** | pinned to the class by `RoadClasses` |
| **asphalt** | the driving surface | measured off the tile mesh by `CityStreets` |

A Rossville street has a **66 ft easement** and paves a **33 ft corridor** down the middle of it,
leaving about **16 ft each side** of public ground that is not road. That is the arithmetic
`SOURCES-OF-TRUTH.md` §2 already set out, and the measurements confirm it: streets and alleys
overran **0%** of their own right of way.

**The main roads were the whole problem.** Class came from the county's functional
classification, which says what a road *is* — Attica is a county highway — and the corridor then
came from the class. But a mainroad corridor is **98 ft** and Attica's easement measures **67 ft**,
the same as Holmes and Summit and every other street in town:

| road | class was | corridor | measured easement | overrunning |
|---|---|---|---|---|
| attica | mainroad | 98 ft | **67 ft** | 93% of it, by 16 ft |
| chicago | mainroad | 98 ft | **80 ft** | 90% of it, by 24 ft |
| 3550 north | mainroad | 98 ft | 102 ft | 25% — fits, it is open country |
| holmes *(for comparison)* | street | 33 ft | 67 ft | 0% |

Attica sits dead centre in its easement — 10.5 m one side, 10.0 m the other — so this was never a
placement error that centring could fix. **Being a county highway does not make the ground any
wider.** The class is now stepped *down* until the corridor fits the easement actually measured,
and never up: a village street with a generous verge is still a village street. The county's own
classification is kept in the file's comments, because it stays true about the road's function
even when it cannot be true about its width.

Two guards were needed, and both came from being wrong first:

- **Judge by how much of the road overruns, not by one percentile of its width.** A 40th-percentile
  test let Ann through as a street — its easement is 33 ft, exactly the street corridor, so it
  "fitted" while spilling over at 38% of the stations along it. The owner had already called Ann
  by eye as *"a narrow lane… fitted as an alley."* The stricter test agrees with him.
- **A through route never demotes below a street.** Chicago Street runs diagonally across a square
  plat, so its parcel gap pinches wherever the diagonal clips a block corner. Read literally, the
  ladder fitted **IL Route 1 as a 13 ft alley**. Those pinches are an artefact of the platting,
  not the width of the road.

**Result: paved corridor lying on private land fell from 93% and 90% on the two main roads to
1.8% across the whole network.**

### The easement is now a field, not a comment

`RoadRun.Easement` and a parser attribute, so the game can read how much public land a road has
rather than guessing outward from the asphalt and putting a sidewalk through somebody's hedge.
`VillageParser` **rejects an easement narrower than its own corridor** — a road may not pave more
than it owns — and that check earned itself immediately: it caught two derived "alleys" sitting in
**6.6 ft** gaps, which are lot-line slivers rather than anything you could drive down. They are
dropped, taking the network from 63 roads to 61.

### Sidewalks

Not all roads have them, and **this data does not say which do.** The downtown photograph shows a
concrete walk set back behind a grass verge on Chicago Street; nothing establishes it street by
street, and it is not invented here. What the easement gives is the room to lay one later without
crossing a lot line — 16 ft each side on an ordinary street, and none to spare on an alley, which
is correct, because alleys do not have sidewalks.

## What is not settled

- ~~**`roads.txt` is a proposal and nothing reads it.**~~ **SETTLED.** `SurveyRoads.Apply` swaps
  it in for `city.txt`'s road block at build time, so the game drives on it and always did once
  the file existed. `city.txt` keeps its 477 authored places, human lines and story anchors and
  is never rewritten by a data refresh — which is the property that paragraph was really
  protecting, and it still holds.

- ~~**The junction graph has not been rebuilt.**~~ **SETTLED, 2026-08-09, and it was the whole
  day's work.** The answer to "whether the runs meet cleanly enough to form the same junction
  set" turned out to be no, twice over, and neither reason was geometry:

  > **The finder only ever compared a north-south road against an east-west one.** `IsNorthSouth`
  > is `dy >= dx` between a road's first and last point, so it describes the whole run and says
  > nothing about which way the road points where it meets another. alley21 runs 121 m west and
  > 63 m north — "east-west" — and its first 13 m run due north into Benton, which is east-west
  > too. The pair was never compared, no junction was made, and cars came out of the alley
  > through Benton's traffic: `NoTwoVehiclesOccupyTheSameSpace` measured two of them **0.60 m
  > apart**, 123 m from the nearest junction the model knew about.
  >
  > **And a junction was a PAIR of roads.** Where Maple ends, Park begins and Route 1 goes past,
  > that is one piece of tarmac with three roads on it and there is no pair to make. A junction
  > is a node with `Arm`s now, crossings closer together than `reachA + reachB` fold into one,
  > and every arm is re-projected onto the merged centre.
  >
  > Underneath both sat **one false assumption written out four times** — that arc length grows
  > the way a road's declared axis does. `RoadPath` measures s from `Points[0]` and the county's
  > chained segments are declared in whichever direction the surveyor walked, so park, greenwood,
  > alley13 and alley18 all run right to left. LaneGraph cut their lanes at the wrong end,
  > classified their turns off the wrong tangent, ended their lanes past the end of the road, and
  > **CityTraffic drew the cars there**. Worst drift 608 m on greenwood; 104 m on `city.txt`'s own
  > railroad, where nothing had ever tested a curved road's lane cuts.

  ```
    junctions      74  ->  122        (city.txt's own network: 111 -> 110)
    lanes arriving somewhere they cannot leave        0
    junctions off any road they claim to join         0
    junctions missing a turn between two of their roads   0
    pairs of roads sharing tarmac with no junction   14 -> 2, both a data fault (ALLEY-2b)
  ```

  Held by `SurveyRoadNetworkTests` — `EveryJunctionLandsOnEveryRoadItClaimsToJoin`,
  `NoLaneArrivesAtAJunctionItCannotLeave`, `EveryBentRoadFindsItsWayBackToACoordinateOnItsOwnAxis`
  and `NoTwoStreetsTouchWithoutAJunctionBetweenThem`. The full account, item by item, is
  `docs/ROAD-FIXES.md`.

- **The alleys reach the town now.** `build-roads.py` carries an alley's end out to the street it
  stops short of, refusing where the new stretch would cross a lot: 58 mouths opened, 1 refused,
  7 too far. Ends reaching a street went **2 of 66 → 57 of 70**, alleys stranded at both ends
  31 of 33 → 1 of 35, median gap 14.9 m → 0.4 m. Two streets that stopped short of Route 1 —
  Dale and Thompson — were carried out the same way.

- **The smoothing curve changed with them.** Roads are `SmoothCentripetal` now; the railway keeps
  the uniform Catmull-Rom it was drawn with. Uniform smoothing overshoots where consecutive spans
  differ wildly, which is exactly what a 13 m alley mouth on a 200 m run produces: **nine of 68
  roads left one of their own ends backwards, and Summit was drawn 39 m from its own survey
  line.** Zero on both maps now.
- **Two roads get slightly worse**, and they are left as measured rather than special-cased:
  summit 4.3% → 5.3%, and one derived alley at 4.0% where the old line happened to score 0.0%.
- **The country roads are now included** — 1350 East, 3450 North, 3650 North, 3680 North, Abner,
  Earl, Creative — which the old file did not carry at all. Their measured widths are meaningless
  because farmland parcels tile edge to edge out there, so they take the class default.
- **House placement no longer depends on any of this.** It used to: *"places move with the
  street they are ADDRESSED on"*, so moving a road meant re-seating its houses. Since the
  buildings are now seated on parcels (`BUILDING-FOOTPRINTS.md`), roads and houses are
  independent and a road can be corrected without disturbing a single building.
