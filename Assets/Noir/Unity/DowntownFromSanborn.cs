using System.Collections.Generic;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.World;
using Noir.Core.Survey;

namespace Noir.Unity
{
    /// <summary>
    /// Builds the downtown terraces from the 1913 survey instead of from 2016 imagery.
    ///
    /// WHY THIS EXISTS. 1991 is the date this town represents, and every measured source here
    /// postdates it: the tax roll starts 2007, the footprints are 2016. For most of Rossville that
    /// is a tolerable fallback - a house standing in 2016 was very probably standing in 1991.
    /// Downtown it is not. A quarter of the commercial section burned in February 2004 and was
    /// replaced with a convenience store and its forecourt, so the imagery there is not a slightly
    /// late picture of 1991, it is a picture of what replaced it.
    ///
    /// <see cref="CommercialRow"/> has encoded the alternative since it was written: the 1913
    /// Sanborn sheets, where the surveyor wrote every unit's storey count and frontage in feet
    /// under it, on all four faces of the crossing. Forty units. It is fitted, tested against the
    /// transcription in docs/research/COMMERCIAL-ROW.md - and until now NOTHING IN THE GAME HAS
    /// EVER CALLED IT. A generator with no consumer, sitting beside a downtown built from the one
    /// source that cannot describe 1991.
    ///
    /// 1913 is the wrong decade too. It is wrong in a direction that can be reasoned about: it
    /// shows the town before the target rather than the town that replaced it, and the FABRIC -
    /// party walls, unit widths, brick giving out at the ends - changes on a century where the
    /// trades change on a decade. A rule read off 1913 is a better guess at 1991 than a photograph
    /// of 2016.
    ///
    /// ONE BUILDING, SEVERAL NARROW SHOPS. The owner, on 112 South Chicago: "they were several
    /// narrow shops... but in the same building". That is what a terrace is and what this lays -
    /// a continuous run along the frontage, subdivided by party walls, every unit square to the
    /// pavement. Not a row of detached boxes with gaps between them - each storefront is its own
    /// Place so it can carry its own business, but they are laid edge to edge with zero gap and
    /// left to the massing grammars to render as one row, the same way the 41 hand-placed
    /// downtown units already do.
    /// </summary>
    public static class DowntownFromSanborn
    {
        /// <summary>
        /// The Attica × Chicago crossing, in village metres. The whole 1913 rule is expressed as
        /// decay away from this point - widths narrow and storeys drop with distance from it - so
        /// it is the one coordinate this pass cannot derive and has to be told.
        /// </summary>
        public static readonly Vector2 Crossing = new Vector2(761f, 1378f);

        /// <summary>
        /// How deep a downtown unit runs back from the pavement.
        ///
        /// NOT SURVEYED. COMMERCIAL-ROW.md is explicit that the sheet annotates no depth figure
        /// and that the units are not uniformly deep. So this is taken from the terraces that
        /// SURVIVED - the median depth of the measured downtown footprints - which is a
        /// measurement of the same buildings rather than a number invented to fill the gap.
        /// </summary>
        public const float DepthMetres = 18f;

        /// <summary>A unit narrower than this is not a shop; see CommercialRow.NarrowestFeet.</summary>
        private const float TooNarrow = 14f * CommercialRow.Foot;

        /// <summary>
        /// Lay a terrace on every lot the owner has ruled a shop whose measured footprint he has
        /// dated later than 1991. Returns how many rows were laid.
        /// </summary>
        public static int Apply(VillageLayout layout, IRng rng)
        {
            if (layout == null || rng == null) return 0;

            int rows = 0, units = 0;
            var add = new List<PlaceSpec>();
            var drop = new List<PlaceSpec>();

            foreach (var lot in ParcelIndex.In1991)
            {
                var ruled = Rulings.For(lot.Id);
                if (!ruled.FootprintIsLater) continue;
                if (ruled.Was != Rulings.Stood.Built) continue;

                var front = FrontageOf(layout, lot, out var alongDir, out var backDir, out float startDist);
                if (front.Length < TooNarrow) continue;

                var laid = CommercialRow.Lay(front.Length, rng);
                if (laid.Length == 0) continue;

                // EVERYTHING ALREADY STANDING ON THIS LOT COMES OFF. The generator put a building
                // here by its own rules before this pass ran, and the terrace replaces it rather
                // than joining it - two buildings on one frontage is exactly the tangle the
                // seating pass spends its time avoiding.
                foreach (var p in layout.Places)
                {
                    if (!p.IsBuilding) continue;
                    var b = p.Bounds;
                    var mid = new Vector2(b.X + b.W / 2f, b.Y + b.H / 2f);
                    var on = ParcelIndex.Find(mid);
                    if (on != null && on.Value.Id == lot.Id) drop.Add(p);
                }

                // EACH STOREFRONT ITS OWN PLACE, laid edge to edge along the frontage with zero
                // gap - the fabric is exactly what it was when this was one merged box, just cut
                // at the same seams CommercialRow already computed. Splitting it is what lets each
                // one carry its own business, kind, jobs and hours instead of all of them sharing
                // whatever the single ruling on the row said.
                string address = CountyRecord.For(lot.Id)?.Address;
                int index = 0;
                int laidHere = 0;

                foreach (var unit in laid)
                {
                    index++;
                    var corners = new List<Tile>();
                    var a0 = front.Start + alongDir * unit.Offset;
                    var a1 = front.Start + alongDir * unit.End;
                    var b1 = a1 + backDir * DepthMetres;
                    var b0 = a0 + backDir * DepthMetres;
                    corners.Add(ToTile(a0));
                    corners.Add(ToTile(a1));
                    corners.Add(ToTile(b1));
                    corners.Add(ToTile(b0));
                    corners.Add(ToTile(a0));           // closed

                    int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                    foreach (var t in corners)
                    {
                        if (t.X < minX) minX = t.X; if (t.X > maxX) maxX = t.X;
                        if (t.Y < minY) minY = t.Y; if (t.Y > maxY) maxY = t.Y;
                    }
                    int w = maxX - minX, h = maxY - minY;
                    if (w < 3 || h < 3) continue;

                    var spec = new PlaceSpec
                    {
                        Kind = PlaceKind.Shop,
                        Bounds = new TileRect(minX, minY, w, h),
                        Outline = corners.ToArray(),
                        Name = CommercialRow.HandleFor(address, lot.Id, index),
                    };
                    add.Add(spec);
                    laidHere++;
                }

                if (laidHere == 0) continue;
                rows++;
                units += laidHere;
            }

            foreach (var p in drop) layout.Places.Remove(p);
            foreach (var p in add) layout.Places.Add(p);

            if (rows > 0)
                Debug.Log($"[survey] {rows} downtown terrace(s) laid from the 1913 survey - "
                        + $"{units} independently-rulable shop units across them, replacing "
                        + $"{drop.Count} raised from post-2000 sources.");
            return rows;
        }

        private struct Frontage
        {
            public Vector2 Start;
            public float Length;
            public string Street;
        }

        /// <summary>
        /// Which edge of a lot faces its street, which way the terrace runs along it, and which
        /// way is back into the lot. The row is laid from the end NEAREST the crossing, because
        /// that is the direction CommercialRow's decay is measured in.
        /// </summary>
        private static Frontage FrontageOf(VillageLayout layout, ParcelIndex.Parcel lot,
                                           out Vector2 along, out Vector2 back, out float startDist)
        {
            along = Vector2.right; back = Vector2.up; startDist = 0f;
            var f = new Frontage { Length = 0f, Street = "" };

            var ring = lot.Points;
            if (ring == null || ring.Length < 3) return f;

            var centre = Vector2.zero;
            for (int i = 0; i < ring.Length; i++) centre += ring[i];
            centre /= ring.Length;

            // The road this lot fronts, and the direction of its kerb.
            var road = NearestRoad(layout, centre, out var tangent, out var normal, out string name);
            if (road < 0f) return f;

            // Project the lot onto the street direction: its extent along the kerb is its frontage.
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in ring)
            {
                float t = Vector2.Dot(p, tangent);
                if (t < lo) lo = t;
                if (t > hi) hi = t;
            }
            float len = hi - lo;
            if (len <= 0f) return f;

            // The frontage line sits on the lot edge closest to the road, not through its middle.
            float nearest = float.MaxValue;
            foreach (var p in ring)
            {
                float d = Vector2.Dot(p, normal);
                if (d < nearest) nearest = d;
            }

            // Run the row from whichever end is closer to the crossing.
            var endA = tangent * lo + normal * nearest;
            var endB = tangent * hi + normal * nearest;
            bool fromA = (endA - Crossing).sqrMagnitude <= (endB - Crossing).sqrMagnitude;

            along = fromA ? tangent : -tangent;
            back = normal;                       // normal points from the road into the lot
            f.Start = fromA ? endA : endB;
            f.Length = len;
            f.Street = name;
            startDist = Vector2.Distance(f.Start, Crossing);
            return f;
        }

        /// <summary>Distance to the nearest street centreline, with its direction and the way in
        /// from it. Negative when there is no road at all.</summary>
        private static float NearestRoad(VillageLayout layout, Vector2 at,
                                         out Vector2 tangent, out Vector2 normal, out string name)
        {
            tangent = Vector2.right; normal = Vector2.up; name = "";
            if (layout == null || layout.Roads.Count == 0) return -1f;

            float best = float.MaxValue;
            foreach (var line in layout.Roads)
            {
                var pts = line.Points;
                if (pts == null || pts.Count < 2) continue;
                for (int i = 1; i < pts.Count; i++)
                {
                    var a = new Vector2(pts[i - 1].X, pts[i - 1].Y);
                    var b = new Vector2(pts[i].X, pts[i].Y);
                    var d = b - a;
                    float L2 = d.sqrMagnitude;
                    float t = L2 <= 0f ? 0f : Mathf.Clamp01(Vector2.Dot(at - a, d) / L2);
                    var q = a + d * t;
                    float dist = Vector2.Distance(at, q);
                    if (dist >= best) continue;
                    best = dist;
                    var dir = d.normalized;
                    tangent = dir;
                    var n = new Vector2(-dir.y, dir.x);
                    // point it from the road towards the lot
                    normal = Vector2.Dot(at - q, n) >= 0f ? n : -n;
                    name = line.Name;
                }
            }
            return best == float.MaxValue ? -1f : best;
        }

        private static Tile ToTile(Vector2 v) =>
            new Tile(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y));
    }
}
