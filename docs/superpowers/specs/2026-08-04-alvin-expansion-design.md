# The Alvin expansion

**Written 2026-08-04.** Alvin, Illinois — six miles south-south-east of Rossville, the other half of
the school district, and the first town added to the map since Rossville became the only one.

This is the design. It covers the whole expansion and it is **five implementation plans, not one**;
§10 says which. Everything measured here was measured in Rossville's own coordinate frame — metres
about the crossing of Chicago Street and Attica Street, `40.3793 N / −87.66897 W` — so no number in
this document needs converting before it is used.

---

## 1. The finding: Alvin is Rossville's inverse

Not a smaller Rossville. An inverted one, and the inversion is measured rather than chosen.

| | Rossville | Alvin |
|---|---|---|
| Population 1990 → 2000 | 1,334 → 1,217 | 339 → 316 |
| Village area | 1.34 sq mi | **0.79 sq mi** |
| Platted core | ~1,000 m across | **548 × 469 m** |
| Median in-town lot | 1,011 m² (¼ acre) | **1,398 m² (0.35 acre)** |
| Residential lots built on | 83% | **63%** |
| Median age | ~42 † | **30** (2000) |
| Under 18 | — | **34.5%** (2000) |
| Over 65 | 20.1% | **8.5%** (2000) |
| Ground | 200.8–225.3 m | **190.9–207.4 m** |
| Grid vs. railroad | fight — town is 14 years older | **agree — town born at the crossing** |
| Footprint survey | 9 Sanborn sheets | **none found** |

† Rossville's median age is quoted from `THE-TRAJECTORY.md` as a trend figure — *"the median age
reaches ~42"* — not as a dated census value. Alvin's 30 is the 2000 census. The gap is real and
large either way, but the two numbers are not the same kind of measurement and should not be put in
a chart as though they were.

**Rossville is old, full, and on the high ground. Alvin is young, gappy, and downhill.**

For a game whose mechanic is who notices you, that is not flavour. Rossville's observation network
is a fifth of the town over 65, indoors, at windows, on a weekday. Alvin's is a third of the town
under 18, outdoors, mobile, and — crucially — *not on a schedule you can learn*. Same county, same
decade, same road, opposite stealth problem.

> **The corollary that should govern the build:** every instinct that produced a good result at
> Rossville is calibrated on the wrong town. Alvin's lots are 38% larger, more than a third of them
> are empty, and its people are twelve years younger. A generator tuned on Rossville and pointed at
> Alvin will produce Rossville.

---

## 2. Three standing project facts are wrong

These are corrections to things the project currently believes. They belong in
`docs/SOURCES-OF-TRUTH.md` §3 as standing facts, not left in a spec.

**1. Alvin is 6.0 miles south-south-east, not five miles south.**
It lands at `x = 5140, y = −7883` (OSM place node) — 9,609 m from the crossing on a bearing of 147°.
The village centroid by lat/lon is `x = 5260, y = −8042`. Either anchor is fine; they differ by
150 m and the streets, below, are better than both.

**2. Alvin is not on Route 1.**
Route 1 / `2300 East` runs at `x ≈ 655…930` for the whole southern half of the frame. Alvin is
**4.4 km east of it**. The Rossville–Alvin link is a **rail** corridor, not a highway corridor, and
the project memory's "corridor along Route 1, about 11 miles" is the wrong shape for the wrong
reason. Alvin's own **Chicago Street** is a village street at `x ≈ 5332`, named for where the road
went, exactly as Rossville's was — but it is not the same road and it is not a highway.

**3. Alvin's grid agrees with its railroad.**
Measured over the fifteen named village streets: north–south segments deviate **−0.37°** from true
north (38 segments, 3,270 m), east–west segments **−1.69°** from true east (40 segments, 4,307 m).
The CSX line through the village runs at **178.7°** — due north–south. They are square to each other
and to the section lines.

Rossville's celebrated diagonal exists because the town was platted in 1857 and the railroad arrived
in 1871. Alvin was **founded at a railroad crossing in 1875** and platted square to it. The two
towns are the same argument answered both ways, and both answers are now measured.

---

## 3. What Alvin is, historically

### The line of descent

Alvin is Rossville's child, and the paperwork says so.

| year | |
|---|---|
| 1838 | **Alvan Gilbert** buys the Liggett farm north of the future Rossville |
| 1839 | Gilbert becomes postmaster of the "North Fork" post office |
| ~1857 | **Alvan Gilbert and Joseph Satterthwait plat Rossville** — four blocks at Chicago × Attica |
| 1872 | a settlement is founded on the C&EI about a mile south of present Alvin, and named **Gilbert** |
| 1873 | the Havana, Rantoul & Eastern is created (January) and chartered (1 April) |
| 1875 | the HR&E crosses the C&EI north of Gilbert. **Alvan is founded at the intersection** and supplants the older town |
| 1878 | Alvin–West Lebanon, Indiana (12 miles) enters service, 1 December |
| 1880 | the HR&E is merged into the Wabash, St. Louis & Pacific, 1 May |
| 1887 | the Illinois Central organises the assets as the Rantoul Railroad, 3 June; re-gauged to standard |
| 1927 | **South Ross Township** is created — Alvin gets its own township, out of Ross |
| 1930s | the IC's Rantoul & Eastern branch is **abandoned** |
| **1942** | **the tornado.** See §5 |
| 2006 | Rossville-Alvin High School closes (board voted 2005) |

Rossville's research concluded that *"the grid is a family tree"* — Gilbert's First, Second, Third
and Fourth Additions, Gilbert & Satterthwaite's Addition, Gilbert Street. **The family tree runs six
miles south.** Alvin is named for Alvan Gilbert's given name; its predecessor settlement was named
for his surname; and **Alvin has a Gilbert Street of its own**, running east–west at `y ≈ −7486`.

The spelling is an accident: the post office wrote *Alvin* for *Alvan* and refused to correct it.
The Census Bureau still files the village as Alvan. Both spellings are correct depending on who is
asking, which is a detail worth keeping rather than tidying.

### What the town had

A post office, a grain elevator, two grocery stores, a bank, a town hall, and **two depots** — one
C&EI, one Illinois Central. All six of the named ones are attested by the same source, and that
source is a disaster report. See §5.

---

## 4. The railroads: one live, one live nearby, one erased

| line | state in 1991 | where |
|---|---|---|
| **CSX Woodland Subdivision** (ex-C&EI) | **live main** | through the village, `x ≈ 5223…5237`, due N–S |
| **KBS "Danville Line"** (ex-Milwaukee Road / CTH&SE) | **live branch** | `x ≈ 6900…6963`, due N–S, 1.6 km east |
| **Illinois Central / HR&E** | **gone** | the east–west line that created the town |

The CSX line is the **same line that runs down the east side of Rossville** — 60 mph, CTC, PTC,
track class 4, `ref WQ`. It carries two level crossings at Alvin, at `(5222, −7481)` and
`(5233, −7923)`, 442 m apart, and three sidings of 370 m, 174 m and 139 m. A grain elevator wants
one of those sidings.

**The Illinois Central line is not in OpenStreetMap at all** — not as `rail`, not as `abandoned`,
not as `razed`. The right-of-way of the railroad that caused Alvin to exist has been farmed over so
completely that the world's most thorough volunteer map does not record it.

Its ghost is on the street signs. **West Railroad Avenue** (`y ≈ −7944`) and **East Railroad Avenue**
(`y ≈ −7876`) run east–west through the village, split at Chicago Street in exactly the convention
Rossville uses — and they run **perpendicular to the only railroad still there**. Fourteen parcels
carry a Railroad Avenue address. The town still numbers its houses off a railroad that has not
existed since before the war.

That is the single most evocative true thing about Alvin and it should survive into the build.

### Rossville Junction and the abandoned network

Between the two towns, at `(2037, −2254)`, OSM records a locality called **Rossville Junction** — a
genuine three-way rail junction where two abandoned lines meet the live one:

- **21.89 km running due east** (axis 91°) from `(2051, −2242)`, tagged `old_railway_operator=C&EI`
- **16.96 km on a south-west diagonal** (axis 44°) from `(−9186, −14922)` up into the junction

A bridge on the diagonal carries the TIGER attribute `name_base = Louisville and Nashville RR` —
the C&EI's successor from 1969. It also carries `tiger:reviewed = no`, and **TIGER is not a survey**;
treat the name as a lead, not a fact.

---

## 5. The 1942 tornado is the generator rule

**Monday 16 March 1942, 11:40 AM.** An F4 on a 60-mile path from west of Ivesdale through Piatt,
Champaign and Vermilion counties, twelve dead in all, moving **west-south-west to east-north-east**
and passing out of the county north-east of Alvin.

At Alvin:

- **at least 20 dwellings utterly destroyed**
- the **town hall's roof** taken off
- **two grocery stores**, a **bank**, and **both railway depots** damaged

Against a village whose 1940 population was 339 — call it a hundred houses — that is **a fifth of
the town's housing stock destroyed in one morning.**

The census corroborates it without being asked. Alvin lost **52 people between 1940 and 1950**
(339 → 287, −15.3%), the steepest decade in its record until the 2010s, and the only fall of that
size not explained by the general regional drift.

### The rule this becomes

Not decoration — a placement rule for the house generator:

1. A **contiguous swath** across the village on a **WSW→ENE** bearing. Tornadoes cut a path; they do
   not scatter. A random 20% of houses dated 1942 is the wrong answer and would read as noise.
2. Inside the swath, build years cluster **1942–1950**; outside it, the ordinary older mix.
3. Size it to **~20 dwellings**, and to the town hall, the two groceries and the bank.
4. **Check it, don't invent it.** The 1940 USDA aerial the project already holds for Rossville's
   alleys covers Alvin **two years before it was hit**. What stood in the swath is photographable.

This is the reason to build Alvin properly rather than fake it, and it is the one thing Alvin has
that Rossville does not: a dated, located, single-morning event that rewrote a fifth of the fabric
and left a photograph of the before.

### The six dead

The memorial marker in the village names them. **They are recorded once, in the research document,
as the monument records them — and nowhere else.**

The project's NO REAL RESIDENTS rule admits "historical figures of the 1800s … and the businesses
named on century-old insurance maps" as public historical record. A public memorial is the same
category of source. But these are twentieth-century deaths with living relatives, and the extension
stops at the page: **no name from that marker may be used as a character, placed on a lot, spoken by
anyone, or added to the surname pool the name generator draws from.** A town that remembers its
tornado is right; a game that casts its dead is not.

---

## 6. Alvin gets its own sourcing doctrine

Rossville's is *fabric from 1913, use from 1991*, and it rests entirely on nine Sanborn sheets that
measured every building in the village. **No Sanborn sheet for Alvin has been found.** The Library
of Congress holds Vermilion County sheets for Danville, Hoopeston, Ridge Farm, Rankin and Westville;
Alvin did not surface. Its 1900 population was 368, which is below what Sanborn usually bothered
with.

Stated honestly: **not found is not the same as does not exist.** The LOC JSON API returned 403 to
this research and the search was indirect. A manual check of the LOC Sanborn collection is an
open task, and it is cheap.

Meanwhile, the doctrine:

> **Fabric from the 1940 aerial, corrected by the 1942 tornado, lot lines from the county.
> Use from 1991.**

| element | source | era |
|---|---|---|
| Street grid, names | OpenStreetMap | modern |
| Lot boundaries | Vermilion County parcels | modern |
| Building footprints | **1940 USDA aerial** | **1940** |
| Post-tornado house era | **the 1942 damage report** | **1942** |
| Rail alignment, crossings, sidings | OpenStreetMap | modern |
| The vanished IC right-of-way | **the two Railroad Avenues** | inferred |
| Elevation | USGS NED 10 m | modern |
| House density, improved/vacant | county assessor use codes | modern |
| Population, age structure | census | 1990 / 2000 |
| Business use | not established — see §9 | — |

**And the tone must change with it.** Rossville's research earned its confidence: a professional
surveyor measured every building and a second dataset confirmed the lot median to within one square
metre. Alvin's has not. The documents should not inherit Rossville's certainty by sounding like
them.

---

## 7. What the county records, and one rule that goes in as law

509 parcels in the Alvin box from the same service Rossville's 794 came from
(`gis.cityofdanville.org/arcgis/rest/services/Property/Property`); 493 carry attributes. **The use
codes decode identically to Rossville's**, which is the cross-check that makes the comparison
legitimate.

| code | n | improved | median size | reading |
|---|---|---|---|---|
| `0021` | 178 | 0% | 32.00 ac | the fields |
| `0040` | 156 | 100% | 0.90 ac | improved residential |
| `0030` | 69 | 0% | 0.23 ac | **vacant residential** |
| `0011` | 57 | 78.9% | 13.41 ac | **country homesteads — a class Rossville has none of** |
| `0090` | 20 | 0% | 0.32 ac | vacant, larger |
| `0060` | 11 | 36.4% | 0.21 ac | the commercial core |
| `0020` | 2 | 0% | 5.00 ac | — |

**Within the village core** (650 m of centre, 204 parcels): 103 improved against 61 vacant
residential — **63% built, 37% empty**, against Rossville's 83%/17%. Across the wider box, which
catches country houses outside the village limits, it is 69%.

The core figure is the one to build to, and it cross-checks: **103 improved residential lots against
the 2000 census's 115 housing units.**

- **In-town lot area:** median **1,398 m²** (15,051 sq ft, 0.35 acre), p10 519, p90 10,235
- **Building assessment:** median **$21,876**, p10 **$1,880**, p90 $43,694 — a p10 well under
  Rossville's $4,361
- **Addressed lots by street:** Chicago 24, Railroad 14, Locust 12, Wood 12, South 10, Foulk 9,
  Oak 7, Gilbert 6, Center 3, Walnut 3

### The rule that goes in as law

**The parcel service now returns owner names.** On the Alvin pull: `FirstName` 493/493, `FullName`
493/493, `TXNME1` 493/493, `MailingAddress` 491/493, `LastName` 445/493 — all populated with real
people. And `AbsenteeOwner`, the two-valued boolean Rossville's analysis relied on and cited as
proof that NO REAL RESIDENTS held *at the point of data collection*, is now **empty on every
record.** The schema changed between the two pulls.

> **Ruling: any pull from the county property service must whitelist `outFields` explicitly. The
> name and mailing fields are never requested, never written to disk, and never cached.** This is
> enforced in the fetch tool, not in a later filter, because a later filter means the names were on
> disk in between.

One consequence: **there is no absentee figure for Alvin comparable to Rossville's 30.1%.** A proxy
computed from mailing city gives 15%, but it is a different measurement from a field the county no
longer publishes, and it is derived from exactly the data the rule above forbids keeping. Record the
proxy as a proxy, once, or drop it. Do not put it in a table next to Rossville's 30.1% as though the
two numbers are the same kind of thing.

---

## 8. The frame, and the price of "all of it live"

### 8.1 Widen the rectangle; do not move the data

Alvin sits at negative *y* in the existing frame. The obvious move — shift everything by ~+7,200 m
so the map stays 0-based — would rewrite every coordinate in `city.txt`, `features.txt`,
`parcels.txt` and `elevation.txt`, invalidate the known-bad sets in
`RoadsSitOnPublicLandTests.cs`, move the killer's anchor at 408 Holmes Ave, and break every
committed snapshot. It is the most dangerous change available and it buys nothing.

**Instead, let the map rectangle have a corner that is not the origin.**

```
    size 2100 2400                 ->    bounds -750 -8900 6200 1065
    crossing implied at (750,1335)       crossing stays exactly where it is
```

Rossville keeps every number it already has. Alvin is authored at its true offset. There is
precedent in the repo: `Assets/Noir/Unity/CityRailBed.cs:69` records that the chunk grid had to be
declared over the **geometry** rather than over the map, after two kilometres of track running off
the north edge got clamped into the edge chunks and could never be culled. The same lesson,
generalised.

The only file that genuinely migrates is `elevation.txt`, which needs a fresh pull to cover the new
area regardless.

Those edges are chosen to land on roads rather than mid-field: the west and north edges are
Rossville's existing ones, the south edge clears `Road 3000 N` at `y = −8797`, and the east edge
clears `North 1850 East Road` at `x = 6147`. **6,950 × 9,965 m.**

**Float precision.** The far corner — `(6200, −8900)` — sits **10,848 m** from the origin, where
float32 resolves to about a millimetre. No origin shift is needed. But this expansion spends the
entire budget: Hoopeston is six miles the other way and **will not fit**. Write that down now rather
than discovering it.

### 8.2 The budget

The decision is a **full rectangle, all of it live** — **69.3 km²** against today's 5.04, a factor of
**13.7**.

Today, on the small map: **81,940 renderers**, 5,723 baked meshes, 10.6 s to bake, ~120 s startup.
On the current rules, 13.7× area is about **1.12 million renderers**. That does not work, so the
frame is the long pole and nothing visible ships until it lands.

> **Phase B's pass/fail: a 69.3 km² map starts no slower than today's 5.04 km² map.**

Three mechanisms, in expected order of yield:

1. **Scatter density falls off with distance from settlement.** This is blocker #1 in the project's
   own notes and the growth is *entirely* countryside. It is also true: Ross Township holds seven
   people per square mile, and a field four kilometres from the nearest door does not need a
   village verge's prop count.
2. **People: simulate all, rig near.** 1,673 townspeople in 1990 against the ~1,000 that currently
   works. The simulation is cheap; the rigged Animator and cloned mesh per citizen are not.
3. **Chunk streaming by camera distance**, instead of baking the world at startup.

**No silent caps.** If any of the three drops content rather than deferring it, it says so in the
build log. A world that quietly stops scattering at 3 km reads as "covered everything" when it did
not — and per the project's own lessons, an invisible change is the expensive kind.

---

## 9. The corridor is not empty

Six miles of it, and OSM already names four things on it:

| | position | |
|---|---|---|
| **Rossville Junction** | `(2037, −2254)` | three-way rail junction, §4 |
| **Mann's Chapel** | `(841, −5257)` | place of worship |
| **Barlow Park** | `(3892, −7759)` | hamlet, 1.25 km west of Alvin |
| **Rayville** | `(900, −8068)` | hamlet, at Alvin's latitude, 4.2 km west |

Plus the 57 `0011` parcels — 13-acre country homesteads, 79% improved — which are a settlement
pattern Rossville's map has no example of.

**The ground falls.** 41 samples along the straight line: 209 m at the Rossville crossing down to
~201 m at Alvin, with a dip to **195 m** where the line crosses the North Fork valley at about 85%
of the way. Alvin's own box runs 190.9–207.4 m against Rossville's map at 200.8–225.3. Alvin is
downhill, and on a till plain with 24 m of relief in total, an eight-metre fall over six miles is
something you would feel in a car and never be able to name.

### The one live flow

The district is **Rossville-Alvin CUSD 7** and its only school building is **Rossville-Alvin
Elementary, in Rossville** — PK-8. Alvin has no school.

**So every child in Alvin travels six miles up the corridor and six miles back, twice a day, on a
timetable.** With 34.5% of the village under 18, that bus carries most of Alvin's children, and it
is the only scheduled, observable, repeated movement between the two towns. It is also the thing
that makes the corridor a place rather than a distance.

---

## 10. The phases

Each gets its own implementation plan. This spec is the shared input to all five.

| | phase | depends on | ships |
|---|---|---|---|
| **A** | **Research documents.** `ALVIN-HISTORY.md`, `ALVIN-SOURCING.md`, `THE-CORRIDOR.md`; the three corrections into `SOURCES-OF-TRUTH.md` §3; the field-whitelist ruling. | — | prose only |
| **B** | **The frame.** `bounds`, the renderer budget, scatter falloff, people LOD, chunk streaming. | — | a 69.3 km² map that starts as fast as today's |
| **C** | **The data pull.** Parcels (whitelisted), roads, rail, features, elevation for Alvin and the corridor. The 1940 aerial for Alvin. | A | `Content/` files |
| **D** | **Alvin built.** Grid, lots, the tornado swath, the two Railroad Avenues, the elevator on its siding, the two crossings. | B, C | the town |
| **E** | **The corridor built.** Rossville Junction, the hamlets, Mann's Chapel, the homesteads, the school bus. | D | the six miles |

**A and B are independent and can run in parallel.** A is cheap and mostly written already — most of
it is in this document. B is the long pole.

---

## 11. What this rules out

- ❌ **Do not build Alvin from Rossville's grammars at 25% scale.** §1 is the whole reason Alvin is
  worth building; a scaled Rossville is a scaled Rossville.
- ❌ **Do not put Alvin on Route 1.** It is 4.4 km east of it. Alvin's Chicago Street is a village
  street.
- ❌ **Do not tilt Alvin's grid to match Rossville's diagonal.** Measured at −0.37° and −1.69°.
  Alvin is square, and the reason it is square is the interesting part.
- ❌ **Do not draw the Illinois Central line.** It is gone and its absence is the point. Its trace,
  if wanted, is inferred from the two Railroad Avenues and marked as inferred.
- ❌ **Do not scatter the tornado rebuild randomly.** A swath, on a bearing, or not at all.
- ❌ **Do not use Rossville's 83% improved rate at Alvin.** 63% in the core.
- ❌ **Do not request owner names from the parcel service.** §7.
- ❌ **Do not cast the 1942 dead.** §5.

---

## 12. Claims to distrust

The Rossville research kept a list like this and it earned its place. Four for Alvin:

1. **"The Alvin–Rossville branch of the Illinois Central."** This surfaced in a search-engine
   summary that had been fed those two town names in the query, and the dedicated HR&E history does
   not mention it. It is the same shape as Rossville's phantom "Opera House built 1908 by Alexander
   Bell McRae". **It will resurface. Do not believe it without a source that was not asked leading
   questions.**
2. **"Alvin is located on County Road 3."** Wikipedia says so; OSM shows the village spine as
   Chicago Street / 1800 East Road, with `Road 3000 N` running east–west a kilometre south.
   Reconcile before using either.
3. **`tiger:name_base = Louisville and Nashville RR`** on the abandoned south-west diagonal.
   TIGER, and tagged `tiger:reviewed = no`. Per the project's own standing note, TIGER is not a
   survey.
4. **"No Sanborn sheet exists for Alvin."** Not found is not the same as does not exist. §6.

One caution *not* inherited: Rossville's research had to fight off Rossville, Georgia / Indiana /
Tennessee on every search. **Alvin did not show that problem** — the searches behind this document
returned the Illinois village cleanly. Alvin, Texas exists and is much larger, so the trap is
plausible and worth watching for, but it has not actually bitten yet and is recorded here as a
possibility rather than an observed hazard.

---

## 13. Open questions

- **Business use in 1991.** Rossville's antique trade is documented and dated. What Alvin's grain
  elevator, tavern, or store were doing in 1991 is **not established here at all** — the same gap
  Rossville has at village level, and worse. The Danville Public Library holds the local microfilm;
  a library card gets what a script cannot.
- **What the 1940 aerial actually shows at Alvin.** Assumed to cover it, on the strength of covering
  Rossville. Verify before Phase C depends on it.
- **Whether a Sanborn sheet exists.** §6.
- **Where the two depots stood.** Both are attested by the 1942 damage report; neither is located.
  The C&EI depot is constrained by the sidings; the IC depot is constrained only by a road named
  after a railroad that is gone.
- **Alvin's peak was 1980 at 378 and it fell 10.3% in the eighties**, so unlike Rossville — which
  opens in 1991 *ahead of* its worst decade — Alvin opens having already had it. Whether that
  difference is worth expressing in the build, or is simply true and invisible, is undecided.
