# Texture and material fixes — the work list

**This is a work file. Delete it when the last item lands.** A read-only audit on 2026-08-08 found
117 verified faults after the owner reported "I see weird shit and I think it is left over textures."
He was right: `Content/textures/` was created in one commit — *"Ashcombe: a 112-person village that
measures itself"* — and nothing has touched it since. A planning pass turned the audit into 85 items
across seven waves. The facts live in `CLAUDE.md`; this is a queue.

**Sibling plans:** `docs/ANIMATION-FIXES.md` and `docs/ROAD-FIXES.md`. All three move the same two
numbers and three of the same files. See [Cross-plan](#cross-plan-collisions) before editing.

**Item IDs:** `ROOF` the covering · `UVX` UVs and mapping · `MS` materials and Main Street ·
`TEX` instruments · `ENG` the English inheritance · `CW` cleanup and waste.

---

## ⚠ The one line that decides whether any of this works

**Nothing in this project has ever written a tangent stream.** A repo-wide search for
`RecalculateTangents` or `SetTangents` under `Assets/Noir` returns **zero hits**.

That matters more than the palette, because every pack roof albedo is **nearly flat** —
`Roof_Shingles_A_Farm_Alb` spans 12 levels out of 255. **All the shingle is in the normal map**, and
URP builds its tangent frame from a vertex stream this project does not produce. Bind the pack sets
without it and the honest prediction is 655 flat, materially *darker* roofs with no shingle read at
all — which can look **worse** than the terracotta it replaced.

So `ROOF-0` — one line, `mesh.RecalculateTangents()` in `MeshChunks.Emit` beside the existing
`RecalculateNormals`/`RecalculateBounds` pair — goes first, inside the first commit, and is not
optional. The same gap applies to the whole ground estate, which has been binding pack normal maps
into nothing this entire time.

---

## The owner's rulings — encoded, and two of them contested

All settled. Two were contested by the plan on evidence, put back to him, and **he took the evidence
both times** — recorded here so nobody re-opens them.

| | Ruling | |
|---|---|---|
| **1** | Houses get three-tab **asphalt shingle** | settled |
| **2** | Mix: **slate grey 40 · charcoal 40 · brown 20**, with one roof in twenty a brown-black. **No green.** | settled 2026-08-09 |
| **3** | **Course exposure ~5–6 inches** on the slope, not the ~9 it would default to | settled 2026-08-09 |
| **4** | **Farm buildings: leave them entirely.** No override, no metal covering. | settled 2026-08-09 |
| **5** | Downtown is **flat built-up tar and gravel**, by tinting a pack gravel map | settled — already the right *shape* |
| **6** | **All seven building surfaces** in one pass, not roofs first | settled |

**On #4 — there was never a fault.** Measured: **zero of the 652 generated roofs are farm buildings.**
`CityBuildings.Handles` claims `farm`, `barn` and `silo`, so they are bought models arriving with the
pack's own roofs. His earlier "barns had shingles" ruling was made on my framing that the models are
*British* — true of the **name** (`Barn_Farm_British`, `House_Farm_British`) and of the silhouette,
but not of the roofs. He withdrew it once measured. **Do not write the material override.** The same
reasoning killed a corrugated-metal covering: it would have no buildings to go on.

**On #2 — the green is out on period grounds.** Faded green is a *modern architectural* shingle and
reads wrong for 1991 east-central Illinois. The settled mix also happens to match the four shades the
pack already ships, so no colour has to be invented.

**On #3 — the spread is correct, not a bug.** Because the four house types have different pitches, the
exposure will land anywhere from 5.1 to 6.4 inches depending on which house you are standing in front
of. That is what a real street looks like. Do not "fix" it to a constant.

**A consequence of ruling 5 he accepted knowingly:** a detailed wall texture makes the UV-degenerate
gable ends *more* obvious, not less. So `UVX-A1` (unweld the gable triangles onto the wall's own
mapping) rides in the **same commit** as the roof work, not a later wave.

---

## The standing gates

| Gate | Command | Today |
|---|---|---|
| Core | `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj` | **458, ~5 min** (was 428 when this was written; `CLAUDE.md` wins) |
| Unity compiles | `dotnet build Noir.Unity.csproj -c Debug` **and** `Noir.Editor.csproj -c Debug` | clean |
| Smoke | `-executeMethod Noir.Editor.SmokeTest.Run` | ~3 min |
| Renders | `-executeMethod Noir.Editor.CityShot.RenderBuiltNoon` | rewrites `docs/snapshots/**` |
| PlayMode | `-testCategory "!Diagnostic"`, never `-nographics`, always `-assemblyNames Noir.PlayTests` | budget **ten** min |
| Player | `-executeMethod Noir.Editor.BuildPlayer.Windows64`, then **launch the exe** | the only proof of ROOF-1 |

**Three greppable lines are the whole verification of wave 1:**

- `Surface textures: 7 loaded` must become **`3 loose`**. Seven after the change means the pack path
  silently failed and the roofs are still placeholders. **This is the single most important line.**
- `Roofs: 6xx buildings, <N> vertices` — **N must be identical** before and after. `Unweld` rebuilds
  the vertex list from triangle indices and `RecalculateTangents` adds a stream, not a vertex. If N
  moves, something changed the mesh that should not have.
- `Signs: 37` must become **`Signs: 30`** after MS-9. The 37 are fully accounted for and exactly ten
  take the default arm.

---

## The waves

### W0 — One append to `.gitignore` · ✅ **DONE 2026-08-09**
`CW-1(a)` `CW-2(a)` `CW-10`

The only repository-level exposure in the plan: **24 MB** (measured; the plan said 23.3) of AI
reference art whose own README says it must never settle a question, sitting untracked *inside*
`Assets/`, where Unity imports it. Ignored, not deleted — it is a target to build toward, and the
same treatment the pack and the llama binaries already get.

> **Gate. THE TRAP IS REAL AND WAS REPRODUCED BEFORE TRUSTING IT.** `git check-ignore -v` on a
> directory with a trailing slash returns **rc=0 citing blank line 80**, echoing the queried path
> back as though it were the pattern:
>
> ```
>   $ git check-ignore -v Assets/Noir/Reference/
>   .gitignore:80:	Assets/Noir/Reference/       rc=0     <- line 80 is BLANK
>   $ git check-ignore -v Assets/Noir/Reference/Rossville1991/README.md
>   rc=1                                                  <- the truth: not ignored
>   ```
>
> Verified after the change on three real file paths, and that `Assets/Noir`'s 630 tracked source
> files are untouched.

### W1 — **The roof he reported** · ~10–14 h editing, editor open · then ONE window, ~30 min
`ROOF-0` `ROOF-1` `ROOF-2` `ROOF-5` `ROOF-3` `ROOF-4` `UVX-A1` `ROOF-13` `ROOF-6` `MS-4,6,7,11,12`
`CW-4,5,6` `TEX-4,9,17` `CW-1(b)` `CW-2(b)`

**Everything here is compile-only, so all of it lands while he works.** Six commits, in order, and
**do not open Unity between them** — ROOF-4 is invisible until ROOF-2 lands and the point of batching
is to judge it in one look.

- **(A)** ✅ **DONE 2026-08-09.** `ROOF-0` + `ROOF-1` + `ROOF-2` + `ROOF-5` — the tangent line;
  `Roofing()` handing `Make()` a real colour instead of `Color.white`; the roof entries added to
  `_packSets`; tiling 2.5 → 1.5. Plus `TEX-11`'s palette as four named colours, the loose PNGs
  regenerated to match, and the four dead English coverings deleted.
  > **`Surface textures: 7 loaded` → `3 loose (brick, wall, water)`.** The line now names them and
  > says what it is: a count of what FELL BACK to the flat placeholder, not an inventory.
  > `Roofs: 651 buildings, 196,362 vertices` — record that; it is the number `UVX-A1` must not move.
  >
  > **⚠ THE PLAN'S CLAIM THAT THE PACK SHIPS THE FOUR SHADES IS WRONG.** Measured: A and B are the
  > same tone (64 and 63), D has the green cast he excluded, and E — the only true charcoal —
  > ships with **no normal map**. Three albedos serve four coverings; charcoal is B at 0.70 and
  > brown-black is C at 0.38. Do not "restore" a fourth sheet.
  >
  > **And I misread the result before there was a camera that could show it** — see W1's own gate
  > below. The roofs are three-tab asphalt with visible tabs and courses in `roof-close.png`.
- **(B)** ✅ **DONE 2026-08-09.** `ROOF-3`, the keying change — public signature, two callers,
  both in `Noir.Unity` (the plan's note that the Editor assembly is "not optional" for this item
  is wrong; nothing in `Noir.Editor` calls it).
  > **The hash was never the problem, and that was measured before anything was touched.**
  > `Scatter(Bounds.X, Bounds.Y, 5309)` lands 37.97 / 38.03 / 19.00 / 5.00 against a target of
  > 38 / 38 / 19 / 5, with neighbour agreement 32.2–32.9% at every lot spacing from 1 m to 66 m
  > (chance is 32.9%). No banding, no correlation.
  >
  > The fault is that a roof was a property of a **coordinate**: `ClearOfRoads` moved 175
  > buildings on 2026-08-09, so re-deriving `roads.txt` re-rolled about two thirds of those
  > coverings. `RoofingFor` takes the `Place` and rolls on `Rolls.Avalanche(place.Key ^ 5309)`.
  > `PlaceSpec.Key` already says "EVERYTHING generated from a place hangs off this string" — the
  > roof was the last thing that did not.
- **(C)** ✅ **ROOF-4 and UVX-A1 DONE 2026-08-09** (`ROOF-13` still open). A1 **takes the gable
  triangles away from** ROOF-4's rotation, so landing them separately means rotating UVs A1 then
  deletes — they went in one commit.
  > **A1 turned out to cover A2 and A3 as well.** `Unweld` already gives every triangle its own
  > three vertices and UVs, so A1 is a *remap*, not a split: `WallsInTheirOwnPlane` walks the wall
  > submesh and maps each triangle by its own normal — drop the axis it faces, keep the other two.
  > The church tower and bell-cote are boxes with the identical fault, so one rule serves all
  > three instead of three special cases that must agree about winding forever. **Cross A2 and A3
  > off W2.**
  >
  > `Roofs: 651 buildings, 196,362 vertices` — **unchanged**, the gate this wave sets itself. Both
  > changes rewrite UV values and neither touches the vertex list.
  >
  > **BOTH OF THE THINGS I RECORDED AS NOT DONE ARE NOW DONE.**
  >
  > **The hip ends ✅** — `RoofFacesInTheirOwnPlane` maps every covering face from its own normal:
  > U horizontally across the face, which on any pitched roof IS that face's ridge or hip, and V
  > straight up the fall line, both in metres ON THE SLOPE. That supersedes ROOF-4's quarter-turn,
  > which could only ever serve one pair of faces, and it retires the "the slope stretches them by
  > about a tenth" apology as well. A flat roof keeps the ground projection and that falls out
  > rather than being special-cased — `cross(n, up)` vanishes on a face pointing straight up.
  > Order matters: coverings first, walls second, because a gable end is in the wall submesh.
  >
  > **The gables ✅ seen** in `roof-mix.png` — they read as flat wall surfaces, not as the vertical
  > eaves-to-ridge streak a degenerate UV produces. Not a close-up, but the smear was never
  > subtle.
  >
  > `Roofs: 651 buildings, 196,362 vertices` — unchanged across all three UV passes, which is what
  > says they rewrite UV values and never the vertex list.
- **(D)** `SurfaceTextures.cs` opened **once** for ROOF-2 plus CW-4/5/6 and TEX-9. That file is hot.
- **(E)** the small material fixes and TEX-4.
- **(F)** `ROOF-6` + `MS-6`, then **one** `dotnet run --project tools/Noir.Sim -- tiles`. That command
  rewrites all fourteen PNGs, so running it before both edits land silently loses one.

**ROOF-1 is not separable from ROOF-2.** `ApplyPack`'s whole body is `#if UNITY_EDITOR` and falls
through to a loader that returns on a null texture. Land ROOF-2 alone and every shipped build still
has **pure white roofs** — the editor gets better and the product does not change.

> **Gates.** Both assemblies compile — the Editor one is not optional, ROOF-3 changes a public
> signature. Core stays **458/458**: no file under `Assets/Noir/Core` is touched by this wave.
> Then editor **closed**: `MeshReadable.Enable` → `SmokeTest.Run` → the three greppable lines above →
> `CityShot.RenderBuiltNoon` → **open the PNGs**.
>
> **LOOK AT IT — AND AT SOMETHING THAT CAN SHOW IT.** Terracotta and straw are gone from every
> roof in `suburb-block.png` ✅. The course direction is NOT yet right: ROOF-4 is outstanding, and
> `roof-close.png` now shows why — roof UVs are a top-down PLANAR projection in metres
> (`Vector2(x, -z)`), so courses follow the world axes and one projection covers both slopes of a
> hipped roof.
>
> **`suburb-block.png` CANNOT settle the shingle and neither can the street shots.** The overview
> is 1,150 m up, where a course is far below a pixel; `town-street` and `city-street` are at eye
> height on the DOWNTOWN block, which is flat-roofed commercial with the pitched roofs above the
> frame. Reading those three, I reported the new roofs as flat with no shingle. They are not.
> `Noir/Render The Roofs` was added for this and `roof-close.png` is the frame that settles it.

### W2 — The rest of the UVs · ~5 h + one window ~20 min
`ROOF-10` `TEX-11` `UVX-A2,A3,A4,A7,A9,A10` `TEX-10,12` `MS-1,2,5,9,10` · roof-AO experiment

`UVX-A2`/`A3` are the same 0..1-into-metres fix as A1, for the church tower, bell-cote and spire.
`TEX-11` encodes his palette as a **named list of four colours, not a saturation band** — the band
fails `roof_worn` at the third decimal (0.30159 against `<= 0.30`) and would land **three** unexpected
reds, not two.

### W3 — The gates and the shipped player · ~5 h + one PlayMode run + one build
`TEX-1,2,3,6,7,8,13,14` `ROOF-8` `UVX-A6(a)` `MS-13`

`ROOF-8` ✅ **DONE 2026-08-09** as `EveryRoofCoveringIsWiredToATextureOrAColour` in
`TownGeometryPlayTests`, exactly as specified: a **wiring** assertion, not a threshold — for each
roof material, `GetTexture("_BaseMap") != null || GetColor("_BaseColor") != Color.white`. It goes
red if somebody reverts `Roofing` to white, instead of asserting a number that contradicts ROOF-2's
own table. It also catches the subtler regression: binding a pack set but leaving the base colour
white on a covering whose whole shade IS the tint, which would take charcoal back to mid grey.

**The build and the launch go at the end**, because launching `Rossville.exe` is the *only* thing in
this project that can prove ROOF-1. Everything before it proves the editor got better.

### W4 — The citizens' atlas · **blocked on `ANIMATION-FIXES` PB-7**
`UVX-A5` `UVX-A8` `UVX-A6(b)` `UVX-A9`

`AgentBody.cs` belongs to the animation plan, which lists seven items and a stated commit order.
PB-7 de-guards it onto a Resources manifest, which **directly falsifies** UVX-A5's stated risk that a
shipped player is unaffected. Rewrite that line before landing.

### W5 — The English inheritance · **ENG-1 ✅ and the witness clauses ✅, 2026-08-09**
`ENG-14` **first** · `ENG-2..13` · `ENG-1` alone, last · `ENG-10`, `ENG-15` separate days

`ENG-14` builds the instrument first, because the PlayMode gate switches off every layer this cluster
changes — until a camera points at the country with the Farm layer built, **nothing here is
falsifiable**.

- **ENG-1 ✅ DONE, and it was bigger than the estimate.** The plan says "13 miles of hedge in
  ~12,800 dashes". Measured off the built town it was **17,849 — 44.9% of every prop in Rossville**.
  > ```
  >   before   39,772 props   Hedge 17,849 (44.9%)  Fence 9,201  Tree 7,492  Bush 3,979
  >   after    22,335 props   Hedge      0          Fence 9,179  Tree 7,518  Bush 4,387
  > ```
  > `PropGenerator` hedged 42% of every grass or field tile touching a road. An Illinois front yard
  > runs open to the sidewalk, and a Vermilion County roadside is a mown verge and a drainage
  > ditch. The fence rows `OnFieldEdge` draws are untouched — that is the osage-orange remnant and
  > it is the part that IS real. `PropKind.Hedge` and its run-drawing stay: nothing plants one, but
  > a map may still author one.
  >
  > **`AddingABuildingMovesNoPropOutsideItsOwnChunk` went red and was right to.** Its premise
  > `was.Count > 500` was calibrated when 45% of props were hedge, so it was really measuring how
  > English the map was. Re-recorded at 300 (the fixture yields 356) with the reason written in.
  > **Do not delete that check** — noticing a sample that has become meaningless is its whole job.

- **The witness clauses ✅ DONE** — this is most of what `ENG-2..13` must have been.
  `Content/particulars.txt` is 914 hand-authored clauses and **98 of them were English**.
  > **"Marlbury" does not exist** — 21 clauses sent people to an invented English market town.
  > Owner's ruling 2026-08-09: **split by errand.** Hoopeston (5 mi N) for the everyday — market
  > day, the haircut, the cafe, the Friday fish, the bus route, the paper a day early. Danville
  > (county seat, 20 mi S) for the occasional — the library, the yarn, the concert, the daughter,
  > and "has not been further than here since 1964", which only works as the far limit.
  >
  > parish→township · the lane→the street · pub→tavern · vicar→pastor · jumble→rummage ·
  > allotment→garden plot · fete→the fair · wireless→radio · chequebook→checkbook · bins→trash
  > cans · pavement→sidewalk · boot→trunk · wool→yarn · noticeboard→bulletin board · adverts→ads ·
  > fortnight→two weeks · library ticket→library card · shilling→dollar · Home Guard→Civil
  > Defense · chutney→relish · the snow of sixty-three→of seventy-eight.
  >
  > **Two jokes were re-built rather than translated**, because the word WAS the joke: *calls the
  > wireless "the wireless"* → *calls the refrigerator "the icebox"*, and *keeps the wireless on
  > the shipping forecast, having never seen the sea* → *keeps the radio on the farm report,
  > having never farmed*. **The thatcher clause** — the one line this plan singled out as awaiting
  > a ruling — is now *remembers the last man who shingled a roof by hand*. It was never about
  > thatch; it is about a trade going out.
  >
  > Nature words keep the habit and lose the accent, on his ruling: cricket→the ball game,
  > conker→buckeye, badger sett→badger hole, water meadow→the bottoms. **The cuckoo stays** — the
  > yellow-billed cuckoo is a real Illinois spring marker.
  >
  > **British SPELLING is left alone on purpose** — colour, favourite, neighbour, recognised is
  > this project's house style in every comment in the tree, not an Ashcombe leftover.
  >
  > ⚠ **STILL ENGLISH AND NOT DONE: `Activity.OnTheAllotment`.** A C# enum in the witness
  > vocabulary (`DayPlan.cs:21`) plus a keyed row in `Content/animations.txt:146`
  > (`ontheallotment`). Renaming it touches Core, Unity and Editor and a content row that
  > "does not throw, it falls through to a default" if the two drift — so it wants its own commit
  > and the animation-table gates watched, not a rushed rename at the end of a long session.

### W6 — The documents, last and once
`ROOF-9` `MS-8` `TEX-18,19` `CW-7` · the **one** `CLAUDE.md` edit

Docs last, because three plans are each moving the PlayMode count and the Core count. Whichever lands
last writes the measured number **once** — which is exactly what `CLAUDE.md`'s preamble exists to
enforce — and edits `docs/ROAD-FIXES.md:59` and `:558` in the same commit so they do not contradict it.

---

## Cross-plan collisions

| File | Owner | Rule |
|---|---|---|
| `CityStreets.cs` | **`ROAD-FIXES` W8** | It lists eleven items and a sequential pass. This plan takes only four small disjoint things (MS-1, MS-2, MS-13, ENG-3(a)/ENG-7). **MS-3 is handed to ROAD-FIXES.** If a road wave is in flight, **wait.** |
| `AgentBody.cs` | **`ANIMATION-FIXES`** | Seven items, stated order. W4 here is calendar-blocked behind PB-7. |
| `TownGeometryPlayTests.cs` | three plans | Add at the **end** of the file, one commit, W3, rebase around any live road wave. |
| `LayerProof.cs` | three-way | Land whichever comes first and rebase. Do not branch in parallel. |
| PlayMode count · Core count | three plans | **Measure it, never predict it, write it once.** |
| `Materials3D.cs` | this plan | Five items, **one** editing pass in W1. Comment rewrites go in W6 — they move the lines the text tests quote. |

---

## Do not do

- **`ROOF-14` — do not bind the brick wall texture to the chimneys.** Its albedo is a *flat colour*
  (span 116–117 of 255). It would replace the placeholder with something **flatter**. All its
  coursing is in a normal map that is inert without tangents.
- **`TEX-16` — do not add new camera frames.** They already exist byte-for-byte in `CityShot`.
- **`TEX-5` — do not route the twelve cameras through a derived crossing.** It is 8.47 m from the
  hand-typed one, so it would move all twelve committed framings.
- **`UVX-A6(a)` as written — it passes vacuously.** `CityChunker.Bake` destroys those MeshFilters
  (`13125 renderers → 2308`), so the test walks an empty subtree. Walk the **baked** nodes.
  > ✅ **DONE** as `NoTriangleInTheTownIsTexturedThroughAPinhole`, walking the nodes under `Baked`
  > and asserting BOTH that the mesh list is non-empty and that at least a thousand triangles were
  > measured — a test that cannot fail is worse than none. Measured as **texel density** (m² of
  > surface per unit of UV area), because a UV area means nothing on its own.
  > **It found two real ones on its first run**, at 32,281 m²/UV against a limit of 400, in
  > material index 0 of chunks (5,-2) and (4,-2). Ratcheted at two and the message now prints the
  > MATERIAL, because a chunk name names nothing anybody can go and look at. Not the gable smear
  > it was written for — that was hundreds of triangles — so these are something else and want a
  > fresh session, not a loosened threshold.
- **`TEX-14` as written — it would permanently rewrite all 21 layer switches.** `Layers.Set` writes
  PlayerPrefs whenever not in batch mode. That is verbatim the mechanism `CLAUDE.md` names for the
  animation incident, proposed for a pre-commit pass. Guard it, or snapshot and restore in a `finally`.
- **Do not delete the seven pack-shadowed loose PNGs.** They are the only ground texture a shipped
  player gets, and `ANIMATION-FIXES` PB-3 is scheduled to make them ship. ROOF-6 **regenerates** them.
- **Refuted — do not plan against these:** `SYMP-8` (every offline render already forces the built
  town on and restores it; the blind spot is the PlayMode gate alone). `SYMP-1`'s address list
  (computed on pre-survey coordinates — **do not walk to those addresses expecting thatch**).
  `SYMP-6`'s mip and compression arguments. `RPS-2`'s tree-trunk symptom (a tiling map cannot know
  where a trunk is; measured effect 1–5%).
- **The English innocence list — do not re-hunt:** `terrace` (a real Illinois form, off the 1913
  Sanborn sheets), Green Street, **`on the village board`** (Rossville is an incorporated Illinois
  village with a Village Board of Trustees — correct American, **must not be touched**),
  `Content/fixture-village.txt` (the deliberately-not-the-real-town fixture), the pack's telephone
  booths (a pay phone on a 1991 Illinois street is period-correct).
- **Do not tidy three things.** `Seed = 1979` is a **seed, not a date**. Do not convert any
  `Materials3D.Scatter` position hash into an `IRng` substream — four systems key on it so the same
  land appears every run. Do not rename a `kinds.txt` `kind` name — kinds resolve through the `words`
  line, so a rename parses fine and silently breaks every comparison.
- **Process.** Never `git add -A` — every render rewrites tracked `docs/snapshots/**`. Never run
  `-- tiles` twice. Never run Core in Debug. Never pipe `dotnet test` into `tail`. Mute the mixer
  before any batch run.

---

## Done means

- **He opens `docs/snapshots/suburb-block.png` and the roofs are asphalt shingle** — grey, charcoal
  and brown across a residential block, with course lines running **along** each ridge. No terracotta,
  no straw, no blue-grey slate. This is the fault he reported and it arrives at the end of the first
  Unity window.
- **`town-holmes.png` shows a house.** 408 Holmes — whose own camera comment says *"if this view is
  wrong nothing else matters"* — has never been photographed: **both cameras aimed at it are 2.50 m
  underground, aiming 6.44 m into the hill.** That is why it is the smallest PNG in its set, and why
  my own attempt to photograph it tonight found a road instead. The file size jumping **is** the proof.
- **A screenshot of `Rossville.exe` after `[boot] ready` shows shingle roofs, not white ones.** Until
  somebody launches the exe and looks, "fixed" means "fixed in the editor."
- The three greppable lines read `3 loose`, `Signs: 30`, and an **unchanged** vertex count.
- `git grep -n thatch` outside `docs/history` returns exactly one line — `Content/particulars.txt:636`,
  the owner's hand-authored clause, awaiting his ruling.
- Every committed frame that changed was **staged by name**.
