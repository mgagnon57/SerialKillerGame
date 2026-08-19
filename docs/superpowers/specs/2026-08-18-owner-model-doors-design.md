# Owner-model doors and interiors — design

> **Landed 2026-08-18 (night)**, plan `docs/superpowers/plans/2026-08-18-owner-model-doors.md`.
> Live-verified in the owner's own session: 4 hinges at 408 (front, rear, garage service,
> overhead panel), leafless 0 town-wide, front doorway capsule-passable with real floor
> inside, house at authored grade, P spawning on the front walk. Three PlayMode gates added
> (baseline 35 → 38); the first full-gate measure is tonight's nightly run. Two lessons the
> first hour taught: a prefab instance must be UNPACKED before its doors can be rehung
> (Unity logs a per-piece error and every hinge stands empty), and the survey's door tile
> for 408 is on the REAR of the lot - the front walk is the lot's south edge.

**Ruled by the owner, 2026-08-18 (late), in chat.** His hand-made houses open their doors and
let the player in, starting with 408 Holmes Street; P spawns him at that front door. Approved
scope, his words: all doors including the garage overhead panel; interiors bare, as authored;
spawn at 408.

## What exists and is kept

- **The model pipeline** (`tools/glb-to-obj.py` → `Assets/Noir/Models/*.obj` →
  `Content/models.txt` → `CityBuildings.Landmark`) stands whole models on lots. Today the
  converter FLATTENS the GLB's node tree into one welded mesh — that flattening is the only
  thing this design removes.
- **`CityDoors`** already swings generated leaves: `Add(hinge, shutYaw, openYaw)`, proximity
  swing, the E-key verb through `PlayerInteraction`, `Force`, `Leafless()` and its PlayMode
  gate. Houses swing inward. The bake (`CityChunker.Combinable`) already spares any renderer
  whose parent is named `hinge`.
- **`CityCollision`** boxes every bought/owner building from its renderer bounds — one solid
  box, which is exactly why an owner model cannot be entered today.

## The naming convention (what the owner already does)

Measured off `408-residence.glb`, 244 named nodes; this design just writes the contract down:

- `door_<name>_slab`, `door_<name>_lite`, `door_<name>_knob` — the swinging parts of one
  door. `door_<name>_casing` stays in the wall. Hinge edge = the slab's edge nearer the
  nearest `wall_*`/`brick_*` piece; ties break toward the slab's -x edge in model space.
- `garage_door_panel` (+ `garage_door_ribs`, `garage_door_lites`) — an overhead door; swings
  as a TILT-UP about its own top edge (the one-piece 1991 door). `garage_door_casing` stays.
- `floor_*`, `ceiling_*`, `partition_*`, `wall_*` — structure. Everything structural
  collides; the player walks the modeled floors.
- Soft dressing never collides: nodes named `shrub_*`, `grass_*`, `foliage`-material pieces,
  `garden_hose`, `hose_reel`, `porch_string_lights`, `paving_joints`.

## The changes

1. **`tools/glb-to-obj.py`** — emit one OBJ group (`g <node-name>`) per GLB node instead of
   one merged mesh. Unity's OBJ importer then yields named child objects. Existing flattened
   models keep working (one child); 408 is re-converted from the GLB already in Downloads —
   no re-export.

2. **`CityBuildings`** — after `Landmark` stands an owner model, a new `HingeOwnerDoors(go)`
   pass: for each distinct `door_<name>` family, create an empty pivot named `hinge` at the
   hinge edge, parent slab/lite/knob under it, and register with `CityDoors.Add` (shut yaw =
   standing yaw; open = shut ± 85° swinging INTO the building — the house rule).
   `garage_door_panel` registers with the new lift kind below. Casing and everything else
   stay put and bake as before.

3. **`CityDoors`** — a second door kind: **lift**. A lift hinge rotates about its LOCAL
   horizontal top-edge axis, shut pitch 0 to open pitch −80°, same proximity/verb/Force
   plumbing, same once-per-frame budget. Data: the existing parallel arrays gain a
   `_kind` byte; `Add` keeps its signature (swing) and a new `AddLift(hinge)` joins it.

4. **`CityCollision`** — owner-model places skip the bounds box. Instead: one static
   collision mesh per model, combined at build from every structural piece's mesh
   (readable: OBJ imports are), EXCLUDING door slabs/lites/knobs/panels and the soft-dressing
   names above. Non-convex MeshCollider, same parent, same layer as the ground. The doorway
   becomes a real hole; the rooms become rooms.

5. **`Player.Spawn`** — `Standing()` first asks the world for the place named
   `408 Holmes Street` (a const with a comment saying whose door this is); if present, spawn
   on the front walk one stride outside its door tile, facing the house. The road-centre
   fallback stays for fixtures and towns without the address.

## Error handling

- A `door_*` family with no slab: logged loudly, skipped — the door stays decorative.
- A model with no recognised structure nodes (old flattened OBJs): collision falls back to
  the bounds box exactly as today, logged once.
- `Leafless()` and its PlayMode gate now also cover owner hinges for free (same registry).

## Testing

- Core: none — this is all Unity-layer (the 29%/71% rule; PlayMode is the automated eye).
- PlayMode additions to the `!Diagnostic` gate:
  - `TheOwnersDoorsSurviveTheBake` — after build, every registered hinge under 408 has a
    renderer (the leafless gate, scoped to the owner model).
  - `TheFrontDoorOfHolmesAdmitsThePlayer` — a capsule cast through the front doorway from
    walk to hall passes with the leaf open and the collision mesh in place.
  - Spawn: assert the player's first stand is within a few tiles of 408's door when the
    address exists.
- Look at it: walk in through the front door at 408, close it behind you, up through the
  house, out the back, service door into the garage, tilt the panel up. Owner's eye rules.

## Known limitation (parity, not regression)

Door leaves — generated and owner alike — do not physically block; a shut door is visual.
Making leaves collide town-wide is its own future decision.

## Out of scope

Furnished owner interiors (he furnishes in Designer); roof cutaway over owner models in
orbit view; citizens pathing through owner interiors; garage door opening for the household
car.
