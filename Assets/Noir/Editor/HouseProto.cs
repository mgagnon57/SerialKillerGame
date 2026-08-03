using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Noir.Core.Contracts;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// The four Illinois house types, built and photographed, next to what the pack already owns.
    ///
    /// WHY A PROTOTYPE RATHER THAN A DECISION. The research settled WHAT the houses have to be -
    /// frame, three date-layers, nothing brick, see docs/research/ROSSVILLE-HISTORY.md §7a - but
    /// not how to make them. There are three live options and no way to choose between them by
    /// argument:
    ///
    ///   1. PROCEDURAL, from the massing grammars, which is what the rest of this village is
    ///   2. THE PACK'S FARMHOUSE, House_Farm_Scandinavian, which is per-storey modular so a
    ///      floor can be dropped to make it 1.5 storey
    ///   3. ASSEMBLED from Modular Parts, whose horizontal clapboard walls carry a real wood
    ///      siding texture rather than the universal atlas
    ///
    /// So this builds one of each and photographs them the same way, and the choice gets made by
    /// looking. Same reasoning as Elevations, which this borrows its lighting and framing from:
    /// flat even light from over the camera's shoulder, because the question is whether the
    /// OUTLINE carries and a raking sun would do the outline's job for it.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.HouseProto.Render
    /// </summary>
    public static class HouseProto
    {
        private static string OutputDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "elevations", "houses"));

        /// <summary>
        /// The four, with a footprint each. Sizes are the real thing: a quarter-acre lot is 26.3m
        /// of frontage by 51.1m deep, and the house standing on it is nothing like that big.
        /// A foursquare is nearly square by definition; a ranch is long and shallow.
        /// </summary>
        private static readonly (string Grammar, int W, int H, string Note)[] Types =
        {
            ("farmhouse",  9, 11, "1.5 storey, steep gable, part-width porch"),
            ("foursquare", 10, 10, "2 storey, low hip, full-width porch"),
            ("bungalow",   11,  8, "low, gable to street, deep porch"),
            ("ranch",      14,  7, "1 storey, long, shallow hip, no porch"),
        };

        [MenuItem("Noir/Prototype The Houses")]
        public static void Render()
        {
            GameObject root = null, camGo = null, sunGo = null;
            var pipeline = QualitySettings.renderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            float wasShadow = pipeline != null ? pipeline.shadowDistance : 0f;
            bool wasShowBuildings = VillageHost.ShowBuildings;

            try
            {
                Directory.CreateDirectory(OutputDir);

                // Or Materials3D.Plan dims the ground to a tenth and the house stands on tar.
                // Same reason GroundShot forces it: batch mode hard-disables ShowGroundColour, so
                // without this every prototype is photographed at night.
                VillageHost.ShowBuildings = true;

                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));

                if (pipeline != null) pipeline.shadowDistance = 200f;

                root = new GameObject("HouseProto");

                sunGo = new GameObject("ProtoSun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.35f;
                sun.intensity = 1.1f;
                sunGo.transform.rotation = Quaternion.Euler(38f, 150f, 0f);

                RenderSettings.skybox = null;
                RenderSettings.sun = sun;
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.42f, 0.45f, 0.50f);

                camGo = new GameObject("ProtoCamera");
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 35f;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 1000f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.62f, 0.67f, 0.74f);

                int written = 0;
                foreach (var (grammar, w, h, note) in Types)
                {
                    var go = BuildOne(root.transform, grammar, w, h);
                    if (go == null) continue;

                    // Frame it the way Elevations does, so these can be compared with the
                    // village's own elevation set without allowing for a different camera.
                    float top = 8f;
                    float span = Mathf.Max(Mathf.Max(w, h), top * 2.2f);
                    float distance = span * 1.9f + 8f;

                    var target = new Vector3(w * 0.5f, top * 0.45f, -(h * 0.5f));
                    var eye = target + new Vector3(distance * 0.42f, distance * 0.34f, distance * 0.84f);
                    camGo.transform.position = eye;
                    camGo.transform.LookAt(target);

                    Capture(cam, Path.Combine(OutputDir, "proc-" + grammar + ".png"));
                    Debug.Log($"[houseproto] {grammar}: {note}");
                    written++;

                    Object.DestroyImmediate(go);
                }

                // And the pack's own farmhouse, seated on the same spot and shot the same way,
                // so the comparison is like for like rather than one render against a memory.
                const string farmhouse =
                    "Assets/polyperfect/Poly Universal Pack/Prefabs/Farm/Buildings Farm/House_Farm_Scandinavian.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(farmhouse);
                if (prefab == null)
                {
                    Debug.LogWarning("[houseproto] the pack farmhouse is not at " + farmhouse
                                   + " - only the procedural set was rendered.");
                }
                else
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    inst.transform.SetParent(root.transform, false);
                    inst.transform.position = new Vector3(5f, 0f, -5.5f);

                    float distance = 11f * 1.9f + 8f;
                    var target = new Vector3(5f, 3.6f, -5.5f);
                    camGo.transform.position = target + new Vector3(distance * 0.42f, distance * 0.34f, distance * 0.84f);
                    camGo.transform.LookAt(target);

                    Capture(cam, Path.Combine(OutputDir, "pack-farmhouse.png"));
                    Debug.Log("[houseproto] pack: House_Farm_Scandinavian, as shipped");
                    written++;
                }

                Debug.Log($"[houseproto] {written} houses rendered to {OutputDir}");
            }
            finally
            {
                if (pipeline != null) pipeline.shadowDistance = wasShadow;
                VillageHost.ShowBuildings = wasShowBuildings;
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (sunGo != null) Object.DestroyImmediate(sunGo);
                if (root != null) Object.DestroyImmediate(root);
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// One house, built through the real massing grammar on a one-building world.
        ///
        /// It goes through VillageMesh rather than calling the grammar directly, because the
        /// question is what the PIPELINE produces - walls, roof, porch, chimney, materials - and
        /// a grammar rendered on its own would answer a question nobody asked.
        /// </summary>
        private static GameObject BuildOne(Transform parent, string grammar, int w, int h)
        {
            // MASSING IS A KINDS COLUMN, NOT A PER-PLACE LINE, so the grammar under test is
            // selected by giving the prototype its own one-row kinds table rather than by
            // annotating the place. That is exactly the open-kind property Stage 4 bought: a kind
            // the enum has never heard of gets the next value along, so this costs no C# at all.
            // APPENDED TO THE REAL TABLE, not substituted for it. The table validates that every
            // kind PlaceKind names has a row - without one a kind falls through every generator
            // in the world - so a one-row table is correctly refused. Adding a row to the real
            // thing is what content does anyway.
            // COPIED FROM THE REAL `dwelling` ROW, with only the name and the massing changed.
            // Hand-writing the row failed twice - the table demands every column and says so
            // ("A row is complete or it is not a row") - and a hand-written copy would go stale
            // the next time the schema grows a field. Deriving it cannot.
            string table = ContentLoader.Read("kinds.txt");
            var row = new System.Text.StringBuilder();
            bool inDwelling = false;
            foreach (var raw in table.Replace("\r\n", "\n").Split('\n'))
            {
                if (raw.TrimStart().StartsWith("kind "))
                {
                    if (inDwelling) break;
                    inDwelling = raw.Trim() == "kind dwelling";
                    if (inDwelling) row.Append("kind ").Append(grammar).Append('\n');
                    continue;
                }
                if (!inDwelling) continue;
                if (raw.Trim().Length == 0) break;

                string trimmed = raw.TrimStart();
                if (trimmed.StartsWith("words ")) row.Append("  words     ").Append(grammar).Append('\n');
                else if (trimmed.StartsWith("massing ")) row.Append("  massing   ").Append(grammar).Append('\n');
                else row.Append(raw).Append('\n');
            }

            string kinds = table + "\n" + row;

            // A STREET IN FRONT, because a building with nothing to face has no door - and the
            // porch needs the door: it is what tells FrameHouse.Porch which side is the front.
            // Placed two metres clear of the footprint, on the south side, so every prototype
            // faces the same way and the four can be compared without allowing for orientation.
            int road = 5 + h + 3;
            string map =
                "village Proto\n" +
                $"size {w + 20} {h + 24}\n" +
                $"terrain grass 0,0 {w + 20}x{h + 24}\n" +
                $"road front 10 0,{road} {w + 19},{road}\n" +
                 "  class street\n" +
                $"place {grammar} 5,5 {w}x{h} \"Proto\"\n" +
                $"  door {5 + w / 2},{5 + h}\n";   // south wall, facing the street

            try
            {
                PlaceKindTable.Install(PlaceKindTable.Parse(kinds));
                var layout = VillageParser.Parse(map);
                var world = WorldBuilder.Build(layout, VillageHost.Seed);
                var go = VillageMesh.Build(world, parent);
                return go;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[houseproto] '{grammar}' failed to build: {e.Message}");
                return null;
            }
        }

        private static void Capture(Camera cam, string path)
        {
            var rt = new RenderTexture(1280, 960, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var was = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();

            var wasActive = RenderTexture.active;
            RenderTexture.active = rt;
            var shot = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            shot.Apply();
            RenderTexture.active = wasActive;

            cam.targetTexture = was;
            File.WriteAllBytes(path, shot.EncodeToPNG());

            Object.DestroyImmediate(shot);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
