using System.Collections.Generic;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.World;
using Noir.Core.Survey;

namespace Noir.Unity
{
    /// <summary>
    /// Puts up the buildings the survey found and the map never had.
    ///
    /// Content/parcel-buildings.txt records a building on 572 of Rossville's lots. Content/city.txt
    /// has one on 229. The missing 343 are the same hole the owner's own 1991 rulings show from
    /// the other side - 91 lots ruled `built` with nothing standing on them - and they are most of
    /// why the town reads as emptier than the place it is a model of.
    ///
    /// ONE BUILDING PER LOT, THE MAIN ONE. Outbuildings are in the survey too, and they are left
    /// out on purpose: a garage stamped as a dwelling is a house, with a household living in it
    /// and a front door onto the alley. The primary structure is the house; the rest is a later
    /// job that needs kinds that are sheds.
    ///
    /// WHAT KIND IT IS, in order of who knows best: the owner's own 1991 ruling for that lot, then
    /// the federal occupancy class the footprint carries, then a dwelling - which is what nine in
    /// ten of these are.
    ///
    /// THREE GUARDS, because adding 343 buildings to a working town can break it in ways adding
    /// one cannot: nothing is put up overlapping a building that already exists, nothing is put up
    /// on a road, and nothing too small to hold an interior is put up at all. A lot that fails any
    /// of them keeps its empty ground, which is what it has today.
    /// </summary>
    public static class FillFromSurvey
    {
        public static int Apply(VillageLayout layout)
        {
            if (layout == null || ParcelBuildings.Count == 0) return 0;

            var kinds = PlaceKindTable.Current;
            if (kinds == null) return 0;

            // Everything already standing, so nothing is put up on top of it. Includes the open
            // places - a house in the middle of the green is as wrong as one inside a barn.
            var standing = new List<TileRect>(layout.Places.Count);
            var lotsTaken = new HashSet<int>();
            foreach (var place in layout.Places)
            {
                standing.Add(place.Bounds);
                var b = place.Bounds;
                var lot = ParcelIndex.Find(new Vector2(b.X + b.W / 2f, b.Y + b.H / 2f));
                if (lot != null && place.IsBuilding) lotsTaken.Add(lot.Value.Id);
            }

            int built = 0, onRoad = 0, tooTight = 0, tooSmall = 0, notABuilding = 0;
            foreach (var parcel in ParcelIndex.In1991)
            {
                if (lotsTaken.Contains(parcel.Id)) continue;
                if (Rulings.Absent(parcel.Id)) continue;

                // A lot the owner has ruled empty stays empty. The survey is 2016 imagery and
                // the game is 1991; where they disagree the person who was there wins.
                var was = Rulings.For(parcel.Id).Was;
                if (was == Rulings.Stood.Vacant) continue;

                var primary = ParcelBuildings.PrimaryOf(parcel.Id);
                if (primary == null || primary.Which != ParcelBuildings.Role.Primary) continue;

                // EVERY REFUSAL IS REPORTED BACK, not just counted. From the browser map a lot the
                // game declined to build on looks exactly like one it built on - the owner rules
                // it, no house appears, and there is no symptom anywhere. SurveyReport is how the
                // map gets to say why.
                var box = SeatOnSurvey.BoxOf(primary, out var outline);
                if (box.W < SeatOnSurvey.Smallest || box.H < SeatOnSurvey.Smallest)
                {
                    tooSmall++;
                    SurveyReport.Say(parcel.Id, false, "too small to hold an interior");
                    continue;
                }

                bool clash = false;
                foreach (var t in standing)
                    if (box.Overlaps(t)) { clash = true; break; }
                if (clash)
                {
                    tooTight++;
                    SurveyReport.Say(parcel.Id, false, "would overlap what is already there");
                    continue;
                }

                if (OnARoad(layout, box))
                {
                    onRoad++;
                    SurveyReport.Say(parcel.Id, false, "would stand in a road");
                    continue;
                }

                if (!KindFor(parcel.Id, primary, kinds, out var kind))
                {
                    notABuilding++;
                    SurveyReport.Say(parcel.Id, false, "ruled to be something that is not a building");
                    continue;
                }
                var door = DoorFacingTheRoad(layout, box, outline);
                if (outline != null && !SeatOnSurvey.Covers(outline, door)) outline = null;

                var row = kinds.Row(kind);
                var spec = new PlaceSpec
                {
                    Kind = kind,
                    Name = NameFor(parcel.Id, primary),
                    Bounds = box,
                    Door = door,
                    Outline = outline,
                    JobSlots = row.Jobs,

                    // KEYED BY THE LOT, NOT BY THE NAME. Everything inside a building is generated
                    // from its key and WorldBuilder refuses two places that share one - and these
                    // names come from a federal address field that is empty on some records and
                    // repeated on others. The lot number is neither.
                    Key = "survey lot " + parcel.Id,
                };
                foreach (var window in row.Hours) spec.Hours.Add(window);

                // THE OWNER'S FLOOR PLAN, ON THE BUILDINGS THIS PASS PUTS UP TOO.
                //
                // Until 2026-08-20 this attach existed in SeatOnSurvey alone, so a plan could
                // only reach a building that city.txt had ALREADY put on the lot. 408 Holmes
                // Street is the case that found it: no place in city.txt stands on parcel 673 at
                // all (the town's other 408, "408 Holmes Ave", is a stale hand-guess on parcel
                // 777 at a dead end), so the house is one of the 313 RAISED here - it builds, the
                // owner's Residence408 model stands on it, and both of the plans he drew for it
                // went unread every single build with the log saying only "never seated".
                //
                // Same three arguments as the other attach, from the same three facts this pass
                // already has in hand: the measured entry's own parcel and index, so the tag
                // matches Content/floorplans/<parcel>-<index>.json and the browser map's own key;
                // the box it is about to stand on; and the door it just faced at the road. Units
                // is 1 on everything raised here (VillageLayout's own default - this pass mints
                // no terraces), and it is read off the spec rather than written as a literal so
                // that stays true by construction if it ever stops being.
                bool ownerModel = CityBuildings.IsOwnerModel(spec.Name);
                spec.AuthoredInterior = FloorPlans.For(parcel.Id, primary.Index, box, door,
                                                       ownerModel, spec.Units,
                                                       out var authoredOutline);

                // THE AUTHORED OUTLINE WINS, exactly as it does in SeatOnSurvey: the survey
                // decides the size, the owner decides when he says otherwise.
                if (authoredOutline != null) spec.Outline = authoredOutline;

                layout.Places.Add(spec);
                standing.Add(box);
                built++;
                SurveyReport.Say(parcel.Id, true, "raised from the survey");

                // And what the map is told was built here - see InteriorsReport's own header for
                // why this is a note now and a look-up after the world exists.
                InteriorsReport.Note(parcel.Id, primary.Index, spec,
                                     spec.AuthoredInterior != null);
            }

            Debug.Log($"[survey] {built} buildings put up from the survey"
                    + $" ({onRoad} skipped as standing on a road, {tooTight} as overlapping "
                    + $"something already there, {tooSmall} as too small to hold an interior, "
                    + $"{notABuilding} because the owner ruled that ground is not a building).");
            return built;
        }

        /// <summary>
        /// The owner first, then the federal occupancy class, then a house.
        ///
        /// False means put nothing here. That is the answer when the owner has ruled the lot to be
        /// something that is not a building at all - a cemetery, a park, the railway. The imagery
        /// found a structure on it and the owner says what the ground is; a house is the one
        /// answer that is certainly wrong, and it is the answer a fallback would give.
        /// </summary>
        private static bool KindFor(int parcelId, ParcelBuildings.Entry e, PlaceKindTable kinds,
                                    out PlaceKind kind)
        {
            var ruled = Rulings.For(parcelId).Kind;
            if (ruled.Length > 0 && kinds.TryKindOf(ruled.ToLowerInvariant(), out var byRuling))
            {
                kind = byRuling;
                return kinds.Row(byRuling).IsBuilding;
            }

            string word;
            switch (e.Zoning)
            {
                case ParcelNotes.Zoning.Commercial:   word = "shop";        break;
                case ParcelNotes.Zoning.Industrial:   word = "factory";     break;
                case ParcelNotes.Zoning.Civic:        word = "villagehall"; break;
                case ParcelNotes.Zoning.Agricultural: word = "barn";        break;
                default:                              word = "dwelling";    break;
            }
            if (kinds.TryKindOf(word, out kind)) return true;
            return kinds.TryKindOf("dwelling", out kind);
        }

        /// <summary>The federal situs address, then the county's, then nothing - the key carries
        /// identity, so an unnamed building is only an unlabelled one.</summary>
        private static string NameFor(int parcelId, ParcelBuildings.Entry e)
        {
            if (!string.IsNullOrWhiteSpace(e.Address)) return e.Address.Trim();
            var county = CountyRecord.For(parcelId);
            return county?.Address ?? "";
        }

        /// <summary>
        /// Whether this box lands on a road corridor.
        ///
        /// Cheap and deliberately generous - the corridor is treated as a band of its own width
        /// about each segment, so a building near a kerb is refused along with one in the middle
        /// of the street. A house that does not go up is a gap; a house in the road is a town
        /// whose own audit fails.
        /// </summary>
        private static bool OnARoad(VillageLayout layout, TileRect box)
        {
            float cx = box.X + box.W / 2f, cy = box.Y + box.H / 2f;
            float reach = Mathf.Max(box.W, box.H) / 2f;

            foreach (var run in layout.Roads)
            {
                float half = run.Width / 2f + reach;
                var pts = run.Points;
                for (int i = 1; i < pts.Count; i++)
                    if (PointToSegment(cx, cy, pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y) < half)
                        return true;
            }
            return false;
        }

        private static float PointToSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float len = dx * dx + dy * dy;
            float t = len <= 0f ? 0f : Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / len);
            float qx = ax + dx * t, qy = ay + dy * t;
            return Mathf.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
        }

        /// <summary>
        /// The front door on the side the street is. A door facing the back fence is not merely
        /// odd to look at - it is the tile everybody walking to this building has to reach, so
        /// putting it on the wrong side sends every visit the long way round the block.
        /// </summary>
        private static Tile DoorFacingTheRoad(VillageLayout layout, TileRect box, Tile[] outline)
        {
            float cx = box.X + box.W / 2f, cy = box.Y + box.H / 2f;
            float best = float.MaxValue, bx = cx, by = cy - 1f;

            foreach (var run in layout.Roads)
            foreach (var p in run.Points)
            {
                float d = (p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy);
                if (d < best) { best = d; bx = p.X; by = p.Y; }
            }

            Tile at; int dx = 0, dy = 0;
            if (Mathf.Abs(bx - cx) > Mathf.Abs(by - cy))
            {
                if (bx > cx) { at = new Tile(box.Right, box.Y + box.H / 2); dx = -1; }
                else         { at = new Tile(box.Left,  box.Y + box.H / 2); dx = 1; }
            }
            else
            {
                if (by > cy) { at = new Tile(box.X + box.W / 2, box.Bottom); dy = -1; }
                else         { at = new Tile(box.X + box.W / 2, box.Top);    dy = 1; }
            }

            if (outline == null) return at;

            for (int step = 0; step < Mathf.Max(box.W, box.H); step++)
            {
                var t = new Tile(at.X + dx * step, at.Y + dy * step);
                if (!box.Contains(t)) break;
                if (SeatOnSurvey.Covers(outline, t)) return t;
            }
            return at;
        }
    }
}
