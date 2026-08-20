# Authored interiors — the floor plan overrides the generated guess

**Status: DESIGN, awaiting the owner's review.** Nothing here is built.

## The ruling this implements

The owner (2026-08-19, in chat): the floor plans drawn in the browser map should drive the
game — "edit the plan and have something regenerate interior partitions." This spec is the
answer to what "something" is.

## What already exists, and what this actually is

Two systems already meet in the middle; this feature is one bridge between them.

- **Every generated building already has an interior.** `WorldBuilder.StampInterior`
  (`Assets/Noir/Core/World/WorldBuilder.cs:186`) fills the unit solid, asks
  `InteriorGenerator.Generate(bounds, frontDoor, rng, grammar, name)` for a room split
  (`DomesticBsp` for homes, `HallGrammar` for the big kinds), carves the rooms back out of
  the tile grid as `Terrain.Floor` with per-tile `RoomId`s, registers `Room`s with a
  `RoomKind` the sim can see, punches doorways, and furnishes (`FurniturePlacer`). The
  layout is an RNG-seeded guess.
- **The floor plans are authored fact.** `Content/floorplans/<parcel>-<index>.json`, drawn
  in the browser map's editor (rooms, walls, doors, windows, in feet), saved through
  `serve-viewer.py`. 408's two plans exist today, derived from the owner's own model.

So this is NOT "add interiors to the game." It is the project's standing pattern, one layer
deeper: **a measured/derived layer (the BSP guess) overridden by an authored ruling (the
plan) wherever a ruling exists.** The web tool is the source of truth; the game must consume
everything it can write.

## The one design rule: generate, then replace

`StampInterior` still calls `InteriorGenerator.Generate` for every building, exactly as
today — and **discards the result when an authored plan is present**, stamping the plan
instead. This is deliberate and load-bearing:

- **RNG neutrality.** The generator's draws are consumed identically whether or not a plan
  exists, so authoring a plan for one house cannot move a single prop, room or citizen in
  any other. Without this, saving a plan reshuffles the town, and the owner's "why did the
  neighbour's furniture move" bug report writes itself.
- **Fallback for free.** If the plan fails validation (below), the generated interior is
  already in hand and is used, loudly.

## Data contract — how a plan reaches Core

Core must not know parcels exist (the `Outline` precedent, `VillageLayout.cs:111`). The
same hand-over:

1. **New Core type** `AuthoredInterior` (in `Noir.Core.World`):
   ```
   public sealed class AuthoredInterior {
       public List<(TileRect bounds, RoomKind kind, string name)> Rooms;
       public List<Tile> Doors;     // interior doorway tiles, already in tile space
       public bool Furnish = true;  // false when a hand-made model owns the visible inside
   }
   ```
   `Furnish` is how Core learns to leave an owner model's rooms empty without learning what
   an owner model is: the Unity side sets it false for any place `Content/models.txt`
   covers, true otherwise.
2. **New optional field** `PlaceSpec.AuthoredInterior` (null everywhere today, like
   `Outline`). A fixture town never sets it and is stamped exactly as before.
3. **The Unity survey side fills it** in the same pass that already seats footprints
   (`SeatOnSurvey`'s neighbourhood): for each seated building whose `<parcel>-<index>` has
   a plan file, convert the plan to tile space and attach it to the PlaceSpec. Unity knows
   the parcel, the feet and the orientation; Core receives tiles and room kinds.

## Conversion (Unity side, where the parcel is known)

- **Scale**: plan feet → metres (× 0.3048) → tiles, rooms rounded to whole tiles and
  clamped inside `PlaceSpec.Bounds`.
- **Orientation**: the editor draws every plan street-side down. The plan's south edge is
  aligned to the building edge that carries the front door (`PlaceSpec.Door`), rotating in
  90° steps. Skew is ignored — interiors stamp axis-aligned in tile space, exactly as the
  BSP's do.
- **Rooms**: stamped in file order; where two overlap, the later room owns the tiles (the
  editor permits overlap; the grid cannot).
- **Room kinds from names**: a small word-match table onto the real `RoomKind` enum
  (`Room.cs:10` — the domestic kinds are Hall, Living, Kitchen, Bedroom, Bathroom,
  Scullery, Workroom): `bed` → Bedroom, `kitchen`/`kit` → Kitchen, `bath` → Bathroom,
  `living`/`family`/`fam`/`din` → Living (dining furnishes as a front room; the enum's own
  header forbids synonym kinds), `hall`/`entry` → Hall, `laundry`/`utility` → Scullery,
  `office`/`work` → Workroom. An unmatched name defaults to Living — a room the table
  cannot name is still a room. The authored NAME travels on the `Room` regardless, for
  anything that later wants to say "the kitchen".
- **Plan doors** (`kind:"door"` on an interior wall) → the doorway tile nearest the
  opening's centre on the wall between the two rooms. **Plan windows are ignored by Core**
  — the drawing layer already has its own window rules, and a window is not walkable.
- **Exterior plan doors** do not move `PlaceSpec.Door`; the survey's door stays the door.

## Core changes (`StampInterior`)

```
var interior = InteriorGenerator.Generate(...);          // always, for RNG neutrality
if (spec.AuthoredInterior != null && spec.Units == 1)
    interior = Adopt(spec.AuthoredInterior);             // replace the guess
```

- Everything downstream is untouched: solid-fill, carve, `RoomId`s, doorways,
  `ConnectFrontDoor`, furnishing — the authored rooms flow through the same code the
  generated ones do.
- **Connectivity guard**: after stamping, any room unreachable from the front door gets a
  doorway punched to an adjacent room (the existing `TryDoorBetween`/punch-through
  machinery, applied as a repair). An authored plan with a missing door yields a house you
  can still walk, plus a loud log line — never a sealed room, never a refusal.
- **`Units > 1` keeps the BSP.** A terrace is several homes in one building and the plan
  format has no per-unit story yet. Phase 2 if ever wanted.

## Authored furniture (added 2026-08-19, owner's ruling: "see + place + override")

The same override pattern, one layer further in. The plan JSON gains an optional
`furniture` array:

```
"furniture": [ { "id": "f1", "name": "Bed", "model": "",        "x": 2, "y": 3,
                 "w": 5, "h": 6.5, "rot": 0 },
               { "id": "f2", "name": "Stove", "model": "Stove1991", "x": 11, "y": 1.5,
                 "w": 2.5, "h": 2, "rot": 90 } ]
```

- **`name` resolves the KIND** (the same word-match stance as room names, onto the
  `Furniture` kinds Core already places), which is what the sim knows and what decides the
  footprint's meaning. **`model` optionally pins a specific mesh**: an owner model's name,
  or empty for "whatever the pack resolver picks for this kind" — the existing
  `InteriorFurnitureModels` behaviour.
- `AuthoredInterior` gains `public List<AuthoredFurniture> Furniture` (position/rotation in
  tiles by the time Core sees it, converted Unity-side like everything else).
- **Generate-then-replace, again**: `FurniturePlacer` still runs and its draws are still
  consumed; where the plan carries a furniture array, the result is discarded and the
  authored pieces stamped instead. Same reason, same guarantee: furnishing one house moves
  nothing in any other.
- **The owner-model rule sharpens**: an owner-model place skips GENERATED furniture (as
  before — nothing may double up inside his mesh) but ACCEPTS authored furniture. He can
  now furnish 408 from the tool without Designer, piece by piece, and the sim knows the
  bed is a bed.
- **His own furniture models** enter by the established pipeline: Designer GLB →
  `glb-to-obj.py` → `Assets/Noir/Models/<Name>.obj`, plus one row in a new hand-edited
  `Content/furniture-models.txt` (`name | model | kind | width x depth in m`) — the
  furniture analogue of `models.txt`. The Unity resolver tries that table first, the
  curated pack list second, the generated fallback mesh third.
- Failure modes: a piece outside its room is clamped in with a log; a piece blocking a
  doorway tile is refused with a log (the placer's own standing rule — nothing may be
  placed against a door); an unknown `model` falls back to the kind's pack resolution.
- Tests join the Core gate: a fixture plan's furniture lands at exactly the authored
  tiles; the RNG-neutrality test extends to furniture; a doorway-blocking piece is refused
  without wrecking the room.

## Owner-model places (408 today)

The plan is MORE valuable here, not less: the model is the visible geometry, and Core's
BSP rooms inside that place currently disagree with it. With the authored plan:

- Core's rooms, walls and doorway tiles for the place come from the plan → **the sim's
  idea of the inside finally matches the walls the player sees.** (This also advances the
  IDEAS item "owner models must block the walkable grid": the plan's walls stamp
  `Terrain.Wall` into the grid through the standard path.)
- **GENERATED furniture stamping is skipped for owner-model places** — generated pieces
  inside his mesh would double up. AUTHORED furniture (see the section above) is accepted:
  the plan is his hand either way. (Generated buildings with plans and no furniture array
  still furnish normally, by room kind.)
- The Unity drawing side must not raise generated interior wall meshes inside an owner
  model; verify current behaviour and keep it.

## What saving in the tool means after this lands

Save still only writes the JSON. The game reads the files at build, like every Content
file — so the flow is: edit plan → Save → next Play (or "Send to the game") builds those
rooms. **`tools/change_gate.py` must class floorplan edits as STRUCTURAL**: interior walls
move the walkable grid.

The survey report (`/__game`, shown per-lot in the viewer) gains one line per plan:
consumed (rooms stamped) or repaired/refused and why — the same "did my ruling arrive"
answer the lot rulings get.

## Failure modes, named

| Plan problem | Behaviour |
|---|---|
| Room outside the shell / building bounds | clamped; log |
| Rooms overlap | later room wins the tiles |
| Room isolated (no door) | doorway punched to a neighbour; log |
| Plan smaller than the footprint | unclaimed tiles stay Wall (a thick-walled house, not a hole) |
| Unparseable/empty JSON | generated interior used; loud log; survey report says refused |
| Plan for a multi-unit building | ignored (BSP), survey report says why |

## Testing

- **Core** (the standing gate):
  - A fixture PlaceSpec with an `AuthoredInterior` produces exactly the authored rooms,
    kinds and names.
  - RNG neutrality: build the same fixture town twice, once with one authored interior —
    every OTHER place's rooms and furniture are identical.
  - The connectivity repair: an authored plan with a sealed room builds a walkable house.
  - `Units > 1` with a plan attached: BSP used, no throw.
- **PlayMode** (the `!Diagnostic` gate): in the built town, the place named
  `408 Holmes Street` has rooms matching `673-0.json`'s count and kinds. This works in the
  PLAN gate town too — the plan applies to the generated 408 there, so no `Assert.Ignore`
  needed (unlike the owner-model door gates).
- **Look at it**: walk 408's plan-stamped rooms in Play against the model's walls.

## Out of scope, deliberately

- **Upstairs.** Plans are single-storey; 408's attic room stays the model's business.
- **Windows** driving the drawn facade.
- **Per-unit plans for terraces/flats.**
- **Plan → 3D model regeneration.** The model stays hand-made in Designer; the plan's
  Export SVG/PNG remains the reference for that work. This feature changes the SIM's
  interior, and the generated houses' visible interiors — never the owner's mesh.
