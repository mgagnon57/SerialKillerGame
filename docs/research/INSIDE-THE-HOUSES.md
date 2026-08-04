# Inside the houses

Compiled 2026-08-03. The project has footprints, materials, storey counts and lot sizes — the
outside of every building in town. It has nothing about the **inside**, and the game has interiors,
lit windows and a *"who is inside this building"* panel that all depend on rooms.

This fills that in from two directions: the footprint distribution already measured off the 1913
Sanborn sheets, and a **primary document for the era's standard plans** — the 1926 Sears *Modern
Homes* supplement, whose room dimensions and ceiling heights are printed on the page.

> **What the Sears catalogue is and is not.** It is **not** a claim that Rossville's houses are
> Sears kits. It is evidence of what a builder's-plan house of the 1920s–40s actually measured,
> because that is the layer this town is mostly made of — median build year **1943**, 54% pre-1950.
> Kit and plan-book houses were the vernacular, and the catalogue documents it.
>
> Though it is worth noting kits shipped **by rail in a boxcar**, and Rossville is on a rail line
> with a depot. Some of these houses may literally be kits. Nothing here establishes that.

---

## Measured, from the 1926 catalogue

**Ceiling heights** — these are printed and they are consistent:

| | height |
|---|---|
| ground floor | **8 ft 6 in to 9 ft** |
| upper floor | **8 ft** |
| basement, floor to joists | **7 ft** |

That is noticeably taller than a modern house and it is the single easiest thing to get wrong. A
9-foot ground-floor ceiling changes window proportion, stair length and how a room reads.

**Room dimensions**, as printed across the catalogue's plans:

```
 9'2" x 10'7"     small bedroom
11'11" x 12'6"    bedroom
12'2" x  9'8"     bedroom / small dining
14'5" x 12'0"     dining room
14'6" x 13'3"     dining room
11'8" x 15'2"     living room
19'6" x 11'3"     living room
21'6" x 15'0"     large living room
12'2" x 23'3"     a long through-room
```

So: **bedrooms cluster around 9×11 to 12×13 feet; living rooms run 15 to 21 feet long.** Nothing is
large. A 12×13 bedroom is a generous one.

**Porches are long and shallow** — and this is exactly what the Sanborn sheets show as thin dashed
projections:

```
25' x  6'        16' x 7'        9' x 22'        7'6" x 19'
```

**Six to nine feet deep, sixteen to twenty-five feet long.** A porch runs across the front; it does
not project.

**Room counts** as advertised: *six rooms and bath bungalow*, *seven or eight rooms*, *eight rooms
and bath*, *nine rooms*. The era's range is **five to eight rooms plus one bathroom** — and *one*
is the number. A second bathroom is not a thing in this stock.

---

## And a contrast worth catching

Sears sold these as fitting *"a lot 31 feet wide"* and *"a lot 47 feet wide"* — they were designed
for **narrow city lots**.

**Rossville's lots are a quarter acre**, median **1,011 m²**, confirmed twice over from independent
datasets. A typical town lot here is something like 60–70 ft wide and very deep.

> **So the same house that is squeezed onto a Chicago side street sits on a Rossville lot with room
> all round it.** Houses are pushed to the *front* of the lot (`RESIDENTIAL-1913.md`), the depth is
> left open behind, and there is an outbuilding at the back. The gap between houses is real and
> walkable — which matters for sightlines, and matters more for the alley network in
> `WHO-SEES-WHOM.md`.

---

## Rooms, derived from the footprints we already measured

`BUILDING-CENSUS-1913.md` gives frame dwelling footprints; the Sanborn sheets give storeys, with
**1½ dominant and nothing above 2**. Combining them:

| | footprint | typical storeys | floor area | rooms + bath |
|---|---|---|---|---|
| p10 | 54 m² / 581 sq ft | 1 | ~580 sq ft | **4** — living, kitchen, two beds |
| **median** | **97 m² / 1,044 sq ft** | **1½** | **~1,500 sq ft** | **6** |
| p75 | 125 m² / 1,340 sq ft | 1½–2 | ~2,000 sq ft | 7 |
| p90 | 163 m² / 1,754 sq ft | 2 | ~3,000 sq ft | **8–9** |

**The median cross-checks cleanly.** `BUILDING-CENSUS-1913.md` independently reasoned that 97 m² at
1½ storeys is "roughly 1,500 sq ft of floor — exactly what a modest 1913 Illinois farmhouse was",
and the catalogue's six-to-seven-room houses are that size. Two sources, arrived at separately,
landing on the same house.

**Three-to-one from smallest to largest still holds inside.** A four-room house and a nine-room
house are on the same street.

---

## The plan itself

*Established architectural vernacular rather than a Rossville measurement — but it is extremely
consistent across the period, and the catalogue's room lists match it.*

**The bungalow / minimal-traditional layer (1920s–40s — the largest cohort):**

```
        [ porch, full width, 6-9 ft deep ]
                     |
              LIVING ROOM  (across the front)
                     |
              DINING ROOM  -- wide cased opening, not a door
                /         \
          short hall       KITCHEN  (at the rear)
          /    |    \          |
       BED   BED   BATH     back door -> yard -> outbuilding -> ALLEY
```

Three features matter for this game:

1. **There is no entrance hall.** The front door opens from the porch **straight into the living
   room**. Anyone at the front door is already looking at the room where the family sits. This is
   the defining feature of the type and it is the opposite of the older houses.
2. **The kitchen is at the back, and the back door faces the yard** — the outbuilding, and beyond
   it the alley. **Every house has a front that faces a watched street and a back that faces an
   unwatched lane.**
3. **One bathroom, off a short hall**, between the bedrooms.

**The older 1857–1910s layer** — what the Sanborn sheets actually draw — is different: an
**L or T plan** with a main mass and a back ell, a front hall with the stair in it, parlour and
sitting room, kitchen in the ell. Porches that **wrap the corner** rather than only crossing the
front. This is the minority cohort by 1991 but it is the one on the oldest streets.

---

## What this changes in the build

1. **Ceilings at 8'6"–9' downstairs**, 8' up, 7' in the basement. Not modern heights.
2. **Bedrooms 9×11 to 12×13 ft; living rooms 15–21 ft long.** Rooms are small.
3. **Room count from footprint**, on the table above — four rooms at the bottom of the
   distribution, eight or nine at the top.
4. **One bathroom.** Always.
5. **Front door opens into the living room** in the 1920s–40s stock; a front hall with a stair in
   the pre-1910s stock. That is a real behavioural difference for anybody knocking.
6. **A back door to the yard on every house**, and the yard leads to the outbuilding and the alley.
7. **Porches 6–9 ft deep across the front**, and wrapping the corner on the older houses.

---

## Confidence, and what is missing

**Documented:** ceiling heights, room dimensions, porch dimensions and room counts, all printed in
the 1926 catalogue. Footprints and storeys, from the Sanborn classification already in this repo.
Lot size, confirmed twice.

**Established vernacular, not measured here:** the plan diagram. The arrangement is consistent
across the period and the catalogue's room lists are consistent with it, but no Rossville house has
been surveyed and no floor plan of any specific house in this town has been seen.

**Missing entirely:** basements. Sanborn does not draw them and nothing here establishes how many
of these houses have one, though 7 ft floor-to-joist appears in the catalogue and eastern Illinois
generally builds them. **Also missing: heating.** The 1906 sheet records steam heat in the schools
and the 1913 downtown has it, but nothing says what a house burned in 1991 — and a coal chute, an
oil tank or a gas furnace are different objects in a basement.
