using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// The places in the country where something happened, or is about to.
    ///
    /// The Survival folder is 174 prefabs and was the best-matched set in the whole pack for this
    /// game - a hunting stand, bear traps, a wooden cross, road flares, abandoned suitcases,
    /// bedrolls, storm lanterns, a radio transceiver - and not one of them had ever been placed.
    /// It was held back on purpose, and the reason was right: WHERE A BEAR TRAP GOES IS A STORY
    /// DECISION, NOT A SCATTER RULE. A bear trap rolled onto a random woodland tile is set
    /// dressing. A bear trap thirty metres off a lane, behind a hedge, where somebody put it, is
    /// a fact about a person.
    ///
    /// So none of this is scattered. Each site is AUTHORED as a place in city.txt with its own
    /// name and its own sentence, exactly like a shop or a farm, and this builds what the kind
    /// says stands there. That also means every one of them is clickable, describable, and
    /// somewhere the simulation can refer to - which a prop rolled onto a tile is not.
    ///
    /// THE REGISTER IS THE ROADSIDE AND THE TREELINE. Not a horror set: a wooden cross on a verge,
    /// a hunting platform overlooking a field, a camp somebody left. Things that are ordinary
    /// until you know what happened, which is the whole tone this game is after.
    ///
    /// Static, so this goes inside the node CityChunker bakes.
    /// </summary>
    public static class CityStory
    {
        private const string Survival = "Assets/polyperfect/Poly Universal Pack/Prefabs/Survival/";

        public static GameObject Build(WorldModel world, Transform parent)
        {
            var root = new GameObject("CityStory");
            root.transform.SetParent(parent, false);

#if UNITY_EDITOR
            int sites = 0, pieces = 0;

            foreach (var place in world.AllPlaces)
            {
                string kind = PlaceKindTable.Current.Row(place.Kind).Name;
                var lot = place.Bounds;

                int made = kind switch
                {
                    "memorial" => Memorial(root.transform, lot),
                    "standing" => Standing(root.transform, lot),
                    "camp"     => Camp(root.transform, lot),
                    _          => -1,
                };

                if (made < 0) continue;
                sites++;
                pieces += made;
            }

            Debug.Log($"[story] {sites} sites, {pieces} pieces, "
                    + $"{root.GetComponentsInChildren<Renderer>().Length} renderers.");
#endif
            return root;
        }

#if UNITY_EDITOR

        /// <summary>
        /// A roadside memorial: a wooden cross, and whoever keeps it has been recently.
        ///
        /// It faces the ROAD rather than the field, because the whole point of one is that it is
        /// seen from a car. Which road it faces is asked of the map rather than authored, so a
        /// memorial keeps facing the traffic if the road is ever reclassified or moved.
        /// </summary>
        private static int Memorial(Transform parent, TileRect lot)
        {
            float cx = lot.X + lot.W * 0.5f, cy = lot.Y + lot.H * 0.5f;
            float yaw = Facing(cx, cy);
            int n = 0;

            if (Put(parent, Survival + "Cross_Wood.prefab", cx, cy, yaw) != null) n++;

            // A lantern at the foot of it, and one flare left from whenever it happened.
            if (Put(parent, Survival + "Lantern_Storm.prefab", cx + 0.7f, cy + 0.5f, yaw) != null) n++;
            if (Materials3D.Scatter(lot.X, lot.Y, 6317) % 2 == 0 &&
                Put(parent, Survival + "Road_Flare.prefab", cx - 0.9f, cy + 0.4f, yaw + 40f) != null) n++;

            return n;
        }

        /// <summary>
        /// A hunting platform, which in a game about somebody watching people is not a hunting
        /// platform. It looks over the open ground rather than into the trees, for the same
        /// reason a real one does.
        /// </summary>
        private static int Standing(Transform parent, TileRect lot)
        {
            float cx = lot.X + lot.W * 0.5f, cy = lot.Y + lot.H * 0.5f;
            int n = 0;

            if (Put(parent, Survival + "Tree_Stand.prefab", cx, cy,
                    Materials3D.Scatter(lot.X, lot.Y, 6323) % 4 * 90f) != null) n++;

            // A trap set a little way off it, in the two cases out of three where somebody is
            // using this for what it was built for.
            if (Materials3D.Scatter(lot.X, lot.Y, 6329) % 3 != 0 &&
                Put(parent, Survival + "Bear_Trap_Open_Rust.prefab",
                    cx + 6f, cy + 4f, 0f) != null) n++;

            return n;
        }

        /// <summary>
        /// Somebody camped here. The fire is out, and how long it has been out is the question.
        /// </summary>
        private static int Camp(Transform parent, TileRect lot)
        {
            float cx = lot.X + lot.W * 0.5f, cy = lot.Y + lot.H * 0.5f;
            uint roll = Materials3D.Scatter(lot.X, lot.Y, 6337);
            float yaw = roll % 4 * 90f;
            int n = 0;

            if (Put(parent, Survival + "Campfire.prefab", cx, cy, 0f) != null) n++;
            if (Put(parent, Survival + "Lean_To_Tarp.prefab", cx + 2.6f, cy + 1.4f, yaw) != null) n++;
            if (Put(parent, Survival + "Bedroll.prefab", cx + 2.2f, cy + 1.1f, yaw) != null) n++;
            if (Put(parent, Survival + "Lantern_Storm.prefab", cx - 1.2f, cy + 0.8f, 0f) != null) n++;

            // The thing that makes it a scene rather than a camp: somebody's case, left open.
            if (Put(parent, Survival + "Suitcase_Big_Open.prefab",
                    cx - 1.9f, cy - 1.6f, yaw + 25f) != null) n++;

            return n;
        }

        /// <summary>
        /// Which way to face to be looking at the nearest road.
        ///
        /// Asked of the road network rather than authored per site, so these keep facing the
        /// traffic if a road is ever moved or reclassified - the same reasoning SunRig uses to
        /// decide which class of lamp a stretch gets.
        /// </summary>
        private static float Facing(float vx, float vy)
        {
            var world = VillageHost.Instance != null ? VillageHost.Instance.World : null;
            if (world == null) return 0f;

            float best = float.MaxValue;
            float yaw = 0f;

            foreach (var line in world.Roads.Lines)
            {
                if (!line.IsStraight) continue;

                float across = line.IsNorthSouth ? vx : vy;
                float along = line.IsNorthSouth ? vy : vx;
                if (along < line.From || along > line.To) continue;

                float d = Mathf.Abs(across - line.Centre);
                if (d >= best) continue;

                best = d;
                // Face the carriageway: turn towards it across its own axis.
                yaw = line.IsNorthSouth
                    ? (across > line.Centre ? 270f : 90f)
                    : (across > line.Centre ? 0f : 180f);
            }
            return yaw;
        }

        /// <summary>A piece by its own base, at a point in village coordinates.</summary>
        private static GameObject Put(Transform parent, string path, float vx, float vy, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(vx, 0f, -vy);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }
#endif
    }
}
