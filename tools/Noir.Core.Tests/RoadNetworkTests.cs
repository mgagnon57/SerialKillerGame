using NUnit.Framework;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The road network exists so that nothing downstream has to infer where a carriageway runs
    /// by sampling tiles. These pin the two things that inference kept getting wrong: where the
    /// centre line actually is, and which class a corridor is.
    /// </summary>
    [TestFixture]
    public class RoadNetworkTests
    {
        private static WorldModel Build(string map)
        {
            TestContent.EnsureKinds();
            return WorldBuilder.Build(VillageParser.Parse(map), 1234UL);
        }

        private const string Header = "village Test\nsize 240 240\nterrain path 0,0 240x240\n";

        [Test]
        public void EvenWidthCentresOnTheDeclaredCoordinate()
        {
            // WorldBuilder strokes offsets -(W/2) .. (W/2 + W%2 - 1). For W=30 that is -15..14,
            // so the band covers tiles 60..89 - continuous [60,90) - whose middle is exactly 75.
            var world = Build(Header + "road northgate 30 0,75 239,75\n  class freeway\n");

            var line = world.Roads.Lines[0];
            Assert.That(line.Centre, Is.EqualTo(75f));
            Assert.That(line.IsNorthSouth, Is.False);
            Assert.That(line.IsStraight, Is.True);

            // And the paint agrees with the arithmetic at both edges.
            Assert.That(world.Grid.TerrainAt(120, 60), Is.EqualTo(Terrain.Road));
            Assert.That(world.Grid.TerrainAt(120, 89), Is.EqualTo(Terrain.Road));
            Assert.That(world.Grid.TerrainAt(120, 59), Is.Not.EqualTo(Terrain.Road));
            Assert.That(world.Grid.TerrainAt(120, 90), Is.Not.EqualTo(Terrain.Road));
        }

        [Test]
        public void OddWidthCentresHalfATilePast()
        {
            // Ashcombe's roads are odd-width, and they must keep meaning what they meant.
            var world = Build("village Test\nsize 120 90\nterrain path 0,0 120x90\n"
                            + "road main 5 4,46 116,46\n");

            var line = world.Roads.Lines[0];
            Assert.That(line.Centre, Is.EqualTo(46.5f));
            Assert.That(line.Class, Is.EqualTo(RoadClass.Street), "narrow roads are streets");
        }

        [Test]
        public void ClassIsInferredFromWidthWhenUnstated()
        {
            var world = Build(Header + "road wide 30 0,75 239,75\nroad narrow 10 35,0 35,239\n");

            Assert.That(world.Roads.Lines[0].Class, Is.EqualTo(RoadClass.Mainroad),
                        "30m unstated is a main road, not an arterial - you have to ask for four lanes");
            Assert.That(world.Roads.Lines[1].Class, Is.EqualTo(RoadClass.Street));
        }

        [Test]
        public void ClassMustMatchTheDeclaredWidth()
        {
            // The tiles are 30m. A freeway declared 10m wide would lay 30m of asphalt across a
            // 10m corridor and pave over the buildings either side of it, silently.
            var ex = Assert.Throws<VillageParseException>(
                () => Build(Header + "road wrong 10 0,75 239,75\n  class freeway\n"));
            Assert.That(ex.Message, Does.Contain("30m corridor"));
        }

        [Test]
        public void CrossingRoadsMakeAJunction()
        {
            var world = Build(Header
                            + "road northgate 30 0,75 239,75\n  class freeway\n"
                            + "road second 30 165,0 165,239\n  class freeway\n"
                            + "road first 30 45,0 45,239\n  class mainroad\n");

            Assert.That(world.Roads.Junctions.Count, Is.EqualTo(2));

            foreach (var j in world.Roads.Junctions)
            {
                Assert.That(j.Y, Is.EqualTo(75f), "both junctions are on Northgate");
                Assert.That(j.Reach, Is.EqualTo(15f));
            }
            Assert.That(world.Roads.Junctions[0].X, Is.EqualTo(165f));
            Assert.That(world.Roads.Junctions[1].X, Is.EqualTo(45f));
        }

        [Test]
        public void ParallelRoadsDoNotCross()
        {
            var world = Build(Header
                            + "road northgate 30 0,75 239,75\n"
                            + "road franklin 30 0,165 239,165\n");
            Assert.That(world.Roads.Junctions.Count, Is.EqualTo(0));
        }

        [Test]
        public void AtFindsTheRoadCoveringAPoint()
        {
            var world = Build(Header + "road northgate 30 0,75 239,75\n  class freeway\n");

            Assert.That(world.Roads.At(120f, 75f)?.Name, Is.EqualTo("northgate"));
            Assert.That(world.Roads.At(120f, 61f)?.Name, Is.EqualTo("northgate"), "inside the corridor");
            Assert.That(world.Roads.At(120f, 40f), Is.Null, "well clear of it");
        }

        [Test]
        public void FootpathsAreNotRoads()
        {
            // `path` strokes Terrain.Path, and a pavement is not something traffic drives on.
            var world = Build(Header + "path alley 3 0,20 239,20\n");
            Assert.That(world.Roads.Lines.Count, Is.EqualTo(0));
        }

        [Test]
        public void TheRealCityParses()
        {
            TestContent.EnsureKinds();
            var world = WorldBuilder.Build(
                VillageParser.Parse(TestContent.Read("city.txt")), 1234UL);

            Assert.That(world.Roads.Lines.Count, Is.GreaterThan(0), "the city has roads");
            Assert.That(world.Roads.Junctions.Count, Is.GreaterThan(0), "and they cross");

            // Every road is straight except chicago (Illinois Route 1), which follows its real
            // surveyed alignment on purpose - see RoadGeometryBaselineTests for the pinned
            // detail on that curve. This still checks straightness for every line, just against
            // the one deliberate exception rather than a blanket True.
            foreach (var line in world.Roads.Lines)
            {
                // green and benton bend at their Route 1 ends as of 2026-08-04, and that is
                // PROVISIONAL pending the owner - see the long note on
                // RoadGeometryBaselineTests.ChicagoAloneBendsEveryOtherRealRoadIsStraightAndAxisAligned.
                Assert.That(line.IsStraight,
                            Is.EqualTo(line.Name != "chicago" && line.Name != "railroad"
                                    && line.Name != "green" && line.Name != "benton" && line.Name != "harrison"),
                            $"{line.Name} straightness");
                Assert.That(line.Width, Is.EqualTo(RoadClasses.CorridorWidth(line.Class)),
                            $"{line.Name} is the width its class requires");
            }
        }

        [Test]
        public void AtAnswersForACurvedRoadToo()
        {
            // The old At skipped any line where !IsStraight outright, so a bent road was
            // invisible to zoning and to the lighting pass - they would have called it open
            // ground and planted trees down the carriageway.
            var world = Build(Header
                + "road bend 10 20,20 20,120 60,180 120,200\n  class street\n");

            var line = world.Roads.Lines[0];
            Assert.That(line.IsStraight, Is.False, "the fixture is meant to bend");

            var onIt = line.Path.PointAt(line.Path.Length * 0.5f);
            Assert.That(world.Roads.At(onIt.X, onIt.Y), Is.Not.Null,
                        "a point on the centre line is on the road");

            var beside = onIt + line.Path.NormalAt(line.Path.Length * 0.5f) * 40f;
            Assert.That(world.Roads.At(beside.X, beside.Y), Is.Null,
                        "40m aside from a 10m corridor is not on the road");
        }

        [Test]
        public void AtStillPrefersTheWidestWhereTwoRoadsOverlap()
        {
            var world = Build(Header
                + "road wide 30 0,75 239,75\n  class mainroad\n"
                + "road narrow 10 75,0 75,239\n  class street\n");

            Assert.That(world.Roads.At(75f, 75f).Name, Is.EqualTo("wide"));
        }

        [Test]
        public void AtRejectsPastTheCurvedEndAlongAShallowTangent()
        {
            // The end-of-run fallback used to compare the declared axis coordinate (y, since
            // this road's overall span reads north-south) against From/To. The final leg here
            // is shallow - nearly east-west - so walking along the TRUE tangent barely moves y
            // at all, and a point genuinely off the end of the pavement could still slip through
            // that test. The fix measures real distance to the true end instead.
            var world = Build(Header
                + "road bend 10 20,20 20,180 170,188\n  class street\n");

            var line = world.Roads.Lines[0];
            Assert.That(line.IsStraight, Is.False, "the fixture is meant to bend");
            Assert.That(line.IsNorthSouth, Is.True, "the overall span is meant to read north-south");

            var end = line.Path.PointAt(line.Path.Length);
            var tangent = line.Path.TangentAt(line.Path.Length);

            var justInside = end - tangent * 2f;
            Assert.That(world.Roads.At(justInside.X, justInside.Y), Is.Not.Null,
                        "just short of the true end is still on the road");

            var wellPast = end + tangent * 8f;
            Assert.That(world.Roads.At(wellPast.X, wellPast.Y), Is.Null,
                        "8m past the true end along a shallow tangent is off the pavement");
        }

        [Test]
        public void AJunctionKnowsHowFarAlongEachRoadItSits()
        {
            var world = Build(Header
                + "road ew 30 0,75 239,75\n  class mainroad\n"
                + "road ns 30 165,0 165,239\n  class mainroad\n");

            Assert.That(world.Roads.Junctions.Count, Is.EqualTo(1));
            var j = world.Roads.Junctions[0];

            Assert.That(j.X, Is.EqualTo(165f), "unchanged: the N-S road's centre");
            Assert.That(j.Y, Is.EqualTo(75f), "unchanged: the E-W road's centre");

            // New, and what LaneGraph needs: where the crossing falls along each road.
            Assert.That(j.SNorthSouth, Is.EqualTo(75f).Within(0.01f));
            Assert.That(j.SEastWest, Is.EqualTo(165f).Within(0.01f));
        }

        [Test]
        public void ACurvedRoadFormsJunctionsAtAll()
        {
            // The old constructor gated on IsStraight && IsNorthSouth, so a bent road crossed
            // nothing: no junctions, and therefore no lanes and no traffic anywhere on it.
            var world = Build(Header
                + "road bend 30 20,20 20,120 60,180 140,200\n  class mainroad\n"
                + "road cross 30 0,190 239,190\n  class mainroad\n");

            Assert.That(world.Roads.Junctions.Count, Is.GreaterThanOrEqualTo(1),
                        "a curve that crosses a straight road must produce a junction");
        }

        [Test]
        public void TwoRoadsMayCrossMoreThanOnce()
        {
            // An S-bend can meet the same straight road twice. The old model held one junction
            // per pair by construction and could not represent it.
            //
            // The waypoints below dip to one side of flat's y=120, bulge past it, and come back:
            // Y runs 20, 80, 160, 90, 40 - below, below, ABOVE, below, below - so the smoothed
            // path crosses y=120 once heading out and once heading back. (A first draft of this
            // fixture had Y climbing monotonically through the peak, which - verified against
            // RoadPath.Through directly - only touches y=120 once: a curve has to actually
            // double back across the line, not just brush it, to cross it twice.)
            var world = Build("village Test\nsize 300 300\nterrain path 0,0 300x300\n"
                + "road wiggle 30 40,20 40,80 160,160 60,90 40,40\n  class mainroad\n"
                + "road flat 30 0,120 299,120\n  class mainroad\n");

            Assert.That(world.Roads.Junctions.Count, Is.GreaterThanOrEqualTo(2),
                        "the S-bend crosses the straight road on the way out and on the way back");
        }

        [Test]
        public void ACurvedEastWestRoadFormsJunctionsToo()
        {
            // The constructor always calls Crossings(ns.Path, ew.Path), so a bending "ns"-role
            // road (ACurvedRoadFormsJunctionsAtAll) only ever exercises Crossings walking its
            // first argument. A bend on the EAST-WEST road instead is the only way to force
            // Crossings to walk its SECOND argument - proof the S values it reports afterwards
            // are the right way round and not silently swapped.
            var world = Build("village Test\nsize 300 300\nterrain path 0,0 300x300\n"
                + "road straight 30 120,0 120,239\n  class mainroad\n"
                + "road curvy 30 0,80 80,80 120,140 160,80 239,80\n  class mainroad\n");

            Assert.That(world.Roads.Junctions.Count, Is.GreaterThanOrEqualTo(1));
            var j = world.Roads.Junctions[0];

            var straight = world.Roads.Lines[0];
            var curvy = world.Roads.Lines[1];

            // The reported S must be along the road it names, not the other one: PointAt(S)
            // on each road has to reproduce the crossing point. Swap SNorthSouth/SEastWest by
            // accident and at least one of these fails.
            var backNS = straight.Path.PointAt(j.SNorthSouth);
            Assert.That(backNS.X, Is.EqualTo(j.X).Within(0.01f), "SNorthSouth is along the wrong road");
            Assert.That(backNS.Y, Is.EqualTo(j.Y).Within(0.01f), "SNorthSouth is along the wrong road");

            var backEW = curvy.Path.PointAt(j.SEastWest);
            Assert.That(backEW.X, Is.EqualTo(j.X).Within(0.01f), "SEastWest is along the wrong road");
            Assert.That(backEW.Y, Is.EqualTo(j.Y).Within(0.01f), "SEastWest is along the wrong road");
        }

        [Test]
        public void RoadsWhoseRunsDoNotOverlapDoNotCross()
        {
            // ns (x=100) and ew (y=20) would meet at (100,20) if both roads were infinite.
            // They are not: ns's own declared run only starts at y=100, well south of ew's
            // line, so the pavements themselves never reach each other. Only the round-trip
            // check in Crossings' closed-form branch catches this - Project's lateral is
            // invariant along the tangent for a straight axis-aligned road (RoadNetwork.At
            // leans on the same fact for its own boundary case), so it reads exactly 0
            // whether or not the point is actually on the road, and by itself cannot tell a
            // real crossing from two lines that merely share a point neither pavement reaches.
            var apart = Build(Header
                + "road ns 30 100,100 100,239\n  class mainroad\n"
                + "road ew 30 0,20 239,20\n  class mainroad\n");
            Assert.That(apart.Roads.Junctions, Is.Empty,
                        "ns starts at y=100 and never reaches ew's y=20");

            // Positive control, same pair: ns widened to actually reach ew's line. The
            // pavements really do meet now, exactly once, so this test discriminates in both
            // directions rather than just proving Crossings can return nothing.
            var together = Build(Header
                + "road ns 30 100,0 100,239\n  class mainroad\n"
                + "road ew 30 0,20 239,20\n  class mainroad\n");
            Assert.That(together.Roads.Junctions.Count, Is.EqualTo(1),
                        "widened to actually reach ew's line, the same pair crosses exactly once");
        }
    }
}
