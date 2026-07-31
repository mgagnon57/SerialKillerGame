# The outer city — the map-size fork, settled

**Date:** 2026-07-31
**Status:** approved, NOT scheduled — deliberately queued behind the traffic fix. See "Sequencing".

## The fork, as it was written down

`docs/IDEAS.md` carried this for a day:

> The outer city, and the map-size fork that comes with it. Downtown fills 255..795; the suburbs
> would take the 105..255 and 795..915 bands, which is one thin ring and squeezes the country to a
> 105m frame. Either the countryside moves out again or the map grows to ~1440 (2.25x the props).
> Decide after measuring what a block actually costs.

Three of those figures are wrong, and the measurement was never taken. Both are done below.

## What the map actually is

The grid is a strict ninety-metre pitch — thirty-metre corridor, sixty-metre block — which cuts the
960m map into a ten-by-ten lattice of cells:

| ring | cells | size |
|---|---|---|
| downtown | inner 6x6 = 36 | 60x60 (27 `district` + 9 authored block by block) |
| the bands | next ring, 28 | **120m** west and north, **90m** east and south |
| outside the outer ring | outermost, 36 | **90m** west and north, **30m** east and south |

**THE COUNTRY HAS NOWHERE TO MOVE TO.** The road network is not centred on the map: `westbound` and
`eastbound` sit at 105 and 915, whose midpoint is 510 against a map centre of 480. So the ground
outside the outer ring is ninety metres on two sides and **thirty metres** on the other two — one
corridor's width, which will not hold a field, let alone a farm. The note's "105m frame" is
optimistic on two sides and fictional on the other two.

And the bands are not empty. Home Farm, the yard, the big barn and both silos are in the west band
at x 125..235; Wicker End, the old barn, the old orchard, the far spinney and the far field are in
the south band at y 815..895. Putting suburbs there does not move the countryside out. It deletes
it, and it evicts the farm a second time — `city.txt` still carries the paragraph explaining the
first move.

So the fork was never "the countryside moves out, or the map grows". It was **"the countryside
stops existing, or the map grows"**, and that is a much easier decision.

## What a block costs, measured

Taken from a clean headless run of `Noir.Editor.CityShot.Render` at HEAD (`c1afb0c`), zero errors:

```
31,814 renderers  ->  4,462 baked   (2,277 meshes, 29,611 originals removed, 30 materials)

  districts   8,228    27 blocks, 662 buildings, 5,814 sections, 753 things in the back yards
  greenery   10,039    7,226 trees + 2,075 bushes
  streets     5,340    509 road tiles, 20 roads, 84 junctions, 4,090 pieces of furniture
  farm        2,369    2,315 pieces of country
  city          448    27 townhouses + landmarks, 280 pieces
  parking        --    5 lots, 40 tiles, 73 cars standing still
```

**A downtown block costs about 305 renderers before chunking**, and twenty-seven of them are 26% of
everything in the city. The chunker takes the whole map down by 7.1:1 across only thirty materials,
which is the fact that makes any of this affordable — the pack is one atlas, so more of the same
kind of thing is close to free once baked.

## The decision

**The map grows to 1290.** Not to 1440, which was a guess, and not to the 1110 that is the cheapest
map with a real country in it.

The arithmetic is forced by the pitch. N blocks span `90N + 30`:

| N | map | downtown | suburb ring | country | area vs 960 |
|---|---|---|---|---|---|
| 10 | 930 (today, in a 960 box) | 6x6 | — | slivers; 30m on two sides | 1.00x |
| 12 | 1110 | 6x6 | 8x8, 28 cells | 2 cells, ~180m | 1.34x |
| **14** | **1290** | **6x6** | **8x8, 28 cells** | **3 cells, ~270m** | **1.81x** |
| 16 | 1470 | 6x6 | 8x8 | 4 cells | 2.34x |

1110 is enough to give the farm its land back and would have been the frugal answer. **1290 was
chosen to buy the land once rather than move things twice**: three cells of country is room for the
Racetrack (152 prefabs, unusable to date for want of ground), the ground-level tram kit, and the
Survival sites — all three of which are open IDEAS entries whose only blocker is land.

### The layout

Roads land at `15 + 90k`, which puts one dead through the map centre at 645 and makes the whole
plan symmetric about it. The current off-centre grid is fixed as a by-product of re-laying rather
than as a job of its own.

```
        15   105  195  285  375 ....... 915  1005 1095 1185 1275
        |     |    |    |    |            |    |    |    |    |
   15  -+-----+----+----+----+---  ...  --+----+----+----+----+-
        |  country, 3 cells, ~270m                            |
  285  -+     +----+----+----+---  ...  --+----+              |
        |     |  suburbs, 8x8 ring, 28 new cells    |         |
  375  -+     |    +----+----+---  ...  --+    |    |          |
        |     |    |  downtown 6x6, UNCHANGED |    |          |
  915  -+     |    +----+----+---  ...  --+    |    |          |
 1005  -+     +----+----+----+---  ...  --+----+              |
 1275  -+-----+----+----+----+---  ...  --+----+----+----+----+-
```

Bounding roads by centre line, and the blocks they enclose:

| ring | bounding roads | blocks | contents |
|---|---|---|---|
| downtown | 375 and 915 | 390..900 | 6x6. The 27 districts and 9 authored blocks keep their contents exactly |
| suburbs | 285 and 1005 | 300..360, 930..990 | the 8x8 ring less the 6x6 core = **28 new cells**, each 60x60 |
| country | — | 30..270, 1020..1260 | 3 cells, ~270m, on every side |

**THE ROAD GRID STOPS AT THE SUBURB RING.** Nine roads each way for the built area, against today's
twenty roads and 84 junctions — near-flat. The country gets sparse lanes and farm tracks, not a
grid.

This is a traffic decision, not a taste one. A full fifteen-by-fifteen grid would be roughly two
hundred junctions and would triple the lane graph, which is the last thing to do to a system with a
reproduced, unfixed give-way fault. Sparse country lanes are also what country lanes are.

## Suburbs are the same kit, differently spaced

**There is no suburban house in the pack.** Searched all ~6,000 prefabs: two farmhouses
(`House_Farm_British` and `House_Farm_Scandinavian`, both already standing as Home Farm and Wicker
End), `Cabin_Big_Summer_Camp`, and the Fantasy set. A street of detached houses built from what
exists would be forty copies of Home Farm — the same failure as the yard of cream garages that
`CityDistrict.Interior` rejects in its own comments.

**It does not need one.** The modules ship in three face variants — `_F` front-only, `_FB`
front-and-back, `_AS` all-sides — and `CityBuildings.Stack` already uses `_AS` for exactly this
reason, with the note that flanks ARE seen and the `_F` saving "was never real here". A freestanding
house is buildable out of the kit the terraces are already stacked from.

So the difference between a downtown block and a suburb is not the building. **It is the setback and
the spacing**, which is also what it is in life:

| | downtown block | suburb cell |
|---|---|---|
| family | Squarehouse, or Bayhouse by lot | **Bayhouse** |
| pitch along the street | 6m — shoulder to shoulder, overlapping | ~14m — gaps between |
| set back from the corridor | 0, on the building line | ~12m, behind a garden |
| storeys | 6 falling to 2 by `RankOf` | 1–2 |
| ground floor | shopfront where footfall justifies it | residential |
| between the houses | nothing, it is a terrace | hedge run, driveway, garage, a car |
| behind | the back yard: bins, skips, boxes | back garden |

**Bayhouse rather than Squarehouse, and the reason is measured.** `Squarehouse_Bottom_A` carries
three metres of unfaced `M_Universal_A` past the end of its brick; `Bayhouse_Bottom_A` has the same
fault at about one metre. `Seat(dressedOnly: true)` aligns the brick to the building line, which
puts that tail out of the back — invisible in a terrace, where the block interior is built over it,
and **visible in a back garden**, which is a suburb's whole point. One metre of it can be hidden
behind a garage; three cannot. Bayhouse is also 7m deep against 9m and has a bay front, which is
the suburban register anyway.

Pieces that already exist and are wanted here:

- `Modular Parts/Fences` — `Fence_Hedge_Short_*` and `Fence_Hedge_Medium_*` in 1m, 2m and 3m with
  corner pieces. Garden boundaries, laid as runs the way `VillageMesh` lays hedgerows.
- `Squarehouse_Garage_City` — **rejected** by `CityDistrict.Interior` because a yard packed with
  them reads as featureless cream boxes from directly overhead, which is how this game is looked at
  half the time. One garage beside one house on one driveway is what the model is for, and there it
  reads correctly.
- `Cars/Cars City` — a car on a drive, which is a different fact about a household from a car on the
  road.

### Where it lives

A new `suburb` place kind and a new `Assets/Noir/Unity/CitySuburb.cs`. **Not a mode on
`CityDistrict`**, which is already 374 lines doing perimeter, interior and tower selection; a
suburb branch through all three would tangle the one file that currently has a single clear job.
This follows the pattern already set: a kind in `kinds.txt`, a renderer that walks the map for it,
and no `switch` on `PlaceKind` anywhere in `Assets/Noir/Unity`.

`units` on a suburb cell is the density knob, the same as it is on a district.

## Everything existing moves by script

Every place shifts by a fixed offset so internal relationships survive intact — the same operation
as the +360 move that centred the town, which is already known to work and is why that move cost
nothing.

**The offset is +120 in both axes, and it is NOT half the growth.** `(1290 - 960) / 2 = 165` is the
wrong number, because the town is not centred in the map it is in now: downtown blocks run 270..780,
whose centre is 525, against a 960 map whose centre is 480. The new map's centre is 645, so the
shift is `645 - 525 = 120`. That lands downtown at 390..900 — the same 510m span, now centred on the
map exactly. Getting this from the map size rather than from where the town actually is would push
the whole city 45m off centre and quietly reintroduce the asymmetry this re-lay exists to remove.

Roads are re-declared from the `15 + 90k` table rather than patched.

## Cost, projected

Estimated, not measured — the measurement comes when it is built:

```
                        today          projected 1290
map area              921,600 tiles    1,664,100      1.81x
country area          ~630,000         ~1,145,700     1.82x
trees                   7,226            ~13,000
greenery renderers     10,039           ~18,000
total pre-bake         31,814           ~42,000
total baked             4,462            ~6,000
```

The suburbs themselves are cheap: low density is the point of them, and 28 cells of spaced
two-storey houses cost far less than 28 more downtown blocks would. The greenery is the bill, and
the chunker has already shown it bakes greenery down hard because it is all one atlas.

## Sequencing — why this is approved and not scheduled

28 suburb cells at roughly sixteen houses each is about **450 new households against the 945
declared today**. Traffic scales off `WorldModel.DeclaredHouseholds`, so building the suburbs grows
the fleet by about half — from 236 vehicles to roughly 350.

**That is exactly the thing density is currently being held down for.** `CarsOutPerHousehold = 0.25`
was chosen last night to keep the fleet flat at 236 against 243, and the reason is written in
IDEAS.md: `NothingCrossing` and `NothingComing` can starve a car indefinitely on a busy road, a
`Patience` timeout was tried and REVERTED because it produced an actual 0.00m collision under the
full suite, and the next step is gap acceptance on closing speed proven over repeated full-suite
runs.

Growing the map and adding four hundred and fifty households on top of an unfixed give-way fault
would make a known, reproduced defect worse and would muddy the evidence for whether any fix works.

**So: the traffic fix lands first. Then this.** Nothing here expires in the meantime — the
measurements are at `c1afb0c` and the pack findings do not change.

## Deliberately not decided here

- **Where the Racetrack goes.** It is 152 prefabs and 25 modular road pieces, so it is a site
  decision and a road-network decision, and the country will not exist to site it in until this
  lands.
- **The tram line's route.** `Modular Parts/Rails` is a six-piece ground-level kit, separate from
  the elevated railway commented out in `city.txt`.
- **The Survival sites.** STATE.md holds these back deliberately: where a bear trap goes is a story
  decision, not a scatter rule. The proposed register is **the roadside and the treeline** — a
  `Cross_Wood` memorial on a country lane, a `Tree_Stand` overlooking a field, a bear trap in the
  shelter belt, a road flare and an abandoned suitcase at a lay-by. That is a proposal to be
  accepted or redirected when the work is scheduled, not a decision taken here.
