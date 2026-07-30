using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Assembles one cottage out of Poly Universal Pack modular parts and photographs it beside
    /// a real Ashcombe cottage under the same sun, sky and fog.
    ///
    /// The question this answers is not "does the pack import" - it does - but "does a house
    /// built from it belong in this village". Both frames use SunRig's own curve and the same
    /// camera geometry, so the only thing differing between them is the building.
    ///
    /// Deliberately the plainest pieces in the kit: Walls Plain and Roof Regular, not the
    /// painted farmhouse siding the publisher's demo scenes show off. Nothing is retinted yet -
    /// this is the pack's own colour, honestly reported, so it can be judged against Ashcombe's
    /// muted palette before anybody starts overriding materials.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.PolyPackCottage.Render -logFile <log>
    /// </summary>
    public static class PolyPackCottage
    {
        private const string Parts = "Assets/polyperfect/Poly Universal Pack/Prefabs/Modular Parts/";
        private const string Plain = Parts + "Walls/3m/Walls Plain/";
        private const string Slope = Parts + "Roofs/Roof Regular/";
        private const string Doors = Parts + "Doors/Doors Fantasy/";
        private const string Windows = Parts + "Windows/Windows Fantasy/";

        /// <summary>Mid-height of a wall piece's window hole, which tops out at 2.2m.</summary>
        private const float WindowMid = 1.55f;

        private const int Width = 1600, Height = 900;

        // Footprint 6m x 4m, 3m to the eaves, 45-degree gable ridge along x at 5m.
        private const float W = 6f, D = 4f, WallTop = 3f;

        // The roof tiles carry a 0.13m lip below their pivot, so seating them at eave height
        // needs that taken back off or every roof floats a hand's breadth above its walls.
        private const float Lip = 0.13f;

        private static string OutputDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "snapshots"));

        [MenuItem("Noir/Render Poly Pack Cottage")]
        public static void Render()
        {
            GameObject root = null, cottage = null, camGo = null, sunGo = null;
            Volume fx = null;
            Material sky = null;
            int written = 0;

            var wasSky = RenderSettings.skybox;
            var wasSun = RenderSettings.sun;
            bool wasFog = RenderSettings.fog;
            var wasFogMode = RenderSettings.fogMode;
            var wasFogColour = RenderSettings.fogColor;
            float wasFogDensity = RenderSettings.fogDensity;
            var wasAmbientMode = RenderSettings.ambientMode;
            var wasAmbient = RenderSettings.ambientLight;

            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float wasShadowDistance = pipeline != null ? pipeline.shadowDistance : 0f;
            int wasCascades = pipeline != null ? pipeline.shadowCascadeCount : 0;

            try
            {
                Directory.CreateDirectory(OutputDir);

                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
                var layout = VillageParser.Parse(ContentLoader.Read("village.txt"));
                var world = WorldBuilder.Build(layout, VillageHost.Seed);

                root = new GameObject("CompareVillage");
                VillageMesh.Build(world, root.transform);

                if (pipeline != null) { pipeline.shadowDistance = 320f; pipeline.shadowCascadeCount = 4; }

                sunGo = new GameObject("CompareSun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.7f;

                camGo = new GameObject("CompareCamera");
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 45f;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 3000f;
                cam.clearFlags = CameraClearFlags.Skybox;

                fx = PostFx.Create(null);
                PostFx.EnableOn(cam);

                sky = Snapshot.MakeSky();
                RenderSettings.skybox = sky;
                RenderSettings.sun = sun;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.ambientMode = AmbientMode.Flat;

                const float hour = 13f;
                var (colour, intensity, ambient) = SunRig.SkyAt(hour);
                sun.color = colour;
                sun.intensity = intensity;
                sun.enabled = intensity > 0.01f;
                sunGo.transform.rotation = SunRig.SunRotation(hour);
                RenderSettings.ambientLight = ambient;
                RenderSettings.fogColor = SunRig.FogAt(colour, intensity, ambient);
                RenderSettings.fogDensity = Mathf.Lerp(0.0022f, 0.0010f, Mathf.Clamp01(intensity));
                if (sky != null && sky.HasProperty("_Exposure"))
                    sky.SetFloat("_Exposure", Mathf.Lerp(0.18f, 1.15f, Mathf.Clamp01(intensity)));

                // ---- shot A: a real Ashcombe cottage ----
                var subject = FindCottage(world);
                var at = new Vector3(subject.Centre.X, 0f, -subject.Centre.Y);
                Debug.Log($"[cottage] Ashcombe subject {subject}");
                Frame(camGo, at, 26f, 30f);
                Capture(cam, Path.Combine(OutputDir, "compare-ashcombe.png"));
                written++;

                // ---- shot B: the same view of a pack cottage on open ground ----
                // Out past the village edge rather than on the green: the green has trees on it
                // that sit between the camera and anything standing there.
                var pad = new Vector3(world.Width + 40f, 0f, -world.Height / 2f);
                cottage = Assemble(pad - new Vector3(W / 2f, 0f, D / 2f));
                Report(cottage);
                Frame(camGo, pad, 26f, 30f);
                Capture(cam, Path.Combine(OutputDir, "compare-polypack.png"));
                written++;

                // Close enough to read the walls. At two dozen metres it is not possible to tell
                // a plaster panel from a hole where one should be.
                Frame(camGo, pad + Vector3.up * 1.5f, 13f, 10f);
                Capture(cam, Path.Combine(OutputDir, "compare-polypack-close.png"));

                // Straight on to the front, where the door and windows are.
                Frame(camGo, pad + Vector3.up * 1.5f, 15f, 6f, yaw: 0f);
                Capture(cam, Path.Combine(OutputDir, "compare-polypack-front.png"));
            }
            catch (Exception ex)
            {
                Debug.LogError("[cottage] FAILED: " + ex);
            }
            finally
            {
                if (cottage != null) UnityEngine.Object.DestroyImmediate(cottage);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
                if (sunGo != null) UnityEngine.Object.DestroyImmediate(sunGo);
                if (fx != null)
                {
                    UnityEngine.Object.DestroyImmediate(fx.sharedProfile);
                    UnityEngine.Object.DestroyImmediate(fx.gameObject);
                }
                if (sky != null) UnityEngine.Object.DestroyImmediate(sky);

                RenderSettings.skybox = wasSky;
                RenderSettings.sun = wasSun;
                RenderSettings.fog = wasFog;
                RenderSettings.fogMode = wasFogMode;
                RenderSettings.fogColor = wasFogColour;
                RenderSettings.fogDensity = wasFogDensity;
                RenderSettings.ambientMode = wasAmbientMode;
                RenderSettings.ambientLight = wasAmbient;

                if (pipeline != null)
                {
                    pipeline.shadowDistance = wasShadowDistance;
                    pipeline.shadowCascadeCount = wasCascades;
                }
            }

            Debug.Log($"[cottage] wrote {written} of 2");
            if (Application.isBatchMode) EditorApplication.Exit(written == 2 ? 0 : 1);
        }

        /// <summary>Same camera geometry for both frames, so only the building differs.</summary>
        private static void Frame(GameObject camGo, Vector3 target, float dist, float pitch,
                                  float yaw = 35f)
        {
            // Well back and pitched up: at seventeen metres and twenty degrees the camera stood
            // inside the neighbouring cottage and photographed its back wall.
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            camGo.transform.position = target + Vector3.up * 2f - rotation * Vector3.forward * dist;
            camGo.transform.rotation = rotation;
        }

        private static readonly Dictionary<string, Material> _flats = new Dictionary<string, Material>();

        /// <summary>An opaque, untextured URP Lit material in one of Ashcombe's colours.</summary>
        private static Material Flat(string name, Color colour)
        {
            if (_flats.TryGetValue(name, out var existing) && existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { name = name };
            m.enableInstancing = true;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
            if (m.HasProperty("_Color")) m.SetColor("_Color", colour);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.05f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            _flats[name] = m;
            return m;
        }

        /// <summary>
        /// What actually got built and what it is painted with. A cottage that comes out one flat
        /// colour is either the wrong material or the wrong UVs, and the render alone cannot say
        /// which.
        /// </summary>
        private static void Report(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { Debug.LogWarning("[cottage] assembled nothing"); return; }

            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            Debug.Log($"[cottage] {rends.Length} pieces, bounds min({b.min.x:0.##},{b.min.y:0.##},{b.min.z:0.##}) "
                    + $"size({b.size.x:0.##},{b.size.y:0.##},{b.size.z:0.##})");

            var seen = new HashSet<string>();
            foreach (var r in rends)
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) { Debug.LogWarning($"[cottage] {r.name}: NULL material"); continue; }
                    string key = m.name;
                    if (!seen.Add(key)) continue;
                    var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                    Debug.Log($"[cottage]   mat {m.name} shader={m.shader.name} "
                            + $"baseMap={(tex ? tex.name : "NONE")} "
                            + $"colour={(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : "-")}");
                }
        }

        private static TileRect FindCottage(WorldModel world)
        {
            foreach (var place in world.AllPlaces)
                if (place.Kind == PlaceKind.Dwelling && place.Bounds.W <= 8 && place.Bounds.H <= 8)
                    return place.Bounds;
            throw new Exception("no small dwelling found");
        }

        /// <summary>
        /// Somewhere with nothing built on it. The pack cottage has to stand on bare ground or
        /// it grows out of somebody's roof, and the green is the only reliably empty acre.
        /// </summary>
        private static TileRect FindOpenGround(WorldModel world)
        {
            foreach (var place in world.AllPlaces)
                if (place.Kind == PlaceKind.Green) return place.Bounds;
            foreach (var place in world.AllPlaces)
                if (place.Kind == PlaceKind.Allotments) return place.Bounds;
            throw new Exception("no open ground found");
        }

        // ---- assembly ----

        /// <summary>
        /// One cottage, origin at its south-west ground corner. Walls pivot on their right edge
        /// at ground level with the 0.1m thickness running to +z, so each face is laid out by
        /// walking segments along it and placing each at the edge it ends on.
        /// </summary>
        private static GameObject Assemble(Vector3 origin)
        {
            var go = new GameObject("PolyPackCottage");
            go.transform.position = origin;

            // The pack's own atlas materials sample to a flat blue here, so every piece is
            // repainted in Ashcombe's palette as it goes in. That is where this was always
            // heading - the village is deliberately muted and the kit's stock colours are not -
            // so the honest comparison is pack GEOMETRY under village colour, not the kit's
            // marketing paint.
            //
            // Flat colour rather than Materials3D.Wall or .Roofs, which carry a surface texture
            // tiled against Ashcombe's own world-space UVs. The kit's meshes are atlas-mapped -
            // every vertex of a panel lands on one palette texel - and under those materials a
            // wall came out invisible. Materials3D.Stone is the one untextured member of the set
            // and it was the one piece that rendered correctly, which is the whole argument.
            var wall = Flat("PackWall", new Color32(0xC6, 0xB8, 0xA6, 0xFF));
            var roof = Flat("PackRoofSlate", new Color32(0x6B, 0x70, 0x79, 0xFF));
            var stone = Materials3D.Stone;

            // An _Ext piece is a single-sided outer skin - that is what the matching _Int pieces
            // are for - so its one visible face points along +z at rest, NOT along -z as the
            // 0.1m thickness running that way suggests. Faced the other way the cottage was
            // built inside out: every wall's only face pointed into the room, and from the road
            // you looked straight through the house.
            //
            // South face, z=0, looking -z: window, door, plain.
            Put(go, Plain + "Wall_Window_2x3m_Ext.prefab",        new Vector3(0f, 0f, 0f), 180f, wall);
            Put(go, Plain + "Wall_Door_Regular_2x3m_Ext.prefab",  new Vector3(2f, 0f, 0f), 180f, wall);
            Put(go, Plain + "Wall_2x3m_Ext.prefab",               new Vector3(4f, 0f, 0f), 180f, wall);

            // North face, z=D, looking +z.
            Put(go, Plain + "Wall_1x3m_Ext.prefab",               new Vector3(1f, 0f, D), 0f, wall);
            Put(go, Plain + "Wall_Window_2x3m_Ext.prefab",        new Vector3(3f, 0f, D), 0f, wall);
            Put(go, Plain + "Wall_3x3m_Ext.prefab",               new Vector3(6f, 0f, D), 0f, wall);

            // West face, x=0, looking -x.
            Put(go, Plain + "Wall_4x3m_Ext.prefab",               new Vector3(0f, 0f, D), 270f, wall);

            // East face, x=W, looking +x.
            Put(go, Plain + "Wall_1x3m_Ext.prefab",               new Vector3(W, 0f, 0f), 90f, wall);
            Put(go, Plain + "Wall_Window_2x3m_Ext.prefab",        new Vector3(W, 0f, 1f), 90f, wall);
            Put(go, Plain + "Wall_1x3m_Ext.prefab",               new Vector3(W, 0f, 3f), 90f, wall);

            // Doors and windows are separate inserts - the wall pieces only supply the hole.
            // Their pivots are not consistent with each other (a door is centred on its own
            // opening, a window is not), so each is seated by measuring the piece and moving its
            // centre onto the hole rather than by a per-prefab magic number.
            var timber = Flat("PackTimber", new Color32(0x6E, 0x5A, 0x46, 0xFF));
            var glass = Materials3D.WindowGlass;

            Fit(go, Doors + "Door_Single_A_Regular_Fantasy.prefab",
                new Vector3(3f, 0f, -0.05f), 180f, timber, sitOnGround: true);

            Fit(go, Windows + "Window_Wood_Small_A_Fantasy.prefab",
                new Vector3(1f, WindowMid, -0.05f), 180f, glass, sitOnGround: false);
            Fit(go, Windows + "Window_Wood_Small_A_Fantasy.prefab",
                new Vector3(2f, WindowMid, D + 0.05f), 0f, glass, sitOnGround: false);
            Fit(go, Windows + "Window_Wood_Small_A_Fantasy.prefab",
                new Vector3(W + 0.05f, WindowMid, 2f), 90f, glass, sitOnGround: false);

            // Gable triangles fill wall-top to ridge on the two ends the ridge runs into.
            Gable(go, x: 0f,  facing: 270f, paint: wall);
            Gable(go, x: W,   facing: 90f,  paint: wall);

            // Roof. Tiles are high at their -z edge and fall to +z, so the north pitch sits at
            // its natural orientation and the south pitch is the same tile turned about.
            for (float x = 2f; x <= W; x += 2f)
                Put(go, Slope + "Roof_Regular_2x2m.prefab", new Vector3(x, WallTop - Lip, D), 0f, roof);
            for (float x = 0f; x < W; x += 2f)
                Put(go, Slope + "Roof_Regular_2x2m.prefab", new Vector3(x, WallTop - Lip, 0f), 180f, roof);

            Put(go, Parts + "Chimneys/Chimney_3m_A_Fantasy.prefab",
                new Vector3(1.4f, WallTop, D / 2f), 0f, stone);

            return go;
        }

        /// <summary>
        /// A gable end: two right triangles meeting at the ridge. The kit only ships the half
        /// that rises toward its pivot, and a Y rotation cannot turn a right triangle into a
        /// left one - so the far half is the same piece reflected through the ridge plane by a
        /// parent with a negative axis.
        /// </summary>
        private static void Gable(GameObject parent, float x, float facing, Material paint)
        {
            const string Half = Plain + "Wall_Roof_Corner_Down_2x2m_Ext.prefab";

            // Seated at the ridge, not at the eave. Both quarter turns put the piece's high
            // corner on the ridge side, so the half is placed at D/2 and the other half is that
            // same placement reflected back through the ridge - at 0 the pair sat a full 2m off
            // each end of the building and left the roof standing on nothing.
            Put(parent, Half, new Vector3(x, WallTop, D / 2f), facing, paint);

            var mirror = new GameObject("GableMirror");
            mirror.transform.SetParent(parent.transform, false);
            mirror.transform.localPosition = new Vector3(0f, 0f, D);
            mirror.transform.localScale = new Vector3(1f, 1f, -1f);
            Put(mirror, Half, new Vector3(x, WallTop, D / 2f), facing, paint);
        }

        /// <summary>
        /// Seat an insert so its own centre lands on <paramref name="anchor"/>, measured rather
        /// than assumed. The kit's door pivots sit at the middle of the opening and its window
        /// pivots do not, so placing either by its pivot puts one of them through the wall.
        /// </summary>
        private static void Fit(GameObject parent, string path, Vector3 anchor, float yaw,
                                Material paint, bool sitOnGround)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[cottage] missing " + path); return; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent.transform, false);
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localPosition = Vector3.zero;
            Paint(go, paint);

            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;

            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            // Where the piece's own centre currently sits relative to its pivot, in world terms.
            var drift = b.center - go.transform.position;
            var target = parent.transform.TransformPoint(anchor);

            // A door stands on the floor; a window is centred on its hole.
            float y = sitOnGround
                ? target.y + (go.transform.position.y - b.min.y)
                : target.y - drift.y;

            go.transform.position = new Vector3(target.x - drift.x, y, target.z - drift.z);
        }

        private static void Paint(GameObject go, Material paint)
        {
            if (paint == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var slots = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < slots.Length; i++) slots[i] = paint;
                r.sharedMaterials = slots;
            }
        }

        private static void Put(GameObject parent, string path, Vector3 localPos, float yaw,
                                Material paint)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[cottage] missing " + path); return; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            if (paint == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var slots = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < slots.Length; i++) slots[i] = paint;
                r.sharedMaterials = slots;
            }
        }

        private static void Capture(Camera cam, string path)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            var previousTarget = cam.targetTexture;
            var previousActive = RenderTexture.active;

            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            shot.Apply();
            File.WriteAllBytes(path, shot.EncodeToPNG());

            cam.targetTexture = previousTarget;
            RenderTexture.active = previousActive;

            UnityEngine.Object.DestroyImmediate(shot);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            Debug.Log("[cottage] wrote " + path);
        }
    }
}
