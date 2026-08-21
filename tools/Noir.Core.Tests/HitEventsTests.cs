using System;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Witness;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The second genuine history. PlayerTrack's header bans folding events into the track,
    /// so hits get their own forward-only store — a list, not a minute-keyed dictionary,
    /// because two hits in one minute are both real.
    /// </summary>
    [TestFixture]
    public class HitEventsTests
    {
        [Test]
        public void TwoEventsInOneMinuteAreBothKept()
        {
            var events = new HitEvents();
            events.Record(100, new Tile(5, 5), CarTone.Dark, CarShape.Car);
            events.Record(100, new Tile(6, 5), CarTone.Dark, CarShape.Car);
            Assert.That(events.Count, Is.EqualTo(2));
        }

        [Test]
        public void TimeOnlyRunsForwards()
        {
            var events = new HitEvents();
            events.Record(100, new Tile(5, 5), CarTone.Mid, CarShape.Van);
            Assert.Throws<ArgumentException>(() =>
                events.Record(99, new Tile(5, 5), CarTone.Mid, CarShape.Van));
        }

        [Test]
        public void ForEachReplaysInOrder()
        {
            var events = new HitEvents();
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
