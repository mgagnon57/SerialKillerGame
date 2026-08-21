# Rossville research — start here

Everything learned about the real town, 2026-08-02/03. **Read this file first; it points at the
rest.** A fresh session needs nothing from any prior conversation.

---

## The three decisions already made

1. **The game opens in 1991 and runs forward** — through the February 2004 downtown fire and the
   2006 high-school closure, both of which fall inside the story's window and are things the player
   watches happen. *(This line used to read "built as it stood around 2000". That is still right for
   **fabric** — the downtown row is whole from 1898 until 2004, so 1991 and 2000 are identical in
   brick — but it is wrong for **use**: in 1991 the antique trade is alive and is the town's
   identity, and the high school has fifteen years left. Fabric from 1913, use from 1991, both
   decaying forward on the calendar in `THE-TRAJECTORY.md`.)*
2. **Each element is sourced from whichever era documents it best.**
   *Take the fabric from whoever measured it, and the use from whoever lived in it.*
   → **`SOURCING.md`** has the table: which source, which year, for every part of the town.
3. **The Sanborn maps are the primary source for fabric.** Nine sheets, public domain, in
   `sanborn/`. They are the only building-footprint source that exists — the county has none and
   OSM has 19 in the whole village.

---

## The documents

| file | what is in it |
|---|---|
| **`ROSSVILLE-HISTORY.md`** | the master narrative — founding, the Hubbard Trail, the railroad, both fires, population, the antique era. **Read this second.** |
| **`SOURCE-PROVENANCE.md`** | which county history is which, and what each one can be trusted for |
| **`SOURCING.md`** | the rule: which era each part of the town comes from, and what it rules out |
| **`COMMERCIAL-ROW.md`** | the downtown, unit by unit, all four faces, with storeys and frontages in feet — **wired to `Core/World/CommercialRow.cs`** |
| **`DOWNTOWN-1913.md`** | the 1913 commercial core in detail |
| **`DOWNTOWN-1898-vs-1913.md`** | what changed in fifteen years — and the fire labelled on the 1898 map |
| **`RESIDENTIAL-1913.md`** | house form, lot occupancy, and what it says about the massing grammars |
| **`PARCEL-STATISTICS.md`** | the assessor's 794 records analysed — density, lot size, values, absentee rate |
| **`BUILDING-CENSUS-1913.md`** | **all 503 buildings counted and measured by machine** — materials, footprint sizes, outbuildings |
| **`LANDMARKS-1906.md`** | the two brick schools (one towered) and the tile works that drained the prairie |
| **`TRAFFIC-COUNTS.md`** | **IDOT's own counts for Rossville's streets by name.** Route 1 is 5,200 AADT, Attica 1,100, a side street ~200 — and the town carries **~19 moving cars at an average instant** against the 159 the game runs |
| **`GEO-CALIBRATION.md`** | real GPS ↔ map tile: the frame, the verified anchor table, the rejected anchors and why — **wired to `Assets/Noir/Unity/GeoAnchors.cs`** |
| `agent-reports/` | three deeper research passes: transport, buildings, economy |
| `sanborn/` | nine map sheets (1898, 1906, 1913), enlarged crops, and two machine-classified renders |

The documents above are the town's **fabric** — what is built and where. Added 2026-08-03, the town
in **time, senses and people**:

| file | what is in it |
|---|---|
| **`THE-TRAJECTORY.md`** | **the game opens in 1991 and the town declines around the player** — the dated calendar of what closes, and the slower drift that shows on the houses. **Read this third.** |
| **`THE-YEAR.md`** | the crop calendar era-matched to the 1990s, the weather, daylight by date, and the canopy. **The fields are most of the map and they are a sequence, not a texture** |
| **`WHO-SEES-WHOM.md`** | the observation network — who is out, who is looking, which month hides what. Synthesis of the rest, aimed at the game's actual mechanic |
| **`WHO-LIVES-THERE.md`** | settlement origin, the denominational fingerprint, the surname pool, and the high school that closes in 2006 |
| **`INSIDE-THE-HOUSES.md`** | rooms, ceiling heights and the standard plan — the front door opens into the living room, and the back door faces the alley |
| **`TECHNOLOGY.md`** | what the town has and when it gets it — adoption curves 1991–2006, rural-corrected, for the year-gated technology layer |
| **`WHAT-IT-SOUNDS-LIKE.md`** | the railroad as metronome, harvest as the loud season, and the one cicada year in the window |
| **`POLICE-AND-INCIDENT.md`** | four officers, five arrests a year, **no homicides at all** — and the blotter as texture. No person named |
| **`photos/`** | **the town from the air in 1940**, plus 29 photographs of the standing buildings. Reference only |

---

## The findings that change the build

**Route 1 is a 1829 footpath.** Gurdon Hubbard's Chicago–Danville trail; Illinois's **first state
highway** in 1833. Modelling it straight erased the reason the town exists. *Now fixed — it runs
on its surveyed curve with asphalt on it.*

**The town is four blocks at Chicago × Attica**, platted 1857 by Gilbert and Satterthwait. The
streets are named for where those roads *went*. Everything else is a named addition — Gilbert's,
Livingood's, Henderson's. **The grid is a family tree.**

**The grid and railroad disagree because the town is 14 years older than the railroad.** Platted
1857 on two roads; the line arrived 1871 and ran along the eastern boundary. The diagonal is
history, not error.

**Houses are frame, never brick — now counted, not estimated.** All four 1913 sheets classified by
Sanborn's own colour key: **473 frame buildings to 30 brick**, and of the brick, sixteen are the
downtown shops and the remaining five are institutional. A whole residential sheet has **one** red
building on it. The median frame house footprint is **97 m² (1,047 sq ft)**, p10 54 to p90 163 —
three to one across the town. `BUILDING-CENSUS-1913.md`.

The three date-layers: 1857–1910s farmhouse/foursquare,
a **larger** 1920s–40s bungalow layer (median build year **1943**), and postwar ranches on the
edges. Every house on the map is **L or T shaped** — a rectangular footprint is the loudest tell
of a generated town.

**Downtown: height and width decay with distance from the crossing.** Two storeys and 22–26 ft at
the corner with halls and offices above; one storey and 14–18 ft further out; frame at the ends.

**83% of residential lots are built on** (517 improved / 106 vacant, assessor's own records), and
the **quarter-acre median lot is confirmed twice** by independent datasets — 1,011 m² from GIS
geometry, 1,012 m² from assessed acreage.

**The incorporation date is half-settled.** **July 1872 is the documented incorporation** — both
county histories give the vote (53–15), the date and every officer. The famous "August 1859"
appears only in modern sources and is **uncorroborated**. What the research did settle is that
1872 is not a *founding* date: Illinois passed a general incorporation act in 1872 and towns
across the county filed under it — Williams uses the identical phrase for **Danville**, county
seat since **1826**. Cite 1872; don't present 1859 as established. `ROSSVILLE-HISTORY.md` §9.

**30% of parcels are absentee-owned.** For a game about who was where: a third of the doors belong
to someone who isn't behind them.

---

## What is built, and what is blocked

**Built and verified** (see `../OVERNIGHT-2026-08-03.md`): the layer system (16 switches, `L` in
Play), Universal Pack ground textures, elevation verified in the mesh, the curved Route 1 with
asphalt, traffic driving the curve.

**Built since, Core-only and Unity-free** — `CommercialRow` lays a downtown terrace from the
Sanborn decay rule (14 tests). It is pure geometry: nothing renders it yet, and giving it a
street to sit on is Unity work.

**Cleared 2026-08-03**, once the editor came free — the back ell compiles and is in; the two
untested one-liners are verified (the `Debug.Log` served its purpose and is gone, the
`ShowBuildings` save/restore is correct); and **the porch was never broken.**

That last one is worth keeping. The porch was diagnosed at length as drawing solid, with five
things carefully ruled out by reading — all five correct. The fault was that `HouseProto` framed
the house from the wrong side, so the porch was *behind* the house in every prototype ever shot,
and the solid lump at the front was the back ell. Logging the emitted centres and comparing them
with where the camera stood settled in thirty seconds what a day of reading geometry could not.
The full note is in `FrameHouseGrammars.cs`'s `<remarks>`.

**Not done, and why**: no foreclosure or property-distress records were used. That data is about
identifiable people in trouble, the repo carries a written **NO REAL RESIDENTS** rule, and it is a
daylight decision. Everything here is geographic or aggregate.

---

## Open questions worth settling

- **How much of the 1913 downtown still stood in 2000.** The 2004 fire taking "a quarter" implies
  three quarters were there. Working assumption: build the full row, error bounded at a quarter.
- **A claim to distrust:** an "Opera House built 1908 by Alexander Bell McRae" appears in search
  results with **no corroborating source**. The opera house is real (drawn on the 1913 sheet); its
  date and builder are not known. This will resurface — do not believe it.
- Searches also return **Rossville Georgia / Indiana / Tennessee**. Anything not naming Vermilion
  County or Illinois should be distrusted.
