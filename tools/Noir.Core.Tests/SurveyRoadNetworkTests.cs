using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// THE ROADS THE GAME ACTUALLY BUILDS. Every other test in this suite loads
    /// `Content/city.txt`; the game has not driven on city.txt's roads since the survey layer
    /// landed. `SurveyRoads.Apply` replaces them wholesale with `Content/roads.txt` whenever that
    /// file exists, and it does - 66 roads instead of 37, traced from the county's own
    /// centrelines, with 33 alleys city.txt never had.
    ///
    /// So the suite was measuring a road network nothing drives on. That is not a small gap. On
    /// 2026-08-07 a change to `RoadNetwork.Crossings` DELETED FIVE JUNCTIONS from the live town
    /// and the whole Core suite stayed green, because not one test had ever looked at the network
    /// those junctions were in. Both bugs found that day were trivially assertable and nothing was
    /// watching:
    ///
    ///   - junctions that were not on either of their own roads - Railroad Avenue "crossed" an
    ///     alley 214 m away, Route 1 "crossed" one 1,118 m away;
    ///   - lanes that arrived at a junction with no way out, which park a car on Hold.NoLegalTurn
    ///     for the rest of the run and stand its whole queue behind it.
    ///
    /// These are the invariants, not the numbers. The counts below are recorded so drift is
    /// visible, but the two structural tests are the ones that matter: they hold whatever anybody
    /// re-derives roads.txt into.
    /// </summary>
    [TestFixture]
    public class SurveyRoadNetworkTests
    {
        /// <summary>
        /// The town as the game builds it: city.txt for its size and terrain, with the survey's
        /// roads swapped in exactly as SurveyRoads.Apply does at runtime.
        /// </summary>
        private static WorldModel SurveyTown()
        {
            TestContent.EnsureKinds();

            var layout = VillageParser.Parse(TestContent.ReadRaw("city.txt"));
            var surveyed = VillageParser.Parse(ReadSurveyRoads()).Roads;

            Assert.That(surveyed.Count, Is.GreaterThan(0), "Content/roads.txt parsed to no roads");

            layout.Roads.Clear();
            layout.Roads.AddRange(surveyed);

            return WorldBuilder.Build(layout, 1234UL);
        }

        private static string ReadSurveyRoads()
        {
            string path = Path.Combine(RepoRoot(), "Content", "roads.txt");
            Assert.That(File.Exists(path), Is.True,
                "Missing Content/roads.txt - it is DERIVED and rebuildable with " +
                "`python tools/build-roads.py`, but the game loads it and so does this test.");
            return File.ReadAllText(path);
        }

        /// <summary>
        /// EVERY JUNCTION IS ON BOTH ITS ROADS. This is the one that was silently false.
        ///
        /// A Junction records how far along each road the crossing falls (SNorthSouth, SEastWest).
        /// Ask each road where that is and you must arrive back at the junction's own X,Y. If you
        /// do not, the crossing was never on that road - `Crossings` found a sign flip in the
        /// projected lateral with no crossing under it, which is what happens when the nearest
        /// dense sample jumps between segments as one road passes a bend far away.
        /// </summary>
        [Test]
        public void EveryJunctionLandsOnBothOfItsOwnRoads()
        {
            var world = SurveyTown();
            var offenders = new List<string>();

            foreach (var j in world.Roads.Junctions)
            {
                foreach (var (road, s, which) in new[]
                {
                    (j.NorthSouth, j.SNorthSouth, "north-south"),
                    (j.EastWest,   j.SEastWest,   "east-west"),
                })
                {
                    if (road?.Path == null) continue;

                    var back = road.Path.PointAt(s);
                    float dx = back.X - j.X, dy = back.Y - j.Y;
                    float off = (float)System.Math.Sqrt(dx * dx + dy * dy);

                    // A metre of slack: the hit is interpolated to one resample pitch, and a real
                    // crossing reprojects to a small fraction of that.
                    if (off > 1f)
                        offenders.Add($"{j.NorthSouth?.Name} x {j.EastWest?.Name} at " +
                                      $"({j.X:0},{j.Y:0}) is {off:0.0}m off its {which} road");
                }
            }

            Assert.That(offenders, Is.Empty,
                "These junctions are not on the roads they claim to join:\n  " +
                string.Join("\n  ", offenders) + "\n\n" +
                "A junction that is not on its own roads is a stop line, a junction tile and a " +
                "signal head laid across open ground, and the lanes cut to reach it arrive " +
                "somewhere they cannot leave. See RoadNetwork.Crossings - the walked branch " +
                "reprojects the hit and requires it to land ON the other road for exactly this " +
                "reason.");
        }

        /// <summary>
        /// NO LANE ARRIVES SOMEWHERE IT CANNOT LEAVE.
        ///
        /// A segment that ends at a junction must have at least one turn out of it. When two
        /// junctions land closer together than their own reach, LaneGraph drops the lane piece
        /// between them and the lanes feeding that junction are left with nowhere to go -
        /// `Choose` returns -1, CityTraffic sets Hold.NoLegalTurn, and nothing ever revisits that
        /// state. The car parks there permanently and everything behind it queues on a road that
        /// will never clear.
        /// </summary>
        [Test]
        public void NoLaneArrivesAtAJunctionItCannotLeave()
        {
            var world = SurveyTown();
            var graph = new LaneGraph(world.Roads, world.Width, world.Height);
            var stranded = new List<string>();

            foreach (var segment in graph.Segments)
            {
                if (segment.ToJunction < 0) continue;              // runs off the map, fine
                if (graph.TurnsFrom(segment.Index).Count > 0) continue;

                var road = world.Roads.Lines[segment.Line];
                stranded.Add($"segment {segment.Index} on {road?.Name} lane {segment.Lane} " +
                             $"{segment.Way} ends at junction {segment.ToJunction} with no turn out");
            }

            Assert.That(stranded, Is.Empty,
                "These lanes end at a junction with no way out:\n  " +
                string.Join("\n  ", stranded) + "\n\n" +
                "Every car that reaches one stops there for the rest of the run and stands its " +
                "whole queue. If a junction near it is bogus, fix the junction; if two real " +
                "junctions are genuinely closer than their reach, LaneGraph has to merge them " +
                "rather than drop the lane between them.");
        }

        /// <summary>
        /// The counts, recorded rather than defended. roads.txt is DERIVED and re-runnable, so
        /// these move whenever it is regenerated and that is legitimate - but a silent move is
        /// how five junctions were deleted without anything noticing. Wide bounds catch a
        /// collapse; the printout is what a person actually reads.
        /// </summary>
        [Test]
        public void TheSurveyNetworkIsTheSizeItShouldBe()
        {
            var world = SurveyTown();
            var graph = new LaneGraph(world.Roads, world.Width, world.Height);

            TestContext.Out.WriteLine($"survey roads     = {world.Roads.Lines.Count}");
            TestContext.Out.WriteLine($"survey junctions = {world.Roads.Junctions.Count}");
            TestContext.Out.WriteLine($"survey segments  = {graph.Segments.Count}");
            TestContext.Out.WriteLine($"survey turns     = {graph.Turns.Count}");
            TestContext.Out.WriteLine($"survey entries   = {graph.Entries.Count}");

            Assert.That(world.Roads.Lines.Count, Is.GreaterThan(50),
                "Content/roads.txt should carry the whole surveyed network, not a fragment.");
            Assert.That(world.Roads.Junctions.Count, Is.InRange(40, 90),
                $"{world.Roads.Junctions.Count} junctions. Outside this range something has " +
                "either collapsed or is inventing crossings - both have happened.");
            Assert.That(graph.Segments.Count, Is.GreaterThan(world.Roads.Lines.Count),
                "fewer lane segments than roads means lanes are being dropped");
            Assert.That(graph.Turns.Count, Is.GreaterThan(0), "no turns: nothing can cross a junction");
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "Noir", "Core", "Observation")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not find Assets/Noir/Core/Observation above " + System.AppContext.BaseDirectory);
        }
    }
}
