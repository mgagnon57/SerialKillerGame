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
- [ ] Jams appeared after the fleet went 97 -> 243. Suspect the give-way gap check starves a minor-road car forever once traffic is dense, blocking everyone behind it. — *2026-07-30*

## City

- [ ] **The tan slab on every building front.** `Squarehouse_Bottom_A_*` has brick and glass ending at z 3.01 but `M_Universal_A` running to z 5.95 - a ~3m projection on the universal atlas, which is the sand colour. MEASURED: all three face variants (_AS, _F, _FB) have it, so swapping variants is NOT the fix; the suffix controls glazing only. Next step is to find what that geometry is and either exclude it from `Seat`'s bounds or stop using Bottom as a ground floor. — *2026-07-30*
- [ ] Parking overlap was reported again after the fix. My render of the PRECINCT lot at HEAD is clean, but that lot's fleet has no vans or taxis and the report did - so a different lot. Also `CityParking.Width` reads `sharedMesh.bounds`, which is local mesh space and IGNORES transform scale: any vehicle authored at scale != 1 is under-measured and will overlap. Check both. — *2026-07-30*

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
