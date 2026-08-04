using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The year-gated technology layer.
    ///
    /// The thing being defended is that a household does not acquire a computer twice. The curve
    /// is a DISTRIBUTION and it is inverted per key, so there is one crossing year computed from
    /// the waypoints and nothing stored anywhere - which means no amount of asking in a different
    /// order, on a different machine, or after a reload can make a household flicker.
    /// </summary>
    [TestFixture]
    public class TechnologyTests
    {
        private const string Sample = @"
# a comment, and a blank line above
computer      household   1994:20  1998:40  2000:50  2006:65
mobilephone   person      1991:0   1996:3   2000:18  2003:40  2006:60
payphone      town        1970:100 1998:60  2005:10       # falls
codis         town        1997:0   1998:100
cctv          town        1991:0
flat          household   1995:30                          # a single waypoint
";

        [SetUp]
        public void Install() => TechnologyTable.Install(TechnologyTable.Parse(Sample));

        [TearDown]
        public void Reset() => TechnologyTable.Install(null);

        /// <summary>A spread of household keys, the way the village generator would hand them over.</summary>
        private static IEnumerable<ulong> Keys(int count = 10000)
        {
            for (int i = 1; i <= count; i++) yield return Noir.Core.Contracts.Keys.Of("house-" + i);
        }

        [Test]
        public void TheSameQuestionAlwaysGetsTheSameAnswer()
        {
            ulong key = Noir.Core.Contracts.Keys.Of("408 Holmes Ave");

            for (int repeat = 0; repeat < 5; repeat++)
                for (int year = 1991; year <= 2006; year++)
                    Assert.That(TechnologyTable.Has("computer", year, key),
                                Is.EqualTo(TechnologyTable.Has("computer", year, key)),
                                $"computer in {year}");

            // Re-parsing the same text must not move anybody either - the rank comes from the key
            // and the name, never from where the row sat in the file or when it was read.
            int before = TechnologyTable.AdoptsIn("computer", key);
            TechnologyTable.Install(TechnologyTable.Parse(Sample));
            Assert.That(TechnologyTable.AdoptsIn("computer", key), Is.EqualTo(before));
        }

        [Test]
        public void NobodyLosesSomethingWhileItsCurveIsStillRising()
        {
            // Sweep every household across the whole game. On a rising curve, once you have it you
            // keep it - and this is the test that would catch a percentile being compared against
            // the curve year by year instead of the curve being inverted once.
            foreach (ulong key in Keys(2000))
            {
                bool had = false;
                for (int year = 1991; year <= 2006; year++)
                {
                    bool has = TechnologyTable.Has("computer", year, key);
                    if (had) Assert.That(has, Is.True, $"key {key} lost its computer in {year}");
                    had = has;
                }
            }
        }

        [Test]
        public void AFallingCurveTakesItAwayAndTheLowestRanksKeepItLongest()
        {
            // A row that only ever rises would pass every other test in this file. Two things
            // fall, and they fall for different reasons.

            // ONE: payphone, a town row, running 100 -> 60 -> 10 as the boxes came out one at a
            // time. Past-half reads as "there is still one to use", so it goes false around 1999.
            Assert.That(TechnologyTable.Has("payphone", 1991), Is.True);
            Assert.That(TechnologyTable.Has("payphone", 1998), Is.True);
            Assert.That(TechnologyTable.Has("payphone", 2001), Is.False, "the last box has gone");
            Assert.That(TechnologyTable.Has("payphone", 2006), Is.False);

            // TWO: the underlying inversion, read downward. Everybody starts with it and the ones
            // holding on longest are the LOWEST ranks - the same curve the same way up.
            var curve = new Adoption(new[] { 1970, 1998, 2005 }, new[] { 100f, 60f, 10f });
            var lostAt = new List<(float rank, int year)>();
            foreach (ulong key in Keys(500))
            {
                float rank = Era.RankOf(key, "payphone");
                Assert.That(curve.YearWhen(rank), Is.EqualTo(1970), "everybody has one at the start");
                int lost = curve.YearLost(rank);
                if (lost != Era.Never) lostAt.Add((rank, lost));
            }

            Assert.That(lostAt.Count, Is.GreaterThan(300), "most ranks lose it inside the record");
            var kept = lostAt.OrderByDescending(p => p.year).Take(50).Average(p => p.rank);
            var wentFirst = lostAt.OrderBy(p => p.year).Take(50).Average(p => p.rank);
            Assert.That(kept, Is.LessThan(wentFirst), "the last to lose it are the lowest ranks");
        }

        [Test]
        public void DialupIsGainedAndThenLostByTheSameHouseholds()
        {
            // The realistic household-scope fall, and the one that matters: dialup rises to 50% by
            // 2004 and drops to 45% by 2006 as broadband takes it. So a household in that band
            // genuinely HAD the internet and then did not - which is a different fact about them
            // in 2006 than never having had it.
            TechnologyTable.Install(TechnologyTable.Parse(
                "dialup household 1995:1 1998:22 2000:39 2004:50 2006:45"));

            var gainedThenLost = Keys(4000)
                .Select(k => (got: TechnologyTable.AdoptsIn("dialup", k),
                              lost: TechnologyTable.LosesIn("dialup", k)))
                .Where(p => p.got != Era.Never && p.lost != Era.Never)
                .ToList();

            Assert.That(gainedThenLost.Count, Is.GreaterThan(50), "the 45-to-50 band is real");
            foreach (var p in gainedThenLost)
            {
                Assert.That(p.lost, Is.GreaterThan(p.got), "you cannot lose it before you have it");
                Assert.That(p.got, Is.InRange(1995, 2006));
                Assert.That(p.lost, Is.InRange(2005, 2006));
            }

            // And the town-level shape matches the row: about half online at the peak, slightly
            // fewer at the end.
            double peak = Keys(4000).Count(k => TechnologyTable.Has("dialup", 2004, k)) / 4000.0;
            double end = Keys(4000).Count(k => TechnologyTable.Has("dialup", 2006, k)) / 4000.0;
            Assert.That(peak, Is.EqualTo(0.50).Within(0.02));
            Assert.That(end, Is.EqualTo(0.45).Within(0.02));
            Assert.That(end, Is.LessThan(peak), "the tail turns over");
        }

        [Test]
        public void TheCurveIsFlatOutsideItsWaypointsAndLinearInside()
        {
            var curve = new Adoption(new[] { 1994, 1998, 2000, 2006 }, new[] { 20f, 40f, 50f, 65f });

            Assert.That(curve.PercentIn(1970), Is.EqualTo(20f), "flat before the first waypoint");
            Assert.That(curve.PercentIn(1994), Is.EqualTo(20f), "the year of it");
            Assert.That(curve.PercentIn(2006), Is.EqualTo(65f), "the last waypoint");
            Assert.That(curve.PercentIn(2050), Is.EqualTo(65f), "flat after it");

            Assert.That(curve.PercentIn(1996), Is.EqualTo(30f).Within(0.01f), "halfway between 20 and 40");
            Assert.That(curve.PercentIn(1999), Is.EqualTo(45f).Within(0.01f));
            Assert.That(curve.PercentIn(2003), Is.EqualTo(57.5f).Within(0.01f));

            // A single waypoint is a constant, and an empty curve answers zero rather than throwing.
            var one = new Adoption(new[] { 1995 }, new[] { 30f });
            Assert.That(one.PercentIn(1900), Is.EqualTo(30f));
            Assert.That(one.PercentIn(2100), Is.EqualTo(30f));

            var none = new Adoption(null, null);
            Assert.That(none.IsEmpty, Is.True);
            Assert.That(none.PercentIn(1998), Is.EqualTo(0f));
            Assert.That(none.YearWhen(0.5f), Is.EqualTo(Era.Never));
        }

        [Test]
        public void TheAdoptedFractionTracksTheCurveItWasAuthoredFrom()
        {
            // The check that the inversion is actually a distribution and not just a monotone
            // function that happens to look like one. Over ten thousand households the share who
            // have a computer in a given year must be the share the row says.
            foreach (int year in new[] { 1994, 1996, 1998, 2000, 2003, 2006 })
            {
                double have = Keys().Count(k => TechnologyTable.Has("computer", year, k)) / 10000.0;
                float authored = TechnologyTable.Adopted("computer", year) / 100f;
                Assert.That(have, Is.EqualTo(authored).Within(0.02),
                            $"{year}: {have:P1} have one, the row says {authored:P1}");
            }
        }

        [Test]
        public void TownScopeIgnoresTheKeyAndEverybodyElseDoesNot()
        {
            // CODIS going national in 1998 is true for everybody or nobody; there is no adoption
            // curve on it and no household is an early adopter of the FBI's database.
            foreach (ulong key in Keys(200))
            {
                Assert.That(TechnologyTable.Has("codis", 1997, key), Is.False);
                Assert.That(TechnologyTable.Has("codis", 1998, key), Is.True);
                Assert.That(TechnologyTable.Has("cctv", 2006, key), Is.False, "no cameras, ever");
            }

            // A household row must actually divide the town, or the whole mechanism is decoration.
            var answers = Keys(500).Select(k => TechnologyTable.Has("computer", 1998, k)).ToList();
            Assert.That(answers.Any(a => a), Is.True);
            Assert.That(answers.Any(a => !a), Is.True);

            Assert.That(TechnologyTable.TryScope("codis", out var codis), Is.True);
            Assert.That(codis, Is.EqualTo(TechScope.Town));
            Assert.That(TechnologyTable.TryScope("mobilephone", out var mobile), Is.True);
            Assert.That(mobile, Is.EqualTo(TechScope.Person));
        }

        [Test]
        public void BeingEarlyToOneThingDoesNotMakeYouEarlyToAnother()
        {
            // The rank is salted per technology. Without that, the same households would be first
            // to the computer, first to the mobile and first to the dish - a town of one rich
            // street and one poor one, which is not what adoption looks like.
            var pairs = Keys(2000)
                .Select(k => (a: Era.RankOf(k, "computer"), b: Era.RankOf(k, "mobilephone")))
                .ToList();

            double ma = pairs.Average(p => p.a), mb = pairs.Average(p => p.b);
            double cov = pairs.Average(p => (p.a - ma) * (p.b - mb));
            double sa = Math.Sqrt(pairs.Average(p => (p.a - ma) * (p.a - ma)));
            double sb = Math.Sqrt(pairs.Average(p => (p.b - mb) * (p.b - mb)));

            Assert.That(Math.Abs(cov / (sa * sb)), Is.LessThan(0.1), "the two queues are independent");
            Assert.That(ma, Is.EqualTo(0.5).Within(0.03), "and each is evenly spread");
            Assert.That(mb, Is.EqualTo(0.5).Within(0.03));
        }

        [Test]
        public void AnUnknownNameIsFalseAndAnEmptyTableIsTheOpeningWorld()
        {
            Assert.That(TechnologyTable.Has("teleporter", 1998, 12345UL), Is.False);
            Assert.That(TechnologyTable.Adopted("teleporter", 1998), Is.EqualTo(0f));
            Assert.That(TechnologyTable.AdoptsIn("teleporter", 12345UL), Is.EqualTo(Era.Never));
            Assert.That(TechnologyTable.TryScope("teleporter", out _), Is.False);
            Assert.DoesNotThrow(() => TechnologyTable.Has(null, 1998));

            // No table installed at all - which is what a missing content file gives - has to mean
            // the 1991 world rather than a crash. That is the safe direction to fail in.
            TechnologyTable.Install(null);
            Assert.That(TechnologyTable.Current.Count, Is.EqualTo(0));
            foreach (int year in new[] { 1991, 1998, 2006 })
                Assert.That(TechnologyTable.Has("computer", year, 99UL), Is.False);
        }

        [Test]
        public void FieldsStillAgreesWithItselfOnTheSharedInverter()
        {
            // Era.Crossing IS Fields.DayWhen lifted out, so the real regression check for that
            // move is the whole of FieldsTests staying green. This pins the join itself: the two
            // roundings are deliberately different and the reason is written down in both files.
            var days = new[] { 100, 110, 120, 130 };
            var pct = new[] { 0f, 8f, 30f, 63f };

            Assert.That(Era.Crossing(days, pct, 0f), Is.EqualTo(100f), "flat at the bottom");
            Assert.That(Era.Crossing(days, pct, 8f), Is.EqualTo(110f));
            Assert.That(Era.Crossing(days, pct, 19f), Is.EqualTo(115f).Within(0.01f), "halfway");
            Assert.That(Era.Crossing(days, pct, 100f), Is.EqualTo(130f), "flat at the top");

            // A falling curve needs the first-waypoint guard, and that is what makes payphone work:
            // a target below the opening percentage means "had it before the record starts".
            var falling = new[] { 1970, 1998, 2005 };
            var fpct = new[] { 100f, 60f, 10f };
            Assert.That(Era.Crossing(falling, fpct, 5f), Is.EqualTo(1970f));

            // Nearest for a day, ceiling for a year - the same crossing, two roundings.
            var curve = new Adoption(new[] { 2000, 2004 }, new[] { 0f, 100f });
            Assert.That(Era.Crossing(new[] { 2000, 2004 }, new[] { 0f, 100f }, 12.5f),
                        Is.EqualTo(2000.5f).Within(0.01f));
            Assert.That(curve.YearWhen(0.125f), Is.EqualTo(2001), "a year is a bucket, so round up");
        }

        [Test]
        public void NobodyHasAMobilePhoneIn1991AndMostDoBy2006()
        {
            // End to end, and it is the arc WHO-SEES-WHOM.md section 5 is built on.
            var people = Keys(2000).ToList();

            Assert.That(people.Count(k => TechnologyTable.Has("mobilephone", 1991, k)), Is.Zero,
                        "in 1991 you cannot reach a person who is not at home");
            Assert.That(people.Count(k => TechnologyTable.Has("mobilephone", 1996, k)) / 2000.0,
                        Is.LessThan(0.06), "and barely anybody in 1996");

            double in2006 = people.Count(k => TechnologyTable.Has("mobilephone", 2006, k)) / 2000.0;
            Assert.That(in2006, Is.EqualTo(0.60).Within(0.03), "by 2006 most of the town carries one");

            // The adoption year itself is inside the window for those who get one, and Never for
            // the four in ten who never do.
            var adopts = people.Select(k => TechnologyTable.AdoptsIn("mobilephone", k)).ToList();
            Assert.That(adopts.Where(y => y != Era.Never).All(y => y >= 1991 && y <= 2006), Is.True);
            Assert.That(adopts.Count(y => y == Era.Never) / 2000.0, Is.EqualTo(0.40).Within(0.03));
        }

        // ---- the authored file, as opposed to the sample above -----------------------------

        [Test]
        public void TheRealTableParsesAndSaysWhatTheResearchSays()
        {
            var table = TechnologyTable.Parse(File.ReadAllText(
                Path.Combine(RepoRoot(), "Content", "technology.txt")));
            TechnologyTable.Install(table);

            Assert.That(table.Count, Is.GreaterThan(12), "the file has rows in it");

            // Spot-checks straight off docs/research/TECHNOLOGY.md, each one a figure somebody
            // measured rather than a number this file made up.
            Assert.That(TechnologyTable.Adopted("telephone", 1991), Is.EqualTo(94f).Within(0.5f),
                        "rural telephone 94.3% and flat - HIGHER than national");
            Assert.That(TechnologyTable.Adopted("computer", 1998), Is.EqualTo(40f).Within(0.5f),
                        "NTIA rural computer 39.9% in 1998");
            Assert.That(TechnologyTable.Adopted("dialup", 1998), Is.EqualTo(22f).Within(0.5f),
                        "NTIA rural internet 22.2% in 1998");
            Assert.That(TechnologyTable.Adopted("dialup", 2000), Is.EqualTo(39f).Within(0.5f),
                        "and 38.9% by 2000 - a 75% rise in two years");
            Assert.That(TechnologyTable.Adopted("dvd", 2002), Is.EqualTo(30f).Within(0.5f));

            // The three town-scope facts the investigation will lean on.
            Assert.That(TechnologyTable.Has("codis", 1997), Is.False, "no national DNA index yet");
            Assert.That(TechnologyTable.Has("codis", 1998), Is.True, "CODIS goes live");
            for (int year = 1991; year <= 2006; year++)
                Assert.That(TechnologyTable.Has("cctv", year), Is.False,
                            "a village of 1,200 has no camera coverage in this era, ever");
            Assert.That(TechnologyTable.Has("caseys", 2003), Is.False, "built on the corner the 2004 fire cleared");
            Assert.That(TechnologyTable.Has("caseys", 2006), Is.True);

            // eBay against the town's own connectivity - the point THE-TRAJECTORY.md turns on.
            Assert.That(TechnologyTable.Adopted("dialup", 1998), Is.LessThan(25f),
                        "the antique trade was not killed by Rossville getting online");
        }

        [Test]
        public void TheUnverifiedE911DateIsNotInTheFile()
        {
            // The plan said do not ship the placeholder year, so the row is commented out. Absent
            // means every query is false, which is ITSELF the claim that rural addressing never
            // arrived - a known-wrong answer standing in for an unknown-correct one, deliberately,
            // where it is visible. Pinned so nobody quietly invents a date later.
            var table = TechnologyTable.Parse(File.ReadAllText(
                Path.Combine(RepoRoot(), "Content", "technology.txt")));
            TechnologyTable.Install(table);

            Assert.That(table.Names.Contains("e911address"), Is.False,
                        "no date was found for Vermilion County - see the note in technology.txt");
            Assert.That(TechnologyTable.Has("e911address", 1999), Is.False);
        }

        [Test]
        public void NobodyIsOnlineWithoutAMachineToReadItOn()
        {
            // THE DIAGNOSTIC FOUND THIS, NOT THE SUITE. With every row ranked off its own name,
            // dialup and computer were independent draws and about a sixth of the town had the
            // internet and no computer - while every curve passed its own test, because each one
            // was individually perfect. `needs computer` puts dialup in the computer queue, so the
            // containment is exact rather than probable.
            TechnologyTable.Install(TechnologyTable.Parse(File.ReadAllText(
                Path.Combine(RepoRoot(), "Content", "technology.txt"))));

            int online = 0;
            foreach (ulong key in Keys(4000))
                for (int year = 1991; year <= 2006; year++)
                    if (TechnologyTable.Has("dialup", year, key))
                    {
                        online++;
                        Assert.That(TechnologyTable.Has("computer", year, key), Is.True,
                                    $"key {key} is online in {year} with no computer");
                    }
            Assert.That(online, Is.GreaterThan(1000), "and the check actually had something to check");

            // The dependency also has to survive the year they drop dialup: losing the internet
            // does not take the machine away with it.
            ulong sample = Keys(4000).First(k => TechnologyTable.LosesIn("dialup", k) != Era.Never);
            int lost = TechnologyTable.LosesIn("dialup", sample);
            Assert.That(TechnologyTable.Has("dialup", lost, sample), Is.False);
            Assert.That(TechnologyTable.Has("computer", lost, sample), Is.True, "the machine stays");
        }

        [Test]
        public void ADependencyThatCannotHoldIsRefusedAtLoad()
        {
            // A child commoner than its parent would silently stop meaning anything - the queue
            // would still be shared, but the top of it would hold the child and not the parent.
            Assert.Throws<VillageParseException>(() => TechnologyTable.Parse(
                "computer household 1994:20 2006:65\n" +
                "dialup   household 1994:30 2006:70 needs computer"), "child above parent");

            Assert.Throws<VillageParseException>(() => TechnologyTable.Parse(
                "dialup household 1994:10 needs computer"), "needs something that is not there");

            Assert.Throws<VillageParseException>(() => TechnologyTable.Parse(
                "computer household 1994:60\n" +
                "dialup   household 1994:40 needs computer\n" +
                "email    household 1994:20 needs dialup"), "chains are not supported");

            Assert.Throws<VillageParseException>(() => TechnologyTable.Parse(
                "computer household 1994:60\n" +
                "webphone person   1994:40 needs computer"), "different scopes cannot share a queue");

            Assert.Throws<VillageParseException>(
                () => TechnologyTable.Parse("dialup household needs computer"), "no curve at all");

            // The legitimate shape still loads, and the child really does share the parent's queue.
            var table = TechnologyTable.Parse(
                "computer household 1994:20 2006:65\n" +
                "dialup   household 1995:1  2006:45 needs computer");
            TechnologyTable.Install(table);
            table.TryRow("dialup", out var row);
            Assert.That(row.Queue, Is.EqualTo("computer"));
            Assert.That(Era.RankOf(99UL, row.Queue), Is.EqualTo(Era.RankOf(99UL, "computer")));
        }

        [Test]
        public void AMalformedRowStopsTheLoad()
        {
            // Queries are quiet; authoring mistakes are loud. That split is the whole design.
            Assert.Throws<VillageParseException>(() => TechnologyTable.Parse("computer household"));
            Assert.Throws<VillageParseException>(() => TechnologyTable.Parse("computer wombat 1994:20"));
            Assert.Throws<VillageParseException>(() => TechnologyTable.Parse("computer household 1994"));
            Assert.Throws<VillageParseException>(() => TechnologyTable.Parse("computer household 1994:x"));
            Assert.Throws<VillageParseException>(() => TechnologyTable.Parse("computer household 1994:140"));
            Assert.Throws<VillageParseException>(
                () => TechnologyTable.Parse("computer household 1998:40 1994:20"), "years must ascend");
            Assert.Throws<VillageParseException>(
                () => TechnologyTable.Parse("a household 1994:1\na household 1995:2"), "named twice");

            // A fractional TOWN row is legitimate and must not be refused - payphone is one, and
            // it means a share of the town's stock rather than a share of its households.
            Assert.DoesNotThrow(() => TechnologyTable.Parse("payphone town 1970:100 1998:60 2005:10"));

            Assert.DoesNotThrow(() => TechnologyTable.Parse(""), "an empty file is the 1991 world");
            Assert.DoesNotThrow(() => TechnologyTable.Parse("# nothing but a comment"));
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Content")))
                dir = dir.Parent;
            if (dir == null) throw new InvalidOperationException("could not find the repo root");
            return dir.FullName;
        }
    }
}
