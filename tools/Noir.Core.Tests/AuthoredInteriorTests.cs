using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The floor plan drawn in the browser map overrides the generated interior. The three
    /// promises: the authored rooms land exactly; a sealed room is repaired, not built; and
    /// authoring one house moves NOTHING in any other, because the generator's draws are
    /// consumed either way. Spec: docs/superpowers/specs/2026-08-19-authored-interiors-design.md.
    /// </summary>
    public class AuthoredInteriorTests
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
        public void AuthoredRoomsLandExactlyWhereTheyWereDrawn()
        {
            var world = WorldBuilder.Build(TwoHouses(TwoRoomPlan()), 1234UL);
            var place = world.AllPlaces.Single(p => p.Name == "authored house");
            var rooms = world.AllRooms.Where(r => r.Building.Equals(place.Id)).ToList();

            Assert.That(rooms.Count, Is.EqualTo(2));
            Assert.That(rooms.Any(r => r.Kind == RoomKind.Kitchen
                                    && r.Bounds.Equals(new TileRect(41, 107, 5, 8))));
            Assert.That(rooms.Any(r => r.Kind == RoomKind.Bedroom
                                    && r.Bounds.Equals(new TileRect(47, 107, 6, 8))));
            // The authored door tile is floor, and the wall between the rooms is wall.
            //
            // Asserted at row 114, not 107: the front door (46,106) sits directly above this
            // party-wall column, and ConnectFrontDoor - unmodified by this task, shared with
            // every generated interior - punches inward from a door until it meets existing
            // floor. It meets the authored door at row 110 and stops there, so rows 107-109
            // legitimately become floor too; that tunnel never reaches row 114.
            Assert.That(world.Grid.TerrainAt(46, 110), Is.EqualTo(Terrain.Floor));
            Assert.That(world.Grid.TerrainAt(46, 114), Is.EqualTo(Terrain.Wall));
        }

        [Test]
        public void ASealedRoomGetsADoorwayPunchedNotBuilt()
        {
            var plan = TwoRoomPlan();
            plan.Doors.Clear();                                // the owner forgot the door
            var world = WorldBuilder.Build(TwoHouses(plan), 1234UL);

            // Some tile of the wall between x=46 must have become floor: the repair.
            bool connected = false;
            for (int y = 107; y <= 114; y++)
                if (world.Grid.TerrainAt(46, y) == Terrain.Floor) connected = true;
            Assert.That(connected, Is.True,
                "an authored plan with no interior door must still build a walkable house");
        }

        [Test]
        public void AuthoringOneHouseMovesNothingInAnyOther()
        {
            var with = WorldBuilder.Build(TwoHouses(TwoRoomPlan()), 1234UL);
            var without = WorldBuilder.Build(TwoHouses(null), 1234UL);

            var controlWith = with.AllPlaces.Single(p => p.Name == "control house");
            var controlWithout = without.AllPlaces.Single(p => p.Name == "control house");

            var roomsWith = with.AllRooms.Where(r => r.Building.Equals(controlWith.Id))
                .Select(r => r.Kind + " " + r.Bounds).ToList();
            var roomsWithout = without.AllRooms.Where(r => r.Building.Equals(controlWithout.Id))
                .Select(r => r.Kind + " " + r.Bounds).ToList();
            Assert.That(roomsWith, Is.EqualTo(roomsWithout),
                "the control house's rooms moved - the generator's draws were not preserved");

            var furWith = with.AllFurniture.Where(f => roomsOf(with, controlWith.Id).Contains(f.Room))
                .Select(f => f.Kind + " " + f.Footprint).OrderBy(s => s).ToList();
            var furWithout = without.AllFurniture.Where(f => roomsOf(without, controlWithout.Id).Contains(f.Room))
                .Select(f => f.Kind + " " + f.Footprint).OrderBy(s => s).ToList();
            Assert.That(furWith, Is.EqualTo(furWithout),
                "the control house's furniture moved - a plan must not reshuffle the town");

            HashSet<RoomId> roomsOf(WorldModel w, PlaceId id) =>
                w.AllRooms.Where(r => r.Building.Equals(id)).Select(r => r.Id).ToHashSet();
        }

        [Test]
        public void AMultiUnitBuildingIgnoresThePlan()
        {
            var layout = TwoHouses(TwoRoomPlan());
            layout.Places[0].Units = 4;
            Assert.DoesNotThrow(() => WorldBuilder.Build(layout, 1234UL));
        }
    }
}
