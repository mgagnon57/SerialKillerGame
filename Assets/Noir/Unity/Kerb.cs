using UnityEngine;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// STEPPING STREET FURNITURE OFF THE CARRIAGEWAY AND ONTO THE KERB.
    ///
    /// Reported by the owner on 2026-08-11, looking at the Chicago x Attica crossing - the town
    /// centre - and saying the stop lights were standing in the road. Measured at that junction:
    /// THREE OF THE FOUR SIGNAL MASTS AND ELEVEN OF THE EIGHTEEN LAMP POSTS stood on a tile
    /// flagged Road, which is the carriageway itself.
    ///
    /// Both callers already believed they were doing this. SunRig's lamp loop is headed "On the
    /// kerb, not in the carriageway" and calls NextToVerge - but NextToVerge asks whether any
    /// NEIGHBOUR is off-road while the loop still requires the tile itself to BE road, so it
    /// picked the outermost lane of asphalt every time. CitySignals derived its offset from
    /// `j.Reach - 2.5`, a guess in metres that never knew how wide the carriageway under it
    /// actually was. Neither had anything to aim at: until the verge was laid earlier the same
    /// day there was no such thing as "the public ground beside the road" for them to find.
    ///
    /// So this is deliberately a LAST STEP applied to a position somebody else chose, not a new
    /// placement rule. Spacing, facing and which corner a mast belongs on are all still decided
    /// upstream on the road tile; only where the post is finally set down moves.
    /// </summary>
    public static class Kerb
    {
        /// <summary>
        /// How far out to look for somewhere lawful to stand, in tiles.
        ///
        /// EIGHT, AND FOUR WAS MEASURABLY TOO FEW. A Rossville street is a 10 m carriageway in a
        /// 20 m easement, so beside an ordinary road the kerb is one or two tiles away - but a
        /// signal mast is placed BEYOND a junction, and at Chicago x Attica, where Route 1 meets
        /// Attica, the nearest ground that is not carriageway is FIVE tiles from where the mast
        /// lands. At four rings three of the four masts at the town's only real crossroads were
        /// left standing in the road, which is the exact fault this file exists to fix.
        ///
        /// It is still bounded: past eight tiles a post would be wandering into somebody's front
        /// garden to avoid the street, which is worse than the fault.
        /// </summary>
        private const int MaxRings = 8;

        /// <summary>
        /// The nearest TILE to the given point that is not carriageway, preferring made ground.
        ///
        /// IN AND OUT IN TILE INDICES, deliberately, because the first version of this took and
        /// returned floats and got the convention wrong. Space3D.ToWorld centres a tile at +0.5
        /// and Tile floors, but this rounded - so a mast at x=742.6 was judged on tile 743, found
        /// it clear, declined to move, and left the post standing in tile 742, which is road. The
        /// lamps escaped only because their coordinates were already whole numbers. Callers now
        /// convert explicitly and the mismatch has nowhere to hide.
        ///
        /// Returns false and sets the tile to the one it was given when there is nowhere better -
        /// a mast on a bridge, or a road with no easement recorded - because standing in the road
        /// is bad and standing in a wall is worse.
        /// </summary>
        /// <summary>
        /// The same, but stepping OUT ALONG A GIVEN DIRECTION rather than to whatever lawful tile
        /// happens to be nearest.
        ///
        /// A SIGNAL MAST MUST NOT CHANGE KERBS, and the plain nearest-tile form moved one that
        /// did. Mount's own header says the arm and the head are rigid in the model: turn the
        /// head and the arm turns with it, so the mast has to be on the kerb its facing belongs
        /// to or "the arm reaches out over the pavement instead of the road". The mast starts
        /// only 2.5 m off the centre line, so the nearest ground off the carriageway is as likely
        /// to be the FAR kerb as the near one - and at Chicago x Attica one of the four ended up
        /// there on 2026-08-11, lenses pointing away from the traffic they govern. Measured: the
        /// three correct heads face the junction at dot 0.82, 0.69 and 0.44, that one at -0.27.
        ///
        /// So the caller says which way "away from the road" is, and this only ever goes that
        /// way. Falls back to the undirected search when the whole ray is blocked, because a mast
        /// somewhere lawful beats a mast in the carriageway.
        /// </summary>
        public static bool TryStepOff(WorldModel world, float x, float y, Vector2 outward,
                                      out int tileX, out int tileY)
        {
            var grid = world.Grid;
            int tx = Mathf.FloorToInt(x), ty = Mathf.FloorToInt(y);
            tileX = tx; tileY = ty;
            if (!grid.InBounds(tx, ty)) return false;
            if ((grid.FlagsAt(tx, ty) & TileFlags.Road) == 0) return false;

            if (outward.sqrMagnitude < 1e-6f) return TryStepOff(world, x, y, out tileX, out tileY);
            outward.Normalize();

            // A HALF-PLANE, NOT A RAY, AND NEVER A FALLBACK ACROSS THE ROAD.
            //
            // The first version walked a single ray along `outward` and, when that ray was
            // blocked, fell back to the undirected search - which is how a mast at Chicago x
            // Attica ended up on the FAR kerb with its arm over the wrong lane. Chicago is wide
            // enough there that eight tiles of the ray were all carriageway, so the fallback ran
            // and found ground on the other side. It was measured facing AWAY from the traffic it
            // governs at dot -0.27 against 0.77, 0.69 and 0.44 for the three that were right.
            //
            // Any tile on the correct side will do, so this is the ordinary nearest-ring search
            // with everything behind the mast rejected. If nothing on that side is reachable it
            // returns false and the caller keeps the position it chose: a mast standing in the
            // carriageway is a blemish, and one showing its lenses to nobody is a fault.
            for (int r = 1; r <= MaxRings; r++)
            {
                int bestX = 0, bestY = 0; float bestDist = float.MaxValue;

                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                    if (dx * outward.x + dy * outward.y <= 0f) continue;   // behind, or level

                    int nx = tx + dx, ny = ty + dy;
                    if (!grid.InBounds(nx, ny)) continue;

                    var f = grid.FlagsAt(nx, ny);
                    if ((f & TileFlags.Road) != 0) continue;
                    if ((f & TileFlags.Walkable) == 0) continue;
                    if ((f & TileFlags.Indoor) != 0) continue;

                    float dist = dx * dx + dy * dy;
                    if (dist < bestDist) { bestDist = dist; bestX = nx; bestY = ny; }
                }

                if (bestDist < float.MaxValue) { tileX = bestX; tileY = bestY; return true; }
            }

            return false;
        }

        public static bool TryStepOff(WorldModel world, float x, float y, out int tileX, out int tileY)
        {
            var grid = world.Grid;

            // FLOOR, not round: this is what Tile means and what Space3D.TileAt does.
            int tx = Mathf.FloorToInt(x), ty = Mathf.FloorToInt(y);
            tileX = tx; tileY = ty;
            if (!grid.InBounds(tx, ty)) return false;

            // Already clear of the carriageway - the common case beside an ordinary street, and
            // it must not be nudged for the sake of it.
            if ((grid.FlagsAt(tx, ty) & TileFlags.Road) == 0) return false;

            // Two passes. Public ground first across the WHOLE search, then anything lawful -
            // otherwise a corner of somebody's lawn two tiles away beats the real kerb five
            // tiles away, and the post ends up in a garden rather than on the pavement.
            for (int pass = 0; pass < 2; pass++)
            {
                int bestX = 0, bestY = 0, bestScore = -1;
                float bestDist = float.MaxValue;

                for (int r = 1; r <= MaxRings; r++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        // The ring only, so nearer tiles are always considered first.
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;

                        int nx = tx + dx, ny = ty + dy;
                        if (!grid.InBounds(nx, ny)) continue;

                        var f = grid.FlagsAt(nx, ny);
                        if ((f & TileFlags.Road) != 0) continue;        // still the road
                        if ((f & TileFlags.Walkable) == 0) continue;    // a wall, or water
                        if ((f & TileFlags.Indoor) != 0) continue;      // somebody's front room

                        // A laid walk is the best place for a post; the verge is what a walk
                        // would be laid on; other public ground will do.
                        bool onSomebodysLot = grid.LotAt(nx, ny) != TileGrid.NoLot;
                        if (pass == 0 && onSomebodysLot) continue;

                        int score = (f & TileFlags.Path) != 0 ? 3
                                  : (f & TileFlags.Verge) != 0 ? 2
                                  : onSomebodysLot ? 0 : 1;

                        float dist = dx * dx + dy * dy;
                        if (score > bestScore || (score == bestScore && dist < bestDist))
                        {
                            bestScore = score; bestDist = dist; bestX = nx; bestY = ny;
                        }
                    }

                    // The nearest ring that had anything wins. Widening once something lawful is
                    // in reach only pushes the post away from the street it belongs to.
                    if (bestScore >= 0) { tileX = bestX; tileY = bestY; return true; }
                }
            }

            return false;
        }
    }
}
