using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Observation;

namespace Noir.Core.Tests
{
    [TestFixture]
    public class EventSightingTests
    {
        [Test]
        public void ADefaultCarDescriptionIsBlank()
        {
            Assert.That(default(CarDescription).IsBlank, Is.True,
                "the default must be the least useful description possible");
        }

        [Test]
        public void AnEventSightingCarriesNoIdentity()
        {
            // The firewall's rule restated small: nothing in this struct can name anybody.
            // ObservationFirewallTests sweeps the whole public surface by reflection; this is
            // the readable version for this one type.
            foreach (var f in typeof(EventSighting).GetFields())
                Assert.That(f.FieldType.Name, Does.Not.Contain("Citizen").And.Not.Contain("Place"),
                    $"EventSighting.{f.Name} leaks identity");
        }

        [Test]
        public void MinuteOfDayDerivesFromMinute()
        {
            var s = new EventSighting(new ObserverId(0), minute: Sighting.MinutesPerDay + 90,
                                      new Tile(3, 3), SightingClarity.Partial,
                                      EventAct.CarStruckSomebody,
                                      new CarDescription(CarTone.Dark, CarShape.Car));
            Assert.That(s.MinuteOfDay, Is.EqualTo(90));
        }
    }
}
