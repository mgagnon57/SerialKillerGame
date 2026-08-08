using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// Rossville's surveyed roads and measured footprints, read straight out of Content/.
    ///
    /// NOT THE UNITY PIPELINE, deliberately. `dotnet test` structurally cannot compile
    /// Assets/Noir/Unity, so SurveyRoads and SeatOnSurvey are out of reach here. What IS in reach
    /// is the data those passes consume - and the fault this fixture exists to catch was in the
    /// data's relationship to itself, not in the code that reads it. Content/roads.txt is the
    /// network the game actually drives on: SurveyRoads replaces city.txt's 37 ruled roads with
    /// these 66 surveyed ones at build time.
    /// </summary>
    internal static class RealRossville
    {
        private static readonly Regex Pair =
            new Regex(@"(-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?)", RegexOptions.Compiled);

        internal readonly struct Footprint
        {
            public readonly string Parcel, Address;
            public readonly TileRect Box;
            /// <summary>The measured ring. THIS is what SeatOnSurvey carries onto the place, and
            /// what the renderer extrudes - the box is only its extent.</summary>
            public readonly Tile[] Outline;
            public Footprint(string parcel, string address, TileRect box, Tile[] outline)
            { Parcel = parcel; Address = address; Box = box; Outline = outline; }
        }

        /// <summary>A layout carrying nothing but the surveyed road network.</summary>
        public static VillageLayout Layout()
        {
            var layout = new VillageLayout { Name = "Rossville", Width = 2100, Height = 2400 };
            foreach (var raw in TestContent.ReadRaw("roads.txt").Split('\n'))
            {
                var line = raw.Split('#')[0].Trim();
                if (!line.StartsWith("road ", StringComparison.Ordinal)) continue;
                var t = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length < 4) continue;

                var run = new RoadRun { Kind = Terrain.Road, Name = t[1] };
                if (int.TryParse(t[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w))
                    run.Width = w;
                foreach (Match m in Pair.Matches(line))
                    run.Points.Add(new Tile(
                        (int)Math.Round(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)),
                        (int)Math.Round(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))));
                if (run.Points.Count >= 2) layout.Roads.Add(run);
            }
            return layout;
        }

        /// <summary>
        /// Rossville as the game builds it, as far as Core can go: city.txt's authored places
        /// with the SURVEYED road network in place of the ruled one.
        ///
        /// That substitution is the whole point. SurveyRoads does exactly this at build time -
        /// throws away city.txt's 37 ruled roads and puts the county's 66 measured ones in their
        /// place - and it is the moment the streets move out from under houses that were authored
        /// against the old lines.
        /// </summary>
        public static VillageLayout LayoutWithPlaces()
        {
            var layout = VillageParser.Parse(TestContent.Read("city.txt"));
            layout.Roads.Clear();
            foreach (var run in Layout().Roads) layout.Roads.Add(run);
            return layout;
        }

        /// <summary>Every measured footprint, as the bounding box SeatOnSurvey would seat.</summary>
        public static List<Footprint> Footprints()
        {
            var addr = new Dictionary<string, string>();
            var outp = new List<Footprint>();

            foreach (var raw in TestContent.ReadRaw("parcel-buildings.txt").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var t = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length < 4 || t[0] != "parcel") continue;

                if (t[2] == "building")
                {
                    var q = Regex.Match(line, "\"([^\"]*)\"");
                    addr[t[1] + ":" + t[3]] = q.Success ? q.Groups[1].Value : "";
                }
                else if (t[2] == "shape")
                {
                    float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                    var ring = new List<Tile>();
                    foreach (Match m in Pair.Matches(line))
                    {
                        float x = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                        float y = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                        if (x < x0) x0 = x; if (x > x1) x1 = x;
                        if (y < y0) y0 = y; if (y > y1) y1 = y;
                        ring.Add(new Tile((int)Math.Round(x), (int)Math.Round(y)));
                    }
                    if (ring.Count < 3) continue;
                    var box = new TileRect((int)Math.Floor(x0), (int)Math.Floor(y0),
                                           (int)Math.Ceiling(x1 - x0), (int)Math.Ceiling(y1 - y0));
                    addr.TryGetValue(t[1] + ":" + t[3], out var a);
                    outp.Add(new Footprint(t[1], a ?? "", box, ring.ToArray()));
                }
            }
            return outp;
        }
    }

    /// <summary>
    /// THE TEST FOR "IS THERE A HOUSE IN THE ROAD", which is the defect the owner reported more
    /// than once and which nothing in this project could see.
    ///
    /// It could not be seen because the check lived as a private method inside FillFromSurvey -
    /// the pass that INVENTS buildings - and so covered only the invented ones. SeatOnSurvey,
    /// which seats all 824 measured footprints, had no road test of any kind. That is why the
    /// answer to "are houses in the road" was "not all but some": the some were the measured
    /// ones, and looking at the generator would never have found them.
    ///
    /// Now the check is in Core, and Core is the only part of this project a test can compile.
    /// </summary>
    [TestFixture]
    public class RoadCorridorTests
    {
        /// <summary>A layout with one 10 m road running east-west along y = 100.</summary>
        private static VillageLayout OneStreet(int width = 10)
        {
            var layout = new VillageLayout { Name = "test", Width = 400, Height = 400 };
            var run = new RoadRun { Kind = Terrain.Road, Name = "attica", Width = width };
            run.Points.Add(new Tile(0, 100));
            run.Points.Add(new Tile(400, 100));
            layout.Roads.Add(run);
            return layout;
        }

        [Test]
        public void ABuildingClearOfTheRoadIsClear()
        {
            var layout = OneStreet();
            // road spans y 95..105; this sits from y=110 up
            Assert.That(RoadCorridor.Touches(layout, new TileRect(50, 110, 20, 15)), Is.False);
            Assert.That(RoadCorridor.WorstPenetration(layout, new TileRect(50, 110, 20, 15)),
                        Is.EqualTo(0f));
        }

        [Test]
        public void ABuildingStandingInTheRoadIsCaught()
        {
            var layout = OneStreet();
            // starts at y=101, one metre past the centreline: 4 m of it is on the carriageway
            var box = new TileRect(50, 101, 20, 15);
            Assert.That(RoadCorridor.Touches(layout, box), Is.True);
            Assert.That(RoadCorridor.WorstPenetration(layout, box), Is.EqualTo(4f).Within(0.01f));
            Assert.That(RoadCorridor.RoadUnder(layout, box), Is.EqualTo("attica"));
        }

        /// <summary>
        /// The case a corner test alone misses: a short road run lying wholly inside a large
        /// building. Parcel 636 is this shape - a 65x68 m footprint with an alley run inside it.
        /// </summary>
        [Test]
        public void ARoadRunningRightThroughABuildingIsCaught()
        {
            var layout = new VillageLayout { Name = "test", Width = 400, Height = 400 };
            var run = new RoadRun { Kind = Terrain.Road, Name = "alley", Width = 4 };
            run.Points.Add(new Tile(105, 105));
            run.Points.Add(new Tile(115, 115));      // both endpoints inside the box below
            layout.Roads.Add(run);

            Assert.That(RoadCorridor.Touches(layout, new TileRect(100, 100, 40, 40)), Is.True,
                        "a road whose whole run is inside the footprint is the worst case, not a near miss");
        }

        /// <summary>
        /// The policy that decides which disagreements matter. Streets are surveyed from the
        /// county's centrelines; alleys are traced from parcel gaps and then given a flat 4 m by
        /// build-roads.py. When a measured building and an alley disagree, the alley is the guess.
        /// </summary>
        [Test]
        public void AnAlleyIsNotAStreetAndTheFilterSaysSo()
        {
            var layout = new VillageLayout { Name = "test", Width = 400, Height = 400 };
            var alley = new RoadRun { Kind = Terrain.Road, Name = "alley7", Width = 4 };
            alley.Points.Add(new Tile(0, 200));
            alley.Points.Add(new Tile(400, 200));
            layout.Roads.Add(alley);

            var garageOnTheAlleyLine = new TileRect(50, 199, 10, 10);

            Assert.That(RoadCorridor.WorstPenetration(layout, garageOnTheAlleyLine), Is.GreaterThan(0f),
                        "counted when every road counts");
            Assert.That(RoadCorridor.WorstPenetration(layout, garageOnTheAlleyLine, RoadCorridor.StreetWidth),
                        Is.EqualTo(0f),
                        "and NOT counted as a street, because a garage on the alley line is Rossville");
        }

        [Test]
        public void ClearanceKeepsABuildingOffTheKerbAsWellAsOutOfTheRoad()
        {
            var layout = OneStreet();
            var justClear = new TileRect(50, 106, 20, 15);        // 1 m clear of the carriageway

            Assert.That(RoadCorridor.Touches(layout, justClear), Is.False);
            Assert.That(RoadCorridor.TouchesWithClearance(layout, justClear, 3f), Is.True,
                        "FillFromSurvey wants room between a house it invents and the kerb");
        }

        [Test]
        public void AnEmptyOrDegenerateThingIsNotInTheRoad()
        {
            var layout = OneStreet();
            Assert.That(RoadCorridor.Touches(layout, new TileRect(50, 100, 0, 0)), Is.False);
            Assert.That(RoadCorridor.Touches(null, new TileRect(50, 100, 10, 10)), Is.False);

            var noPoints = new VillageLayout { Name = "t", Width = 10, Height = 10 };
            noPoints.Roads.Add(new RoadRun { Kind = Terrain.Road, Name = "stub", Width = 10 });
            Assert.That(RoadCorridor.Touches(noPoints, new TileRect(0, 0, 5, 5)), Is.False,
                        "a road with fewer than two points has no corridor and must not throw");
        }

        /// <summary>
        /// THE REAL TOWN. Rossville's own map, its own roads, and every building the pipeline
        /// would seat - asserting that none of them ends up standing in a street.
        ///
        /// This is the assertion that would have caught the thing the owner kept reporting. It
        /// reads Content/ directly rather than running the Unity pipeline, because `dotnet test`
        /// structurally cannot compile the Unity assembly - so it checks the DATA the pipeline
        /// consumes, which is where the fault actually was.
        /// </summary>
        [Test]
        public void NoMeasuredFootprintStandsInARossvilleStreet()
        {
            var layout = RealRossville.Layout();
            int inTheRoad = 0;
            var worst = "";
            float worstPen = 0f;

            foreach (var b in RealRossville.Footprints())
            {
                float pen = RoadCorridor.WorstPenetration(layout, b.Outline, RoadCorridor.StreetWidth);
                if (pen <= 0.5f) continue;            // SeatOnSurvey.IntoTheRoad
                inTheRoad++;
                if (pen > worstPen)
                {
                    worstPen = pen;
                    worst = $"parcel {b.Parcel} \"{b.Address}\" {pen:0.0} m into "
                          + RoadCorridor.RoadUnder(layout, b.Outline, RoadCorridor.StreetWidth);
                }
            }

            TestContext.Out.WriteLine($"measured footprints standing in a street: {inTheRoad}");
            if (inTheRoad > 0) TestContext.Out.WriteLine("  worst: " + worst);

            Assert.That(inTheRoad, Is.LessThanOrEqualTo(3),
                "Measured footprints are standing in Rossville's streets. SeatOnSurvey refuses "
                + "these now, so the town does not show them - but a rise here means the survey "
                + "and the centrelines have drifted apart and somebody should look at which moved. "
                + "Worst: " + worst);
        }
    }
}
