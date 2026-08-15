using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.Witness
{
    /// <summary>
    /// Vehicular harm, one entry per event. THE SECOND GENUINE HISTORY: like PlayerTrack —
    /// whose own header bans widening the track — this is player-caused and underivable from
    /// the seed, so it is stored rather than replayed. A LIST, not a minute-keyed dictionary:
    /// two hits in one minute are both real. What is NOT here, deliberately: no victim id, no
    /// PlaceId — the victim's identity is the sim's fact (AgentState.Downed) and evidence may
    /// only carry what a stranger could see. Phase 2's police join the two by minute and tile.
    /// </summary>
    public sealed class HitEvents
    {
        private readonly struct Hit
        {
            public readonly int Minute;
            public readonly Tile Where;
            public readonly CarTone Tone;
            public readonly CarShape Shape;
            public Hit(int minute, Tile where, CarTone tone, CarShape shape)
            { Minute = minute; Where = where; Tone = tone; Shape = shape; }
        }

        private readonly List<Hit> _hits = new List<Hit>();
        public int Count => _hits.Count;

        public void Record(int minute, Tile where, CarTone tone, CarShape shape)
        {
            if (_hits.Count > 0 && minute < _hits[_hits.Count - 1].Minute)
                throw new ArgumentException(
                    "A history runs forwards. Tried to record minute " + minute +
                    " after minute " + _hits[_hits.Count - 1].Minute + ".", nameof(minute));
            _hits.Add(new Hit(minute, where, tone, shape));
        }

        /// <summary>In-order replay, without handing out the internal type.</summary>
        public void ForEach(Action<int, Tile, CarTone, CarShape> visit)
        {
            foreach (var h in _hits) visit(h.Minute, h.Where, h.Tone, h.Shape);
        }
    }
}
