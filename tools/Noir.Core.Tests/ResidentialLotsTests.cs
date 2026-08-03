using System.Collections.Generic;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// What stands on a residential lot. The figures come from BUILDING-CENSUS-1913.md (503
    /// buildings classified off the Sanborn sheets) and PARCEL-STATISTICS.md (the assessor's 794
    /// records), so a failure here means the generator drifted off the measurement.
    /// </summary>
    [TestFixture]
    public class ResidentialLotsTests
    {
        private static IRng Rng(ulong seed) => new Xoshiro256ss(seed);

        /// <summary>
        /// A town's worth of lots spread from centre to edge. Era shares are a function of
        /// position now, so any town-wide claim has to be measured over the whole town.
        /// </summary>
        private static List<Homestead> TownWide(int n = 8000)
        {
            var lots = new List<Homestead>(n);
            var rng = Rng(20260803);
            for (int i = 0; i < n; i++) lots.Add(ResidentialLots.Occupy((float)i / (n - 1), rng));
            return lots;
        }

        /// <summary>A town's worth of lots at a given position, for measuring shares.</summary>
        private static List<Homestead> Town(float edgeness, int n = 4000)
        {
            var lots = new List<Homestead>(n);
            var rng = Rng(20260803);
            for (int i = 0; i < n; i++) lots.Add(ResidentialLots.Occupy(edgeness, rng));
            return lots;
        }

        [Test]
        public void SeventeenPercentOfLotsStayEmpty()
        {
            // The assessor's own count: 517 improved of 623 residential parcels. Leaving the
            // other 106 empty is not a shortcut, it is what the record shows.
            var lots = TownWide();
            int built = 0;
            foreach (var lot in lots) if (lot.Built) built++;

            float rate = (float)built / lots.Count;
            Assert.That(rate, Is.EqualTo(ResidentialLots.Occupancy2000).Within(0.03f),
                        $"built {rate:P1}, assessor says {ResidentialLots.Occupancy2000:P1}");
        }

        [Test]
        public void AVacantLotCanStillCarryAShed()
        {
            // Lots 57 and 81 on the surveyed block have an outbuilding and no house. The shed
            // belongs to the lot, not to the house - which is exactly why it is not in the house
            // grammar's Extras.
            bool found = false;
            var rng = Rng(5);
            for (int i = 0; i < 3000 && !found; i++)
            {
                var lot = ResidentialLots.Occupy(0.4f, rng);
                if (!lot.Built && lot.Outbuilding != Outbuilding.None) found = true;
            }

            Assert.That(found, Is.True, "no vacant lot ever got an outbuilding");
        }

        [Test]
        public void OutbuildingsRunAboutFivePerSixLots()
        {
            // 226 outbuildings counted against 268 dwellings.
            var lots = TownWide();
            int withOut = 0, sheds = 0, barns = 0;
            foreach (var lot in lots)
            {
                if (lot.Outbuilding == Outbuilding.None) continue;
                withOut++;
                if (lot.Outbuilding == Outbuilding.Shed) sheds++; else barns++;
            }

            Assert.That((float)withOut / lots.Count,
                        Is.EqualTo(ResidentialLots.OutbuildingRate).Within(0.03f));

            // 67 sheds to 46 barns among the 113 measured.
            Assert.That((float)sheds / (sheds + barns),
                        Is.EqualTo(ResidentialLots.ShedShare).Within(0.04f));
        }

        [Test]
        public void FootprintsMatchTheMeasuredPercentiles()
        {
            var sizes = new List<float>();
            foreach (var lot in TownWide()) if (lot.Built) sizes.Add(lot.Footprint);
            sizes.Sort();

            Assert.That(sizes.Count, Is.GreaterThan(4000));

            // The measured median is 97 m2; p25 is 75 and p75 is 125.
            Assert.That(At(sizes, 0.50f), Is.EqualTo(97f).Within(6f), "median footprint");
            Assert.That(At(sizes, 0.25f), Is.EqualTo(75f).Within(6f), "p25 footprint");
            Assert.That(At(sizes, 0.75f), Is.EqualTo(125f).Within(8f), "p75 footprint");
            Assert.That(At(sizes, 0.10f), Is.EqualTo(54f).Within(6f), "p10 footprint");
            Assert.That(At(sizes, 0.90f), Is.EqualTo(163f).Within(9f), "p90 footprint");
        }

        [Test]
        public void TheSpreadIsThreeToOneAndSurvivesGeneration()
        {
            // The p90 house is three times the p10 house. A street of identical median houses is
            // the failure this test exists to catch - and it would pass every other test here.
            var sizes = new List<float>();
            foreach (var lot in TownWide()) if (lot.Built) sizes.Add(lot.Footprint);
            sizes.Sort();

            float ratio = At(sizes, 0.90f) / At(sizes, 0.10f);
            Assert.That(ratio, Is.GreaterThan(2.5f), $"only {ratio:0.0}:1 spread - too uniform");
        }

        [Test]
        public void FootprintAtSpansTheMeasuredRangeAndNeverLeavesIt()
        {
            Assert.That(ResidentialLots.FootprintAt(0f), Is.EqualTo(45f));
            Assert.That(ResidentialLots.FootprintAt(1f), Is.EqualTo(200f));
            Assert.That(ResidentialLots.FootprintAt(0.5f), Is.EqualTo(97f).Within(0.01f));

            float last = 0f;
            for (float u = 0f; u <= 1f; u += 0.005f)
            {
                float a = ResidentialLots.FootprintAt(u);
                Assert.That(a, Is.InRange(45f, 200f), $"u={u} left the measured range");
                Assert.That(a, Is.GreaterThanOrEqualTo(last - 0.001f), $"u={u} went backwards");
                last = a;
            }
        }

        [Test]
        public void NothingIsEverThreeStoreys()
        {
            // Every sheet, every year: 1, 1 1/2 and 2. Never more.
            foreach (float edge in new[] { 0f, 0.3f, 0.7f, 1f })
                foreach (var lot in Town(edge, 1500))
                {
                    if (!lot.Built) continue;
                    Assert.That(lot.Storeys, Is.AnyOf(1f, 1.5f, 2f), $"{lot.Era} at {lot.Storeys} storeys");
                }
        }

        [Test]
        public void TheOldCoreIsOldAndTheFringeIsNot()
        {
            // The town grew outward from four blocks at the crossing, so vintage tracks position.
            // The first version of this ignored edgeness for everything but ranches and put as
            // many 1890s farmhouses on the fringe as in the core - which no test caught, because
            // the town-wide shares were all correct. Printing a street out is what caught it.
            int coreEarly = 0, coreBuilt = 0, fringeEarly = 0, fringeBuilt = 0;

            foreach (var lot in Town(0.1f, 3000))
                if (lot.Built) { coreBuilt++; if (IsEarly(lot)) coreEarly++; }

            foreach (var lot in Town(0.9f, 3000))
                if (lot.Built) { fringeBuilt++; if (IsEarly(lot)) fringeEarly++; }

            float core = (float)coreEarly / coreBuilt;
            float fringe = (float)fringeEarly / fringeBuilt;

            Assert.That(core, Is.GreaterThan(0.9f), $"the old core is only {core:P0} old");
            Assert.That(fringe, Is.LessThan(0.1f), $"the fringe is {fringe:P0} pre-1913");
        }

        [Test]
        public void TheRingsInterleaveRatherThanBandingCleanly()
        {
            // A hard boundary between vintages is as wrong as no boundary at all: somebody's
            // house burned in 1961 and was replaced two doors from the crossing.
            int strays = 0;
            foreach (var lot in Town(0.35f, 3000))
                if (lot.Built && !IsEarly(lot)) strays++;

            Assert.That(strays, Is.GreaterThan(30),
                        "not one later house in an inner street - the rings are too clean");
        }

        private static bool IsEarly(Homestead h) =>
            h.Era == HouseEra.Farmhouse || h.Era == HouseEra.Foursquare;

        [Test]
        public void TheFringeGetsRanchesAndTheCentreGetsTheOldLayers()
        {
            int fringeRanch = 0, coreEarly = 0;
            foreach (var lot in Town(1f, 2000)) if (lot.Era == HouseEra.Ranch) fringeRanch++;
            foreach (var lot in Town(0f, 2000)) if (lot.Built && IsEarly(lot)) coreEarly++;

            Assert.That(fringeRanch, Is.GreaterThan(100), "the town limit built no ranches at all");
            Assert.That(coreEarly, Is.GreaterThan(1000), "the old core is not mostly old");
        }

        [Test]
        public void TheEarlyLayerIsMostlyOneAndAHalfStorey()
        {
            // The survey writes 1 1/2 far more often than 2. Foursquares are the minority.
            int half = 0, two = 0;
            foreach (var lot in Town(0.1f, 6000))
            {
                if (lot.Era == HouseEra.Farmhouse && lot.Built) half++;
                if (lot.Era == HouseEra.Foursquare) two++;
            }

            Assert.That(two, Is.GreaterThan(0), "no foursquares at all");
            Assert.That((float)two / (half + two),
                        Is.EqualTo(ResidentialLots.FoursquareShare).Within(0.05f));
        }

        [Test]
        public void TheInfillLayerIsTheGapBetweenTheTwoSurveys()
        {
            // 1913 fabric, 2000 density. The lots empty in 1913 are where the bungalows went,
            // which is why the parcel data's median build year is 1943.
            var lots = TownWide();
            int early = 0, infill = 0;
            foreach (var lot in lots)
            {
                if (!lot.Built) continue;
                if (IsEarly(lot)) early++; else infill++;
            }

            float earlyOfBuilt = (float)early / (early + infill);

            // 268 houses in 1913 against 517 today: a little over half the town predates the
            // Sanborn survey, and the rest went up between then and 2000.
            Assert.That(earlyOfBuilt, Is.EqualTo(ResidentialLots.EarlyShareOfBuilt).Within(0.03f),
                        $"pre-1913 layer is {earlyOfBuilt:P0} of built lots");
            Assert.That(early + infill, Is.GreaterThan(6000), "not enough built lots to judge");
        }

        [Test]
        public void TheSameSeedBuildsTheSameLot()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var a = ResidentialLots.Occupy(0.5f, Rng(seed));
                var b = ResidentialLots.Occupy(0.5f, Rng(seed));
                Assert.That(a.Built, Is.EqualTo(b.Built));
                Assert.That(a.Footprint, Is.EqualTo(b.Footprint));
                Assert.That(a.Era, Is.EqualTo(b.Era));
                Assert.That(a.Outbuilding, Is.EqualTo(b.Outbuilding));
            }
        }

        [Test]
        public void AVacantLotDrawsAsManyNumbersAsABuiltOne()
        {
            // Content has to be additive. If the vacant branch drew fewer numbers than the built
            // branch, one lot going empty would shift every lot after it in the walk - the same
            // problem Place.Key exists to solve, arriving by a different door.
            var rng = Rng(99);
            var first = new List<string>();
            for (int i = 0; i < 60; i++) first.Add(ResidentialLots.Occupy(0.5f, rng).ToString());

            // Same stream, same sequence, regardless of how many of those lots came out vacant.
            var again = Rng(99);
            for (int i = 0; i < 60; i++)
                Assert.That(ResidentialLots.Occupy(0.5f, again).ToString(), Is.EqualTo(first[i]),
                            $"lot {i} diverged");

            Assert.That(first.FindAll(s => s.StartsWith("vacant")).Count, Is.GreaterThan(0),
                        "no vacant lots in the sample - the test proved nothing");
        }

        private static float At(List<float> sorted, float q)
        {
            int i = (int)(q * (sorted.Count - 1));
            return sorted[i];
        }
    }
}
