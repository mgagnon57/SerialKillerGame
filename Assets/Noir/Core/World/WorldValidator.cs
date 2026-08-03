using System;
using System.Collections.Generic;
using System.Text;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    /// <summary>
    /// Catches the layout mistakes that are invisible in a text file and obvious once someone
    /// walks into them: a door sealed behind a wall, a cottage cut off from the road, two
    /// buildings stamped on top of each other.
    ///
    /// Deliberately small. This checks what a human would NOT catch by looking at the map —
    /// it is not a proof harness for hand-authored content.
    /// </summary>
    public sealed class ValidationReport
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public int WalkableTiles;
        public int LargestRegion;
        public int RegionCount;

        public bool IsValid => Errors.Count == 0;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"walkable tiles : {WalkableTiles}");
            sb.AppendLine($"regions        : {RegionCount} (largest {LargestRegion}, "
                        + $"{(WalkableTiles > 0 ? 100.0 * LargestRegion / WalkableTiles : 0):0.0}% connected)");
            sb.AppendLine();

            if (Errors.Count == 0 && Warnings.Count == 0)
            {
                sb.AppendLine("OK — no problems found.");
                return sb.ToString();
            }

            foreach (var e in Errors) sb.AppendLine("ERROR   " + e);
            foreach (var w in Warnings) sb.AppendLine("warning " + w);
            sb.AppendLine();
            sb.AppendLine($"{Errors.Count} error(s), {Warnings.Count} warning(s)");
            return sb.ToString();
        }
    }

    public static class WorldValidator
    {
        public static ValidationReport Validate(WorldModel world)
        {
            var report = new ValidationReport();
            var grid = world.Grid;

            // --- connectivity: label every walkable region ---
            var region = new int[grid.Count];
            for (int i = 0; i < region.Length; i++) region[i] = -1;

            int regionCount = 0;
            int largest = 0;
            int walkable = 0;
            var queue = new Queue<int>();

            for (int start = 0; start < grid.Count; start++)
            {
                var t0 = grid.TileAt(start);
                if (!grid.IsWalkable(t0) || region[start] >= 0) continue;

                int size = 0;
                region[start] = regionCount;
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    int idx = queue.Dequeue();
                    size++;
                    var t = grid.TileAt(idx);

                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = t.X + dx, ny = t.Y + dy;
                        if (!grid.InBounds(nx, ny)) continue;
                        if (!grid.IsWalkable(nx, ny)) continue;

                        // No cutting diagonally between two blocked corners.
                        if (dx != 0 && dy != 0 &&
                            (!grid.IsWalkable(t.X + dx, t.Y) && !grid.IsWalkable(t.X, t.Y + dy)))
                            continue;

                        int ni = grid.Index(nx, ny);
                        if (region[ni] >= 0) continue;
                        region[ni] = regionCount;
                        queue.Enqueue(ni);
                    }
                }

                if (size > largest) largest = size;
                walkable += size;
                regionCount++;
            }

            report.WalkableTiles = walkable;
            report.LargestRegion = largest;
            report.RegionCount = regionCount;

            // The main region is the one everybody shares — the streets.
            int mainRegion = -1;
            {
                var sizes = new Dictionary<int, int>();
                for (int i = 0; i < region.Length; i++)
                {
                    if (region[i] < 0) continue;
                    sizes.TryGetValue(region[i], out int c);
                    sizes[region[i]] = c + 1;
                }
                int best = -1;
                foreach (var kv in sizes)
                    if (kv.Value > best) { best = kv.Value; mainRegion = kv.Key; }
            }

            // --- per-place checks ---
            var places = world.AllPlaces;
            for (int i = 0; i < places.Count; i++)
            {
                var p = places[i];

                if (p.Door.IsValid)
                {
                    if (!grid.InBounds(p.Door))
                        report.Errors.Add($"'{p.Name}': door {p.Door} is off the map");
                    else if (!grid.IsWalkable(p.Door))
                        report.Errors.Add($"'{p.Name}': door {p.Door} is not walkable");
                    else if (!p.Bounds.Contains(p.Door))
                        // AN ERROR, NOT A WARNING. This was a warning until 2026-08-03, when
                        // thirty-five downtown buildings were moved onto Chicago Street's curve
                        // and their `door` lines - which are separate lines in the map file - were
                        // left behind. Every door ended up stranded in the carriageway, several
                        // metres outside the building it belongs to. The map loaded, the town
                        // built, thirty-two warnings scrolled past unread, and the editor then
                        // locked up on one core with flat memory until it was killed.
                        //
                        // A door is where a person enters a building. One that is not on the
                        // building cannot be walked to from inside it, and nothing downstream is
                        // written to cope with that. It is a broken map, so it says so.
                        report.Errors.Add($"'{p.Name}': door {p.Door} is outside its footprint "
                                        + $"{p.Bounds} - a door has to be ON the building. If the "
                                        + "building was moved, its `door` line has to move with it.");
                    else if (region[grid.Index(p.Door)] != mainRegion)
                        report.Errors.Add($"'{p.Name}': door {p.Door} is cut off from the rest of the village");

                    // A door needs somewhere to step out to.
                    if (grid.InBounds(p.Door) && !HasOutdoorNeighbour(grid, p.Door))
                        report.Warnings.Add($"'{p.Name}': door {p.Door} has no outdoor tile beside it");
                }

                for (int j = i + 1; j < places.Count; j++)
                {
                    if (p.Bounds.Overlaps(places[j].Bounds))
                        report.Errors.Add($"'{p.Name}' {p.Bounds} overlaps '{places[j].Name}' {places[j].Bounds}");
                }

                // Dwellings get their character from the household living in them, not from the
                // building, so they are not expected to carry a description of their own. Which
                // kinds are is the `describe` column.
                if (string.IsNullOrEmpty(p.Human) && PlaceKindTable.Current.Row(p.Kind).WantsDescription)
                    report.Warnings.Add($"'{p.Name}': no human description — the UI would have nothing to say about it");
            }

            if (regionCount > 1)
                report.Warnings.Add($"{regionCount - 1} walkable area(s) are separate from the main village");

            return report;
        }

        private static bool HasOutdoorNeighbour(TileGrid grid, Tile door)
        {
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = door.X + dx, ny = door.Y + dy;
                if (!grid.InBounds(nx, ny)) continue;
                if (grid.IsWalkable(nx, ny) && (grid.FlagsAt(nx, ny) & TileFlags.Indoor) == 0)
                    return true;
            }
            return false;
        }
    }
}
