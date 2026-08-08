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
        /// The ground slab's own grid spacing, in metres - matches ElevationGrid's native
        /// resolution, so this samples the real data at exactly the density it was measured at
        /// rather than inventing detail between samples or throwing detail away.
        /// </summary>
        private const float Step = 30f;

        public static GameObject Build(WorldModel world, Transform parent, params GameObject[] built)
        {
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

            int generated = SolidifyGeneratedWalls(parent);

            Debug.Log($"[collision] 1 ground mesh, {walls} bought-building boxes and "
                    + $"{generated} generated wall chunk(s), "
                    + $"floor {Floor:0.00}m above local terrain.");
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

            var tris = new int[(cols - 1) * (rows - 1) * 6];
            int t = 0;
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
                tris[t++] = v0; tris[t++] = v1; tris[t++] = v2;
                tris[t++] = v1; tris[t++] = v3; tris[t++] = v2;
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
        /// Found by walking for "Walls" rather than by being handed it, because VillageMesh owns
        /// its own hierarchy and a signature that named the path would go stale the first time it
        /// moved. Costs one recursive search once per build.
        /// </summary>
        private static int SolidifyGeneratedWalls(Transform parent)
        {
            var walls = FindByName(parent, "Walls");
            if (walls == null)
            {
                Debug.LogWarning("[collision] no generated Walls node - the houses have no "
                               + "surface and the player will walk through them.");
                return 0;
            }

            int made = 0;
            foreach (var mf in walls.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                if (mf.GetComponent<MeshCollider>() != null) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                made++;
            }
            return made;
        }

        private static Transform FindByName(Transform where, string name)
        {
            if (where == null) return null;
            if (where.name == name) return where;
            foreach (Transform child in where)
            {
                var hit = FindByName(child, name);
                if (hit != null) return hit;
            }
            return null;
        }

    }
}
