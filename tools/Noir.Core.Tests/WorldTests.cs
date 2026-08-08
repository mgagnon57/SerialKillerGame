using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>Helper: the repo's Content/ folder, copied beside the test assembly.</summary>
    internal static class TestContent
    {
        public static string Read(string name)
        {
            EnsureKinds();
            return ReadRaw(name);
        }

        public static string ReadRaw(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Content", name);
            if (!File.Exists(path))
                Assert.Fail($"content file not found: {path}");
            return File.ReadAllText(path);
        }

        /// <summary>
        /// The authored kinds table, in place before any test reads a `place` line — so that the
        /// tests are looking at the same seventeen rows the harness and the renderer are, rather
        /// than at the bootstrap copy compiled into Core.
        /// </summary>
        public static void EnsureKinds()
        {
            if (PlaceKindTable.IsInstalled) return;
            PlaceKindTable.Install(PlaceKindTable.Parse(ReadRaw("kinds.txt")));
        }
    }

    [TestFixture]
    public class VillageParserTests
    {
        private const string Sample = @"
village Testholm
size 20 12

terrain field 0,0 20x3
road main 3 1,6 18,6

place tavern 4,2 6x4 ""The Anchor""
  door 6,5
  hours 11:00-14:30
  hours 17:30-23:00 mon-sat
  jobs 2
  human A room where the same men sit in the same order.

place green 12,8 5x3 ""the Green""
  human Flat, wet, and crossed by nobody in winter.
";

        [Test]
        public void ParsesHeaderAndSize()
        {
            var layout = VillageParser.Parse(Sample);
            Assert.That(layout.Name, Is.EqualTo("Testholm"));
            Assert.That(layout.Width, Is.EqualTo(20));
            Assert.That(layout.Height, Is.EqualTo(12));
        }

        [Test]
        public void ParsesTerrainRoadsAndPlaces()
        {
            var layout = VillageParser.Parse(Sample);
            Assert.That(layout.Terrain, Has.Count.EqualTo(1));
            Assert.That(layout.Roads, Has.Count.EqualTo(1));
            Assert.That(layout.Roads[0].Points, Has.Count.EqualTo(2));
            Assert.That(layout.Places, Has.Count.EqualTo(2));
        }

        [Test]
        public void ParsesPlaceAttributes()
        {
            var pub = VillageParser.Parse(Sample).Places[0];
            Assert.That(pub.Kind, Is.EqualTo(PlaceKind.Tavern));
            Assert.That(pub.Name, Is.EqualTo("The Anchor"));
            Assert.That(pub.Door, Is.EqualTo(new Tile(6, 5)));
            Assert.That(pub.JobSlots, Is.EqualTo(2));
            Assert.That(pub.Hours, Has.Count.EqualTo(2));
            Assert.That(pub.Human, Does.StartWith("A room where"));
        }

        [Test]
        public void ParsesTimesIntoMinutes()
        {
            var pub = VillageParser.Parse(Sample).Places[0];
            Assert.That(pub.Hours[0].StartMinute, Is.EqualTo(11 * 60));
            Assert.That(pub.Hours[0].EndMinute, Is.EqualTo(14 * 60 + 30));
        }

        [Test]
        public void ParsesDayMasks()
        {
            var pub = VillageParser.Parse(Sample).Places[0];
            Assert.That(pub.Hours[0].DaysMask, Is.EqualTo(OpenWindow.AllDays));
            Assert.That(pub.Hours[1].DaysMask, Is.EqualTo(0b0011_1111), "mon-sat");
        }

        [Test]
        public void CommentsAndBlankLinesAreIgnored()
        {
            var layout = VillageParser.Parse("# a comment\n\nvillage X\nsize 4 4  # trailing\n");
            Assert.That(layout.Name, Is.EqualTo("X"));
            Assert.That(layout.Width, Is.EqualTo(4));
        }

        [Test]
        public void UnknownDirectiveReportsTheLineNumber()
        {
            var ex = Assert.Throws<VillageParseException>(() =>
                VillageParser.Parse("village X\nsize 4 4\nwibble 1 2\n"));
            Assert.That(ex.Message, Does.Contain("line 3"));
        }

        [Test]
        public void BuildingWithoutADoorIsRejected()
        {
            Assert.Throws<VillageParseException>(() =>
                VillageParser.Parse("village X\nsize 20 20\nplace tavern 2,2 5x5 \"P\"\n"));
        }

        [Test]
        public void PlaceOutsideTheMapIsRejected()
        {
            Assert.Throws<VillageParseException>(() =>
                VillageParser.Parse("village X\nsize 10 10\nplace tavern 8,8 5x5 \"P\"\n  door 9,9\n"));
        }
    }

    [TestFixture]
    public class WorldBuilderTests
    {
        private static WorldModel BuildSample()
        {
            return WorldBuilder.Build(VillageParser.Parse(@"
village B
size 30 20
road main 3 1,15 28,15
place tavern 4,4 7x6 ""P""
  door 7,9
  human somewhere
"));
        }

        [Test]
        public void BuildingHasWallsAroundAFloor()
        {
            var w = BuildSample();

            Assert.That(w.Grid.TerrainAt(4, 4), Is.EqualTo(Terrain.Wall), "corner should be wall");
            Assert.That(w.Grid.BlocksSight(4, 4), Is.True);
            Assert.That(w.Grid.IsWalkable(4, 4), Is.False);

            // Not a specific tile: interiors are generated now, so any given inside tile may
            // legitimately be an internal wall. What must hold is that there IS floor in there.
            int floor = 0;
            for (int y = 5; y <= 8; y++)
            for (int x = 5; x <= 9; x++)
                if (w.Grid.TerrainAt(x, y) == Terrain.Floor) floor++;

            Assert.That(floor, Is.GreaterThan(0), "the building has no floor inside it at all");
        }

        [Test]
        public void DoorIsCarvedThroughTheWall()
        {
            var w = BuildSample();
            Assert.That(w.Grid.TerrainAt(7, 9), Is.EqualTo(Terrain.Floor));
            Assert.That(w.Grid.IsWalkable(7, 9), Is.True, "you must be able to walk through a door");
        }

        [Test]
        public void RoadIsWalkableAndCheapest()
        {
            var w = BuildSample();
            Assert.That(w.Grid.TerrainAt(10, 15), Is.EqualTo(Terrain.Road));
            Assert.That(w.Grid.MoveCost(10, 15), Is.LessThan(w.Grid.MoveCost(10, 2)),
                "road should be quicker than open grass");
        }

        [Test]
        public void RoadHasTheRequestedWidth()
        {
            var w = BuildSample();
            Assert.That(w.Grid.TerrainAt(10, 14), Is.EqualTo(Terrain.Road));
            Assert.That(w.Grid.TerrainAt(10, 16), Is.EqualTo(Terrain.Road));
            Assert.That(w.Grid.TerrainAt(10, 13), Is.Not.EqualTo(Terrain.Road));
        }

        [Test]
        public void TilesAreClaimedByTheirPlace()
        {
            var w = BuildSample();
            var id = w.Grid.PlaceAt(7, 6);
            Assert.That(id.IsValid, Is.True);
            Assert.That(w.GetPlace(id).Name, Is.EqualTo("P"));
            Assert.That(w.Grid.PlaceAt(20, 2).IsValid, Is.False, "open ground belongs to no place");
        }

        [Test]
        public void SameLayoutBuildsIdenticallyEveryTime()
        {
            var a = BuildSample();
            var b = BuildSample();
            for (int i = 0; i < a.Grid.Count; i++)
            {
                var t = a.Grid.TileAt(i);
                Assert.That(b.Grid.TerrainAt(t), Is.EqualTo(a.Grid.TerrainAt(t)), $"diverged at {t}");
            }
        }
    }

    /// <summary>
    /// Content has to be additive, and this is the test that says whether it is.
    ///
    /// It was not. Inserting one building at the top of the 345-place file used to reshuffle
    /// fifty-four other buildings' interiors — buildings with byte-identical footprints, nowhere
    /// near the edit — because every interior was drawn from one stream threaded through the
    /// file in order, so a place's rooms depended on how many places came before it. Narrowing
    /// one building by one tile moved a hundred props for the same reason. That is the kind of
    /// thing that gets worse every week: at 61 places it is an oddity, at 600 it means nobody
    /// can add a shop without re-checking the whole village.
    ///
    /// Interiors now hang off the building's own name and props off a 32-metre chunk of ground,
    /// so an edit can only reach as far as the thing edited.
    /// </summary>
    [TestFixture]
    public class ContentIsAdditiveTests
    {
        /// <summary>
        /// A cottage in the empty field between The Firs and Long Barrow Farm, inserted as the
        /// FIRST place in the file so that every other place's PlaceId shifts by one. Nothing
        /// else is on this ground and no road crosses it, so the only tiles that change are its
        /// own.
        /// </summary>
        private const string Inserted =
            "place cottage 105,6 10x10 \"Test Cottage\"\n" +
            "  door 110,15\n" +
            "  human Somebody put a house here to see what would move.\n";

        /// <summary>Which 32x32 chunk of ground a tile belongs to, matching PropGenerator.</summary>
        private static (int, int) ChunkOf(Tile t) => (t.X / 32, t.Y / 32);

        /// <summary>The one chunk the new cottage stands in: x 105..114, y 6..15.</summary>
        private static readonly (int, int) Disturbed = (3, 0);

        private static (WorldModel before, WorldModel after) BuildBothWays()
        {
            string text = TestContent.Read("fixture-village.txt");

            int firstPlace = text.IndexOf("place church", StringComparison.Ordinal);
            Assert.That(firstPlace, Is.GreaterThan(0), "village.txt no longer starts its places with the church");

            return (WorldBuilder.Build(VillageParser.Parse(text)),
                    WorldBuilder.Build(VillageParser.Parse(
                        text.Substring(0, firstPlace) + Inserted + text.Substring(firstPlace))));
        }

        /// <summary>Every room and every stick of furniture in one building, as a string.</summary>
        private static string Describe(WorldModel world, PlaceId id)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var roomId in world.RoomsIn(id))
            {
                var room = world.GetRoom(roomId);
                sb.Append(room.Kind).Append(' ').Append(room.Bounds).Append(" [");
                foreach (var f in world.AllFurniture)
                    if (f.Room == room.Id) sb.Append(f.Kind).Append('@').Append(f.Footprint).Append(' ');
                sb.Append("] ");
            }
            return sb.ToString();
        }

        [Test]
        public void AddingABuildingChangesNoOtherInterior()
        {
            var (before, after) = BuildBothWays();

            var moved = new List<string>();
            int compared = 0;

            foreach (var place in before.AllPlaces)
            {
                PlaceId twin = PlaceId.None;
                foreach (var other in after.AllPlaces)
                    if (other.Name == place.Name) { twin = other.Id; break; }

                Assert.That(twin.IsValid, Is.True, $"'{place.Name}' vanished when a cottage was added");

                string was = Describe(before, place.Id);
                if (was.Length == 0) continue;

                compared++;
                if (Describe(after, twin) != was) moved.Add(place.Name);
            }

            Assert.That(compared, Is.GreaterThan(40), "almost nothing had an interior — the test proved nothing");
            Assert.That(moved, Is.Empty,
                $"{moved.Count} of {compared} interiors were rebuilt by adding an unrelated cottage: "
              + string.Join(", ", moved.ToArray()));
        }

        [Test]
        public void AddingABuildingMovesNoPropOutsideItsOwnChunk()
        {
            var (before, after) = BuildBothWays();

            var was = new Dictionary<string, int>();
            var now = new Dictionary<string, int>();

            void Tally(WorldModel world, Dictionary<string, int> into)
            {
                foreach (var p in world.AllProps)
                {
                    if (ChunkOf(p.At) == Disturbed) continue;
                    string key = p.Kind + "@" + p.At + "#" + p.Variant;
                    into[key] = into.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }

            Tally(before, was);
            Tally(after, now);

            Assert.That(was.Count, Is.GreaterThan(500),
                "hardly any props were compared — the test proved nothing");

            var differences = new List<string>();
            foreach (var pair in was)
                if (!now.TryGetValue(pair.Key, out int n) || n != pair.Value)
                    differences.Add(pair.Key + " (gone)");
            foreach (var pair in now)
                if (!was.ContainsKey(pair.Key)) differences.Add(pair.Key + " (new)");

            Assert.That(differences, Is.Empty,
                $"{differences.Count} props moved outside the cottage's own 32x32 chunk. "
              + "First few: " + string.Join(", ", differences.GetRange(0, Math.Min(8, differences.Count)).ToArray()));
        }

        [Test]
        public void TheAddedBuildingItselfIsRealAndFurnished()
        {
            // Otherwise the two tests above would pass on a cottage that was never built.
            var (_, after) = BuildBothWays();

            PlaceId added = PlaceId.None;
            foreach (var p in after.AllPlaces)
                if (p.Name == "Test Cottage") { added = p.Id; break; }

            Assert.That(added.IsValid, Is.True, "the cottage was not added at all");
            Assert.That(after.RoomsIn(added).Count, Is.GreaterThan(0), "the cottage has no rooms");

            bool furnished = false;
            foreach (var roomId in after.RoomsIn(added))
                foreach (var f in after.AllFurniture)
                    if (f.Room == roomId) { furnished = true; break; }

            Assert.That(furnished, Is.True, "the cottage is empty, so nothing about it was generated");
        }

        [Test]
        public void TwoPlacesMayNotShareAKey()
        {
            // Everything inside a building comes from its key, so two places sharing one would be
            // two copies of the same interior — and the failure would be invisible.
            var ex = Assert.Throws<InvalidOperationException>(() => WorldBuilder.Build(VillageParser.Parse(
                "village D\nsize 40 20\n" +
                "place cottage 2,2 8x8 \"Same\"\n  door 6,9\n  human x\n" +
                "place cottage 20,2 8x8 \"Same\"\n  door 24,9\n  human x\n")));

            Assert.That(ex.Message, Does.Contain("Same"));
            Assert.That(ex.Message, Does.Contain("key"), "the message should say how to fix it");
        }

        [Test]
        public void AKeyLineSeparatesTwoPlacesWithTheSameName()
        {
            var world = WorldBuilder.Build(VillageParser.Parse(
                "village D\nsize 40 20\n" +
                "place cottage 2,2 8x8 \"The Cottage\"\n  door 6,9\n  key north cottage\n  human x\n" +
                "place cottage 20,2 8x8 \"The Cottage\"\n  door 24,9\n  key south cottage\n  human x\n"));

            Assert.That(world.PlaceCount, Is.EqualTo(2));
            Assert.That(world.AllPlaces[0].Key, Is.Not.EqualTo(world.AllPlaces[1].Key));
        }

        [Test]
        public void EveryPlaceInAshcombeHasItsOwnKey()
        {
            var world = WorldBuilder.Build(VillageParser.Parse(TestContent.Read("fixture-village.txt")));
            var seen = new Dictionary<ulong, string>();

            foreach (var place in world.AllPlaces)
            {
                Assert.That(seen.ContainsKey(place.Key), Is.False,
                    $"'{place.Name}' and '{(seen.TryGetValue(place.Key, out var other) ? other : "?")}' hash alike");
                seen[place.Key] = place.Name;
            }
        }
    }

    [TestFixture]
    public class PlaceHoursTests
    {
        private static Place Tavern()
        {
            var layout = VillageParser.Parse(@"
village H
size 20 20
place tavern 2,2 6x5 ""P""
  door 4,6
  hours 11:00-14:30
  hours 17:30-23:00
  human x
");
            return WorldBuilder.Build(layout).AllPlaces[0];
        }

        [Test]
        public void ClosedBetweenSittings()
        {
            var p = Tavern();
            Assert.That(p.IsOpenAt(12 * 60, 0), Is.True);
            Assert.That(p.IsOpenAt(16 * 60, 0), Is.False, "closed in the afternoon");
            Assert.That(p.IsOpenAt(20 * 60, 0), Is.True);
            Assert.That(p.IsOpenAt(2 * 60, 0), Is.False);
        }

        [Test]
        public void MinutesUntilOpenCountsForward()
        {
            var p = Tavern();
            Assert.That(p.MinutesUntilOpen(12 * 60, 0), Is.EqualTo(0), "already open");
            Assert.That(p.MinutesUntilOpen(16 * 60, 0), Is.EqualTo(90), "90 minutes to evening opening");
        }

        [Test]
        public void PlacesWithNoHoursAreAlwaysOpen()
        {
            var layout = VillageParser.Parse("village H\nsize 20 20\nplace green 2,2 4x4 \"G\"\n  human x\n");
            var g = WorldBuilder.Build(layout).AllPlaces[0];
            Assert.That(g.AlwaysOpen, Is.True);
            Assert.That(g.IsOpenAt(3 * 60, 2), Is.True);
        }
    }

    /// <summary>
    /// The regression test that matters: the real, authored village must load and be sane.
    /// This is what stops a careless edit to village.txt from quietly stranding a cottage.
    /// </summary>
    [TestFixture]
    public class AshcombeTests
    {
        private static WorldModel _world;

        [OneTimeSetUp]
        public void Load() =>
            _world = WorldBuilder.Build(VillageParser.Parse(TestContent.Read("fixture-village.txt")));

        [Test]
        public void LoadsWithTheExpectedShape()
        {
            Assert.That(_world.Name, Is.EqualTo("Ashcombe"));
            // Deliberately a range rather than exact numbers: the map is authored content and
            // will keep being edited. What matters is that it stays a village rather than
            // silently becoming a city or collapsing to a hamlet.
            Assert.That(_world.Width, Is.InRange(120, 260));
            Assert.That(_world.Height, Is.InRange(80, 200));
        }

        [Test]
        public void EveryDwellingHasRoomsIncludingSomewhereToSleep()
        {
            foreach (var id in _world.PlacesOfKind(PlaceKind.Dwelling))
            {
                var rooms = _world.RoomsIn(id);
                Assert.That(rooms.Count, Is.GreaterThan(0),
                    $"'{_world.GetPlace(id).Name}' has no rooms at all");

                bool hasBed = false;
                foreach (var roomId in rooms)
                    if (_world.GetRoom(roomId).Kind == RoomKind.Bedroom) { hasBed = true; break; }

                Assert.That(hasBed, Is.True,
                    $"'{_world.GetPlace(id).Name}' has nowhere to sleep");
            }
        }

        [Test]
        public void TheBathroomIsTheSmallestRoom()
        {
            // The precise invariant, rather than "not the biggest": the generator assigns the
            // bathroom the smallest available room, so nothing except the hall may be smaller.
            // Ties are fine and common — plenty of houses have rooms of equal size.
            foreach (var id in _world.PlacesOfKind(PlaceKind.Dwelling))
            {
                Room bathroom = null;
                foreach (var roomId in _world.RoomsIn(id))
                {
                    var room = _world.GetRoom(roomId);
                    if (room.Kind == RoomKind.Bathroom) { bathroom = room; break; }
                }
                if (bathroom == null) continue;

                foreach (var roomId in _world.RoomsIn(id))
                {
                    var room = _world.GetRoom(roomId);
                    if (room.Id == bathroom.Id || room.Kind == RoomKind.Hall) continue;

                    Assert.That(room.Area, Is.GreaterThanOrEqualTo(bathroom.Area),
                        $"'{_world.GetPlace(id).Name}': the {room.Kind} ({room.Area} m2) is smaller "
                      + $"than the bathroom ({bathroom.Area} m2)");
                }
            }
        }

        [Test]
        public void EveryRoomIsReachableFromTheFrontDoor()
        {
            // The whole village is one connected region (asserted elsewhere), so if a room's
            // anchor is walkable and the village is connected, it is reachable. This catches
            // an interior sealed off by its own walls.
            foreach (var room in _world.AllRooms)
                Assert.That(_world.Grid.IsWalkable(room.Anchor), Is.True,
                    $"{room.Kind} in '{_world.GetPlace(room.Building).Name}' has an unwalkable centre");
        }

        [Test]
        public void HousesAreNotAllTheSame()
        {
            var shapes = new HashSet<string>();
            foreach (var id in _world.PlacesOfKind(PlaceKind.Dwelling))
            {
                var sb = new System.Text.StringBuilder();
                foreach (var roomId in _world.RoomsIn(id))
                {
                    var r = _world.GetRoom(roomId);
                    sb.Append(r.Kind).Append(r.Bounds.W).Append('x').Append(r.Bounds.H).Append(';');
                }
                shapes.Add(sb.ToString());
            }

            int dwellings = _world.PlacesOfKind(PlaceKind.Dwelling).Count;
            Assert.That(shapes.Count, Is.GreaterThan(dwellings / 2),
                $"only {shapes.Count} distinct layouts across {dwellings} houses — they are too samey");
        }

        [Test]
        public void FurnitureNeverBlocksAWalkway()
        {
            // Furniture sits against walls and the floor stays walkable, so pathfinding never
            // has to know it exists.
            foreach (var f in _world.AllFurniture)
                for (int y = f.Footprint.Y; y <= f.Footprint.Bottom; y++)
                for (int x = f.Footprint.X; x <= f.Footprint.Right; x++)
                    Assert.That(_world.Grid.IsWalkable(x, y), Is.True,
                        $"{f.Kind} at {x},{y} is standing somewhere unwalkable");
        }

        [Test]
        public void EveryBedroomHasABed()
        {
            foreach (var room in _world.AllRooms)
            {
                if (room.Kind != RoomKind.Bedroom) continue;

                bool bed = false;
                foreach (var f in _world.AllFurniture)
                    if (f.Room == room.Id && f.Kind == FurnitureKind.Bed) { bed = true; break; }

                Assert.That(bed, Is.True,
                    $"a bedroom in '{_world.GetPlace(room.Building).Name}' "
                  + $"({room.Bounds.W}x{room.Bounds.H}) has no bed");
            }
        }

        [Test]
        public void HasNoLayoutProblems()
        {
            var report = WorldValidator.Validate(_world);
            Assert.That(report.IsValid, Is.True, report.ToString());
        }

        [Test]
        public void IsFullyConnected()
        {
            var report = WorldValidator.Validate(_world);
            Assert.That(report.LargestRegion, Is.EqualTo(report.WalkableTiles),
                "every walkable tile must be reachable from every other");
        }

        [Test]
        public void HasEnoughHomesForAVillage()
        {
            Assert.That(_world.PlacesOfKind(PlaceKind.Dwelling).Count, Is.GreaterThanOrEqualTo(40));
        }

        [Test]
        public void HasTheAmenitiesAVillageNeeds()
        {
            foreach (var kind in new[]
            {
                PlaceKind.Tavern, PlaceKind.Shop, PlaceKind.Church, PlaceKind.School,
                PlaceKind.Clinic, PlaceKind.PostOffice, PlaceKind.Mill,
                PlaceKind.Farm, PlaceKind.Garage, PlaceKind.VillageHall, PlaceKind.Playground
            })
                Assert.That(_world.PlacesOfKind(kind).Count, Is.GreaterThan(0), $"no {kind} in the village");
        }

        [Test]
        public void HasEnoughWorkForAboutHalfThePopulation()
        {
            // Tied to the number of HOMES rather than to an absolute count of jobs.
            //
            // It was InRange(35, 70), pinned to a hundred-person village, and it fired the
            // moment Ashcombe grew - which is exactly when a test about PROPORTION should not.
            // The village had gained an estate and then a factory to employ it; the ratio was
            // fine and the constant was stale.
            //
            // Roughly 2.2 people to a home, about half of them of working age, and not all of
            // those work in the village.
            int homes = 0;
            foreach (var id in _world.PlacesOfKind(PlaceKind.Dwelling))
                homes += _world.GetPlace(id).Units;

            Assert.That(_world.TotalJobSlots, Is.InRange((int)(homes * 0.6), (int)(homes * 1.4)),
                $"{_world.TotalJobSlots} jobs against {homes} homes");
        }

        [Test]
        public void EveryWorkplaceIsOpenAtSomePointInTheWeek()
        {
            foreach (var p in _world.AllPlaces)
            {
                if (p.JobSlots == 0) continue;
                Assert.That(p.MinutesUntilOpen(0, 0), Is.GreaterThanOrEqualTo(0),
                    $"'{p.Name}' employs {p.JobSlots} people but never opens");
            }
        }
    }
}
