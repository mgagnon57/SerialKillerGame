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
    /// A village built to make the thing under test happen, rather than hoping the fixture village throws
    /// one up.
    ///
    /// The shop is the ONLY errand in it — no green, no pub, no allotments — so every adult's
    /// discretionary time is spent walking to the same counter, and every one of those journeys
    /// crosses two front doors each way. Nobody has children and nobody shares a roof, which
    /// keeps the school run and walking companions out of the way.
    ///
    /// It is also entirely self-contained: the layout is written here and the people are built
    /// by hand rather than generated, so editing Content/ cannot silently stop these tests from
    /// testing anything. The fixture village's shop holds about one and a half people at a time, which
    /// makes a queue of four there a matter of luck rather than a matter of test.
    /// </summary>
    internal static class Queueham
    {
        private const int Homes = 16;
        public const ulong Seed = 1979;

        private static WorldModel _world;
        private static Population _people;

        public static WorldModel World
        {
            get
            {
                if (_world == null) _world = WorldBuilder.Build(VillageParser.Parse(Layout()));
                return _world;
            }
        }

        public static Population People
        {
            get
            {
                if (_people == null) _people = Inhabit(World);
                return _people;
            }
        }

        /// <summary>
        /// A population built exactly like <see cref="People"/>, except each citizen's beats
        /// come from <paramref name="beatFor"/> instead of always being <see cref="Beat.None"/>.
        /// Lets a test give exactly one villager a beat without duplicating the fixture.
        /// </summary>
        public static Population PeopleWith(System.Func<int, Beat> beatFor) => Inhabit(World, beatFor);

        public static PlaceId Shop => World.PlacesOfKind(PlaceKind.Shop)[0];

        /// <summary>The one person who works behind the counter rather than queuing at it.</summary>
        public static CitizenId Shopkeeper => new CitizenId(0);

        private static string Layout()
        {
            var sb = new StringBuilder();
            sb.AppendLine("village Queueham");
            sb.AppendLine("size 120 26");
            sb.AppendLine("road main 3 1,13 118,13");

            // Open before anybody wakes and shut after everybody is home, so opening hours never
            // decide whether an errand happens.
            sb.AppendLine("place shop 53,2 14x9 \"The Shop\"");
            sb.AppendLine("  door 59,10");
            sb.AppendLine("  hours 07:00-19:00");
            sb.AppendLine("  jobs 1");
            sb.AppendLine("  human Where everybody has to wait their turn.");

            for (int i = 0; i < Homes; i++)
            {
                int x = 2 + i * 7;
                sb.AppendLine($"place dwelling {x},17 6x7 \"{i + 1} The Row\"");
                sb.AppendLine($"  door {x + 3},17");
                sb.AppendLine("  human A house with somebody in it.");
            }
            return sb.ToString();
        }

        /// <summary>One adult per cottage, hand-built rather than generated so the fixture is exact.</summary>
        private static Population Inhabit(WorldModel world, System.Func<int, Beat> beatFor = null)
        {
            var homes = world.PlacesOfKind(PlaceKind.Dwelling);
            var shop = world.PlacesOfKind(PlaceKind.Shop)[0];

            var citizens = new Citizen[homes.Count];
            var households = new Household[homes.Count];

            for (int i = 0; i < homes.Count; i++)
            {
                var id = new CitizenId(i);
                var house = new HouseholdId(i);

                // The first of them keeps the shop; the rest have nothing to do but go to it.
                bool keeper = i == 0;

                // Punctuality zero on everybody: they all wake at the same minute, so the only
                // thing spreading their errands out is the planner's own jitter — which is what
                // gets several of them to the counter inside the same few minutes.
                citizens[i] = new Citizen(id, "Customer", (i + 1).ToString(), GameClock.EpochYear - (34 + i % 30),
                                          keeper ? Occupation.Shopkeeper : Occupation.None,
                                          house, homes[i], keeper ? shop : PlaceId.None, 0, 0,
                                          (byte)(110 + i * 5), (byte)(40 + i * 8), null,
                                          beatFor == null ? Beat.None : beatFor(i));

                households[i] = new Household(house, homes[i], "Customer" + (i + 1),
                                              HouseholdShape.Solitary, new[] { id });
            }

            return new Population(citizens, households);
        }
    }

    [TestFixture]
    public class CounterLineTests
    {
        [Test]
        public void TheShopHasALineRunningBackFromTheCounterToTheDoor()
        {
            var line = Queueham.World.CounterAt(Queueham.Shop);
            var place = Queueham.World.GetPlace(Queueham.Shop);

            Assert.That(line, Is.Not.Null, "the shop has nowhere to queue");
            Assert.That(line.Count, Is.InRange(2, CounterLine.MaxSlots));
            Assert.That(line.Entry, Is.EqualTo(place.Door), "the line is fed from the front door");

            var seen = new HashSet<Tile>();
            foreach (var slot in line.Slots)
            {
                Assert.That(place.Bounds.Contains(slot), Is.True, $"{slot} is outside the shop");
                Assert.That(Queueham.World.Grid.IsWalkable(slot), Is.True, $"{slot} is not standable");
                Assert.That(seen.Add(slot), Is.True, $"{slot} is used twice in the same line");
            }

            // Orthogonal all the way back, which is what lets somebody shuffle forward in a
            // straight line without any chance of clipping the corner of a doorway.
            for (int i = 1; i < line.Count; i++)
                Assert.That(Tile.ManhattanDistance(line.Slots[i - 1], line.Slots[i]), Is.EqualTo(1),
                    $"positions {i - 1} and {i} are not next to each other");

            Assert.That(Tile.ManhattanDistance(line.Slots[line.Count - 1], line.Entry), Is.EqualTo(1),
                "the back of the queue should be one step inside the door");
        }

        [Test]
        public void EveryCounterInTheFixtureVillageHasAUsableLine()
        {
            foreach (var place in Village.World.AllPlaces)
            {
                if (!place.HasACounter) continue;

                var line = Village.World.CounterAt(place.Id);
                Assert.That(line, Is.Not.Null, $"'{place.Name}' has a counter but nowhere to queue");
                Assert.That(line.Count, Is.GreaterThanOrEqualTo(2),
                    $"'{place.Name}' has room for only {line.Count} in the queue");

                for (int i = 1; i < line.Count; i++)
                    Assert.That(Tile.ManhattanDistance(line.Slots[i - 1], line.Slots[i]), Is.EqualTo(1),
                        $"'{place.Name}': positions {i - 1} and {i} are not next to each other");
            }
        }

        [Test]
        public void NowhereWithoutACounterHasALine()
        {
            foreach (var place in Village.World.AllPlaces)
                if (!place.HasACounter)
                    Assert.That(Village.World.CounterAt(place.Id), Is.Null,
                        $"'{place.Name}' ({place.Kind}) is not somewhere you queue");
        }
    }

    [TestFixture]
    public class QueueTests
    {
        /// <summary>06:00 to 13:00 — they wake at seven and the errands land through the morning.</summary>
        private const int StartHour = 6;
        private const int Hours = 7;

        [Test]
        public void ArrivalsFormOneLineInArrivalOrderAndItDrains()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, StartHour * 60);
            var shop = Queueham.Shop;
            var line = Queueham.World.CounterAt(shop);

            // When each person last turned up at the shop, so "in arrival order" is checkable
            // rather than assumed.
            var arrived = new long[sim.AgentCount];
            for (int i = 0; i < arrived.Length; i++) arrived[i] = -1;

            int longestLine = 0;
            bool everQueued = false, drainedAfterAQueue = false;

            for (int tick = 0; tick < Hours * 60 * GameClock.TicksPerMinute; tick++)
            {
                sim.Tick();

                var holders = new SortedDictionary<int, int>();

                for (int i = 0; i < sim.AgentCount; i++)
                {
                    var a = sim.GetAgent(i);

                    if (a.At != shop) { arrived[i] = -1; continue; }
                    if (arrived[i] < 0) arrived[i] = sim.Clock.Tick;

                    if (a.QueueSlot < 0) continue;

                    Assert.That(holders.ContainsKey(a.QueueSlot), Is.False,
                        $"agents {(holders.TryGetValue(a.QueueSlot, out int clash) ? clash : -1)} "
                      + $"and {i} are both {a.QueueSlot} in the queue at {sim.Clock}");
                    holders[a.QueueSlot] = i;
                }

                // Nobody may be ahead of somebody who got here first. This holds past the end of
                // the standing positions too: a full shop still serves people in the order they
                // came through the door.
                int last = -1;
                foreach (var pair in holders)
                {
                    if (last >= 0)
                        Assert.That(arrived[pair.Value], Is.GreaterThanOrEqualTo(arrived[last]),
                            $"agent {pair.Value} is behind agent {last} but arrived first, at {sim.Clock}");
                    last = pair.Value;
                }

                int waiting = holders.Count;
                if (waiting > longestLine) longestLine = waiting;
                if (waiting >= 3) everQueued = true;
                if (everQueued && waiting == 0) drainedAfterAQueue = true;
            }

            Assert.That(everQueued, Is.True,
                $"the longest queue all morning was {longestLine} — the fixture is not making one");
            Assert.That(drainedAfterAQueue, Is.True,
                "the queue formed and never emptied — nobody is being served");
        }

        [Test]
        public void TheLineIsALineRatherThanAHeap()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, StartHour * 60);
            var shop = Queueham.Shop;
            var line = Queueham.World.CounterAt(shop);

            int standing = 0;

            for (int tick = 0; tick < Hours * 60 * GameClock.TicksPerMinute; tick++)
            {
                sim.Tick();

                var occupied = new Dictionary<Tile, int>();
                for (int i = 0; i < sim.AgentCount; i++)
                {
                    var a = sim.GetAgent(i);
                    if (a.At != shop || a.QueueSlot < 0) continue;

                    var tile = a.Position.ToTile();
                    int at = line.IndexOf(tile);
                    if (at < 0) continue;   // still in the doorway, waiting to get on the line

                    // They may be short of the position they hold — the person in front has not
                    // moved up yet — but never past it.
                    Assert.That(at, Is.GreaterThanOrEqualTo(a.QueueSlot),
                        $"agent {i} holds position {a.QueueSlot} but is standing at position {at}");

                    Assert.That(occupied.ContainsKey(tile), Is.False,
                        $"agents {(occupied.TryGetValue(tile, out int other) ? other : -1)} and {i} "
                      + $"are stacked on {tile} at {sim.Clock}");
                    occupied[tile] = i;
                    standing++;
                }
            }

            Assert.That(standing, Is.GreaterThan(0), "nobody ever stood in the queue");
        }

        [Test]
        public void TheShopkeeperDoesNotQueueForHerOwnCounter()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, StartHour * 60);
            var line = Queueham.World.CounterAt(Queueham.Shop);

            int minutesBehindTheCounter = 0;

            for (int minute = 0; minute < Hours * 60; minute++)
            {
                sim.Tick(GameClock.TicksPerMinute);

                var keeper = sim.GetAgent(Queueham.Shopkeeper);
                Assert.That(keeper.QueueSlot, Is.EqualTo(-1),
                    $"the shopkeeper is standing at position {keeper.QueueSlot} in her own queue");

                if (keeper.At != Queueham.Shop) continue;
                minutesBehindTheCounter++;

                Assert.That(line.Claims(keeper.Position.ToTile()), Is.False,
                    "the shopkeeper is standing in the queue's way");
            }

            Assert.That(minutesBehindTheCounter, Is.GreaterThan(60),
                "the shopkeeper never turned up for work, so nothing was tested");
        }

        [Test]
        public void TheSameSeedPutsTheSamePeopleInTheSamePositions()
        {
            var a = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, StartHour * 60);
            var b = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, StartHour * 60);

            for (int minute = 0; minute < 5 * 60; minute++)
            {
                a.Tick(GameClock.TicksPerMinute);
                b.Tick(GameClock.TicksPerMinute);

                for (int i = 0; i < a.AgentCount; i++)
                {
                    Assert.That(b.GetAgent(i).QueueSlot, Is.EqualTo(a.GetAgent(i).QueueSlot),
                        $"agent {i} is in a different place in the queue at {a.Clock}");
                    Assert.That(b.GetAgent(i).Position.X,
                        Is.EqualTo(a.GetAgent(i).Position.X).Within(0.0001f), $"agent {i} X");
                }
            }
        }
    }

    /// <summary>
    /// Everybody in Queueham walks out of their own front door and in through the shop's, twice
    /// or more a day, so there are thresholds being crossed all morning by construction.
    /// </summary>
    [TestFixture]
    public class DoorwayTests
    {
        private const int StartHour = 6;
        private const int Minutes = 240;

        /// <summary>Six to eleven ticks, which is 0.30 s to 0.55 s at 20 Hz.</summary>
        private const int Shortest = 6;
        private const int Longest = 11;

        [Test]
        public void PeopleStopOnTheThresholdAndOnlyThere()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, StartHour * 60);
            var grid = Queueham.World.Grid;

            var wasTile = new Tile[sim.AgentCount];
            var wasPaused = new int[sim.AgentCount];
            for (int i = 0; i < sim.AgentCount; i++) wasTile[i] = sim.GetAgent(i).Position.ToTile();

            int pauses = 0;

            for (int tick = 0; tick < Minutes * GameClock.TicksPerMinute; tick++)
            {
                sim.Tick();

                for (int i = 0; i < sim.AgentCount; i++)
                {
                    var a = sim.GetAgent(i);
                    var tile = a.Position.ToTile();

                    if (wasPaused[i] == 0 && a.DoorPauseTicks > 0)
                    {
                        // Roughly four-tenths of a second: long enough to read as a door being
                        // opened, short enough not to read as a stutter.
                        Assert.That(a.DoorPauseTicks, Is.InRange(Shortest, Longest),
                            $"agent {i} paused for {a.DoorPauseTicks} ticks");

                        // The stop is on the near side of the threshold, so the tile they are
                        // standing on must have a neighbour on the other side of a wall.
                        bool insideHere = (grid.FlagsAt(tile) & TileFlags.Indoor) != 0;
                        bool onAThreshold = false;
                        foreach (var step in new[]
                        {
                            new Tile(tile.X + 1, tile.Y), new Tile(tile.X - 1, tile.Y),
                            new Tile(tile.X, tile.Y + 1), new Tile(tile.X, tile.Y - 1),
                        })
                        {
                            if (!grid.IsWalkable(step)) continue;
                            if (((grid.FlagsAt(step) & TileFlags.Indoor) != 0) != insideHere)
                                onAThreshold = true;
                        }

                        Assert.That(onAThreshold, Is.True,
                            $"agent {i} stopped at {tile}, which is nowhere near a doorway");
                        Assert.That(a.Heading.X, Is.EqualTo(0f), $"agent {i} is still striding");
                        Assert.That(a.Heading.Y, Is.EqualTo(0f), $"agent {i} is still striding");
                        pauses++;
                    }

                    wasPaused[i] = a.DoorPauseTicks;
                    wasTile[i] = tile;
                }
            }

            Assert.That(pauses, Is.GreaterThan(20),
                $"only {pauses} doorway pauses in four hours — nobody is opening any doors");
        }

        [Test]
        public void NobodyMovesWhilePausedInADoorway()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, StartHour * 60);

            var held = new Vec2[sim.AgentCount];
            var wasPaused = new int[sim.AgentCount];
            int held_checks = 0;

            for (int tick = 0; tick < Minutes * GameClock.TicksPerMinute; tick++)
            {
                sim.Tick();

                for (int i = 0; i < sim.AgentCount; i++)
                {
                    var a = sim.GetAgent(i);

                    if (a.DoorPauseTicks > 0 && wasPaused[i] == 0)
                    {
                        held[i] = a.Position;
                    }
                    else if (a.DoorPauseTicks > 0)
                    {
                        Assert.That(a.Position, Is.EqualTo(held[i]),
                            $"agent {i} drifted while standing in a doorway");
                        held_checks++;
                    }

                    wasPaused[i] = a.DoorPauseTicks;
                }
            }

            Assert.That(held_checks, Is.GreaterThan(0), "nobody ever paused, so nothing was tested");
        }

        [Test]
        public void APauseNeverStrandsAnybodyMidJourney()
        {
            // The pause defers arrival, which is exactly the sort of thing that quietly leaves
            // somebody standing in a doorway for the rest of the day.
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, StartHour * 60);
            sim.Tick(GameClock.TicksPerMinute * Minutes);

            int travelling = 0;
            for (int i = 0; i < sim.AgentCount; i++)
            {
                Assert.That(sim.GetAgent(i).DoorPauseTicks, Is.LessThanOrEqualTo(Longest),
                    $"agent {i} has been on a threshold for {sim.GetAgent(i).DoorPauseTicks} ticks");
                if (sim.GetAgent(i).Travelling) travelling++;
            }

            Assert.That(travelling, Is.LessThan(sim.AgentCount / 3),
                $"{travelling} of {sim.AgentCount} still walking after four hours");
        }

        [Test]
        public void EverybodyStaysOnWalkableGroundInQueueham()
        {
            var sim = new Simulation(Queueham.World, Queueham.People, Queueham.Seed, StartHour * 60);

            for (int minute = 0; minute < Minutes; minute++)
            {
                sim.Tick(GameClock.TicksPerMinute);
                for (int i = 0; i < sim.AgentCount; i++)
                {
                    var tile = sim.GetAgent(i).Position.ToTile();
                    Assert.That(Queueham.World.Grid.IsWalkable(tile), Is.True,
                        $"agent {i} is standing in a wall at {tile}");
                }
            }
        }
    }
}
