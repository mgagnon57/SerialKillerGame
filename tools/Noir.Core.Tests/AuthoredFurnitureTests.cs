using System.Linq;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// Authored furniture: a plan's pieces replace the generated furnishing of that building
    /// wholesale, the placer still runs for RNG neutrality, and a piece on a doorway is refused
    /// without wrecking the room. Spec: docs/superpowers/specs/2026-08-19-authored-interiors-design.md.
    /// </summary>
    public class AuthoredFurnitureTests
    {
        private static VillageLayout TwoHouses(AuthoredInterior forFirst)
        {
            TestContent.EnsureKinds();
            var layout = new VillageLayout { Width = 200, Height = 200 };
            layout.Roads.Add(new RoadRun
            {
                Name = "mckibben", Width = 10,
                Points = { new Tile(0, 100), new Tile(199, 100) },
            });
            layout.Places.Add(new PlaceSpec
            {
                Kind = PlaceKind.Dwelling, Name = "authored house",
                Bounds = new TileRect(40, 106, 14, 10), Door = new Tile(46, 106),
                AuthoredInterior = forFirst,
            });
            layout.Places.Add(new PlaceSpec
            {
                Kind = PlaceKind.Dwelling, Name = "control house",
                Bounds = new TileRect(80, 106, 13, 7), Door = new Tile(86, 106),
            });
            return layout;
        }

        // Interior tiles of Bounds(40,106,14,10) are x 41..52, y 107..114.
        private static AuthoredInterior TwoRoomPlan()
        {
            var a = new AuthoredInterior();
            a.Rooms.Add(new AuthoredRoom(new TileRect(41, 107, 5, 8), RoomKind.Kitchen, "Kitchen"));
            a.Rooms.Add(new AuthoredRoom(new TileRect(47, 107, 6, 8), RoomKind.Bedroom, "Bedroom 1"));
            a.Doors.Add(new Tile(46, 110));                    // through the wall between them
            return a;
        }

        [Test]
        public void AuthoredFurnitureLandsAtTheAuthoredTiles()
        {
            var plan = TwoRoomPlan();
            plan.Furniture.Add(new AuthoredFurniture(FurnitureKind.Bed,
                new TileRect(48, 108, 2, 3), "OwnersBed"));
            var world = WorldBuilder.Build(TwoHouses(plan), 1234UL);
            var place = world.AllPlaces.Single(p => p.Name == "authored house");
            var mine = world.AllFurniture.Where(f =>
                world.GetRoom(f.Room).Building.Equals(place.Id)).ToList();

            Assert.That(mine.Count, Is.EqualTo(1),
                "an authored furniture list replaces the generated furnishing entirely");
            Assert.That(mine[0].Kind, Is.EqualTo(FurnitureKind.Bed));
            Assert.That(mine[0].Footprint, Is.EqualTo(new TileRect(48, 108, 2, 3)));
            Assert.That(mine[0].Model, Is.EqualTo("OwnersBed"));
        }

        [Test]
        public void APieceOnADoorwayIsRefusedWithoutWreckingTheRoom()
        {
            var plan = TwoRoomPlan();
            plan.Furniture.Add(new AuthoredFurniture(FurnitureKind.Wardrobe,
                new TileRect(46, 110, 1, 1), ""));            // exactly on the door tile
            var world = WorldBuilder.Build(TwoHouses(plan), 1234UL);
            var place = world.AllPlaces.Single(p => p.Name == "authored house");
            Assert.That(world.AllFurniture.Any(f =>
                world.GetRoom(f.Room).Building.Equals(place.Id)
                && f.Kind == FurnitureKind.Wardrobe), Is.False,
                "nothing may be placed against a door - the placer's own standing rule");
        }

        [Test]
        public void FurnishFalseSkipsGeneratedFurnitureOnly()
        {
            var plan = TwoRoomPlan();
            plan.Furnish = false;                              // an owner-model place
            plan.Furniture.Add(new AuthoredFurniture(FurnitureKind.Bed,
                new TileRect(48, 108, 2, 3), ""));
            var world = WorldBuilder.Build(TwoHouses(plan), 1234UL);
            var place = world.AllPlaces.Single(p => p.Name == "authored house");
            var mine = world.AllFurniture.Where(f =>
                world.GetRoom(f.Room).Building.Equals(place.Id)).ToList();
            Assert.That(mine.Count, Is.EqualTo(1));            // the authored bed, nothing else
        }
    }

    /// <summary>The word-match table for an authored furniture piece's name. Mirrors RoomWords'
    /// own test shape: first match wins, an unmatched name defaults to Table.</summary>
    public class FurnitureWordsTests
    {
        [TestCase("Bed", FurnitureKind.Bed)]
        [TestCase("stove", FurnitureKind.Cooker)]
        [TestCase("Sofa", FurnitureKind.Sofa)]
        [TestCase("couch", FurnitureKind.Sofa)]
        [TestCase("tub", FurnitureKind.Bath)]
        [TestCase("fireplace", FurnitureKind.Hearth)]
        [TestCase("bookshelf", FurnitureKind.Shelf)]
        [TestCase("nightstand", FurnitureKind.Table)]
        [TestCase("wardrobe", FurnitureKind.Wardrobe)]
        [TestCase("dresser", FurnitureKind.Dresser)]
        [TestCase("cooker", FurnitureKind.Cooker)]
        [TestCase("range", FurnitureKind.Cooker)]
        [TestCase("sink", FurnitureKind.Sink)]
        [TestCase("counter", FurnitureKind.Counter)]
        [TestCase("bath", FurnitureKind.Bath)]
        [TestCase("basin", FurnitureKind.Basin)]
        [TestCase("hearth", FurnitureKind.Hearth)]
        [TestCase("desk", FurnitureKind.Desk)]
        [TestCase("shelf", FurnitureKind.Shelf)]
        [TestCase("cabinet", FurnitureKind.Cabinet)]
        [TestCase("bench", FurnitureKind.Bench)]
        public void WordsMatchTheirKind(string word, FurnitureKind expected)
        {
            Assert.That(FurnitureWords.KindFor(word), Is.EqualTo(expected));
        }
    }
}
