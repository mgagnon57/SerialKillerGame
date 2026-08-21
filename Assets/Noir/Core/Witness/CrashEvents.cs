using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.Witness
{
    /// <summary>
    /// Two cars coming together, one entry per event. THE THIRD GENUINE HISTORY, same shape and
    /// same reason as HitEvents and AskEvents: player-facing evidence rather than something
    /// derivable from the seed, so it is stored rather than replayed. A LIST, not a minute-keyed
    /// dictionary — two crashes in one minute are both real, however unlikely today's planner
    /// makes that. What is NOT here, deliberately: no driver id, no PlaceId, no fault, no
    /// verdict — a witness only ever saw one car (the at-fault one, degraded the same way a hit
    /// is) meet another; which car was which and who was to blame are the case's business, not
    /// the street's.
    /// </summary>
    public sealed class CrashEvents
    {
        private readonly struct Crash
        {
            public readonly int Minute;
            public readonly Tile Where;
            public readonly CarTone Tone;
            public readonly CarShape Shape;
            public Crash(int minute, Tile where, CarTone tone, CarShape shape)
            { Minute = minute; Where = where; Tone = tone; Shape = shape; }
        }

        private readonly List<Crash> _crashes = new List<Crash>();
        public int Count => _crashes.Count;

        public void Record(int minute, Tile where, CarTone tone, CarShape shape)
        {
            if (_crashes.Count > 0 && minute < _crashes[_crashes.Count - 1].Minute)
                throw new ArgumentException(
                    "A history runs forwards. Tried to record minute " + minute +
                    " after minute " + _crashes[_crashes.Count - 1].Minute + ".", nameof(minute));
            _crashes.Add(new Crash(minute, where, tone, shape));
        }

        /// <summary>In-order replay, without handing out the internal type.</summary>
        public void ForEach(Action<int, Tile, CarTone, CarShape> visit)
        {
            foreach (var c in _crashes) visit(c.Minute, c.Where, c.Tone, c.Shape);
        }
    }
}
