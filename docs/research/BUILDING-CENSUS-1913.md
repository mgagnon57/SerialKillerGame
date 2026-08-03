# Every building in town, counted

The 1913 Sanborn sheets colour every footprint by what it is made of. That colour key is machine
readable, so instead of reading a sample of eight lots off one crop, **all four sheets were
classified pixel by pixel and every footprint counted** — 503 buildings.

This replaces eyeballed impressions with numbers. It is also the first measurement in this whole
research set that says how *big* a Rossville house actually is.

Method and reproduction at the bottom. Evidence: `classified-1913-sheet2.png` (downtown) and
`classified-1913-sheet4.png` (residential).

---

## The count

| | frame (Sanborn yellow) | brick (Sanborn red) | frame share |
|---|---|---|---|
| **all buildings** | **473** | **30** | **94.0%** |
| dwelling-sized (≥ 45 m²) | 247 | 21 | 92.2% |
| outbuildings (4–45 m²) | 226 | — | — |

But the 21 brick "dwelling-sized" footprints are not houses. **Sixteen of them are on sheet 2** —
they are the commercial terrace at Attica × Chicago, shops rather than homes. Outside the downtown
the brick count over the whole town is **five buildings**: three on sheet 1, one on sheet 3, one on
sheet 4. Given the 1906 sheet shows two brick schools, and the churches, those five are almost
certainly institutional.

> **So: essentially no brick houses in Rossville.** Not "mostly frame" — the residential town is
> frame with a handful of civic exceptions, and every one of those exceptions is a landmark rather
> than a home. `classified-1913-sheet4.png` is a whole residential sheet with **one** red building
> on it.

This confirms by count what `RESIDENTIAL-1913.md` inferred from one crop. The two agree, and they
were arrived at independently.

---

## How big a house is — the number the massing grammars never had

Frame footprints of dwelling size, across the town:

| | m² | sq ft |
|---|---|---|
| p10 | 54 | 585 |
| p25 | 75 | 802 |
| **p50 (median)** | **97** | **1,047** |
| p75 | 125 | 1,340 |
| p90 | 163 | 1,756 |

A **97 m² footprint at 1½ storeys** is roughly 1,500 sq ft of floor — which is exactly what a
modest 1913 Illinois farmhouse was. The spread matters as much as the median: the p90 house is
**three times** the p10 house. A generated street where every house is the median is wrong in a way
that is obvious at a glance.

### And the outbuildings, which are their own size class

The size distribution is clearly bimodal, and the gap falls around 45 m²:

| class | n | median |
|---|---|---|
| sheds, privies | 67 | **11 m²** (120 sq ft) |
| barns, stables, later garages | 46 | **27 m²** (290 sq ft) |

Counting the whole town: **226 outbuildings against 268 dwellings — 0.84 per house.** That is the
quantitative form of what the crop showed by eye: nearly every lot has something at the back of
it, and `RESIDENTIAL-1913.md` is right that the outbuilding belongs to the *lot* rather than to the
house, because some lots carry one with no house at all.

---

## Cross-checks

**Against population.** 268 dwellings against the Sanborn title block's "population 1,500" gives
**5.6 people per dwelling**. High, but 1913 households were large and took boarders, and the
threshold between "small house" and "large barn" is a judgement call that moves this figure — a
45 m² cut gives 5.6, a lower cut gives nearer 4.5. Consistent, not precise.

**Against the modern assessor, and against the Census.** 268 dwellings in 1913 against **517
improved residential parcels** today: the town nearly doubled its houses while its population fell
from 1,500 to 1,217, which is the ordinary twentieth-century story of household size collapsing.

The Census ACS gives a sharper test than that. It puts **48.2%** of the current housing stock in
its bottom bucket, **"1939 or earlier"**. Pre-1913 houses are necessarily a subset of pre-1940
ones, so this count's 268 of 517 — **51.8%** — should sit *below* 48.2% and instead sits 3.6
points above it.

That gap is the measurement, not the town. The whole count hangs on a 45 m² threshold separating a
small house from a large barn, and moving it a few square metres moves the dwelling count by more
than nineteen buildings. **So the two surveys agree to within 8%, and the honest reading is that
roughly half of Rossville's houses predate the First World War.** Anything that depends on the
third significant figure of that is depending on the threshold, not on the evidence.

**Against the commercial row.** The classifier puts brick exactly where `COMMERCIAL-ROW.md` says
the terrace is, in one continuous mass at the crossing with no gaps in it, and puts frame
everywhere else. That is a transcription made by eye and a measurement made by machine landing on
the same answer.

---

## What this changes in the build

1. **House footprints should be drawn from a distribution, not a constant.** Median 97 m², p10 54,
   p90 163. Three-to-one from smallest to largest.
2. **Brick is for landmarks only.** A brick house anywhere but the downtown terrace is wrong. The
   schools, the churches and the shops are the exceptions, and they should read as exceptions.
3. **Outbuildings at 0.84 per house**, in two size classes — an 11 m² shed and a 27 m² barn.
4. **Nothing above two storeys**, confirmed across every sheet.

---

## Method, so this can be re-run or disputed

Sanborn's colour key is fixed: **yellow = frame, red = brick, blue = stone, gray = iron, brown =
fireproof.** The scans are low-saturation but the hue survives, so classification is by channel
relationship rather than by absolute colour:

- **frame** — blue is the minimum channel, red ≈ green, saturation ≥ 30
- **brick** — red is the maximum channel, green ≈ blue, red − green ≥ 30
- **stone** — blue is the maximum channel. **Zero pixels town-wide**, which is expected: there is no
  stone construction in a prairie town with its own brickworks.

Footprints are connected components after a 3×3 binary closing (to heal JPEG speckle) and hole
filling, discarding anything under 4 m² as ink.

**Scale** is read off the sheets' own printed bar — "Scale of Feet", graduated 50/100/150 — which
measures **73.3 px per 50 ft**, giving **1 px = 0.208 m** and 1 px² = 0.0432 m². Every area on this
page derives from that one measurement, so if the bar was misread every figure scales together.

### Known limits

- **The scale bar was read by eye off a magnified crop.** A 2% error there is a 4% error in every
  area quoted. The medians are robust; the third significant figure is not.
- **Adjacent buildings that touch merge into one component.** In the residential blocks this is
  rare, which is why the counts there are trustworthy; along the downtown terrace it is universal,
  which is why the commercial units are **not** counted this way — `COMMERCIAL-ROW.md` transcribes
  them individually from the surveyor's own annotations instead.
- **Porches are drawn dashed and unfilled**, so they are not in these footprint areas. A house's
  actual ground coverage is larger than the figure here by the depth of its porch.
- Sanborn mapped the insurable built-up area, not the municipality. Anything on the edge of town
  that no insurer cared about is not on these sheets and so is not in this count.
