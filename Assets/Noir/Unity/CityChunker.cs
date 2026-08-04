using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Noir.Unity
{
    /// <summary>
    /// Bakes the placed prefabs down into a few big meshes.
    ///
    /// The city is assembled out of bought pieces, and a piece is a renderer: a townhouse is six
    /// sections, a street tile is one, and every hydrant, bin, sign and parked car is another.
    /// That is the right way to BUILD it and the wrong way to DRAW it - the village manages the
    /// whole of Ashcombe in 1,835 renderers by combining its procedural geometry on a grid, and
    /// the city had drifted well past that with nothing merged at all.
    ///
    /// So: group every renderer by which chunk of the map it stands in and which material it
    /// wears, combine each group into one mesh, and throw the originals away. Same picture,
    /// far fewer things for the CPU to cull and submit.
    ///
    /// What it deliberately does NOT touch:
    ///   - anything with a Light on it, or under one - those have to stay addressable, because
    ///     SunRig turns windows and lamps on and off by the hour
    ///   - skinned meshes and particles, which cannot be combined this way at all
    ///
    /// Chunked on the same 64m grid as MeshChunks, so the two agree about what "near" means.
    /// </summary>
    public static class CityChunker
    {
        /// <summary>Match MeshChunks.Size, so the whole renderer agrees on one grid.</summary>
        public const int Chunk = 64;

        public static void Bake(GameObject root)
        {
            if (root == null) return;

            var before = root.GetComponentsInChildren<MeshRenderer>();
            if (before.Length == 0) return;

            // THE GRID COARSENS FOR A LAYER THAT COVERS THE COUNTY.
            //
            // 64 m matches MeshChunks.Size and is right for anything the size of a town: the
            // buildings bake to a couple of hundred meshes. But the trees and the farmland are
            // scattered over the whole 2100 x 2400 map, which is about 1,250 chunks, and with a
            // handful of materials that came to 5,723 baked meshes out of 81,940 renderers -
            // fourteen renderers per mesh. Thousands of tiny CombineMeshes calls cost 10.6 s,
            // and thousands of tiny meshes are worse to draw afterwards than a few big ones.
            //
            // Measured, after the whole 120-second startup was profiled rather than guessed at.
            // Below the threshold nothing changes at all, so every layer that was already fine
            // bakes on exactly the grid it did before.
            int chunk = before.Length > 20000 ? Chunk * 4 : Chunk;

            // key -> the pieces that will become one mesh
            var groups = new Dictionary<(int cx, int cz, int mat), List<(MeshFilter f, int sub)>>();
            var materials = new List<Material>();
            var index = new Dictionary<Material, int>();

            // EXACTLY the renderers whose triangles ended up in a baked mesh. Deciding what to
            // destroy from the same test that decided what to bake is not enough: the bake also
            // needs the mesh to be READABLE, and an unreadable one was being skipped by the
            // grouping loop and then deleted anyway by the removal loop. The elevated railway
            // vanished that way - placed, logged, and quietly thrown away a moment later.
            var consumed = new HashSet<GameObject>();

            foreach (var r in before)
            {
                if (!Combinable(r)) continue;

                var f = r.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null) continue;
                if (!f.sharedMesh.isReadable)
                {
                    Debug.LogWarning($"[chunker] '{r.name}' is not Read/Write enabled, so it is "
                                   + "left alone. Run Noir/Make City Meshes Readable.");
                    continue;
                }
                consumed.Add(r.gameObject);

                var at = r.bounds.center;
                int cx = Mathf.FloorToInt(at.x / chunk), cz = Mathf.FloorToInt(at.z / chunk);

                var slots = r.sharedMaterials;
                for (int s = 0; s < slots.Length && s < f.sharedMesh.subMeshCount; s++)
                {
                    var m = slots[s];
                    if (m == null) continue;
                    if (!index.TryGetValue(m, out int mi))
                    {
                        index[m] = mi = materials.Count;
                        materials.Add(m);
                    }

                    var key = (cx, cz, mi);
                    if (!groups.TryGetValue(key, out var list))
                        groups[key] = list = new List<(MeshFilter, int)>();
                    list.Add((f, s));
                }
            }

            if (groups.Count == 0) return;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            long groupMs = clock.ElapsedMilliseconds;

            var baked = new GameObject("Baked");
            baked.transform.SetParent(root.transform, false);

            int meshes = 0;
            foreach (var pair in groups)
            {
                var combines = new CombineInstance[pair.Value.Count];
                for (int i = 0; i < pair.Value.Count; i++)
                    combines[i] = new CombineInstance
                    {
                        mesh = pair.Value[i].f.sharedMesh,
                        subMeshIndex = pair.Value[i].sub,
                        transform = pair.Value[i].f.transform.localToWorldMatrix,
                    };

                var mesh = new Mesh { name = $"Chunk_{pair.Key.cx}_{pair.Key.cz}_{pair.Key.mat}" };
                // Well past 65k vertices in a chunk this size, so a 16-bit index buffer silently
                // wraps and the mesh comes out as confetti.
                mesh.indexFormat = IndexFormat.UInt32;
                mesh.CombineMeshes(combines, true, true);
                mesh.RecalculateBounds();

                var go = new GameObject(mesh.name);
                go.transform.SetParent(baked.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = materials[pair.Key.mat];
                mr.shadowCastingMode = ShadowCastingMode.On;
                meshes++;
            }

            long combineMs = clock.ElapsedMilliseconds - groupMs;

            // Only now, or the combine reads meshes that have already gone.
            int removed = 0;
            foreach (var r in before)
            {
                if (r == null || !consumed.Contains(r.gameObject)) continue;
                if (r.transform.IsChildOf(baked.transform)) continue;

                // NEVER DESTROY THE ROOT WE WERE ASKED TO BAKE. A builder is free to put its
                // mesh straight onto the node it returns - CityOutlines does, and so did
                // RoadCentrelines - and that node is also the LAYER root the switches hold.
                // Destroying it took the layer's switch with it and threw on the very next line
                // of this method, which aborted VillageHost.Awake half built and left a black
                // screen with no camera. Strip the drawing off it instead; the baked copy is
                // already parented under it, so nothing is lost and nothing draws twice.
                if (r.gameObject == root)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    Object.DestroyImmediate(r);
                    if (mf != null) Object.DestroyImmediate(mf);
                    removed++;
                    continue;
                }

                Object.DestroyImmediate(r.gameObject);
                removed++;
            }

            long destroyMs = clock.ElapsedMilliseconds - groupMs - combineMs;

            int after = root.GetComponentsInChildren<MeshRenderer>().Length;
            Debug.Log($"[chunker] {before.Length} renderers -> {after} "
                    + $"({meshes} baked meshes, {removed} originals removed, "
                    + $"{materials.Count} materials, {chunk} m grid) "
                    + $"combine {combineMs} ms, destroy {destroyMs} ms.");
        }

        /// <summary>
        /// A renderer that can be merged away without breaking something that needs to move,
        /// light up, or be switched off.
        /// </summary>
        private static bool Combinable(MeshRenderer r)
        {
            if (r == null || !r.enabled) return false;
            if (r.GetComponentInParent<Light>() != null) return false;
            if (r.GetComponent<SkinnedMeshRenderer>() != null) return false;
            if (r.GetComponentInParent<ParticleSystem>() != null) return false;

            // ANYTHING THAT LIGHTS UP STAYS ADDRESSABLE.
            //
            // Baking trades away the ability to change a thing at runtime, which is the whole
            // point of a chunker and exactly wrong for a window. A city where you watch who is
            // home needs its glass switchable by the hour, and a headlight needs to come on at
            // dusk. The pack marks these for us: _Night glass is an emissive warm orange and the
            // Emission materials are the lamps and headlights, so the material name is a
            // reliable signal about which renderers have to survive.
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                if (m.name.IndexOf("Night", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
                if (m.name.IndexOf("Emission", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            }
            return true;
        }
    }
}
