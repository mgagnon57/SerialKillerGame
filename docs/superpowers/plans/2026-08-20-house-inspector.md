# House Inspector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Selecting a house in the browser map shows, in the right panel, a 3D frame of the building the game actually draws with everything it contains (rooms and furniture), and the floor-plan editor opens prefilled with the layout the game is actually using — plus furniture editing and outline overrides, closing the loop the authored-interiors feature opened.

**Architecture:** The game writes its truth out on every build (`game-interiors.json`, riding the `SurveyReport` pattern) and per-building meshes on demand (`TownExport`, editor-only). `serve-viewer.py` serves both. The tool grows a dependency-free WebGL frame, a prefill path, a furniture palette, and outline editing — all writing back through the already-landed `Content/floorplans/` machinery.

**Tech Stack:** C# (Noir.Unity runtime + Noir.Editor), Python (serve-viewer.py), vanilla JS/WebGL in tools/viewer-template.html.

**Spec:** `docs/superpowers/specs/2026-08-19-house-inspector-design.md` (updated 2026-08-20 with the owner's right-panel 3D-frame ruling). The authored-interiors spec and code (landed, commits 75e85e3..ff0d229) are the write path this reads back.

## Global Constraints

- **Unity may be OPEN throughout** (the owner is in it). NO task may launch `Unity.exe -batchmode` while `Unity.exe` runs, and none may kill it. Verification for Unity-side tasks is compile-level (`dotnet build Noir.Unity.csproj -c Debug`, `dotnet build Noir.Editor.csproj -c Debug`, `dotnet build Noir.PlayTests.csproj -c Debug`); live-town validation happens in the controller's live-editor session after landing. The generated `.csproj` files are gitignored and may be stale — patch the LOCAL Compile lists to build, never commit them.
- Core gate `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj` stands at **639 pass / 0 fail / 8 skipped** and must not move: NO task in this plan touches `Assets/Noir/Core`. If you think you need to, STOP and report BLOCKED.
- No PlayMode baseline moves (this plan adds no gate tests).
- Derived outputs are gitignored, never committed: `tools/game-interiors.json`, `tools/furniture-palette.json`, `tools/preview-cache/` (Task 1/2 add the .gitignore entries beside `tools/game-verdict.json`'s — check how that one is ignored and match).
- The viewer page must keep working file:// with no network: no CDN, no vendored libraries; hand-rolled WebGL only.
- UTF-8 discipline: never round-trip viewer-template.html or any doc through PowerShell Get-Content/Set-Content; use precise string edits.
- Stage only files you touched; plain-sentence commit messages + "\n\nCo-Authored-By: Claude Fable 5 <noreply@anthropic.com>"; never push.

## Cross-task data contracts (normative — writers and readers implement THESE)

**`tools/game-interiors.json`** (Task 1 writes, Tasks 3-6 read):
```json
{ "when": "2026-08-20 14:05",
  "buildings": [
    { "parcel": 673, "index": 0, "place": "408 Holmes Street",
      "bounds": {"x": 610, "y": 812, "w": 12, "h": 15},
      "door": {"x": 616, "y": 826}, "units": 1, "authored": true,
      "rooms": [ {"x": 611, "y": 813, "w": 3, "h": 4, "kind": "Kitchen", "name": "Kitchen"} ],
      "furniture": [ {"x": 612, "y": 814, "w": 1, "h": 2, "kind": "Bed",
                      "height": 0.55, "model": ""} ] } ] }
```
All coordinates are world TILES (1 tile = 1 m). `authored` = the stamped interior came from
`PlaceSpec.AuthoredInterior`. Only seated survey buildings appear (those with a recorded
parcel/index).

**`tools/preview-cache/<parcel>-<index>.obj` + `.mtl`** (Task 2 writes, 3-4 read):
building-local METRES, y-up, **origin = the world position of the NW corner of the
building's `bounds` tile rect** — so the browser places an interiors box for tile-rect
(x,y,w,h) at local (x−bounds.x, 0, y−bounds.y) with size (w, kindHeight, h), no other
transform. `usemtl` per material with `Kd` diffuse colours in the `.mtl`; no textures.

**`tools/furniture-palette.json`** (Task 2 writes, Task 6 reads):
```json
{ "when": "…", "yours": [ {"name": "OwnersBed", "kind": "Bed", "w_ft": 5.0, "d_ft": 6.5} ],
  "pack":  [ {"kind": "Bed", "w_ft": 5.0, "d_ft": 6.5}, {"kind": "Cooker", "w_ft": 2.5, "d_ft": 2.0} ] }
```
`pack` = the domestic kinds `FurniturePlans` (Core, read-only) actually places, footprints
from its own tables converted to feet; `yours` = rows of `Content/furniture-models.txt`.

**`Content/furniture-models.txt`** (Task 2 creates; hand-edited, TRACKED): the furniture
analogue of models.txt — `name | model | kind | width_m x depth_m` per line, `#` comments,
created with a header in the house style and one commented-out example row.

**Plan-JSON furniture entries** (Task 6 writes; already consumed by FloorPlans.cs since
the authored-interiors landing): `{"id","name","model","x","y","w","h","rot"}` in feet —
IDENTICAL to what `FloorPlans.PlanFurniture` already parses (verify field names there).

**Plan-JSON outline** (Task 7): `"outline": [[x_ft,y_ft], …]` in plan feet, same frame as
rooms; consumed by FloorPlans → `PlaceSpec.Outline` (existing field).

---

### Task 1: The game writes its interiors out on every build

**Files:**
- Create: `Assets/Noir/Unity/InteriorsReport.cs`
- Modify: `Assets/Noir/Unity/SeatOnSurvey.cs` (record parcel/index ↔ place at the attach), `Assets/Noir/Unity/TownPipeline.cs` (Clear/Write hooks beside SurveyReport's at lines ~94/138), `.gitignore`
**Interfaces:** produces `tools/game-interiors.json` per the contract; produces `InteriorsReport.Note(int parcel, int index, PlaceSpec spec, bool authored)` called from SeatOnSurvey, and `InteriorsReport.Write(WorldModel world)` called from TownPipeline after the world is built (it needs `world.AllRooms`/`AllFurniture`/`AllPlaces` to look up each noted place's stamped rooms by `PlaceId`; find how a `PlaceSpec` maps to its built `Place` — by name/Key — and record whatever key makes that lookup exact; read how VillageHost/TownPipeline reach the built world).

- [ ] **Step 1:** Read `SurveyReport.cs` (the pattern: static rows, Clear/Write, path `ContentLoader.Root/../tools/`), `TownPipeline.cs:80-140`, and the FloorPlans attach site in `SeatOnSurvey.cs`. Write `InteriorsReport` in the same voice: derived, rewritten every build, safe to delete. Serialize with a StringBuilder like SurveyReport (no JsonUtility — the schema has nested arrays).
- [ ] **Step 2:** `Note(...)` at the SeatOnSurvey attach (it has ParcelId, Index, the PlaceSpec, and whether `AuthoredInterior != null` in hand). `Write(world)` resolves each noted spec to its built Place and emits rooms (`world.AllRooms` filtered by `Building`) with `kind.ToString()`, `Name`, bounds; and furniture (`world.AllFurniture` whose `Room` belongs to the place) with kind, `Height`, `Model`, footprint. `authored` flag from the note. Skip notes whose place didn't build.
- [ ] **Step 3:** `.gitignore`: add the three derived outputs beside the existing tools entries (check `git check-ignore tools/game-verdict.json` first — mirror however that's done).
- [ ] **Step 4:** Builds green (all three csproj). Do NOT launch Unity.
- [ ] **Step 5:** Commit: "The game reports every interior it built, beside the verdicts".

### Task 2: The mesh exporter and the palette

**Files:**
- Create: `Assets/Noir/Editor/TownExport.cs`, `Content/furniture-models.txt`
- Modify: `.gitignore` if Task 1 didn't already cover preview-cache/ and furniture-palette.json
**Interfaces:** produces `Noir.Editor.TownExport.ForViewer()` — `[MenuItem("Noir/Export For The Map Tool")]` and batch-safe static method; writes the mesh cache + furniture-palette.json per the contracts; census line `[export] N buildings meshed, M skipped, palette K yours + L pack`.

- [ ] **Step 1:** Read `Assets/Noir/Editor/SmokeTest.cs` (an existing executeMethod editor entry that builds the town — copy its build-and-exit shape) and `Assets/Noir/Unity/CityChunker.cs` (what the bake destroys). The exporter builds via `TownPipeline.Build()` ONLY. Capture per building BEFORE the bake if the pipeline bakes inside Build; if it does, use the same layer/бake opt-out `CityUnderTest` uses (read `Assets/Noir/PlayTests/CityUnderTest.cs`) — building the plan-level town without the bake is acceptable for the preview.
- [ ] **Step 2:** Per seated building (reuse the parcel/index mapping recorded by Task 1's `InteriorsReport` — expose the noted list), find its GameObjects: read how `CityBuildings.Build` parents per-place objects (Townhouse/Stack take a `Place`) and match by place. Walk `MeshFilter`+`MeshRenderer` (and `SkinnedMeshRenderer` if any), transform vertices to world, subtract the bounds-NW-corner origin per the contract, write OBJ v/vn/f with `usemtl` per material and an `.mtl` of `Kd` colours (`material.color`). Non-readable meshes: skip that renderer, count it, keep going.
- [ ] **Step 3:** The palette: `FurniturePlans` (Core, read its actual shape) → domestic kinds + footprints (tiles→feet ×3.28084); `Content/furniture-models.txt` parsed for `yours` (create the file with header + commented example). Write furniture-palette.json.
- [ ] **Step 4:** Builds green. Do NOT run the export (editor open) — the controller runs it live after landing.
- [ ] **Step 5:** Commit: "An editor export writes every building's mesh and the furniture palette".

### Task 3: The server serves the game's truth

**Files:** Modify: `tools/serve-viewer.py`
**Interfaces:** `GET /__interiors` → tools/game-interiors.json verbatim or `{"when": null}`; `GET /__palette` → furniture-palette.json or `{"when": null}`; `GET /preview/<parcel>-<index>.obj|.mtl` → the cache file with correct Content-Type (`text/plain` fine), 404 when absent; parcel/index validated as integers (the `_floorplan_path` discipline — read it); log_message filter extended so /preview and /__interiors polls don't drown the log.

- [ ] Steps: read the existing GET routing (the `/__floorplans`-before-`/__floorplan` ordering note), implement, verify with the server running on a spare port (`python tools/serve-viewer.py 8998` + curl each route with and without files present), commit: "The map's server hands over meshes and interiors".

### Task 4: The 3D frame in the right panel

**Files:** Modify: `tools/viewer-template.html`; run `python tools/build-viewer-data.py` after (regenerates the gitignored page — do not commit docs/rossville-buildings.html).
**Interfaces:** consumes /__interiors, /preview/, the existing `show(i)` parcel panel and its `d-bldgs` building cards (each card knows `b.p`/`b.i`).

- [ ] **Step 1:** A `fetchInteriors()` at load (LIVE only, like `loadPlans()`), stored keyed `"p|i"`.
- [ ] **Step 2:** Each building card gains a collapsed `<canvas>` frame (expanded automatically for the primary building of the selected lot). Loader: fetch `/preview/p-i.obj` + `.mtl`; on 404 show "no export yet — press Publish or Noir > Export For The Map Tool" and STILL draw the contents boxes.
- [ ] **Step 3:** The viewer, hand-rolled: parse OBJ v/vn/f (+usemtl) and MTL Kd; one WebGL program (position+normal+color attributes, uniform MVP, lambert against a fixed light); orbit = drag (yaw/pitch around the model's bounds centre), wheel = dolly. Furniture boxes and room floor-plates generated as triangles from the interiors JSON per the coordinate contract (box at local x=r.x−bounds.x etc., height from the JSON, kind-tinted with a small palette; room plates 5 cm tall, room-kind tint, 40% alpha via ordering or just dim colours — keep it simple and opaque if blending fights). Labels: skip 3D text; a hover tooltip naming the piece/room via a picking pass is OPTIONAL — a legend listing contents under the canvas is the required minimum.
- [ ] **Step 4:** Roof-off toggle: a checkbox that clips triangles whose all-three vertex Y exceed 2.6 m (drop them at build time into a second index buffer — toggle swaps buffers).
- [ ] **Step 5:** Verify in a real browser via the local server (Playwright or manual): 408 with no mesh cache shows boxes-only; then with a hand-made tiny OBJ placed in preview-cache (write a unit cube fixture yourself) shows mesh+boxes. Commit: "Select a house and the right panel shows it in 3D, contents and all".

### Task 5: The editor opens on the game's layout

**Files:** Modify: `tools/viewer-template.html` (openFloorPlan resolution order + banner); rebuild page.
**Interfaces:** consumes /__interiors and the fp editor's plan model; must produce plan JSON that round-trips through FloorPlans.cs unchanged in meaning.

- [ ] **Step 1:** In `openFloorPlan`, when no authored plan exists but interiors data does: convert the building's rooms+furniture from world tiles to plan feet — the INVERSE of FloorPlans' fit: translate by bounds origin, un-rotate by the building's rot (recompute rot the same nearest-door-edge way from the interiors JSON's door+bounds), scale tiles→feet ×3.28084, shell = bounds (un-rotated) ×3.28084. Perfect inversion is impossible (the fit is lossy); the goal is a faithful EDITABLE approximation — rooms in the right relative places with the street at the bottom. Set `fpPlan.name` to "<addr> - as the game builds it".
- [ ] **Step 2:** A banner in the fp header when the plan came from the game: "This is the game's generated layout. Save to make it yours — close without saving and the game keeps generating it." (authored plans keep no banner; the empty-shell fallback keeps the current behaviour).
- [ ] **Step 3:** Verify in browser: a house with no plan (e.g. 407, parcel 600) opens showing its generated rooms; 408 still opens its authored plan. Commit: "The plan editor opens on the layout the game is actually using".

### Task 6: Furniture in the plan editor

**Files:** Modify: `tools/viewer-template.html`; rebuild page.
**Interfaces:** consumes /__palette; writes `furniture` arrays in the plan JSON exactly as `FloorPlans.PlanFurniture` parses (verify its field names in FloorPlans.cs before writing).

- [ ] **Step 1:** Render `fpPlan.furniture` as draggable rects above rooms (kind-tinted, name label, w×d ft), selectable like rooms; panel edits: name (palette dropdown grouped yours/pack + free text), model, x/y/w/h, rotate button (swaps w/h, flips rot 0↔90), delete. Prefilled game layouts (Task 5) carry their furniture in.
- [ ] **Step 2:** "+ Furniture" button → palette picker (from /__palette; falls back to a plain kind list when absent), places at 1,1 with the palette footprint.
- [ ] **Step 3:** Undo/dirty/save already generic — verify they cover furniture (fpPushUndo on mutations). Verify in browser: add a bed to 407's prefill, Save, confirm the JSON on disk carries the furniture array with the exact FloorPlans field names. Commit: "Furniture on the plan: see what the game placed, move it, make it yours".

### Task 7: Outline editing

**Files:** Modify: `tools/viewer-template.html`; `Assets/Noir/Unity/FloorPlans.cs` (parse `outline`, hand to `PlaceSpec.Outline` — Unity-side only, no Core change); rebuild page; builds green.
**Interfaces:** plan-JSON `outline` per contract; FloorPlans converts feet→tiles through the same fitted axes as rooms and sets `spec.Outline` (a `Tile[]` ring — read how SeatOnSurvey builds its rings for the exact type/winding) INSTEAD of the measured ring only when present and valid (≥3 points, non-self-intersecting by the same test the codebase uses if one exists; else reject with the survey-report reason).

- [ ] **Step 1:** "Edit outline" toggle in the fp header: draws the measured ring (from the building's `r` ring in DATA.buildings, converted to plan feet via the same reverse transform as Task 5) as a draggable-vertex polygon; dbl-click an edge inserts a vertex, Del removes the selected one; Save writes `outline` only if the user actually toggled and moved something.
- [ ] **Step 2:** FloorPlans parsing + hand-over + census note (an outline consumed/rejected is worth a word in the `[floorplans]` line).
- [ ] **Step 3:** Builds green; browser verify the editing UX; commit: "The owner can redraw a footprint the imagery got wrong".

## Self-review notes
- The data contracts at the top are the plan's spine — every task cites them instead of negotiating formats pairwise.
- Task 2's bake-timing and Task 5's lossy inversion are the two judgment-heavy spots; both carry explicit read-before-write directives and acceptance bars rather than pretending exact code.
- No Core changes anywhere; no PlayMode baseline moves; the editor-open constraint is global because the owner is in Play all day.
