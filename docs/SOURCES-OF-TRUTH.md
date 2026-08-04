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

3. *(space kept for the owner to add)*

4. *(space kept for the owner to add)*

---

## 4. Where `city.txt` stands

`Content/city.txt` is **hand-authored** and holds 41 roads, 477 places, 373 doors and 148 human
lines. Two different kinds of thing are in there and they have different authority:

| in `city.txt` | authority |
|---|---|
| Road **coordinates** | **Derived — `parcels.txt` wins.** These get regenerated from the right-of-way strips. |
| Place kinds, names, doors, human lines, story anchors | **Authored — `city.txt` wins.** Nothing derives these and nothing may overwrite them. |
| A road the owner has personally corrected | **Authored — marked in the file, never regenerated.** Chicago Street's curve is the standing example. |

**Ruling, per the owner, 2026-08-04: when `city.txt` and `parcels.txt` disagree about where a road
is, the parcels win and the road gets moved.** Because houses are placed at a setback from roads,
moving a road means re-seating its lot rows in the same pass — see `POSTMORTEM-2026-08-03-ROADS.md`
§6 for why all four steps go together.

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
