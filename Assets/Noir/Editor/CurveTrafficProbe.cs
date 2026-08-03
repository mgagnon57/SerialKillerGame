using System.Text;
using UnityEditor;
using UnityEngine;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Where do the lanes on a bending road sit, against the terrain that is supposed to be under
    /// them?
    ///
    /// The PlayMode suite says a van at the south end of Chicago Street was "582.50m past the
    /// asphalt". That number can mean two very different things - the LANE is in the wrong place,
    /// or the lane is right and the ROAD SURFACE was never rasterised under it - and guessing
    /// which is how an afternoon disappears. This walks the road and asks both questions at every
    /// step.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.CurveTrafficProbe.Run
    /// </summary>
    public static class CurveTrafficProbe
    {
        [MenuItem("Noir/Probe The Curve's Traffic")]
        public static void Run()
        {
            var log = new StringBuilder();

            PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
            var world = WorldBuilder.Build(
                VillageParser.Parse(ContentLoader.Read(VillageHost.MapFile)), VillageHost.Seed);

            RoadLine chicago = null;
            foreach (var l in world.Roads.Lines) if (l.Name == "chicago") chicago = l;
            if (chicago == null) { Debug.LogError("no chicago"); return; }

            var path = chicago.Path;
            log.AppendLine($"[curve] chicago: straight={chicago.IsStraight} "
                         + $"From={chicago.From} To={chicago.To} pathLen={path.Length:0.0}");
            log.AppendLine("[curve] arc -> where the path puts it -> is that tile Terrain.Road?");

            int off = 0, on = 0;
            for (float arc = 0f; arc <= path.Length; arc += 100f)
            {
                var at = path.PointAt(arc);
                int tx = Mathf.FloorToInt(at.X), ty = Mathf.FloorToInt(at.Y);
                bool road = tx >= 0 && ty >= 0 && tx < world.Width && ty < world.Height
                         && world.Grid.TerrainAt(tx, ty) == Noir.Core.World.Terrain.Road;
                if (road) on++; else off++;

                log.AppendLine($"     arc {arc,7:0}  ->  ({at.X,6:0.0}, {at.Y,6:0.0})   "
                             + (road ? "road" : "NOT ROAD"));
            }
            log.AppendLine($"[curve] centre line: {on} samples on rasterised road, {off} not.");

            // The lane graph's own idea of where its segments are, which is the number the
            // traffic actually drives. If `along` were a village coordinate rather than
            // From + arc length, this is where it would show.
            var graph = new LaneGraph(world.Roads, world.Width, world.Height);
            int chicagoIndex = -1;
            for (int i = 0; i < world.Roads.Lines.Count; i++)
                if (ReferenceEquals(world.Roads.Lines[i], chicago)) chicagoIndex = i;

            log.AppendLine("[curve] the lane segments on it, in travel coordinates:");
            int shown = 0;
            foreach (var seg in graph.Segments)
            {
                if (seg.Line != chicagoIndex || shown >= 8) continue;
                float fromAlong = LaneGraph.AlongOf(seg.Way, seg.FromS);
                float toAlong = LaneGraph.AlongOf(seg.Way, seg.ToS);
                log.AppendLine($"     seg{seg.Index,4} way={seg.Way,-5} lane={seg.Lane} "
                             + $"along {fromAlong,8:0.0} .. {toAlong,8:0.0}  "
                             + $"(arc {fromAlong - chicago.From,8:0.0} .. {toAlong - chicago.From,8:0.0})");
                shown++;
            }
            log.AppendLine($"[curve] a segment ending past arc {path.Length:0} is off the end of "
                         + "the path, and PointAt clamps there rather than running on.");

            Debug.Log(log.ToString());
            System.IO.File.WriteAllText(
                System.IO.Path.GetFullPath(System.IO.Path.Combine(
                    Application.dataPath, "..", "Logs", "curve-traffic-probe.txt")),
                log.ToString());

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
