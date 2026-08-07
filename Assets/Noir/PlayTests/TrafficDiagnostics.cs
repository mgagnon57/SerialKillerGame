using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Noir.Unity;

namespace Noir.PlayTests
{
    /// <summary>
    /// WHY THE TRAFFIC JAMS, measured rather than argued about.
    ///
    /// These REPORT. They do not fail the build, in the same way `ratio`, `street` and
    /// `elevations` grade the village without gating it - the numbers here are evidence for a
    /// design decision, and a number that fails a build is a number somebody will tune until it
    /// passes. `TrafficPlayTests` holds the gates.
    ///
    /// EVERYTHING IS MEASURED FROM OUTSIDE, through the same black-box helper the other tests
    /// use: positions, headings and the signal state, all of which are already public. Nothing
    /// here reaches into CityTraffic's private state, which is deliberate - the fault under
    /// investigation is a rule that stops cars moving, and an instrument wired into the rule it
    /// is measuring is an instrument that can be wrong in the same direction as the bug.
    /// </summary>
    public class TrafficDiagnostics
    {
        private const float Speed = 8f;             // game seconds per real second

        /// <summary>Below this, a car is standing still rather than crawling.</summary>
        private const float Still = 0.02f;

        /// <summary>
        /// How far ahead to look for the car in front.
        ///
        /// A stationary car with somebody close in front of it is QUEUING, which is the system
        /// working. A stationary car with clear road ahead is being HELD by a rule, and that is
        /// the thing under investigation. Without this split every jam reads as one long jam and
        /// the head of it - the only car whose behaviour is a decision - is lost in the tail.
        /// </summary>
        private const float QueueAhead = 12f;

        [UnitySetUp]
        public IEnumerator Ready()
        {
            Time.timeScale = Speed;
            yield return CityUnderTest.WaitUntilBuilt();
        }

        [TearDown]
        public void Slow() => Time.timeScale = 1f;

        private sealed class Spell
        {
            public float Held;              // continuous seconds stationary AND at the head
            public float Longest;
            public Vector3 Where;
            public string What = "";
            public bool GreenWhenHeld;      // was its own signal green while it sat there?
            public bool Signalised;
        }

        /// <summary>
        /// THE STARVATION, in seconds.
        ///
        /// A car at the head of its queue, on clear road, not moving, is waiting for a rule to
        /// let it go. If its own light is GREEN while that happens then no signal is holding it
        /// and the only things left are NothingComing (a left turn across the oncoming stream)
        /// and NothingCrossing (giving way at a priority junction). Both wait for zero traffic
        /// inside a fixed distance, and a busy road can hand that gap from one car to the next
        /// with no frame ever clear.
        /// </summary>
        [UnityTest, Category("Diagnostic"), Timeout(900000)]
        public IEnumerator HowLongTheHeadOfAQueueWaits()
        {
            var signals = CityUnderTest.Signals;
            var spells = new Dictionary<int, Spell>();
            var previous = new Dictionary<int, Vector3>();

            float last = Time.time;

            yield return Watch(120f, () =>
            {
                float dt = Time.time - last;
                last = Time.time;
                if (dt <= 0f) return;

                var cars = CityUnderTest.Vehicles();

                foreach (var car in cars)
                {
                    if (car == null) continue;
                    int id = car.GetInstanceID();
                    var now = car.position;

                    if (!previous.TryGetValue(id, out var was)) { previous[id] = now; continue; }
                    previous[id] = now;

                    if (!spells.TryGetValue(id, out var spell)) spells[id] = spell = new Spell();

                    bool moving = Vector3.Distance(was, now) >= Still;
                    bool queued = SomebodyClose(cars, car);

                    if (moving || queued) { spell.Held = 0f; continue; }

                    spell.Held += dt;
                    if (spell.Held <= spell.Longest) continue;

                    spell.Longest = spell.Held;
                    spell.Where = now;
                    spell.What = car.name;

                    // Which junction is it sitting at, and what is that junction telling it?
                    spell.Signalised = false;
                    spell.GreenWhenHeld = false;
                    bool northSouth = CityUnderTest.IsNorthSouth(car);

                    for (int j = 0; j < signals.Count; j++)
                    {
                        var at = signals.Where(j);
                        float reach = signals.Reach(j);
                        float dx = Mathf.Abs(now.x - at.x);
                        float dy = Mathf.Abs(-now.z - at.y);

                        bool waiting = northSouth
                            ? dx < reach && dy > reach && dy < reach + 14f
                            : dy < reach && dx > reach && dx < reach + 14f;
                        if (!waiting) continue;

                        spell.Signalised = signals.IsSignalised(j);
                        spell.GreenWhenHeld = signals.State(j, northSouth) == CitySignals.Light.Green;
                        break;
                    }
                }
            });

            Report(spells);
            Assert.Pass();
        }

        /// <summary>
        /// WHAT IS HOLDING THEM, straight from the rule that made the decision.
        ///
        /// Every other measurement here is taken from outside on purpose, but "why is this car
        /// not moving" cannot be: a car held by a red light and a car held by a give-way rule and
        /// a car stuck behind another car are the same picture from the outside. Two fixes were
        /// written against a guess about which one it was and neither of them worked, which is
        /// what this is for.
        /// </summary>
        [UnityTest, Category("Diagnostic"), Timeout(900000)]
        public IEnumerator WhatIsHoldingTheTraffic()
        {
            var traffic = CityUnderTest.Traffic;
            Assert.That(traffic, Is.Not.Null);

            for (int sample = 0; sample < 8; sample++)
            {
                float until = Time.time + 15f;
                while (Time.time < until) yield return null;

                Debug.Log($"[held] at {sample * 15 + 15}s: {traffic.Holds()}");
            }

            Assert.Pass();
        }

        private static void Report(Dictionary<int, Spell> spells)
        {
            var waits = new List<float>();
            int onGreen = 0, unsignalised = 0;
            Spell worst = null;

            foreach (var spell in spells.Values)
            {
                if (spell.Longest <= 0.5f) continue;
                waits.Add(spell.Longest);
                if (spell.Signalised && spell.GreenWhenHeld) onGreen++;
                if (!spell.Signalised) unsignalised++;
                if (worst == null || spell.Longest > worst.Longest) worst = spell;
            }

            waits.Sort();

            if (waits.Count == 0)
            {
                Debug.Log("[jam] nothing waited at the head of a queue at all.");
                return;
            }

            Debug.Log($"[jam] {waits.Count} vehicles were held at the head of a clear queue over "
                    + $"120 game seconds.\n"
                    + $"[jam]   median {waits[waits.Count / 2]:0.0}s, "
                    + $"p90 {waits[Mathf.Min(waits.Count - 1, waits.Count * 9 / 10)]:0.0}s, "
                    + $"worst {waits[waits.Count - 1]:0.0}s\n"
                    + $"[jam]   {onGreen} of them were held at a junction showing them GREEN, "
                    + $"which no signal explains\n"
                    + $"[jam]   {unsignalised} were at a priority junction (give-way)\n"
                    + $"[jam]   worst: {worst.What} held {worst.Longest:0.0}s at {worst.Where}");
        }

        /// <summary>
        /// WHAT ACTUALLY KEEPS CROSSING PATHS APART, and how close they come.
        ///
        /// `Blocked()` is a FOLLOWING model: it projects a 2.4m-wide box straight down the car's
        /// own heading. Two cars on crossing paths are not in front of each other until they are
        /// nearly on top of each other, so that box cannot separate them and is not what keeps
        /// them apart. The signals do, and the give-way rule does.
        ///
        /// That matters for the fix rather than as a curiosity: a change that lets a car pull out
        /// on a computed gap instead of an empty road is removing the ONLY thing separating those
        /// streams, and this measures what is left underneath. If crossing pairs never come close
        /// today, that is not the follow model working - it is the rule that is about to be
        /// relaxed doing all of it.
        /// </summary>
        [UnityTest, Category("Diagnostic"), Timeout(900000)]
        public IEnumerator HowCloseCrossingPathsCome()
        {
            var world = CityUnderTest.World;

            float closestCrossing = float.MaxValue, closestParallel = float.MaxValue;
            string crossingWhere = "nothing crossed", parallelWhere = "";
            int insideTogether = 0, samples = 0;

            yield return Watch(120f, () =>
            {
                var cars = CityUnderTest.Vehicles();

                for (int a = 0; a < cars.Count; a++)
                for (int b = a + 1; b < cars.Count; b++)
                {
                    if (cars[a] == null || cars[b] == null) continue;
                    samples++;

                    var pa = cars[a].position;
                    var pb = cars[b].position;
                    float gap = Vector3.Distance(pa, pb);

                    // The same test Blocked() applies: roughly the same way, or not.
                    bool parallel = Vector3.Dot(cars[a].forward, cars[b].forward) >= 0.7f;

                    if (parallel)
                    {
                        if (gap < closestParallel)
                        {
                            closestParallel = gap;
                            parallelWhere = $"{cars[a].name} and {cars[b].name} at {gap:0.00}m";
                        }
                        continue;
                    }

                    if (gap < closestCrossing)
                    {
                        closestCrossing = gap;
                        crossingWhere = $"{cars[a].name} and {cars[b].name} at {gap:0.00}m "
                                      + $"near {pa}";
                    }

                    // Both inside the same junction box at the same moment, on crossing paths.
                    if (gap < 40f && InSameJunction(world, pa, pb)) insideTogether++;
                }
            });

            Debug.Log($"[cross] over 120 game seconds, {samples} pair-frames sampled.\n"
                    + $"[cross]   closest CROSSING pair:  {closestCrossing:0.00}m - {crossingWhere}\n"
                    + $"[cross]   closest PARALLEL pair:  {closestParallel:0.00}m - {parallelWhere}\n"
                    + $"[cross]   crossing pairs inside one junction together: {insideTogether} "
                    + $"frames");
            Assert.Pass();
        }

        private static bool InSameJunction(Noir.Core.World.WorldModel world, Vector3 a, Vector3 b)
        {
            if (world == null) return false;

            foreach (var junction in world.Roads.Junctions)
            {
                bool inA = Mathf.Abs(a.x - junction.X) <= junction.Reach &&
                           Mathf.Abs(-a.z - junction.Y) <= junction.Reach;
                if (!inA) continue;

                bool inB = Mathf.Abs(b.x - junction.X) <= junction.Reach &&
                           Mathf.Abs(-b.z - junction.Y) <= junction.Reach;
                if (inB) return true;
            }
            return false;
        }

        /// <summary>Is there a vehicle close in front of this one, going roughly its way?</summary>
        private static bool SomebodyClose(List<Transform> cars, Transform me)
        {
            var here = me.position;
            var forward = me.forward;

            foreach (var other in cars)
            {
                if (other == null || other == me) continue;
                if (Vector3.Dot(forward, other.forward) < 0.7f) continue;

                var gap = other.position - here;
                float ahead = Vector3.Dot(gap, forward);
                if (ahead <= 0f || ahead > QueueAhead) continue;
                if (Vector3.Cross(forward, gap).magnitude > 2.4f) continue;

                return true;
            }
            return false;
        }

        private static IEnumerator Watch(float gameSeconds, System.Action perFrame)
        {
            float until = Time.time + gameSeconds;
            while (Time.time < until)
            {
                perFrame();
                yield return null;
            }
        }
    }
}
