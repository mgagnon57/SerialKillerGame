using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Looks over the whole map for the faults that show up as "that looks wrong" and reports
    /// them with coordinates.
    ///
    /// Written because a tree was standing in a road. Eyeballing renders finds the glaring ones
    /// and misses the rest, and a fault that can be stated can be tested for - so each of these
    /// is a rule the map is supposed to obey, checked over every tile or every place rather than
    /// over whatever happened to be in frame.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.MapAudit.Run -logFile &lt;log&gt;
    /// </summary>
    public static class MapAudit
    {
        private const int Report = 8;      // examples per fault, so the log stays readable

        [MenuItem("Noir/Audit the Map")]
        public static void Run()
        {
            int faults = 0;

            try
            {
                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
                var layout = VillageParser.Parse(ContentLoader.Read("city.txt"));
                var world = WorldBuilder.Build(layout, VillageHost.Seed);
                var kinds = PlaceKindTable.Current;

                Debug.Log($"[audit] {world.Width}x{world.Height}, {world.PlaceCount} places, "
                        + $"{world.Roads.Lines.Count} roads, {world.AllProps.Count} props.");

                // ---- 1. anything growing in the carriageway ----------------------------
                //
                // PropGenerator has no case for Road, so a prop on one means the tile was NOT
                // road when the props were generated - which happens when an open place stamps
                // its own ground over a road it overlaps. That is the fault that put trees
                // across the roads.
                var onRoad = new List<string>();
                foreach (var prop in world.AllProps)
                    if (world.Grid.TerrainAt(prop.At.X, prop.At.Y) == Noir.Core.World.Terrain.Road)
                        onRoad.Add($"{prop.Kind} at {prop.At.X},{prop.At.Y}");

                faults += Say("props standing in a road", onRoad);

                // ---- 2. a place laid over a road ---------------------------------------
                //
                // Painted in order: terrain, then roads, then places - and an OPEN place stamps
                // its ground last of all. So a lot that overlaps a corridor quietly erases the
                // road under it, which is what the Elevated once did to Second Street.
                // A BUILDING in a road is checked too, and is worse: it does not erase the road,
                // it stands in it.
                var over = new List<string>();
                foreach (var place in world.AllPlaces)
                {
                    var row = kinds.Row(place.Kind);
                    if (!row.IsBuilding && row.Ground == Noir.Core.World.Terrain.Road)
                        continue;                          // the railway, authored over a street

                    foreach (var line in world.Roads.Lines)
                    {
                        if (!line.IsStraight) continue;
                        if (!Overlaps(place.Bounds, line, out float bite)) continue;
                        over.Add($"'{place.Name}' ({row.Name}) at {place.Bounds} "
                               + $"lies {bite:0}m into {line.Name}"
                               + (row.IsBuilding ? " - and STANDS IN IT" : ""));
                    }
                }
                faults += Say("places laid over a road", over);

                // ---- 3. two places on the same ground ----------------------------------
                var clashes = new List<string>();
                var all = world.AllPlaces;
                for (int a = 0; a < all.Count; a++)
                for (int b = a + 1; b < all.Count; b++)
                {
                    if (!all[a].Bounds.Overlaps(all[b].Bounds)) continue;

                    // The Elevated is authored ON TOP OF a street and over whatever it passes.
                    if (kinds.Row(all[a].Kind).Name == "railway"
                     || kinds.Row(all[b].Kind).Name == "railway") continue;

                    clashes.Add($"'{all[a].Name}' {all[a].Bounds} overlaps "
                              + $"'{all[b].Name}' {all[b].Bounds}");
                }
                faults += Say("places overlapping each other", clashes);

                // ---- 4. a building with no model ---------------------------------------
                var modelless = new List<string>();
                foreach (var place in world.AllPlaces)
                {
                    var row = kinds.Row(place.Kind);
                    if (!row.IsBuilding) continue;
                    if (!CityBuildings.Handles(place))
                        modelless.Add($"'{place.Name}' is a {row.Name}, which no renderer places");
                }
                faults += Say("buildings nothing knows how to build", modelless);

                // ---- 5. anything off the edge of the world -----------------------------
                var outside = new List<string>();
                foreach (var place in world.AllPlaces)
                {
                    var b = place.Bounds;
                    if (b.X < 0 || b.Y < 0 || b.X + b.W > world.Width || b.Y + b.H > world.Height)
                        outside.Add($"'{place.Name}' at {b} runs off a {world.Width}x{world.Height} map");
                }
                faults += Say("places off the edge of the map", outside);

                // ---- 6. roads that meet without a junction -----------------------------
                //
                // A junction forms only where one road's centre falls INSIDE the other's declared
                // run. A road stopping one metre short crosses the other with no crossing between
                // them - which has happened once already, and is invisible until traffic runs.
                var missed = new List<string>();
                foreach (var ns in world.Roads.Lines)
                foreach (var ew in world.Roads.Lines)
                {
                    if (!ns.IsStraight || !ew.IsStraight) continue;
                    if (!ns.IsNorthSouth || ew.IsNorthSouth) continue;

                    // Their corridors physically overlap...
                    bool touches = Math.Abs(ns.Centre - ew.From) < ns.HalfWidth + ew.HalfWidth
                                || Math.Abs(ns.Centre - ew.To) < ns.HalfWidth + ew.HalfWidth
                                || (ns.Centre > ew.From && ns.Centre < ew.To);
                    if (!touches) continue;
                    if (ew.Centre < ns.From - ns.HalfWidth || ew.Centre > ns.To + ns.HalfWidth) continue;

                    // ...but no junction was made.
                    bool made = false;
                    foreach (var j in world.Roads.Junctions)
                        if (Math.Abs(j.X - ns.Centre) < 0.5f && Math.Abs(j.Y - ew.Centre) < 0.5f)
                        { made = true; break; }

                    if (!made)
                        missed.Add($"{ns.Name} and {ew.Name} meet near "
                                 + $"{ns.Centre},{ew.Centre} with no junction");
                }
                faults += Say("roads crossing without a junction", missed);

                Debug.Log(faults == 0
                    ? "[audit] VERDICT: nothing found."
                    : $"[audit] VERDICT: {faults} kinds of fault, listed above.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[audit] FAILED: " + ex);
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>How far a lot reaches into a road's corridor, if at all.</summary>
        private static bool Overlaps(TileRect lot, RoadLine line, out float bite)
        {
            bite = 0f;
            float lo = line.Centre - line.HalfWidth, hi = line.Centre + line.HalfWidth;

            float a = line.IsNorthSouth ? lot.X : lot.Y;
            float b = a + (line.IsNorthSouth ? lot.W : lot.H);
            if (b <= lo || a >= hi) return false;

            // And it has to be alongside the road, not merely on its line extended.
            float c = line.IsNorthSouth ? lot.Y : lot.X;
            float d = c + (line.IsNorthSouth ? lot.H : lot.W);
            if (d <= line.From || c >= line.To) return false;

            bite = Math.Min(hi, b) - Math.Max(lo, a);
            return bite > 0.5f;
        }

        private static int Say(string what, List<string> found)
        {
            if (found.Count == 0) { Debug.Log($"[audit] ok - no {what}."); return 0; }

            Debug.LogError($"[audit] {found.Count} x {what}:");
            for (int i = 0; i < found.Count && i < Report; i++) Debug.LogError("           " + found[i]);
            if (found.Count > Report) Debug.LogError($"           ...and {found.Count - Report} more");
            return 1;
        }
    }
}
