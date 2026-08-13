using System.Linq;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// HOW BUSY A ROAD IS, ASKED OF THE COUNTY RATHER THAN OF A FOUR-STEP LADDER.
    ///
    /// `AmbientTrafficWeight(RoadClass)` is `mainroad 8 : street 2` — 4:1. IDOT counts Rossville
    /// at **21:1**: Chicago Street (Route 1) 5,200 vehicles a day against a side street's ~250.
    /// It also calls Route 1 and Attica the same thing, when the first carries 4.7 times the
    /// second. See docs/research/TRAFFIC-COUNTS.md.
    /// </summary>
    [TestFixture]
    public class TrafficWeightTests
    {
        [SetUp]
        public void InstallKinds() => TestContent.EnsureKinds();

        private static RoadLine Road(string name, RoadClass carries, int aadt) =>
            new RoadLine(name, RoadClass.Street, 10,
                         new[] { new Tile(0, 0), new Tile(100, 0) }, carries, 20f, aadt);

        [Test]
        public void ACountedRoadIsWeighedByItsCountAndNotByItsClass()
        {
            // Both are `carries mainroad`. The class ladder cannot tell them apart.
            var route1 = Road("chicago", RoadClass.Mainroad, 5200);
            var attica = Road("attica", RoadClass.Mainroad, 1100);

            Assert.That(RoadClasses.AmbientTrafficWeight(RoadClass.Mainroad),
                        Is.EqualTo(RoadClasses.AmbientTrafficWeight(RoadClass.Mainroad)),
                        "precondition: the class ladder gives them the same answer");

            int a = RoadClasses.AmbientTrafficWeight(route1);
            int b = RoadClasses.AmbientTrafficWeight(attica);

            Assert.That(a, Is.GreaterThan(b),
                        "Route 1 carries 4.7x what Attica does and must weigh more");

            float ratio = a / (float)b;
            Assert.That(ratio, Is.EqualTo(5200f / 1100f).Within(0.6f),
                        $"the weights should hold the counted ratio; got {ratio:0.0}:1");
        }

        [Test]
        public void TheArterialToSideStreetRatioIsTheOneTheCountyMeasured()
        {
            int route1 = RoadClasses.AmbientTrafficWeight(Road("chicago", RoadClass.Mainroad, 5200));
            int church = RoadClasses.AmbientTrafficWeight(Road("church", RoadClass.Street, 200));

            float ratio = route1 / (float)church;

            TestContext.WriteLine($"  Route 1 {route1} : side street {church}  = {ratio:0.0} : 1");
            TestContext.WriteLine($"  IDOT measures 5200 : 200 = 26 : 1 (median side street 21 : 1)");
            TestContext.WriteLine($"  the class ladder managed "
                + $"{RoadClasses.AmbientTrafficWeight(RoadClass.Mainroad)} : "
                + $"{RoadClasses.AmbientTrafficWeight(RoadClass.Street)}");

            Assert.That(ratio, Is.GreaterThan(10f),
                        "the whole point is that 4:1 was wrong by a factor of five");
        }

        [Test]
        public void AnUncountedRoadKeepsItsClassWeight()
        {
            // IDOT counts none of maple, grove, holmes, harrison, park, perry... Not counting a
            // road is weak evidence it is quiet; it is not a measurement, and inventing a number
            // here would be the same mistake in the other direction.
            var maple = Road("maple", RoadClass.Street, 0);

            Assert.That(RoadClasses.AmbientTrafficWeight(maple),
                        Is.EqualTo(RoadClasses.AmbientTrafficWeight(RoadClass.Street)));
        }

        [Test]
        public void AnAlleyStillCarriesNoAmbientTrafficWhateverItIsCounted()
        {
            var alley = Road("alley2", RoadClass.Alley, 900);

            Assert.That(RoadClasses.AmbientTrafficWeight(alley), Is.EqualTo(0),
                        "zero is about ambient traffic having no reason to be there, not access");
        }

        [Test]
        public void RossvillesOwnSurveyFileCarriesTheCounts()
        {
            var layout = VillageParser.Parse(TestContent.Read("roads.txt"));
            var counted = layout.Roads.Where(r => r.Aadt > 0).ToList();

            Assert.That(counted.Count, Is.GreaterThan(0),
                        "Content/roads.txt has no aadt lines - the counts never landed");

            var chicago = layout.Roads.FirstOrDefault(r => r.Name == "chicago");
            var attica = layout.Roads.FirstOrDefault(r => r.Name == "attica");

            Assert.That(chicago, Is.Not.Null);
            Assert.That(attica, Is.Not.Null);
            Assert.That(chicago.Aadt, Is.EqualTo(5200), "Route 1, IDOT 2019");
            Assert.That(attica.Aadt, Is.EqualTo(1100), "Attica St, IDOT 2019");

            TestContext.WriteLine($"  {counted.Count} of {layout.Roads.Count} road runs carry an "
                                + "IDOT count");
        }
    }
}
