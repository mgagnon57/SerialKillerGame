using System.Collections.Generic;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Sim;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// An alley is not a narrow street - it is the other way through town, and different people
    /// use it. Owner's standing fact 4 in docs/SOURCES-OF-TRUTH.md, from having grown up here:
    ///
    ///   "Most alleys were only used for trash pick up or parking their cars in the garage that
    ///    faced the alley... when I was a kid and rode my bike around, I would take alleys to get
    ///    to where I wanted. Cars would not normally use an alley."
    ///
    /// Two claims, and these are the tests for both: a car is never routed down one, and a
    /// traveller who prefers them will go out of their way to pick one up.
    /// </summary>
    public class AlleyBehaviourTests
    {
        // ---- what cars do -------------------------------------------------------------------

        [Test]
        public void NoAmbientTrafficIsRoutedDownAnAlley()
        {
            Assert.That(RoadClasses.AmbientTrafficWeight(RoadClass.Alley), Is.Zero,
                "cars do not use alleys to get anywhere");
        }

        [Test]
        public void EveryOtherClassStillCarriesTrafficInOrder()
        {
            // Ordered rather than tuned: each step up should dominate the one below.
            Assert.That(RoadClasses.AmbientTrafficWeight(RoadClass.Freeway),
                Is.GreaterThan(RoadClasses.AmbientTrafficWeight(RoadClass.Mainroad)));
            Assert.That(RoadClasses.AmbientTrafficWeight(RoadClass.Mainroad),
                Is.GreaterThan(RoadClasses.AmbientTrafficWeight(RoadClass.Street)));
            Assert.That(RoadClasses.AmbientTrafficWeight(RoadClass.Street),
                Is.GreaterThan(RoadClasses.AmbientTrafficWeight(RoadClass.Track)));
            Assert.That(RoadClasses.AmbientTrafficWeight(RoadClass.Track), Is.GreaterThan(0),
                "a track is a farm entrance - rare, but not nothing");
        }

        // ---- what the ground says -----------------------------------------------------------

        /// <summary>A street across the top, an alley across the middle, both full width.</summary>
        private static TileGrid TownWithAnAlley()
        {
            var layout = new VillageLayout { Name = "alleytest", Width = 140, Height = 60 };

            var street = new RoadRun { Kind = Terrain.Road, Name = "main", Width = 10,
                                       Class = RoadClass.Street };
            street.Points.Add(new Tile(0, 10));
            street.Points.Add(new Tile(139, 10));
            layout.Roads.Add(street);

            // Half a block south of the street, which is where an alley actually is. The first
            // version of this put it 100 ft away and the test failed for a good reason: the
            // detour cost more grass than the preference could save, and the pathfinder was
            // right to stay on the street. A preference is not a compulsion.
            var alley = new RoadRun { Kind = Terrain.Road, Name = "alley1", Width = 4,
                                      Class = RoadClass.Alley };
            alley.Points.Add(new Tile(0, 24));
            alley.Points.Add(new Tile(139, 24));
            layout.Roads.Add(alley);

            return WorldBuilder.Build(layout).Grid;
        }

        [Test]
        public void AnAlleyIsMarkedOnTheGroundAndAStreetIsNot()
        {
            var grid = TownWithAnAlley();
            Assert.That(grid.IsAlley(60, 24), Is.True, "the alley should be flagged");
            Assert.That(grid.IsAlley(60, 10), Is.False, "the street should not be");
        }

        [Test]
        public void AnAlleyIsJustAsWalkableAndJustAsQuickAsAStreet()
        {
            // The flag says who USES it, not what it is made of. An alley is carriageway.
            var grid = TownWithAnAlley();
            Assert.That(grid.IsWalkable(60, 24), Is.True);
            Assert.That(grid.MoveCost(60, 24), Is.EqualTo(grid.MoveCost(60, 10)).Within(0.001f));
        }

        // ---- what a boy on a bike does ------------------------------------------------------

        private static int AlleyTilesIn(TileGrid grid, List<Tile> path)
        {
            int n = 0;
            foreach (var t in path) if (grid.IsAlley(t)) n++;
            return n;
        }

        [Test]
        public void AnOrdinaryTravellerKeepsToTheStreet()
        {
            var grid = TownWithAnAlley();
            var path = new List<Tile>();
            Assert.That(new Pathfinder(grid).FindPath(new Tile(6, 10), new Tile(133, 10), path),
                Is.EqualTo(PathOutcome.Found));
            Assert.That(AlleyTilesIn(grid, path), Is.Zero,
                "somebody with no feeling about alleys has no reason to leave the street");
        }

        [Test]
        public void SomebodyWhoTakesTheAlleyActuallyTakesIt()
        {
            var grid = TownWithAnAlley();
            var path = new List<Tile>();

            // Both ends sit level with the STREET, so the direct line never touches the lane
            // half a block south. He goes down to it anyway, runs it, and comes back up.
            Assert.That(new Pathfinder(grid).FindPathViaAlley(new Tile(6, 10), new Tile(133, 10), path),
                Is.EqualTo(PathOutcome.Found));
            Assert.That(AlleyTilesIn(grid, path), Is.GreaterThan(40),
                "he should pick the lane up and stay on it, not clip a corner of it");
            Assert.That(path[path.Count - 1], Is.EqualTo(new Tile(133, 10)),
                "and still arrive where he was going");
        }

        [Test]
        public void TheRouteIsStillWalkableEndToEnd()
        {
            var grid = TownWithAnAlley();
            var path = new List<Tile>();
            new Pathfinder(grid).FindPathViaAlley(new Tile(6, 10), new Tile(133, 10), path);
            foreach (var t in path)
                Assert.That(grid.IsWalkable(t), Is.True, $"{t} is not walkable");
        }

        [Test]
        public void WithNoAlleyWithinReachHeSimplyWalksTheNormalWay()
        {
            var layout = new VillageLayout { Name = "nolanes", Width = 140, Height = 60 };
            var st = new RoadRun { Kind = Terrain.Road, Name = "main", Width = 10,
                                   Class = RoadClass.Street };
            st.Points.Add(new Tile(0, 10));
            st.Points.Add(new Tile(139, 10));
            layout.Roads.Add(st);
            var grid = WorldBuilder.Build(layout).Grid;

            var viaAlley = new List<Tile>();
            var ordinary = new List<Tile>();
            var finder = new Pathfinder(grid);

            Assert.That(finder.FindPathViaAlley(new Tile(6, 10), new Tile(133, 10), viaAlley),
                Is.EqualTo(PathOutcome.Found));
            finder.FindPath(new Tile(6, 10), new Tile(133, 10), ordinary);
            Assert.That(viaAlley, Is.EqualTo(ordinary),
                "asking for the alley in a town without one must not change the walk");
        }

        [Test]
        public void HeWillNotFollowALaneMilesOutOfHisWay()
        {
            var grid = TownWithAnAlley();
            var path = new List<Tile>();
            var direct = new List<Tile>();
            var finder = new Pathfinder(grid);

            // A journey ALONG the street, short enough that the lane is a silly detour.
            finder.FindPath(new Tile(6, 10), new Tile(20, 10), direct);
            finder.FindPathViaAlley(new Tile(6, 10), new Tile(20, 10), path, searchRadius: 45,
                                    maxDetour: 1.4f);
            Assert.That(path.Count, Is.LessThanOrEqualTo((int)(direct.Count * 1.4f) + 1),
                "past the detour he is willing to make, he stays on the street");
        }
    }
}
