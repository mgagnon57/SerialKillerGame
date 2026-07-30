using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The lane graph decides which movements exist in the city, so a mistake in it is a car
    /// driving into oncoming traffic rather than a cosmetic fault. These pin the rules that are
    /// easy to get backwards: which side of the road each direction uses, which lane a turn
    /// leaves from, and that no route ever ends in mid-air.
    /// </summary>
    [TestFixture]
    public class LaneGraphTests
    {
        private const string Header = "village Test\nsize 240 240\nterrain path 0,0 240x240\n";

        /// <summary>Northgate's shape: two freeway arterials, two main roads, four junctions.</summary>
        private const string Grid =
            "road northgate 30 0,75 239,75\n  class freeway\n"
          + "road franklin 30 0,165 239,165\n  class mainroad\n"
          + "road first 30 75,0 75,239\n  class mainroad\n"
          + "road second 30 165,0 165,239\n  class freeway\n";

        private static LaneGraph Build(string map, out WorldModel world)
        {
            TestContent.EnsureKinds();
            world = WorldBuilder.Build(VillageParser.Parse(map), 1234UL);
            return new LaneGraph(world.Roads, world.Width, world.Height);
        }

        private static LaneGraph Northgate() => Build(Header + Grid, out _);

        // ---- the driving side ---------------------------------------------------------

        [Test]
        public void TrafficKeepsRight()
        {
            // Village coordinates are x east, y south, so for a travel vector (dx,dy) the
            // right-hand side is (-dy,dx). Northbound is (0,-1) and its right is east.
            Assert.That(Headings.Side(Heading.North), Is.EqualTo(1), "northbound keeps east");
            Assert.That(Headings.Side(Heading.South), Is.EqualTo(-1), "southbound keeps west");
            Assert.That(Headings.Side(Heading.East), Is.EqualTo(1), "eastbound keeps south");
            Assert.That(Headings.Side(Heading.West), Is.EqualTo(-1), "westbound keeps north");
        }

        [Test]
        public void TurningRightGoesRoundTheCompass()
        {
            Assert.That(Headings.Right(Heading.North), Is.EqualTo(Heading.East));
            Assert.That(Headings.Right(Heading.East), Is.EqualTo(Heading.South));
            Assert.That(Headings.Right(Heading.South), Is.EqualTo(Heading.West));
            Assert.That(Headings.Right(Heading.West), Is.EqualTo(Heading.North));

            Assert.That(Headings.Left(Heading.North), Is.EqualTo(Heading.West));
            Assert.That(Headings.Back(Heading.North), Is.EqualTo(Heading.South));
        }

        [Test]
        public void TravelCoordinatesAlwaysRunForwards()
        {
            // The whole point of travel coordinates: a car's progress rises whichever way it
            // points, so no caller has to carry a sign.
            var graph = Northgate();
            foreach (var segment in graph.Segments)
                Assert.That(segment.Length, Is.GreaterThan(0f),
                            $"segment {segment.Index} on line {segment.Line} runs backwards");

            Assert.That(LaneGraph.AlongOf(Heading.North, LaneGraph.TravelOf(Heading.North, 42f)),
                        Is.EqualTo(42f).Within(0.001f), "the conversion round-trips");
        }

        // ---- the shape of the graph ----------------------------------------------------

        [Test]
        public void EveryLaneIsCutAtEveryJunctionItCrosses()
        {
            var graph = Northgate();

            // Each road crosses the two roads on the other axis, so each of its lanes is cut
            // into three: edge to first junction, between them, last junction to edge.
            // Freeways carry two lanes each way, main roads one.
            //   northgate 2 lanes x 2 ways x 3 = 12      second 2 x 2 x 3 = 12
            //   franklin  1 lane  x 2 ways x 3 =  6      first  1 x 2 x 3 =  6
            Assert.That(graph.Segments.Count, Is.EqualTo(36));

            foreach (var segment in graph.Segments)
                Assert.That(segment.FromJunction, Is.Not.EqualTo(segment.ToJunction),
                            "a segment cannot start and end at the same junction");
        }

        [Test]
        public void EveryRouteEntersAndLeavesTheCity()
        {
            var graph = Northgate();

            // Eight approaches (four roads x two ends) times the lanes on each.
            Assert.That(graph.Entries.Count, Is.EqualTo(2 + 2 + 1 + 1 + 2 + 2 + 1 + 1));

            foreach (var segment in graph.Segments)
            {
                if (!segment.IsExit)
                    Assert.That(graph.TurnsFrom(segment.Index), Is.Not.Empty,
                                $"segment {segment.Index} stops at junction "
                              + $"{segment.ToJunction} with nowhere to go");
            }
        }

        [Test]
        public void EverySegmentIsReachableFromAnEntry()
        {
            // A lane nothing can reach is a lane the traffic will never use, which is the sort
            // of fault that looks like "the city is a bit quiet" rather than like a bug.
            var graph = Northgate();

            var seen = new HashSet<int>(graph.Entries);
            var queue = new Queue<int>(graph.Entries);
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                foreach (int t in graph.TurnsFrom(at))
                {
                    int next = graph.Turns[t].To;
                    if (seen.Add(next)) queue.Enqueue(next);
                }
            }

            Assert.That(seen.Count, Is.EqualTo(graph.Segments.Count),
                        "some lanes cannot be reached from any entry");
        }

        // ---- the turn rules -------------------------------------------------------------

        [Test]
        public void NobodyMayTurnRoundInAJunction()
        {
            var graph = Northgate();
            foreach (var turn in graph.Turns)
            {
                var from = graph.Segments[turn.From];
                var to = graph.Segments[turn.To];
                Assert.That(to.Way, Is.Not.EqualTo(Headings.Back(from.Way)),
                            "a U-turn was offered");
            }
        }

        [Test]
        public void GoingStraightStaysOnTheSameRoadAndTheSameLane()
        {
            var graph = Northgate();
            foreach (var turn in graph.Turns.Where(t => t.Kind == TurnKind.Straight))
            {
                var from = graph.Segments[turn.From];
                var to = graph.Segments[turn.To];
                Assert.That(to.Line, Is.EqualTo(from.Line));
                Assert.That(to.Lane, Is.EqualTo(from.Lane));
                Assert.That(to.Way, Is.EqualTo(from.Way));
            }
        }

        [Test]
        public void LeftTurnsLeaveFromTheInsideLaneAndRightTurnsFromTheOutside()
        {
            var graph = Build(Header + Grid, out var world);

            foreach (var turn in graph.Turns)
            {
                var from = graph.Segments[turn.From];
                var to = graph.Segments[turn.To];
                int fromLanes = RoadClasses.LanesEachWay(world.Roads.Lines[from.Line].Class);
                int toLanes = RoadClasses.LanesEachWay(world.Roads.Lines[to.Line].Class);

                switch (turn.Kind)
                {
                    case TurnKind.Left:
                        Assert.That(from.Lane, Is.EqualTo(0), "a left turn crosses from the inside");
                        Assert.That(to.Lane, Is.EqualTo(0));
                        break;
                    case TurnKind.Right:
                        Assert.That(from.Lane, Is.EqualTo(fromLanes - 1),
                                    "a right turn is made from the kerbside lane");
                        Assert.That(to.Lane, Is.EqualTo(toLanes - 1));
                        break;
                }
            }
        }

        [Test]
        public void AFourLaneRoadAndATwoLaneRoadStillConnect()
        {
            // Northgate is a freeway and First Street a main road; they cross at 75,75. The
            // rule has to join two lanes to one without a special case for the pair.
            var graph = Build(Header + Grid, out var world);

            int northgate = -1, first = -1;
            for (int i = 0; i < world.Roads.Lines.Count; i++)
            {
                if (world.Roads.Lines[i].Name == "northgate") northgate = i;
                if (world.Roads.Lines[i].Name == "first") first = i;
            }
            Assert.That(northgate, Is.GreaterThanOrEqualTo(0));
            Assert.That(first, Is.GreaterThanOrEqualTo(0));

            bool freewayOntoMainroad = graph.Turns.Any(t =>
                graph.Segments[t.From].Line == northgate &&
                graph.Segments[t.To].Line == first);
            bool mainroadOntoFreeway = graph.Turns.Any(t =>
                graph.Segments[t.From].Line == first &&
                graph.Segments[t.To].Line == northgate);

            Assert.That(freewayOntoMainroad, Is.True, "nothing can turn off the arterial");
            Assert.That(mainroadOntoFreeway, Is.True, "nothing can turn onto the arterial");
        }

        [Test]
        public void EveryJunctionOffersAllThreeMovementsFromEveryApproach()
        {
            var graph = Build(Header + Grid, out var world);

            for (int j = 0; j < world.Roads.Junctions.Count; j++)
            {
                var junction = world.Roads.Junctions[j];
                var arriving = graph.Segments.Where(s => s.ToJunction == j).ToList();

                // Both directions of both roads arrive, so the count follows from the two
                // classes that meet - 8 where two freeways cross, 4 where two main roads do,
                // 6 at the two mixed ones. Northgate has one of each kind of junction.
                int expected = 2 * RoadClasses.LanesEachWay(junction.NorthSouth.Class)
                             + 2 * RoadClasses.LanesEachWay(junction.EastWest.Class);
                Assert.That(arriving.Count, Is.EqualTo(expected),
                            $"junction {j}: {junction.NorthSouth.Name} x {junction.EastWest.Name}");

                foreach (var segment in arriving)
                {
                    var kinds = graph.TurnsFrom(segment.Index)
                                     .Select(t => graph.Turns[t].Kind).ToList();
                    Assert.That(kinds, Does.Contain(TurnKind.Straight),
                                $"segment {segment.Index} cannot carry on");

                    // The inside lane turns left, the kerbside lane turns right; on a one-lane
                    // road the single lane is both.
                    int lanes = RoadClasses.LanesEachWay(world.Roads.Lines[segment.Line].Class);
                    if (segment.Lane == 0)
                        Assert.That(kinds, Does.Contain(TurnKind.Left));
                    if (segment.Lane == lanes - 1)
                        Assert.That(kinds, Does.Contain(TurnKind.Right));
                }
            }
        }

        // ---- geometry ------------------------------------------------------------------

        [Test]
        public void SegmentsStopShortOfTheJunctionTheyRunInto()
        {
            // The gap either side of a junction is where the crossing itself is; a segment that
            // ran to the junction's centre would put a car halfway across it before it had
            // decided which way to turn.
            var graph = Build(Header + Grid, out var world);

            foreach (var segment in graph.Segments)
            {
                if (segment.IsExit) continue;
                var junction = world.Roads.Junctions[segment.ToJunction];
                var line = world.Roads.Lines[segment.Line];

                float centre = line.IsNorthSouth ? junction.Y : junction.X;
                float endsAt = LaneGraph.AlongOf(segment.Way, segment.ToS);

                Assert.That(System.Math.Abs(centre - endsAt), Is.EqualTo(junction.Reach).Within(0.01f),
                            "a segment ends exactly one junction-reach short of the middle");
            }
        }

        [Test]
        public void ARoadWithNoJunctionsIsOneSegmentEachWay()
        {
            var graph = Build(Header + "road lonely 30 0,75 239,75\n  class mainroad\n", out _);

            Assert.That(graph.Segments.Count, Is.EqualTo(2));
            Assert.That(graph.Turns, Is.Empty);
            Assert.That(graph.Entries.Count, Is.EqualTo(2));
            foreach (var segment in graph.Segments)
                Assert.That(segment.IsEntry && segment.IsExit, Is.True);
        }

        [Test]
        public void TheRealCityBuildsAGraph()
        {
            TestContent.EnsureKinds();
            var world = WorldBuilder.Build(
                VillageParser.Parse(TestContent.Read("city.txt")), 1234UL);
            var graph = new LaneGraph(world.Roads, world.Width, world.Height);

            Assert.That(graph.Segments.Count, Is.GreaterThan(0));
            Assert.That(graph.Turns.Count, Is.GreaterThan(0));
            Assert.That(graph.Entries.Count, Is.GreaterThan(0));

            foreach (var segment in graph.Segments)
                if (!segment.IsExit)
                    Assert.That(graph.TurnsFrom(segment.Index), Is.Not.Empty);
        }
    }
}
