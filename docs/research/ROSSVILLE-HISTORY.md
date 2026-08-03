# Rossville, Illinois — what the record actually says

Research compiled 2026-08-03. Every claim here is sourced; where a thing is inferred rather than
found, it says so. **Nothing in this document is about a living person.** It is about a place: how
it came to be where it is, what was built on it, and what it did for a living.

---

## The one-paragraph version

Rossville exists because of a footpath. Gurdon Hubbard's trading trail from Chicago to Danville
ran past a lodging house John Liggett built in 1829, and the settlement that grew there was called
**Liggett's Grove**. In **1833 that trail became Illinois's first state highway** — it is Illinois
Route 1 today, and part of the Dixie Highway from 1914. The village incorporated in **August 1859**,
named for the settler **Jacob Ross**. The **Chicago & Eastern Illinois Railroad** arrived and cut
across the town on a diagonal, which is why the rail line and the street grid disagree in angle to
this day. A fire took a quarter of the downtown in **1893**; it was rebuilt in brick, from brick
made at a works inside the village. The town supported itself on grain, a canning factory and
lumber, peaked around **1,500 people** — a figure it had already reached by 1898 and has never
exceeded — became the self-described **"Antique Capital of Illinois"** in the 1960s–70s, and then
lost another quarter of its downtown to a **second fire in February 2004**.

---

## 1. Founding, and why the road is where it is

| when | what |
|---|---|
| 1829 | **John Liggett** builds a lodging house. The place is called **Liggett's Grove**. |
| 1829 | Liggett's cabin stands on the **Hubbard Trail** — Gurdon Hubbard's trading route, Chicago to Danville. |
| **1833** | **The trail becomes Illinois's first state highway.** It is Illinois Route 1 today. |
| 1838 | **Alvan Gilbert** buys the Liggett farm. |
| 1839 | Gilbert becomes postmaster of the new post office. |
| **Aug 1859** | **Rossville incorporates as a village**, named for settler **Jacob Ross**. |
| 1914 | Route 1 becomes part of the **Dixie Highway**. |

Sources: [Village of Rossville](https://villageofrossville.org/our-history),
[Wikipedia](https://en.wikipedia.org/wiki/Rossville,_Illinois),
[History in Your Own Backyard](https://historyinyourownbackyard.com/video/historic-rossville-illinois/).

> **This matters for the simulation more than anything else here.** Route 1 is not an arbitrary
> line — it is a **1829 footpath that became a road**. Its curve is the shape of the ground and of
> how people actually walked between Chicago and Danville. Modelling it as a straight line was not
> merely inaccurate; it erased the reason the town exists.

The Gilbert name recurs constantly in the plat: *A. Gilbert's First Addition*, *Gilbert's Second*,
*Third*, *Fourth*, *Gilbert & Satterthwaite's Addition*, and Gilbert Street itself. The family that
bought the founding farm subdivided most of the town.

---

## 2. The railroad

The line through Rossville — today **CSX's Woodland Subdivision**, running NNW–SSE on a diagonal —
was the **Chicago & Eastern Illinois Railroad (C&EI)**. It is labelled **"C. & E. I. R. R."** on
the Sanborn maps of 1898 and 1913.

- The **depot was built by the C&EI in 1903**. It survives, restored to its 1950s appearance, and
  is now the **Rossville Depot Railroad Museum**, run by the Danville Junction Chapter of the
  National Railway Historical Society.
- Sources: [Enjoy Illinois](https://www.enjoyillinois.com/explore/listing/rossville-depot-railroad-museum/),
  [Danville Area Visitors Bureau](https://www.visitdanvillearea.com/attractions/museums-historic-sites-markers/the-depot-museum/).

**The angle disagreement is real and historical.** The street grid is laid to the cardinal
directions; the railroad runs diagonally through it. The 1898 and 1913 Sanborn sheets show the
grid and the rail crossing at a clear skew, with **stock pens**, elevators, lumber yards and the
canning factory all strung along the tracks rather than along the streets. Industry followed the
rail; houses followed the grid.

---

## 3. The two fires

### 1893 — and where the brick came from

A fire destroyed **about a quarter of the downtown commercial section** in 1893. It was rebuilt in
**brick**, on **East Attica Street**.
([Village of Rossville](https://villageofrossville.org/our-history),
[HIYOB](https://historyinyourownbackyard.com/video/historic-rossville-illinois/))

The 1898 Sanborn sheet shows why brick was available: **Habel Bros. Tile Works**, inside the
village at McKibben & Gilbert, *"capacity 25,000 brick per day & 2,000 tile per day"*, with two
kilns and a brick kiln shed. A town with its own brickworks rebuilds in brick.

### 27 February 2004 — inside living memory

A large fire destroyed **another quarter of the downtown commercial section, opposite the 1893
burn**. It took a whole block of businesses — a restaurant, gift shops, and many of the **antique
galleries the village was known for**. Some damaged buildings had to be demolished afterwards.

**A Casey's petrol station now stands on the corner where those shops were.** The village lost its
antique-shop identity as a result, and has since used a **tax increment financing (TIF) district**
to encourage redevelopment.

Sources: [News-Gazette](https://www.news-gazette.com/living/rossville-changes-after-2004-fire-with-times/article_5002a309-9179-4bf2-8bea-b2d43addf418.html),
[HIYOB](https://historyinyourownbackyard.com/video/historic-rossville-illinois/).

> For a game set in **1995–2006**, the 2004 fire falls *inside the story's window*. The downtown
> changes shape partway through the period being simulated.

**And the antique trade was already dying when it burned.** eBay had been eroding the small-town
antique business through the late 1990s. The fire did not interrupt a thriving district; it
finished one that was already going.

---

## 4. The Sanborn maps — the best source we have

**Five fire-insurance atlases of Rossville are public domain and downloaded** to
`docs/research/sanborn/`: **1898 (2 sheets), 1906 (3), 1913 (5)**. Two later sets exist at the
Library of Congress — **1927 and 1933** — but did not expose downloadable images through the API.

Collection: [LOC Sanborn Maps — Rossville](https://www.loc.gov/collections/sanborn-maps/?fa=location:vermilion+county%7Clocation:illinois%7Clocation:rossville)

These are the single most useful documents for this project, because they record, building by
building: **footprint, construction material, number of storeys, and use.**

### The colour key, which answers the housing question

```
YELLOW = frame        RED = brick        BLUE = stone
GRAY   = iron         BROWN = fire proof
```

**The commercial core is red. Everything residential is yellow.** Rossville is a **frame town with
a brick main street** — small wood-framed houses on a grid, around one tightly packed block of
two-storey brick commercial buildings at **Attica × Chicago**, which is exactly the crossing the
simulation uses as its origin.

A handful of houses are annotated **"STUCCOED"**. Nothing residential is drawn in brick.

**Checked against a purely residential sheet** (1913, sheet 4 — Summit / Gilbert / Stewart /
Church / McKibben / Chicago): **not one brick house on the entire sheet.** Every building is
yellow, marked **"D"** for dwelling, annotated **1, 1½ or 2 storeys** — overwhelmingly 1 and 1½ —
each with a small rear outbuilding, standing on **large and mostly empty lots**. Whole blocks carry
three or four houses among a dozen numbered lots.

At the southern edge of that sheet the town simply stops and the map is labelled **"FARM LAND."**

This is exactly the pattern the simulation already has from the county parcel data — median lot
1,011 m², about a quarter acre, sparsely built, with a hard edge to farmland. **The lots were
right; only the houses standing on them were wrong.**

### What stood downtown in 1913

From the 1913 sheet 2, at and around Attica × Chicago — all brick:

**Public and fraternal:** Opera House (with **Masonic Lodge** above; light electric, heat steam) ·
Township Hall · Fire Department (*2 hose reels, 1 hook & ladder truck*) · Electric Light Plant
(*"not in operation"*) · **G.A.R. Hall** (Grand Army of the Republic — the Civil War veterans'
organisation) · Lodge Rooms on several second floors

**Trade:** bank (offices above) · jeweller · confectionery · bakery · restaurant · meat market ·
groceries · hardware (two) · drugs · millinery · furniture · barber · plumbing · novelties ·
tailor · printing · **Rossville Steam Laundry** · livery stables and hitching · garage & machine
shop · general store

**Churches:** Presbyterian (*15' to eaves*) · Methodist Episcopal (*20' to eaves*) · Christian ·
United Brethren

**Lodging:** Park View House · Windsor Hotel

### Industry along the tracks

| 1898 | 1913 |
|---|---|
| Ed. Putnam Elevator — **60,000 bu** | Wm. Prillaman Elevator No. 2 — 10,000 bu |
| Wm. Prillaman Elevator No. 1 — **70,000 bu** | Geo. L. Merritt & Co. Elevator |
| Wm. Prillaman Elevator No. 2 — 12,000 bu | F. A. Smith Lumber Co. |
| Andrews Bros. Lumber Yard | J. E. Swift Lumber Yard |
| **Cronkhite & Austin Wagon Works** — 1 rip saw, 1 wood shaper, 1 cut-off saw, 1 scroll saw, **3 blacksmith forges** | **Rossville Canning Co.** — warehouses, husking shed, **pea viners**, kraut manufacturing |
| **Habel Bros. Tile Works** — 25,000 brick/day, 2 kilns | Chambers Stock Food Co. · Creamery · Steam Laundry · Water Works |
| **Stock pens** along the C&EI | Rossville Public School · High School |

The 1898 sheet carries a melancholy note beside the canning works: **"CANNING FACTORIES STORAGE
(MAIN FACTORY DESTROYED BY FIRE)"** — so the cannery had already burned once before 1898 and was
rebuilt by 1913. Canning fits the region: Hoopeston, the next town north, was a canning centre.

### Utilities, recorded contemporaneously

- **1898:** waterworks standpipe **120 ft** above the business centre, two driven wells, 2 Gould
  pumps, 100 gal/min, 4,000 gal average daily consumption, 4 miles of mains, 18 double hydrants.
  Volunteer fire department, 30 men.
- **1913:** two 8" wells **126' and 130' deep**, two Erb pumps, standpipe **85 ft**, 53,200 gal
  capacity, mains laid since 1896, **30 double hydrants**, domestic pressure 40 lb, fire pressure
  80 lb. Volunteer department, 20 men, 1,200 ft of hose. *"Streets graded & level. Public lights:
  electric."*
- **Today:** the village still runs its own **gas, water and sewer**; wells at 127' and 135',
  300 gpm, and a **150,000-gallon spherical tank**.
  ([Village of Rossville](https://villageofrossville.org/our-history))

> *"Streets graded & level"*, written in 1913, independently corroborates the USGS elevation data:
> 24.5 m of relief across the whole 2.1 × 2.4 km map, a 1% grade. This is till plain. Anything that
> looks like a hill is wrong.

---

## 5. Population — the town was bigger a century ago

| year | population | source |
|---|---|---|
| 1898 | ~1,500 | Sanborn title block (mapmaker's estimate) |
| 1913 | ~1,500 | Sanborn title block |
| **1920** | **1,588 — the peak** | census |
| 1930–1990 | a shallow plateau, **1,300–1,470** | census |
| **2000** | **1,217 — the low** | census |
| 2010 | 1,331 | census |
| today | ~1,150–1,250 | estimate |

**The decline is gentle, not a collapse.** About **−23% from peak across a century** — far softer
than Danville (−31% from its 1970 peak) or Vermilion County as a whole (−24% since 1970, and
accelerating). Rossville did not empty out; it slowly thinned.

Note the shape: the town's **low point is the year 2000**, and it then *rose* through the decade.
Full decade-by-decade series in `.superpowers/research/rossville-economy.md`.

---

## 6. The antique era, and its end

In the **1960s and 1970s** Rossville called itself the **"Antique Capital of Illinois"**, on the
strength of the number of antique shops in the downtown brick blocks. At its height it drew **as
many as seven Greyhound buses a day** of visitors — into a village of about 1,200 people.

The **2004 fire ended it.** It destroyed the galleries and boutiques the trade depended on, and the
identity did not recover.
([HIYOB](https://historyinyourownbackyard.com/video/historic-rossville-illinois/),
[News-Gazette](https://www.news-gazette.com/living/rossville-changes-after-2004-fire-with-times/article_5002a309-9179-4bf2-8bea-b2d43addf418.html))

---

## 6a. What was happening *around* Rossville in the game's window

This is the part that matters for writing people rather than buildings. A character in Rossville
around 2000 is not nostalgic about a distant golden age — they have **recent, specific bad news**
from just up and down Route 1:

| when | what | where |
|---|---|---|
| **1995** | **GM foundry closes** — roughly **$50 million** of annual payroll gone | Danville, 15 miles south |
| **1998** | Chiquita closes one of the two historic **canneries** | Hoopeston, 9 miles north |
| late 1990s | **eBay** erodes the small-town antique trade | Rossville's own main street |
| **2004** | **FMC farm-equipment plant** closes | Hoopeston |
| **27 Feb 2004** | **the downtown fire** | Rossville |
| **2006** | **Rossville-Alvin High School closes** | Rossville |

Six things in eleven years, and four of them land in the last three. **Coal was never Rossville's
story** — Vermilion County coal was real but concentrated at Danville, Westville, Catlin and
Georgetown. Rossville was grain, canning and the railroad.

Nothing from the 1898–1913 industrial peak — three competing grain elevators, the wagon works, the
brick and tile works, the cannery with its own sauerkraut plant, the creamery, two hotels, the
opera house — survived to the game's era.

Source: `.superpowers/research/rossville-economy.md`, with citations.

## 7. Institutions today

- **Rossville-Alvin High School closed in 2006.** Students now attend Bismarck-Henning-Rossville-Alvin
  High School under a co-operative arrangement. The village retains its own grade school.
- **Rossville Historical & Genealogical Society Museum** — in the **former Ross Township Building**,
  W Attica Street (listed variously at 101 and 108 W Attica), open Tuesday and Saturday afternoons.
- **Depot Railroad Museum** — the 1903 C&EI depot.
- **Christman Park** — the village's park; the Rossville Community Organization runs
  *"Christmas in the Village"* there.
- Village government: a President (Mayor) and six Trustees, four-year terms, plus an elected Clerk.

Sources: [Village of Rossville](https://villageofrossville.org/our-history),
[Danville Area Visitors Bureau](https://www.visitdanvillearea.com/attractions/museums-historic-sites-markers/rossville-historical-society-museum/).

> The **high school closing in 2006** is the other event inside the game's window. In a town this
> size, losing the high school is not an administrative footnote — it is the thing that happens to
> a place.

---

## 8. What this changes for the simulation

1. **The houses should be frame, not brick.** The Sanborn colour key settles it. The current asset
   pack offers only Chicago brownstones, which is not a near miss — it is the wrong country. Small
   one- and two-storey wood-framed houses on generous lots are what the record shows.
2. **The downtown should be a short row of two-storey brick blocks at Attica × Chicago**, with
   shopfronts below and lodge rooms and offices above — not a scattering of buildings.
3. **Route 1's curve is the town's founding fact**, not a detail. It is a footpath from 1829.
4. **The railroad diagonal is correct and should stay disagreeing with the grid** — industry along
   the tracks, houses along the streets.
5. **Two fires shaped the downtown**, and one of them (2004) is inside the story's period.
6. **A water tower, grain elevators, and the 1903 depot** are the silhouette of this town.
7. The simulated population of ~1,300 is right for today; **1,500 is the historical high-water
   mark**, not a target to grow toward.

---

## 9. Confidence, and what is still missing

**Well sourced.** Founding dates and names; the Hubbard Trail becoming the first state highway in
1833; incorporation 1859; the 1893 and 2004 fires; the C&EI and the 1903 depot; the antique era;
the 2006 high-school closure; everything read directly off the Sanborn sheets (those are primary
documents).

**Contradictions found, and how they are resolved here.**

- One low-quality source gives a founding date of **1862**. Both Wikipedia and the village's own
  history say **incorporated August 1859**. This document uses 1859. A "founded" date differing
  from an "incorporated" date is common and both may be defensible, but 1862 has no good source
  behind it.
- Current **median age** is reported as **~42** by the 2020 Census and as **~34.6** by several
  aggregator sites that recycle each other. Trust 42.
- The Sanborn title blocks say "population 1,500" in both 1898 and 1913; the census says the peak
  was **1,588 in 1920**. These are not in conflict — the Sanborn figures are mapmakers' round
  estimates, not enumerations.

**Weaker.** The exact street-by-street extent of each fire — the 1893 burn is attributed to East
Attica Street by the village's own history, but the 2004 burn is described only as "opposite" it
and "a whole block". Which specific buildings were lost in 2004 has not been established here.

**Not yet retrieved.** The **1927 and 1933** Sanborn atlases are catalogued by the Library of
Congress but **were never digitised** — both items return zero image resources, so they are not
merely awkward to fetch, they are not online. Paper copies would have to be consulted. A decade-by-decade census series. The original town plat. Any architectural survey of the
housing stock. The 1879 and 1911 *History of Vermilion County* volumes, which are public domain and
would likely add a great deal.

**Deliberately not researched.** Anything about people currently living in Rossville — no names,
no addresses, no ownership, no financial records. Historical figures of the 1800s (Liggett, Ross,
Gilbert, and the businesses named on century-old insurance maps) are public historical record and
are included. That line is the project's existing **NO REAL RESIDENTS** rule and this research
holds to it.
