using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// Swaps city.txt's roads for the ones measured off the survey, when Content/roads.txt is
    /// present. Optional by design: delete the file and the town goes back to exactly what it
    /// was, which is the property that makes this safe to leave installed.
    ///
    /// WHY IT EXISTS. 32 of city.txt's 37 road lines are TWO POINTS - a straight line ruled from
    /// one edge of the map to the other. `road attica 30 0,1353 2099,1353` is the whole of
    /// Attica Street. The 2026-08-04 refit then slid each of those lines bodily onto the nearest
    /// parcel-free strip, which is the most a rigid shift can do and no help to a road whose
    /// SHAPE is wrong: Harrison turns 15.4 degrees at Benton and a straight line cannot.
    ///
    /// Content/roads.txt is built from Vermilion County's own centreline layer for the streets
    /// and from the parcel gaps themselves for the alleys - see docs/research/ROADS-FROM-SURVEY.md
    /// and tools/build-roads.py. Measured as the share of centreline running across private
    /// land, which for a public right of way should be zero: city.txt manages 9.0%, this 1.6%.
    ///
    /// CHICAGO STREET IS CARRIED OVER FROM city.txt UNCHANGED, and roads.txt already contains
    /// that copy. It is authored, the owner corrected it personally, and the measurement agrees
    /// with the ruling - the authored curve scores 0.0% and the county's own Route 1 line 3.4%.
    ///
    /// THE SAME PARSER. roads.txt is written in city.txt's own road syntax and goes through
    /// VillageParser, so a malformed file is a parse error rather than a silently odd town, and
    /// the width of every road is still pinned to its class by the parser.
    /// </summary>
    public static class SurveyRoads
    {
        public const string FileName = "roads.txt";

        /// <summary>Whether a survey road file is sitting there waiting to be used.</summary>
        public static bool Available
        {
            get
            {
                try { return File.Exists(Path.Combine(ContentLoader.Root, FileName)); }
                catch { return false; }
            }
        }

        /// <summary>
        /// Replace <paramref name="layout"/>'s roads with the surveyed ones, in place. Does
        /// nothing at all if the file is missing, unreadable or parses to no roads - a town with
        /// its old roads is a working town, and a town with none is not.
        /// </summary>
        public static bool Apply(VillageLayout layout)
        {
            if (layout == null) return false;

            string text;
            try { text = ContentLoader.Read(FileName); }
            catch (FileNotFoundException) { return false; }
            catch (IOException e)
            {
                Debug.LogWarning($"[roads] {FileName} unreadable, keeping city.txt's roads: {e.Message}");
                return false;
            }

            List<RoadRun> roads;
            try
            {
                roads = VillageParser.Parse(text).Roads;
            }
            catch (VillageParseException e)
            {
                Debug.LogError($"[roads] {FileName} did not parse, keeping city.txt's roads: {e.Message}");
                return false;
            }

            if (roads == null || roads.Count == 0)
            {
                Debug.LogWarning($"[roads] {FileName} holds no roads, keeping city.txt's.");
                return false;
            }

            int before = layout.Roads.Count;
            layout.Roads.Clear();
            layout.Roads.AddRange(roads);

            int alleys = 0;
            foreach (var r in roads)
                if (r.EffectiveClass == RoadClass.Alley) alleys++;

            Debug.Log($"[roads] survey network in use: {roads.Count} roads ({alleys} alleys) "
                    + $"from Content/{FileName}, replacing {before} from {VillageHost.MapFile}. "
                    + "Delete the file to go back.");
            return true;
        }
    }
}
