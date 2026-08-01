using UnityEngine;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// The elevated railway, laid along a corridor authored in the map.
    ///
    /// An El is the most city-defining object in the pack: it runs ten metres over the top of a
    /// street on pillars, the street carries on underneath, and it puts something in the skyline
    /// that no amount of street furniture can. The station and the carriages ship with
    /// M_Universal_Glass_Night on them, so they are lit from the inside before anybody writes a
    /// lighting pass.
    ///
    /// The line is a `place railway` in city.txt rather than a constant here, so where it runs is
    /// a property of the city and not of the renderer - the same rule the rest of this follows.
    ///
    /// Editor-only in practice: the pieces load through AssetDatabase.
    /// </summary>
    public static class CityRail
    {
        private const string Rails = "Assets/polyperfect/Poly Universal Pack/Prefabs/City/Rails City/";

        /// <summary>
        /// How far one straight advances the line. The prefab measures 35.7m but the last 5.7m
        /// of it is overhang past the pivot, so the piece REPEATS every thirty.
        /// </summary>
        private const float Span = 30f;

        /// <summary>Half the deck's width, measured: the mesh sits x[-23.69,-6.31] about its pivot.</summary>
        private const float DeckOffset = 15f;

        public static GameObject Build(WorldModel world, Transform parent)
        {
            var root = new GameObject("CityRail");
            root.transform.SetParent(parent, false);

#if UNITY_EDITOR
            int n = 0;
            foreach (var place in world.AllPlaces)
            {
                if (PlaceKindTable.Current.Row(place.Kind).Name != "railway") continue;
                n += Line(root.transform, place.Bounds);
            }

            if (n > 0)
                Debug.Log($"[rail] {n} pieces of elevated railway, "
                        + $"{root.GetComponentsInChildren<Renderer>().Length} renderers.");
#endif
            return root;
        }

#if UNITY_EDITOR
        /// <summary>
        /// One line down the long axis of its corridor: a straight every thirty metres, a pillar
        /// between each pair, one station a third of the way along, and a train standing at it.
        /// </summary>
        private static int Line(Transform parent, TileRect lot)
        {
            bool northSouth = lot.H >= lot.W;
            int n = 0;

            // The centre line of the corridor, in Unity terms. Village y runs into -z.
            float cx = lot.X + lot.W / 2f;
            float cz = -(lot.Y + lot.H / 2f);
            float length = northSouth ? lot.H : lot.W;

            // Where the station goes - a third along, so it is not in the dead centre of frame.
            int sections = Mathf.Max(1, Mathf.FloorToInt(length / Span));
            int stationAt = sections / 3;

            // Real terrain under a point given in this file's own Unity-space (x, z) terms -
            // every position below used to assume y=0 was ground, which held while the whole map
            // was flat and stopped holding the moment real elevation gave it 24m of relief. The
            // pillars plant in whatever is actually underfoot and the deck (and the train riding
            // it) stay the fixed 10m above THAT, not above sea level.
            float Ground(float wx, float wz) => ElevationGrid.HeightAt(wx, -wz);

            for (int i = 0; i < sections; i++)
            {
                // Distance along the line from its start.
                float along = northSouth
                    ? -(lot.Y) - i * Span
                    : lot.X + (i + 1) * Span;

                bool station = i == stationAt;
                string piece = station
                    ? Rails + "Rails_Overground_Station_A_City.prefab"
                    : Rails + "Rails_Overground_Striaght_City.prefab";

                // The deck sits to one side of its pivot, so the pivot is pushed the other way
                // to land the track on the corridor's centre line.
                var at = northSouth
                    ? new Vector3(cx + DeckOffset, 0f, along)
                    : new Vector3(along, 0f, cz - DeckOffset);
                at.y = Ground(at.x, at.z);
                float yaw = northSouth ? 0f : 90f;

                if (Put(parent, piece, at, yaw) != null) n++;

                // A pillar under the joint between this section and the next.
                var pillar = northSouth
                    ? new Vector3(cx + DeckOffset, 0f, along - Span / 2f)
                    : new Vector3(along + Span / 2f, 0f, cz - DeckOffset);
                pillar.y = Ground(pillar.x, pillar.z);
                if (!station && Put(parent, Rails + "Rails_Overground_Pillar_City.prefab", pillar, yaw) != null)
                    n++;

                if (!station) continue;

                // A two-car train that RUNS the line rather than standing on it. Built under
                // its own node and driven by CityTrain, so it is never handed to the chunker -
                // a combined mesh cannot move.
                var train = new GameObject("Train");
                train.transform.SetParent(parent, false);

                for (int c = 0; c < 2; c++)
                {
                    string car = c == 0
                        ? Rails + "Train_Overground_Clean_City.prefab"
                        : Rails + "Carriage_Overground_Clean_City.prefab";

                    // The deck is 10m up and a carriage is 16.9m long, so they sit nose to tail.
                    var on = northSouth
                        ? new Vector3(cx, 10.05f, -c * 17f)
                        : new Vector3(-c * 17f, 10.05f, cz);
                    on.y += Ground(on.x, on.z);
                    if (Put(train.transform, car, on, yaw) != null) n++;
                }

                // End to end of the corridor, pausing where the platform is.
                var driver = train.AddComponent<CityTrain>();
                driver.Along = northSouth ? new Vector3(0f, 0f, -1f) : new Vector3(1f, 0f, 0f);
                driver.From = northSouth
                    ? new Vector3(0f, 0f, 20f)
                    : new Vector3(-20f, 0f, 0f);
                driver.To = northSouth
                    ? new Vector3(0f, 0f, -(lot.Y + lot.H) - 20f)
                    : new Vector3(lot.X + lot.W + 20f, 0f, 0f);
                driver.StopAt = northSouth
                    ? Mathf.InverseLerp(20f, -(lot.Y + lot.H) - 20f, along - 15f)
                    : Mathf.InverseLerp(-20f, lot.X + lot.W + 20f, along - 15f);
            }
            return n;
        }

        private static GameObject Put(Transform parent, string path, Vector3 at, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[rail] missing " + path); return null; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }
#endif
    }
}
