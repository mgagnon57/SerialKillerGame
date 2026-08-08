using System.Linq;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// THE PASS THAT ANSWERS "THERE ARE STILL HOUSES IN THE ROAD".
    ///
    /// Fifty of Rossville's 373 authored buildings stand inside a surveyed road corridor, and they
    /// stand in COLUMNS: houses authored at x = 977 against Church Street, whose centreline is
    /// x = 975. They were positioned from the street's own coordinate back when city.txt's 37
    /// ruled roads were the only roads; SurveyRoads then replaced those with the county's 66
    /// measured ones and the streets moved out from under them.
    /// </summary>
    [TestFixture]
    public class ClearOfRoadsTests
    {
        [SetUp]
        public void InstallKinds() => TestContent.EnsureKinds();

        private static VillageLayout WithStreet(int roadY = 100)
        {
            var layout = new VillageLayout { Name = "test", Width = 400, Height = 400 };
            var run = new RoadRun { Kind = Terrain.Road, Name = "church", Width = 10 };
            run.Points.Add(new Tile(0, roadY));
            run.Points.Add(new Tile(400, roadY));
            layout.Roads.Add(run);
            return layout;
        }

        private static PlaceSpec House(int x, int y, int w = 13, int h = 7)
        {
            var kind = PlaceKindTable.Current.TryKindOf("house", out var k) ? k : PlaceKind.Dwelling;
            return new PlaceSpec { Kind = kind, Name = $"{x},{y}", Bounds = new TileRect(x, y, w, h) };
        }

        [Test]
        public void AHouseInTheRoadIsMovedOutOfIt()
        {
            var layout = WithStreet();
            var house = House(50, 98);                 // road spans y 95..105
            layout.Places.Add(house);

            Assert.That(RoadCorridor.WorstPenetration(layout, house.Bounds), Is.GreaterThan(0.5f),
                        "precondition: it starts in the road");

            int moved = ClearOfRoads.Apply(layout, out int stuck);

            Assert.That(moved, Is.EqualTo(1));
            Assert.That(stuck, Is.EqualTo(0));
            Assert.That(RoadCorridor.WorstPenetration(layout, house.Bounds), Is.EqualTo(0f),
                        "and it is out of the road afterwards");
        }

        [Test]
        public void AHouseClearOfTheRoadIsLeftExactlyWhereItIs()
        {
            var layout = WithStreet();
            var house = House(50, 200);
            var was = house.Bounds;
            layout.Places.Add(house);

            Assert.That(ClearOfRoads.Apply(layout), Is.EqualTo(0));
            Assert.That(house.Bounds, Is.EqualTo(was),
                        "a pass that moves things it did not need to move is worse than no pass");
        }

        /// <summary>The door is the tile everybody walking here has to reach. Leaving it behind
        /// puts it outside its own wall, which has cut this town in two before.</summary>
        [Test]
        public void TheDoorTravelsWithTheHouse()
        {
            var layout = WithStreet();
            var house = House(50, 98);
            house.Door = new Tile(56, 105);
            layout.Places.Add(house);

            var wasBox = house.Bounds;
            var wasDoor = house.Door;
            ClearOfRoads.Apply(layout);

            Assert.That(house.Door.X - wasDoor.X, Is.EqualTo(house.Bounds.X - wasBox.X));
            Assert.That(house.Door.Y - wasDoor.Y, Is.EqualTo(house.Bounds.Y - wasBox.Y));
        }

        [Test]
        public void AMeasuredBuildingIsNeverMoved()
        {
            var layout = WithStreet();
            var house = House(50, 98);
            house.Outline = new[] { new Tile(50, 98), new Tile(63, 98), new Tile(63, 105), new Tile(50, 105) };
            var was = house.Bounds;
            layout.Places.Add(house);

            Assert.That(ClearOfRoads.Apply(layout), Is.EqualTo(0),
                        "a measured footprint is the survey's statement about where a building "
                        + "stood; nudging it would be inventing a fact. SeatOnSurvey refuses it "
                        + "instead.");
            Assert.That(house.Bounds, Is.EqualTo(was));
        }

        [Test]
        public void AHouseIsNotMovedOnTopOfItsNeighbour()
        {
            var layout = WithStreet();
            var inRoad = House(50, 98);
            layout.Places.Add(inRoad);
            // Both kerbs are built up, so the nearest clear ground is past the neighbour rather
            // than beside it. The house has to step over them, not onto them.
            layout.Places.Add(House(50, 106, 13, 10));    // occupies y 106..116
            layout.Places.Add(House(50, 80, 13, 14));     // occupies y  80..94

            ClearOfRoads.Apply(layout);

            Assert.That(RoadCorridor.WorstPenetration(layout, inRoad.Bounds), Is.EqualTo(0f));
            foreach (var other in layout.Places.Where(q => !ReferenceEquals(q, inRoad)))
                Assert.That(inRoad.Bounds.Overlaps(other.Bounds), Is.False,
                            "clearing the road by landing on the neighbour is not a fix");
        }

        /// <summary>
        /// THE REAL TOWN. city.txt's own buildings against the surveyed network, run through the
        /// pass, asserting that afterwards nothing is left standing in a road.
        ///
        /// This is the assertion the owner has been asking for. It reads Content/ directly - the
        /// Unity passes cannot be compiled by `dotnet test` - so it measures city.txt as authored
        /// plus roads.txt as surveyed, which is exactly the disagreement that put houses in roads.
        /// </summary>
        [Test]
        public void NoAuthoredBuildingIsLeftStandingInARossvilleRoad()
        {
            var layout = RealRossville.LayoutWithPlaces();
            var table = PlaceKindTable.Current;

            int before = layout.Places.Count(
                p => table.Row(p.Kind).IsBuilding &&
                     RoadCorridor.WorstPenetration(layout, p.Bounds) > ClearOfRoads.Tolerance);

            int moved = ClearOfRoads.Apply(layout, out int stuck);

            int after = layout.Places.Count(
                p => table.Row(p.Kind).IsBuilding &&
                     RoadCorridor.WorstPenetration(layout, p.Bounds) > ClearOfRoads.Tolerance);

            TestContext.Out.WriteLine($"authored buildings in a road: {before} before, {after} after");
            TestContext.Out.WriteLine($"  moved {moved}, could not clear {stuck}");

            Assert.That(before, Is.GreaterThan(0),
                        "if this ever reaches zero on its own the authoring was fixed at source "
                        + "and this pass has become a no-op worth deleting");
            Assert.That(after, Is.EqualTo(0),
                        "NOT ONE building may be left standing in a Rossville road.");
        }
    }
}
