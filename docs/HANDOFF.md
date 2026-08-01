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
      -testResults <xml> -logFile <log>

Expect **13/13**. Do NOT pass `-nographics` — two tests render and fail spuriously without it.

## Next, in the order I would do it

1. **Verify the suite** (above). One run.
2. **Walk the plan and correct it.** The user grew up there for 26 years; their eye is worth
   more than any dataset. Ask what is wrong before building anything else.
3. **A house kit.** Nothing about the residential streets can be right until there is one.
   Candidates: Suburb Neighborhood House Pack (modular, suits `Stack`), POLYGON Town Pack
   (Synty, closest style match). Swapping `only: "Bayhouse"` for a new family is a small
   contained change — grid, addresses and population all stay.
4. **Building footprints**, if wanted: the county has none (Danville's layer stops ~4km south),
   OSM has 19 in the whole village. Microsoft US Building Footprints and Overture are unchecked.
5. Elevation is **scoped in `docs/IDEAS.md`** and deliberately not started.

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
