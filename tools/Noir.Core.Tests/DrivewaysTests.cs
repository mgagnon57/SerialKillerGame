using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// WHERE THE TOWN'S CARS ACTUALLY ARE.
    ///
    /// IDOT counts Rossville at ~19 moving vehicles at an average instant and Data USA puts the
    /// village at two cars per household - so against 624 households, roughly 98% of the town's
    /// cars are standing at a house at any given moment, and until now the game drew none of them.
    /// See docs/research/TRAFFIC-COUNTS.md.
    ///
    /// This is the deciding half, and it lives in Core on purpose: it is geometry and a seed, it
    /// is the 27% of the tree that has automated cover, and a player build gets it for free.
    /// Only the drawing is Unity's.
    /// </summary>
    [TestFixture]
    public class DrivewaysTests
    {
        [SetUp]
        public void InstallKinds() => TestContent.EnsureKinds();

        // ---------- a village of one house on one street ----------

        private const int RoadY = 100;          // 10 wide, so it covers y 95..105

        private static VillageLayout OneHouse(Tile door, int units = 1, int houseY = 108)
        {
            var layout = new VillageLayout { Name = "test", Width = 300, Height = 300 };

            var run = new RoadRun { Kind = Terrain.Road, Name = "church", Width = 10 };
            run.Points.Add(new Tile(0, RoadY));
            run.Points.Add(new Tile(300, RoadY));
            layout.Roads.Add(run);

            var kind = PlaceKindTable.Current.TryKindOf("house", out var k) ? k : PlaceKind.Dwelling;
            layout.Places.Add(new PlaceSpec
            {
                Kind = kind,
                Name = "one",
                Bounds = new TileRect(50, houseY, 13, 7),
                Door = door,
                Units = units,
            });
            return layout;
        }

        private static WorldModel Build(VillageLayout layout, ulong seed = 1991UL) =>
            WorldBuilder.Build(layout, seed);

        [Test]
        public void ACarDoesNotStandDirectlyInFrontOfTheDoor_OffCentreDoorToo()
        {
            // Door NOT centred - nearer the left corner of a 13-wide frontage. The car still has
            // to clear the door's own line, not just the middle of the wall it happens to share.
            var world = Build(OneHouse(new Tile(51, 108)));

            var plan = Driveways.Plan(world, 1991UL);
            Assert.That(plan.Length, Is.EqualTo(1));

            var spot = plan[0].Spot;
            Assert.That(System.Math.Abs(spot.X - 51), Is.GreaterThanOrEqualTo(2),
                        $"spot {spot} stands on the door's own line out of the house");
        }

        [Test]
        public void AHouseGetsItsCarOnTheSideItsDoorIsOn()
        {
            // Door on the TOP edge, which is the side the street is on.
            var world = Build(OneHouse(new Tile(56, 108)));

            var plan = Driveways.Plan(world, 1991UL);

            Assert.That(plan.Length, Is.EqualTo(1), "one household, one place to put a car");

            var spot = plan[0].Spot;
            Assert.That(spot.Y, Is.LessThan(108), "the car stands in front of the house, not behind it");
            Assert.That(spot.Y, Is.GreaterThan(RoadY + 5),
                        "and it is on the lot, not in the carriageway");
        }

        [Test]
        public void ACarNeverStandsInTheRoad()
        {
            var world = Build(OneHouse(new Tile(56, 108)));

            foreach (var d in Driveways.Plan(world, 1991UL))
                Assert.That(world.Grid.TerrainAt(d.Spot), Is.Not.EqualTo(Terrain.Road),
                            $"{d.Spot} is in the road");
        }

        [Test]
        public void ACarDoesNotStandDirectlyInFrontOfTheDoor()
        {
            // Door dead centre of the frontage - the shape most Rossville houses actually are.
            // Standing() must not simply walk straight out from the middle of the wall, or the
            // one car this house owns is parked on the only path out of its own front door.
            var world = Build(OneHouse(new Tile(56, 108)));

            var plan = Driveways.Plan(world, 1991UL);
            Assert.That(plan.Length, Is.EqualTo(1));

            var spot = plan[0].Spot;
            Assert.That(System.Math.Abs(spot.X - 56), Is.GreaterThanOrEqualTo(2),
                        $"spot {spot} stands on the door's own line out of the house");
        }

        [Test]
        public void ATerraceGetsOneSpotPerFrontDoorAndTheyDoNotStack()
        {
            var world = Build(OneHouse(new Tile(56, 108), units: 4));

            var plan = Driveways.Plan(world, 1991UL);

            Assert.That(plan.Length, Is.EqualTo(4),
                        "a terrace is one building with four front doors and four cars");
            Assert.That(plan.Select(d => d.Spot).Distinct().Count(), Is.EqualTo(4),
                        "and four cars cannot be parked on the same square metre");
        }

        [Test]
        public void TheSameSeedPlansTheSameDriveways()
        {
            var a = Driveways.Plan(Build(OneHouse(new Tile(56, 108), units: 3)), 1991UL);
            var b = Driveways.Plan(Build(OneHouse(new Tile(56, 108), units: 3)), 1991UL);

            Assert.That(a.Select(d => d.Spot).ToList(), Is.EqualTo(b.Select(d => d.Spot).ToList()),
                        "a seed has to reproduce the village, cars included");
        }

        [Test]
        public void AHouseWithNoAuthoredDoorStillFindsTheStreet()
        {
            // No door at all. The front is then whichever way the road is.
            var world = Build(OneHouse(Tile.None));

            var plan = Driveways.Plan(world, 1991UL);

            Assert.That(plan.Length, Is.EqualTo(1));
            Assert.That(plan[0].Spot.Y, Is.LessThan(108),
                        "it should face the street it can actually reach");
        }

        // ---------- and now against Rossville itself ----------

        private static WorldModel Rossville()
        {
            TestContent.EnsureKinds();
            return WorldBuilder.Build(VillageParser.Parse(TestContent.Read("city.txt")), 1991UL);
        }

        [Test]
        public void NoCarInRossvilleIsParkedInAStreetOrInsideABuilding()
        {
            var world = Rossville();
            var plan = Driveways.Plan(world, 1991UL);

            Assert.That(plan.Length, Is.GreaterThan(0), "nothing was planned at all");

            var inRoad = plan.Where(d => world.Grid.TerrainAt(d.Spot) == Terrain.Road).ToList();
            var indoors = plan.Where(d => world.Grid.PlaceAt(d.Spot).IsValid).ToList();

            Assert.That(inRoad, Is.Empty,
                        $"{inRoad.Count} cars parked in the carriageway, e.g. {inRoad.FirstOrDefault().Spot}");
            Assert.That(indoors, Is.Empty,
                        $"{indoors.Count} cars parked inside a building, e.g. {indoors.FirstOrDefault().Spot}");
        }

        [Test]
        public void TwoCarsAreNeverParkedOnTheSameSpot()
        {
            var plan = Driveways.Plan(Rossville(), 1991UL);
            var seen = new HashSet<Tile>();
            var clash = plan.Where(d => !seen.Add(d.Spot)).ToList();

            Assert.That(clash, Is.Empty, $"{clash.Count} cars share a spot with another");
        }

        [Test]
        public void MostOfRossvilleKeepsItsCarAtHome()
        {
            var world = Rossville();
            var plan = Driveways.Plan(world, 1991UL);

            int households = world.DeclaredHouseholds;
            float share = plan.Length / (float)households;

            TestContext.WriteLine($"  ---- CARS AT HOME ----");
            TestContext.WriteLine($"  {households} declared households");
            TestContext.WriteLine($"  {plan.Length} with somewhere to stand a car  ({share:P0})");
            TestContext.WriteLine($"  IDOT says ~19 of the town's cars are moving at an average instant.");

            // Not a hardcoded count - the town grows and shrinks with every survey pass. What is
            // being asserted is that the great majority of homes find room, because a town where
            // half the driveways are empty is one where the geometry is failing, not one where
            // everybody happens to be out.
            Assert.That(share, Is.GreaterThan(0.75f),
                        $"only {share:P0} of households found anywhere to put a car");
        }
    }
}
