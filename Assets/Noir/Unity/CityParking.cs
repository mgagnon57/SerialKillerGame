using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// The surface car parks, and the cars standing still in them.
    ///
    /// Northgate had a hospital, a casino, a cinema, a police station, a school and a bank, and
    /// nowhere at all for a car to stop. Every vehicle on the map was in motion. The road kit
    /// ships five parking tiles - Road_Parking and its corner, entrance, side and half variants -
    /// plus a main road with a parking entrance cut into it, and not one of them had ever been
    /// placed.
    ///
    /// A PARKED CAR IS CONTENT IN THIS GAME. It is a thing that was somewhere at a time and can
    /// be looked at, remembered, and found to have moved. A city where every car is driving past
    /// is a city where nothing is ever anywhere.
    ///
    /// The lots are authored in city.txt as places of kind `carpark`, so they can be seen and
    /// moved on the map like everything else, and CityStreets.Owned leaves their ground alone -
    /// otherwise a car park would be paved with city flagstones and hung with street lamps, the
    /// way the farmyard once was.
    ///
    /// WHERE THE BAYS ARE IS NOT MEASURABLE. Road_Parking_10x10_City is one submesh of asphalt
    /// with the bay markings in the atlas, so unlike the road tiles there is no geometry to read
    /// a lane position off. The rows below are therefore laid to standard dimensions - a five
    /// metre bay, a six metre aisle, cars at 2.8m centres - and left deliberately unfull, which
    /// reads as parking whether or not it lands on the paint.
    ///
    /// Static, so this goes inside the node CityChunker bakes.
    /// </summary>
    public static class CityParking
    {
        private const string Kit = "Assets/polyperfect/Poly Universal Pack/Prefabs/City/Roads City/";
        private const string Cars = "Assets/polyperfect/Poly Universal Pack/Prefabs/Cars/Cars City";

        /// <summary>A bay is this deep, and two rows stand back to back with an aisle between.</summary>
        private const float Bay = 5f, Aisle = 6f;

        /// <summary>Car centres along a row.</summary>
        private const float Pitch = 2.8f;

        /// <summary>
        /// How full a lot is. Not 1: a lot parked to capacity reads as a model of a lot rather
        /// than as a lot, and the empty spaces are where the eye goes.
        /// </summary>
        private const float Fill = 0.62f;

        public static GameObject Build(WorldModel world, Transform parent)
        {
            var root = new GameObject("CityParking");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 0.12f, 0f);   // as CityStreets

#if UNITY_EDITOR
            int lots = 0, tiles = 0, parked = 0;

            foreach (var place in world.AllPlaces)
            {
                if (PlaceKindTable.Current.Row(place.Kind).Name != "carpark") continue;
                lots++;
                tiles += Pave(root.transform, place.Bounds);
                parked += Park(root.transform, place.Bounds, place.Name);
            }

            Debug.Log($"[parking] {lots} lots, {tiles} tiles, {parked} cars standing still.");
#endif
            return root;
        }

#if UNITY_EDITOR

        /// <summary>
        /// Lay the asphalt, on whole tiles only.
        ///
        /// A lot whose dimensions are not a multiple of ten keeps a metre or two of pavement
        /// round its edge rather than getting a tile hanging over the kerb. That strip is
        /// exactly what a car park has round it anyway.
        /// </summary>
        private static int Pave(Transform parent, TileRect lot)
        {
            const int Cell = CityStreets.Cell;
            int n = 0;

            for (int y = lot.Y; y + Cell <= lot.Y + lot.H; y += Cell)
            for (int x = lot.X; x + Cell <= lot.X + lot.W; x += Cell)
                if (Seat(parent, Kit + "Road_Parking_10x10_City.prefab", x, y, Cell, Cell) != null)
                    n++;

            return n;
        }

        /// <summary>
        /// Park cars in rows down the long axis of the lot.
        ///
        /// Rows run along the LONG side, because that is how anybody lays out a rectangle of
        /// tarmac, and they come in back-to-back pairs with an aisle between - so a car faces
        /// out of its row, and the two rows of a pair face opposite ways.
        /// </summary>
        private static int Park(Transform parent, TileRect lot, string name)
        {
            var fleet = Fleet(name);
            if (fleet.Count == 0) return 0;

            bool alongX = lot.W >= lot.H;
            float length = alongX ? lot.W : lot.H;     // the direction rows run
            float depth = alongX ? lot.H : lot.W;      // the direction rows stack

            int n = 0;
            int row = 0;

            // Where each row of bays sits across the lot, and which way its cars face.
            //
            // A back-to-back PAIR plus its aisle is the normal case: the pair stands against one
            // edge, the aisle is what you drive down to reach the next pair, and the two rows of
            // a pair face opposite ways. A lot too shallow to hold a pair - the strip beside the
            // hospital is ten metres deep - gets a SINGLE row down the middle instead. Without
            // that case it got no cars at all and read as a slab of fresh tarmac.
            var rows = new List<(float across, bool facingUp)>();

            if (depth < Bay * 2f + 2f)
            {
                rows.Add((depth * 0.5f, true));
            }
            else
            {
                for (float band = 1.5f; band + Bay * 2f <= depth - 1f; band += Bay * 2f + Aisle)
                for (int half = 0; half < 2; half++)
                    rows.Add((band + Bay * (half + 0.5f), half == 1));
            }

            foreach (var (across, facingUp) in rows)
            {
                int half = facingUp ? 1 : 0;
                row++;

                for (float at = 2f; at <= length - 2f; at += Pitch)
                {
                    uint roll = Materials3D.Scatter(
                        Mathf.RoundToInt(at * 4f), row * 2 + half, 449);
                    if (roll % 100 >= Fill * 100) continue;      // a space, not a car

                    float vx = alongX ? lot.X + at : lot.X + across;
                    float vy = alongX ? lot.Y + across : lot.Y + at;

                    // Nose-in: the car points ACROSS the row, out of its bay.
                    float yaw = alongX
                        ? (facingUp ? 0f : 180f)          // rows run east-west: face north/south
                        : (facingUp ? 270f : 90f);        // rows run north-south: face west/east

                    var path = fleet[(int)(Materials3D.Scatter(
                        Mathf.RoundToInt(at), row * 2 + half, 457) % (uint)fleet.Count)];

                    if (Put(parent, path, vx, vy, yaw) != null) n++;
                }
            }
            return n;
        }

        /// <summary>
        /// What is parked at this particular lot.
        ///
        /// A police station's lot is mostly cruisers and a hospital's has an ambulance in it.
        /// That is not decoration: a player who has learned that the third car from the end
        /// outside the precinct is always a cruiser has learned something about the city, and
        /// the day it is a station wagon is a day something happened.
        /// </summary>
        private static List<string> Fleet(string lotName)
        {
            string lower = (lotName ?? "").ToLowerInvariant();

            var wanted = new List<string>
            {
                "Car_Modern", "Car_Pickup_Modern", "Car_Taxi_Modern",
                "Car_Cargovan_Modern", "Car_Sport_Modern", "Car_Roadster_Cabrio_Modern",
            };

            if (lower.Contains("precinct"))
                wanted = new List<string> { "Car_Police_Modern", "Car_Modern", "Car_Pickup_Modern" };
            else if (lower.Contains("hospital"))
                wanted.Add("Car_Ambulance_Modern");
            else if (lower.Contains("school"))
                wanted.Add("Car_Bus_School_Modern");

            var found = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Cars }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf("Collider", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                foreach (var w in wanted)
                    if (file == w || file.StartsWith(w + "_", System.StringComparison.Ordinal))
                    { found.Add(path); break; }
            }
            found.Sort(System.StringComparer.Ordinal);    // stable, so the lot looks the same twice
            return found;
        }

        /// <summary>A tile seated so its measured footprint covers the patch. As CityStreets.</summary>
        private static GameObject Seat(Transform parent, string path,
                                       float x, float y, float w, float h)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[parking] missing " + path); return null; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);

            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return go;

            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            var want = new Vector3(x + w / 2f, 0f, -(y + h / 2f));
            var drift = b.center - go.transform.position;
            go.transform.position =
                new Vector3(want.x - drift.x, parent.position.y, want.z - drift.z);
            return go;
        }

        private static GameObject Put(Transform parent, string path, float vx, float vy, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(vx, parent.position.y, -vy);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }
#endif
    }
}
