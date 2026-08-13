using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// AN AMENITY IS NOT A HOUSE WITH A DIFFERENT WORD ON IT.
    ///
    /// Rossville had NINE schools. Two are real - Rossville Grade School at 47x18 and
    /// Rossville-Alvin High School at 60x38, both off the survey. The other seven were 13x7 boxes
    /// with residential street addresses for names: `305 W Benton Ave`, `303 W Benton Ave`,
    /// `301 W Benton Ave`, `101 W Benton Ave`, `102 E Green Ave`, `101 E Benton Ave` and
    /// `103 E Benton Ave`. 13x7 is the default house footprint and EVERY ONE of the town's 315
    /// houses is exactly that size, so these were houses that a commit re-KINDED instead of taking
    /// down, standing in a row between identical neighbours.
    ///
    /// WHAT IT COST, AND IT IS NOT A COSMETIC NUMBER. Catchment is assigned by nearest amenity of
    /// the kind, so 75.2% of the town's dwellings catchmented to `103 E Benton Ave` - a 91 m2
    /// house. And `SeatOnSurvey` pairs biggest-first, which seated two of them onto a 251 m2 and a
    /// 43 m2 outbuilding: the built town contained a FORTY-THREE SQUARE METRE SCHOOL WITH FOUR
    /// TEACHER POSTS. Every child in Rossville walked to it.
    ///
    /// The failure is silent by construction. A kind is a word in a text file; nothing checks that
    /// the word matches the building, so a school the size of a bedroom parses, seats, staffs,
    /// catchments and renders without one warning anywhere.
    ///
    /// THE RULE IS THE DEFAULT BOX, NOT AN AREA THRESHOLD. A guard saying "a school must be over
    /// 200 m2" is a number somebody will have to re-tune the first time a real one-room school
    /// turns up on a survey sheet. This says something narrower and permanent: an amenity may not
    /// have the EXACT footprint that every single house in the map has. If a real amenity ever
    /// genuinely is 13x7, give it a footprint off the survey like everything else, or add it here
    /// by name with the sheet it came from.
    /// </summary>
    [TestFixture]
    public class AmenitiesAreNotHousesTests
    {
        [SetUp]
        public void InstallKinds() => TestContent.EnsureKinds();

        /// <summary>The default box the map generator gives a dwelling, measured: all 315 of
        /// Rossville's houses are exactly this and nothing else is.</summary>
        private const int HouseW = 13, HouseH = 7;

        /// <summary>
        /// The kinds a person is ASSIGNED to rather than merely visits - the ones where getting the
        /// building wrong misroutes a citizen's day rather than just looking odd. Catchment is what
        /// makes this expensive.
        /// </summary>
        private static readonly string[] Catchments = { "school", "church", "clinic", "precinct" };

        [Test]
        public void NoCatchmentAmenityHasTheDefaultHouseFootprint()
        {
            var layout = RealRossville.LayoutWithPlaces();
            var table = PlaceKindTable.Current;

            var offenders = new List<string>();
            var kept = new List<string>();

            foreach (var place in layout.Places)
            {
                string kind = table.Row(place.Kind).Name;
                if (System.Array.IndexOf(Catchments, kind) < 0) continue;

                if (place.Bounds.W == HouseW && place.Bounds.H == HouseH)
                    offenders.Add($"{kind} \"{place.Name}\" at {place.Bounds.X},{place.Bounds.Y}");
                else
                    kept.Add($"{kind} \"{place.Name}\" {place.Bounds.W}x{place.Bounds.H}");
            }

            TestContext.Out.WriteLine($"catchment amenities: {kept.Count} sized off the survey, "
                                    + $"{offenders.Count} on the default house box");
            foreach (var k in kept) TestContext.Out.WriteLine("  " + k);

            Assert.That(kept, Is.Not.Empty,
                "no catchment amenities were found at all, so this test is reading nothing - "
              + "either city.txt stopped loading or the kind names moved.");

            Assert.That(offenders, Is.Empty,
                "These are houses wearing an amenity's name:\n  " + string.Join("\n  ", offenders)
              + "\n\nEvery one of Rossville's 315 houses is exactly 13x7, so an amenity with that "
              + "footprint is a dwelling that was re-KINDED rather than taken down. It is not a "
              + "cosmetic fault: catchment goes to the nearest amenity of the kind, and seven such "
              + "schools sent 75.2% of the town's dwellings to a 91 m2 house - one of which "
              + "SeatOnSurvey then seated onto a 43 m2 outbuilding.");
        }

        /// <summary>
        /// And the town still HAS the amenities it needs, which is the other half: deleting the
        /// seven schools by turning them into houses must not leave a town with no school in it.
        /// This is what stops a future session satisfying the test above by deletion.
        /// </summary>
        [Test]
        public void RossvilleStillHasTheAmenitiesItsPeopleAreSentTo()
        {
            var layout = RealRossville.LayoutWithPlaces();
            var table = PlaceKindTable.Current;

            var counts = new SortedDictionary<string, int>();
            foreach (var place in layout.Places)
            {
                string kind = table.Row(place.Kind).Name;
                if (System.Array.IndexOf(Catchments, kind) < 0) continue;
                counts.TryGetValue(kind, out int had);
                counts[kind] = had + 1;
            }

            TestContext.Out.WriteLine("catchment amenities: "
                + string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}")));

            Assert.That(counts.ContainsKey("school") && counts["school"] >= 2, Is.True,
                "Rossville has a grade school and a high school and both are on the survey. If "
              + "this fails, somebody satisfied NoCatchmentAmenityHasTheDefaultHouseFootprint by "
              + "deleting schools rather than by taking the house boxes off the roll.");

            Assert.That(counts.ContainsKey("church"), Is.True, "the town has a church");
        }
    }
}
