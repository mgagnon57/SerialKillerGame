using Noir.Core.Contracts;

namespace Noir.Core.Observation
{
    /// <summary>
    /// The coarse verbs a stranger could put to something they watched HAPPEN. ObservedAct is
    /// the house style (nine verbs, no interpretation); this enum exists separately because an
    /// event sighting carries a Tile and ObservedAct's log deliberately cannot.
    /// </summary>
    public enum EventAct : byte
    {
        CarStruckSomebody = 0,
        SomebodyAskedQuestions = 1,
    }

    /// <summary>
    /// Something a witness saw HAPPEN, at a place and a minute. Sighting's sibling, not its
    /// extension — Sighting deliberately has no verb, and widening it would erode the
    /// vagueness doctrine. Like Sighting: a claim, not a record; no victim, no driver, no id
    /// of any kind. The figure that went down is at most a PersonDescription-shaped blur a
    /// LATER pass may add; v1 reports the act and the car.
    /// </summary>
    public readonly struct EventSighting
    {
        public readonly ObserverId Observer;
        /// <summary>Minutes since the simulation began — same stamp as Sighting.Minute.</summary>
        public readonly int Minute;
        public readonly Tile Where;
        public readonly SightingClarity Clarity;
        public readonly EventAct Act;
        public readonly CarDescription Car;

        public int MinuteOfDay => Minute % Sighting.MinutesPerDay;

        public EventSighting(ObserverId observer, int minute, Tile where,
                             SightingClarity clarity, EventAct act, CarDescription car)
        {
            Observer = observer; Minute = minute; Where = where;
            Clarity = clarity; Act = act; Car = car;
        }
    }
}
