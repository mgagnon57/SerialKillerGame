using System.Collections.Generic;
using Noir.Core.Contracts;
using Noir.Core.Observation;
using Noir.Core.People;
using Noir.Core.World;

// ---------------------------------------------------------------------------------------------
//  THE PRODUCER SIDE OF THE FIREWALL.
//
//  Noir.Core.Observation is safe because it can see almost nothing: its asmdef names
//  Noir.Core.Contracts and stops, so a Sighting cannot hold a Citizen. This assembly is the
//  opposite. It sees the world, the population, every day plan and the player's whole track,
//  and it is safe for one reason only:
//
//      THE ONLY THING IT HANDS OUT IS A Sighting[], AND A Sighting CANNOT NAME ANYBODY.
//
//  Everything this assembly knows is narrowed through that return type. The compiler does the
//  narrowing, which is why the boundary is worth anything.
//
//  THE RULE, and it is about callers rather than about code in here:
//
//      NOTHING MAY REFERENCE Noir.Core.Witness EXCEPT THE ONE CALLER THAT ASKS IT A QUESTION.
//
//  The instant a single scope holds a Sighting[] and a DayPlan together, the narrowing is
//  decorative — whoever wrote it can just look the answer up, and will. WitnessFirewallTests
//  pins the reference list and greps for callers.
//
//  Do not add a method here that returns anything richer than a Sighting. Not "which citizen
//  was nearest", not a debug overload that returns the candidate before degradation, not a
//  bool "did anyone see him". Each is one line and each is the whole game.
// ---------------------------------------------------------------------------------------------

namespace Noir.Core.Witness
{
    public static class Recollection
    {
        public const int MinutesPerDay = Sighting.MinutesPerDay;

        /// <summary>
        /// Everything one villager could tell you about the player, for one day.
        ///
        /// Nothing is stored and nothing is stepped: the citizen's day is replayed from
        /// DayPlanner, which is a pure function of (seed, citizen, day), and the player's track
        /// is the only history consulted. Ask about a day from a fortnight ago and it costs the
        /// same as asking about yesterday.
        ///
        /// STATIONARY WITNESSES ONLY, and this is the honest limit of the first version. A
        /// citizen's position comes from the door of the place their plan has them at. While
        /// they are TravellingTo, nobody knows where they are — there is no stored route — and
        /// interpolating between two doors would invent a path and then treat it as evidence.
        /// So the witnesses here are the man at his gate and the woman behind the counter.
        /// Walking witnesses need routing and are a later piece of work; the gap is deliberate.
        /// </summary>
        public static Sighting[] WhatTheySaw(WorldModel world, Population population,
                                             Citizen who, int day, PlayerTrack track, ulong seed,
                                             INightWitnesses nightWitnesses = null,
                                             ISightBlocked blocked = null,
                                             IInterruptions interruptions = null)
        {
            DayPlan plan = DayPlanner.Plan(world, population, who, day, seed);
            var found = new List<Sighting>();

            // THE SAME GATE WhatTheySawOfEvents CARRIES - see its own comment ("the victim
            // testifies to nothing"). A downed witness is not on their feet at their planned
            // door any more; they are lying wherever the car left them, and this is the half
            // of Interruptions.cs's promise that stops the corpse describing the player's own
            // afternoon after it stopped being able to see anything.
            int downedFrom = interruptions?.DownedFromMinute(who.Id) ?? int.MaxValue;
            int backFrom = interruptions?.BackFromMinute(who.Id) ?? int.MaxValue;

            // ASKED ONCE, not per minute: whether somebody is a light sleeper is a fact about
            // the person, not about the moment. Null means nobody is, which is the default.
            bool seesWhileAsleep = nightWitnesses != null && nightWitnesses.AwakeEnough(who.Id);

            bool inSight = false;

            for (int minuteOfDay = 0; minuteOfDay < MinutesPerDay; minuteOfDay++)
            {
                int minute = day * MinutesPerDay + minuteOfDay;
                if (minute >= downedFrom && minute < backFrom) { inSight = false; continue; }   // saw nothing, downed or away

                if (!track.TryGet(minute, out Step step)) { inSight = false; continue; }

                // A car among cars. While the player is driving, a witness saw ONE MORE CAR
                // in a town where ambient traffic passes all day - not a figure, and not a
                // memory. Sightings of the person stop; anything worth remembering about the
                // car is an EVENT (WhatTheySawOfEvents), which is where a hit goes.
                if ((step.Looked & Visibly.InAVehicle) != 0) { inSight = false; continue; }

                Block block = plan.At(minuteOfDay);
                if (block.What == Activity.TravellingTo) { inSight = false; continue; }

                // ASLEEP IS A GATE, and until 2026-08-05 it was not - which made the night
                // BUSIER than the morning. Only TravellingTo was skipped, so at 02:00 the whole
                // village counted as a stationary witness standing at its own front door: the
                // diagnostic printed 148 of 148 at two in the morning against 135 of 148 at
                // nine, because at nine the ones who are out or between places drop out.
                //
                // A village asleep witnesses nothing. The exception is authored per person -
                // the light sleeper, the man with the scanner on, the one who walks the dog
                // late - and comes in through INightWitnesses, because who those people are is
                // a thing somebody decided and not a rule.
                if (block.What == Activity.Asleep && !seesWhileAsleep)
                { inSight = false; continue; }
                if (!block.Where.IsValid) { inSight = false; continue; }

                Tile watcher = world.GetPlace(block.Where).Door;

                // THE DATE GOES WITH THE HOUR. Sightlines needs to know the season to know how
                // dark it is - sunset here moves nearly four hours between the solstices - so it
                // takes the clock rather than a minute-of-day.
                var when = new GameClock(GameClock.TickAt(day, minuteOfDay));
                SightingClarity clarity = Sightlines.HowGoodALook(watcher, step.Where, when, who);
                if (!Sightlines.SawAnythingAtAll(clarity, watcher, step.Where, when, blocked))
                {
                    inSight = false;
                    continue;
                }

                // One visit, not one frame a minute. A figure who stays in sight is a single
                // thing a witness remembers, stamped at the moment they first noticed him.
                if (inSight) continue;
                inSight = true;

                PersonDescription seen = Degradation.WhatRegistered(
                    clarity, step.Looked,
                    subjectIsMale: true, subjectAge: 35, heightCm: 178, buildIndex: 1,
                    who.Key, minute, seed);

                found.Add(new Sighting(new ObserverId(found.Count),
                                       BlurredMinute(minute, clarity),
                                       watcher, clarity, seen));
            }

            return found.ToArray();
        }

        /// <summary>
        /// Everything one villager could tell you about recorded EVENTS, for one day. The
        /// same stationary-witness arithmetic as WhatTheySaw, run against the event list
        /// instead of the player's track: an event is witnessed by exactly the people the
        /// existing rules say could see that tile at that minute — INCLUDING the same
        /// INightWitnesses exception WhatTheySaw honours, asked once per citizen rather than
        /// once per event, because whether somebody is a light sleeper is a fact about the
        /// person, not about the moment.
        /// </summary>
        public static EventSighting[] WhatTheySawOfEvents(WorldModel world, Population population,
            Citizen who, int day, HitEvents hits, ulong seed,
            INightWitnesses nightWitnesses = null,
            IInterruptions interruptions = null, ISightBlocked blocked = null)
        {
            var found = new List<EventSighting>();
            if (hits == null || hits.Count == 0) return System.Array.Empty<EventSighting>();

            DayPlan plan = DayPlanner.Plan(world, population, who, day, seed);
            int downedFrom = interruptions?.DownedFromMinute(who.Id) ?? int.MaxValue;
            int backFrom = interruptions?.BackFromMinute(who.Id) ?? int.MaxValue;

            // ASKED ONCE, not per event: see the identical comment on WhatTheySaw.
            bool seesWhileAsleep = nightWitnesses != null && nightWitnesses.AwakeEnough(who.Id);

            hits.ForEach((minute, where, tone, shape) =>
            {
                int minuteOfDay = minute % MinutesPerDay;
                if (minute / MinutesPerDay != day) return;
                if (minute >= downedFrom && minute < backFrom) return;   // silenced while down or away

                Block block = plan.At(minuteOfDay);
                if (block.What == Activity.TravellingTo) return;
                if (block.What == Activity.Asleep && !seesWhileAsleep) return;
                if (!block.Where.IsValid) return;

                Tile watcher = world.GetPlace(block.Where).Door;
                var when = new GameClock(GameClock.TickAt(day, minuteOfDay));
                SightingClarity clarity = Sightlines.HowGoodALook(watcher, where, when, who);
                if (!Sightlines.SawAnythingAtAll(clarity, watcher, where, when, blocked)) return;

                var car = Degradation.CarRegistered(clarity, tone, shape, who.Key, minute, seed);
                found.Add(new EventSighting(new ObserverId(found.Count),
                                            BlurredMinute(minute, clarity),
                                            watcher, clarity,
                                            EventAct.CarStruckSomebody, car));
            });

            return found.ToArray();
        }

        /// <summary>
        /// The same question, answered in English. One line per sighting, oldest first, and a
        /// single sentence when they saw nothing.
        ///
        /// WHY THE STRING VERSION LIVES HERE. Noir.Unity references Noir.Core.Witness and does
        /// NOT reference Noir.Core.Observation - that is the firewall, and it is worth keeping.
        /// Handing the game a Sighting[] would force the reference and put the investigation's own
        /// types in reach of every MonoBehaviour that wanted one. Handing it string[] does not.
        /// The game gets testimony; it never gets the evidence to reason backwards from.
        ///
        /// So this is the seam the whole layer was waiting for, and it is one method wide on
        /// purpose.
        /// </summary>
        public static string[] AskInEnglish(WorldModel world, Population population,
                                            Citizen who, int day, PlayerTrack track, ulong seed,
                                            INightWitnesses nightWitnesses = null,
                                            ISightBlocked blocked = null,
                                            HitEvents hits = null,
                                            IInterruptions interruptions = null)
        {
            Sighting[] saw = WhatTheySaw(world, population, who, day, track, seed,
                                         nightWitnesses, blocked, interruptions);
            EventSighting[] events = WhatTheySawOfEvents(world, population, who, day, hits,
                                                         seed, nightWitnesses, interruptions, blocked);

            // A SENTENCE, NOT AN EMPTY ARRAY. "I saw nobody" and "nobody asked me" are the same
            // value to a caller holding an empty list and completely different answers to a
            // player. An alibi is evidence too.
            if (saw.Length == 0 && events.Length == 0) return new[] { Testimony.SawNothing };

            // MINUTE-ORDERED, TIES GO TO THE PERSON LINE - "the order they happened" is this
            // method's own promise, and it has to hold ACROSS the two kinds of thing a witness
            // can report, not just within one of them: drive past, hit somebody, walk on is the
            // feature's whole scenario, and telling it hit-then-drive is telling it backwards.
            // Both arrays already arrive in TRUE-minute order - WhatTheySaw and
            // WhatTheySawOfEvents each walk the day forwards - so this is a merge of two sorted
            // runs, not a sort. NOT STRICTLY OLDEST-FIRST ON THE STAMP SHOWN, THOUGH: the merge
            // key is BlurredMinute, and blur (up to -14 minutes, worse for a worse look) can
            // locally invert two close true minutes, so an equal blurred stamp is a genuine tie
            // rather than proof the two happened at the same moment. Ties go to the person line:
            // seeing somebody is the ordinary thing a witness reports, and it is what they would
            // say first if asked "what did you see?" at that same moment.
            var lines = new List<string>(saw.Length + events.Length);
            int i = 0, j = 0;
            while (i < saw.Length && j < events.Length)
            {
                if (saw[i].Minute <= events[j].Minute) lines.Add(Testimony.InEnglish(saw[i++]));
                else lines.Add(Testimony.InEnglish(events[j++]));
            }
            while (i < saw.Length) lines.Add(Testimony.InEnglish(saw[i++]));
            while (j < events.Length) lines.Add(Testimony.InEnglish(events[j++]));
            return lines.ToArray();
        }

        /// <summary>
        /// "About half seven." Nobody reports a minute, and a consumer given one would trust it.
        /// The worse the look, the coarser the memory of when it was.
        /// </summary>
        private static int BlurredMinute(int minute, SightingClarity clarity)
        {
            int to = clarity == SightingClarity.Clear ? 1
                   : clarity == SightingClarity.Partial ? 5 : 15;
            return minute / to * to;
        }
    }
}
