using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Sim;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// Coverage for the People and Sim layers, which had none.
    ///
    /// The load-bearing tests here are <see cref="DayPlanTests"/> â€” a day plan that overlaps
    /// itself or runs past midnight makes people silently skip whole activities, and that is
    /// invisible in the game. These sweep every citizen across four weeks rather than spot-
    /// checking, because the failures were rare: 4 bad blocks in ~3,000.
    /// </summary>
    internal static class Village
    {
        private static WorldModel _world;
        private static Population _people;
        public const ulong Seed = 1979;

        public static WorldModel World
        {
            get
            {
                if (_world == null)
                    _world = WorldBuilder.Build(VillageParser.Parse(TestContent.Read("village.txt")));
                return _world;
            }
        }

        public static Population People
        {
            get
            {
                if (_people == null)
                    _people = PopulationGenerator.Generate(
                        World,
                        NameTable.Parse(TestContent.Read("names.txt")),
                        ParticularsTable.Parse(TestContent.Read("particulars.txt")),
                        Seed);
                return _people;
            }
        }
    }

    [TestFixture]
    public class PopulationTests
    {
        /// <summary>The village fixture is the one the generator builds, which is the epoch year.</summary>
        private const int Year = GameClock.EpochYear;

        [Test]
        public void VillageSizedPopulation()
        {
            // Widened from (85,125) when the east quarter was authored and Ashcombe went
            // from 112 people to 158. The bound is a sanity check on the generator - that
            // dwellings turn into a plausible number of residents - not a pin on one village,
            // and the plan of record takes this town to 600.
            Assert.That(Village.People.Count, Is.InRange(85, 700),
                "the population should follow from the authored dwellings");
        }

        [Test]
        public void EveryoneHasAHomeThatIsADwelling()
        {
            foreach (var c in Village.People.Citizens)
            {
                Assert.That(c.Home.IsValid, Is.True, $"{c.FullName} has no home");
                Assert.That(Village.World.GetPlace(c.Home).Kind, Is.EqualTo(PlaceKind.Dwelling));
            }
        }

        [Test]
        public void EveryChildSharesAHomeWithAnAdult()
        {
            foreach (var c in Village.People.Citizens)
            {
                if (!c.IsChildIn(Year)) continue;
                bool guardian = false;
                foreach (var id in Village.People.HouseholdMembers(c))
                    if (!Village.People.Get(id).IsChildIn(Year)) { guardian = true; break; }
                Assert.That(guardian, Is.True, $"{c.FullName} ({c.AgeIn(Year)}) lives with no adult");
            }
        }

        [Test]
        public void NobodyWorksSomewhereWithNoJobs()
        {
            foreach (var c in Village.People.Citizens)
            {
                if (!c.Works) continue;
                Assert.That(Village.World.GetPlace(c.Work).JobSlots, Is.GreaterThan(0),
                    $"{c.FullName} works at a place with no jobs");
            }
        }

        [Test]
        public void NoWorkplaceIsOverstaffed()
        {
            foreach (var place in Village.World.AllPlaces)
            {
                if (place.JobSlots == 0) continue;
                Assert.That(Village.People.WorkersAt(place.Id).Count,
                    Is.LessThanOrEqualTo(place.JobSlots),
                    $"'{place.Name}' has more staff than posts");
            }
        }

        [Test]
        public void ChildrenAndEldersDoNotHoldJobs()
        {
            foreach (var c in Village.People.Citizens)
                if (c.IsChildIn(Year) || c.StageIn(Year) == LifeStage.Elder)
                    Assert.That(c.Works, Is.False, $"{c.FullName} ({c.StageIn(Year)}) should not be employed");
        }

        [Test]
        public void SameSeedProducesTheSamePeople()
        {
            var names = NameTable.Parse(TestContent.Read("names.txt"));
            var parts = ParticularsTable.Parse(TestContent.Read("particulars.txt"));

            var a = PopulationGenerator.Generate(Village.World, names, parts, 4242);
            var b = PopulationGenerator.Generate(Village.World, names, parts, 4242);

            Assert.That(b.Count, Is.EqualTo(a.Count));
            for (int i = 0; i < a.Count; i++)
            {
                var x = a.Get(new CitizenId(i));
                var y = b.Get(new CitizenId(i));
                Assert.That(y.FullName, Is.EqualTo(x.FullName), $"citizen {i} differs");
                Assert.That(y.BirthYear, Is.EqualTo(x.BirthYear));
                Assert.That(y.Job, Is.EqualTo(x.Job));
                Assert.That(y.Home, Is.EqualTo(x.Home));
            }
        }

        [Test]
        public void DifferentSeedsProduceDifferentPeople()
        {
            var names = NameTable.Parse(TestContent.Read("names.txt"));
            var parts = ParticularsTable.Parse(TestContent.Read("particulars.txt"));

            var a = PopulationGenerator.Generate(Village.World, names, parts, 1);
            var b = PopulationGenerator.Generate(Village.World, names, parts, 2);

            bool anyDifferent = false;
            int n = a.Count < b.Count ? a.Count : b.Count;
            for (int i = 0; i < n; i++)
                if (a.Get(new CitizenId(i)).FullName != b.Get(new CitizenId(i)).FullName)
                { anyDifferent = true; break; }

            Assert.That(anyDifferent, Is.True);
        }
    }

    /// <summary>
    /// CitizenId is a slot in an array, and the game's central event takes somebody out of that
    /// array. Everything that has to outlive one run â€” a save, a case note, an observation â€”
    /// needs a name that is not an offset.
    /// </summary>
    [TestFixture]
    public class CitizenKeyTests
    {
        [Test]
        public void EverybodyHasTheirOwnKey()
        {
            var seen = new Dictionary<ulong, string>();
            foreach (var c in Village.People.Citizens)
            {
                Assert.That(c.Key.IsValid, Is.True, $"{c.FullName} has no key");
                Assert.That(seen.ContainsKey(c.Key.Value), Is.False,
                    $"{c.FullName} shares a key with {(seen.TryGetValue(c.Key.Value, out var o) ? o : "?")}");
                seen[c.Key.Value] = c.FullName;
            }
        }

        [Test]
        public void AKeyFindsItsPersonAgain()
        {
            foreach (var c in Village.People.Citizens)
            {
                Assert.That(Village.People.Find(c.Key), Is.EqualTo(c.Id));
                Assert.That(Village.People.Get(c.Key).FullName, Is.EqualTo(c.FullName));
            }

            Assert.That(Village.People.Find(new CitizenKey(1)).IsValid, Is.False,
                "a key for somebody who is not here has to come back as nobody, not as somebody");
        }

        [Test]
        public void AKeyIsMadeOfTheHomeAndThePlaceInIt()
        {
            // Not of the array index â€” that is the whole point. Recomputing it from the two
            // things it is documented to be made of has to give the same answer.
            foreach (var c in Village.People.Citizens)
            {
                var household = Village.People.GetHousehold(c.Household);
                Assert.That(Citizen.KeyFor(household.Key, c.BirthOrder), Is.EqualTo(c.Key),
                    $"{c.FullName}'s key is not (household, birth order)");
            }
        }

        [Test]
        public void TheSameSeedNamesTheSamePeople()
        {
            var names = NameTable.Parse(TestContent.Read("names.txt"));
            var parts = ParticularsTable.Parse(TestContent.Read("particulars.txt"));

            var a = PopulationGenerator.Generate(Village.World, names, parts, 4242);
            var b = PopulationGenerator.Generate(Village.World, names, parts, 4242);

            for (int i = 0; i < a.Count; i++)
                Assert.That(b.Get(new CitizenId(i)).Key, Is.EqualTo(a.Get(new CitizenId(i)).Key));
        }
    }

    [TestFixture]
    public class DayPlanTests
    {
        /// <summary>The village fixture is the one the generator builds, which is the epoch year.</summary>
        private const int Year = GameClock.EpochYear;

        private const int Days = 28;

        /// <summary>Every minute of every day belongs to exactly one block. This is the test
        /// that catches the planner emitting data the rest of the game cannot represent.</summary>
        [Test]
        public void PlansCoverTheWholeDayWithoutOverlapOrGap()
        {
            foreach (var citizen in Village.People.Citizens)
            for (int day = 0; day < Days; day++)
            {
                var plan = DayPlanner.Plan(Village.World, Village.People, citizen, day, Village.Seed);
                var blocks = plan.Blocks;

                Assert.That(blocks.Count, Is.GreaterThan(0),
                    $"{citizen.FullName} day {day}: empty plan");
                Assert.That(blocks[0].StartMinute, Is.EqualTo(0),
                    $"{citizen.FullName} day {day}: day does not start at midnight");
                Assert.That(blocks[blocks.Count - 1].EndMinute, Is.EqualTo(1440),
                    $"{citizen.FullName} day {day}: day does not end at midnight");

                for (int i = 0; i < blocks.Count; i++)
                {
                    Assert.That(blocks[i].EndMinute, Is.GreaterThan(blocks[i].StartMinute),
                        $"{citizen.FullName} day {day}: {blocks[i]} runs backwards");
                    Assert.That(blocks[i].EndMinute, Is.LessThanOrEqualTo(1440),
                        $"{citizen.FullName} day {day}: {blocks[i]} runs past midnight");

                    if (i > 0)
                        Assert.That(blocks[i].StartMinute, Is.EqualTo(blocks[i - 1].EndMinute),
                            $"{citizen.FullName} day {day}: gap or overlap between "
                          + $"{blocks[i - 1]} and {blocks[i]}");
                }
            }
        }

        /// <summary>Nobody should be standing inside a place that is shut.</summary>
        [Test]
        public void NobodyVisitsAPlaceThatIsClosed()
        {
            var offences = new List<string>();

            foreach (var citizen in Village.People.Citizens)
            for (int day = 0; day < Days; day++)
            {
                var plan = DayPlanner.Plan(Village.World, Village.People, citizen, day, Village.Seed);
                int dayOfWeek = day % 7;

                foreach (var b in plan.Blocks)
                {
                    // Home and work are exempt: you can be at home at any hour, and a shift is
                    // defined by the workplace's own hours already.
                    if (b.Where == citizen.Home || b.Where == citizen.Work) continue;
                    if (b.What == Activity.AtHome || b.What == Activity.Asleep) continue;

                    var place = Village.World.GetPlace(b.Where);
                    if (place == null || place.AlwaysOpen) continue;

                    if (!place.IsOpenAt(b.StartMinute, dayOfWeek))
                        offences.Add($"{citizen.FullName} d{day}: arrives at '{place.Name}' at "
                                   + $"{b.StartMinute / 60:00}:{b.StartMinute % 60:00} while shut");
                    else if (!place.IsOpenAt(b.EndMinute - 1, dayOfWeek))
                        offences.Add($"{citizen.FullName} d{day}: still in '{place.Name}' at "
                                   + $"{(b.EndMinute - 1) / 60:00}:{(b.EndMinute - 1) % 60:00} after closing");
                }
            }

            Assert.That(offences, Is.Empty,
                "visits to closed places:\n  " + string.Join("\n  ", offences.ToArray()));
        }

        [Test]
        public void PlanningIsAPureFunctionOfSeedCitizenAndDay()
        {
            var citizen = Village.People.Get(new CitizenId(3));

            for (int day = 0; day < 5; day++)
            {
                var a = DayPlanner.Plan(Village.World, Village.People, citizen, day, Village.Seed);
                var b = DayPlanner.Plan(Village.World, Village.People, citizen, day, Village.Seed);

                Assert.That(b.Blocks.Count, Is.EqualTo(a.Blocks.Count), $"day {day}");
                for (int i = 0; i < a.Blocks.Count; i++)
                {
                    Assert.That(b.Blocks[i].StartMinute, Is.EqualTo(a.Blocks[i].StartMinute));
                    Assert.That(b.Blocks[i].EndMinute, Is.EqualTo(a.Blocks[i].EndMinute));
                    Assert.That(b.Blocks[i].Where, Is.EqualTo(a.Blocks[i].Where));
                    Assert.That(b.Blocks[i].What, Is.EqualTo(a.Blocks[i].What));
                }
            }
        }

        [Test]
        public void EveryoneIsAsleepAtThreeInTheMorning()
        {
            int awake = 0;
            foreach (var citizen in Village.People.Citizens)
            {
                var plan = DayPlanner.Plan(Village.World, Village.People, citizen, 2, Village.Seed);
                if (plan.At(3 * 60).What != Activity.Asleep) awake++;
            }
            // The night shift is legitimately up; nearly everyone else should not be.
            Assert.That(awake, Is.LessThan(Village.People.Count / 4),
                $"{awake} people awake at 03:00 â€” the night is not quiet enough");
        }

        /// <summary>
        /// A shift that covers the middle of the day is broken by a dinner hour, and the dinner
        /// hour is not spent in the workplace.
        ///
        /// This is the test the `street` instrument asked for. Before it, a shift was one
        /// unbroken block 09:00-17:00, so thirty-seven people were sealed inside buildings for
        /// eight hours and the village measured 0.2 visible bodies at half past twelve. A break
        /// that keeps somebody in the building would satisfy the letter of "a break" and change
        /// nothing anyone can see, so the assertion is on WHERE, not merely on the split.
        /// </summary>
        [Test]
        public void AShiftAcrossTheDinnerHourIsBrokenByABreakOutOfTheWorkplace()
        {
            var offences = new List<string>();
            int broken = 0;

            foreach (var citizen in Village.People.Citizens)
            for (int day = 0; day < Days; day++)
            {
                var plan = DayPlanner.Plan(Village.World, Village.People, citizen, day, Village.Seed);

                // Only shifts that genuinely straddle the dinner hour are owed one.
                if (plan.At(11 * 60 + 30).What != Activity.AtWork) continue;
                if (plan.At(13 * 60 + 30).What != Activity.AtWork) continue;

                Block? away = null;
                foreach (var b in plan.Blocks)
                {
                    if (b.StartMinute < 11 * 60 || b.EndMinute > 14 * 60 + 30) continue;
                    if (b.What == Activity.AtWork) continue;
                    away = b;
                    break;
                }

                if (away == null)
                {
                    offences.Add($"{citizen.FullName} d{day}: never leaves work between 11:00 and 14:30");
                    continue;
                }

                broken++;
                Assert.That(away.Value.Where, Is.Not.EqualTo(citizen.Work),
                    $"{citizen.FullName} d{day}: 'break' is still inside the workplace");
            }

            Assert.That(offences, Is.Empty,
                $"{offences.Count} shifts with no dinner hour:\n  "
              + string.Join("\n  ", offences.ToArray()));
            Assert.That(broken, Is.GreaterThan(0),
                "no shift in the whole village crosses midday â€” this test is measuring nothing");
        }

        /// <summary>
        /// Children get out of the school building at dinnertime, the same as the adults get out
        /// of theirs. A school day is 08:30-15:30 unbroken otherwise, which is twenty children
        /// invisible for the six hours of best light in the day.
        /// </summary>
        [Test]
        public void SchoolchildrenGetADinnerBreakOutOfTheSchoolBuilding()
        {
            var offences = new List<string>();
            int broken = 0;

            foreach (var citizen in Village.People.Citizens)
            {
                if (!citizen.IsChildIn(Year)) continue;

                for (int day = 0; day < Days; day++)
                {
                    var plan = DayPlanner.Plan(Village.World, Village.People, citizen, day, Village.Seed);
                    if (plan.At(11 * 60 + 30).What != Activity.AtSchool) continue;
                    if (plan.At(13 * 60 + 30).What != Activity.AtSchool) continue;

                    var school = plan.At(11 * 60 + 30).Where;

                    bool out_ = false;
                    foreach (var b in plan.Blocks)
                    {
                        if (b.StartMinute < 11 * 60 || b.EndMinute > 14 * 60 + 30) continue;
                        if (b.What == Activity.AtSchool || b.Where == school) continue;
                        out_ = true;
                        break;
                    }

                    if (out_) broken++;
                    else offences.Add($"{citizen.FullName} d{day}: in school all through dinner");
                }
            }

            Assert.That(offences, Is.Empty,
                $"{offences.Count} school days with no dinner break:\n  "
              + string.Join("\n  ", offences.ToArray()));
            Assert.That(broken, Is.GreaterThan(0),
                "no child is ever at school across midday â€” this test is measuring nothing");
        }

        [Test]
        public void WorkersActuallyGoToWork()
        {
            foreach (var citizen in Village.People.Citizens)
            {
                if (!citizen.Works) continue;

                bool worksThisWeek = false;
                for (int day = 0; day < 7 && !worksThisWeek; day++)
                {
                    var plan = DayPlanner.Plan(Village.World, Village.People, citizen, day, Village.Seed);
                    foreach (var b in plan.Blocks)
                        if (b.What == Activity.AtWork) { worksThisWeek = true; break; }
                }

                Assert.That(worksThisWeek, Is.True,
                    $"{citizen.FullName} is employed at "
                  + $"'{Village.World.GetPlace(citizen.Work).Name}' but never goes in");
            }
        }
    }

    [TestFixture]
    public class SimulationTests
    {
        [Test]
        public void SameSeedRunsIdentically()
        {
            var a = new Simulation(Village.World, Village.People, Village.Seed, 6 * 60);
            var b = new Simulation(Village.World, Village.People, Village.Seed, 6 * 60);

            const int ticks = GameClock.TicksPerMinute * 90;
            a.Tick(ticks);
            b.Tick(ticks);

            Assert.That(b.Clock.Tick, Is.EqualTo(a.Clock.Tick));
            for (int i = 0; i < a.AgentCount; i++)
            {
                var x = a.GetAgent(i);
                var y = b.GetAgent(i);
                Assert.That(y.Position.X, Is.EqualTo(x.Position.X).Within(0.0001f), $"agent {i} X");
                Assert.That(y.Position.Y, Is.EqualTo(x.Position.Y).Within(0.0001f), $"agent {i} Y");
                Assert.That(y.Doing, Is.EqualTo(x.Doing), $"agent {i} activity");
            }
        }

        [Test]
        public void TickingInChunksMatchesTickingOneAtATime()
        {
            var chunked = new Simulation(Village.World, Village.People, Village.Seed, 6 * 60);
            var single = new Simulation(Village.World, Village.People, Village.Seed, 6 * 60);

            const int ticks = GameClock.TicksPerMinute * 45;
            chunked.Tick(ticks);
            for (int i = 0; i < ticks; i++) single.Tick();

            for (int i = 0; i < chunked.AgentCount; i++)
                Assert.That(single.GetAgent(i).Position.X,
                    Is.EqualTo(chunked.GetAgent(i).Position.X).Within(0.0001f),
                    $"agent {i} diverged â€” fast-forward is not equivalent to real time");
        }

        [Test]
        public void EverybodyStaysOnWalkableGround()
        {
            var sim = new Simulation(Village.World, Village.People, Village.Seed, 6 * 60);

            for (int step = 0; step < 60; step++)
            {
                sim.Tick(GameClock.TicksPerMinute * 15);
                for (int i = 0; i < sim.AgentCount; i++)
                {
                    var tile = sim.GetAgent(i).Position.ToTile();
                    Assert.That(Village.World.Grid.InBounds(tile), Is.True,
                        $"agent {i} left the map at {tile}");
                    Assert.That(Village.World.Grid.IsWalkable(tile), Is.True,
                        $"agent {i} is standing in a wall at {tile}");
                }
            }
        }

        [Test]
        public void PeopleStopAndTalkInTheStreet()
        {
            var sim = new Simulation(Village.World, Village.People, Village.Seed, 0);

            int everTalked = 0;
            var seen = new HashSet<int>();

            // A whole day, not a slice of one.
            //
            // Two people passing close enough at the same second is a chaotic quantity: over
            // three hours it swings between none and eight depending on nothing in particular,
            // so a threshold on one three-hour window measures the dice rather than the
            // village. Over a day it settles to a dozen or twenty, which is what the assertion
            // below is actually about â€” that this is not a place where everybody walks briskly
            // past everybody else.
            for (int minute = 0; minute < 1440; minute++)
            {
                sim.Tick(GameClock.TicksPerMinute);
                for (int i = 0; i < sim.AgentCount; i++)
                {
                    var a = sim.GetAgent(i);
                    if (a.TalkTicks > 0 && seen.Add(i)) everTalked++;
                }
            }

            Assert.That(everTalked, Is.GreaterThan(8),
                $"only {everTalked} people stopped to talk all day â€” the village is too brisk");
        }

        [Test]
        public void ConversationsAlwaysHaveTwoSides()
        {
            var sim = new Simulation(Village.World, Village.People, Village.Seed, 8 * 60);

            for (int step = 0; step < 120; step++)
            {
                sim.Tick(GameClock.TicksPerMinute);

                for (int i = 0; i < sim.AgentCount; i++)
                {
                    var a = sim.GetAgent(i);
                    if (a.TalkTicks <= 0) continue;

                    Assert.That(a.TalkingTo.IsValid, Is.True, $"agent {i} is talking to nobody");

                    var other = sim.GetAgent(a.TalkingTo);
                    Assert.That(other.TalkTicks, Is.GreaterThan(0),
                        $"agent {i} is talking at someone who is not listening");
                    Assert.That(other.TalkingTo.Value, Is.EqualTo(i),
                        $"agent {i}'s partner is talking to somebody else");
                }
            }
        }

        [Test]
        public void PeopleWalkTogetherWhenTheyLeaveTogether()
        {
            var sim = new Simulation(Village.World, Village.People, Village.Seed, 8 * 60);

            int paired = 0;
            var seen = new HashSet<int>();

            for (int minute = 0; minute < 90; minute++)
            {
                sim.Tick(GameClock.TicksPerMinute);
                for (int i = 0; i < sim.AgentCount; i++)
                {
                    var a = sim.GetAgent(i);
                    if (!a.WalkingWith.IsValid || !seen.Add(i)) continue;

                    // A companion must be somebody from the same household.
                    var me = Village.People.Get(new CitizenId(i));
                    var them = Village.People.Get(a.WalkingWith);
                    Assert.That(them.Household, Is.EqualTo(me.Household),
                        $"{me.FullName} is walking with {them.FullName}, who lives elsewhere");
                    paired++;
                }
            }

            Assert.That(paired, Is.GreaterThan(0),
                "nobody walked with anybody all morning â€” the school run is not pairing up");
        }

        [Test]
        public void NobodyGetsPermanentlyStuckTravelling()
        {
            var sim = new Simulation(Village.World, Village.People, Village.Seed, 6 * 60);
            sim.Tick(GameClock.TicksPerMinute * 240);   // four hours

            int travelling = 0;
            for (int i = 0; i < sim.AgentCount; i++)
                if (sim.GetAgent(i).Travelling) travelling++;

            Assert.That(travelling, Is.LessThan(sim.AgentCount / 3),
                $"{travelling} of {sim.AgentCount} still walking â€” journeys are not completing");
        }
    }

    /// <summary>
    /// Everybody's day plan changes on a minute boundary, so every journey a day contains lands
    /// on one of 1,440 ticks out of 1,728,000 â€” and they land in heaps. At five thousand people
    /// 1,317 journeys start on the single 08:30 tick, and PathBudgetPerTick then behaves as a
    /// queue rather than a smoother: the last citizen sets off five and a half seconds after the
    /// first, and the school run dribbles out of the houses instead of the street emptying.
    ///
    /// The answer is to spread departures inside the minute by design. What these tests pin is
    /// the tension in doing so: spread ENOUGH that no tick carries the whole clump, and spread
    /// BY HOUSEHOLD so that the people who leave a house together still do.
    /// </summary>
    [TestFixture]
    public class DepartureJitterTests
    {
        /// <summary>Every journey started in a day, as (agent, tick, where they set off for).</summary>
        private static List<(int who, long tick, PlaceId to)> DepartuesOverADay()
        {
            var sim = new Simulation(Village.World, Village.People, Village.Seed, 0);
            var wasHeadedFor = new PlaceId[sim.AgentCount];
            for (int i = 0; i < sim.AgentCount; i++) wasHeadedFor[i] = sim.GetAgent(i).Destination;

            var departures = new List<(int, long, PlaceId)>();

            for (int t = 0; t < GameClock.TicksPerDay; t++)
            {
                sim.Tick();
                for (int i = 0; i < sim.AgentCount; i++)
                {
                    var now = sim.GetAgent(i).Destination;
                    if (now == wasHeadedFor[i]) continue;
                    departures.Add((i, sim.Clock.Tick, now));
                    wasHeadedFor[i] = now;
                }
            }
            return departures;
        }

        [Test]
        public void JourneysDoNotAllStartOnTheFirstTickOfTheirMinute()
        {
            var departures = DepartuesOverADay();
            Assert.That(departures.Count, Is.GreaterThan(200), "hardly anybody went anywhere all day");

            int onTheBoundary = 0;
            foreach (var d in departures)
                if (d.tick % GameClock.TicksPerMinute == 0) onTheBoundary++;

            // Every one of them landed there before this change. Spread evenly across the 1,200
            // ticks of a minute the expectation is one in twelve hundred; anything under a
            // twentieth is comfortably past "they all still move at once".
            Assert.That(onTheBoundary * 20, Is.LessThan(departures.Count),
                $"{onTheBoundary} of {departures.Count} journeys still begin on the tick the "
              + "minute turns â€” the clump has not been spread at all");
        }

        [Test]
        public void TheBusiestTickOfTheDayCarriesFarLessThanTheBusiestMinute()
        {
            var departures = DepartuesOverADay();

            var perTick = new Dictionary<long, int>();
            var perMinute = new Dictionary<long, int>();

            foreach (var d in departures)
            {
                perTick[d.tick] = perTick.TryGetValue(d.tick, out int a) ? a + 1 : 1;
                long minute = d.tick / GameClock.TicksPerMinute;
                perMinute[minute] = perMinute.TryGetValue(minute, out int b) ? b + 1 : 1;
            }

            int worstTick = 0, worstMinute = 0;
            foreach (var n in perTick.Values) if (n > worstTick) worstTick = n;
            foreach (var n in perMinute.Values) if (n > worstMinute) worstMinute = n;

            Assert.That(worstMinute, Is.GreaterThan(8), "no minute of the day is busy â€” nothing was tested");
            Assert.That(worstTick, Is.LessThan(worstMinute),
                $"the busiest tick still carries {worstTick} of the busiest minute's {worstMinute} "
              + "journeys â€” the whole clump is landing on one tick");
        }

        [Test]
        public void PeopleWhoLeaveTheSameHouseForTheSamePlaceStillLeaveTogether()
        {
            // This is what the jitter is keyed on the household to protect. A brother and sister
            // more than a second apart are more than six tiles apart by the time the second one
            // moves, FindCompanion has nothing to pair them with, and the companion link â€” two
            // dots moving together, which reads as "together" instantly â€” quietly stops
            // happening. It is the effect the spreading was supposed to serve, not spend.
            var departures = DepartuesOverADay();

            var groups = new Dictionary<string, List<long>>();
            foreach (var d in departures)
            {
                var who = Village.People.Get(new CitizenId(d.who));
                string key = who.Household + "|" + d.to + "|" + (d.tick / GameClock.TicksPerMinute);
                if (!groups.TryGetValue(key, out var ticks)) groups[key] = ticks = new List<long>();
                ticks.Add(d.tick);
            }

            int together = 0;
            foreach (var pair in groups)
            {
                if (pair.Value.Count < 2) continue;

                long first = long.MaxValue, last = long.MinValue;
                foreach (long t in pair.Value)
                {
                    if (t < first) first = t;
                    if (t > last) last = t;
                }

                Assert.That(last - first, Is.LessThanOrEqualTo(GameClock.TicksPerSecond),
                    $"two people left {pair.Key} for the same place {last - first} ticks apart");
                together++;
            }

            Assert.That(together, Is.GreaterThan(0),
                "no two people from one household ever left for the same place â€” nothing was tested");
        }
    }

    /// <summary>
    /// Midnight used to rebuild every DayPlan on one tick: 17.8 ms at five thousand people, a
    /// hundred ordinary ticks' work on the tick where the date changes. The plan document
    /// claimed the pure-function DayPlan meant there was no such hitch; there was.
    /// </summary>
    [TestFixture]
    public class DayRolloverTests
    {
        [Test]
        public void EverybodyIsPlanningTodayWithinAMinuteOfMidnight()
        {
            var sim = new Simulation(Village.World, Village.People, Village.Seed, 23 * 60 + 59);
            sim.Tick(GameClock.TicksPerMinute * 2);   // over midnight, and a minute past it

            Assert.That(sim.Clock.Day, Is.EqualTo(1));
            for (int i = 0; i < sim.AgentCount; i++)
                Assert.That(sim.PlanFor(new CitizenId(i)).Day, Is.EqualTo(1),
                    $"agent {i} is still working from yesterday's plan a minute into the day");
        }

        [Test]
        public void NobodyActsOnAStalePlanBecauseNobodyIsUpYet()
        {
            // What makes spreading the rollover safe rather than merely cheaper: DayPlanner
            // always opens a day with (0 .. wake) asleep at home, and the earliest wake anybody
            // can draw is 04:45. Every plan therefore says the same thing for the first four and
            // three quarter hours whichever day it was built for.
            foreach (var citizen in Village.People.Citizens)
            for (int day = 0; day < 8; day++)
            {
                var plan = DayPlanner.Plan(Village.World, Village.People, citizen, day, Village.Seed);
                var first = plan.Blocks[0];

                Assert.That(first.StartMinute, Is.EqualTo(0));
                Assert.That(first.What, Is.EqualTo(Activity.Asleep),
                    $"{citizen.FullName} does not begin day {day} asleep");
                Assert.That(first.Where, Is.EqualTo(citizen.Home));
                // AT 04:45, NOT PAST IT. The earliest wake anybody can draw is exactly 285:
                // WakeTime bottoms out at 05:00 for a farmer and Punctuality runs -15..+15, so
                // 300-15 is reachable by construction. This read GreaterThan for a long time and
                // passed on luck alone - no farmer in the old population happened to draw -15.
                // Retuning names.txt for 1991 shifted the generator's stream, one of them drew
                // it, and a boundary that was always legal finally turned up. The invariant the
                // rollover window actually needs is "nobody is up BEFORE 04:45".
                Assert.That(first.EndMinute, Is.GreaterThanOrEqualTo(4 * 60 + 45),
                    $"{citizen.FullName} is up at {first.EndMinute / 60:00}:{first.EndMinute % 60:00} "
                  + "on day " + day + " â€” earlier than the rollover window assumes");
            }
        }

        [Test]
        public void CrossingMidnightMovesNobody()
        {
            var sim = new Simulation(Village.World, Village.People, Village.Seed, 23 * 60 + 55);
            sim.Tick(GameClock.TicksPerMinute * 5 - 1);   // one tick short of midnight

            var wasAt = new PlaceId[sim.AgentCount];
            var wasDoing = new Activity[sim.AgentCount];
            for (int i = 0; i < sim.AgentCount; i++)
            {
                wasAt[i] = sim.GetAgent(i).At;
                wasDoing[i] = sim.GetAgent(i).Doing;
            }

            sim.Tick();   // the tick the date changes on

            for (int i = 0; i < sim.AgentCount; i++)
            {
                Assert.That(sim.GetAgent(i).At, Is.EqualTo(wasAt[i]), $"agent {i} moved at midnight");
                Assert.That(sim.GetAgent(i).Doing, Is.EqualTo(wasDoing[i]),
                    $"agent {i} changed what they were doing at midnight");
            }
        }
    }

    /// <summary>
    /// The village must never contain a person the simulation is wrong about.
    ///
    /// A failed path used to be swallowed by filing the citizen at their destination while
    /// leaving their feet where they were, which is invisible on screen â€” the renderer draws
    /// the position â€” and wrong everywhere else, because the inspector, the density table and
    /// the window lights all read the place. There is nothing to see, so there has to be
    /// something to run.
    /// </summary>
    [TestFixture]
    public class StrandingTests
    {
        [Test]
        public void NobodyIsStrandedOrMisfiledOverAWholeDayInAshcombe()
        {
            var sim = new Simulation(Village.World, Village.People, Village.Seed, 0);

            for (int minute = 0; minute < 1440; minute++)
            {
                for (int t = 0; t < GameClock.TicksPerMinute; t++)
                {
                    sim.Tick();
                    if (sim.StrandedCount > 0) Assert.Fail(WhoIsStranded(sim));
                }

                // Once a minute is enough for the second invariant: somebody wrongly filed
                // stays wrongly filed until they next set off, so a per-tick sweep would find
                // the same offence twelve hundred times and cost a hundredfold for it.
                for (int i = 0; i < sim.AgentCount; i++)
                {
                    var a = sim.GetAgent(i);
                    if (!a.At.IsValid) continue;

                    var place = Village.World.GetPlace(a.At);
                    Assert.That(place.Bounds.Contains(a.Position.ToTile()), Is.True,
                        $"{Village.People.Get(new CitizenId(i)).FullName} is recorded as being in "
                      + $"'{place.Name}' at {sim.Clock} while standing at {a.Position.ToTile()}");
                }
            }

            Assert.That(sim.GaveUpTotal, Is.EqualTo(0),
                "no journey in Ashcombe should come anywhere near the node cap");
        }

        private static string WhoIsStranded(Simulation sim)
        {
            for (int i = 0; i < sim.AgentCount; i++)
            {
                var a = sim.GetAgent(i);
                if (!a.Stranded) continue;

                return $"{Village.People.Get(new CitizenId(i)).FullName} could not reach "
                     + $"'{Village.World.GetPlace(a.Destination)?.Name}' at {sim.Clock}";
            }
            return "somebody is stranded and then was not";
        }
    }


    [TestFixture]
    public class StandingStillTests
    {
        // A strand-specific version of the test below was attempted and removed. It built a
        // three-kilometre village with the only shop on the far side of uncrossable water, so
        // that an errand would fire mid-walk and come back with no route. It never produced one,
        // and the reason is worth recording: the node budget gates when a search may START, not
        // how deep it may go, so a long walk does not strand anybody. Tuning the fixture until
        // it cooperated would have produced a test that passes for reasons nobody could state.
        //
        // The gap is accepted rather than hidden. Ashcombe strands nobody, and since the
        // pathfinder was given a map-scaled cap and a weighted heuristic, neither does a tripled
        // Ashcombe - `strand --days 3 --tile 3` reports zero. The bug class that made a
        // strand-specific test valuable is the one that was fixed. If stranding ever returns,
        // `Noir.Sim strand` reports it and this is the place to put the regression.

        /// <summary>
        /// The general form of the same rule, over a whole ordinary day: if somebody did not
        /// move, they must not be pointing anywhere. This is what lets the renderer read Heading
        /// as "is this person walking" without a second field it would have to be taught about.
        /// </summary>
        [Test]
        public void AnybodyWhoDidNotMoveIsNotPointingAnywhere()
        {
            var sim = new Simulation(Village.World, Village.People, Village.Seed, 6 * 60);
            var was = new Vec2[sim.AgentCount];
            for (int i = 0; i < sim.AgentCount; i++) was[i] = sim.GetAgent(i).Position;

            for (int t = 0; t < GameClock.TicksPerMinute * 240; t++)
            {
                sim.Tick();

                for (int i = 0; i < sim.AgentCount; i++)
                {
                    var a = sim.GetAgent(i);
                    bool moved = a.Position.X != was[i].X || a.Position.Y != was[i].Y;
                    was[i] = a.Position;

                    if (moved) continue;
                    Assert.That(a.Heading.X, Is.EqualTo(0f), $"agent {i} stood still facing somewhere at {sim.Clock}");
                    Assert.That(a.Heading.Y, Is.EqualTo(0f), $"agent {i} stood still facing somewhere at {sim.Clock}");
                }
            }
        }
    }

    [TestFixture]
    public class PathfinderTests
    {
        [Test]
        public void FindsAPathBetweenTwoDoors()
        {
            var pf = new Pathfinder(Village.World.Grid);
            var path = new List<Tile>();

            var pub = Village.World.GetPlace(Village.World.PlacesOfKind(PlaceKind.Pub)[0]);
            var shop = Village.World.GetPlace(Village.World.PlacesOfKind(PlaceKind.Shop)[0]);

            Assert.That(pf.TryFindPath(pub.Door, shop.Door, path), Is.True);
            Assert.That(path.Count, Is.GreaterThan(0));
            Assert.That(path[path.Count - 1], Is.EqualTo(shop.Door));
        }

        [Test]
        public void EveryStepOfAPathIsWalkableAndAdjacent()
        {
            var pf = new Pathfinder(Village.World.Grid);
            var path = new List<Tile>();

            var a = Village.World.GetPlace(Village.World.PlacesOfKind(PlaceKind.Church)[0]).Door;
            var b = Village.World.GetPlace(Village.World.PlacesOfKind(PlaceKind.Mill)[0]).Door;

            Assert.That(pf.TryFindPath(a, b, path), Is.True);

            var previous = a;
            foreach (var step in path)
            {
                Assert.That(Village.World.Grid.IsWalkable(step), Is.True, $"{step} is not walkable");
                Assert.That(Tile.ChebyshevDistance(previous, step), Is.EqualTo(1),
                    $"jump from {previous} to {step}");
                previous = step;
            }
        }

        [Test]
        public void RepeatedQueriesGiveTheSameAnswer()
        {
            // Guards the reused scratch buffers: a stale visit-stamp would show up here.
            var pf = new Pathfinder(Village.World.Grid);
            var first = new List<Tile>();
            var again = new List<Tile>();

            var a = Village.World.GetPlace(Village.World.PlacesOfKind(PlaceKind.School)[0]).Door;
            var b = Village.World.GetPlace(Village.World.PlacesOfKind(PlaceKind.Garage)[0]).Door;

            Assert.That(pf.TryFindPath(a, b, first), Is.True);
            for (int i = 0; i < 5; i++) pf.TryFindPath(b, a, again);   // churn the buffers
            Assert.That(pf.TryFindPath(a, b, again), Is.True);

            Assert.That(again.Count, Is.EqualTo(first.Count));
            for (int i = 0; i < first.Count; i++)
                Assert.That(again[i], Is.EqualTo(first[i]), $"step {i} differs on a repeat query");
        }

        /// <summary>
        /// The two failures have to be distinguishable, because the caller does different
        /// things with them: a route that does not exist will never exist, and a search that
        /// ran out of budget is worth asking again in a moment.
        /// </summary>
        [Test]
        public void FailureSaysWhetherTheRouteExistsOrTheSearchRanOut()
        {
            var grid = new TileGrid(60, 30);
            var path = new List<Tile>();

            // A sealed room in the corner: walkable inside, no way through the wall.
            for (int x = 2; x <= 8; x++)
            for (int y = 2; y <= 8; y++)
                if (x == 2 || x == 8 || y == 2 || y == 8)
                    grid.Set(x, y, Terrain.Wall, TileGrid.FlagsFor(Terrain.Wall));

            Assert.That(new Pathfinder(grid).FindPath(new Tile(30, 15), new Tile(5, 5), path),
                Is.EqualTo(PathOutcome.NoRouteExists));

            // The same walk, from one side of the map to the other, with an allowance of one
            // node. Nothing is wrong with the map; the search simply is not allowed to finish.
            Assert.That(new Pathfinder(grid, maxNodesExamined: 1)
                            .FindPath(new Tile(1, 15), new Tile(58, 28), path),
                Is.EqualTo(PathOutcome.GaveUp));

            Assert.That(new Pathfinder(grid).FindPath(new Tile(1, 15), new Tile(58, 28), path),
                Is.EqualTo(PathOutcome.Found));
        }

        [Test]
        public void SamePlaceIsATrivialPath()
        {
            var pf = new Pathfinder(Village.World.Grid);
            var path = new List<Tile>();
            var door = Village.World.GetPlace(Village.World.PlacesOfKind(PlaceKind.Pub)[0]).Door;

            Assert.That(pf.TryFindPath(door, door, path), Is.True);
            Assert.That(path, Is.Empty, "a path to where you already are should have no steps");
        }
    }
}
