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
            // Northgate is standing on the countryside rather than falling out of the world. USED
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
                    var rends = child.GetComponentsInChildren<Renderer>();
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

            Debug.Log($"[collision] 1 ground mesh and {walls} building boxes, "
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
                // Wound to face up, matching the ground renderer's own convention.
                tris[t++] = v0; tris[t++] = v2; tris[t++] = v1;
                tris[t++] = v1; tris[t++] = v2; tris[t++] = v3;
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
    }
}
