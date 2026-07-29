using System;
using System.Collections.Generic;

namespace Noir.Core.Observation
{
    /// <summary>
    /// Everything one watch produced, in the order it was written down.
    ///
    /// THERE IS NO SUBJECT FIELD, and that is not an oversight. A log does not know whose life
    /// it is; the caller holds the mapping, outside this assembly, and throws it away before
    /// printing anything. It is what makes the classifier structurally unable to score a person
    /// — Salience.Weigh takes one of these and it is not a person, so no observation can ever be
    /// useful BECAUSE OF WHO IT IS ABOUT, which is precisely the demographic-vulnerability
    /// inference the content contract forbids.
    ///
    /// The two minute counters are the honest denominator. A watch is mostly a closed door, and
    /// a report that quoted only the entries would be quoting the 4% of a fortnight the street
    /// happened to show and calling it a life.
    /// </summary>
    public sealed class ObservationLog
    {
        private readonly List<Observed> _entries = new List<Observed>();
        private int _lastMinute = -1;

        public IReadOnlyList<Observed> Entries => _entries;

        /// <summary>Minutes the watcher was there at all.</summary>
        public int MinutesWatched { get; private set; }

        /// <summary>Minutes of those in which the subject could be seen at all.</summary>
        public int MinutesInSight { get; private set; }

        /// <summary>
        /// A minute of the watch has gone by, and whether the subject was visible for it.
        ///
        /// Called for every minute, including the thousands with nothing in them. SILENCE IS
        /// NEVER EVIDENCE: a minute out of sight advances MinutesWatched and produces no entry,
        /// and there is deliberately no way to record what the subject was doing during one.
        /// </summary>
        public void AMinutePassed(bool inSight)
        {
            MinutesWatched++;
            if (inSight) MinutesInSight++;
        }

        /// <summary>
        /// Write a line. Time only ever moves forward.
        ///
        /// The throw is not defensive tidiness. Salience confirms propositions in the order it
        /// reads them, so which of two entries comes first decides which column BOTH of them
        /// land in; a log assembled out of order does not produce a slightly different number,
        /// it produces a different measurement wearing the same name.
        /// </summary>
        public void Add(in Observed entry)
        {
            if (entry.Minute < _lastMinute)
                throw new ArgumentException(
                    "An observation log runs forwards. Tried to add an entry at minute " +
                    entry.Minute + " after one at minute " + _lastMinute + ". Whichever entry " +
                    "confirms a proposition first decides whether both are counted as useful or " +
                    "as texture, so the order is part of the measurement, not a presentation detail.");

            _lastMinute = entry.Minute;
            _entries.Add(entry);
        }

        public int Count => _entries.Count;
    }
}
