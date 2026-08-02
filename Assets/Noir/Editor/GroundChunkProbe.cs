using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// What the ground's chunk size actually costs, swept over the real map in one editor launch.
    ///
    /// WHY IT EXISTS. The greedy merge took the ground from 20.5M vertices to 576K and did not
    /// move the draw calls at all - 2,626 either way - because a draw call is one per (chunk,
    /// material present in that chunk), and merging tiles WITHIN a chunk cannot change which
    /// materials are in it. The only lever left is the number of chunks, and picking that number
    /// by eye is how MeshChunks.Size got its comment about "the two views that matter": a wide
    /// shot, where every chunk is on screen and the count is the whole bill, and a street-level
    /// wedge, where the chunk size is the whole saving. This measures both ends of that trade
    /// instead of arguing about it.
    ///
    /// It reports, per candidate size:
    ///   - draw calls, which is what a coarser grid is bought FOR;
    ///   - the worst single chunk, which is what a coarser grid COSTS - that is the bill for
    ///     having one corner of one chunk on screen, and the reason 64 m could not simply be
    ///     raised before the merge landed, when a single 64 m chunk was already four thousand
    ///     quads.
    ///
    /// Everything is read off the meshes that were actually emitted rather than off the builder's
    /// own counters, so it cannot report a number the GPU would not see.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.GroundChunkProbe.Run -logFile &lt;log&gt;
    /// </summary>
    public static class GroundChunkProbe
    {
        /// <summary>The sweep. MeshChunks.Size (64) is first so the log always carries the
        /// before-number next to the after-number rather than in a different session's
        /// scrollback.</summary>
        private static readonly int[] Candidates = { 64, 128, 192, 256, 384, 512 };

        [MenuItem("Noir/Probe Ground Chunk Size")]
        public static void Run()
        {
            PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
            var layout = VillageParser.Parse(ContentLoader.Read(VillageHost.MapFile));
            var world = WorldBuilder.Build(layout, VillageHost.Seed);

            Debug.Log($"[ground-chunks] {world.Width}x{world.Height} map, "
                    + $"{world.Width * world.Height:N0} tiles. "
                    + $"Currently building at {MeshChunks.GroundSize}m.");

            var rows = new List<string>();

            foreach (int size in Candidates)
            {
                var root = new GameObject($"GroundChunkProbe {size}");
                try
                {
                    VillageMesh.BuildGround(world, root.transform, size);

                    int chunks = 0, draws = 0, verts = 0, tris = 0;
                    int worstTris = 0, worstVerts = 0, worstDraws = 0;
                    string worstName = "-";

                    var ground = root.transform.Find("Ground");
                    foreach (Transform child in ground)
                    {
                        var mesh = child.GetComponent<MeshFilter>()?.sharedMesh;
                        if (mesh == null) continue;

                        int t = 0;
                        for (int s = 0; s < mesh.subMeshCount; s++)
                            t += (int)mesh.GetIndexCount(s) / 3;

                        verts += mesh.vertexCount;
                        tris += t;
                        draws += mesh.subMeshCount;

                        // The skirt is one always-drawn mesh by design (see BuildGround) and is
                        // not a chunk - counted in the totals, excluded from "worst chunk", or it
                        // would win every sweep on a technicality and say nothing about culling.
                        if (!child.name.StartsWith("Ground ")) continue;
                        chunks++;
                        if (t <= worstTris) continue;
                        worstTris = t;
                        worstVerts = mesh.vertexCount;
                        worstDraws = mesh.subMeshCount;
                        worstName = child.name;
                    }

                    rows.Add($"{size,6} {chunks,8} {draws,11:N0} {verts,12:N0} {tris,12:N0} "
                           + $"{worstTris,12:N0} {worstVerts,10:N0} {worstDraws,7}  {worstName}");
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }

            Debug.Log("[ground-chunks] swept:\n"
                    + "  size   chunks  draw calls     vertices    triangles "
                    + "worst chunk:  tris     verts  draws  where\n"
                    + string.Join("\n", rows));

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
