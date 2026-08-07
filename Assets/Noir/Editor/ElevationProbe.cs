using System.Text;
using UnityEditor;
using UnityEngine;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Is the real ground actually under the town, or is the map flat?
    ///
    /// WHY ASKING IS NOT SILLY. Rossville sits on glacial till plain: 24.5 metres of relief
    /// across 2100 x 2400 metres, which is a one percent grade. That is far too gentle to see in
    /// any normal view, so "the ground looks flat" is exactly what CORRECT elevation looks like
    /// here, and so is a bug that dropped it entirely. The two are indistinguishable by eye,
    /// which is the whole reason this reads the numbers instead.
    ///
    /// It checks three separate things, because they can fail independently:
    ///   1. the DATA loads and has the range Content/elevation.txt says it does
    ///   2. HeightAt actually varies across the map rather than returning a constant
    ///   3. the BUILT MESH carries it - vertices at different heights, not just a grid that
    ///      knows about them
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.ElevationProbe.Run
    /// </summary>
    public static class ElevationProbe
    {
        [MenuItem("Noir/Probe The Elevation")]
        public static void Run()
        {
            var log = new StringBuilder();
            log.AppendLine("[elev] Is the real ground under Rossville?");

            // ---- 1. the data ----
            float lo = float.MaxValue, hi = float.MinValue;
            const int step = 25;
            for (int y = 0; y < 2400; y += step)
            for (int x = 0; x < 2100; x += step)
            {
                float h = ElevationGrid.HeightAt(x, y);
                if (h < lo) lo = h;
                if (h > hi) hi = h;
            }

            log.AppendLine($"  sampled every {step}m over the whole map:");
            log.AppendLine($"     lowest  {lo:0.00}m");
            log.AppendLine($"     highest {hi:0.00}m");
            log.AppendLine($"     relief  {hi - lo:0.00}m");

            if (hi - lo < 0.01f)
            {
                log.AppendLine("  FLAT. HeightAt returns a constant - the elevation is NOT applied.");
            }
            else
            {
                log.AppendLine($"  varies, so the grid is loaded and being read.");
                log.AppendLine($"     that is a {100f * (hi - lo) / 2400f:0.00}% grade over the map's "
                             + "long axis - a till plain, which is what it should be.");
            }

            // ---- 2. a section across the town, so the shape is legible as numbers ----
            log.AppendLine("  a section west to east along Attica (y=1335), every 150m:");
            for (int x = 0; x <= 2100; x += 150)
                log.AppendLine($"     x={x,5}  {ElevationGrid.HeightAt(x, 1335):0.0}m");

            // ---- 3. does the BUILT MESH carry it ----
            //
            // The grid can be right and the mesh still flat: the ground is baked once at build
            // and nothing re-reads the grid afterwards, so this is a separate question.
            var built = TownPipeline.Build();
            var world = built.World;

            var root = new GameObject("ElevationProbe");
            try
            {
                VillageMesh.Build(world, root.transform, true);

                float meshLo = float.MaxValue, meshHi = float.MinValue;
                int verts = 0;
                foreach (var f in root.GetComponentsInChildren<MeshFilter>())
                {
                    if (f.sharedMesh == null) continue;
                    if (!f.gameObject.name.StartsWith("Ground")) continue;

                    foreach (var v in f.sharedMesh.vertices)
                    {
                        float wy = f.transform.TransformPoint(v).y;
                        if (wy < meshLo) meshLo = wy;
                        if (wy > meshHi) meshHi = wy;
                        verts++;
                    }
                }

                if (verts == 0)
                {
                    log.AppendLine("  MESH: no ground chunks found to measure.");
                }
                else
                {
                    log.AppendLine($"  the built ground mesh, {verts:N0} vertices:");
                    log.AppendLine($"     lowest  {meshLo:0.00}m");
                    log.AppendLine($"     highest {meshHi:0.00}m");
                    log.AppendLine($"     relief  {meshHi - meshLo:0.00}m");
                    log.AppendLine(meshHi - meshLo < 0.01f
                        ? "  THE MESH IS FLAT. The grid varies but the ground does not carry it."
                        : "  the mesh carries the relief. The real ground is under the town.");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            Debug.Log(log.ToString());
            System.IO.File.WriteAllText(
                System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..",
                                                                  "Logs", "elevation-probe.txt")),
                log.ToString());

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
