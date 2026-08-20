using System.Collections.Generic;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.World;
using Noir.Core.Survey;

namespace Noir.Unity
{
    /// <summary>
    /// Moves each generated building onto the building that was actually measured on its lot.
    ///
    /// WHAT WAS WRONG. Every place in Content/city.txt carries a box the generator chose, and the
    /// massing grammars extrude the town from those boxes. There are seventeen distinct box sizes
    /// in the whole of Rossville and every house is the same 13x7. Against the 824 footprints in
    /// Content/parcel-buildings.txt - traced from federal imagery and seated on the county's own
    /// parcels - the generated building sits a median of 10 metres from the real one and covers
    /// less than half its floor area. That is what "the houses are way off" is.
    ///
    /// WHAT THIS DOES. Takes the position and size from the measurement instead. The massing
    /// pipeline is untouched: it still extrudes a box, the box is just the right box now, in the
    /// right place. Building on the real OUTLINE rather than its rectangle is the next step and a
    /// change to VillageMesh, not to this.
    ///
    /// A FILTER, NOT AN EDIT, for the same reason as RuledAway: city.txt is hand-authored and
    /// re-seating happens every load, so regenerating parcel-buildings.txt moves the town with it
    /// and nothing has to be kept in step by hand.
    ///
    /// THE DOOR MOVES WITH THE BUILDING. A door is written as an absolute tile that must land on
    /// its own box's edge; leaving one behind while the walls move puts the door outside the
    /// house, and a door that is not in its own wall is what cut this town into two pieces once
    /// already. The new door goes on the same side it was on - which is the author's statement
    /// about which way the building faces - centred on that edge.
    /// </summary>
    public static class SeatOnSurvey
    {
        /// <summary>Smallest usable footprint, in tiles. Under this there is no room for an
        /// interior to be generated and the building is better left where it was.</summary>
        internal const int Smallest = 5;

        /// <summary>
        /// How far a measured footprint may reach into a street before it is refused, in metres.
        ///
        /// Not zero, and the reason is the survey rather than tolerance for error: the footprints
        /// are traced from imagery and the centrelines come from the county, so the two disagree
        /// by tens of centimetres all over the town without either being wrong. Half a metre is
        /// under the width of a kerbstone and well under anything a player could see; past it,
        /// masonry is on the tarmac.
        /// </summary>
        private const float IntoTheRoad = 0.5f;

        /// <summary>
        /// How much of its box a footprint has to fill before the box is taken at face value.
        ///
        /// An L-shaped building - a school with wings, a shop with a back ell - has a bounding box
        /// far larger than the building. Twenty of these fill less than 55% of their own box, and
        /// taking the box whole would put up a solid block covering the yard as well as the
        /// school. Below this the box is shrunk about its own centre until the measured area is a
        /// believable fill of it, which keeps the position and the bulk honest while a rectangle
        /// is still all that can be built. Step two - real outlines - is what actually fixes it.
        /// </summary>
        private const float LooseBox = 0.7f;
        private const float TargetFill = 0.85f;

        /// <summary>Above this much of its own bounding box filled, a footprint is a rectangle and
        /// is built as one. Handing over an outline here would cost a polygon test per tile to
        /// arrive back at the same walls.</summary>
        private const float Rectangular = 0.9f;

        public static int Apply(VillageLayout layout)
        {
            if (layout == null || ParcelBuildings.Count == 0) return 0;

            // Which places stand on which lot, biggest first - so the biggest building on a lot
            // is matched to the biggest thing measured on it, rather than by file order.
            var byLot = new Dictionary<int, List<PlaceSpec>>();
            foreach (var place in layout.Places)
            {
                if (!place.IsBuilding) continue;
                var b = place.Bounds;
                var lot = ParcelIndex.Find(new Vector2(b.X + b.W / 2f, b.Y + b.H / 2f));
                if (lot == null) continue;
                if (!byLot.TryGetValue(lot.Value.Id, out var list))
                    byLot[lot.Value.Id] = list = new List<PlaceSpec>();
                list.Add(place);
            }

            var seated = new List<(PlaceSpec Place, TileRect Was, TileRect Now, Tile Door,
                                  Tile[] Outline, int ParcelId, int Index)>();
            int skippedAsLater = 0;
            foreach (var pair in byLot)
            {
                var places = pair.Value;
                places.Sort((a, b) => b.Bounds.Area.CompareTo(a.Bounds.Area));

                // A FOOTPRINT THE OWNER HAS DATED LATER THAN 1991 IS NOT SEATED.
                //
                // 112 South Chicago is the case that found this. A block-long row of antique shops
                // and a restaurant stood there in 1991, burned in February 2004, and a Casey's was
                // built on the cleared ground. The lot is emphatically built on - but seating its
                // MEASURED footprint puts a set-back convenience store where storefronts met the
                // pavement, which is the shape of 2016 wearing the date of 1991.
                //
                // Skipping it here drops the lot through to FillFromSurvey, which raises a building
                // by rule: the right era and the right posture even where it is not the right
                // building. A rule is a better guess about 1991 than a photograph of 2016.
                if (Rulings.For(pair.Key).FootprintIsLater)
                {
                    skippedAsLater += places.Count;
                    NoteUnmoved(pair.Key, places);
                    continue;
                }

                var measured = ParcelBuildings.For(pair.Key);          // already largest first

                for (int i = 0; i < places.Count && i < measured.Count; i++)
                {
                    var box = BoxOf(measured[i], out var outline);
                    if (box.W < Smallest || box.H < Smallest)
                    {
                        // Too small to seat onto, so it stays on the generator's box - but it is
                        // still a building the game stands on this lot, and the map asks what got
                        // built here rather than what got MOVED here.
                        InteriorsReport.Note(measured[i].ParcelId, measured[i].Index, places[i], false);
                        continue;
                    }
                    var door = DoorFor(places[i], box, outline);
                    if (outline != null && !Covers(outline, door)) outline = null;
                    seated.Add((places[i], places[i].Bounds, box, door, outline,
                               measured[i].ParcelId, measured[i].Index));
                }
            }

            // A building moved onto its measurement can land on another building. The bigger one
            // keeps its ground and the smaller gives way, because two sets of walls in the same
            // tiles is worse than one building being where the generator put it.
            //
            // THE GROUND THAT IS NOT MOVING IS CLAIMED FIRST, and that is the half this was
            // missing. The guard compared a seated building only against other SEATED ones, so
            // anything that stayed put - a building with no measured footprint on its lot, which
            // is 77 of them - could be built straight over. Downtown is where it showed: a single
            // FEMA polygon there routinely covers a whole run of joined brick, so seating one
            // shopfront onto it laid that shopfront across three of its neighbours, and the plan
            // came out as a tangle of overlapping outlines along Chicago Street.
            // EVERY BUILDING'S CURRENT GROUND IS CLAIMED FIRST, its own included, and a building
            // that moves gives its old ground up as it goes.
            //
            // Claiming only the ones that were NOT moving was still not enough. A building whose
            // measured box clashed gave way and STAYED WHERE IT WAS - and its old position could
            // overlap the box another building had already moved onto, so the yield created the
            // very collision it was meant to avoid. The smoke test found the survivors on the
            // real town: Henderson's lying across the Attica Street market, the old printing
            // office across the hardware, 302 Thompson across 304.
            //
            // So the ledger holds where everything stands right now. A seated building lifts its
            // own claim, asks whether the new ground is clear, and puts one or the other back.
            // EVERY place, not only the buildings. A field is ground somebody has already spoken
            // for, and the world validator counts a barn standing inside one as an overlapping
            // place exactly as it counts two shops in the same tiles - which is how Home Farm 9
            // and Wicker End 10 came to be seated into the middle of the west belt.
            var taken = new List<TileRect>(layout.Places.Count);
            var claim = new Dictionary<PlaceSpec, int>();
            foreach (var place in layout.Places)
            {
                claim[place] = taken.Count;
                taken.Add(place.Bounds);
            }

            // THE CURVE THE GAME WILL DRAW, not the polyline the map declares. WorldBuilder runs
            // Catmull-Rom through the authored points, so at a bend the real centreline is off the
            // chord - and testing the chord let 45 measured buildings be seated into streets that
            // only the built town could see. Built once; it is a resample at one metre.
            var corridors = new RoadCorridor.Corridors(layout);

            seated.Sort((a, b) => b.Now.Area.CompareTo(a.Now.Area));
            int moved = 0, yielded = 0, shaped = 0, inTheRoad = 0;
            var nowhere = new TileRect(0, 0, 0, 0);   // an empty rect overlaps nothing
            foreach (var s in seated)
            {
                // IN A STREET. Until 2026-08-08 this pass had no road test of any kind, while
                // FillFromSurvey - the pass that INVENTS buildings - refused to raise one in a
                // road and said so. So the houses standing in the road were the measured ones,
                // which is why it was "not all but some" and why looking at the generator never
                // found it. Parcel 636 is the case that proves it: a 65x68 m footprint seated on
                // a 30x13 m lot, sprawling 3.8 m into Attica Street.
                //
                // ALLEYS ARE DELIBERATELY NOT COUNTED - see RoadCorridor.StreetWidth. Their 4 m
                // is a flat guess from build-roads.py's corridor table and the building is a
                // measurement; a garage on the alley line is Rossville, not a fault.
                // THE OUTLINE, NOT THE BOX, wherever there is one. The grade school stands on
                // three parcels in an L; its bounding box takes in the yard and crosses Benton,
                // Green and Chicago, none of which the school is anywhere near. Testing the box
                // refuses the school and the town loses it.
                float pen = s.Outline != null && s.Outline.Length >= 3
                    ? corridors.WorstPenetration(s.Outline, RoadCorridor.StreetWidth)
                    : corridors.WorstPenetration(s.Now, RoadCorridor.StreetWidth);
                if (pen > IntoTheRoad)
                {
                    inTheRoad++;
                    var road = corridors.RoadUnder(s.Now, RoadCorridor.StreetWidth);
                    var at = ParcelIndex.Find(new Vector2(s.Was.X + s.Was.W / 2f, s.Was.Y + s.Was.H / 2f));
                    if (at != null)
                        SurveyReport.Say(at.Value.Id, false,
                                         $"measured footprint stands {pen:0.0} m into {road} - not seated");
                    InteriorsReport.Note(s.ParcelId, s.Index, s.Place, false);
                    continue;                         // keep the authored box, which is set back
                }

                int mine = claim[s.Place];
                var wasClaiming = taken[mine];
                taken[mine] = nowhere;                // stand aside while asking about the new spot

                bool clash = false;
                foreach (var t in taken)
                    if (s.Now.Overlaps(t)) { clash = true; break; }

                if (clash)
                {
                    taken[mine] = wasClaiming;        // stay put, and keep holding the old ground
                    yielded++;

                    var b = s.Was;
                    var lot = ParcelIndex.Find(new Vector2(b.X + b.W / 2f, b.Y + b.H / 2f));
                    if (lot != null)
                        SurveyReport.Say(lot.Value.Id, true,
                                         "left where it was - the measured spot was taken");
                    InteriorsReport.Note(s.ParcelId, s.Index, s.Place, false);
                    continue;
                }

                taken[mine] = s.Now;
                s.Place.Bounds = s.Now;
                if (s.Door.IsValid) s.Place.Door = s.Door;
                s.Place.Outline = s.Outline;

                // The owner's floor plan, if he drew one for this building - converted to
                // tiles here, where the parcel, the seated bounds and the door are all in
                // hand, and handed to Core the same way the measured outline is.
                bool ownerModel = CityBuildings.IsOwnerModel(s.Place.Name);
                // s.Place.Door, not s.Door: the line above only overwrites the place's door when
                // the MEASURED one came out valid, so a building whose measured door could not be
                // derived still has a perfectly good authored one to orient the plan by. Passing
                // the measured Tile.None refused those plans for having no door at all.
                s.Place.AuthoredInterior = FloorPlans.For(s.ParcelId, s.Index, s.Now,
                                                          s.Place.Door, ownerModel, s.Place.Units,
                                                          out var authoredOutline);

                // THE AUTHORED OUTLINE WINS. s.Place.Outline already carries the measured ring
                // (assigned above, before this call) - FloorPlans.For only hands one back when
                // this plan carries a valid redrawn override, so overwriting here is exactly the
                // "survey wins on size, the owner wins when he says otherwise" rule the measured
                // ring itself follows for everything else this pass does.
                if (authoredOutline != null) s.Place.Outline = authoredOutline;

                // What Write() will need once the world exists to say what actually got built
                // here - see InteriorsReport's own header for why this can only be a note now
                // and a lookup later.
                InteriorsReport.Note(s.ParcelId, s.Index, s.Place, s.Place.AuthoredInterior != null);

                moved++;
                // s.Place.Outline, not s.Outline: a rectangular measured footprint (s.Outline ==
                // null) the owner then redrew a real shape for is just as "built to its real
                // outline" as one the county's imagery already gave a ring - the override above
                // is what s.Place actually carries into the world, and the census should count
                // what got built, not what the survey alone would have built.
                if (s.Place.Outline != null) shaped++;
            }

            Debug.Log($"[survey] {moved} buildings seated on their measured footprint, "
                    + $"{shaped} of them built to its real outline"
                    + (yielded > 0 ? $", {yielded} left alone to avoid overlapping one" : "")
                    + (inTheRoad > 0 ? $", {inTheRoad} REFUSED for standing in a street" : "")
                    + (skippedAsLater > 0
                        ? $", {skippedAsLater} left to the rules because the owner dated the "
                        + "footprint later than 1991" : "") + ".");
            return moved;
        }

        /// <summary>
        /// Tell <see cref="InteriorsReport"/> about buildings on a lot this pass is giving up on
        /// wholesale, so the map still hears what got built there.
        ///
        /// Paired to the measured entries the same way the seating loop pairs them - both lists
        /// are largest first - because <c>&lt;parcel&gt;-&lt;index&gt;</c> is the browser map's own
        /// key for a building and an index that means anything else is worse than none. A place
        /// past the end of the measured list has no footprint on the map to be keyed against at
        /// all, and is deliberately left out rather than given an invented index.
        /// </summary>
        private static void NoteUnmoved(int lotId, List<PlaceSpec> places)
        {
            var measured = ParcelBuildings.For(lotId);
            for (int i = 0; i < places.Count && i < measured.Count; i++)
                InteriorsReport.Note(measured[i].ParcelId, measured[i].Index, places[i], false);
        }

        /// <summary>
        /// The measured footprint as a box in tiles, squared back to its lot first - and, where
        /// the building is not a rectangle, the outline to cut that box back to.
        ///
        /// A footprint filling nearly all of its own bounding box IS a rectangle, and handing one
        /// over would cost a polygon test per tile to arrive back at the same building. Below
        /// that, the shape is worth keeping and the box is left at full size, because the outline
        /// is what removes the parts that are not the building. The shrink is only for the case
        /// where there is no outline to do it properly.
        /// </summary>
        internal static TileRect BoxOf(ParcelBuildings.Entry e, out Tile[] outline)
        {
            outline = null;

            // SQUARED FIRST, THEN THE OWNER'S ADJUSTMENT, and that order is the whole contract.
            // Squaring is a correction to the TRACING - a footprint two degrees off is more likely
            // badly drawn than genuinely askew - and the owner's turn is a correction to the
            // result. Applied the other way round the squaring would argue with him and a house he
            // had lined up by eye would come out a couple of degrees off. Placements is an overlay
            // on the measurement and never an edit to it; see Content/placement-1991.txt.
            var ring = Placements.Apply(e.Squared(), Placements.For(e.ParcelId, e.Index));
            if (ring == null || ring.Length < 3) return new TileRect(0, 0, 0, 0);

            float minX = ring[0].x, maxX = ring[0].x, minY = ring[0].y, maxY = ring[0].y;
            foreach (var p in ring)
            {
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
            }

            float w = maxX - minX, h = maxY - minY;
            if (w <= 0f || h <= 0f) return new TileRect(0, 0, 0, 0);

            float fill = e.Area > 0f ? e.Area / (w * h) : 1f;
            if (fill > 0f && fill < Rectangular)
            {
                outline = TilesOf(ring);
                if (outline == null && fill < LooseBox)
                {
                    float s = Mathf.Sqrt(fill / TargetFill);
                    float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
                    w *= s; h *= s;
                    minX = cx - w * 0.5f; minY = cy - h * 0.5f;
                }
            }

            return new TileRect(Mathf.RoundToInt(minX), Mathf.RoundToInt(minY),
                                Mathf.Max(1, Mathf.RoundToInt(w)), Mathf.Max(1, Mathf.RoundToInt(h)));
        }

        /// <summary>The ring rounded to whole tiles, with the closing repeat and any point that
        /// lands on top of its neighbour dropped - a doubled vertex is harmless to the crossing
        /// test but makes every later loop over the ring do nothing twice.</summary>
        private static Tile[] TilesOf(Vector2[] ring)
        {
            var outp = new List<Tile>(ring.Length);
            foreach (var p in ring)
            {
                var t = new Tile(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y));
                if (outp.Count > 0 && outp[outp.Count - 1].X == t.X && outp[outp.Count - 1].Y == t.Y)
                    continue;
                outp.Add(t);
            }
            while (outp.Count > 1 && outp[0].X == outp[outp.Count - 1].X
                                  && outp[0].Y == outp[outp.Count - 1].Y)
                outp.RemoveAt(outp.Count - 1);
            return outp.Count >= 3 ? outp.ToArray() : null;
        }

        /// <summary>Whether a tile's own centre is inside the outline. The same test WorldBuilder
        /// makes, and it has to stay the same one: a door this says is inside and the stamper
        /// says is outside would be a building with no way in.</summary>
        internal static bool Covers(Tile[] ring, Tile t)
        {
            if (ring == null || ring.Length < 3 || !t.IsValid) return false;
            float px = t.X + 0.5f, py = t.Y + 0.5f;
            bool inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                float ax = ring[i].X, ay = ring[i].Y, bx = ring[j].X, by = ring[j].Y;
                if ((ay > py) != (by > py) && px < (bx - ax) * (py - ay) / (by - ay) + ax)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>The door on the same side of the new box as it was on the old one, centred.
        /// Tile.None if this place never had one.</summary>
        private static Tile DoorFor(PlaceSpec place, TileRect now, Tile[] outline)
        {
            var d = place.Door;
            if (!d.IsValid) return Tile.None;
            var was = place.Bounds;

            // Which wall it was in. Top and Left are tested first, and a door in a corner - both
            // tests true - keeps the horizontal wall, which is the street side of nearly every
            // building on this grid.
            Tile at; int dx = 0, dy = 0;
            if (d.Y == was.Top)         { at = new Tile(now.X + now.W / 2, now.Top);    dy = 1; }
            else if (d.Y == was.Bottom) { at = new Tile(now.X + now.W / 2, now.Bottom); dy = -1; }
            else if (d.X == was.Left)   { at = new Tile(now.Left, now.Y + now.H / 2);   dx = 1; }
            else if (d.X == was.Right)  { at = new Tile(now.Right, now.Y + now.H / 2);  dx = -1; }
            else
            {
                // Not in any wall of its own box. A fault in the map rather than something to
                // carry forward, so it is named and the new door goes where a front door belongs.
                Debug.LogWarning($"[survey] '{place.Name}' had a door at {d.X},{d.Y} which is not "
                               + $"in its own wall {was} - the re-seated one is on the bottom edge.");
                at = new Tile(now.X + now.W / 2, now.Bottom); dy = -1;
            }

            if (outline == null) return at;

            // A SHAPED BUILDING MAY NOT REACH ITS OWN BOUNDING BOX on the side the door faces, so
            // the door walks in from the street until it meets the building. Landing it on the
            // box corner instead would put the front door in the garden.
            int depth = Mathf.Max(now.W, now.H);
            for (int step = 0; step < depth; step++)
            {
                var t = new Tile(at.X + dx * step, at.Y + dy * step);
                if (!now.Contains(t)) break;
                if (Covers(outline, t)) return t;
            }

            // Never met it. Hand back the plain edge tile - the caller sees it is not covered by
            // the outline, drops the outline, and the building is built as its rectangle.
            return at;
        }
    }
}
