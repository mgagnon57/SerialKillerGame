using System.IO;
using NUnit.Framework;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// Content/roads.txt is written by tools/build-roads.py and is meant to be loadable by the
    /// same parser city.txt goes through. It nearly was not: the first version wrote the
    /// MEASURED right of way as the road's width - 20 m for a street - and VillageParser pins
    /// width to class and would have thrown on the first line. This is the guard for that.
    /// </summary>
    public class RoadsFileParseTests
    {
        private static string RoadsPath()
        {
            var dir = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(dir, "Content", "roads.txt");
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        [Test]
        public void RoadsFileParsesAndEveryWidthMatchesItsClass()
        {
            var path = RoadsPath();
            if (path == null) Assert.Ignore("Content/roads.txt not present");

            var layout = VillageParser.Parse(File.ReadAllText(path));

            Assert.That(layout.Roads.Count, Is.GreaterThan(50), "expected the whole network");
            foreach (var run in layout.Roads)
            {
                Assert.That(run.Points.Count, Is.GreaterThanOrEqualTo(2), run.Name);
                Assert.That(run.Class, Is.Not.Null, $"{run.Name} has no class");
                Assert.That(run.Width,
                    Is.EqualTo(RoadClasses.CorridorWidth(run.Class.Value)),
                    $"{run.Name} is declared {run.Width} m but a "
                    + $"{run.Class.Value} corridor is {RoadClasses.CorridorWidth(run.Class.Value)} m");
            }
        }

        [Test]
        public void TownStillBuildsAndValidatesWithTheSurveyRoads()
        {
            var roadsPath = RoadsPath();
            if (roadsPath == null) Assert.Ignore("Content/roads.txt not present");
            var cityPath = Path.Combine(Path.GetDirectoryName(roadsPath), "city.txt");
            if (!File.Exists(cityPath)) Assert.Ignore("Content/city.txt not present");

            // Exactly what SurveyRoads.Apply does at runtime, minus Unity: parse the map, swap
            // its road block for the surveyed one, then build and validate the town. Parsing
            // proves the file is well formed; this proves the GAME still stands up on it.
            var layout = VillageParser.Parse(File.ReadAllText(cityPath));
            int before = layout.Roads.Count;
            layout.Roads.Clear();
            layout.Roads.AddRange(VillageParser.Parse(File.ReadAllText(roadsPath)).Roads);
            Assert.That(layout.Roads.Count, Is.GreaterThan(before), "expected more roads, not fewer");

            var world = WorldBuilder.Build(layout);
            var report = WorldValidator.Validate(world);

            foreach (var e in report.Errors) TestContext.WriteLine("ERROR   " + e);
            foreach (var w in report.Warnings) TestContext.WriteLine("warning " + w);
            Assert.That(report.Errors, Is.Empty, "the survey roads must not break the town");
        }

        [Test]
        public void EveryRoadDeclaresAnEasementWiderThanItsOwnCorridor()
        {
            var path = RoadsPath();
            if (path == null) Assert.Ignore("Content/roads.txt not present");
            var layout = VillageParser.Parse(File.ReadAllText(path));

            int measured = 0;
            foreach (var run in layout.Roads)
            {
                if (run.Easement <= 0f) continue;      // 0 is "not measured", which is allowed
                measured++;
                Assert.That(run.Easement, Is.GreaterThanOrEqualTo(run.Width),
                    $"{run.Name} paves a {run.Width} m corridor in a {run.Easement} m easement");
            }
            Assert.That(measured, Is.GreaterThan(40), "expected most roads to carry an easement");
        }

        [Test]
        public void AnEasementNarrowerThanTheCorridorIsRejected()
        {
            // The whole point of the field: a road that paves more than it owns must not load.
            const string bad = "road overreach 10 0,0 100,0\n  class street\n  easement 6\n";
            Assert.Throws<VillageParseException>(() => VillageParser.Parse(bad));

            const string ok = "road fits 10 0,0 100,0\n  class street\n  easement 20\n";
            Assert.That(VillageParser.Parse(ok).Roads[0].Easement, Is.EqualTo(20f).Within(0.01f));
        }
    }
}
