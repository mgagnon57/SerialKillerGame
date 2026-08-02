using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// A rough address for a lot that has no house on it: which street it fronts, and which
    /// hundred block, reproducing relay-rossville.py's own numbering (block count out from
    /// Chicago Street, times a hundred) rather than inventing a different convention.
    ///
    /// This is an ESTIMATE for a plot nothing was ever addressed on - the real answer is "no
    /// address exists", but "somewhere in the 400 block of Holmes Ave" is what a person standing
    /// on that lot would actually say, and is more useful than nothing.
    /// </summary>
    public static class StreetAddressing
    {
        // Chicago St x Attica St - the same crossing everything else is measured from.
        private const float CX = 750f, CY = 1335f;

        /// <summary>
        /// The street's REAL name at a point, where that differs from the RoadLine's own
        /// internal identifier. Five roads change name partway along their length - Maple/Park,
        /// Gilbert/Perry, Stewart/Stufflebeam and McKibben/McKibbin split at Route 1, Ann/Smith
        /// splits at Attica - confirmed by Vermilion County's own tax address PropertyAddress
        /// field on 2026-08-01, except McKibbin west, kept from an earlier research pass with no
        /// addressed lot on that side to confirm it.
        ///
        /// APPLIED HERE, BY POSITION, RATHER THAN IN THE ROAD NETWORK ITSELF. Splitting the
        /// actual RoadLine to match cost the lane graph its straight-through turn at the
        /// junction where the two halves met: a car crossing there had no wired continuation
        /// from one line to the other and stood with clear road ahead
        /// (NoCarWaitsForeverAtTheHeadOfAClearQueue caught it). The name really does change; the
        /// road it changes on does not.
        /// </summary>
        public static string RealName(string internalName, Vector2 at)
        {
            switch (internalName)
            {
                case "maple":    return at.x < CX ? "park" : "maple";
                case "gilbert":  return at.x < CX ? "perry" : "gilbert";
                case "stewart":  return at.x < CX ? "stufflebeam" : "stewart";
                case "mckibben": return at.x < CX ? "mckibbin" : "mckibben";
                case "ann":      return at.y < CY ? "smith" : "ann";
                default:         return internalName;
            }
        }

        public static string Estimate(WorldModel world, Vector2 at)
        {
            if (world?.Roads?.Lines == null) return null;

            RoadLine frontage = null;
            float best = float.MaxValue;

            foreach (var line in world.Roads.Lines)
            {
                if (line == null || line.Class == RoadClass.Track) continue;
                if (string.IsNullOrEmpty(line.Name)) continue;

                float along = line.IsNorthSouth ? at.y : at.x;
                if (along < line.From - 40f || along > line.To + 40f) continue;

                float across = line.IsNorthSouth ? at.x : at.y;
                float dist = Mathf.Abs(across - line.Centre);
                if (dist < best) { best = dist; frontage = line; }
            }
            if (frontage == null) return null;

            int block = BlockNumber(world.Roads.Lines, frontage, at);
            string real = RealName(frontage.Name, at);
            string name = char.ToUpperInvariant(real[0]) + real.Substring(1);
            return block > 0 ? $"{block} block of {name} Ave" : name + " Ave";
        }

        /// <summary>
        /// How many streets out from Chicago Street the point is, times a hundred - the same
        /// arithmetic relay-rossville.py's blocks() uses, run in reverse from the live road
        /// network rather than from the generator's own working data.
        /// </summary>
        private static int BlockNumber(IReadOnlyList<RoadLine> lines, RoadLine frontage, Vector2 at)
        {
            // The cross streets are whichever axis the frontage road is NOT on.
            var crossCentres = new List<float>();
            float chicago = float.NaN;
            foreach (var line in lines)
            {
                if (line == null || line.Class == RoadClass.Track) continue;
                if (line.IsNorthSouth == frontage.IsNorthSouth) continue;
                if (string.IsNullOrEmpty(line.Name)) continue;

                crossCentres.Add(line.Centre);
                if (line.Name == "chicago") chicago = line.Centre;
            }
            if (float.IsNaN(chicago) || crossCentres.Count == 0) return 0;

            crossCentres.Sort();
            float here = frontage.IsNorthSouth ? at.y : at.x;

            int chiIndex = crossCentres.IndexOf(chicago);
            int hereIndex = 0;
            while (hereIndex < crossCentres.Count && crossCentres[hereIndex] < here) hereIndex++;

            return System.Math.Abs(hereIndex - chiIndex) * 100;
        }
    }
}
