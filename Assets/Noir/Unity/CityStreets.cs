using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// Lays the street itself out of bought models: road surface, kerbs, pavement, lamps, signs,
    /// bins and parked cars.
    ///
    /// Until this existed the city was bought buildings standing on Ashcombe's ground - a flat
    /// village-lane colour with nothing on it - which is why it read as models on a plane rather
    /// than as a street. Almost none of the pack was actually on screen.
    ///
    /// THE ROAD TILE IS 10m, exactly as the townhouse module is 6.1m, and the grid in city.txt is
    /// built on that. Tiles are chosen from the TERRAIN GRID rather than placed by hand, so the
    /// map stays the single description of the city and this stays a renderer: paint a road in
    /// city.txt and the surface, kerbs and lamps follow.
    ///
    /// Furniture is picked by scanning the pack's own folders rather than naming prefabs here.
    /// There are 232 cars and 181 props in it; a hand-written list would have used six.
    ///
    /// Editor-only in practice - the pieces load through AssetDatabase.
    /// </summary>
    public static class CityStreets
    {
        private const string Parts = "Assets/polyperfect/Poly Universal Pack/Prefabs/Modular Parts/Roads/";
        private const string CityProps = "Assets/polyperfect/Poly Universal Pack/Prefabs/City";

        /// <summary>The road kit's tile. Everything here is a multiple of it.</summary>
        public const int Cell = 10;

        public static GameObject Build(WorldModel world, Transform parent)
        {
            var root = new GameObject("CityStreets");
            root.transform.SetParent(parent, false);

            // A whisker above Ashcombe's ground, which is still drawn underneath at y=0. The
            // sidewalk tile is a flat plane at exactly zero, so without this the two planes
            // z-fight and the street comes out in blotches that look like a texture fault.
            root.transform.localPosition = new Vector3(0f, 0.04f, 0f);

#if UNITY_EDITOR
            int cols = world.Width / Cell, rows = world.Height / Cell;

            // Is this whole 10m cell road? Sampled at the centre, because a road painted 10 wide
            // lands exactly on a cell and anything narrower is a path, not a carriageway.
            bool IsRoad(int cx, int cy)
            {
                if (cx < 0 || cy < 0 || cx >= cols || cy >= rows) return false;
                return world.Grid.TerrainAt(cx * Cell + Cell / 2, cy * Cell + Cell / 2) == Noir.Core.World.Terrain.Road;
            }

            var lamps = Catalogue(CityProps + "/Lamps City", "Lamp_Sidewalk");
            var cars = Catalogue(CityProps + "/../Cars", null);
            var bins = Catalogue(CityProps + "/Props City", "Bin_");
            var lights = Catalogue(CityProps + "/TrafficLights City", "Traffic_Light");
            var planters = Catalogue(CityProps + "/Park City", "City_Planter");
            var potted = Catalogue(CityProps + "/Park City", "Tree_Pot_City");
            var play = Catalogue(CityProps + "/Playground City", null);
            var skate = Catalogue(CityProps + "/SkatePark City", "Skate_");
            var poles = Catalogue(CityProps + "/Poles City", "Pole_Electric_A");

            int tiles = 0, dressing = 0;

            for (int cy = 0; cy < rows; cy++)
            for (int cx = 0; cx < cols; cx++)
            {
                // A tile occupies x from its west edge to its east, and the prefab's pivot is at
                // the corner OPPOSITE the origin: it fills x[-10,0] z[-10,0] about its own point.
                // Village y runs into Unity -z, as everywhere else.
                var at = new Vector3(cx * Cell + Cell, 0f, -(cy * Cell));

                if (IsRoad(cx, cy))
                {
                    bool ew = IsRoad(cx - 1, cy) || IsRoad(cx + 1, cy);
                    bool ns = IsRoad(cx, cy - 1) || IsRoad(cx, cy + 1);

                    // Every road in the city runs edge to edge, so the only cases are a straight
                    // and a crossing. Turns and T-pieces exist in the kit and are not needed
                    // until a road stops somewhere.
                    string piece = ew && ns ? "Road_Paved_X_10x10m" : "Road_Paved_Straight_10x10m";
                    float yaw = ew && ns ? 0f : (ew ? 90f : 0f);

                    if (Put(root.transform, Parts + piece + ".prefab", at, yaw) != null) tiles++;

                    // A crossing gets signals on the corner it is entered from. Four would be
                    // correct and reads as a thicket at this scale; one apiece says junction.
                    if (ew && ns && lights.Count > 0)
                    {
                        var corner = at + new Vector3(-Cell + 1f, 0f, -Cell + 1f);
                        if (Put(root.transform, Pick(lights, cx, cy, 5), corner, 0f) != null) dressing++;
                    }

                    // Lamps stand at the kerb where the carriageway meets the pavement, one
                    // every other tile so a street is lit without becoming a fence of poles.
                    if (lamps.Count > 0 && ew && !ns && (cx & 1) == 0)
                    {
                        var kerb = at + new Vector3(-Cell / 2f, 0f, 0.5f);
                        if (Put(root.transform, Pick(lamps, cx, cy, 11), kerb, 0f) != null) dressing++;
                    }

                    // A car at the kerb on some straights. Nothing says "a street somebody lives
                    // on" faster than something parked badly on it.
                    if (cars.Count > 0 && ew && !ns && (cx % 3) == 1)
                    {
                        var bay = at + new Vector3(-Cell / 2f, 0f, -2.5f);
                        if (Put(root.transform, Pick(cars, cx, cy, 29), bay, 90f) != null) dressing++;
                    }
                }
                // Pavement FRAMES the carriageway; it does not carpet the map. Tiling every cell
                // paved the whole two hundred metres in one repeating stone pattern and the city
                // read as a retail park with houses parked on it. A pavement is the edge of a
                // street, so it only exists where a street is.
                else if (world.Grid.TerrainAt(cx * Cell + Cell / 2, cy * Cell + Cell / 2) == Noir.Core.World.Terrain.Path
                         && (IsRoad(cx - 1, cy) || IsRoad(cx + 1, cy) || IsRoad(cx, cy - 1) || IsRoad(cx, cy + 1)))
                {
                    if (Put(root.transform, Parts + "Sidewalk_Paved_10x10m.prefab", at, 0f) != null) tiles++;

                    // Pavement furniture. A bare kerb is what makes a street read as a diagram,
                    // and the pack has thirty planters and potted trees nobody had touched.
                    uint roll = Materials3D.Scatter(cx, cy, 613) % 6;
                    var spot = at + new Vector3(-Cell / 2f, 0f, -Cell / 2f);

                    if (roll == 0 && bins.Count > 0)
                    { if (Put(root.transform, Pick(bins, cx, cy, 71), spot, 0f) != null) dressing++; }
                    else if (roll == 1 && planters.Count > 0)
                    { if (Put(root.transform, Pick(planters, cx, cy, 83), spot, 0f) != null) dressing++; }
                    else if (roll == 2 && potted.Count > 0)
                    { if (Put(root.transform, Pick(potted, cx, cy, 97), spot, 0f) != null) dressing++; }
                    else if (roll == 3 && poles.Count > 0)
                    { if (Put(root.transform, Pick(poles, cx, cy, 103), spot, 0f) != null) dressing++; }
                }
                // The parks: the only unpaved ground, and where the playground and the skatepark
                // live. Both were bought and neither had ever been on screen.
                else if (world.Grid.TerrainAt(cx * Cell + Cell / 2, cy * Cell + Cell / 2)
                         == Noir.Core.World.Terrain.Grass)
                {
                    var kit = (cx + cy) % 2 == 0 ? play : skate;
                    if (kit.Count == 0) continue;

                    // Three pieces to a park cell, spread so it reads as equipment rather than
                    // as one object dropped in the middle of a lawn.
                    for (int k = 0; k < 3; k++)
                    {
                        float ox = -2f - (Materials3D.Scatter(cx * 7 + k, cy, 131) % 6);
                        float oz = -2f - (Materials3D.Scatter(cx, cy * 7 + k, 137) % 6);
                        var where = at + new Vector3(ox, 0f, oz);
                        if (Put(root.transform, Pick(kit, cx + k, cy, 149 + k), where,
                                Materials3D.Scatter(cx, cy + k, 151) % 4 * 90f) != null) dressing++;
                    }
                }
            }

            Debug.Log($"[streets] {tiles} road and pavement tiles, {dressing} pieces of furniture, "
                    + $"{root.GetComponentsInChildren<Renderer>().Length} renderers.");
#endif
            return root;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Every prefab in a folder, so the city draws on the whole library rather than on the
        /// half-dozen names somebody could be bothered to type.
        /// </summary>
        private static List<string> Catalogue(string folder, string startsWith)
        {
            var found = new List<string>();
            folder = System.IO.Path.GetFullPath(folder).Replace('\\', '/');
            int at = folder.IndexOf("Assets/", System.StringComparison.Ordinal);
            if (at >= 0) folder = folder.Substring(at);

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf("Collider", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (startsWith != null &&
                    System.IO.Path.GetFileName(path).StartsWith(startsWith, System.StringComparison.Ordinal) == false)
                    continue;
                found.Add(path);
            }
            found.Sort(System.StringComparer.Ordinal);   // stable, so the city looks the same twice
            return found;
        }

        private static string Pick(List<string> from, int x, int y, int salt) =>
            from[(int)(Materials3D.Scatter(x, y, salt) % (uint)from.Count)];

        private static GameObject Put(Transform parent, string path, Vector3 at, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }
#endif
    }
}
