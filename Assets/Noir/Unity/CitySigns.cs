using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// The road signs, each one placed because of something that is actually there.
    ///
    /// WHAT THIS REPLACES. CityStreets used to furnish the kerb by asking the pack for anything
    /// beginning with "Sign_" and dropping it every fourth cell at a fixed offset, facing 180
    /// degrees whichever way the street ran. Two things were wrong with that and both of them
    /// were on screen:
    ///
    ///   THE FOLDER IS TWO KITS. Sign_Stop_A_City measures y -0.38..0.38 - it is the PLATE, 3cm
    ///   thick, pivoted at its own centre and meant to be mounted on something. Sign_Stop_B_City
    ///   measures y -0.10..2.38 and is the plate AND its post. Catalogued together and dropped at
    ///   ground level, half the road signs in Northgate were bare metal discs sunk to their
    ///   equator in the pavement.
    ///
    ///   A SIGN THAT SAYS NOTHING IS WORSE THAN NO SIGN. Picking uniformly out of 171 put deer
    ///   crossings, cattle crossings, Route 66 shields and airport signs on a downtown kerb, and
    ///   speed limits between 10 and 65 on the same street. For a game where the player has to
    ///   learn a city well enough to know when somebody was where they should not have been,
    ///   signage that cannot be read is worse than a bare pavement - it is a bare pavement you
    ///   wasted time reading.
    ///
    /// So every sign here is placed FROM something: the class of the road it stands on, the
    /// right of way at the junction ahead, or the building it is pointing at.
    ///
    /// THE RIGHT-OF-WAY SIGNS AND THE TRAFFIC READ THE SAME ANSWER. Which arm of a country
    /// junction gives way is CitySignals.GiveWayAxisOf, called from here and from the traffic,
    /// so the stop sign is on the arm that actually stops. Written as two independent judgements
    /// they would agree today and drift the first time either was touched.
    ///
    /// Static, so this goes inside the node CityChunker bakes.
    /// </summary>
    public static class CitySigns
    {
        private const string Kit = "Assets/polyperfect/Poly Universal Pack/Prefabs/City/Signs City/";

        /// <summary>How far out from the centre line a sign stands: just off the tarmac.</summary>
        private static float Verge(RoadClass klass) => CityStreets.Asphalt(klass) + 1.6f;

        /// <summary>One speed limit per this many metres of road, in each direction.</summary>
        private const float SpeedEvery = 240f;

        /// <summary>How far back from a junction the warning for it goes.</summary>
        private const float WarningBack = 45f;

        /// <summary>How far back from a building its own sign goes.</summary>
        private const float LandmarkBack = 55f;

        /// <summary>
        /// What a road of each class tells you its limit is.
        ///
        /// The pack carries every 5mph step from 10 to 65, so this is a choice rather than a
        /// compromise: an arterial, an ordinary main road, a residential street. A dirt track
        /// gets nothing, because nobody posts a limit on the way to a barn.
        /// </summary>
        private static string SpeedFor(RoadClass klass) => klass switch
        {
            RoadClass.Freeway  => "Sign_Speed_55_B_City",
            RoadClass.Mainroad => "Sign_Speed_35_B_City",
            RoadClass.Street   => "Sign_Speed_25_B_City",
            _                  => null,
        };

        public static GameObject Build(WorldModel world, Transform parent)
        {
            var root = new GameObject("CitySigns");
            root.transform.SetParent(parent, false);

            // The same lift CityStreets uses. A sign stands on the pavement, and the pavement is
            // 12cm above the village's own ground plane.
            root.transform.localPosition = new Vector3(0f, 0.12f, 0f);

#if UNITY_EDITOR
            int rightOfWay = RightOfWay(world, root.transform);
            int limits = SpeedLimits(world, root.transform);
            int landmarks = Landmarks(world, root.transform);
            int country = CountryWarnings(world, root.transform);

            Debug.Log($"[signs] {rightOfWay} right-of-way, {limits} speed limits, "
                    + $"{landmarks} landmark, {country} country. "
                    + $"{root.GetComponentsInChildren<Renderer>().Length} renderers.");
#endif
            return root;
        }

#if UNITY_EDITOR

        // ---- right of way ---------------------------------------------------------------

        /// <summary>
        /// Stop signs on the arm that gives way, and a crossroads warning on the arm that does
        /// not - at every junction the town's signals do not cover.
        ///
        /// The signalised junctions get nothing: a stop sign under a traffic light is a
        /// contradiction, and drivers here obey the light.
        /// </summary>
        private static int RightOfWay(WorldModel world, Transform parent)
        {
            int n = 0;

            foreach (var j in world.Roads.Junctions)
            {
                if (CitySignals.InTheTown(world, j)) continue;

                bool northSouthGivesWay = CitySignals.GiveWayAxisOf(j);
                var stops = northSouthGivesWay ? j.NorthSouth : j.EastWest;
                var runs  = northSouthGivesWay ? j.EastWest   : j.NorthSouth;

                // Both directions of the arm that has to stop. A stop sign faces the driver it
                // is for, so it goes on the right-hand kerb of each approach.
                foreach (var travel in Approaches(stops.IsNorthSouth))
                {
                    var at = Beside(j, stops, travel, stops.HalfWidth + 2.5f, Verge(stops.Class));
                    if (Put(parent, "Sign_Stop_B_City", at, travel) != null) n++;
                }

                // And a warning on the road that runs through - but only where the thing being
                // warned about is a made road. A signed crossroads on an arterial that turns out
                // to be a farm gate reads as noise, and there are two of those on this map.
                if (stops.Class < RoadClass.Mainroad) continue;

                foreach (var travel in Approaches(runs.IsNorthSouth))
                {
                    var at = Beside(j, runs, travel, runs.HalfWidth + WarningBack, Verge(runs.Class));
                    if (!OnTheRoad(world, runs, at)) continue;
                    if (Put(parent, "Sign_Crossroad_B_City", at, travel) != null) n++;
                }
            }
            return n;
        }

        // ---- limits ---------------------------------------------------------------------

        /// <summary>
        /// A limit sign every few hundred metres along each made road, in both directions.
        ///
        /// Spaced off the road's own declared run rather than off the map, so a road that stops
        /// short does not get a limit posted in a field past the end of it.
        /// </summary>
        private static int SpeedLimits(WorldModel world, Transform parent)
        {
            int n = 0;

            foreach (var line in world.Roads.Lines)
            {
                if (!line.IsStraight) continue;
                string sign = SpeedFor(line.Class);
                if (sign == null) continue;

                foreach (var travel in Approaches(line.IsNorthSouth))
                {
                    // Start a little inside the run and step along it. Offset by direction so
                    // the two directions do not end up back to back on the same post.
                    bool forward = travel.x + travel.y > 0f;
                    float first = line.From + (forward ? 60f : 160f);

                    for (float along = first; along < line.To - 40f; along += SpeedEvery)
                    {
                        var at = Along(line, along, travel, Verge(line.Class));
                        if (InAnyJunction(world, at)) continue;
                        if (Put(parent, sign, at, travel) != null) n++;
                    }
                }
            }
            return n;
        }

        // ---- what is down this road -----------------------------------------------------

        /// <summary>
        /// Which kinds of place are worth a sign of their own, and which sign.
        ///
        /// The test is whether a stranger would look for it. You go to a hospital because you
        /// need one and you have never been; you do not go to a newsstand that way.
        /// </summary>
        private static readonly Dictionary<string, string> Landmark = new Dictionary<string, string>
        {
            { "hospital",    "Sign_Hospital_B_City" },
            { "school2",     "Sign_School_B_City" },
            { "gasstation",  "Sign_Gasoline_B_City" },
            { "diner",       "Sign_Restaurant_B_City" },
            { "precinct",    "Sign_Emergency_B_City" },
            { "firestation", "Sign_Emergency_B_City" },
            { "carpark",     "Sign_Parking_B_City" },
        };

        /// <summary>
        /// A sign on the approach to each landmark, on the road that actually passes it.
        ///
        /// Both directions, set back far enough to be a direction rather than an announcement.
        /// </summary>
        private static int Landmarks(WorldModel world, Transform parent)
        {
            int n = 0;

            foreach (var place in world.AllPlaces)
            {
                string kind = PlaceKindTable.Current.Row(place.Kind).Name;
                if (!Landmark.TryGetValue(kind, out string sign)) continue;

                var centre = place.Bounds.Centre;
                var line = Nearest(world, centre.X, centre.Y);
                if (line == null) continue;

                // Where along the road the building is.
                float at = line.IsNorthSouth ? centre.Y : centre.X;

                foreach (var travel in Approaches(line.IsNorthSouth))
                {
                    bool forward = travel.x + travel.y > 0f;
                    float along = forward ? at - LandmarkBack : at + LandmarkBack;
                    if (along < line.From + 10f || along > line.To - 10f) continue;

                    var where = Along(line, along, travel, Verge(line.Class));
                    if (InAnyJunction(world, where)) continue;
                    if (Put(parent, sign, where, travel) != null) n++;
                }
            }
            return n;
        }

        // ---- out of town ----------------------------------------------------------------

        /// <summary>
        /// Deer where a road runs past a wood, cattle where it runs past a paddock.
        ///
        /// These are the two signs the old random scatter put downtown, and they are perfectly
        /// good signs - they were simply in the wrong place. Here they mark something the map
        /// actually contains, which is also a small piece of information: a wood beside a road is
        /// somewhere a car can stop and not be seen from it.
        /// </summary>
        private static int CountryWarnings(WorldModel world, Transform parent)
        {
            int n = 0;

            foreach (var place in world.AllPlaces)
            {
                string kind = PlaceKindTable.Current.Row(place.Kind).Name;
                string sign = kind == "copse"   ? "Sign_Deer_B_City"
                            : kind == "paddock" ? "Sign_Cattle_B_City"
                            : null;
                if (sign == null) continue;

                var centre = place.Bounds.Centre;
                var line = Nearest(world, centre.X, centre.Y);
                if (line == null) continue;

                // Only if the road actually runs alongside it. A wood four hundred metres from
                // the nearest road does not warrant a deer sign on that road.
                float across = line.IsNorthSouth
                    ? Mathf.Abs(centre.X - line.Centre) : Mathf.Abs(centre.Y - line.Centre);
                float reach = (line.IsNorthSouth ? place.Bounds.W : place.Bounds.H) * 0.5f + 40f;
                if (across > reach) continue;

                float at = line.IsNorthSouth ? centre.Y : centre.X;
                foreach (var travel in Approaches(line.IsNorthSouth))
                {
                    bool forward = travel.x + travel.y > 0f;
                    float along = forward ? at - 50f : at + 50f;
                    if (along < line.From + 10f || along > line.To - 10f) continue;

                    var where = Along(line, along, travel, Verge(line.Class));
                    if (InAnyJunction(world, where)) continue;
                    if (Put(parent, sign, where, travel) != null) n++;
                }
            }
            return n;
        }

        // ---- geometry -------------------------------------------------------------------

        /// <summary>The two directions of travel along a road, in village coordinates.</summary>
        private static Vector2[] Approaches(bool northSouth) => northSouth
            ? new[] { new Vector2(0f, 1f), new Vector2(0f, -1f) }
            : new[] { new Vector2(1f, 0f), new Vector2(-1f, 0f) };

        /// <summary>
        /// A point beside a road, on the RIGHT of a car travelling <paramref name="travel"/>.
        ///
        /// Right of travel in village space is (-dy, dx): heading south, your right hand points
        /// west. This city drives on the right, so that is the kerb a sign is read from.
        /// </summary>
        private static Vector2 Along(RoadLine line, float along, Vector2 travel, float outward)
        {
            var right = new Vector2(-travel.y, travel.x);
            float cross = line.Centre + (line.IsNorthSouth ? right.x : right.y) * outward;
            return line.IsNorthSouth ? new Vector2(cross, along) : new Vector2(along, cross);
        }

        /// <summary>Back down an approach from a junction, and out to the kerb.</summary>
        private static Vector2 Beside(Junction j, RoadLine line, Vector2 travel,
                                      float back, float outward)
        {
            float at = (line.IsNorthSouth ? j.Y : j.X) - (travel.x + travel.y) * back;
            return Along(line, at, travel, outward);
        }

        /// <summary>The road whose corridor centre is nearest a point, or null.</summary>
        private static RoadLine Nearest(WorldModel world, float x, float y)
        {
            RoadLine best = null;
            float nearest = float.MaxValue;

            foreach (var line in world.Roads.Lines)
            {
                if (!line.IsStraight) continue;

                // Only if the point is alongside the road rather than off the end of it.
                float along = line.IsNorthSouth ? y : x;
                if (along < line.From || along > line.To) continue;

                float d = Mathf.Abs((line.IsNorthSouth ? x : y) - line.Centre);
                if (d < nearest) { nearest = d; best = line; }
            }
            return best;
        }

        private static bool InAnyJunction(WorldModel world, Vector2 at)
        {
            foreach (var j in world.Roads.Junctions)
                if (Mathf.Abs(at.x - j.X) < j.Reach + 3f && Mathf.Abs(at.y - j.Y) < j.Reach + 3f)
                    return true;
            return false;
        }

        /// <summary>Is this point still within the road's declared run?</summary>
        private static bool OnTheRoad(WorldModel world, RoadLine line, Vector2 at)
        {
            float along = line.IsNorthSouth ? at.y : at.x;
            return along >= line.From && along <= line.To;
        }

        // ---- placing --------------------------------------------------------------------

        private static GameObject _base;

        /// <summary>
        /// Stand a sign up facing the traffic it is for.
        ///
        /// MEASURED, NOT ASSUMED. The prefab is asked how tall it is, and a piece of geometry
        /// that does not reach the ground is treated as a PLATE and given a post to stand on
        /// rather than being dropped on the pavement to sink to its equator. Naming only the _B_
        /// variants above should mean this never fires - but it is the check that makes the
        /// whole class of fault impossible rather than merely absent today, and it says so in
        /// the log when it does.
        /// </summary>
        /// <param name="name">
        /// The prefab's NAME, not its path. The folder and the extension are added here, because
        /// every call site building its own path is how the first run of this placed exactly
        /// nothing: `Kit + "Sign_Stop_B_City"` is missing the ".prefab", AssetDatabase returned
        /// null for all four hundred of them, and the only sign that anything was wrong was a
        /// warning in a log nobody had to read.
        /// </param>
        private static GameObject Put(Transform parent, string name, Vector2 at, Vector2 travel)
        {
            string path = Kit + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[signs] missing " + path); return null; }

            // Face the driver: a car travelling `travel` in village space moves along
            // (travel.x, -travel.y) in Unity, so the sign must look back down that.
            var facing = new Vector3(-travel.x, 0f, travel.y);
            var rotation = Quaternion.LookRotation(facing, Vector3.up);
            var ground = new Vector3(at.x, parent.position.y + ElevationGrid.HeightAt(at.x, at.y), -at.y);

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.rotation = rotation;

            var b = Measure(go);
            bool complete = b.max.y > 1.5f && b.min.y < 0.4f;

            if (complete)
            {
                go.transform.position = ground;
                return go;
            }

            // A plate. Give it a post, and sit it just below the top of that post.
            Debug.LogWarning($"[signs] {System.IO.Path.GetFileNameWithoutExtension(path)} is a "
                           + "bare plate - mounting it on a post.");

            _base ??= AssetDatabase.LoadAssetAtPath<GameObject>(Kit + "Sign_Base_City.prefab");
            if (_base == null) { go.transform.position = ground; return go; }

            var post = (GameObject)PrefabUtility.InstantiatePrefab(_base);
            post.transform.SetParent(parent, false);
            post.transform.position = ground;
            post.transform.rotation = rotation;

            float top = Measure(post).max.y;
            go.transform.position = ground
                + Vector3.up * (top - b.size.y * 0.5f - 0.15f)
                + facing * 0.12f;                       // proud of the post rather than in it
            return go;
        }

        private static Bounds Measure(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.zero);

            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            // Relative to where the object currently is, so "does it reach the ground" is a
            // question about the MODEL rather than about where it happens to be standing.
            b.center -= go.transform.position;
            return b;
        }
#endif
    }
}
