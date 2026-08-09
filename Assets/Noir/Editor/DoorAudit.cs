using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// WHY a door cannot be reached, one door at a time.
    ///
    /// `WorldValidator` says which doors fail and that is all it can say - it is a gate, not an
    /// investigation. Nine of them failed on 2026-08-09 and the report gave a coordinate and a
    /// verdict, which is not enough to fix one: "not walkable" and "cut off from the rest of the
    /// village" are completely different faults with completely different causes, and the second
    /// one needs to know how big the pocket is that the door is trapped in.
    ///
    /// So this prints the ground under the door, the ground all round it, which walkable region it
    /// belongs to and how many tiles that region has. A door in a two-tile pocket behind a garage
    /// is a different problem from a door in a four-hundred-tile block that has been cut off from
    /// the rest of town by a road.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.DoorAudit.Run -logFile &lt;log&gt;
    /// </summary>
    public static class DoorAudit
    {
        [MenuItem("Noir/Audit the Doors")]
        public static void Run()
        {
            var built = TownPipeline.Build();
            var world = built.World;
            var grid = world.Grid;

            var report = WorldValidator.Validate(world);
            Debug.Log($"[doors] validator: {report.Errors.Count} error(s), " +
                      $"{report.Warnings.Count} warning(s)");

            // The same regions the validator walks, so the numbers below are its numbers.
            var regions = new Noir.Core.Sim.WalkableRegions(grid);
            var sizes = new Dictionary<int, int>();
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                int r = regions.RegionAt(new Tile(x, y));
                if (r < 0) continue;
                sizes.TryGetValue(r, out int c);
                sizes[r] = c + 1;
            }

            int main = -1, biggest = -1;
            foreach (var kv in sizes)
                if (kv.Value > biggest) { biggest = kv.Value; main = kv.Key; }

            Debug.Log($"[doors] {sizes.Count} walkable regions, largest is {biggest} tiles");

            foreach (var place in world.AllPlaces)
            {
                if (!place.Door.IsValid || !grid.InBounds(place.Door)) continue;

                bool walkable = grid.IsWalkable(place.Door);
                int region = regions.RegionAt(place.Door);
                if (walkable && region == main) continue;

                var sb = new StringBuilder();
                sb.AppendLine($"[doors] '{place.Name}' door {place.Door} in {place.Bounds}");
                sb.AppendLine($"           under it   : {grid.TerrainAt(place.Door.X, place.Door.Y)}"
                            + $"  walkable={walkable}");

                if (region >= 0)
                {
                    sizes.TryGetValue(region, out int size);
                    sb.AppendLine($"           region     : {region} of {size} tiles"
                                + (region == main ? " (the main one)" : $", against {biggest} in the main one"));
                }
                else
                {
                    sb.AppendLine("           region     : none - the tile itself is not walkable");
                }

                // What is all round it, which is what says whether the door is buried or the
                // whole pocket is sealed.
                for (int dy = -1; dy <= 1; dy++)
                {
                    var row = new StringBuilder("           ");
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var t = new Tile(place.Door.X + dx, place.Door.Y + dy);
                        row.Append(grid.InBounds(t)
                            ? $"{Short(grid.TerrainAt(t.X, t.Y))} "
                            : "??  ");
                    }
                    sb.AppendLine(row.ToString());
                }

                // WHO ELSE IS STANDING THERE. A door is sealed either because its own building
                // paints over it or because a NEIGHBOUR moved against its outside face, and those
                // want different fixes. `ClearOfRoads` shoved 175 buildings off road corridors on
                // 2026-08-09 and carries each door along with its building, so the door keeps its
                // place on the wall - what it cannot carry is the room outside the wall.
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var t = new Tile(place.Door.X + dx, place.Door.Y + dy);
                    if (!grid.InBounds(t)) continue;

                    foreach (var other in world.AllPlaces)
                    {
                        if (ReferenceEquals(other, place)) continue;
                        if (!other.Bounds.Contains(t)) continue;
                        sb.AppendLine($"           blocked by : '{other.Name}' {other.Bounds} "
                                    + $"covers {t}, {dx:+0;-0;0},{dy:+0;-0;0} from the door");
                    }
                }

                // And whether the door is even on the edge of its own building.
                var b = place.Bounds;
                bool onEdge = place.Door.X == b.X || place.Door.X == b.X + b.W - 1
                           || place.Door.Y == b.Y || place.Door.Y == b.Y + b.H - 1;
                sb.AppendLine($"           on its edge: {onEdge}");

                Debug.Log(sb.ToString());
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>Two letters, so the neighbourhood reads as a picture rather than a list.</summary>
        private static string Short(Noir.Core.World.Terrain t) => t switch
        {
            Noir.Core.World.Terrain.Road => "RD",
            Noir.Core.World.Terrain.Path => "PA",
            Noir.Core.World.Terrain.Floor => "FL",
            Noir.Core.World.Terrain.Wall => "WA",
            Noir.Core.World.Terrain.Water => "WT",
            Noir.Core.World.Terrain.Grass => "GR",
            _ => t.ToString().Substring(0, 2).ToUpperInvariant(),
        };
    }
}
