using System;
using System.Collections.Generic;
using NUnit.Framework;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// WHICH ARM OF EACH UNSIGNALISED JUNCTION GIVES WAY — `ROAD-FIXES` CONS-3, and the reason it
    /// was never allowed to land without CONS-1.
    ///
    /// The rule lived in `CitySignals`, which `dotnet test` structurally cannot compile, so the
    /// one decision that starved half the town's junctions had no automated cover at all. It is
    /// `JunctionPriority.GiveWayIsNorthSouth` now, in Core, for the same reason
    /// `Headings.SideOfPath` is: it is a statement about the map.
    ///
    /// THE FAULT IS NOT THAT A TIE IS BROKEN ARBITRARILY. It is that it was broken arbitrarily in
    /// a DIRECTION. `Carries <= Carries` hands every tie in the town to the east-west road, so
    /// the arbitrariness does not average out — it accumulates. `CitySignals.cs` records what that
    /// cost the last time it fired everywhere: nine tenths of stopped vehicles waiting 119.9 s
    /// with clear road ahead.
    /// </summary>
    [TestFixture]
    public class StopSignsLandOnBothAxesTests
    {
        [SetUp]
        public void InstallKinds() => TestContent.EnsureKinds();

        private static RoadNetwork Network() => RealRossville.Network();

        /// <summary>The old rule, kept verbatim so the difference can be measured rather than
        /// asserted from memory.</summary>
        private static bool EastWestAlwaysWins(Junction j) =>
            j.NorthSouth == null || j.EastWest == null
                ? j.NorthSouth == null
                : j.NorthSouth.Carries <= j.EastWest.Carries;

        [Test]
        public void TheTownsStopSignsAreNotAllOnOneAxis()
        {
            var roads = Network();

            int nsGivesWay = 0, ewGivesWay = 0, ties = 0, byClass = 0, byCount = 0;

            foreach (var j in roads.Junctions)
            {
                if (j.NorthSouth == null || j.EastWest == null) continue;

                if (j.NorthSouth.Carries != j.EastWest.Carries) byClass++;
                else if (j.NorthSouth.Aadt != j.EastWest.Aadt) byCount++;
                else ties++;

                if (JunctionPriority.GiveWayIsNorthSouth(j)) nsGivesWay++; else ewGivesWay++;
            }

            int total = nsGivesWay + ewGivesWay;
            TestContext.Out.WriteLine(
                $"{total} junctions with two named arms: {byClass} settled by class, "
              + $"{byCount} by the county's counts, {ties} on neither");
            TestContext.Out.WriteLine(
                $"  stop signs: {nsGivesWay} on the north-south arm, {ewGivesWay} on the east-west");

            int old = 0;
            foreach (var j in roads.Junctions)
                if (j.NorthSouth != null && j.EastWest != null && EastWestAlwaysWins(j)) old++;
            TestContext.Out.WriteLine(
                $"  under the old rule: {old} on the north-south arm, {total - old} on the east-west");

            Assert.That(total, Is.GreaterThan(20),
                "hardly any junctions have two named arms, so this proves nothing");

            // NOT a 50/50 demand — the class comparison SHOULD favour whichever axis the town's
            // through routes happen to run along, and in Rossville that is real. What must not
            // happen again is every junction in the town answering the same way.
            Assert.That(Math.Min(nsGivesWay, ewGivesWay), Is.GreaterThan(total / 10),
                $"only {Math.Min(nsGivesWay, ewGivesWay)} of {total} junctions put the stop sign "
              + "on the minority axis. An arbitrary tie broken in a consistent direction is a "
              + "systematic bias, and CitySignals.cs records what it cost: 119.9 s waits with "
              + "clear road ahead.");

            // AND NO COMPASS DIRECTION SETTLES ANYTHING. Every junction the survey cannot settle
            // is settled by a property of the two ROADS — their length, then their names — so
            // this count is the one that must stay at zero.
            Assert.That(ties, Is.LessThanOrEqualTo(19),
                $"{ties} junctions are settled by neither class nor the county's counts, up from "
              + "19. That is not a failure in itself, but it is the population a tie-break has to "
              + "guess at, and it should be shrinking as the survey improves.");
        }

        /// <summary>
        /// A ROAD THE COUNTY COUNTS AS BUSIER NEVER GIVES WAY TO ONE IT COUNTS AS QUIETER.
        ///
        /// This is the property the whole item is about, and it is the one that can regress
        /// silently: `Carries` is compared before `Aadt`, so a road paved up a class but counted
        /// down could take priority over the street that actually carries the town. Measured
        /// rather than assumed.
        /// </summary>
        [Test]
        public void NoRoadTheCountyCountsAsBusierGivesWayToAQuieterOne()
        {
            var offenders = new List<string>();
            int bothCounted = 0;

            foreach (var j in Network().Junctions)
            {
                if (j.NorthSouth == null || j.EastWest == null) continue;
                if (j.NorthSouth.Aadt <= 0 || j.EastWest.Aadt <= 0) continue;
                if (j.NorthSouth.Aadt == j.EastWest.Aadt) continue;

                bothCounted++;
                bool nsGivesWay = JunctionPriority.GiveWayIsNorthSouth(j);
                bool nsIsBusier = j.NorthSouth.Aadt > j.EastWest.Aadt;

                if (nsGivesWay == nsIsBusier)
                    offenders.Add(
                        $"{j.NorthSouth.Name} ({j.NorthSouth.Aadt}/day, {j.NorthSouth.Carries}) x "
                      + $"{j.EastWest.Name} ({j.EastWest.Aadt}/day, {j.EastWest.Carries}) — the "
                      + $"busier one gives way");
            }

            TestContext.Out.WriteLine(
                $"{bothCounted} junctions where the county counted both arms differently, "
              + $"{offenders.Count} where the busier one gives way");
            foreach (var o in offenders) TestContext.Out.WriteLine("  " + o);

            Assert.That(offenders, Is.Empty, string.Join("\n  ", offenders));
        }

        /// <summary>
        /// AND THE ANSWER DOES NOT MOVE BETWEEN RUNS. The tie-break is a position hash, not an
        /// `IRng` substream and not a clock — the same junction gets the same stop sign in every
        /// run of every seed, which is what lets `CitySigns` erect a post where `CitySignals`
        /// expects one.
        /// </summary>
        [Test]
        public void TheSameJunctionAnswersTheSameWayEveryTime()
        {
            var first = new List<bool>();
            foreach (var j in Network().Junctions) first.Add(JunctionPriority.GiveWayIsNorthSouth(j));

            var again = new List<bool>();
            foreach (var j in Network().Junctions) again.Add(JunctionPriority.GiveWayIsNorthSouth(j));

            Assert.That(again, Is.EqualTo(first),
                "the give-way axis moved between two builds of the same network");
        }

        /// <summary>
        /// THE CLASS COMPARISON IS UNTOUCHED, which is the half of the rule that was never wrong:
        /// the dirt track to the big barn gives way to the road it joins, and no hash gets a vote.
        /// </summary>
        [Test]
        public void ASmallerRoadStillGivesWayToABiggerOne()
        {
            var roads = Network();
            int checkedPairs = 0;

            foreach (var j in roads.Junctions)
            {
                if (j.NorthSouth == null || j.EastWest == null) continue;
                if (j.NorthSouth.Carries == j.EastWest.Carries) continue;

                checkedPairs++;
                bool nsGivesWay = JunctionPriority.GiveWayIsNorthSouth(j);
                Assert.That(nsGivesWay, Is.EqualTo(j.NorthSouth.Carries < j.EastWest.Carries),
                    $"at {j.NorthSouth.Name} x {j.EastWest.Name} the "
                  + $"{(nsGivesWay ? "north-south" : "east-west")} arm gives way, but it carries "
                  + $"{(nsGivesWay ? j.NorthSouth.Carries : j.EastWest.Carries)} against "
                  + $"{(nsGivesWay ? j.EastWest.Carries : j.NorthSouth.Carries)}");
            }

            TestContext.Out.WriteLine($"{checkedPairs} junctions settled by class alone");
            Assert.That(checkedPairs, Is.GreaterThan(0),
                "no junction in the town joins two different classes of road, which cannot be "
              + "right on a map with alleys, streets and a state highway");
        }
    }
}
