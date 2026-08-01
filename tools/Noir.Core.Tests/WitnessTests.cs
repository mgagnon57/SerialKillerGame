using System;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Witness;

namespace Noir.Core.Tests
{
    [TestFixture]
    public class WitnessTests
    {
        [Test]
        public void ATrackRemembersWhereABodyWasEachMinute()
        {
            var track = new PlayerTrack();
            track.Record(100, new Tile(10, 20), Visibly.Carrying);
            track.Record(101, new Tile(11, 20), Visibly.Carrying | Visibly.Quickly);

            Assert.That(track.Count, Is.EqualTo(2));
            Assert.That(track.FirstMinute, Is.EqualTo(100));
            Assert.That(track.LastMinute, Is.EqualTo(101));

            Assert.That(track.TryGet(100, out Step first), Is.True);
            Assert.That(first.Where, Is.EqualTo(new Tile(10, 20)));
            Assert.That(first.Looked, Is.EqualTo(Visibly.Carrying));

            Assert.That(track.TryGet(999, out _), Is.False);
        }

        [Test]
        public void ATrackRunsForwardsOnly()
        {
            var track = new PlayerTrack();
            track.Record(100, new Tile(10, 20), Visibly.Nothing);

            var ex = Assert.Throws<ArgumentException>(
                () => track.Record(99, new Tile(10, 20), Visibly.Nothing));
            Assert.That(ex.Message, Does.Contain("forwards"));
        }
    }
}
