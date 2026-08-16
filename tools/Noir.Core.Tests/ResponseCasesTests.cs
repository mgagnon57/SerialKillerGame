using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.Response;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The case machine, hand-fed minute by minute. Every threshold below is
    /// <see cref="ResponseCases"/>'s own published constant, so a passing run here is a promise
    /// that a later task can drive the same machine off the sim clock and get the same orders at
    /// the same minutes.
    /// </summary>
    [TestFixture]
    public class ResponseCasesTests
    {
        [Test]
        public void AWitnessedHitRunsTheWholeResponse()
        {
            var cases = new ResponseCases();
            var orders = new List<CaseOrder>();
            int id = cases.Open(new CitizenId(7), 1000, new Tile(50, 50),
                                CarTone.Dark, CarShape.Pickup, fatal: false);
            Assert.That(cases.StateOf(id), Is.EqualTo(CaseState.Undiscovered));

            cases.BodySeen(id, 1000, new CitizenId(3));
            cases.Tick(1000, orders);
            Assert.That(orders, Is.Empty, "the alarm takes AlarmDelayMinutes to raise");

            cases.Tick(1004, orders);
            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.DispatchOfficer));
            orders.Clear();
            cases.Tick(1005, orders);
            Assert.That(orders, Is.Empty, "an order is emitted exactly once");

            cases.OfficerDispatched(id, new CitizenId(12));
            cases.OfficerArrived(id, 1010);
            cases.Tick(1027, orders);   // 1010 + 18 = 1028: not yet
            Assert.That(orders, Is.Empty);
            cases.Tick(1028, orders);
            Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.CountyCarIn));
            orders.Clear();

            cases.CountyArrived(id, 1033);
            cases.CanvassBegins(id, new[] { new CitizenId(3), new CitizenId(9) });
            cases.Tick(1033, orders);
            Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.CanvassNext));
            Assert.That(orders[0].Who, Is.EqualTo(new CitizenId(3)));
            orders.Clear();

            cases.CountyReachedDoor(id, 1036, new CitizenId(3), new[] { "16:40, I saw a dark pickup hit somebody." });
            cases.Tick(1040, orders);   // 1036 + 5 = 1041: still dwelling
            Assert.That(orders, Is.Empty);
            cases.Tick(1041, orders);
            Assert.That(orders[0].Who, Is.EqualTo(new CitizenId(9)));
            orders.Clear();

            cases.CountyReachedDoor(id, 1044, new CitizenId(9), new[] { "Nothing. I never saw anybody." });
            cases.Tick(1049, orders);   // canvass done at 1044+5=1049 -> ambulance due 1049+10=1059
            Assert.That(orders, Is.Empty);
            cases.Tick(1059, orders);
            Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.AmbulanceIn));
            orders.Clear();

            cases.AmbulanceArrived(id, 1065);
            cases.Tick(1068, orders);   // 1065 + 3 loading
            Assert.That(orders.Select(o => o.Kind), Is.EqualTo(new[]
                { OrderKind.TakeBodyAway, OrderKind.VehiclesLeave, OrderKind.ReleaseOfficer }));
            Assert.That(orders[0].Who, Is.EqualTo(new CitizenId(7)), "the body order names the victim");
            Assert.That(orders[2].Who, Is.EqualTo(new CitizenId(12)), "the release order names the officer");
            Assert.That(cases.StateOf(id), Is.EqualTo(CaseState.Closed));
            Assert.That(cases.ReturnMinuteOf(id), Is.EqualTo(1000 + 3 * 1440));

            var file = cases.FileOf(id);
            Assert.That(file.Any(l => l.Contains("dark pickup")), "the canvass answer is in the file");
        }

        [Test]
        public void AFatalCaseNeverReturns()
        {
            var cases = new ResponseCases();
            int id = cases.Open(new CitizenId(1), 500, new Tile(10, 10),
                                CarTone.Mid, CarShape.Car, fatal: true);
            Assert.That(cases.ReturnMinuteOf(id), Is.EqualTo(int.MaxValue));
        }

        /// <summary>
        /// Drive to Canvassing exactly as the happy-path test does, but hand CanvassBegins an
        /// empty witness list. Nobody to knock on a door for still costs the county car time on
        /// scene: AmbulanceIn must land at countyArrivedMinute + NoWitnessSceneMinutes +
        /// AmbulanceOffMapMinutes, and not one minute before.
        /// </summary>
        [Test]
        public void AnEmptyCanvassStillWorksTheScene()
        {
            var cases = new ResponseCases();
            var orders = new List<CaseOrder>();
            int id = cases.Open(new CitizenId(2), 2000, new Tile(20, 20),
                                CarTone.Light, CarShape.Van, fatal: false);

            cases.BodySeen(id, 2000, new CitizenId(5));
            cases.Tick(2004, orders);   // 2000 + AlarmDelayMinutes(4)
            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.DispatchOfficer));
            orders.Clear();

            cases.OfficerDispatched(id, new CitizenId(20));
            cases.OfficerArrived(id, 2010);
            cases.Tick(2028, orders);   // 2010 + CountyOffMapMinutes(18)
            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.CountyCarIn));
            orders.Clear();

            cases.CountyArrived(id, 2033);
            cases.CanvassBegins(id, new CitizenId[0]);

            // 2033 + NoWitnessSceneMinutes(10) + AmbulanceOffMapMinutes(10) = 2053
            cases.Tick(2052, orders);
            Assert.That(orders, Is.Empty, "not a minute before 2053");
            cases.Tick(2053, orders);
            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.AmbulanceIn));
        }

        /// <summary>
        /// Rossville has one response team. Case A is opened first (lower id) but discovered
        /// SECOND, while case B — opened later, higher id — is already well into its own response.
        /// The controller's ruling: the active case is sticky by discovery order, not by id, and a
        /// later discovery never preempts. A must sit at Alarm, emitting nothing, until B closes;
        /// and A's own alarm delay is measured from whichever is later — its own discovery, or the
        /// minute B actually closed — never from its discovery alone, which would have already
        /// elapsed by the time A gets its turn.
        /// </summary>
        [Test]
        public void ALaterDiscoveredLowerIdCaseNeverPreemptsTheActiveOne()
        {
            var cases = new ResponseCases();
            var orders = new List<CaseOrder>();

            int idA = cases.Open(new CitizenId(101), 100, new Tile(1, 1), CarTone.Mid, CarShape.Car, fatal: false);
            int idB = cases.Open(new CitizenId(102), 200, new Tile(2, 2), CarTone.Dark, CarShape.Van, fatal: false);
            Assert.That(idA, Is.LessThan(idB), "A must have the lower id for this test to prove anything");

            // B is discovered first and becomes active.
            cases.BodySeen(idB, 210, new CitizenId(103));
            cases.Tick(214, orders);   // 210 + AlarmDelayMinutes
            Assert.That(orders.Select(o => o.Case), Is.EqualTo(new[] { idB }));
            orders.Clear();

            cases.OfficerDispatched(idB, new CitizenId(105));
            cases.OfficerArrived(idB, 220);
            cases.Tick(238, orders);   // 220 + CountyOffMapMinutes
            Assert.That(orders.Select(o => o.Case), Is.EqualTo(new[] { idB }));
            orders.Clear();

            cases.CountyArrived(idB, 245);   // B is now well past CountyArrived: mid-response

            // A is discovered while B is still being worked. Lower id, later discovery: must NOT
            // preempt B, whatever its id.
            cases.BodySeen(idA, 250, new CitizenId(104));
            Assert.That(cases.StateOf(idA), Is.EqualTo(CaseState.Alarm));

            cases.CanvassBegins(idB, new CitizenId[0]);

            // (a) subsequent Ticks keep emitting orders for B only, (b) A gets nothing.
            cases.Tick(250, orders);
            Assert.That(orders, Is.Empty);
            cases.Tick(255, orders);   // 245 + NoWitnessSceneMinutes: canvass done, ambulance due 265
            Assert.That(orders, Is.Empty);
            Assert.That(cases.StateOf(idA), Is.EqualTo(CaseState.Alarm), "A has not moved");

            cases.Tick(265, orders);   // ambulance due
            Assert.That(orders.Select(o => o.Case), Is.EqualTo(new[] { idB }));
            Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.AmbulanceIn));
            orders.Clear();
            Assert.That(cases.StateOf(idA), Is.EqualTo(CaseState.Alarm), "still nothing for A");

            cases.AmbulanceArrived(idB, 270);
            cases.Tick(273, orders);   // 270 + LoadingMinutes: B closes here
            Assert.That(orders.Select(o => o.Case), Is.EqualTo(new[] { idB, idB, idB }));
            Assert.That(cases.StateOf(idB), Is.EqualTo(CaseState.Closed));
            orders.Clear();

            // (c) A's alarm floor is max(discoveredAt=250, B's close minute=273) = 273, so
            // DispatchOfficer fires at 277, not at 254 (250 + AlarmDelayMinutes) which would
            // already have elapsed had A's own discovery alone been the floor.
            cases.Tick(276, orders);   // one minute short of 277
            Assert.That(orders, Is.Empty, "A's alarm floor is B's close minute, not its own discovery");
            cases.Tick(277, orders);   // 273 + AlarmDelayMinutes
            Assert.That(orders.Select(o => o.Case), Is.EqualTo(new[] { idA }));
            Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.DispatchOfficer));
        }

        [Test]
        public void AWrongStateArrivalCallThrows()
        {
            var cases = new ResponseCases();
            int id = cases.Open(new CitizenId(1), 100, new Tile(5, 5), CarTone.Mid, CarShape.Car, fatal: false);

            // The case is still Undiscovered: nobody can have an officer arrive at it yet.
            Assert.Throws<InvalidOperationException>(() => cases.OfficerArrived(id, 105));
        }

        [Test]
        public void CountyReachedDoorThrowsOnAMisattributedWitness()
        {
            var cases = new ResponseCases();
            var orders = new List<CaseOrder>();
            int id = cases.Open(new CitizenId(1), 1000, new Tile(5, 5), CarTone.Mid, CarShape.Car, fatal: false);

            cases.BodySeen(id, 1000, new CitizenId(3));
            cases.Tick(1004, orders);
            orders.Clear();
            cases.OfficerDispatched(id, new CitizenId(12));
            cases.OfficerArrived(id, 1010);
            cases.Tick(1028, orders);
            orders.Clear();
            cases.CountyArrived(id, 1033);
            cases.CanvassBegins(id, new[] { new CitizenId(3), new CitizenId(9) });
            cases.Tick(1033, orders);   // emits CanvassNext for citizen 3
            orders.Clear();

            // The outstanding order names citizen 3; reporting citizen 9's answer against it must
            // throw rather than file the words under the wrong name.
            Assert.Throws<InvalidOperationException>(() =>
                cases.CountyReachedDoor(id, 1036, new CitizenId(9), new[] { "wrong door" }));
        }

        /// <summary>
        /// The officer dispatched to a case can be struck the same way the case's own victim was
        /// — or the host may simply have found nobody free to send. Either way OfficerLost must
        /// clear the officer and make the very next Tick ask again, exactly as if the alarm had
        /// only just fired.
        /// </summary>
        [Test]
        public void ADownedOfficerIsReplaced()
        {
            var cases = new ResponseCases();
            var orders = new List<CaseOrder>();
            int id = cases.Open(new CitizenId(1), 1000, new Tile(5, 5), CarTone.Mid, CarShape.Car, fatal: false);

            cases.BodySeen(id, 1000, new CitizenId(3));
            cases.Tick(1004, orders);
            Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.DispatchOfficer));
            orders.Clear();

            cases.OfficerDispatched(id, new CitizenId(12));
            Assert.That(cases.OfficerOf(id), Is.EqualTo(new CitizenId(12)));

            cases.OfficerLost(id);
            Assert.That(cases.OfficerOf(id), Is.EqualTo(CitizenId.None),
                "no officer until the host reports a replacement");

            cases.Tick(1005, orders);
            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.DispatchOfficer));

            cases.OfficerDispatched(id, new CitizenId(20));
            Assert.That(cases.OfficerOf(id), Is.EqualTo(new CitizenId(20)));
        }

        [Test]
        public void TheLogRunsForwardAndDrains()
        {
            var cases = new ResponseCases();
            var orders = new List<CaseOrder>();
            int id = cases.Open(new CitizenId(7), 1000, new Tile(50, 50),
                                CarTone.Dark, CarShape.Pickup, fatal: false);

            cases.BodySeen(id, 1000, new CitizenId(3));
            cases.Tick(1004, orders);
            orders.Clear();
            cases.OfficerDispatched(id, new CitizenId(12));
            cases.OfficerArrived(id, 1010);
            cases.Tick(1028, orders);
            orders.Clear();
            cases.CountyArrived(id, 1033);

            var log = new List<string>();
            cases.DrainLog(log);
            Assert.That(log.Count, Is.GreaterThan(0));

            int discovered = log.FindIndex(l => l.Contains("discovered at minute"));
            int alarm = log.FindIndex(l => l.Contains("alarm raised"));
            int officerOnScene = log.FindIndex(l => l.Contains("officer on scene"));
            int countySentFor = log.FindIndex(l => l.Contains("county car sent for"));
            int countyOnScene = log.FindIndex(l => l.Contains("county car on scene"));

            Assert.That(discovered, Is.EqualTo(0), "discovered is the very first line");
            Assert.That(alarm, Is.GreaterThan(discovered), "discovered before the alarm is raised");
            Assert.That(officerOnScene, Is.GreaterThan(alarm), "the alarm before the officer arrives");
            Assert.That(countySentFor, Is.GreaterThan(officerOnScene));
            Assert.That(countyOnScene, Is.GreaterThan(countySentFor));

            var second = new List<string>();
            cases.DrainLog(second);
            Assert.That(second, Is.Empty, "a drain empties the queue; a second drain finds nothing new");
        }

        /// <summary>
        /// CloseLoudly is the escape hatch nothing should reach: verifies it can still force-close
        /// the ACTIVE case, free the slot for whoever queued behind it (same machinery as a normal
        /// close), and that the cleanup pair — VehiclesLeave, ReleaseOfficer — lands on the very
        /// next Tick rather than being lost, since CloseLoudly itself has no orders list to write
        /// into.
        /// </summary>
        [Test]
        public void CloseLoudlyFreesTheSlotAndQueuesCleanupOrders()
        {
            var cases = new ResponseCases();
            var orders = new List<CaseOrder>();

            int idA = cases.Open(new CitizenId(1), 100, new Tile(1, 1), CarTone.Mid, CarShape.Car, fatal: false);
            int idB = cases.Open(new CitizenId(2), 300, new Tile(2, 2), CarTone.Dark, CarShape.Van, fatal: false);

            cases.BodySeen(idA, 100, new CitizenId(9));
            cases.Tick(104, orders);   // DispatchOfficer for A
            orders.Clear();
            cases.OfficerDispatched(idA, new CitizenId(12));
            cases.OfficerArrived(idA, 110);   // SceneHeld, officer 12 on scene
            cases.Tick(110, orders);          // nothing due yet; keeps the clock current
            orders.Clear();

            // B queues behind A while A is still active.
            cases.BodySeen(idB, 115, new CitizenId(8));
            Assert.That(cases.StateOf(idB), Is.EqualTo(CaseState.Alarm));

            // A's victim state stops making sense; the host force-closes rather than keep asking
            // for orders against a story that is no longer true.
            cases.CloseLoudly(idA, "victim turned up alive");
            Assert.That(cases.StateOf(idA), Is.EqualTo(CaseState.Closed));

            // The very next Tick emits A's cleanup pair before anything else, and the slot has
            // already passed to B.
            cases.Tick(112, orders);
            Assert.That(orders.Select(o => o.Kind), Is.EqualTo(new[] { OrderKind.VehiclesLeave, OrderKind.ReleaseOfficer }));
            Assert.That(orders[0].Case, Is.EqualTo(idA));
            Assert.That(orders[1].Who, Is.EqualTo(new CitizenId(12)), "the officer who was on scene is released");
            orders.Clear();

            // B's own alarm floor (max(115, A's close minute 110) = 115) proceeds normally,
            // proving the slot really did pass rather than the queue silently freezing.
            cases.Tick(118, orders);
            Assert.That(orders, Is.Empty);
            cases.Tick(119, orders);
            Assert.That(orders.Select(o => o.Case), Is.EqualTo(new[] { idB }));
            Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.DispatchOfficer));
        }
    }
}
