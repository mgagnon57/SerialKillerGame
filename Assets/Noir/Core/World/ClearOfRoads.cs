using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// MOVES ANY BUILDING THAT IS STANDING IN A ROAD ONTO THE NEAREST GROUND THAT IS NOT A ROAD.
    ///
    /// WHY THIS IS NEEDED AND WHY IT IS A PASS RATHER THAN AN EDIT. Fifty of the 373 buildings in
    /// Content/city.txt stand inside a surveyed road corridor. They are not scattered: they sit
    /// in columns.
    ///
    ///     place house 977,1705 …   "301 Stewart Ave"      road church 10 975,846 975,2104
    ///     place house 977,1744 …   "302 Stewart Ave"
    ///     place house 977,1619 …   "302 Gilbert Ave"
    ///
    /// Church Street's centreline IS x = 975, and those houses are authored at x = 977 - two
    /// metres from the middle of the road. The same column appears at x = 1163 against Summit and
    /// x = 1344 against Grove. These are corner houses whose position was authored from the
    /// STREET's coordinate instead of from a setback off it, back when city.txt's own ruled roads
    /// were the only roads there were. Then SurveyRoads replaced those 37 ruled lines with the
    /// county's 66 measured ones, the streets moved to where they really are, and the houses
    /// stayed where they were written.
    ///
    /// So this is not an authoring mistake to be corrected once. It is a standing disagreement
    /// between a hand-authored file and a survey, and SOURCES-OF-TRUTH.md is explicit about which
    /// wins: the survey does, and the thing that was derived from the old roads gets re-derived.
    /// A filter pass keeps that property - regenerate Content/roads.txt and the town follows it,
    /// with nothing to keep in step by hand. Editing city.txt would freeze today's answer.
    ///
    /// WHAT IT WILL NOT DO. It never moves a building that carries a measured Outline. Those come
    /// from <c>SeatOnSurvey</c> and are the surveyed truth about where a building stood; if one of
    /// those disagrees with a road, the RIGHT answer is to refuse the seating and say so, which is
    /// what SeatOnSurvey now does. Nudging a measured building would be inventing a fact.
    /// </summary>
    public static class ClearOfRoads
    {
        /// <summary>
        /// How far into a corridor a building may reach before it is moved, in metres.
        /// Matches SeatOnSurvey's own tolerance: below a kerbstone's width, nobody can see it.
        /// </summary>
        public const float Tolerance = 0.5f;

        /// <summary>How far the search will push a building before giving up, in metres.</summary>
        public const int Reach = 24;

        /// <summary>
        /// What a move is worth. A building that clears the road but lands on top of its
        /// neighbour has not been fixed, so the search rejects any position that overlaps another
        /// place - and prefers the SHORTEST move that works, so a house ends up on its own side
        /// of the street rather than across it.
        /// </summary>
        public static int Apply(VillageLayout layout) => Apply(layout, out _);

        public static int Apply(VillageLayout layout, out int stuck)
        {
            stuck = 0;
            if (layout == null) return 0;

            var table = PlaceKindTable.Current;
            var buildings = new List<PlaceSpec>();
            foreach (var p in layout.Places)
                if (table.Row(p.Kind).IsBuilding) buildings.Add(p);

            int moved = 0;
            foreach (var p in buildings)
            {
                // Measured buildings are not ours to move - see the header.
                if (p.Outline != null && p.Outline.Length >= 3) continue;

                if (RoadCorridor.WorstPenetration(layout, p.Bounds) <= Tolerance) continue;

                if (TryClear(layout, buildings, p, out var moveTo))
                {
                    var was = p.Bounds;
                    p.Bounds = moveTo;
                    if (p.Door.IsValid)
                        p.Door = new Tile(p.Door.X + (moveTo.X - was.X), p.Door.Y + (moveTo.Y - was.Y));
                    moved++;
                }
                else stuck++;
            }
            return moved;
        }

        /// <summary>
        /// The nearest clear ground, searched outward a metre at a time on the four axes.
        ///
        /// AXES ONLY, DELIBERATELY. Rossville is a plat: its blocks are rectangles and its houses
        /// face the street square-on. Moving a house diagonally to save a metre would turn a row
        /// of front doors into a zigzag, which is worse than the metre is worth.
        /// </summary>
        private static bool TryClear(VillageLayout layout, List<PlaceSpec> all, PlaceSpec p,
                                     out TileRect best)
        {
            best = p.Bounds;
            for (int step = 1; step <= Reach; step++)
            {
                for (int dir = 0; dir < 4; dir++)
                {
                    int dx = dir == 0 ? step : dir == 1 ? -step : 0;
                    int dy = dir == 2 ? step : dir == 3 ? -step : 0;

                    var box = new TileRect(p.Bounds.X + dx, p.Bounds.Y + dy, p.Bounds.W, p.Bounds.H);
                    if (box.X < 0 || box.Y < 0) continue;
                    if (box.X + box.W > layout.Width || box.Y + box.H > layout.Height) continue;
                    if (RoadCorridor.WorstPenetration(layout, box) > 0f) continue;
                    if (HitsAnother(all, p, box)) continue;

                    best = box;
                    return true;
                }
            }
            return false;
        }

        private static bool HitsAnother(List<PlaceSpec> all, PlaceSpec self, TileRect box)
        {
            foreach (var o in all)
            {
                if (ReferenceEquals(o, self)) continue;
                if (box.Overlaps(o.Bounds)) return true;
            }
            return false;
        }
    }
}
