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
                int cx = Mathf.FloorToInt(at.x / Chunk), cz = Mathf.FloorToInt(at.z / Chunk);

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

            // Only now, or the combine reads meshes that have already gone.
            int removed = 0;
            foreach (var r in before)
            {
                if (r == null || !consumed.Contains(r.gameObject)) continue;
                if (r.transform.IsChildOf(baked.transform)) continue;
                Object.DestroyImmediate(r.gameObject);
                removed++;
            }

            int after = root.GetComponentsInChildren<MeshRenderer>().Length;
            Debug.Log($"[chunker] {before.Length} renderers -> {after} "
                    + $"({meshes} baked meshes, {removed} originals removed, "
                    + $"{materials.Count} materials).");
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
            return true;
        }
    }
}
