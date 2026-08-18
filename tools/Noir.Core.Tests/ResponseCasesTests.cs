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

        /// <summary>
        /// Phase 2 of witness voices: a badge-on ask lands the witness's words in the file
        /// through a sibling of the canvass's own seam — verbatim, prefixed so a reader can
        /// tell a volunteered county answer from one the player's badge collected.
        /// </summary>
        [Test]
        public void ABadgeAskLandsTheWordsInTheFile()
        {
            var cases = new ResponseCases();
            var orders = new List<CaseOrder>();
            int id = cases.Open(new CitizenId(7), 1000, new Tile(50, 50),
                                CarTone.Dark, CarShape.Pickup, fatal: false);
            cases.BodySeen(id, 1000, new CitizenId(3));
            cases.Tick(1004, orders);

            cases.BadgeAsked(id, new CitizenId(41), new[] { "16:30, I saw a dark van hit somebody." });

            var file = cases.FileOf(id);
            Assert.That(file.Any(l => l.Contains("citizen 41 told the badge: 16:30, I saw a dark van hit somebody.")),
                "the badge ask should be in the file verbatim: " + string.Join(" | ", file));
        }

        [Test]
        public void ABadgeAskOnAnUndiscoveredCaseThrows()
        {
            var cases = new ResponseCases();
            int id = cases.Open(new CitizenId(7), 1000, new Tile(50, 50),
                                CarTone.Dark, CarShape.Pickup, fatal: false);
            Assert.Throws<InvalidOperationException>(() =>
                cases.BadgeAsked(id, new CitizenId(41), new[] { "anything" }),
                "the town cannot file what it does not know exists");
        }

        [Test]
        public void ABadgeAskOnAClosedCaseThrows()
        {
            var cases = new ResponseCases();
            int id = cases.Open(new CitizenId(7), 1000, new Tile(50, 50),
                                CarTone.Dark, CarShape.Pickup, fatal: false);
            cases.CloseLoudly(id, "test: shutting the file");
            Assert.Throws<InvalidOperationException>(() =>
                cases.BadgeAsked(id, new CitizenId(41), new[] { "anything" }));
        }

        /// <summary>
        /// The badge writer must be a BYSTANDER to the canvass state machine: after a badge ask
        /// mid-canvass, the outstanding CanvassNext witness is still the one CountyReachedDoor
        /// expects, and the next door comes due on the county's own clock, unmoved.
        /// </summary>
        [Test]
        public void ABadgeAskDoesNotAdvanceTheCanvass()
        {
            var cases = new ResponseCases();
            var orders = new List<CaseOrder>();
            int id = cases.Open(new CitizenId(7), 1000, new Tile(50, 50),
                                CarTone.Dark, CarShape.Pickup, fatal: false);
            cases.BodySeen(id, 1000, new CitizenId(3));
            cases.Tick(1004, orders); orders.Clear();
            cases.OfficerDispatched(id, new CitizenId(12));
            cases.OfficerArrived(id, 1010);
            cases.Tick(1028, orders); orders.Clear();
            cases.CountyArrived(id, 1033);
            cases.CanvassBegins(id, new[] { new CitizenId(3), new CitizenId(9) });
            cases.Tick(1033, orders);
            Assert.That(orders[0].Who, Is.EqualTo(new CitizenId(3)));
            orders.Clear();

            cases.BadgeAsked(id, new CitizenId(41), new[] { "Nothing. I never saw anybody." });

            // The canvass neither skipped citizen 3 nor reset its clock: their answer still lands,
            // and citizen 9 comes due exactly CanvassMinutesPerDoor after it, as ever.
            cases.CountyReachedDoor(id, 1036, new CitizenId(3), new[] { "Nothing. I never saw anybody." });
            cases.Tick(1040, orders);
            Assert.That(orders, Is.Empty, "the door clock must be the county's own, unmoved by the badge");
            cases.Tick(1041, orders);
            Assert.That(orders[0].Who, Is.EqualTo(new CitizenId(9)));
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

        /// <summary>
        /// A bender: neither driver hurt. The interview queue is [Victim, Other] — the at-fault
        /// driver first, then the one hit into — paced exactly like the canvass's own
        /// once-per-index/NextDoorReadyAt clock. With LetGo as the verdict, once both have
        /// answered the case skips straight to a verdict and closes: no ambulance ever runs.
        /// </summary>
        [Test]
        public void ABenderRunsInterviewsAndAVerdictAndNoAmbulance()
        {
            var cases = new ResponseCases();
            var orders = new List<CaseOrder>();
            var allOrders = new List<CaseOrder>();
            var atFault = new CitizenId(7);
            var other = new CitizenId(8);
            int id = cases.OpenCollision(atFault, other, 1000, new Tile(50, 50),
                                         CarTone.Dark, CarShape.Pickup, injury: false, verdict: CrashVerdict.LetGo);

            cases.BodySeen(id, 1000, new CitizenId(3));
            cases.Tick(1004, orders);   // 1000 + AlarmDelayMinutes
            Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.DispatchOfficer));
            allOrders.AddRange(orders); orders.Clear();

            cases.OfficerDispatched(id, new CitizenId(12));
            cases.OfficerArrived(id, 1010);
            cases.Tick(1028, orders);   // 1010 + CountyOffMapMinutes
            Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.CountyCarIn));
            allOrders.AddRange(orders); orders.Clear();

            cases.CountyArrived(id, 1033);
            cases.Tick(1033, orders);
            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.InterviewDriver));
            Assert.That(orders[0].Who, Is.EqualTo(atFault), "the at-fault driver is interviewed first");
            allOrders.AddRange(orders); orders.Clear();

            cases.CountyReachedDoor(id, 1036, atFault, new[] { "It was my fault, I ran the light." });
            cases.Tick(1040, orders);   // 1036 + CanvassMinutesPerDoor(5) = 1041: still dwelling
            Assert.That(orders, Is.Empty);
            cases.Tick(1041, orders);
            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.InterviewDriver));
            Assert.That(orders[0].Who, Is.EqualTo(other), "the other driver is interviewed second");
            allOrders.AddRange(orders); orders.Clear();

            cases.CountyReachedDoor(id, 1044, other, new[] { "He ran the light and hit me." });
            cases.Tick(1048, orders);   // 1044 + 5 = 1049: still dwelling
            Assert.That(orders, Is.Empty);
            cases.Tick(1049, orders);
            Assert.That(orders.Select(o => o.Kind), Is.EqualTo(new[]
                { OrderKind.ReleaseDrivers, OrderKind.VehiclesLeave, OrderKind.ReleaseOfficer }));
            Assert.That(orders[0].Who, Is.EqualTo(CitizenId.None));
            Assert.That(orders[2].Who, Is.EqualTo(new CitizenId(12)), "the release order names the officer");
            Assert.That(cases.StateOf(id), Is.EqualTo(CaseState.Closed));
            allOrders.AddRange(orders);

            Assert.That(allOrders.Any(o => o.Kind == OrderKind.AmbulanceIn), Is.False,
                "a bender never calls an ambulance");
            Assert.That(allOrders.Any(o => o.Kind == OrderKind.TakeBodyAway), Is.False,
                "nobody was hurt, so nobody is carried away");
        }

        /// <summary>
        /// A DUI verdict ends in an arrest and a tow, in that order, before the case's ordinary
        /// close.
        /// </summary>
        [Test]
        public void DrinkEndsInArrestAndATow()
        {
            var cases = new ResponseCases();
            var orders = new List<CaseOrder>();
            var atFault = new CitizenId(7);
            var other = new CitizenId(8);
            int id = cases.OpenCollision(atFault, other, 1000, new Tile(50, 50),
                                         CarTone.Dark, CarShape.Pickup, injury: false, verdict: CrashVerdict.Dui);

            cases.BodySeen(id, 1000, new CitizenId(3));
            cases.Tick(1004, orders); orders.Clear();
            cases.OfficerDispatched(id, new CitizenId(12));
            cases.OfficerArrived(id, 1010);
            cases.Tick(1028, orders); orders.Clear();
            cases.CountyArrived(id, 1033);

            cases.Tick(1033, orders);   // InterviewDriver(atFault)
            orders.Clear();
            cases.CountyReachedDoor(id, 1036, atFault, new[] { "I might've had a couple." });
            cases.Tick(1041, orders);   // InterviewDriver(other)
            orders.Clear();
            cases.CountyReachedDoor(id, 1044, other, new[] { "He swerved right into me." });

            cases.Tick(1049, orders);   // interviews done -> verdict -> close, all in one tick
            Assert.That(orders.Select(o => o.Kind), Is.EqualTo(new[]
                { OrderKind.ArrestDriver, OrderKind.TowVehicle, OrderKind.VehiclesLeave, OrderKind.ReleaseOfficer }));
            Assert.That(orders[0].Who, Is.EqualTo(atFault), "the arrest names the at-fault driver");
            Assert.That(orders[1].Who, Is.EqualTo(atFault), "the tow follows the arrested driver's own car");
            Assert.That(cases.StateOf(id), Is.EqualTo(CaseState.Closed));
            Assert.That(cases.ReturnMinuteOf(id), Is.EqualTo(1000 + 3 * 1440),
                "the lockup costs the same days as the hospital, in phase 1");

            var file = cases.FileOf(id);
            Assert.That(file.Any(l => l.Contains("under arrest")),
                "the file should say the driver is under arrest: " + string.Join(" | ", file));
            Assert.That(file.Any(l => l.Contains("tow")),
                "the file should mention the tow: " + string.Join(" | ", file));
        }

        /// <summary>
        /// The at-fault driver was hurt badly enough that the roadside interview skips him
        /// entirely — only the other driver answers — and he goes off in the ambulance instead,
        /// the same arc a person-hit gets, before the case still reaches a verdict.
        /// </summary>
        [Test]
        public void AnInjuredDriverIsNotInterviewedButIsTakenAway()
        {
            var cases = new ResponseCases();
            var orders = new List<CaseOrder>();
            var allOrders = new List<CaseOrder>();
            var atFault = new CitizenId(7);
            var other = new CitizenId(8);
            int id = cases.OpenCollision(atFault, other, 1000, new Tile(50, 50),
                                         CarTone.Dark, CarShape.Pickup, injury: true, verdict: CrashVerdict.Ticket);

            cases.BodySeen(id, 1000, new CitizenId(3));
            cases.Tick(1004, orders); allOrders.AddRange(orders); orders.Clear();
            cases.OfficerDispatched(id, new CitizenId(12));
            cases.OfficerArrived(id, 1010);
            cases.Tick(1028, orders); allOrders.AddRange(orders); orders.Clear();
            cases.CountyArrived(id, 1033);

            cases.Tick(1033, orders);
            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.InterviewDriver));
            Assert.That(orders[0].Who, Is.EqualTo(other),
                "the injured at-fault driver cannot be interviewed at the roadside");
            allOrders.AddRange(orders); orders.Clear();

            cases.CountyReachedDoor(id, 1036, other, new[] { "The other car ran the stop sign." });

            // interview exhausted at 1041 (1036+5); ambulance due 1041+10=1051
            cases.Tick(1040, orders);
            Assert.That(orders, Is.Empty);
            cases.Tick(1050, orders);
            Assert.That(orders, Is.Empty, "not a minute before the ambulance is due");
            cases.Tick(1051, orders);
            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders[0].Kind, Is.EqualTo(OrderKind.AmbulanceIn));
            allOrders.AddRange(orders); orders.Clear();

            cases.AmbulanceArrived(id, 1057);
            cases.Tick(1060, orders);   // 1057 + LoadingMinutes(3)
            Assert.That(orders.Select(o => o.Kind), Is.EqualTo(new[]
                { OrderKind.TakeBodyAway, OrderKind.TicketDriver, OrderKind.VehiclesLeave, OrderKind.ReleaseOfficer }));
            Assert.That(orders[0].Who, Is.EqualTo(atFault), "the ambulance carries off the at-fault driver");
            Assert.That(orders[1].Who, Is.EqualTo(atFault), "the ticket still names the at-fault driver");
            Assert.That(cases.StateOf(id), Is.EqualTo(CaseState.Closed));
            allOrders.AddRange(orders);

            Assert.That(allOrders.Count(o => o.Kind == OrderKind.InterviewDriver), Is.EqualTo(1),
                "exactly one interview: the at-fault driver was too hurt to answer questions");
        }

        [Test]
        public void KindOfTellsTheTwoCasesApart()
        {
            var cases = new ResponseCases();
            int personHit = cases.Open(new CitizenId(1), 100, new Tile(1, 1),
                                       CarTone.Mid, CarShape.Car, fatal: false);
            int collision = cases.OpenCollision(new CitizenId(2), new CitizenId(3), 200, new Tile(2, 2),
                                                CarTone.Dark, CarShape.Van, injury: false, verdict: CrashVerdict.LetGo);

            Assert.That(cases.KindOf(personHit), Is.EqualTo(CaseKind.PersonDown));
            Assert.That(cases.KindOf(collision), Is.EqualTo(CaseKind.Collision));
            Assert.That(cases.FatalOf(collision), Is.False, "a collision injury is never fatal in phase 1");
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
