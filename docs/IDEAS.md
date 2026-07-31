# Ideas

Things to look at later. Nothing here is a commitment and nothing here has been started.

Captured with `/idea <thing>` — optionally prefixed with a category, e.g.
`/idea road: the freeway should have on-ramps rather than crossings`. Tick a box when it is
done or delete the line when it turns out to be a bad idea.

## Env

- [x] ~~Power lines down the country roads.~~ DONE - `Assets/Noir/Unity/CityPowerlines.cs`, 394 poles and 284 spans down 18 roads. USED THE FARM SET, NOT THE CITY ONE: measuring both showed there are two internally-consistent pairs that must not be mixed, because the wire has to end where the pole does - `Pole_Electric_A_City` is 6.88m with its wire hanging 6.13-6.81, and `Pole_Electric_Old` is 7.37m with `Wire_20m_Tri` at 6.08-7.22. The old timber one belongs on a country road, and the note above only knew about the concrete city pair. Span is 20m because that is the wire's own measured length (z -20.02..0.18, drawn BACKWARD along -z), so a wire placed at a pole facing the previous one lands on both tops - not a spacing anybody picked. WHERE THEY GO IS ASKED, NOT DECLARED: each candidate spot asks the map what its ground is, and only grass, field or wood takes a pole, so the line stops itself where the fields stop rather than at a hardcoded town boundary that has already moved four times. Junctions exclude themselves for free (a crossing road's tile is Road, not grass) and so does anything inside a place, so no pole stands in a farmyard or a paddock. A wire is only hung when the previous spot also took a pole, so a line that reaches the edge of town ends cleanly instead of throwing a span across the gap. `country-poles.png` added to the CityShot set. MapAudit clean on all eight. — *2026-07-31*

## Roads

- [ ] The outer city. **THE FORK IS SETTLED — the map grows to 1290** and the design is written up
  at `docs/superpowers/specs/2026-07-31-the-outer-city-design.md`. Not started, and deliberately
  QUEUED BEHIND THE JAMS ITEM below: 28 suburb cells is ~450 households against the 945 declared
  now, which grows the fleet 236 -> ~350, and holding the fleet flat is the entire reason
  `CarsOutPerHousehold` is 0.25. Building it on an unfixed give-way fault makes a reproduced defect
  worse and muddies the evidence for whether any fix worked.

  Three things the old note had wrong, all of them checked. THE COUNTRY HAS NOWHERE TO MOVE TO: the
  road grid is not centred on the map (`westbound`/`eastbound` at 105 and 915 have a midpoint of 510
  against a map centre of 480), so outside the outer ring there is 90m on two sides and **30m** on
  the other two - one corridor's width. And the bands are not empty, they are where Home Farm and
  Wicker End live, so "the countryside moves out" actually means it stops existing and the farm is
  evicted a second time. THE PACK HAS NO SUBURBAN HOUSE - two farmhouses, both already placed, and a
  summer-camp cabin - but it does not need one: the modules ship an `_AS` all-sides variant that
  `CityBuildings.Stack` already uses, so a suburb is the same kit at ~14m pitch set back behind a
  hedge, not a new model. Bayhouse, because its unfaced universal tail is ~1m against Squarehouse's
  3m and on a detached house that tail is in the back garden. AND 1440 WAS AN OVER-ESTIMATE: the
  pitch is 90m, N blocks span `90N + 30`, and N=14 gives 1290 at 1.81x the area - 1110 would even
  have done at 1.34x, and the extra was bought deliberately to give the Racetrack, the tram kit and
  the Survival sites land without moving anything twice.

  MEASURED at `c1afb0c`, which the note asked for and nobody had taken: 31,814 renderers -> 4,462
  baked over 30 materials; the 27 district blocks are 8,228 of that, so **a block costs ~305
  renderers** and downtown is 26% of the city. — *2026-07-31*
- [ ] `Modular Parts/Rails` is a 6-piece GROUND-LEVEL tram kit (1/3/5/10m plus turns), unused and quite separate from the elevated railway that is commented out in city.txt. — *2026-07-30*
- [ ] `Racetrack` is 152 prefabs - 25 road pieces, 91 fences, a control gate, an overpass - plus 79 racing cars excluded from traffic because there is nowhere to race them. Needs land. — *2026-07-30*

## Traffic

- [x] ~~Traffic budget is per HOUSEHOLD and a `district` has no residents~~ FIXED. A district now declares `units 29` in city.txt - MEASURED, not liked: CityDistrict reports 662 buildings across the twenty-three blocks it fills with a perimeter, which is 28.8 apiece. Core reads it through the new `WorldModel.DeclaredHouseholds`, deliberately a SECOND number rather than a wider definition of `Households`: a district has no rooms and no interiors, so making it a home would hand PopulationGenerator hundreds of citizens to house in a paved rectangle. `Households` and the simulated population are untouched; only things that scale to how busy the map is read the new one. `CarsPerHome = 1.5` is gone, replaced by `CarsOutPerHousehold = 0.25` - and the point is that the multiplier now MEANS something rather than covering for a different number being wrong: not how many cars a household owns, but how many are on the road at once, the rest being parked or never owned. DENSITY DELIBERATELY HELD: 945 declared households x 0.25 = 236 vehicles against 243 before, because jams are still an open problem below and doubling the fleet overnight would have made a known fault worse. The knob that moves density is now `units` in content, not a constant in code. Core gate builds, MapAudit clean, PlayMode 7/7. — *2026-07-31*

- [x] ~~**A car crossing a junction ignores every other vehicle.**~~ FIXED: CrossJunction now calls Blocked(), and inside a junction the same-heading filter is dropped because a turning car crosses the others rather than following them. `Blocked()` is only called from `RunSegment`; `CrossJunction` has no check at all, so a turning car drives through anything on the lane it is entering. This is what `NoTwoVehiclesOccupyTheSameSpace` catches at 0.00m, and the look-ahead fix does not touch it. — *2026-07-30*
- [x] ~~`NoJunctionEverShowsGreenBothWays` tests an invariant that no longer holds.~~ FIXED: the test now skips unsignalised junctions in both assertions. `MayEnter` returns true on BOTH axes at a priority junction by design - the separation moved into `NothingCrossing`. The test should assert the new rule: signalised junctions never both green, priority junctions have exactly one axis with priority. — *2026-07-30*
- [x] ~~Vehicle look-ahead is a constant 8m~~ — done in 4724abf: it is now both vehicles' measured half-lengths plus a headway. Did NOT fix the failing test; see the junction item above. — *2026-07-30*
- [ ] No colliders on any vehicle: `CityTraffic` avoids by RULES (signals, give-way, look-ahead box), never by intersection test, so where a rule has no case cars pass through each other. Probably right for AI-vs-AI; needs revisiting the moment the player can drive. — *2026-07-30*
- [x] ~~Jams appeared after the fleet went 97 -> 243.~~ **FIXED**, and the fix is four separate
  faults rather than the one this entry described. Measured on the live city before: **100 of 236
  vehicles held at the head of a CLEAR queue**, median 20.8s, **p90 and worst both 119.9s in a
  120s window** - the worst tenth never moved once while they were watched. After: median 16.9s,
  p90 24.7s, worst 53.6s, and the commonest reason a car is stopped is now a red light.

  THE GAP TESTS ASKED THE WRONG QUESTION. Both reduced to "is anybody within N metres of the
  junction", counting cars that were STOPPED as well as approaching, so a queue standing at its
  own red light thirty metres up the crossing road blocked this junction for as long as it stood
  there. Now `Arrival()` against `WhenClear()`: will it get here before I am out of its way, from
  measured pace and this vehicle's own turn - so a lorry and a hatchback no longer wait for the
  same gap.

  THERE WAS NEVER A COLLISION TEST INSIDE A JUNCTION. `CrossJunction`'s comment called `Blocked()`
  the safety net; `Blocked()` is a FOLLOWING model, a 2.4m box down the car's own heading, and it
  cannot separate crossing paths at all. Measured: crossing pairs shared a junction for 17,395
  frames and came within 2.84m. So signals and give-way were doing ALL of it, which is exactly why
  `Patience` produced a collision the moment it let a car pull out anyway. `MapConflicts` now
  derives which turns cross which from the arcs themselves (2,847 pairs over 84 junctions), and a
  car claims its turn on entry.

  A CAR COULD STOP INSIDE A JUNCTION AND HOLD ITS CLAIM FOR EVER. Adding claims turned that from a
  local stall into a global lock: eighteen cars stuck on give-way in every sample, following
  traffic climbing 151 -> 167, **total distance travelled by the whole fleet ZERO**. Fixed by
  `RoomBeyond` (do not enter a box you cannot leave) plus restricting `Blocked()` to parallel
  traffic everywhere, so crossing traffic can never stop a committed car.

  `Reintroduce` PUT CARS INSIDE EACH OTHER. It dropped a recycled vehicle on an entry with no
  regard for what was standing there - two cars at 0.00m exactly, which is not a driving fault at
  all. It only became common ONCE THE TRAFFIC FLOWED, because while the city was jamming almost
  nothing ever reached an exit to be recycled. This is the one that would have been missed by
  isolated reruns.

  The safe version of `Patience` is in as `Rethink`: after twelve seconds a driver who cannot turn
  left **chooses a different movement**. It bypasses no rule - the signal, the claim, the room
  beyond and the gap all still apply - it only changes where the car wants to go, which no safety
  rule depends on.

  VERIFIED THE WAY THE OLD NOTE SAID TO: two consecutive FULL-SUITE PlayMode runs, 11/11 both
  times, exit 0. Core 163/165 (the two known 2:1 gates). MapAudit clean on all eight. New
  `TrafficDiagnostics` reports the numbers above and gates nothing; `NoCarWaitsForeverAtTheHead
  OfAClearQueue` gates the distribution - p90 under one signal cycle, worst under two. — *2026-07-31*

- [ ] ~~Jams~~ SUPERSEDED, kept for the reasoning: the original entry follows.

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

- [x] ~~Downtown block interiors are flat paving.~~ DONE - `CityDistrict.Interior`, 753 things across the twenty-three blocks that get a perimeter. Laid on a seven-metre lattice, one thing to a cell, and every piece MEASURED to fit inside one, so nothing can reach its neighbour and no clearance check is needed - the same guarantee the parking bays got two items up, arrived at the same way. Forty-four percent of cells stay empty (a yard packed to its edges is a car park) and one column is kept clear end to end, because a yard nothing can drive into is a courtyard. LOW BUILDINGS WERE TRIED AND DROPPED: `Squarehouse_Garage_City` is the pack's only low outbuilding and almost all of it is on `M_Universal_A` - the same sand-coloured atlas behind the tan slab - so a yard of them renders as featureless cream boxes from directly overhead, which is how this game is looked at half the time. Blank placeholder geometry is a worse answer than the bare paving it was meant to fix, so the yards are bins, skips, boxes and vehicles: things that read as themselves from above. If a low outbuilding is ever wanted here it needs a model that is not on the universal atlas. `block-yard.png` added to the CityShot set. MapAudit clean, PlayMode 7/7. — *2026-07-31*

## People

## Story

- [ ] `Survival` is 174 prefabs and nothing has ever placed one: `Tree_Stand`, `Bear_Trap`, `Cross_Wood` for a roadside memorial, `Road_Flare`, abandoned suitcases, bedrolls, storm lanterns. The best-matched folder in the pack for this game, held back deliberately because where they go is a story decision. — *2026-07-30*

- [ ] Deduction as recipes: a corkboard where pinning evidence in a *shape* produces a lead. The Crafting System's `TableRecipe` is already position-aware rather than only contents-aware, and `ISatisfier` is the "do these inputs match this pattern" abstraction. Build it in Core against `particulars.txt`. — *2026-07-30*

## Tech

- [x] ~~`-quit` and `-runTests` MUST NOT be combined~~ FIXED: `Assets/Noir/Editor/TestInvocationGuard.cs` now catches the combination in batchmode and exits 1 with a clear error instead of letting Unity exit 0 silently. CONFIRMED by direct reproduction: the raw combo logs "Batchmode quit successfully invoked" before any test callback fires and writes no results file (exit 0); with the guard it now exits 1 before that race even starts; the correct invocation (no `-quit`) is untouched — reran the PlayMode suite and got 7/7 passing. Root cause: `-quit` and the test runner's start-up both hang off `EditorApplication.update`, and `-quit` wins the race. — *2026-07-30*

- [ ] **Two Core tests are failing and were already failing.** `TwoToOneTests.TheMedianVillagerYieldsTwiceAsMuchTextureAsUse` (wants a ratio >= 2.0) and `TheTenthPercentileIsNotALock` (wants >= 1.0). Found while running the gate for the district work; CONFIRMED pre-existing by stashing that work and running them at HEAD, where they fail identically, so nothing above caused them. 163 of 165 pass. Not investigated - they are about the 2:1 texture-to-use instrument, which is a different subsystem from anything being touched here. Note also that a full Debug `dotnet test` takes 7m41s against 30s in Release, and there is a crashdump under `tools/Noir.Core.Tests/TestResults/` from an earlier run - see the CPU-instability note before blaming code for anything intermittent. — *2026-07-31*

- [ ] `MapAudit` reports faults with `Debug.LogError` and then exits 0 REGARDLESS - `EditorApplication.Exit(0)` is unconditional. So a batchmode caller cannot tell a clean map from a broken one without grepping the log, which is the same "looks exactly like a clean pass" shape as the `-quit`/`-runTests` bug above. Left alone for now because something may already depend on the zero. — *2026-07-31*

- [ ] Lift the Crafting System's UGUI inventory UI — drag-drop slots, transfer, tabs — rather than writing one. Tedious to build, and presentation belongs in Unity anyway. — *2026-07-30*
- [ ] Evidence catalogue as `Content/items.txt` in the shape of `kinds.txt`, read by Core. NOT the Crafting System's ScriptableObjects: content authored in an editor window is content `MapAudit` and the PlayMode tests cannot see. — *2026-07-30*

## Ad hoc
