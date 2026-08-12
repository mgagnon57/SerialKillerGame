using System.Collections.Generic;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The downtown terrace. Every expectation here traces to a frontage figure the 1913 Sanborn
    /// surveyor wrote under a building — see docs/research/COMMERCIAL-ROW.md — so a test failing
    /// means either the rule moved or the transcription was wrong, and both are worth stopping for.
    /// </summary>
    [TestFixture]
    public class CommercialRowTests
    {
        private static IRng Rng(ulong seed = 1) => new Xoshiro256ss(seed);

        /// <summary>A row long enough to run brick to frame: west side of Chicago was ~55 m.</summary>
        private const float FullBlock = 80f;

        [Test]
        public void TheCornerUnitIsAnAnchorAndIsTheWidest()
        {
            var row = CommercialRow.Lay(FullBlock, Rng());

            Assert.That(row[0].IsAnchor, Is.True);
            Assert.That(row[0].Offset, Is.EqualTo(0f));
            Assert.That(row[0].Storeys, Is.EqualTo(2));
            Assert.That(row[0].Construction, Is.EqualTo(Construction.Brick));

            // 26 ft, the widest the surveyor recorded, at the crossing.
            Assert.That(row[0].Width, Is.EqualTo(26f * CommercialRow.Foot).Within(0.001f));

            for (int i = 1; i < row.Length; i++)
                Assert.That(row[i].IsAnchor, Is.False, "only the corner unit anchors");
        }

        [Test]
        public void TheTerraceIsContinuous()
        {
            // Party walls, no gaps, no setback variation. Each unit begins exactly where the last
            // one ended - that is what makes it a terrace rather than a street of sheds.
            var row = CommercialRow.Lay(FullBlock, Rng());

            Assert.That(row.Length, Is.GreaterThan(3));
            for (int i = 1; i < row.Length; i++)
                Assert.That(row[i].Offset, Is.EqualTo(row[i - 1].End).Within(0.0001f),
                            $"gap before unit {i}");
        }

        [Test]
        public void TheRowNeverOverrunsItsBlock()
        {
            // A unit hanging off the end of the block would poke through the cross street.
            for (float frontage = 5f; frontage <= 120f; frontage += 3.7f)
            {
                var row = CommercialRow.Lay(frontage, Rng());
                if (row.Length == 0) continue;

                Assert.That(row[row.Length - 1].End, Is.LessThanOrEqualTo(frontage + 0.0001f),
                            $"overran a {frontage}m block");
            }
        }

        [Test]
        public void AFrontageTooNarrowForAShopGetsNoShop()
        {
            // 14 ft is the narrowest unit downtown. Less than that is an alley, not a shopfront.
            float narrowest = 14f * CommercialRow.Foot;

            Assert.That(CommercialRow.Lay(narrowest - 0.01f, Rng()), Is.Empty);
            Assert.That(CommercialRow.Lay(0f, Rng()), Is.Empty);
            Assert.That(CommercialRow.Lay(narrowest + 0.01f, Rng()).Length, Is.EqualTo(1));
        }

        [Test]
        public void HeightDecaysWithDistanceFromTheCrossing()
        {
            // The single most useful thing the survey says. Two storeys near, one storey far.
            var row = CommercialRow.Lay(FullBlock, Rng());

            foreach (var unit in row)
            {
                if (unit.Offset >= CommercialRow.TwoStoreyReach)
                    Assert.That(unit.Storeys, Is.EqualTo(1),
                                $"two storeys at {unit.Offset:0.0}m, past the reach");
            }

            // And the near end really is two-storey, infill dips aside.
            Assert.That(row[0].Storeys, Is.EqualTo(2));
        }

        [Test]
        public void TheBrickStopsAndTheRowFinishesInFrame()
        {
            // West side of Chicago Street: 179 ft of brick, then a frame barber's shop.
            var row = CommercialRow.Lay(FullBlock, Rng());

            Assert.That(row[0].Construction, Is.EqualTo(Construction.Brick));
            Assert.That(row[row.Length - 1].Construction, Is.EqualTo(Construction.Frame),
                        "an 80m block should outrun the brick");

            // Brick never resumes once it has stopped.
            bool frameSeen = false;
            foreach (var unit in row)
            {
                if (unit.Construction == Construction.Frame) frameSeen = true;
                else Assert.That(frameSeen, Is.False, "brick reappeared after frame");
            }
        }

        [Test]
        public void WidthNarrowsWithDistanceAndSnapsToASurveyedFrontage()
        {
            Assert.That(CommercialRow.WidthAt(0f), Is.EqualTo(26f * CommercialRow.Foot).Within(0.001f));
            Assert.That(CommercialRow.WidthAt(999f), Is.EqualTo(14f * CommercialRow.Foot).Within(0.001f));

            // Monotonically non-increasing, and always one of the frontages actually written down.
            float last = float.MaxValue;
            for (float d = 0f; d <= 100f; d += 1f)
            {
                float w = CommercialRow.WidthAt(d);
                Assert.That(w, Is.LessThanOrEqualTo(last + 0.0001f), $"width grew at {d}m");
                last = w;

                bool surveyed = false;
                foreach (float feet in CommercialRow.FrontagesFeet)
                    if (Abs(w - feet * CommercialRow.Foot) < 0.001f) surveyed = true;

                Assert.That(surveyed, Is.True, $"{w / CommercialRow.Foot:0.0} ft was never surveyed");
            }
        }

        [Test]
        public void SnapPicksTheNearestSurveyedFrontage()
        {
            Assert.That(CommercialRow.Snap(25.6f), Is.EqualTo(26f));
            Assert.That(CommercialRow.Snap(24.9f), Is.EqualTo(25f));
            Assert.That(CommercialRow.Snap(20f), Is.EqualTo(18f).Or.EqualTo(22f)); // equidistant
            Assert.That(CommercialRow.Snap(17f), Is.EqualTo(16f).Or.EqualTo(18f)); // equidistant
            Assert.That(CommercialRow.Snap(3f), Is.EqualTo(14f));
            Assert.That(CommercialRow.Snap(500f), Is.EqualTo(26f));
        }

        [Test]
        public void HandleUsesTheAddressWhenThereIsOne()
        {
            Assert.That(CommercialRow.HandleFor("112 S Chicago", 237, 1),
                        Is.EqualTo("112 S Chicago #1"));
            Assert.That(CommercialRow.HandleFor("112 S Chicago", 237, 3),
                        Is.EqualTo("112 S Chicago #3"));
        }

        [Test]
        public void HandleFallsBackToTheParcelIdWithNoAddress()
        {
            Assert.That(CommercialRow.HandleFor(null, 501, 2), Is.EqualTo("parcel 501 unit 2"));
            Assert.That(CommercialRow.HandleFor("", 501, 2), Is.EqualTo("parcel 501 unit 2"));
        }

        [Test]
        public void TheSameSeedLaysTheSameRow()
        {
            var a = CommercialRow.Lay(FullBlock, Rng(12345));
            var b = CommercialRow.Lay(FullBlock, Rng(12345));

            Assert.That(a.Length, Is.EqualTo(b.Length));
            for (int i = 0; i < a.Length; i++)
            {
                Assert.That(a[i].Offset, Is.EqualTo(b[i].Offset), $"unit {i} offset");
                Assert.That(a[i].Width, Is.EqualTo(b[i].Width), $"unit {i} width");
                Assert.That(a[i].Storeys, Is.EqualTo(b[i].Storeys), $"unit {i} storeys");
            }
        }

        [Test]
        public void DifferentSeedsDiffer()
        {
            // Not a strong claim - just that the infill is actually wired to the rng, because a
            // generator that ignores its seed passes every other test in this file.
            var seen = new HashSet<string>();
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var row = CommercialRow.Lay(FullBlock, Rng(seed));
                var key = "";
                foreach (var unit in row) key += $"{unit.Width:0.00}/{unit.Storeys};";
                seen.Add(key);
            }

            Assert.That(seen.Count, Is.GreaterThan(1), "every seed produced an identical row");
        }

        [Test]
        public void InfillIsRareEnoughToStillReadAsARow()
        {
            // The dips are meant to be the exception the real survey shows - three units in
            // forty - not a randomised street. Measured across many rows so one unlucky seed
            // does not decide it.
            int units = 0, dips = 0;

            for (ulong seed = 1; seed <= 200; seed++)
                foreach (var unit in CommercialRow.Lay(FullBlock, Rng(seed)))
                {
                    if (unit.IsAnchor) continue;
                    units++;

                    // A dip is a single-storey unit sitting inside the two-storey zone, where
                    // the decay rule alone would have given it two storeys.
                    if (unit.Offset < CommercialRow.TwoStoreyReach && unit.Storeys == 1) dips++;
                }

            Assert.That(units, Is.GreaterThan(500), "not enough sample to judge a 7.5% rate");

            float rate = (float)dips / units;
            Assert.That(rate, Is.GreaterThan(0f), "the infill never fired");
            Assert.That(rate, Is.LessThan(0.15f), $"infill at {rate:P1} - that is not a terrace");
        }

        [Test]
        public void NothingCutsAHallInHalf()
        {
            // The upper floors at the corner are let as halls spanning two shopfronts. A
            // single-storey infill dropped under one would slice it, and no dip appears in the
            // first three units on any of the four surveyed faces. Checked hard across many
            // seeds, because this is exactly the kind of rule that holds until it doesn't.
            for (ulong seed = 1; seed <= 300; seed++)
                foreach (var unit in CommercialRow.Lay(FullBlock, Rng(seed)))
                {
                    if (unit.Offset >= CommercialRow.HallSpan) continue;

                    Assert.That(unit.Storeys, Is.EqualTo(2),
                                $"seed {seed}: single storey at {unit.Offset:0.0}m, under the halls");
                }
        }

        [Test]
        public void AnInfillIsCheaperThanItsNeighboursNotDerelict()
        {
            // The three real dips are 18ft, 18ft and 15ft - never the 14ft minimum. A dip at the
            // floor of the range reads as a gap in the street rather than as a replacement.
            float floor = 14f * CommercialRow.Foot;
            bool sawADip = false;

            for (ulong seed = 1; seed <= 300; seed++)
                foreach (var unit in CommercialRow.Lay(FullBlock, Rng(seed)))
                {
                    if (unit.Offset >= CommercialRow.TwoStoreyReach || unit.Storeys != 1) continue;

                    sawADip = true;
                    Assert.That(unit.Width, Is.GreaterThan(floor + 0.001f),
                                $"seed {seed}: dip at the 14ft floor");
                    Assert.That(unit.Width, Is.EqualTo(CommercialRow.InfillFeet * CommercialRow.Foot)
                                                .Within(0.001f));
                }

            Assert.That(sawADip, Is.True, "no dip in 300 seeds - the test proved nothing");
        }

        [Test]
        public void TheRealChicagoStreetBlockComesOutTheRightShape()
        {
            // The west side of Chicago Street, as surveyed: ten units, four of them two-storey,
            // 179 ft (54.6 m) of brick and then frame. This is the closest thing to a ground
            // truth the whole generator has.
            var row = CommercialRow.Lay(54.6f + 5f, Rng(7));

            int twoStorey = 0, brick = 0;
            foreach (var unit in row)
            {
                if (unit.Storeys == 2) twoStorey++;
                if (unit.Construction == Construction.Brick) brick++;
            }

            // Nine to eleven units against the surveyed ten. The generator is fitted to the rule,
            // not to this block, so an exact match would be suspicious rather than reassuring.
            Assert.That(row.Length, Is.InRange(9, 11), "unit count is off the surveyed ten");
            Assert.That(twoStorey, Is.InRange(3, 6), "surveyed four two-storey units");
            Assert.That(brick, Is.GreaterThanOrEqualTo(row.Length - 2), "surveyed one frame unit");
        }

        private static float Abs(float v) => v < 0f ? -v : v;
    }
}
