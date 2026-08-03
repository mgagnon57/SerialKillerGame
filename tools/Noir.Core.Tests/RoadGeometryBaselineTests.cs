using NUnit.Framework;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// What the road network and the lane graph produce for the REAL town, pinned.
    ///
    /// Phase A rewrites how both are computed - junctions become path intersections, lanes
    /// become arc length - and the whole safety argument is that the 27 straight roads
    /// Rossville is built from come out unchanged. That claim is only worth anything if it is
    /// a number somebody recorded before the rewrite started.
    ///
    /// These figures were READ OFF THE BUILD, not off any document. docs/STATE.md quotes
    /// counts from a 960x960 map that no longer exists.
    /// </summary>
    [TestFixture]
    public class RoadGeometryBaselineTests
    {
        public static WorldModel RealCity()
        {
            TestContent.EnsureKinds();
            return WorldBuilder.Build(
                VillageParser.Parse(TestContent.ReadRaw("city.txt")), 1234UL);
        }

        [Test]
        public void TheRealCityHasTheRoadsAndJunctionsItHadBeforePhaseA()
        {
            var world = RealCity();
            var graph = new LaneGraph(world.Roads, world.Width, world.Height);

            TestContext.Out.WriteLine($"roads      = {world.Roads.Lines.Count}");
            TestContext.Out.WriteLine($"junctions  = {world.Roads.Junctions.Count}");
            TestContext.Out.WriteLine($"segments   = {graph.Segments.Count}");
            TestContext.Out.WriteLine($"turns      = {graph.Turns.Count}");
            TestContext.Out.WriteLine($"entries    = {graph.Entries.Count}");

            Assert.That(world.Roads.Lines.Count, Is.EqualTo(27), "roads in city.txt");
            Assert.That(world.Roads.Junctions.Count, Is.EqualTo(BaselineJunctions));
            Assert.That(graph.Segments.Count, Is.EqualTo(BaselineSegments));
            Assert.That(graph.Turns.Count, Is.EqualTo(BaselineTurns));
            Assert.That(graph.Entries.Count, Is.EqualTo(BaselineEntries));
        }

        [Test]
        public void EveryRealRoadIsStraightAndAxisAligned()
        {
            // The premise of the whole zero-regression argument. If this ever fails, a curve
            // has entered the map and the equivalence tests below stopped meaning what they say.
            foreach (var line in RealCity().Roads.Lines)
                Assert.That(line.IsStraight, Is.True, line.Name + " is not straight");
        }

        [Test]
        public void EveryRealRoadsPathReproducesItsOldCentreExactly()
        {
            // The zero-regression guarantee, asserted against real content rather than a
            // fixture. Centre is the single float Phase A is replacing; if Path disagrees with
            // it anywhere on any of the 27 roads, the town has moved.
            foreach (var line in RealCity().Roads.Lines)
            {
                Assert.That(line.Path, Is.Not.Null, line.Name + " has no path");
                Assert.That(line.Path.IsStraightAxisAligned, Is.True, line.Name);
                Assert.That(line.Path.Length, Is.EqualTo(line.To - line.From).Within(0f),
                            line.Name + " length");

                for (int step = 0; step <= 10; step++)
                {
                    float s = line.Path.Length * (step / 10f);
                    var p = line.Path.PointAt(s);

                    float across = line.IsNorthSouth ? p.X : p.Y;
                    float along = line.IsNorthSouth ? p.Y : p.X;

                    Assert.That(across, Is.EqualTo(line.Centre),
                                line.Name + " drifted off its centre at s=" + s);
                    Assert.That(along, Is.EqualTo(line.From + s),
                                line.Name + " is not where From+s says at s=" + s);
                }
            }
        }

        [Test]
        public void APathsTangentAgreesWithTheAxisTheLineSaysItRunsOn()
        {
            foreach (var line in RealCity().Roads.Lines)
            {
                var t = line.Path.TangentAt(line.Path.Length * 0.5f);
                if (line.IsNorthSouth)
                    Assert.That(t.X, Is.EqualTo(0f), line.Name + " is N-S but its tangent has x");
                else
                    Assert.That(t.Y, Is.EqualTo(0f), line.Name + " is E-W but its tangent has y");
            }
        }

        // Filled in at Step 3 from the run in Step 2.
        private const int BaselineJunctions = 142;
        private const int BaselineSegments = 620;
        private const int BaselineTurns = 1692;
        private const int BaselineEntries = 54;
    }
}
