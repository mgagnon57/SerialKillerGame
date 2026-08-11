# Ideas

Things to look at later. Nothing here is a commitment and nothing here has been started.

Captured with `/idea <thing>` — optionally prefixed with a category, e.g.
`/idea road: the freeway should have on-ramps rather than crossings`. Tick a box when it is
done or delete the line when it turns out to be a bad idea.

> ## ⚠ SWEPT 2026-08-11 — a THIRD of this file was describing a game that no longer exists
>
> **33 open items went to 21.** Twelve were closed, and not one of them by doing any work: they had
> already been done and nobody came back to tick the box. Five said *"superseded, kept for the
> reasoning"* in their own text while still carrying an open checkbox. The other seven were checked
> against the code and the measurement is written into each:
>
> | | was claimed | is true |
> |---|---|---|
> | ribbons float 6 cm | `CityOutlines.Lift = 0.06f` | **`0.25f`** |
> | roads that end on roads make no junction | ~21 of them | `JUNC-2` + `GATE-8`; 74 → **120** junctions |
> | `AmbientTrafficWeight` is 4:1 | ladder only | reads the county's **AADT**, called at `CityTraffic:607` |
> | nobody's car is in their driveway | drawn: none | **620 cars at 612 houses** |
> | two Core tests are failing | permanently red | `[Explicit]`, suite is **477/0** |
> | the PlayMode command hangs | hangs | `-assemblyNames Noir.PlayTests`, **19 of 19** |
> | no layered visibility system | none | `Layers.Kind`, **21** kinds |
>
> **This is the failure mode to watch for, not a one-off tidy.** A backlog is what decides what gets
> built next, so a stale item does not sit harmlessly — it spends a session. Two of the twelve were
> actively *blocking*: the `AmbientTrafficWeight` entry was cited in `CityTraffic`'s own docstring as
> the reason not to fix the fleet size, and it had been fixed for days.
>
> **Tick the box in the same commit as the fix.** Every one of these twelve was somebody finishing
> the work and not coming back.

## Decisions needed — read this section first

Two things asked for overnight on 2026-08-02 that were NOT done, on purpose, because they run
against a standing project rule rather than being merely large or risky. Both need an explicit
"yes, do this anyway" from a person, not an autonomous call at 1am.

- **Google Street View, to grade a real home's condition/quality at its real address.** The
  project already has a rule for exactly this shape of request - see "NO REAL RESIDENTS" in
  `docs/superpowers/specs/2026-08-01-terrain-pipeline-multiagent-design.md`. That rule is about
  names, specifically, and the letter of it would not be broken by looking at a house rather than
  a person - but the reasoning behind it is not really about names. Rossville is a real, small,
  currently-inhabited town, and this project is a murder simulation set in it. Pulling a current
  photograph of a real, named address to score its upkeep for a murder game is the same category
  of thing the NO REAL RESIDENTS section was written to head off, whether or not a person's name
  ends up attached: it makes a real, current, identifiable home into set dressing for a fictional
  killing, without whoever lives there knowing that happened. `ParcelNotes`/`CountyRecord` already
  derive housing quality from the ASSESSOR'S OWN real data (assessed value, class code, year
  bracket) for exactly this reason - it is real without being a photograph of someone's actual
  front door. If this is wanted anyway, it wants a decision from a person, in daylight, not a
  default "the agent was told to keep going." Not attempted.

- **Newspaper/archive research per real parcel, 1995-2006, to inform character notes.** This one
  is not a grey area - it is the EXACT thing the design spec's "NO REAL RESIDENTS" section
  withdrew, by name: *"An earlier draft of this document asked Stream 1 to research 'occupant
  names' from public records and gated it on being 'grounded in community research'. That
  instruction is withdrawn... Character notes are invented ON PURPOSE."* A 1995-2006 small-town
  newspaper archive is built almost entirely out of named, identifiable, still-living-in-2026
  people - real crime items, real obituaries, real school-board minutes with real names attached
  to real addresses - and mining it per-parcel for a serial-killer game is precisely what that
  paragraph exists to prevent. Doing it would not be a bug fix or a judgement call inside normal
  scope; it would be undoing a decision the project already made in writing. Not attempted, and
  not recommended without a very deliberate, specific override of that section - the household
  generation this would replace (`Households.cs`, real demographic SHAPE from the county record,
  fictional people from `Content/names.txt`) is the thing that let the game have a demographically
  real town without a real resident in it.

## From the town history research — 2026-08-03

Everything here comes out of `docs/research/ROSSVILLE-HISTORY.md` and the nine Sanborn sheets in
`docs/research/sanborn/`. **The town is built as it stood in 1991, long before the February 2004
fire** — the year is decided in `docs/research/THE-ERA.md` and nowhere else.

- [ ] **Georeference the Sanborn footprints for the downtown core.** The 1913 sheets draw every
  building at Attica × Chicago with its material, storey count and use. Most of that row was built
  after the 1893 fire and stood until 2004, so it is substantially valid for 2000. This is the only
  real building-footprint source this project has — the county has none and OSM has 19. — *2026-08-03*
- [ ] **The commercial row is shops below, lodge rooms and offices above.** Not a scatter of
  buildings — a continuous two-storey brick terrace. The 1913 sheet names bank, jeweller, bakery,
  meat, drugs, hardware, furniture, millinery, barber, plumbing, printing, and a steam laundry,
  with "Lodge Rms 2nd" over several of them. That pattern goes back to the very first store in
  1857, which the Odd Fellows built with their hall on the upper floor. — *2026-08-03*
- [ ] **A frame house kit in three date-layers**, replacing the pack's Chicago brownstones. See
  HANDOFF §3 for the spec. Nothing residential in this town is brick. — *2026-08-03*
- [ ] **The original town is four blocks at Chicago × Attica**, and everything else is a named
  addition — Gilbert's, Satterthwait's, Livingood's, Henderson's. Worth encoding: the oldest,
  densest fabric should be those four blocks, thinning outward through the additions in the order
  they were platted. — *2026-08-03*
- [ ] **Industry belongs along the railroad, not the streets.** Historically three grain elevators,
  two lumber yards, a wagon works, a brick and tile works, a cannery with its own sauerkraut plant,
  a creamery, and stock pens — all strung along the C&EI. By 2000 nearly all of it is gone, which
  is itself worth drawing: the town has empty industrial ground on its east side. — *2026-08-03*
- [ ] **Landmarks that should exist by name**: the 1903 C&EI depot (still standing, a museum), the
  old Ross Township building (the historical society), Christman Park, the spherical water tower
  (150,000 gal), the grain elevator. These are the silhouette a person who grew up there
  recognises. — *2026-08-03*
- [ ] **The antique shops should be trading in 2000** — many small shops in ordinary storefronts,
  not one mall building. It is the tail of the era; eBay is already eating it. — *2026-08-03*
- [ ] Unresolved and worth settling before anything leans on it: **the incorporation date**.
  Wikipedia and the village say August 1859; both county histories say July 1872 with the vote
  count, the trustee election and the officers named. Two independent research passes hit this.
  — *2026-08-03*

## Env

- [x] ~~We need to add elevation at some point.~~ **ALREADY DONE, and this entry was stale** -
  nobody came back to tick it. Checked against the current code on 2026-08-02 rather than trusting
  the note, the way the MapAudit entry below had to be. `Content/elevation.txt` is real USGS NED
  10m data resampled to a 71 x 81 grid at 30m, and `ElevationGrid.HeightAt` bilinearly
  interpolates it, RELATIVE TO THE CROSSROADS - which is why a caller passing a literal `0f` still
  works and why this looked unstarted at a glance. Measured from the data:

      raw elevation      200.8 .. 225.3 m   (659 .. 739 ft)
      relief             24.5 m over the whole map
      about the crossing -8.3 .. +16.2 m

  EVERY ITEM ON THIS ENTRY'S OWN "what has to follow it" LIST IS DONE, verified one at a time:
  `CityCollision` builds a MeshCollider sampling the same grid rather than one flat box;
  `CityStreets.Seat` and `CityParking` take a per-tile ground sample (the literal `0f` in both is
  only the x/z target - there is a comment there saying exactly that, and saying that writing a
  world y of zero used to put the carriageway under the village); `CityBuildings` measures a base
  Y per lot; `CityTraffic` positions vehicles through `HeightAt`; and the camera goes through
  `Space3D.GroundHit`, a real ray-versus-ground intersection instead of a flat y=0 plane. The
  sculpt tool's additive delta layer rides on top of all of it.

  So there is nothing to build here. What the entry proposed - "a gentle height field OUTSIDE the
  road grid only, which leaves every system that assumes flatness untouched" - was the cautious
  version, and the real data went in everywhere instead. Left as a tick rather than deleted
  because the reasoning below is still the best description of what elevation touches, and the
  next person to wonder whether the map is flat should be able to read why it is not.
  — *2026-07-31, verified done and closed 2026-08-02*

  SCOPED 2026-08-01. Everything in the game is on one plane and the plane is
  assumed, not stored: `Space3D.ToWorld(pos, 0f)` is how every person, vehicle,
  building and prop gets its height, and the argument is a literal zero at every
  call site. So elevation is not a terrain feature, it is a change to the single
  function that answers "how high is here", plus a height field for it to read.
  What has to follow it: CityCollision's ground slab (one flat box today, would
  become a mesh), CityStreets seating tiles by bounds, CityBuildings' Storey
  stacking which measures from a base Y, CityTraffic moving cars along a lane
  coordinate with no Y at all, and the follow camera. The cheap first version is
  a gentle height field OUTSIDE the road grid only - the green edge and the
  country - which leaves every system that assumes flatness untouched, because
  roads and buildings all sit inside the grid. That is probably the right first
  step and it is worth doing on its own.

- [x] ~~Power lines down the country roads.~~ DONE - `Assets/Noir/Unity/CityPowerlines.cs`, 394 poles and 284 spans down 18 roads. USED THE FARM SET, NOT THE CITY ONE: measuring both showed there are two internally-consistent pairs that must not be mixed, because the wire has to end where the pole does - `Pole_Electric_A_City` is 6.88m with its wire hanging 6.13-6.81, and `Pole_Electric_Old` is 7.37m with `Wire_20m_Tri` at 6.08-7.22. The old timber one belongs on a country road, and the note above only knew about the concrete city pair. Span is 20m because that is the wire's own measured length (z -20.02..0.18, drawn BACKWARD along -z), so a wire placed at a pole facing the previous one lands on both tops - not a spacing anybody picked. WHERE THEY GO IS ASKED, NOT DECLARED: each candidate spot asks the map what its ground is, and only grass, field or wood takes a pole, so the line stops itself where the fields stop rather than at a hardcoded town boundary that has already moved four times. Junctions exclude themselves for free (a crossing road's tile is Road, not grass) and so does anything inside a place, so no pole stands in a farmyard or a paddock. A wire is only hung when the previous spot also took a pole, so a line that reaches the edge of town ends cleanly instead of throwing a span across the gap. `country-poles.png` added to the CityShot set. MapAudit clean on all eight. — *2026-07-31*

- [x] ~~`VillageMesh.BuildGround` draws one quad per 1x1m tile - ~20M vertices for the ground alone.~~
  FIXED, Stream 4 (performance): greedy-merged into one quad per uniform run, bounded so a run
  never crosses a 64m chunk edge or a 30m elevation-grid line (`ElevationGrid.Step`, newly
  exposed) and Bank tiles never merge. Classification is untouched, byte-for-byte, so this cannot
  blur a real zoning or terrain boundary - it only draws the same decision less often. Measured:
  20,552,984 -> 575,652 vertices (35.7x), 10,276,492 -> 287,826 triangles, on the real map.
  Draw calls did NOT move (2,626 both times) - see the new entry below, this is the follow-up it
  opens. Branch `stream4-performance`, PR #1, not merged to main yet - waiting on review.
  — *2026-08-02*

- [x] ~~**Ground draw calls (2,626) didn't move when the vertex count did.**~~ DONE - the ground
  has its own grid now, `MeshChunks.GroundSize = 256`, separate from the `Size = 64` Walls and
  Props share. **2,626 -> 319 draw calls, 8.2x.** MEASURED, not picked: the new `GroundChunkProbe`
  sweeps the size over the real map in one editor launch and reports what each value costs -

      size   chunks   draw calls   vertices   worst chunk
        64     1254        2,626    575,652     4,924 tris
       128      323          860    559,504     8,902
       256       90          319    553,148    15,846
       384       42          184    549,676    19,738
       512       25          120    549,452    37,098

  Vertices go DOWN as chunks coarsen, which is the opposite of what I expected: a run may not
  cross a chunk edge, so a coarser grid cuts fewer of them in half. 384 and 512 keep buying draw
  calls and were NOT taken - twenty-five chunks over the whole town is few enough that turning
  round discards nothing, and culling is the reason the class exists. Gates: Preflight exit 0 and
  `VERDICT: nothing found`, SculptCheck passed (90 chunks, brush paint ok - `SculptBrush` maps
  world coordinates to the Ground chunk cache and had to move to `GroundSize` with it).
  — *2026-08-02*

- [x] **The plan view's feature ribbons float 6cm and that is not enough clearance.**
  `CityOutlines.Lift = 0.06f`, and a ribbon vertex is `ElevationGrid.HeightAt(x,y) + Lift` - the
  TRUE bilinear height - while the ground under it is now a coarser triangulation of that same
  surface. The two are within a hair of each other, so the depth test flips on edge pixels. Found
  by the chunk-size change above: `plan-top-down.png` moved by **1,091 pixels of 4,194,304
  (0.026%)**, all of it on the diagonal rail ribbon and the ditch lines, none of it on the
  axis-aligned lot lines. It is NOT non-determinism - two consecutive Preflight runs on identical
  code are byte-identical, which is how this was attributed rather than guessed at - and it is
  invisible to the eye (I cropped and compared both at 4x; the rail reads the same). But it means
  the plan snapshot is no longer a stable regression check across ANY ground-mesh change, which is
  most of what it was for. The fix is probably to raise `Lift`, which in a top-down orthographic
  plan costs nothing visually - the caution is that the plan camera can orbit, and a ribbon lifted
  far enough to always win will visibly hover at an oblique angle. Not done here: it is a change
  to a different subsystem than the one being worked on, and it wants somebody to look at the plan
  from an angle after changing it. — *2026-08-02*

- [x] ~~**There is no real river or ponds anywhere in the simulated town, despite having their real
  coordinates.**~~ DONE, and it took **no runtime code at all**. THE DECISION THIS ENTRY ASKED FOR
  WENT TO "REAL TERRAIN KIND", by a route the entry did not consider: not a rasteriser in
  `WorldBuilder`, and not a `GroundZoning` overlay, but `terrain water` tiles generated straight
  into `Content/city.txt` by `tools/relay-rossville.py` - which is already the one place real OSM
  and county data becomes the map. `Terrain.Water` was built out end to end and unused purely
  because no tile said `water`, so generating the tiles lit up the ground at -0.35m, the material,
  the bank riser, `BlocksSight` and un-walkability in one go. It cannot drift from the plan view
  either, because both are derived from the same `features.txt`. **1,009 rectangles over 44,866
  tiles** (greedy-merged the same way the ground mesh merges runs, or it would be 44,866 lines);
  44,073 survive into the world, the difference being road crossings that correctly overwrite it.

  FIVE THINGS THE WORK FOUND THAT NOTHING WAS WATCHING FOR:

  **The order in `WorldBuilder` is what makes crossings free.** Terrain patches are stamped, then
  roads, then place ground - so a road over the river stays a road. Measured on the real map:
  four separate crossings (Attica 25m, section1 16m, crossroad0 twice), and **no road runs ALONG
  the river**, which was the case that would have been ugly.

  **Twenty-one country places stood on the river and would have erased it** - four copses, eight
  cornfields, three orchards, two paddocks and a farmstead - because a place's ground is stamped
  after terrain. The generator now refuses any country lot that touches water at all. Nothing
  spilled; they moved east.

  **Every pond came out ringed by an unbroken post-and-rail fence** following each wiggle of the
  shoreline. `Prop.OnFieldEdge` counted ANY non-field neighbour as the edge of a field, which was
  the same test as "the next field starts here" right up until there was water in the corn. A
  watercourse is where a field STOPS. Fixed in Core; Release 183/185, the two being the known 2:1
  gates.

  **The country lot placer tested the wrong rectangle, and had done all along.** Whether a lot
  was in the town was `X0-40 < gx < X1 and Y0-40 < gy < Y1` - the lot's top-left CORNER - and a
  lot is 122m across, so one sitting just outside the boundary passed and then reached deep into
  the village. It never showed because the country places filled the far west first and never got
  as far as the marginal lots; taking the west column out of service for the river spilled them
  straight in. MapAudit caught it by exiting 1: **nine fields laid over Abner, York, Harrison and
  Church, and three houses on the 300 block of York Ave standing inside a cornfield.** It is a
  proper rectangle overlap now. This is the second time a place-placement bug has been invisible
  to everything until something forced placement down a path it had not taken before.

  **A headless plan render cannot show any of this.** `Materials3D.ShowGroundColour` is
  `!Application.isBatchMode && ...`, so it is hard-false in every batch run, and the committed
  snapshot set is the dark survey plan. Verifying there would have used the one path that bypasses
  the thing being verified. `WaterShot` forces `ShowBuildings` on for the duration and writes
  `water-river`, `water-crossing`, `water-ponds` and `water-bank`.

  STILL OPEN, deliberately: **the river's 12m width is the one invented number** - OSM gives a
  centreline with no width - so it is `RIVER_W` in the generator and nothing else about the
  river's course is guessed. **A road over the river is at-grade**, with no bridge deck or
  parapet; the water simply stops square at the carriageway. And the shoreline is quantised to
  1m tiles, which at eye level on the bank reads as a staircase. — *2026-08-02*

- [x] ~~**There is no real river or ponds, superseded note kept for the reasoning.**~~ `Content/features.txt` carries the actual North Fork Vermilion (`river ...`, one
  long real polyline) and the real school ponds (`water ...` closed polygons), pulled from
  OpenStreetMap - and `CityOutlines.Features()` is the ONLY thing that ever reads that file. It
  draws them as a flat painted line/fill in the survey-plan view (`CityOutlines.Build`, gated
  `!ShowBuildings` in `VillageHost.cs`), which is decoration only. `Content/city.txt` - the map
  that actually gets simulated - has ZERO `terrain water` tiles anywhere (checked directly:
  `grep -n "terrain water" city.txt` is empty). `Terrain.Water` is fully built out and unused on
  this map: `HeightOf(Water)` sits a real ground at -0.35m, `Materials3D.ForTerrain(Water)` has
  its look, riser logic closes the bank - all of it currently only exercised by the Ash, the
  FICTIONAL river on the old 170x120 `village.txt` map nothing loads any more.

  So the real river and the real ponds you can see traced correctly in the plan view are not
  actually there when you look at the ground - no bank, no reflection, nobody can fall in, and a
  path across the school field walks straight over open water that isn't drawn.

  THE DECISION THIS NEEDS BEFORE ANYONE BUILDS IT: does real water become a TERRAIN KIND
  (`Terrain.Water`, decided in `WorldBuilder`/`TileGrid` from features.txt at load time,
  alongside city.txt's own painted terrain) or a GROUND LOOK overlay in the `VillageMesh`/
  `GroundZoning` style (visual only, no change to `TileFlags.Water`/`BlocksSight`/pathing)? The
  first is more honest - water should probably block sight and stop a person walking through it -
  but it touches `Core.World.TileGrid` and anything that already reads `Terrain.Water` for game
  logic, not just the renderer, and needs its own careful test pass the way the traffic and
  parking fixes above did. The second is safer and faster (a rasterizer that reuses
  `GroundZoning.EnsureGrid`'s exact "walk each shape's own bounding box, point-in-polygon per
  tile" technique against the river polyline's own width and the pond polygons) but ships
  good-looking water nobody can actually get wet in or blocked by, which may or may not be worth
  having on its own.

  NOT ATTEMPTED tonight on purpose - real coordinates are sitting right there and the conversion
  is not hard, but it is a world-model decision, not a rendering tweak, and it deserves a look at
  the result before it is called done the way every other visual system in this project gets one.
  — *2026-08-02*

## Roads

- [x] **A road that ENDS on another road makes no junction, and there are ~21 of them.**
  `RoadNetwork.Crossings` finds a junction by the projected lateral FLIPPING SIGN as one centre
  line passes THROUGH the other. A road that stops dead on another leaves on the side it arrived
  on, so it never flips, so no junction is recorded. With no junction, `LaneGraph` makes both
  approaches `IsExit`/`IsEntry`, `CityTraffic.cs:842` skips `MayCross` entirely on an exit
  segment - no signal, no give-way, no turn claim - and `Blocked()` skips the pair because the
  headings differ by more than 45°. Nothing at all separates the two streams.

  **Confirmed by measurement, 2026-08-07.** Maple's east end and Grove's north end are the same
  point in `Content/roads.txt` (`1303,1475`, meeting at ~70°); their lane centre lines genuinely
  intersect at ≈(1302.8,1477.0) with a minimum separation of **0.002 m**. That is
  `NoTwoVehiclesOccupyTheSameSpace` failing at 0.40 m, 132.3 m from `grove x gilbert` - and 132.4 m
  is exactly where that junction is. **28 road-end-touches-road cases across ~21 distinct
  corners**: grove/maple, grove/henderson, harrison/york, goodwine/holmes, abner/park,
  abner/perry, watson/stufflebeam, stewart/railroad, smith/henderson, thompson/grove, earl/grove,
  church/thompson, church/henderson, holmes/harrison, green/benton, harrison/benton, ann/perry,
  railroad/gilbert, railroad/mckibben, gilbert/chicago, perry/chicago. Fixing one moves the
  failure to the next.

  **THE PROJECT ALREADY LEARNED THIS ONCE.** `RoadGeometryBaselineTests` records it in as many
  words - *"twenty-six T junctions silently broken... Forty-three road ends were then extended to
  their cross street and 16 ft past it, because an end that stops ON a centreline only touches it
  and the intersection finder wants a real crossing."* That hand-extension went into
  `Content/city.txt`. **`tools/build-roads.py`, which generates the survey network that replaced
  it, has no such step**, so every one of them came back.

  **DO NOT just add an overhang in build-roads.py without measuring.** That was tried against the
  real data on 2026-08-07: 46 ends moved, junctions 64→83, and lane pairs closer than 2 m went
  21→5 - but **three of those five did not exist before**. It does not restore the invariant, it
  manufactures new instances of the same fault. The durable version is a `Touches` rule beside
  `Crossings` in Core, where an end landing on another road's centre line is a junction in its own
  right; that is untried and needs the same measurement before it is believed.

- [x] ~~**THE PEOPLE ARE HALF THE FRAME.**~~ **FIXED 2026-08-08 — 88 fps to 219 fps.**
  `AgentMeshView` now animates only the nearest `AnimatingBudget` (150) people; the rest hold a
  pose. Measured by `PerfCensus` on the same camera, both runs with drift under 15%:

  ```
    before   baseline 11.3 / 12.8 ms   (88 fps)    1385 animating
    after    baseline  4.6 /  4.1 ms   (219 fps)    ~150 animating
  ```

  **A FIXED RADIUS IS THE WRONG SHAPE AND THE NUMBER IT PRODUCES IS A LIE.** Eighty metres read
  1.4 ms / 712 fps - and `0 of 1385 animating`, because `OrbitCamera` opens at 330 m. That is not
  an optimisation, it is a static town, and it took `WhyAreThePeopleNotAnimating` red for exactly
  the right reason: with nobody animating the walk rate is 0.00x and the test guarding against
  skating feet has nothing to measure. **The animating count is what caught it** - the timing alone
  looked like a triumph.

  So the budget is a HEAD COUNT with the radius chasing it by feedback (no per-frame sort of 1,385
  people). Cost is bounded at any zoom, the nearest people always move, and the town is never
  still. Measured converging to 161 / 151 / 136 as the day moves.

  Still open, smaller: a person beyond the budget holds a pose while still SLIDING along the ground
  if they are walking. Invisible at overview range, potentially not at street level with a big
  crowd - worth a look before the budget is raised or lowered.

- [x] ~~**THE PEOPLE ARE HALF THE FRAME. THE CARS ARE NOT THE PROBLEM AND NEVER WERE.**~~
  Measured 2026-08-08 by `PerfCensus.WhatIsEatingTheFrame`, which switches one layer off and
  straight back on inside a SINGLE run, so drift cancels:

  ```
    baseline start   median 11.3 ms   p90 18.2 ms   (88 fps)
    baseline end     median 12.8 ms   p90 17.7 ms
    DRIFT +1.5 ms - under 15%, so the numbers hold

    People OFF        5.9 ms   saves  6.2 ms   1385 renderers
    Traffic OFF      11.1 ms   saves  1.0 ms
    Driveways OFF    13.4 ms   saves -1.3 ms
  ```

  **1,385 animators cost more than everything else put together.** The 611 parked cars measure
  NEGATIVE, i.e. indistinguishable from zero. A whole session was spent optimising car meshes
  before anybody measured which layer was expensive.

  **THE OBVIOUS FIX IS ALREADY TRIED AND REVERTED - read `AgentBody.cs:204` before repeating it.**
  `AnimatorCullingMode.CullCompletely` stops people dead: a disabled animator does not update the
  bounds that decide visibility, so a figure that falls out of view never comes back. Measured at
  the time: 40 of 40 animators had not advanced a frame in a whole second. `CullUpdateTransforms`
  is what is there now, and it still evaluates every state machine and every clip off-screen -
  it only skips writing the bones.

  **What that failure actually says is that the cull must not be keyed on RENDERER VISIBILITY**,
  because the thing being disabled is what maintains it. That is circular. A DISTANCE cull is not:
  the camera's position and a citizen's simulated position are both known whatever the animator is
  doing, so there is no state to get stuck in. Wants: `animator.enabled = false` beyond some
  radius with hysteresis so a person on the boundary does not flicker, the check sliced across
  frames the way `CityTraffic.Update` already slices its movers rather than sweeping 1,385 every
  frame, and `PerfCensus` re-run to prove the saving.

  Worth knowing before starting: the town already has a cheap representation - `AgentMeshView`
  draws primitives where `AgentFigure` draws rigged people - so "far away people are capsules" may
  be a shorter path than an animator LOD, and it is measurable the same way.

  **Two limits of the census as it stands.** The renderer count reads 0 for `Traffic` and
  `Driveways` because those register through the `Action<bool>` overload rather than by root, so
  `Layers.RootsOf` finds nothing - the timing is right, the count is not. And only three layers
  were probed: the rest were not wired or not on in that configuration, so Trees, Buildings,
  Districts, Houses, Streets, Lamps, Powerlines, Farm and Massing are UNMEASURED, not cheap.

- [x] ✅ **DONE 2026-08-10 — THE FLEET IS A CURVE.** `CityTraffic.CarsOutByHour`, a 24-entry table
  summing to a mean of **19.3** against IDOT's measured 19.3, peaking at **46** at 07:00 and 17:00.
  Built once at the peak and garaged; `CityTraffic.Retime` holds it to `Sim.Clock.MinuteOfDay` from
  `VillageHost.Update`. The second half of this item — the class weighting — **had already landed**:
  `AmbientTrafficWeight(RoadLine)` reads the county's AADT and `CityTraffic` calls that overload, so
  the "do not cut the fleet until the weighting is fixed" caveat below was stale when it was read.
  ⚠ **The PlayMode gate has NOT been run against this**, and the noon-start trap named below is now
  live: the sim opens at 24 cars where it used to open at 159.

  *Original entry, kept for the measurement:*

  **THE FLEET IS EIGHT TIMES TOO BIG, AND IT IS WHY THE JUNCTIONS STARVE.** Measured against
  IDOT's own counts, 2026-08-08 — full working in `docs/research/TRAFFIC-COUNTS.md`.

  `CityTraffic.CarsOutPerHousehold = 0.25` puts **159 cars** on the street against 624 households,
  all day, flat. Rossville's real counted traffic supports **~19 moving at an average instant** and
  **~46 in the peak hour**. That is a multiplier of **0.03 off-peak and 0.07 at peak**, not 0.25.

  The constant's docstring is right about what the number MEANS - "not how many cars a household
  OWNS, but how many are ON THE ROAD AT ONCE" - and wrong about the value, which was reasoned
  ("roughly one household in four") rather than measured. It is one household in thirty-three.

  **Two changes, not one.** The value is too high AND it is a constant: the real thing is a curve,
  peaking when the town leaves for Hoopeston and Danville and nearly flat at midday. The sim already
  knows when the men leave. Feeding `DayPlan`'s away-work curve into the fleet size gets the shape
  for free, and a town whose traffic has a rush hour is worth more than one that has the right
  average.

  **Do not do this as a lone constant edit.** Dropping to 19 cars with the current class weighting
  spreads them evenly over a network where 80% of the movement belongs on two roads, so the town
  would read as empty everywhere rather than quiet-with-a-busy-Route-1. **That half is now done** -
  see the entry above - so the blocker is cleared and this is the next thing to do.

  **NOT ATTEMPTED 2026-08-08, ON PURPOSE, AND HERE IS THE TRAP.** The owner chose "measured, with
  a rush hour" and this was deferred rather than half-landed unattended, because it collides with
  the test suite in a way worth knowing before starting:

  `VillageHost` starts the simulation at **noon**, which is OFF-PEAK — so a correctly implemented
  curve puts about **19 cars** on the map for every PlayMode test in the run. `TrafficMovesAndStops
  AtRedLights` needs a car to arrive at the town's **single** signalised junction while it is being
  watched, and `NoTwoVehiclesOccupyTheSameSpace` needs enough traffic to be worth asserting. Both
  are likely to fail on an honestly quiet town, and the fix is NOT to inflate the fleet back: it is
  that a test about signals should watch the junction at rush hour, and the shared-city rule means
  it cannot simply wind the clock (the city is built once per run and a clock will not go
  backwards for the next test).

  So the work is three things, not one: the curve; a way for a traffic test to observe a chosen
  hour without mutating the shared clock; and re-baselining the traffic gates against a town that
  is genuinely quiet at noon. Cheapest shape for the curve itself is to build the fleet at the
  PEAK figure and deactivate the surplus off-peak - the same trick `CityDriveways` uses for cars
  whose owners are at work - rather than spawning and despawning movers.

- [x] ~~**`AmbientTrafficWeight` is 4:1 where the county measures 21:1, and Route 1 is not Attica.**~~
  **DONE 2026-08-08.** `Content/roads.txt` carries an `aadt` line beside `easement` on all twelve
  counted road runs, parsed by the same `VillageParser` the survey pass already uses, through
  `RoadRun.Aadt` → `RoadLine.Aadt` → `RoadClasses.AmbientTrafficWeight(RoadLine)`. Both readers -
  the spawn pitch and the turn scoring - ask the road now instead of its class. Measured in
  `TrafficWeightTests`: **Route 1 42 : side street 2 = 21.0 : 1**, against the class ladder's 8 : 2.
  An uncounted road keeps its class weight, because IDOT not counting a road is weak evidence that
  it is quiet and not a measurement. Alleys stay at 0. Original entry follows.

- [x] ~~**`AmbientTrafficWeight` is 4:1 where the county measures 21:1, and Route 1 is not Attica.**~~
  `RoadNetwork.AmbientTrafficWeight` is `Mainroad 8 : Street 2`. IDOT's counts give **Route 1 5,200
  AADT, Attica 1,100, a side street ~200-250** - so 21:1 arterial-to-local, and Route 1 carries
  **4.7x what Attica does** while the table hands them the identical weight of 8.

  Measured share of vehicle-miles: **real 80-86% on the two main roads, game 55.5%** - the game puts
  **2.25-3.26x too much traffic on the side streets**. That is the owner's point exactly: the side
  roads exist to get a car from a house to Route 1 or Attica and out of town, not to be driven
  around on.

  **The durable fix is to stop proxying through the class.** `Content/roads.txt` already carries
  measured `easement` and right-of-way per road because this project takes numbers from the survey
  rather than from a ruler - and IDOT counts **twelve of these roads by name** (chicago, attica,
  stewart, stufflebeam, benton, henderson, creative, summit, mckibben, church, dale). An `aadt` line
  beside `easement`, read by the spawn pitch and the turn scoring, replaces a guessed class ladder
  with the county's own measurement. The 21 uncounted roads take a floor.

- [x] **Nobody's car is in their driveway.** `CityParking` fills only authored `carpark` places. Its
  own docstring makes the argument and then stops short of the houses: *"A PARKED CAR IS CONTENT IN
  THIS GAME... a city where every car is driving past is a city where nothing is ever anywhere."*

  If ownership scales with households and movement does not, the difference has to be standing
  somewhere: **roughly 600 cars owned, ~19 moving.** The overwhelming majority of Rossville's
  vehicles are parked at a house at any moment, and the game draws none of them. For a game about
  noticing what changed, a car that sat in a driveway on Tuesday and is gone on Wednesday is worth
  more than any number of cars driving past. Wants: a driveway or kerb spot per dwelling from the
  parcel geometry, a car that belongs to a household rather than to the road, and the ability for it
  to be absent because its owner is at work in Hoopeston.

- [ ] **`church x maple` is two county roads crossing on give-way, and a car starves there.**
  > ⚠ **EVERY NUMBER BELOW IS AGAINST A FLEET THAT NO LONGER EXISTS.** The 159-car constant became
  > `CityTraffic.CarsOutByHour` on 2026-08-10: the town now opens at **25** cars at noon and peaks
  > at 47. A wait time measured on 159 cars says nothing about this junction under 25, and the
  > give-way fault may not even reproduce. **Re-measure before working this item**, and per
  > `CLAUDE.md` run it twice before believing a traffic number moved.

  Measured 2026-08-08 on the 159-car fleet: `NoCarWaitsForeverAtTheHeadOfAClearQueue` now
  attributes every wait longer than one signal cycle to its junction, and the whole tail is **two
  junctions, not the fleet** - 2 cars up to **53.7 s** at `church x maple`, 1 car at 36.3 s at
  `church x alley2`, 0 at the signals. So this is the give-way rules, not a town over capacity.

  `church` (`970,849 1303`-ish N-S) and `maple` (`797,1481 1303,1475` E-W) are BOTH declared
  `county, right of way 20.0 m` in `Content/roads.txt`. Two county roads meeting, and the crossing
  is run on **priority** - the town has exactly **1 signalised junction out of 74**. A car on the
  give-way arm waits for a gap in a continuous county-road stream and may not get one.

  **`TurnPace` already documents this failure mode and saturates before it helps.** A waiting
  driver launches harder the longer it waits - which is honest, and is why it is used by both
  `WhenClear` and the motion itself - but `Patience = 25 s` caps the effect. Past 25 seconds the
  car is as eager as it can get and just sits. The docstring's own example is "a car starved at
  Ross and Attica for 111 of the 120 seconds it was watched".

  **DO NOT fix this with a "go anyway after N seconds" timeout.** That was tried here and REVERTED
  because it produced a real 0.00 m collision: "go anyway" is precisely "enter the junction in
  front of a car that is still coming". Candidate fixes worth measuring instead: signalise (or
  four-way-stop) major/major crossings so `InTheTown` is not the only route to a light; give the
  priority stream platooning so gaps exist at all; or a courtesy rule where a queued major-road
  car yields. Each is a content-or-model decision, not a threshold.

  **Consequence today: the p90 arm of that test is a coin toss** - 37.2 s and 21.9 s measured on
  an unchanged tree with the same fleet and the same test order. Do not tune the gate; it is
  already the honest 36.0 s. See CLAUDE.md.

- [ ] **Curved roads on the outer parts - the map is too square.** The pack HAS the
  pieces: `Road_Turn_20x20_City`, `Road_Turn_Shift_20x20_City` (an S-bend), and
  `Road_Dirt_A/B_Turn_20x20m` for lanes, all found and unused. The blocker is not
  art, it is the lane graph. `RoadLine` already stores a POLYLINE (`Points`) and
  computes `IsStraight` from it, so a bent road can be authored today - but
  `LaneGraph` skips any line where `!IsStraight` (LaneGraph.cs:164) and junctions
  only form between axis-aligned N-S and E-W straights (RoadNetwork.cs:195-200).
  So a curved road would be DRAWN and carry no traffic and meet nothing. Work is:
  lanes along a polyline, junction detection that is not axis-aligned, and
  CityStreets walking the points to drop turn tiles at the corners. Worth doing
  for the country lanes first, where the traffic is thinnest and the squareness
  is most obvious. — *2026-08-01*

- [x] ~~The outer city.~~ **BUILT.** Map 960 -> 1290, downtown 6x6 untouched, a suburb ring of 28
  cells, and 270m of country on every side. `Assets/Noir/Unity/CitySuburb.cs`: **272 houses, 179
  garages, 102 cars on drives, 2,016 lengths of hedge** across the 28 cells. Everything already
  built moved +120 by script - not half the growth, because the town was centred on 525 and the new
  map's centre is 645. 83 junctions against 84 before, which is the flat junction count the road
  grid was stopped at the suburb ring to get.

  THREE THINGS THE BUILD FOUND THAT NOTHING WAS WATCHING FOR:

  `CityDistrict.TownX` was `const 525f` and went silently wrong the moment the town moved. Nothing
  failed - blocks still built, they just ranked against a point 120m away, so storeys fell off
  towards the wrong edge and shopfronts faced the wrong way. 662 buildings became 580 and four
  towers became seven, with no error anywhere. It is asked of the map now.

  `MeshReadable` was missing FOUR renderers - CityParking, CitySigns, CityDistrict and the new
  CitySuburb - so the parking tiles, every road sign and all seven market shopfronts had never
  been made readable and the chunker had been silently leaving them alone. Adding them took the
  bake from **13,791 renderers to 5,206**. The 960 map baked to 4,462, so the entire outer city
  costs +17% renderers for +81% area.

  MapAudit caught two of my own eastern-strip lots straddling an east-west corridor - the east
  strip is cut by those roads and the west strip is not - and it caught them by EXITING 1, which
  it could not have done this morning.

  Fleet 236 -> 306 and the traffic is unmoved: median wait 15.8s against 17.0s on the old map, p90
  24.6s against 22.7s, worst 53.9s against 53.6s. `units 10` on a suburb cell is measured (272
  houses over 28 cells), not liked. PlayMode 11/11, Core 163/165, MapAudit clean on all eight.
  Three new stills: `suburb-street`, `suburb-block`, `suburb-edge`. — *2026-07-31*

- [x] ~~The outer city, superseded note kept for the reasoning.~~ The fork was settled at
  `docs/superpowers/specs/2026-07-31-the-outer-city-design.md` and was QUEUED BEHIND THE JAMS ITEM: 28 suburb cells is ~450 households against the 945 declared
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
- [ ] `Modular Parts/Rails` is a 6-piece GROUND-LEVEL tram kit (1/3/5/10m plus turns), unused and quite separate from the elevated railway that is commented out in city.txt. **NO LONGER BLOCKED ON LAND** after the 1290 re-lay. What it needs is a ROUTE, and that is a decision about where people go rather than about where there is room - the lane graph is public on `CityTraffic.Graph` for exactly this. — *2026-07-30*

- [x] ~~**The real CSX line has correct real-world alignment and is invisible the moment the game
  leaves plan mode.**~~ DONE - `Assets/Noir/Unity/CityRailBed.cs`. **4,790m of track, 6,694 ties**,
  ballast crown and shoulders, two rails, following the real surveyed polyline. Built as generated
  geometry rather than from the pack's tram kit, for the reason the note gave. It reads the SAME
  curve the survey plan does: the parse and the Catmull-Rom moved out of `CityOutlines` into a new
  `MapFeatures`, so there is one reader of `features.txt` on the Unity side rather than two that
  can disagree about where the railroad is.

  A LEVEL CROSSING IS NOT AUTHORED, IT IS ASKED. Rather than matching the four `crossing` entries
  to positions - a second opinion about where a road is - the bed asks the map what it is standing
  on: ballast and ties stop, the rails carry on flush. The four surveyed OSM crossings are exactly
  where it fires, because that is where the real roads are.

  FOUR THINGS THE RENDERS CAUGHT, none of which a test could:

  **The crown never drew at all.** The widest and most visible part of the bed was a quad passed
  the same offset for both its edges - zero area, silently dropped. **And all three ballast strips
  plus both rail flanks were wound face-down** and backface-culled. Correct geometry, correct
  material, correct normals, invisible. The whole bed rendered as two dark lines and read as a
  scratch on the field. The rule is written down now: a cross-section runs low offset to high, or
  its face points at the ground.

  **Trees were growing between the rails.** Scatter is decided in Core off terrain, and a railroad
  corridor is not a terrain kind. `CityRailBed.OnRightOfWay` clears 6.5m either side off the same
  polyline the bed is built from, and `CityGreenery` asks it.

  **The line runs off the map and MeshChunks said so.** features.txt carries it to y=2674 on a
  2400 map - correct, Hoopeston is six miles up the line - and the chunk grid was declared over
  the map, so two kilometres of track got clamped into the edge chunks and could never be culled.
  The grid is declared over the geometry now.

  **Borrowed materials were the wrong objects.** Ballast was `Stone`, which is a weathered wall,
  and the bed disappeared into the grass; rails were `Ironwork`, which is a lamp column and reads
  as black. There is now a `Ballast`, a `Sleeper` and a `RailSteel`.

  Stills: `rail-track`, `rail-crossing`, `rail-alignment`, `rail-town`. NOTE FOR WHOEVER LOOKS:
  there are genuinely **two parallel ways** at the south-east end - that is what OSM has, a siding,
  not a bug. Still open: no crossbucks, gates or lights at the four crossings, and no train runs on
  it. Whether it should carry real freight is the separate, smaller decision the old note called
  out, and the bed exists for it now. — *2026-08-02*

- [x] ~~**The real CSX line, superseded note kept for the reasoning.**~~ `Content/features.txt`'s `rail` polyline is the actual surveyed CSX
  right-of-way (converted from OpenStreetMap - see the note in `city.txt` itself at the "NO
  SIMULATED RAILROAD AVENUE" comment: getting this alignment right already fixed a quarter of the
  town's houses being on the wrong side of the real track). `CityOutlines.Features()` draws it
  correctly, with tick marks for ties and short cross-ticks at the four real OSM level crossings -
  but only as a flat painted ribbon in the survey-plan view, and `CityOutlines.Build` is called
  from `VillageHost` ONLY when `!ShowBuildings` (line ~272). `CityRail.cs`, the OTHER rail system
  in the project, is a completely different thing - an ELEVATED URBAN "El" with a station and a
  running two-car train - gated on a `place railway` in city.txt that does not exist (deliberately
  commented out, per the same comment). So: turn `ShowBuildings` on for real gameplay, as the
  project is clearly heading towards, and the real, correctly-aligned CSX line - the one thing
  Rossville people would recognise on sight - simply is not there. Nothing paints an at-grade rail
  bed (ballast, two rails, ties, a level crossing with gates or lights at the real OSM crossing
  points) anywhere the dressed game can show it.

  A real at-grade line is a much better fit for Rossville than the El kit - this is a single-track
  branch through a farm town, not a Chicago transit line. Proposed shape: a small renderer that
  reads the SAME `rail` polyline `CityOutlines` already parses (do not re-derive the coordinate
  transform, reuse it), walks it in short segments the way `CityOutlines.Smoothed` already
  smooths the polyline for drawing, and drops a simple ballast-strip + rail-pair mesh along it
  (generated geometry in the `VillageMesh`/`Frontage` style, not a bought prefab - the pack's kit
  is ground-level and city-styled, wrong era and wrong country same as the El). Grade crossings at
  the four real OSM points already tagged in `features.txt`. Whether this should also carry a
  real freight train the way `CityRail`/`CityTrain` does is a separate, smaller decision once the
  bed itself exists. NOT ATTEMPTED tonight - this is real art/geometry work on the town's most
  recognisable real feature, and it deserves the same "render it and look before calling it done"
  treatment the ground-mesh work above got, which means it wants a session where the result can
  actually be looked at rather than one where it can only be described. — *2026-08-02*
- [ ] `Racetrack` is 152 prefabs - 25 road pieces, 91 fences, a control gate, an overpass - plus 79 racing cars excluded from traffic because there is nowhere to race them. **THE LAND EXISTS NOW**: the 1290 map's north-east corner is 270x270 with no road through it, which is exactly what this was waiting on. What it still needs is a track BUILDER - the kit is 25 modular pieces, so laying one is a CityDistrict-sized job rather than a placement, and it was left out of the outer city deliberately for that reason rather than forgotten. — *2026-07-30*

## Traffic

- [x] ~~Traffic budget is per HOUSEHOLD and a `district` has no residents~~ FIXED. A district now declares `units 29` in city.txt - MEASURED, not liked: CityDistrict reports 662 buildings across the twenty-three blocks it fills with a perimeter, which is 28.8 apiece. Core reads it through the new `WorldModel.DeclaredHouseholds`, deliberately a SECOND number rather than a wider definition of `Households`: a district has no rooms and no interiors, so making it a home would hand PopulationGenerator hundreds of citizens to house in a paved rectangle. `Households` and the simulated population are untouched; only things that scale to how busy the map is read the new one. `CarsPerHome = 1.5` is gone, replaced by `CarsOutPerHousehold = 0.25` - and the point is that the multiplier now MEANS something rather than covering for a different number being wrong: not how many cars a household owns, but how many are on the road at once, the rest being parked or never owned. DENSITY DELIBERATELY HELD: 945 declared households x 0.25 = 236 vehicles against 243 before, because jams are still an open problem below and doubling the fleet overnight would have made a known fault worse. The knob that moves density is now `units` in content, not a constant in code. Core gate builds, MapAudit clean, PlayMode 7/7. — *2026-07-31*

- [x] ~~**A car crossing a junction ignores every other vehicle.**~~ FIXED: CrossJunction now calls Blocked(), and inside a junction the same-heading filter is dropped because a turning car crosses the others rather than following them. `Blocked()` is only called from `RunSegment`; `CrossJunction` has no check at all, so a turning car drives through anything on the lane it is entering. This is what `NoTwoVehiclesOccupyTheSameSpace` catches at 0.00m, and the look-ahead fix does not touch it. — *2026-07-30*
- [x] ~~`NoJunctionEverShowsGreenBothWays` tests an invariant that no longer holds.~~ FIXED: the test now skips unsignalised junctions in both assertions. `MayEnter` returns true on BOTH axes at a priority junction by design - the separation moved into `NothingCrossing`. The test should assert the new rule: signalised junctions never both green, priority junctions have exactly one axis with priority. — *2026-07-30*
- [x] ~~Vehicle look-ahead is a constant 8m~~ — done in 4724abf: it is now both vehicles' measured half-lengths plus a headway. Did NOT fix the failing test; see the junction item above. — *2026-07-30*
- [ ] **The eastbound ring road at x=1008 is the one junction that starves.**
  > ⚠ **SAME INVALIDATION AS `church x maple` ABOVE**: the 83.9 s and 86.5 s below were measured on
  > the 159-car fleet, and the fleet is a curve now — 25 at noon, 47 at peak. "A property of that
  > junction and not of the load" was a fair reading at 159 and is untested at 25. Re-measure.

  Every run of
  `NoCarWaitsForeverAtTheHeadOfAClearQueue` puts its worst wait at the same place - 83.9s and
  86.5s on consecutive runs, against a fleet p90 of 22-25s - so it is a property of that junction
  and not of the load. It is a left turn across two lanes of through traffic on the busiest road
  on the map, and `NothingComing` wants 22m of clear road before it will go. The gate has been
  widened to three signal cycles so it stops flapping, which hides nothing: the number to watch
  is the p90, and this is recorded so the tail is not mistaken for noise later. Worth either a
  filter lane, a signal, or letting a car that has waited two cycles take a smaller gap. — *2026-07-31*
- [x] ~~Semi trailers drive the freeway with no cab.~~ TOOK THE CHEAP FIX: dropped
  `Car_Truck_Trailer_Modern`, `Car_Truck_Trailer_Container_Large` (+B..F) and
  `Car_Truck_Trailer_Car_Modern` from `CityTraffic.Everyday`'s freeway list, keeping only
  `Car_Truck_Trailer_Sleepercab_Modern` (+B..F) - the one complete unit of the four, confirmed by
  listing the actual prefab files rather than trusting the note. THE PROPER FIX - towing a real
  trailer behind its own tractor unit, a second `Mover` pinned a fixed offset behind the first - is
  still not done and is the better long-term answer if articulated variety is wanted back; this
  just stops the driverless-box sighting tonight, at the cost of the freeway's heavy traffic being
  a little less varied. Not gated/measured beyond compiling - a content-list change, nothing about
  the traffic MODEL moved. — *2026-07-31, cheap fix taken 2026-08-02*

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

- [x] ~~Jams~~ SUPERSEDED, kept for the reasoning: the original entry follows.

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

- [x] ~~Two buildings standing on the same ground, at every block corner.~~ FIXED, and it was the
  same arithmetic slip in two files. A block's end runs take the full width and are `Depth` deep;
  its side runs have to begin past that. `CityDistrict` began them exactly AT `lot.Y + Depth`,
  which is right to the millimetre in lot terms and wrong in geometry - the modules are 6.10 across
  on a 6m pitch, so a building overhangs its own lot by 5cm at each end and the bottom section
  carries its tail back the full depth, and abutting therefore meant overlapping. `CitySuburb` was
  worse: it began them past `Pitch` (14) when the end runs occupy `Setback..Setback + Depth`
  (12..19), so the first house of every side run stood inside the last house of the end run, and
  three garages ended up in a neighbour's front room for the same reason. **298 overlapping pairs
  -> 0, over 3,798 buildings.** Found by `StackProbe.Overlaps`, which builds the city and compares
  real footprints - MapAudit cannot, because its checks are arithmetic on the AUTHORED layout and a
  district or suburb building is decided at build time. Same blind spot that let overlapping parked
  cars be reported twice with the audit clean both times. — *2026-07-31*

- [x] ~~The street lamps are a cylinder with a box on top.~~ FIXED - `SunRig.BuildStreetLamps` now
  places the pack's own `Lamp_Street_*`. It was `PrimitiveType.Cylinder` for the column and
  `PrimitiveType.Cube` for the lantern, which was the right answer while Rossville had no pack
  behind it and the alternative was light appearing out of nothing five metres up. The pack ships
  SIXTEEN lamps in `Lamps City` and none of the four tall `Lamp_Street` ones had ever been placed -
  `CityStreets` only catalogued the short `Lamp_Sidewalk` kind as pavement dressing, so the city had
  bought lamps on its pavements and a grey box on a stick over its roads. The bought lamp carries
  its lens on a second submesh (`M_Universal_Glass_Night`), so the night glow now drives that
  submesh alone rather than tinting the ironwork with it. Which way a lamp faces is asked of the
  road network so the arm reaches over the carriageway. — *2026-07-31*

- [x] ~~**The front steps stop a metre short of the front door.**~~ FIXED, and it was in `Place`,
  not anywhere the four earlier theories looked. `Place` returned the section's total vertical
  EXTENT and stacked the next storey on it. For four of the kit's five pieces that is the same
  number; `Squarehouse_Bottom_A` and `Bayhouse_Bottom_A` are semi-basements with an AREA RAILING
  round the top, and the railing stands proud of the floor slab. MEASURED, per section, by the
  highest sizeable upward-facing surface:

      Squarehouse_Bottom_A   floor 2.00m, extent 3.01m   ->  1.01m of railing
      Bayhouse_Bottom_A      floor 2.00m, extent 2.68m   ->  0.68m
      every other section    floor and extent identical  ->  0.00m

  So every residential building above its basement floated a metre clear of it, the stoop landed a
  metre below the door, and the basement read as a separate tan plinth in front of the facade. It
  only showed on RESIDENTIAL frontages, because a shopfront ground floor is a `Squarehouse_Market`
  piece with no railing - which is why the isolated shopfront stack looked right and sent the
  investigation the wrong way twice. `Storey()` now finds the floor by looking for the highest
  upward-facing surface of at least a sixth of the section's plan, so a railing cannot be mistaken
  for a floor and the answer follows the mesh rather than a table of section names. Gates: PlayMode
  11/11, Core 163/165, MapAudit clean, PickCheck 48/48 from above and 46/46 from the street.
  `probe-join-Squarehouse.png` and `probe-section-*-plusZ.png` show the join and which section owns
  the steps and which the door. — *2026-07-31*

- [x] ~~**The tan slab on every building front.**~~ FIXED: found what the geometry was, per-submesh. `Squarehouse_Bottom_A`'s brick and glass end at z 3.01 but its `M_Universal_A` submesh runs to 5.96 - and the OTHER sections in the same stack (Entrance, Mid, Roof) carry nowhere near as much of a tail past their own brick (0.1-0.6m, not 3m), so this is a defect in the Bottom piece specifically, not a real building depth. `Bayhouse_Bottom_A` has the same fault at a smaller scale (~1m). `Seat()` was aligning the front wall to the far edge of the WHOLE combined renderer bounds, which for a north-facing front put that unfaced tail's edge on the building line and left the actual brick wall recessed up to 3m behind it, invisible from the street - confirmed by rendering the same view before and after: flat black wall with no texture at all, vs. proper brick, at the identical camera position. This is also almost certainly why this file used to say Bayhouse is 7m deep and Squarehouse 9m: both figures come from the same whole-bounds measurement, and both families' BRICK footprints alone measure within a centimetre of each other. FIX: `CityBuildings.Seat` takes a `dressedOnly` flag; `Stack()` (the modular townhouse path) now measures by brick and glass submeshes only via `TryDressedBounds`, skipping `M_Universal_A`. `Tower()` still passes the old raw-bounds path unchanged, since skyscraper meshes have no brick or glass submesh to measure by. MapAudit clean, PlayMode 7/7. — *2026-07-30*
- [x] ~~Parking overlap was reported again after the fix.~~ FOUND AND FIXED, and it was never the scale bug. Wrote the audit that was missing - `MapAudit` check 8 now BUILDS the lots and compares real renderer bounds pairwise, because the other seven checks are arithmetic on the authored layout and a parked car is not in the layout. First run: **19 overlapping pairs out of 85 cars**, including the vans and taxis at the lot near 584,545 that the original report described and that the precinct render could never have shown. ROOT CAUSE was `Bay = 5f` - the last guessed number in the file, exactly the fault its own `Gap` comment rails against one axis over. The pack's cars are 5.0-5.5m long, a back-to-back PAIR was pitched `Bay` apart, and each car was CENTRED on its row line, so any two cars longer than 5m met in the middle and buried their tails in each other by (L1+L2)/2 - 5, which is the observed 0.24-0.61m exactly. Invisible from above because it happens BETWEEN rows, not along them. `Width()` was correct all along - verified it against the placed renderer bounds on four vehicles, agreeing to 0.00m. FIX: a car now backs onto the outer edge of its own half of the pair, positioned by its own measured length (`Long()`, mirroring `Width()`), so the two halves cannot reach each other whatever parks in them; anything too long for its half (a school bus, an ambulance) leaves the slot empty rather than hanging into the row behind. 19 overlaps -> 0, 85 cars -> 73, all eight audit checks clean, precinct render eyeballed. — *2026-07-31*

- [x] ~~Add a way to set a plot's TYPE/zoning on the parcel editor.~~ DONE - `ParcelNotes.Note` gained `Zoning` (residential, commercial, industrial, civic, agricultural, vacant), `Stories`, `Basement`, and `Housing` (single-family, duplex, apartment, apartment complex - shown only when Zoning is residential, since the others don't take a housing form). Shares the same edit/save form as the household editor in `VillageUI.DrawNoteEditor`, since both are the same parcel note; zoning cycles on click (IMGUI has no dropdown), stories are +/- like adults/kids, basement is a toggle button. Persisted as two new lines in `parcel-notes.txt` (`zoning <word>`, `building <stories> <0/1> <word>`), verified with a temporary round-trip PlayTest (save → force-reload from disk → compare → clean up), then deleted per the usual practice. MapAudit clean, PlayTests 12/12 (the one expected failure untouched). — *2026-08-01*

- [x] ~~Downtown block interiors are flat paving.~~ DONE - `CityDistrict.Interior`, 753 things across the twenty-three blocks that get a perimeter. Laid on a seven-metre lattice, one thing to a cell, and every piece MEASURED to fit inside one, so nothing can reach its neighbour and no clearance check is needed - the same guarantee the parking bays got two items up, arrived at the same way. Forty-four percent of cells stay empty (a yard packed to its edges is a car park) and one column is kept clear end to end, because a yard nothing can drive into is a courtyard. LOW BUILDINGS WERE TRIED AND DROPPED: `Squarehouse_Garage_City` is the pack's only low outbuilding and almost all of it is on `M_Universal_A` - the same sand-coloured atlas behind the tan slab - so a yard of them renders as featureless cream boxes from directly overhead, which is how this game is looked at half the time. Blank placeholder geometry is a worse answer than the bare paving it was meant to fix, so the yards are bins, skips, boxes and vehicles: things that read as themselves from above. If a low outbuilding is ever wanted here it needs a model that is not on the universal atlas. `block-yard.png` added to the CityShot set. MapAudit clean, PlayMode 7/7. — *2026-07-31*

- [ ] Age the town across the 1991-2013 window. The build targets 1991 (see
  `docs/research/THE-ERA.md`); the twenty-two years after it are a separate feature and this is
  it. The events are already sourced and dated: the February 2004 fire takes about a quarter of
  the downtown commercial block, Rossville-Alvin High School closes in 2006, and the antique
  district that carries downtown in 1991 is gone by the end. Deterioration is not only decay -
  a shop empties, a roof goes, a lot clears, but the school building stays up with something
  else in it. The owner: *"For now I want it as close to 1991 as possible. Add deterioration
  later."* — *2026-08-04*

## People

- [x] ~~The people are capsules.~~ DONE - `AgentBody`, 365 of them, bought and animated, and NO
  TWO ALIKE. The pack has about twenty figures in register for an ordinary town against a
  population of 365, so the variety is not in the prefabs: `Universal_A_Alb` is a labelled SWATCH
  GRID - 4096 square and 428KB, which is the compression signature of flat colour blocks - where
  each row is a role (primary, secondary, tertiary, hair, skin, hide). **The grid is 32 x 32 cells
  of 128px, measured 2026-08-09**, and a row is not a ramp all the way across: columns 0-19 are the
  role's twenty-step ramp, 20-29 are one flat colour repeated, 30 is the emission key (the only
  non-black column in `Universal_A_Emit.png`) and 31 is an accent. A coat colour is a UV coordinate,
  not a texture. Measured: `Man_Slavic_Summer_Hair` puts 2,841 vertices on 27 cells across 10 roles.
  Each person's mesh is cloned and every vertex nudged ALONG its own row, never across it - which is
  what makes it safe without knowing which row is which, because skin stays in the skin ramp and
  hair in the hair ramp. **What it does NOT make safe is wrapping**: a shift that runs past column 19
  leaves the ramp for the flat block or the emission key, so the shift is bounded, not a modulo.
  Hue variation needs the rows mapped first and is a later job; shade alone is plenty.

  `Citizen.Male` now exists. `PopulationGenerator` always decided it, to pick which forename list
  to draw from, and then THREW IT AWAY - so nothing downstream could tell a Margaret from a
  George, and half of them would have been rendered as men. It is also the first of the six things
  `PersonDescription` describes that the running game can actually fill in.

  Adults and children come from separate casts; elders come from the adult cast, stooped and
  shrunk by `AgentLook`, because there is NO ELDERLY FIGURE ANYWHERE IN THE PACK - which is also
  why `AgeBand` will only ever say adult or child. — *2026-07-31*

- [x] ~~Which animation a person plays was a switch statement in C#.~~ FIXED - it is
  `Content/animations.txt` now, in the same shape and for the same reason as `kinds.txt`. Adding
  an animation is a line of text: drop the .fbx in `Assets/Noir/Animations` (it configures its own
  import), name it in a row, done. A row may name SEVERAL clips and each person is given one off
  their citizen key, so a bar holds three sorts of drinker and the same person drinks the same way
  every time you look. `Noir/Check The Animations` reports both directions - clips a row asks for
  that are not downloaded, and clips downloaded that no row mentions, which is the quiet failure
  where somebody grabs Sweeping and waits forever for a caretaker to sweep. — *2026-07-31*

- [x] ~~Traffic decisions were made once per RENDERED frame.~~ FIXED - `CityTraffic` steps in
  thirtieths now and carries the remainder, capped at twelve slices so a hitch cannot spiral. The
  fault was that a car looking for a gap got ONE look per frame, so the quality of the driving was
  a property of the frame rate. Not theoretical: putting 365 rigged people in the town lengthened
  the frame enough that one lane of the eastbound ring road starved - median wait and p90 did not
  move at all, and the worst single wait went 54s -> 120s, three runs running, every one of them
  at x=1002. With fixed slices it is 57s and both the median and p90 came out better than before.
  Same fault the leg swing had, and the same lesson: a renderer may know the frame rate and
  nothing that DECIDES anything may. — *2026-07-31*

- [ ] **Decide whether a sleeping villager is a witness.** `ObservationDiagnostic` walked a
  player past thirty front doors and found MORE people see you at 02:00 than at 09:00 - 148 of
  148 against 135 of 148. The cause is that `Recollection` skips a witness only while their
  block is `TravellingTo`, so at two in the morning the whole village is `Asleep` at home,
  standing at its own front door, watching the street. At nine the ones who are out or between
  places drop out and the number FALLS. Not patched, because it is a design question with the
  investigation hanging off it: a light sleeper at a front bedroom is a real witness and
  "everyone in bed sees the street" is not. Same class of deliberate gap as the walking-witness
  limit in `Recollection`. See `docs/plans/observation-wiring.md`. — *2026-08-05*


## Story

- [x] ~~`Survival` is 174 prefabs and nothing has ever placed one.~~ DONE - `Assets/Noir/Unity/CityStory.cs`,
  six sites and nineteen pieces. AUTHORED, NOT SCATTERED, which is the whole of it: the note above
  was right that where a bear trap goes is a story decision, so each site is a `place` in city.txt
  with its own name and its own sentence, exactly like a shop. That also makes every one of them
  clickable, describable and something the simulation can refer to - which a prop rolled onto a
  tile is not. Three new kinds: `memorial`, `standing`, `camp`. The register is the roadside and
  the treeline rather than a horror set - two crosses on verges, two platforms overlooking fields,
  two camps with the fire out. A cross faces the ROAD, and which road is asked of the network
  rather than authored, so it keeps facing the traffic if a road is ever moved. The seat above
  Wicker End looks at the orchard and at the back of the house, which is a choice somebody made.
  Still unplaced from that folder: the radio transceiver, the signposts and the explosive barrels,
  none of which have a story yet. — *2026-07-31*

- [ ] Deduction as recipes: a corkboard where pinning evidence in a *shape* produces a lead. The Crafting System's `TableRecipe` is already position-aware rather than only contents-aware, and `ISatisfier` is the "do these inputs match this pattern" abstraction. Build it in Core against `particulars.txt`. — *2026-07-30*

## Tech

- [x] ~~`-quit` and `-runTests` MUST NOT be combined~~ FIXED: `Assets/Noir/Editor/TestInvocationGuard.cs` now catches the combination in batchmode and exits 1 with a clear error instead of letting Unity exit 0 silently. CONFIRMED by direct reproduction: the raw combo logs "Batchmode quit successfully invoked" before any test callback fires and writes no results file (exit 0); with the guard it now exits 1 before that race even starts; the correct invocation (no `-quit`) is untouched — reran the PlayMode suite and got 7/7 passing. Root cause: `-quit` and the test runner's start-up both hang off `EditorApplication.update`, and `-quit` wins the race. — *2026-07-30*

- [x] **Two Core tests are failing and were already failing.** `TwoToOneTests.TheMedianVillagerYieldsTwiceAsMuchTextureAsUse` (wants a ratio >= 2.0) and `TheTenthPercentileIsNotALock` (wants >= 1.0). Found while running the gate for the district work; CONFIRMED pre-existing by stashing that work and running them at HEAD, where they fail identically, so nothing above caused them. 163 of 165 pass. Not investigated - they are about the 2:1 texture-to-use instrument, which is a different subsystem from anything being touched here. Note also that a full Debug `dotnet test` takes 7m41s against 30s in Release, and there is a crashdump under `tools/Noir.Core.Tests/TestResults/` from an earlier run - see the CPU-instability note before blaming code for anything intermittent. — *2026-07-31*

- [x] ~~`MapAudit` reports faults with `Debug.LogError` and then exits 0 REGARDLESS~~ ALREADY
  FIXED by the time Stream 4 (performance) checked it on 2026-08-02 - nobody had come back to
  tick the box. `MapAudit.Run` is now `EditorApplication.Exit(faults == 0 ? 0 : 1)`, and `Run`
  was itself split from `RunCore` (which exits nothing) specifically so `Preflight` could call
  the audit as one step of a longer pass and still inherit its exit code. Confirmed by reading
  the current file rather than assuming the note was still true - it wasn't. — *2026-07-31,
  closed 2026-08-02*

- [x] **The documented PlayMode command now hangs, and it is LLMUnity's tests, not ours.**
  `-runTests -testPlatform PlayMode` with no filter discovers `LLMUnityTests.TestLLM`, whose
  constructor calls `LLMManager.DownloadModel` - so the run sits there trying to pull a language
  model off the network instead of testing the town, and never reaches a result file. It came in
  with the LLMUnity package and the invocation written down in `docs/HANDOFF.md` predates it.
  **Add `-assemblyNames Noir.PlayTests`**, which is what HANDOFF now says. Worth doing properly at
  some point - either an assembly filter in a `.runsettings`, or excluding that package's tests -
  because the next person to run the command as written will lose ten minutes to it the way I
  did. — *2026-08-02*

- [ ] Lift the Crafting System's UGUI inventory UI — drag-drop slots, transfer, tabs — rather than writing one. Tedious to build, and presentation belongs in Unity anyway. — *2026-07-30*
- [ ] Evidence catalogue as `Content/items.txt` in the shape of `kinds.txt`, read by Core. NOT the Crafting System's ScriptableObjects: content authored in an editor window is content `MapAudit` and the PlayMode tests cannot see. — *2026-07-30*
- [x] A layered visibility toggle system for the town view: independently switch off/on trees, buildings, and other dressing layers, graduating all the way down to the bare black survey-plan layout (roads + lot boundaries only), and a single control to restore everything back to full detail at once. — *2026-08-02*

## Ad hoc

## Roads

- [ ] **Chicago Street's 30 m corridor runs through 47 buildings** — measured against building
  footprints (not the county parcels, which include the right of way and tile through every
  street, so they judge nothing). 415 samples of carriageway sit on a building: the Opera House,
  the Rossville bank, the village office, the G.A.R. and I.O.O.F. halls, the grain office,
  Henderson's — most of the downtown row it is meant to run *past* — plus a dozen houses on York,
  Henderson, Gilbert, Stewart, McKibben, Dale, Thompson and Earlcourt. Thirty metres is 98 ft of
  surface; Route 1 through a village of 1,200 is two lanes and a shoulder. Dropping
  `RoadClass.Mainroad` to 14 m takes it to 126 — but it fails 19 road tests whose ASSERTIONS bake
  in the 30 m width (lane counts, corridor coverage, `EvenWidthCentresOnTheDeclaredCoordinate`),
  not just their fixtures. Attempted 2026-08-03 and reverted rather than leave the suite red;
  it is a scoped job of its own. — *2026-08-03*
- [ ] **The CSX line is drawn ~32 m off its own right-of-way, in town.** The owner confirmed the
  lots stop short of the track, and the parcel data shows it: through the platted town there is a
  consistent 18.5–25.5 m corridor (61–84 ft, a standard railroad ROW) lying about 32 m to one side
  of where `features.txt` puts the rail. Measured at (1294,1340) −35.3 m, (1277,1315) −34.6,
  (1254,1281) −32.0, (1205,1210) −32.3, (1123,1089) −25.4. Zero of 28 in-town samples sit in a
  corridor that contains the rail. The two short spur features are legitimate — the owner confirmed
  spurs exist and those run through parcelled ground as they should. Fixing it means shifting the
  `rail` polyline in `features.txt`, which also moves the ballast, the four level crossings and
  anything referencing them — so it goes with the road refit, not before it. — *2026-08-04*
