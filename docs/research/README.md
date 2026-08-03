# Rossville research — start here

Everything learned about the real town, 2026-08-02/03. **Read this file first; it points at the
rest.** A fresh session needs nothing from any prior conversation.

---

## The three decisions already made

1. **The town is built as it stood around 2000** — before the February 2004 downtown fire, before
   the 2006 high-school closure. Both fall inside the story's 1995–2006 window.
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
| **`SOURCING.md`** | the rule: which era each part of the town comes from, and what it rules out |
| **`COMMERCIAL-ROW.md`** | the downtown, unit by unit, all four faces, with storeys and frontages in feet |
| **`DOWNTOWN-1913.md`** | the 1913 commercial core in detail |
| **`DOWNTOWN-1898-vs-1913.md`** | what changed in fifteen years — and the fire labelled on the 1898 map |
| **`RESIDENTIAL-1913.md`** | house form, lot occupancy, and what it says about the massing grammars |
| **`PARCEL-STATISTICS.md`** | the assessor's 794 records analysed — density, lot size, values, absentee rate |
| `agent-reports/` | three deeper research passes: transport, buildings, economy |
| `sanborn/` | nine map sheets (1898, 1906, 1913) plus enlarged crops |
| `pending/` | code written but never compiled — see "blocked" below |

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

**Houses are frame, never brick**, in three date-layers: 1857–1910s farmhouse/foursquare,
a **larger** 1920s–40s bungalow layer (median build year **1943**), and postwar ranches on the
edges. Every house on the map is **L or T shaped** — a rectangular footprint is the loudest tell
of a generated town.

**Downtown: height and width decay with distance from the crossing.** Two storeys and 22–26 ft at
the corner with halls and offices above; one storey and 14–18 ft further out; frame at the ends.

**83% of residential lots are built on** (517 improved / 106 vacant, assessor's own records), and
the **quarter-acre median lot is confirmed twice** by independent datasets — 1,011 m² from GIS
geometry, 1,012 m² from assessed acreage.

**30% of parcels are absentee-owned.** For a game about who was where: a third of the doors belong
to someone who isn't behind them.

---

## What is built, and what is blocked

**Built and verified** (see `../OVERNIGHT-2026-08-03.md`): the layer system (16 switches, `L` in
Play), Universal Pack ground textures, elevation verified in the mesh, the curved Route 1 with
asphalt, traffic driving the curve.

**Blocked on Unity** — a parallel session has the editor:
- `pending/FrameHouseGrammars.with-ell.cs.txt` — adds the back ell to the house grammars.
  **Written, never compiled.** Restore and compile when the editor is free.
- The **porch draws solid** instead of open. Everything ruled out by reading is recorded in
  `Assets/Noir/Unity/Massing/FrameHouseGrammars.cs`'s `<remarks>` — do not re-derive it.
- Two untested one-liners in the tree from commit `3837124`: a `Debug.Log` in `Porch` and a
  `ShowBuildings` save/restore in `HouseProto`.

**Not done, and why**: no foreclosure or property-distress records were used. That data is about
identifiable people in trouble, the repo carries a written **NO REAL RESIDENTS** rule, and it is a
daylight decision. Everything here is geographic or aggregate.

---

## Open questions worth settling

- **The incorporation date.** Wikipedia and the village say **August 1859**; both county histories
  say **July 1872** with the vote count (53–15), the trustee election and the officers named. Two
  independent research passes hit this. Unresolved.
- **How much of the 1913 downtown still stood in 2000.** The 2004 fire taking "a quarter" implies
  three quarters were there. Working assumption: build the full row, error bounded at a quarter.
- **A claim to distrust:** an "Opera House built 1908 by Alexander Bell McRae" appears in search
  results with **no corroborating source**. The opera house is real (drawn on the 1913 sheet); its
  date and builder are not known. This will resurface — do not believe it.
- Searches also return **Rossville Georgia / Indiana / Tennessee**. Anything not naming Vermilion
  County or Illinois should be distrusted.
