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

            var line = world.Roads.At(vx, vy);
            if (line == null)
            {
                // Off the map entirely is fine - lanes run past the edge so cars arrive from
                // off-stage rather than appearing out of nothing.
                if (vx < 0f || vy < 0f || vx > world.Width || vy > world.Height) return true;
                over = 999f;
                return false;
            }

            // HOW FAR FROM THE CENTRE LINE, measured the way the road is actually shaped.
            //
            // This used to be |across - line.Centre| on one axis, and against a road that bends
            // that is not a measurement, it is a straight ruler held up to a curve. It reported
            // a van sitting correctly on Chicago Street's south end as "583.23m past the
            // asphalt" - which is exactly |903.23 - 314| - 6, the distance from the x of the
            // road's FIRST point, 2.4km away at the north edge.
            //
            // RoadNetwork.At had already been taught to ask the path; this had not, so the test
            // was failing a car the game was placing correctly.
            float off;
            if (line.Path != null)
            {
                var (_, lateral) = line.Path.Project(new Noir.Core.Contracts.Vec2(vx, vy));
                off = Mathf.Abs(lateral);
            }
            else
            {
                float across = line.IsNorthSouth ? vx : vy;
                off = Mathf.Abs(across - line.Centre);
            }

            float half = CityStreets.Asphalt(line.Class);

            over = off - half;
            return over <= tolerance;
        }

        /// <summary>Which axis a vehicle is travelling on, from where it is pointing.</summary>
        public static bool IsNorthSouth(Transform vehicle) =>
            Mathf.Abs(vehicle.forward.z) > Mathf.Abs(vehicle.forward.x);
    }
}
