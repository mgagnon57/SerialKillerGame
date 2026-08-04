using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The crop year.
    ///
    /// Two things are being checked here and they are different in kind. The first is that the
    /// curves reproduce the half-way dates THE-YEAR.md gives - that is arithmetic, and a mistyped
    /// percentage fails it. The second is that the SHAPE of the year survives: corn before beans,
    /// beans cut before corn, the gold-against-green fortnight in September, the chessboard in
    /// late October. Those are the claims the research actually makes, and they are what the game
    /// is going to be looked at for.
    /// </summary>
    [TestFixture]
    public class FieldsTests
    {
        /// <summary>Day of year for a date in a common year, so expectations read as dates.</summary>
        private static int Doy(int month, int day) =>
            new[] { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334 }[month - 1] + day;

        /// <summary>A spread of fields, by sampling the survey grid over a few miles of country.</summary>
        private static IEnumerable<long> SampleFields()
        {
            for (int i = 0; i < 40; i++)
                for (int j = 0; j < 40; j++)
                    yield return Fields.KeyAt(i * Fields.FortyAcres + 5f, j * Fields.FortyAcres + 5f);
        }

        [Test]
        public void TheMedianFieldWorksToTheDatesTheResearchGives()
        {
            // THE-YEAR.md, "The half-way dates, which are the ones to build to". Rank 0.5 is by
            // construction the field the 50% figure describes, so these are exact targets, not
            // averages - a few days of tolerance for the linear interpolation between tabulated
            // points and nothing more.
            Assert.That(Fields.PlantedOn(Crop.Corn, 0.5f), Is.EqualTo(Doy(5, 5)).Within(3), "corn 50% planted ~5 May");
            Assert.That(Fields.PlantedOn(Crop.Soybean, 0.5f), Is.EqualTo(Doy(5, 25)).Within(3), "beans 50% planted ~25 May");

            Assert.That(Fields.FullHeightOn(Crop.Corn, 0.5f), Is.EqualTo(Doy(7, 22)).Within(3), "corn 50% tasselled ~22 Jul");
            Assert.That(Fields.FullHeightOn(Crop.Soybean, 0.5f), Is.EqualTo(Doy(7, 20)).Within(3), "beans 50% blooming ~20 Jul");

            Assert.That(Fields.TurnsOn(Crop.Corn, 0.5f), Is.EqualTo(Doy(9, 18)).Within(3), "corn 50% mature ~18 Sep");
            Assert.That(Fields.TurnsOn(Crop.Soybean, 0.5f), Is.EqualTo(Doy(9, 5)).Within(3), "beans 50% yellow ~5 Sep");

            Assert.That(Fields.CutOn(Crop.Corn, 0.5f), Is.EqualTo(Doy(10, 17)).Within(3), "corn 50% harvested ~17 Oct");
            Assert.That(Fields.CutOn(Crop.Soybean, 0.5f), Is.EqualTo(Doy(10, 8)).Within(3), "beans 50% harvested ~8 Oct");
        }

        [Test]
        public void CornGoesInBeforeBeansAndThatIsThePeriodFact()
        {
            // In the 1990s corn was drilled about three weeks ahead of soybeans. Modern Illinois
            // has flipped and often plants beans first, so getting this backwards is the single
            // easiest way to plant this town with the wrong decade's calendar. It has to hold at
            // every rank, not just at the median.
            for (float rank = 0.05f; rank < 1f; rank += 0.05f)
                Assert.That(Fields.PlantedOn(Crop.Corn, rank),
                            Is.LessThan(Fields.PlantedOn(Crop.Soybean, rank)),
                            $"corn must be drilled before beans at rank {rank:0.00}");

            int gap = Fields.PlantedOn(Crop.Soybean, 0.5f) - Fields.PlantedOn(Crop.Corn, 0.5f);
            Assert.That(gap, Is.EqualTo(20).Within(4), "about three weeks apart");
        }

        [Test]
        public void BeansAreCutBeforeCornExceptForTheFirstFewFields()
        {
            // The bulk claim - beans 50% off around 8 October, corn not until the 17th - holds
            // from about the first fifth onwards, and that is what makes late October a
            // chessboard of flat bean stubble against standing corn.
            for (float rank = 0.25f; rank < 1f; rank += 0.05f)
                Assert.That(Fields.CutOn(Crop.Soybean, rank),
                            Is.LessThan(Fields.CutOn(Crop.Corn, rank)),
                            $"beans come off first at rank {rank:0.00}");

            Assert.That(Fields.CutOn(Crop.Corn, 0.5f) - Fields.CutOn(Crop.Soybean, 0.5f),
                        Is.EqualTo(9).Within(3), "nine days apart at the median");

            // THE EARLIEST FIELDS GO THE OTHER WAY, AND THAT IS THE DATA, NOT A FAULT. On 20
            // September the 1990s figures have 9% of corn harvested against 6% of beans: a little
            // corn comes off wet and early while the beans are still drying down in the field.
            // The curves cross around a fifth of the acreage. Pinned here so nobody "fixes" the
            // first two weeks of the harvest to match the headline.
            Assert.That(Fields.CutOn(Crop.Soybean, 0.05f),
                        Is.GreaterThan(Fields.CutOn(Crop.Corn, 0.05f)),
                        "the very first combine of the year is in a corn field");
            Assert.That(Fields.CutOn(Crop.Soybean, 0.20f),
                        Is.LessThanOrEqualTo(Fields.CutOn(Crop.Corn, 0.20f) + 1),
                        "and the curves have crossed by a fifth of the acreage");
        }

        [Test]
        public void SeptemberIsGoldBeansAgainstStandingCorn()
        {
            // THE-YEAR.md: "the single most striking thing the Illinois year does, and it happens
            // before a single field has been cut." Two thirds of the beans are yellow by 10
            // September while the corn is still green and only a quarter mature.
            const int tenthOfSeptember = 253;

            var beans = SampleFields().Select(k => Fields.StateOf(Crop.Soybean, Fields.RankOf(k), tenthOfSeptember)).ToList();
            var corn = SampleFields().Select(k => Fields.StateOf(Crop.Corn, Fields.RankOf(k), tenthOfSeptember)).ToList();

            double beansTurned = beans.Count(s => s == FieldState.Turning) / (double)beans.Count;
            double cornTurned = corn.Count(s => s == FieldState.Turning) / (double)corn.Count;

            Assert.That(beansTurned, Is.EqualTo(0.67).Within(0.10), "two thirds of the beans are gold");
            Assert.That(cornTurned, Is.EqualTo(0.28).Within(0.10), "a quarter of the corn is browning");
            Assert.That(beansTurned, Is.GreaterThan(cornTurned + 0.25), "and the contrast is the point");

            // And essentially nothing has been cut yet.
            Assert.That(beans.Count(s => s == FieldState.Stubble) / (double)beans.Count, Is.LessThan(0.05));
            Assert.That(corn.Count(s => s == FieldState.Stubble) / (double)corn.Count, Is.LessThan(0.02));
        }

        [Test]
        public void LateOctoberIsAChessboard()
        {
            // WHO-SEES-WHOM.md section 2, the month of least visibility: "by mid-October the
            // landscape is a checkerboard of bare stubble and standing corn". The test of a
            // checkerboard is that BOTH squares are well represented - a month that is 95% one
            // thing is not patchy, it is uniform.
            const int twentiethOfOctober = 293;

            var states = SampleFields()
                .Select(k => Fields.StateOf(Crop.Corn, Fields.RankOf(k), twentiethOfOctober))
                .ToList();

            double cut = states.Count(s => s == FieldState.Stubble) / (double)states.Count;
            Assert.That(cut, Is.EqualTo(0.56).Within(0.10), "56% of corn harvested by 20 October");
            Assert.That(1 - cut, Is.GreaterThan(0.3), "and a third of it still standing");

            // Start of October: the fields still hide you, because only a fifth is off.
            var earlyOct = SampleFields()
                .Select(k => Fields.StateOf(Crop.Corn, Fields.RankOf(k), 274))
                .Count(s => s == FieldState.Stubble) / (double)1600;
            Assert.That(earlyOct, Is.LessThan(0.30), "at the start of October most of the corn is up");
        }

        [Test]
        public void CornIsABarrierFromJulyUntilItIsCutAndBeansNeverAre()
        {
            // The claim the whole file exists to support: "Corn stands at full height - a ~2.5 m
            // visual barrier - from July until it is cut."
            float rank = 0.5f;

            Assert.That(Fields.HeightOf(Crop.Corn, rank, Doy(6, 15)), Is.LessThan(Fields.EyeLevel),
                        "mid June you can still see over it");
            Assert.That(Fields.HeightOf(Crop.Corn, rank, Doy(7, 5)), Is.GreaterThan(Fields.EyeLevel),
                        "by early July you cannot");
            Assert.That(Fields.HeightOf(Crop.Corn, rank, Doy(9, 15)), Is.EqualTo(Fields.CornHeight),
                        "and it stays at full height right through the turn");
            Assert.That(Fields.HeightOf(Crop.Corn, rank, Doy(11, 1)), Is.LessThan(Fields.EyeLevel),
                        "gone once the combine has been");

            // Beans are waist-to-chest at their tallest. They never hide anybody, in any month.
            for (int d = 1; d <= 365; d++)
                Assert.That(Fields.HeightOf(Crop.Soybean, rank, d), Is.LessThan(Fields.EyeLevel),
                            $"beans must never block a sightline - day {d}");
        }

        [Test]
        public void JuneIsTheWorstMonthToGoUnseenAndOctoberTheBest()
        {
            // WHO-SEES-WHOM.md draws exactly this inverse, and it falls out of the curves rather
            // than being asserted anywhere: in June nothing is tall enough to hide behind; in
            // early October half the country is still a wall.
            double Blocked(int dayOfYear) => SampleFields()
                .Count(k => Fields.At(k % 97 * 500f, k % 89 * 500f, 1991, dayOfYear).BlocksSightline)
                / (double)1600;

            double june = Blocked(Doy(6, 10));
            double october = Blocked(Doy(10, 1));

            Assert.That(june, Is.LessThan(0.05), "in June the map is open");
            Assert.That(october, Is.GreaterThan(0.25), "in October a quarter of it is still opaque");
            Assert.That(october, Is.GreaterThan(june + 0.25));
        }

        [Test]
        public void WinterIsBareFromDecemberToApril()
        {
            // "A game set in winter is a game set on bare ground." Nothing is growing and nothing
            // hides anyone between December and the spring tillage.
            foreach (Crop crop in new[] { Crop.Corn, Crop.Soybean })
                foreach (int day in new[] { Doy(12, 15), Doy(1, 15), Doy(2, 15), Doy(3, 15) })
                {
                    var state = Fields.StateOf(crop, 0.5f, day);
                    Assert.That(state, Is.EqualTo(FieldState.Stubble), $"{crop} on day {day}");
                    Assert.That(Fields.HeightOf(crop, 0.5f, day), Is.LessThan(Fields.EyeLevel));
                }
        }

        [Test]
        public void TheYearRunsForwardAndNeverBackwards()
        {
            // The states are ordered, and a field walks through them once. If any pair of dates
            // ever crossed - a curve inverted, a floor mis-set - a field would flicker between
            // states on consecutive days and nothing else here would catch it.
            foreach (Crop crop in new[] { Crop.Corn, Crop.Soybean })
                for (float rank = 0.01f; rank < 1f; rank += 0.01f)
                {
                    Assert.That(Fields.PlantedOn(crop, rank), Is.LessThan(Fields.FullHeightOn(crop, rank)));
                    Assert.That(Fields.FullHeightOn(crop, rank), Is.LessThan(Fields.TurnsOn(crop, rank)));
                    Assert.That(Fields.TurnsOn(crop, rank), Is.LessThan(Fields.CutOn(crop, rank)));
                    Assert.That(Fields.CutOn(crop, rank), Is.LessThanOrEqualTo(365));

                    var seen = new List<FieldState>();
                    for (int d = 1; d <= 365; d++)
                    {
                        var s = Fields.StateOf(crop, rank, d);
                        if (seen.Count == 0 || seen[seen.Count - 1] != s) seen.Add(s);
                    }

                    // Stubble, Tilled, Seedling, Growing, Standing, Turning, Stubble - each state
                    // entered once, in order, with the year closing back on stubble.
                    Assert.That(seen.Count, Is.EqualTo(7), $"{crop} rank {rank:0.00} saw {string.Join(">", seen)}");
                    Assert.That(seen[0], Is.EqualTo(FieldState.Stubble));
                    Assert.That(seen[6], Is.EqualTo(FieldState.Stubble));
                    for (int i = 1; i <= 5; i++)
                        Assert.That((int)seen[i], Is.EqualTo(i), $"{crop} rank {rank:0.00} state {i}");
                }
        }

        [Test]
        public void HeightOnlyRisesWhileItIsGrowing()
        {
            foreach (Crop crop in new[] { Crop.Corn, Crop.Soybean })
            {
                int planted = Fields.PlantedOn(crop, 0.5f), tall = Fields.FullHeightOn(crop, 0.5f);
                float previous = -1f;
                for (int d = planted; d <= tall; d++)
                {
                    float h = Fields.HeightOf(crop, 0.5f, d);
                    Assert.That(h, Is.GreaterThanOrEqualTo(previous), $"{crop} shrank on day {d}");
                    previous = h;
                }
                Assert.That(previous, Is.EqualTo(crop == Crop.Corn ? Fields.CornHeight : Fields.SoybeanHeight));
            }
        }

        [Test]
        public void AFieldIsFortyAcresOfTheSurveyGrid()
        {
            // Points inside one quarter-quarter section are the same field; the next one along is
            // not. Field edges land on the PLSS grid, which is why section roads and hedgerows in
            // this county all line up with each other.
            long a = Fields.KeyAt(10f, 10f);
            Assert.That(Fields.KeyAt(400f, 400f), Is.EqualTo(a), "still inside the same forty");
            Assert.That(Fields.KeyAt(410f, 10f), Is.Not.EqualTo(a), "over the line to the east");
            Assert.That(Fields.KeyAt(10f, 410f), Is.Not.EqualTo(a), "and to the north");

            // Negative coordinates are west and south of the origin, not a fold back onto it.
            Assert.That(Fields.KeyAt(-10f, 10f), Is.Not.EqualTo(a));
            Assert.That(Fields.KeyAt(-10f, -10f), Is.Not.EqualTo(Fields.KeyAt(-10f, 10f)));
            Assert.That(Fields.KeyAt(-410f, -10f), Is.Not.EqualTo(Fields.KeyAt(-10f, -10f)));

            Assert.That(Fields.FortyAcres, Is.EqualTo(402.336f).Within(0.01f), "a quarter of a quarter mile-square");
        }

        [Test]
        public void CropsRotateAndNeighboursMostlyDisagree()
        {
            long key = Fields.KeyAt(1000f, 1000f);
            for (int year = 1991; year < 2006; year++)
                Assert.That(Fields.CropOn(key, year), Is.Not.EqualTo(Fields.CropOn(key, year + 1)),
                            $"the same ground must change crop between {year} and {year + 1}");

            // Adjacent fields disagree about half the time - a scatter, not stripes and not a
            // strict chessboard. Both failure modes are visible from a mile away in the game.
            int differ = 0, pairs = 0;
            for (int i = 0; i < 40; i++)
                for (int j = 0; j < 39; j++)
                {
                    var here = Fields.CropOn(Fields.KeyAt(i * 402.4f + 5f, j * 402.4f + 5f), 1991);
                    var next = Fields.CropOn(Fields.KeyAt(i * 402.4f + 5f, (j + 1) * 402.4f + 5f), 1991);
                    if (here != next) differ++;
                    pairs++;
                }
            Assert.That(differ / (double)pairs, Is.EqualTo(0.5).Within(0.12), "neither striped nor interleaved");
        }

        [Test]
        public void TheWorkingOrderIsSpreadAndDoesNotReshuffleEveryYear()
        {
            var ranks = SampleFields().Select(Fields.RankOf).ToList();
            Assert.That(ranks.Min(), Is.LessThan(0.05f), "somebody is always first");
            Assert.That(ranks.Max(), Is.GreaterThan(0.95f), "and somebody is always last");
            Assert.That(ranks.Average(), Is.EqualTo(0.5).Within(0.05), "evenly spread across the county");

            foreach (var quintile in Enumerable.Range(0, 5))
            {
                double share = ranks.Count(r => r >= quintile * 0.2f && r < (quintile + 1) * 0.2f) / (double)ranks.Count;
                Assert.That(share, Is.EqualTo(0.2).Within(0.05), $"quintile {quintile}");
            }

            // Rank takes no year argument at all - the early farmer is early every spring. This
            // asserts the design rather than the arithmetic, so it cannot drift silently.
            long key = Fields.KeyAt(2000f, 3000f);
            Assert.That(Fields.RankOf(key), Is.EqualTo(Fields.RankOf(key)));
        }

        [Test]
        public void TheClockDrivesItStraightThroughWithNoConversion()
        {
            // The join to the rest of the simulation. Fields takes a year and a day-of-year, and
            // GameClock has both - the point is that nothing in between has to do arithmetic.
            var augustFirst = new GameClock(GameClock.TickOn(1995, 8, 1, 12 * 60));
            var condition = Fields.At(1500f, 2200f, augustFirst.Year, augustFirst.DayOfYear);

            Assert.That(augustFirst.DayOfYear, Is.EqualTo(213));
            Assert.That(condition.State, Is.EqualTo(FieldState.Standing), "everything is up in August");
            if (condition.Crop == Crop.Corn)
                Assert.That(condition.BlocksSightline, Is.True, "and if it is corn you cannot see past it");

            // The leap day is a real day in the fields too, and February is bare.
            var leapDay = new GameClock(GameClock.TickOn(2004, 2, 29, 12 * 60));
            Assert.That(Fields.At(1500f, 2200f, leapDay.Year, leapDay.DayOfYear).State,
                        Is.EqualTo(FieldState.Stubble));
        }
    }
}
