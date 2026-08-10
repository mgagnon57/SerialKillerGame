namespace Noir.Core.World
{
    /// <summary>
    /// WHICH ARM OF AN UNSIGNALISED JUNCTION CARRIES THE STOP SIGN.
    ///
    /// Hoisted out of `CitySignals` for the reason `Headings.SideOfPath` was: the rule is about
    /// the map, not about Unity, so it belongs where the suite can reach it. `CitySignals` and
    /// `CitySigns` both call this, which is what keeps what a junction DOES and what a junction
    /// SAYS from ever drifting apart — and now a Core test can watch it too.
    ///
    /// THE BIGGER ROAD RUNS THROUGH, and "bigger" is asked of what the arms CARRY rather than
    /// what they are paved to. On the surveyed network no road is paved to a main road's
    /// corridor — Attica is a county highway sitting on a village street's right of way — so
    /// comparing `Class` made every road in town equal and the tie below fired at all 64
    /// junctions at once. Nine tenths of stopped vehicles then waited 119.9 s with clear road
    /// ahead, because every north-south arm in Rossville was giving way permanently to an
    /// east-west stream that never yielded back.
    /// </summary>
    public static class JunctionPriority
    {
        /// <summary>
        /// True when the NORTH-SOUTH arm gives way.
        ///
        /// The class comparison settles the case that matters — the dirt track to the big barn
        /// gives way to the road it joins — and `RoadClass` is ordered from track up to freeway,
        /// so it is a comparison rather than a table.
        /// </summary>
        public static bool GiveWayIsNorthSouth(Junction j)
        {
            if (j.NorthSouth == null || j.EastWest == null)
                return j.NorthSouth == null;

            if (j.NorthSouth.Carries != j.EastWest.Carries)
                return j.NorthSouth.Carries < j.EastWest.Carries;

            // SAME CLASS, SO ASK THE COUNTY HOW BUSY EACH ONE IS.
            //
            // This used to stop at the class comparison and hand the tie to the east-west road
            // unconditionally. That is arbitrary, and it is arbitrary in a DIRECTION: every tie
            // in the town fires the same way, so the arbitrariness accumulates into a systematic
            // bias instead of averaging out — which is the exact shape that starved the
            // north-south arms.
            //
            // IDOT counts twelve of Rossville's streets by name (`tools/rossville-aadt.txt`,
            // `docs/research/TRAFFIC-COUNTS.md`), and a road with four times the traffic of the
            // one it crosses is the road that should run through whatever the two are paved to.
            // Where the county counted only one of the pair, the counted one wins: an uncounted
            // street is one the county did not think worth a counter.
            if (j.NorthSouth.Aadt != j.EastWest.Aadt)
                return j.NorthSouth.Aadt < j.EastWest.Aadt;

            // NEITHER COUNTED, OR BOTH COUNTED THE SAME — so ask which of the two is the road
            // that GOES somewhere. A street running the width of the town is a through route
            // whatever the county paved it to; a hundred-metre stub that meets it is not.
            //
            // AND THE ANSWER IS A PROPERTY OF THE ROAD, NOT OF THE JUNCTION, which is the point.
            // A per-junction tie-break — a compass direction, a coordinate hash — can put the
            // stop sign on Maple at one corner and on the street crossing it at the next, and no
            // real town is signed that way: a driver on the through street expects to keep
            // priority for its whole length. Comparing lengths gives every junction along one
            // street the same answer for free.
            float mine = j.NorthSouth.Path?.Length ?? 0f;
            float theirs = j.EastWest.Path?.Length ?? 0f;
            if (mine < theirs - 0.5f) return true;
            if (theirs < mine - 0.5f) return false;

            // Two roads of the same class, the same count and the same length to within half a
            // metre. Broken on the name so the answer is stable across runs and across machines —
            // no RNG, no clock, and deliberately not an `IRng` substream, for the same reason
            // `Materials3D.Scatter`'s position hash is not one.
            return string.CompareOrdinal(j.NorthSouth.Name, j.EastWest.Name) < 0;
        }
    }
}
