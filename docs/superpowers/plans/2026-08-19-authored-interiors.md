# Authored Interiors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A floor plan drawn in the browser map (`Content/floorplans/<parcel>-<index>.json`) overrides the generated interior — rooms, doors and furniture — for that building, with zero effect on any other building.

**Architecture:** Core gains an `AuthoredInterior` handed over on `PlaceSpec` (the `Outline` pattern — Core never learns what a parcel is). `WorldBuilder.StampInterior` still runs the generator for RNG neutrality, then replaces its result where a plan exists. The Unity survey side (`SeatOnSurvey`) converts plan feet to oriented tiles and attaches. `change_gate.py` learns floorplan edits are structural.

**Tech Stack:** C# (Noir.Core + Noir.Unity), NUnit (tools/Noir.Core.Tests), Python (tools/change_gate.py), Unity JsonUtility for plan JSON.

**Spec:** `docs/superpowers/specs/2026-08-19-authored-interiors-design.md` (read it first; the "generate, then replace" section is the one rule nothing may violate).

## Global Constraints

- **Core purity**: no UnityEngine types in `Assets/Noir/Core`; no `System.Random`/`DateTime.Now`; no transcendentals (`Math.Sqrt` allowed). No Core type named like a UnityEngine type.
- **RNG neutrality is a hard requirement**: `InteriorGenerator.Generate` and `FurniturePlacer.Place` are ALWAYS called with the same draws whether or not a plan exists. Replace results, never skip calls.
- **Core gate**: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj` — 596 pass, 0 fail, 8 skipped baseline; this plan adds tests, never removes.
- **Unity compile checks** (cheap, after any Unity-side task): `dotnet build Noir.Unity.csproj -c Debug`, `dotnet build Noir.Editor.csproj -c Debug`, `dotnet build Noir.PlayTests.csproj -c Debug`.
- **Do not edit .cs while a batch Unity run is going.** Check for `Unity.exe` before PlayMode runs; the editor wins.
- **Never `git add -A`**; stage exactly the files touched. Commit messages in the repo's plain-sentence style.
- Moving the Core baseline number: update CLAUDE.md and `tools/nightly-gate.ps1` in the same commit that moves it.

---

### Task 1: Core — AuthoredInterior rooms and doors, generate-then-replace

**Files:**
- Create: `Assets/Noir/Core/World/AuthoredInterior.cs`
- Modify: `Assets/Noir/Core/World/VillageLayout.cs` (PlaceSpec, after the `Outline` field ~line 124)
- Modify: `Assets/Noir/Core/World/WorldBuilder.cs:186-244` (StampInterior)
- Test: `tools/Noir.Core.Tests/AuthoredInteriorTests.cs`

**Interfaces:**
- Produces: `public sealed class AuthoredInterior { public readonly List<AuthoredRoom> Rooms; public readonly List<Tile> Doors; public bool Furnish = true; }` and `public readonly struct AuthoredRoom { public readonly TileRect Bounds; public readonly RoomKind Kind; public readonly string Name; }` (constructor `(TileRect, RoomKind, string)`), plus `PlaceSpec.AuthoredInterior` (null default). Task 3 adds a `Furniture` list to `AuthoredInterior`; Task 5 constructs these from Unity.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The floor plan drawn in the browser map overrides the generated interior. The three
    /// promises: the authored rooms land exactly; a sealed room is repaired, not built; and
    /// authoring one house moves NOTHING in any other, because the generator's draws are
    /// consumed either way. Spec: docs/superpowers/specs/2026-08-19-authored-interiors-design.md.
    /// </summary>
    public class AuthoredInteriorTests
    {
        private static VillageLayout TwoHouses(AuthoredInterior forFirst)
        {
            TestContent.EnsureKinds();
            var layout = new VillageLayout { Width = 200, Height = 200 };
            layout.Roads.Add(new RoadRun
            {
                Name = "mckibben", Width = 10,
                Points = { new Tile(0, 100), new Tile(199, 100) },
            });
            layout.Places.Add(new PlaceSpec
            {
                Kind = PlaceKind.Dwelling, Name = "authored house",
                Bounds = new TileRect(40, 106, 14, 10), Door = new Tile(46, 106),
                AuthoredInterior = forFirst,
            });
            layout.Places.Add(new PlaceSpec
            {
                Kind = PlaceKind.Dwelling, Name = "control house",
                Bounds = new TileRect(80, 106, 13, 7), Door = new Tile(86, 106),
            });
            return layout;
        }

        // Interior tiles of Bounds(40,106,14,10) are x 41..52, y 107..114.
        private static AuthoredInterior TwoRoomPlan()
        {
            var a = new AuthoredInterior();
            a.Rooms.Add(new AuthoredRoom(new TileRect(41, 107, 5, 8), RoomKind.Kitchen, "Kitchen"));
            a.Rooms.Add(new AuthoredRoom(new TileRect(47, 107, 6, 8), RoomKind.Bedroom, "Bedroom 1"));
            a.Doors.Add(new Tile(46, 110));                    // through the wall between them
            return a;
        }

        [Test]
        public void AuthoredRoomsLandExactlyWhereTheyWereDrawn()
        {
            var world = WorldBuilder.Build(TwoHouses(TwoRoomPlan()), 1234UL);
            var place = world.AllPlaces.Single(p => p.Name == "authored house");
            var rooms = world.AllRooms.Where(r => r.Building.Equals(place.Id)).ToList();

            Assert.That(rooms.Count, Is.EqualTo(2));
            Assert.That(rooms.Any(r => r.Kind == RoomKind.Kitchen
                                    && r.Bounds.Equals(new TileRect(41, 107, 5, 8))));
            Assert.That(rooms.Any(r => r.Kind == RoomKind.Bedroom
                                    && r.Bounds.Equals(new TileRect(47, 107, 6, 8))));
            // The authored door tile is floor, and the wall between the rooms is wall.
            Assert.That(world.Grid.TerrainAt(46, 110), Is.EqualTo(Terrain.Floor));
            Assert.That(world.Grid.TerrainAt(46, 107), Is.EqualTo(Terrain.Wall));
        }

        [Test]
        public void ASealedRoomGetsADoorwayPunchedNotBuilt()
        {
            var plan = TwoRoomPlan();
            plan.Doors.Clear();                                // the owner forgot the door
            var world = WorldBuilder.Build(TwoHouses(plan), 1234UL);

            // Some tile of the wall between x=46 must have become floor: the repair.
            bool connected = false;
            for (int y = 107; y <= 114; y++)
                if (world.Grid.TerrainAt(46, y) == Terrain.Floor) connected = true;
            Assert.That(connected, Is.True,
                "an authored plan with no interior door must still build a walkable house");
        }

        [Test]
        public void AuthoringOneHouseMovesNothingInAnyOther()
        {
            var with = WorldBuilder.Build(TwoHouses(TwoRoomPlan()), 1234UL);
            var without = WorldBuilder.Build(TwoHouses(null), 1234UL);

            var controlWith = with.AllPlaces.Single(p => p.Name == "control house");
            var controlWithout = without.AllPlaces.Single(p => p.Name == "control house");

            var roomsWith = with.AllRooms.Where(r => r.Building.Equals(controlWith.Id))
                .Select(r => r.Kind + " " + r.Bounds).ToList();
            var roomsWithout = without.AllRooms.Where(r => r.Building.Equals(controlWithout.Id))
                .Select(r => r.Kind + " " + r.Bounds).ToList();
            Assert.That(roomsWith, Is.EqualTo(roomsWithout),
                "the control house's rooms moved - the generator's draws were not preserved");

            var furWith = with.AllFurniture.Where(f => roomsOf(with, controlWith.Id).Contains(f.Room))
                .Select(f => f.Kind + " " + f.Footprint).OrderBy(s => s).ToList();
            var furWithout = without.AllFurniture.Where(f => roomsOf(without, controlWithout.Id).Contains(f.Room))
                .Select(f => f.Kind + " " + f.Footprint).OrderBy(s => s).ToList();
            Assert.That(furWith, Is.EqualTo(furWithout),
                "the control house's furniture moved - a plan must not reshuffle the town");

            HashSet<RoomId> roomsOf(WorldModel w, PlaceId id) =>
                w.AllRooms.Where(r => r.Building.Equals(id)).Select(r => r.Id).ToHashSet();
        }

        [Test]
        public void AMultiUnitBuildingIgnoresThePlan()
        {
            var layout = TwoHouses(TwoRoomPlan());
            layout.Places[0].Units = 4;
            Assert.DoesNotThrow(() => WorldBuilder.Build(layout, 1234UL));
        }
    }
}
```

Adjust the `world.Grid`/`TerrainAt` accessors to the real `WorldModel` surface if they differ (check `WorldModel.cs` — rooms via `AllRooms`, furniture via `AllFurniture` are confirmed; the grid accessor name must be read from the file, not guessed).

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~AuthoredInteriorTests"`
Expected: compile FAILURE — `AuthoredInterior` does not exist. That counts as the failing state for a type-introducing task.

- [ ] **Step 3: Implement**

`Assets/Noir/Core/World/AuthoredInterior.cs`:

```csharp
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>One room of an authored floor plan, already in tile space.</summary>
    public readonly struct AuthoredRoom
    {
        public readonly TileRect Bounds;
        public readonly RoomKind Kind;
        public readonly string Name;

        public AuthoredRoom(TileRect bounds, RoomKind kind, string name)
        {
            Bounds = bounds;
            Kind = kind;
            Name = name ?? "";
        }
    }

    /// <summary>
    /// A floor plan the owner drew, overriding the generated interior of one building.
    ///
    /// Core never reads Content/floorplans/ and never learns what a parcel is: the Unity
    /// survey side converts feet to oriented tiles and hands the result over on
    /// PlaceSpec.AuthoredInterior — the same hand-over Outline uses for the measured
    /// footprint. A fixture town never sets it and is stamped exactly as before.
    ///
    /// Spec: docs/superpowers/specs/2026-08-19-authored-interiors-design.md.
    /// </summary>
    public sealed class AuthoredInterior
    {
        public readonly List<AuthoredRoom> Rooms = new List<AuthoredRoom>();

        /// <summary>Interior doorway tiles — each should sit in a wall between two rooms.</summary>
        public readonly List<Tile> Doors = new List<Tile>();

        /// <summary>
        /// False when a hand-made model owns the visible inside (a Content/models.txt
        /// building): generated furniture would double up inside his mesh. Authored
        /// furniture is stamped regardless — the plan is his hand either way.
        /// </summary>
        public bool Furnish = true;
    }
}
```

`PlaceSpec` (in `VillageLayout.cs`, directly after `Outline`):

```csharp
        /// <summary>
        /// The interior the owner drew for this building, in tile space, or null for the
        /// generated one. Filled by the Unity survey side from Content/floorplans/; nothing
        /// in the map file writes it and nothing in Core computes it — the Outline pattern.
        /// </summary>
        public AuthoredInterior AuthoredInterior;
```

`StampInterior` — replace the two lines at WorldBuilder.cs:197-199 with:

```csharp
            var plan = PlaceKindTable.Current.Row(spec.Kind);
            // ALWAYS generated, even when a plan will replace it: the generator's draws are
            // consumed identically either way, so authoring one house cannot move a stick of
            // furniture in any other. The result is also the fallback if the plan is bad.
            var interior = InteriorGenerator.Generate(bounds, frontDoor, rng, plan.Grammar, plan.Name);
            bool authored = spec.AuthoredInterior != null && spec.Units == 1
                            && spec.AuthoredInterior.Rooms.Count > 0;
            if (authored) interior = Adopt(spec.AuthoredInterior, bounds, rng);
            if (interior.Rooms.Count == 0) return;
```

and add the adoption + repair below `StampInterior` (names/kinds only in this task; the
room name is carried in Task 2):

```csharp
        /// <summary>
        /// The authored plan as an Interior: rooms clamped inside the unit, the authored
        /// doors kept, and any room the doors leave unreachable given a doorway to its
        /// nearest neighbour — a plan with a missing door yields a walkable house and a
        /// note, never a sealed room and never a refusal.
        /// </summary>
        private static Interior Adopt(AuthoredInterior authored, TileRect bounds, IRng rng)
        {
            var interior = new Interior();
            var inner = new TileRect(bounds.X + 1, bounds.Y + 1,
                                     Math.Max(1, bounds.W - 2), Math.Max(1, bounds.H - 2));
            foreach (var room in authored.Rooms)
            {
                var r = room.Bounds.Intersect(inner);          // clamp; see step note below
                if (r.W < 1 || r.H < 1) continue;
                interior.Rooms.Add((r, room.Kind));
            }
            foreach (var door in authored.Doors)
                if (bounds.Contains(door)) interior.Doors.Add(door);

            // Connectivity repair: union-find over rooms, joined where a door tile touches
            // both, then TryDoorBetween for every room still cut off from room 0.
            // (Reuse InteriorGeometry.TryDoorBetween — it already knows what a shared wall is.)
            ConnectAuthoredRooms(interior, rng);
            return interior;
        }
```

`ConnectAuthoredRooms`: mark room 0 reached; repeat passes: a room becomes reached if an
existing door tile is adjacent (4-neighbourhood) to both it and a reached room; for each
unreached room after the passes, try `InteriorGeometry.TryDoorBetween(reachedRoom.bounds,
room.bounds, rng, out var door)` against every reached room, add the first success to
`interior.Doors`, mark reached, and loop until stable. A room no door can reach (not
adjacent to anything) is left — `ConnectFrontDoor`'s punch already guarantees the front
door reaches SOME floor, and an island room is the owner's drawing to fix; log nothing in
Core (Core has no logger), the Unity conversion warns (Task 5).

If `TileRect` has no `Intersect`, write the four-line clamp inline (max of X/Y, min of
Right/Bottom, reject on negative size). If `InteriorGeometry` is `internal`, make
`TryDoorBetween` visible to `WorldBuilder` (same assembly — it already is).

- [ ] **Step 4: Run the new tests**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj --filter "FullyQualifiedName~AuthoredInteriorTests"`
Expected: 4 PASS.

- [ ] **Step 5: Run the whole Core gate**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj`
Expected: 600 pass (596 + 4), 0 fail, 8 skipped. Any other red is a regression this task caused.

- [ ] **Step 6: Commit**

```bash
git add Assets/Noir/Core/World/AuthoredInterior.cs Assets/Noir/Core/World/VillageLayout.cs Assets/Noir/Core/World/WorldBuilder.cs tools/Noir.Core.Tests/AuthoredInteriorTests.cs
git commit -m "An authored floor plan overrides the generated interior, and moves nothing else"
```

---

### Task 2: Core — the room keeps its authored name, and names resolve kinds

**Files:**
- Modify: `Assets/Noir/Core/World/Room.cs` (Room gains `Name`; RoomWords added to the same file)
- Modify: `Assets/Noir/Core/World/WorldBuilder.cs` (thread the name through StampInterior)
- Test: `tools/Noir.Core.Tests/RoomWordsTests.cs`, additions to `AuthoredInteriorTests.cs`

**Interfaces:**
- Produces: `Room.Name` (string, "" for generated rooms) with constructor overload `Room(id, building, kind, bounds, anchor, name)`; `public static class RoomWords { public static RoomKind KindFor(string name); }`.
- Consumes: `AuthoredRoom.Name` from Task 1.

- [ ] **Step 1: Write the failing tests**

```csharp
using NUnit.Framework;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The word-match from an authored room's name to the RoomKind the sim understands.
    /// The table is deliberately small: the enum's own header forbids synonym kinds, so
    /// "dining" and "family room" both furnish as the front room.
    /// </summary>
    public class RoomWordsTests
    {
        [TestCase("Bedroom 1", RoomKind.Bedroom)]
        [TestCase("bed 2", RoomKind.Bedroom)]
        [TestCase("Kitchen + dining", RoomKind.Kitchen)]
        [TestCase("Bath 2 (addition)", RoomKind.Bathroom)]
        [TestCase("Half bath", RoomKind.Bathroom)]
        [TestCase("Living room", RoomKind.Living)]
        [TestCase("Family room", RoomKind.Living)]
        [TestCase("Dining / entry", RoomKind.Living)]
        [TestCase("Hall", RoomKind.Hall)]
        [TestCase("Back hall", RoomKind.Hall)]
        [TestCase("Laundry", RoomKind.Scullery)]
        [TestCase("utility", RoomKind.Scullery)]
        [TestCase("Office", RoomKind.Workroom)]
        [TestCase("Sun porch", RoomKind.Living)]      // unmatched -> the front-room default
        public void NamesResolveToTheKindsTheSimKnows(string name, RoomKind expected)
            => Assert.That(RoomWords.KindFor(name), Is.EqualTo(expected));
    }
}
```

And in `AuthoredInteriorTests`, one addition:

```csharp
        [Test]
        public void TheAuthoredNameTravelsOnTheRoom()
        {
            var world = WorldBuilder.Build(TwoHouses(TwoRoomPlan()), 1234UL);
            var place = world.AllPlaces.Single(p => p.Name == "authored house");
            Assert.That(world.AllRooms.Any(r => r.Building.Equals(place.Id)
                                             && r.Name == "Bedroom 1"));
        }
```

- [ ] **Step 2: Run to verify failure** — same filter commands; expect compile failure on `RoomWords` / `Room.Name`.

- [ ] **Step 3: Implement**

`RoomWords.KindFor`: lower-case the name, then first match wins in this order —
`bed`→Bedroom, `bath`→Bathroom (checked BEFORE `bed` would false-match nothing; order
bath/bed does not collide but keep bath first anyway for "bathroom"), `kit`→Kitchen,
`hall`/`entry` (entry only when not preceded by "dining "? No — "Dining / entry" must be
Living: match `din`→Living BEFORE `entry`)… the exact order that satisfies the test
table: `bath` → Bathroom; `bed` → Bedroom; `kit` → Kitchen; `din`/`liv`/`family`/`lounge`
→ Living; `laundry`/`utilit`/`scull` → Scullery; `office`/`work`/`study` → Workroom;
`hall`/`entry`/`foyer` → Hall; default Living. Use `IndexOf(..., StringComparison.Ordinal)`
on the lowered string; no regex, no culture surprises.

`Room`: add `public readonly string Name;` set from a new constructor parameter with a
`= ""` default — the existing 5-argument calls compile unchanged. In `StampInterior`,
the authored branch carries names: adopt returns room names alongside — simplest is a
parallel `List<string>` on `Interior` filled by `Adopt` ("" for generated), consumed at
the `rooms.Add(new Room(...))` line.

- [ ] **Step 4: Run tests** — expect the 14 new cases + 1 addition green.
- [ ] **Step 5: Whole Core gate** — expect 615 pass, 0 fail, 8 skipped.
- [ ] **Step 6: Commit** (`Room.cs`, `WorldBuilder.cs`, both test files):

```bash
git commit -m "A room remembers what the owner called it, and the name picks its kind"
```

---

### Task 3: Core — authored furniture

**Files:**
- Modify: `Assets/Noir/Core/World/AuthoredInterior.cs` (AuthoredFurniture + list)
- Modify: `Assets/Noir/Core/World/Furniture.cs` (`Model` field + `FurnitureWords`)
- Modify: `Assets/Noir/Core/World/WorldBuilder.cs` (StampInterior furnishing branch)
- Test: `tools/Noir.Core.Tests/AuthoredFurnitureTests.cs`

**Interfaces:**
- Produces: `public readonly struct AuthoredFurniture { public readonly FurnitureKind Kind; public readonly TileRect Footprint; public readonly string Model; }` on `AuthoredInterior.Furniture` (List, empty default); `Furniture.Model` (string, "" = resolve by kind); `FurnitureWords.KindFor(string)` (bed→Bed, stove/cooker/range→Cooker, fridge→Cabinet? NO — no synonym invention: fridge→Counter is wrong too; the table maps only words with an existing kind: bed, wardrobe, dresser, table, chair, sofa/couch→Sofa, cooker/stove/range→Cooker, sink, counter, bath/tub→Bath, basin, hearth/fireplace→Hearth, desk, shelf, cabinet, bench; default Table).
- Consumes: Task 1's structure.

- [ ] **Step 1: Failing tests**

```csharp
        [Test]
        public void AuthoredFurnitureLandsAtTheAuthoredTiles()
        {
            var plan = TwoRoomPlan();
            plan.Furniture.Add(new AuthoredFurniture(FurnitureKind.Bed,
                new TileRect(48, 108, 2, 3), "OwnersBed"));
            var world = WorldBuilder.Build(TwoHouses(plan), 1234UL);
            var place = world.AllPlaces.Single(p => p.Name == "authored house");
            var mine = world.AllFurniture.Where(f =>
                world.GetRoom(f.Room).Building.Equals(place.Id)).ToList();

            Assert.That(mine.Count, Is.EqualTo(1),
                "an authored furniture list replaces the generated furnishing entirely");
            Assert.That(mine[0].Kind, Is.EqualTo(FurnitureKind.Bed));
            Assert.That(mine[0].Footprint, Is.EqualTo(new TileRect(48, 108, 2, 3)));
            Assert.That(mine[0].Model, Is.EqualTo("OwnersBed"));
        }

        [Test]
        public void APieceOnADoorwayIsRefusedWithoutWreckingTheRoom()
        {
            var plan = TwoRoomPlan();
            plan.Furniture.Add(new AuthoredFurniture(FurnitureKind.Wardrobe,
                new TileRect(46, 110, 1, 1), ""));            // exactly on the door tile
            var world = WorldBuilder.Build(TwoHouses(plan), 1234UL);
            var place = world.AllPlaces.Single(p => p.Name == "authored house");
            Assert.That(world.AllFurniture.Any(f =>
                world.GetRoom(f.Room).Building.Equals(place.Id)
                && f.Kind == FurnitureKind.Wardrobe), Is.False,
                "nothing may be placed against a door - the placer's own standing rule");
        }

        [Test]
        public void FurnishFalseSkipsGeneratedFurnitureOnly()
        {
            var plan = TwoRoomPlan();
            plan.Furnish = false;                              // an owner-model place
            plan.Furniture.Add(new AuthoredFurniture(FurnitureKind.Bed,
                new TileRect(48, 108, 2, 3), ""));
            var world = WorldBuilder.Build(TwoHouses(plan), 1234UL);
            var place = world.AllPlaces.Single(p => p.Name == "authored house");
            var mine = world.AllFurniture.Where(f =>
                world.GetRoom(f.Room).Building.Equals(place.Id)).ToList();
            Assert.That(mine.Count, Is.EqualTo(1));            // the authored bed, nothing else
        }
```

Plus `FurnitureWordsTests` (a `[TestCase]` table like RoomWords: "Bed"→Bed, "stove"→Cooker,
"Sofa"→Sofa, "couch"→Sofa, "tub"→Bath, "fireplace"→Hearth, "bookshelf"→Shelf,
"nightstand"→Table default).

- [ ] **Step 2: Verify compile failure.**
- [ ] **Step 3: Implement.** In `StampInterior`'s furnishing block (WorldBuilder.cs:237-243):

```csharp
            var authoredPlan = authored ? spec.AuthoredInterior : null;
            bool generatedFurnishing = authoredPlan == null
                || (authoredPlan.Furnish && authoredPlan.Furniture.Count == 0);
            if (generatedFurnishing)
            {
                for (int i = firstRoom; i < rooms.Count; i++)
                    FurniturePlacer.Place(rooms[i], doorKeys, grid.Width, furniture);
            }
            else
            {
                // The placer still runs for RNG neutrality; its output is discarded.
                var discard = new List<Furniture>();
                for (int i = firstRoom; i < rooms.Count; i++)
                    FurniturePlacer.Place(rooms[i], doorKeys, grid.Width, discard);

                foreach (var piece in authoredPlan.Furniture)
                {
                    bool onDoor = false;
                    for (int y = piece.Footprint.Y; y <= piece.Footprint.Bottom && !onDoor; y++)
                    for (int x = piece.Footprint.X; x <= piece.Footprint.Right; x++)
                        if (doorKeys.Contains(y * grid.Width + x)) { onDoor = true; break; }
                    if (onDoor) continue;

                    var room = RoomAt(rooms, firstRoom, piece.Footprint.Centre);
                    if (room == null) continue;               // outside every room: dropped
                    furniture.Add(new Furniture(piece.Kind, piece.Footprint, room.Id, piece.Model));
                }
            }
```

`RoomAt`: linear scan of `rooms[firstRoom..]` for `Bounds.Contains(centre)`. Note the case
split: `Furnish && Furniture.Count == 0` keeps today's behaviour for a walls-only plan on a
generated house; `Furnish == false` with no authored pieces furnishes nothing (the
owner-model rule); any authored pieces replace wholesale. `Furniture` gains
`public readonly string Model;` via a new constructor parameter defaulting `""` —
existing call sites compile unchanged.

- [ ] **Step 4/5: Filtered tests green, then the whole gate** — expect ~626 pass, 0 fail, 8 skipped (count whatever it truly is and remember it for Task 6).
- [ ] **Step 6: Commit**

```bash
git commit -m "Authored furniture: the plan's pieces replace the generated ones, and only there"
```

---

### Task 4: change_gate — a floorplan edit is structural

**Files:**
- Modify: `tools/change_gate.py`

**Interfaces:**
- Consumes: the existing `WATCHED`/`VERIFIED` snapshot mechanism and `unverified()`/`mark_verified()`.
- Produces: floorplan JSON files participate in the diff, always structural.

- [ ] **Step 1: Read the top of `change_gate.py`** for `WATCHED`, `VERIFIED`, `mark_verified` — then extend: floorplans are per-FILE facts, not per-line rulings, so diff them as `filename -> sha1`:

```python
FLOORPLANS = os.path.join(CONTENT, "floorplans")

def _floorplan_hashes(root):
    """filename -> content hash. Interior walls move the walkable grid, so every floorplan
    change is structural; hashing the whole file keeps this honest without teaching the
    gate to parse JSON."""
    out = {}
    try:
        for name in sorted(os.listdir(root)):
            if not name.endswith(".json"):
                continue
            with open(os.path.join(root, name), "rb") as fh:
                out[name] = hashlib.sha1(fh.read()).hexdigest()
    except FileNotFoundError:
        pass
    return out
```

In `unverified()`: compare `_floorplan_hashes(FLOORPLANS)` against
`_floorplan_hashes(os.path.join(VERIFIED, "floorplans"))`, and append
`{"scope": "floor plan", "what": "floorplans/" + name + (" changed"/" added"/" removed")}`
to `structural` for every difference. In `mark_verified()`: copy `Content/floorplans/*.json`
into `VERIFIED/floorplans/` beside the existing file copies (and remove stale copies).
Import `hashlib` at the top.

- [ ] **Step 2: Verify by running**

Run: `python tools/change_gate.py --audit` (must not crash) and a quick inline check:
`python -c "import sys; sys.path.insert(0,'tools'); import change_gate; print(len(change_gate.unverified()['structural']))"` — with 673-0/673-1 present and unverified, expect >= 2.

- [ ] **Step 3: Commit**

```bash
git add tools/change_gate.py
git commit -m "The change gate learns floor plans: interior walls are structural"
```

---

### Task 5: Unity — the plan reaches the PlaceSpec

**Files:**
- Create: `Assets/Noir/Unity/FloorPlans.cs`
- Modify: `Assets/Noir/Unity/SeatOnSurvey.cs` (attach after `s.Place.Outline = s.Outline;` ~line 218)

**Interfaces:**
- Consumes: `ContentLoader.Read(relativePath)` (the `ParcelBuildings` pattern), the seated tuple's `ParcelId`/`Index`/`Now` bounds/`Door`, `Content/models.txt` place names (whatever `CityBuildings` reads them through — reuse that reader; do not re-parse the file), Task 1-3's Core types.
- Produces: `public static class FloorPlans { public static AuthoredInterior For(int parcel, int index, TileRect bounds, Tile door, bool ownerModel); }` returning null when no plan exists.

- [ ] **Step 1: Implement `FloorPlans.cs`.** Key pieces, in full:

**JSON shape** (JsonUtility, `[Serializable]` classes matching the editor's fields):

```csharp
        [Serializable] private class PlanFile {
            public string name; public Shell shell; public List<PlanRoom> rooms;
            public List<PlanOpening> openings; public List<PlanFurniture> furniture; }
        [Serializable] private class Shell { public float w; public float d; }
        [Serializable] private class PlanRoom {
            public string id; public string name; public float x, y, w, h; }
        [Serializable] private class PlanOpening {
            public string id; public string roomId; public string side;
            public float off; public float w; public string kind; }
        [Serializable] private class PlanFurniture {
            public string id; public string name; public string model;
            public float x, y, w, h; public float rot; }
```

**Load**: `ContentLoader.Read("floorplans/" + parcel + "-" + index + ".json")` inside a
try; any parse/read failure logs one warning
(`[floorplans] {parcel}-{index}: unreadable, generated interior kept: {e.Message}`) and
returns null. If `ContentLoader` cannot take a subdirectory, fall back to
`File.ReadAllText` rooted the same way `ContentLoader` roots itself — read its source
first.

**Feet → oriented tiles.** One tile = one metre. Plan coordinates: x from the west face,
y from the NORTH face, street at the bottom (south). Orientation: the plan's south edge
maps onto the side of `bounds` that carries `door`; rotation in 90° steps:

```csharp
        // rot 0: door on the south edge (door.Y == bounds.Bottom) - plan and world agree.
        // rot 2: door north - flip both axes. rot 1/3: door east/west - swap axes.
        private static TileRect Orient(float fx, float fy, float fw, float fh,
                                       Shell shell, TileRect b, int rot)
        {
            int  x = Mathf.RoundToInt(fx * 0.3048f), y = Mathf.RoundToInt(fy * 0.3048f);
            int  w = Mathf.Max(1, Mathf.RoundToInt(fw * 0.3048f));
            int  h = Mathf.Max(1, Mathf.RoundToInt(fh * 0.3048f));
            int sw = Mathf.Max(1, Mathf.RoundToInt(shell.w * 0.3048f));
            int sd = Mathf.Max(1, Mathf.RoundToInt(shell.d * 0.3048f));
            switch (rot)
            {
                case 0: return new TileRect(b.X + x,               b.Y + y,               w, h);
                case 2: return new TileRect(b.X + (sw - x - w),    b.Y + (sd - y - h),    w, h);
                case 1: return new TileRect(b.X + (sd - y - h),    b.Y + x,               h, w);
                default:return new TileRect(b.X + y,               b.Y + (sw - x - w),    h, w);
            }
        }
```

`rot` from the door: `door.Y == bounds.Bottom ? 0 : door.Y == bounds.Y ? 2 :
door.X == bounds.Right ? 1 : 3`.

**Walls between rooms**: after converting all rooms, enforce a one-tile wall where two
room rects touch or overlap: for each ordered pair (i earlier, j later), if their tile
rects intersect or share an edge, shrink j on the side facing i by the overlap + 1. (The
editor's rooms are interior rects separated by a real wall thickness that rounds to zero
tiles; without this, adjacent rooms would merge into one open space.) Rooms shrunk below
1x1 are dropped with a warning.

**Plan doors → door tiles**: for each opening with `kind == "door"` on an interior wall,
compute the opening's centre in plan feet (room edge + off + w/2 along the wall), convert
and orient that POINT, then take the wall tile between the two adjacent room rects nearest
it (scan the 1-tile gap line). Exterior-wall plan doors (no second room across) are
skipped — `PlaceSpec.Door` stays the door.

**Furniture**: each entry → `AuthoredFurniture(FurnitureWords.KindFor(name),
Orient(x, y, rot%180==0 ? w : h, ...), model)` — note a piece's own `rot` swaps its w/h
before orienting; 90° model yaw is the Unity furnisher's business (`Model` + footprint
orientation already express it).

**Rooms**: `new AuthoredRoom(orientedRect, RoomWords.KindFor(name), name)`.
**Furnish**: `!ownerModel`.

- [ ] **Step 2: Attach in `SeatOnSurvey`** after `s.Place.Outline = s.Outline;`:

```csharp
                // The owner's floor plan, if he drew one for this building - converted to
                // tiles here, where the parcel, the seated bounds and the door are all in
                // hand, and handed to Core the same way the measured outline is.
                bool ownerModel = OwnerModels.Covers(s.Place);   // however CityBuildings tests a models.txt row; reuse, don't re-parse
                s.Place.AuthoredInterior = FloorPlans.For(entryParcelId, entryIndex,
                                                          s.Now, s.Door, ownerModel);
```

Read `SeatOnSurvey` around the seated-tuple construction to thread `ParcelId`/`Index`
into the tuple if they are not already there (they come from `ParcelBuildings.Entry`).
Find how `CityBuildings` recognises a models.txt place (grep `models.txt` /
`Landmark`) and call that; if it is not reusable as a predicate, extract one there —
do not parse `models.txt` a second time.

Count and log once per build: `[floorplans] N consumed, M refused` (Debug.Log, matching
the survey passes' bracket style).

- [ ] **Step 3: Compile all three**

Run: `dotnet build Noir.Unity.csproj -c Debug && dotnet build Noir.Editor.csproj -c Debug && dotnet build Noir.PlayTests.csproj -c Debug`
Expected: three greens. (If the editor is open, these builds are still safe — they compile outside Unity.)

- [ ] **Step 4: Commit**

```bash
git add Assets/Noir/Unity/FloorPlans.cs Assets/Noir/Unity/SeatOnSurvey.cs
git commit -m "The survey hands the owner's floor plan to Core, oriented to the door"
```

---

### Task 6: The PlayMode gate sees 408's plan, and the baselines move

**Files:**
- Modify: `Assets/Noir/PlayTests/TownGeometryPlayTests.cs` (one new test, near the owner-door gates)
- Modify: `CLAUDE.md` (Core baseline 596 → the Task 3 count; PlayMode expected 36 → 37)
- Modify: `tools/nightly-gate.ps1` (`$coreBaselinePass`, `$playmodeBaselinePass`)

**Interfaces:**
- Consumes: the built town's `host.World` (`AllPlaces`/`AllRooms`), `Content/floorplans/673-0.json` on disk.

- [ ] **Step 1: Write the gate test.** Unlike the owner-door gates this needs NO
`Assert.Ignore` in the plan town — the plan applies to the GENERATED 408 there too:

```csharp
        [Test]
        public void TheOwnersFloorPlanIsTheHousesRealRooms()
        {
            var world = /* however sibling tests reach the built WorldModel */;
            var place = world.AllPlaces.FirstOrDefault(p => p.Name == "408 Holmes Street");
            if (place == null) Assert.Ignore("no 408 Holmes Street in this town");

            var rooms = world.AllRooms.Where(r => r.Building.Equals(place.Id)).ToList();
            // 673-0.json authors 11 rooms; conversion may drop slivers, so the gate is
            // a floor, not an equality - and the named rooms must be the authored ones.
            Assert.That(rooms.Count, Is.GreaterThanOrEqualTo(9),
                "408's rooms are not the authored plan's");
            Assert.That(rooms.Count(r => r.Kind == RoomKind.Bedroom), Is.GreaterThanOrEqualTo(3));
            Assert.That(rooms.Any(r => r.Kind == RoomKind.Kitchen));
        }
```

Copy the world-access idiom from the nearest sibling test in the file rather than
inventing one.

- [ ] **Step 2: Build the test assembly** (`dotnet build Noir.PlayTests.csproj -c Debug`) — the cheapest four seconds in this project.

- [ ] **Step 3: Run the gates.** Editor closed (check for `Unity.exe`; if the owner left
it open, STOP and say so — the editor wins):

```
dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj
Unity.exe -batchmode -projectPath C:\SerialKillerGame -runTests -testPlatform PlayMode ^
  -assemblyNames Noir.PlayTests -testCategory "!Diagnostic" -testResults <xml> -logFile <log>
```

Expected: Core at the Task 3 count, 0 fail; PlayMode 37 pass (36 + this), 3 skipped.
If the editor is open, run Core only and leave PlayMode to tonight's nightly — note it
in the commit message.

- [ ] **Step 4: Move the baselines.** CLAUDE.md's Core baseline paragraph gains the new
count with this feature named; the PlayMode expected-standing number moves 36 → 37;
`tools/nightly-gate.ps1` `$coreBaselinePass` and `$playmodeBaselinePass` move in the SAME
commit (the script's own comment demands it).

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/PlayTests/TownGeometryPlayTests.cs CLAUDE.md tools/nightly-gate.ps1
git commit -m "408's rooms are the owner's plan, and the gates say so"
git push
```

---

## Self-review notes

- Spec coverage: generate-then-replace (T1, T3), Units>1 (T1), name→kind (T2),
  furniture + Furnish + door-block (T3), change gate (T4), conversion + orientation +
  wall re-insertion + owner-model detection (T5), PlayMode + survey log line (T5/T6). The
  spec's "survey report gains one line per plan" is delivered as the `[floorplans]` census
  log; wiring per-lot detail into game-verdict.json belongs to the house-inspector plan.
- Exact struct/method names for `TileRect.Intersect`, `Interior` internals, the world
  accessor in PlayTests, and `ContentLoader`'s subdirectory behaviour are flagged in-task
  as read-before-write: the implementer verifies against the file rather than trusting
  this plan's guess. Everything else is verbatim from the sources read on 2026-08-19.
