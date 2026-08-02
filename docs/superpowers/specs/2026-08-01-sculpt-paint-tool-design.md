# Sculpt/Paint Tool — Stream 2 design

**Date:** 2026-08-01
**Status:** approved, ready for planning
**Parent:** `docs/superpowers/specs/2026-08-01-terrain-pipeline-multiagent-design.md`, Stream 2

## What this is for

The base terrain comes from real USGS elevation data (`Content/elevation.txt`, 71×81 samples at
30m spacing) and is otherwise correct, but a few specific spots need a human hand — a resample
artefact, a lot that wants levelling for a building pad, a low spot that should sit right for a
scene. This is an in-editor brush that nudges height at those spots without ever touching the
real, measured data underneath.

**Edit Mode only.** Sculpting happens in the Scene view with the editor stopped, the same way
Unity's own Terrain tool works — not live in Play mode. That keeps the brush from having to
patch the physics `MeshCollider` (`CityCollision.cs`) or interact with anything Play mode is
doing; it only ever has to keep one thing in sync, the ground you can see.

## What's already there

- `ElevationGrid` (`Assets/Noir/Unity/ElevationGrid.cs`) is a static class holding a single
  immutable `float[,]` grid, loaded once from `elevation.txt`, queried through
  `HeightAt(x, y)` (bilinear-sampled, relative to the Chicago/Attica crossing). No write path
  exists today.
- `VillageMesh.BuildGround` (private, inside `Assets/Noir/Unity/VillageMesh.cs`) bakes the
  visible ground into 64m chunks via `MeshChunks` — one `GameObject` per chunk, named
  `Ground {col},{row}`, each holding its own (non-shared) `Mesh`. Every tile is four
  independent vertices sampling `ElevationGrid.HeightAt` at build time; tiles are not welded to
  their neighbours, they simply sample the same corner height. Risers (the vertical faces that
  close a step between two terrain types) and the pasture skirt beyond the map edge sample it
  too.
- `Space3D.GroundHit` already raycasts against the *real* terrain shape by iterating
  `ElevationGrid.HeightAt` under a ray — not a mesh or physics raycast — so cursor placement for
  a brush needs no new picking code.
- `CityCollision.Build` bakes a second, coarser (30m-step) `MeshCollider` for Play-mode physics
  from the same `ElevationGrid.HeightAt`. Out of scope here (Edit Mode only) — it naturally
  picks up any saved delta the next time the world is built.
- Nothing today builds a persistent Edit-Mode village. The village exists only via
  `VillageHost.Awake` at Play, or in throwaway scenes editor tools build and immediately
  `DestroyImmediate` (`Elevations.cs`, `MapAudit.cs`, etc.).

## Architecture

Four pieces:

1. **`ElevationGrid` gains a delta layer** — a second `float[,]` grid, same 71×81/30m shape as
   the base, added into `HeightAt` after the base lookup. The base grid is never written to.
2. **A ground-only Edit-Mode preview** — the sculpt window builds just the ground mesh (no
   city, buildings, traffic, people) into the open scene when it opens, using the same code
   `VillageMesh.BuildGround` already runs, and tears it down when it closes.
3. **`SculptTerrainWindow`**, an `EditorWindow` hooking `SceneView.duringSceneGui` for mouse
   painting, placing the cursor via `Space3D.GroundHit`.
4. **Live mesh patching** — a stroke never rebuilds the world. It updates the delta grid, then
   walks only the `Ground {col},{row}` chunks the brush overlaps and nudges affected vertices
   directly.

### 1. `ElevationGrid` delta layer

- New content file `Content/elevation-delta.txt`, identical text format to `elevation.txt`
  (`grid cols rows step` header, then rows of floats) — diffable, and consistent with the
  project's plain-text content convention (`elevation.txt`, `parcel-notes.txt`, `names.txt`).
- Missing file → all-zero delta, the same "flat until proven otherwise" fallback
  `elevation.txt`'s own loader already uses for a missing base file.
- `HeightAt` becomes `RawAt(...) - _baseline + DeltaAt(...)`, sampled bilinearly the same way
  the base grid is. This is what makes a single cell nudge blend smoothly into its neighbours
  with no separate falloff math needed at the mesh level.
- New editor-only surface: `GetDeltaCell(col, row)`, `SetDeltaCell(col, row, value)`,
  `SaveDelta()` (writes the text file to `ContentLoader.Root`), and enough grid-shape accessors
  (`DeltaCols`, `DeltaRows`) for the sculpt window to map world positions to cells. Runtime
  code never calls the setter — only the sculpt window does.

### 2. Ground-only preview

- `VillageMesh.BuildGround` becomes a public entry point (or gets a thin public wrapper) so the
  sculpt window can call it directly instead of duplicating the ~260 lines of tile/riser/skirt
  logic. No behaviour change to the existing full build path.
- Opening the window loads `city.txt` (`VillageHost.MapFile`) through the same
  `VillageParser`/`WorldBuilder` path VillageHost uses, builds *only* the ground into a scene
  root (e.g. `SculptPreview`), and caches a `Dictionary<(int col, int row), MeshFilter>` from
  its chunk children — built once, not re-searched per stroke.
- Closing the window (or an explicit "Rebuild" button, for picking up map edits made outside
  the tool) tears the preview root down with `DestroyImmediate`.

### 3. Brush tool

- Mouse down + drag in the Scene view; each sample point comes from `Space3D.GroundHit` against
  the live (base + delta) height, so the brush always reads the terrain as it currently stands,
  including its own prior strokes.
- Window controls: radius, strength, and Shift-to-invert (lower instead of raise) — the
  standard convention for a terrain brush.
- A sample point maps to the nearest delta cell(s); when the brush radius spans more than one
  30m cell, neighbouring cells get a fraction of the stroke's strength weighted by distance from
  the brush centre (smoothstep falloff), so a wide brush doesn't produce a hard step at a cell
  boundary.
- After updating cells, the tool computes the touched world-space bounding box, resolves it to
  the overlapping chunk coordinates (`floor(x / MeshChunks.Size)`), and for every vertex in
  those cached chunks whose world (x, y) falls inside the brush footprint, adds
  `(new delta − old delta)` at that (x, y) straight onto the vertex's stored Y — then
  `RecalculateNormals()` and `RecalculateBounds()` on just those chunk meshes.
- This one adjustment covers flat tiles, risers, and the skirt uniformly: none of them need to
  be told what they are, because the patch is a height *delta* applied at each vertex's own
  (x, y), not a value recomputed from scratch.
- Cost scales with brush size (a handful of chunks, a few dozen–few hundred vertices), not with
  map size — this is what keeps painting frame-drop-free.

### 4. Undo/redo & persistence

- The full delta grid is 71×81 = 5,751 floats (~23KB) — cheap enough to snapshot whole on every
  stroke rather than diff cell-by-cell. The sculpt window keeps its own Undo/Redo stacks of
  full-grid snapshots.
- Undo/Redo are window buttons, plus Ctrl+Z/Ctrl+Y while the Scene view has focus and the
  window is open. Popping a snapshot writes it back into `ElevationGrid` and re-patches every
  chunk touched since that snapshot.
- Deliberately **not** wired into Unity's global Undo system — that would mean wrapping the
  delta grid in a `ScriptableObject` purely to get `Undo.RegisterCompleteObjectUndo` to track
  it. A private stack is simpler, fully deterministic, easy to test in isolation, and can't
  collide with the user's own scene-edit undo history sharing the same Ctrl+Z.
- **Save** is explicit (a button), writing `elevation-delta.txt` via `ElevationGrid.SaveDelta()`.
  Closing the window with unsaved strokes prompts to save first, matching Unity's own
  unsaved-scene prompt.

## Verification gate mapping

From the parent spec's Stream 2 sign-off criteria:

| Gate | How this design meets it |
|---|---|
| Brush is responsive, no frame drops | Patch touches only the chunks under the brush, never a full rebuild |
| Painted deltas persist on save/reload | `elevation-delta.txt`, loaded the same way `elevation.txt` is |
| Undo/redo correct; base grid unchanged | Base `float[,]` is never written; delta-only snapshot stack |
| Integrates with `ElevationGrid` (base + delta) | `HeightAt` sums both layers for every caller, not just the sculpt tool |
| No crashes at terrain boundaries or rapid undo | Chunk lookup clamps the same way `MeshChunks.Bucket` already does; snapshot-based undo has no incremental state to corrupt |

## Out of scope

- Play-mode sculpting, and any live update to `CityCollision`'s `MeshCollider` — Edit Mode
  only, per the confirmed design choice. The collider picks up saved deltas the next time the
  world is built (Play, or any editor tool that runs `WorldBuilder.Build`).
- Texturing and performance work (Streams 3–4 of the parent spec) — this tool only moves
  height.
- Any change to `elevation.txt` itself or its 30m sampling — deltas are additive and separate,
  never a resample.
