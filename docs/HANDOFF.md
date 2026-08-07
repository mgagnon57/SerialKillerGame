# Where Northgate is, 2026-08-01

Read this first in a fresh session. It exists so nobody has to re-derive any of it.

## The town is Rossville, Illinois — and it is real data, not a pastiche

Everything hangs off ONE origin: **Chicago Street crosses Attica Street**, which is where the
village numbers its addresses from (East Attica ends and West Attica begins on exactly that
longitude — that is how the origin was found rather than assumed).

    real world   40.379300 N, -87.668970 W
    game map     x 750, y 1335   on a 2100 x 2400 map
    scale        1 unit = 1 metre, true scale, nothing shrunk

| what | where it came from | file |
|---|---|---|
| Street grid, names, positions | OpenStreetMap (Overpass) | `tools/rossville-streets.json` |
| 794 lot boundaries | Vermilion County parcel service | `tools/rossville-parcels.geojson` → `Content/parcels.txt` |
| Railway, river, ponds, schools | OpenStreetMap | `Content/features.txt` |
| Everything assembled | generator | `tools/relay-rossville.py` → `Content/city.txt` |

The parcel service, if it needs re-querying:
`https://gis.cityofdanville.org/arcgis/rest/services/Property/Property/FeatureServer/0/query`

Facts worth not re-learning:
- The town is **mostly east of Route 1** — 780m east, 224m west.
- Block spacing is **irregular** (104m, 109m, 186m, 181m), averaging ~113m.
- Median lot is **1,011 m²** — 26.3m frontage by 51.1m deep. A quarter acre.
- Several streets **change name across Route 1**: McKibben/McKibbin, Maple/Park Place,
  Gilbert/Perry, Stewart/Stufflebeam.
- Ross Township holds 1,617 people against the village's 1,331 — so the country is about
  **seven people per square mile**. It is supposed to look empty.
- The ground is glacial **till plain**, 679–706 ft. Flat. Any elevation work should be a few
  metres of roll and drainage swales, never hills.

## What it looks like now: a survey plan

`VillageHost.ShowBuildings` is **false** by default. That draws the town as a dark plan:

- cyan — road corridors (the right of way, not the asphalt)
- chalk white — the 794 county lot boundaries
- amber — shops, pubs, diner, bank, casino, cinema
- violet — school, hospital, precinct, firehouse, water tower, elevator
- pink — the two school campuses
- blue — the river, the ponds behind the school
- cream — the CSX Woodland Subdivision

Set `ShowBuildings = true` to raise the built town again for comparison.

**WHY the plan exists, and it is not a stopgap to be embarrassed about:** the Polyperfect pack
contains exactly two house families, Bayhouse and Squarehouse, and both are **Chicago
brownstones** — bay fronts, stoops, fire escapes. There is no clapboard, no porch and no gable
roof anywhere in it. A Rossville street built from that kit is not a near miss. The plan draws
only what we can back up.

**408 Holmes Ave exists** as a real addressed place with its own front door — it is the
user's childhood home and the killer's address in the story. Holmes runs east of Chicago only,
which is why it carries no E prefix.

## The one loose end

Hiding the cars and people for the plan (`VillageHost.HideActors`) is **committed but not
verified by the test suite**. Reasoning says it is safe — it only sets `Renderer.enabled =
false`, and the traffic tests read transforms, not renderers — but it has not been run. Confirm
with:

    Unity.exe -batchmode -projectPath C:\SerialKillerGame -runTests -testPlatform PlayMode \
      -assemblyNames Noir.PlayTests -testResults <xml> -logFile <log>

Expect **13/13**. Do NOT pass `-nographics` — two tests render and fail spuriously without it.

**`-assemblyNames Noir.PlayTests` is not optional any more.** Without it the runner also discovers
`LLMUnityTests.TestLLM`, whose constructor calls `LLMManager.DownloadModel`, and the run sits
trying to fetch a language model off the network instead of testing the town — no results file,
no error, just a process that never finishes.

## Two switches, and one first-run step

**`Noir > Show The Built Town`** raises the buildings, streets, greenery, farm, powerlines and
the CSX rail bed. Without it, pressing Play gives the survey plan below — which is the right
default and has a real reason, but it means everything that dresses the town is invisible and
there was no way to turn it on short of editing `VillageHost.ShowBuildings`. The tick is
remembered between sessions and read once at bootstrap, so it takes effect on the NEXT Play.
It is ignored in batch mode on purpose: the PlayMode suite bootstraps the same host, and a
local tick must not decide what the tests build.

**`Noir > Show Ground Colour`** defeats the plan's dimming without raising the town, for
looking at zoning and slope on their own.

**Run `Noir > Make City Meshes Readable` once on a fresh clone.** `Assets/polyperfect/` is
gitignored, so the pack's import settings are NOT in the repo, and Read/Write defaults to off —
which means `CityChunker` silently cannot combine those models. Measured on 2026-08-02 with 53
models needing it: the bake was **18,059 renderers, and 7,635 after**, with 11,002 warnings in
the log before and none after. The chunker does tell you, once per prefab, but it is one line
among thousands.

## THE TOWN IS SET IN ~2000, BEFORE THE FIRE

Decided 2026-08-03, and it settles a question that was open for as long as the project has
existed. The research in `docs/research/ROSSVILLE-HISTORY.md` found that Rossville **changed
inside the story's own window**:

- **27/28 February 2004** — a fire destroyed about a quarter of the downtown commercial row.
  A Casey's petrol station stands on that corner today.
- **2006** — Rossville-Alvin High School closed.

**We build the town as it stood in 1991.** The year is decided in `docs/research/THE-ERA.md` and
followed everywhere else; this line said "around 2000" until 2026-08-07. Everything below still
holds — 1991 is on the same side of the 2004 fire as 2000 was — but the two are nine years apart
and the difference shows in the trades, the technology and the canopy. That means:

- The **downtown brick row is whole** — the buildings the 1893 fire produced are all still
  standing. No gap, no Casey's.
- The **antique shops are trading** — this is the tail of the "Antique Capital of Illinois" era,
  already being eaten by eBay but not yet burnt out.
- **The high school is open.**
- Population is at its **census low of 1,217** (2000) — the town is thin but whole.

Anything built from here should be checked against that date. A detail that is true of Rossville
in 2026 is not automatically true of Rossville in 2000, and the fire is the dividing line.

**But no single source describes that town, so each element is taken from whichever era documents
it best.** The rule, and a table naming the source and era for every part of the town, is in
`docs/research/SOURCING.md`. In one line:

> **Take the fabric from whoever measured it, and the use from whoever lived in it.**

A building's footprint, material and storey count were surveyed in 1913 and had not changed by
2000 — those buildings stood until the 2004 fire. What the shop *sold* changed completely. So the
shape comes from 1913 and the sign over the door comes from 2000. Counter-intuitively, **1913 is
the better-documented year**: it has a professional survey of every building, where 2000 has
modern parcels, 19 OSM footprints and living memory.

## Next, in the order I would do it

1. ~~Verify the suite~~ **DONE 2026-08-03.** Core 227/2 (the two by-design), PlayMode 11/13 —
   see `docs/OVERNIGHT-2026-08-03.md` for the two known failures.
2. ~~Walk the plan and correct it~~ **PARTLY DONE.** The user's eye caught that Chicago Street was
   straight; it is now on its real surveyed curve with asphalt on it.
3. **A house kit — and the research now says what it must be.** See
   `docs/research/ROSSVILLE-HISTORY.md` §7a. Three date-layers, **all frame, none brick**:
   - the **1857–1910s core** — 1 to 1½ storey vernacular farmhouse and American Foursquare, small
     rear outbuilding, quarter-acre lots *(directly observed on the Sanborn sheets)*
   - a **larger 1920s–40s layer** — bungalows and minimal-traditional cottages. Median build year
     for the town is **1943**, so this is the biggest single cohort and it postdates the maps
   - a **minority of postwar ranches** on the newer edges

   **Approach undecided on purpose:** prototype one house procedurally and one from the owned
   packs, look at them side by side, then choose. Buying a pack and spending AI points are both
   still open but neither is committed.
4. **Building footprints — and there is a source nobody knew about.** The county has none and OSM
   has 19 in the whole village, but **five Sanborn fire-insurance atlases are public domain and
   nine sheets are already downloaded** to `docs/research/sanborn/`. They give exact footprints,
   construction material, storey count and use for 1898, 1906 and 1913 — and most of the downtown
   brick row survived from 1893 to 2004, so **the 1913 footprints are substantially valid for
   2000**. Decided: georeference them for the commercial core.
5. ~~Elevation~~ **DONE and verified.** USGS NED, 24.85m of relief carried by the built mesh across
   600,780 vertices, reading exactly 0.0m at the Chicago/Attica origin. `Noir/Probe The Elevation`.

## Cost discipline — read this before doing anything

A previous session burned a month's budget in about three hours. What did it:

- **Two 11-agent workflows, ~2.95M tokens**, designing things before checking whether they were
  needed. The actual blocker that evening — "does the pack contain an Illinois frame house?" —
  was one `find` command.
- **A ~250-call main loop on a 200k context.** Every call re-sends the whole conversation.
- **Unity runs take 5–15 minutes**, which exceeds the 5-minute prompt cache TTL — so every
  render/test cycle re-read the entire context at FULL input price.

So: ask the cheap question first. Batch the Unity loop — one render, look at everything, fix
everything, one render. Do not run workflows unless asked by name. Start a fresh session when
the context gets long.
