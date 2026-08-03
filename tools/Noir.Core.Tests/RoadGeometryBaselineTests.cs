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
    /// Phase B then put a real curve into one of those 27: Chicago Street / Illinois Route 1
    /// now follows its surveyed alignment instead of a straight line at x=750 (see
    /// Content/city.txt). That is a deliberate content change, not a regression, so the
    /// baseline below was re-recorded against it rather than defended against it. The other 26
    /// roads are untouched and still straight - see
    /// <see cref="ChicagoAloneBendsEveryOtherRealRoadIsStraightAndAxisAligned"/>.
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
            // ways - chicago bends and nothing else does - so it fails just as loudly whether a
            // second road loses its straightness or chicago gets straightened back out.
            foreach (var line in RealCity().Roads.Lines)
            {
                bool shouldBeStraight = line.Name != "chicago";
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
        private const int BaselineJunctions = 142;
        private const int BaselineSegments = 614;
        private const int BaselineTurns = 1656;
        private const int BaselineEntries = 54;

        // Same rule as the counts above: re-record deliberately, by reading the new digest off
        // TestContext.Out and pasting it in, never by loosening this to a prefix or a tolerance.
        // Re-recorded alongside the counts above, for the same Phase B change.
        private const string BaselineSegmentChecksum =
            "9BFF36F308B76B0DDB3FAF1B54D0E352064C776D03E1FC6E80DD0051E34964F1";
    }
}
