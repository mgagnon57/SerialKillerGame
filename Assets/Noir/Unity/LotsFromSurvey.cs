using UnityEngine;
using Noir.Core.World;

namespace Noir.Unity
{
    /// <summary>
    /// WHOSE YARD EACH TILE IS, stamped into the grid from the county's parcel polygons.
    ///
    /// WHY THIS PASS EXISTS. Reported by the owner on 2026-08-11: "people are walking through
    /// other people's yards." They were, and the reason is that until this ran there was no such
    /// thing as somebody's yard. <c>TileGrid.PlaceAt</c> claims only the tiles inside a
    /// building's own outline - the walls and the rooms - so the lawn around a house was
    /// anonymous open grass at a move cost of 1.30 against a road's 1.00, and A* cut the corner
    /// across it whenever going round was more than about a third further. Every one of those
    /// routes was a legal, successful path. Nothing was broken; the town simply had no concept of
    /// private ground for the pathfinder to respect.
    ///
    /// WHAT IT DOES NOT CLAIM, and this is the load-bearing half. A parcel polygon runs to the
    /// right of way, and the survey's carriageway and verge sit inside that easement - so a lot
    /// whose boundary overhangs the street would otherwise charge trespass for walking down the
    /// public footway outside it, which is the exact fault this whole change is meant to fix,
    /// inverted. Road and verge tiles are skipped and stay public.
    ///
    /// FARMLAND IS EXEMPT, AND THAT IS A JUDGEMENT CALL WITH A NUMBER BEHIND IT. Stamping every
    /// parcel claimed 2,155,131 tiles - 43% of the whole 2,100x2,400 map - because Rossville's
    /// parcels run out over the county's fields, and a crop field is somebody's property in exactly
    /// the sense a front lawn is. Two reasons not to:
    ///
    ///   - The complaint was about yards in town. Priced trespass on half the map is a far larger
    ///     change to how everyone in the game moves than was asked for, and open country is
    ///     already Rough at 1.70 against a road's 1.00, so nobody crosses a field by preference.
    ///   - The A* heuristic is scaled to TileGrid.TypicalMoveCost, 1.3. Make most of the map cost
    ///     4 x 1.70 = 6.8 a step and the heuristic underestimates the true remaining cost by five
    ///     times, the search degenerates towards Dijkstra, and long country journeys start hitting
    ///     MaxNodesExamined and coming back GaveUp. That is a person who stands still for ever
    ///     with nothing in any log to say why - the exact shape of both faults CLAUDE.md records
    ///     as having been called "flaky" for months.
    ///
    /// The count is printed either way, so reversing this is a one-line change made against a
    /// measurement rather than against a feeling.
    ///
    /// AFTER THE WORLD IS BUILT, because it writes to the grid and the grid does not exist until
    /// then; and only from <see cref="TownPipeline.Build()"/>, never from BuildUnsurveyed, because
    /// the parcel ids are keyed to the real town. Handed a fixture it would stamp Rossville's lot
    /// lines onto a village that has none - silently, which is the failure mode TownPipeline's own
    /// header was written about.
    /// </summary>
    public static class LotsFromSurvey
    {
        /// <summary>Tiles claimed for a property. Zero means the parcels never loaded.</summary>
        public static int Apply(WorldModel world)
        {
            // THE CONTROL, and it is here because "my control was not a control" is a commit
            // message in this repo from two days ago. Pricing private ground changes how hard
            // every search in the game works, and the only honest way to say by how much is to
            // build the same town twice. NOIR_NO_LOTS=1 leaves every tile public.
            if (System.Environment.GetEnvironmentVariable("NOIR_NO_LOTS") == "1")
            {
                Debug.Log("[lots] NOIR_NO_LOTS=1 - nothing stamped, every yard is public ground. "
                        + "This is the control, not the game.");
                return 0;
            }

            var grid = world.Grid;
            int claimed = 0, public_ = 0, farmland = 0;

            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                var f = grid.FlagsAt(x, y);
                if ((f & (TileFlags.Road | TileFlags.Verge)) != 0) { public_++; continue; }

                int lot = GroundZoning.PropertyAt(world, x, y);
                if (lot < 0) continue;

                // FARMLAND IS SOMEBODY'S, AND IS DELIBERATELY NOT TRESPASS. See the header.
                if (GroundZoning.IsFarmland(lot)) { farmland++; continue; }

                grid.SetLot(x, y, lot);
                claimed++;
            }

            // GREPPABLE, because the failure here is silence: no parcels loaded, nothing stamped,
            // and a town that paths exactly as badly as it did before with nothing in any log to
            // say so. The same trap SurveyRoads carries, and it is called out in CLAUDE.md.
            //
            // The farmland figure is printed rather than dropped because it is the number that
            // decides whether the exemption above is the right call, and the first version of
            // this pass shipped without it and claimed 43% of the map without saying so.
            Debug.Log($"[lots] {claimed:N0} tiles claimed for a property, "
                    + $"{public_:N0} left public as road or verge, "
                    + $"{farmland:N0} left open as farmland.");

            return claimed;
        }
    }
}
