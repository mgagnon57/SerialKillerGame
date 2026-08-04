using System;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// What the road network and the lane graph produce for the REAL town, pinned.
    ///
    /// Phase A rewrote how both are computed - junctions become path intersections, lanes
    /// become arc length - and the whole safety argument was that the 27 roads Rossville is
    /// built from, all straight at the time, came out unchanged. That claim is only worth
    /// anything if it is a number somebody recorded before the rewrite started.
    ///
    /// Phase B then put a curve into one of those 27: Chicago Street / Illinois Route 1 was
    /// given the alignment of OpenStreetMap way 22037977, and the baseline was re-recorded
    /// against it - 614 segments, 1656 turns.
    ///
    /// IT WAS BRIEFLY STRAIGHTENED IN ERROR on 2026-08-03 and put straight back. The OSM way carries
    /// `tiger:reviewed = no` and `tiger:county = Iroquois, IL`; Rossville is in Vermilion. It
    /// is unchecked TIGER import, and every other street in town is square to two or three
    /// degrees - which looked damning until the owner pointed out that this road IS THE HUBBARD
    /// TRAIL. A footpath from 1829 that became a highway in 1833, with the town platted square
    /// around it in 1857. A cardinal grid with a diagonal highway through it is exactly what
    /// that history produces, and the road curves. The counts below are the curve's.
    ///
    /// THE SIDE STREETS WERE THEN CUT TO THEIR REAL LENGTH, 2026-08-03, which is why these
    /// figures drop so far: 142 junctions to 59, 614 segments to 250. Every street used to run
    /// the identical span - a 1,034 x 1,541 m stencil with all of them starting and stopping
    /// together and running out into open field. Abner is really 99 m, not 1,541; Goodwine 128;
    /// Watson 145. Abner and Watson came out of the map entirely: at their real length nothing
    /// connects to them, and an isolated road's traffic drives off the world.
    ///
    /// Those extents are from OSM and are NOT independently corroborated - see the note in
    /// Content/city.txt. The positions are; the lengths are not. Owner accepted them on sight.
    ///
    /// THEN FIFTEEN ALLEYS WENT IN, which is why the figures climb again: 25 roads to 40, 59
    /// junctions to 113. A Midwestern plat is split down the middle of every block, and without
    /// them every block here was twice its true depth with each lot backing onto another lot.
    /// Unlike the extents these ARE corroborated twice over - the 1940 USDA aerial shows a
    /// weaker bright line exactly halfway between every pair of streets, and OSM maps them
    /// separately as `service=alley`. Both agree on where they run.
    ///
    /// They went in at ten metres, the Track width, and every block read as though it had a
    /// second street down its middle. RoadClass.Alley now carries a SIX-metre corridor, which
    /// moves every alley lane and is why the counts hold but the checksum moved.
    ///
    /// Then RAILROAD AVENUE, which was missing from both sources. It runs at +36.4 degrees off
    /// north-south against the CSX line's 33.9 - parallel within two and a half degrees, because
    /// it is the street that serves the track, and the 1940 aerial shows the rail-side industry
    /// set at the rail's angle rather than the grid's. rossville-streets.json could never have
    /// held it: that file gives one latitude or longitude per street and cannot express a
    /// diagonal. 40 roads to 41.
    ///
    /// What was actually wrong was the DOWNTOWN, which was authored on a straight line while
    /// the road curved away from it - the centreline passed inside the barber and the steam
    /// laundry and left the west shop row 94 m behind. The buildings moved, not the road. See
    /// docs/research/ROADS-AND-BLOCKS.md.
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

            Assert.That(world.Roads.Lines.Count, Is.EqualTo(41), "roads in city.txt");
            Assert.That(world.Roads.Junctions.Count, Is.EqualTo(BaselineJunctions));
            Assert.That(graph.Segments.Count, Is.EqualTo(BaselineSegments));
            Assert.That(graph.Turns.Count, Is.EqualTo(BaselineTurns));
            Assert.That(graph.Entries.Count, Is.EqualTo(BaselineEntries));
        }

        [Test]
        public void EverySegmentsGeometryMatchesTheRecordedChecksum()
        {
            // The five counts above catch a segment appearing or disappearing; they say nothing
            // about one MOVING. A uniform sub-metre offset applied to every FromS would still
            // produce 27 roads, 142 junctions, 614 segments, 1656 turns and 54 entries and pass
            // every assertion above unnoticed. This one is sensitive to the actual geometry of
            // every one of the 614 segments - which road, which way, which lane, and where it
            // starts and ends - not just how many of them there are.
            var world = RealCity();
            var graph = new LaneGraph(world.Roads, world.Width, world.Height);

            string checksum = SegmentChecksum(graph);
            TestContext.Out.WriteLine($"segment checksum = {checksum}");

            Assert.That(checksum, Is.EqualTo(BaselineSegmentChecksum));
        }

        /// <summary>
        /// A SHA-256 digest over every segment's Line, Way, Lane, FromS and ToS, taken in
        /// segment order - the per-segment geometry the counts above cannot see. Floats are
        /// folded in by their raw bits (<see cref="BitConverter.SingleToInt32Bits"/>) rather
        /// than their decimal text, so the digest cannot drift with how a float happens to
        /// format; the same segments in the same order always hash the same way.
        /// </summary>
        private static string SegmentChecksum(LaneGraph graph)
        {
            var text = new StringBuilder();
            foreach (var segment in graph.Segments)
            {
                text.Append(segment.Line).Append(',')
                    .Append((int)segment.Way).Append(',')
                    .Append(segment.Lane).Append(',')
                    .Append(BitConverter.SingleToInt32Bits(segment.FromS)).Append(',')
                    .Append(BitConverter.SingleToInt32Bits(segment.ToS)).Append(';');
            }

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
            return Convert.ToHexString(hash);
        }

        [Test]
        public void ChicagoAloneBendsEveryOtherRealRoadIsStraightAndAxisAligned()
        {
            // The premise of the whole zero-regression argument, updated for Phase B. It used
            // to be "every road in the map is straight"; Chicago Street / Illinois Route 1 now
            // follows its real surveyed alignment (Content/city.txt), a 14-point polyline, and
            // is a deliberate, permanent exception to it. This checks the narrower premise both
            // ways - chicago and railroad bend, nothing else does - so it fails just as loudly
            // whether a third road loses its straightness or one of these is straightened out.
            // RAILROAD AVENUE is the second exception, added 2026-08-03. It runs at +36.4
            // degrees off north-south against the CSX line's 33.9 - parallel within two and a
            // half degrees, because it is the street that serves the track. Its bend is the
            // rail's bend, not an error.
            foreach (var line in RealCity().Roads.Lines)
            {
                bool shouldBeStraight = line.Name != "chicago" && line.Name != "railroad";
                Assert.That(line.IsStraight, Is.EqualTo(shouldBeStraight),
                            shouldBeStraight ? line.Name + " should be straight"
                                              : line.Name + " should bend");
            }
        }

        [Test]
        public void EveryStraightRoadsPathReproducesItsOldCentreExactly()
        {
            // The zero-regression guarantee, asserted against real content rather than a
            // fixture. Centre is the single float Phase A is replacing; if Path disagrees with
            // it anywhere on any of the 26 straight roads, the town has moved. Chicago is
            // excluded on purpose, not by oversight: Phase B gave it a real curve, so its Path
            // no longer reduces to a single Centre float offset from a From/To run - that is
            // the intended effect of the change, not a regression in it.
            // ChicagoAloneBendsEveryOtherRealRoadIsStraightAndAxisAligned above is what pins
            // chicago's own shape down instead.
            foreach (var line in RealCity().Roads.Lines)
            {
                if (!line.IsStraight) continue;

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
        public void AStraightRoadsPathTangentAgreesWithTheAxisTheLineSaysItRunsOn()
        {
            // Same exclusion, same reason, as EveryStraightRoadsPathReproducesItsOldCentre-
            // Exactly above: chicago's Path is a curve now, so its tangent at the midpoint has
            // no reason to be purely axis-aligned, and asserting that it is would be asserting
            // the bend away rather than describing it.
            foreach (var line in RealCity().Roads.Lines)
            {
                if (!line.IsStraight) continue;

                var t = line.Path.TangentAt(line.Path.Length * 0.5f);
                if (line.IsNorthSouth)
                    Assert.That(t.X, Is.EqualTo(0f), line.Name + " is N-S but its tangent has x");
                else
                    Assert.That(t.Y, Is.EqualTo(0f), line.Name + " is E-W but its tangent has y");
            }
        }

        // Recorded from the real-city build at the time this road geometry was rewritten (the
        // Phase A described in the class comment above). If a legitimate change to
        // Content/city.txt ever moves one of these, re-record it deliberately - read the new
        // value off TestContext.Out from a run of the test it belongs to and paste it in here -
        // rather than loosening the assertion to make the mismatch stop mattering.
        //
        // RE-RECORDED for Phase B: Chicago Street / Illinois Route 1 now follows its real
        // surveyed alignment instead of a straight line at x=750 (Content/city.txt, class
        // comment above). Junctions and entries are unchanged, because the curve still crosses
        // the same 16 east-west streets it always did; segments moved 620 -> 614 and turns
        // moved 1692 -> 1656. Rather than assume that drop is benign, three things were checked
        // against the rebuilt graph:
        //   - 0 stranded segments - every segment arriving at a junction still has at least one
        //     legal exit, so no vehicle can vanish. This is the invariant that matters most,
        //     and it holds.
        //   - 32 straight continuations through chicago, exactly 2 per direction across its 16
        //     junctions - the curve is read as the road continuing, not as a turn.
        //   - Left and right turns touching chicago are symmetric, 56 and 56. A
        //     curvature-induced misclassification would bias one direction, because a curve
        //     bends one way; the symmetry is evidence the tangent-based turn classification is
        //     not skewed by it.
        private const int BaselineJunctions = 115;
        private const int BaselineSegments = 456;
        private const int BaselineTurns = 1122;
        private const int BaselineEntries = 39;

        // Same rule as the counts above: re-record deliberately, by reading the new digest off
        // TestContext.Out and pasting it in, never by loosening this to a prefix or a tolerance.
        // Re-recorded alongside the counts above, for the same Phase B change.
        private const string BaselineSegmentChecksum =
            "6CB2CDF906EBBCBA759B5C723EA9663EED6722AB525BF5C3A300A22A7C44A408";
    }
}
