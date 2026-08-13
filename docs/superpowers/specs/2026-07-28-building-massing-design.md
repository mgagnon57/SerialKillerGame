# Building massing — making each kind read as what it is

**Date:** 2026-07-28
**Status:** approved, not yet implemented

## The problem, measured

`Frontage.cs` already differentiates buildings well, and it is not the problem. The pub swings a
board on a bracket, the shop has a fascia plus a hanging sign, the surgery has a brass plate that
"would be embarrassed by anything larger", the school has its name cut in stone. Doors are painted
per kind; businesses get shutters, farms and garages get gates.

All of it is at the front door, and none of it is in the silhouette:

```
RoofBuilder.cs:61     AddHipRoof(...)      every roofed building, without exception
RoofBuilder.cs:39     Pitch = 2.2f         the same on all of them
Space3D.cs:18         WallHeight = 3.0f    the same on all of them
```

St Anne's, the mill, the school and a two-up-two-down are **the same box with the same roof**,
differing only in footprint and a sign by the door. From the overview camera — the default view —
there is nothing to tell them apart. A church reads as a church at two hundred metres because of a
tower. Ours has the roofline of a bungalow.

**This spec is about massing and silhouette.** Street-level detail (windows, porches, materials) is
a deliberate follow-up, not part of this work.

## Decisions taken

| | |
|---|---|
| Read distance | Both, **silhouette first** — the wide shot is the default view |
| Fidelity | **Bespoke per kind**, implemented as grammars rather than a switch |
| Success test | A new repeatable `elevations` instrument, plus snapshot determinism |

The fidelity decision needs its condition stated, because it is the one that could undo Stage 4.
Bespoke massing written as a `switch (place.Kind)` inside the mesh builder would put `PlaceKind`
back into Unity's geometry code and make a new amenity cost C# again — exactly what the kind table
was built to prevent. Written as one grammar per kind behind an interface, it costs nothing: an
unknown kind falls back to `cottage` and still looks like a building.

## Architecture

### The seam

A per-place profile that every geometry builder consults instead of the two global constants.

```csharp
public enum RoofForm { Hip, Gable, LeanTo, Flat }

public readonly struct Massing
{
    public readonly float    Eaves;        // replaces Space3D.WallHeight per building
    public readonly RoofForm Roof;
    public readonly float    Pitch;        // replaces RoofBuilder.Pitch per building
    public readonly bool     RidgeAcross;  // ridge on the short axis rather than the long
}
```

`Massing.Of(Place)` is the single entry point. It is a pure function of the place and the kind
table, with no per-frame cost — resolve once at build time and pass the struct down.

### The grammar registry

```csharp
public interface IMassingGrammar
{
    Massing Profile(Place place);
    void    Extras(Place place, MeshChunk into);
}
```

Selected by a new `massing` column in `Content/kinds.txt`. The column is **optional and defaults to
`cottage`**, unlike the columns `PlaceKindTable` asserts on — a missing massing row should give a
plain building, not refuse to load a village. This is a deliberate difference from `rooms`, `roof`
and `frontage`, and the reason is that massing is decoration: a village with an unstyled barber is
worth having, a village that will not open is not.

`Extras` emits into the same chunked mesh as the roofs, so towers and bell-cotes stay inside the
existing chunking and culling and add no renderers of their own.

### The eleven building kinds

Open-ground kinds (green, churchyard, playground, allotments, bus stop, phone box) are `form open`
and have no massing at all.

| kind | grammar | eaves | roof | pitch | signature extra |
|---|---|---|---|---|---|
| dwelling | `cottage` | 3.0 | hip | 2.2 | — |
| farm | `cottage` | 3.0 | hip | 2.2 | — |
| shop | `shopfront` | 3.6 | hip | 2.0 | — |
| postoffice | `shopfront` | 3.6 | hip | 2.0 | — |
| surgery | `shopfront` | 3.4 | hip | 2.0 | — |
| pub | `pub` | 3.4 | gable | 2.4 | heavy chimney stack |
| villagehall | `hall` | 4.2 | gable | 2.6 | porch |
| school | `school` | 4.0 | gable | 2.6 | **bell-cote on the ridge** |
| church | `church` | 5.5 | gable | 4.5 | **west tower and spire** |
| mill | `mill` | 6.5 | gable | 1.6 | **lucam / hoist** |
| garage | `garage` | 3.4 | flat | 0 | wide opening |

`dwelling` and `farm` keep today's exact numbers on purpose. The village people already know should
not shift underneath this change; only the buildings that were failing to announce themselves move.

Extras are positioned relative to the bounds **and the front door**. The church tower goes at the
end furthest from the door, which is what makes it read as a west tower rather than a lump on a
shed. `Locality.AnchorOf` already gives the door; the same call `Frontage` makes.

## Changes to existing code

| file | change |
|---|---|
| `Unity/Massing/` | new — `Massing`, `IMassingGrammar`, the registry, the eleven grammars |
| `World/PlaceKindTable.cs` | new optional `massing` column, defaulting to `cottage` |
| `Content/kinds.txt` | one `massing` line per building kind |
| `Unity/VillageMesh.cs` | **the surgery** — see below |
| `Unity/RoofBuilder.cs` | `AddHipRoof` becomes `AddRoof(form, eaves, pitch)`; gains gable, lean-to, flat |
| `Unity/Frontage.cs` | door head reads `Massing.Of(place).Eaves`, not the constant |
| `Unity/XRay.cs` | outline box reads it too — already noted at `XRay.cs:108` |

### The surgery: walls do not know their building

`BuildWalls` scans the tile grid for wall tiles and merges them into horizontal then vertical runs.
It is purely geometric and never asks which `Place` a wall belongs to, so per-building height is
not free.

The change: paint a place-id-per-tile array once (the same trick `StreetReport` uses for its
roofed-tile lookup), then extend the run-merging condition so a run also breaks when the owning
place changes, and give each run its own height.

Two consequences worth predicting before measuring them:

- Runs break more often, so wall renderer and vertex counts go **up**. `Noir.Bench` says by how
  much. If the cost is real it is paid in the wide shot, where every chunk is on screen anyway.
- Party walls between terrace units belong to two places at once. A terrace is one `Place` with
  several `Units`, so this does not arise for houses — but it must be checked where two separate
  buildings share a boundary tile, and the tie broken deterministically (lowest place id wins) so
  the mesh stays identical run to run.

## How we judge it

### `elevations` — a new instrument

A headless editor renderer emitting **one straight-on PNG per building kind**: no signs, no
frontage, no context, no label. You name the building or it has failed to read.

This is the name-that-building test made repeatable. It grades silhouette specifically rather than
grading the whole picture, it can be regenerated after any change, and it doubles as documentation
of what each kind is supposed to look like. Run alongside the existing snapshot set, not instead of
it.

### Snapshot determinism holds

The twelve snapshots must still come out byte-identical across two separate Unity processes. They
will all change **once** as a result of this work; that re-baseline is deliberate and gets its own
commit so any later diff is attributable.

### The smoke test covers the new path

`SmokeTest` already builds the whole village outside play mode. It gains an assertion that every
building kind resolves to a grammar and that no `Extras` call throws — the geometry failures this
work can introduce are exactly the ones compiling cannot catch.

## Risks

- **Every snapshot moves.** One-time, deliberate, re-baselined in its own commit.
- **Vertex and renderer count go up** from broken wall runs. Measured, not guessed — `Noir.Bench`.
- **Taller buildings cast longer shadows.** `first-light` is the shot with the long raking shadows;
  the mill at 6.5 m and the church tower are what to look at there.
- **The church tower may not fit.** St Anne's is 14x16 with a 9x14 nave. A tower needs a footprint
  carved out of that, and the interior grammar has already placed rooms in it. The tower is
  exterior-only geometry for this pass — it does not claim interior floor space, and that
  simplification is recorded here rather than discovered later.

## Out of scope

Street-level detail: windows, porches, wall materials, doorway mouldings, roof texture variation.
That is the second pass and gets its own spec.
