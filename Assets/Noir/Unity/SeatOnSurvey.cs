using System.Collections.Generic;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.World;

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
        private const int Smallest = 5;

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

            var seated = new List<(PlaceSpec Place, TileRect Was, TileRect Now, Tile Door)>();
            foreach (var pair in byLot)
            {
                var places = pair.Value;
                places.Sort((a, b) => b.Bounds.Area.CompareTo(a.Bounds.Area));
                var measured = ParcelBuildings.For(pair.Key);          // already largest first

                for (int i = 0; i < places.Count && i < measured.Count; i++)
                {
                    var box = BoxOf(measured[i]);
                    if (box.W < Smallest || box.H < Smallest) continue;
                    seated.Add((places[i], places[i].Bounds, box, DoorFor(places[i], box)));
                }
            }

            // A building moved onto its measurement can land on one that has already moved onto
            // its own. Rare - four pairs on today's data - and the smaller one gives way, because
            // leaving two sets of walls in the same tiles is worse than one building being where
            // the generator put it.
            seated.Sort((a, b) => b.Now.Area.CompareTo(a.Now.Area));
            var taken = new List<TileRect>(seated.Count);
            int moved = 0, yielded = 0;
            foreach (var s in seated)
            {
                bool clash = false;
                foreach (var t in taken)
                    if (s.Now.Overlaps(t)) { clash = true; break; }
                if (clash) { yielded++; continue; }

                taken.Add(s.Now);
                s.Place.Bounds = s.Now;
                if (s.Door.IsValid) s.Place.Door = s.Door;
                moved++;
            }

            Debug.Log($"[survey] {moved} buildings seated on their measured footprint"
                    + (yielded > 0 ? $", {yielded} left alone to avoid overlapping one" : "") + ".");
            return moved;
        }

        /// <summary>The measured footprint as a box in tiles, squared back to its lot first and
        /// shrunk to a believable bulk if the outline is too diagonal for its own bounds.</summary>
        private static TileRect BoxOf(ParcelBuildings.Entry e)
        {
            var ring = e.Squared();
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
            if (fill > 0f && fill < LooseBox)
            {
                float s = Mathf.Sqrt(fill / TargetFill);
                float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
                w *= s; h *= s;
                minX = cx - w * 0.5f; minY = cy - h * 0.5f;
            }

            return new TileRect(Mathf.RoundToInt(minX), Mathf.RoundToInt(minY),
                                Mathf.Max(1, Mathf.RoundToInt(w)), Mathf.Max(1, Mathf.RoundToInt(h)));
        }

        /// <summary>The door on the same side of the new box as it was on the old one, centred.
        /// Tile.None if this place never had one.</summary>
        private static Tile DoorFor(PlaceSpec place, TileRect now)
        {
            var d = place.Door;
            if (!d.IsValid) return Tile.None;
            var was = place.Bounds;

            // Which wall it was in. Top and Left are tested first, and a door in a corner - both
            // tests true - keeps the horizontal wall, which is the street side of nearly every
            // building on this grid.
            if (d.Y == was.Top)    return new Tile(now.X + now.W / 2, now.Top);
            if (d.Y == was.Bottom) return new Tile(now.X + now.W / 2, now.Bottom);
            if (d.X == was.Left)   return new Tile(now.Left, now.Y + now.H / 2);
            if (d.X == was.Right)  return new Tile(now.Right, now.Y + now.H / 2);

            // Not in any wall of its own box. That is a fault in the map rather than something to
            // carry forward, so the new door is put where a front door belongs and the old one is
            // named so it can be fixed at source.
            Debug.LogWarning($"[survey] '{place.Name}' had a door at {d.X},{d.Y} which is not in "
                           + $"its own wall {was} - the re-seated one is on the bottom edge.");
            return new Tile(now.X + now.W / 2, now.Bottom);
        }
    }
}
