# The street grid, and what is wrong with it

Audited 2026-08-03 against OSM way 22037977 (IL 1), `tools/rossville-streets.json`, the county
parcels, and the 1940 USDA aerial. **No road code has been changed yet** — this records what was
measured, so the fix is not designed from memory.

---

## 1. The downtown does not front the road it is on

Measured, not eyeballed. The two shop rows are dead straight at x=726 and x=772. Route 1's
centreline moves from 758 to 815 across the same stretch. A shop fronting a 30 m road should sit
15–25 m off its centre **at every northing**:

| y | building x | road x | offset | |
|---|---|---|---|---|
| 1361 | 772 | 758.2 | **16.1** | correct |
| 1408 | 773 | 771.5 | **1.6** | the road is inside the barber |
| 1421 | 775 | 775.6 | **1.1** | the road is inside the steam laundry |
| 1513 | 772 | 803.2 | 33.1 | |
| 1363 | 727 | 754.4 | 29.3 | |
| 1459 | 726 | 782.1 | 59.1 | |
| 1566 | 726 | 814.7 | **94.1** | three street-widths from the road |

The east row starts correctly, **the road crosses through it**, and then leaves it behind. The west
row walks from 29 m to 94 m. The terrace was authored on a straight line and the road curves.

## 2. Route 1 crosses the north–south streets

Also measured, off the rasterised corridors — no projection involved. Chicago Street's corridor
centre runs from x=542 at y=700 to x=891 at y=2240. Ann Street is a fixed x=686 and Harrison a
fixed x=854, so:

| y | gap to Ann (declared −61) | gap to Harrison (declared +107) |
|---|---|---|
| 700 | **+144** | +312 |
| 1360 | −68 | +100 |
| 2240 | **−205** | **−37** |

Route 1 starts west of Ann and finishes east of Harrison. **Two platted streets cannot cross.**

## 3. What IS right

- **The north–south street positions**, to within a few metres of the real longitudes: Abner −221
  against −224, Watson −106/−109, Harrison +107/+104, Church +216/+213, Summit +402/+398, Grove
  +583/+579. Only **Goodwine is out, by 27 m**.
- **Block depth.** The 1940 aerial gives 117/125/117/117 m between street lines; `city.txt`
  declares 117–136 m; the real latitudes give 117 m York→Henderson and 125 m Holmes→Attica.
- **The curve itself.** `city.txt`'s polyline matches the real OSM way to a mean 16.9 m.

## 4. What is invented

- **The extents.** Every north–south street runs exactly y 699→2240 and every east–west street
  exactly x 496→1530 — a 1,034 × 1,541 m rectangle with every street starting and stopping
  together, running out into open fields at all four edges. Real grids fray; this one is a stencil.
- **The alleys, by omission.** The aerial shows a weaker bright line exactly halfway between every
  pair of streets — the standard mid-block service alley at ~58 m. `city.txt` has none, so every
  block in the model is twice its true depth and every lot backs onto another lot.
- **Uniform block size.** The owner's correction, and the data agrees: Rossville's blocks are
  square but **differ in size**. The real north–south spacings are 115, 35, 20, 54, 104, 109, 185,
  181, 141, 60 m — nothing like a repeating module.

## 5. RESOLVED — the curve was a TIGER artifact

**Route 1 and Chicago Street are one road.** Outside the village it is Illinois Route 1; inside it
is Chicago Street. It runs straight through the plat. Settled by the owner, who grew up here, and
confirmed by every measurement once the right question was asked.

The curve came from OpenStreetMap way 22037977. Its own tags condemn it:

```
tiger:reviewed = no
tiger:county   = Iroquois, IL      <-- Rossville is in VERMILION county
```

Raw, never-checked TIGER import of the rural highway from the county to the north. Nobody ever
traced its line through this town, and **OSM has no way named "Chicago Street" at all** — the
highway way swallowed the street and is misnamed "2300 East".

What settles it is OSM's *own* separate mapping of the town's streets. Measured end to end:

| street | length | degrees off square |
|---|---|---|
| East 3550 North Road | 4,713 m | **0.0** |
| East Attica Street | 1,029 m | 0.6 |
| South Summit | 871 m | 2.3 |
| Henderson | 744 m | 2.2 |
| Gilbert | 598 m | 1.6 |
| Railroad Avenue | 414 m | 39.0 — correct, it follows the rail |
| **"2300 East" (IL 1)** | **8,582 m** | **11.8** |

The grid is square to two or three degrees. Railroad Avenue is properly diagonal. The IL 1 way is
the **only** long feature in town that is neither — and it is the unreviewed one.

### What straightening it restored

`road chicago 30 747,0 747,2210 762,2280 800,2399` — straight across the plat (York y=729 to Earl
Court y=2210), bending only beyond it.

- **Downtown offsets: 20–28 m at every northing**, from 1.1–94.1 m. A consistent shopfront setback
  on a 30 m road, both rows.
- **Gap to Ann −61 m at every northing** (declared −61), Harrison +108 (declared +107). Constant
  the whole length; no street crosses another.
- **The lane graph returned to its pre-curve figures exactly** — 620 segments and 1692 turns,
  against 614/1656 while the curve was in. That the old numbers come back unchanged is the
  strongest single piece of evidence that the curve was the anomaly.

The real bend south of town is genuine and the owner describes it, but its **shape is not
established** — the only source is the same unreviewed way, so it is drawn as a modest departure
past y=2210 and should be replaced when something better turns up. The 1940 aerial is the obvious
candidate.
