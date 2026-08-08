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
    /// second street down its middle. RoadClass.Alley took a SIX-metre corridor, which moved
    /// every alley lane and is why the counts held but the checksum moved.
    ///
    /// SIX WAS STILL TOO WIDE and it is FOUR now, which moved the checksum again for the same
    /// reason and again without touching a count. Six metres is twenty feet - a residential
    /// street. A platted alley here is a sixteen-foot right of way with about ten feet of gravel
    /// run down it, and the rendered width was worse than the corridor anyway: the dirt tile
    /// measures 7.1 m across whatever it is asked to be, so CityStreets.Narrow now squeezes it
    /// to half that for alleys alone.
    ///
    /// AND THEN FIVE OF THEM MOVED, which is this checksum. alley2, 3, 4, 5 and 8 - the
    /// north-south ones - were laid ACROSS HOUSES: 162 samples of alley surface sitting on a
    /// building between them, alley8 the worst at 60. Shifts of one to eight metres clear every
    /// one, and the count is 0 now. Measured against BUILDING FOOTPRINTS and not the county
    /// parcels, because the parcels include the right of way and tile straight through the
    /// streets - they call every road on this map an intruder, so they cannot judge one.
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
    /// These figures were READ OFF THE BUILD, not off any document. That distinction is why they
    /// are still right: the documents that quoted road and junction counts were all quoting a
    /// 960x960 map that has been 2100x2400 for months, and every one of them has since been
    /// archived to docs/history/ for exactly that reason.
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

            Assert.That(world.Roads.Lines.Count, Is.EqualTo(37), "roads in city.txt");
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
            // GREEN AND BENTON, added 2026-08-04, and this one is PROVISIONAL - it is a claim
            // about the town's shape, and the owner is the authority on shape, not the parcels.
            //
            // What the data says: walking east from Route 1, the 66 ft parcel gap that is these
            // streets' right of way sits at +109, +89, +68, +47, +27, +3 ft and then on the road.
            // Benton does the same from +139 ft. Tracked every 33 ft, on two independent streets,
            // converging smoothly - that is a plat feature, not noise. Drawn straight, their last
            // 260 ft cut across lots 2, 3, 4 and 338, which are ordinary third-of-an-acre house
            // lots. The owner reported exactly that, twice, before it was measured.
            //
            // A grid meeting a diagonal highway skewing near the join is ordinary. But if the
            // owner says these two run straight into Route 1, HE IS RIGHT AND THIS COMES OUT -
            // the curve is one command to revert, and the parcels on that block would then be the
            // thing at fault.
            //
            // HARRISON is a different shape of thing and is NOT provisional: the owner called it
            // before it was measured - "it angles at the Harrison/Benton junction and it is
            // assuming straight at that jct and it is not". Measured, its right of way runs 0.2
            // degrees off north-south above Benton and 15.7 degrees below it, turning 15.4 degrees
            // at the junction and walking 204 ft east by the south end. That is a CORNER, not a
            // curve, so it is fitted as two straight legs meeting at the junction rather than
            // smoothed - smoothing would have rounded off a real street corner. 74% of it sat on
            // private lots before; 0% now.
            //
            // Three roads called by the owner and confirmed by the parcels, against an invariant
            // that said only two bend. The invariant was a Phase A regression guard, not a survey
            // fact, and Rossville's grid distorts more than it assumed.
            var bends = new[] { "chicago", "railroad", "green", "benton", "harrison" };
            foreach (var line in RealCity().Roads.Lines)
            {
                bool shouldBeStraight = !bends.Contains(line.Name);
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
        // RE-RECORDED 2026-08-04 for the road refit. Every road was moved onto the parcel-free
        // strip that is its own right of way, so a wholesale change to this graph is the POINT of
        // the change rather than a symptom of one. What moved, and why the new numbers are the
        // right ones:
        //
        //   junctions 115 -> 125. A road shifted sideways stops reaching the cross street it used
        //     to end on, and the count first fell to 89 - twenty-six T junctions silently broken,
        //     which would have read on screen as streets not meeting. Forty-three road ends were
        //     then extended to their cross street and 16 ft past it, because an end that stops ON
        //     a centreline only touches it and the intersection finder wants a real crossing.
        //     125 is above the old 115 because the refit also brought roads into contact that
        //     never met before - Ann is now a lane that reaches Harrison, for one.
        //   segments 456 -> 492 and turns 1122 -> 1216 follow from the extra junctions.
        //   entries 39 -> 38: one road end no longer dangles in open ground.
        //
        // The invariant that actually matters was re-checked, not assumed: alleys over a building
        // is still ZERO, which is the owner's standing fact and has no exemption list. Four alleys
        // that could not be laid on their own right of way without crossing a house were put back
        // where they were instead - see RoadsSitOnPublicLandTests.
        // Re-recorded again the same day, after the owner decoupled house placement from the road
        // fit: "allow the houses/buildings to be separate from the road, parcel, train tracks".
        // Freed of having to dodge buildings that are themselves still at pre-refit positions,
        // alleys 2, 3 and 4 moved onto their own right of way and the off-right-of-way count fell
        // from 8 to 5. Junctions 125 -> 126, turns 1216 -> 1212, entries 38 -> 37.
        // Re-recorded again after the four placeholder country roads were deleted from
        // Content/city.txt at the owner's instruction: "remove the unknown cross roads. no idea
        // what they are." They were section0, section1, crossroad0 and crossroad1 - two dead
        // straight lines the full width of the map at y=220 and y=2180, two the full height at
        // x=220 and x=1880. Nothing was ever identified against them: PlanLabels already refused
        // to name them and drew "UNKNOWN - section0" instead, and RoadsSitOnPublicLandTests
        // already excluded tracks from the right-of-way check because they run through farmland
        // the county parcels tile. A road nobody can identify, drawn across the whole plan, costs
        // more than it tells - and this is a survey drawing of a real town.
        //
        // Everything here falls by exactly what four map-length roads were carrying:
        //   roads     41 -> 37
        //   junctions 123 -> 111: twelve crossings gone, theirs with each other and the streets.
        //   segments  494 -> 442
        //   turns    1218 -> 1088
        //   entries    44 -> 38
        private const int BaselineJunctions = 111;
        private const int BaselineSegments = 442;
        private const int BaselineTurns = 1088;
        private const int BaselineEntries = 38;

        // Same rule as the counts above: re-record deliberately, by reading the new digest off
        // TestContext.Out and pasting it in, never by loosening this to a prefix or a tolerance.
        // Re-recorded for the road refit described above, and again for the removal of the four
        // placeholder country roads.
        private const string BaselineSegmentChecksum =
            "8FCD6CD7D6878747EE26A2B262F7097FF752C4AE7809A66584C939D6B69F35EC";
    }
}
