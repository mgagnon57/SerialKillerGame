# The residential blocks, read off the 1913 Sanborn sheet

Traced from `sanborn/rossville-1913-sheet4.jpg` (enlarged crop at
`sanborn/crop-residential-1913.png`), a block bounded by Summit, Gilbert, Stewart and Church.
Read directly off the primary document.

This is the other half of the town — the part the game currently builds out of Chicago
brownstones, which is wrong in every particular.

---

## What a residential block actually contains

A sample of eight platted lots from one crop:

| lot | what is on it |
|---|---|
| 55 | house, 1½ storey, articulated footprint with porch |
| 56 | house, 1½ storey, with a 2-storey element |
| 57 | **empty** — one outbuilding at the rear, no house |
| 58 | house, 1½ storey, at the street edge |
| 80 | house, 2 storey, set to one side |
| 81 | **empty** — one small outbuilding, no house |
| 82 | **completely empty** |
| 83 | house, 2 storey |

**Five houses on eight lots.** That is the density, and it is the thing a generated suburb gets
wrong by default: a real small-town block in 1913 is *substantially vacant*.

---

## Five rules the map states plainly

### 1. Houses sit at the FRONT of the lot

Every house in the sample is pushed toward the street, with the depth of the lot left open behind
it. The setback is shallow and consistent. There is no house in the middle of its lot.

### 2. There is an outbuilding at the BACK, and it is on almost every lot

Small squares with an X through them, at the rear boundary — barn, shed, privy, later a garage.
Note lots **57 and 81 have an outbuilding but no house**: the outbuilding is not a dependency of
the house, it is a feature of the *lot*.

### 3. Storey counts are annotated, and they are low

`1`, `1½`, `2` written inside each footprint. **1½ dominates**, with `1` for wings and porches and
`2` for the taller minority. Nothing above 2.

### 4. Footprints are ARTICULATED, not rectangles

This is the finding that matters most for the models. None of these houses is a simple box. They
are L-shaped and T-shaped — a main mass with an **ell** running back, plus smaller `1`-storey wings.
A rectangular footprint is the single most obvious tell of a generated town, and this map shows
the real thing is never rectangular.

### 5. Porches are drawn — as DASHED outlines

The thin dashed projections on the front and side of each house are open porches. They wrap
around a corner as often as they run straight across a front. They are drawn *lighter* than the
house because they are unwalled — which is exactly what a porch is.

---

## What this changes in the work already done

The four massing grammars committed in `FrameHouseGrammars.cs` are **right about the roofs and
wrong about the plan**:

| grammar says | the map says |
|---|---|
| ✅ 1½ storey farmhouse, steep gable | ✅ confirmed — `1½` over and over |
| ✅ 2 storey foursquare, low hip | ✅ confirmed — the `2` minority |
| ✅ porch on the front | ⚠️ **porches wrap corners**, they are not only frontal |
| ❌ rectangular footprint | ❌ **wrong — every house is L or T shaped** |
| ❌ outbuilding tied to the house | ❌ **wrong — the outbuilding belongs to the lot** |

Two concrete changes follow, neither of which needs Unity to decide:

- **`Massing` needs an ell.** A main mass plus a back wing, with its own lower ridge. That is the
  difference between "a house" and "a box with a gable on it", and it is cheap — one more rectangle
  and one more roof.
- **`RearOutbuilding` should move out of the house grammar** and become a property of the lot, so a
  vacant lot can still carry a shed. It is currently in `FrameHouse.RearOutbuilding`, called from
  each grammar's `Extras`, which cannot put a shed on an empty lot.

## And a density rule for the suburb generator

Roughly **60% of platted lots carry a house** in 1913. The current generator fills what it is
given. Leaving four lots in ten empty is not a shortcut — it is what the record shows, and it is
also why the county parcel data (median lot 1,011 m², a quarter acre) reads as generous.

By **2000**, the game's setting, occupancy is higher — the 1920s–40s bungalow layer filled much of
that gap, which is why the median build year is 1943. So: **1913 fabric, 2000 density.** The empty
lots of 1913 are where the bungalows went.

## Caveats

- One crop of one block. The 60% figure is a **sample, not a survey** — it should be counted
  across all four 1913 sheets before being used as a constant.
- Storey annotations are legible; the fainter interior markings (room counts, materials) are not,
  at this scan resolution.
- Lot numbers on the sheet are Sanborn's own and are **not** street addresses. The map's index
  says the house numbers are arbitrary.
