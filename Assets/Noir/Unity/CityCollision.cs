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
        /// Where the ground is, near enough.
        ///
        /// The walking surfaces of this map are three, and MEASURED rather than assumed: village
        /// ground at y=0, a road tile's asphalt at 0.02, and its pavement at 0.12 - the road kit
        /// draws a tile as two planes and `CityStreets` lifts its root to 0.12 to keep the
        /// carriageway clear of the ground plane. So the whole world is inside a twelve-centimetre
        /// band, and one floor through the middle of it is never more than six centimetres out.
        ///
        /// Six centimetres is under the CharacterController's own skin width and far under its
        /// step offset. Modelling the three properly would mean a collider per road corridor and
        /// per paved block, which is a great deal of arithmetic to move a player's feet by less
        /// than the thickness of a kerb.
        /// </summary>
        private const float Floor = 0.06f;

        /// <summary>How thick the ground slab is. Enough that nothing tunnels through it.</summary>
        private const float Bedrock = 4f;

        public static GameObject Build(WorldModel world, Transform parent, params GameObject[] built)
        {
            var root = new GameObject("CityCollision");
            root.transform.SetParent(parent, false);

            // ---- the ground ----
            //
            // One slab, the whole map and a margin past it, so somebody who walks off the edge of
            // Northgate is standing on the countryside rather than falling out of the world.
            const float Beyond = 200f;
            float w = world.Width + Beyond * 2f, h = world.Height + Beyond * 2f;

            var ground = new GameObject("ground");
            ground.transform.SetParent(root.transform, false);
            var slab = ground.AddComponent<BoxCollider>();
            slab.size = new Vector3(w, Bedrock, h);
            // Village y runs into -z, as everywhere. The slab's TOP is the walking surface.
            slab.center = new Vector3(world.Width * 0.5f,
                                      Floor - Bedrock * 0.5f,
                                      -world.Height * 0.5f);

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

            Debug.Log($"[collision] 1 ground slab and {walls} building boxes, "
                    + $"floor at y={Floor:0.00}.");
            return root;
        }
    }
}
