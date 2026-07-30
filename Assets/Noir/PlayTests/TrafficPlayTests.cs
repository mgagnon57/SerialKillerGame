using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Noir.Unity;

namespace Noir.PlayTests
{
    /// <summary>
    /// What the city does while it is running.
    ///
    /// Every other check in this project measures the city standing still: TrafficCheck walks the
    /// lane geometry, PickCheck fires rays at buildings, the renders photograph a frozen frame.
    /// All of them together cannot answer the one question that matters about traffic - DOES IT
    /// STOP - because stopping happens over time and a still has none.
    ///
    /// Time is compressed with Time.timeScale so a 37-second signal cycle can be watched inside a
    /// test rather than inside a coffee break. That is legitimate here: everything under test
    /// reads Time.time or Time.deltaTime and therefore scales with it. It would not be legitimate
    /// if anything counted frames instead, and nothing does.
    /// </summary>
    public class TrafficPlayTests
    {
        private const float Speed = 8f;      // game seconds per real second

        [UnitySetUp]
        public IEnumerator Ready()
        {
            Time.timeScale = Speed;
            yield return CityUnderTest.WaitUntilBuilt();
        }

        [TearDown]
        public void Slow() => Time.timeScale = 1f;

        /// <summary>Watch for this many game seconds, a frame at a time.</summary>
        private static IEnumerator Watch(float gameSeconds, System.Action perFrame)
        {
            float until = Time.time + gameSeconds;
            while (Time.time < until)
            {
                perFrame();
                yield return null;
            }
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator TheCityComesUpAndRuns()
        {
            var host = CityUnderTest.Host;
            Assert.That(host, Is.Not.Null);
            Assert.That(host.LoadError, Is.Null);
            // Not a hardcoded size. The map has been 160x120, 240x240, 360x360 and 960x960 in
            // one day, and a test that pins the number fails every time the city grows without
            // ever having found a fault.
            Assert.That(host.World.Width, Is.GreaterThan(100));
            Assert.That(host.World.Height, Is.EqualTo(host.World.Width), "the map is square");
            Assert.That(host.World.Roads.Junctions.Count, Is.GreaterThan(0), "no junctions");
            Assert.That(host.World.Households, Is.GreaterThan(host.World.Homes.Count),
                        "homes hold more than one household each");

            // THE SIMULATION RUNS ON UNSCALED TIME, deliberately: VillageHost advances it by
            // Time.unscaledDeltaTime times its own speed setting, so that how fast the day goes
            // is a property of the game and not of Unity's timescale. Which means the trick this
            // fixture uses to compress a signal cycle speeds up the traffic and the lights and
            // does nothing at all to the clock - so this has to wait in real seconds.
            int wasMinute = host.Sim.Clock.MinuteOfDay;
            float until = Time.unscaledTime + 10f;
            while (Time.unscaledTime < until) yield return null;

            Assert.That(host.Sim.Clock.MinuteOfDay, Is.Not.EqualTo(wasMinute),
                        "the clock did not advance - the simulation is not running");
            Assert.That(CityUnderTest.Vehicles().Count, Is.GreaterThan(0), "no vehicles");
        }

        /// <summary>
        /// The one property that makes signals worth having: the two flows are never released at
        /// the same moment. Everything else about crossing a junction rests on this.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator NoJunctionEverShowsGreenBothWays()
        {
            var signals = CityUnderTest.Signals;
            Assert.That(signals.Count, Is.GreaterThan(0));

            var seen = new HashSet<string>();
            int both = 0;

            // Two whole cycles, so every phase is observed at every junction.
            yield return Watch(80f, () =>
            {
                for (int j = 0; j < signals.Count; j++)
                {
                    var ns = signals.State(j, true);
                    var ew = signals.State(j, false);

                    if (ns == CitySignals.Light.Green && ew == CitySignals.Light.Green) both++;
                    seen.Add($"{j}:{ns}");
                    seen.Add($"{j}:{ew}");
                }
            });

            Assert.That(both, Is.Zero, "a junction showed green on both axes at once");

            // And they are not simply stuck: every colour is reached on both axes.
            for (int j = 0; j < signals.Count; j++)
            foreach (var light in new[] { CitySignals.Light.Red, CitySignals.Light.Amber,
                                          CitySignals.Light.Green })
                Assert.That(seen, Does.Contain($"{j}:{light}"),
                            $"junction {j} never showed {light} - the cycle is stuck");
        }

        /// <summary>
        /// Nobody leaves the tarmac, at any point in the manoeuvre.
        ///
        /// TrafficCheck proves the LANES are on asphalt. This proves the CARS are - which is a
        /// different claim, because a car spends part of its life on a bezier through a junction
        /// and none of that curve is a lane.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator NoVehicleEverLeavesTheRoad()
        {
            float worst = 0f;
            string where = "";
            int samples = 0;

            yield return Watch(60f, () =>
            {
                foreach (var car in CityUnderTest.Vehicles())
                {
                    if (car == null) continue;
                    samples++;

                    // A metre and a half of slack: the lane centre is what is measured, and a
                    // car has width and a turning arc of its own.
                    if (CityUnderTest.OnTheRoad(car.position, 1.5f, out float over)) continue;
                    if (over > worst)
                    {
                        worst = over;
                        where = $"{car.name} at {car.position} was {over:0.00}m past the asphalt";
                    }
                }
            });

            Assert.That(samples, Is.GreaterThan(0), "nothing was sampled");
            Assert.That(worst, Is.Zero, where);
        }

        /// <summary>Cars are solid. They may queue nose to tail; they may not share a bumper.</summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator NoTwoVehiclesOccupyTheSameSpace()
        {
            float closest = float.MaxValue;
            string where = "";

            yield return Watch(60f, () =>
            {
                var cars = CityUnderTest.Vehicles();
                for (int a = 0; a < cars.Count; a++)
                for (int b = a + 1; b < cars.Count; b++)
                {
                    if (cars[a] == null || cars[b] == null) continue;
                    float gap = Vector3.Distance(cars[a].position, cars[b].position);
                    if (gap < closest)
                    {
                        closest = gap;
                        where = $"{cars[a].name} and {cars[b].name} came within {gap:0.00}m";
                    }
                }
            });

            Assert.That(closest, Is.GreaterThan(2.0f), where);
        }

        /// <summary>
        /// THE TEST THIS WHOLE RIG EXISTS FOR: traffic both moves and stops.
        ///
        /// Either half alone is worthless. A city where nothing moves passes "nobody crashed";
        /// a city where nothing stops passes "everything moves". Both together, with the stop
        /// happening at a stop line on a red, is the behaviour a still can never show.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator TrafficMovesAndStopsAtRedLights()
        {
            var signals = CityUnderTest.Signals;
            var world = CityUnderTest.World;

            var previous = new Dictionary<int, Vector3>();
            float travelled = 0f;
            int stoppedAtARed = 0;

            yield return Watch(90f, () =>
            {
                foreach (var car in CityUnderTest.Vehicles())
                {
                    if (car == null) continue;
                    int id = car.GetInstanceID();
                    var now = car.position;

                    if (previous.TryGetValue(id, out var was))
                    {
                        float moved = Vector3.Distance(was, now);
                        travelled += moved;

                        // Standing still, and standing still just short of a junction whose
                        // signal is against it. Waiting at the line, in other words.
                        if (moved < 0.02f)
                        {
                            bool northSouth = CityUnderTest.IsNorthSouth(car);
                            for (int j = 0; j < signals.Count; j++)
                            {
                                var at = signals.Where(j);
                                float reach = signals.Reach(j);
                                float dx = Mathf.Abs(now.x - at.x);
                                float dy = Mathf.Abs(-now.z - at.y);

                                // Just outside the crossing, lined up with it.
                                bool waiting = northSouth
                                    ? dx < reach && dy > reach && dy < reach + 12f
                                    : dy < reach && dx > reach && dx < reach + 12f;

                                if (waiting && signals.State(j, northSouth) != CitySignals.Light.Green)
                                { stoppedAtARed++; break; }
                            }
                        }
                    }
                    previous[id] = now;
                }
            });

            Assert.That(world, Is.Not.Null);
            Assert.That(travelled, Is.GreaterThan(100f),
                        "the traffic barely moved - it may be deadlocked");
            Assert.That(stoppedAtARed, Is.GreaterThan(0),
                        "no vehicle was ever seen waiting at a red light, so either the signals "
                      + "are not being obeyed or no car reached a junction in ninety seconds");
        }
    }
}
