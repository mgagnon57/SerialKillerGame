using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// HOW FAR THE DRAWN ROAD LEAVES ITS OWN SURVEY LINE — measured, on every road, every run.
    ///
    /// `CLAUDE.md` records this fault and, in the same breath, why it survived: *"nine of 68 roads
    /// left one of their own ends backwards and Summit Street was drawn 39 m (128 ft) off its own
    /// survey line. **Nothing could see it: it moves no count and fails no test.**"* Moving the
    /// roads onto `SmoothCentripetal` fixed most of it. It did not fix all of it, and there was
    /// still nothing that could see the rest.
    ///
    /// **THIS DOES NOT ASK THE ROADS TO BE STRAIGHTER, AND MUST NOT BE READ AS ASKING.** The owner
    /// ruled on 2026-08-09: *leave it, corners stay rounded — a real street corner has a turning
    /// radius and a car cannot pivot on a point.* A junction drawn as an arc is CORRECT and the
    /// divergence at a corner is the arc, which is the whole point of it. What this measures is
    /// the size of that arc, so a number exists to rule on instead of an impression.
    ///
    /// WHY IT IS WORTH A GATE ANYWAY. The divergence is not only cosmetic — it decides whether two
    /// roads MEET. `NoTwoStreetsTouchWithoutAJunctionBetweenThem` carries two offenders it cannot
    /// close, benton x summit at 8.3 m and dale x grove at 6.1 m, and `docs/ROAD-FIXES.md`
    /// ALLEY-2b explains them as "the generator and the model do not measure the same gap".
    /// Measured, that is exactly right and the numbers are large:
    ///
    ///     benton x summit    declared gap 0.20 m     drawn 8.3 m apart
    ///     dale   x grove     declared gap 0.65 m     drawn 6.1 m apart
    ///
    /// The county says those roads touch. They are drawn apart because summit leaves its line by
    /// **16.7 m** and benton by 8.9 m. **The junction is not missing; the two curves are.** Which
    /// means ALLEY-2b cannot be closed by extending a road, and widening the generator's tolerance
    /// to force it would be inventing tarmac to paper over the drawing.
    /// </summary>
    [TestFixture]
    public class DrawnRoadFollowsItsSurveyLineTests
    {
        [SetUp]
        public void InstallKinds() => TestContent.EnsureKinds();

        /// <summary>
        /// The worst road today, to a tenth of a metre, plus a little room. A RATCHET, not a
        /// target: it may only ever fall, and it falls when somebody changes the smoothing with
        /// the owner's agreement — never by being edged up to admit a new one.
        /// </summary>
        private const float WorstAllowed = 16.8f;   // summit, measured at 16.71 m

        /// <summary>How many roads may leave their line by more than five metres. Measured at five.</summary>
        private const int OverFiveAllowed = 5;

        [Test]
        public void NoRoadIsDrawnFurtherFromItsSurveyLineThanTheWorstOneToday()
        {
            var layout = RealRossville.LayoutWithPlaces();

            var measured = new List<(float Off, string Name, int Points)>();

            foreach (var run in layout.Roads)
            {
                var tiles = run.Points;
                if (tiles == null || tiles.Count < 3) continue;   // a straight line is its own line

                // The same conversion RoadCorridor makes before calling Through - so this measures
                // the curve the game builds and not one assembled a second way.
                var declared = new Vec2[tiles.Count];
                for (int i = 0; i < tiles.Count; i++) declared[i] = new Vec2(tiles[i].X, tiles[i].Y);

                // THE REAL SMOOTHING, not a copy of it. That is the whole reason this test is in
                // Core rather than a python script beside the generator: `RoadPath` is one call
                // away, so this cannot drift from what the game draws.
                var curve = RoadPath.SmoothCentripetal(declared);

                float worst = 0f;
                foreach (var q in curve)
                {
                    float best = float.MaxValue;
                    for (int i = 0; i < declared.Length - 1; i++)
                        best = Math.Min(best, DistanceToSegment(q, declared[i], declared[i + 1]));
                    worst = Math.Max(worst, best);
                }

                measured.Add((worst, run.Name, declared.Length));
            }

            Assert.That(measured, Is.Not.Empty,
                "no road had three or more declared points, so this measured nothing");

            var ordered = measured.OrderByDescending(m => m.Off).ToList();

            TestContext.Out.WriteLine(
                "how far the DRAWN road leaves its own survey line, worst first:");
            foreach (var m in ordered.Take(10))
                TestContext.Out.WriteLine($"  {m.Off,7:0.00} m  {m.Name,-14} ({m.Points} declared points)");

            int overFive = ordered.Count(m => m.Off > 5f);
            TestContext.Out.WriteLine(
                $"  {ordered.Count} roads with 3+ points; {ordered.Count(m => m.Off > 1f)} over a "
              + $"metre, {overFive} over five");

            Assert.That(ordered[0].Off, Is.LessThanOrEqualTo(WorstAllowed),
                $"'{ordered[0].Name}' is drawn {ordered[0].Off:0.0} m from its own survey line, "
              + $"which is further than any road was when this was measured ({WorstAllowed} m).\n\n"
              + "This is a RATCHET and it may only fall. It is NOT a demand that the roads be "
              + "straightened - the owner ruled on 2026-08-09 that corners stay rounded, because a "
              + "real corner has a turning radius. It is here because the divergence decides "
              + "whether two roads MEET: benton and summit are declared 0.20 m apart and drawn 8.3 "
              + "m apart, and that is the whole of ALLEY-2b.");

            Assert.That(overFive, Is.LessThanOrEqualTo(OverFiveAllowed),
                $"{overFive} roads are drawn more than five metres from their survey line, against "
              + $"{OverFiveAllowed} when this was measured. Worst: "
              + string.Join(", ", ordered.Where(m => m.Off > 5f).Select(m => $"{m.Name} {m.Off:0.0} m")));
        }

        private static float DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len = dx * dx + dy * dy;
            float t = len <= 0f ? 0f : ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len;
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            float qx = a.X + t * dx, qy = a.Y + t * dy;
            return (float)Math.Sqrt((p.X - qx) * (p.X - qx) + (p.Y - qy) * (p.Y - qy));
        }
    }
}
