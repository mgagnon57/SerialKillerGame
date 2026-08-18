using System;
using System.Collections.Generic;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Witness;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The third genuine history. Same shape as HitEvents and AskEvents and for the same
    /// reason: forward-only, no identity, two crashes in one minute are both real.
    /// </summary>
    [TestFixture]
    public class CrashEventsTests
    {
        [Test]
        public void TwoCrashesInOneMinuteAreBothKept()
        {
            var events = new CrashEvents();
            events.Record(100, new Tile(5, 5), CarTone.Dark, CarShape.Van);
            events.Record(100, new Tile(6, 5), CarTone.Dark, CarShape.Van);
            Assert.That(events.Count, Is.EqualTo(2));
        }

        [Test]
        public void TimeOnlyRunsForwards()
        {
            var events = new CrashEvents();
            events.Record(100, new Tile(5, 5), CarTone.Mid, CarShape.Van);
            Assert.Throws<ArgumentException>(() =>
                events.Record(99, new Tile(5, 5), CarTone.Mid, CarShape.Van));
        }

        [Test]
        public void ForEachReplaysInOrder()
        {
            var events = new CrashEvents();
            events.Record(10, new Tile(1, 1), CarTone.Dark, CarShape.Car);
            events.Record(20, new Tile(2, 2), CarTone.Light, CarShape.Pickup);
            int seen = 0, lastMinute = -1;
            events.ForEach((minute, where, tone, shape) =>
            {
                Assert.That(minute, Is.GreaterThanOrEqualTo(lastMinute));
                lastMinute = minute; seen++;
            });
            Assert.That(seen, Is.EqualTo(2));
        }
    }
}
