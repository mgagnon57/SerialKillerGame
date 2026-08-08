# Replacing procedural buildings with Poly Universal Pack

**Status:** design, not yet built. A working prototype exists behind
`Noir/Render Poly Pack Cottage` and the findings below come from it rather than from reading
the pack's store page.

## The goal

Rossville's buildings are generated in code. `Grammars.cs` picks a footprint and massing,
`VillageMesh` / `RoofBuilder` / `Frontage` turn that into walls and roofs, and `Materials3D`
colours them. There are no models in the project at all — no `.fbx`, no `.prefab`, no `.mat`.

The intent is to replace that generated geometry with parts from the Poly Universal Pack
(polyperfect, `Assets/polyperfect/`), keeping the simulation and the village layout exactly as
they are. Buildings should stop being extruded prisms and start being houses.

## What the prototype settled

A 6m × 4m cottage assembled entirely from kit parts — plain walls, a 45° gabled slate roof,
gable ends, chimney, door and window — standing in the village under `SunRig`'s own light:

- `docs/snapshots/compare-polypack-close.png` — the pack cottage
- `docs/snapshots/compare-polypack.png` — the same in village context
- `docs/snapshots/compare-ashcombe.png` — what Rossville builds today, for contrast

**The kit is genuinely modular and metric**, and its vocabulary lines up with concepts the
codebase already has. Walls come in 1–5m widths in 3m and 4m tall families, with door and
window variants pre-cut. Roofs are sorted by pitch — Regular, Moderate, Steep, Very Steep —
which maps onto the roof-steepness the massing code already decides. Doors, windows, chimneys,
gutters, floors and stairs are separate. Only `Modular Parts/` is of interest; the ~680
prefabs under `Prefabs/City`, `Prefabs/Farm` and the rest are whole fixed buildings (bank,
diner, skyscraper) and are the wrong thing for this village.

**This is therefore a renderer swap, not a layout redesign.** `Grammars.cs` and everything
upstream of it can stay as it is.

## What it cost to learn, and must not be re-learned

These are the things that were wrong on the first attempt. Each one produced a plausible-looking
failure rather than an error, so each needs to be encoded rather than rediscovered.

1. **`_Ext` wall pieces are single-sided outer skins.** That is what the matching `_Int` pieces
   are for. The one visible face points along **+z at rest**, which is the opposite of what the
   0.1m thickness running that way suggests. Faced the other way the cottage builds inside out:
   every wall's only face points into the room and you see straight through the house from the
   road. The facing convention belongs in the catalog.

2. **Wall pivots are on the right edge, at ground level**, with thickness running to +z. A
   segment spanning `[a, a+w]` is therefore placed at `a` or `a+w` depending on its rotation.

3. **Roof tiles are 45°, high edge at their −z side, with a 0.13m lip hanging below the pivot.**
   Seat one at wall-top without subtracting the lip and every roof floats a hand's breadth above
   its walls.

4. **Only one handedness of gable half ships.** A rotation about Y cannot turn a right triangle
   into a left one, so the far half has to be reflected through the ridge plane by a parent with
   a negative axis. Both halves then sit at the same local offset.

5. **Neither the pack's materials nor Rossville's textured ones can be used.** The kit's atlas
   materials sampled to a flat blue. But `Materials3D.Wall` and `Materials3D.Roofs` are tiled
   against Rossville's own world-space UVs, and the kit's meshes are atlas-mapped — every vertex
   of a panel lands on one palette texel — so under those a wall rendered *invisible*.
   `Materials3D.Stone`, the one untextured member of the set, was the only one that worked.
   **Pack geometry must be painted with flat, untextured colours in Rossville's palette.**

6. **Door and window inserts have inconsistent pivots.** A door is centred on its own opening;
   a window is not. Seat inserts by measuring the piece's bounds and moving its centre onto the
   hole, not by a per-prefab offset.

7. **Sub-family is a style decision, not a detail.** The `Fantasy` doors and windows read
   medieval — an arched plank door and a pointed leaded light. `Plain`, `Plain City`,
   `Horizontal A/B` and `Vertical A/B/C` wall families are all available and read differently.
   Pick deliberately and consistently before building the catalog.

8. **There is no exterior trim in the kit.** `Walls/Trims/` is entirely `_Int_` — skirting and
   ceiling coving. The white casing round a window in the publisher's own farmhouse is part of
   the *window prefab*, so choosing the family IS how you get trim. Swapping Fantasy for City
   is what turned the prototype from a shed with holes into something that reads as a house.

9. **Repaint by the original material's name, not by piece.** A window is a frame submesh plus
   a pane submesh, and the kit names the second one for what it is. Painting every slot the
   same colour turns a window into a dark slab — a frame with no glass reads as a hole. Keep
   anything the pack called glass as glass.

10. **Inserts keep their own height.** A City window carries its sill at 0.54m and its head at
    2.65m. Steer inserts onto the opening in x and z only; re-centring them on a guessed
    mid-height slides them off the hole they were cut for.

## The dressing layer

Eaves, barge boards, gutters and a downpipe are what the eye reads as "a built house" rather
than "a shape", and none of them is structural. The bare prototype used only flat slope tiles,
so the roof stopped dead at the wall with no overhang and no shadow line under it — the single
biggest difference from the publisher's dressed farmhouse.

Adding them is cheap, because the pivots cooperate: an `_Edge` piece occupies the half metre
*outside* the eave line exactly where the slope tile occupies the two metres inside it, so eaves
take the same position and rotation as the tile they hang off. Gutters pivot on their **left**
edge, unlike walls and roofs. Barge boards (`_Front`) have the same one-handedness problem as
the gable and need the same reflection.

**This doubles the piece count: 26 → 49 for one cottage.** Which makes the draw-call question
below sharper, not softer — a hundred dressed buildings is on the order of five thousand
renderers.

Still missing against the reference, in rising order of cost: a plinth (no dedicated piece —
would need a thin floor slab or a bespoke one), porch steps (`Stairs/` has them), and **a second
volume** — the lean-to with its own lower roof. That last one is not decoration, it is massing,
and it means `Grammars.cs` would have to emit buildings as more than one box. It is the only
item on this list that changes the shape of the work.

## Shape of the work

**A seam in the Unity layer only.** `Noir.Core` stays a pure simulation and is not touched.
Introduce a building-renderer interface that takes the same footprint and attribute data
`VillageMesh` already has per building, with the current procedural generator as one
implementation and the pack assembler as the other. The toggle is worth keeping permanently: it
makes A/B comparison per building possible while the catalog is filled in, and it is the
fallback if a footprint has no pack representation yet.

**A piece catalog.** A lookup from the semantics the code already computes — segment width,
whether the segment carries a door or a window, roof pitch, wall and roofing material — to a
prefab path plus its placement rule (rotation, pivot offset, facing). Everything in the previous
section lives here, once.

**Footprints must decompose into whole-metre runs.** Walls exist at 1, 2, 3, 4 and 5 metres, so
each face has to be expressible as a sum of those. Village tiles are metres, so this should hold
already — but it needs checking against the footprints `Grammars.cs` actually emits before the
catalog is designed around it.

## The main risk: draw calls

Today `MeshChunks` merges building geometry on a 64m grid and gets the village down from 5,487
renderers to 1,835. The prototype cottage is **26 separate prefab instances**. At a hundred-odd
buildings that is several thousand additional renderers, which would be a serious performance
regression if they are instantiated naively and left as individual objects.

The mitigation is the one already in the codebase: bake the placed pack pieces into combined
chunk meshes exactly as `MeshChunks` does now, rather than leaving live prefab instances in the
scene. **This should be proven early — ideally in the same step as the first real building
type — because if per-building piece counts make chunking impractical, that changes the whole
approach and is much cheaper to discover now than after the catalog is written.**

## Other things that are wired to the current geometry

These all work today by reaching into procedurally generated meshes and will each need an
equivalent on pack pieces:

- **Lit windows.** `SunRig.BuildFixtures` builds window lights and glowing panes per place, and
  `Snapshot.LightUp` drives them. Pack windows are separate insert prefabs, so the pane and the
  light have to be attached to those instead.
- **Shutters.** `Frontage.HasShutter` / `Frontage.SetOpen` open and close shop shutters on the
  authored opening hours.
- **X-ray.** `XRay` hides buildings so the population can be watched. Pack walls being
  single-sided may make the interior read oddly when it is toggled.
- **Roofing variety.** `Materials3D.RoofingFor` gives each building slate, tile, worn tile or
  thatch from a stable hash. The kit has real material variants; the mapping should preserve the
  same per-building stability so a roof does not change as you walk past.

## Repository question

`Assets/polyperfect/` is **1.4GB on disk** once imported (764MB as a download). It has been
added to `.gitignore` as a precaution, because the cost of the two mistakes is wildly
asymmetric: un-ignoring it is one deleted line, whereas a 1.4GB blob committed by an absent
minded `git add -A` is painful to get back out of history. The pack is also licensed per seat,
which is an argument against committing it anywhere public.

If it should be tracked after all — a vendored-dependency argument, or Git LFS — remove the
entry deliberately.

## Scaffolding that exists now

Three editor scripts, all evaluation aids rather than product code:

- `Assets/Noir/Editor/PolyPackProbe.cs` — prints piece pivots, bounds, corner heights and
  height profiles. This is how the conventions above were established; keep it until the catalog
  is settled, then delete.
- `Assets/Noir/Editor/PolyPackCottage.cs` — assembles the prototype cottage and photographs it
  beside a real Rossville cottage under identical light.
- `Assets/Noir/Editor/PolyPackPreview.cs` — renders the publisher's own demo scenes, for judging
  the kit's stock look.

## Open questions

- Which wall and roof sub-families are the village's, given the Fantasy set reads medieval?
- Do the footprints `Grammars.cs` emits decompose cleanly into 1–5m runs?
- Do 3m walls suit every building, or do the taller places (mill, church, hall) need the 4m
  family and a different massing rule?
- Is the roof pitch vocabulary (Regular / Moderate / Steep / Very Steep) mapped from something
  the massing code already decides, or does it become a new per-building property?
