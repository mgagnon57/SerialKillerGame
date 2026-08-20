# The house inspector — the tool shows the game's truth

**Status: DESIGN, awaiting the owner's review.** Nothing here is built.

## The ruling this implements

The owner (2026-08-19, in chat): "When I click on a parcel/house, I want the tool to pull
up the 3D model it is using, whether it is generated or built in Designer. I also want the
interior layout that it is using to come up… and be able to adjust the interior/exterior
walls if I know the correct layout; if I don't, I will let it stay as is." Asked which
fidelity for generated houses, he chose **full 3D for all**.

Companion spec: `2026-08-19-authored-interiors-design.md` — the write path (an authored
plan overrides the generated interior). This spec is the READ path that makes the write
path honest: the editor must open showing what the game actually builds, so "leave it as
is" is a real choice and not a guess.

## The shape of it

Today the tool's floor-plan editor opens on either an authored plan or an EMPTY shell.
The game's real answer — the BSP-generated rooms, the massing's actual mesh — never
reaches the browser. Three additions close the loop:

1. **The game exports what it built** (interiors + per-building meshes) during the run the
   publish button already starts.
2. **The server serves those exports**, like it serves the game verdict today.
3. **The tool shows them**: a 3D pane on every house, and the floor-plan editor prefilled
   with the layout the game is actually using.

The flow the owner sees: click a lot → the house turns in 3D exactly as the game draws it
→ the interior comes up exactly as the game stamps it → drag walls / rename / Save to make
it authored, or close and the generated layout stands.

## 1. Game-side export — `Noir.Editor.TownExport`

A new editor-only exporter, run in two ways: as part of the verify run that the map's
publish button starts (the same run that writes `tools/game-verdict.json` via
SurveyReport), and by hand as `Noir > Export For The Map Tool`. It builds the town through
`TownPipeline.Build()` — no hand-rolled build, per CLAUDE.md — and writes:

- **`tools/game-interiors.json`** — for every place the survey seated: the parcel and
  building index it came from (the survey side owns that mapping already — the same one
  the placement overlay and SurveyReport use), the place bounds and door tile, units, and
  every room: bounds (tiles), `RoomKind`, name, plus whether the interior was GENERATED or
  AUTHORED (consumed from `Content/floorplans/`). Tile coordinates; the browser converts.
- **`tools/preview-cache/<parcel>-<index>.obj`** (+ `.mtl`) — one mesh per seated
  building, captured from the place's own GameObjects **before `CityChunker` bakes them** —
  building with the bake withheld if the pipeline offers no earlier seam (the layer opt-out
  `CityUnderTest` already uses is the precedent). Transformed to building-local metres, y-up. Owner models export through the same path as
  generated ones — one code path, and the cache is uniformly "what the game draws," which
  for 408 is the imported model, proving the pipeline end to end.
- Flat MTL colours only (`Kd` from each material; textures skipped — the preview answers
  "is this the right house," not "is this the right brick").

Cost and hygiene: ~600 buildings at a few hundred KB each ≈ tens of MB. The cache and
`game-interiors.json` are **gitignored** — derived, regenerable, and stale the moment the
town rebuilds, which is why they are written by the verify run rather than by hand. The
export prints one census line (`[export] N buildings, M interiors, K skipped`) so a
half-written cache is visible.

## 2. Server — `tools/serve-viewer.py`

- `GET /__interiors` → `tools/game-interiors.json`, or `{"when": null}` before the first
  export (the `/__game` precedent: nothing can be said about what the game did before it
  did it).
- `GET /preview/<parcel>-<index>.obj|.mtl` → the cache file, 404 when absent. Parcel and
  index validated as integers — the same path discipline as `_floorplan_path`.

## 3. The tool

### The 3D pane

A `<canvas>` WebGL viewer, **hand-rolled and dependency-free** (~200 lines: OBJ parse,
computed flat normals, one shader, MTL diffuse colours, orbit/zoom/pan) — the page must
keep working from the filesystem with no network, so no CDN and no vendored megalib. It
appears in the floor-plan overlay beside the plan (and collapses on narrow windows).
Missing cache file → a quiet "no export yet — press Publish" note, never an error.

### The prefilled editor

Opening a floor plan resolves in order:

1. An **authored plan** exists (`Content/floorplans/`) → open it (today's behaviour).
2. No plan, but `game-interiors.json` has the building → convert its rooms
   (tiles → feet, the same street-edge-down orientation the write path uses in reverse)
   and open THAT, labelled: *"This is the game's generated layout. Save to make it yours —
   close without saving and the game keeps generating it."* Room kinds arrive as names
   ("Bedroom", "Kitchen"), so a saved file round-trips through the authored-interiors
   name-matching losslessly.
3. Neither → the empty measured-footprint shell, as today.

Save is unchanged: the file lands in `Content/floorplans/` and the authored-interiors
machinery consumes it on the next build. **Not saving changes nothing** — the owner's
"if I don't know it, I will let it stay as is," implemented by doing nothing.

### Exterior walls

The plan's shell is a rectangle; the building's real outline is the measured ring in
`Content/parcel-buildings.txt`, which is 2016 imagery and can be wrong about 1991. So the
editor gains an **optional outline**: a "Edit outline" toggle showing the building's ring
in the plan (converted to feet), with draggable vertices. Saved into the plan JSON as
`outline: [[x,y],…]`, and the Unity conversion hands it to Core as a `PlaceSpec.Outline`
override — a field that already exists and already stamps. No outline in the file → the
measured ring stands, untouched. This is an override on the measurement, never an edit to
it — the `placement-1991.txt` stance.

## Failure modes, named

| Problem | Behaviour |
|---|---|
| No export ever run | 3D pane says "press Publish"; editor prefills from footprint as today |
| Cache stale (town rebuilt since) | `game-interiors.json` carries the export timestamp; the tool shows it; the verify run refreshes both together |
| A building the export skipped | listed in the export's census line; tool falls back per-item |
| Authored outline self-intersects | Unity conversion rejects it, keeps the measured ring, survey report says why |

## Testing

- **Core**: none — nothing in Core changes (the outline override uses an existing field).
- **Editor/batch**: the export method exits non-zero if it writes zero buildings; the
  verify run already surfaces failures to the publish dialog.
- **PlayMode**: none new. The authored-interiors spec's gates cover consumption.
- **Tool**: exercised end-to-end in a real browser during implementation (open 408 → see
  the model turn; open an unruled house → see its generated rooms; save → file lands).
- **Look at it**: the owner clicks houses he knows.

## Out of scope, deliberately

- **Textures in the 3D pane** (flat MTL colours only).
- **Editing the 3D mesh in the browser** — Designer is the modeller; the pane is a viewer.
- **Live sync with a running editor** — the cache refreshes on publish/export, not per frame.
- **Terrace/multi-unit interiors** (per the companion spec).
- **People/furniture in the preview** — walls and roofs answer the layout question.

## Build order (when approved)

1. Export (interiors JSON first, meshes second) — everything else reads its output.
2. Server routes.
3. Prefilled editor (highest owner value per hour).
4. 3D pane.
5. Outline editing.
