using System;
using System.Collections.Generic;

namespace Noir.Core.Observation
{
    /// <summary>
    /// What a fortnight's watching came to, in two columns.
    ///
    /// Four numbers and three counts, and nothing per-person or per-fact left in it to sort by.
    /// The three counts are by QUESTION, not by proposition: "her house stood empty at four
    /// confirmed times" carries no time, no day-class and no key, so it tells you the shape of
    /// the tactical column without being any part of a timetable. The propositions themselves
    /// live in a local inside <see cref="Salience.Weigh"/> and die with the call.
    /// </summary>
    public readonly struct WatchTally
    {
        /// <summary>Distinct confirmed propositions useful to somebody who meant her harm.</summary>
        public readonly int Useful;

        /// <summary>Distinct kinds of moment that were not any of those.</summary>
        public readonly int Texture;

        public readonly int Entries;
        public readonly int MinutesInSight;

        /// <summary>How the useful column divides between the three questions. Counts only.</summary>
        public readonly int Alone;
        public readonly int HouseStandsEmpty;
        public readonly int NobodyCalls;

        public WatchTally(int useful, int texture, int entries, int minutesInSight,
                          int alone, int houseStandsEmpty, int nobodyCalls)
        {
            Useful = useful;
            Texture = texture;
            Entries = entries;
            MinutesInSight = minutesInSight;
            Alone = alone;
            HouseStandsEmpty = houseStandsEmpty;
            NobodyCalls = nobodyCalls;
        }

        /// <summary>
        /// Texture per unit of use. The 2:1 rule says this must be at least two.
        ///
        /// The one division in the whole instrument, and it is at the end. Everything upstream
        /// is integer set arithmetic, so two runs of the same seed cannot disagree about a
        /// rounding — the same discipline Simulation.Sqrt already keeps.
        /// </summary>
        public float Ratio => Texture / (float)Math.Max(1, Useful);
    }

    /// <summary>
    /// The thing that weighs what the watcher saw.
    ///
    /// THE RULE, and it is one sentence: an observation is USEFUL when it confirms a predator's
    /// question, and TEXTURE otherwise. Both columns count distinct PROPOSITIONS, never
    /// sightings — so neither can be moved by watching harder, by making the map bigger, or by
    /// adding events. Only by the village having more kinds of thing to say about a person.
    ///
    /// ---- WHY THERE ARE THREE QUESTIONS AND NOT FOUR -----------------------------------------
    ///
    /// Someone will one day want to move a category from one column to the other. Read this
    /// first, because the obvious fourth question was tried and it inverted the whole
    /// instrument.
    ///
    /// The tempting fourth is REGULARITY: "the subject was in the same place at the same time
    /// again." It is satisfied by every single in-sight observation, so "he whistles" and "she
    /// takes the top lane on Tuesdays" both fold into a bin, confirm, and land in the
    /// DENOMINATOR. That measures recurrence and calls it exploitability, which is backwards:
    /// Content/particulars.txt's own authoring rule is "observable, or nearly so — someone
    /// watching for a week could plausibly learn it", and a particular is by definition
    /// constant. An instrument that punishes a village for having constant, harmless, human
    /// things in it is an instrument that will be satisfied by a village of strangers.
    ///
    /// So the questions are the three the design document names, taken literally, and there is
    /// no fourth. A beat enacted on a metronome — the bridge at ten past ten every night, for
    /// the length of a cigarette — stays texture forever, because standing on a bridge is not
    /// one of the three questions however predictable it is. The instrument does not ask the
    /// beats to be arrhythmic. It asks them to be VARIOUS.
    ///
    /// ---- AND WHY THE TEXTURE KEY IS (Act, Manner) AND NOTHING ELSE ---------------------------
    ///
    /// It reads no place. Observed carries no Tile and no PlaceId, so a hospital is not tagged,
    /// not named and not even locatable; it can reach the useful column only by making somebody
    /// predictably alone or their house predictably empty, which is the true thing about it.
    /// Enlarging the map, adding destinations or rearranging the village moves this number by
    /// exactly zero.
    ///
    /// It is a set, not a counter. Adding EVENTS cannot raise it; only adding KINDS of event
    /// can. There is no N at which filler passes.
    ///
    /// ---- THE THREE CONSEQUENCES THAT LOOK WRONG AND ARE RIGHT --------------------------------
    ///
    /// 1. Nothing is useful because of what it is. "She works at the hospital" is not tagged and
    ///    could not be; it counts only through the door she leaves standing shut.
    /// 2. When the classifier is unsure it counts USEFUL. A merely Glimpsed sighting still
    ///    folds. An instrument that resolves its own doubt in favour of the flattering column is
    ///    not an instrument.
    /// 3. Silence is the one deliberate exception, and it runs the other way — see
    ///    ObservationLog.AMinutePassed.
    /// </summary>
    public static class Salience
    {
        // ---- the model, and it is permanently coarse ---------------------------------------
        //
        // Pinned by TwoToOneTests.TheWatchLengthAndTheBinsArePinned, in direct imitation of the
        // asmdef pin next door. EVERY REFINEMENT HERE MAKES THIS A BETTER TARGETING TOOL AND A
        // WORSE INSTRUMENT. Finer bins, a fourth day-class, one more confirmation: each of them
        // sharpens a model of a predator that this file only wants to be coarse enough to count
        // with. Loosening one is a deliberate act with a red test attached.

        /// <summary>
        /// Fourteen days, and there is no --days flag anywhere that reaches this.
        ///
        /// Measured on Ashcombe at 3 / 7 / 14 days: 2.20 / 1.40 / 1.00. The number is meaningless
        /// without the day count, so "2:1" means "2:1 at exactly fourteen days". Fourteen because
        /// DayPlanner branches on day % 7, weekend and sunday, and this village's only period is
        /// the week: over three days a Sunday churchgoing looks like a one-off and scores as
        /// texture when it is the most predictable thing in the man's week. Fourteen gives ten
        /// weekdays, two Saturdays and two Sundays — the least that lets every day-class reach
        /// two confirmations. If this ever changes, every line of Content/watched.floor is void
        /// and must be re-measured in the same commit.
        /// </summary>
        public const int DaysWatched = 14;

        /// <summary>Weekday, Saturday, Sunday. The three DayPlanner itself branches on.</summary>
        public const int DayClasses = 3;

        public const int HalfHoursPerDay = 48;

        /// <summary>
        /// How many times a thing must happen before it is a fact about Tuesdays.
        ///
        /// The one number in this file that is a judgement rather than a measurement, and it is
        /// said so plainly: the day count, the floors and the day-class split are all either
        /// read off a run or derived from DayPlanner's own branches. This is somebody's opinion
        /// that the first time you see a woman alone on the lane at half nine you have learned
        /// something about a woman, and the second time you have learned something about
        /// Tuesdays.
        /// </summary>
        public const int Confirmations = 2;

        private const int MinutesPerDay = 1440;
        private const int MinutesPerBin = 30;

        // The three questions, verbatim from the design document, and no fourth. The number is
        // also the key's millions digit, so a proposition's key says which question made it.
        private const int QuestionAlone = 1;             // when she is alone
        private const int QuestionHouseStandsEmpty = 2;  // when the house stands empty
        private const int QuestionNobodyCalls = 3;       // whether anybody would notice her gone

        private const int QuestionStride = 1_000_000;

        /// <summary>
        /// Read a watch and return what it came to.
        ///
        /// Takes a log and not a person, because there is no person here to take: the parameter
        /// list is the guarantee that no observation can be useful because of whose it is.
        ///
        /// <paramref name="texture"/> is filled with the distinct texture propositions if it is
        /// supplied, because a report is allowed to say those in full English. There is
        /// deliberately no equivalent for the useful side — the count is all the ratio needs and
        /// the prose is all an audit of the texture needs, so the tactical column's sentences
        /// are the part the measurement has no use for. Not producing them costs the
        /// measurement nothing and removes the artifact entirely.
        /// </summary>
        public static WatchTally Weigh(ObservationLog log, ICollection<int> texture = null)
        {
            if (log == null) throw new ArgumentNullException(nameof(log));

            // Three locals. They are never returned, never a field of any public type, and they
            // die with this call — so there is no type anywhere in this codebase that holds
            // "when is she alone".
            var seen = new Dictionary<int, int>();
            var useful = new HashSet<int>();
            var textureSet = new HashSet<int>();

            var byQuestion = new int[QuestionNobodyCalls + 1];

            bool Confirm(int question, int within)
            {
                int key = question * QuestionStride + within;

                seen.TryGetValue(key, out int times);
                times++;
                seen[key] = times;

                if (times < Confirmations) return false;
                if (useful.Add(key)) byQuestion[question]++;
                return true;
            }

            foreach (Observed o in log.Entries)
            {
                int day = o.Minute / MinutesPerDay;
                int dayClass = day % 7 == 6 ? 2 : day % 7 == 5 ? 1 : 0;   // Sun / Sat / weekday
                int bin = dayClass * HalfHoursPerDay + o.Minute % MinutesPerDay / MinutesPerBin;

                // Q3 is a verdict on a whole day, not on a moment, so it can never become
                // texture and it is keyed on the day-class alone. Keying it by half-hour would
                // let one estimator flood the column with 144 propositions and drown the other
                // two — "whether anybody calls" is simply not a half-hour question.
                if (o.Act == ObservedAct.NobodyCameOrWent)
                {
                    Confirm(QuestionNobodyCalls, dayClass);
                    continue;
                }

                // Both estimators are always offered the entry: |= and not ||=, because the
                // second one must be counted even when the first has already confirmed.
                bool tactical = false;
                if ((o.Manner & ObservedManner.Alone) != 0)
                    tactical |= Confirm(QuestionAlone, bin);
                if ((o.Manner & ObservedManner.TheHouseIsDark) != 0)
                    tactical |= Confirm(QuestionHouseStandsEmpty, bin);

                // Note what falls through: an entry that satisfied an estimator for the FIRST
                // time is not yet a confirmed proposition, so it counts as texture. Seeing a
                // woman alone on the lane once really is a thing about her rather than about
                // her week; the second time it stops being one, and it stops for good.
                if (tactical) continue;

                textureSet.Add(TextureKey(o.Act, o.Manner));
            }

            if (texture != null)
                foreach (int key in textureSet) texture.Add(key);

            return new WatchTally(useful.Count, textureSet.Count,
                                  log.Entries.Count, log.MinutesInSight,
                                  byQuestion[QuestionAlone],
                                  byQuestion[QuestionHouseStandsEmpty],
                                  byQuestion[QuestionNobodyCalls]);
        }

        /// <summary>
        /// One distinct kind of moment. The encoding lives here so that only one file knows it.
        ///
        /// Ceiling: |Act| x |Manner|, about sixty in practice. Compare the useful side's
        /// 144 + 144 + 3 = 291. Both columns are bounded, which is why the ratio saturates with
        /// watch length instead of decaying without bound.
        /// </summary>
        public static int TextureKey(ObservedAct act, ObservedManner manner) =>
            ((int)act << 8) | (int)manner;

        public static ObservedAct ActOf(int textureKey) => (ObservedAct)(textureKey >> 8);
        public static ObservedManner MannerOf(int textureKey) => (ObservedManner)(textureKey & 0xFF);
    }
}
