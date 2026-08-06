using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.Sim;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Where the walkable map is cut, on the town the GAME builds.
    ///
    /// WHY THIS EXISTS BESIDE WalkableDiagnostic, WHICH ASKS THE SAME QUESTION. That one lives in
    /// Core's test project and builds straight from city.txt, and it passes: city.txt on its own
    /// is one piece. The town the game builds is city.txt plus four passes that live in the Unity
    /// assembly - SurveyRoads, RuledAway, SeatOnSurvey, FillFromSurvey - and Core cannot see them,
    /// so a cut introduced by one of those is invisible to the guard that exists to catch it.
    ///
    /// The smoke test reported five regions the first time it was pointed at the real town. This
    /// is how to find out which pass did it and where.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.WalkableProbe.Run
    /// </summary>
    public static class WalkableProbe
    {
        [MenuItem("Noir/Probe The Walkable Map")]
        public static void Run()
        {
            try
            {
                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));

                // ONE PASS AT A TIME, so the report names the culprit rather than the symptom.
                Report("city.txt alone", null);
                Report("+ SurveyRoads", l => SurveyRoads.Apply(l));
                Report("+ RuledAway", l => { SurveyRoads.Apply(l); RuledAway.Apply(l); });
                Report("+ SeatOnSurvey", l =>
                {
                    SurveyRoads.Apply(l); RuledAway.Apply(l); SeatOnSurvey.Apply(l);
                });
                Report("+ FillFromSurvey  (what the game builds)", l =>
                {
                    SurveyRoads.Apply(l); RuledAway.Apply(l);
                    SeatOnSurvey.Apply(l); FillFromSurvey.Apply(l);
                });
            }
            catch (Exception ex)
            {
                Debug.LogError("[walkable] " + ex);
            }
            finally
            {
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
        }

        private static void Report(string label, Action<VillageLayout> passes)
        {
            var layout = VillageParser.Parse(ContentLoader.Read(VillageHost.MapFile));
            passes?.Invoke(layout);
            var world = WorldBuilder.Build(layout, VillageHost.Seed);
            var regions = new WalkableRegions(world.Grid);
            int largest = regions.Largest();

            Debug.Log($"[walkable] {label,-42} {regions.RegionCount} region(s), "
                    + $"{regions.SizeOf(largest):N0} in the town");

            if (regions.RegionCount <= 1) return;

            // Bounding box per cut-off region, and any door stranded in it. The box says where and
            // the size says what: a few hundred tiles is a building whose door is walled in, a few
            // thousand is a quarter of the town hanging off something impassable.
            var minX = new Dictionary<int, int>(); var maxX = new Dictionary<int, int>();
            var minY = new Dictionary<int, int>(); var maxY = new Dictionary<int, int>();
            var grid = world.Grid;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                int r = regions.RegionAt(x, y);
                if (r == WalkableRegions.None || r == largest) continue;
                if (!minX.ContainsKey(r)) { minX[r] = maxX[r] = x; minY[r] = maxY[r] = y; }
                else
                {
                    if (x < minX[r]) minX[r] = x; if (x > maxX[r]) maxX[r] = x;
                    if (y < minY[r]) minY[r] = y; if (y > maxY[r]) maxY[r] = y;
                }
            }

            foreach (var kv in minX)
            {
                int r = kv.Key;
                var who = new List<string>();
                foreach (var place in world.AllPlaces)
                {
                    if (place == null || !place.Door.IsValid) continue;
                    if (place.Bounds.X > maxX[r] || place.Bounds.X + place.Bounds.W < minX[r]) continue;
                    if (place.Bounds.Y > maxY[r] || place.Bounds.Y + place.Bounds.H < minY[r]) continue;
                    who.Add(place.Name.Length > 0 ? place.Name : place.Kind.ToString());
                    if (who.Count >= 4) break;
                }
                Debug.Log($"[walkable]     region {r,-3} {regions.SizeOf(r),7:N0} tiles  "
                        + $"{minX[r]},{minY[r]} to {maxX[r]},{maxY[r]}   {string.Join(" / ", who)}");
            }
        }
    }
}
