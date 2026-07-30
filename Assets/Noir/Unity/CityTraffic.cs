using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// Cars that drive the streets, keep off the pavement, and stop for each other.
    ///
    /// Each vehicle owns ONE corridor - an avenue or a street, edge to edge - and drives it end
    /// to end, wrapping round when it leaves the map. There is still no turning: a car cannot
    /// change corridor. What it now does have is the two rules that stop it looking broken.
    ///
    /// STAYING ON THE ROAD. A lane is placed by measuring, not by taste: the carriageway is the
    /// road tile less its kerbs, and the lane centre sits half a lane in from the middle. That
    /// keeps a 2m car inside a 10m road with room either side, so nothing ever mounts a kerb.
    ///
    /// NOT DRIVING THROUGH EACH OTHER. One rule covers both cases. Before advancing, a car looks
    /// a few metres up its own path; if anything is in that box it does not move this frame.
    /// Same-lane following and junction conflicts are the same test, because a crossing car IS
    /// in the box when it is in the junction.
    ///
    /// AND A RIGHT OF WAY, which is what stops that rule deadlocking. Two cars arriving at a
    /// crossing can each be in the other's box, and with a symmetric rule both wait for ever.
    /// So the north-south streets have priority: a car on an avenue yields, a car on a street
    /// does not. Arbitrary, deterministic, and it cannot gridlock.
    ///
    /// Built AFTER CityChunker and outside the node it bakes - a combined mesh cannot move.
    /// </summary>
    public sealed class CityTraffic : MonoBehaviour
    {
        private const int Cell = CityStreets.Cell;

        /// <summary>Metres per second. A town speed, not a motorway one.</summary>
        private const float Speed = 7f;

        /// <summary>
        /// How far off the road's centre line a moving lane sits - MEASURED, via CityStreets,
        /// from the tarmac submesh of the road tile.
        ///
        /// Every previous number here was picked by eye off the centre of the TILE, and a tile
        /// is carriageway plus pavement. Parked cars sat 2.6m from the tile edge and moving ones
        /// 2.8m, which is to say the traffic drove through the parking.
        /// </summary>
        private static float Lane => CityStreets.LaneOffset;

        /// <summary>How far ahead a car looks, and how wide that look is.</summary>
        private const float LookAhead = 7f, LookWide = 3.2f;

        private sealed class Mover
        {
            public Transform What;
            public Vector3 From, To, Dir;
            public float At, Rate;
            public bool Priority;      // north-south goes first
        }

        private readonly List<Mover> _movers = new List<Mover>();

        public static CityTraffic Create(WorldModel world, Transform parent)
        {
            var go = new GameObject("CityTraffic");
            go.transform.SetParent(parent, false);
            var traffic = go.AddComponent<CityTraffic>();
#if UNITY_EDITOR
            traffic.Populate(world);
#endif
            return traffic;
        }

#if UNITY_EDITOR
        private void Populate(WorldModel world)
        {
            int cols = world.Width / Cell, rows = world.Height / Cell;

            bool IsRoad(int cx, int cy) =>
                cx >= 0 && cy >= 0 && cx < cols && cy < rows &&
                world.Grid.TerrainAt(cx * Cell + Cell / 2, cy * Cell + Cell / 2)
                    == Noir.Core.World.Terrain.Road;

            var cars = Everyday();
            if (cars.Count == 0) return;

            // A corridor is a road that reaches both edges. Every road in this city does, which
            // is why there is no turning to worry about yet.
            var lanes = new List<(Vector3 from, Vector3 to, bool priority)>();

            for (int cy = 0; cy < rows; cy++)
                if (IsRoad(0, cy) && IsRoad(cols - 1, cy))
                {
                    float z = -(cy * Cell + Cell / 2f);
                    lanes.Add((new Vector3(-20f, 0f, z - Lane), new Vector3(world.Width + 20f, 0f, z - Lane), false));
                    lanes.Add((new Vector3(world.Width + 20f, 0f, z + Lane), new Vector3(-20f, 0f, z + Lane), false));
                }

            for (int cx = 0; cx < cols; cx++)
                if (IsRoad(cx, 0) && IsRoad(cx, rows - 1))
                {
                    float x = cx * Cell + Cell / 2f;
                    lanes.Add((new Vector3(x - Lane, 0f, 20f), new Vector3(x - Lane, 0f, -world.Height - 20f), true));
                    lanes.Add((new Vector3(x + Lane, 0f, -world.Height - 20f), new Vector3(x + Lane, 0f, 20f), true));
                }

            int n = 0;
            for (int c = 0; c < lanes.Count; c++)
            for (int k = 0; k < 2; k++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    cars[(int)(Materials3D.Scatter(c, k, 811) % (uint)cars.Count)]);
                if (prefab == null) continue;

                var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                car.transform.SetParent(transform, false);

                var (from, to, priority) = lanes[c];
                var mover = new Mover
                {
                    What = car.transform,
                    From = from,
                    To = to,
                    Dir = (to - from).normalized,
                    At = (k + Materials3D.Scatter(c, k, 821) % 100 / 100f) / 2f % 1f,
                    Rate = Speed / Vector3.Distance(from, to),
                    Priority = priority,
                };
                _movers.Add(mover);

                car.transform.position = Vector3.Lerp(from, to, mover.At);
                car.transform.rotation = Quaternion.LookRotation(mover.Dir, Vector3.up);
                n++;
            }

            Debug.Log($"[traffic] {n} vehicles on {lanes.Count} lanes, "
                    + $"{Lane:0.0}m off the centre line.");
        }

        /// <summary>The same everyday set the parked cars use - no fire engines on patrol.</summary>
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
                if (me.What == null) continue;

                if (!Blocked(i)) me.At += me.Rate * dt;
                if (me.At > 1f) me.At -= 1f;          // off one edge and back on the other

                me.What.position = Vector3.Lerp(me.From, me.To, me.At);
                me.What.rotation = Quaternion.LookRotation(me.Dir, Vector3.up);
            }
        }

        /// <summary>
        /// Is anything sitting in the few metres in front of this car?
        ///
        /// One test for two problems. A car in the same lane is in front of me when I am
        /// following it; a car on a crossing street is in front of me when it is in the
        /// junction I am about to enter. The right-of-way check is what keeps two cars from
        /// each waiting on the other for ever.
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

                var gap = other.What.position - here;

                float ahead = Vector3.Dot(gap, me.Dir);
                if (ahead <= 0f || ahead > LookAhead) continue;          // behind, or far off

                float side = Vector3.Cross(me.Dir, gap).magnitude;
                if (side > LookWide) continue;                           // not in my path

                // Both of us in each other's way at a crossing: the street wins, the avenue
                // waits. Without this the pair sit nose to nose until the heat death.
                bool crossing = Mathf.Abs(Vector3.Dot(me.Dir, other.Dir)) < 0.5f;
                if (crossing && me.Priority && !other.Priority) continue;

                return true;
            }
            return false;
        }
    }
}
