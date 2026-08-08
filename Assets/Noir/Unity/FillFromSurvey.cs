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

            // THE CURVE, NOT THE CHORD. WorldBuilder runs Catmull-Rom through the authored road
            // points, so the centreline the game draws is off the declared polyline at every bend.
            // This pass was refusing houses against the chord while the town was built round the
            // curve, which is how buildings kept turning up in roads that every check called clear.
            var corridors = new RoadCorridor.Corridors(layout);
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

                if (OnARoad(corridors, box))
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

                layout.Places.Add(spec);
                standing.Add(box);
                built++;
                SurveyReport.Say(parcel.Id, true, "raised from the survey");
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
        private static bool OnARoad(RoadCorridor.Corridors roads, TileRect box)
        {
            // THE BOX, NOT A CIRCLE ROUND ITS CENTRE. This used to measure the distance from the
            // box's CENTRE and add half its longest side as slack, which treats every building as
            // a disc: a 30 m school was refused anywhere within 15 m of a kerb it was nowhere
            // near, on its short axis as well as its long one. Corridors tests the box itself -
            // its corners, its edge midpoints and its centre - against the curve the game draws.
            //
            // Clearance is kept, and is the honest part of the old test: this pass is CHOOSING
            // where to put a building rather than honouring a measurement, so it should leave
            // room between a house it invents and the kerb rather than butt one against it.
            return roads.WorstPenetration(box, 0) > 0f || roads.WorstPenetration(Grown(box), 0) > 0f;
        }

        /// <summary>
        /// The box with a kerb's worth of breathing room round it.
        ///
        /// ONE METRE, and three was measured to be wrong: at three this pass refused 153 lots
        /// instead of 23 and the town lost 129 houses, because a 13x7 house set back six metres
        /// from a ten-metre road is one metre off the kerb - which is what a house on a Rossville
        /// street actually looks like. The clearance is here to stop a NEW building being butted
        /// against the tarmac, not to enforce a front garden the town does not have.
        /// </summary>
        private static TileRect Grown(TileRect b)
        {
            const int Clearance = 1;
            return new TileRect(b.X - Clearance, b.Y - Clearance,
                                b.W + Clearance * 2, b.H + Clearance * 2);
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
