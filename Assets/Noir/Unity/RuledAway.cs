using System.Collections.Generic;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// Takes down the buildings standing on ground the owner says had no building on it in 1991.
    ///
    /// WHY IT IS A FILTER AND NOT AN EDIT. Content/city.txt holds 477 authored places and has been
    /// hand-edited across dozens of commits; the rulings in Content/parcel-1991.txt were made
    /// separately, in the browser map, and the two had never been introduced. Reconciling them by
    /// rewriting city.txt would mean the owner could not change a ruling back without somebody
    /// regenerating the map. Removing them from the parsed layout instead means the ruling is the
    /// only thing that decides, every load, and re-ruling a lot in the browser map hands the
    /// building back on the next Play.
    ///
    /// ABSENT DOES NOT MEAN THE GROUND WAS NOT THERE. It means the LOT was not - the county's
    /// boundaries are today's, and ground subdivided out of a field in 1998 has no business on a
    /// map of 1991. In 1991 that ground was the field it was cut from. So only places whose kind
    /// is a BUILDING come down: the cornfields, copses and open ground on those same lots are not
    /// mistakes, they are what was actually there, and sweeping them away would empty the
    /// countryside to fix the houses. `form building|open` in Content/kinds.txt is the test.
    ///
    /// VACANT IS THE SAME ACT FOR A DIFFERENT REASON: the lot was there, and nothing stood on it.
    ///
    /// Applied next to SurveyRoads.Apply, on the layout, before the world is built - so all 23
    /// things that walk AllPlaces see the same town, rather than 23 chances to forget one.
    /// </summary>
    public static class RuledAway
    {
        /// <summary>Removes the ruled-away buildings and returns how many went. Safe with no
        /// rulings file and safe with no parcels file: both leave the layout alone.</summary>
        public static int Apply(VillageLayout layout)
        {
            if (layout == null) return 0;
            TakeAwayRoads(layout);
            if (Rulings.Count == 0) return 0;

            int gone = 0;
            layout.Places.RemoveAll(place =>
            {
                if (!place.IsBuilding) return false;          // a field on a field is not a fault

                var b = place.Bounds;
                var centre = new Vector2(b.X + b.W / 2f, b.Y + b.H / 2f);

                // FindIncludingGone, not Find: the lot this stands on is exactly the one that has
                // been filtered out of the town, so asking the filtered index would never find it.
                var lot = ParcelIndex.FindIncludingGone(centre);
                if (lot == null) return false;

                var was = Rulings.For(lot.Value.Id).Was;
                if (was != Rulings.Stood.Absent && was != Rulings.Stood.Vacant) return false;

                gone++;
                SurveyReport.Say(lot.Value.Id, false,
                                 was == Rulings.Stood.Absent ? "taken down - no lot here in 1991"
                                                             : "taken down - the lot was empty");
                return true;
            });

            if (gone > 0)
                Debug.Log($"[1991] {gone} buildings taken down - they stand on lots ruled "
                        + "absent or vacant. Change the ruling in the browser map to get them back.");
            return gone;
        }

        /// <summary>
        /// The roads that were not there in 1991, taken off the map.
        ///
        /// The same act as ruling a lot absent and for the same reason: Content/roads.txt is the
        /// county's network as it stands TODAY, and a street cut through in 1998 has no business
        /// on a map of 1991. An absent road stops being drawn, stops being driven and stops being
        /// walked, because it is removed from the layout before the world is built at all - so
        /// nothing downstream has to know the ruling exists.
        ///
        /// Whole roads only. A road that was HALF built in 1991 is a block-level question and the
        /// file has the shape for it; splitting a run at a block boundary is a different job and
        /// is not pretended at here.
        /// </summary>
        private static void TakeAwayRoads(VillageLayout layout)
        {
            int whole = layout.Roads.RemoveAll(run => !RoadRulings.Existed(run.Name));

            // AND THE ROADS THAT WERE ONLY EXTENDED. Some of these were built out after 1991 and
            // only the new end should come off, so the run is CUT rather than dropped: the blocks
            // ruled away are taken out of the polyline and what survives comes back as one run per
            // surviving stretch. A road cut in the middle legitimately becomes two.
            int cut = 0, pieces = 0;
            var extra = new List<RoadRun>();
            foreach (var run in layout.Roads)
            {
                if (run.Points.Count < 2 || !RoadRulings.AnyBlockGone(run.Name)) continue;

                var spans = Surviving(run);
                if (spans.Count == 0) continue;              // handled by the sweep below
                if (spans.Count == 1 && spans[0].From <= 0.01f && spans[0].To >= Length(run.Points) - 0.01f)
                    continue;                                // nothing actually removed

                cut++;
                var first = SubPath(run.Points, spans[0].From, spans[0].To);
                for (int i = 1; i < spans.Count; i++)
                {
                    var more = SubPath(run.Points, spans[i].From, spans[i].To);
                    if (more.Count < 2) continue;
                    var clone = new RoadRun
                    {
                        Kind = run.Kind, Name = run.Name, Width = run.Width,
                        Easement = run.Easement, Class = run.Class, Carries = run.Carries,
                    };
                    clone.Points.AddRange(more);
                    extra.Add(clone);
                    pieces++;
                }

                run.Points.Clear();
                run.Points.AddRange(first);
                pieces++;
            }
            layout.Roads.AddRange(extra);

            // A run cut down to nothing is a road that was entirely in the removed blocks.
            int emptied = layout.Roads.RemoveAll(r => r.Points.Count < 2);

            if (whole == 0 && cut == 0) return;
            var named = new List<string>(RoadRulings.Gone);
            Debug.Log($"[1991] {whole} road line(s) taken off the map"
                    + (named.Count > 0 ? " - " + string.Join(", ", named) : "")
                    + (cut > 0 ? $"; {cut} more cut back to {pieces} surviving stretch(es)" : "")
                    + (emptied > 0 ? $"; {emptied} left with nothing" : "") + ".");
        }

        private readonly struct Span
        {
            public readonly float From, To;
            public Span(float from, float to) { From = from; To = to; }
        }

        /// <summary>The stretches of this road that were there in 1991, in metres along it.</summary>
        private static List<Span> Surviving(RoadRun run)
        {
            float total = Length(run.Points);
            var keep = new List<Span>();
            float open = 0f;

            // The blocks are already in order along the road, which is how build-road-blocks.py
            // writes them, so this is one walk and not a sort.
            foreach (var blk in RoadRulings.Blocks)
            {
                if (blk.Road != run.Name) continue;
                if (RoadRulings.ExistedAt(run.Name, (blk.Start + blk.End) * 0.5f)) continue;

                if (blk.Start > open) keep.Add(new Span(open, Mathf.Min(blk.Start, total)));
                open = Mathf.Max(open, blk.End);
            }
            if (open < total) keep.Add(new Span(open, total));

            keep.RemoveAll(s => s.To - s.From < 1f);          // a metre of road is not a road
            return keep;
        }

        private static float Length(List<Tile> pts)
        {
            float run = 0f;
            for (int i = 1; i < pts.Count; i++)
                run += Mathf.Sqrt((pts[i].X - pts[i - 1].X) * (pts[i].X - pts[i - 1].X)
                                + (pts[i].Y - pts[i - 1].Y) * (pts[i].Y - pts[i - 1].Y));
            return run;
        }

        /// <summary>The part of a polyline between two distances along it, ends included.</summary>
        private static List<Tile> SubPath(List<Tile> pts, float from, float to)
        {
            var outp = new List<Tile>();
            float run = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1];
                var b = pts[i];
                float dx = b.X - a.X, dy = b.Y - a.Y;
                float len = Mathf.Sqrt(dx * dx + dy * dy);
                if (len < 1e-4f) continue;

                float t0 = Mathf.Clamp01((from - run) / len);
                float t1 = Mathf.Clamp01((to - run) / len);
                run += len;
                if (t1 <= t0) continue;

                var p = new Tile(Mathf.RoundToInt(a.X + dx * t0), Mathf.RoundToInt(a.Y + dy * t0));
                if (outp.Count == 0 || outp[outp.Count - 1].X != p.X || outp[outp.Count - 1].Y != p.Y)
                    outp.Add(p);
                var q = new Tile(Mathf.RoundToInt(a.X + dx * t1), Mathf.RoundToInt(a.Y + dy * t1));
                if (outp[outp.Count - 1].X != q.X || outp[outp.Count - 1].Y != q.Y) outp.Add(q);
            }
            return outp;
        }
    }
}
