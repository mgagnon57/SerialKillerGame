# Road and alley fixes — the work list

> ## ✅ W0 HAS LANDED — 2026-08-08. The generator is disarmed.
> `python tools/build-roads.py` with no flag is now a **dry run**: it prints what it would write,
> writes nothing, and says so. `--write` takes a backup through `sv.backup` (which refuses to
> overwrite an earlier one) and then writes. Verified: `git status --porcelain` is byte-identical
> across a no-flag run.
>
> **The generator is faithful, and TRUST-4 closed the one real hole.** `tools/rossville-aadt.txt`
> now holds the twelve IDOT counts and the generator writes them, so a regeneration carries them
> instead of eating them. Proven by writing to a scratch path and diffing against the file in use:
> **24 lines differ and every one is an `aadt` line changing position** (the generator emits it
> after `easement`; the hand edit put it before). Nothing else moves.
>
> **A CORRECTION TO WHAT THIS BLOCK SAID FOR AN HOUR.** It claimed a `--write` would drop five
> roads, because the generator reports "61 roads" against 66 `road ` lines in the file. Those count
> different things: 61 is DISTINCT NAMES, 66 is RUNS, and Summit, Grove, Green, Harrison and Holmes
> each arrive as two. Both files have 61 names and 66 runs. There were never five missing roads —
> it was my own counting error, published in a commit message before it was checked. The dry run
> compares runs with runs now and says so.
>
> The audit's "no backup exists" was also already refuted twice over: `Content/roads.txt.before-aadt`
> is on disk, and the twelve `aadt` lines are clean in HEAD.
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

## Where this stands, 2026-08-10

The wave headers below are **stale relative to what landed on 2026-08-09**, which was the largest
day the road network has ever had — the alley mouths, the axis filter, the smoothing curve, what
counts as two roads touching, and the IDOT counts. Checked directly rather than trusted:

| | |
|---|---|
| **W0** | ✅ done |
| **W1** | ✅ its own gates pass: `grep -c '^  aadt'` returns **12**, `tools/rossville-aadt.txt` and `tools/rossville-alley-names.txt` both exist and are read by the generator |
| **ALLEY-2** | ✅ landed — 58 alley mouths |
| **DOOR-1** | ✅ fixed, gated at zero |
| **SPLINE-1** | ✅ fixed — **and see ALLEY-2b: it fixed 39 m of it and left 16.7 m** |
| **ALLEY-2b** | **half done, and the other half is not this item.** Two of four carried out; the other two are the smoothing, not a missing junction. Measured, gated, and now a question for the owner |
| **W4** | ✅ `RealRossville` is GATE-2/ADDR-6's shared helper. **GATE-3(a) settled: the instrument was the fault** — the sampler took exactly fifteen samples per road whatever its length, so attica sat on the list at 5/15 = 33.3%. Stepped at a flat 4 m it is 125/394 = 31.7% and the list shrank to three |
| **W5** | ✅ **unblocked** — decision 3 is answered (a), not (c), so `RC-17` is overruled. **GATE-5 re-recorded: the 134 is 28**, from five identical runs |
| **W6** | ✅ its alley items are in — 33 alley names with **zero** shipping as multiple runs (ALLEY-1), 58 mouths opened (ALLEY-2). `JUNC-4/8/9`, `CONS-4`, `RC-7/8`, `ADDR-11/15` not verified |
| **W7** | **CONS-2 and CONS-3 done.** CONS-2's gate was watched failing first: 5,438 of 13,154 lane positions in the oncoming carriageway, 41%. CONS-3: 51 of the town's 122 two-armed junctions were handed to a compass direction; the county's counts settle 32 and road length the other 19. **CONS-1 needs no work — give-way is already on.** The rest of `CONS-*` not started |
| **W8** | not started |
| **W9** | **his** — decision 1 defers it: *"do not treat 408 Holmes as settled, and do not quietly fix it in either direction — it is an edit he will make on purpose"* |
| **map audit** | ✅ no longer permanently red on the intended design — 3 kinds of fault → 2, both real |

**Measured after all of it:**

```
  Core       473 of 473
  PlayMode   19 of 19, 1 Explicit aspiration skipped, ~690 s
  map audit  20 places over a road, 1 road pair without a junction, and nothing else
  the curve  16.71 m worst (summit), 5 roads over five metres, 16 over one — ratcheted
  the 134    28, from five identical runs — ratcheted
  the lanes  0 of 13,154 in the oncoming carriageway, from 5,438
  the signs  44 north-south / 78 east-west, from 69/53 — no compass decides any of them now
  the alleys alley1 78.4% of its length on a lot — ratcheted, and nothing moves until he says so
```

**Two things wait on him and neither is a defect to chase:** whether the alleys get re-derived to
obey the back-lot-line rule now that it is enforced again, and the addresses in W9.

---

## The standing gates

| Gate | Command | Today |
|---|---|---|
| Core | `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj` | **see `CLAUDE.md` — it is the one home for this number.** This line has said 428 and 458; do not add a third. |
| Unity compiles | `dotnet build Noir.Unity.csproj -c Debug` **and** `Noir.Editor.csproj -c Debug` | clean |
| PlayMode | `-runTests -testPlatform PlayMode -assemblyNames Noir.PlayTests -testCategory "!Diagnostic"` | **see `CLAUDE.md` — it is the one home for this number.** This line said 13 of 13 and was written when that was true. Budget **ten** min, and `NOIR_BUILT_TOWN=1` if you need the dressed town |
| Smoke | `-executeMethod Noir.Editor.SmokeTest.Run` | ~3 min |
| Map audit | `-executeMethod Noir.Editor.MapAudit.Run` | **2 kinds of fault, both real, measured 2026-08-10** — see below |
| Player | `-executeMethod Noir.Editor.BuildPlayer.Windows64` | exit 0 |

**THE MAP AUDIT WAS PERMANENTLY RED ON THE INTENDED DESIGN, AND THAT MADE IT USELESS.** Its
fourth check asked `CityBuildings.Handles` — *is there a BOUGHT MODEL for this* — and called **658
of the town's 776 places** a fault for not having one. The answer is no for almost everything by
design: `CLAUDE.md` records that the pack holds two house families and both are Chicago
brownstones, so *"until there is a kit that can build an Illinois frame house, the town draws its
FOOTPRINTS instead"*. The generated massing builds all 672 of them and SmokeTest counts it doing
it. A gate that is red on the intended design teaches you to skim — the exact failure `CLAUDE.md`
records for the two permanent reds in the Core suite: *"Two permanent reds make a THIRD red easy
to miss."*

It asks whether ANYTHING can draw it now — `MassingGrammars.Knows`, the same check SmokeTest's
kinds gate uses, so the two cannot drift — and reports the bought count as a plain number rather
than a verdict. What is left is two faults and both are real:

```
  [audit] 14 buildings come from a bought model; the rest are drawn from their own massing
  [audit] ok - no buildings NOTHING can draw
  [audit] 20 x places laid over a road        'the Commercial Hotel' 2 m into alley2, and STANDS IN IT
  [audit] 1 x roads crossing without a junction   benton and summit, 8.3 m — see ALLEY-2b
  [audit] VERDICT: 2 kinds of fault
```

**The Editor build is not optional.** `Noir.Editor.csproj` is the one that catches the `SurveyRoads`
signature change; the items' own verification steps build only `Noir.Unity` and would miss it.

**`NOIR_BUILT_TOWN=1` or W8 verifies nothing.** `VillageHost.cs:326-327` reads the built-town
preference only when `!Application.isBatchMode`, so `CityStreets`, `CityAlleys`, `CityParking` and
`CitySigns` are **never built in a headless run**. `play3.log:1945` records it: *"7 of 20 switches
have nothing behind them."* A green default gate run has seen nothing of the street layer.

---

## Decisions — ANSWERED 2026-08-08, all on the documented recommendation

| | decision | answer |
|---|---|---|
| **1** | 408 Holmes Ave | **(a)** THE COUNTY WINS EVERYWHERE — owner's call, against the recommendation |
| **2** | the other addresses | **(a)** delete the Earl Court nineteen, re-address the rest |
| **3** | the drawn road vs the county's points | ~~**(c)**~~ → **(a) KEEP THE SMOOTHING**, re-ruled 2026-08-09 and again 2026-08-10 with the measurement in hand. Summit is 16.71 m = 54.8 ft, which is this file's own predicted figure for centripetal, and he ruled it IS enough. **RC-17 is overruled; W5 and W6 are not blocked on this.** |
| **4** | Holmes Avenue's hole | **(a)** join the ends — a digitising gap |
| **5** | where Rossville parked | **(c)** MOSTLY FRONT DRIVEWAYS — owner's call, against the recommendation |
| **6** | speed limits | **(a)** 30 town / 55 approaches / 20 school |
| **7** | Street or Avenue | **(a)** adopt the county's suffixes, (c) for anything that reads wrong |
| **8** | road surface | **(a)** asphalt / oil-and-chip / gravel, (c) for corrections |
| **9** | alley through a garage | **(a)** only primary buildings count |
| **10** | the escape hatch | **(a)** explicit `roads.txt.off` rename |
| **11** | the traffic fleet | **(c)** now, **(a)** after |
| **12** | the road checkers | **(a)** plus **(c)** for the 1.6% specifically |

Ten were taken on the documented recommendation. **The two that matter most were put to the owner
and he went AGAINST the recommendation on both** — recorded here as his rulings, not the plan's,
because both change what the town IS rather than how it is measured.

- **#1 (a) — THE COUNTY WINS EVERYWHERE.** Confirmed twice, the second time after being told
  explicitly that 408 Holmes is **the house the owner grew up in** and that the county puts that
  number on a different lot across the street. He chose the county anyway, and gave the reason:

  > *"I want to reset to use the county. I made changes before some of the new stuff was added.
  > Need a solid source and then we can do some edits."*

  **So the order is: solid source first, deliberate edits second.** The authored addresses predate
  the survey layer entirely; rebuilding all 202 on the county gives one rule, and re-anchoring his
  house afterwards becomes an edit made against something trustworthy rather than a guess buried in
  data nobody had checked. Do not treat 408 Holmes as settled, and do not quietly "fix" it in
  either direction — it is an edit he will make on purpose.

- **#5 (c) — MOSTLY FRONT DRIVEWAYS.** The plan recommended ~70% in back-lane garages as
  period-correct. He chose front drives, which is what the 611 cars placed on 2026-08-08 already
  do, so **no rework** — and the alleys keep their traffic-free justification rather than gaining
  a parking one.

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

### 3. The drawn road does not follow the road — **ANSWERED (a), 2026-08-09 and again 2026-08-10**

> ⚠ **THE OWNER RULED AGAINST THIS SECTION'S RECOMMENDATION, TWICE, AND THE SECOND TIME WITH THE
> MEASUREMENT IN FRONT OF HIM.** *"Leave it, corners stay rounded — a real street corner has a
> turning radius and a car cannot pivot on a point."*
>
> **The middle option was taken and it is enough.** This section says centripetal Catmull-Rom
> "halves the worst divergence to 55 ft. Not enough on its own." Measured on the tree as it
> stands, Summit is **16.71 m = 54.8 ft** — the predicted number exactly — and asked again with
> that figure in hand, he ruled to keep it.
>
> **So RC-17 is overruled and W5/W6 are NOT blocked on this.** A session that follows the
> recommendation below will straighten the streets against a standing ruling. The recommendation
> is left in place because the reasoning is worth reading and because deleting a superseded
> argument hides that it was ever made — but it is superseded.
>
> `DrawnRoadFollowsItsSurveyLineTests` ratchets the divergence so it cannot grow, and
> `docs/SOURCES-OF-TRUTH.md` carries the ruling with the numbers.


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

## DOOR-1 — nine doors nobody can walk through · ✅ **FIXED 2026-08-09, gated at zero**

> The owner ruled: fix them, then gate the validator at zero so it cannot creep again. It went
> 6 → 7 → 9 across one night's road work, unasserted the whole way.
>
> **`Noir/Audit the Doors` (`Noir.Editor.DoorAudit`) exists for this** — the validator can only say
> *which* door fails, which is not enough to fix one. It prints the ground under the door, the
> ground all round it, which walkable region it is in and how big that region is against the main
> one. That turned nine separate-looking failures into **two faults**:
>
> **A — five doors buried in Wall**, with all eight neighbours Wall too: `401 Dale Ave`,
> `502 Stewart Ave`, `501 Mckibben Ave`, `502 Mckibben Ave`, `208 Henderson Street`. The door tile
> itself is not walkable.
>
> **B — four doors sealed inside their own building**: `201 Perry Street` (pocket of 79 tiles),
> `315 Mc Kibben Street` (114), `400 South Grove Street` (164), `316 Mc Kibben Street` (165). The
> door is on Floor and the only walkable ground beside it is the building's own interior — there
> is no way out. The main region is 4,935,969 tiles, so these are not "cut off from the town",
> they are cut off from everything.
>
> Both are the same sentence: **the door is not on the outside face.** `ClearOfRoads` moved 175
> buildings off road corridors that run and it *does* carry each door with its building
> (`ClearOfRoads.cs:87`), so the door keeps its place on the wall — what it cannot carry is the
> room outside the wall. The audit now also names any neighbouring place covering a tile beside
> the door, which is what says whether a building painted over its own door or a neighbour moved
> against it.
>
> **THE FIX: `DoorsThatOpen`, a Core pass in `TownPipeline.Finish`.** A sealed door is moved round
> to a wall of its own building that has reachable ground outside it, preferring the wall nearest a
> road — because that is what a front door is. The building never moves: where a house stands is
> survey, which wall its door is in is not.
>
> **It runs after the world is built and forces one rebuild, and that is the whole correctness of
> it.** A door is judged on the GRID — can it be stood on, is it joined to the town — and the grid
> does not exist until the world is built. Judged on the LAYOUT instead, a neighbour's *lot* reads
> as its walls: the first cut moved **ninety** doors, eighty-one of them working front doors taken
> off the street to solve a problem they did not have. The validator went to zero either way, which
> is exactly why a green run proves nothing about how much was disturbed to get there.
>
> `WorldValidator.Regions` was extracted so the pass and the gate label reachability with the same
> code. Two implementations of "reachable" is how a pass comes to fix something a gate still fails.
>
> ```
>   9 door(s) moved round to a wall with ground outside it
>   validator: 0 error(s), 19 warning(s)      (was 9 errors, 28 warnings)
> ```
>
> Gated at zero by `EveryDoorOpensOntoSomething` in PlayMode — it has to be PlayMode, because
> `city.txt` on its own has no unreachable door and the fault only exists once the survey passes
> have run. Five `DoorsThatOpenTests` in Core cover the pass itself, including the ninety-door
> overreach.

## SPLINE-1 — the curve overshot, and nine roads left their own ends backwards · ✅ **FIXED 2026-08-09**

> **Not one of the 148. Found by looking at a picture**, which is the only reason it was found at
> all: `CityStreets` called alley8 × alley12 a *dead end facing east* while the render plainly
> showed both alleys running through it. Chasing the disagreement rather than trusting either one
> is what turned it up.
>
> `RoadPath.Smooth` is uniform Catmull-Rom: every span between declared points is one unit of
> parameter however long it is on the ground. Where consecutive spans differ wildly it overshoots —
> the curve leaves a vertex in the wrong direction, loops out, and comes back. **Opening the alley
> mouths is what created that shape**: a 13 m stub prepended to a 200 m run.
>
> ```
>   city.txt    0 of 37 roads
>   roads.txt   9 of 68 roads leave one end BACKWARDS
>     summit    83m then 1116m   wandered 39.2 m off its own polyline
>     alley2    14m then  139m   wandered  6.8 m
>     alley8    15m then  212m   wandered  5.0 m
> ```
>
> **Thirty-nine metres is a hundred and twenty-eight feet.** Summit Street was drawn, driven and
> built along a line that far from where the county says it runs.
>
> Fixed with **centripetal** Catmull-Rom (alpha = 0.5), which is provably free of cusps and
> self-intersections for any control points at all, applied through `RoadPath.SmoothCentripetal`
> and used by `RoadPath.Through`. **The railway keeps the uniform curve** — `Smooth` is untouched
> and `SmoothReproducesTheRailwaysOwnCatmullRomToTheBit` still holds the committed rail bed to the
> bit. After: **0 backwards on both maps**, summit's worst stray 39.2 m → **16.7 m**, which is the
> curve rounding a real corner rather than overshooting one — the same thing harrison does on
> city.txt at 6.3 m by design.
>
> `city.txt`'s counts did not move at all — 109 / 440 / 1100 / 37 either side — and only the
> segment checksum did. Same roads, same junctions, same lanes, drawn where the survey put them.
> The survey network went 112 → 110 junctions: two of them were the overshoot crossing something
> it never really reached.
>
> **CLOSED — the owner ruled on it, 2026-08-09: leave it, corners stay rounded.** Summit's 16.7 m
> is the curve rounding a corner the county recorded as a corner, and a real street corner has a
> turning radius; a car cannot pivot on a point. He is the authority on the town's shape
> (`SOURCES-OF-TRUTH.md`) and he grew up on these streets.
>
> **So do not "fix" this later by straightening streets, and do not smooth them further either.**
> Both directions are now a decision to be re-taken with him, not a defect to be chased. The
> number to watch is not the bow — it is `EveryBentRoadFindsItsWayBackToACoordinateOnItsOwnAxis`
> and the backwards count, which must stay at zero on both maps.

## ALLEY-2b — four streets stop short of the street they should meet · **OPEN, and well understood**

> Named by `NoTwoStreetsTouchWithoutAJunctionBetweenThem` on 2026-08-09, which is ratcheted at
> exactly these four so a fifth fails the suite:
>
> ```
>   benton and summit      8.3 m at (1117,1091)
>   dale and grove         6.1 m at (1351,1979)
>   dale and chicago       5.4 m at ( 883,1983)
>   thompson and chicago   7.4 m at ( 891,2101)
> ```
>
> Their corridors overlap, so a car ought to be able to turn between them, and there is no
> junction because there is genuinely **no tarmac** in the gap: Dale Avenue's county segment stops
> 0.4 m outside Route 1's carriageway. Inventing a junction there would be inventing a road.
>
> **The fix already exists and is simply not pointed at the streets.** `extend_to_streets` in
> `tools/build-roads.py` carries an alley's end out to the street it stops short of, refuses where
> the new stretch would cross somebody's lot, and counts what it did — that is what opened the 58
> alley mouths. Run it over street-class ends too, with the same refusal.
>
> ✅ **DONE for two of the four**, by `extend_streets_to_streets`: dale and thompson both reach
> Route 1 now.
>
> ⚠ **AND THE OTHER TWO ARE NOT THIS ITEM AT ALL. MEASURED 2026-08-10.** The declared gaps are:
>
> ```
>   benton x summit    declared  0.20 m      drawn  8.3 m apart
>   dale   x grove     declared  0.65 m      drawn  6.1 m apart
> ```
>
> **The county says those roads touch — twenty centimetres apart.** There is nothing for
> `extend_streets_to_streets` to extend, and it is right to say "already meeting". They are drawn
> apart because the SMOOTHED CURVE LEAVES THE SURVEY LINE, and by a lot:
>
> ```
>   16.71 m  summit    (3 declared points)      9.18 m  grove
>    9.31 m  watson                             8.85 m  benton
>    5.09 m  green
>   44 roads with 3+ points; 16 over a metre, 5 over five
> ```
>
> That is the same fault `CLAUDE.md` records at **39 m** under the uniform curve — *"Summit Street
> was drawn 39 m (128 ft) off its own survey line. Nothing could see it: it moves no count and
> fails no test."* Moving the roads onto `SmoothCentripetal` took it 39 → 16.7. It did not take it
> to nothing, and **there was still nothing that could see the rest.**
>
> **THE JUNCTION IS NOT MISSING; THE TWO CURVES ARE.** So ALLEY-2b's remaining half cannot be
> closed by extending a road, and widening the generator's tolerance to force a junction there
> would be inventing tarmac to paper over the drawing. **Do not do it.**
>
> ✅ **`DrawnRoadFollowsItsSurveyLineTests` now measures this on every run and ratchets it** — the
> worst road and the count over five metres, both may only fall. It uses `RoadPath` itself rather
> than a second implementation, so it cannot drift from what the game draws.
>
> ⚠ **IT IS NOT A DEMAND THAT THE ROADS BE STRAIGHTENED, and the test says so in its own header.**
> The owner ruled 2026-08-09: *leave it, corners stay rounded — a real street corner has a turning
> radius and a car cannot pivot on a point.* **What is open is a question for him, with numbers
> now attached:** is 16.7 m at Summit the turning radius he ruled for, or is it the overshoot the
> centripetal curve was supposed to have removed? Three declared points and a 16.7 m bow is a very
> large corner. That is his call and nobody should take it by adjusting a constant.

## The waves

### ALLEY-2 — the alley mouths · ✅ **LANDED 2026-08-09**

> **`Content/roads.txt` is written.** `python tools/build-roads.py --write` took its backup and
> opened **58 mouths, refused 1** (would cross a lot), **7 too far**. `tools/check-alleys.py` on the
> written file: **57 of 70 ends reach a street**, 1 alley touches none at either end (3%), median
> end-to-street gap **0.4 m**. All 12 `aadt` lines and 33 alley names preserved.
>
> **It could not land alone, and the ordering warning below is why.** Opening the mouths made the
> alleys drivable, and the first thing they drove into was the axis filter: an alley whose overall
> run is east-west, meeting an east-west street, made no junction, so cars came out of it through
> the traffic. JUNC-1 and JUNC-2 both had to land first — see W3. The sequence in the warning was
> right; only the item it named was wrong (JUNC-2 is the one that drops the filter).
>
> **What it cost when I got the order wrong**, kept because it is the measurement that settled it:
>
> ```
>   before   2 of 66 ends reach a street ·  31 of 33 stranded (94%) · median 14.9 m · 1.6% private
>   after   57 of 70 ends reach a street ·   1 of 35 stranded  (3%) · median  0.4 m · 1.5% private
> ```
>
> **Core went 447 -> 445 with two failures, and only one of them was expected.**
>
> - `TheSurveyNetworkIsTheSizeItShouldBe`: **117 junctions** against `InRange(40, 90)`. This wave
>   already predicts that gate widening "to the measured value (expect ~104-116)". Alleys reaching
>   streets is precisely what makes junctions, so 117 is the fix working, not a fault.
> - `NoLaneArrivesAtAJunctionItCannotLeave`: **`ann` lane 0 South ends at junction 5 with no turn
>   out.** That is a car trap - every vehicle that arrives stops there for the rest of the run and
>   stands its whole queue behind it. Same fault class as the starving junction fixed that morning.
>
> This file says **"⚠ Eight alley mouths land on a street of the same axis and make no junction
> until JUNC-1 lands - so W3 before W6"**. ALLEY-2 is W6 work; I pulled it forward because ALLEY-6's
> checker had just made the fault measurable, and walked straight into the ordering the plan warned
> about. Restored from the backup the generator now takes, Core back to 447 of 447.
>
> **⚠ ONE DOOR WAS LOST TO THIS AND IT IS NOT FIXED.** The geometry validator went from 6 errors to
> 7: `'the rooms over the meat market': door (821,1497) is not walkable`. No road within 16 m of that
> door moved — the cause is further back. Opening the mouths made 58 more corridors, so the survey
> pass shoved **182 buildings off a road corridor instead of 165** and refused **25 rather than 23**
> for standing on one, and the downtown re-settled around the difference. It is a diagnostic print,
> not an assertion, and no PlayMode gate fails on it. It is still a person who cannot leave his rooms.
> Belongs with the other six, which predate all of this.
>
> **The sequence that actually worked, paid for twice: JUNC-1, then `build-roads.py --write`, then
> JUNC-2.** Writing the mouths after JUNC-1 alone cleared the car trap and went 447/447 on Core, and
> the fault it left behind was invisible to Core entirely — only PlayMode could see two cars in the
> same place, because only PlayMode drives on it. The mouth code needed no further work: it is
> idempotent, keeps all 12 counts and 33 alley names, and refuses the one mouth that would cross a
> lot.

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
- **JUNC-1** ✅ **DONE, commit `e1c7056`.** Makes a junction a node with arms instead of a pair.
  ⚠ **Cluster at `a.Reach + b.Reach`, not `max(HalfWidth)`** — the proposed radius leaves benton ×
  summit (5.46 m against a 5.0 radius) unmerged and misses its own acceptance criterion. LaneGraph
  drops a piece when the arc separation is under reachA + reachB. Also the `Arm` struct as drafted has
  no constructor, so its readonly fields can never be assigned.
  > **"Choose the merged X,Y so no arm exceeds 1 m" WAS THE WRONG INSTRUCTION AND I FOLLOWED IT.**
  > There is no such point for a real multi-road corner: where Maple ends, Park begins and Route 1
  > goes past, nothing is within a metre of all three. The guard I wrote from this line was worse
  > still — it measured each arm's **pre-merge** S against the new centre, which is the crossing the
  > merge had just moved away from, so it refused every genuine corner in the town and left the
  > overlapping pairs it exists to remove. Re-project each arm onto the merged centre, then require
  > it to land inside the node's own **reach**. See JUNC-2 below for what refusing them cost.
- **JUNC-2** ✅ **DONE 2026-08-09.** Drops the axis filter and pays for it with a bounding-box reject
  (`RoadPath.MinX/MaxX/MinY/MaxY`, four comparisons per pair). `IsNorthSouth` is `dy >= dx` between a
  road's first and last point, so it describes the whole run and says nothing about which way the road
  points when it meets another one — alley21 runs 121 m west and 63 m north, so it is "east-west", and
  its first 13 m run due north into Benton, which is east-west too. The pair was never compared, no
  junction was made, and **cars drove out of the alley through Benton's traffic**:
  `NoTwoVehiclesOccupyTheSameSpace` measured 0.60 m between two of them, 123 m from the nearest
  junction the model knew about.
  > **FOUR MORE BUGS CAME OUT WITH IT, ALL THE SAME FALSE ASSUMPTION** — that arc length grows the
  > way a road's declared axis does. `RoadPath` measures s from `Points[0]`, and the county's chained
  > segments are declared in whichever direction the surveyor walked, so park, greenwood, alley13 and
  > alley18 all run right to left. Written out four times: LaneGraph cut the lane at `line.From + s`,
  > LaneGraph took the tangent it classifies turns from at `AlongOf(way, S) - line.From`, LaneGraph
  > ENDED the lane at `line.From + line.Path.Length`, and **`CityTraffic` drew the cars at
  > `along - line.From`**. Worst drift 608 m on `roads.txt`, 104 m on `city.txt`'s own railroad.
  > The fourth gave alley13 lanes to x=489 on an alley that stops at x=452 — thirty-six metres of
  > lane past the end of the road, and cars on it. **Only PlayMode could see that**: Core went
  > 451/451 green with it in. The cut and the tangent ask the junction now, the extent asks the
  > path's own bounding box, and the one remaining conversion is `RoadPath.ArcAt`, written down once
  > and held by `EveryBentRoadFindsItsWayBackToACoordinateOnItsOwnAxis` over both maps.
  > **Two tests were asserting these bugs and had to be corrected, not loosened**:
  > `ABendDeclaredInDecreasingOrderClassifiesTheSameAsIncreasingOrder` passed only because the cut
  > and the tangent were wrong in ways that cancelled on its fixture, and
  > `ACurvedRoadThatEndsInsideTheMapGetsNoOffStageMargin` expected `From + Path.Length` — 77 m of
  > lane past the end of its own fixture's road, under a name promising the opposite.
  > **And LaneGraph asked `NorthSouth`/`EastWest` which junctions were on a road.** Those report the
  > first arm of each AXIS, so at a merged three-arm node the third road got no cut and no turns. It
  > walks `Arms` now. That is what took city.txt's turns UP, 1088 → 1100.
- **JUNC-5, JUNC-7, JUNC-11, JUNC-12 and JUNC-13 — RE-DERIVED 2026-08-09 at the owner's
  instruction, and the audit found nothing left to do.** The five were unworkable as written (see
  below), so the junction model was re-audited from scratch against the code as it now stands.
  Measured, not read:
  > ```
  >   122 junctions · 0 with no lanes at all · 0 missing a turn between two of their roads
  >     0 lanes arriving somewhere they cannot leave
  >     0 junctions off any road they claim to join
  >     1 pair of roads running ALONG each other for more than 25 m
  >         3550north x alley13, 26 m of 3550north's 458 m, closest 0.4 m at (453,1324)
  > ```
  > The last is the one fault class still open and it is marginal — 26 m against a 25 m threshold,
  > entirely inside alley13's own mouth, where an alley meeting a street at a shallow angle
  > genuinely does share tarmac for a stretch. It is the residue of the fault that started all of
  > this (benton × alley21 at 2.0 m, two cars in the same place) and that pair no longer appears.
  >
  > **What the five items were probably about is already fixed**, by JUNC-1, JUNC-2 and JUNC-6:
  > the axis filter, junctions as nodes with arms, merged multi-road corners, lanes cut at every
  > arm rather than at a pair, and turns classified from the junction's own tangents. There is no
  > honest way to tick five items whose text is lost, so they are struck out rather than ticked,
  > and the audit above is the record of what was checked instead.
  >
  > **One modelling wart is left and is NOT a proven fault.** `Junction.NorthSouth` holds an
  > east-west road at 7 junctions, because a same-axis junction has no north-south arm to put
  > there. `CityStreets` no longer cares (JUNC-6), `CitySigns` asks the road its own axis, and
  > `CitySignals` compares road CLASSES rather than axes — and every traffic gate passes. It is
  > recorded because a name that lies is how the next person gets caught, not because anything is
  > currently misbehaving.

- ~~**JUNC-5, JUNC-7, JUNC-11, JUNC-12 and JUNC-13 CANNOT BE WORKED — they do not exist anywhere but
  in the line above.** Checked 2026-08-09: `grep -rl "JUNC-7\|JUNC-11" docs/` returns this file and
  nothing else, and `docs/history/` has no audit carrying them either. The read-only audit that
  turned 173 faults into 148 items was never committed, so **five of the ten items in this wave are
  ID numbers with no statement of what is wrong**. JUNC-5 is the sharpest illustration: the bullet
  below describes in detail why its *step 2* must not be done, and nowhere says what *step 1* is.
  **Do not guess at them and do not quietly drop them from the wave's tick list** — either the
  audit is recovered, or somebody re-derives the faults and writes them down here, in which case
  they should get honest new IDs.
  > **W3 IS FINISHED.** `JUNC-3` ✅ `JUNC-1` ✅ `JUNC-2` ✅ `JUNC-6` ✅ `GATE-7` ✅ `GATE-8` ✅ ·
  > `JUNC-5` `JUNC-7` `JUNC-11,12,13` unworkable, re-audited in their place.~~
- **JUNC-5 step 1 only.** ⚠ Step 2 as written drops the car through to `Choose` at ~90 interior exit
  segments, `Choose` returns −1, and the car parks on `Hold.NoLegalTurn` permanently — a 159-car fleet
  drains into 90 cul-de-sacs over a six-minute run. Say so in the flag's own doc comment.
- **JUNC-6 — ✅ BUILT 2026-08-09.** `CityStreets` asks the arms now, and the built town reports what
  it laid instead of one undifferentiated total:
  > ```
  >   3583 road tiles on 67 roads and 111 junctions
  >   (46 three-way, 7 corners, 1 dead end, 4 straight through and laid as plain road)
  > ```
  > The seven corners are real and are all the county's chain changing its name at a bend: abner ×
  > park, abner × perry, goodwine × holmes, grove × maple, grove × thompson, grove × henderson,
  > harrison × york. The dead end is alley8 × alley12. Every one used to be laid as a four-way
  > crossroads with two arms painted into whatever was behind the bend.
  >
  > **⚠ LAYING NOTHING LEAVES A HOLE, and the first cut of this had one.** `Lay` skips any tile
  > within reach of any junction, on the understanding that a junction tile has covered that
  > ground. Where JUNC-6 deliberately lays none, that is false and the road simply stops for the
  > width of the crossing. The untiled nodes are collected and handed to the carriageway walk;
  > tiles went 3573 → 3583, which is those four crossings being paved.
  >
  > **The turn and end pieces' own orientation is not written down in the pack**, so
  > `Noir/Render The Odd Junctions` exists to settle it: one frame straight down on each corner
  > yaw, the dead end, a straight-through node and an ordinary crossroads for comparison.

  The measurement that scoped it, kept because it is what said the item was live:
  > ```
  >   city.txt    109 junctions ·  0 same-axis ·  0 wrong-axis in the NorthSouth slot ·  2 merged
  >   roads.txt   112 junctions ·  7 same-axis ·  7 wrong-axis in the NorthSouth slot · 18 merged
  > ```
  > **city.txt is untouched by this**, so the authored map cannot be used to see it. The seven are
  > attica × 3550north, attica × alley2, attica × alley6, attica × alley10, alley10 × 3550north,
  > **benton × alley21** (the one the cars collided at) and benton × alley24. Every one has an
  > EAST-WEST road in the `NorthSouth` slot, and `CityStreets.cs:384-387` reads
  > `j.NorthSouth.From < j.Y - reach` — an x extent against a y coordinate. The arms come out
  > nonsense and the tile is laid to the wrong yaw.
  > **And the 18 merged nodes are a second half nobody has scoped:** `CityStreets.cs:554` gates on
  > `ReferenceEquals(j.NorthSouth, line) || ReferenceEquals(j.EastWest, line)`, which is the same
  > first-arm-of-each-axis rule LaneGraph had, so a merged node's third road gets no stop line and
  > no gap in its stroke. Walk `Arms` there too.
  >
  > **Only `CityStreets` is actually broken — the item says three renderers and that is now
  > measured wrong.** `CitySigns.cs:114-130` reads `stops.IsNorthSouth`, asking the ROAD its own
  > axis rather than trusting the slot it came out of, so it is already right. `CitySignals.cs:258`
  > and `:291` compare `Carries` between the two, which is a road-class question and axis-agnostic.
  > Both survive a same-axis pair unchanged. Do not "fix" them.
  >
  > The clean shape for `CityStreets` is to stop inferring arms from `From`/`To` at all: each
  > `Arm` knows its `S` and its road's `Path.Length`, so the road continues backwards out of the
  > junction when `S > reach` and forwards when `S < Length - reach`, and the tangent says which
  > compass direction each of those is (village y runs SOUTH, so north is the smaller y — the same
  > convention `CityStreets.cs:384` already uses). Correct for three roads, four roads and oblique
  > ones, and it is the same data `Reach` already reads.
  >
  > **THE PIECES ARE ALREADY IN THE PACK — do not lay a crossroads for want of one.** The item
  > says "a 2-arm case laying no tile", which is right for two arms facing OPPOSITE ways (a road
  > passing straight through, or two roads meeting head-on, which is what all seven of these are).
  > It is wrong for two arms at a right angle, and `Roads City/` has the piece for that:
  > `Road_Turn_10x10_City`, `Mainroad_Turn_30x30_City`, `Freeway_Turn_30x30_City`. A single arm is
  > a dead end and has `Road_End_10x10_City` / `Mainroad_End_30x30_City`. So the full table is
  > 4→Cross, 3→Tee, 2 opposite→nothing, 2 square→Turn, 1→End, 0→nothing and a warning. The turn
  > and end pieces have a built-in orientation that has to be READ OFF A RENDER, not guessed.
  ⚠ needs an explicit **2-arm case laying no tile**, excluded from the `inJunction` tests
  at **both** `CityStreets.cs:470-476` and `:542-547`, or 3550north × attica logs the crossing warning
  forever. Three renderers read `Junction.NorthSouth` as a compass direction and will draw a crossroads
  tile and a stop sign in the middle of a straight street once same-axis junctions exist.
- **GATE-7** — the fix already exists twice in this tree (commit `412f7cb` did it to `PlanLabels`, and
  `RoadCentrelines.cs:17-27` does it too): walk the `Path` instead of guarding on `IsStraight`.
  > **LOCATED 2026-08-09: it is `MapAudit`, in three checks, and one of them also carries the axis
  > filter JUNC-2 has just removed from the model.**
  > - `:88` — *places laid over a road*: `if (!line.IsStraight) continue`
  > - `:149` — *car parks with no way in*: same
  > - `:168` — *roads that meet without a junction*: `if (!ns.IsStraight || !ew.IsStraight) continue`,
  >   and then `if (!ns.IsNorthSouth || ew.IsNorthSouth) continue`
  >
  > On `Content/city.txt` that blinds it to five roads. On `Content/roads.txt` it blinds it to most
  > of them, so the audit has been reporting on a town the game does not build — which is the same
  > fault the survey layer had everywhere else and the reason `TownPipeline` exists. `Overlaps` and
  > `Gap` both take a `RoadLine` and read `Centre`/`From`/`To`, so they have to walk the path too;
  > check 7's corridor test should become the `Path`-and-half-width one `RoadNetwork.CouldMeet`
  > now uses, with no axis filter.
- **GATE-8 — ✅ DONE 2026-08-09** as `NoTwoStreetsTouchWithoutAJunctionBetweenThem`, and the split
  the item asks for is exactly the one it makes: street class is ratcheted, alleys are asserted at
  zero, and everything within 30 m that does NOT overlap is printed unasserted so the gap between
  the worst real fault and the first innocent pair is visible. **The numbers came out differently
  from the item's and the item's could not be checked** — the audit that produced "3 today, clean
  gap to 28 m" is not in the repo (see JUNC-7 above), and the model has changed twice since. What
  was measured: **8 street pairs and 6 alley pairs** whose carriageways overlap with no junction,
  taken to **4 and 0** by letting `Touches` accept an end inside the other road's carriageway. The
  four that remain are ALLEY-2b above. The gate also refuses any overlap deeper than 5 m, so the
  count cannot be ratcheted upward past the shape that traps a car.
- ~~**GATE-8** ⚠ ratchets **street-class near-misses only** (3 today, clean gap to 28 m) and prints the
  55 alley near-misses unasserted. Do not ratchet the combined 58 — 55 of them are the `STREET_CLEAR`
  artefact the item itself assigns elsewhere, so the gate would pin the number it argues must not
  drive it.~~

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

~~Blocked on **decision #3**.~~ ⚠ **UNBLOCKED 2026-08-10, AND `RC-17` IS OVERRULED.** Decision 3 is
answered **(a) keep the smoothing** — twice, the second time with the divergence measured at 16.71 m
(54.8 ft), which is this file's own predicted figure for centripetal Catmull-Rom. `RC-17` reverses
the smoothing and must not be done. Everything else in this wave stands; **do not tune the alley
extensions or the junction snapping to close benton × summit**, because that gap is the arc he ruled
for and closing it means inventing tarmac.

`MODEL-6` (the dead "past the end of the run" guard, so every corridor is effectively infinite) and
`MODEL-7` (corridors offset half a metre from the game's own centre lines) both move the 134 ratchet,
which is why GATE-5's re-record sits after them. **ADDR-2** puts the front-door decision in Core and
shares it with SeatOnSurvey; all three `RoadCorridor.cs` additions land in one commit before anything
calls them. Only **one** cluster may change `RoadPath.Smooth` — it is shared with the committed rail bed.

> **Gates.** `SmokeTest` green — `WorldValidator.cs:151` makes "door is cut off from the rest of the
> village" an **error**, and `VillageHost.cs:428` turns that into no town at all, so a moved door can
> delete the whole town silently.
>
> ✅ **GATE-5 RE-RECORDED 2026-08-10: the 134 is 28.** The item is right that the old number was
> unstable — *"it reads 9 in two logs and 31 in a third"* — so it was taken from four independent
> PlayMode runs on today's tree rather than one:
>
> ```
>   [geometry] 28 buildings standing in a street, worst 4.9 m      x4 runs, identical
> ```
>
> A ratchet at 134 is nearly five times the real number: **106 more buildings could drift into a
> carriageway before it said a word**, which is a ratchet that has stopped ratcheting. The open
> question it carries is unchanged and still open — the layout-level test reports ZERO against this
> 28, and until somebody establishes which corridor the tarmac is drawn to, the honest number is
> "28, and it must not grow".
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

> ✅ **CONS-4 / ALLEY-12 DONE 2026-08-10, and it was a whole road class wide.**
> `[streets] Alley: 7.1m of asphalt in a 4m corridor (-1.6m of pavement each side)` — not merely
> wrong but impossible, and it had been in every log this project has ever produced. `Seat`
> narrows a tile whose width does not match its corridor, and the alley tile is 7.1 m across in a
> 4 m corridor; `Asphalt` measured the PREFAB and never applied that scale. It reads
> **`4.0m of asphalt in a 4m corridor … tile narrowed x0.56 to fit`** now.
>
> **The alley exemption this wave predicted is gone with it.** `CityUnderTest`'s "is this vehicle
> on the road" check passed every point inside an alley corridor UNCONDITIONALLY, because the
> asphalt it was told about (3.55 m) was wider than the corridor containing it (2.0 m) — so a
> third of the town's network was exempt from `NoVehicleEverLeavesTheRoad` and nobody had chosen
> that. With the number fixed, the test still passes: **the population the wave warned would be
> exposed is not there.**
>
> And the player constant went 3.55 → 2.0. It was the editor's unscaled measurement copied into
> the `#else` branch, so a shipped build and the editor disagreed about a whole road class.

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

> ✅ **STALE, AND HARMLESSLY SO — CONS-1 LANDED WITHOUT THIS ITEM.** Give-way is on: `MayCross`
> asks `_signals.GivesWay(node, northSouth)`, holds with `Hold.GiveWay`, and `NothingCrossing`
> asks the time question rather than a fixed-distance one. It went on with the traffic-counts
> work, which is also what stopped it starving anything: `Carries` differentiates because the
> county's counts differentiate. So CONS-3 landed alone, and the pairing this line demands was
> satisfied by the order things actually happened rather than by one commit.

The wave comes after W3 and W6 for a hard reason: junction topology changes move the junction count
from 74, the turns from 630 and the conflict pairs from 2,393 in one step, and **a p90 that moves in a
run containing both is unattributable.**

> ✅ **CONS-2 DONE 2026-08-10, AND ITS GATE WAS WATCHED FAILING FIRST**, which is what this line
> asks for. `CarsKeepRightOnABendTests` was written against the unfixed arithmetic and reported
> **5,438 of 13,154 lane positions in the ONCOMING carriageway — 41% of the town's lane geometry,
> across 27 roads.** Then `Headings.SideOfPath` made it 0 of 13,154.
>
> The fault is exactly as stated: a coordinate-frame sign multiplied by a path-frame normal.
> `Headings.Side` answers "+1 is the greater x or the greater y"; `RoadPath.NormalAt` is
> `(-t.Y, t.X)`, the right-hand side of the PATH's direction. The product counts handedness twice
> wherever the local tangent has left the quadrant its declared heading names — which a straight
> road never does and a bending one always does. `SideOfPath` asks the only frame-consistent
> question: does this car travel WITH the path or against it. No compass at all.
>
> ⚠ **THE ITEM'S NUMBER IS WRONG FOR THIS TREE AND THE CORRECTION IS WORTH KEEPING.** It says "25
> of the 60 bending roads, Route 1 among them". Measured on `Content/roads.txt` as it stands:
> **7 roads by declared-heading disagreement, 27 by lane position, and Route 1 is not one of
> them** — the figure predates the centripetal curve and the re-derived roads.
>
> A second test, `TheCoordinateAnswerIsMeasurablyWrongSomewhere`, fails if the two answers ever
> agree everywhere — so "simplifying" `SideOfPath` back into `Side` names the roads it breaks
> instead of going quietly green.
>
> **Gates. The missing gate is the deliverable:** CONS-7's side check must still be **seen to fail
> on the unfixed tree first**. A fix whose gate has never gone red proves nothing.
>
> ✅ **CONS-3 DONE 2026-08-10 — and the compass has stopped deciding anything.**
>
> `CitySignals.GiveWayAxisOf` was `NorthSouth.Carries <= EastWest.Carries`, so every tie in the
> town went to the east-west road. Measured on the surveyed network: **122 junctions have two
> named arms, and `Carries` settles only 71 of them.** The other **51 — 42% of the town — were
> handed to a compass direction.** That is not "a tie broken arbitrarily": it is arbitrary in a
> DIRECTION, so it accumulates into a systematic bias instead of averaging out, which is the exact
> shape `CitySignals.cs:249-256` records as producing 119.9 s waits.
>
> The rule is `JunctionPriority.GiveWayIsNorthSouth` in **Core** now, where the suite can reach it
> — the same hoist CONS-2 got, for the same reason: it is a statement about the map, not about
> Unity. `CitySignals` and `CitySigns` both still read the one answer, so what a junction DOES and
> what it SAYS cannot drift apart.
>
> ```
>   class settles          71 junctions      unchanged, and the barn track still gives way
>   the county's counts    32 of the 51      IDOT counts twelve of Rossville's streets by name
>   the longer road        19 of the 51      a property of the ROAD, not of the junction
>   stop signs             69/53  ->  44/78
> ```
>
> **THE LAST TIE-BREAK IS LENGTH, NOT A HASH, AND THAT IS THE WHOLE POINT.** A per-junction
> tie-break — a compass direction, a coordinate hash — can put the stop sign on Maple at one
> corner and on the street crossing it at the next, and no real town is signed that way: a driver
> on the through street expects to keep priority for its whole length. Comparing lengths gives
> every junction along one street the same answer for free. It reads no RNG and no clock.
>
> Gated by `StopSignsLandOnBothAxesTests`: the split may not collapse onto one axis, the answer
> may not move between two builds of the same network, the class comparison is unchanged, and
> **no road the county counts as busier gives way to one it counts as quieter** — 18 junctions
> where both arms are counted differently, 0 offenders.
>
> ⚠ **CONS-3 TOOK `TrafficMovesAndStopsAtRedLights` RED, TWICE ON THE SAME TREE, AND THE TEST
> WAS THE THING THAT WAS WRONG.** Worth reading in full, because "my change broke a gate, so I
> will look at the gate" is exactly the reasoning this project forbids — and here the measurement
> is what settled it, not the reasoning.
>
> The old message could not tell two opposite faults apart: *"either the signals are not being
> obeyed or no car reached a junction in ninety seconds."* So the first thing that landed was the
> count that separates them — stationary samples, how many of those were at a signal's line, how
> many of THOSE were on green, and the nearest a stopped car ever got. It said:
>
> ```
>   stationary samples anywhere        281
>   of those, at a signal's line         0   (0 on green)
>   nearest approach by a stopped car  5.9 m
>   travelled                        133 km
> ```
>
> **5.9 m, against a band that started at 15 m.** The test demanded `dy > reach` — outside the
> junction box — so the LEAD car, the one actually waiting at the line, never counted. What it had
> been catching all along was the SECOND car in a queue, which means it only ever passed while the
> queues were long enough to have one. It was a test of queue length wearing a test of signal
> obedience's clothes, and CONS-3 shortened the queues.
>
> With the inner cut removed: **55 samples at the line, 55 of them on a red, 0 on green.** The
> lights were being obeyed the whole time and nothing could see it.
>
> **AND THE CHEAP WAY TO REPRODUCE IT IS `-testFilter "Noir.PlayTests.TrafficPlayTests"`** — four
> minutes against twelve, because it skips the 598 s `WhyAreThePeopleNotAnimating`. It reproduces
> the failure, which running the one test alone does NOT: alone it passes, because the city is
> shared and a fresh even spread puts cars everywhere. Three full-suite runs were spent learning
> that.
>
> The p90 gate swings **21.9 s to 37.2 s on an unchanged tree** — confirmed again 2026-08-10, red
> at 37.8 s and green at 15.5 s on consecutive runs of the same code. Run it twice before believing
> a traffic number moved, and never in the same run as a topology change.


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
  further than predicted. ✅ **DONE 2026-08-09**, re-recorded with the reason in the constants' own
  comment: junctions 111 → 109, segments 442 → 440, turns 1088 → 1100, entries 38 → 37. Both moves are
  bug fixes, not content changes — nothing in `city.txt` was touched. The test that was
  `EveryJunctionLandsOnBothOfItsOwnRoads` **was** re-stated, and deliberately not by widening a
  tolerance: it is `EveryJunctionLandsOnEveryRoadItClaimsToJoin` now, it checks every arm rather than
  the pair, and it gained a *sharper* second claim that has no tolerance at all — the S on record must
  be the nearest the road ever comes to the junction. That is what caught the 214 m alley, and it is
  size-independent, so the reach-sized first claim cannot hide anything behind it.
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
