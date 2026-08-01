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
            string name = char.ToUpperInvariant(frontage.Name[0]) + frontage.Name.Substring(1);
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
