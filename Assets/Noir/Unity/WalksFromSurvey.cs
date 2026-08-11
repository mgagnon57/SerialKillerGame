using UnityEngine;
using Noir.Core.Survey;
using Noir.Core.World;
using System.Collections.Generic;

namespace Noir.Unity
{
    /// <summary>
    /// THE SIDEWALKS THE OWNER REMEMBERS, LAID AS GROUND SOMEBODY CAN WALK ON.
    ///
    /// <c>Content/roads-1991.txt</c> has been read since it was written, and until 2026-08-11 the
    /// only thing done with a walk ruling was to PAINT IT PINK on the survey plan - see
    /// RoadCentrelines, which is a drawing. The tile grid never heard about it, so the sidewalks
    /// existed on the map of the town and not in the town, and a person crossing Rossville had
    /// nothing better underfoot than the road.
    ///
    /// THE OFFSET CONVENTION HERE MUST MATCH RoadCentrelines EXACTLY. The push is (-dy, dx), so
    /// on a road running north it points WEST and on one running east it points north; that is
    /// also the convention the browser map draws by, and the owner rules on which side a walk was
    /// by looking at that drawing. If these two disagree, the walk gets laid on the opposite side
    /// of the street from the one he said - which no test can see, because a sidewalk on the wrong
    /// side is a perfectly good sidewalk.
    ///
    /// WHAT IS RULED SO FAR IS FOUR BLOCKS - two on Chicago and two on Attica. Thirty-five of the
    /// thirty-eight walk rulings are `none`, and those are the alleys. Every residential street is
    /// UNRULED, which the file is explicit is not the same as `none`. So this pass is downtown
    /// only by construction, and the town-wide answer to people walking in the road is the verge
    /// (TileFlags.Verge), not this. It will cover more of the town exactly as fast as he rules on
    /// more of it in the browser map, with no further code.
    /// </summary>
    public static class WalksFromSurvey
    {
        /// <summary>Sampled along the kerb at half a metre, which is under a tile, so a walk comes
        /// out joined up rather than as a dotted line of stamps on the diagonals.</summary>
        private const float Pitch = 0.5f;

        public static int Apply(WorldModel world)
        {
            var grid = world.Grid;
            int laid = 0, blocks = 0;

            // Which run of its own name each line is: five streets are two runs in the survey and
            // their block offsets each start again at zero. Same bookkeeping as RoadCentrelines,
            // and it has to be, because it is what RoadRulings.Block.Line is counted against.
            var lineIndex = new Dictionary<RoadLine, int>();
            var seenName = new Dictionary<string, int>();
            foreach (var l in world.Roads.Lines)
            {
                if (l == null) continue;
                seenName.TryGetValue(l.Name, out int already);
                lineIndex[l] = already;
                seenName[l.Name] = already + 1;
            }

            foreach (var line in world.Roads.Lines)
            {
                if (line == null || line.Path == null) continue;
                if (line.Class == RoadClass.Alley) continue;   // ruled `none`, every one of them
                int myLine = lineIndex.TryGetValue(line, out int li) ? li : 0;

                float half = line.Width / 2f;
                bool hasVerge = line.Easement > line.Width + 0.5f;
                float row = line.Easement / 2f;

                // Against the lot line rather than against the kerb - that is the side of the
                // verge a walk sits on, and it is what the easement was measured for.
                float walkOff = hasVerge ? Mathf.Max(half + 1f, row - 1.3f) : half + 1f;

                bool anyHere = false;
                var prev = line.Path.PointAt(0f);

                for (float d = Pitch; d <= line.Path.Length; d += Pitch)
                {
                    var here = line.Path.PointAt(Mathf.Min(d, line.Path.Length));
                    var a = new Vector2(prev.X, prev.Y);
                    var b = new Vector2(here.X, here.Y);
                    prev = here;

                    var walk = RoadRulings.WalkAt(line.Name, myLine, d);
                    if (walk == RoadRulings.Walk.Unruled || walk == RoadRulings.Walk.None) continue;

                    var dir = b - a;
                    if (dir.sqrMagnitude < 1e-8f) continue;
                    var push = new Vector2(-dir.y, dir.x) / dir.magnitude;

                    if (walk == RoadRulings.Walk.Both)
                    {
                        laid += Stamp(grid, b + push * walkOff);
                        laid += Stamp(grid, b - push * walkOff);
                    }
                    else
                    {
                        bool ns = line.IsNorthSouth;
                        float sign = ns ? (walk == RoadRulings.Walk.East ? -1f : 1f)
                                        : (walk == RoadRulings.Walk.North ? 1f : -1f);
                        laid += Stamp(grid, b + push * (walkOff * sign));
                    }
                    anyHere = true;
                }

                if (anyHere) blocks++;
            }

            Debug.Log($"[walks] {laid:N0} tiles of sidewalk laid on {blocks} ruled street run(s) "
                    + $"from {RoadRulings.FileName}.");

            return laid;
        }

        /// <summary>
        /// A 2x2 of tiles at the sample point, so consecutive samples overlap and the walk is
        /// continuous. Laid ONLY over plain grass or verge: the easement runs through ponds, rail
        /// bed, yards and the odd building corner, and a walk that overwrote those would move the
        /// town about to tidy up its pedestrians.
        ///
        /// Marked Path AND Verge. Path is what makes it the cheapest ground in town; Verge is what
        /// keeps LotsFromSurvey's hands off it, so a lot whose polygon overhangs the right of way
        /// cannot charge trespass for walking down the public footway outside it.
        /// </summary>
        private static int Stamp(TileGrid grid, Vector2 at)
        {
            int laid = 0;
            int x0 = Mathf.FloorToInt(at.x), y0 = Mathf.FloorToInt(at.y);

            for (int y = y0; y <= y0 + 1; y++)
            for (int x = x0; x <= x0 + 1; x++)
            {
                if (!grid.InBounds(x, y)) continue;

                // Qualified: UnityEngine has a Terrain too, and CLAUDE.md names it as one of the
                // types never to collide with. `using Noir.Core.World` is not enough here.
                var t = grid.TerrainAt(x, y);
                if (t != Noir.Core.World.Terrain.Grass) continue;

                var f = grid.FlagsAt(x, y);
                if (f != TileFlags.Walkable && f != (TileFlags.Walkable | TileFlags.Verge)) continue;

                grid.Set(x, y, Noir.Core.World.Terrain.Path,
                         TileFlags.Walkable | TileFlags.Path | TileFlags.Verge);
                laid++;
            }
            return laid;
        }
    }
}
