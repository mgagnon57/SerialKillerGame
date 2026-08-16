using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Response;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.PlayTests
{
    /// <summary>
    /// The whole of Phase 2, watched once by the gate: a hit in view of a door runs discovery,
    /// an officer walks over, the county drives in, the doors get knocked, an ambulance takes
    /// the body, and the case file at the end of it is real. Everything asserts on ACCESSOR
    /// STATE (host.Cases), never on log text - the [case] lines are echoed for humans.
    /// </summary>
    public class ResponsePlayTests
    {
        // Static, not instance: a UnityTearDown runs against a fresh instance of this class,
        // so instance fields would not survive between the test body and the teardown.
        private static int _victim = -1;
        private static int _caseId = -1;
        private static int _wasSpeed = -1;
        private static int _startMinuteOfDay = -1;

        [UnitySetUp]
        public IEnumerator Ready()
        {
            Time.timeScale = 1f;
            yield return CityUnderTest.WaitUntilBuilt();
        }

        /// <summary>
        /// THE CITY IS BUILT ONCE AND SHARED BY EVERY TEST IN A RUN. This test downs a real
        /// citizen, sends a real officer off-plan, and runs the clock at 300x - every one of
        /// those must be handed back on every exit path, assertion failure included, or the
        /// rest of the suite inherits a corrupted town.
        /// </summary>
        [UnityTearDown]
        public IEnumerator EverythingBack()
        {
            var host = Object.FindFirstObjectByType<VillageHost>();
            var player = Object.FindFirstObjectByType<Player>();
            if (player != null && player.Walking) player.Toggle();
            if (host != null && host.Sim != null)
            {
                if (_caseId >= 0)
                {
                    var officer = host.Cases.OfficerOf(_caseId);
                    if (officer.IsValid) host.Sim.Release(officer);
                }
                if (_victim >= 0)
                {
                    host.Sim.Return(new CitizenId(_victim));   // no-op unless taken away
                    host.Sim.Revive(new CitizenId(_victim));   // no-op unless still downed
                }

                // The sweep's segment can catch bystanders beside the chosen victim, and every
                // hit opens a case now - both are shared-town state a later test would inherit
                // as bodies in the street and responses firing mid-measurement. Stand everyone
                // back up and close every case, whatever this test's exit path was.
                for (int i = 0; i < host.Sim.AgentCount; i++)
                    if (host.Sim.GetAgent(i).Downed) host.Sim.Revive(new CitizenId(i));
                // Officers AND gawkers alike: anyone still off-plan goes back to their day,
                // whatever this test's exit path was. Release also clears an Aboard ride.
                for (int i = 0; i < host.Sim.AgentCount; i++)
                    if (host.Sim.GetAgent(i).Responding) host.Sim.Release(new CitizenId(i));
                for (int c = 0; c < host.Cases.Count; c++)
                    if (host.Cases.StateOf(c) != CaseState.Closed)
                        host.Cases.CloseLoudly(c, "test residue: the suite moves on");

                if (_wasSpeed >= 0) host.SpeedIndex = _wasSpeed;

                // GIVE BACK THE HOUR. This test runs the shared town at 300x while the county
                // car and the ambulance drive on REAL seconds, so one pass burns ~9 sim HOURS -
                // and the fixtures after this one measure whatever hour the suite leaves them.
                // First measured 2026-08-16: the traffic gates ran against a 02:18 town, fleet
                // garaged, "no car ever reached a signalised junction", four reds, none of them
                // real. A clock only goes forward, so restoring it means winding to the SAME
                // minute-of-day next day - PeopleDiagnostics' own busy-wind, chunked so the
                // runner keeps breathing. Skipped when the test barely moved the clock (an hour
                // of drift is what the suite already tolerated before this test existed).
                if (_startMinuteOfDay >= 0)
                {
                    int past = (host.Sim.Clock.MinuteOfDay - _startMinuteOfDay + 1440) % 1440;
                    if (past > 60)
                    {
                        long guard = 0;
                        while (host.Sim.Clock.MinuteOfDay != _startMinuteOfDay && guard < 1_800_000)
                        {
                            for (int t = 0; t < 50_000
                                 && host.Sim.Clock.MinuteOfDay != _startMinuteOfDay; t++)
                            { host.Sim.Tick(); guard++; }
                            yield return null;
                        }
                    }
                }
            }
            _victim = -1; _caseId = -1; _wasSpeed = -1; _startMinuteOfDay = -1;
            yield break;
        }

        /// <summary>The whole response, compressed: a hit in view of a door runs discovery →
        /// officer → county → canvass → ambulance → removal, and the case file is real.</summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator AWitnessedHitBringsTheTownsWholeResponse()
        {
            var host = Object.FindFirstObjectByType<VillageHost>();
            var sim = host.Sim;
            var player = Object.FindFirstObjectByType<Player>();
            _startMinuteOfDay = sim.Clock.MinuteOfDay;   // the hour the teardown hands back

            // A victim who is outdoors AND within testimony range (< 55 tiles, inside
            // Sightlines.NeverBeyond = 60 with margin) of some other citizen's occupied
            // door - otherwise discovery legitimately never fires. Walk the census for a
            // candidate; skip Downed/AwayFromTown/Indoor exactly as the sweep does.
            int victim = -1;
            for (int i = 0; i < sim.AgentCount && victim < 0; i++)
            {
                var a = sim.GetAgent(i);
                if (a.Downed || a.Doing == Activity.AwayFromTown) continue;
                if ((host.World.Grid.FlagsAt(a.Position.ToTile()) & TileFlags.Indoor) != 0) continue;
                // near an occupied door: any OTHER agent At a place whose door is close.
                for (int j = 0; j < sim.AgentCount; j++)
                {
                    if (j == i) continue;
                    var b = sim.GetAgent(j);
                    if (b.Travelling || !b.At.IsValid) continue;
                    if (b.Doing == Activity.Asleep) continue;
                    var door = host.World.GetPlace(b.At).Door;
                    if (Tile.ChebyshevDistance(door, a.Position.ToTile()) < 55) { victim = i; break; }
                }
            }
            Assert.That(victim, Is.GreaterThanOrEqualTo(0), "no witnessable victim in the noon town");

            int casesBefore = host.Cases.Count;
            player.Toggle();
            for (int f = 0; f < 5; f++) yield return null;

            // Two calls, not one: SweepForVictims' first-ever call only seeds its per-agent
            // motion cache and reports no hits by design - the second is the real sweep.
            var p = Space3D.ToWorld(sim.GetAgent(victim).Position);
            player.SweepForVictims(p + new Vector3(-3f, 0f, 0f), p + new Vector3(3f, 0f, 0f));
            player.SweepForVictims(p + new Vector3(-3f, 0f, 0f), p + new Vector3(3f, 0f, 0f));
            yield return null;

            Assert.That(sim.GetAgent(victim).Doing, Is.EqualTo(Activity.Downed));
            _victim = victim;
            Assert.That(host.Cases.Count, Is.EqualTo(casesBefore + 1), "the hit opened a case");
            _caseId = casesBefore;
            player.Toggle();

            _wasSpeed = host.SpeedIndex;
            host.SpeedIndex = VillageHost.Speeds.Length - 1;   // 300x

            // The full sequence is ~50-70 sim minutes ≈ 15-25 real seconds at 300x plus
            // walking/driving; poll state, not time. Give it four real minutes.
            float deadline = Time.time + 240f;
            var seen = new List<CaseState>();
            while (Time.time < deadline && host.Cases.StateOf(_caseId) != CaseState.Closed)
            {
                var s = host.Cases.StateOf(_caseId);
                if (seen.Count == 0 || seen[seen.Count - 1] != s) seen.Add(s);
                yield return null;
            }
            host.SpeedIndex = _wasSpeed; _wasSpeed = -1;

            Assert.That(host.Cases.StateOf(_caseId), Is.EqualTo(CaseState.Closed),
                "the case never closed; states seen: " + string.Join(" → ", seen));
            // The order of what we saw is the order the machine promises.
            CollectionAssert.IsOrdered(seen.Select(s => (int)s), "states ran out of order");

            // The body is gone: the victim is away, not downed, and their figure is not drawn.
            var after = sim.GetAgent(victim);
            Assert.That(after.Downed, Is.False);
            Assert.That(after.Doing, Is.EqualTo(Activity.AwayFromTown));

            // The case file is real.
            Assert.That(host.Cases.FileOf(_caseId).Count, Is.GreaterThan(3),
                "a worked case files its transitions and its canvass");
            foreach (var line in host.Cases.FileOf(_caseId)) Debug.Log("[case-file] " + line);
        }
    }
}
