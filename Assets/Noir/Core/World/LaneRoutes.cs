using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// A drive route over the lane graph — the planner CityTraffic.Graph's "the bus routes
    /// will want the same one" comment has been waiting for. Ambient traffic stays a
    /// memoryless wander; this answers the one question it cannot: how to get from THIS
    /// lane to THAT one. Deterministic, no RNG, Sqrt only.
    /// </summary>
    public static class LaneRoutes
    {
        /// <summary>
        /// Dijkstra over segments; cost is segment length (turn arcs are a junction wide and
        /// near-constant, so they cancel). On success `turnsOut` holds LaneTurn indices in
        /// driving order — empty when from == to. False when no chain of legal turns joins them.
        /// </summary>
        public static bool Plan(LaneGraph graph, int fromSegment, int toSegment, List<int> turnsOut)
        {
            turnsOut.Clear();
            if (fromSegment == toSegment) return true;

            int n = graph.Segments.Count;
            var best = new float[n];
            var via = new int[n];              // the turn that reached each segment, -1 unreached
            for (int i = 0; i < n; i++) { best[i] = float.MaxValue; via[i] = -1; }
            best[fromSegment] = 0f;

            // A few hundred segments: a plain scan-for-minimum is simpler than a heap and
            // costs nothing at this size.
            var done = new bool[n];
            for (int round = 0; round < n; round++)
            {
                int at = -1; float low = float.MaxValue;
                for (int i = 0; i < n; i++)
                    if (!done[i] && best[i] < low) { low = best[i]; at = i; }
                if (at < 0) break;
                done[at] = true;
                if (at == toSegment) break;

                foreach (int t in graph.TurnsFrom(at))
                {
                    int next = graph.Turns[t].To;
                    float cost = best[at] + graph.Segments[next].Length;
                    if (cost < best[next]) { best[next] = cost; via[next] = t; }
                }
            }

            if (via[toSegment] < 0) return false;
            for (int at = toSegment; at != fromSegment; at = graph.Turns[via[at]].From)
                turnsOut.Add(via[at]);
            turnsOut.Reverse();
            return true;
        }

        /// <summary>
        /// The lane segment beside a village-space point, and the travel coordinate of the
        /// nearest spot on it. FromS/ToS are travel-signed AXIS coordinates while
        /// RoadPath.Project returns ARC length — the conversion goes arc → point → axis
        /// coordinate → TravelOf, or the two disagree on every curve.
        /// </summary>
        public static bool NearestSegment(LaneGraph graph, RoadNetwork roads, Vec2 point,
                                          out int segment, out float s)
        {
            segment = -1; s = 0f;
            float nearest = float.MaxValue;
            for (int i = 0; i < graph.Segments.Count; i++)
            {
                var seg = graph.Segments[i];
                var line = roads.Lines[seg.Line];
                if (line.Path == null) continue;

                var (arc, lateral) = line.Path.Project(point);
                var on = line.Path.PointAt(arc);
                float axis = line.IsNorthSouth ? on.Y : on.X;
                float travel = LaneGraph.TravelOf(seg.Way, axis);
                if (travel < seg.FromS || travel > seg.ToS) continue;

                float d = lateral < 0f ? -lateral : lateral;
                if (d >= nearest) continue;
                nearest = d; segment = i; s = travel;
            }
            return segment >= 0;
        }
    }
}
