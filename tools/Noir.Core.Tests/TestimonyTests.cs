using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Observation;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The sentence a witness gives. Tested because it is the first thing in this project a
    /// PLAYER will read out of the investigation layer, and because the failure mode is not a
    /// crash - it is testimony that says slightly more than the witness registered, which is
    /// indistinguishable from working and quietly ruins the only mechanic the game has.
    /// </summary>
    [TestFixture]
    public class TestimonyTests
    {
        private static Sighting At(int minuteOfDay, SightingClarity clarity, PersonDescription d) =>
            new Sighting(new ObserverId(1), minuteOfDay, new Tile(10, 10), clarity, d);

        private static PersonDescription Desc(
            ApparentSex sex = ApparentSex.Unnoticed,
            HeightBand height = HeightBand.Unnoticed,
            BuildBand build = BuildBand.Unnoticed,
            AgeBand age = AgeBand.Unnoticed,
            ClothingTone clothing = ClothingTone.Unnoticed,
            CarriedThing carrying = CarriedThing.Unnoticed) =>
            new PersonDescription(sex, height, build, age, clothing, carrying);

        [Test]
        public void ARegisteredNothingSightingIsStillASentence()
        {
            string said = Testimony.InEnglish(At(23 * 60 + 5, SightingClarity.Glimpsed, PersonDescription.Nothing));

            Assert.That(said, Is.EqualTo("23:05, I caught sight of somebody."),
                "A witness who registered nothing still saw somebody, and still has to be able to " +
                "say so. Returning an empty string here would make 'saw nothing' and 'was never " +
                "asked' the same answer.");
        }

        [Test]
        public void TheVerbCarriesTheCertainty()
        {
            var d = PersonDescription.Nothing;
            Assert.That(Testimony.InEnglish(At(600, SightingClarity.Clear, d)), Does.Contain("good look"));
            Assert.That(Testimony.InEnglish(At(600, SightingClarity.Partial, d)), Does.Contain("I saw"));
            Assert.That(Testimony.InEnglish(At(600, SightingClarity.Glimpsed, d)), Does.Contain("caught sight"));
        }

        [Test]
        public void AFullDescriptionReadsAsOneSentence()
        {
            string said = Testimony.InEnglish(At(7 * 60 + 42, SightingClarity.Clear,
                Desc(ApparentSex.Man, HeightBand.Tall, BuildBand.Heavy, AgeBand.MiddleAged,
                     ClothingTone.Dark, CarriedThing.Bag)));

            Assert.That(said, Is.EqualTo(
                "07:42, I got a good look at a middle-aged tall heavy-set man, in something dark, carrying a bag."),
                "The order is time, certainty, then description - the order somebody actually " +
                "answers 'what did you see?' in.");
        }

        /// <summary>
        /// The case that decides whether this type is honest. Six bands, five Unnoticed.
        /// </summary>
        [Test]
        public void OneRegisteredBandSaysOneThingAndNoMore()
        {
            string said = Testimony.InEnglish(At(22 * 60 + 50, SightingClarity.Glimpsed,
                Desc(height: HeightBand.Tall)));

            Assert.That(said, Is.EqualTo("22:50, I caught sight of somebody tall."),
                "A figure crossing a lane at eleven at night is 'somebody, tallish' and nothing " +
                "more. Any extra word here is invented evidence.");
        }

        [Test]
        public void WithNoApparentSexTheAdjectivesGoBehind()
        {
            // "a tall somebody" is not English; "somebody tall" is.
            string said = Testimony.InEnglish(At(60, SightingClarity.Partial,
                Desc(height: HeightBand.Short, build: BuildBand.Slight)));

            Assert.That(said, Is.EqualTo("01:00, I saw somebody short, slight."));
        }

        [Test]
        public void TheArticleAgreesWithTheWordAfterIt()
        {
            string elderly = Testimony.InEnglish(At(0, SightingClarity.Clear,
                Desc(ApparentSex.Woman, age: AgeBand.Old)));
            Assert.That(elderly, Does.Contain("an elderly woman"), "'a elderly woman' is wrong");

            string tall = Testimony.InEnglish(At(0, SightingClarity.Clear,
                Desc(ApparentSex.Man, height: HeightBand.Tall)));
            Assert.That(tall, Does.Contain("a tall man"));
        }

        [Test]
        public void EmptyHandedIsNotTheSameAsNotHavingLooked()
        {
            string seen = Testimony.InEnglish(At(0, SightingClarity.Clear,
                Desc(ApparentSex.Man, carrying: CarriedThing.NothingSeen)));
            Assert.That(seen, Does.Contain("empty-handed"));

            string unlooked = Testimony.InEnglish(At(0, SightingClarity.Clear, Desc(ApparentSex.Man)));
            Assert.That(unlooked, Does.Not.Contain("empty-handed"),
                "CarriedThing.Unnoticed means nobody looked at their hands. Saying 'empty-handed' " +
                "there is testimony the witness never gave.");
        }

        [Test]
        public void ClothingIsToneAndNeverColour()
        {
            string said = Testimony.InEnglish(At(0, SightingClarity.Clear,
                Desc(ApparentSex.Woman, clothing: ClothingTone.Light)));

            Assert.That(said, Does.Contain("something light"));
            foreach (string colour in new[] { "blue", "red", "green", "brown", "black", "white" })
                Assert.That(said.ToLowerInvariant(), Does.Not.Contain(colour),
                    "ClothingTone is deliberately tone and not colour - colour is the first thing " +
                    "a witness gets wrong and the first thing they will swear to.");
        }

        [Test]
        public void MidnightAndNoonAreBothReadable()
        {
            Assert.That(Testimony.InEnglish(At(0, SightingClarity.Clear, PersonDescription.Nothing)),
                        Does.StartWith("00:00, "));
            Assert.That(Testimony.InEnglish(At(12 * 60, SightingClarity.Clear, PersonDescription.Nothing)),
                        Does.StartWith("12:00, "));
            Assert.That(Testimony.InEnglish(At(24 * 60 - 1, SightingClarity.Clear, PersonDescription.Nothing)),
                        Does.StartWith("23:59, "));
        }

        [Test]
        public void AnEmptyRunOfSightingsRendersToNoLines()
        {
            Assert.That(Testimony.InEnglish((Sighting[])null), Is.Empty);
            Assert.That(Testimony.InEnglish(new Sighting[0]), Is.Empty);
        }

        [Test]
        public void EverySightingRendersToExactlyOneLine()
        {
            var many = new[]
            {
                At(9 * 60, SightingClarity.Clear, Desc(ApparentSex.Man)),
                At(10 * 60, SightingClarity.Partial, PersonDescription.Nothing),
                At(11 * 60, SightingClarity.Glimpsed, Desc(height: HeightBand.Short)),
            };

            string[] lines = Testimony.InEnglish(many);

            Assert.That(lines.Length, Is.EqualTo(3));
            foreach (string line in lines)
            {
                Assert.That(line, Does.EndWith("."));
                Assert.That(line, Does.Not.Contain("\n"), "one sighting is one line");
                Assert.That(line.Trim(), Is.Not.Empty);
            }
        }
    }
}
