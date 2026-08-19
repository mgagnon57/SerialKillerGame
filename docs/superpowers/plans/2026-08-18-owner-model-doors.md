# Owner-Model Doors and Interiors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The owner's hand-made houses open their doors and let the player walk the modeled
rooms — 408 Holmes Street first — and P spawns him at that front door.

**Architecture:** `glb-to-obj.py` stops flattening the GLB node tree, so Unity imports owner
models as named children. `CityBuildings` hinges every `door_*` family through the existing
`CityDoors` swing (plus a new tilt-up kind for `garage_door_panel`), which already survives
the bake via the `hinge`-parent exemption. `CityCollision` gives owner models per-piece mesh
colliders on collider-only copies (the bake destroys originals) instead of one solid box.
`Player.Standing` prefers 408's front walk.

**Tech Stack:** Python 3 (converter), C# Unity layer only (no Core change), NUnit PlayMode.

**Spec:** `docs/superpowers/specs/2026-08-18-owner-model-doors-design.md`

## Global Constraints

- The year is 1991. Editor closed for any `Unity.exe` batch run; check `Unity.exe` first and
  NEVER kill the owner's editor.
- Do not edit any `.cs` while a batch run is going.
- Core suite untouched: this plan compiles nothing into `Noir.Core`. Core baseline stays
  **596 pass, 0 fail, 8 skipped**.
- PlayMode gate baseline going in: **35 of 35, 0 fail, 1 skipped**. This plan adds 3 tests →
  **38**; `tools/nightly-gate.ps1`'s `$playmodeBaselinePass` and CLAUDE.md move to 38 in the
  SAME commit (Task 7).
- The generated csprojs are gitignored and stale for NEW files — none are created here
  except tests inside existing assemblies, so `dotnet build` needs no csproj patching.
- Build verification after every Unity-layer task:
  `dotnet build Noir.Unity.csproj -c Debug` (and `Noir.PlayTests.csproj` when tests change),
  exit 0.
- The bake spares only renderers whose DIRECT parent Transform is named exactly `hinge`
  (`CityChunker.cs:279`). Every swinging piece must be a direct child of its hinge pivot.
- Hinging (CityBuildings) and collision (CityCollision) both run BEFORE `CityChunker.Bake`
  in `VillageHost.Build` — Task 3's executor verifies that order by reading
  `VillageHost.Build` before wiring, and stops if it does not hold.

## File Structure

- Modify: `tools/glb-to-obj.py` — per-node OBJ groups
- Modify: `Assets/Noir/Models/Residence408.obj` — regenerated (yaw baked at conversion is
  NOT used; the 180 stays in models.txt)
- Modify: `Assets/Noir/Unity/CityDoors.cs` — lift kind
- Modify: `Assets/Noir/Unity/CityBuildings.cs` — `OwnerModel` marker + `HingeOwnerDoors`
- Modify: `Assets/Noir/Unity/CityCollision.cs` — owner-model mesh collision branch
- Modify: `Assets/Noir/Unity/Player.cs` — spawn preference
- Modify: `Assets/Noir/PlayTests/TownGeometryPlayTests.cs` — three gates

---

### Task 1: The converter keeps the owner's names

**Files:**
- Modify: `tools/glb-to-obj.py`
- Regenerate: `Assets/Noir/Models/Residence408.obj` + `.mtl`

**Interfaces:**
- Produces: OBJ with one `g <node-name>` per GLB node that carries a mesh (`usemtl` runs
  inside each group). Unity imports each group as a named child GameObject under the model
  root — Task 3 finds children by these names (`door_front_slab`, `garage_door_panel`,
  `floor_west`, `shrub_1`, ...). Node names sanitised: spaces → `_`.

- [ ] **Step 1: Restructure the grouping.** In `main()`, replace the material-keyed `groups`
  dict with a node-keyed ordered list. In `walk`, replace the `groups.setdefault(...)` body:

```python
    nodes_out = []   # [(node_name, material_index, world_verts, idx, uv)] in scene order

    def walk(ni, parent):
        n = doc['nodes'][ni]
        m = matmul(parent, node_matrix(n))
        if 'mesh' in n:
            node_name = (n.get('name') or f'node{ni}').replace(' ', '_')
            for prim in doc['meshes'][n['mesh']]['primitives']:
                pos = accessor(doc, buf, prim['attributes']['POSITION'])
                idx = ([v[0] for v in accessor(doc, buf, prim['indices'])]
                       if 'indices' in prim else list(range(len(pos))))
                uv = (accessor(doc, buf, prim['attributes']['TEXCOORD_0'])
                      if 'TEXCOORD_0' in prim['attributes'] else None)
                world = [(m[0][0] * p[0] + m[0][1] * p[1] + m[0][2] * p[2] + m[0][3],
                          m[1][0] * p[0] + m[1][1] * p[1] + m[1][2] * p[2] + m[1][3],
                          m[2][0] * p[0] + m[2][1] * p[1] + m[2][2] * p[2] + m[2][3])
                         for p in pos]
                nodes_out.append((node_name, prim.get('material', 0), world, idx, uv))
        for c in n.get('children', []):
            walk(c, m)
```

  and the OBJ writer loop becomes (same v/vt counter discipline, one `g` per node name —
  consecutive primitives of the same node share one `g` line):

```python
    vbase, vtbase, tris = 1, 1, 0
    with open(os.path.join(outdir, name + '.obj'), 'w') as f:
        f.write(f"mtllib {name}.mtl\no {name}\n")
        last_group = None
        for node_name, mi, verts, idx, uv in nodes_out:
            if node_name != last_group:
                f.write(f"g {node_name}\n")
                last_group = node_name
            mat_name = mats[mi].get('name', 'mat' + str(mi)) if mi < len(mats) else 'default'
            f.write(f"usemtl {mat_name}\n")
            for v in verts:
                f.write(f"v {v[0]:.5f} {v[1]:.5f} {v[2]:.5f}\n")
            if uv is not None:
                for u in uv:
                    f.write(f"vt {u[0]:.5f} {1.0 - u[1]:.5f}\n")
                for t in range(0, len(idx), 3):
                    f.write(f"f {idx[t] + vbase}/{idx[t] + vtbase} "
                            f"{idx[t + 1] + vbase}/{idx[t + 1] + vtbase} "
                            f"{idx[t + 2] + vbase}/{idx[t + 2] + vtbase}\n")
                vtbase += len(uv)
            else:
                for t in range(0, len(idx), 3):
                    f.write(f"f {idx[t] + vbase} {idx[t + 1] + vbase} {idx[t + 2] + vbase}\n")
            tris += len(idx) // 3
            vbase += len(verts)
```

  Update the docstring: named groups are the contract (`door_*_slab/lite/knob` swing;
  `garage_door_panel` tilts; `floor_*`/`wall_*`/`partition_*`/`ceiling_*` collide;
  `shrub_*`/`grass_*` never do), one `g` per node, flattened models still import as one
  child.

- [ ] **Step 2: Re-convert 408 and verify the groups.**

Run: `python tools/glb-to-obj.py "C:\Users\mgagn\Downloads\408-residence.glb" Residence408`
Then: `grep -c "^g " Assets/Noir/Models/Residence408.obj`
Expected: > 200 groups (244 nodes, most carry meshes), and
`grep -c "^g door_front_slab$" ...` = 1. Also re-run the size check — bbox must still read
~18.1 x 5.1 x 30.5 m:

```bash
python - <<'EOF'
xs, ys, zs = [], [], []
for line in open(r"Assets/Noir/Models/Residence408.obj"):
    if line.startswith("v "):
        _, x, y, z = line.split()[:4]
        xs.append(float(x)); ys.append(float(y)); zs.append(float(z))
print(round(max(xs)-min(xs),1), round(max(ys)-min(ys),1), round(max(zs)-min(zs),1))
EOF
```

- [ ] **Step 3: Commit** (converter + regenerated obj/mtl):
  `The converter keeps the owner's names, and 408 comes through in pieces`

---

### Task 2: CityDoors learns the tilt-up

**Files:**
- Modify: `Assets/Noir/Unity/CityDoors.cs`

**Interfaces:**
- Consumes: the existing parallel arrays (`_hinges`, `_shut`, `_open`, `_angle`, `_at`,
  `_overrideUntil`, `_forceOpenUntil`) and `Update`'s swing application at
  `hinge.localEulerAngles = new Vector3(0f, _angle[i], 0f)`.
- Produces: `public void AddLift(Transform hinge, float openPitch)` — registers a hinge that
  rotates about its LOCAL X axis from 0 (shut) to `openPitch` degrees (negative tilts the
  panel bottom up and in when local X runs along the panel's top edge). Task 3 calls it.
  `Add(...)` keeps its exact signature and behaviour.

- [ ] **Step 1: Add the kind array and AddLift.** Beside the other lists:

```csharp
        /// <summary>0 = swing (yaw about local Y, every door in town until 2026-08-18);
        /// 1 = lift (pitch about local X - the one-piece tilt-up garage door of 1991,
        /// which rotates about its own top edge). The angle arrays are degrees on
        /// whichever axis the kind names.</summary>
        private readonly List<byte> _kindOf = new List<byte>();
```

  `Add` appends `_kindOf.Add(0);` as its last line. New method after `Add`:

```csharp
        /// <summary>Take a lift hinge - an overhead door rotating about its local X, which the
        /// caller has aligned with the panel's top edge. Shut is pitch 0 (the pivot's own
        /// rest pose); open is <paramref name="openPitch"/> degrees.</summary>
        public void AddLift(Transform hinge, float openPitch)
        {
            if (hinge == null) return;
            _hinges.Add(hinge);
            _shut.Add(0f);
            _open.Add(openPitch);
            _angle.Add(0f);
            _at.Add(hinge.position);
            _overrideUntil.Add(0f);
            _forceOpenUntil.Add(0f);
            _kindOf.Add(1);
        }
```

- [ ] **Step 2: Branch the two applications in Update.** Both places that write
  `hinge.localEulerAngles` (the out-of-range snap-shut at ~line 293 and the swing at ~307)
  become:

```csharp
        private void Apply(Transform hinge, int i)
        {
            hinge.localEulerAngles = _kindOf[i] == 0
                ? new Vector3(0f, _angle[i], 0f)
                : new Vector3(_angle[i], 0f, 0f);
        }
```

  called as `Apply(hinge, i);` after each `_angle[i]` write, replacing the inline
  assignments. Nothing else in Update changes — proximity, overrides, Force, Leafless and
  NearestDoor are kind-blind by construction.

- [ ] **Step 3: Build.** `dotnet build Noir.Unity.csproj -c Debug` — exit 0, then commit:
  `CityDoors learns the tilt-up: one more kind, same clock`

---

### Task 3: CityBuildings hinges the owner's doors

**Files:**
- Modify: `Assets/Noir/Unity/CityBuildings.cs`

**Interfaces:**
- Consumes: `CityDoors.Add(hinge, shutYaw, openYaw)`, `CityDoors.AddLift(hinge, openPitch)`
  (Task 2); the standing owner model instance from `Landmark` (both the exact-match arm and
  the terrace arm).
- Produces: `public sealed class OwnerModel : MonoBehaviour { }` (marker, top of the file's
  namespace, outside the CityBuildings class) — Task 4 keys collision on it;
  `private static void HingeOwnerDoors(GameObject stood)` — called from both arms right
  after a successful `Landmark`, before `Record`.

- [ ] **Step 0: Verify order.** Read `VillageHost.Build` and confirm `CityBuildings` runs
  and `CityCollision.Build` is called BEFORE `CityChunker.Bake`. If not, STOP and report.

- [ ] **Step 1: The marker and the call sites.** Add the marker class; in the exact-match
  arm after `if (owned != null) {` add `owned.AddComponent<OwnerModel>();
  HingeOwnerDoors(owned);` (before `Record`), and likewise `stood.AddComponent<OwnerModel>();
  HingeOwnerDoors(stood);` in the terrace arm.

- [ ] **Step 1b: Ground owner models at AUTHORED grade, not bounds-min.** Reported live by
  the owner 2026-08-18 ("the house is sitting above the terrain"): `Landmark` grounds with
  `go.transform.position.y + (groundY - b.min.y)`, which puts the model's LOWEST VERTEX at
  ground level - and his convention (the pipeline's own rule: "wheels/foundation on y=0")
  authors grade at model y=0 with the foundation BELOW it, so bounds-min grounding lifts
  the deliberate below-grade foundation out of the earth and floats the house by exactly
  the burial he modeled. `Landmark` gains a parameter `bool authoredGrade = false`; the two
  owner-model call sites pass `true`; pack prefabs keep bounds-min (their origins are not
  trustworthy). In `Landmark`, the position line becomes:

```csharp
            float lift = authoredGrade
                ? groundY                                  // authored y=0 sits AT grade;
                                                            // below-grade parts stay below
                : go.transform.position.y + (groundY - b.min.y);
            go.transform.position = new Vector3(want.x - drift.x, lift, want.z - drift.z);
```

- [ ] **Step 2: HingeOwnerDoors.** After `TerraceKeyFor`:

```csharp
        /// <summary>
        /// THE OWNER'S DOORS SWING (2026-08-18). His GLBs name their parts, and the convention
        /// is his own first house's: door_&lt;name&gt;_slab/lite/knob move together, the casing
        /// stays in the wall, garage_door_panel(+ribs/lites) tilts up about its top edge. Each
        /// family gets an empty pivot named "hinge" - EXACTLY that, lowercase: CityChunker's
        /// bake exemption keys on the parent being called "hinge" - placed on the slab's hinge
        /// edge. The hinge edge is the vertical edge FARTHER from the knob (the latch is where
        /// the knob is; the hinge is the other side), falling back to the slab's -X edge in
        /// model space when a door has no knob. Swing direction: into the building - the sign
        /// whose 85-degree rotation moves the leaf's centre toward the model's own bounds
        /// centre, tested arithmetically before committing. A family with no slab is logged
        /// loudly and left decorative.
        /// </summary>
        private static void HingeOwnerDoors(GameObject stood)
        {
            var doors = Object.FindFirstObjectByType<CityDoors>();
            if (doors == null) return;

            Bounds model = default; bool has = false;
            foreach (var r in stood.GetComponentsInChildren<Renderer>(true))
            { if (!has) { model = r.bounds; has = true; } else model.Encapsulate(r.bounds); }
            if (!has) return;

            // ---- the swing families: door_<name>_slab (+_lite/_knob) ----
            var kids = stood.GetComponentsInChildren<Transform>(true);
            foreach (var slabT in kids)
            {
                string n = slabT.name;
                if (!n.StartsWith("door_") || !n.EndsWith("_slab")) continue;
                string family = n.Substring(0, n.Length - "_slab".Length);   // "door_front"

                var slabR = slabT.GetComponent<Renderer>();
                if (slabR == null)
                {
                    Debug.LogError("[doors] owner door '" + n + "' has no renderer - left decorative");
                    continue;
                }
                Bounds slab = slabR.bounds;

                Transform knob = null, lite = null;
                foreach (var k in kids)
                {
                    if (k.name == family + "_knob") knob = k;
                    if (k.name == family + "_lite") lite = k;
                }

                // The slab is a thin box: its width axis is the larger of world X/Z extents.
                bool wideX = slab.size.x >= slab.size.z;
                Vector3 edgeA = slab.center + (wideX ? new Vector3(-slab.extents.x, 0, 0)
                                                     : new Vector3(0, 0, -slab.extents.z));
                Vector3 edgeB = slab.center + (wideX ? new Vector3(+slab.extents.x, 0, 0)
                                                     : new Vector3(0, 0, +slab.extents.z));
                Vector3 knobAt = knob != null && knob.GetComponent<Renderer>() != null
                    ? knob.GetComponent<Renderer>().bounds.center : edgeB;
                Vector3 hingeAt = (knobAt - edgeA).sqrMagnitude >= (knobAt - edgeB).sqrMagnitude
                    ? edgeA : edgeB;                          // farther from the knob
                hingeAt.y = slab.min.y;

                var hinge = new GameObject("hinge").transform;
                hinge.SetParent(slabT.parent, false);
                hinge.position = hingeAt;
                slabT.SetParent(hinge, true);
                if (lite != null) lite.SetParent(hinge, true);
                if (knob != null) knob.SetParent(hinge, true);

                // Swing INWARD: the sign whose quarter-turn carries the leaf centre toward
                // the model's own centre.
                Vector3 arm = slab.center - hingeAt;
                Vector3 plus = Quaternion.Euler(0f, 85f, 0f) * arm;
                Vector3 minus = Quaternion.Euler(0f, -85f, 0f) * arm;
                Vector3 toCentre = model.center - hingeAt;
                float open = Vector3.Dot(plus, toCentre) >= Vector3.Dot(minus, toCentre) ? 85f : -85f;
                doors.Add(hinge, 0f, open);
            }

            // ---- the overhead panel: garage_door_panel (+_ribs/_lites) tilts up ----
            foreach (var panelT in kids)
            {
                if (panelT.name != "garage_door_panel") continue;
                var panelR = panelT.GetComponent<Renderer>();
                if (panelR == null) continue;
                Bounds panel = panelR.bounds;

                bool wideX = panel.size.x >= panel.size.z;
                var hinge = new GameObject("hinge").transform;
                hinge.SetParent(panelT.parent, false);
                hinge.position = new Vector3(panel.center.x, panel.max.y, panel.center.z);
                // Local X must run along the panel's width for the pitch to be the tilt.
                if (!wideX) hinge.rotation = Quaternion.Euler(0f, 90f, 0f);
                panelT.SetParent(hinge, true);
                foreach (var k in kids)
                    if (k.name == "garage_door_ribs" || k.name == "garage_door_lites")
                        k.SetParent(hinge, true);

                // Tilt so the bottom edge moves toward the garage's inside: try -80, and if
                // that carries the panel's centre AWAY from the model centre, use +80.
                Vector3 arm = panel.center - hinge.position;
                Vector3 tiltA = hinge.rotation * Quaternion.Euler(-80f, 0f, 0f)
                              * Quaternion.Inverse(hinge.rotation) * arm;
                float openPitch = Vector3.Dot(tiltA, model.center - hinge.position)
                                  >= Vector3.Dot(arm, model.center - hinge.position) ? -80f : 80f;
                doors.AddLift(hinge, openPitch);
            }
        }
```

- [ ] **Step 3: Build.** `dotnet build Noir.Unity.csproj -c Debug` — exit 0, commit:
  `The owner's doors get their hinges, and the garage panel tilts`

---

### Task 4: CityCollision lets the player into an owner model

**Files:**
- Modify: `Assets/Noir/Unity/CityCollision.cs`

**Interfaces:**
- Consumes: `OwnerModel` marker (Task 3); the existing bought-building box loop in
  `Build` (`foreach (var node in built) ... foreach (Transform child in node.transform)`).
- Produces: owner-model children get collider-only mesh copies under the collision root
  (they must NOT live on the render pieces - the bake destroys those); everything else
  keeps its box.

- [ ] **Step 1: The branch.** Inside the child loop, before the bounds-box logic:

```csharp
                    // AN OWNER MODEL IS ENTERED, NOT BOXED (2026-08-18). The bounds box that
                    // is right for a bought prefab seals a hand-made house's doorway shut.
                    // His models carry real interiors - floors, partitions, ceilings - so the
                    // collision IS the model: one static MeshCollider per structural piece,
                    // on collider-only copies under this root, because CityChunker's bake
                    // destroys the render originals. Door leaves (under a "hinge" pivot) and
                    // soft dressing never collide - a shrub that stops a car is worse than a
                    // shrub that does not, and a door leaf blocking is a town-wide decision
                    // not taken tonight (parity with every generated door).
                    var owner = child.GetComponent<OwnerModel>();
                    if (owner != null
                        && child.GetComponentsInChildren<MeshFilter>(true).Length > 1)
                    {
                        // A FLATTENED model (one welded MeshFilter - every conversion before
                        // 2026-08-18) keeps the bounds box below, exactly as the spec says:
                        // its interior was never separable, so a box costs nothing it had.
                        int meshed = 0;
                        foreach (var mf in child.GetComponentsInChildren<MeshFilter>(true))
                        {
                            if (mf.sharedMesh == null) continue;
                            if (mf.transform.parent != null && mf.transform.parent.name == "hinge") continue;
                            if (SoftDressing(mf.name)) continue;

                            var cc = new GameObject(child.name + ":" + mf.name);
                            cc.transform.SetParent(root.transform, false);
                            cc.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                            cc.transform.localScale = mf.transform.lossyScale;
                            cc.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                            meshed++;
                        }
                        Debug.Log($"[collision] owner model '{child.name}': {meshed} piece "
                                + "collider(s), no box - the doorway is a real hole");
                        continue;
                    }
```

- [ ] **Step 2: SoftDressing.** A private helper beside `GroundMesh`:

```csharp
        /// <summary>The owner-model pieces a body passes through: planting, hose, string
        /// lights, painted joints. Names are the owner's own convention (see the spec).</summary>
        private static bool SoftDressing(string n) =>
            n.StartsWith("shrub_") || n.StartsWith("grass_") || n.StartsWith("bed_")
            || n == "garden_hose" || n == "hose_reel" || n == "porch_string_lights"
            || n == "paving_joints" || n == "foliage" || n.StartsWith("planters");
```

- [ ] **Step 3: Build.** `dotnet build Noir.Unity.csproj -c Debug` — exit 0, commit:
  `An owner model is entered, not boxed`

---

### Task 5: P spawns at 408's front walk

**Files:**
- Modify: `Assets/Noir/Unity/Player.cs` (the `Standing` method, ~line 818)

**Interfaces:**
- Consumes: `WorldModel.AllPlaces` (each `Place` has `Name`, `Door` (Tile)),
  `Space3D.ToWorld(Tile)`.
- Produces: nothing later tasks use; behavioural only.

- [ ] **Step 1: The preference.** At the top of `Standing(WorldModel world)`:

```csharp
            // HOME FIRST (owner's ruling, 2026-08-18): when the town has 408 Holmes Street -
            // the survey-seated lot, his own hand-made house since the same night - P stands
            // you on its front walk, one stride out from the door, facing the house. The
            // road-centre fallback below still serves every fixture town and any map without
            // the address, so no test moves.
            const string HomeAddress = "408 Holmes Street";
            foreach (var place in world.AllPlaces)
                if (place != null && place.Name == HomeAddress)
                {
                    var w = Space3D.ToWorld(place.Door);
                    return new Vector3(w.x, w.y, w.z - 2f);   // village +y is toward Holmes
                }
```

- [ ] **Step 2: Build.** `dotnet build Noir.Unity.csproj -c Debug` — exit 0, commit:
  `P stands you at your own front door`

---

### Task 6: The three PlayMode gates

**Files:**
- Modify: `Assets/Noir/PlayTests/TownGeometryPlayTests.cs` (append inside the class)

**Interfaces:**
- Consumes: `CityUnderTest.WaitUntilBuilt()/Host`, `CityDoors` (`Count`, `Leafless`,
  `PositionOf`, `NearestDoor`, `Force`), the world place `408 Holmes Street`,
  `Object.FindFirstObjectByType<Player>()`.

- [ ] **Step 1: Write the three tests** (they FAIL until Tasks 1-5 are all in a built town,
  and pass together after — run order inside this plan means write-then-verify happens in
  one PlayMode run at Step 2):

```csharp
        /// <summary>The owner's hinges exist and none lost its leaf to the bake - the exact
        /// fault the town-wide Leafless gate was built for, scoped to 408's four doors
        /// (front, rear, garage service, garage panel).</summary>
        [UnityTest]
        public IEnumerator TheOwnersDoorsSurviveTheBake()
        {
            yield return CityUnderTest.WaitUntilBuilt();
            var host = CityUnderTest.Host;
            var doors = Object.FindFirstObjectByType<CityDoors>();
            Assert.That(doors, Is.Not.Null);

            Place home = null;
            foreach (var p in host.World.AllPlaces)
                if (p != null && p.Name == "408 Holmes Street") home = p;
            Assert.That(home, Is.Not.Null, "the survey lost 408 Holmes Street");

            var centre = Space3D.ToWorld(home.Door);
            int nearby = 0;
            for (int i = 0; i < doors.Count; i++)
            {
                var d = doors.PositionOf(i) - centre;
                if (d.x * d.x + d.z * d.z < 40f * 40f) nearby++;
            }
            Assert.That(nearby, Is.GreaterThanOrEqualTo(4),
                "408 should hinge front, rear, garage service and the overhead panel");
            Assert.That(doors.Leafless(), Is.EqualTo(0),
                "a hinge with no renderer is a door the bake ate");
        }

        /// <summary>The front doorway is a hole a body fits through, and the floor inside is
        /// real: a capsule cast crosses the threshold untouched, and a ray straight down
        /// inside the hall lands on a collider.</summary>
        [UnityTest]
        public IEnumerator TheFrontDoorOfHolmesAdmitsThePlayer()
        {
            yield return CityUnderTest.WaitUntilBuilt();
            var host = CityUnderTest.Host;
            Place home = null;
            foreach (var p in host.World.AllPlaces)
                if (p != null && p.Name == "408 Holmes Street") home = p;
            Assert.That(home, Is.Not.Null);

            var doorW = Space3D.ToWorld(home.Door);
            // Outside is toward Holmes (world -z, see Player.Standing); inside is +z.
            var outside = new Vector3(doorW.x, doorW.y + 0.9f, doorW.z - 1.5f);
            var inside = new Vector3(doorW.x, doorW.y + 0.9f, doorW.z + 2.0f);
            var dir = (inside - outside).normalized;
            float len = (inside - outside).magnitude;

            bool blocked = Physics.CapsuleCast(
                outside + Vector3.up * 0.3f, outside + Vector3.up * 1.2f, 0.25f, dir, len);
            Assert.That(blocked, Is.False,
                "the doorway is sealed - the box is back, or a wall crosses the threshold");

            bool floor = Physics.Raycast(inside + Vector3.up * 1.5f, Vector3.down, 3f);
            Assert.That(floor, Is.True, "no floor inside the hall - nothing to stand on");
        }

        /// <summary>P stands you at 408's front walk when the address exists - within a few
        /// strides of the door, not out on Route 1.</summary>
        [UnityTest]
        public IEnumerator ThePlayerSpawnsAtHolmesFrontDoor()
        {
            yield return CityUnderTest.WaitUntilBuilt();
            var host = CityUnderTest.Host;
            var player = Object.FindFirstObjectByType<Player>();
            Assert.That(player, Is.Not.Null);

            Place home = null;
            foreach (var p in host.World.AllPlaces)
                if (p != null && p.Name == "408 Holmes Street") home = p;
            Assert.That(home, Is.Not.Null);

            bool wasWalking = player.Walking;
            if (!wasWalking) player.Toggle();
            for (int f = 0; f < 5; f++) yield return null;
            try
            {
                var at = player.Where;
                Assert.That(at.HasValue, "the body never stood up");
                var door = Space3D.ToWorld(home.Door);
                var d = at.Value - door;
                Assert.That(new Vector2(d.x, d.z).magnitude, Is.LessThan(6f),
                    "P put the player " + d.magnitude.ToString("0.0") + "m from his own door");
            }
            finally
            {
                if (player.Walking && !wasWalking) player.Toggle();
            }
        }
```

  Note: `Place` and `Space3D` are already in the file's usings (`Noir.Core.World`,
  `Noir.Unity`); `CityDoors` is `Noir.Unity` too. `player.Walking`/`Where`/`Toggle` are the
  Player API the response suite already drives.

- [ ] **Step 2: Build both.** `dotnet build Noir.Unity.csproj -c Debug` and
  `dotnet build Noir.PlayTests.csproj -c Debug` — exit 0 both.

- [ ] **Step 3: Run the three tests batch** (editor closed - CHECK for Unity.exe first;
  if the owner has it open, hand him the run instead of killing anything):

```
Unity.exe -batchmode -projectPath C:\SerialKillerGame -runTests -testPlatform PlayMode ^
  -assemblyNames Noir.PlayTests ^
  -testFilter "Noir.PlayTests.TownGeometryPlayTests.TheOwnersDoorsSurviveTheBake|Noir.PlayTests.TownGeometryPlayTests.TheFrontDoorOfHolmesAdmitsThePlayer|Noir.PlayTests.TownGeometryPlayTests.ThePlayerSpawnsAtHolmesFrontDoor" ^
  -testResults <xml> -logFile <log>
```

Expected: 3 of 3 PASS. On a red, read the log's `[doors]`/`[collision]` lines before
touching code - the failure names which layer lied.

- [ ] **Step 4: Commit**: `Three gates on the owner's front door`

---

### Task 7: Land it

- [ ] **Step 1:** Full PlayMode gate (`-testCategory "!Diagnostic"`, editor closed) OR, if
  the owner is in the editor, leave the full gate to tonight's nightly run and say so in the
  report - the three new tests already ran in Task 6.
- [ ] **Step 2:** Update `tools/nightly-gate.ps1` `$playmodeBaselinePass = 35` → `38`, and
  CLAUDE.md's PlayMode baseline entry (new entry above the 35-of-35 one, naming the three
  tests and this plan's path), in the same commit.
- [ ] **Step 3:** The spec gets its Landed line (date + measured numbers). `docs/CONTROLS.md`
  gains the door verb line if it still lacks one (the doors audit flagged it missing).
- [ ] **Step 4:** Commit docs, push. Named leftovers: door leaves do not physically block
  (town-wide parity); PubRow/RossvilleStorefront re-conversion under the new pipeline waits
  for the owner's eye; furnishing 408 is his, in Designer.
