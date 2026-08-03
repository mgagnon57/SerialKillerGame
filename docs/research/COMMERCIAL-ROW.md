# The commercial row, unit by unit, with widths

Read off `sanborn/crop-attica-north-row.png`, enlarged from the 1913 sheet. **Every unit carries
its storey count and its frontage in feet**, written under it by the surveyor. This is the fabric
the sourcing doctrine says to build from — see `SOURCING.md`.

**Not yet wired to anything.** Deliberately kept here rather than dropped into `Content/`: a
content file nothing reads is the dead-data problem the health audit already flagged. It moves to
`Content/` when a generator consumes it.

---

## North side of Attica Street, west → east

| # | use | storeys | frontage | notes |
|---|---|---|---|---|
| 1 | Bank, **Offices 2nd** | 2 | 25 ft | west end of the row |
| 2 | Groceries / Cigars | 2 | 22 ft | **Lodge Rms 2nd** above |
| 3 | Confectionery | 2 | 25 ft | **Lodge Rms 2nd** above |
| 4 | **Jewelery** | 1 | 18 ft | single storey |
| 5 | Restaurant | 1 | 18 ft | single storey |
| 6 | **Motion Pictures** | 2 | 25 ft | the cinema |
| 7 | Tobacco | 2 | 25 ft | |
| 8 | Groceries | 2 | 25 ft | |
| 9 | Confectionery, dwelling above | 2 | 25 ft | east end |

**Total frontage ≈ 208 ft ≈ 63 m** for nine units.

Behind the row, in frame (yellow): **Tin Shop · Ice House · Bake Oven · Livery · Bakery.**

One construction note the surveyor added: a band marked **"IRON CLAD"** runs along the rear wall of
the lodge section — a metal-sheathed wall, a fire measure. Twenty years after the town burned down,
they were still building against it.

---

## West side of Chicago Street, north → south

From `crop-chicago-west-row.png`. Runs south from the Attica corner.

| # | use | storeys | frontage |
|---|---|---|---|
| 1 | Bank, **I.O.O.F. Hall 2nd** | 2 | corner unit |
| 2 | Dry Goods & Groceries | 2 | 22 ft |
| 3 | Dry Goods 1st, **Tailor 2nd** | 2 | 22 ft |
| 4 | Groceries 1st, **G.A.R. Hall 2nd** | 2 | 22 ft |
| 5 | Groceries | 1 | 18 ft |
| 6 | Hardware | 1 | 16 ft |
| 7 | Millinery | 1 | 18 ft |
| 8 | General Merchandise | 1 | 18 ft |
| 9 | Furniture | 1 | 18 ft |
| 10 | Barber | — | **frame**, south end |

Behind: Warehouse, Tin Shop, sheds, Agricultural Implements + shed — all frame.

## East side of Chicago Street, north → south

From `crop-chicago-east-row.png`.

| # | use | storeys |
|---|---|---|
| 1 | Drugs | 2 — corner |
| 2 | Restaurant, **Lodge Rms 2nd** | 2 |
| 3 | Barber, **Office 2nd** | 2 |
| 4 | Drugs 1st, **Law Office 2nd** *(marked vacant)* | 2 |
| 5 | Groceries 1st, **Dentist 2nd** | 2 |
| 6 | Novelties | 1 |
| 7 | Meat | 1 |
| 8 | Barber | 1 |
| 9 | Hardware / Plumbing | 1 |
| 10 | Groceries | 1 |
| 11 | **Rossville Steam Laundry** | 1 |
| 12 | Printing | 1 |
| 13 | Blacksmith *(elec. motor)* | 1 |

Behind: Poultry Killing, a gasoline tank, and the **gas generator for lighting the shoe factory**.

## The Attica frontage east of Chicago, west → east

| use | storeys | frontage |
|---|---|---|
| Confectionery | 2 | 26 ft |
| Clothing | 2 | 26 ft |
| Meat | 1 | 15 ft |
| "The Potter", dwelling above | 2 | 25 ft |
| **Harness & Vehicle Repairs** | 2 | 48 ft — the widest unit downtown |
| Agricultural Implements | 2 | 28 ft |
| Groceries | 1 | 18 ft |
| Office / Wall Paper | 1 | 14 ft |

---

## THE RULE ALL FOUR FACES SHARE

Put the faces side by side and one pattern governs the whole downtown:

> **Height and width decay with distance from the crossing.**

The corner of Attica × Chicago carries two-storey units of 22–26 ft with **halls, offices, a
dentist and a law office upstairs**. Walk away from it in any direction and the buildings drop to
one storey, narrow to 14–18 ft, and the trades get heavier and dirtier — millinery gives way to
hardware, then to a steam laundry, then to a blacksmith, then to frame sheds and poultry killing.

That gradient is the single most useful thing in this document. It is not decoration; it is land
value written in brick, and it means a generator does not need to place each unit by hand. It
needs:

- **distance from the crossing** → storeys (2 near, 1 far), width (26 ft near, 14 ft far)
- **the corner units are the anchors** — bank, drugs, and the fraternal halls above them
- **the row degrades to frame at its far ends** — the brick simply stops

## The pattern this gives the generator

1. **25 ft is the default unit.** Six of nine. Two at 18 ft, one at 22 ft.
2. **Two storeys is the default.** Seven of nine. The two single-storey units are the *narrow*
   ones — jeweller and restaurant, both 18 ft. Narrow and low go together.
3. **The upper floor is not the shop.** Offices over the bank; a lodge hall spanning **two**
   shopfronts; a dwelling over the confectioner. The first floor is let separately from the ground
   floor, which is why the row reads as varied above and uniform below.
4. **A continuous terrace.** Party walls, no gaps. The variation is in *width and height*, never
   in setback.

### In metres, for building

| frontage | metres |
|---|---|
| 18 ft | **5.5 m** |
| 22 ft | **6.7 m** |
| 25 ft | **7.6 m** |

A generator that lays units of 5.5 / 6.7 / 7.6 m along a street frontage, two storeys by default
and one for the narrow ones, produces this row. That is a small, specific rule — and it is read off
a survey rather than invented.

---

## Confidence

**Solid:** the sequence of uses, the storey counts, the presence of lodge rooms spanning two units,
the frame service buildings behind, the iron-clad wall.

**Read at the limit of the scan:** the individual widths. The figures are legible as `25'`, `22'`
and `18'` and the pattern is consistent, but a single digit misread would change one unit by a
few feet. The *distribution* — mostly 25 with a couple of narrow 18s — is not in doubt; an
individual number might be.

**Not established:** the depth of the units. The footprints show they are not uniform, and several
have rear extensions of different lengths, but no depth figure is annotated.

**The house numbers on the sheet are arbitrary** and say so in the map's own index. The `#` column
above is sequence along the street, not an address.
