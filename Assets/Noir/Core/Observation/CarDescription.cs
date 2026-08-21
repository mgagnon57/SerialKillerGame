using Noir.Core.Contracts;

namespace Noir.Core.Observation
{
    /// <summary>
    /// What a witness could say about a car. Same doctrine as PersonDescription, restated for
    /// wheels: every band wide enough to hold a good fraction of the town's fleet, Unnoticed
    /// as zero so the default is "there was a car, couldn't tell you a thing", tone never
    /// colour, shape never a make. A description that identifies one specific car is a design
    /// bug, not a feature.
    /// </summary>
    public readonly struct CarDescription
    {
        public readonly CarTone Tone;
        public readonly CarShape Shape;

        public CarDescription(CarTone tone, CarShape shape) { Tone = tone; Shape = shape; }

        public bool IsBlank => Tone == CarTone.Unnoticed && Shape == CarShape.Unnoticed;
    }
}
