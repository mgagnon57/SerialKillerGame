# Road and alley fixes — the work list

> ## ✅ W0 HAS LANDED — 2026-08-08. The generator is disarmed.
> `python tools/build-roads.py` with no flag is now a **dry run**: it prints what it would write,
> writes nothing, and says so. `--write` takes a backup through `sv.backup` (which refuses to
> overwrite an earlier one) and then writes. Verified: `git status --porcelain` is byte-identical
> across a no-flag run.
>
> **Two things the audit did not know, both found by running it:**
> 1. The generator produces **61 roads**; `Content/roads.txt` currently holds **66 road runs**. So a
>    `--write` would drop five roads as well as the twelve `aadt` counts. Do not treat `--write` as
>    "regenerate and carry on" until that gap is understood.
> 2. The audit's "no backup exists" was already refuted twice over: `Content/roads.txt.before-aadt`
>    is on disk, and the twelve `aadt` lines are clean in HEAD.
>
> The warning below is kept for the record of what it was like.

> ## ⛔ BEFORE YOU TOUCH ANYTHING *(historical — fixed by W0)*
> **Do not run `python tools/build-roads.py` for any reason until W0 has landed.**
> Line 591 opens `Content/roads.txt` with mode `w` unconditionally, takes no backup, and the writer
> emits no `aadt` line. One dry run to check a hypothesis silently reverts commits `bd6d451` and
> `85029b2` and deletes the twelve IDOT counts that fixed the starving junction. It is recoverable
> from git — but you will not know it happened. This is the single most expensive mistake available
> in this estate, and W0 is three lines that close it.

**This is a work file. Delete it when the last item lands.** A read-only audit on 2026-08-08 found
173 verified faults in the road and alley estate; a planning pass turned them into 148 items across
ten waves. Nothing here is a fact about the project — the facts live in `CLAUDE.md`. This is a queue.

**How to use it.** Work in waves. A wave is a batch of edits ending in ONE verification pass, because
a PlayMode run costs 6–15 minutes, a headless render 5–15, and **the editor must be closed for any
`Unity.exe` run** while the owner's normal state is editor-open. Waves W0, W1 and W4 need no Unity at
all — land those while he is working. Do not verify per item. Do not reorder inside a wave without
reading [Hot files](#hot-files).

**Before W1:** answer the twelve questions in [Decisions](#decisions). Four of them block work.

**Item IDs** by area: `TRUST` survey trust and the generator · `JUNC` the junction model · `ALLEY`
alleys · `GATE` tests and tooling · `RC` rendering and collision · `ADDR` addresses and attachments ·
`CONS` traffic and pathfinding · `SALVAGE` discarded survey data and documents.

---

## The standing gates

| Gate | Command | Today |
|---|---|---|
| Core | `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj` | 428 pass, ~2 min |
| Unity compiles | `dotnet build Noir.Unity.csproj -c Debug` **and** `Noir.Editor.csproj -c Debug` | clean |
| PlayMode | `-runTests -testPlatform PlayMode -assemblyNames Noir.PlayTests -testCategory "!Diagnostic"` | 13 of 13, budget **ten** min |
| Smoke | `-executeMethod Noir.Editor.SmokeTest.Run` | ~3 min |
| Map audit | `-executeMethod Noir.Editor.MapAudit.Run` | exit 0 today |
| Player | `-executeMethod Noir.Editor.BuildPlayer.Windows64` | exit 0 |

**The Editor build is not optional.** `Noir.Editor.csproj` is the one that catches the `SurveyRoads`
signature change; the items' own verification steps build only `Noir.Unity` and would miss it.

**`NOIR_BUILT_TOWN=1` or W8 verifies nothing.** `VillageHost.cs:326-327` reads the built-town
preference only when `!Application.isBatchMode`, so `CityStreets`, `CityAlleys`, `CityParking` and
`CitySigns` are **never built in a headless run**. `play3.log:1945` records it: *"7 of 20 switches
have nothing behind them."* A green default gate run has seen nothing of the street layer.

---

## Decisions

Four block work: **#1 (408 Holmes)**, **#2 (the addresses)**, **#3 (the smoothing)** and **#10 (the
escape hatch)**. Answer #1 before #2 — the anchor should constrain the system, not the reverse.

### 1. 408 Holmes Ave — **blocks W9, and take it first**

Vermilion County's own address records put 408 Holmes at parcel 673, **on the other side of the
street and a block away** from where the game puts the killer's house. And it is not isolated: of 202
lots where the county has an address on file, the authored address agrees on **zero**. The parity is
inverted (county puts even numbers north, the game puts them south) and every hundred block east of
Route 1 is one too high.

- **(a)** The county wins everywhere — 408 Holmes moves about 250 ft east, to the other side.
- **(b)** The house wins; the county's convention governs the other 201, with 408 a recorded exception.
- **(c)** The house wins **and sets the rule** — adopt whichever parity and block scheme reproduces
  408 on the lot it already occupies, and let the other 201 follow.
- **(d)** Change nothing; record the divergence and move on.

**Recommend (c).** It is the project's fixed story anchor — the first thing in the game that is a
fact rather than a measurement. There is a real chance (c) and (a) are the same answer: if the
county's inverted parity happens to put 408 on the side of Holmes the house already stands on, you
get the county's rigour and the anchor for free, and the whole thing becomes a one-line rule instead
of 202 exceptions. **That is worth ten minutes of measuring before you answer.** What (c) buys that
(a) does not is that no future session can move the killer's front door by regenerating a file.

### 2. The other addresses — **blocks W9**

Nineteen numbered houses stand on **Earl Court, a street you ruled out of 1991 entirely**, and
another 47–56 stand past the end of the street they are addressed on.

- **(a)** Delete the Earl Court nineteen; re-address the rest to the nearest surviving street.
- **(b)** Re-address all of them, keep every house.
- **(c)** Extend the streets so the addresses become true.

**Recommend (a).** Houses on a street that did not exist are not houses — deleting them honours your
own ruling rather than losing content. The other 47–56 are a numbering error, not a survey error.
(c) invents street the county does not have.

### 3. The drawn road does not follow the road — **blocks W6**

Every centreline is smoothed with a uniform Catmull-Rom, which throws **Summit Street's tarmac up to
129 ft off its own right of way** — into the back gardens — Grove 46 ft and Benton 44 ft. It also
manufactures a phantom junction 103 ft off Summit's true line that cuts Benton Street in two. Six of
the seventeen T junctions the model "misses" are within 2 ft of the surveyed line and only look
missed because the smoothing moved the road.

- **(a)** Keep the smoothing; accept Summit drawn 129 ft from where the county says it is.
- **(b)** Smooth only where the county's points are close together; straight between them where far apart.
- **(c)** Draw straight between the county's points, and round a corner **only where you have ruled
  there was a curve**.

**Recommend (c)** — it is exactly what you already ruled for Harrison's corner at Benton (*"fitted as
two straight legs meeting at the junction, NOT smoothed; it is a street corner and rounding it off
would be inventing a curve"*), and the same argument applies to every other street. A 129 ft error is
a house-and-a-half wide: the difference between the road going past your front door and through your
kitchen. Answer early — it blocks the alley work, the junction snapping and the door placement.
*(A measured middle option exists: centripetal Catmull-Rom halves the worst divergence to 55 ft. Not
enough on its own.)*

### 4. Holmes Avenue's hole

A **115 ft hole**, about 52 ft east of the killer's front door — the county's centreline stops and
restarts with nothing in the gap. Was there a break in Holmes in 1991?

- **(a)** No break — join the two ends; it is a digitising gap. · **(b)** There WAS something (a
private drive, a lot never cut through) — leave it and record why. · **(c)** Decide later.

**Recommend (a) unless you remember otherwise.** A car cannot currently drive past the killer's house.
But you are the authority and (b) costs nothing.

### 5. Where did Rossville park its cars?

Every parked car stands about **43 ft out from its own front wall**, in the middle of the lawn, with
no driveway drawn under it. You have said *"most alleys were only used for trash pick up or parking
their cars in the garage that faced the alley."* The survey says 55% of 250 outbuildings sit nearer
the alley than the street — which is not the same statement.

- **(a)** 70–80% kept the car in a garage off the back lane; the rest on a front drive.
- **(b)** About half and half. · **(c)** Mostly front driveways. · **(d)** Give a number.

**Recommend (a) at ~70%.** This decides where 611 cars stand and it is one of the most visible things
in the game. Be specific rather than letting the code pick.

### 6. Speed limits

The only number the county measured is **55 mph**, and it sits on the stretch of Route 1 the generator
drops. It says nothing about Route 1 downtown, between two schools.

- **(a)** 30 in town, 55 on the approaches, 20 in school zones. · **(b)** 25 in town, 55 approaches.
- **(c)** 30 everywhere in the village, no school zone. · **(d)** Post nothing.

**Recommend (a).** Whatever you pick, the county's 55 must **not** be carried through faithfully — it
would post 55 mph on the north approach and nothing anywhere else, which is worse than guessing.

### 7. Street or Avenue?

`city.txt` calls twelve of fourteen streets "Ave". The county calls the same twelve "St", and has the
correct suffix for all 28 named roads. *(The audit had this backwards — it is the authored file that
says Ave, not the label code that says St.)*

- **(a)** Adopt the county's suffixes wholesale. · **(b)** Keep the authored names. · **(c)** County
except where you remember otherwise, road by road.

**Recommend (a)**, or (c) if a specific one reads wrong. The county is a better witness to this than
anyone's memory of a street sign, it costs one generator field, and it kills a hand-maintained table
that will otherwise drift.

### 8. Road surface

Nothing in the model knows whether a road was asphalt, oil-and-chip or gravel. The county's file is
null on all 146 segments, so this can only come from you. `city.txt` already asserts by hand that an
alley is *"about ten feet of gravel."*

- **(a)** Route 1 and downtown asphalt, residential oil-and-chip, alleys gravel. · **(b)** All streets
asphalt, alleys gravel. · **(c)** Rule it road by road in the browser map (~28 rulings). · **(d)** Do
not model surface.

**Recommend (a) as the default with (c) for corrections.** ⚠ One warning: adding ~28 surface rulings
to a file holding 47 records **trips the browser map's own 10% loss guard** — that must be fixed in
the same change or your map stops being able to save anything at all.

### 9. Does an alley through a garage count as a road through a building?

Eight to ten alleys pass through a surveyed primary building; nineteen to twenty if outbuildings
count. The test file already records the counter-rule: *"a garage on the alley line is Rossville."*

- **(a)** Only primary buildings count. · **(b)** Everything counts. · **(c)** Outbuildings above some size.

**Recommend (a).** Matches the note already in the code, and sets a known-bad list for good.

### 10. The escape hatch — **blocks W2**

Today the only way to deliberately go back to the authored roads is to **delete the file**, which is
indistinguishable from the failure. Both look identical in every log, test and render.

- **(a)** An explicit rename: `Content/roads.txt.off` means "I meant this"; a missing `roads.txt` is a
  **loud refusal**. · **(b)** A command-line switch. · **(c)** Keep delete-to-revert, make the log an error.

**Recommend (a).** The escape hatch and the failure become different events with different names,
which is the entire point, and it costs one file-exists check. (c) leaves them the same shape.

### 11. The traffic fleet

IDOT puts ~19 vehicles moving at an average moment and 46 at peak; the game runs 159, flat. The trap:
the sim starts at noon, off-peak, so an honest curve gives every test ~19 cars and the signals test
may have nothing to watch.

- **(a)** Build the curve now, move the tests to a busier hour. · **(b)** Curve plus a test-only
override. · **(c)** Leave 159 until the junction and lane work settles.

**Recommend (c) for this plan, then (a).** The junction model, lane sides and give-way rules are all
in flight; changing fleet size mid-plan makes every number unattributable.

### 12. The road checkers

Four Python checkers exist and nobody runs them. One produces the headline accuracy number — 9.0%
before, 1.6% after — and nothing else can compute it.

- **(a)** One entry point a session will type, in the research index. · **(b)** Document only.
- **(c)** Port the 1.6% into the C# suite as a ratcheted number.

**Recommend (a) plus (c) for the 1.6% specifically.** The per-road test this plan adds does **not**
cover it — that asks "is any single road more than a third on private land", and the worst road sits
at 20%, so the whole network could degrade from 1.6% to far worse and stay green on every road.

---

## The waves

### W0 — Disarm the generator · **DONE 2026-08-08**

> All five landed. Verified against the gates this wave set for itself:
>
> - **No-flag run writes nothing** — `git status --porcelain` identical before and after.
> - **`check-roads.py` prints both percentages**: `Content/city.txt 9.0%` and
>   `Content/roads.txt 1.6%`, the second labelled as the one the game actually draws. Taken in the
>   **non-renaming** form, so `derive-alleys.py:305` and `build-roads.py:48` still import
>   `city_roads` by name and the alley generator three waves depend on is untouched.
> - **SALVAGE-1**: `roads_and_places()`'s return is used now. `road_scores` was being fed an empty
>   list and reporting the result as though it had measured something; it computes
>   `old_pct=9.0, new_pct=1.6` from the real arrays.
> - **TRUST-12**: no orphan `.meta` files remain — the three named were already committed with W3
>   of the animation plan earlier today, individually, never `git add -A`.
>
> **The `--write` path is disarmed but NOT proven.** Its own gate — "`--write` reproduces today's
> `roads.txt` byte for byte" — was deliberately not run, because the dry run reports 61 roads
> against the file's 66 and it emits no `aadt`, so that gate would have FAILED and taken the twelve
> IDOT counts with it. Establish where the five roads went before anybody runs `--write`.

### W0 — Disarm the generator · **the first commit** · ~1 h, no Unity

`TRUST-14` · `TRUST-12` · `GATE-11` · `TRUST-11` · `SALVAGE-1`

Every later wave runs or re-runs `build-roads.py`. Three lines convert every subsequent verification
in this plan from destructive to safe, so **nothing may be scheduled ahead of it**.

**TRUST-14 — copy the idiom its own sibling already uses.** `build-road-blocks.py:26` has
`WRITE = "--write" in sys.argv`. Do the same: no flag prints a dry-run summary and writes nothing; a
path argument writes elsewhere; `--write` backs up first (via the `sv.backup` helper `build-roads.py`
can already load) and then writes. Gate `tools/roads-proposed.json` behind the same flag — it is the
browser map's copy and must not be refreshed by a dry run. Then update `CLAUDE.md:290` to
`python tools/build-roads.py --write` with the caveat the `parcel-1991.txt` row already carries.

**GATE-11 + TRUST-11 are the same fix** — point `check-roads.py` at `roads.txt`. Take the
**non-renaming** form: `def city_roads(fname="city.txt")` plus `srv = city_roads("roads.txt")` in
main. Do **not** rename it to `village_roads` — `derive-alleys.py:305` and `build-roads.py:48` import
that symbol by name, and the rename raises `AttributeError` in the alley generator three waves depend
on. `roads.txt` uses city.txt's exact road syntax, so the existing parser reads it for free.

**SALVAGE-1** is three tokens at `build-viewer-data.py:119-130`: `roads_and_places()` returns `r, pl`
and the next line takes neither, so the viewer's whole "show the ruled lines the survey replaces"
comparison is computed and dropped, and `viewer-stats.py`'s already-working `road_scores` is fed an
empty list. **TRUST-12** stages three orphan `.meta` files — named individually, never `git add -A`.

> **Gates.** No-flag run writes nothing (`git status --porcelain` empty). `--write` reproduces today's
> `roads.txt` byte for byte — that is the proof the flag changed nothing else. `check-roads.py` prints
> **both** percentages, 9.0% and 1.6%. `derive-alleys.py` still imports `city_roads`. Core still 428.
>
> **Carry this correction in the commit message:** the audit's "no backup exists" is refuted twice —
> `roads.txt.before-aadt` is on disk *and* the twelve `aadt` lines are clean in HEAD. Write
> *"recoverable from git, invisible at the time."* Then **push**.

### W1 — One pass over the generator · ~5 h, no Unity

`TRUST-4` `TRUST-5`/`ALLEY-0` `TRUST-7` `TRUST-10` `TRUST-6` `GATE-9` `GATE-16` `ALLEY-6` `ALLEY-19`
`ALLEY-4` `ALLEY-16` `SALVAGE-2,4,5,10-18,21,22`

`build-roads.py` is named by eleven items across four clusters, mostly inside one function. Line
numbers move under every edit, so it gets **one owner in one session**.

- **TRUST-4** — give the IDOT counts a committed source (`tools/rossville-aadt.txt`) the generator
  reads, instead of a hand edit the generator eats.
- **TRUST-5 and ALLEY-0 are the same item.** One alley-name registry, frozen from today's ranking,
  which neither generator rewrites. ⚠ **Take the corrected matching rule**: match by distance to the
  derived **polyline**, not to its midpoint. All 11 stitch joins land 39.5–92.5 m from their parent's
  midpoint — outside the proposed 30 m radius — so the midpoint rule orphans all 22 parent names it
  exists to protect.
- **TRUST-10** ⚠ goes in at **:547 where `note` is assembled**, not at the :586 easement branch. The
  proposed placement writes the annotation once per polyline, so 56 of 61 roads get nothing and the
  five two-run names leak the first run's note onto the second line.
- **ALLEY-4** ⚠ needs a **separate width-ordered stroking pass**, not an `OrderBy` on the loop that
  also builds `lines`. `RoadGeometryBaselineTests.SegmentChecksum` hashes the *index* into
  `roads.Lines`; reordering moves the checksum, moves `LaneGraph.Entries` indices (so every
  `Materials3D.Scatter` draw lands elsewhere) and moves `CitySigns.Nearest`'s tie-break. Core contains
  no `using System.Linq` anywhere — do not make this the first.
- The Core gates (**TRUST-6, GATE-9, GATE-16, ALLEY-6**) land here because they are what proves the
  generator is now faithful. GATE-9 replaces two tautologies the parser already enforces by throwing.

> **Gates.** The dry-run diff shows **only** intended lines — that no-op is the deliverable. `grep -c
> '^  aadt'` returns 12 after `--write`; all 31 alley names reproduce identically. Core 428 → the
> **measured** number (expect ~436 — paste the run's, not a prediction). Re-run `build-viewer-data.py`
> so the browser map matches, and **say it is ready — do not open it at him**. One `CLAUDE.md` edit at
> the end of the wave.

### W2 — Silence becomes audible, and the bake stops lying · ~6 h, ~55 min editor-closed

`TRUST-1,2,3,8,9,13` · `GATE-4,6,10,13,17` · `RC-1,2,3,4,14` · `ADDR-17` · `SALVAGE-3,19,20`

The one editor-closed window for the first half of the plan. Everything here verifies from three
SmokeTests, one PlayMode run and one player build.

- **TRUST-1** ⚠ **must be the one-argument struct-return form.** `WalkableProbe.cs:41,42,45,49` call
  `Apply` as bare invocation lambdas; `out int installed` is a CS7036 at all four sites and takes
  `Noir.Editor.csproj` down — which the item's own verification (Unity only) would not catch.
- **TRUST-9 before TRUST-8** (the stated dependency arrow is backwards), and ⚠ TRUST-8's check goes
  **after** the census or forces `Load()` in the getter — as written, `LoadProblem` is null on the
  first build in a fresh domain, so it never fires on the run it is written for and *does* fire on
  later builds, reading as flaky.
- **TRUST-3 and GATE-4 are the same assertion.** Write one test, with GATE-4's negative proof
  (`roads.txt` renamed aside) riding the same run.
- **RC-1 must land first and alone** in the draw estate: bake in root-local space. Until then every
  still is 12 cm out and **any visual judgement made before it is void** — which is why it is pulled
  forward ahead of W5's first look-at-it render. RC-2 adds the guard that the bake never moves
  geometry silently.
- **RC-3** stops the bake destroying the wall colliders (a `DoNotBake` marker) and fixes the log line
  that reports them as present. **RC-4** gives the player a spawn that asks the map honestly — using
  the county's counts to find the busiest road — and gives `ThePlayerCanStandInTheStreet` a real
  assertion for the first time.
- **GATE-17's leave-it is overturned.** ⚠ `TownGeometryPlayTests.cs:227-228` scans `report.Errors` for
  a string `WorldValidator.cs:173` puts in `report.Warnings`, so the assertion **cannot fail for any
  town, ever** — and `play3.log:1520` already recorded the town in three pieces on a green run. Assert
  on `report.RegionCount`, a number, and re-record it from a run.

> **Gates.** Both assemblies compile — **the Editor build is mandatory**. `MeshReadable.Enable` first
> or the log drowns in 4,893 Read/Write warnings. **SmokeTest three times in the one window**: normal;
> `roads.txt` aside; `roads-1991.txt` aside — three different log lines, not one silence. PlayMode
> 13 → the measured count, `CLAUDE.md` edited in the same commit with the wall clock beside it. A
> Windows64 build boots.
>
> **Two printed numbers move with no fault behind them:** the smoke ruling line 38 → 48, and
> `RuledAway` now logs on every run. Put both in the commit message or the next session hunts a
> regression that is not there.

### W3 — A junction becomes a node · ~8 h + one 15-min and one 10-min Unity window

`JUNC-3` `JUNC-1` `JUNC-2` `JUNC-7` `JUNC-5`(step 1 only) `JUNC-6` `JUNC-11,12,13` `GATE-7` `GATE-8`

One sequence, one branch, never parallel. The internal order was **measured**: the axis filter off
alone gives 3 stranded lanes; axis off plus a loose tolerance gives 11 coincident clusters and 7
stranded lanes — either lands red on `NoLaneArrivesAtAJunctionItCannotLeave`.

- **JUNC-3 opens in its own commit** because it was measured as producing *literally zero change* on
  both maps: delete the `Line` equality from the Straight case only, so a car may go straight across
  onto a road with a different name.
- **JUNC-1** makes a junction a node with arms instead of a pair. ⚠ **Cluster at `a.Reach + b.Reach`,
  not `max(HalfWidth)`** — the proposed radius leaves benton × summit (5.46 m against a 5.0 radius)
  unmerged and misses its own acceptance criterion. LaneGraph drops a piece when the arc separation is
  under reachA + reachB. Also the `Arm` struct as drafted has no constructor, so its readonly fields
  can never be assigned. Choose the merged X,Y so no arm exceeds 1 m or
  `EveryJunctionLandsOnBothOfItsOwnRoads` trips.
- **JUNC-2** drops the axis filter and pays for it with a bounding-box reject that makes junction
  finding 47% cheaper than today.
- **JUNC-5 step 1 only.** ⚠ Step 2 as written drops the car through to `Choose` at ~90 interior exit
  segments, `Choose` returns −1, and the car parks on `Hold.NoLegalTurn` permanently — a 159-car fleet
  drains into 90 cul-de-sacs over a six-minute run. Say so in the flag's own doc comment.
- **JUNC-6** ⚠ needs an explicit **2-arm case laying no tile**, excluded from the `inJunction` tests
  at **both** `CityStreets.cs:470-476` and `:542-547`, or 3550north × attica logs the crossing warning
  forever. Three renderers read `Junction.NorthSouth` as a compass direction and will draw a crossroads
  tile and a stop sign in the middle of a straight street once same-axis junctions exist.
- **GATE-7** — the fix already exists twice in this tree (commit `412f7cb` did it to `PlanLabels`, and
  `RoadCentrelines.cs:17-27` does it too): walk the `Path` instead of guarding on `IsStraight`.
- **GATE-8** ⚠ ratchets **street-class near-misses only** (3 today, clean gap to 28 m) and prints the
  55 alley near-misses unasserted. Do not ratchet the combined 58 — 55 of them are the `STREET_CLEAR`
  artefact the item itself assigns elsewhere, so the gate would pin the number it argues must not
  drive it.

> **Gates.** `RoadGeometryBaselineTests`' five constants (111/442/1088/38) and the segment checksum
> **all go red legitimately** — re-record before/after in the commit message, and note the baseline
> moves *further* than predicted (reach-sum clustering merges two coincident pairs on city.txt, not
> one). `NoRoadIsSeveredByItsOwnJunctions` = 0 — that number is what says JUNC-1 worked. JUNC-2 and
> JUNC-3 must reproduce 111/442/1088/38 exactly; that invariance is the evidence each is neutral.
> `MapAudit` exits non-zero **for the first time** — that is the fix working. Three log lines must
> agree on the junction count with no "being laid as a crossing anyway" warnings.
>
> Use the validated scratchpad replica to test a rule in ~1 minute instead of 10 minutes per Unity run.

### W4 — The Core tests move onto the town the game builds · ~6 h, no Unity

`GATE-1` `GATE-2` `ADDR-6` `GATE-3` `GATE-12` `GATE-15`(scope only)

**This wave goes deliberately red**, so it gets its own commit boundary and no Unity — a two-minute
Core run makes the red/green cycle cheap.

**GATE-2's `SurveyedTown` and ADDR-6's shared helper are the same object** — two clusters were about
to write it twice. Building it once also closes the three hand-rolled substitutions and the silent
3 m width default at `RoadCorridorTests.cs:49`. **GATE-1** puts the substitution itself in Core so the
game and the tests share one implementation.

Three corrections: **drop the LaneGraphTests move** (`LaneGraph.cs:97` makes it a straight duplicate
of `NoLaneArrivesAtAJunctionItCannotLeave`); **add `DrivewaysTests.MostOfRossvilleKeepsItsCarAtHome`
to the named-red list** — it is not on the original list, and losing ~128 buildings to the corridors
lands `share` near 0.77 against a hard 0.75 bar; **keep SurveyRoadNetworkTests' RepoRoot reader**
rather than standardising on the `bin/Debug` copy, which is measurably stale (zero `aadt` lines).

**GATE-3(a) should go green with an empty `KnownOffTheirRightOfWay`** — independently recomputed
against all 794 parcel rings: zero roads exceed 33%, worst is green at 20.0%, 3550north at 0.0%. If it
does not come out empty, **investigate the instrument — do not re-add names.**

> ⚠ **Obsolescence to settle here.** GATE-3's retargeted `NoRoadRunsThroughABuilding` largely
> duplicates `RoadCorridorTests.NoMeasuredFootprintStandsInARossvilleStreet`, which measures the same
> footprints with a *better* instrument (outline penetration, not a bounding box expanded by half a
> corridor) — **and they disagree, 10 versus 3.** Two tests answering one question with different
> geometry and different verdicts is the fault shape this audit exists to find. The call: it should
> not exist; land GATE-3 (a) and (d) only.

### W5 — RoadPath, the corridor, and the 134 · ~10 h + one 30-min Unity window

`RC-17` `MODEL-6` `MODEL-7` `GATE-5` `ADDR-2,3,4,8,18`

Blocked on **decision #3**. Tuning the alley extensions or the junction snapping before this is tuning
against an artefact. **RC-17 reverses the audit's direction** — the chord is faithful, the smoothing is
the invention — so this is an owner decision executed, not a bug fixed.

`MODEL-6` (the dead "past the end of the run" guard, so every corridor is effectively infinite) and
`MODEL-7` (corridors offset half a metre from the game's own centre lines) both move the 134 ratchet,
which is why GATE-5's re-record sits after them. **ADDR-2** puts the front-door decision in Core and
shares it with SeatOnSurvey; all three `RoadCorridor.cs` additions land in one commit before anything
calls them. Only **one** cluster may change `RoadPath.Smooth` — it is shared with the committed rail bed.

> **Gates.** `SmokeTest` green — `WorldValidator.cs:151` makes "door is cut off from the rest of the
> village" an **error**, and `VillageHost.cs:428` turns that into no town at all, so a moved door can
> delete the whole town silently. Re-record the 134 from **that** run (it reads 9 in two logs and 31 in
> a third — it is unstable).
>
> **LOOK AT IT.** ADDR-2 moves ~78% of survey-raised front doors to a different wall, turning the porch
> and gable of ~300 houses. Render and actually open the PNGs. Valid only because RC-1 landed in W2.

### W6 — The alleys reach the town, and the streets reach Route 1 · ~12 h + one 40-min window

`ALLEY-1,2,3,5,7,8,9,10,12,14,15,17` · `JUNC-4,8,9` · `CONS-4` · `RC-7,8` · `ADDR-11,15`

**This is the one `roads.txt` regeneration in the plan** and everything needing one rides it — a second
regeneration is a second chance to lose the counts. Safe only because W0 gated the writer, W1 taught it,
and W5 settled what `RoadPath` does. **Split the commit at the regeneration boundary** so a bad
regeneration is one revert.

- **ALLEY-1** — `stitch()` cannot join two fragments facing head-to-head, so eleven single alleys ship
  as twenty-two stubs. Test all four end pairings.
- **ALLEY-2** ⚠ **must be computed in `build-roads.py` after chain/simplify/clip_and_round, aimed at
  the RoadPath the shipped runs will build** — not in `derive-alleys.py` against raw county
  centrelines, which is three transforms upstream and lands only 30 of 44 mouths inside tolerance,
  missing by up to 44 ft. Aiming at the RoadPath lands 41 of 44. Round to the integer lattice (worst
  residual 0.707 m against a 1.0 m tolerance, 30% headroom).
- **ALLEY-3** ⚠ has **five call sites, not three.** All 33 street runs are curved, so every one goes
  through `LayCurved`, which has its own `inJunction` at `:542-547` and its own `atCrossing` at
  `:551-565`. The named three sit on a path no Rossville street takes — the fix would be delivered for
  zero streets.
- **CONS-4 lands before ALLEY-5**: today the alley exemption hides any car `Reintroduce` dropped in a
  back lane, and fixing the width first exposes that population to `NoVehicleEverLeavesTheRoad`.
- Three duplicate pairs — write each once: **ALLEY-5 = RC-7**, **ALLEY-9 = RC-8**, **CONS-4 = ALLEY-12**.

> **Gates.** ALLEY-6's committed checker **gates the write** — the mouth count is proved before the
> file is written, not predicted after. 12 `aadt` and 31 alley names still present.
> `SurveyRoadNetworkTests.cs:165`'s `InRange(40, 90)` widens to the **measured** value (expect
> ~104–116). Watch the SmokeTest's `stuck` count: a rise means a house is standing in a new alley
> mouth and **the extension must be refused there, not forced**. `[streets] Alley:` reports 2.0 m, not
> 3.55 m. Re-record the 134 again.
>
> ⚠ Eight alley mouths land on a street of the **same axis** and make no junction until JUNC-1 lands —
> so W3 before W6, and expect those eight.

### W7 — The traffic model · ~10 h + one 25-min window

`CONS-1,2,3,5,6,7,9,10,11,12,13,14,15` · `CONS-8`(docstring only) · `SALVAGE-8` · `GATE-18`

**CONS-7 lands first with its BEFORE numbers recorded** — it converts a ten-minute PlayMode round trip
into a two-minute headless run for the two biggest fixes here. Then **CONS-2** (a path-frame normal
multiplied by a coordinate-frame sign, putting cars in the oncoming carriageway on 25 of the 60 bending
roads, Route 1 among them) — hoist the flip into Core where the 428-test suite can reach it.

⚠ **CONS-1 and CONS-3 are one commit and must never be split.** Give-way is currently off, which is
what makes the arbitrary axis tie-break free; turning give-way on alone recreates the starving-junction
shape that `CitySignals.cs:249-256` records as producing 119.9 s waits.

The wave comes after W3 and W6 for a hard reason: junction topology changes move the junction count
from 74, the turns from 630 and the conflict pairs from 2,393 in one step, and **a p90 that moves in a
run containing both is unattributable.**

> **Gates. The missing gate is the deliverable:** CONS-2's new test and CONS-7's side check must both
> be **seen to fail on the unfixed tree first**. Nothing today can fail on a car in the oncoming
> carriageway. A fix whose gate has never gone red proves nothing.
>
> The p90 gate swings **21.9 s to 37.2 s on an unchanged tree**. Run it twice before believing a
> traffic number moved, and never in the same run as a topology change.

### W8 — The street layer is actually drawn · ~12 h + two windows, ~45 min

`RC-5,6,9,10,11,12,13,15,16,18,19,20,21,22` · `ADDR-9,10,11,13` · `ALLEY-11,13,18` · `SALVAGE-5`

**The gate is blind to this whole wave by default** — set `NOIR_BUILT_TOWN=1` or the run has seen
nothing. `CityStreets.cs` is edited by seven items and RC-5/RC-6 rewrite the same two loops, so it is
**one sequential pass in this order**: RC-6 (unify `Lay` and `LayCurved` into one interval tiler) →
RC-5 (Verge from the rulings, on the new walker) → RC-12 (pitch, needs RC-6's variable step) → RC-14/15.

`CitySigns` and `CityParking` are lazy, so no committed log carries a `[signs]` or `[parking]` line —
use `LayerShot.All`, which calls both Build methods eagerly.

⚠ **ALLEY-11's alley preference must not be a Beat.** `NameTable.cs:105-112` records that
`Beat.RoundAbout` was removed because it was the only beat needing a routing change and risked the
determinism guarantee to buy nothing. Use a per-person-per-day `Rolls.Chance` at the journey site —
pure, deterministic, no ordering dependency — the same shape as `DepartureOffset`.

> **Gates.** Render and **open the PNGs**: ADDR-9 goes from 2 landmark signs to dozens, RC-5 repaints
> every zebra and kerb in town. 202 m of untiled centreline goes to zero and no junction gaps on one
> arm. The town gains utility poles for the first time.

### W9 — The addresses · ~10 h + one 20-min window

`ADDR-0,1,5,7,12,14` · `SALVAGE-6,7,9` · `GATE-15`(only if a threshold is set)

Last, because it is **entirely owner-ruled** and upstream of four findings whose corrections must not
be lost. **ADDR-1 (408 Holmes) is taken first and constrains ADDR-0**, not the other way round.

⚠ `serve-viewer.py`'s `write_walks` is touched by two items and must be **touched exactly once**. It
rewrites `Content/roads-1991.txt` wholesale from a dict holding four verb kinds, so any verb it does
not know is dropped on the next save — and the cliff guard at `:484` raises `RuntimeError` once new
lines exceed 10% of the file's 47 records. About 28 surface rulings makes every walk-save look like a
37% loss and **his browser map stops being able to save anything at all.**

---

## Hot files

| File | Items | Order |
|---|---|---|
| `tools/build-roads.py` | 11+ | TRUST-14 alone in W0, then **all the rest in one W1 session by one owner** — line numbers move under every edit |
| `Content/roads.txt` | 16 | W0 gates the writer, W1 teaches it, **W6 is the one regeneration** and everything needing one rides it |
| `SurveyRoads.cs` | TRUST-1,2,3 + SALVAGE-18 | One rewrite in W2. **Signature is decided: `Apply(VillageLayout)` returning a struct** |
| `RoadNetwork.cs` | JUNC-1,2,12,13 · ALLEY-16 · SALVAGE-5,8 · MODEL-5,6,7,9 | Docstrings + Label in W1; the junction sequence in W3 as one branch; corridor work in W5. **Never parallel branches** |
| `CityStreets.cs` | 11 | JUNC-6 in W3 → ALLEY-5,17,3 in W6 → the RC pass in W8 as one sequential edit |
| `TownGeometryPlayTests.cs` | 6 | Additions + GATE-17 in W2; the 134 re-recorded in W5 **after** MODEL-6/7, and **again** in W6 |
| `CityTraffic.cs` | CONS-1,2,4,7 · JUNC-5 · MODEL-4 | Three regions, no textual overlap, but **committed together** — CONS-2 moves the arcs CONS-1 measures |
| `CityChunker.cs` | RC-1,2,3 | **RC-1 first and alone.** Until the bake is root-local every visual judgement is void |
| `CLAUDE.md` | 9 | **One edit per wave, at the end**, in the commit that moves the number. Its own preamble is about six documents disagreeing on a baseline — do not create a seventh |

**Same-item duplicates across clusters — write each once:** TRUST-5 = ALLEY-0 · GATE-11 = TRUST-11 ·
GATE-2 = ADDR-6 · TRUST-3 = GATE-4 · ALLEY-5 = RC-7 · ALLEY-9 = RC-8 · CONS-4 = ALLEY-12 ·
ALLEY-16 = SALVAGE-16.

**Two clusters were about to fix the same "dead code" in opposite directions:** ALLEY-17 wants to
delete `CityStreets.VergeOffset`; CONS-5 gives it its first caller. **CONS-5 wins — do not delete.**

---

## Expect these to go red

- **W1:** Core 428 → ~436. These are *additions*, not reds — but move the `CLAUDE.md` baseline in the
  same commit or the next session reads a green run as a regression. Paste the run's number.
- **W3:** `RoadGeometryBaselineTests`' five constants and the checksum all go red legitimately, and
  further than predicted. `EveryJunctionLandsOnBothOfItsOwnRoads` may trip on merged nodes — choose the
  merged X,Y so no arm exceeds 1 m, or re-state the test in the same commit with the reason recorded.
  **Do not quietly widen the tolerance.**
- **W4 is the wave that goes red on purpose.** All four `RoadsSitOnPublicLandTests` and three
  `DrivewaysTests` go red on retargeting. Every one must be a **named** red with a re-recorded value,
  and **the session must not "fix" a test by loosening it back to green.**
- **W6:** `Junctions.Count InRange(40,90)` widens deliberately to the measured ~104–116. Record the
  number; do not widen to a range that could not fail.
- **W7:** the traffic p90 swings 21.9–37.2 s on an unchanged tree. One green run is not evidence.
- **Standing, so nobody re-derives it:** the two 2:1 aspiration tests are `[Test, Explicit,
  Category("Aspiration")]` and are **not** part of the 428. There is no standing exception any more —
  any other red is a regression. And do not close the 2:1 gap by adjusting the instrument.

---

## Do not do

- **Do not run `build-roads.py` before W0.** See the box at the top.
- **Do not loosen the 1 m junction tolerance to buy T junctions.** Measured sweep: 1.0 → 75 junctions,
  2.5 → 79, 4.0 → 79, 6.0 → 82, 8.0 → 83 — and it manufactures two coincident clusters that **sever
  Route 1 in two places**. Extend the street's end instead.
- **Do not merge continuing road runs into one `RoadLine`.** Dead for three independent reasons: it
  destroys one of two real street names that three layers key on; it contradicts the owner's ruling
  that Harrison's corner is two straight legs; and it shifts the metres-along origin of every block on
  the merged name, which `road-blocks.txt` and his own `block … was absent` rulings are keyed to.
- **Do not add an overhang step to `build-roads.py`.** The causal claim is refuted — `Touches` **is**
  implemented — and `IDEAS.md:285-289` records that a naive overhang manufactured three new sub-2 m
  lane pairs on 2026-08-07.
- **Do not flip `TileGrid.MoveCost` so Path beats Road.** Refuted on two grounds the audit did not
  check: there are no Path tiles beside any Rossville street to prefer, and `Pathfinder.cs:289-297`
  already records the **measured** finding that a multiplier this size changes no route. Flipping it
  slows every walker by 10%, moves nothing, and leaves a green suite.
- **Do not leave GATE-17.** It is the one leave-it that must not be honoured — see W2.
- **Do not route traffic randomness through `IRng`.** It is deterministic already and changing it
  reshuffles the town.
- **Do not plan fixes for these refuted findings:** SURVEY-2 (no backup) · SURVEY-5 (alley asphalt 78%
  over its easement) · ROADSRC-11 (a deleted roads.txt is green everywhere) · MODEL-9 (nine of fourteen
  callers *do* guard) · MODEL-14 (the "API with no caller" is called, and is the only assertion that
  can see where a building stands) · CONS-12's "no crossing point is recorded" (`features.txt` has four
  `crossing` lines matching the four roads that cross, no extras, no misses) · RENDER-6 · RENDER-7 ·
  RENDER-4's stated consequence · **ATTACH-14, -15, -16 and -17** — wrong or backwards, and two of them
  read as small, safe, obvious fixes that would make the town **measurably worse**.
- **Process.** Never run a Unity batch command while his editor is open (Unity takes an exclusive lock
  and the run simply fails) — check for `Unity.exe` first and **say so rather than killing his work**.
  Never `-nographics`. Never omit `-assemblyNames Noir.PlayTests`. Never run Core in Debug. Never pipe
  `dotnet test` into `tail`. Never `git add -A`. Never PowerShell `Get-Content`/`Set-Content` on
  anything under `Content/`. **Do not open the browser map at him.**

---

## Done means

- `python tools/build-roads.py` with no arguments writes nothing anywhere, and `--write` regenerates
  `roads.txt` with all 12 `aadt` lines and all 31 `alleyN` names intact.
- `check-roads.py` prints 9.0% and 1.6% side by side, measured off `roads.txt`, without writing to it.
- A missing or malformed `roads.txt` produces a **loud, named refusal** on every path, and deliberately
  reverting is a different, differently-named event.
- Core is green at its new stated number, that number is in `CLAUDE.md`, and **every test that went red
  on the way was named in advance** with its re-recorded value in the commit. No test was loosened.
- PlayMode is green at its new count, and the built town **asserts** it carries the survey network
  rather than merely logging it in three places.
- `NoRoadIsSeveredByItsOwnJunctions` reports zero. **Benton Street is one street.**
- **Every alley in Rossville reaches a street**, proved by a committed non-destructive checker that
  *gates the generator's write*. The number today is zero of 62 ends.
- Thirteen streets that stop 2–24 ft short of Route 1 now meet it, and **Route 1 is still one road**.
- `KnownOffTheirRightOfWay` is the **empty set** and the test passes with it empty.
- The Core fixtures build the town the game builds: one helper, one file-resolution path, **zero
  hand-rolled substitutions anywhere** (provable by grep).
- Every pinned number was measured against the tree it guards, re-recorded in the same commit as the
  change that moved it.
- **No car drives on the wrong side of the road**, and the test that says so was seen to fail first.
- A still of Chicago × Park/Maple, Chicago × Stufflebeam/Stewart and Benton × Harrison has been
  rendered **after** the bake went root-local, and **opened and looked at**.
- The browser map has been regenerated and **reported as ready — not opened at him** — and it can
  still save, with the cliff guard verified against every new ruling verb.
- Every owner decision above has an answer recorded in `docs/SOURCES-OF-TRUTH.md`, and **408 Holmes
  Ave stands where he says it stands.**
- The branch is **pushed**.
