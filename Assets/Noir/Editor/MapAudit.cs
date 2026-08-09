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
        /// <summary>The audit as a batch entry point: run it, then leave, with the exit code
        /// saying whether the map was clean. Kept separate from RunCore so the audit can also be
        /// one step of a longer pass - see Preflight, which could not call this at all while the
        /// exit lived inside the work.</summary>
        public static void Run()
        {
            int faults = RunCore();
            if (Application.isBatchMode) EditorApplication.Exit(faults == 0 ? 0 : 1);
        }

        /// <summary>Every check, and how many things were wrong. Exits nothing.</summary>
        public static int RunCore()
        {
            int faults = 0;

            try
            {
                // THE SAME TOWN THE GAME BUILDS, or this audits a town nobody plays.
                //
                // This used to build the world straight from city.txt and never swap in the
                // surveyed roads, so every number below described the OLD network - it reported
                // 37 roads and 56 places lying over one while the game was building 61 and a
                // different set of overlaps. An audit that measures a different town than the
                // one on screen is worse than no audit, because it is believed. TownPipeline
                // guarantees the swap now, and the rest of the survey layer with it.
                var built = TownPipeline.Build();
                var world = built.World;
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
                        // NO IsStraight GUARD. See Gap/Overlaps - they walk the centre line now,
                        // so a road that bends is judged instead of skipped.
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

                // ---- 6. a car park you cannot drive into --------------------------------
                //
                // A surface lot has to touch a road. Placed in the middle of a block it looks
                // perfectly correct from above and is reachable only by driving across the
                // pavement, which is exactly the sort of thing that survives a screenshot: the
                // cars are parked, the tarmac is laid, and there is no way in.
                var landlocked = new List<string>();
                foreach (var place in world.AllPlaces)
                {
                    if (kinds.Row(place.Kind).Name != "carpark") continue;

                    float nearest = float.MaxValue;
                    foreach (var line in world.Roads.Lines)
                        nearest = Math.Min(nearest, Gap(place.Bounds, line));

                    if (nearest > 4f)
                        landlocked.Add($"'{place.Name}' at {place.Bounds} is {nearest:0}m from "
                                     + "the nearest road");
                }
                faults += Say("car parks with no way in", landlocked);

                // ---- 7. roads that meet without a junction -----------------------------
                //
                // A junction forms only where one road's centre falls INSIDE the other's declared
                // run. A road stopping one metre short crosses the other with no crossing between
                // them - which has happened once already, and is invisible until traffic runs.
                // NEITHER STRAIGHT NOR AXIS-CROSSED ANY MORE. This asked its question entirely in
                // scalars - `ns.Centre` against `ew.From`/`ew.To` - which needs both roads to be
                // axis-aligned bars, and then took only north-south against east-west pairs. Both
                // conditions describe the model as it was before the survey. The axis one in
                // particular is the exact filter JUNC-2 removed from RoadNetwork, and it was the
                // filter that let an alley run into Benton with no junction and cars drive out
                // through the traffic: the check that should have caught it could not see it
                // either, because it was written from the same assumption as the bug.
                //
                // Walked instead: where two centre lines come within their own half-widths of
                // each other, there has to be a junction near that spot.
                var missed = new List<string>();
                for (int a = 0; a < world.Roads.Lines.Count; a++)
                for (int b = a + 1; b < world.Roads.Lines.Count; b++)
                {
                    var one = world.Roads.Lines[a];
                    var two = world.Roads.Lines[b];
                    if (one?.Path == null || two?.Path == null) continue;

                    float touching = one.HalfWidth + two.HalfWidth;

                    // Cheap reject first: this is O(roads squared) over 68 roads and each pair
                    // would otherwise walk two centre lines a metre at a time.
                    if (one.Path.MinX - touching > two.Path.MaxX || two.Path.MinX - touching > one.Path.MaxX
                     || one.Path.MinY - touching > two.Path.MaxY || two.Path.MinY - touching > one.Path.MaxY)
                        continue;

                    float closest = float.MaxValue;
                    float atX = 0f, atY = 0f;
                    foreach (var p in Walk(one))
                    {
                        var (s, lateral) = two.Path.Project(p);
                        float d = lateral < 0f ? -lateral : lateral;

                        // Project clamps, so a point beyond the end of `two` reports the lateral
                        // at its end rather than the real distance. Measure to the point itself.
                        var q = two.Path.PointAt(s);
                        float dx = q.X - p.X, dy = q.Y - p.Y;
                        d = (float)Math.Sqrt(dx * dx + dy * dy);

                        if (d < closest) { closest = d; atX = p.X; atY = p.Y; }
                    }
                    if (closest > touching) continue;

                    // ...but no junction near where they meet. Judged against the reach of the
                    // junction rather than half a metre: a merged node sits between its arms by
                    // construction and is not on any one of their centre lines exactly.
                    bool made = false;
                    foreach (var j in world.Roads.Junctions)
                    {
                        float dx = j.X - atX, dy = j.Y - atY;
                        if (dx * dx + dy * dy <= (j.Reach + touching) * (j.Reach + touching))
                        { made = true; break; }
                    }

                    if (!made)
                        missed.Add($"{one.Name} and {two.Name} come within {closest:0.0}m at "
                                 + $"{atX:0},{atY:0} with no junction");
                }
                faults += Say("roads crossing without a junction", missed);

                // ---- 8. two parked cars in the same bay --------------------------------
                //
                // The seven checks above are arithmetic on the AUTHORED layout, and a parked
                // car is not in the layout - it is a prefab CityParking works out the position
                // of at build time. So an overlap in a car park is invisible to every one of
                // them, which is how "cars sharing a bay" got reported twice from screenshots
                // with the audit clean both times. This one builds the lots and measures what
                // actually ends up standing in them.
                faults += Say("parked cars in the same space", ParkedCarClashes(world));

                Debug.Log(faults == 0
                    ? "[audit] VERDICT: nothing found."
                    : $"[audit] VERDICT: {faults} kinds of fault, listed above.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[audit] FAILED: " + ex);
                faults++;                      // a crashed audit has not proved the map is clean
            }

            // THE EXIT CODE IS THE VERDICT, and it used to be zero whatever was found.
            //
            // Every check above reports with Debug.LogError and the process then exited 0
            // regardless, so a caller could only tell a clean map from a broken one by grepping
            // the log for words - which is to say a broken map LOOKED EXACTLY LIKE A CLEAN PASS,
            // the same shape as the `-quit` + `-runTests` race that TestInvocationGuard exists to
            // stop. Nothing depended on the zero: no script, workflow or task in the repo runs
            // this, only the documented command line and the menu item.
            //
            // A thrown exception counts as a fault too. An audit that fell over part way through
            // has not looked at the rest of the map, and reporting that as success is the same
            // lie in a different coat.
            return faults;
        }

        /// <summary>
        /// Every pair of parked cars whose bodies actually intersect, measured off the built
        /// lots rather than worked out from the same arithmetic that placed them.
        ///
        /// Renderer bounds are axis-aligned, which would normally overstate a rotated object -
        /// but a parked car is only ever turned by a right angle here, so its box stays square
        /// to the world and an intersection is a real one.
        /// </summary>
        private static List<string> ParkedCarClashes(WorldModel world)
        {
            var found = new List<string>();
            GameObject probe = null;

            try
            {
                probe = new GameObject("ParkingProbe");
                var node = CityParking.Build(world, probe.transform);

                var parked = new List<(string name, Bounds box)>();
                foreach (Transform child in node.transform)
                {
                    // The lots lay tarmac AND cars under one node; only the cars can clash.
                    if (!child.name.StartsWith("Car", StringComparison.Ordinal)) continue;

                    var rends = child.GetComponentsInChildren<Renderer>();
                    if (rends.Length == 0) continue;

                    var box = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) box.Encapsulate(rends[i].bounds);
                    parked.Add((child.name, box));
                }

                for (int a = 0; a < parked.Count; a++)
                for (int b = a + 1; b < parked.Count; b++)
                {
                    var one = parked[a].box;
                    var two = parked[b].box;
                    if (!one.Intersects(two)) continue;

                    // How far one is INTO the other on the ground plane. Bumpers that merely
                    // touch are not a fault; a car buried in its neighbour is.
                    float ox = Math.Min(one.max.x, two.max.x) - Math.Max(one.min.x, two.min.x);
                    float oz = Math.Min(one.max.z, two.max.z) - Math.Max(one.min.z, two.min.z);
                    float bite = Math.Min(ox, oz);
                    if (bite < 0.05f) continue;

                    found.Add($"'{parked[a].name}' and '{parked[b].name}' overlap by {bite:0.00}m "
                            + $"near {one.center.x:0}, {-one.center.z:0}");
                }

                Debug.Log($"[audit] measured {parked.Count} parked cars.");
            }
            finally
            {
                if (probe != null) UnityEngine.Object.DestroyImmediate(probe);
            }

            return found;
        }

        /// <summary>
        /// The shortest distance from a lot to a road's corridor, or zero where they touch.
        ///
        /// Both axes matter: a lot beside the road but two hundred metres past the end of it is
        /// not beside that road at all.
        /// </summary>
        /// <summary>
        /// How close a lot comes to a road's corridor, walking the road's own centre line.
        ///
        /// THIS FOLLOWED Centre/From/To, AND EVERY CALLER GUARDED `if (!line.IsStraight) continue`.
        /// A scalar centre and a pair of extents describe an axis-aligned bar and nothing else, so
        /// the audit could only speak about roads shaped like that - and it dealt with the rest by
        /// not looking at them. On Content/city.txt that is five roads. On Content/roads.txt,
        /// which is what the game actually drives on, it is most of them: the audit was reporting
        /// a clean town while saying nothing at all about the town.
        ///
        /// Same idiom as RoadCentrelines and PlanLabels, and for the same stated reason - an
        /// axis-aligned reading quietly straightens exactly the roads whose shape is in question.
        /// A straight axis-aligned road's Path IS Centre from From to To, so those answers do not
        /// move by more than the sample pitch.
        /// </summary>
        private static float Gap(TileRect lot, RoadLine line)
        {
            float nearest = float.MaxValue;
            foreach (var p in Walk(line))
            {
                float d = ToRect(lot, p.X, p.Y) - line.HalfWidth;
                if (d < nearest) nearest = d;
            }
            return nearest < 0f ? 0f : nearest;
        }

        /// <summary>How far a lot reaches into a road's corridor, if at all.</summary>
        private static bool Overlaps(TileRect lot, RoadLine line, out float bite)
        {
            bite = 0f;

            float deepest = 0f;
            foreach (var p in Walk(line))
            {
                float reach = line.HalfWidth - ToRect(lot, p.X, p.Y);
                if (reach > deepest) deepest = reach;
            }

            bite = deepest;
            return bite > 0.5f;
        }

        /// <summary>
        /// The road's centre line at one-metre steps, ends included. One metre is RoadPath's own
        /// resample pitch, so a curve is not being read at a resolution it was not built at.
        /// </summary>
        private static IEnumerable<Noir.Core.Contracts.Vec2> Walk(RoadLine line)
        {
            if (line?.Path == null) yield break;

            float length = line.Path.Length;
            for (float s = 0f; s < length; s += 1f) yield return line.Path.PointAt(s);
            yield return line.Path.PointAt(length);
        }

        /// <summary>Distance from a point to a lot, 0 inside it.</summary>
        private static float ToRect(TileRect lot, float px, float py)
        {
            float dx = Math.Max(0f, Math.Max(lot.X - px, px - (lot.X + lot.W)));
            float dy = Math.Max(0f, Math.Max(lot.Y - py, py - (lot.Y + lot.H)));
            return (float)Math.Sqrt(dx * dx + dy * dy);
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
