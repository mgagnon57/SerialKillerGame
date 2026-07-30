using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// Cars that drive the streets, keep to their lane, and stop at red lights.
    ///
    /// LANES COME FROM THE ROAD, NOT FROM THE TILE. Each lane is derived from its road's class
    /// and the MEASURED width of that class's asphalt: a freeway is two lanes each way inside 24
    /// metres, a main road one each way inside 12, a street one each way inside 6. Nothing here
    /// is a number somebody liked the look of, because every previous version of this file was,
    /// and every one of them put a wheel on the kerb.
    ///
    /// THEY DRIVE ON THE RIGHT, which is not decoration: it decides which side of the centre
    /// line each direction sits on, and getting it wrong is two head-on streams sharing a lane.
    ///
    /// SIGNALS, NOT PRIORITY. The previous version avoided deadlock at crossings by giving one
    /// axis an arbitrary permanent right of way and letting the other yield, which meant cars
    /// sailed through red lights - no good in a game whose whole subject is who was where and
    /// when. Now each lane knows the stop line of every junction along it and asks CitySignals
    /// whether it may enter. Only one axis is ever green, so the flows cannot conflict and there
    /// is nothing to deadlock.
    ///
    /// FOLLOWING is still a look-ahead box, but only against PARALLEL traffic. Cross traffic is
    /// the signals' business; treating it as an obstacle made a car stop dead beside a junction
    /// it had a green light for.
    ///
    /// Still no turning: a car owns one lane end to end and wraps round when it leaves the map.
    /// Turning needs junction geometry - which lane feeds which - and that is the next thing.
    ///
    /// Built AFTER CityChunker and outside the node it bakes: a combined mesh cannot move.
    /// </summary>
    public sealed class CityTraffic : MonoBehaviour
    {
        /// <summary>Metres per second. A town speed, not a motorway one.</summary>
        private const float Speed = 8f;

        /// <summary>How far ahead a car looks for the car in front, and how wide that look is.</summary>
        private const float LookAhead = 8f, LookWide = 2.6f;

        /// <summary>Where a car begins easing off for a red light.</summary>
        private const float Braking = 14f;

        /// <summary>
        /// Moving vehicles per home. The budget comes from HOW MANY PEOPLE LIVE HERE rather than
        /// from how much tarmac was laid, which is the only version of this that scales: a bigger
        /// map gets more lanes and the same traffic per resident, so a village stays quiet and a
        /// city fills up without anybody retuning a number.
        /// </summary>
        public static float CarsPerHome = 0.5f;

        /// <summary>A stop line: where it is along the lane, and which junction it belongs to.</summary>
        private struct StopLine
        {
            public int Node;
            public float At;
        }

        private sealed class Lane
        {
            public Vector3 From, Dir;
            public float Length;
            public bool NorthSouth;
            public readonly List<StopLine> Stops = new List<StopLine>();
        }

        private sealed class Mover
        {
            public Transform What;
            public Lane Road;
            public float At;              // metres travelled along the lane
        }

        private readonly List<Lane> _lanes = new List<Lane>();
        private readonly List<Mover> _movers = new List<Mover>();
        private CitySignals _signals;

        public static CityTraffic Create(WorldModel world, Transform parent, CitySignals signals)
        {
            var go = new GameObject("CityTraffic");
            go.transform.SetParent(parent, false);
            var traffic = go.AddComponent<CityTraffic>();
            traffic._signals = signals;
#if UNITY_EDITOR
            traffic.Populate(world);
#endif
            return traffic;
        }

#if UNITY_EDITOR
        private void Populate(WorldModel world)
        {
            BuildLanes(world);
            if (_lanes.Count == 0) { Debug.LogWarning("[traffic] no lanes"); return; }

            var cars = Everyday();
            if (cars.Count == 0) { Debug.LogWarning("[traffic] no vehicles"); return; }

            int budget = Mathf.Clamp(Mathf.RoundToInt(world.Homes.Count * CarsPerHome),
                                     Mathf.Min(6, _lanes.Count), _lanes.Count * 2);

            for (int n = 0; n < budget; n++)
            {
                var lane = _lanes[n % _lanes.Count];

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    cars[(int)(Materials3D.Scatter(n, 0, 811) % (uint)cars.Count)]);
                if (prefab == null) continue;

                var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                car.transform.SetParent(transform, false);

                // Spread along the lane rather than clumped at its start, and jittered so two
                // lanes do not run in lockstep.
                float spacing = lane.Length / Mathf.Max(1, budget / _lanes.Count + 1);
                var mover = new Mover
                {
                    What = car.transform,
                    Road = lane,
                    At = (n / _lanes.Count + 1) * spacing
                       + Materials3D.Scatter(n, 1, 821) % 20,
                };
                mover.At = Mathf.Repeat(mover.At, lane.Length);
                _movers.Add(mover);

                car.transform.position = lane.From + lane.Dir * mover.At;
                car.transform.rotation = Quaternion.LookRotation(lane.Dir, Vector3.up);
            }

            int stops = 0;
            foreach (var lane in _lanes) stops += lane.Stops.Count;
            Debug.Log($"[traffic] {_movers.Count} vehicles on {_lanes.Count} lanes "
                    + $"({world.Homes.Count} homes x {CarsPerHome:0.00}), {stops} stop lines.");
        }

        /// <summary>
        /// Every running lane in the city, both directions of every road.
        ///
        /// Village y runs into Unity -z, so village north is Unity +z. Driving on the right then
        /// means: a northbound car keeps to the EAST of the centre line, a southbound to the
        /// west, an eastbound to the SOUTH, a westbound to the north.
        /// </summary>
        private void BuildLanes(WorldModel world)
        {
            const float Margin = 30f;      // cars appear and vanish off the edge of the map

            foreach (var line in world.Roads.Lines)
            {
                if (!line.IsStraight) continue;

                int perSide = CityStreets.LanesEachWay(line.Class);
                float span = line.IsNorthSouth ? world.Height : world.Width;

                for (int side = 0; side < 2; side++)
                for (int i = 0; i < perSide; i++)
                {
                    float off = CityStreets.LaneOffset(line.Class, i);

                    // side 0 runs along the axis in the DECREASING direction, side 1 increasing.
                    float sign = side == 0 ? -1f : 1f;

                    // Right-hand driving. Going down-axis (north, or west) the right-hand side is
                    // the greater cross-coordinate for a north-south road and the lesser for an
                    // east-west one - which is what these two signs are.
                    float across = line.Centre
                                 + off * (line.IsNorthSouth ? -sign : sign);

                    float startAlong = side == 0 ? span + Margin : -Margin;
                    float endAlong = side == 0 ? -Margin : span + Margin;

                    Vector3 At(float along) => line.IsNorthSouth
                        ? new Vector3(across, 0f, -along)
                        : new Vector3(along, 0f, -across);

                    var from = At(startAlong);
                    var to = At(endAlong);

                    var lane = new Lane
                    {
                        From = from,
                        Dir = (to - from).normalized,
                        Length = Vector3.Distance(from, to),
                        NorthSouth = line.IsNorthSouth,
                    };

                    // Where this lane has to stop. A stop line sits one junction-reach back from
                    // the middle of the crossing, which is its near edge.
                    for (int j = 0; j < world.Roads.Junctions.Count; j++)
                    {
                        var junction = world.Roads.Junctions[j];
                        float mine = line.IsNorthSouth ? junction.X : junction.Y;
                        if (Mathf.Abs(mine - line.Centre) > 0.5f) continue;   // not on this road

                        float alongOf = line.IsNorthSouth ? junction.Y : junction.X;
                        float at = Mathf.Abs(alongOf - startAlong) - junction.Reach;
                        if (at > 0f) lane.Stops.Add(new StopLine { Node = j, At = at });
                    }
                    lane.Stops.Sort((a, b) => a.At.CompareTo(b.At));

                    _lanes.Add(lane);
                }
            }
        }

        /// <summary>
        /// What is plausibly driving through a town.
        ///
        /// The Cars folder is 232 prefabs and holds a whole motorsport paddock - Formula, Nascar,
        /// Le Mans prototypes, a gokart - as well as heavy haulage and a monster truck. Taking
        /// "a car" from all of that put a stock car on the avenue.
        /// </summary>
        private static List<string> Everyday()
        {
            string[] wanted =
            {
                "Car_Modern", "Car_Pickup_Modern", "Car_Sport_Modern", "Car_Taxi_Modern",
                "Car_Cargovan_Modern", "Car_Van_Old", "Car_Offroad_Modern",
            };
            var found = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets(
                         "t:Prefab", new[] { "Assets/polyperfect/Poly Universal Pack/Prefabs/Cars" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (path.IndexOf("Collider", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (name.IndexOf("Racing", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                foreach (var w in wanted)
                    if (name == w || name.StartsWith(w + "_", System.StringComparison.Ordinal))
                    { found.Add(path); break; }
            }
            found.Sort(System.StringComparer.Ordinal);
            return found;
        }
#endif

        private void Update()
        {
            float dt = Time.deltaTime;

            for (int i = 0; i < _movers.Count; i++)
            {
                var me = _movers[i];
                if (me.What == null || me.Road == null) continue;

                float step = Speed * dt;

                // Hold at the stop line if the next junction ahead is not showing green, easing
                // off rather than stopping dead on the last frame.
                float toLine = NextRedLine(me);
                if (toLine >= 0f)
                {
                    float allowed = Mathf.Max(0f, toLine - 0.4f);
                    step = Mathf.Min(step, allowed, step * (toLine / Braking));
                }

                if (Blocked(i)) step = 0f;

                me.At += step;
                if (me.At > me.Road.Length) me.At -= me.Road.Length;

                me.What.position = me.Road.From + me.Road.Dir * me.At;
                me.What.rotation = Quaternion.LookRotation(me.Road.Dir, Vector3.up);
            }
        }

        /// <summary>
        /// Distance to the next stop line this car may not cross, or -1 if it may carry on.
        ///
        /// Only the NEXT junction matters: the ones behind have been cleared and the ones beyond
        /// it will have changed by the time anybody gets there.
        /// </summary>
        private float NextRedLine(Mover me)
        {
            var stops = me.Road.Stops;
            for (int s = 0; s < stops.Count; s++)
            {
                float gap = stops[s].At - me.At;
                if (gap < -0.5f) continue;                 // already through this one
                if (gap > Braking) return -1f;             // too far away to matter yet

                if (_signals != null && _signals.MayEnter(stops[s].Node, me.Road.NorthSouth))
                    return -1f;
                return gap;
            }
            return -1f;
        }

        /// <summary>
        /// Is there a car close in front, in my own lane?
        ///
        /// Parallel traffic only. Cross traffic at a junction is separated by the signals, and
        /// counting it here made a car with a green light sit still because a car waiting at the
        /// red on the crossing street happened to fall inside the box.
        /// </summary>
        private bool Blocked(int index)
        {
            var me = _movers[index];
            var here = me.What.position;

            for (int j = 0; j < _movers.Count; j++)
            {
                if (j == index) continue;
                var other = _movers[j];
                if (other.What == null) continue;
                if (Mathf.Abs(Vector3.Dot(me.Road.Dir, other.Road.Dir)) < 0.7f) continue;

                var gap = other.What.position - here;

                float ahead = Vector3.Dot(gap, me.Road.Dir);
                if (ahead <= 0f || ahead > LookAhead) continue;          // behind, or far off

                float side = Vector3.Cross(me.Road.Dir, gap).magnitude;
                if (side > LookWide) continue;                           // not in my lane

                return true;
            }
            return false;
        }
    }
}
