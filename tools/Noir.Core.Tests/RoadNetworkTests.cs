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

            foreach (var line in world.Roads.Lines)
            {
                Assert.That(line.IsStraight, Is.True, $"{line.Name} is straight");
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
    }
}
