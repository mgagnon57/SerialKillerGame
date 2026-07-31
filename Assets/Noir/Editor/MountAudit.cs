using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// Finds prefabs that are COMPONENTS rather than complete objects, in the folders the city
    /// places things from at ground level.
    ///
    /// This has now cost real time twice, in the same folder each way up:
    ///
    ///   Sign_Stop_A_City is the sign PLATE, 3cm thick and pivoted at its own centre. Dropped on
    ///   the pavement it is a metal disc sunk to its equator. Half the road signs in Northgate
    ///   were like that, because the catalogue took everything beginning with "Sign_" and the
    ///   folder is two kits - plates and mounted signs - with nothing in the name to say which.
    ///
    ///   Traffic_Light_A_City is the signal HEAD, one metre tall. It was being stood on the kerb
    ///   with a primitive sphere glued to the top to show the state, for months, while
    ///   Light_A_City - six metres of mast with an arm out over the road and three drivable
    ///   lenses - sat unopened in the same folder.
    ///
    /// Neither of those is a bug in the pack. Both are a bug in taking a prefab whose name reads
    /// like a whole object and putting it on the floor without measuring it. A thing meant to be
    /// mounted DOES NOT REACH THE GROUND, and that is checkable.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.MountAudit.Run -logFile &lt;log&gt;
    /// </summary>
    public static class MountAudit
    {
        private const string Pack = "Assets/polyperfect/Poly Universal Pack/Prefabs/";

        /// <summary>
        /// Above this and a prefab does not reach the ground, so standing it on the ground
        /// leaves it hanging in the air. Generous: a few centimetres of clearance is modelling,
        /// a quarter of a metre is a mounting point.
        /// </summary>
        private const float Floating = 0.25f;

        /// <summary>Below this and it is mostly UNDER the ground - inverted, or pivoted midway.</summary>
        private const float Sunk = -0.2f;

        /// <summary>
        /// The folders the city stands things up in. Not the whole pack: a roof prop, a wall
        /// lamp and a first-floor module are all supposed to be off the ground, and reporting
        /// them would bury the two answers that matter in four hundred that do not.
        /// </summary>
        private static readonly string[] Ground =
        {
            "City/Props City", "City/Lamps City", "City/Park City", "City/Signs City",
            "City/TrafficLights City", "City/Poles City", "City/Playground City",
            "City/SkatePark City", "City/Beach City", "City/Buildings City",
            "Farm/Buildings Farm", "Farm/Vehicles Farm", "Farm/Crops Farm",
            "Cars/Cars City", "Cars/Cars Trucks",
            "Nature/Trees", "Nature/Trees City", "Nature/Bushes", "Nature/Rocks",
            "Nature/Flowers", "Nature/Grass", "Nature/Hedges",
            "Modular Parts/Fences", "Survival",
        };

        /// <summary>
        /// Folders whose contents are MEANT to hang. Skipped wholesale rather than filtered by
        /// name, because "Roof Props" is an honest statement about every prefab in it.
        /// </summary>
        private static readonly string[] Hangs =
        {
            "Roof Props", "Neon Props", "Pipes Props", "Wall", "Window", "Ceiling",
        };

        [MenuItem("Noir/Audit Prefab Mounting")]
        public static void Run()
        {
            var floating = new List<string>();
            var sunk = new List<string>();
            int looked = 0;

            foreach (var folder in Ground)
            {
                string path = Pack + folder;
                if (!AssetDatabase.IsValidFolder(path))
                { Debug.LogWarning("[mount] no such folder: " + folder); continue; }

                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { path }))
                {
                    string asset = AssetDatabase.GUIDToAssetPath(guid);
                    if (Skip(asset)) continue;

                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                    if (prefab == null) continue;

                    if (!Measure(prefab, out float lo, out float hi)) continue;
                    looked++;

                    string name = System.IO.Path.GetFileNameWithoutExtension(asset);

                    if (lo > Floating)
                        floating.Add($"{folder}/{name} sits at y {lo:0.00}..{hi:0.00} - "
                                   + $"it needs {lo:0.0}m of something underneath it");
                    else if (hi < 0.05f && lo < Sunk)
                        sunk.Add($"{folder}/{name} runs y {lo:0.00}..{hi:0.00} - it is "
                               + "entirely below the ground it would be stood on");
                }
            }

            Debug.Log($"[mount] looked at {looked} prefabs in {Ground.Length} folders.");
            int faults = Say("prefabs that do not reach the ground", floating)
                       + Say("prefabs that are entirely under the ground", sunk);

            Debug.Log(faults == 0
                ? "[mount] VERDICT: nothing found."
                : $"[mount] VERDICT: {faults} kinds of fault, listed above. Each of these is a "
                + "COMPONENT: mount it, or find the variant that is the whole thing.");

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static bool Skip(string asset)
        {
            if (asset.IndexOf("Collider", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var h in Hangs)
                if (asset.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>
        /// The prefab's vertical extent about its own pivot, off the mesh rather than off a
        /// Renderer - an unspawned prefab's renderer bounds are not meaningful.
        /// </summary>
        private static bool Measure(GameObject prefab, out float lo, out float hi)
        {
            lo = float.MaxValue;
            hi = float.MinValue;

            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;

                // Where this piece sits relative to the prefab root, so a multi-part prefab
                // reads in one coordinate system rather than each part in its own.
                float off = mf.transform.position.y - prefab.transform.position.y;
                var b = mesh.bounds;

                lo = Math.Min(lo, b.min.y + off);
                hi = Math.Max(hi, b.max.y + off);
            }
            return lo < float.MaxValue;
        }

        private static int Say(string what, List<string> found)
        {
            if (found.Count == 0) { Debug.Log($"[mount] ok - no {what}."); return 0; }

            Debug.LogError($"[mount] {found.Count} x {what}:");
            foreach (var line in found) Debug.LogError("           " + line);
            return 1;
        }
    }
}
