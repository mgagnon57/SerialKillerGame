# Ideas

Things to look at later. Nothing here is a commitment and nothing here has been started.

Captured with `/idea <thing>` — optionally prefixed with a category, e.g.
`/idea road: the freeway should have on-ramps rather than crossings`. Tick a box when it is
done or delete the line when it turns out to be a bad idea.

## Env

- [ ] Power lines down the country roads. `Pole_Electric_A_City` is a 6.9m pole pivoting at its base and `Pole_Electric_Wire_20m_A_City` is a 20m span of wire at 6.1-6.8m drawn back along -z; they are authored to chain. Twelve rural roads have nothing on their verges. Approved once and deferred when the focus moved to the city. — *2026-07-30*

## Roads

- [ ] The outer city, and the map-size fork that comes with it. Downtown fills 255..795; the suburbs would take the 105..255 and 795..915 bands, which is one thin ring and squeezes the country to a 105m frame. Either the countryside moves out again or the map grows to ~1440 (2.25x the props). Decide after measuring what a block actually costs. — *2026-07-30*
- [ ] `Modular Parts/Rails` is a 6-piece GROUND-LEVEL tram kit (1/3/5/10m plus turns), unused and quite separate from the elevated railway that is commented out in city.txt. — *2026-07-30*
- [ ] `Racetrack` is 152 prefabs - 25 road pieces, 91 fences, a control gate, an overpass - plus 79 racing cars excluded from traffic because there is nowhere to race them. Needs land. — *2026-07-30*

## Traffic

- [ ] Traffic budget is per HOUSEHOLD and a `district` has no residents, so twenty-seven new downtown blocks added nothing. `CarsPerHome` was raised to 1.5 to compensate, which is a fudge. A district should declare how many live in the block, and then it drops back towards one. — *2026-07-30*

- [x] ~~**A car crossing a junction ignores every other vehicle.**~~ FIXED: CrossJunction now calls Blocked(), and inside a junction the same-heading filter is dropped because a turning car crosses the others rather than following them. `Blocked()` is only called from `RunSegment`; `CrossJunction` has no check at all, so a turning car drives through anything on the lane it is entering. This is what `NoTwoVehiclesOccupyTheSameSpace` catches at 0.00m, and the look-ahead fix does not touch it. — *2026-07-30*
- [x] ~~`NoJunctionEverShowsGreenBothWays` tests an invariant that no longer holds.~~ FIXED: the test now skips unsignalised junctions in both assertions. `MayEnter` returns true on BOTH axes at a priority junction by design - the separation moved into `NothingCrossing`. The test should assert the new rule: signalised junctions never both green, priority junctions have exactly one axis with priority. — *2026-07-30*
- [x] ~~Vehicle look-ahead is a constant 8m~~ — done in 4724abf: it is now both vehicles' measured half-lengths plus a headway. Did NOT fix the failing test; see the junction item above. — *2026-07-30*
- [ ] No colliders on any vehicle: `CityTraffic` avoids by RULES (signals, give-way, look-ahead box), never by intersection test, so where a rule has no case cars pass through each other. Probably right for AI-vs-AI; needs revisiting the moment the player can drive. — *2026-07-30*
- [ ] Jams appeared after the fleet went 97 -> 243. CONFIRMED, both halves of it, by direct reproduction on the live city - and a fix was tried and reverted, because it traded starvation for an actual collision.

  ROOT CAUSE: `NothingCrossing` (give-way) and `NothingComing` (left turn) both wait for zero
  traffic within a fixed distance (`Crossing`=35m, `Oncoming`=22m), and a busy two-lane road can
  relay-hand that gap from one car to the next with no frame ever clear - not a jam, not a
  stopped queue, just continuous alternating arrivals. Watched it happen: a give-way car held
  station for 235 of a 240-second window, and a left-turner for the full length of a 60-second
  test, neither with a single frame of clearance.

  THE FIX TRIED: a `Patience` timeout (15s) that lets a car commit anyway once it has waited too
  long, leaning on `Blocked()` - already called every frame of `CrossJunction`, already checking
  every heading once a car is inside the junction - as the real-time safety net. Isolated reruns
  of `NoTwoVehiclesOccupyTheSameSpace` passed clean (5/5). The FULL SUITE did not: every PlayMode
  test shares one continuously-running city, so by the time that test runs the traffic has been
  live for several times longer than an isolated run ever exercises, and twice in a row it found
  an actual 0.00m collision (`Car_Truck_Modern_Dump`, same spot both times). Isolated short reruns
  were not a valid safety check for this - full-suite is.

  A SECOND BUG SURFACED ALONG THE WAY: `Blocked()`'s same-heading filter is only dropped when
  `me.Turn >= 0` (the turning car watches everyone), never when `other.Turn >= 0` (nobody watches
  a turning car back). That was fine while a turn only ever started with the crossing lane
  measured clear 35m/22m out, which gave the turning car's own check a long lead time - and
  stopped being fine the moment Patience removed that precondition. Making the filter symmetric
  did not stop the collision on its own either.

  REVERTED rather than shipped: a mechanism that can put two vehicles in the same space is worse
  than one that occasionally makes a car wait. NEXT STEP is not another bypass - it is either a
  graduated reduction of `Crossing`/`Oncoming` toward a conservative, empirically-verified floor
  (never zero), or gap acceptance based on actual closing speed rather than fixed distance -
  proven safe over several FULL-SUITE repeats, not isolated ones, before it ships.

  SEEN LIVE, not just in a test: a screenshot from an actual Play session showed
  `Car_Truck_Modern_Garbage` and `Car_Truck_Modern_Cistern` stopped nose-to-tail at a SIGNALISED
  city junction with a GREEN light showing - not a give-way case at all. Instrumented
  `NothingComing` directly and confirmed it: both trucks turn up in the block log within the same
  run, the garbage truck held by oncoming traffic and the cistern truck (queued behind it) along
  for the ride via ordinary follow distance. So this is not a country-priority-junction-only
  problem - `NothingComing`'s left-turn gap wait hits ordinary signalised city traffic too, on a
  green light, which is exactly the case the class-level comment already calls out as "the one
  conflict signals never resolve on their own." Whatever fix is designed for `NothingCrossing`
  needs to cover this case too, and be checked against it the same way: full-suite, repeated,
  watching for both the wait (`NothingComing` never clearing) and the crash (two vehicles at
  0.00m), not one or the other. — *2026-07-30*

## City

- [x] ~~**The tan slab on every building front.**~~ FIXED: found what the geometry was, per-submesh. `Squarehouse_Bottom_A`'s brick and glass end at z 3.01 but its `M_Universal_A` submesh runs to 5.96 - and the OTHER sections in the same stack (Entrance, Mid, Roof) carry nowhere near as much of a tail past their own brick (0.1-0.6m, not 3m), so this is a defect in the Bottom piece specifically, not a real building depth. `Bayhouse_Bottom_A` has the same fault at a smaller scale (~1m). `Seat()` was aligning the front wall to the far edge of the WHOLE combined renderer bounds, which for a north-facing front put that unfaced tail's edge on the building line and left the actual brick wall recessed up to 3m behind it, invisible from the street - confirmed by rendering the same view before and after: flat black wall with no texture at all, vs. proper brick, at the identical camera position. This is also almost certainly why this file used to say Bayhouse is 7m deep and Squarehouse 9m: both figures come from the same whole-bounds measurement, and both families' BRICK footprints alone measure within a centimetre of each other. FIX: `CityBuildings.Seat` takes a `dressedOnly` flag; `Stack()` (the modular townhouse path) now measures by brick and glass submeshes only via `TryDressedBounds`, skipping `M_Universal_A`. `Tower()` still passes the old raw-bounds path unchanged, since skyscraper meshes have no brick or glass submesh to measure by. MapAudit clean, PlayMode 7/7. — *2026-07-30*
- [x] ~~Parking overlap was reported again after the fix.~~ FOUND AND FIXED, and it was never the scale bug. Wrote the audit that was missing - `MapAudit` check 8 now BUILDS the lots and compares real renderer bounds pairwise, because the other seven checks are arithmetic on the authored layout and a parked car is not in the layout. First run: **19 overlapping pairs out of 85 cars**, including the vans and taxis at the lot near 584,545 that the original report described and that the precinct render could never have shown. ROOT CAUSE was `Bay = 5f` - the last guessed number in the file, exactly the fault its own `Gap` comment rails against one axis over. The pack's cars are 5.0-5.5m long, a back-to-back PAIR was pitched `Bay` apart, and each car was CENTRED on its row line, so any two cars longer than 5m met in the middle and buried their tails in each other by (L1+L2)/2 - 5, which is the observed 0.24-0.61m exactly. Invisible from above because it happens BETWEEN rows, not along them. `Width()` was correct all along - verified it against the placed renderer bounds on four vehicles, agreeing to 0.00m. FIX: a car now backs onto the outer edge of its own half of the pair, positioned by its own measured length (`Long()`, mirroring `Width()`), so the two halves cannot reach each other whatever parks in them; anything too long for its half (a school bus, an ambulance) leaves the slot empty rather than hanging into the row behind. 19 overlaps -> 0, 85 cars -> 73, all eight audit checks clean, precinct render eyeballed. — *2026-07-31*

- [ ] Downtown block interiors are flat paving. Real blocks have rear yards, parking and low buildings in the middle. — *2026-07-30*

## People

## Story

- [ ] `Survival` is 174 prefabs and nothing has ever placed one: `Tree_Stand`, `Bear_Trap`, `Cross_Wood` for a roadside memorial, `Road_Flare`, abandoned suitcases, bedrolls, storm lanterns. The best-matched folder in the pack for this game, held back deliberately because where they go is a story decision. — *2026-07-30*

- [ ] Deduction as recipes: a corkboard where pinning evidence in a *shape* produces a lead. The Crafting System's `TableRecipe` is already position-aware rather than only contents-aware, and `ISatisfier` is the "do these inputs match this pattern" abstraction. Build it in Core against `particulars.txt`. — *2026-07-30*

## Tech

- [x] ~~`-quit` and `-runTests` MUST NOT be combined~~ FIXED: `Assets/Noir/Editor/TestInvocationGuard.cs` now catches the combination in batchmode and exits 1 with a clear error instead of letting Unity exit 0 silently. CONFIRMED by direct reproduction: the raw combo logs "Batchmode quit successfully invoked" before any test callback fires and writes no results file (exit 0); with the guard it now exits 1 before that race even starts; the correct invocation (no `-quit`) is untouched — reran the PlayMode suite and got 7/7 passing. Root cause: `-quit` and the test runner's start-up both hang off `EditorApplication.update`, and `-quit` wins the race. — *2026-07-30*

- [ ] Lift the Crafting System's UGUI inventory UI — drag-drop slots, transfer, tabs — rather than writing one. Tedious to build, and presentation belongs in Unity anyway. — *2026-07-30*
- [ ] Evidence catalogue as `Content/items.txt` in the shape of `kinds.txt`, read by Core. NOT the Crafting System's ScriptableObjects: content authored in an editor window is content `MapAudit` and the PlayMode tests cannot see. — *2026-07-30*

## Ad hoc
