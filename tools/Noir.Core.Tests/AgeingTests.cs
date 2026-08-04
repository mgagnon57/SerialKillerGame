using System.Linq;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.People;

namespace Noir.Core.Tests
{
    /// <summary>
    /// People get older now.
    ///
    /// Age used to be a `readonly int` on <see cref="Citizen"/> and there was a second frozen year
    /// beside it, `Households.Year = 1991`. Over a fifteen-year game a seven-year-old stayed seven
    /// and a schoolchild never left school. A birth year is identity; an age is not.
    ///
    /// TWO THINGS ARE BEING PROTECTED AND THEY PULL OPPOSITE WAYS. The 1991 village must be exactly
    /// the village it was before the change - same people, same stages, same jobs - and yet the
    /// town must genuinely move across the window. The first is what makes the refactor safe; the
    /// second is what makes it worth doing.
    /// </summary>
    [TestFixture]
    public class AgeingTests
    {
        private const int Opens = GameClock.EpochYear;      // 1991
        private const int Closes = 2006;

        [Test]
        public void TheOpeningVillageIsExactlyTheVillageItAlwaysWas()
        {
            // The generator picks a STAGE first and then draws an age inside it - 5 to 15 for a
            // child, 21 to 64 for an adult, 65 to 88 for an elder. Stage is derived from age now,
            // so the thresholds have to put every person back where they were generated. If this
            // ever fails, the whole town has silently reshuffled.
            foreach (var c in Village.People.Citizens)
            {
                int age = c.AgeIn(Opens);
                var stage = c.StageIn(Opens);

                switch (stage)
                {
                    case LifeStage.Child: Assert.That(age, Is.InRange(5, 15), c.FullName); break;
                    case LifeStage.Elder: Assert.That(age, Is.InRange(65, 88), c.FullName); break;
                    default: Assert.That(age, Is.InRange(21, 64), c.FullName); break;
                }

                Assert.That(c.BirthYear, Is.EqualTo(Opens - age), "birth year is the age at the epoch");
            }

            // Nobody is generated in the gaps the generator leaves - 16 to 20, and 0 to 4. Those
            // holes are the proof that stage was not re-derived from some other rule.
            var ages = Village.People.Citizens.Select(c => c.AgeIn(Opens)).ToList();
            Assert.That(ages.Any(a => a >= 16 && a <= 20), Is.False, "the generator draws no teenagers");
            Assert.That(ages.Any(a => a < 5), Is.False, "nor anybody under five");
        }

        [Test]
        public void ChildrenAndEldersStillHoldNoJobsInTheOpeningYear()
        {
            // Jobs are handed out once, at generation, in the epoch year. This is the same claim
            // PeopleTests makes, restated against the derived stage so that the two cannot drift.
            foreach (var c in Village.People.Citizens)
                if (c.StageIn(Opens) != LifeStage.Adult)
                    Assert.That(c.Works, Is.False, $"{c.FullName} works at {c.StageIn(Opens)}");
        }

        [Test]
        public void AChildInNinetyOneIsAnAdultBeforeTheGameEnds()
        {
            // The whole point. Fifteen years is longer than childhood.
            var children = Village.People.Citizens.Where(c => c.IsChildIn(Opens)).ToList();
            Assert.That(children, Is.Not.Empty, "the village has children in it");

            foreach (var c in children)
            {
                Assert.That(c.AgeIn(Closes) - c.AgeIn(Opens), Is.EqualTo(Closes - Opens),
                            "fifteen years is fifteen years");
                Assert.That(c.IsChildIn(Closes), Is.False,
                            $"{c.FullName} was {c.AgeIn(Opens)} in 1991 and is still at school in 2006");
            }

            // And the crossing is a birthday, not a cliff: the oldest child leaves first.
            var oldest = children.OrderByDescending(c => c.AgeIn(Opens)).First();
            var youngest = children.OrderBy(c => c.AgeIn(Opens)).First();
            int LeavesIn(Citizen c) =>
                Enumerable.Range(Opens, 40).First(y => !c.IsChildIn(y));
            Assert.That(LeavesIn(oldest), Is.LessThan(LeavesIn(youngest)));
            Assert.That(LeavesIn(oldest), Is.EqualTo(oldest.BirthYear + Citizen.SchoolLeaving));
        }

        [Test]
        public void AnAdultNearRetirementReachesItInsideTheWindow()
        {
            var nearlyDone = Village.People.Citizens
                .Where(c => c.StageIn(Opens) == LifeStage.Adult && c.AgeIn(Opens) >= 55)
                .ToList();

            Assert.That(nearlyDone, Is.Not.Empty);
            foreach (var c in nearlyDone.Where(c => c.AgeIn(Closes) >= Citizen.Retirement))
                Assert.That(c.StageIn(Closes), Is.EqualTo(LifeStage.Elder),
                            $"{c.FullName} is {c.AgeIn(Closes)} in 2006 and not yet an elder");
        }

        [Test]
        public void TheWholeTownIsFifteenYearsOlderAndNOBODYHasBeenBornOrDied()
        {
            // THIS TEST RECORDS A KNOWN GAP RATHER THAN A FINISHED BEHAVIOUR, and it is written
            // as an assertion so that the day somebody adds demography it fails and gets read.
            //
            // Ageing without births or deaths is only half a town. THE-TRAJECTORY.md has Rossville
            // losing about 117 people across the 1990s - roughly one a month - and the research
            // has 23.6% under 18 and 20.1% over 65 at the opening. Neither can hold if the same
            // people simply get older:
            //
            //   * the youngest person generated is 5 in 1991 and therefore 20 in 2006
            //   * everybody over 65 in 1991 is over 80 in 2006, and none of them has died
            //   * the school that closes in 2006 has no pupils left to lose
            //
            // So the population arithmetic below is CORRECT AS IMPLEMENTED and WRONG AS A TOWN.
            // The fix is a demography pass - births, deaths, arrivals, departures - and it is the
            // necessary follow-up to this one, not part of it.
            var people = Village.People;
            int opens = people.Citizens.Count;

            Assert.That(people.Citizens.Count, Is.EqualTo(opens), "nobody arrives");
            Assert.That(people.Citizens.All(c => c.AgeIn(Closes) == c.AgeIn(Opens) + 15), Is.True);

            int childrenAtOpen = people.CountOfStage(LifeStage.Child, Opens);
            int childrenAtClose = people.CountOfStage(LifeStage.Child, Closes);
            int eldersAtOpen = people.CountOfStage(LifeStage.Elder, Opens);
            int eldersAtClose = people.CountOfStage(LifeStage.Elder, Closes);

            Assert.That(childrenAtOpen, Is.GreaterThan(0), "there are children in 1991");
            Assert.That(childrenAtClose, Is.Zero,
                        "and NONE in 2006, because none was ever born - see this test's remarks");
            Assert.That(eldersAtClose, Is.GreaterThan(eldersAtOpen),
                        "everybody ages into the elder bracket and nobody leaves it");

            // The three stages still account for everybody in every year, which is the one thing
            // that must hold whatever demography arrives later.
            for (int year = Opens; year <= Closes; year++)
                Assert.That(people.CountOfStage(LifeStage.Child, year)
                          + people.CountOfStage(LifeStage.Adult, year)
                          + people.CountOfStage(LifeStage.Elder, year),
                            Is.EqualTo(opens), $"everybody is somewhere in {year}");
        }

        [Test]
        public void ADayPlanFollowsThePersonIntoTheirNextStageOfLife()
        {
            // DayPlanner derives the year from the day it is planning, so the same citizen gets a
            // schoolchild's day in 1991 and an adult's in 2006 with nothing else changing.
            var child = Village.People.Citizens.First(c => c.AgeIn(Opens) <= 8);

            int dayIn(int year) => (int)(GameClock.TickOn(year, 3, 5) / GameClock.TicksPerDay);

            var atSchool = DayPlanner.Plan(Village.World, Village.People, child, dayIn(1991), Village.Seed);
            var grownUp = DayPlanner.Plan(Village.World, Village.People, child, dayIn(2006), Village.Seed);

            Assert.That(atSchool.Blocks.Any(b => b.What == Activity.AtSchool), Is.True,
                        $"{child.FullName} is {child.AgeIn(1991)} in 1991 and should be at school");
            Assert.That(grownUp.Blocks.Any(b => b.What == Activity.AtSchool), Is.False,
                        $"{child.FullName} is {child.AgeIn(2006)} in 2006 and should not be");
        }

        [Test]
        public void ThereIsExactlyOneNineteenNinetyOneInTheCodebase()
        {
            // GameClock.EpochYear is it. Households.Year used to be a second, with a comment
            // saying ages were worked against it "not against whatever year the machine thinks it
            // is" - true before the clock had a calendar, and the source of the whole problem.
            Assert.That(GameClock.EpochYear, Is.EqualTo(1991));
            Assert.That(new GameClock(0).Year, Is.EqualTo(GameClock.EpochYear));

            Assert.That(typeof(Citizen).GetField("Age"), Is.Null,
                        "Age is not a field any more - it is AgeIn(year), because it changes");
            Assert.That(typeof(Citizen).GetField("Stage"), Is.Null,
                        "nor Stage - it is derived from the birth year");
            Assert.That(typeof(Citizen).GetField("BirthYear"), Is.Not.Null);
        }
    }
}
