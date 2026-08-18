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
                //
                // Anybody the town's own machinery took AWAY comes back first - the collision
                // scenario's DUI arrest, or an ambient crash's, holds a citizen off-map for
                // three sim days otherwise. Return no-ops on AwayUntilMinute == 0, so
                // plan-away commuters are untouched.
                for (int i = 0; i < host.Sim.AgentCount; i++)
                    host.Sim.Return(new CitizenId(i));
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
            // walking/driving; poll state, not time. Give it four real minutes. Along the way,
            // OBSERVE the new choreography rather than assume it: whether the officer ever rode
            // (Doing == AwayFromTown mid-OfficerEnRoute — the noon watch drives, but an on-call
            // fallback walking is not a failure, so it is logged, not asserted) and whether the
            // crowd gathered (somebody Gawking during the canvass — that one IS the contract).
            float deadline = Time.time + 240f;
            var seen = new List<CaseState>();
            bool rode = false, gawked = false, cordoned = false;
            while (Time.time < deadline && host.Cases.StateOf(_caseId) != CaseState.Closed)
            {
                var s = host.Cases.StateOf(_caseId);
                if (seen.Count == 0 || seen[seen.Count - 1] != s) seen.Add(s);

                if (s == CaseState.OfficerEnRoute && !rode)
                {
                    var officer = host.Cases.OfficerOf(_caseId);
                    if (officer.IsValid
                        && sim.GetAgent(officer).Doing == Activity.AwayFromTown) rode = true;
                }
                if (s == CaseState.Canvassing && !gawked)
                    for (int i = 0; i < sim.AgentCount && !gawked; i++)
                        gawked = sim.GetAgent(i).Doing == Activity.Gawking;

                if (!cordoned && s >= CaseState.SceneHeld && s < CaseState.Closed)
                    cordoned = GameObject.Find("Scene Cordon " + _caseId) != null;

                yield return null;
            }
            host.SpeedIndex = _wasSpeed; _wasSpeed = -1;
            Debug.Log($"[response-test] officer rode the cruiser: {rode}");

            Assert.That(gawked, Is.True,
                "nobody ever stood watching during the canvass - the crowd never gathered");

            // And the crowd goes home: within a few real seconds of the close, nobody is
            // still Gawking (dispersal keys on Closed in RunResponse's own minute).
            float dispersed = Time.time + 5f;
            bool anybody = true;
            while (Time.time < dispersed && anybody)
            {
                anybody = false;
                for (int i = 0; i < sim.AgentCount && !anybody; i++)
                    anybody = sim.GetAgent(i).Doing == Activity.Gawking;
                if (anybody) yield return null;
            }
            Assert.That(anybody, Is.False, "the crowd never dispersed after the case closed");

            Assert.That(cordoned, Is.True,
                "the cordon never went up while the scene was held - no 'Scene Cordon " + _caseId + "' object appeared");

            // And it comes down with the case: RunResponse lowers it in the same Closed
            // arm that disperses the crowd, so the same few-real-seconds window applies.
            float lowered = Time.time + 5f;
            bool standing = true;
            while (Time.time < lowered && standing)
            {
                standing = GameObject.Find("Scene Cordon " + _caseId) != null;
                if (standing) yield return null;
            }
            Assert.That(standing, Is.False,
                "the cordon outlived its case - the barricades are still standing after Closed");

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

        /// <summary>The collision arc, staged with a manufactured plan and ridden to its
        /// verdict: two of the town's own drivers stand at a wreck, the county takes their
        /// statements at the kerb, the drink decides it, and the at-fault driver leaves in
        /// custody with his car on the hook.</summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator AStagedCollisionRunsToItsVerdict()
        {
            var host = Object.FindFirstObjectByType<VillageHost>();
            var sim = host.Sim;
            _startMinuteOfDay = sim.Clock.MinuteOfDay;   // the hour the teardown hands back

            // The scene must be discoverable: a road tile within testimony range of an occupied
            // door. Walk the census for a citizen standing outdoors near one - the hit test's
            // own search - then ring outward from them to the nearest road tile, the planner's
            // own idiom.
            int anchor = -1;
            for (int i = 0; i < sim.AgentCount && anchor < 0; i++)
            {
                var a = sim.GetAgent(i);
                if (a.Downed || a.Doing == Activity.AwayFromTown) continue;
                if ((host.World.Grid.FlagsAt(a.Position.ToTile()) & TileFlags.Indoor) != 0) continue;
                for (int j = 0; j < sim.AgentCount; j++)
                {
                    if (j == i) continue;
                    var b = sim.GetAgent(j);
                    if (b.Travelling || !b.At.IsValid) continue;
                    if (b.Doing == Activity.Asleep) continue;
                    var door = host.World.GetPlace(b.At).Door;
                    if (Tile.ChebyshevDistance(door, a.Position.ToTile()) < 40) { anchor = i; break; }
                }
            }
            Assert.That(anchor, Is.GreaterThanOrEqualTo(0), "no witnessable anchor in the noon town");

            Tile around = sim.GetAgent(anchor).Position.ToTile();
            Tile? scene = null;
            for (int r = 0; r <= 12 && !scene.HasValue; r++)
                for (int dy = -r; dy <= r && !scene.HasValue; dy++)
                    for (int dx = -r; dx <= r && !scene.HasValue; dx++)
                    {
                        if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != r) continue;
                        var t = new Tile(around.X + dx, around.Y + dy);
                        if (host.World.Grid.InBounds(t)
                            && host.World.Grid.TerrainAt(t) == Noir.Core.World.Terrain.Road) scene = t;
                    }
            Assert.That(scene.HasValue, "no road tile within 12 tiles of the anchor");

            // Two adult drivers, neither down nor away. The anchor drives if they are an adult;
            // otherwise the first two eligible strangers do - StageCollision brings the drivers
            // to the scene itself, so where they stand now does not matter.
            var drivers = new List<int>();
            if (!host.People.Get(new CitizenId(anchor)).IsChildIn(1991)) drivers.Add(anchor);
            for (int i = 0; i < sim.AgentCount && drivers.Count < 2; i++)
            {
                if (drivers.Contains(i)) continue;
                var a = sim.GetAgent(i);
                if (a.Downed || a.Doing == Activity.AwayFromTown || a.Responding) continue;
                if (host.People.Get(new CitizenId(i)).IsChildIn(1991)) continue;
                drivers.Add(i);
            }
            Assert.That(drivers.Count, Is.EqualTo(2), "the noon town could not seat two drivers");
            var atFault = new CitizenId(drivers[0]);
            var other = new CitizenId(drivers[1]);

            int casesBefore = host.Cases.Count;
            var plan = new CrashPlan(sim.Clock.MinuteOfDay, scene.Value, atFault, other,
                                     injury: false, CrashVerdict.Dui, CarTone.Dark, CarShape.Pickup);
            host.StageCollision(plan);

            Assert.That(host.Cases.Count, Is.EqualTo(casesBefore + 1), "the crash opened a case");
            _caseId = casesBefore;
            _victim = atFault.Value;   // the teardown's Return/Revive pair covers the arrest
            Assert.That(host.Cases.KindOf(_caseId), Is.EqualTo(CaseKind.Collision));

            _wasSpeed = host.SpeedIndex;
            host.SpeedIndex = VillageHost.Speeds.Length - 1;   // 300x

            // Ride it to Closed, latching each state once - the hit test's own poll shape.
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
                "the collision case never closed; states seen: " + string.Join(" → ", seen));

            var file = host.Cases.FileOf(_caseId);
            foreach (var line in file) Debug.Log("[case-file] " + line);
            Assert.That(file.Any(l => l.Contains("under arrest")),
                "the file never recorded the arrest");
            Assert.That(file.Any(l => l.Contains("tow")),
                "the file never recorded the tow");

            // The verdict's aftermath reaches the street within a few real seconds of the
            // close: the arrested man is away, and neither driver still stands at the scene
            // (the other is released by RunResponse's own Closed arm, on its minute).
            float settled = Time.time + 10f;
            while (Time.time < settled
                   && (sim.GetAgent(atFault).Responding || sim.GetAgent(other).Responding))
                yield return null;
            Assert.That(sim.GetAgent(atFault).Responding, Is.False,
                "the at-fault driver is still standing at the scene");
            Assert.That(sim.GetAgent(other).Responding, Is.False,
                "the other driver was never released after the close");
            Assert.That(sim.GetAgent(atFault).Doing, Is.EqualTo(Activity.AwayFromTown),
                "the arrested driver should be off in the county lockup");
        }
    }
}
