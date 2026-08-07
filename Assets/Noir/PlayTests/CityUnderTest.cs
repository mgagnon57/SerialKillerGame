using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.PlayTests
{
    /// <summary>
    /// The living city, for tests to watch.
    ///
    /// Everything checked headlessly until now has been GEOMETRY - where a lane is, how wide the
    /// asphalt is, which building a ray touches. All of that is arithmetic and none of it needs
    /// the game to be running. What could not be checked was BEHAVIOUR OVER TIME: that a car
    /// actually stops at a red rather than merely having a stop line in front of it.
    ///
    /// A PlayMode test is the answer, because it is not a simulation of the game loop - it IS the
    /// game loop. Unity enters Play, VillageHost bootstraps itself off its own
    /// RuntimeInitializeOnLoadMethod exactly as it does when you press the button, and a
    /// [UnityTest] coroutine can yield a frame at a time and watch what happens.
    ///
    /// Play is entered ONCE for the whole run and every test shares the city that comes up, which
    /// is why this is a static helper rather than a fixture: rebuilding four thousand prefabs per
    /// test would take longer than anybody will wait.
    /// </summary>
    public static class CityUnderTest
    {
        /// <summary>Long enough to build the whole city, short enough to fail rather than hang.</summary>
        private const int BuildFrames = 4000;

        public static VillageHost Host => VillageHost.Instance;
        public static WorldModel World => Host != null ? Host.World : null;

        private static CityTraffic _traffic;
        private static CitySignals _signals;

        /// <summary>
        /// INACTIVE ONES COUNT, and leaving them out broke the entire suite.
        ///
        /// FindFirstObjectByType skips deactivated objects by default. VillageHost builds signals
        /// and traffic whenever EITHER layer is on, and then hands each object to
        /// Layers.Register, whose last act is `root.SetActive(IsOn(kind))`. So with the Traffic
        /// layer on and the Signals layer off - which is what was stored in PlayerPrefs -
        /// CitySignals is built, immediately deactivated, and then cannot be found.
        ///
        /// WaitUntilBuilt requires both, so it spun for 4000 frames and every one of the thirteen
        /// tests failed in SetUp with "the city was still not built (host=up, traffic=up)" - a
        /// message that never mentioned the one thing that was null. The city was fine. The
        /// suite was reading a UI preference as if it were test state.
        ///
        /// A test has no business caring which layers somebody left switched on.
        /// </summary>
        public static CityTraffic Traffic =>
            _traffic != null ? _traffic
                             : _traffic = Object.FindFirstObjectByType<CityTraffic>(
                                   FindObjectsInactive.Include);

        public static CitySignals Signals =>
            _signals != null ? _signals
                             : _signals = Object.FindFirstObjectByType<CitySignals>(
                                   FindObjectsInactive.Include);

        /// <summary>
        /// Wait until the city is up, or give up loudly.
        ///
        /// The build is synchronous inside Awake, so this is really waiting for the one very long
        /// frame in which four hundred prefabs are placed and baked.
        /// </summary>
        public static IEnumerator WaitUntilBuilt()
        {
            for (int frame = 0; frame < BuildFrames; frame++)
            {
                if (Host != null && Host.LoadError != null)
                    throw new System.Exception("the city failed to load: " + Host.LoadError);

                if (Host != null && Host.Sim != null && Traffic != null && Signals != null)
                {
                    // One more frame so everything has had an Update before anybody looks.
                    yield return null;
                    yield break;
                }
                yield return null;
            }

            throw new System.Exception(
                $"the city was still not built after {BuildFrames} frames "
              + $"(host={(Host == null ? "null" : "up")}, "
              + $"traffic={(Traffic == null ? "null" : "up")})");
        }

        /// <summary>Every moving vehicle, as transforms. Black box on purpose.</summary>
        public static List<Transform> Vehicles()
        {
            var found = new List<Transform>();
            if (Traffic == null) return found;

            foreach (Transform child in Traffic.transform) found.Add(child);
            return found;
        }

        /// <summary>
        /// Is this point on a carriageway, or inside a junction where a turning car belongs?
        ///
        /// The same test TrafficCheck applies to the lane geometry, applied instead to where a
        /// car has actually got to - which is the version that catches a turn that swings wide.
        /// </summary>
        public static bool OnTheRoad(Vector3 at, float tolerance, out float over)
        {
            over = 0f;
            var world = World;
            if (world == null) return true;

            float vx = at.x, vy = -at.z;

            // In a junction? Then any position within it is legitimate.
            foreach (var junction in world.Roads.Junctions)
                if (Mathf.Abs(vx - junction.X) <= junction.Reach &&
                    Mathf.Abs(vy - junction.Y) <= junction.Reach)
                    return true;

            // ON ASPHALT, NOT ON A PARTICULAR ROAD'S ASPHALT.
            //
            // This asked RoadNetwork.At, which answers a DIFFERENT question: whose CORRIDOR holds
            // this point, widest first and declaration order on a tie. Near a crossing the
            // perpendicular street's corridor holds the point too, and when it is the wider one it
            // wins - so a car sitting correctly in its own lane was measured against the centre
            // line of the road it was driving ACROSS, five metres away, and failed.
            //
            // The ceiling gives it away: inside a 10 m corridor `over` can never exceed
            // HalfWidth(5) - Asphalt(3) = 2.00 m, and the failure was 1.99 m. The junction escape
            // above cannot cover it either - that is an axis-aligned square, a corridor is a strip
            // about a centre line that bends, and where the survey's roads meet at a T there is no
            // Junction recorded at all (see RoadNetwork.Crossings).
            //
            // So: keep the SMALLEST overshoot over every corridor that contains the point. A car
            // on any road's carriageway is on the road. Containment is decided by exactly At's own
            // rule, including its straight/bent end guard, so the only thing that changes is which
            // containing road gets measured against - which is the whole fix and nothing more.
            //
            // NOTE ON ALLEYS: Asphalt(Alley) measures 3.55 m off the dirt tile while an alley's
            // HalfWidth is 2.0 m, so any point inside an alley corridor passes unconditionally.
            // That is the alley tile being wider than the corridor it is declared to fill, not
            // this test being loose, and it is recorded here so the next person does not re-derive
            // it from a surprising pass.
            bool anyCorridor = false;
            float least = float.MaxValue;

            foreach (var road in world.Roads.Lines)
            {
                if (road.Path == null) continue;

                var (s, lateral) = road.Path.Project(new Noir.Core.Contracts.Vec2(vx, vy));
                float off = Mathf.Abs(lateral);
                if (off > road.HalfWidth) continue;

                if (s <= 0f || s >= road.Path.Length)
                {
                    if (road.Path.IsStraightAxisAligned)
                    {
                        float along = road.IsNorthSouth ? vy : vx;
                        if (along < road.From || along > road.To) continue;
                    }
                    else
                    {
                        var end = road.Path.PointAt(s);
                        float dx = vx - end.X, dy = vy - end.Y;
                        if (dx * dx + dy * dy > road.HalfWidth * road.HalfWidth) continue;
                    }
                }

                anyCorridor = true;
                float overThis = off - CityStreets.Asphalt(road.Class);
                if (overThis < least) least = overThis;
            }

            if (!anyCorridor)
            {
                // Off the map entirely is fine - lanes run past the edge so cars arrive from
                // off-stage rather than appearing out of nothing.
                if (vx < 0f || vy < 0f || vx > world.Width || vy > world.Height) return true;
                over = 999f;
                return false;
            }

            over = least;
            return over <= tolerance;
        }

        /// <summary>Which axis a vehicle is travelling on, from where it is pointing.</summary>
        public static bool IsNorthSouth(Transform vehicle) =>
            Mathf.Abs(vehicle.forward.z) > Mathf.Abs(vehicle.forward.x);
    }
}
