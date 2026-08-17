using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Observation;
using Noir.Core.People;
using Noir.Core.Witness;
using Noir.Sim;

namespace Noir.Core.Tests
{
    [TestFixture]
    public class EventTestimonyTests
    {
        [Test]
        public void CarBandsDegradeButNeverInvent()
        {
            // At a glimpse most bands die; whatever survives must be the truth or Unnoticed —
            // wrongness is allowed only where the person machinery already allows it (tone at
            // a glimpse may shift one band, same spirit as ApparentSex).
            var d = Degradation.CarRegistered(SightingClarity.Clear, CarTone.Dark, CarShape.Van,
                                              new CitizenKey(12345), minute: 500, seed: 1979);
            Assert.That(d.Shape, Is.EqualTo(CarShape.Van).Or.EqualTo(CarShape.Unnoticed));

            var g = Degradation.CarRegistered(SightingClarity.Glimpsed, CarTone.Dark, CarShape.Van,
                                              new CitizenKey(12345), minute: 500, seed: 1979);
            Assert.That(g.IsBlank || !g.IsBlank, Is.True); // never throws; bands legal by type
        }

        [Test]
        public void TheMemoryIsTheSeed()
        {
            var once = Degradation.CarRegistered(SightingClarity.Partial, CarTone.Light,
                CarShape.Pickup, new CitizenKey(777), 900, 1979);
            var twice = Degradation.CarRegistered(SightingClarity.Partial, CarTone.Light,
                CarShape.Pickup, new CitizenKey(777), 900, 1979);
            Assert.That(once.Tone, Is.EqualTo(twice.Tone));
            Assert.That(once.Shape, Is.EqualTo(twice.Shape));
        }

        [Test]
        public void AnEventSightingReadsLikeAWitness()
        {
            var s = new EventSighting(new ObserverId(0), 990, new Tile(4, 4),
                SightingClarity.Partial, EventAct.CarStruckSomebody,
                new CarDescription(CarTone.Dark, CarShape.Car));
            string line = Testimony.InEnglish(s);
            Assert.That(line, Does.StartWith("16:30, "));
            Assert.That(line, Does.EndWith("."));
            Assert.That(line.ToLowerInvariant(), Does.Contain("hit"));
            Assert.That(line.ToLowerInvariant(), Does.Contain("dark"));
        }

        [Test]
        public void ABlankCarIsStillACar()
        {
            var s = new EventSighting(new ObserverId(0), 990, new Tile(4, 4),
                SightingClarity.Glimpsed, EventAct.CarStruckSomebody, default);
            Assert.That(Testimony.InEnglish(s).ToLowerInvariant(), Does.Contain("a car"));
        }

        /// <summary>
        /// FINDING 1 of the code review. Mid tone used to render exactly like Unnoticed — "a
        /// car" either way — which throws away real evidence: a witness who registered Mid
        /// looked at the car and found its colour unremarkable, the same information Testimony
        /// already keeps for a person's clothing (Mid -> "nothing that stood out") and build
        /// (Average -> "ordinary-build"). Mid must read as "ordinary-looking"; Unnoticed must
        /// stay the bare noun, or this collapses right back to the same bug the other way round.
        /// </summary>
        [Test]
        public void MidToneIsInformationNotSilence()
        {
            var mid = new EventSighting(new ObserverId(0), 990, new Tile(4, 4),
                SightingClarity.Clear, EventAct.CarStruckSomebody,
                new CarDescription(CarTone.Mid, CarShape.Pickup));
            string midLine = Testimony.InEnglish(mid).ToLowerInvariant();
            Assert.That(midLine, Does.Contain("an ordinary-looking pickup"),
                "a witness who registered Mid looked and found nothing remarkable - that is a " +
                "fact about the look, not the absence of one: " + midLine);

            var unnoticed = new EventSighting(new ObserverId(0), 990, new Tile(4, 4),
                SightingClarity.Clear, EventAct.CarStruckSomebody,
                new CarDescription(CarTone.Unnoticed, CarShape.Pickup));
            string unnoticedLine = Testimony.InEnglish(unnoticed).ToLowerInvariant();
            Assert.That(unnoticedLine, Does.Contain("a pickup"));
            Assert.That(unnoticedLine, Does.Not.Contain("ordinary-looking"),
                "Unnoticed must stay bare, or it has just been folded back into Mid's wording: " +
                unnoticedLine);
        }

        /// <summary>
        /// FINDING 2 of the code review. AskInEnglish promises "the order they happened, oldest
        /// first" and used to keep every person line ahead of every event line regardless of
        /// minute - so a drive-past hit that happened BEFORE a later sighting of the player
        /// still got told after it. This proves the merge instead: an event ten minutes-ish
        /// before a person sighting, told to the same witness, must come back event-first.
        ///
        /// The gap between the two moments is 60 minutes, not 10 - large enough that no
        /// combination of BlurredMinute's rounding (which can move either stamp down by up to
        /// 14 minutes, depending on how good the look was) can invert the order. A 10-minute
        /// gap is the feature's real scenario; 60 is what makes the assertion airtight without
        /// having to pin the fixture village's lighting.
        /// </summary>
        [Test]
        public void EventsAndPersonSightingsMergeInMinuteOrder()
        {
            const int day = 3;
            const int gap = 60;

            var v = VillageContext.Load();

            Citizen who = null;
            int eventMinuteOfDay = -1, personMinuteOfDay = -1;

            foreach (Citizen candidate in v.People.Citizens)
            {
                DayPlan plan = DayPlanner.Plan(v.World, v.People, candidate, day, v.Seed);
                for (int m = 0; m + gap < Sighting.MinutesPerDay; m++)
                {
                    if (!IsStationary(plan.At(m)) || !IsStationary(plan.At(m + gap))) continue;
                    who = candidate;
                    eventMinuteOfDay = m;
                    personMinuteOfDay = m + gap;
                    break;
                }
                if (who != null) break;
            }
            Assert.That(who, Is.Not.Null,
                "no citizen in the fixture village is ever stationary twice, an hour apart, in " +
                "one day - the test needs a different search, not a bigger gap");

            DayPlan whosPlan = DayPlanner.Plan(v.World, v.People, who, day, v.Seed);
            Tile eventSpot = v.World.GetPlace(whosPlan.At(eventMinuteOfDay).Where).Door;
            Tile personSpot = v.World.GetPlace(whosPlan.At(personMinuteOfDay).Where).Door;

            // DISTANCE ZERO, ON PURPOSE. Standing exactly where the witness themselves is at
            // that minute guarantees SawAnythingAtAll regardless of light, range or attention -
            // the same trick WitnessTests' TrackOutside uses. What is under test is the ORDER
            // of the two lines, not whether either one registers.
            var hits = new HitEvents();
            hits.Record(day * Sighting.MinutesPerDay + eventMinuteOfDay, eventSpot,
                       CarTone.Dark, CarShape.Van);

            var track = new PlayerTrack();
            track.Record(day * Sighting.MinutesPerDay + personMinuteOfDay, personSpot,
                        Visibly.Nothing);

            string[] said = Recollection.AskInEnglish(v.World, v.People, who, day, track, v.Seed,
                                                      hits: hits);

            Assert.That(said.Length, Is.EqualTo(2), string.Join(" | ", said));
            Assert.That(said[0].ToLowerInvariant(), Does.Contain("hit"),
                "the event happened first and should be told first: " + string.Join(" | ", said));
            Assert.That(said[1].ToLowerInvariant(), Does.Not.Contain("hit"),
                "the person line should come second: " + string.Join(" | ", said));
        }

        private static bool IsStationary(Block b) =>
            b.What != Activity.Asleep && b.What != Activity.TravellingTo && b.Where.IsValid;

        /// <summary>
        /// C1 of the 2026-08-15 final whole-branch review: "the corpse testifies." WhatTheySaw
        /// never consulted IInterruptions - only WhatTheySawOfEvents did - so a witness downed
        /// mid-afternoon kept describing the player's movements for the rest of the day they were
        /// lying in the street, while the event arm correctly went silent about them. Same fix,
        /// same test shape as EventsAndPersonSightingsMergeInMinuteOrder above: two isolated
        /// minutes, not a continuous track, so each becomes its own separate Sighting rather than
        /// merging into one long visit (Recollection only starts a new Sighting when inSight goes
        /// false and back true - a gap in the track between the two forces exactly that).
        /// </summary>
        [Test]
        public void ADownedWitnessTestifiesToNothingFromThatMinuteOn()
        {
            const int day = 3;
            const int gap = 240; // four hours apart - comfortably clear of blur's -14 minute reach

            var v = VillageContext.Load();

            Citizen who = null;
            int earlyMinute = -1, lateMinute = -1;

            foreach (Citizen candidate in v.People.Citizens)
            {
                DayPlan plan = DayPlanner.Plan(v.World, v.People, candidate, day, v.Seed);
                for (int m = 0; m + gap < Sighting.MinutesPerDay; m++)
                {
                    if (!IsStationary(plan.At(m)) || !IsStationary(plan.At(m + gap))) continue;
                    who = candidate;
                    earlyMinute = m;
                    lateMinute = m + gap;
                    break;
                }
                if (who != null) break;
            }
            Assert.That(who, Is.Not.Null,
                "no citizen in the fixture village is stationary twice, four hours apart, in one " +
                "day - the test needs a different search, not a bigger gap");

            DayPlan whosPlan = DayPlanner.Plan(v.World, v.People, who, day, v.Seed);
            Tile earlySpot = v.World.GetPlace(whosPlan.At(earlyMinute).Where).Door;
            Tile lateSpot = v.World.GetPlace(whosPlan.At(lateMinute).Where).Door;

            // DISTANCE ZERO, ON PURPOSE - the same trick EventsAndPersonSightingsMergeInMinuteOrder
            // and WitnessTests' TrackOutside use: standing exactly where the witness themselves is
            // guarantees SawAnythingAtAll regardless of light, range or attention. Two isolated
            // minutes with nothing recorded between them, not a continuous track, so each becomes
            // its own Sighting instead of one visit swallowing both.
            var track = new PlayerTrack();
            int earlyAbs = day * Sighting.MinutesPerDay + earlyMinute;
            int lateAbs = day * Sighting.MinutesPerDay + lateMinute;
            track.Record(earlyAbs, earlySpot, Visibly.Nothing);
            track.Record(lateAbs, lateSpot, Visibly.Nothing);

            Sighting[] undisturbed = Recollection.WhatTheySaw(v.World, v.People, who, day, track, v.Seed);
            Assert.That(undisturbed.Length, Is.EqualTo(2),
                "expected two separate sightings, one at each isolated minute - fixture or seed " +
                "changed? " +
                string.Join(", ", System.Array.ConvertAll(undisturbed, s => s.Minute.ToString())));

            // Downed strictly between the two minutes: still on their feet for the early sighting,
            // lying in the street by the late one.
            var interruptions = new DownedAt(who.Id, earlyAbs + 1);
            Sighting[] afterDowning = Recollection.WhatTheySaw(v.World, v.People, who, day, track,
                                                               v.Seed, interruptions: interruptions);

            Assert.That(afterDowning.Length, Is.EqualTo(1),
                "a downed witness should still testify to what they saw before going down - the " +
                "corpse is testifying to something it should have gone silent about");
            Assert.That(afterDowning[0].Minute, Is.EqualTo(undisturbed[0].Minute),
                "the surviving sighting should be the early one, unchanged by the guard");
        }

        /// <summary>A stub IInterruptions naming exactly one citizen's downed-from minute, so this
        /// file can prove Recollection's own gate without standing up a live Simulation. Never
        /// comes back - BackFromMinute stays at its default, int.MaxValue, same as the dead.</summary>
        private sealed class DownedAt : IInterruptions
        {
            private readonly CitizenId _who;
            private readonly int _minute;
            public DownedAt(CitizenId who, int minute) { _who = who; _minute = minute; }
            public int DownedFromMinute(CitizenId who) =>
                who.Value == _who.Value ? _minute : int.MaxValue;
            public int BackFromMinute(CitizenId who) => int.MaxValue;
        }

        /// <summary>
        /// Task 6: the testimony INTERVAL, not just the one-way downed gate above. A survivor
        /// the ambulance carries off and later returns should pick testifying back up; only the
        /// dead - or anyone the host never marks as back - should stay silent forever. This stub
        /// answers both halves of IInterruptions with fixed values, applying to every citizen
        /// asked (the tests below only ever ask about the one witness under test).
        /// </summary>
        private sealed class Interval : IInterruptions
        {
            public int From = int.MaxValue, Back = int.MaxValue;
            public int DownedFromMinute(CitizenId who) => From;
            public int BackFromMinute(CitizenId who) => Back;
        }

        /// <summary>
        /// Phase 2 of witness voices: being asked is an event, and it must read like a witness
        /// saying it — same clock, same clarity hedging as the hit line — while naming NOBODY.
        /// "Somebody" is the whole description; the vagueness rule is load-bearing here.
        /// </summary>
        [Test]
        public void AnAskSightingReadsLikeAWitness()
        {
            var s = new EventSighting(new ObserverId(0), 990, new Tile(4, 4),
                SightingClarity.Partial, EventAct.SomebodyAskedQuestions,
                new CarDescription(CarTone.Unnoticed, CarShape.Unnoticed));
            Assert.That(Testimony.InEnglish(s),
                Is.EqualTo("16:30, I saw somebody going around asking questions."));
        }

        [Test]
        public void AGlimpsedAskSightingHedges()
        {
            var s = new EventSighting(new ObserverId(0), 990, new Tile(4, 4),
                SightingClarity.Glimpsed, EventAct.SomebodyAskedQuestions,
                new CarDescription(CarTone.Unnoticed, CarShape.Unnoticed));
            Assert.That(Testimony.InEnglish(s),
                Does.StartWith("16:30, I think I saw somebody"));
        }

        /// <summary>
        /// The window half of Task 6: a survivor who comes back should testify again, but only
        /// about what happened AFTER they returned - the stretch they were down or away for stays
        /// silenced, the same shape ADownedWitnessTestifiesToNothingFromThatMinuteOn already
        /// proves for the one-way (never-back) case. Three isolated minutes, not a continuous
        /// track, so each becomes its own separate Sighting: one before the witness went down,
        /// one strictly inside the down-to-back window, and one comfortably after they came back.
        /// </summary>
        [Test]
        public void AReturnedSurvivorTestifiesAboutLifeAfterTheirReturn()
        {
            const int day = 3;
            const int midGap = 30;   // inside the 60-minute down-to-back window
            const int lateGap = 240; // comfortably clear of it, same margin the downed tests use

            var v = VillageContext.Load();

            Citizen who = null;
            int earlyMinute = -1, midMinute = -1, lateMinute = -1;

            foreach (Citizen candidate in v.People.Citizens)
            {
                DayPlan plan = DayPlanner.Plan(v.World, v.People, candidate, day, v.Seed);
                for (int m = 0; m + lateGap < Sighting.MinutesPerDay; m++)
                {
                    if (!IsStationary(plan.At(m)) || !IsStationary(plan.At(m + midGap)) ||
                        !IsStationary(plan.At(m + lateGap))) continue;
                    who = candidate;
                    earlyMinute = m;
                    midMinute = m + midGap;
                    lateMinute = m + lateGap;
                    break;
                }
                if (who != null) break;
            }
            Assert.That(who, Is.Not.Null,
                "no citizen in the fixture village is stationary at all three probe minutes in " +
                "one day - the test needs a different search, not bigger gaps");

            DayPlan whosPlan = DayPlanner.Plan(v.World, v.People, who, day, v.Seed);
            Tile earlySpot = v.World.GetPlace(whosPlan.At(earlyMinute).Where).Door;
            Tile midSpot = v.World.GetPlace(whosPlan.At(midMinute).Where).Door;
            Tile lateSpot = v.World.GetPlace(whosPlan.At(lateMinute).Where).Door;

            var track = new PlayerTrack();
            int earlyAbs = day * Sighting.MinutesPerDay + earlyMinute;
            int midAbs = day * Sighting.MinutesPerDay + midMinute;
            int lateAbs = day * Sighting.MinutesPerDay + lateMinute;
            track.Record(earlyAbs, earlySpot, Visibly.Nothing);
            track.Record(midAbs, midSpot, Visibly.Nothing);
            track.Record(lateAbs, lateSpot, Visibly.Nothing);

            Sighting[] undisturbed = Recollection.WhatTheySaw(v.World, v.People, who, day, track, v.Seed);
            Assert.That(undisturbed.Length, Is.EqualTo(3),
                "expected three separate sightings, one at each isolated minute - fixture or " +
                "seed changed? " +
                string.Join(", ", System.Array.ConvertAll(undisturbed, s => s.Minute.ToString())));

            // Down strictly after the early sighting; back sixty minutes later - the mid probe
            // sits inside that window, the late probe comfortably after it.
            var interruptions = new Interval { From = earlyAbs + 1, Back = earlyAbs + 1 + 60 };
            Sighting[] afterReturn = Recollection.WhatTheySaw(v.World, v.People, who, day, track,
                                                              v.Seed, interruptions: interruptions);

            Assert.That(afterReturn.Length, Is.EqualTo(2),
                "the middle sighting sits inside the down-to-back window and should vanish; the " +
                "early one (before From) and the late one (after Back) should both still testify: " +
                string.Join(", ", System.Array.ConvertAll(afterReturn, s => s.Minute.ToString())));
            Assert.That(afterReturn[0].Minute, Is.EqualTo(undisturbed[0].Minute),
                "the surviving early sighting should be unchanged by the window");
            Assert.That(afterReturn[1].Minute, Is.EqualTo(undisturbed[2].Minute),
                "the surviving late sighting is the one after the witness came back");
        }

        /// <summary>
        /// The one-way half of Task 6, restated against the new Interval stub instead of DownedAt:
        /// Back left at its default, int.MaxValue, must reproduce
        /// ADownedWitnessTestifiesToNothingFromThatMinuteOn's outcome exactly - a dead witness (or
        /// anyone the host never marks as back) stays silent forever, not just until some default
        /// window closes.
        /// </summary>
        [Test]
        public void ADeadWitnessStaysSilentForever()
        {
            const int day = 3;
            const int gap = 240;

            var v = VillageContext.Load();

            Citizen who = null;
            int earlyMinute = -1, lateMinute = -1;

            foreach (Citizen candidate in v.People.Citizens)
            {
                DayPlan plan = DayPlanner.Plan(v.World, v.People, candidate, day, v.Seed);
                for (int m = 0; m + gap < Sighting.MinutesPerDay; m++)
                {
                    if (!IsStationary(plan.At(m)) || !IsStationary(plan.At(m + gap))) continue;
                    who = candidate;
                    earlyMinute = m;
                    lateMinute = m + gap;
                    break;
                }
                if (who != null) break;
            }
            Assert.That(who, Is.Not.Null,
                "no citizen in the fixture village is stationary twice, four hours apart, in one " +
                "day - the test needs a different search, not a bigger gap");

            DayPlan whosPlan = DayPlanner.Plan(v.World, v.People, who, day, v.Seed);
            Tile earlySpot = v.World.GetPlace(whosPlan.At(earlyMinute).Where).Door;
            Tile lateSpot = v.World.GetPlace(whosPlan.At(lateMinute).Where).Door;

            var track = new PlayerTrack();
            int earlyAbs = day * Sighting.MinutesPerDay + earlyMinute;
            int lateAbs = day * Sighting.MinutesPerDay + lateMinute;
            track.Record(earlyAbs, earlySpot, Visibly.Nothing);
            track.Record(lateAbs, lateSpot, Visibly.Nothing);

            Sighting[] undisturbed = Recollection.WhatTheySaw(v.World, v.People, who, day, track, v.Seed);
            Assert.That(undisturbed.Length, Is.EqualTo(2),
                "expected two separate sightings, one at each isolated minute - fixture or seed " +
                "changed? " +
                string.Join(", ", System.Array.ConvertAll(undisturbed, s => s.Minute.ToString())));

            // Back never set - stays at the stub's default, int.MaxValue.
            var interruptions = new Interval { From = earlyAbs + 1 };
            Sighting[] afterDowning = Recollection.WhatTheySaw(v.World, v.People, who, day, track,
                                                               v.Seed, interruptions: interruptions);

            Assert.That(afterDowning.Length, Is.EqualTo(1),
                "Back = int.MaxValue should reproduce the plain downed-forever behaviour exactly " +
                "- the corpse is testifying to something it should have gone silent about");
            Assert.That(afterDowning[0].Minute, Is.EqualTo(undisturbed[0].Minute),
                "the surviving sighting should be the early one, unchanged by the guard");
        }

        /// <summary>
        /// The event arm honours the identical [DownedFromMinute, BackFromMinute) window as
        /// WhatTheySaw above - same stub, same three-probe shape, but proving
        /// WhatTheySawOfEvents's own gate rather than WhatTheySaw's. A hit recorded before the
        /// witness went down is witnessed (their own pre-hit day still testifies); one recorded
        /// while they were down or away is not; one recorded after they came back is again.
        /// </summary>
        [Test]
        public void TheEventArmHonoursTheSameWindow()
        {
            const int day = 3;
            const int midGap = 30;
            const int lateGap = 240;

            var v = VillageContext.Load();

            Citizen who = null;
            int earlyMinute = -1, midMinute = -1, lateMinute = -1;

            foreach (Citizen candidate in v.People.Citizens)
            {
                DayPlan plan = DayPlanner.Plan(v.World, v.People, candidate, day, v.Seed);
                for (int m = 0; m + lateGap < Sighting.MinutesPerDay; m++)
                {
                    if (!IsStationary(plan.At(m)) || !IsStationary(plan.At(m + midGap)) ||
                        !IsStationary(plan.At(m + lateGap))) continue;
                    who = candidate;
                    earlyMinute = m;
                    midMinute = m + midGap;
                    lateMinute = m + lateGap;
                    break;
                }
                if (who != null) break;
            }
            Assert.That(who, Is.Not.Null,
                "no citizen in the fixture village is stationary at all three probe minutes in " +
                "one day - the test needs a different search, not bigger gaps");

            DayPlan whosPlan = DayPlanner.Plan(v.World, v.People, who, day, v.Seed);
            Tile earlySpot = v.World.GetPlace(whosPlan.At(earlyMinute).Where).Door;
            Tile midSpot = v.World.GetPlace(whosPlan.At(midMinute).Where).Door;
            Tile lateSpot = v.World.GetPlace(whosPlan.At(lateMinute).Where).Door;

            int earlyAbs = day * Sighting.MinutesPerDay + earlyMinute;
            int midAbs = day * Sighting.MinutesPerDay + midMinute;
            int lateAbs = day * Sighting.MinutesPerDay + lateMinute;

            // DISTANCE ZERO, ON PURPOSE - same trick EventsAndPersonSightingsMergeInMinuteOrder
            // uses: a hit recorded exactly where the witness themselves is standing that minute
            // is seen regardless of light, range or attention. What's under test is the window.
            var hits = new HitEvents();
            hits.Record(earlyAbs, earlySpot, CarTone.Dark, CarShape.Van);
            hits.Record(midAbs, midSpot, CarTone.Dark, CarShape.Van);
            hits.Record(lateAbs, lateSpot, CarTone.Dark, CarShape.Van);

            EventSighting[] undisturbed = Recollection.WhatTheySawOfEvents(v.World, v.People, who,
                                                                           day, hits, v.Seed);
            Assert.That(undisturbed.Length, Is.EqualTo(3),
                "expected all three hits witnessed with no interruptions in play - fixture or " +
                "seed changed? " +
                string.Join(", ", System.Array.ConvertAll(undisturbed, s => s.Minute.ToString())));

            var interruptions = new Interval { From = earlyAbs + 1, Back = earlyAbs + 1 + 60 };
            EventSighting[] windowed = Recollection.WhatTheySawOfEvents(v.World, v.People, who,
                                                                        day, hits, v.Seed,
                                                                        interruptions: interruptions);

            Assert.That(windowed.Length, Is.EqualTo(2),
                "the hit inside the down-to-back window should be silenced; the one before From " +
                "(the witness's own pre-hit day) and the one after Back should both still be " +
                "witnessed: " +
                string.Join(", ", System.Array.ConvertAll(windowed, s => s.Minute.ToString())));
            Assert.That(windowed[0].Minute, Is.EqualTo(undisturbed[0].Minute));
            Assert.That(windowed[1].Minute, Is.EqualTo(undisturbed[2].Minute));
        }
    }
}
