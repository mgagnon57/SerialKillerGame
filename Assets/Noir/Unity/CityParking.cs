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
    /// metre bay and a six metre aisle - and left deliberately unfull, which reads as parking
    /// whether or not it lands on the paint. What is NOT invented is how much room a car takes:
    /// that is measured off the vehicle, one at a time. See Width.
    ///
    /// Static, so this goes inside the node CityChunker bakes.
    /// </summary>
    public static class CityParking
    {
        private const string Kit = "Assets/polyperfect/Poly Universal Pack/Prefabs/City/Roads City/";
        private const string Cars = "Assets/polyperfect/Poly Universal Pack/Prefabs/Cars/Cars City";

        /// <summary>
        /// How deep a bay is, and how wide the aisle between two rows of them.
        ///
        /// BAY WAS THE LAST GUESSED NUMBER IN THIS FILE. Five metres is a reasonable-sounding
        /// bay and it is not what the pack ships: its cars run 5.0 to 5.5m nose to tail. A
        /// back-to-back PAIR was pitched exactly `Bay` apart with each car CENTRED on its row
        /// line, so two cars longer than Bay met in the middle and buried their tails in each
        /// other - the same fault as the old 2.8m pitch, one axis over, and invisible from
        /// above because it happens between the rows rather than along them.
        ///
        /// Bay is now only the SPACING the layout is built on; where a car actually stands
        /// inside its half of a pair comes off its own measured length. See Park.
        /// </summary>
        private const float Bay = 5.9f, Aisle = 6f;

        /// <summary>Daylight left between the tails of two cars standing back to back.</summary>
        private const float Spine = 0.3f;

        /// <summary>
        /// The gap left between one parked car and the next.
        ///
        /// THE PITCH IS MEASURED, NOT TYPED. It used to be a flat 2.8m between centres, which is
        /// a number I made up and never checked against a single vehicle - and the pack's cars
        /// are wider than that, so every car in every row was buried halfway into its neighbour,
        /// identically, all the way down. Exactly the fault the traffic light and the sign plate
        /// both were: a figure that should have come off the mesh, guessed instead.
        ///
        /// Now each car advances the row by ITS OWN measured width plus this, which also stops a
        /// cargo van being given the same bay as a hatchback.
        /// </summary>
        private const float Gap = 0.7f;

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
            // `band` is where the PAIR starts, not where a car stands: which of the two halves
            // a row is decides which end of the pair its cars back onto, and how far in from
            // that end depends on how long the particular car turns out to be.
            var rows = new List<(float band, int half, bool single)>();

            if (depth < Bay * 2f + 2f)
            {
                rows.Add((depth * 0.5f, 1, true));
            }
            else
            {
                for (float band = 1.5f; band + Bay * 2f <= depth - 1f; band += Bay * 2f + Aisle)
                for (int half = 0; half < 2; half++)
                    rows.Add((band, half, false));
            }

            foreach (var (band, half, single) in rows)
            {
                bool facingUp = half == 1;
                row++;

                int slot = 0;
                for (float at = 1f; at < length - 1f; slot++)
                {
                    var path = fleet[(int)(Materials3D.Scatter(
                        Mathf.RoundToInt(at), row * 2 + half, 457) % (uint)fleet.Count)];

                    // How much of the row this one takes. Measured off the prefab, so a cargo
                    // van is given a van's bay and a hatchback a hatchback's.
                    float wide = Width(path);
                    if (at + wide > length - 1f) break;

                    // How deep this one stands, and whether that is more than its half of the
                    // pair can hold. A school bus and an ambulance are half as long again as a
                    // hatchback; rather than let one hang into the row behind - which is the
                    // whole fault being fixed here - the slot is simply left empty, which a
                    // real car park is full of anyway.
                    float deep = Long(path);
                    float room = single ? depth - 1f : Bay - Spine * 0.5f;

                    uint roll = Materials3D.Scatter(slot, row * 2 + half, 449);
                    if (roll % 100 < Fill * 100 && deep <= room)
                    {
                        // Nose-in: the car points ACROSS the row, out of its bay, so its WIDTH
                        // is what the row has to find room for.
                        float yaw = alongX
                            ? (facingUp ? 0f : 180f)      // rows run east-west: face north/south
                            : (facingUp ? 270f : 90f);    // rows run north-south: face west/east

                        // WHERE IT SITS ACROSS THE ROW COMES OFF ITS OWN LENGTH. A car backs
                        // onto the outer edge of its half of the pair and points at the aisle,
                        // which is what a car park looks like and, more to the point, is what
                        // stops two cars meeting in the middle: each is inside its own half, so
                        // the tails cannot reach each other whatever the pair happens to be.
                        float across = single
                            ? band
                            : (half == 0 ? band + deep * 0.5f
                                         : band + Bay * 2f - deep * 0.5f);

                        float mid = at + wide * 0.5f;
                        float vx = alongX ? lot.X + mid : lot.X + across;
                        float vy = alongX ? lot.Y + across : lot.Y + mid;

                        if (Put(parent, path, vx, vy, yaw) != null) n++;
                    }

                    at += wide + Gap;
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

        private static readonly Dictionary<string, float> _wide = new Dictionary<string, float>();

        /// <summary>
        /// How wide this vehicle is across its own body, in metres.
        ///
        /// Off the mesh, not off the renderer: an unspawned prefab has no meaningful renderer
        /// bounds. A car is modelled nose-along-z, so its width is its x extent - and if a
        /// prefab turns out not to be, the fallback below is still wider than the 2.8m that was
        /// being used for every vehicle in the pack regardless of what it was.
        ///
        /// `sharedMesh.bounds` is in the MESH's own local space, untouched by any transform, so
        /// it has to be scaled by the mesh filter's OWN lossy scale before it can be added to
        /// `off` - which is a world-space offset and already carries the scale of every ancestor,
        /// root included. Skipping this half of the scale left a car's own half-width frozen at
        /// scale 1 while its offset from the root scaled normally, so a scaled-up prefab measured
        /// as wider than the original but not as wide as it actually was.
        /// </summary>
        private static float Width(string path)
        {
            if (_wide.TryGetValue(path, out float known)) return known;

            float lo = float.MaxValue, hi = float.MinValue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
                foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null) continue;
                    float off = mf.transform.position.x - prefab.transform.position.x;
                    float scale = mf.transform.lossyScale.x;
                    var b = mf.sharedMesh.bounds;
                    lo = Mathf.Min(lo, b.min.x * scale + off);
                    hi = Mathf.Max(hi, b.max.x * scale + off);
                }

            return _wide[path] = hi > lo ? hi - lo : 3.2f;
        }

        private static readonly Dictionary<string, float> _long = new Dictionary<string, float>();

        /// <summary>
        /// How long this vehicle is nose to tail, in metres. As <see cref="Width"/>, one axis
        /// over: these are modelled facing +z, so the length is the z extent.
        ///
        /// This is what decides how deep a bay has to be. It used to be assumed - a flat five
        /// metres - and the pack's cars run to 5.5, which is why the tails of two rows standing
        /// back to back were inside each other.
        /// </summary>
        private static float Long(string path)
        {
            if (_long.TryGetValue(path, out float known)) return known;

            float lo = float.MaxValue, hi = float.MinValue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
                foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null) continue;
                    float off = mf.transform.position.z - prefab.transform.position.z;
                    float scale = mf.transform.lossyScale.z;
                    var b = mf.sharedMesh.bounds;
                    lo = Mathf.Min(lo, b.min.z * scale + off);
                    hi = Mathf.Max(hi, b.max.z * scale + off);
                }

            return _long[path] = hi > lo ? hi - lo : 5.5f;
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

            float groundY = parent.position.y + ElevationGrid.HeightAt(x + w / 2f, y + h / 2f);
            var want = new Vector3(x + w / 2f, 0f, -(y + h / 2f));
            var drift = b.center - go.transform.position;
            go.transform.position =
                new Vector3(want.x - drift.x, groundY, want.z - drift.z);
            return go;
        }

        private static GameObject Put(Transform parent, string path, float vx, float vy, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.position =
                new Vector3(vx, parent.position.y + ElevationGrid.HeightAt(vx, vy), -vy);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }
#endif
    }
}
