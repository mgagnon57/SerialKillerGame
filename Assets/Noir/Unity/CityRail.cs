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
                float yaw = northSouth ? 0f : 90f;

                if (Put(parent, piece, at, yaw) != null) n++;

                // A pillar under the joint between this section and the next.
                var pillar = northSouth
                    ? new Vector3(cx + DeckOffset, 0f, along - Span / 2f)
                    : new Vector3(along + Span / 2f, 0f, cz - DeckOffset);
                if (!station && Put(parent, Rails + "Rails_Overground_Pillar_City.prefab", pillar, yaw) != null)
                    n++;

                // A train standing at the platform. Two carriages, because one reads as a bus.
                if (!station) continue;
                for (int c = 0; c < 2; c++)
                {
                    string car = c == 0
                        ? Rails + "Train_Overground_Clean_City.prefab"
                        : Rails + "Carriage_Overground_Clean_City.prefab";

                    // On the deck: the track surface is 10m up, and a carriage is 16.9m long.
                    var on = northSouth
                        ? new Vector3(cx, 10.05f, along - 8f - c * 17f)
                        : new Vector3(along - 8f - c * 17f, 10.05f, cz);
                    if (Put(parent, car, on, yaw) != null) n++;
                }
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
