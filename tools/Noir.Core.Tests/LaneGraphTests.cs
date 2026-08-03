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
        public void ALaneOnlyRunsPastTheMapWhereTheROADDoes()
        {
            // A farm track that comes off a street in the middle of the map ends AT that street.
            // Lanes run a margin past the edge of the world so traffic arrives from off-stage,
            // and applying that to an end which is not the edge sent a van thirty metres out
            // into a field, driving in a straight line on nothing. Caught in Play, pinned here.
            // A 360m map, because the track is declared out to x=359 and a road declared past
            // the edge of its own map is a different bug from the one under test.
            const string Big = "village Test\nsize 360 360\nterrain path 0,0 360x360\n";
            var graph = Build(Big
                            + "road second 30 165,0 165,359\n  class freeway\n"
                            + "road backtrack 10 150,318 359,318\n  class track\n", out var world);

            int track = -1;
            for (int i = 0; i < world.Roads.Lines.Count; i++)
                if (world.Roads.Lines[i].Name == "backtrack") track = i;
            Assert.That(track, Is.GreaterThanOrEqualTo(0));

            foreach (var segment in graph.Segments)
            {
                if (segment.Line != track) continue;

                float a = LaneGraph.AlongOf(segment.Way, segment.FromS);
                float b = LaneGraph.AlongOf(segment.Way, segment.ToS);

                // Second Street's corridor is x 150..180, so nothing on this track may reach
                // west of it. The east end may overhang, because there the map does end.
                Assert.That(System.Math.Min(a, b), Is.GreaterThanOrEqualTo(149.9f),
                            "a track segment runs west of the street it joins");
                Assert.That(System.Math.Max(a, b),
                            Is.LessThanOrEqualTo(world.Width + graph.Margin + 0.1f));
            }
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

        [Test]
        public void ACurvedRoadGetsLanes()
        {
            // `if (!line.IsStraight) continue` meant a bent road got no segments at all, so no
            // car could ever be placed on it. This is the line Phase A exists to delete.
            var graph = Build(Header
                + "road bend 30 20,20 20,120 60,180 140,200\n  class mainroad\n"
                + "road cross 30 0,190 239,190\n  class mainroad\n", out var world);

            var bend = world.Roads.Lines[0];
            Assert.That(bend.IsStraight, Is.False, "the fixture is meant to bend");

            int onBend = 0;
            foreach (var seg in graph.Segments) if (seg.Line == 0) onBend++;
            Assert.That(onBend, Is.GreaterThan(0), "a curved road must carry lanes");
        }

        [Test]
        public void NoSegmentAnywhereHasNegativeOrZeroLength()
        {
            var graph = Build(Header
                + "road bend 30 20,20 20,120 60,180 140,200\n  class mainroad\n"
                + "road cross 30 0,190 239,190\n  class mainroad\n", out _);

            foreach (var seg in graph.Segments)
                Assert.That(seg.Length, Is.GreaterThan(0f),
                            "segment " + seg.Index + " on line " + seg.Line + " has no length");
        }

        [Test]
        public void EveryArrivalStillHasALegalWayOut()
        {
            // The invariant that mattered most before Phase A and still does: a car reaching a
            // junction must have somewhere to go, or it vanishes or wedges.
            var graph = Build(Header + Grid, out _);
            foreach (var seg in graph.Segments)
            {
                if (seg.IsExit) continue;
                Assert.That(graph.TurnsFrom(seg.Index).Count, Is.GreaterThan(0),
                            "segment " + seg.Index + " arrives at a junction with no way out");
            }
        }

        [Test]
        public void TurnsAreStillClassifiedTheWayTheEnumClassifiedThem()
        {
            // Tangent-based classification must agree with Headings.Between on axis-aligned
            // roads, or every existing signal phase and give-way rule changes meaning.
            var graph = Build(Header + Grid, out _);
            foreach (var turn in graph.Turns)
            {
                var from = graph.Segments[turn.From];
                var to = graph.Segments[turn.To];
                var expected = Headings.Between(from.Way, to.Way);
                Assert.That(expected, Is.Not.Null, "a U-turn was offered");
                Assert.That(turn.Kind, Is.EqualTo(expected.Value),
                            "turn " + from.Way + "->" + to.Way + " classified differently");
            }
        }

        /// <summary>
        /// The real Illinois Route 1, as surveyed, on a map the size of the real one.
        ///
        /// These twelve points are the OSM centre line (way 22037977, ref IL 1) projected into
        /// village metres and rotated into the parcels' frame - the curve that runs down the
        /// corridor the county's own lots leave for it, where the straight x=750 we ship today
        /// puts 85% of its length inside somebody's back garden.
        ///
        /// It is a FIXTURE, not content. Phase A ships no curve in Content/city.txt, because the
        /// renderers still skip anything where !IsStraight and a road with traffic and no asphalt
        /// under it is worse than the straight one. This proves the geometry works so that Phase C
        /// can lay it down once Phase B has migrated the consumers.
        /// </summary>
        private const string RealRoute1 =
            "road route1 30 371,177 466,470 593,855 675,1109 747,1332 776,1423 "
          + "799,1491 839,1607 857,1687 863,1740 872,1876 881,2049\n  class mainroad\n";

        [Test]
        public void TheRealRoute1ProducesASaneLaneGraph()
        {
            var graph = Build(
                "village Rossville\nsize 2100 2400\nterrain path 0,0 2100x2400\n"
                + RealRoute1
                + "road attica 30 0,1335 2099,1335\n  class mainroad\n"
                + "road benton 10 496,1113 1530,1113\n  class street\n", out var world);

            var route1 = world.Roads.Lines[0];
            Assert.That(route1.IsStraight, Is.False, "the real Route 1 bends");
            Assert.That(route1.Path.Length, Is.GreaterThan(1900f), "it spans most of the map");

            Assert.That(world.Roads.Junctions.Count, Is.GreaterThanOrEqualTo(2),
                        "Route 1 crosses both Attica and Benton");

            int onRoute1 = 0;
            foreach (var seg in graph.Segments)
            {
                Assert.That(seg.Length, Is.GreaterThan(0f), "segment " + seg.Index + " has no length");
                if (seg.Line == 0) onRoute1++;
            }
            Assert.That(onRoute1, Is.GreaterThan(0), "the real curve carries lanes");

            foreach (var seg in graph.Segments)
                if (!seg.IsExit)
                    Assert.That(graph.TurnsFrom(seg.Index).Count, Is.GreaterThan(0),
                                "segment " + seg.Index + " arrives somewhere with no way out");
        }

        [Test]
        public void EveryLaneOnTheRealRoute1StaysWithinItsOwnCorridor()
        {
            // The claim that matters for Phase B: a lane offset from a curved centre line is
            // still ON the road. Measured perpendicular to the curve, which is the only
            // meaningful way to ask it once the road stops being axis-aligned.
            Build("village Rossville\nsize 2100 2400\nterrain path 0,0 2100x2400\n"
                  + RealRoute1
                  + "road attica 30 0,1335 2099,1335\n  class mainroad\n", out var world);

            var path = world.Roads.Lines[0].Path;
            float halfWidth = world.Roads.Lines[0].HalfWidth;

            for (float s = 0f; s <= path.Length; s += 25f)
            {
                var lane = path.PointAt(s) + path.NormalAt(s) * (halfWidth - 2f);
                var (_, lateral) = path.Project(lane);
                Assert.That(lateral, Is.EqualTo(halfWidth - 2f).Within(1.5f),
                            "a lane 2m inside the kerb reads as somewhere else at s=" + s);
            }
        }
    }
}
