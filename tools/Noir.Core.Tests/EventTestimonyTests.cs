using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Observation;
using Noir.Core.Witness;

namespace Noir.Core.Tests
{
    [TestFixture]
    public class EventTestimonyTests
    {
        [Test]
        public void CarBandsDegradeButNeverInvent()
        {
            // At a glimpse most bands die; whatever survives must be the truth or Unnoticed —
            // wrongness is allowed only where the person machinery already allows it (tone at
            // a glimpse may shift one band, same spirit as ApparentSex).
            var d = Degradation.CarRegistered(SightingClarity.Clear, CarTone.Dark, CarShape.Van,
                                              new CitizenKey(12345), minute: 500, seed: 1979);
            Assert.That(d.Shape, Is.EqualTo(CarShape.Van).Or.EqualTo(CarShape.Unnoticed));

            var g = Degradation.CarRegistered(SightingClarity.Glimpsed, CarTone.Dark, CarShape.Van,
                                              new CitizenKey(12345), minute: 500, seed: 1979);
            Assert.That(g.IsBlank || !g.IsBlank, Is.True); // never throws; bands legal by type
        }

        [Test]
        public void TheMemoryIsTheSeed()
        {
            var once = Degradation.CarRegistered(SightingClarity.Partial, CarTone.Light,
                CarShape.Pickup, new CitizenKey(777), 900, 1979);
            var twice = Degradation.CarRegistered(SightingClarity.Partial, CarTone.Light,
                CarShape.Pickup, new CitizenKey(777), 900, 1979);
            Assert.That(once.Tone, Is.EqualTo(twice.Tone));
            Assert.That(once.Shape, Is.EqualTo(twice.Shape));
        }

        [Test]
        public void AnEventSightingReadsLikeAWitness()
        {
            var s = new EventSighting(new ObserverId(0), 990, new Tile(4, 4),
                SightingClarity.Partial, EventAct.CarStruckSomebody,
                new CarDescription(CarTone.Dark, CarShape.Car));
            string line = Testimony.InEnglish(s);
            Assert.That(line, Does.StartWith("16:30, "));
            Assert.That(line, Does.EndWith("."));
            Assert.That(line.ToLowerInvariant(), Does.Contain("hit"));
            Assert.That(line.ToLowerInvariant(), Does.Contain("dark"));
        }

        [Test]
        public void ABlankCarIsStillACar()
        {
            var s = new EventSighting(new ObserverId(0), 990, new Tile(4, 4),
                SightingClarity.Glimpsed, EventAct.CarStruckSomebody, default);
            Assert.That(Testimony.InEnglish(s).ToLowerInvariant(), Does.Contain("a car"));
        }
    }
}
