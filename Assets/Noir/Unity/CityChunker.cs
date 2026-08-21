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
    /// whole of Rossville in 1,835 renderers by combining its procedural geometry on a grid, and
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

            // Instance ids are reused across a domain reload, so a cache that outlives one bake
            // would answer for an object that no longer exists.
            _prefabHeight.Clear();

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
            int unreadable = 0; string unreadableFirst = null;

            foreach (var r in before)
            {
                if (!Combinable(r, root)) continue;

                var f = r.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null) continue;
                if (!f.sharedMesh.isReadable)
                {
                    // ONCE, NAMED, AND AT THE END - not one line per renderer. This warned inside
                    // the loop, and the loop runs over every renderer in the town: measured, that
                    // was 3,503 identical lines in a single PlayMode log before MeshReadable was
                    // run, plus 1,390 more once the people were built. Four thousand nine hundred
                    // warnings is not a warning, it is a wall to scroll past, and the one line
                    // that mattered was somewhere inside it.
                    unreadable++;
                    if (unreadableFirst == null) unreadableFirst = r.name;
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

            // AN ERROR, NOT A WARNING, because the consequence is a town that quietly does not
            // bake: every one of these keeps its own draw call, and the whole point of this pass
            // is bringing a city of bought models down near the village's 1,835 renderers.
            if (unreadable > 0)
                Debug.LogError($"[chunker] {unreadable} renderer(s) are NOT Read/Write enabled and "
                    + $"were left unbaked - first: '{unreadableFirst}'. Mesh.CombineMeshes cannot "
                    + "read them. Run Noir/Make City Meshes Readable; Assets/polyperfect is "
                    + "gitignored, so this is the normal state of a fresh clone.");
        }

        /// <summary>How tall the prefab a renderer belongs to is, cached per prefab instance so
        /// a tree with forty leaf clusters is measured once and not forty times.</summary>
        private static readonly Dictionary<int, float> _prefabHeight = new Dictionary<int, float>();

        /// <summary>
        /// The line between something that survives being combined and something that does not,
        /// in metres.
        ///
        /// IT WAS 1 m AND THAT WAS TOO LOW. The height census below is a census of PREFABS, and
        /// it was read as if it were a census of failures: only the under-1 m band had been
        /// LOOKED at, and "a tree's leaf cluster survives the combine" was generalised from a
        /// single capture of a single `Leaves_Bunch`. The owner's screenshot settled it - flat
        /// bright-green splats lying across the pavement where the bushes should be, with 861
        /// baked chunks still carrying a SpeedTree material.
        ///
        /// A bush is a ball of crossed cards. It is structurally the same object as a grass tuft
        /// and nothing like a poplar, so it fails the combine for exactly the same reason. 4 m
        /// spares grass, flowers, bushes and saplings and still bakes the canopy, which DID come
        /// through the combine looking like a tree - checked at street level, not inferred.
        /// </summary>
        /// ⚠ AND IT IS BACK TO 1 m. Owner's ruling 2026-08-10, taken with the consequence
        /// stated in front of him: 4 m costs 14,833 un-baked renderers against 5,896, and he
        /// chose the cheaper line knowing the bushes stay as flat splats. Do not move this back
        /// without asking him - the trade is his, not a defect to re-fix.
        private const float TuftHeight = 1f;

        /// <summary>
        /// Is this renderer part of something growing on the ground rather than over it?
        ///
        /// Asked of the whole PREFAB and not of the renderer, because a tree's leaf cluster is
        /// the same size as a grass tuft and only the thing it belongs to tells them apart.
        ///
        /// THE PREFAB IS THE DIRECT CHILD OF THE BAKE ROOT, which is why `root` is threaded down
        /// here rather than the walk stopping on some property of the transform. The first
        /// version stopped at "a parent with no renderer and no MonoBehaviour", and the layer
        /// containers are bare `new GameObject("CityGreenery")` nodes with neither - so the walk
        /// ran all the way to `Rossville`, measured the height of the entire town, and concluded
        /// that nothing anywhere was ground foliage. It baked exactly as before and the count
        /// said so: 1,348 "spared" renderers, every one of them named `Chunk_*`.
        /// </summary>
        private static bool IsGroundFoliage(MeshRenderer r, GameObject root)
        {
            Transform stop = root.transform;
            Transform top = r.transform;
            while (top.parent != null && top.parent != stop
                   && top.parent.GetComponent<MeshRenderer>() == null)
                top = top.parent;

            int key = top.GetInstanceID();
            if (!_prefabHeight.TryGetValue(key, out float height))
            {
                var parts = top.GetComponentsInChildren<MeshRenderer>(true);
                if (parts.Length == 0) height = 0f;
                else
                {
                    var b = parts[0].bounds;
                    for (int i = 1; i < parts.Length; i++) b.Encapsulate(parts[i].bounds);
                    height = b.size.y;
                }
                _prefabHeight[key] = height;
            }

            return height < TuftHeight;
        }

        /// <summary>
        /// A renderer that can be merged away without breaking something that needs to move,
        /// light up, or be switched off.
        /// </summary>
        private static bool Combinable(MeshRenderer r, GameObject root)
        {
            if (r == null || !r.enabled) return false;
            if (r.GetComponentInParent<Light>() != null) return false;
            if (r.GetComponent<SkinnedMeshRenderer>() != null) return false;
            if (r.GetComponentInParent<ParticleSystem>() != null) return false;

            // A DOOR LEAF HANGS ON A HINGE SO IT CAN SWING, AND A BAKED DOOR CANNOT. Frontage
            // parents every swinging leaf under an empty pivot named "hinge" and registers that
            // pivot with CityDoors; baking the leaf freezes its triangles into a chunk at the
            // shut pose and destroys the GameObject, leaving CityDoors rotating a childless
            // transform. That happened, to all 589 doors at once, and nothing could see it -
            // every test reads the angle bookkeeping, which needs no leaf. The parent's name is
            // the same kind of signal the material names below are: nothing else in the town is
            // called "hinge", and CityDoors.Leafless() plus a PlayMode gate now fail loudly if
            // this exemption ever stops matching.
            //
            // ANCESTORS, NOT JUST THE PARENT, since 2026-08-18: an owner model's OBJ import
            // can put the renderer a level below the named group (hinge -> door_front_slab ->
            // mesh), and the direct-parent check baked three of 408's four leaves on the
            // fix's very first run. Bounded walk - a leaf is never more than a few levels
            // under its pivot.
            for (var up = r.transform.parent; up != null; up = up.parent)
                if (up.name == "hinge") return false;

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

                // SPEEDTREE CANNOT BE COMBINED, AND THE TOWN HAD THOUSANDS OF DEAD SHARDS TO
                // PROVE IT.
                //
                // `Nature/Grass`, `Nature/Flowers` and `Nature/Bushes` are SpeedTree assets -
                // shader `Universal Render Pipeline/Nature/SpeedTree7` - and SpeedTree does its
                // work in the VERTEX shader off data that lives in the extra UV channels and the
                // vertex colours: which way a leaf card faces, how it bends, how the wind moves
                // it. `Mesh.CombineMeshes` carries positions, normals, tangents and UV0. It does
                // not carry the rest, so every tuft in Rossville came out of the bake as a flat
                // grey plate lying on the lawn, and there were 3,092 chunks' worth of them.
                //
                // PROVED RATHER THAN REASONED: six of the same prefabs instantiated un-baked ten
                // feet from the baked ones render as proper grass clumps. Same prefab, same
                // material, same light - the only difference is the combine.
                //
                // The cost is real and is printed by the [chunker] line below: these keep their
                // own renderers. They are all instances of a handful of meshes and materials, so
                // GPU instancing is the thing to reach for if it hurts, NOT the combiner.
                //
                // GROUND FOLIAGE ONLY, AND THE HEIGHT IS WHERE THE LINE FALLS.
                //
                // Sparing every SpeedTree renderer costs 21,180 of them, because a tree is made
                // of SpeedTree parts too and the town has thousands of trees. Measured on the
                // live town, by the height of the PREFAB each renderer belongs to:
                //
                //     under 1 m   6,858   Grass_*, Flower_*, Leaves_Bunch_*, the cabbage bushes
                //     1-2 m       4,108   Bush_Yew_*, Bush_Knee_Timber_*
                //     2-4 m       3,867   Bush_Round_*, Bush_Shrub_*, Tree_*_Sapling
                //     over 4 m    6,347   Tree_Oak_*, Tree_Poplar_*, Tree_Willow_*
                //
                // ⚠ THE LINE WAS 1 m FOR ONE DAY AND IT WAS WRONG. Everything under 4 m is a
                // ball of crossed cards - a bush is the same object as a grass tuft, only bigger
                // - and all of it comes out of the combine flat. Only the canopy survives, and
                // that one WAS looked at rather than assumed. Owner's ruling 2026-08-10: spare
                // the ground, keep baking the canopy; the line moved when his screenshot showed
                // the bushes lying flat across the pavement. 14,833 rather than 21,180.
                if (m.shader != null &&
                    m.shader.name.IndexOf("SpeedTree", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return !IsGroundFoliage(r, root);
            }
            return true;
        }
    }
}
