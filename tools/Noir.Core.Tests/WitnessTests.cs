using System;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Observation;
using Noir.Core.People;
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

        private static Citizen Villager(byte sociability, Beat beats = Beat.None) =>
            new Citizen(new CitizenId(7), "Ada", "Reed", 44, LifeStage.Adult, Occupation.Shopkeeper,
                        new HouseholdId(3), new PlaceId(1), new PlaceId(2), 0,
                        0, 128, sociability, new int[0], beats, male: false);

        [Test]
        public void CloseAndInDaylightIsAClearLook()
        {
            var look = Sightlines.HowGoodALook(new Tile(0, 0), new Tile(5, 0), 12 * 60,
                                               Villager(128));
            Assert.That(look, Is.EqualTo(SightingClarity.Clear));
        }

        [Test]
        public void TheSameLookAtNightIsWorse()
        {
            var look = Sightlines.HowGoodALook(new Tile(0, 0), new Tile(5, 0), 2 * 60,
                                               Villager(128));
            Assert.That(look, Is.EqualTo(SightingClarity.Partial));
        }

        [Test]
        public void BeyondSixtyTilesNobodySeesAnything()
        {
            var watcher = new Tile(0, 0);
            var subject = new Tile(61, 0);
            var look = Sightlines.HowGoodALook(watcher, subject, 12 * 60, Villager(128));
            Assert.That(Sightlines.SawAnythingAtAll(look, watcher, subject), Is.False);
        }

        [Test]
        public void TheManWhoLingersSeesMoreThanTheManWhoDoesNot()
        {
            var watcher = new Tile(0, 0);
            var subject = new Tile(20, 0);   // the Partial band in daylight

            var ordinary = Sightlines.HowGoodALook(watcher, subject, 12 * 60, Villager(128));
            var lingerer = Sightlines.HowGoodALook(watcher, subject, 12 * 60,
                                                   Villager(128, Beat.Lingers));

            Assert.That(ordinary, Is.EqualTo(SightingClarity.Partial));
            Assert.That(lingerer, Is.EqualTo(SightingClarity.Clear));
        }

        [Test]
        public void SomebodyWhoKeepsHisHeadDownSeesLess()
        {
            var watcher = new Tile(0, 0);
            var subject = new Tile(20, 0);

            var withdrawn = Sightlines.HowGoodALook(watcher, subject, 12 * 60, Villager(32));
            Assert.That(withdrawn, Is.EqualTo(SightingClarity.Glimpsed));
        }

        [Test]
        public void AtNightBeyondThirtyTilesThereIsStillAGlimpse()
        {
            // The plan's table says this cell is nothing at all; the arithmetic says Glimpsed,
            // because range alone decides whether there was a statement and range does not know
            // about light. The behaviour is pinned here rather than corrected because which of
            // the two is right is a question about how much thin night testimony the town should
            // produce, and the statement census answers that with numbers. If the census shows a
            // flood of night glimpses, change Sightlines.SawAnythingAtAll to take the minute and
            // shorten the range after dark — and change this test with it, deliberately.
            var watcher = new Tile(0, 0);
            var subject = new Tile(45, 0);

            var look = Sightlines.HowGoodALook(watcher, subject, 2 * 60, Villager(128));

            Assert.That(look, Is.EqualTo(SightingClarity.Glimpsed));
            Assert.That(Sightlines.SawAnythingAtAll(look, watcher, subject), Is.True);
        }
    }
}
