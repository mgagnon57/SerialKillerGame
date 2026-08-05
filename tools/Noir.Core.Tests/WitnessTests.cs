using System;
using System.Collections.Generic;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Observation;
using Noir.Core.People;
using Noir.Core.Witness;
using Noir.Sim;
using Noir.Core.World;

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
            new Citizen(new CitizenId(7), "Ada", "Reed", GameClock.EpochYear - 44, Occupation.Shopkeeper,
                        new HouseholdId(3), new PlaceId(1), new PlaceId(2), 0,
                        0, 128, sociability, new int[0], beats, male: false);

        /// <summary>
        /// The clock at a given calendar date and minute of the day.
        ///
        /// Found by walking forward from the epoch rather than by arithmetic, so these tests do
        /// not have to know how GameClock maps a day number onto a date. They only have to be
        /// able to say WHICH SEASON they mean, which is the entire reason Sightlines takes a
        /// clock instead of a minute now.
        ///
        /// minuteOfDay is local standard time, matching both GameClock and Daylight.At.
        /// </summary>
        private static GameClock On(int month, int dayOfMonth, int minuteOfDay)
        {
            for (int day = 0; day < 800; day++)
            {
                var clock = new GameClock(GameClock.TickAt(day, minuteOfDay));
                if (clock.Month == month && clock.DayOfMonth == dayOfMonth) return clock;
            }
            throw new System.ArgumentException(
                $"no {month}/{dayOfMonth} within two years of the epoch");
        }

        [Test]
        public void CloseAndInDaylightIsAClearLook()
        {
            var look = Sightlines.HowGoodALook(new Tile(0, 0), new Tile(5, 0),
                                               On(6, 21, 12 * 60), Villager(128));
            Assert.That(look, Is.EqualTo(SightingClarity.Clear));
        }

        [Test]
        public void TheSameLookAtNightIsWorse()
        {
            var look = Sightlines.HowGoodALook(new Tile(0, 0), new Tile(5, 0),
                                               On(6, 21, 2 * 60), Villager(128));
            Assert.That(look, Is.EqualTo(SightingClarity.Partial));
        }

        /// <summary>
        /// THE POINT OF GIVING SIGHTLINES A DATE. Half past six in the evening is broad daylight
        /// in June and long dark in December at this latitude - sunset moves nearly four hours
        /// between the solstices - so the same walk past the same gate at the same hour is
        /// witnessed in one season and unwitnessed in the other.
        ///
        /// That sentence is the argument of WHO-SEES-WHOM.md, and the three-band constant this
        /// replaced could not express any of it: it called 18:30 daylight all year round.
        /// </summary>
        [Test]
        public void TheSameEveningWalkIsSeenInJuneAndNotInDecember()
        {
            var watcher = new Tile(0, 0);
            var subject = new Tile(5, 0);
            const int HalfPastSix = 18 * 60 + 30;

            var june = Sightlines.HowGoodALook(watcher, subject,
                                               On(6, 21, HalfPastSix), Villager(128));
            var december = Sightlines.HowGoodALook(watcher, subject,
                                                   On(12, 21, HalfPastSix), Villager(128));

            Assert.That(june, Is.EqualTo(SightingClarity.Clear),
                "the sun is still up over this crossing at half six on midsummer's day");
            Assert.That(december, Is.EqualTo(SightingClarity.Partial),
                "it set before half four on the shortest day - this is two hours into the dark");
            Assert.That(june, Is.Not.EqualTo(december),
                "if these are equal the date is being ignored and the light model is a constant "
              + "again, which is the whole thing this replaced");
        }

        [Test]
        public void BeyondSixtyTilesNobodySeesAnything()
        {
            var watcher = new Tile(0, 0);
            var subject = new Tile(61, 0);
            var look = Sightlines.HowGoodALook(watcher, subject, On(6, 21, 12 * 60), Villager(128));
            Assert.That(Sightlines.SawAnythingAtAll(look, watcher, subject), Is.False);
        }

        [Test]
        public void TheManWhoLingersSeesMoreThanTheManWhoDoesNot()
        {
            var watcher = new Tile(0, 0);
            var subject = new Tile(20, 0);   // the Partial band in daylight

            var ordinary = Sightlines.HowGoodALook(watcher, subject, On(6, 21, 12 * 60), Villager(128));
            var lingerer = Sightlines.HowGoodALook(watcher, subject, On(6, 21, 12 * 60),
                                                   Villager(128, Beat.Lingers));

            Assert.That(ordinary, Is.EqualTo(SightingClarity.Partial));
            Assert.That(lingerer, Is.EqualTo(SightingClarity.Clear));
        }

        [Test]
        public void SomebodyWhoKeepsHisHeadDownSeesLess()
        {
            var watcher = new Tile(0, 0);
            var subject = new Tile(20, 0);

            var withdrawn = Sightlines.HowGoodALook(watcher, subject, On(6, 21, 12 * 60), Villager(32));
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

            var look = Sightlines.HowGoodALook(watcher, subject, On(6, 21, 2 * 60), Villager(128));

            Assert.That(look, Is.EqualTo(SightingClarity.Glimpsed));
            Assert.That(Sightlines.SawAnythingAtAll(look, watcher, subject), Is.True);
        }

        private const ulong TestSeed = 1979UL;

        private static PersonDescription Describe(SightingClarity clarity, CitizenKey witness,
                                                  int minute = 500) =>
            Degradation.WhatRegistered(clarity, Visibly.Carrying, true, 40, 185, 2,
                                       witness, minute, TestSeed);

        [Test]
        public void AGlimpseRegistersAlmostNothing()
        {
            var seen = Describe(SightingClarity.Glimpsed, new CitizenKey(11));
            Assert.That(seen.NoticedCount, Is.EqualTo(1));
        }

        [Test]
        public void AClearLookRegistersMost()
        {
            var seen = Describe(SightingClarity.Clear, new CitizenKey(11));
            Assert.That(seen.NoticedCount, Is.EqualTo(5));
        }

        [Test]
        public void TheSameWitnessRemembersTheSameThingTwice()
        {
            var once = Describe(SightingClarity.Partial, new CitizenKey(11));
            var twice = Describe(SightingClarity.Partial, new CitizenKey(11));
            Assert.That(once, Is.EqualTo(twice));
        }

        [Test]
        public void TwoWitnessesToTheSameMomentRememberDifferently()
        {
            // A single pair of keys proves nothing: two witnesses can agree by chance, and a
            // shuffle keyed on nothing at all would still pass one lucky comparison. What has to
            // be true is that the TOWN disagrees — that a moment seen by many people yields many
            // different accounts, which is the only reason interviewing more than one of them is
            // worth doing.
            var accounts = new HashSet<PersonDescription>();
            for (ulong key = 1; key <= 200; key++)
                accounts.Add(Describe(SightingClarity.Partial, new CitizenKey(key)));

            Assert.That(accounts.Count, Is.GreaterThan(10),
                "Two hundred witnesses to the same moment produced " + accounts.Count +
                " distinct accounts. They are all remembering the same thing, which means the " +
                "shuffle is not keyed on the witness — check that witness.Value reaches " +
                "Rolls.Int as the subject argument rather than a constant.");
        }

        [Test]
        public void AClearLookNeverGetsTheSexWrong()
        {
            for (int minute = 0; minute < 400; minute++)
            {
                var seen = Degradation.WhatRegistered(SightingClarity.Clear, Visibly.Nothing,
                                                      true, 40, 185, 2,
                                                      new CitizenKey(11), minute, TestSeed);
                if (seen.Sex != ApparentSex.Unnoticed)
                    Assert.That(seen.Sex, Is.EqualTo(ApparentSex.Man),
                                "wrong at minute " + minute + ", in a clear look");
            }
        }

        [Test]
        public void AGlimpseSometimesGetsTheSexWrong()
        {
            int wrong = 0;
            for (int minute = 0; minute < 2000; minute++)
            {
                var seen = Degradation.WhatRegistered(SightingClarity.Glimpsed, Visibly.Nothing,
                                                      true, 40, 185, 2,
                                                      new CitizenKey(11), minute, TestSeed);
                if (seen.Sex == ApparentSex.Woman) wrong++;
            }
            Assert.That(wrong, Is.GreaterThan(0),
                "A witness who never mistakes a man for a woman in the dark is not a witness.");
        }

        /// <summary>The authored village, once, so these tests see the same world as the harness.</summary>
        private static VillageContext Village() => VillageContext.Load();

        /// <summary>A track that parks the player on one citizen's doorstep all afternoon.</summary>
        private static PlayerTrack TrackOutside(VillageContext v, Citizen who, int day,
                                                Visibly looked = Visibly.Carrying)
        {
            var plan = DayPlanner.Plan(v.World, v.People, who, day, v.Seed);
            var track = new PlayerTrack();
            for (int m = 0; m < Sighting.MinutesPerDay; m++)
            {
                Tile door = v.World.GetPlace(plan.At(m).Where).Door;
                track.Record(day * Sighting.MinutesPerDay + m, door, looked);
            }
            return track;
        }

        [Test]
        public void AManStoodOnYourDoorstepAllDayIsRemembered()
        {
            var v = Village();
            Citizen who = v.People.Citizens[0];
            var track = TrackOutside(v, who, 3);

            Sighting[] said = Recollection.WhatTheySaw(v.World, v.People, who, 3, track, v.Seed);

            Assert.That(said.Length, Is.GreaterThan(0),
                "Somebody standing on the doorstep for a whole day and being remembered by " +
                "nobody means no sighting is ever produced, and the census will be empty.");
        }

        [Test]
        public void ARunOfMinutesIsOneThingRemembered()
        {
            var v = Village();
            Citizen who = v.People.Citizens[0];
            var track = TrackOutside(v, who, 3);

            Sighting[] said = Recollection.WhatTheySaw(v.World, v.People, who, 3, track, v.Seed);

            Assert.That(said.Length, Is.LessThan(60),
                "A figure in sight for hours produced one sighting a minute. A witness remembers " +
                "a visit, not a frame count.");
        }

        [Test]
        public void TheSameQuestionTwiceGetsTheSameAnswer()
        {
            var v = Village();
            Citizen who = v.People.Citizens[0];
            var track = TrackOutside(v, who, 3);

            Sighting[] once = Recollection.WhatTheySaw(v.World, v.People, who, 3, track, v.Seed);
            Sighting[] twice = Recollection.WhatTheySaw(v.World, v.People, who, 3, track, v.Seed);

            Assert.That(twice.Length, Is.EqualTo(once.Length));
            for (int i = 0; i < once.Length; i++)
            {
                Assert.That(twice[i].Minute, Is.EqualTo(once[i].Minute), "minute at " + i);
                Assert.That(twice[i].Clarity, Is.EqualTo(once[i].Clarity), "clarity at " + i);
                Assert.That(twice[i].Description, Is.EqualTo(once[i].Description), "description at " + i);
                Assert.That(twice[i].Where, Is.EqualTo(once[i].Where), "place at " + i);
            }
        }

        [Test]
        public void TestimonyCannotTellWhyHeWasThere()
        {
            var v = Village();
            Citizen who = v.People.Citizens[0];

            // The same movements, twice. Whatever the player was "doing" is not an input to any
            // of this, so if these two ever differ, something is reading intent.
            var innocent = TrackOutside(v, who, 3, Visibly.Carrying);
            var guilty = TrackOutside(v, who, 3, Visibly.Carrying);

            Sighting[] a = Recollection.WhatTheySaw(v.World, v.People, who, 3, innocent, v.Seed);
            Sighting[] b = Recollection.WhatTheySaw(v.World, v.People, who, 3, guilty, v.Seed);

            Assert.That(b.Length, Is.EqualTo(a.Length));
            for (int i = 0; i < a.Length; i++)
                Assert.That(b[i].Description, Is.EqualTo(a[i].Description), "at " + i);
        }
    }
}
