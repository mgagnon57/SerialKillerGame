using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.Witness
{
    /// <summary>
    /// Every time somebody went around a witness's door asking questions, as bare facts: a
    /// minute and a tile. The same shape as HitEvents and for the same reason — witnesses
    /// testify about it through the identical sighting arithmetic, and the history carries no
    /// identity at all: WHO asked is exactly what a witness cannot hand you. Phase 2 of the
    /// witness-voices spec: the killer's own canvass becomes a sighting.
    /// </summary>
    public sealed class AskEvents
    {
        private readonly struct Ask
        {
            public readonly int Minute;
            public readonly Tile Where;
            public Ask(int minute, Tile where) { Minute = minute; Where = where; }
        }

        private readonly List<Ask> _asks = new List<Ask>();

        public int Count => _asks.Count;

        public void Record(int minute, Tile where)
        {
            if (_asks.Count > 0 && minute < _asks[_asks.Count - 1].Minute)
                throw new ArgumentException(
                    "A history runs forwards. Tried to record minute " + minute +
                    " after minute " + _asks[_asks.Count - 1].Minute + ".", nameof(minute));
            _asks.Add(new Ask(minute, where));
        }

        public void ForEach(Action<int, Tile> visit)
        {
            foreach (var a in _asks) visit(a.Minute, a.Where);
        }
    }
}
