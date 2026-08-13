# Shaped-Place Perimeter Walls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Draw a shaped building's exterior wall along its own `Place.Outline` polygon instead of
approximating it one tile at a time, so an angled row like 112 S Chicago gets a straight wall
instead of a staircase, and two neighbouring shaped places no longer leave a gap where their
independent tile approximations disagree.

**Architecture:** `VillageMesh.BuildWalls` currently finds axis-aligned runs of `Terrain.Wall`
tiles and boxes each one — correct for a plain rectangular house, but a staircase for any building
whose real `Outline` is not axis-aligned, because `WorldBuilder.MaskToOutline` (Core) can only
carve that outline onto the tile grid one tile at a time. This plan adds a second path,
`DrawShapedPerimeters`, that reads a shaped place's `Outline` corners directly (already Core data,
already float-precision-rounded once per corner rather than once per tile) and draws each edge as
its own true-angle slab, inset toward the building's interior by `Materials3D.WallDepthFor`. The
existing tile-run walker is told to skip a shaped place's exterior tiles (leaving its own interior
partitions alone), so the two paths never draw the same wall twice. Nothing in Core changes —
`Place.Outline` already carries everything this needs — so this is entirely
`Assets/Noir/Unity/VillageMesh.cs`, verified by `dotnet build` and a look at the built town rather
than `dotnet test`, matching how `DowntownFromSanborn.Apply` and `BusinessFromRulings.Apply` were
verified in `docs/superpowers/plans/2026-08-12-terrace-business-units.md`.

**Tech Stack:** C# / .NET 9 (Unity assemblies compile via `dotnet build Noir.Unity.csproj`), Unity
6000.3.20f1.

**Spec:** No separate spec document — the requirement came directly from the owner in
conversation, prompted by seeing 112 S Chicago's front wall live in Play mode: "draw both from the
actual outline polygon" (walls and, in a later plan, the downtown sidewalks — see Global
Constraints).

## Global Constraints

- This plan touches only `Assets/Noir/Unity/VillageMesh.cs`. No Core file changes, so the Core
  baseline (501 pass, 0 fail, measured 2026-08-12 19:47 per `CLAUDE.md`) is not expected to move.
  Confirm it hasn't with `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj`
  before Task 4's commit if in doubt — it should read exactly the same.
- **Scope is walls only.** The sidewalk half of "draw both from the actual outline polygon" is a
  separate subsystem — `CityStreets.cs` paves downtown sidewalks from bought prefab tiles keyed to
  grid cells near a building, not from procedural mesh, and replacing that is a different shape of
  change (see `CityStreets.cs:1307-1316`, the owner's 2026-08-10 ruling on the same staircase
  symptom in that system). It gets its own plan once this one is confirmed working and looked at.
- **Precondition for any `Unity.exe -batchmode` command in Task 4:** the editor must be closed.
  Check for `Unity.exe` first — if the owner is using it, verify live via `Camera.main` capture
  (already proven working this session) instead of asking him to close it.
- No `UnityEngine` reference is available to anything under `Assets/Noir/Core` — not touched by
  this plan, but do not introduce one while editing `VillageMesh.cs`'s Unity-layer code near it.
- **Known, accepted residual limitation** (do not try to fix it in this plan — see Task 3's note):
  where a shaped place's excluded exterior tile is adjacent to a tile-based interior partition
  (rare — only happens inside a shaped multi-room building), the partition's end may sit a
  sub-centimetre off the new polygon wall's inner face rather than perfectly flush, because the
  flush-fitting band data still comes from the old tile approximation. This was already an
  approximation before this plan; it does not get worse, and fixing it precisely needs the same
  polygon-edge treatment extended to partitions, which is out of scope here.

---

### Task 1: Generalize `AddWall` to take four explicit corners

**Files:**
- Modify: `Assets/Noir/Unity/VillageMesh.cs:1590-1594` (the two call sites) and
  `Assets/Noir/Unity/VillageMesh.cs:1647-1716` (`AddWall` itself)

**Interfaces:**
- Produces: `AddWall(MeshChunk into, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float bottom,
  float top, int submesh)` — four grid-space (X,Y) corners walked in order around the slab's
  footprint, replacing the old `(float wx0, float wx1, float wy0, float wy1, ...)` signature.
  Consumed by Task 2's `DrawShapedPerimeters` and by the existing axis-aligned call sites in
  `BuildWalls`, updated in this task to pass four corners instead of four extents.

This is a pure refactor — behaviour for the existing axis-aligned callers is unchanged. No new
geometry appears until Task 2. Verified by `dotnet build`, because `Assets/Noir/Unity` has no
`dotnet test` coverage (`UnityEngine.Vector2`/`Vector3` do not compile there) — the same reasoning
`docs/superpowers/plans/2026-08-12-terrace-business-units.md` used for
`DowntownFromSanborn.Apply`.

- [ ] **Step 1: Rewrite `AddWall`'s signature and corner setup**

In `Assets/Noir/Unity/VillageMesh.cs`, find:

```csharp
        private static void AddWall(MeshChunk into, float wx0, float wx1, float wy0, float wy1,
                                    float bottom, float top, int submesh)
        {
            var verts = into.Verts;
            var uvs = into.Uvs;
            var tris = into.Tris[submesh];

            // Float extents in tile space, because a wall is a slab seated within its tiles now
            // rather than a box filling them - the caller has already decided where its faces
            // sit and how far its ends reach.
            float x0 = wx0, x1 = wx1;
            float z0 = -wy0, z1 = -wy1;

            // (corner a, corner b) walked so that a->b->up is wound outward.
            // Sunk half a metre below the ground it stands on, so a wall meets a contour that
            // dips slightly across the footprint without daylight under its foot.
            float y0 = bottom - 0.5f;

            Face(new Vector3(x1, y0, z0), new Vector3(x0, y0, z0));   // north
            Face(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1));   // south
            Face(new Vector3(x1, y0, z1), new Vector3(x1, y0, z0));   // east
            Face(new Vector3(x0, y0, z0), new Vector3(x0, y0, z1));   // west

            Cap();

            void Cap()
            {
                int i = verts.Count;

                verts.Add(new Vector3(x0, top, z0));
                verts.Add(new Vector3(x1, top, z0));
                verts.Add(new Vector3(x1, top, z1));
                verts.Add(new Vector3(x0, top, z1));

                for (int v = i; v < verts.Count; v++)
                    uvs.Add(new Vector2(verts[v].x, -verts[v].z));

                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }
```

Replace the signature and the block up to (not including) the inner `Face` method with:

```csharp
        /// <summary>
        /// One slab, given as four grid-space (X,Y) corners walked in order around its
        /// footprint - <c>a</c>&#8594;<c>b</c>&#8594;<c>c</c>&#8594;<c>d</c>&#8594;back to
        /// <c>a</c>. An axis-aligned caller passes the same four corners a rectangle always
        /// had; <see cref="DrawShapedPerimeters"/> passes a true outline edge and its
        /// depth-inset partner, which need not be axis-aligned at all - nothing below this
        /// line ever assumed they were.
        /// </summary>
        private static void AddWall(MeshChunk into, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
                                    float bottom, float top, int submesh)
        {
            var verts = into.Verts;
            var uvs = into.Uvs;
            var tris = into.Tris[submesh];

            // Sunk half a metre below the ground it stands on, so a wall meets a contour that
            // dips slightly across the footprint without daylight under its foot.
            float y0 = bottom - 0.5f;

            Vector3 A = new Vector3(a.x, y0, -a.y);
            Vector3 B = new Vector3(b.x, y0, -b.y);
            Vector3 C = new Vector3(c.x, y0, -c.y);
            Vector3 D = new Vector3(d.x, y0, -d.y);

            // (corner p, corner q) walked so that p->q->up is wound outward. Each face is the
            // REVERSE of one edge of the a-b-c-d loop, which is what a rectangle's four old
            // north/south/east/west faces always were - see Task 1's plan notes for the
            // corner-by-corner check against the box this replaces.
            Face(B, A);
            Face(D, C);
            Face(C, B);
            Face(A, D);

            Cap();

            void Cap()
            {
                int i = verts.Count;

                verts.Add(new Vector3(A.x, top, A.z));
                verts.Add(new Vector3(B.x, top, B.z));
                verts.Add(new Vector3(C.x, top, C.z));
                verts.Add(new Vector3(D.x, top, D.z));

                for (int v = i; v < verts.Count; v++)
                    uvs.Add(new Vector2(verts[v].x, -verts[v].z));

                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }
```

Leave the `Face(Vector3 p, Vector3 q)` local method and its closing braces exactly as they are —
it already takes two `Vector3` corners and was never axis-aligned itself, so nothing inside it
changes.

- [ ] **Step 2: Update the two call sites**

Still in `Assets/Noir/Unity/VillageMesh.cs`, find:

```csharp
                if (run.Horizontal)
                    AddWall(chunks.At(run.X, run.Y), aLo, aHi, run.Lo, run.Hi,
                            BaseAt(run.X, run.Y), HeightAt(run.X, run.Y), WallingAt(run.X, run.Y));
                else
                    AddWall(chunks.At(run.X, run.Y), run.Lo, run.Hi, aLo, aHi,
                            BaseAt(run.X, run.Y), HeightAt(run.X, run.Y), WallingAt(run.X, run.Y));
                count++;
```

Replace with:

```csharp
                if (run.Horizontal)
                    AddWall(chunks.At(run.X, run.Y),
                            new Vector2(aLo, run.Lo), new Vector2(aHi, run.Lo),
                            new Vector2(aHi, run.Hi), new Vector2(aLo, run.Hi),
                            BaseAt(run.X, run.Y), HeightAt(run.X, run.Y), WallingAt(run.X, run.Y));
                else
                    AddWall(chunks.At(run.X, run.Y),
                            new Vector2(run.Lo, aLo), new Vector2(run.Hi, aLo),
                            new Vector2(run.Hi, aHi), new Vector2(run.Lo, aHi),
                            BaseAt(run.X, run.Y), HeightAt(run.X, run.Y), WallingAt(run.X, run.Y));
                count++;
```

(This reproduces the old `x0=wx0, x1=wx1, z0=-wy0, z1=-wy1` corners exactly: horizontal passed
`(wx0,wx1,wy0,wy1)=(aLo,aHi,run.Lo,run.Hi)`, so its four corners in the old A,B,C,D order were
`(aLo,run.Lo), (aHi,run.Lo), (aHi,run.Hi), (aLo,run.Hi)` — precisely what is written above.)

- [ ] **Step 3: Verify it compiles**

Close the Unity editor first if it is open. If it is open and in use, verify with
`EditorApplication.isCompiling`/`Unity_GetConsoleLogs` via the live MCP tools instead of closing
it out from under whoever is using it.

Run: `dotnet build Noir.Unity.csproj -c Debug`

Expected: build succeeds, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Noir/Unity/VillageMesh.cs
git commit -m "AddWall takes four corners now, not four axis-aligned extents"
```

---

### Task 2: `DrawShapedPerimeters` — the real fix

**Files:**
- Modify: `Assets/Noir/Unity/VillageMesh.cs` (add a new private static method, near `BuildWalls`)

**Interfaces:**
- Consumes: `AddWall(MeshChunk, Vector2, Vector2, Vector2, Vector2, float, float, int)` (Task 1),
  `Place.Outline` (existing, `Tile[]`, `Assets/Noir/Core/World/Place.cs:108`),
  `Materials3D.WallDepthFor(Place)` (existing), `Materials3D.WallingFor(Place)` (existing),
  `Space3D.GroundUnder(TileRect)` (existing), `MassingGrammars.Of(Place).Eaves` (existing),
  `PlaceKindTable.Current.Row(place.Kind).IsBuilding` (existing), `CityBuildings.Handles(Place)`
  (existing — true for a bought model, which brings its own walls).
- Produces: `DrawShapedPerimeters(WorldModel world, MeshChunks chunks, ref int count)` — walks
  every shaped, non-bought building place's `Outline` and emits one slab per edge (split around
  the door if the edge carries one). Consumed by Task 3's wiring into `BuildWalls`.

- [ ] **Step 1: Add the method**

In `Assets/Noir/Unity/VillageMesh.cs`, add this new method directly after `BuildWalls` closes (its
closing brace is followed by the `WallRun` class — insert between them):

```csharp
        /// <summary>
        /// A shaped place's real wall, drawn along its own <see cref="Place.Outline"/> instead
        /// of approximated one tile at a time. <see cref="BuildWalls"/> excludes these places'
        /// exterior tiles from the run-walker (see the `shapedExterior` branch there) so this is
        /// the only thing that draws their skin.
        ///
        /// EACH EDGE BECOMES ONE SLAB, inset toward the inside of the polygon by
        /// <see cref="Materials3D.WallDepthFor"/> - the true outdoor face stays exactly on the
        /// surveyed line, which the old tile classification could only approximate one metre at
        /// a time. Two neighbouring shaped places (adjoining storefronts in a terrace) each draw
        /// their own skin hugging the SAME shared corner-to-corner line, because both read it
        /// from the same underlying corners rather than from two independent tile roundings -
        /// which is what left an actual gap at 112 S Chicago's party walls before this.
        ///
        /// THE DOOR IS FOUND IN CONTINUOUS SPACE, not by testing a tile. The one-tile gap
        /// <c>WorldBuilder.MaskToOutline</c> (Core) carves into the terrain grid for the door has
        /// no tile for a polygon edge to test any more, so this projects the door position onto
        /// whichever edge it sits nearest and leaves a one-metre gap centred on that projection.
        /// </summary>
        private static void DrawShapedPerimeters(WorldModel world, MeshChunks chunks, ref int count)
        {
            foreach (var place in world.AllPlaces)
            {
                if (place == null || place.Outline == null || place.Outline.Length < 3) continue;
                if (!PlaceKindTable.Current.Row(place.Kind).IsBuilding) continue;
                if (CityBuildings.Handles(place)) continue;   // a bought model brings its own walls

                float depth = Materials3D.WallDepthFor(place);
                int submesh = Materials3D.WallingFor(place);
                float bottom = Space3D.GroundUnder(place.Bounds);
                float top = bottom + MassingGrammars.Of(place).Eaves;

                var ring = place.Outline;
                int n = ring.Length;

                // The ring's own centre, so each edge can tell which of its two perpendicular
                // directions points inward without caring which way the ring winds.
                Vector2 centre = Vector2.zero;
                for (int i = 0; i < n; i++) centre += new Vector2(ring[i].X, ring[i].Y);
                centre /= n;

                Vector2? door = place.Door.IsValid
                    ? new Vector2(place.Door.X + 0.5f, place.Door.Y + 0.5f)
                    : (Vector2?)null;

                for (int i = 0; i < n; i++)
                {
                    var p0 = new Vector2(ring[i].X, ring[i].Y);
                    var p1 = new Vector2(ring[(i + 1) % n].X, ring[(i + 1) % n].Y);
                    var edge = p1 - p0;
                    float len = edge.magnitude;
                    if (len < 0.01f) continue;
                    var dir = edge / len;

                    var normal = new Vector2(-dir.y, dir.x);
                    if (Vector2.Dot(normal, centre - p0) < 0f) normal = -normal;   // point inward

                    float doorLo = -1f, doorHi = -1f;
                    if (door.HasValue)
                    {
                        float t = Vector2.Dot(door.Value - p0, dir);
                        float perp = Mathf.Abs(Vector2.Dot(door.Value - p0, normal));
                        if (perp < 0.75f && t > -0.5f && t < len + 0.5f)
                        {
                            doorLo = Mathf.Max(0f, t - 0.5f);
                            doorHi = Mathf.Min(len, t + 0.5f);
                        }
                    }

                    if (doorLo >= 0f && doorHi > doorLo)
                    {
                        if (doorLo > 0.05f) Slab(p0, p0 + dir * doorLo);
                        if (len - doorHi > 0.05f) Slab(p0 + dir * doorHi, p1);
                    }
                    else
                    {
                        Slab(p0, p1);
                    }

                    void Slab(Vector2 s0, Vector2 s1)
                    {
                        var mid = (s0 + s1) * 0.5f;
                        AddWall(chunks.At(mid.x, mid.y),
                                s0, s1, s1 + normal * depth, s0 + normal * depth,
                                bottom, top, submesh);
                        count++;
                    }
                }
            }
        }
```

- [ ] **Step 2: Verify it compiles**

Close the Unity editor first if it is open (or verify via the live MCP tools as in Task 1 Step 3
if someone is using it).

Run: `dotnet build Noir.Unity.csproj -c Debug`

Expected: build succeeds, 0 errors. (Nothing calls `DrawShapedPerimeters` yet, so this only proves
it compiles — Task 3 wires it in and is where new geometry first appears.)

- [ ] **Step 3: Commit**

```bash
git add Assets/Noir/Unity/VillageMesh.cs
git commit -m "DrawShapedPerimeters draws a shaped place's wall from its own outline"
```

---

### Task 3: Wire it into `BuildWalls` and exclude shaped exteriors from the tile walker

**Files:**
- Modify: `Assets/Noir/Unity/VillageMesh.cs:1502-1604` (`BuildWalls`'s classification and emit
  loops, and its summary log line)

**Interfaces:**
- Consumes: `DrawShapedPerimeters` (Task 2).
- Produces: `BuildWalls`'s behaviour changes — a shaped place's exterior tiles are no longer
  boxed by the tile-run walker; `DrawShapedPerimeters` draws them instead. Interior partitions
  (floor on both flanks, same or different owner) are untouched. The summary log line gains a
  count of shaped perimeter edges drawn and tile runs left to them.

- [ ] **Step 1: Add a `Skip` field to `WallRun`**

Find:

```csharp
        private sealed class WallRun
        {
            public readonly int X, Y, Len;
            public readonly bool Horizontal;
            public readonly int Owner;
            public float Lo, Hi;

            public WallRun(int x, int y, int len, bool horizontal, int owner)
            {
                X = x; Y = y; Len = len; Horizontal = horizontal; Owner = owner;
            }
        }
```

Replace with:

```csharp
        private sealed class WallRun
        {
            public readonly int X, Y, Len;
            public readonly bool Horizontal;
            public readonly int Owner;
            public float Lo, Hi;

            /// <summary>True for a shaped place's exterior run - DrawShapedPerimeters draws its
            /// real wall from the place's own Outline instead, so this run still fills the
            /// flush-fit band data below but is never boxed itself.</summary>
            public bool Skip;

            public WallRun(int x, int y, int len, bool horizontal, int owner)
            {
                X = x; Y = y; Len = len; Horizontal = horizontal; Owner = owner;
            }
        }
```

- [ ] **Step 2: Exclude a shaped place's exterior from the classification**

Find:

```csharp
                float centre = (run.Horizontal ? run.Y : run.X) + 0.5f;
                var place = run.Owner < 0 ? null : world.GetPlace(new PlaceId(run.Owner));

                if (run.Owner >= 0 && (sideA > 0) != (sideB > 0))
                {
                    skins++;
                    float depth = Materials3D.WallDepthFor(place);
                    if (sideA > 0) { run.Lo = centre + 0.5f - depth; run.Hi = centre + 0.5f; }
                    else { run.Lo = centre - 0.5f; run.Hi = centre - 0.5f + depth; }
                }
                else if (run.Owner >= 0 && sideA > 0 && sideB > 0)
                {
                    partitions++;
                    run.Lo = centre - Partition * 0.5f;
                    run.Hi = centre + Partition * 0.5f;
                }
                else
                {
                    boundaries++;
                    float half = Materials3D.WallDepthFor(place) * 0.5f;
                    run.Lo = centre - half;
                    run.Hi = centre + half;
                }
            }
```

Replace with:

```csharp
                float centre = (run.Horizontal ? run.Y : run.X) + 0.5f;
                var place = run.Owner < 0 ? null : world.GetPlace(new PlaceId(run.Owner));
                bool isPartition = run.Owner >= 0 && sideA > 0 && sideB > 0;

                if (place != null && place.Outline != null && !isPartition)
                {
                    // This tile is on a SHAPED place's exterior - DrawShapedPerimeters draws
                    // its real skin from the place's own Outline instead. Lo/Hi are still
                    // computed the old way so a neighbouring TILE-BASED wall (an interior
                    // partition, say) still has something sane to flush against at this
                    // boundary below - only the render is skipped here, not the geometry
                    // other runs may reach for.
                    run.Skip = true;
                    shapedSkipped++;
                    float depth = Materials3D.WallDepthFor(place);
                    if (sideA > 0) { run.Lo = centre + 0.5f - depth; run.Hi = centre + 0.5f; }
                    else { run.Lo = centre - 0.5f; run.Hi = centre - 0.5f + depth; }
                }
                else if (run.Owner >= 0 && (sideA > 0) != (sideB > 0))
                {
                    skins++;
                    float depth = Materials3D.WallDepthFor(place);
                    if (sideA > 0) { run.Lo = centre + 0.5f - depth; run.Hi = centre + 0.5f; }
                    else { run.Lo = centre - 0.5f; run.Hi = centre - 0.5f + depth; }
                }
                else if (isPartition)
                {
                    partitions++;
                    run.Lo = centre - Partition * 0.5f;
                    run.Hi = centre + Partition * 0.5f;
                }
                else
                {
                    boundaries++;
                    float half = Materials3D.WallDepthFor(place) * 0.5f;
                    run.Lo = centre - half;
                    run.Hi = centre + half;
                }
            }
```

Just above this block, find `int skins = 0, partitions = 0, boundaries = 0;` and replace with:

```csharp
            int skins = 0, partitions = 0, boundaries = 0, shapedSkipped = 0;
```

- [ ] **Step 3: Skip skipped runs in the emit loop, and call `DrawShapedPerimeters`**

Find:

```csharp
            foreach (var run in runs)
            {
                float aLo = run.Horizontal ? run.X : run.Y;
                float aHi = aLo + run.Len;
```

Replace with:

```csharp
            foreach (var run in runs)
            {
                if (run.Skip) continue;   // DrawShapedPerimeters draws this one instead

                float aLo = run.Horizontal ? run.X : run.Y;
                float aHi = aLo + run.Len;
```

Then find (the end of that same loop, right after it closes and before the `chunks.Emit` call):

```csharp
                count++;
            }

            var renderers = chunks.Emit(walls.transform, "Walls", Materials3D.Walls,
                                        ShadowCastingMode.On, true);

            Debug.Log($"Walls: {count} runs at their real thickness - {skins} building skins, "
                    + $"{partitions} partitions, {boundaries} freestanding - "
                    + $"{chunks.VertexCount:N0} vertices, {renderers.Count} chunk meshes.");
        }
```

Replace with:

```csharp
                count++;
            }

            int shapedBefore = count;
            DrawShapedPerimeters(world, chunks, ref count);
            int shapedEdges = count - shapedBefore;

            var renderers = chunks.Emit(walls.transform, "Walls", Materials3D.Walls,
                                        ShadowCastingMode.On, true);

            Debug.Log($"Walls: {count} slabs at their real thickness - {skins} building skins, "
                    + $"{partitions} partitions, {boundaries} freestanding, {shapedEdges} shaped "
                    + $"perimeter edge(s) drawn from their own outline ({shapedSkipped} tile "
                    + $"run(s) left to them) - {chunks.VertexCount:N0} vertices, "
                    + $"{renderers.Count} chunk meshes.");
        }
```

- [ ] **Step 4: Verify it compiles**

Close the Unity editor first if it is open (or verify live via the MCP tools if someone is using
it).

Run: `dotnet build Noir.Unity.csproj -c Debug`

Expected: build succeeds, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Noir/Unity/VillageMesh.cs
git commit -m "Shaped places draw their wall from the outline; the tile walker leaves them alone"
```

---

### Task 4: Verification — build the town and look at 112 S Chicago

No new code — this confirms Tasks 1-3 actually fixed what the owner pointed at, and catches the
one thing that cannot be verified by reading the diff: face winding. `AddWall`'s four corners for
an axis-aligned run are proven identical to the old ones by direct substitution (Task 1's own
step notes work through it), but `DrawShapedPerimeters`'s corners for a genuinely rotated edge are
new geometry with no prior example to match against — if the inward-normal sign came out backwards
for every edge of a place (a single, consistent, plan-wide mistake rather than a per-edge one — see
the note in Step 2 below), the wall would render as invisible or as visibly backfaced from the
street, and that is only checkable by looking.

- [ ] **Step 1: Full Unity build**

Close the Unity editor first if it is open.

Run, in order:

```
dotnet build Noir.Unity.csproj -c Debug
dotnet build Noir.Editor.csproj -c Debug
dotnet build Noir.PlayTests.csproj -c Debug
```

Expected: all three succeed, 0 errors.

- [ ] **Step 2: Look at 112 S Chicago**

If the editor is closed, run the PlayMode gate and read its log for the new `Walls:` summary line:

```
Unity.exe -batchmode -projectPath C:\SerialKillerGame -runTests -testPlatform PlayMode -assemblyNames Noir.PlayTests -testCategory "!Diagnostic" -testResults <xml> -logFile <log>
```

Expected: 20 of 20 pass, 0 fail, 1 skipped (unchanged from the 2026-08-12 baseline in
`CLAUDE.md`), and the log's `Walls:` line reports a nonzero `shaped perimeter edge(s)` count.

If the editor is open and in use instead, drive it live the way this session already proved works:
enter Play, find `Camera.main`'s `GameObject.GetInstanceID()` via `Unity_RunCommand`, and call
`Unity_Camera_Capture` with that `cameraInstanceID` while aimed at 112 S Chicago (roughly world
`(745, y, -1380)`, per the place data pulled earlier in this session). Compare against what the
owner was looking at when he first pointed this out:

- **The staircase should be gone.** The row's front should read as one straight wall following
  Chicago Street's real angle, not a jagged stepped edge.
- **The gaps should be gone.** No daylight visible through the wall at any party-wall seam.
- **If the wall has disappeared or reads as visibly backfaced** (dark, or the wrong faces culled),
  the inward-normal sign in `DrawShapedPerimeters` is backwards for this engine's winding
  convention. Fix by swapping the candidate perpendicular in Task 2's code from
  `new Vector2(-dir.y, dir.x)` to `new Vector2(dir.y, -dir.x)`, rebuild, and look again.

- [ ] **Step 3: Confirm the Core baseline did not move**

Run: `dotnet test -c Release tools/Noir.Core.Tests/Noir.Core.Tests.csproj`

Expected: 501 pass, 0 fail (unchanged — this plan touches no Core file).

- [ ] **Step 4: Report to the owner**

Tell him it is ready to look at, with the `Walls:` log line's shaped-edge count and, if he wants
the sidewalks fixed the same way, that it is next as its own plan per the Global Constraints note
above.
