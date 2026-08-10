using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// A CAR KEEPS RIGHT, INCLUDING ROUND A BEND — and nothing in this project could fail on a car
    /// in the oncoming carriageway until this existed.
    ///
    /// `ROAD-FIXES` W7 says so in as many words: *"the missing gate is the deliverable ... nothing
    /// today can fail on a car in the oncoming carriageway. A fix whose gate has never gone red
    /// proves nothing."* So this was written first and watched failing against the old
    /// arithmetic - **5,438 of 13,154 lane positions in the oncoming carriageway, 41% of the
    /// town's lane geometry** - and only then was `Headings.SideOfPath` allowed to make it green.
    ///
    /// THE FAULT (CONS-2) IS A FRAME MISTAKE. `CityTraffic` placed a car with
    /// `Headings.Side(way) * laneOffset` stepped along `path.NormalAt(s)`. `Side` answers in
    /// COORDINATES — +1 means "the greater x or the greater y" — while `NormalAt` is `(-t.Y, t.X)`,
    /// the right-hand side of the PATH's own direction at that point. Multiply them and the
    /// handedness is counted twice wherever the road's local tangent has left the quadrant its
    /// declared heading names. A straight road never leaves it. A bending one does.
    ///
    /// MEASURED, 2026-08-10: 7 of the 68 roads have such segments — alley13 for 34% of its length,
    /// alley24 and watson for 31%. `ROAD-FIXES` CONS-2 says "25 of the 60 bending roads, Route 1
    /// among them"; that predates the centripetal curve and the re-derived roads. It is seven, and
    /// Route 1 is not one of them. **Recorded rather than repeated**, because a plan's number and
    /// a measurement are different things.
    /// </summary>
    [TestFixture]
    public class CarsKeepRightOnABendTests
    {
        [SetUp]
        public void InstallKinds() => TestContent.EnsureKinds();

        /// <summary>
        /// For every road and both directions of travel, the offset side must put the car on the
        /// RIGHT of the way it is going — measured against the direction of travel itself, which
        /// is the only frame the question means anything in.
        /// </summary>
        [Test]
        public void EveryLaneOffsetPutsTheCarOnTheRightOfItsOwnTravel()
        {
            var layout = RealRossville.LayoutWithPlaces();

            var wrong = new List<string>();
            int checkedSteps = 0;

            foreach (var run in layout.Roads)
            {
                var tiles = run.Points;
                if (tiles == null || tiles.Count < 2) continue;

                var v = new Vec2[tiles.Count];
                for (int i = 0; i < tiles.Count; i++) v[i] = new Vec2(tiles[i].X, tiles[i].Y);
                var path = RoadPath.Through(v);

                // Both ways down the road, named by the declared axis the way RoadLine does.
                bool northSouth = Math.Abs(v[v.Length - 1].Y - v[0].Y)
                                >= Math.Abs(v[v.Length - 1].X - v[0].X);
                var ways = northSouth
                    ? new[] { Heading.North, Heading.South }
                    : new[] { Heading.East, Heading.West };

                foreach (var way in ways)
                {
                    var w = Headings.Way(way);

                    for (float s = 2f; s < path.Length - 2f; s += 4f)
                    {
                        var t = path.TangentAt(s);
                        float agree = t.X * w.X + t.Y * w.Y;
                        if (Math.Abs(agree) < 0.2f) continue;   // near a right-angle corner

                        int side = Headings.SideOfPath(path, s, way);
                        var n = path.NormalAt(s);

                        // Where the car ends up, one metre off the line.
                        float ox = n.X * side, oy = n.Y * side;

                        // Right of a travel vector (dx,dy) is (-dy,dx) - the file's own
                        // derivation, and the whole convention this town drives on.
                        float rx = -w.Y, ry = w.X;

                        checkedSteps++;
                        if (ox * rx + oy * ry <= 0f)
                            wrong.Add($"{run.Name} at s={s:0} going {way}");
                    }
                }
            }

            TestContext.Out.WriteLine(
                $"{checkedSteps} lane positions checked, {wrong.Count} on the wrong side");
            foreach (var w in wrong.Take(10)) TestContext.Out.WriteLine("  " + w);

            Assert.That(checkedSteps, Is.GreaterThan(1000),
                "hardly any lane positions were checked, so this proves nothing");

            Assert.That(wrong, Is.Empty,
                $"{wrong.Count} lane positions put the car in the ONCOMING carriageway.\n\n"
              + "This is a frame mistake: a coordinate-frame sign multiplied by a path-frame "
              + "normal. Ask Headings.SideOfPath, which compares the direction of travel with the "
              + "path's own tangent and involves no compass at all.\n\n"
              + string.Join("\n  ", wrong.Take(20)));
        }

        /// <summary>
        /// AND THE OLD ARITHMETIC FAILS IT, which is what makes the test above worth having.
        ///
        /// A gate nobody has watched go red is a guess. This reproduces `Headings.Side(way)` - the
        /// coordinate answer, exactly as `CityTraffic` used it - and requires it to be wrong
        /// somewhere, so that if a future session "simplifies" `SideOfPath` back into `Side` the
        /// suite says which roads that breaks rather than going quietly green.
        /// </summary>
        [Test]
        public void TheCoordinateAnswerIsMeasurablyWrongSomewhere()
        {
            var layout = RealRossville.LayoutWithPlaces();
            var disagree = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var run in layout.Roads)
            {
                var tiles = run.Points;
                if (tiles == null || tiles.Count < 3) continue;

                var v = new Vec2[tiles.Count];
                for (int i = 0; i < tiles.Count; i++) v[i] = new Vec2(tiles[i].X, tiles[i].Y);
                var path = RoadPath.Through(v);

                bool northSouth = Math.Abs(v[v.Length - 1].Y - v[0].Y)
                                >= Math.Abs(v[v.Length - 1].X - v[0].X);
                var ways = northSouth
                    ? new[] { Heading.North, Heading.South }
                    : new[] { Heading.East, Heading.West };

                foreach (var way in ways)
                    for (float s = 2f; s < path.Length - 2f; s += 4f)
                    {
                        var t = path.TangentAt(s);
                        var w = Headings.Way(way);
                        if (Math.Abs(t.X * w.X + t.Y * w.Y) < 0.2f) continue;

                        if (Headings.SideOfPath(path, s, way) != Headings.Side(way))
                            disagree.Add(run.Name);
                    }
            }

            TestContext.Out.WriteLine(
                $"roads where the coordinate answer and the path answer differ: {disagree.Count}");
            foreach (var d in disagree) TestContext.Out.WriteLine("  " + d);

            Assert.That(disagree, Is.Not.Empty,
                "Headings.Side and Headings.SideOfPath agree everywhere, which means either the "
              + "roads have all been straightened or SideOfPath has been reduced to Side. Both are "
              + "changes somebody should have to argue for: the whole reason SideOfPath exists is "
              + "that on a bending road the two are different, and the difference is a car in the "
              + "oncoming carriageway.");
        }
    }
}
