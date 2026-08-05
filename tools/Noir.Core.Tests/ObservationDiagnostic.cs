using System;
using System.Collections.Generic;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Observation;
using Noir.Core.People;
using Noir.Core.Witness;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// Walks a fabricated player down a street and prints what each villager could tell you.
    /// NOT AN ASSERTION - it is a way of LOOKING at the thing, which `docs/plans/
    /// observation-wiring.md` step 2 asks for before this is wired to anything.
    ///
    /// The argument for it, from that plan: "An observation system is exactly the kind of thing
    /// that will pass every assertion and still produce nonsense." The project has twice been
    /// caught that way with every test green - CommercialRow's infill laid under the lodge
    /// halls, and the technology layer putting a sixth of the town online with nothing to read
    /// it on. Both were found by printing something and reading it.
    ///
    /// [Explicit] so it stays out of normal runs. To look:
    ///
    ///     dotnet test -c Release tools/Noir.Core.Tests --filter "Name=PrintWhatTheySaw" ^
    ///                 -l "console;verbosity=detailed"
    ///
    /// WHAT TO READ IT FOR, all of it claimed by WHO-SEES-WHOM.md:
    ///   - does the right SORT of person see you? The retired man at his gate should see more
    ///     than the commuter who is in Danville from eight to five.
    ///   - SOMEBODY SHOULD SEE NOTHING AT ALL. If everyone sees everything, the range or the
    ///     light gate is wrong, and the whole game is wrong with it - a village where every
    ///     walk is witnessed has no investigation in it.
    ///   - the same walk at 02:00 should be thinner than the same walk at noon. If it is not,
    ///     the light model is not being consulted.
    ///   - nobody should be able to say WHO it was. PersonDescription cannot name a person and
    ///     that is deliberate; if a name appears here, something has been widened that should
    ///     not have been.
    ///
    /// WHAT IT FOUND ON ITS FIRST RUN, 2026-08-05, and it is not fixed:
    ///
    ///     09:00   135 of 148 saw something,  20 Clear,  88 Partial, 197 Glimpsed
    ///     02:00   148 of 148 saw something,   0 Clear,  29 Partial, 297 Glimpsed
    ///
    /// MORE PEOPLE SEE YOU AT TWO IN THE MORNING THAN AT NINE. Two separate causes, and only
    /// the second is in the wiring plan:
    ///
    /// 1. SLEEP IS NOT A GATE. Recollection skips a witness only while their block is
    ///    TravellingTo. At 02:00 the whole village is Asleep AT HOME, so every one of them is a
    ///    stationary witness standing at their own front door - which is why the count is not
    ///    135 but all 148. At 09:00 the ones who are out, or between two places, drop out and
    ///    the number falls. So the night is busier than the morning purely because nobody is
    ///    going anywhere.
    ///
    ///    NOT QUIETLY FIXED. Whether a sleeping villager can testify is a design question with
    ///    the investigation hanging off it - a light sleeper at a front bedroom is a real
    ///    witness, and "everyone in bed sees the street" is not. It wants deciding, not
    ///    patching, and it is the same class of deliberate gap as the walking-witness limit.
    ///
    /// 2. THE LIGHT MODEL DEGRADES BUT NEVER BLOCKS. Clear goes 20 -> 0 and Partial 88 -> 29,
    ///    so darkness is being consulted - but nobody is gated OUT by it, they are all just
    ///    demoted to Glimpsed. That is the clamp Sightlines documents and pins on purpose, and
    ///    it is exactly what step 3 of the wiring plan is about.
    ///
    /// Fixing 2 without 1 would still print 148 of 148.
    /// </summary>

    /// <summary>
    /// Stands in for the authored traits until there is a real caller to carry them.
    ///
    /// The traits that make somebody a night witness - "light sleeper", "keeps a police scanner
    /// on" - are typed into the parcel editor and live in the Unity layer, which Core cannot
    /// see and must not. The adapter that reads them belongs with the thing that calls
    /// Recollection, and WitnessFirewallTests.NothingInTheGameReferencesWitnessYet is explicit
    /// that the first file in Assets to reference this assembly must be THAT caller, named
    /// deliberately, and never a list. It does not exist yet, so this proves the hook works
    /// without spending the precedent on the wrong file.
    /// </summary>
    internal sealed class EveryNthSleeperIsLight : INightWitnesses
    {
        private readonly int _n;
        public EveryNthSleeperIsLight(int n) { _n = n; }
        public bool AwakeEnough(CitizenId who) => _n > 0 && who.Value % _n == 0;
    }

    [TestFixture, Explicit("Diagnostic - prints what villagers saw, asserts nothing")]
    public class ObservationDiagnostic
    {
        private const int MinutesPerDay = 1440;

        /// <summary>Which day of the simulation to walk. Any day; the citizen's is recomputed.</summary>
        private const int Day = 3;

        [Test]
        public void PrintWhatTheySaw()
        {
            var world = Village.World;
            var people = Village.People;
            ulong seed = Village.Seed;

            // A WALK PAST THE FRONT DOORS, rather than a straight line across the map. The
            // witnesses are stationary and stand at their own door (Recollection's documented
            // limit), so a track that does not pass any door is a track nobody could see - which
            // would print an empty page and prove nothing about the gates.
            var doors = new List<Tile>();
            foreach (var place in world.AllPlaces)
                if (place != null && place.Door.X > 0 && place.Door.Y > 0) doors.Add(place.Door);
            doors.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));

            foreach (int startMinute in new[] { 9 * 60, 2 * 60 })
            foreach (var night in new INightWitnesses[] { null, new EveryNthSleeperIsLight(12) })
            {
                var track = new PlayerTrack();
                int walked = 0;

                // One tile a minute, past thirty doors, starting at the given hour. Stepping on
                // the minute rather than per frame is what the plan warns about for the real
                // recorder; here it is simply what the structure is.
                for (int i = 0; i < doors.Count && walked < 30; i += Math.Max(1, doors.Count / 30))
                {
                    int minute = Day * MinutesPerDay + startMinute + walked;
                    track.Record(minute, doors[i], Visibly.Nothing);
                    walked++;
                }

                Console.WriteLine();
                Console.WriteLine("=================================================================");
                Console.WriteLine("  A WALK OF {0} MINUTES PAST {0} FRONT DOORS, STARTING {1:00}:00   ({2})",
                                  walked, startMinute / 60,
                                  night == null ? "nobody is a light sleeper"
                                                : "one villager in twelve is");
                Console.WriteLine("=================================================================");

                int sawSomething = 0, sawNothing = 0, sightings = 0;
                var byClarity = new Dictionary<SightingClarity, int>();

                for (int i = 0; i < people.Count; i++)
                {
                    var who = people.Get(new CitizenId(i));
                    // STANDING CORN IS CONSULTED NOW. It is expected to change nothing on this
                    // particular walk - the track runs door to door in the town and the corn is
                    // out in the country - and that is the answer to read it for. If a walk down
                    // Attica Street starts losing witnesses to a cornfield, the crop is being
                    // asked about tiles that are not fields.
                    var seen = Recollection.WhatTheySaw(world, people, who, Day, track, seed,
                                                        night, new StandingCrop(world));

                    if (seen.Length == 0) { sawNothing++; continue; }
                    sawSomething++;
                    sightings += seen.Length;

                    foreach (var s in seen)
                        byClarity[s.Clarity] = byClarity.TryGetValue(s.Clarity, out int n) ? n + 1 : 1;

                    // Only the first dozen are printed in full. The counts below are the part
                    // that answers "does the right SORT of person see you"; the transcript is
                    // there so the wording can be read for nonsense.
                    if (sawSomething > 12) continue;

                    Console.WriteLine();
                    Console.WriteLine("  {0} {1}, {2}, {3}", who.Forename, who.Surname,
                                      who.BirthYear, Occupations.NameOf(who.Job));
                    foreach (var s in seen)
                        Console.WriteLine("      {0:00}:{1:00}  {2,-10}  at {3},{4}",
                                          s.Minute % MinutesPerDay / 60, s.Minute % 60,
                                          s.Clarity, s.Where.X, s.Where.Y);
                }

                Console.WriteLine();
                Console.WriteLine("  ---- who saw the walk ----");
                Console.WriteLine("     saw something : {0,4} of {1}", sawSomething, people.Count);
                Console.WriteLine("     saw nothing   : {0,4}", sawNothing);
                Console.WriteLine("     sightings     : {0,4}", sightings);
                foreach (var kv in byClarity)
                    Console.WriteLine("       {0,-12} {1,4}", kv.Key, kv.Value);

                if (sawSomething == people.Count)
                    Console.WriteLine("     ** EVERY villager saw the walk. That is a broken gate, "
                                    + "not a busy street.");
                if (sawSomething == 0)
                    Console.WriteLine("     ** NOBODY saw the walk. Either the track misses every "
                                    + "door or a gate rejects everything.");
            }
        }
    }
}
