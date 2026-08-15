using System;
using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.Witness
{
    /// <summary>
    /// What was visibly true of a body, at a hundred feet, in 1979 street lighting.
    ///
    /// Every flag here must be perceptible from outside — the same test ObservedManner applies.
    /// A flag that could only be known by reading the simulation does not belong, however
    /// convenient it would be later.
    /// </summary>
    [Flags]
    public enum Visibly : byte
    {
        Nothing = 0,
        Carrying = 1,
        Quickly = 2,
        InCompany = 4,
        /// <summary>In (driving) a vehicle. A witness who saw this saw a car, not a figure —
        /// Recollection treats such minutes as unremarkable traffic rather than a person
        /// sighting, because in a town with ambient cars, one more car is not a memory.</summary>
        InAVehicle = 8,
    }

    /// <summary>One minute of a body's history: where it was, and how it looked.</summary>
    public readonly struct Step
    {
        public readonly Tile Where;
        public readonly Visibly Looked;

        public Step(Tile where, Visibly looked)
        {
            Where = where;
            Looked = looked;
        }
    }

    /// <summary>
    /// The player's movement, one entry a minute. THE ONLY THING THIS LAYER STORES.
    ///
    /// Everyone else replays: DayPlanner.Plan is a pure function of (seed, citizen, day), so any
    /// villager's past is recomputable and nothing about them needs keeping. The player has no
    /// plan and no key, so their track is the simulation's one piece of genuine history.
    ///
    /// READ THE LIST OF WHAT IS NOT HERE BEFORE ADDING A FIELD. There is no PlaceId, no activity,
    /// no intent, and no record of anything done — only where a body was and what it looked like
    /// from across a road. A PlaceId here would put the answer to the whole investigation one
    /// dereference away from the witness, and it is exactly what somebody will reach for the
    /// first time a report wants to say WHERE HE WENT.
    ///
    /// A fortnight is about twenty thousand entries at a handful of bytes. Cost is not the reason
    /// for any decision in this file.
    /// </summary>
    public sealed class PlayerTrack
    {
        private readonly Dictionary<int, Step> _steps = new Dictionary<int, Step>();
        private int _first = -1;
        private int _last = -1;

        /// <summary>Minutes since the simulation began — the same stamp Sighting.Minute uses.</summary>
        public int FirstMinute => _first;
        public int LastMinute => _last;
        public int Count => _steps.Count;

        /// <summary>
        /// Write down one minute. Time only ever moves forward, for the same reason it does in
        /// ObservationLog: a history assembled out of order is not a slightly wrong history, it
        /// is a different one wearing the same name.
        /// </summary>
        public void Record(int minute, Tile where, Visibly looked)
        {
            if (minute < _last)
                throw new ArgumentException(
                    "A track runs forwards. Tried to record minute " + minute +
                    " after minute " + _last + ".", nameof(minute));

            if (_first < 0) _first = minute;
            _last = minute;
            _steps[minute] = new Step(where, looked);
        }

        public bool TryGet(int minute, out Step step) => _steps.TryGetValue(minute, out step);
    }
}
