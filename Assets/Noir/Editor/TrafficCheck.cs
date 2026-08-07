using System;
using UnityEditor;
using UnityEngine;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Walks every metre of every lane and every turn, and reports anything that is not on
    /// asphalt.
    ///
    /// The one class of bug that has cost real time on this city is GEOMETRY: a lane measured off
    /// the centre of a tile rather than the centre of a road, a tile rotated off its own pivot, a
    /// carriageway hidden under the ground plane. None of it shows up in a compile and all of it
    /// shows up as a car on the pavement.
    ///
    /// A still cannot prove a car stops at a red light, but it does not need to prove where the
    /// lanes ARE - that is arithmetic, and arithmetic can be checked without pressing Play. So
    /// this samples the whole graph and measures each point against the measured half-width of
    /// the asphalt it should be on.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.TrafficCheck.Run -logFile &lt;log&gt;
    /// </summary>
    public static class TrafficCheck
    {
        /// <summary>Half a car's width, near enough. A lane centre this close to the kerb is a wheel on it.</summary>
        private const float HalfCar = 1.0f;

        [MenuItem("Noir/Check Traffic Geometry")]
        public static void Run()
        {
            GameObject root = null;
            int problems = 0;

            try
            {
                // The town as the game builds it. Every measurement below is taken against
                // world.Roads, and SurveyRoads MOVES those lines - so a town built without the
                // survey layer checks the geometry of roads that are not the ones being driven on.
                var world = TownPipeline.Build().World;

                root = new GameObject("TrafficCheck");
                var signals = CitySignals.Create(world, root.transform);
                var traffic = CityTraffic.Create(world, root.transform, signals);
                var graph = traffic.Graph;

                Debug.Log($"[check] {graph.Segments.Count} segments, {graph.Turns.Count} turns, "
                        + $"{graph.Entries.Count} entries.");

                // ---- 1. every lane stays inside its own asphalt -------------------------
                float worstLane = 0f;
                string worstWhere = "";

                foreach (var segment in graph.Segments)
                {
                    var line = world.Roads.Lines[segment.Line];
                    float half = CityStreets.Asphalt(line.Class);

                    for (float s = segment.FromS; s <= segment.ToS; s += 2f)
                    {
                        var at = traffic.PointOn(segment.Index, s);

                        // Only judge it where the road actually is: a lane runs off the edge of
                        // the map by design, and there is no asphalt out there to be on.
                        float along = LaneGraph.AlongOf(segment.Way, s);
                        if (along < 0f || along > (line.IsNorthSouth ? world.Height : world.Width))
                            continue;

                        float cross = line.IsNorthSouth ? at.x : -at.z;
                        float off = Mathf.Abs(cross - line.Centre);

                        if (off + HalfCar > half)
                        {
                            problems++;
                            if (off > worstLane)
                            {
                                worstLane = off;
                                worstWhere = $"{line.Name} ({line.Class}) lane {segment.Lane} "
                                           + $"{segment.Way}: {off:0.00}m from the centre line, "
                                           + $"asphalt reaches {half:0.00}m";
                            }
                        }
                    }
                }

                if (worstLane > 0f) Debug.LogError("[check] LANE OFF THE ASPHALT: " + worstWhere);
                else Debug.Log("[check] every lane is inside its own asphalt, "
                             + $"with {HalfCar:0.0}m of car either side.");

                // ---- 2. every turn stays inside its junction ----------------------------
                float worstTurn = 0f;
                string worstTurnWhere = "";

                for (int t = 0; t < graph.Turns.Count; t++)
                {
                    var turn = graph.Turns[t];
                    var junction = world.Roads.Junctions[turn.Junction];
                    float reach = junction.Reach;

                    for (float u = 0f; u <= 1.0001f; u += 0.05f)
                    {
                        var at = traffic.PointInTurn(t, u);
                        float dx = Mathf.Abs(at.x - junction.X);
                        float dy = Mathf.Abs(-at.z - junction.Y);
                        float out_ = Mathf.Max(dx, dy) - reach;

                        if (out_ > 0.01f && out_ > worstTurn)
                        {
                            worstTurn = out_;
                            worstTurnWhere = $"{turn.Kind} at junction {turn.Junction} "
                                           + $"({junction.X},{junction.Y}) strays {out_:0.00}m "
                                           + $"outside the {reach * 2f:0}m crossing";
                        }
                    }
                }

                // A turn leaves the junction square at its two ends by definition - it starts on
                // the stop line and finishes on the far side - so only a real excursion counts.
                if (worstTurn > 0.5f)
                {
                    problems++;
                    Debug.LogError("[check] TURN OUTSIDE THE JUNCTION: " + worstTurnWhere);
                }
                else Debug.Log("[check] every turn stays within its crossing.");

                // ---- 3. the signals cover every junction --------------------------------
                if (signals.Count != world.Roads.Junctions.Count)
                {
                    problems++;
                    Debug.LogError($"[check] {world.Roads.Junctions.Count} junctions but "
                                 + $"{signals.Count} signal nodes - traffic asks by junction "
                                 + "index, so these must be the same list.");
                }
                else Debug.Log($"[check] {signals.Count} junctions all signalled.");

                // ---- 4. nothing can drive into a wall -----------------------------------
                foreach (var segment in graph.Segments)
                    if (!segment.IsExit && graph.TurnsFrom(segment.Index).Count == 0)
                    {
                        problems++;
                        Debug.LogError($"[check] segment {segment.Index} arrives at junction "
                                     + $"{segment.ToJunction} with nowhere legal to go.");
                    }

                Debug.Log(problems == 0
                    ? "[check] VERDICT: the traffic geometry is sound."
                    : $"[check] VERDICT: {problems} problems.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[check] FAILED: " + ex);
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
