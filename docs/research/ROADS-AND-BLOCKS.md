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

## 5. RESOLVED — the road was right, the buildings were wrong

**Route 1 and Chicago Street are one road, and it curves.** Outside the village it is Illinois
Route 1; inside it, Chicago Street. Settled by the owner, who has driven it his whole life.

**Why a square town has a diagonal highway, which is the part that confused this for two days.**
This road is the **Hubbard Trail** — a footpath from 1829, Chicago to Danville, which became
Illinois's first state highway in 1833. A footpath does not follow section lines. The town was
platted square in 1857 *around a trail that was already there*. So the grid is cardinal, the road
that made the town cuts across it at about twenty degrees, and both are correct. See
`ROSSVILLE-HISTORY.md` §1, which said this all along: *"Route 1 is not an arbitrary line — it is a
1829 footpath that became a road."*

### A wrong turn, recorded because the reasoning was seductive

On 2026-08-03 the road was **straightened in error** and put back the same day. The case for
straightening looked strong: OSM way 22037977 carries `tiger:reviewed = no` and
`tiger:county = Iroquois, IL` while Rossville is in Vermilion — raw, unchecked 2007 census import —
and every other street in town measures square to two or three degrees.

All of that is true. None of it mattered. A bad source can still point the right way, and the
Hubbard Trail explains the diagonal completely. **The owner had already said twice that the road
was right and the shops were not aligned to it**; the correct response was to move the shops.

### What actually got fixed

The downtown was authored on two straight columns at x=726 and x=772 while the road curved away
from them. Every building in the strip has been re-laid against the centreline at a 20 m setback —
15 m of half-carriageway plus a 5 m walk:

| | before | after |
|---|---|---|
| offset from the centreline | **1.1 m to 94.1 m** | **20.5 m to 29.6 m** |
| the barber, the steam laundry | road passing **inside** them | 23.5 and 24.5 m |
| the west row at y=1566 | 94.1 m adrift | 25.3 m |

Thirty-five buildings moved, from the I.O.O.F. hall at y=1137 down to the north sub-office at
y=1558. The road did not move.

**Known limitation:** places in `city.txt` are axis-aligned rectangles with no rotation, so the row
*steps* along the curve instead of turning to face it, leaving small wedges between units. That is
a content-format limit, not a placement error, and it wants a rotation field on `place` before it
can be better.
