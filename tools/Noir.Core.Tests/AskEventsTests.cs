using System;
using System.Collections.Generic;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Witness;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The history of being questioned. Forward-only, no names kept — just the minute
    /// and tile, the same shape as HitEvents and for the same reason.
    /// </summary>
    [TestFixture]
    public class AskEventsTests
    {
        [Test]
        public void TwoAsksInOneMinuteAreBothKept()
        {
            var asks = new AskEvents();
            asks.Record(100, new Tile(5, 5));
            asks.Record(100, new Tile(6, 5));
            Assert.That(asks.Count, Is.EqualTo(2));
        }

        [Test]
        public void TimeOnlyRunsForwards()
        {
            var asks = new AskEvents();
            asks.Record(100, new Tile(5, 5));
            Assert.Throws<ArgumentException>(() => asks.Record(99, new Tile(5, 5)));
        }

        [Test]
        public void ForEachReplaysInOrder()
        {
            var asks = new AskEvents();
            asks.Record(10, new Tile(1, 1));
            asks.Record(20, new Tile(2, 2));
            var minutes = new List<int>();
            asks.ForEach((minute, where) => minutes.Add(minute));
            Assert.That(minutes, Is.EqualTo(new[] { 10, 20 }));
        }
    }
}
