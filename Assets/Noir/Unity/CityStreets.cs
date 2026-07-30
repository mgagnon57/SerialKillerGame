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
            var lights = Catalogue(CityProps + "/TrafficLights City", "Traffic_Light");
            var play = Catalogue(CityProps + "/Playground City", null);
            var skate = Catalogue(CityProps + "/SkatePark City", "Skate_");

            // What actually stands on a pavement, as against one bin every seventh cell. Each
            // of these is a role rather than a prefab, so the pack's own variants get used:
            // there are five bins, four bollards and two phone boxes in here and a hand-written
            // list would have picked one of each.
            var kerbside = new List<List<string>>
            {
                Catalogue(CityProps + "/Props City", "Hydrant"),
                Catalogue(CityProps + "/Props City", "Bollard_"),
                Catalogue(CityProps + "/Props City", "Bin_"),
                Catalogue(CityProps + "/Props City", "Parking_Machine"),
                Catalogue(CityProps + "/Props City", "Telephone_Booth"),
                Catalogue(CityProps + "/Props City", "Newspaper_Stand"),
                Catalogue(CityProps + "/Props City", "Clock_"),
                Catalogue(CityProps + "/Props City", "Stand_"),
                Catalogue(CityProps + "/Park City", "City_Planter"),
                Catalogue(CityProps + "/Park City", "Tree_Pot_City"),
                Catalogue(CityProps + "/Props City", "Barrier_"),
                Catalogue(CityProps + "/Props City", "Cone"),
            };
            kerbside.RemoveAll(c => c.Count == 0);

            var manholes = Catalogue(CityProps + "/Props City", "Manhole");
            var signs = Catalogue(CityProps + "/Signs City", "Sign_");

            // Nature has 760 prefabs and the city was using none of them. Trees City is the
            // nine authored for a pavement; the rest is what a park is made of.
            const string Nat = "Assets/polyperfect/Poly Universal Pack/Prefabs/Nature";
            var streetTrees = Catalogue(Nat + "/Trees City", null);
            var parkKit = new List<List<string>>
            {
                Catalogue(Nat + "/Trees", null),
                Catalogue(Nat + "/Bushes", null),
                Catalogue(Nat + "/Flowers", null),
                Catalogue(Nat + "/Rocks", null),
                Catalogue(Nat + "/Grass", null),
            };
            parkKit.RemoveAll(c => c.Count == 0);

            // What piles up behind a building rather than in front of it.
            var alley = new List<List<string>>
            {
                Catalogue(CityProps + "/Props City/Garbage Props", null),
                Catalogue(CityProps + "/Props City/Pipes Props", null),
            };
            alley.RemoveAll(c => c.Count == 0);

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

                    // A crossing gets signals and a stop sign on the corner it is entered from.
                    if (ew && ns)
                    {
                        var corner = at + new Vector3(-Cell + 1.2f, 0f, -Cell + 1.2f);
                        if (lights.Count > 0 && Put(root.transform, Pick(lights, cx, cy, 5), corner, 0f) != null)
                            dressing++;
                        if (signs.Count > 0 &&
                            Put(root.transform, Pick(signs, cx, cy, 17), corner + new Vector3(0f, 0f, -2f), 180f) != null)
                            dressing++;
                    }
                    else
                    {
                        // A straight gets both its kerbs walked. Everything below is placed
                        // ALONG the carriageway rather than once per tile, which is the whole
                        // difference between a street with things on it and a road with a bin.
                        dressing += Kerb(root.transform, at, cx, cy, ew, lamps, cars, kerbside, signs);

                        if (manholes.Count > 0 && Materials3D.Scatter(cx, cy, 191) % 3 == 0)
                        {
                            var mid = at + new Vector3(-Cell / 2f, 0f, -Cell / 2f);
                            if (Put(root.transform, Pick(manholes, cx, cy, 193), mid, 0f) != null) dressing++;
                        }
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

                    // The back of the pavement, against the buildings, gets its own scatter -
                    // the kerb walk above only furnishes the road edge, and a pavement has two.
                    if (kerbside.Count > 0 && Materials3D.Scatter(cx, cy, 613) % 3 == 0)
                    {
                        var role = kerbside[(int)(Materials3D.Scatter(cy, cx, 617) % (uint)kerbside.Count)];
                        var spot = at + new Vector3(-Cell / 2f, 0f, -Cell / 2f);
                        if (Put(root.transform, Pick(role, cx, cy, 619), spot, 0f) != null) dressing++;
                    }

                    // Street trees. Nature/Trees City is the nine authored to stand in a
                    // pavement rather than in a wood, and they do more for a street than any
                    // other single prop - they break the roofline and cast something on it.
                    if (streetTrees.Count > 0 && Materials3D.Scatter(cx, cy, 641) % 2 == 0)
                    {
                        var verge = at + new Vector3(-Cell + 2f, 0f, -2f);
                        if (Put(root.transform, Pick(streetTrees, cx, cy, 643), verge,
                                Materials3D.Scatter(cx, cy, 647) % 4 * 90f) != null) dressing++;
                    }
                }
                // Behind the buildings. Everything a street hides round the back: bins, crates,
                // pallets, tyres, and the pipework that runs up an alley wall.
                else if (world.Grid.TerrainAt(cx * Cell + Cell / 2, cy * Cell + Cell / 2)
                         == Noir.Core.World.Terrain.Path && alley.Count > 0)
                {
                    if (Materials3D.Scatter(cx, cy, 653) % 3 != 0) continue;
                    for (int k = 0; k < 2; k++)
                    {
                        var role = alley[(int)(Materials3D.Scatter(cx + k, cy, 659) % (uint)alley.Count)];
                        float ox = -2f - (Materials3D.Scatter(cx * 5 + k, cy, 661) % 7);
                        float oz = -2f - (Materials3D.Scatter(cx, cy * 5 + k, 673) % 7);
                        if (Put(root.transform, Pick(role, cx + k, cy, 677), at + new Vector3(ox, 0f, oz),
                                Materials3D.Scatter(cx + k, cy, 683) % 4 * 90f) != null) dressing++;
                    }
                }
                // The parks: the only unpaved ground, and where the playground and the skatepark
                // live. Both were bought and neither had ever been on screen.
                else if (world.Grid.TerrainAt(cx * Cell + Cell / 2, cy * Cell + Cell / 2)
                         == Noir.Core.World.Terrain.Grass)
                {
                    var kit = (cx + cy) % 2 == 0 ? play : skate;

                    // Three pieces of equipment to a park cell, spread so it reads as a park
                    // rather than as one object dropped in the middle of a lawn.
                    for (int k = 0; k < 3 && kit.Count > 0; k++)
                    {
                        float ox = -2f - (Materials3D.Scatter(cx * 7 + k, cy, 131) % 6);
                        float oz = -2f - (Materials3D.Scatter(cx, cy * 7 + k, 137) % 6);
                        var where = at + new Vector3(ox, 0f, oz);
                        if (Put(root.transform, Pick(kit, cx + k, cy, 149 + k), where,
                                Materials3D.Scatter(cx, cy + k, 151) % 4 * 90f) != null) dressing++;
                    }

                    // And the park itself - trees, bushes, flowers, rocks, tufts of grass. The
                    // pack has 760 nature prefabs and the city had been using precisely none.
                    for (int k = 0; k < 5 && parkKit.Count > 0; k++)
                    {
                        var role = parkKit[(int)(Materials3D.Scatter(cx + k, cy, 691) % (uint)parkKit.Count)];
                        float ox = -1f - (Materials3D.Scatter(cx * 11 + k, cy, 701) % 8);
                        float oz = -1f - (Materials3D.Scatter(cx, cy * 11 + k, 709) % 8);
                        if (Put(root.transform, Pick(role, cx + k, cy + k, 719), at + new Vector3(ox, 0f, oz),
                                Materials3D.Scatter(cx + k, cy, 727) % 4 * 90f) != null) dressing++;
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
        /// Walk both kerbs of one straight tile, placing what actually stands on a pavement.
        ///
        /// The point is that it walks ALONG the carriageway rather than dropping one prop per
        /// tile: a street is furnished at intervals of a few metres, and at ten it reads as a
        /// road with an ornament on it. Lamps keep a fixed rhythm because street lighting is
        /// laid out by a highways department; everything else is rolled, so no two blocks carry
        /// the same run of hydrant, meter, phone box and planter.
        /// </summary>
        private static int Kerb(Transform parent, Vector3 at, int cx, int cy, bool ew,
                                List<string> lamps, List<string> cars,
                                List<List<string>> kerbside, List<string> signs)
        {
            int n = 0;

            // Both sides of the street. The kerb line sits just inside the tile edge; the
            // pavement is outside it and the carriageway inside.
            for (int side = 0; side < 2; side++)
            {
                float kerb = side == 0 ? -0.9f : -Cell + 0.9f;   // near and far pavement
                float facing = side == 0 ? 180f : 0f;

                for (int step = 0; step < 3; step++)
                {
                    float along = -1.8f - step * 3.2f;
                    var spot = ew
                        ? at + new Vector3(along, 0f, kerb)
                        : at + new Vector3(kerb, 0f, along);
                    float yaw = ew ? facing : facing + 90f;

                    uint roll = Materials3D.Scatter(cx * 31 + step, cy * 17 + side, 211);

                    // Lighting on its own rhythm - twice a block, same place every block.
                    if (step == 1 && lamps.Count > 0 && ((cx + cy) & 1) == 0)
                    {
                        if (Put(parent, Pick(lamps, cx, cy + side, 11), spot, yaw) != null) n++;
                        continue;
                    }

                    // Not every metre of kerb has something on it, or it reads as a jumble sale.
                    if (roll % 5 >= 3) continue;

                    var role = kerbside[(int)(roll / 5 % (uint)kerbside.Count)];
                    if (Put(parent, Pick(role, cx + step, cy + side, 223), spot, yaw) != null) n++;
                }

                // Parked cars, nose to tail against the kerb and pointing the way the road runs.
                if (cars.Count > 0 && Materials3D.Scatter(cx, cy * 3 + side, 29) % 3 != 0)
                {
                    float lane = side == 0 ? -2.6f : -Cell + 2.6f;
                    var bay = ew
                        ? at + new Vector3(-Cell / 2f, 0f, lane)
                        : at + new Vector3(lane, 0f, -Cell / 2f);
                    float carYaw = (ew ? 90f : 0f) + (side == 0 ? 0f : 180f);
                    if (Put(parent, Pick(cars, cx, cy + side * 5, 29), bay, carYaw) != null) n++;
                }
            }

            // One traffic sign per block, on the near kerb, facing the traffic.
            if (signs.Count > 0 && Materials3D.Scatter(cx, cy, 233) % 4 == 0)
            {
                var post = ew
                    ? at + new Vector3(-Cell + 1.2f, 0f, -0.9f)
                    : at + new Vector3(-0.9f, 0f, -Cell + 1.2f);
                if (Put(parent, Pick(signs, cx, cy, 239), post, ew ? 180f : 270f) != null) n++;
            }

            return n;
        }

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

                // Nature ships the whole world in one folder. A palm or a cactus on Northgate
                // Avenue is not a bug in the pack, it is a bug in asking the pack for "a tree"
                // and taking whatever comes back.
                if (path.IndexOf("Palm", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (path.IndexOf("Cactus", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (path.IndexOf("Dead", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
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
