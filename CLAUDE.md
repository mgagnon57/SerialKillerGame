# SerialKillerGame — Rossville, Illinois, 1991

A narrative crime game built on a measured model of a real town: Rossville, Vermilion County,
Illinois, as it stood in 1991. The town is assembled from county parcel records, federal building
footprints and 1913 Sanborn survey sheets, and where those sources disagree with somebody who
lived there, he wins.

---

## THIS FILE WINS

**If a number or a command in this file disagrees with any other document, this file is right and
the other one is stale — go and fix it.**

That rule exists because the alternative was measured. On 2026-08-07 six documents each stated a
different Core test baseline (72, 227, 316/318, 323/325, 359, 341) and none of them matched the
suite. Worse, `-c Release` appeared in the three OLDEST documents and had been dropped from the
newest, so the knowledge had actively regressed and every session since was paying four times the
runtime for a noisier answer. Facts here get ONE home. Everywhere else points at them.

---

## The load-bearing facts

| | |
|---|---|
| **The year** | **1991.** Not 1995, not 2000, not "1991–2013". Authority: `docs/research/THE-ERA.md` |
| **The map** | `Content/city.txt`, loaded via `VillageHost.MapFile` |
| **The one file no re-run can rebuild** | `Content/parcel-1991.txt` — 173 hand-made rulings about what stood on each lot. See *Content* below: it is not the only file that cannot be rebuilt, and "never overwrite it" has two named exceptions |
| **Building the town** | `TownPipeline.Build()` and nothing else. See below |
| **Randomness** | Everything routes through `IRng`, one substream per system: `Xoshiro256ss.Substream(seed, "name")`. No `System.Random`, no `DateTime.Now`. That is what makes a seed reproduce a village |
| **Which source outranks which** | `docs/SOURCES-OF-TRUTH.md` |
| **The people, and what animates them** | `Content/animations.txt` maps a SITUATION to clips; the key is `Activity.ToString().ToLowerInvariant()`, optionally `@place` and `:person`. Rename an `Activity` and the row must move in the same commit or `Resolve` falls through to `default` and those people play a generic idle forever — `EveryActivityHasARowInTheRealFile` fails the Core gate if you forget. The clips live in `Townsfolk.controller`, rebuilt by `Noir > Build The Townsfolk Animator` |

### There is exactly one way to build the town

```csharp
var built = TownPipeline.Build();     // Assets/Noir/Unity/TownPipeline.cs
```

It installs the kind and technology tables, parses the map, runs all five survey passes **in
order**, builds the world and validates it. `TownPipelineTests` fails the suite if any other file
under `Assets/Noir/Unity` or `Assets/Noir/Editor` calls `WorldBuilder.Build` directly.

For a fixture or an in-memory map that is deliberately **not** the real town, use
`TownPipeline.BuildUnsurveyed(layout, seed, name)` — named for what it gives up.

Before 2026-08-07 this pipeline was a run of statements inside `VillageHost.Awake`, and eighteen
editor tools each rebuilt it by hand; fourteen ran no survey passes at all, so every offline
render and audit measured a town the game does not build. Do not reintroduce that by hand-rolling
a build "just for this tool".

---

## Verifying a change

> **Precondition for every `Unity.exe` command below: the editor must be closed.** Unity allows
> one instance per project and takes an exclusive lock on `Library/`; a `-batchmode` run started
> while the editor is open simply fails. The owner's normal state is *editor open* — he is the one
> who presses Play — so check for `Unity.exe` first, and if he left it open deliberately, say so
> rather than killing his work. Unity **6000.3.20f1**.

**1. Core tests — the standing gate.** Run in **Release**: it is four times faster and it is the
configuration the baseline is stated for.

```
dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj
```

> **477 pass, 0 fail, 477 total, 7 skipped, 3 m 05 s.** Measured 2026-08-10 17:14, at the end of
> the road pass. **Two other documents were stale within the same afternoon** — this file said 469
> and `docs/ROAD-FIXES.md` had measured 473 — which is the failure THIS FILE WINS exists to stop.
> The 7 skipped are the four Core `[Explicit]` printers, `PrintWalkableRegions`, and the two
> `Aspiration` tests; a run reporting 0 skipped means somebody un-quarantined a diagnostic.
> (469 earlier that day, 462 the day before; +4 `SurfaceTextureTests` — the texture estate reached this gate for the
> first time — then +7 more, and every one of them is a GATE ON A SILENT FAULT rather than a new
> feature's test: `AmenitiesAreNotHousesTests` (an amenity may not have the default house
> footprint; it found the town's church on its first run), `HomeIsAColumnNotAnEnumMemberTests`
> (six places asked `Kind == PlaceKind.Dwelling` about a town whose `kinds.txt` says
> `apartment / home yes`), `NoContentLoadFailsInSilenceTests` (six content loads swallowed a
> failure and returned; one of them cost the map its relief), two on `watched.floor` failing open,
> and `EveryActivityHasARowInTheRealFile`.)
>
> **THE RELIEF IS 24.5 m (80 ft), NOT 195 m.** Commit `f191e75` and `docs/SIM-FIXES.md` both say
> "5,754 samples, 30.00 m to 225.30 m, 195.3 m of relief". That is the measuring script eating the
> data header: `Content/elevation.txt` opens its grid with `grid 71 81 30`, and 71 × 81 = **5,751**
> — the three extra "samples" are the column count, the row count and the 30 m step, which is where
> the 30.00 m minimum comes from. The file's own header states the range: **200.8 m to 225.3 m**.
> `ElevationGrid.cs:116` parses it correctly, so the GAME was never wrong; only the number written
> about it was. Rossville is glacial till plain — as `Content/elevation.txt` puts it, *"it is not
> flat, but it is close, and anything that looks like a hill here is wrong."*
> (447 on 2026-08-08, and 415 earlier that day; +8 `DrivewaysTests`, +5 `TrafficWeightTests`,
> +3 `AnimatorContractTests`, +8 `AnimationTableTests`, +8 `AnimationRowTests`,
> +4 `RoadPathTests` and +2 `SurveyRoadNetworkTests` — `RoadPath.ArcAt`, and the gate on two
> roads sharing tarmac with no junction between them; +5 `DoorsThatOpenTests`.)
> **Any red is a regression.** There is no standing exception any more, and there was one for
> months: `TwoToOneTests` G1 and G2 asserted the project's 2:1 design rule, the town is at
> 0.89 : 1, and they were correctly and permanently red. Two permanent reds make a THIRD red easy
> to miss — on 2026-08-09 a session twice read "4 failed" and had to dig to find which two were
> new — so they are `[Test, Explicit, Category("Aspiration")]` now.
>
> **Nothing was deleted and the standard did not move.** The number is measured every run on the
> same three ~148-person **fixture** villages, seeds 1979/1980/1981 — **not on Rossville, which has
> never been measured against this rule** — printed by `TheGapToTheRuleIsReported` and recorded
> with its full instrument in `Content/watched.floor`:
>
> ```
>   ---- THE 2:1 RULE, worst of three villages ----
>   median villager   0.89 : 1   against a rule of 2.0   short by 1.11, 2.3x to go
>   tenth percentile  0.33 : 1   against a bar of 1.0    short by 0.67, 3.0x to go
> ```
>
> It is still ratcheted by `ProgressDoesNotReverse` and `TheEyeHasNotBeenNarrowed`, which DO fail
> the standing gate if it regresses — they were always the tests doing the protecting. Run the
> aspiration itself with `dotnet test --filter "TestCategory=Aspiration"`.
>
> **Do not close the gap by adjusting the instrument — and do not assume more KINDS of moment will
> close it either.** `Content/watched.floor`'s 2026-08-01 entry is the counter-example, recorded
> with the instrument provably unmoved: texture went 24 → 29 kinds and the ratio went DOWN,
> 1.21 → 0.89. Anything that puts a person in view gives a watcher both columns at once. What
> moves it is a kind of moment that is not simply more time in the open.

**Do not run this suite in Debug.** It takes 8–9 minutes and it is not stable: across three runs
on an unchanged tree it produced a third failure that did not reproduce. That is consistent with
the known 13900K Vmin instability on this machine — verify in Release before blaming code.

**2. Unity assemblies compile.** A green `dotnet test` is not evidence that Unity compiles. The
test suite compiles `Assets/Noir/Core` and nothing else — **17,289 of the 59,739 lines under
`Assets/Noir`, or 29%.** The other 71%, including the whole survey layer, has no automated cover
at all and is reached only by PlayMode and by looking at it.

```
dotnet build Noir.Unity.csproj -c Debug
dotnet build Noir.Editor.csproj -c Debug
dotnet build Noir.PlayTests.csproj -c Debug
```

**THE THIRD ONE IS THE CHEAPEST FOUR SECONDS IN THIS PROJECT AND I DID NOT KNOW IT EXISTED.**
On 2026-08-09 two eighteen-minute PlayMode runs were lost to compile errors in the test assembly
itself — one a missing `using`, one a half-written file Unity picked up during its startup asset
refresh. Both read the same way in the log, near the top where nobody looks:
`Test run completed. Exiting with code 3 (RunError). Scripts had compilation errors.` A commit
message that day claimed there was nothing to build `Noir.PlayTests` against short of Unity.
There is. Build it before every PlayMode run.

These `.csproj` files are **gitignored and generated by Unity from the asmdefs** — on a fresh
clone, open the project in Unity once before expecting them to exist or to list new files.

**3. PlayMode — the only automated thing that can see the Unity layer.**

```
Unity.exe -batchmode -projectPath C:\SerialKillerGame -runTests -testPlatform PlayMode ^
  -assemblyNames Noir.PlayTests -testResults <xml> -logFile <log>
```

`-assemblyNames Noir.PlayTests` is **not optional**: without it the runner discovers
`LLMUnityTests.TestLLM`, whose constructor downloads a language model, and the run never finishes.
Do **not** pass `-nographics` — two tests render and fail spuriously without it.

**Run the gates, not the diagnostics.** The suite is split by category:

> **`NOIR_BUILT_TOWN=1` BUILDS THE DRESSED TOWN INSTEAD OF THE SURVEY PLAN, AND IT IS NOT ON BY
> DEFAULT.** Measured 2026-08-09, both runs on this machine within ninety minutes:
> **`[build] 23538 ms` for the plan against `26777 ms` for the built town — +3.2 s, ONCE per run,
> not per test.** That is a fifth of what the same comparison cost on 2026-08-06 (+10 s), because
> `CityUnderTest` now opts out of Trees, Farm and Powerlines and those were most of it. The build
> cost is NOT the reason to leave it off.
>
> **What IS the reason, measured the first time the gate ever ran against the built town:**
> `NoCarWaitsForeverAtTheHeadOfAClearQueue` went red at **37.45 s against the 36.0 s cycle**, in a
> run where it passed at 15.3 s ninety minutes earlier on the plan town. Streets, parking and signs
> had never been drawn under the traffic suite before, and the fleet is still the documented
> eight-times-too-big one. **Do not turn this on for the standing gate until the fleet curve
> lands** — and when it does, run it twice before believing the number, per the rule below.
>
> The variable is read in `CityUnderTest`, beside the layer opt-out, and every run prints
> `[gate] town=…` so a session can never again wonder which town a green tick was green for. It
> used to live inside `LayerProof`, a `Category("Diagnostic")` file the gate excludes.



```
Unity.exe -batchmode -projectPath C:\SerialKillerGame -runTests -testPlatform PlayMode ^
  -assemblyNames Noir.PlayTests -testCategory "!Diagnostic" -testResults <xml> -logFile <log>
```

> **BASELINE, 2026-08-10: 19 of 19 PASS, 1 skipped, 700 s.** (18 of 18 the day before.)
> **BASELINE, 2026-08-09: 18 of 18 PASS, 1 skipped, 744 s.** (17 of 17 and 16 of 16 earlier that day, 13 of 13
> on 2026-08-08, 8 of 8 on 2026-08-07; TownGeometryPlayTests added three that can see where a
> building STANDS, a fourth that can see whether the car outside a house is there today, a fifth
> that fails if any door in the town cannot be walked through, a sixth that fails if the roofs ever
> go white again, and a seventh that reads the covering off the town that is RUNNING - shader,
> bound map and the census over all 672 roofs - instead of off the source that describes it.)
>
> **The one skipped is `[Explicit]` and is meant to be**: `AMovingCarCostsOneRendererAndNoMeshCollider`
> is an aspiration, the same treatment as the 2:1 rule, for the same reason - a permanent red hides
> the next real one. `-testCategory "!Diagnostic"` selects 18, one is that aspiration, and that is
> exactly the "17 of 17, 1 skipped" above.
> `[body] 1390 Animators in the scene`, and the layer preferences are the same after the run as
> before it.
>
> **The town the game now drives on: `120 junctions: 2 signalised in the town (8 heads), 118 on
> priority out in the country`** — against 74 junctions and one signal before the alley mouths
> landed on 2026-08-09. The road network changed more that day than on any other: the mouths, the
> axis filter, the smoothing curve and what counts as two roads touching. The account now lives
> in `docs/research/ROADS-FROM-SURVEY.md`, whose two open questions — "nothing reads roads.txt"
> and "the junction graph has not been rebuilt" — are both answered there with the measurements;
> item by item it is `docs/ROAD-FIXES.md`.
>
> **THE RUN IS NOT ~4 MINUTES AND NEVER WAS.** Measured 371.6 s wall clock, of which
> `WhyAreThePeopleNotAnimating` alone is **292.9 s** — 79% of the suite. Budget ten minutes.
>
> **THE STARVING JUNCTION WAS FIXED BY THE COUNTY'S TRAFFIC COUNTS, NOT BY MOVING A GATE.**
> `NoCarWaitsForeverAtTheHeadOfAClearQueue` swings **37.2 s to 21.9 s on an unchanged tree** with
> the same 159-car fleet — so do not chase it by tightening the gate, make it name which junction
> is starving, and **run it twice before believing a traffic number moved, never in the same run
> as a topology change.** The fix was to weight ambient traffic by IDOT's own counts instead of
> the road-class ladder: a measured **21:1** where `AmbientTrafficWeight` could only say 4:1, held
> now by `TrafficWeightTests`. Cars stopped circulating on Church and Maple because the county
> says they are not there. See `docs/research/TRAFFIC-COUNTS.md`; the junction-by-junction tail is
> in `docs/IDEAS.md`.
>
> **THE FLEET IS A CURVE NOW, 2026-08-10. It was eight times too big for months.**
> `CarsOutPerHousehold = 0.25` ran **159 cars flat, all day**, against IDOT's ~19 moving at an
> average instant and ~46 at peak. It is `CityTraffic.CarsOutByHour` — a 24-entry table, absolute
> cars for the 624 households the counts were taken against, scaled off `DeclaredHouseholds`. It
> sums to 464, a mean of **19.3** against the measured 19.3, and peaks at **46** at 07:00 and
> 17:00, which is when Rossville leaves for Hoopeston and Danville and comes back.
>
> The fleet is instantiated ONCE at the peak and **garaged**: `CityTraffic.Retime`, driven from
> `VillageHost.Update` off `Sim.Clock.MinuteOfDay` beside the driveways, moves vehicles between
> `_movers` and `_garage`. **`_movers` is a second list rather than a flag** because eight loops in
> that file walk it to decide who is following whom — keeping it as exactly "the cars out right
> now" leaves all eight correct untouched.
>
> ⚠ **THE TRAP IS NOW LIVE AND UNMEASURED: the sim starts at NOON, which the table puts at 24
> cars.** Every PlayMode test now gets a near-empty town where it used to get 159, and
> `NoCarWaitsForeverAtTheHeadOfAClearQueue` may have nothing to watch. **The PlayMode gate has not
> been run against this.** That is a test-design problem — the fix is for a test that needs traffic
> to set the hour — and it is not a reason to inflate the town.
>
> **The cycle is 36.0 s, not 37.** `TrafficPlayTests` carried `const float Cycle = 37f` under a
> comment saying it was "CitySignals' own cycle length" for months. It is 14 s green + 3 s amber +
> 1 s all-red, twice — and the `[signals]` line has printed `36.0s cycle` every single run. The
> test asks `CitySignals.Cycle` now. A number sitting next to a comment asserting it is right is
> the hardest kind to see.
>
> **THERE WERE NEVER ANY FLAKY TESTS.** Two were called flaky in this very file, by me, and both
> were real bugs with a persistent cause:
>
> - `ThePlayerCanStandInTheStreet` — `CityCollision`'s ground mesh was wound to face DOWNWARDS, so
>   the player dropped onto the back of the floor. PhysX stops a `CharacterController` on a
>   backface *sometimes*, which is what intermittent looks like when nobody has measured it.
> - `WhyAreThePeopleNotAnimating` — `Layers.Set` wrote `PlayerPrefs`, so `LayerProof`'s walk
>   through every layer combination **permanently rewrote the editor's layer preferences**.
>   Whatever combination a run ended on stuck. When it ended with People off, `VillageHost` never
>   built `AgentMeshView` and this test failed with no error in the log at all.
>
> Flaky is a diagnosis of last resort. Both of these cost under an hour once somebody measured
> instead of shrugging.

**There are 23 tests. `-testCategory "!Diagnostic"` selects 16, one of those 16 is `[Explicit]`,
and that is exactly the "15 of 15, 1 skipped" above.** The other seven are diagnostics wearing a
test's clothes — four call `Assert.Pass()`, and `Tour`/`FilmStrip`/`LayerProof` assert only that
the right number of image files appeared. They carry **150 minutes** of the timeout budget between
them, which is why the whole suite never finished: run un-split it can take **four hours**, and it
was being killed at twenty minutes and called a hang. It was never hung. It was slow by design and
nobody had ever seen the end of it.

**`PeopleDiagnostics.WhyAreThePeopleNotAnimating` carries no `Category("Diagnostic")`**, so the
gate command above still runs it, and at 292.9 s it is 79% of the eleven minutes. It is also the
only test that can see the people animate — categorising it out buys a two-minute gate that is
blind to the thing this project spent a week fixing.

**Run this first, or the run drowns.** `Assets/polyperfect/` is gitignored, so the pack's mesh
import settings are NOT in the repo and Read/Write defaults to off:

```
Unity.exe -batchmode -quit -projectPath C:\SerialKillerGame ^
  -executeMethod Noir.Editor.MeshReadable.Enable -logFile <log>
```

Measured 2026-08-07: **79 models needed it, 316 already had it**, across 395 model assets — the
city's tiles *and* the 79 figure prefabs, which were invisible to this tool until it learned to
walk `SkinnedMeshRenderer` as well as `MeshFilter` (a skinned mesh has no `MeshFilter` at all).
Before it ran, the PlayMode log carried **3,503** city "not Read/Write enabled" warnings and, once
the people were built, **1,390** more against `boy-*`, `girl-*` and `man-*`. After it: **zero**.

Also measured, and worth knowing because a doc claims otherwise: the town builds in
**`[build] 1941 ms`**, not the two minutes quoted elsewhere.

**5. Build a player.** The only check that compiles the runtime assemblies WITHOUT `UNITY_EDITOR`.

```
Unity.exe -batchmode -quit -projectPath C:\SerialKillerGame ^
  -executeMethod Noir.Editor.BuildPlayer.Windows64 -logFile <log>
```

Ships `Content/` beside the exe (holding back the gitignored, name-bearing files), forces every
`Shader.Find` name into Always Included, and exits non-zero on failure. **Do this weekly.** The
first one ever attempted, on 2026-08-07, took three tries: it would not compile, then loaded no
content, then drew nothing — each failure invisible to the editor, to the whole Core suite and to
Play. It now boots the town and starts the clock:
**`[boot] ready after 5.4s — 12 straight frames under 25 ms. Clock running.`**

> **MEASURED IN THE PRODUCT, 2026-08-09, and it is the only place these can be measured.** A
> launched `Rossville.exe` prints its own texture line now, and it reads
> **`Surface textures: player build, no pack path. 0 loose (), 16 MISSING: brick, churchyard,
> field, floor, grass, ground_rough, path, road, roof_builtup, roof_shingle_black,
> roof_shingle_brown, roof_shingle_charcoal, roof_shingle_grey, wall, water, wood.`** Sixteen of
> sixteen, because `ShipTheContent` copies Content's TOP LEVEL only — `Content/textures/` has
> never once shipped. So every surface in the product is a flat `Materials3D` fallback colour, and
> that is why those colours had to be the measured means: the screenshot shows dark grey, charcoal
> and brown roofs on near-black asphalt, where the same build a day earlier drew white roofs on
> pale grey roads. **`docs/ANIMATION-FIXES.md` PB-3 is the item that makes the textures ship**;
> until it lands, the fallback IS the game. The launch also reports
> `[boot] gave up waiting after 188.7s - the frames never settled` and then runs at 104 fps with
> 22,310 renderers — the settle check is measuring the build, not the frame rate.
>
> **What it boots is not what you see in the editor, and this is not yet fixed.**
> `AssetDatabase` and `PrefabUtility` are `UnityEditor` APIs that do not exist in a player, so
> `CityBuildings`, `CityStreets`, `CityGreenery`, `CityTraffic`, `CityParking`, `CitySigns` and
> `SunRig` all sit behind `#if UNITY_EDITOR`. A standalone build gives you the procedural survey
> plan, **primitive capsule people, and none of the bought props.**
>
> **WHAT CHANGED 2026-08-10, AND WHAT DID NOT.** Three of the silences are closed and the rest are
> now audible rather than invisible:
>
> - **`Content/` SHIPS WHOLE.** `ShipTheContent` walked the top level only, so `Content/textures/`
>   and `Content/audio/` had never once shipped and a launched build reported
>   `Surface textures: 0 loose (), 16 MISSING` — every surface in the product a flat colour. It is
>   `15 loose, 0 MISSING` now. ⚠ **That recursion made `NeverShip`'s `private` entry load-bearing
>   for the first time** — it is a DIRECTORY, and `Directory.GetFiles` returns none, so it had been
>   matching nothing at all. It is matched against every path SEGMENT, and every build prints
>   `[build] privacy: …` so nobody has to remember to check.
> - **PRESSING P WORKS IN A BUILD.** `Player.Spawn` was editor-only end to end. It loads the
>   armature from `Resources` first, in the editor too, so the shipped path is the one exercised
>   daily; run `Noir > Make The Player Shippable` after re-importing Starter Assets, and
>   `ThePlayersBodyIsWhereAShippedBuildWouldLookForIt` fails the gate if you forget.
> - **THE CAPSULE CROWD SAYS SO.** `[people] all 1400 of Rossville is PRIMITIVE CAPSULES` — split
>   on `Application.isEditor`, because in the editor the cause is the gitignored pack and in a
>   build it is `AgentBody` reading through `AssetDatabase`. The fix is the cast manifest,
>   `docs/ANIMATION-FIXES.md` PB-6/PB-7, and it is the largest thing left in that plan.
>
> Still true, and still the trap: only `CityStreets` has an `#else` that does anything — it covers
> one *measurement* — and `SunRig`'s returns `null`. `PolyPackCottageBuilder` warns from its own
> `#else` as well, which is the second voice in this paragraph and the first version of it missed.
> `CityChunker` has no editor guard at all, so it runs in a player and combines an empty scene.
> `tools/check-editor-only.py` catches the half of this trap that fails to compile; **the half that
> silently draws nothing is caught by launching the exe and reading its own log**, which now says
> what it got and what it did not.

**6. Smoke test — the cheapest thing that builds the real town.** `Noir → Smoke Test`, or:

```
Unity.exe -batchmode -quit -projectPath C:\SerialKillerGame ^
  -executeMethod Noir.Editor.SmokeTest.Run -logFile smoke.log
```

**4b. Frame rate — measure it INSIDE one run, never between runs.**

```
Unity.exe -batchmode -projectPath C:\SerialKillerGame -runTests -testPlatform PlayMode ^
  -assemblyNames Noir.PlayTests -testFilter "Noir.PlayTests.PerfCensus.WhatIsEatingTheFrame" ^
  -testResults <xml> -logFile <log>
```

> **~6 minutes, against 17 for the whole suite.** Switches one layer off and straight back on and
> reports median frame time, so drift is common to every sample and cancels. It measures the
> baseline at BOTH ends and prints the gap: if that exceeds 15%, the instrument moved and the table
> is soft.
>
> **DO NOT INFER FRAME RATE FROM HOW LONG THE SUITE TAKES.** Suite duration does not repeat on this
> machine, and a whole session was lost to believing it did on 2026-08-08.
>
> **A NEGATIVE "saves" FIGURE IS THE INSTRUMENT, NOT THE TOWN.** Lean on the baseline pair, which
> needs no toggle. Both of these are argued out in full, with the numbers, in `PerfCensus.cs`'s own
> class header.
>
> **AND ALWAYS READ THE ANIMATING COUNT NEXT TO THE MILLISECONDS.** An 80 m animator cull measured
> 11.3 ms → 1.4 ms, 88 → 712 fps, and was a static town: `0 of 1385 animating`, because
> `OrbitCamera` opens at 330 m. The head-count budget that shipped instead is 219 fps with ~150
> animating. The fastest number was the wrong one.

**4. Look at it.** The tests and `MapAudit` cannot see ugly. Render a still and actually view it.

`dotnet test | tail` reports **tail's** exit status, so a crashed run reads as a pass. Run it
bare, or capture the exit code directly.

---

## Content — what can be rebuilt, and what cannot

`CLAUDE.md` used to say everything but `parcel-1991.txt` was "derived and regenerable from
`tools/*.py`". That is not true, and believing it is how an irreplaceable file gets deleted.

| File | Rebuild |
|---|---|
| `parcel-1991.txt` | **Cannot be rebuilt.** 173 rulings, authored in the browser map (below) |
| `kinds.txt` | **Hand-edited** — the +6 kinds `cemetery park waterworks sewageworks publicworks municipal`. Keep |
| `particulars.txt` | **Hand-authored**, 914 clauses. See the era warning under *Traps* |
| `parcels.txt`, `parcel-county.txt`, `elevation.txt` | **No committed regeneration script.** Downloaded, not derived. Treat as irreplaceable until one exists |
| `parcel-buildings.txt` | `python tools/seat-buildings.py` — 824 footprints seated on the county's lots |
| `roads.txt` | `python tools/build-roads.py --write` — county centrelines, alleys traced from parcel gaps. **Bare, it is a dry run:** it prints what it would write and writes nothing. `--write` backs up first. The twelve IDOT counts come from `tools/rossville-aadt.txt` |

**Only two scripts may write `parcel-1991.txt`** — `tools/merge-back-strips.py` and
`tools/group-terraces.py`, both of which back it up first. Neither should be re-run without
reading what it would change. Everything else reads it.

**The browser map is how the rulings are made.** `docs/rossville-buildings.html`, built by
`python tools/build-viewer-data.py`, served by `python tools/serve-viewer.py` on
`http://127.0.0.1:8750`; the server owns the write path (`POST /__verdict`). The HTML is
gitignored and regenerable from `tools/viewer-template.html`. Lose this and the one file nothing
can rebuild becomes unmaintainable. **Do not open it at the owner unasked.**

---

## Traps that are still in the code

- **Silence is a failure signal in `SurveyRoads`.** If `Content/roads.txt` is absent or malformed,
  `SurveyRoads.Apply` no-ops and the town quietly keeps `city.txt`'s roads — deliberate, but it
  means a run that built the *pre-survey* town looks identical in every log, test and render.
  Confirm the line appears: `[roads] survey network in use: … from Content/roads.txt, replacing …`
- **`Time.timeScale` does not speed the sim clock.** The simulation runs on `Time.unscaledDeltaTime`
  on purpose — how fast a day passes is a property of the game, not of Unity. Anything asserting on
  sim time waits in *real* seconds. This is why the PlayMode diagnostics carry two and a half hours
  of timeout budget between them (counted under *Verifying a change*) and cannot be shortened by
  touching `timeScale`.
- **The city is built once and shared by every test in a run.** Anything you change on
  `VillageHost` — especially `SpeedIndex` — must be restored in a teardown. Both "flaky" tests
  above were this shape.
- **DO NOT EDIT ANY `.cs` WHILE A BATCH RUN IS GOING.** A `-batchmode` Unity refreshes the asset
  database as it starts — 67 s of the 82 s startup — so a file saved in that window is compiled,
  and a file saved half-written is a compile error. On 2026-08-09 a new editor script was added
  during an eighteen-minute PlayMode run and the whole run died with
  `Test run completed. Exiting with code 3 (RunError). Scripts had compilation errors.` Eighteen
  minutes for nothing, and the log says it near the top where nobody looks. Docs and `Content/`
  are safe to edit mid-run; source is not. Queue the edit, or accept losing the run.
- **Core bans transcendentals** for replay determinism. `Daylight` embeds a 365-entry table rather
  than call six of them. Linear interpolation and integer hashing only — no `Math.Pow` to shape a
  curve. `Math.Sqrt` is fine and always was: IEEE-754 requires it correctly rounded.
- **A ROAD AND THE RAILWAY ARE SMOOTHED BY DIFFERENT CURVES, on purpose.** `RoadPath.Smooth` is
  uniform Catmull-Rom and draws the **railway**; `RoadPath.SmoothCentripetal` is alpha=0.5 and
  draws every **road**. The uniform form treats each span between declared points as one unit of
  parameter however long it is on the ground, so where consecutive spans differ wildly it
  overshoots: the curve leaves a vertex in the wrong direction and loops back. The county's
  centrelines are chained segments and the alley mouths add a 13 m stub to a 200 m run, which is
  precisely that shape — on 2026-08-09 **nine of 68 roads left one of their own ends backwards and
  Summit Street was drawn 39 m (128 ft) off its own survey line.** Nothing could see it: it moves
  no count and fails no test. **Ruled by the owner, 2026-08-09: leave it, corners stay rounded** —
  a real street corner has a turning radius and a car cannot pivot on a point. Do not straighten
  the streets and do not smooth them further; both are decisions to re-take with him, not defects
  to chase. Do not "unify" these two curves back into one without re-rendering the rail bed, and
  do not put a road through the uniform one.
- **Do not give a Core type a name `UnityEngine` also uses:** `Light`, `Terrain`, `Object`,
  `Random`, `Debug`, `Material`, `Space`, `Bounds`, `Color`, `Camera`, `Input`, `Animation`.
- **Do not touch the witness layer's vagueness.** `PersonDescription.CarriedThing` is deliberately
  coarse — `Bag`, `Case`, `Bundle`, `LongObject` — because a witness says "something in his hand",
  not "a Nokia". That imprecision is the design, not an unfinished enum.
- **Population scales off `WorldModel.Households`, which counts *units*, not buildings.** 1,300
  people is the target and the number is load-bearing. A terrace is one building with four front
  doors; scaling anything off the building count gets it wrong.
- **`Unity_RunCommand` WORKS. This file said it was broken and that cost a whole session.**
  Verified 2026-08-08 against a live editor: it compiled and ran a `CommandScript`, found the
  cameras, read `EditorApplication.isPlaying`, walked `AgentMeshView`'s animators and moved the
  scene view. So does `Unity_GetConsoleLogs`, and so does the multi-angle scene capture — which
  photographs the real town, streets, railway and all.

  **What that means, and it is the whole point: DRIVE UNITY YOURSELF.** Open it, close it,
  recompile, enter Play, capture pictures, read state out of the running game. Do not write "please
  press Play and tell me what you see" — that is the owner doing an assistant's job, and this line
  is why it kept happening. The one thing measured as failing is capturing a *specific camera by
  id while in Play* (`Failed to render scene preview`); the multi-angle scene capture with
  `focusObjectIds` works and is the way to look at something.

  Still true: Unity only rescans scripts on focus **gain**, so already-focused plus an edit means
  nothing happens, forever. Marker file + focus-loss-then-gain, and verify the marker was consumed.
- **THE TOWN IS BUILT OF CLAPBOARD AND BRICK, AND ITS ROOFS ARE ASPHALT SHINGLE.** All of it was
  Ashcombe's until 2026-08-09: pale English render on every wall, and slate, clay tile and THATCH
  on six hundred Illinois roofs. `Content/textures/` had not been touched since the commit that
  created it. The one line that decides whether any of it reads is `mesh.RecalculateTangents()` in
  `MeshChunks.Emit` — every pack albedo is nearly flat and all the detail is in the normal map,
  which URP builds from a tangent stream this project had never written. **The greppable gate is
  the `Surface textures:` line**, and it says what happened to each NAME rather than counting a
  cache the main path never filled — which is what it did through forty runs that read 7, seven
  that read 1, and five that read 8 or 14. Healthy, measured 2026-08-09:
  `15 of 15 pack names resolve; 15 bound from the pack, 1 from Content/textures/ (water),
  0 MISSING`. **MISSING is the failure**, and a shipped player prints its own version of that line
  because the pack path is `#if UNITY_EDITOR` and a build has no `Content/textures/` at all.
- **THE EDITOR AND THE SHIPPED GAME DREW DIFFERENT TOWNS, AND THE COLOURS SAY SO.** Every
  `Make(name, colour, …)` in `Materials3D` is a FALLBACK — overwritten the moment a texture binds
  — so nobody on this machine had ever seen one, and they had been authored back when they *were*
  the render. A player drew pale grey `0x9A9690` roads where the editor draws near-black asphalt
  `0x313131`; the roof materials were built `Color.white` and shipped as white paper. Each is now
  the **measured mean of the sheet it stands in for**, and `TileGenerator` takes the same numbers,
  so the two tiers agree by construction. `NoMaterialIsBuiltWhiteAndThenTextured` in the Core gate
  fails if that comes back. **If you change a pack set, re-measure the fallback.**
  > ⚠ **THE GROUND IS ALL GREEN GRASS NOW AND THOSE MEASURED BROWNS ARE NO LONGER REACHED.**
  > Owner's instruction 2026-08-10: `Materials3D.GrassEverywhere` (default **true**) binds the
  > grass sheet for Field, Wood, Path and the two hard zoned kinds, which drew
  > `Ground_Dirt_Stubble`, `Dirt_A` and `Ground_Dirt_Flat`. Churchyard, Rough and Pasture were
  > already grass and keep their own tiling and tint; Road, Water and Floor are untouched. The
  > measured means are still in the switch and a player still falls back to them — **so
  > re-measuring them when a pack set changes is still the rule**, it is just no longer something
  > you can check by looking at the town. Set it false to see the ground the survey measured.
- **SPEEDTREE CANNOT GO THROUGH `CityChunker`, AND FOR A LONG TIME ALL OF IT DID.** SpeedTree
  encodes leaf orientation and wind in the extra UV channels and the vertex colours;
  `Mesh.CombineMeshes` carries positions, normals, tangents and UV0 and drops the rest, so every
  baked grass tuft in Rossville was a flat grey plate lying on the lawn. `Combinable` spares
  SpeedTree renderers **whose whole prefab is under 1 m** — measured: 6,858 of them are grass,
  flowers and leaf bunches and only those came out wrong, while the 15,000 above that line are
  trees and bushes whose leaf clusters are 3D blobs and survive the combine looking like blobs.
  The city runs **8,081 renderers** (2,166 baked chunks + 5,896 tufts) against 2,177 when the
  plates were baked in and 23,408 if you spare every SpeedTree object. **The frame cost of those
  5,896 has not been measured** — that is `PerfCensus`, and it needs the editor closed.
- **`VillageHost.Seed = 1979` IS A SEED, NOT A DATE, and neither are the 1979s in the fixtures
  and in `tools/Noir.Sim`.** The year is 1991 and this file says so six lines up, so a session
  hunting era mistakes finds `1979` and reaches for it. Changing any of them reshuffles every
  citizen, prop and roof in the town. The same rule covers `Materials3D.Scatter`: **do not convert
  a position hash into an `IRng` substream** — four systems key on it, and that is what makes the
  same land come out the same every run. What IS an era mistake is a 1979 in a *comment* reasoning
  about the place: several said things like "in 1979 almost everything in a village is heated by
  something that needs a flue", which is a sentence about rural England.
- **A UTF-8 BOM stops `^\s*#` matching the first line**, so `grep -vcE '^\s*$|^\s*#'` over-counts
  a `Content/` file by one. Several of these files have one.
- **`kinds.txt` resolves a kind through its `words` line, not its `kind` line.** Renaming the
  `kind` and leaving the `words` makes every map fail to parse. And the `frontage`/`massing`/
  `grammar` values are keys into `Frontage.cs` and `MassingGrammars.cs` — a value nothing answers
  to does **not** throw, it falls through to a default and the building just looks wrong.
  **SmokeTest does that diff now** and fails the run on a word nothing draws — `kinds  N frontage
  and M massing key(s) declared, all answered`. It was written because the rule above had been
  stated here for months and nothing checked it: `bank` and `icecream` declared
  `frontage shopfront`, a word no arm answered to, so the bank wore the same anonymous plank as
  seven apartment blocks; and `waterworks` and `publicworks` declared `massing shed`, a grammar
  that has never existed, so the pump house rendered as a cottage.

---

## What does not exist yet

Worth knowing before planning against it, because more than one document has described these as
though they were built:

- **The dialogue LLM is not in the code.** `Assets/LLMUnity` is vendored, but there is no `ILLM`
  port, no Anthropic client and no dialogue system anywhere in the tree. It is scoped, not built.
- **There is no save or replay system.** Nothing in Core or `tools/` serialises anything.
- **The game has no name.** Banked: PATTERN OF LIFE · SIGNATURE · CREATURE OF HABIT ·
  HE KEPT TO HIMSELF · SMALL HOURS. Working title NIGHT WORK.

---

## The documents

**Live — keep these true:**

- `CLAUDE.md` (this file) — the entry point and the load-bearing facts
- `docs/SOURCES-OF-TRUTH.md` — what outranks what, and the owner's standing facts
- `docs/research/THE-ERA.md` — the year, and what building to it settles
- `docs/research/README.md` — the index of the research documents; read it before any of them
- `docs/IDEAS.md` — the backlog

- `docs/ASSETS.md`, `docs/ASSET-GAPS.md`, `docs/PACK.md` — what the packs hold and what is missing
- `docs/CONTROLS.md` — the keys, mirroring the in-game **H** panel

**Archived — `docs/history/`.** The eleven dated `HANDOFF-*`, `OVERNIGHT-*`, `LESSONS-*` and
`POSTMORTEM-*` records were moved there on 2026-08-08, and `docs/STATE.md` was deleted outright.
Read them as case studies; take no command, count or path from one. See `docs/history/README.md`.

`docs/code-review-2026-08-07.html` is a deep multi-agent review of the whole project — read it if
you are wondering what to work on.

**Adding to this file means replacing something in it.** These docs went from eleven to zero
because they only ever grew. If a session learns something still true tomorrow, it edits the
sentence here that was wrong. If it is not true tomorrow, it does not go in the repo.

---

## Standing rules

- **Imperial units when writing to the owner.** Feet and miles. The files and code stay metric.
- **Real names never leave this machine.** They may be researched locally for design; nothing
  ships with one and none is committed. Business, street and trade names are public and fine.
- **Headless runs must be silent.** Batch mode has blared game audio into the owner's headphones.
  If you hear it, suspect your own process first.
- **Do not open a browser at him.** Verify from logs, files and tests.
- **Drive Unity yourself; do not ask him to press Play.** Automate editor work in editor scripts,
  and read the `Unity_RunCommand` trap above for what actually works.
- **Push at the end of every session.** On 2026-08-07 the branch had never been pushed at all and
  `origin/main` was 166 commits behind, with the one irreplaceable file existing on a single disk.
- **Never `git add -A` or `git add .`.** Stage only what you edited. The tree carries unrelated
  dirty files, including `docs/snapshots/**`, which every render run rewrites.
- **Batch the Unity loop.** One render, look at everything, fix everything, one render. A Unity
  headless render or PlayMode run takes 5–15 minutes; a session that round-trips it per question
  is the single largest cost in this project.
- **Do not run workflows or `ultracode` unless asked for by name.** They spawn agent fleets.
