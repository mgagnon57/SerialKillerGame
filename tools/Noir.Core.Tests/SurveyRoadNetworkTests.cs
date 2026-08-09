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
        /// EVERY JUNCTION LANDS ON EVERY ROAD IT CLAIMS TO JOIN. This is the one that was
        /// silently false.
        ///
        /// A Junction records how far along each of its roads the crossing falls. Ask each road
        /// where that is and you must arrive somewhere the junction actually covers. If you do
        /// not, the crossing was never on that road - `Crossings` found a sign flip in the
        /// projected lateral with no crossing under it, which is what happens when the nearest
        /// dense sample jumps between segments as one road passes a bend far away. Railroad
        /// Avenue "crossed" an alley 214 m off, and Route 1 one 1,118 m off.
        ///
        /// TWO CLAIMS, AND THE SECOND IS WHY THIS SURVIVED THE MERGE.
        ///
        /// It used to be one: the recorded point must be within a metre of the junction. That is
        /// the right tolerance for a single crossing - the hit is interpolated to one resample
        /// pitch - and the wrong one for a merged node, which has DELIBERATELY been moved to the
        /// middle of several. Maple ends and Park begins where Chicago Street goes past; that is
        /// one piece of tarmac with three roads on it, and no single point is within a metre of
        /// all three. Held to the metre, the clusterer had to refuse the merge, and refusing it
        /// left two stop lines inside one another's reach with the lane between them dropped -
        /// `NoLaneArrivesAtAJunctionItCannotLeave`, five of them on 2026-08-09.
        ///
        /// So the size test is the junction's OWN REACH, the square of tarmac it occupies, and it
        /// is joined by a sharper one that does not care about size at all: the S on record must
        /// be the NEAREST the road ever comes to the junction. A bogus crossing fails that
        /// however wide the roads are, which is what actually caught the 214 m alley.
        /// </summary>
        [Test]
        public void EveryJunctionLandsOnEveryRoadItClaimsToJoin()
        {
            var world = SurveyTown();
            var offenders = new List<string>();

            foreach (var j in world.Roads.Junctions)
            {
                string name = $"{j.NorthSouth?.Name} x {j.EastWest?.Name} at ({j.X:0},{j.Y:0})";

                foreach (var arm in j.Arms)
                {
                    var road = arm.Road;
                    if (road?.Path == null) continue;

                    var back = road.Path.PointAt(arm.S);
                    float dx = back.X - j.X, dy = back.Y - j.Y;
                    float off = (float)System.Math.Sqrt(dx * dx + dy * dy);

                    if (off > j.Reach)
                        offenders.Add($"{name} is {off:0.0}m off {road.Name}, " +
                                      $"which is more than its own {j.Reach:0.0}m reach");

                    // NEAREST, not merely near. Ask the road for the closest it comes to this
                    // junction and it must be the same place the junction says it is. A sign
                    // flip with no crossing under it lands somewhere else entirely and says so
                    // here whatever the corridor widths are.
                    var (nearestS, _) = road.Path.Project(new Vec2(j.X, j.Y));
                    var nearest = road.Path.PointAt(nearestS);
                    float nx = nearest.X - back.X, ny = nearest.Y - back.Y;
                    float slip = (float)System.Math.Sqrt(nx * nx + ny * ny);

                    // One resample pitch. Project walks the same dense samples PointAt reads.
                    if (slip > 1f)
                        offenders.Add($"{name} records {road.Name}@{arm.S:0.0}, but the nearest " +
                                      $"that road comes to it is {slip:0.0}m away at {nearestS:0.0}");
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
            // 40-90 UNTIL 2026-08-09, AND HERE IS WHY IT MOVED.
            //
            // The alleys used to reach nothing. `derive-alleys.py` blanks 11 m around every street
            // centreline before tracing and nothing put the mouth back, so 31 of 33 alleys touched
            // no street at either end and a car could not enter one from any direction. Opening
            // those mouths is what makes junctions: a back lane meeting a street IS a junction, and
            // there were 58 of them missing.
            //
            // 117 raw, 109 after JUNC-1's clustering folds crossings closer together than
            // reachA + reachB into single nodes. That gap of 8 is the clustering working - each one
            // was a pair of stop lines inside each other's reach, which is the exact shape that
            // strands the lane between them and parks a car there for the rest of the run.
            //
            // The band is still a band and not the number, because it is guarding against the two
            // things that have actually happened here: the network collapsing to a handful, and the
            // finder inventing crossings out of near-misses. 90 to 130 keeps both in view.
            Assert.That(world.Roads.Junctions.Count, Is.InRange(90, 130),
                $"{world.Roads.Junctions.Count} junctions. Outside this range something has " +
                "either collapsed or is inventing crossings - both have happened.");
            Assert.That(graph.Segments.Count, Is.GreaterThan(world.Roads.Lines.Count),
                "fewer lane segments than roads means lanes are being dropped");
            Assert.That(graph.Turns.Count, Is.GreaterThan(0), "no turns: nothing can cross a junction");
        }

        /// <summary>
        /// A COORDINATE ON A ROAD'S AXIS COMES BACK TO THAT COORDINATE. The invariant that four
        /// separate pieces of this codebase assumed and none of them checked.
        ///
        /// `RoadPath` measures arc length from Points[0]; a road's declared axis runs low to high
        /// whichever way it was authored. Where those disagree - which is every county segment
        /// walked right to left - `along - line.From` is not an arc length and PointAt gives back
        /// somewhere else entirely. It was written out in LaneGraph twice, in CityTraffic once,
        /// and once more as `line.From + line.Path.Length` for where a lane stops, which laid
        /// alley13 thirty-six metres of lane past the end of the alley.
        ///
        /// Walked over BOTH maps, at half a metre, over exactly the range LaneGraph lays lanes
        /// across, because the failure was never uniform - it was 0.5 m on a road declared the
        /// convenient way and 608 m on one that was not.
        /// </summary>
        [Test]
        public void EveryBentRoadFindsItsWayBackToACoordinateOnItsOwnAxis()
        {
            TestContent.EnsureKinds();
            var authored = WorldBuilder.Build(
                VillageParser.Parse(TestContent.ReadRaw("city.txt")), 1234UL);

            var offenders = new List<string>();

            foreach (var (label, world) in new[] { ("city.txt", authored), ("roads.txt", SurveyTown()) })
            foreach (var line in world.Roads.Lines)
            {
                if (line.Path == null || line.IsStraight) continue;

                float lo = line.IsNorthSouth ? line.Path.MinY : line.Path.MinX;
                float hi = line.IsNorthSouth ? line.Path.MaxY : line.Path.MaxX;

                float worst = 0f, worstAt = 0f;
                for (float along = lo; along <= hi; along += 0.5f)
                {
                    float arc = line.Path.ArcAt(along, line.IsNorthSouth);

                    // PointAt clamps, so only the path's own span is a round trip. Inside the
                    // bounding box every coordinate has a point, so this never skips anything.
                    if (arc < 0f || arc > line.Path.Length) continue;

                    var at = line.Path.PointAt(arc);
                    float slip = (line.IsNorthSouth ? at.Y : at.X) - along;
                    if (slip < 0f) slip = -slip;
                    if (slip > worst) { worst = slip; worstAt = along; }
                }

                // One resample pitch and a half: ArcAt interpolates between dense samples a metre
                // apart, and a road running nearly square across its own axis resolves to the
                // near end of that stretch rather than to the asked-for coordinate exactly.
                if (worst > 1.5f)
                    offenders.Add($"{label} {line.Name} " +
                                  $"({(line.IsNorthSouth ? "N-S" : "E-W")}, {lo:0}..{hi:0}) is " +
                                  $"{worst:0.0} m out when asked for {worstAt:0}");
            }

            Assert.That(offenders, Is.Empty,
                "These roads do not come back to the coordinate they were asked for:\n  " +
                string.Join("\n  ", offenders) + "\n\n" +
                "Everything that positions anything on a bent road goes through this: where the " +
                "lane is cut, which way the road is heading there, where a lane stops, and where " +
                "the car is drawn. See RoadPath.ArcAt.");
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
