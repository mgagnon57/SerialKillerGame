using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// WHAT ONE CAR IS ALLOWED TO COST.
    ///
    /// A Poly Universal Pack car is **11 MeshRenderers, 11 MeshFilters and 11 MeshColliders**
    /// across 12 GameObjects — body, glass, lights, wheels and trim each on their own object.
    /// That is a sensible way to ship a prefab and a ruinous way to put 770 of them in a town:
    ///
    ///   611 parked cars   ~6,700 renderers, ~6,700 mesh colliders, ~7,300 transforms
    ///   159 moving cars   ~1,750 renderers, ~1,750 mesh colliders — TRANSFORMED EVERY FRAME
    ///
    /// For scale, this project once fought the ground alone from 2,626 draw calls down to 319.
    ///
    /// `CityChunker` cannot help either of them. The parked cars are built after the bake on
    /// purpose, because a combined mesh cannot be taken away when its owner drives to Hoopeston,
    /// and the moving ones obviously move. So each car does its own combine, one car wide, and
    /// stays an object that can be switched off or driven.
    ///
    /// SUBMESHES, NOT ONE MERGED LUMP. `CombineMeshes(combine, false)` keeps one submesh per
    /// source, so the body, the glass and the lights each keep their own material. It is one
    /// renderer with several materials rather than one material — the object count collapses and
    /// the car is not repainted.
    ///
    /// AND THE MESH COLLIDERS GO. PhysX bakes every one it is given, and a NON-CONVEX mesh
    /// collider that is moved every frame is close to the worst thing you can hand it: eleven of
    /// them per car, on 159 cars, rebuilding the static AABB tree continuously. A car needs to be
    /// solid, not accurate. One box does that.
    /// </summary>
    public static class CarMesh
    {
        /// <summary>
        /// Collapse a car to a single renderer and a single box, in place.
        /// </summary>
        /// <param name="car">The instantiated car. Must NOT be a linked prefab instance — you may
        /// not restructure one, and this destroys every child.</param>
        /// <param name="moving">
        /// True for a car the traffic model drives. Kept as a parameter because the distinction is
        /// real, and currently changes nothing — see the note below on the Rigidbody that was
        /// tried here and REVERTED.
        ///
        /// A collider moved without a Rigidbody is a STATIC collider being moved, and the textbook
        /// answer is a kinematic Rigidbody. **That was measured and it made the game half as
        /// fast.** `CityTraffic.Update` drives up to twelve 1/30 s slices per frame and moves each
        /// car by setting `transform.position`, so 159 kinematic bodies became ~1,900 physics
        /// transform-syncs a frame, and every `CharacterController` query forced another.
        /// `WhyAreThePeopleNotAnimating` went 288 s → 590 s and `ThePlayerCanStandInTheStreet`
        /// tripled. One box and no Rigidbody is still eleven times cheaper than the eleven mesh
        /// colliders it replaced, which was the actual problem.
        ///
        /// The textbook fix only works if the mover uses `Rigidbody.MovePosition`, and rewriting
        /// the traffic model's sub-stepping to do that is a bigger change than this one.
        /// </param>
        public static void Flatten(GameObject car, bool moving = false)
        {
            if (car == null) return;

            var filters = car.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0) return;

            var combine = new List<CombineInstance>(filters.Length);
            var materials = new List<Material>(filters.Length);

            foreach (var filter in filters)
            {
                var mesh = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (mesh == null || renderer == null) continue;

                var mats = renderer.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    combine.Add(new CombineInstance
                    {
                        mesh = mesh,
                        subMeshIndex = sub,
                        transform = car.transform.worldToLocalMatrix
                                  * filter.transform.localToWorldMatrix,
                    });
                    materials.Add(sub < mats.Length ? mats[sub]
                                : mats.Length > 0 ? mats[0] : null);
                }
            }

            if (combine.Count == 0) return;

            var merged = new Mesh { name = car.name + "_merged" };
            merged.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            merged.CombineMeshes(combine.ToArray(), false, true);

            // Everything below the car goes, colliders and all.
            for (int i = car.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(car.transform.GetChild(i).gameObject);

            foreach (var stray in car.GetComponents<MeshCollider>()) Object.DestroyImmediate(stray);
            var ownFilter = car.GetComponent<MeshFilter>();
            if (ownFilter != null) Object.DestroyImmediate(ownFilter);
            var ownRenderer = car.GetComponent<MeshRenderer>();
            if (ownRenderer != null) Object.DestroyImmediate(ownRenderer);

            car.AddComponent<MeshFilter>().sharedMesh = merged;
            car.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();

            var box = car.AddComponent<BoxCollider>();
            box.center = merged.bounds.center;
            box.size = merged.bounds.size;

            // `moving` deliberately changes nothing today. See the parameter docs: the kinematic
            // Rigidbody that belongs here halved the frame rate and was reverted.
            _ = moving;
        }
    }
}
