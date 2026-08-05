using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Witness
{
    /// <summary>
    /// Whether something standing between two people stops them seeing each other.
    ///
    /// AN INTERFACE BECAUSE SIGHTLINES HAS NO WORLD, ON PURPOSE. Its own comment says a
    /// line-of-sight raycast through the tile grid "would make every number below untestable in
    /// isolation, because a test would have to build a world first" - and that is still true.
    /// Every distance and light band in that class can be checked with two tiles and a citizen.
    ///
    /// So the question is stated here and answered elsewhere, the same way INightWitnesses does
    /// it. Pass null and nothing blocks, which is what every existing test wants.
    /// </summary>
    public interface ISightBlocked
    {
        bool Between(Tile watcher, Tile subject, GameClock when);
    }

    /// <summary>
    /// Standing corn, which is a wall for a quarter of the year.
    ///
    /// THE-YEAR.md argues this is the same mechanic as darkness: corn is a 2.5 m barrier over
    /// half the map from July to October and then gone in three weeks, so anything reasoning
    /// about what a person could have seen has to know the month for exactly the reason it has
    /// to know the hour. Fields.BlocksSightline was built to say so and was read by nobody.
    ///
    /// IT IS NOT A DEGRADED VIEW, IT IS NO VIEW. A wall of corn between two people does not make
    /// the look Partial - there is nothing to be partial about - so this gates
    /// SawAnythingAtAll rather than shading the clarity.
    ///
    /// ONLY WHERE THE MAP SAYS THERE IS A FIELD. Fields.At is a pure function of position and
    /// date and will happily describe the corn growing in the middle of Chicago Street, so every
    /// sample is first asked of the world: what stands on this tile? Only a cornfield counts.
    /// </summary>
    public sealed class StandingCrop : ISightBlocked
    {
        /// <summary>How often to look along the line, in tiles. A field is tens of metres
        /// across and the shortest one on this map is wider than this, so nothing is stepped
        /// over; sampling every tile would be four times the work for the same answer.</summary>
        private const int StrideTiles = 4;

        private readonly WorldModel _world;
        private readonly PlaceKind _cornfield;
        private readonly bool _known;

        public StandingCrop(WorldModel world)
        {
            _world = world;
            _known = PlaceKindTable.Current.TryNamed("cornfield", out _cornfield);
        }

        public bool Between(Tile watcher, Tile subject, GameClock when)
        {
            if (_world == null || !_known) return false;

            int year = when.Year, dayOfYear = when.DayOfYear;

            int dx = subject.X - watcher.X, dy = subject.Y - watcher.Y;
            int steps = Mathf(dx, dy) / StrideTiles;
            if (steps <= 0) return false;

            // The ENDS ARE NOT SAMPLED. Somebody standing at the edge of their own field can
            // see out of it; what stops them is corn BETWEEN them and the other person.
            for (int i = 1; i < steps; i++)
            {
                int x = watcher.X + dx * i / steps;
                int y = watcher.Y + dy * i / steps;

                var id = _world.Grid.PlaceAt(x, y);
                if (!id.IsValid) continue;

                var place = _world.GetPlace(id);
                if (place == null || !place.Kind.Equals(_cornfield)) continue;

                if (Fields.At(x, y, year, dayOfYear).BlocksSightline) return true;
            }
            return false;
        }

        /// <summary>Chebyshev, matching the distance Sightlines measures in.</summary>
        private static int Mathf(int dx, int dy)
        {
            int ax = dx < 0 ? -dx : dx;
            int ay = dy < 0 ? -dy : dy;
            return ax > ay ? ax : ay;
        }
    }
}
