using System.Collections.Generic;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.People;
using Noir.Core.Sim;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// THE CARS AT THE HOUSES, AND THE ONES THAT ARE NOT THERE TODAY.
    ///
    /// IDOT counts Rossville at about **nineteen moving vehicles at an average instant**, and Data
    /// USA puts the village at **two cars per household**. Against 624 declared households that is
    /// roughly twelve hundred cars owned and nineteen being driven: **about 98% of this town's
    /// vehicles are standing still at a house right now.** Until this file, the game drew none of
    /// them — `CityParking` fills authored public lots and stops at the kerb.
    /// See `docs/research/TRAFFIC-COUNTS.md`.
    ///
    /// WHERE a car stands is decided in Core by <see cref="Driveways"/>, not here. That is the
    /// half worth protecting: it is geometry and a seed, it has automated cover, and it works in a
    /// standalone player. This file only draws the result, and says so in the log when it cannot.
    ///
    /// BUILT AFTER THE BAKE, ON PURPOSE. `CityChunker` combines a layer's meshes into one, and a
    /// combined car cannot be taken away when its owner drives it to Hoopeston. These are created
    /// alongside `CityTraffic` — after `CityChunker.Bake` has run — for the same reason the moving
    /// vehicles are.
    ///
    /// AND THE ABSENCE IS THE POINT. `CityParking`'s docstring made the argument and stopped short
    /// of the houses: *"A PARKED CAR IS CONTENT IN THIS GAME. It is a thing that was somewhere at a
    /// time and can be looked at, remembered, and found to have moved."* A car that sat in a
    /// driveway on Tuesday and is gone on Wednesday is worth more to a game about noticing things
    /// than any number of cars driving past. <see cref="Refresh"/> takes them away while their
    /// owners are at work — `DayPlan` already sends the town out of it at twenty past six and
    /// brings it back at ten past five, and this is the first thing in the game you can SEE that
    /// happen to.
    /// </summary>
    public sealed class CityDriveways : MonoBehaviour
    {
        private const string Cars = "Assets/polyperfect/Poly Universal Pack/Prefabs/Cars/Cars City";

        /// <summary>
        /// The ordinary cars of an ordinary town. No ambulance, no police cruiser, no taxi: those
        /// belong to a place of work and `CityParking` already puts them outside one.
        /// </summary>
        private static readonly string[] Wanted =
        {
            "Car_Modern", "Car_Pickup_Modern", "Car_Offroad_Modern",
            "Car_Van_Old", "Car_Old", "Car_Sport_Modern",
        };

        private readonly List<GameObject> _cars = new List<GameObject>();
        private readonly List<PlaceId> _homeOf = new List<PlaceId>();
        private readonly List<CarTone> _tone = new List<CarTone>();
        private readonly List<CarShape> _shape = new List<CarShape>();
        private readonly Dictionary<int, List<int>> _byHome = new Dictionary<int, List<int>>();

        /// <summary>How many cars were drawn, standing or not.</summary>
        public int Count => _cars.Count;

        /// <summary>How many are visible right now.</summary>
        public int Standing
        {
            get
            {
                int n = 0;
                foreach (var car in _cars) if (car != null && car.activeSelf) n++;
                return n;
            }
        }

        /// <summary>
        /// Plan the town's driveways and put a car in each of them.
        ///
        /// Returns null when there is nothing to draw, rather than an empty object nobody can
        /// find — the caller registers this as a layer and a dead root confuses the bake.
        /// </summary>
        public static CityDriveways Create(WorldModel world, ulong seed, Transform parent)
        {
            if (world == null) return null;

            var plan = Driveways.Plan(world, seed);
            if (plan.Length == 0)
            {
                Debug.LogWarning("[driveways] no household on this map found room for a car.");
                return null;
            }

            var go = new GameObject("Driveways");
            go.transform.SetParent(parent, false);
            var it = go.AddComponent<CityDriveways>();

            var fleet = Fleet();
            if (fleet.Count == 0)
            {
                // THE TRAP CLAUDE.md NAMES, MADE AUDIBLE. Every prop system in this project reads
                // the asset database and therefore draws nothing in a player, silently. This one
                // still PLANNED the driveways - that part is Core and runs anywhere - so say
                // exactly which half is missing rather than leaving an empty street.
                Debug.LogWarning($"[driveways] planned {plan.Length} cars and can draw none: no car "
                               + "prefabs available. Outside the editor this is expected - the "
                               + "AssetDatabase does not exist in a player build.");
                return it;
            }

            var rng = Xoshiro256ss.Substream(seed, "driveways:models");

            foreach (var d in plan)
            {
                // A tile is a metre and Driveway.Spot is its corner, so stand the car in the
                // middle of it. Yaw follows the frontage: nose-on to the street it serves.
                float vx = d.Spot.X + 0.5f, vy = d.Spot.Y + 0.5f;
                float yaw = d.AlongX ? 90f : 0f;
                if (rng.Chance(0.5f)) yaw += 180f;        // nose in or nose out, both are normal

                var car = Put(go.transform, fleet[rng.NextInt(fleet.Count)], vx, vy, yaw);
                if (car == null) continue;

                CarMesh.Flatten(car);

                // The identity the rename below is about to discard, banded the way a witness
                // would band it: shape off the prefab's own name, tone off the flattened
                // mesh's average albedo. Captured here because after Flatten + rename there is
                // nothing left to read it from.
                it._shape.Add(ShapeOf(car.name));
                it._tone.Add(ToneOf(car));

                car.name = $"Parked_{d.Home.Value}_{d.Unit}";
                it._homeOf.Add(d.Home);
                it._cars.Add(car);

                if (!it._byHome.TryGetValue(d.Home.Value, out var list))
                    it._byHome[d.Home.Value] = list = new List<int>();
                list.Add(it._cars.Count - 1);
            }

            Debug.Log($"[driveways] {it._cars.Count} cars standing at {it._byHome.Count} houses, "
                    + $"from {plan.Length} planned. IDOT puts ~19 of this town's cars in motion.");
            return it;
        }

        /// <summary>
        /// Take away the cars whose owners are out of town, and put back the ones who are home.
        ///
        /// One resident away takes one car off the drive. A household with two cars and one
        /// commuter keeps the other, which is what Rossville's two-cars-per-household actually
        /// looks like at nine in the morning — the street empties a bit, not completely.
        ///
        /// Returns how many cars are standing afterwards. Cheap enough to call on a timer rather
        /// than per frame: it walks the citizen list once.
        /// </summary>
        public int Refresh(Simulation sim)
        {
            if (sim == null || sim.People == null) return Standing;

            var away = new Dictionary<int, int>();
            var people = sim.People.Citizens;

            for (int i = 0; i < people.Count; i++)
            {
                var who = people[i];
                if (!who.Home.IsValid) continue;
                if (sim.CurrentBlock(new CitizenId(i)).What != Activity.AwayFromTown) continue;

                away[who.Home.Value] = (away.TryGetValue(who.Home.Value, out int n) ? n : 0) + 1;
            }

            foreach (var pair in _byHome)
            {
                int gone = away.TryGetValue(pair.Key, out int n) ? n : 0;
                var list = pair.Value;

                for (int i = 0; i < list.Count; i++)
                {
                    var car = _cars[list[i]];
                    if (car == null) continue;
                    // The first `gone` cars of this house are the ones that left.
                    bool here = i >= gone;
                    if (car.activeSelf != here) car.SetActive(here);
                }
            }

            return Standing;
        }

        private static CarShape ShapeOf(string prefabName) =>
            prefabName.IndexOf("Pickup", System.StringComparison.OrdinalIgnoreCase) >= 0
                ? CarShape.Pickup
          : prefabName.IndexOf("Van", System.StringComparison.OrdinalIgnoreCase) >= 0
                ? CarShape.Van
          : CarShape.Car;

        private static CarTone ToneOf(GameObject car)
        {
            var r = car.GetComponent<MeshRenderer>();
            if (r == null) return CarTone.Mid;
            float sum = 0f; int n = 0;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                var c = m.color; sum += (c.r + c.g + c.b) / 3f; n++;
            }
            if (n == 0) return CarTone.Mid;
            float lum = sum / n;
            return lum < 0.35f ? CarTone.Dark : lum > 0.65f ? CarTone.Light : CarTone.Mid;
        }

        /// <summary>
        /// Where car <paramref name="index"/> stands right now. Only defined for an index
        /// <see cref="NearestCar"/> actually returned: a taken slot is null and this will NRE
        /// against it, on purpose — NearestCar already skips null and inactive cars, and never
        /// hands back an index it would not be safe to call this on.
        /// </summary>
        public Vector3 PositionOf(int index) => _cars[index].transform.position;

        /// <summary>
        /// The nearest standing car to <paramref name="from"/> within <paramref name="within"/>
        /// metres, or -1. CityDoors.NearestDoor's XZ scan, on wheels. Skips inactive cars —
        /// their owners drove them to work — and taken ones (null slots).
        /// </summary>
        public int NearestCar(Vector3 from, float within)
        {
            int best = -1;
            float bestD2 = within * within;
            for (int i = 0; i < _cars.Count; i++)
            {
                var car = _cars[i];
                if (car == null || !car.activeSelf) continue;
                var d = car.transform.position - from;
                float d2 = d.x * d.x + d.z * d.z;
                if (d2 > bestD2) continue;
                bestD2 = d2; best = i;
            }
            return best;
        }

        /// <summary>
        /// Hand car <paramref name="index"/> over and stop owning it: the slot goes null so
        /// Refresh's absence schedule and the layer switch never touch it again — Refresh
        /// already tolerates a null slot by construction. Once taken, a car is loose for
        /// good; whether it ever goes home again is a later feature, recorded in IDEAS.
        /// </summary>
        public (GameObject car, CarTone tone, CarShape shape) Take(int index)
        {
            var car = _cars[index];
            _cars[index] = null;
            return (car, _tone[index], _shape[index]);
        }

        // ---------- the prefabs ----------

        private static List<string> Fleet()
        {
            var found = new List<string>();
#if UNITY_EDITOR
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Cars }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf("Collider", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                foreach (var w in Wanted)
                    if (file == w || file.StartsWith(w + "_", System.StringComparison.Ordinal))
                    { found.Add(path); break; }
            }
            found.Sort(System.StringComparer.Ordinal);   // stable, so the street looks the same twice
#endif
            return found;
        }

        private static GameObject Put(Transform parent, string path, float vx, float vy, float yaw)
        {
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return null;

            // Instantiate, NOT PrefabUtility.InstantiatePrefab. `CityParking` uses the latter so
            // its cars stay linked to their prefab in the scene, and that is right for a dozen
            // cars nobody rebuilds. These get Flattened the moment they land, and you may not
            // restructure a linked prefab instance - destroying its children throws. A plain copy
            // is what a thing about to be merged actually wants.
            var go = Object.Instantiate(prefab);
            go.transform.SetParent(parent, false);
            go.transform.position =
                new Vector3(vx, parent.position.y + ElevationGrid.HeightAt(vx, vy), -vy);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
#else
            return null;
#endif
        }
    }
}
