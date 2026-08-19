using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// Something to stand on, and something to be stopped by.
    ///
    /// THE CITY HAS NO COLLIDERS AND THAT IS DELIBERATE. `CityChunker` combines four hundred
    /// prefabs into a handful of meshes, so a physics raycast into the baked city would hit
    /// "chunk 7, material 3" and have no idea which building that was - which is exactly why
    /// `PlacePicker` walks the ray against the world model instead, and why `CityTraffic` avoids
    /// by rules rather than by intersection test. None of that changes here.
    ///
    /// What changes is that a PERSON can now walk around, and a CharacterController is physics.
    /// It needs a floor or it falls through the world, and it needs walls or it walks through the
    /// bank. So this builds a collision shell that is SEPARATE from everything the renderer knows
    /// about: no meshes, no renderers, nothing the chunker will touch, and nothing anything else
    /// in the project raycasts against.
    ///
    /// BOXES, NOT MESH COLLIDERS. A mesh collider per baked chunk would be exact and would also
    /// hand the physics scene the whole city's geometry twice over. Every building here is placed
    /// at a right angle - `Seat` only ever uses 0, 90, 180 and 270 - so an axis-aligned box round
    /// each one is a close fit for a fraction of the cost.
    ///
    /// Built BEFORE the bake, because it reads the renderer bounds of things the bake destroys,
    /// and parented OUTSIDE the node the bake touches so it survives it.
    /// </summary>
    public static class CityCollision
    {
        /// <summary>
        /// True once the town's collision has been built AND the post-bake wall pass has run.
        ///
        /// It exists so the generated walls can tell the difference between the two ways they get
        /// built. When `Massing` is on at startup the walls are made during `VillageHost.Build`,
        /// long before this file runs, and the pass after the bake picks them up. When it is off
        /// they are made ON DEMAND, minutes later, by somebody ticking a switch - and by then this
        /// pass has been and gone and nothing will ever come back for them. `VillageMesh` reads
        /// this to know which of the two it is in, and surfaces its own walls in the second case.
        ///
        /// Reset at the top of Build, because a static survives a domain reload when the editor is
        /// configured not to do one, and a stale `true` here means the walls surface themselves
        /// during startup and are then thrown away by the bake.
        /// </summary>
        public static bool Ready { get; private set; }

        /// <summary>
        /// Where the ground is, near enough, ABOVE the real terrain.
        ///
        /// The walking surfaces of this map are three, and MEASURED rather than assumed: village
        /// ground at y=0 (of its own LOCAL terrain height), a road tile's asphalt at 0.02, and its
        /// pavement at 0.12 - the road kit draws a tile as two planes and `CityStreets` lifts its
        /// root to 0.12 to keep the carriageway clear of the ground plane. So at any given point
        /// the walking surfaces are within a twelve-centimetre band of each other, and one floor
        /// through the middle of that band is never more than six centimetres out - but the BAND
        /// ITSELF now rides the real elevation, which used to be nothing (see ElevationGrid).
        ///
        /// Six centimetres is under the CharacterController's own skin width and far under its
        /// step offset. Modelling the three properly would mean a collider per road corridor and
        /// per paved block, which is a great deal of arithmetic to move a player's feet by less
        /// than the thickness of a kerb.
        /// </summary>
        private const float Floor = 0.06f;

        /// <summary>
        /// The ground lattice's sample spacing - a QUARTER of ElevationGrid's native 30 m, and
        /// ONE SHARED UNIFORM LATTICE, re-learned 2026-08-18 the hard way: the first saddle
        /// repair subdivided each 30 m cell independently by its own curvature, and
        /// neighbouring cells that chose different lattices disagreed along the edge they
        /// shared - measured 338 seams with a lip over 10 cm, 15 cm at the worst - and the
        /// owner's car found them within the hour, as invisible kerbs across open ground. A
        /// uniform lattice shares every border vertex, so there is nothing to disagree; at
        /// 7.5 m the flat-triangle bow off the bilinear surface is a sixty-fourth of a cell's
        /// corner cross-difference - under 3 cm everywhere in town, ~12 cm only at the creek
        /// ravine's corner of the countryside, all inside the CharacterController's step.
        /// </summary>
        private const float Step = 7.5f;

        public static GameObject Build(WorldModel world, Transform parent, params GameObject[] built)
        {
            Ready = false;

            var root = new GameObject("CityCollision");
            root.transform.SetParent(parent, false);

            // ---- the ground ----
            //
            // One mesh, the whole map and a margin past it, so somebody who walks off the edge of
            // Rossville is standing on the countryside rather than falling out of the world. USED
            // TO BE A FLAT BOX: correct while the whole map was one flat plane, wrong the moment
            // real elevation gave the map 24m of relief - a flat floor at y=0.06 would have had
            // the player walking on stilts at one edge of town and buried at the other. A
            // MeshCollider sampling the same ElevationGrid the visual ground uses keeps physics
            // and what is drawn in agreement everywhere, not just at the crossing.
            const float Beyond = 200f;

            var ground = new GameObject("ground");
            ground.transform.SetParent(root.transform, false);
            var mc = ground.AddComponent<MeshCollider>();
            mc.sharedMesh = GroundMesh(world, Beyond);

            // ---- the buildings ----
            int walls = 0;

            foreach (var node in built)
            {
                if (node == null) continue;

                foreach (Transform child in node.transform)
                {
                    // AN OWNER MODEL IS ENTERED, NOT BOXED (2026-08-18). The bounds box that
                    // is right for a bought prefab seals a hand-made house's doorway shut -
                    // the owner's own report: "I am blocked from approaching doors", against
                    // a box that wrapped his whole modeled yard. His models carry real
                    // interiors - floors, partitions, ceilings - so the collision IS the
                    // model: one static MeshCollider per structural piece, on collider-only
                    // copies under this root, because CityChunker's bake destroys the render
                    // originals. Door leaves (under a "hinge" pivot) and soft dressing never
                    // collide. A FLATTENED model (one welded MeshFilter - every conversion
                    // before 2026-08-18) keeps the box below: its interior was never
                    // separable, so a box costs nothing it had.
                    if (child.GetComponent<OwnerModel>() != null
                        && child.GetComponentsInChildren<MeshFilter>(true).Length > 1)
                    {
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

                    // INCLUDE INACTIVE, or a layer switch silently deletes the walls.
                    //
                    // These roots are the Buildings, Districts and Houses layers, and VillageHost
                    // registers them with Layers BEFORE calling this - and Layers.Register ends in
                    // root.SetActive(IsOn(kind)). GetComponentsInChildren<Renderer>() skips
                    // inactive objects by default, so with those layers switched off this found
                    // nothing, built no colliders at all, and the player walked through every
                    // building in Rossville. Layers persist in PlayerPrefs, so that was not a
                    // session-long glitch - it was however long ago somebody last looked at a
                    // survey view.
                    //
                    // Same fault as the traffic freeze: a switch about what is DRAWN reaching
                    // something that is not about drawing. Physics is not a view.
                    var rends = child.GetComponentsInChildren<Renderer>(true);
                    if (rends.Length == 0) continue;

                    var b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

                    // Anything too small to walk into is not worth a collider. A bin is scenery;
                    // walking through one costs nothing and eight thousand box colliders does.
                    if (b.size.x < 1.5f || b.size.z < 1.5f || b.size.y < 1.5f) continue;

                    var box = new GameObject(child.name);
                    box.transform.SetParent(root.transform, false);
                    box.transform.position = b.center;

                    var solid = box.AddComponent<BoxCollider>();
                    solid.size = b.size;
                    walls++;
                }
            }

            // THE GENERATED WALLS ARE NOT DONE HERE ANY MORE. They used to be, and they were
            // wrong twice over - see SolidifyWalls. VillageHost calls it after the bake.
            Debug.Log($"[collision] 1 ground mesh and {walls} bought-building boxes, "
                    + $"floor {Floor:0.00}m above local terrain. "
                    + "The generated houses are surfaced after the bake.");
            return root;
        }

        /// <summary>
        /// A simple heightfield grid, ElevationGrid sampled directly rather than through
        /// Space3D - this is LOCAL to the collider's own GameObject, and Space3D's automatic
        /// elevation would double it up the moment this mesh's transform were ever moved off the
        /// origin. Extends `beyond` past the map on every side, sampling ElevationGrid.HeightAt
        /// there too - it clamps to the nearest real column past the edge of its own data, so the
        /// ground you can walk on stays flush with the mapped town instead of stepping flat at
        /// the boundary the way the old box did.
        /// </summary>
        /// <summary>The owner-model pieces a body passes through: planting, hose, string
        /// lights, painted joints - and, learned the night the owner got stuck in his own
        /// front doorway, the SNAG TRIM: brick course bands an inch proud of the wall, the
        /// entry lamp, porch battens, downspouts, vents, gutters. A doorway is a 3-foot
        /// opening and the player capsule is 22 inches wide; every centimetre of decorative
        /// collider beside it is a shoulder-catch. Structure collides; detail does not.
        /// Names are the owner's own convention (see the spec at
        /// docs/superpowers/specs/2026-08-18-owner-model-doors-design.md).</summary>
        private static bool SoftDressing(string n) =>
            n.StartsWith("shrub_") || n.StartsWith("grass_") || n.StartsWith("bed_")
            || n == "garden_hose" || n == "hose_reel" || n == "porch_string_lights"
            || n == "paving_joints" || n == "foliage" || n.StartsWith("planters")
            || n.StartsWith("course_") || n.EndsWith("_battens") || n.EndsWith("_joints")
            || n == "entry_lamp" || n == "porch_light_cord" || n.StartsWith("downspout")
            || n == "meters_east" || n == "crawl_vent" || n == "service_mast"
            || n.StartsWith("vent_") || n == "roof_vents" || n.EndsWith("_gutter")
            || n.EndsWith("_fascia") || n.EndsWith("_bars") || n.StartsWith("satellite")
            || n.StartsWith("dish_");

        private static Mesh GroundMesh(WorldModel world, float beyond)
        {
            float x0 = -beyond, x1 = world.Width + beyond;
            float y0 = -beyond, y1 = world.Height + beyond;

            int cols = Mathf.CeilToInt((x1 - x0) / Step) + 1;
            int rows = Mathf.CeilToInt((y1 - y0) / Step) + 1;

            var verts = new Vector3[cols * rows];
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                float wx = x0 + c * Step;
                float wy = y0 + r * Step;
                verts[r * cols + c] = new Vector3(wx, Floor + ElevationGrid.HeightAt(wx, wy), -wy);
            }

            var tris = new List<int>((cols - 1) * (rows - 1) * 6);
            for (int r = 0; r < rows - 1; r++)
            for (int c = 0; c < cols - 1; c++)
            {
                int v0 = r * cols + c, v1 = v0 + 1, v2 = v0 + cols, v3 = v2 + 1;

                // WOUND TO FACE UP, AND IT DID NOT. This read `v0,v2,v1 / v1,v2,v3` and said in a
                // comment that it faced up. It faced DOWN, and the arithmetic says so plainly:
                // row r maps to world z = -wy, so stepping a row FORWARD steps z BACKWARD, and
                // that negation mirrors the handedness of every quad. With
                //   v0=(x,h,-y)  v1=(x+S,h,-y)  v2=(x,h,-y-S)
                //   (v2-v0) x (v1-v0) = (0,0,-S) x (S,0,0) = (0,-S²,0)
                // the normal points at the floor. RecalculateNormals then faithfully reproduced
                // it, so the shaded ground and the collision agreed with each other and both were
                // upside down.
                //
                // A CharacterController dropping onto the BACK of a MeshCollider face is not
                // reliably stopped by it - sometimes PhysX catches it, sometimes the man goes
                // through - which is why ThePlayerCanStandInTheStreet has been intermittent rather
                // than simply broken, and why it was written off as flaky. Probed: spawn 6.90,
                // ground 3.90, and the fall passes straight through 3.90 at 0.06 m a step. That is
                // not tunnelling. That is a floor with no upward face.
                tris.Add(v0); tris.Add(v1); tris.Add(v2);
                tris.Add(v1); tris.Add(v3); tris.Add(v2);
            }

            var mesh = new Mesh
            {
                name = "GroundCollision",
                indexFormat = verts.Length > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>
        /// THE GENERATED HOUSES, WHICH HAD NO COLLIDER AT ALL.
        ///
        /// WHAT WAS WRONG. Everything above collides the BOUGHT prefabs - the Universal Pack
        /// models placed on authored lots - and those are behind ShowBuildings, which is off by
        /// default and describes a Chicago brownstone rather than an Illinois frame house. The
        /// town this project actually draws is VillageMesh's generated massing, it is built
        /// unconditionally, and nothing ever gave it a surface. A PlayMode run said so plainly
        /// and nobody had read the line:
        ///
        ///     [collision] 1 ground mesh and 0 building boxes
        ///
        /// Zero. Press P and you walked through every house in Rossville.
        ///
        /// A MESH COLLIDER PER CHUNK, not a box per building. VillageMesh already chunks the
        /// walls on a grid for culling, and those chunks are the exact geometry that is drawn -
        /// so this costs one component each, needs no bounds arithmetic, and cannot disagree with
        /// what the eye sees. Boxes would fill every doorway and every yard between an L.
        ///
        /// ⚠ AND THE FIRST VERSION OF THAT DID NOT WORK, FOR TWO SEPARATE REASONS, AND THE TOWN
        /// WAS WALK-THROUGH FOR DAYS WITH THE FIX ALREADY IN THE FILE. Both were found on
        /// 2026-08-11 in a live editor, in the log and in the scene graph:
        ///
        ///     [collision] no generated Walls node - the houses have no surface
        ///     Walls node: 342 MeshFilters, 0 MeshColliders
        ///
        ///  1. IT RAN TOO EARLY. `VillageMesh` registers the massing with `Layers.RegisterLazy`,
        ///     so with the layer off at startup there is no `Walls` node when `Build` runs, this
        ///     found nothing, and NOTHING RE-RAN IT when the walls appeared. Ticking the switch
        ///     drew 342 chunks of house that you walked straight through for the rest of the
        ///     session. That is the state the owner reported from.
        ///  2. AND WHEN IT RAN ON TIME, THE BAKE ATE IT. `CityChunker.Bake` ends in
        ///     `DestroyImmediate(r.gameObject)` on every renderer it consumed - and a MeshCollider
        ///     added by this pass is a component ON one of those GameObjects. So with the layer ON
        ///     at startup the colliders were built, cooked, and destroyed thirty lines later. The
        ///     old code could not win either way round.
        ///
        /// SO IT IS CALLED AFTER THE BAKE, AND AGAIN WHEN THE WALLS TURN UP LATE, and it FINDS THE
        /// WALLS BY THEIR MATERIAL RATHER THAN BY THEIR NODE. That last part is what makes one
        /// method serve both: before the bake the geometry is 342 chunk meshes under `Walls`, and
        /// after it the same triangles are Chunk_* meshes under `Baked` with the node gone - but
        /// `CityChunker` groups by material and re-uses the same `Materials3D.Walls` instances, so
        /// the paint is the one thing that survives the move. Searching for a node name could only
        /// ever have found one of the two.
        ///
        /// Idempotent, because it is called from two places and either may be the one that fires.
        /// </summary>
        public static int SolidifyWalls(Transform under)
        {
            Ready = true;
            if (under == null) return 0;

            var paint = new System.Collections.Generic.HashSet<Material>(Materials3D.Walls);

            int made = 0, already = 0;
            foreach (var mf in under.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;

                var r = mf.GetComponent<MeshRenderer>();
                if (r == null) continue;

                bool wall = false;
                foreach (var m in r.sharedMaterials)
                    if (m != null && paint.Contains(m)) { wall = true; break; }
                if (!wall) continue;

                if (mf.GetComponent<MeshCollider>() != null) { already++; continue; }

                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                made++;
            }

            // LOUD WHEN IT FINDS NOTHING, because finding nothing is indistinguishable from
            // working right up until somebody walks into a house. The startup pass legitimately
            // finds nothing when the massing is switched off - there are no walls to be stopped
            // by - so it says which of the two it was rather than crying wolf.
            if (made == 0 && already == 0)
                Debug.Log("[collision] no generated wall geometry to surface - the massing layer "
                        + "is off. It will be surfaced when the switch is ticked.");
            else
                Debug.Log($"[collision] {made} generated wall chunk(s) given a surface"
                        + (already > 0 ? $", {already} already had one." : "."));

            return made;
        }
    }
}
