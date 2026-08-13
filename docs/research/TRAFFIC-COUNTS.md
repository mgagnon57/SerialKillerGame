# Traffic counts — how much of Rossville actually moves

**Measured 2026-08-08 from IDOT's own count program.** The owner's standing observation was the
starting point and it turned out to be right in every part:

> *Most cars are parked in their driveway. Traffic in Rossville is not by houses, but the amount of
> cars is. They do not ALL be driving. Route 1 and Attica are the 2 busiest. The side roads are
> mostly used to get from a house to Route 1 or Attica to leave town to go to work outside the map.*

This document is the evidence, the numbers it implies, and what in the code disagrees with them.

---

## The source

Illinois DOT publishes Annual Average Daily Traffic for its whole count program, including named
village streets — not just state routes. It is a public ArcGIS service and needs no key:

```
https://gis1.dot.illinois.gov/arcgis/rest/services/AdministrativeData/AADT_Historical/MapServer
```

Query any layer with an envelope around the village (`inSR=4326`, roughly
`-87.70,40.363 → -87.635,40.395`) and read `ROAD_NAME`, `AADT`, `AADT_YR`, `HCV_AADT`.
**The layer id is the publication year, not the count year** — read `AADT_YR` off the data. Sampled
layers gave count years **2008, 2019 and 2023**. Nothing in the service reaches 1991.

AADT is a **two-way daily total**. `HCV_AADT` is the heavy-commercial share.

---

## What Rossville carries — 2019 counts

| road | AADT | heavy | in the map as |
|---|---:|---:|---|
| **Chicago St — IL Route 1** | **5,200** | 400 | `chicago`, `carries mainroad` |
| Chicago St / Korean Memorial Hwy (second leg) | 5,100 | 450 | same road |
| **Attica St** | **1,100** | 90 | `attica`, `carries mainroad` |
| Attica St (further legs) | 950 / 775 / 500 | 48–60 | same road |
| Stewart Ave | 375 | 0 | `stewart`, street |
| Stufflebeam Dr | 375 | 0 | `stufflebeam`, street |
| Benton St | 300 / 100 | 0 | `benton`, street |
| Henderson St | 250 / 75 | 0 | `henderson`, street |
| Creative Ave | 225 | 0 | `creative`, street |
| Summit St | 225 / 100 | 0 | `summit`, street |
| McKibben Ave | 200 | 0 | `mckibben`, street |
| **Church St** | **200** | 0 | `church`, street |
| Dale Ave | 125 | 0 | `dale`, street |

**Twenty-one of the map's named roads are not counted at all** — 3550north, abner, earl, gilbert,
goodwine, green, greenwood, grove, harrison, holmes, **maple**, park, perry, railroad, smith,
thompson, york. IDOT does not count them because there is nothing there to count.

### The ratios, and they are durable

| count year | IL-1 (Chicago) | Attica | side streets (median) |
|---|---:|---:|---:|
| 2008 | 5,750 | 1,150–1,700 | 250 |
| 2019 | 5,200 | 1,100 | ~212 |
| 2023 | 4,550 | 1,250 | 250 |

- **Route 1 : side street ≈ 21 : 1** (18:1 to 23:1 across fifteen years)
- **Route 1 : Attica ≈ 4.7 : 1**
- **Attica : side street ≈ 4.4 : 1**

**Route 1 is falling ~4% a year and the side streets are flat.** Rossville has been shrinking gently
for a century, so **1991 was very likely BUSIER on Route 1 than any count here** — extrapolating the
2008–2023 slope backwards puts it near **6,000–6,500**. That makes the arterial-to-local ratio in
1991 *wider* than 21:1, not narrower. The era gap is real, but it runs in the direction that
strengthens the finding, which is why these counts are usable despite none of them being from 1991.

---

## Where the traffic is, against where the game puts it

Share of **vehicle-miles** — AADT × length, which is what "where the traffic is" actually means.
Uncounted roads are given the median counted side street (250) as a generous upper bound, and the
lowest counted leg (75) as the honest one:

| class | REAL (IDOT) | GAME (`AmbientTrafficWeight`) | game overshoot |
|---|---:|---:|---:|
| mainroad — Route 1 + Attica | 80.2% – 86.3% | 55.5% | 0.64–0.69× |
| **side streets** | **13.7% – 19.8%** | **44.5%** | **2.25–3.26×** |

**The game puts two to three times too much traffic on the side streets.** `AmbientTrafficWeight`
is `Mainroad 8 : Street 2` — a 4:1 ratio against a measured **21:1**. And it gives Chicago and
Attica the *same* weight, when Route 1 carries **4.7 times** what Attica does.

---

## How many cars should be moving at all

Total vehicle-miles per day ÷ average speed = vehicle-hours per day ÷ 24 = **cars on the network at
an average instant**. Peak hour assumed at 10% of AADT, the usual K-factor for a rural arterial.

| assumption | average instant | peak hour |
|---|---:|---:|
| uncounted at 250 AADT, 25 mph | **19.3 cars** | 46.3 cars |
| uncounted at 250 AADT, 30 mph | 16.1 cars | 38.6 cars |
| uncounted at 75 AADT, 25 mph | 17.8 cars | 42.8 cars |
| uncounted at 75 AADT, 30 mph | **14.8 cars** | 35.6 cars |

### **The game runs 159 cars, all day, every day.**

`CityTraffic.CarsOutPerHousehold = 0.25` against 624 households. The measurement says:

| | multiplier | cars |
|---|---:|---:|
| game today | 0.25 | 159 |
| **measured, average instant** | **~0.03** | **~19** |
| **measured, peak hour** | **~0.07** | **~46** |

**Eight times too many at the average, three and a half times too many at the peak — and flat, when
the real thing is a curve.** The docstring on that constant is not wrong about what the number
*means* — it says explicitly "not how many cars a household OWNS, but how many are ON THE ROAD AT
ONCE" — it is wrong about the value, which was reasoned ("roughly one household in four") rather
than measured. It is one household in thirty-three.

---

## What this explains that was already failing

- **`church x maple` starves cars for 53.7 s** (`NoCarWaitsForeverAtTheHeadOfAClearQueue`, see
  `docs/IDEAS.md`). Church St is **200 AADT** — about one vehicle every seven minutes. Maple is not
  counted at all. Two sleepy residential streets, and the game has cars queuing at them, because it
  is running 159 vehicles on a network that carries 19.
- **`NoTwoVehiclesOccupyTheSameSpace` near-misses.** Density the town never has.
- **The p90-wait gate is a coin toss.** A congested network is where small changes swing hard.

The traffic faults being chased at the junction level are downstream of the fleet being an order of
magnitude too big.

---

## The half of the town that is missing

The owner's first clause — *most cars are parked in their driveway* — has no representation at all.
`CityParking` places cars only in **authored `carpark` places**; its own docstring says *"Every
vehicle on the map was in motion"* and *"A PARKED CAR IS CONTENT IN THIS GAME... a city where every
car is driving past is a city where nothing is ever anywhere."* That argument was made for public
lots and never carried to the houses.

If cars-owned scales with households and cars-moving does not, the difference has to be **standing
somewhere**: roughly 600 cars owned, ~19 moving, so **the overwhelming majority of Rossville's
vehicles are parked at a house at any moment.** For a game about noticing what changed, a car that
sat in a driveway on Tuesday and is gone on Wednesday is worth more than a car driving past.

---

## What to do with it — see `docs/IDEAS.md`

1. Carry the counts into `Content/roads.txt` as a measured `aadt` line, the same way right of way
   and easement already are. The survey file's whole premise is *from survey rather than from a
   ruler*, and IDOT counts 12 of these roads by name.
2. Make the fleet a **time-of-day curve** off the sim clock rather than a constant. The town already
   knows the men leave for Hoopeston at six.
3. Park the rest at the houses.

---

## Sources

- [IDOT AADT — ArcGIS REST service](https://gis1.dot.illinois.gov/arcgis/rest/services/AdministrativeData/AADT_Historical/MapServer)
- [IDOT — Annual Average Daily Traffic, open data](https://gis-idot.opendata.arcgis.com/datasets/annual-average-daily-traffic-aadt)
- [IDOT — Illinois travel statistics](https://idot.illinois.gov/transportation-system/network-overview/highway-system/reports/illinois-travel-statistics.html)
- [Getting Around Illinois — AADT viewer](https://www.gettingaroundillinois.com/gai.htm?mt=aadt)
- Commuting context already established in `agent-reports/rossville-economy.md` §2.5: Rossville is a
  bedroom/agricultural village on the Route 1 corridor between Danville (14 mi south) and Hoopeston
  (8 mi north), 75% driving alone, mean travel time to work 23.6 min
  ([Census Reporter](http://censusreporter.org/profiles/16000US1765962-rossville-il/)).

**Note on the Census API:** `api.census.gov` now rejects keyless requests ("Missing Key") and
censusreporter's API returns 403 to scripted fetches. Neither was needed for the finding above.
