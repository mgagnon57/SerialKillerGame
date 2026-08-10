# Sources of truth

**This file is law, not research.** It says which dataset wins when two disagree. Where a question
is settled here, it is settled — do not re-litigate it from a measurement without changing this file
first.

It exists because the ruling it contains *already existed*, scattered, and went unenforced. See §6.

`docs/research/SOURCING.md` remains the record of **which era** each element comes from — fabric from
1913, use from 1991. That is a different question and it is still that file's. This one is about
**which dataset is authoritative when they conflict.**

---

## 1. The ruling

| Question | Authority | Corroborates | Never use for this |
|---|---|---|---|
| **Where is public land** — road and alley right-of-way | `Content/parcels.txt` — 794 county polygons | — | — |
| **Where do the lots sit** | `Content/parcels.txt` | Sanborn 1913 block structure | — |
| **Does a road exist, and roughly where** | OSM | 1940 USDA aerial | OSM *extents* — uncorroborated by anything |
| **Do alleys exist, and at what spacing** | 1940 aerial (mid-block line) | OSM `service=alley` | the aerial for absolute position — georeferencing failed four times |
| **Does a road run through something** | building footprints in `city.txt` | — | parcels — they are tax boundaries and cannot answer this |
| **Ground height** | `Content/elevation.txt` — USGS NED | — | — |
| **Rail, water, school grounds** | `Content/features.txt` — OSM | Sanborn 1898/1913 for the rail corridor | — |
| **Where the rail sits ACROSS its corridor** | `parcels.txt` **779, 781, 785** — see §2.1 | — | the gaps between parcels — there is no gap, the railroad owns the strip |
| **What the county records per lot** | `Content/parcel-county.txt` | — | inferring anything not literally recorded |
| **Shape, character, what the town is like** | **the owner** | everything else | — |

**When the owner and a dataset disagree about shape, the owner wins.** When the owner and a dataset
disagree about a *coordinate*, ask — that ambiguity is what caused the Route 1 episode.

---

## 2. What the parcels actually measure — read this before using the numbers

The parcel polygons leave a clean gap where public land is. Measured over all roads, the width of
that gap:

```
STREETS   n=125      99 of 125 samples land at exactly 20 m
   20 m (66 ft)  ###################################################################################################
   15 m (49 ft)  ######
   10 m (33 ft)  #####
    9 m (30 ft)  ####

ALLEYS    n=107      mostly 13 ft, with a real cluster at 20 ft
    4 m (13 ft)  ###############################################################################################
    6 m (20 ft)  #######
    5 m (16 ft)  ###
```

**66 ft is one surveyor's chain.** It is not an average and not a coincidence — it is the standard
Midwest plat street right-of-way, and 99 of 125 samples hit it exactly.

### 2.1 A RAILROAD RIGHT OF WAY IS A PARCEL, NOT A GAP

**Parcels 779, 781 and 785 are the railroad's own land.** Three strips, chained end to end for
about 2 km through the town:

| parcel | true area | perimeter | implied strip width |
|---|---|---|---|
| 779 | 18,493 m² | 1,327 m | 27.9 m |
| 781 | 34,513 m² | 2,035 m | 33.9 m |
| 785 | 10,848 m² | 806 m | 26.9 m |

A *street* right of way shows up as a **gap** in the parcel coverage, because nobody owns it. A
railroad right of way is the opposite: the railroad bought the strip, so the county records it as
a parcel like any other. **"The track is inside a parcel" is therefore the correct state, not a
fault** — it means the train is on railroad land.

This cost most of a day on 2026-08-04. Every scan looked for a parcel-free corridor beside the
track, found only farmland and open ground, and concluded no corridor existed — while the owner
could see it plainly on screen, because he was looking at the *edges of the strip* and the scan was
looking for the *absence of one*. The measurement that finally worked asked a different question:
**which parcel is the track in, and how wide and long is it?** Area over half-perimeter gives a
strip's width, and 28–34 m over 2 km is a railroad and cannot be anything else.

**How to place the rail across it:** for each vertex inside 779/781/785, walk perpendicular to
both walls of *that same parcel* and move the vertex to the midpoint. Vertices outside those three
parcels are in open country, where the parcels tile farmland and "inside a lot" means nothing —
**leave them alone.** Applied 2026-08-04: median off-centre went 4.3 m to 0.5 m, worst 13.5 m to
9.0 m, and the tightest clearance to a wall 1.5 m to 5.5 m. Through the platted town the track now
runs with 15.0 m of its own land on each side.

### THE DISTINCTION THAT MATTERS

**The strip is the RIGHT OF WAY. It is not the pavement.**

A 66 ft right-of-way in a town like this carries roughly 26–30 ft of driving surface, with the rest
as verge, sidewalk and utility strip. A 66 ft *street* would be enormous and Rossville does not have
one.

This was stated carelessly once — as "streets are 65 ft" — and the owner caught it immediately. The
consequence is the important part:

> **The current corridor widths are fine.** `RoadClass.Street` is 10 m (33 ft), which sits inside a
> 66 ft right-of-way with 5 m either side for verge and walk. **Only the placement is wrong.**
> Do not open the road fix by adjusting a width.

### Alleys are not uniform

Most are ~13 ft, but there is a genuine cluster at 20 ft — roughly one in eight. **Treat the alley
right-of-way as a range of 13–20 ft, not a constant.** The current `RoadClass.Alley` corridor of 4 m
(13 ft) is right for the common case and will under-fill the wide ones, which is the correct
direction to be wrong in.

---

## 3. The owner's standing facts

These sit **above every dataset**. A measurement that contradicts one of these is wrong until the
owner says otherwise.

1. **Route 1 and Chicago Street are the same road, and it curves.** Outside the village it is
   Route 1; inside it is Chicago Street. It is the 1829 Hubbard Trail and the town was platted
   square around a footpath that was already there. **It has been straightened once from OSM tags
   and that was wrong. Do not do it again.**

2. **Alleys run along the back lot line, behind the houses — never across a lot.** *"I have never
   seen a town where the alley runs right through their back yard."* This is assertable and is now
   asserted: see §5.

   > **ENFORCEMENT SUSPENDED 2026-08-04, by the owner, until the houses are re-derived.** *"I think
   > the house placement is getting in your way of road/alley and lot plotting... allow the
   > houses/buildings to be separate from the road, parcel, train tracks."*
   >
   > **The fact is not in question — only what it is being enforced against.** House positions are
   > authored and they inherited the OLD, wrong road positions. Holding this rule while the houses
   > were still wrong meant the houses were deciding where the alleys went: five alleys had already
   > been put back onto known-wrong ground to satisfy it. Released, alleys 2, 3 and 4 went straight
   > onto their right of way and the off-right-of-way count fell from 8 to 5.
   >
   > `RoadsSitOnPublicLandTests` now compares against an exact list of five alleys that may cross a
   > building. **That list may shrink and must never grow.** When the houses are re-seated against
   > the corrected streets it goes back to empty and the assertion goes back to `Is.Empty`.
   >
   > ⚠ **THE SUSPENSION IS LIFTED, 2026-08-10.** The condition it named has been met: the houses
   > have been re-derived against the corrected streets by `SeatOnSurvey`, `ClearOfRoads` and the
   > school and church corrections. **A ratchet, not a target, and that is the ruling as given —
   > "a ratchet only, no alley moves until you say so."** Going to zero means MOVING alleys, which
   > is what the suspension existed to prevent while the houses were wrong, and it is a separate
   > decision he has deliberately not taken.
   >
   > **AND THE RULE IS ABOUT A LOT, WHICH NOTHING HAD EVER ASSERTED.** The existing test measures
   > BUILDINGS — a house is a house, and that is the unambiguous half. But an alley can miss every
   > house on a block and still run up the middle of seven back gardens, which is what the sentence
   > above actually describes. `tools/check-roads.py` has printed it all along and no gate read it.
   > `NoAlleyRunsFurtherAcrossLotsThanTheWorstOneToday` measures it now, on the DRAWN path:
   >
   > ```
   >   alley1  78.4%   alley12 42.2%   alley8 15.7%   alley9 12.9%   alley5 11.4%
   >   16 alleys; 7 with more than a tenth of their length on a lot.   0% is right.
   > ```

3. **Harrison Street turns at the Benton junction. Green and Benton stop at Route 1.** Owner,
   2026-08-04, both called before either was measured:

   > *"look at Harrison street, it angles at the Harrison/Benton junction and it is assuming
   > straight at that jct and it is not"*

   Measured after the fact: Harrison's right of way runs 0.2° off north–south above Benton and
   **15.7° below it — a 15.4° corner at the junction**, walking 204 ft east by its south end. It
   was drawn as one straight line, so its whole southern half was off; 74% of it sat on private
   lots, now 0%. It is fitted as two straight legs meeting at the junction, **not smoothed** — it
   is a street corner and rounding it off would be inventing a curve.

   Green and Benton do reach Route 1 and terminate there, and their west ends **swing north** to
   meet it — 104 ft and 123 ft respectively. That part is data-led rather than owner-stated and is
   marked provisional in the tests until confirmed.

   **The pattern:** a square plat meeting a diagonal highway distorts near the join. The project
   previously asserted that only Chicago St and Railroad Ave bend; that was a Phase A regression
   guard, not a survey fact, and three roads have now broken it with the owner calling each one.

4. **An alley is not a road. It is the second way through town, and cars do not use it.**
   Owner, 2026-08-05, from having grown up here:

   > *"Most alleys were only used for trash pick up or parking their cars in the garage that
   > faced the alley… when I was a kid and rode my bike around, I would take alleys to get to
   > where I wanted. Cars would not normally use an alley."*

   **Why this is load-bearing rather than flavour.** It means Rossville has *two* movement
   networks laid over the same ground, and they are not used by the same people:

   | | streets | alleys |
   |---|---|---|
   | through traffic | yes | **no** |
   | cars at all | yes | only reaching a garage, and the trash truck |
   | on foot or on a bike | yes | **yes — by preference, for a kid** |
   | overlooked by | front windows, porches, the street | back windows, and much less of them |

   `INSIDE-THE-HOUSES.md` already found the physical half of this from the plans — *"every house
   has a front that faces a watched street and a back that faces an unwatched lane"* — and
   `WHO-SEES-WHOM.md` is built on who is overlooked by whom. This is the movement half: **there
   is a route through this town that adults in cars do not take and children on bikes do.**
   For a game about who was where and who noticed, that is not a detail about paving.

   **THE SURVEY AGREES, and it did not have to.** Measured off the 822 seated footprints against
   the derived alley network — the house and the garage face opposite ways, and the two
   populations flip cleanly:

   | | to the nearest street | to the nearest alley | nearer the alley |
   |---|---|---|---|
   | houses (n=572) | **81 ft** | 129 ft | 18% |
   | outbuildings (n=250) | 125 ft | **84 ft** | **55%** |

   A house is half as far from its street as from its alley; its garage is the other way round.
   That is the owner's account arriving independently out of federal imagery and a county
   cadastre, neither of which knows what an alley is for.

   **THE CODE CURRENTLY CONTRADICTS THIS.** Recorded so it is not mistaken for done:

   - `CityTraffic` weights spawns by class — `Freeway 12, Mainroad 8, Street 2, _ => 1` — and
     that final case is the alleys, so cars do drive down them, at half the rate of a street.
     Through traffic on an alley should be **none**.
   - Nothing in movement consults `RoadClass.Alley` at all. `Pathfinder` is a tile A* over
     `IsWalkable`, so an alley is simply a shortcut, taken by anyone when it happens to be
     shorter. Nobody prefers one and nobody avoids one.

   **What the model should be**, when someone builds it: no through traffic; local access only
   for a resident reaching their own garage and for the collection round; and a walking cost that
   makes the alley *attractive* rather than merely permitted — because the shortest path is not
   why a boy on a bike goes down the alley.

5. **A town lot ran from the street to the alley. The half-lots behind them are LATER.**
   Owner, 2026-08-05:

   > *"I know that the lots ran to the alley as I had a lot of friends in 1991 that lived along
   > Maple and their property went to the alley."*

   Raised on seeing strips behind the houses on E Maple that *"make no sense to me"* as lots.
   They make no sense because they are not lots. Measured, they are unmistakable:

   | | |
   |---|---|
   | size | **66 × 33 ft** — half of a 66 × 66 ft lot, one surveyor's chain wide |
   | distance to the alley | 22–27 ft — hard against it |
   | distance to the nearest street | 65–182 ft — nowhere near one |
   | street address | **none of them have one** |
   | buildings | 15 of 17 empty; where one is assessed, median **$4,312** against the town's ~$23,000 median dwelling — a garage, not a house |

   So they are the **back halves of the platted lots**, deeded off separately at some point
   after 1991 — to a neighbour, to a son, to whoever wanted somewhere to put a garage off the
   alley. The county has to number any separately-owned scrap of ground whether or not it is a
   building lot, so a split made in 1998 shows up as a boundary on a map of 1991.

   **Seventeen have been joined back to the lot in front of them** by
   `tools/merge-back-strips.py`, using the property mechanism in `Content/parcel-1991.txt`: the
   strip and the house lot share a property name, so they draw as one lot running to the alley.
   Fourteen of the seventeen share **exactly 66 ft** of boundary with the lot in front — the
   full width — which is what a clean back-half split looks like and is the reason to believe
   the join is right.

   **This bears on the setback and yard figures in `BUILDING-FOOTPRINTS.md`.** Those were
   measured against today's parcels, so on any lot whose back half had been split away the
   depth is short and the house looks less deep-set than it was. The medians are dominated by
   lots that were never split, but the figure is a floor rather than a centre.

---

### The drawn road and its survey line — re-affirmed 2026-08-10

**Ruled 2026-08-09 and asked again with numbers on 2026-08-10: leave it, corners stay rounded.**
A real street corner has a turning radius and a car cannot pivot on a point, so a junction drawn
as an arc is CORRECT and the divergence at a corner IS the arc.

Measured, so nobody has to ask a third time:

```
  16.71 m  summit    (3 declared points)      9.18 m  grove
   9.31 m  watson                             8.85 m  benton
   5.09 m  green
  44 roads with 3+ points; 16 over a metre, 5 over five
```

`DrawnRoadFollowsItsSurveyLineTests` ratchets the worst road and the count over five metres, and
both may only fall. **It is not a demand that the roads be straightened.** It exists because the
divergence decides whether two roads MEET: benton and summit are declared **0.20 m** apart and
drawn **8.3 m** apart, which is the whole of `ROAD-FIXES` ALLEY-2b's remaining half — the junction
is not missing, the two curves are.


### What the town is built of — settled 2026-08-09

Asked with a picture in hand. Two were contested by the evidence and put back to him, and he took
the evidence both times. `docs/TEXTURE-FIXES.md` carried these while that work was in flight and
was deleted when it finished, so they live here now.

| | Ruling | |
|---|---|---|
| **1** | Houses get three-tab **asphalt shingle** | settled |
| **2** | Mix: **slate grey 40 · charcoal 40 · brown 20**, with one roof in twenty a brown-black. **NO GREEN.** | settled |
| **3** | **Course exposure ~5–6 inches** on the slope, not the ~9 it would default to | settled |
| **4** | **Farm buildings: leave them entirely.** No override, no metal covering. | settled |
| **5** | Downtown is **flat built-up tar and gravel** | settled |
| **6** | **All seven building surfaces** in one pass, not roofs first | settled |

**On #2 the green is out on PERIOD grounds, not taste.** Faded green is a modern architectural
shingle and reads wrong for 1991 east-central Illinois. The pack's `Roof_Shingles_D` is exactly
that green and is the one sheet of five this town does not touch.

**On #3 the SPREAD is correct and is not a bug.** The four house types have different pitches, so
the exposure lands anywhere from 5.1 to 6.4 inches depending on which house you are standing in
front of. That is what a real street looks like. Do not "fix" it to a constant.

**On #4 there was never a fault, and he withdrew an earlier ruling once it was measured.** Zero of
the 652 generated roofs are farm buildings — `CityBuildings` handles `farm`, `barn` and `silo`, so
those are bought models arriving with the pack's own roofs. His "barns had shingles" ruling was
made on my framing that the models are *British*, which is true of the NAME (`Barn_Farm_British`)
and of the silhouette but not of the roofs. **Do not write the material override.** The same
reasoning killed a corrugated-metal covering: it would have had no buildings to go on.

**A consequence of #5 he accepted knowingly:** a detailed wall texture makes a UV-degenerate gable
end *more* obvious, not less. That is why the UV fixes had to land before the siding was bound,
and they did.

---

## 4. Where `city.txt` stands

`Content/city.txt` is **hand-authored** and holds 37 roads, 477 places, 373 doors and 148 human
lines. (Measured 2026-08-08. The roads it declares are not what the game drives on: `SurveyRoads`
replaces them at build time with the 66 in `Content/roads.txt`, which are surveyed rather than
ruled.) Two different kinds of thing are in there and they have different authority:

| in `city.txt` | authority |
|---|---|
| Road **coordinates** | **Derived — `parcels.txt` wins.** These get regenerated from the right-of-way strips. |
| Place kinds, names, doors, human lines, story anchors | **Authored — `city.txt` wins.** Nothing derives these and nothing may overwrite them. |
| A road the owner has personally corrected | **Authored — marked in the file, never regenerated.** Chicago Street's curve is the standing example. |

**Ruling, per the owner, 2026-08-04: when `city.txt` and `parcels.txt` disagree about where a road
is, the parcels win and the road gets moved.** Because houses are placed at a setback from roads,
moving a road means re-seating its lot rows in the same pass — see `POSTMORTEM-2026-08-03-ROADS.md`
§6 for why all four steps go together.

### THE REFIT LANDED, 2026-08-04

| | before | after | after decoupling |
|---|---|---|---|
| roads off their own right of way | 26 | 8 | **5** |
| median share of a road on private land | 80% | **0%** | **0%** |
| alleys crossing a building | 0 | 0 | 5 *(suspended, §3)* |
| buildings on no parcel | 100 | **76** | 76 |
| junctions | 115 | 125 | 126 |
| roads whose surface covers a building | 2 | 6 | 11 |

The last column is after the owner decoupled house placement from the road fit — see §3 fact 2. The
five roads still off are **alley 1, alley 12, Attica, Green and Railroad Ave**. Attica, Green and
Railroad each fit at a shift of 1–4 ft, meaning they are already on their strip; their reading comes
from the country stretches where the parcels tile farmland and "inside a lot" means nothing. Only
alley 1 and alley 12 are genuinely unplaced.

**Places move with the street they are ADDRESSED on.** A house sits at some setback from its street,
so shifting the house by the same amount as the street preserves the setback and lands it on the
right lot. The address names the street, so the pairing is authored rather than guessed; the
handful with no street in their name fall back to the nearest one, and that fallback is where most
of the residual damage is. 408 Holmes Ave went from sitting on **no parcel at all** to sitting on
parcel 777 — same lot, same block, same address.

Three things the refit taught, all of them the hard way:

1. **Moving a road breaks the junctions it used to make.** A street that dead-ended *at* another
   street stops reaching it once that street moves; the junction count fell 115 → 89 before anyone
   looked. Road ends are now extended to their cross street and 16 ft past it — an end that stops
   *on* a centreline only touches it, and the intersection finder wants a real crossing.
2. **An alley that cannot be laid without crossing a house is left where it was.** alley 1, 2, 3, 4
   and 12 are still off their right of way for exactly this reason. §3 fact 2 has no exemption list
   and stays at zero; being wrong in a recorded way beats being wrong in a hidden one.
3. **Ann is a lane, not a street** — owner's call, confirmed by its right of way measuring 25 ft
   against the 66 ft every platted street here gets. Reclassified, and its carriageway narrowed
   from 33 ft to 13 ft so it fits inside its own strip.

Per-road offsets: `docs/research/road-refit-deltas.txt`.

To compare against the town as it was before the refit, take it out of git rather than keeping a
second copy in `Content/` — a spare town file beside the real one is the exact ambiguity §1 exists
to prevent:

```
git show ece3f5a:Content/city.txt > /tmp/city-before-refit.txt
```

---

## 5. How this is enforced

A ruling nobody checks is what caused this. `tools/Noir.Core.Tests/RoadsSitOnPublicLandTests.cs`
reads `parcels.txt` and `city.txt` directly and asserts:

- **every road's centreline sits on public land**, recorded as an exact known-bad set so that both a
  regression *and* a fix fail the test and force this file to be updated
- **no road corridor covers a building footprint** — the unambiguous check, since a house is a house
- **alleys never cross a lot**, which is the owner's standing fact #2 made assertable

The known-bad sets in that file are the current state, not the target. They shrink as the refit
lands. **They are not allowed to grow.**

---

## 6. Why this file exists

`docs/research/SOURCING.md` already said *"Lot boundaries — Vermilion County parcels"*. It also
recorded, for Route 1 alone:

> *Verified against county parcels — 0 of 112 sample points fall inside a lot.*

That is the correct method, written down, and independently confirmed on 2026-08-03: Chicago Street
is the **one** mainroad that sits on its right-of-way. It is the one road anybody ever ran that check
against. The other forty never got it — 17 of 22 streets and 13 of 15 alleys are off their own
right-of-way by 3 to 25 m.

**The ruling was not missing. It was unenforced, and applied to a single road.** Two days went into
rediscovering it, and a correct finding was retracted along the way on a measurement whose sample
window was narrower than the error it was looking for.

That is the whole reason for §5. A ruling in prose is a suggestion; a ruling with a test is a rule.
