# Handoff: the survey layer — buildings, roads, and the 1991 rulings

Written 2026-08-05, ~21:30, at the end of an owner session. **Everything below is uncommitted
and none of the C# has ever been compiled.** That is the whole point of the run.

The owner is asleep. **Decide, do not ask. Skip anything blocked and say so at the end.** One
report in the morning, not a running commentary.

---

## THE ONE THING THAT MATTERS MOST

**Eleven C# files were changed or added and Unity has never seen any of them.** Unity was
holding the project lock all session, so `-batchmode` could not run and the editor window could
not be reached to force a recompile. Every claim below about C# is "reviewed against the real
symbols", **not** "compiled".

```
 M Assets/Noir/Core/Sim/Pathfinder.cs         FindPathViaAlley, TryFindAlleyNear
 M Assets/Noir/Core/World/ContentText.cs      Number() — a float parser next to Int()
 M Assets/Noir/Core/World/RoadNetwork.cs      AmbientTrafficWeight()
 M Assets/Noir/Core/World/TileGrid.cs         TileFlags.Alley (bit 7), IsAlley()
 M Assets/Noir/Core/World/VillageLayout.cs    RoadRun.Easement
 M Assets/Noir/Core/World/VillageParser.cs    `easement` road attribute + its guard
 M Assets/Noir/Core/World/WorldBuilder.cs     stamps TileFlags.Alley when rasterising
 M Assets/Noir/Unity/CityTraffic.cs           spawn + turn weights read AmbientTrafficWeight
 M Assets/Noir/Unity/VillageHost.cs           calls SurveyRoads.Apply before WorldBuilder.Build
?? Assets/Noir/Unity/SurveyRoads.cs           NEW — swaps in Content/roads.txt
?? Assets/Noir/Unity/ParcelBuildings.cs       NEW — reads Content/parcel-buildings.txt
```

**Job 1. Close Unity, compile, fix what breaks.** Nothing else in this document is worth doing
until that is green.

```
Unity.exe -batchmode -quit -projectPath C:\SerialKillerGame ^
  -executeMethod Noir.Editor.Preflight.Run -logFile <log>
```

Unity 6000.3.20f1. It will not take the lock while the editor is open — **check for and close
`Unity.exe` first**, and if the owner left it open deliberately, say so rather than killing work.

Then press on to `-runTests`:

```
Unity.exe -batchmode -projectPath C:\SerialKillerGame -runTests ^
  -testPlatform PlayMode -assemblyNames Noir.PlayTests -testResults <xml> -logFile <log>
```

**`-assemblyNames Noir.PlayTests` is not optional** — without it the runner discovers
`LLMUnityTests.TestLLM`, whose constructor downloads a language model, and the run never
finishes. Do **not** pass `-nographics`; two tests render and fail spuriously without it.
Expect **13/13**. See `docs/HANDOFF.md` for the standing version of this.

---

## What was built

### Data, all derived and re-runnable

| file | what | rebuild with |
|---|---|---|
| `Content/parcel-buildings.txt` | **824 building footprints** seated on the county's lots, with area, use, address, and two skew angles | `python tools/seat-buildings.py` |
| `Content/roads.txt` | **61 roads** — county centrelines for the streets, alleys traced out of the parcel gaps | `python tools/build-roads.py` |
| `Content/kinds.txt` | **+6 kinds**: cemetery, park, waterworks, sewageworks, publicworks, municipal | hand-edited, keep |

### `Content/parcel-1991.txt` — AUTHORED. DO NOT REGENERATE.

**170 rulings the owner made by hand about what stood on each lot in 1991.** Every other file
here can be rebuilt from the downloads in `tools/`. This one cannot. No script may write it
except the two that already do (`merge-back-strips.py`, `group-terraces.py`, both of which back
it up first), and neither should be re-run without reading what it would change.

Five `.before-*` backups sit beside it. Leave them until the owner says otherwise.

### The browser map

`docs/rossville-buildings.html`, built by `python tools/build-viewer-data.py`, served by
`python tools/serve-viewer.py` on **http://127.0.0.1:8750**. It is gitignored (`docs/*.html`)
and fully regenerable from `tools/viewer-template.html`.

The server also owns the write path for the rulings (`POST /__verdict`). **If the owner is
going to use the map in the morning, leave the server running or restart it.**

---

## What to do, in order

1. **Compile. Fix whatever breaks.** Above. Nothing else matters until this passes.

2. **Run the Core suite.** `dotnet test tools/Noir.Core.Tests/Noir.Core.Tests.csproj`
   Expect **359 pass, 2 fail**. The two failures are `TwoToOneTests.TheMedianVillagerYields…`
   and `TheTenthPercentileIsNotALock` — **they fail by design and failed at HEAD before any of
   this work**; `docs/IDEAS.md` records the confirmation. Do not "fix" them.

   New tests that must pass: `RoadsFileParseTests` (4), `AlleyBehaviourTests` (9).

3. **Press Play, or drive it headlessly, and LOOK AT IT.** The console should say:
   ```
   [roads] survey network in use: 61 roads (33 alleys) from Content/roads.txt, replacing 37 from city.txt
   ```
   If `Content/roads.txt` is absent or malformed, `SurveyRoads.Apply` no-ops and the town keeps
   city.txt's roads — that is deliberate, but it means **silence is a failure signal here**, not
   success. Confirm the line appears.

4. **Render a still and look at it.** `Noir > Preflight (audit + render, one pass)`. The house
   of standing practice is that MapAudit and the tests cannot see ugly.

5. **Commit, on a branch.** Do not commit to `main`. Suggested split, so a bad piece can be
   reverted without losing the rest:
   - the derived data + tools
   - the C# (Core, then Unity)
   - `Content/parcel-1991.txt` **on its own**, because it is the irreplaceable one
   - the docs

---

## Known loose ends — leave them unless they block the build

- **11–14% of each downtown terrace polygon sits on no parcel**, overhanging the right of way.
  Partly real (a terrace built to the sidewalk edge, awnings traced in), partly loose tracing.
  Not separable with the data on hand. Not a bug to fix blind.
- **`green` runs on private land 12.2%** of its length — the worst road left, and it is bad in
  *both* sources (city.txt 14.8%, county 10.9%). Something is genuinely odd about Green Street.
- **`SurveyRoads`/`ParcelBuildings` are wired but nothing consumes the footprints yet.** The
  buildings are loadable and unused; that is the next feature, not a defect.
- **The junction graph has not been rebuilt** against the new roads. Geometry only so far.
- **`kind shop` was set across all 17 terrace lots**, which flattens what the 1913 Sanborn sheet
  names individually (bank, jeweller, bakery, hardware, barber, printing). Fine for now.

## Standing rules for this run

- **Headless runs must be silent.** Batch mode has blared game audio into the owner's headphones
  before. If you hear it, suspect your own process first.
- **Do not open a browser.** The owner uses Chrome profile `Profile 1` ("Billy") and had
  Playwright windows thrown in his face all session. Verify from logs, files and tests.
- **Do not ask.** Decide, note the decision, move on. Report once at the end.
- **Imperial units when writing to the owner.** Feet and miles. The files stay metric.

## The research, if you need the why

- `docs/research/BUILDING-FOOTPRINTS.md` — where the buildings came from, the three validation
  checks, the setback measurements, and every limit of the source
- `docs/research/ROADS-FROM-SURVEY.md` — why 32 of city.txt's 37 roads were ruled straight lines,
  and the easement/corridor distinction
- `docs/SOURCES-OF-TRUTH.md` §3 — the owner's standing facts. **Fact 4 (alleys are not for cars)
  and fact 5 (a lot ran to the alley) were added this session** and are load-bearing.
