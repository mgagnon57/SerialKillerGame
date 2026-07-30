using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// Cars that actually drive, and a train that actually runs.
    ///
    /// Everything placed until now was a static prop, and a street of parked cars reads as a
    /// photograph rather than a place. This is the cheapest possible version that is still
    /// honest: each vehicle owns ONE corridor - an avenue or a street, edge to edge - and drives
    /// it end to end, wrapping round when it leaves the map. There is no junction logic, no
    /// turning and no collision.
    ///
    /// That is a deliberate first cut rather than a shortcut nobody noticed. Real traffic wants
    /// a graph, lane discipline, gap-keeping and obedience at the signals that are already
    /// standing at every crossing, and none of that is worth building until the thing it drives
    /// on is settled. What this does buy, for very little, is the single biggest difference
    /// between a model and a city: movement in the corner of your eye.
    ///
    /// Built AFTER CityChunker, and deliberately not under the node it bakes - a combined mesh
    /// cannot move, and anything handed to the chunker stops being able to.
    /// </summary>
    public sealed class CityTraffic : MonoBehaviour
    {
        private const int Cell = CityStreets.Cell;

        /// <summary>Metres per second. A town speed, not a motorway one.</summary>
        private const float Speed = 7f;

        private struct Mover
        {
            public Transform What;
            public Vector3 From, To;
            public float At;        // 0..1 along the corridor
            public float Rate;
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
            var corridors = new List<(Vector3 from, Vector3 to)>();

            for (int cy = 0; cy < rows; cy++)
                if (IsRoad(0, cy) && IsRoad(cols - 1, cy))
                {
                    float z = -(cy * Cell + Cell / 2f);
                    corridors.Add((new Vector3(-20f, 0f, z - 2.6f), new Vector3(world.Width + 20f, 0f, z - 2.6f)));
                    corridors.Add((new Vector3(world.Width + 20f, 0f, z + 2.6f), new Vector3(-20f, 0f, z + 2.6f)));
                }

            for (int cx = 0; cx < cols; cx++)
                if (IsRoad(cx, 0) && IsRoad(cx, rows - 1))
                {
                    float x = cx * Cell + Cell / 2f;
                    corridors.Add((new Vector3(x - 2.6f, 0f, 20f), new Vector3(x - 2.6f, 0f, -world.Height - 20f)));
                    corridors.Add((new Vector3(x + 2.6f, 0f, -world.Height - 20f), new Vector3(x + 2.6f, 0f, 20f)));
                }

            // Two moving cars per direction of travel, spaced down the corridor so they do not
            // all arrive at the same crossing together.
            int n = 0;
            for (int c = 0; c < corridors.Count; c++)
            for (int k = 0; k < 2; k++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    cars[(int)(Materials3D.Scatter(c, k, 811) % (uint)cars.Count)]);
                if (prefab == null) continue;

                var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                car.transform.SetParent(transform, false);

                var (from, to) = corridors[c];
                _movers.Add(new Mover
                {
                    What = car.transform,
                    From = from,
                    To = to,
                    At = (k + Materials3D.Scatter(c, k, 821) % 100 / 100f) / 2f % 1f,
                    Rate = Speed / Vector3.Distance(from, to),
                });
                n++;
            }

            Debug.Log($"[traffic] {n} vehicles moving on {corridors.Count} corridors.");
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
                var m = _movers[i];
                if (m.What == null) continue;

                m.At += m.Rate * dt;
                if (m.At > 1f) m.At -= 1f;          // off one edge and back on the other
                _movers[i] = m;

                m.What.position = Vector3.Lerp(m.From, m.To, m.At);
                m.What.rotation = Quaternion.LookRotation((m.To - m.From).normalized, Vector3.up);
            }
        }
    }
}
