using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// Measures where the CARRIAGEWAY actually is inside a road tile.
    ///
    /// The first version of this probed the Modular Parts kit and found the lesson that a 10m
    /// tile is 6m of tarmac and 4m of pavement. It is kept and widened because the pack turns
    /// out to have a SECOND road kit - Prefabs/City/Roads City - built at 30m with painted lanes,
    /// stop lines, crosswalks and parking bays, which is the kit a city actually wants.
    ///
    /// Reports per-submesh extents via SubMeshDescriptor.bounds, which is importer metadata and
    /// so works whether or not Read/Write is enabled on the model.
    /// </summary>
    public static class RoadProbe
    {
        private const string Parts = "Assets/polyperfect/Poly Universal Pack/Prefabs/Modular Parts/Roads/";
        private const string City  = "Assets/polyperfect/Poly Universal Pack/Prefabs/City/Roads City/";

        [MenuItem("Noir/Probe Road Tile")]
        public static void Probe()
        {
            Debug.Log("=== MODULAR PARTS (the 10m kit the city is built from today) ===");
            foreach (var n in new[]
            {
                "Road_Paved_Straight_10x10m", "Mainroad_Paved_Straight_10x10m", "Road_Paved_X_10x10m",
            })
                One(Parts + n + ".prefab", n);

            Debug.Log("=== ROADS CITY (the 30m kit, never used) ===");
            foreach (var n in new[]
            {
                "Mainroad_Straight_30x30_City", "Mainroad_Cross_30x30_City",
                "Mainroad_Cross_Crosswalk_30x30_City", "Mainroad_T_Crosswalk_30x30_City",
                "Mainroad_Crosswalk_City", "Mainroad_Stop_30x30_City",
                "Mainroad_Stop_Middle_30x30_City", "MainRoad_Stop_Start_30x30_City",
                "Mainroad_Stop_Start_L_30x30_City", "Mainroad_Stop_Start_R_30x30_City",
                "Mainroad_Turn_30x30_City", "Mainroad_End_30x30_City",
                "Road_Straight_10x10_City", "Road_Cross_10x10_City", "Road_Crosswalk_10x10_City",
                "Road_Stop_10x10_City", "Road_T_10x10_City", "Road_Turn_10x10_City",
                "Road_Parking_10x10_City", "Road_Parking_Side_10x10_City",
                "MainroadToRoad_30x30_City", "MainroadToParking_30x30_City",
                "Sidewalk_30x30_City", "Sidewalk_10x10_City",
                "Freeway_Straight_30x30_City",

                // The pieces for junctions that are NOT four-way crossings. Every junction in
                // Rossville is laid with a Cross tile, including the two where a dirt track
                // dead-ends into a through road - so those crossings have a fourth arm painted
                // on them that leads into a paddock.
                "Freeway_T_30x30_City", "Freeway_Turn_30x30_City",
                "Mainroad_T_30x30_City", "Road_End_10x10_City",
                "FreewayToMainroad_30x30_City", "FreewayToMainroad_T_30x30_City",
                "FreewayToMainroad_Cross_30x30_City", "FreewayToMainroad_Exit_30x30_City",
                "Road_Parking_Corner_10x10_City", "Road_Parking_Entrance_10x10_City",
                "Road_Parking_Half_10x10_City", "Grass_10x10_City",
            })
                One(City + n + ".prefab", n);

            // What stands BESIDE the road. The country roads have nothing on their verges at
            // all, and the pack has nine electric poles - two of which carry a twenty-metre
            // span of wire, which is the piece that turns a road through a field into a road
            // somebody built.
            Debug.Log("=== STREET FURNITURE (verges and junction signs) ===");
            const string Poles = "Assets/polyperfect/Poly Universal Pack/Prefabs/City/Poles City/";
            const string Signs = "Assets/polyperfect/Poly Universal Pack/Prefabs/City/Signs City/";
            foreach (var n in new[]
            {
                Poles + "Pole_Electric_A_City", Poles + "Pole_Electric_B_City",
                Poles + "Pole_Electric_Double_A_City",
                Poles + "Pole_Electric_Wire_20m_A_City", Poles + "Pole_Electric_Wire_20m_B_City",
                Signs + "Sign_Stop_A_City", Signs + "Sign_Stop_B_City",
                Signs + "Sign_Yield_A_City", Signs + "Sign_Stop_Ahead_A_City",
                Signs + "Sign_Speed_25_A_City", Signs + "Sign_Destination_Medium_City",
                Signs + "Sign_Base_City",
            })
                One(n + ".prefab", System.IO.Path.GetFileNameWithoutExtension(n));

            // THE SIGNAL HEADS. CitySignals places Traffic_Light_A_City and then glues a
            // primitive sphere on it for the state, because that one prefab's lenses are baked
            // into the atlas. There are FIFTEEN prefabs in the folder and only that one has ever
            // been looked at - four of them are called Light_*, which does not sound like a post.
            Debug.Log("=== TRAFFIC LIGHTS (fifteen of them; one has ever been used) ===");
            const string Sig = "Assets/polyperfect/Poly Universal Pack/Prefabs/City/TrafficLights City/";
            foreach (var n in new[]
            {
                "Traffic_Light_A_City", "Traffic_Light_B_City", "Traffic_Light_C_City",
                "Traffic_Light_Middle_A_City",
                "Traffic_Light_Simple_A_City", "Traffic_Light_Simple_B_City",
                "Traffic_Light_Simple_C_City", "Traffic_Light_Simple_D_City",
                "Light_A_City", "Light_B_City", "Light_C_City", "Light_Pedestrian_City",
                "Lamp_Light_A_City",
            })
                One(Sig + n + ".prefab", n);

            // WHAT A DOWNTOWN BLOCK IS MADE OF. The terraces are stacked from Bottom + Entrance
            // + Mid + Roof and every tower is one of three WHOLE prefabs, so Rossville has
            // exactly three building heights. Base/Floor/Roof stack to any height, and the seven
            // Market pieces are shopfront ground floors - all of it unused. Before any of it can
            // be stacked, the pieces have to line up: a Market that is not the same width and
            // depth as a Bottom would come out as a ragged terrace.
            Debug.Log("=== BUILDINGS MODULAR (the downtown kit, mostly unused) ===");
            const string Mod = "Assets/polyperfect/Poly Universal Pack/Prefabs/City/Buildings Modular City/";
            const string Bld = "Assets/polyperfect/Poly Universal Pack/Prefabs/City/Buildings City/";
            foreach (var n in new[]
            {
                Mod + "Squarehouse_Bottom_A_AS_City",
                Mod + "Squarehouse_Bottom_A_F_City", Mod + "Squarehouse_Bottom_A_FB_City",
                Mod + "Squarehouse_Floor_A_Entrance_AS_City", Mod + "Squarehouse_Floor_A_Mid_AS_City",
                Mod + "Squarehouse_Roof_A_City",
                Mod + "Squarehouse_Market_A_City", Mod + "Squarehouse_Market_B_City",
                Mod + "Squarehouse_Market_G_City", Mod + "Squarehouse_Garage_City",
                Mod + "Bayhouse_Bottom_A_AS_City", Mod + "Bayhouse_Roof_A_City",
                Bld + "Skyscraper_A_Base_City", Bld + "Skyscraper_A_Floor_City",
                Bld + "Skyscraper_A_Roof_City", Bld + "Skyscraper_A_City",
                Bld + "Skyscraper_B_Base_City", Bld + "Skyscraper_B_Floor_City",
                Bld + "Skyscraper_B_Roof_City",
                Bld + "Skyscraper_C_Base_City", Bld + "Skyscraper_C_Floor_City",
                Bld + "Skyscraper_C_Roof_City",
            })
                One(n + ".prefab", System.IO.Path.GetFileNameWithoutExtension(n));

            Debug.Log("=== ROADS FARM (the dirt track kit) ===");
            const string Farm = "Assets/polyperfect/Poly Universal Pack/Prefabs/Farm/Roads Farm/";
            foreach (var n in new[]
            {
                "Road_Dirt_A_Straight_10x10m", "Road_Dirt_B_Straight_10x10m",
                "Road_Dirt_A_Turn_20x20m", "Road_Dirt_B_Turn_20x20m",
            })
                One(Farm + n + ".prefab", n);

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void One(string path, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.Log($"[road] MISSING {name}"); return; }

            var lines = new List<string>();
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mesh == null || mr == null) continue;

                // Where this piece sits relative to the prefab root, so multi-part prefabs read
                // in one coordinate system rather than each in its own.
                var off = mf.transform.position - prefab.transform.position;

                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var b = mesh.GetSubMesh(s).bounds;
                    string mat = s < mr.sharedMaterials.Length && mr.sharedMaterials[s] != null
                        ? mr.sharedMaterials[s].name : "?";
                    lines.Add($"    {mat,-24} "
                            + $"x {b.min.x + off.x,7:0.##}..{b.max.x + off.x,-7:0.##} "
                            + $"y {b.min.y + off.y,6:0.##}..{b.max.y + off.y,-6:0.##} "
                            + $"z {b.min.z + off.z,7:0.##}..{b.max.z + off.z,-7:0.##}");
                }
            }

            Debug.Log($"[road] {name}\n{string.Join("\n", lines)}");
        }
    }
}
