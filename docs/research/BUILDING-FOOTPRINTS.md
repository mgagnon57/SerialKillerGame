# Where every building in Rossville actually stands

Compiled 2026-08-05. The project had 794 surveyed lots, the county's record of each one, and
**no building standing on any of them**. Houses were authored rectangles placed at a setback
from a road; 76 of them stood on no parcel at all. This puts a measured footprint on 572 of
those lots and, for the first time, lets the question *"where on its lot does a house sit"* be
answered by measurement instead of by one crop of one Sanborn sheet.

Data: `Content/parcel-buildings.txt` (derived, read-only). Raw download
`tools/rossville-structures.geojson`. Rebuild with `tools/seat-buildings.py`. Browse it at
`docs/rossville-buildings.html`.

---

## The source, and why the earlier answer was wrong

`IDEAS.md` recorded that *"the county has none and OSM has 19"*. The first half of that is
half right and worth correcting, because the correction is where the data came from.

| source | buildings in Rossville | what it is |
|---|---|---|
| OpenStreetMap | 19 | hand-mapped, essentially absent here |
| Vermilion County `Property/Buildings` | **0** | the layer **exists** — 17,665 buildings — and covers **Danville only** |
| **FEMA / ORNL USA Structures** | **885** | national footprint layer, ML-derived from imagery |

The county's ArcGIS server does carry a Buildings layer, on the same host that supplied the
parcels. It simply stops at the Danville city limit. So "the county has none" is true *here*
and false in general, and nobody would find the distinction without enumerating the server's
folders — which is how this was found.

**885 structures stand in the Rossville box. 822 are seated on a county lot.** The other 63
are in open country where the parcels tile farmland, or on a lot `parcels.txt` does not carry.

## Getting them onto the map, and the bug that nearly ruined it

The structures arrive in WGS84; the game draws in village metres. The conversion was
**fitted**, not reconstructed — `parcels.txt` and the parcel download are the same lots in the
two frames, so the transform between them can be solved for and, more importantly, its error
reported. It comes out at **+1.8042°** rotation against the +1.81° the parcels' own header
records, and **111,180 m per degree** of latitude against a true 111,132.

That fit first landed at a **3 m median residual**, which was quietly blamed on the data. It
was not the data. `ring_centroid` was computing cross products on raw lon/lat — numbers around
87 × 40, encoding a quantity around 1e-5 — and double precision was eating the result. The
same ring's centroid differed by up to **11.7 m** depending on whether it was projected before
or after. Shifting each ring to a local origin first fixed it:

| | before | after |
|---|---|---|
| parcels matched | 734 of 794 | **794 of 794** |
| median residual | 3.11 m | **0.42 m** |
| worst residual | 19.4 m | **1.17 m** |
| footprints landing inside their own lot | 51% | **100%** |

The lesson is the ordinary one and it is worth writing down: a wrong number that is *plausible*
costs more than a wrong number that is obviously wrong. A 3 m error looks like survey slop.

## Why the houses sat crooked and half off their lots

The first published version of this looked right from across the room and wrong up close:
houses overhanging their boundaries, and plenty of them visibly turned against the lot they
stand on. Three things could cause that, and they are separable by measurement rather than by
argument.

**Two of the three were mine.**

*The town was squashed.* The fit was a **similarity** — one scale for both axes — and the
conversion that produced `parcels.txt` did not use quite the same metres-per-degree of
longitude this pipeline reconstructs. The two axes differ by **0.197%**, which a single scale
cannot represent, so it spread the mismatch across the town as position error, up to about 3 m
at the edges. On a lot 20 m wide that is enough to push a house over its own line. A
six-parameter **affine** fit absorbs it exactly: median residual **0.42 m → 0.028 m**, an inch.

*The two agencies are not registered to each other.* A FEMA outline sits about **17 ft
south-east** of the lot the assessor draws around it. Measured two independent ways that agree
to within 15 cm:

| method | correction | bias |
|---|---|---|
| minimise the area hanging off each lot | −3.50, +3.25 m | biased — houses sit at the front, so this always pulls inward |
| **solve the mean side-offset to zero** | **−3.43, +3.14 m** | unbiased — a house has no reason to sit off-centre *sideways* on its lot |

The second is the one to trust: side to side, the builder centred the house between the
neighbours, so the mean sideways offset should be zero and whatever it actually is, is the
registration error projected onto that lot's frontage axis. Rossville's blocks face two
perpendicular directions, so both components fall out of one least-squares solve.
Bootstrapping puts the 95% interval comfortably clear of zero, and splitting the town into
north–south and east–west blocks gives the same answer to within a metre — which the
front-of-lot bias could not do, since it would push those two groups along different axes.

Correcting it is done **before** structures are assigned to lots, not after: a footprint 17 ft
out of position can fall inside the *neighbour's* lot, so correcting afterwards would leave a
house perfectly placed on the wrong parcel. The offset and the assignment each depend on the
other, so it is iterated to a fixed point — which is also why the seated count rose from 808
to 822.

| | before | after |
|---|---|---|
| houses sitting wholly inside their own lot | 38% | **69%** |
| crossing the lot line | 62% | **31%** |
| more than a tenth of the house outside | 34% | **14%** |
| footprints inside the lot they were assigned | 807 of 808 | **822 of 822** |

**The third was not mine, and cannot be fixed by placing.** The angle. After both corrections
the house-versus-lot angle is unchanged — median **4.7°** off, p90 **21°** — because a
translation cannot rotate anything and an affine of 0.197% can turn a shape by at most 0.06°.
Measuring the same thing in **raw WGS84**, before this pipeline touches the data, gives 4.79°
against the 4.74° it gives afterwards. The crookedness was in the download.

And it is the buildings, not the lots:

| measured against the town grid | within 5° of it |
|---|---|
| the county's lot lines | **94%** |
| FEMA's building outlines | **49%** |

The outlines are internally square — the four corners of a traced rectangle come within
**0.04°** of right angles — so each one is a clean rectangle that has simply been *rotated
wrong*. That is the signature of automated extraction, and it is what `VAL_METHOD: Automated`
buys you.

So it is **recorded, not corrected**. Every building carries two angles, and squaring means
rotating the ring by minus one of them about its own centroid:

| field | what it is off square to |
|---|---|
| `skew` | its own lot |
| `block` | the block it stands in — every lot within 130 m, averaged |

### Squaring to the block, not to the lot

The first version squared to the lot and refused anything past 20°, and it visibly worked on
some houses and not others. Both halves of that rule were wrong, and the way to tell was a
measurement that is not circular: **on a platted street, neighbouring houses are parallel**, so
score each rule by how much a house disagrees with its six nearest neighbours after correction.

| rule | median disagreement | 90th percentile |
|---|---|---|
| uncorrected | 10.7° | 21.6° |
| square to lot, skip past 20° *(the first version)* | 4.3° | **17.6°** |
| square to lot, no cutoff | 0.15° | 5.1° |
| **square to the block, no cutoff** | **0.4°** | **1.5°** |

**The cutoff was the bigger fault.** It left 57 buildings — a tenth of the town — untouched,
and only 7 of those stood on a lot that is genuinely off the grid. So fifty houses stayed
crooked for no reason, and they are exactly the ones that catch the eye.

**The lot is a noisy reference.** A single parcel polygon sits a median 1.07° off the town grid
and in the worst case 31.4°, and squaring a house to it inherits that error exactly. Averaging
the direction over every lot within 130 m cancels the wobble while staying local enough that the
diagonal blocks along the railroad keep their own angle rather than being dragged onto the grid.

One hypothesis this **disproved**, worth recording so it is not re-tried: the suspicion was that
the minimum-area rectangle was misreading L-shaped and T-shaped houses, since
`RESIDENTIAL-1913.md` is insistent that real houses here are L or T plans. It is not. FEMA's
outlines score 1.00 for rectilinearity almost universally — they are traced as simple boxes,
*not* as the L plans the Sanborn sheets draw — so the min-area box and the length-weighted edge
direction agree on 95% of them. Which is its own finding about this data: **these footprints
have had the articulation flattened out of them.**

## Three checks, all computed

1. **822 of 822** footprints land inside the very lot they were assigned to, in the coordinates
   `parcels.txt` draws. 69% sit wholly within their boundary; the rest overhang it, which is
   partly real — garages are built on lot lines — and partly loose tracing.

2. **96.0% carry the same street address the county assessor records for the same parcel** —
   679 agree, 28 differ, on the 707 where both have one. A county tax roll and a federal
   imagery product agreeing on which house is at which number is a real check on the geometry,
   and most disagreements are two house numbers apart on the right street.

3. **Both sources agree about whether a lot is built on, 91% of the time.** The county books a
   dwelling on 575 lots; imagery finds a building on 572; 522 are the same lots.

### The 103 that differ, and which source to believe

Not one answer — four, and only two of them are conflicts at all:

| n | what it is | who is right |
|---|---|---|
| **21** | a church, school or hall | **both.** A tax roll lists *taxable dwellings*; a tax-exempt building is correctly absent from one and correctly present on the ground. Calling this a disagreement was an error in the first version of this comparison. |
| **43** | the downtown terrace | **the county.** FEMA traces a continuous run of joined brick as ONE polygon, which lands on one parcel and leaves its neighbours reading empty. The commercial cases sit a median 99 m from the crossing and 15 of 18 have a footprinted lot within 35 m. The 1913 Sanborn classification hit the identical artefact — *"adjacent buildings that touch merge into one component"* — which is why `COMMERCIAL-ROW.md` transcribes the terrace by hand instead. |
| **29** | a real building on land the county calls vacant | **unresolved.** The genuine conflict. Median 2,457 sq ft, so not sheds. Either the roll is stale or the building is newer than it. |
| **10** | booked, but nothing stands there | **the imagery.** Scattered, a median 405 m out, median assessed dwelling $7,811 — the cheapest housing in town. Demolitions the roll has not caught up with. |

**So neither source is simply better, and asking which is the wrong question.** The county books
every parcel separately and never merges, so it is the one to trust about **whether** a lot is
built on — decisively so downtown. The imagery is a survey of the ground, so it is the one to
trust about **where** a building is and what shape it is. The browser view colours all four
causes separately.

---

## What the footprints say that nothing else could

### Houses sit at the front of the lot — and here is how far

`RESIDENTIAL-1913.md` asserted this from **eight lots on one crop** and never put a number on
it. Measured across **513 town residential lots**, road data not involved — position taken
along each lot's own depth axis:

| | value |
|---|---|
| median lot | **165 ft deep × 66 ft wide** |
| house centre, from the front lot line | **53 ft** |
| position along lot depth | **0.32** (0.50 would be dead centre) |
| houses in the front third of their lot | 52% |

**So the rule is confirmed and it is not as tight as "shallow setback" suggested.** A Rossville
house stands about 53 ft in from its front line with roughly 110 ft of lot open behind it. For
comparison `CitySuburb.Setback` is a hard-coded 12 m (39 ft) to the front wall, invented for
the pack city — which turns out to be close, by luck rather than by evidence.

### The outbuilding is behind the house

Of 205 lots carrying more than one structure, **71% put the second one behind the first** —
and where it is behind, a median **53 ft** further back. This is the alley-facing yard that
`INSIDE-THE-HOUSES.md` and `WHO-SEES-WHOM.md` both depend on, now observed rather than assumed.

**"Behind" is measured against the STREET**, with the alleys deliberately excluded from the
reference: an alley runs along the back lot line, right past the outbuildings, so counting it
would call the same shed both *behind the house* and *beside the road*. Measured against the
lot's own long axis instead — "further from whichever end the house is nearest" — the figure
comes out at 95%, and that is very nearly circular, since it is true of almost anything else
on the lot. The browser view computes the street-based version, so the page and this file
cannot drift apart.

An earlier pass of this section read 74% and 35 ft. Same finding, two definitions moved: the
street reference is now the survey road network rather than city.txt's ruled lines, and the
distance is the median among the ones actually behind rather than across all of them.

### Footprints are bigger than 1913, and that is a real disagreement

| | median footprint |
|---|---|
| 1913 Sanborn, frame dwellings (`BUILDING-CENSUS-1913.md`) | **1,047 sq ft** |
| these, primary residential | **2,184 sq ft** |

**Do not average these.** They are not measuring the same thing:

- Sanborn draws porches as dashed and **excludes them from the footprint**; these outlines
  include the porch, the attached garage and every later addition.
- These are **2016 imagery**. Eighty years of additions sit between the two.
- The outlines are **machine-traced** and generous — see the limits below.

The honest reading is that the 1913 figure is the better one for *what a house was*, and this
one is the better one for *what stands on the ground now*. The build year is 2000, which is
nearer to the second.

---

## Limits — read before using any of it

- **`VAL_METHOD` is `Automated` on all 822.** Not one outline was checked by a person. They are
  reliable about *where* a building is, and loose about how it is TURNED — see the alignment
  section above: half of them are more than 5° off the town grid, and that is the source, not
  the seating. Each carries its `skew` so a consumer can square it if it wants to.
- **No heights.** The `HEIGHT` column is empty on every record. Storeys still come from the
  Sanborn sheets.
- **No rooms, no year built.** Footprint is *ground area*; a 1½-storey house has half again as
  much floor. Bedrooms, baths and floor area remain unavailable from any public source —
  `parcel-county.txt`'s header already establishes the county publishes none of it.
- **The primary/outbuilding split is derived, not recorded.** FEMA ships an `OUTBLDG` flag and
  it is empty on every record here, so the largest structure on a lot is called the building.
  Checked against "closest to the street it is addressed on": the two rules agree on **74%** of
  multi-structure lots. The other quarter are genuinely ambiguous — a barn bigger than the
  house is an ordinary thing in this town.
- **Imagery is 2016; the game is 2000.** The commercial row at Attica × Chicago **burned in
  February 2004**, so the downtown in this data is the town *after* the fire and is wrong for
  the build year — see `DOWNTOWN-1991.md`. The residential streets are substantially unchanged
  and are the part to trust.

## What this could change in the build

1. **Houses can stand where houses stand**, rather than at a pitch along a road. 572 lots have
   a real outline with a real address.
2. **The setback is now a measurement** — 53 ft to the house centre on a 165 ft lot — and can
   replace `CitySuburb`'s invented 12 m.
3. **The outbuilding belongs at the back of the lot**, 35 ft behind the house, which is what
   `ResidentialLots.Outbuilding` already models and had no geometry for.
4. **The disagreement layer is a to-do list.** 54 lots the assessor books as built and imagery
   finds empty are worth a look; several are likely the vacant lots the town has been thinning
   into.

## Privacy

Situs addresses are already in `parcel-county.txt` and appear here on the same terms. **No
owner names**, in this file or in the data behind it — the FEMA layer has no name field, the
same way the county download has none. The browser view is a **local file and nothing in this
pipeline sends the town's addresses anywhere**; it was built to be opened off the filesystem
for that reason. The standing rules in `parcel-county.txt`'s header apply unchanged.
