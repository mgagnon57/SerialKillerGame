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
                                             ISightBlocked blocked = null)
        {
            DayPlan plan = DayPlanner.Plan(world, population, who, day, seed);
            var found = new List<Sighting>();

            // ASKED ONCE, not per minute: whether somebody is a light sleeper is a fact about
            // the person, not about the moment. Null means nobody is, which is the default.
            bool seesWhileAsleep = nightWitnesses != null && nightWitnesses.AwakeEnough(who.Id);

            bool inSight = false;

            for (int minuteOfDay = 0; minuteOfDay < MinutesPerDay; minuteOfDay++)
            {
                int minute = day * MinutesPerDay + minuteOfDay;

                if (!track.TryGet(minute, out Step step)) { inSight = false; continue; }

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
                                            ISightBlocked blocked = null)
        {
            Sighting[] saw = WhatTheySaw(world, population, who, day, track, seed,
                                         nightWitnesses, blocked);

            // A SENTENCE, NOT AN EMPTY ARRAY. "I saw nobody" and "nobody asked me" are the same
            // value to a caller holding an empty list and completely different answers to a
            // player. An alibi is evidence too.
            if (saw.Length == 0) return new[] { Testimony.SawNothing };

            return Testimony.InEnglish(saw);
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
