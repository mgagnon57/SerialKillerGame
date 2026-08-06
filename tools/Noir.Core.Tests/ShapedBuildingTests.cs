using System.Collections.Generic;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// A building whose real footprint has been measured is stamped as its bounding rectangle and
    /// then cut back to that outline - see PlaceSpec.Outline and WorldBuilder.MaskToOutline. The
    /// cut is the dangerous part: it runs after the interior has been laid out, so it can open a
    /// room to the street or strand one behind the new wall, and a building nobody can get out of
    /// cuts the town into pieces. Nothing in any map file writes an outline, so the ordinary
    /// suite never reaches this code at all - these are the only tests that do.
    /// </summary>
    [TestFixture]
    public class ShapedBuildingTests
    {
        private const string Header = "village Shaped\nsize 80 80\nterrain grass 0,0 80x80\n";

        /// <summary>A 20x20 house with its door in the middle of the bottom wall.</summary>
        private const string OneHouse =
            "place house 20,20 20x20 \"the test house\"\n  door 30,39\n";

        private static WorldModel Build(Tile[] outline)
        {
            TestContent.EnsureKinds();
            var layout = VillageParser.Parse(Header + OneHouse);
            layout.Places[0].Outline = outline;
            return WorldBuilder.Build(layout, 1234UL);
        }

        /// <summary>An L: the whole 20x20 square with the top-right quarter bitten out.</summary>
        private static Tile[] Ell() => new[]
        {
            new Tile(20, 20), new Tile(30, 20), new Tile(30, 30),
            new Tile(40, 30), new Tile(40, 40), new Tile(20, 40),
        };

        private static List<Tile> FloorIn(WorldModel w, TileRect b)
        {
            var found = new List<Tile>();
            for (int y = b.Y; y <= b.Bottom; y++)
            for (int x = b.X; x <= b.Right; x++)
                if (w.Grid.TerrainAt(x, y) == Terrain.Floor) found.Add(new Tile(x, y));
            return found;
        }

        [Test]
        public void TheCornerThatIsNotTheBuildingGoesBackToBeingGround()
        {
            var world = Build(Ell());

            // Deep inside the bite, and well clear of the outline itself.
            Assert.That(world.Grid.TerrainAt(36, 24), Is.EqualTo(Terrain.Grass),
                        "the part of the rectangle the building does not occupy should be the "
                      + "ground it was standing on, not wall or floor");
        }

        [Test]
        public void TheBuildingStillStandsWhereItIsTheBuilding()
        {
            var world = Build(Ell());
            var floor = FloorIn(world, world.AllPlaces[0].Bounds);
            Assert.That(floor.Count, Is.GreaterThan(20),
                        "cutting the shape should not have demolished the whole house");
        }

        [Test]
        public void EveryRoomCanStillBeWalkedToFromTheFrontDoor()
        {
            var world = Build(Ell());
            var place = world.AllPlaces[0];
            var b = place.Bounds;

            Assert.That(world.Grid.TerrainAt(place.Door), Is.EqualTo(Terrain.Floor),
                        "the front door has to be something you can stand in");

            var seen = new HashSet<int>();
            var queue = new Queue<Tile>();
            seen.Add(place.Door.Y * world.Width + place.Door.X);
            queue.Enqueue(place.Door);
            while (queue.Count > 0)
            {
                var at = queue.Dequeue();
                foreach (var n in new[] { new Tile(at.X + 1, at.Y), new Tile(at.X - 1, at.Y),
                                          new Tile(at.X, at.Y + 1), new Tile(at.X, at.Y - 1) })
                {
                    if (!b.Contains(n)) continue;
                    if (world.Grid.TerrainAt(n) != Terrain.Floor) continue;
                    if (!seen.Add(n.Y * world.Width + n.X)) continue;
                    queue.Enqueue(n);
                }
            }

            var floor = FloorIn(world, b);
            var stranded = floor.FindAll(t => !seen.Contains(t.Y * world.Width + t.X));
            Assert.That(stranded, Is.Empty,
                        $"{stranded.Count} floor tiles are walled off from the front door. A room "
                      + "nobody can reach is how the town gets cut into pieces.");
        }

        [Test]
        public void NoFloorIsLeftOpenToTheStreet()
        {
            var world = Build(Ell());
            var b = world.AllPlaces[0].Bounds;
            var door = world.AllPlaces[0].Door;

            foreach (var t in FloorIn(world, b))
            {
                if (t.X == door.X && t.Y == door.Y) continue;
                foreach (var n in new[] { new Tile(t.X + 1, t.Y), new Tile(t.X - 1, t.Y),
                                          new Tile(t.X, t.Y + 1), new Tile(t.X, t.Y - 1) })
                {
                    var terrain = world.Grid.TerrainAt(n);
                    Assert.That(terrain == Terrain.Floor || terrain == Terrain.Wall, Is.True,
                                $"floor at {t.X},{t.Y} touches {terrain} at {n.X},{n.Y} - the cut "
                              + "has left a room standing open to the outside");
                }
            }
        }

        [Test]
        public void AnOutlineTheDoorIsNotInsideIsIgnoredRatherThanObeyed()
        {
            // A ring covering only the top half, while the door is at the bottom. Obeying it
            // would wall the door out of its own house; the stamper is supposed to decline.
            var world = Build(new[]
            {
                new Tile(20, 20), new Tile(40, 20), new Tile(40, 28), new Tile(20, 28),
            });

            Assert.That(world.Grid.TerrainAt(world.AllPlaces[0].Door), Is.EqualTo(Terrain.Floor),
                        "the door has to survive an outline that does not contain it");
            Assert.That(FloorIn(world, world.AllPlaces[0].Bounds).Count, Is.GreaterThan(20),
                        "declining the outline should leave the ordinary rectangular house");
        }
    }
}
