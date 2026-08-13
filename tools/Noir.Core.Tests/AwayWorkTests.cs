using System.Linq;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// THE MEN WHO LEAVE ROSSVILLE TO WORK.
    ///
    /// Rossville's authored amenities carry about 168 job slots between them, and the town holds
    /// roughly 450 adults of working age. Before this existed, everybody left over had no
    /// workplace at all - and DayPlanner reads "no workplace" as "free day", so two adults in
    /// three were planned as people running five errands around town at ten on a Tuesday morning.
    ///
    /// That is the opposite of what a Vermilion County farm village looked like in 1991. The men
    /// were gone by six: the canneries at Hoopeston five miles north, Danville twenty south, the
    /// grain elevators, section work on the CSX, over-the-road trucking, and the ground itself.
    /// </summary>
    [TestFixture]
    public class AwayWorkTests
    {
        private static Population Town()
        {
            TestContent.EnsureKinds();
            var world = WorldBuilder.Build(VillageParser.Parse(TestContent.Read("city.txt")), 1979UL);
            var names = NameTable.Parse(TestContent.ReadRaw("names.txt"));
            var particulars = ParticularsTable.Parse(TestContent.ReadRaw("particulars.txt"));
            return PopulationGenerator.Generate(world, names, particulars, 1979UL);
        }

        [Test]
        public void MostWorkingAgeAdultsAreNoLongerIdle()
        {
            var people = Town();
            int year = GameClock.EpochYear;

            var adults = people.Citizens.Where(c => c.StageIn(year) == LifeStage.Adult).ToList();
            int inTown = adults.Count(c => c.Work.IsValid);
            int away = adults.Count(c => c.WorksAwayIn(year));
            int idle = adults.Count - inTown - away;

            TestContent.EnsureKinds();
            TestContext.Out.WriteLine($"working-age adults : {adults.Count}");
            TestContext.Out.WriteLine($"  employed in town : {inTown}");
            TestContext.Out.WriteLine($"  working away     : {away}");
            TestContext.Out.WriteLine($"  neither          : {idle}");

            Assert.That(away, Is.GreaterThan(0),
                        "nobody leaves Rossville to work, which is not a farm town");
            Assert.That(inTown + away, Is.GreaterThan(adults.Count / 2),
                        "more than half the working-age adults should have somewhere to be on a "
                        + "weekday. Before this existed it was about one in three.");
        }

        /// <summary>The period, not a guess: the men commute, the women are likelier to be at
        /// home or to hold one of the village's own part-time jobs.</summary>
        [Test]
        public void TheMenGoAndTheWomenAreLikelierToStay()
        {
            var people = Town();
            int year = GameClock.EpochYear;

            var adults = people.Citizens.Where(
                c => c.StageIn(year) == LifeStage.Adult && !c.Work.IsValid).ToList();

            double men = adults.Count(c => c.Male) == 0 ? 0
                : (double)adults.Count(c => c.Male && c.WorksAwayIn(year)) / adults.Count(c => c.Male);
            double women = adults.Count(c => !c.Male) == 0 ? 0
                : (double)adults.Count(c => !c.Male && c.WorksAwayIn(year)) / adults.Count(c => !c.Male);

            TestContext.Out.WriteLine($"of the adults Rossville cannot employ: "
                                    + $"{men:P0} of men work away, {women:P0} of women");

            Assert.That(men, Is.GreaterThan(women),
                        "in 1991 the men of a farm village commuted and the women were far more "
                        + "likely to be at home");
        }

        [Test]
        public void NobodyWhoHasAJobInTownIsAlsoWorkingAway()
        {
            var people = Town();
            int year = GameClock.EpochYear;

            foreach (var c in people.Citizens)
                if (c.Work.IsValid)
                    Assert.That(c.WorksAwayIn(year), Is.False,
                                $"{c.FullName} both keeps a shop in Rossville and commutes to it");
        }

        [Test]
        public void ChildrenAndTheRetiredDoNotCommute()
        {
            var people = Town();
            int year = GameClock.EpochYear;

            foreach (var c in people.Citizens)
                if (c.StageIn(year) != LifeStage.Adult)
                    Assert.That(c.WorksAwayIn(year), Is.False,
                                "school and retirement are not jobs in Hoopeston");
        }

        /// <summary>
        /// A hand-built fixture that says Occupation.None must MEAN it. This is why the decision
        /// is stored at generation rather than derived from the citizen's key on every call: an
        /// earlier version derived it, and every purpose-built test village quietly lost most of
        /// its adults to a commute it never asked for - QueueAndDoorTests' shop stopped forming a
        /// queue at all.
        /// </summary>
        [Test]
        public void AHandBuiltCitizenIsNotGivenACommuteItNeverAskedFor()
        {
            var who = new Citizen(new CitizenId(0), "Test", "Person", GameClock.EpochYear - 40,
                                  Occupation.None, new HouseholdId(0), new PlaceId(1),
                                  PlaceId.None, 0, 0, 128, 128, null);

            Assert.That(who.WorksAwayIn(GameClock.EpochYear), Is.False,
                        "a fixture that hand-builds its people is exact on purpose");
        }
    }
}
