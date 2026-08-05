using System.Collections.Generic;
using System.Text;
using Noir.Core.Contracts;

namespace Noir.Unity
{
    /// <summary>
    /// Which parts of the town nobody can walk to, and who is stuck because of it.
    ///
    /// The road refit left the walkable space in pieces, and a large share of the town spends
    /// the day trying to reach somewhere there is no route to. Bounding the search made that
    /// cheap; it did not make it right, and a count on its own is not something anybody can
    /// act on. This names the places.
    ///
    /// Runtime rather than editor-only because the probe drives Play by itself and needs to be
    /// able to write this without a person clicking a menu.
    /// </summary>
    public static class StrandedSurvey
    {
        public static string Describe(VillageHost host)
        {
            if (host == null || host.Sim == null) return "no simulation to survey";

            var sim = host.Sim;
            var regions = sim.Regions;
            var sb = new StringBuilder();

            sb.AppendLine("--- who cannot get where they are going ---");
            sb.AppendLine($"   citizens         : {sim.AgentCount}");
            sb.AppendLine($"   stranded         : {sim.StrandedCount}");
            sb.AppendLine();

            // With a denominator. Give-ups on their own cannot be judged: they are searches for
            // somewhere that IS reachable which ran past the node ceiling, so a high share means
            // the ceiling is refusing journeys people could walk - the exact fault the ceiling
            // replaced, reintroduced from the other side.
            long total = sim.PathsFoundTotal + sim.GaveUpTotal + sim.NoRouteTotal;
            sb.AppendLine($"   searches found   : {sim.PathsFoundTotal:N0}");
            sb.AppendLine($"   no route (free)  : {sim.NoRouteTotal:N0}");
            sb.AppendLine($"   GAVE UP at cap   : {sim.GaveUpTotal:N0}"
                        + (total > 0 ? $"   ({sim.GaveUpTotal * 100.0 / total:0.00}% of all searches)" : ""));
            if (sim.GaveUpTotal > 0)
                sb.AppendLine("      ^ these are REACHABLE destinations refused by the node "
                            + "ceiling. Any at all is somebody who did not set off.");
            sb.AppendLine($"   worst SUCCESSFUL : {sim.WorstNodesFound:N0} nodes"
                        + $"   (ceiling is {Noir.Core.Sim.Pathfinder.HardNodeCeiling:N0})");
            sb.AppendLine("      ^ the ceiling has to clear this or honest journeys get refused.");
            sb.AppendLine();

            // The shape of the walkable space. One region is a healthy town; more than one
            // means somewhere has been sealed off, and the sizes say whether that is a cupboard
            // or a whole neighbourhood.
            int largest = regions.Largest();
            sb.AppendLine($"   walkable areas   : {regions.RegionCount}");

            var bySize = new List<KeyValuePair<int, int>>();
            for (int r = 0; r < regions.RegionCount; r++)
                bySize.Add(new KeyValuePair<int, int>(r, regions.SizeOf(r)));
            bySize.Sort((a, b) => b.Value.CompareTo(a.Value));

            int pockets = 0, shown = 0;
            foreach (var kv in bySize)
            {
                if (kv.Value < 2) { pockets++; continue; }
                if (shown++ < 12)
                    sb.AppendLine($"      region {kv.Key,-5} {kv.Value,10:N0} tiles"
                                + (kv.Key == largest ? "   <- the town" : ""));
            }
            if (shown > 12) sb.AppendLine($"      and {shown - 12} more areas of 2 tiles or more");
            if (pockets > 0)
                sb.AppendLine($"      and {pockets:N0} single tiles nobody can step off");
            sb.AppendLine();

            // Grouped by destination, because one walled-off door strands everybody who wants
            // to use it. Seventy-six stuck people are seventy-six symptoms of a much smaller
            // number of faults, and it is the faults that get repaired.
            var byPlace = new Dictionary<int, Trouble>();
            foreach (var s in sim.StrandedPeople())
            {
                if (byPlace.TryGetValue(s.Where.Value, out var seen))
                {
                    seen.Count++;
                    byPlace[s.Where.Value] = seen;
                    continue;
                }

                var place = host.World != null ? host.World.GetPlace(s.Where) : null;
                byPlace[s.Where.Value] = new Trouble
                {
                    Name = place != null ? place.Name : $"place {s.Where.Value}",
                    Count = 1,
                    At = s.At,
                    Region = s.ItsRegion,
                };
            }

            // Where the STUCK PEOPLE are standing, which is the other half of the answer. A
            // destination in a pocket is a door to reconnect; a person in one is somebody the
            // map has walled in, and they need the opposite repair.
            var byPen = new Dictionary<int, int>();
            int onUnwalkable = 0;
            foreach (var s in sim.StrandedPeople())
            {
                if (s.TheirRegion < 0) { onUnwalkable++; continue; }
                if (s.TheirRegion == largest) continue;   // free to move; the destination is the fault
                byPen[s.TheirRegion] = byPen.TryGetValue(s.TheirRegion, out int n) ? n + 1 : 1;
            }

            if (onUnwalkable > 0 || byPen.Count > 0)
            {
                sb.AppendLine("   people who are themselves walled in:");
                if (onUnwalkable > 0)
                    sb.AppendLine($"      {onUnwalkable,3} standing on a tile that is NOT WALKABLE "
                                + "- a building was written over them");
                foreach (var kv in byPen)
                    sb.AppendLine($"      {kv.Value,3} shut inside area {kv.Key} "
                                + $"({regions.SizeOf(kv.Key):N0} tiles)");
                sb.AppendLine();
            }

            sb.AppendLine($"   destinations nobody can reach: {byPlace.Count}");
            if (byPlace.Count == 0)
            {
                sb.AppendLine("      none. Everybody can reach where they are going.");
                return sb.ToString();
            }

            var worst = new List<Trouble>(byPlace.Values);
            worst.Sort((a, b) => b.Count.CompareTo(a.Count));

            foreach (var t in worst)
            {
                string why = t.Region < 0
                    ? "the target tile is not walkable at all"
                    : t.Region == largest
                        ? "target is in the town's own area - so the PERSON is the one walled in"
                        : $"target is in area {t.Region} ({regions.SizeOf(t.Region):N0} tiles), "
                          + "cut off from the town";

                sb.AppendLine($"      {t.Count,3} people -> {t.Name}");
                sb.AppendLine($"          tile {t.At.X},{t.At.Y}: {why}");
            }

            return sb.ToString();
        }

        private struct Trouble
        {
            public string Name;
            public int Count;
            public Tile At;
            public int Region;
        }
    }
}
