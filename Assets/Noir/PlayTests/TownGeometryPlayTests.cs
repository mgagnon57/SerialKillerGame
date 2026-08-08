using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.PlayTests
{
    /// <summary>
    /// THE TOWN AS THE GAME ACTUALLY BUILDS IT.
    ///
    /// Everything else that ever checked "is a house standing in the road" checked a FILE. The
    /// Core tests read Content/city.txt and Content/roads.txt and reason about them, which is
    /// worth doing and is not the same claim: the town on screen is those files put through five
    /// survey passes - SurveyRoads, RuledAway, SeatOnSurvey, DowntownFromSanborn, FillFromSurvey -
    /// and then ClearOfRoads. Any one of them can move a building, and only this assembly can see
    /// the result.
    ///
    /// That gap is exactly how the owner's complaint survived for weeks. He said houses were
    /// standing in the road; the file-level tests said otherwise and were right about the files.
    ///
    /// No Category attribute, deliberately - these must run inside the `!Diagnostic` gate, which
    /// is the suite anybody actually runs.
    /// </summary>
    public class TownGeometryPlayTests
    {
        /// <summary>
        /// How far into a carriageway a building may reach. Same number SeatOnSurvey and
        /// ClearOfRoads use: the footprints are traced from imagery and the centrelines come from
        /// the county, so the two disagree by tens of centimetres everywhere without either being
        /// wrong.
        /// </summary>
        private const float Tolerance = 0.5f;

        [UnityTest]
        public IEnumerator NoBuildingStandsInAStreet()
        {
            yield return CityUnderTest.WaitUntilBuilt();

            var world = CityUnderTest.World;
            Assert.That(world, Is.Not.Null, "the town did not build");

            var table = PlaceKindTable.Current;
            var offenders = new List<string>();
            float worst = 0f;

            foreach (var place in world.AllPlaces)
            {
                if (!table.Row(place.Kind).IsBuilding) continue;

                float pen = place.Outline != null && place.Outline.Length >= 3
                    ? RoadCorridor.WorstPenetration(world.Roads, place.Outline, RoadCorridor.StreetWidth)
                    : RoadCorridor.WorstPenetration(world.Roads, place.Bounds, RoadCorridor.StreetWidth);

                if (pen <= Tolerance) continue;
                if (pen > worst) worst = pen;
                offenders.Add($"{place.Name} {place.Bounds} — {pen:0.0} m into "
                            + RoadCorridor.RoadUnder(world.Roads, place.Bounds, RoadCorridor.StreetWidth));
            }

            Debug.Log($"[geometry] {offenders.Count} buildings standing in a street, worst {worst:0.0} m");
            foreach (var o in offenders) Debug.Log("[geometry]   " + o);

            // A RATCHET AT THE MEASURED VALUE, AND AN OPEN QUESTION - said plainly rather than
            // tuned into a green tick.
            //
            // The layout-level test (ClearOfRoadsTests.NoAuthoredBuildingIsLeftStandingInARoad)
            // measures city.txt against roads.txt and reports ZERO. This measures the same town
            // after the pipeline and reports 134. Both cannot be describing the same thing, and
            // the difference is NOT the passes - ClearOfRoads runs last and leaves 8 stuck, not
            // 134. It is the ROAD: a layout carries RoadRun, a list of points with a declared
            // width; the world carries RoadLine wrapping a RoadPath that has been smoothed and
            // resampled, and RoadPath.Project is being asked for a lateral distance against that
            // smoothed curve. Until somebody establishes which of the two corridors the tarmac is
            // actually drawn to, the honest number is "134, and it must not grow".
            //
            // Ratcheting rather than asserting zero because zero here would mean deleting the
            // test, and this is the only assertion in the project that can see the built town at
            // all. That blindness is why "there are houses in the road" survived for weeks.
            Assert.That(offenders.Count, Is.LessThanOrEqualTo(134),
                "More buildings stand in a street in the BUILT town than before. Either a pass "
                + "moved something into a road, or the corridor the world draws has changed "
                + "shape. Worst offender: " + (offenders.Count > 0 ? offenders[0] : "none"));
        }

        /// <summary>
        /// Every road the town drives on has at least two points and a width its class allows.
        /// A one-point road is not a road; a zero-width one is drawn as nothing and is invisible
        /// in every render, which is the quietest possible way to lose a street.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryRoadIsDrawable()
        {
            yield return CityUnderTest.WaitUntilBuilt();
            var world = CityUnderTest.World;

            foreach (var line in world.Roads.Lines)
            {
                Assert.That(line.Path, Is.Not.Null, $"{line.Name} has no path");
                Assert.That(line.Path.Length, Is.GreaterThan(0f),
                            $"{line.Name} has zero length - it is drawn as nothing, which is the "
                          + "quietest possible way to lose a street");
                Assert.That(line.Width, Is.GreaterThan(0), $"{line.Name} is zero wide");
            }

            Debug.Log($"[geometry] {world.Roads.Lines.Count} roads, all drawable");
        }

        /// <summary>
        /// The whole town is walkable as one piece.
        ///
        /// Two regions means somebody's front door opens onto ground nothing else can reach, and
        /// the people behind it will stand still all day for reasons no log explains. This has
        /// happened before, from a door left outside its own wall.
        /// </summary>
        [UnityTest]
        public IEnumerator TheTownIsOnePiece()
        {
            yield return CityUnderTest.WaitUntilBuilt();
            var world = CityUnderTest.World;

            var report = WorldValidator.Validate(world);
            var split = new List<string>();
            foreach (var e in report.Errors)
                if (e.Contains("separate from the main")) split.Add(e);

            Debug.Log($"[geometry] validator: {report.Errors.Count} error(s), "
                    + $"{report.Warnings.Count} warning(s)");
            foreach (var e in report.Errors) Debug.Log("[geometry]   ERROR " + e);

            // A RATCHET, NOT A ZERO, and the honesty matters more than the green tick. Rossville
            // has ONE walkable area cut off from the rest and it predates this test - it is not
            // known yet whether that is a door outside its own wall, a yard fully enclosed by
            // buildings, or ground the survey legitimately walled off. Asserting zero today would
            // mean deleting the test tomorrow; asserting "no worse" catches the thing that
            // actually matters, which is a change that cuts the town further.
            Assert.That(split.Count, Is.LessThanOrEqualTo(1),
                "The town has been cut into more pieces than it was. Somebody's front door now "
                + "opens onto ground nothing can reach, and the people behind it will stand still "
                + "all day with nothing in the log to say why.");
        }
    }
}
