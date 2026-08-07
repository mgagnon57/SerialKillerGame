using System.Collections.Generic;
using UnityEngine;
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
            int gone = layout.Roads.RemoveAll(run => !RoadRulings.Existed(run.Name));
            if (gone == 0) return;

            var named = new List<string>(RoadRulings.Gone);
            Debug.Log($"[1991] {gone} road line(s) taken off the map - "
                    + string.Join(", ", named) + " did not exist in 1991.");
        }
    }
}
