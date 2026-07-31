using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// One townhouse stack, alone, in daylight, close up.
    ///
    /// Everything about the storey alignment has been argued from numbers and from screenshots of
    /// a whole street, where a building standing behind another looks exactly like a building
    /// whose floors do not line up. This removes the argument: three stacks on empty ground with
    /// nothing behind them and nothing beside them, photographed from the pavement.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.StackProbe.Run -logFile &lt;log&gt;
    /// </summary>
    public static class StackProbe
    {
        private static string Out =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "snapshots"));

        /// <summary>
        /// Every pair of buildings whose footprints genuinely interpenetrate.
        ///
        /// MapAudit's checks are arithmetic on the AUTHORED layout, and a building put up by
        /// CityDistrict or CitySuburb is not in the layout - it is worked out at build time from
        /// the block's own bounds. So a renderer that stands one thing inside another is invisible
        /// to every check there is, which is exactly the hole that let overlapping parked cars be
        /// reported twice from screenshots with the audit clean both times.
        ///
        ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.StackProbe.Overlaps
        /// </summary>
        [MenuItem("Noir/Probe Buildings Standing In Each Other")]
        public static void Overlaps()
        {
            GameObject root = null;
            int bad = 0;

            try
            {
                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
                var layout = VillageParser.Parse(ContentLoader.Read(VillageHost.MapFile));
                var world = WorldBuilder.Build(layout, VillageHost.Seed);

                root = new GameObject("BuildingProbe");
                var nodes = new[]
                {
                    CityBuildings.Build(world, root.transform),
                    CityDistrict.Build(world, root.transform),
                    CitySuburb.Build(world, root.transform),
                };

                var boxes = new List<(string name, Rect plan)>();
                foreach (var node in nodes)
                foreach (Transform child in node.transform)
                {
                    var rends = child.GetComponentsInChildren<Renderer>();
                    if (rends.Length == 0) continue;
                    var b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    boxes.Add((child.name, Rect.MinMaxRect(b.min.x, -b.max.z, b.max.x, -b.min.z)));
                }

                var worst = new List<(float area, string line)>();

                for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    // The modules are 6.1m on a 6m pitch, so neighbours in a terrace overlap by
                    // about a tenth of a metre BY DESIGN - that is what makes a terrace read as
                    // one run rather than a row with light through the joints. Only a metre and a
                    // half in BOTH directions is one thing standing inside another.
                    var o = Meet(boxes[i].plan, boxes[j].plan);
                    if (o.width < 1.5f || o.height < 1.5f) continue;

                    bad++;
                    worst.Add((o.width * o.height,
                        $"[overlap]   {boxes[i].name} through {boxes[j].name} - "
                      + $"{o.width:0.00} x {o.height:0.00}m at {o.center.x:0.0},{o.center.y:0.0}"));
                }

                worst.Sort((x, y) => y.area.CompareTo(x.area));
                for (int i = 0; i < Mathf.Min(12, worst.Count); i++) Debug.Log(worst[i].line);

                Debug.Log(bad == 0
                    ? $"[overlap] VERDICT: nothing stands in anything else ({boxes.Count} buildings)."
                    : $"[overlap] VERDICT: {bad} pairs share ground, worst {worst[0].area:0.0} sq m.");
            }
            catch (Exception ex) { Debug.LogError("[overlap] FAILED: " + ex); bad++; }
            finally { if (root != null) UnityEngine.Object.DestroyImmediate(root); }

            if (Application.isBatchMode) EditorApplication.Exit(bad == 0 ? 0 : 1);
        }

        private static Rect Meet(Rect a, Rect b)
        {
            float x0 = Mathf.Max(a.xMin, b.xMin), x1 = Mathf.Min(a.xMax, b.xMax);
            float y0 = Mathf.Max(a.yMin, b.yMin), y1 = Mathf.Min(a.yMax, b.yMax);
            return Rect.MinMaxRect(x0, y0, Mathf.Max(x0, x1), Mathf.Max(y0, y1));
        }

        /// <summary>
        /// A real residential frontage in the real city, close, at noon.
        ///
        /// The isolated stacks come out right, so whatever is wrong is something the city does
        /// that building one on empty ground does not. This photographs an actual district block's
        /// OUTWARD face - the side that gets a residential ground floor rather than a shopfront,
        /// because the shopfronts face in towards town - from the middle of the ring road.
        ///
        ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.StackProbe.Frontage
        /// </summary>
        [MenuItem("Noir/Probe A Real Residential Frontage")]
        public static void Frontage()
        {
            GameObject root = null, camGo = null, sunGo = null;
            try
            {
                Directory.CreateDirectory(Out);

                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
                var layout = VillageParser.Parse(ContentLoader.Read(VillageHost.MapFile));
                var world = WorldBuilder.Build(layout, VillageHost.Seed);

                root = new GameObject("CityProbe");
                CityStreets.Build(world, root.transform);
                CityBuildings.Build(world, root.transform);
                CityDistrict.Build(world, root.transform);

                sunGo = new GameObject("Sun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                sun.intensity = 1.15f;
                sunGo.transform.rotation = Quaternion.Euler(52f, 150f, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.5f, 0.52f, 0.56f);
                RenderSettings.fog = false;

                camGo = new GameObject("Cam");
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 55f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.62f, 0.72f, 0.85f);
                cam.farClipPlane = 800f;

                // "Westway at Northway" is the block at 390,390. Its WEST face looks out at the
                // ring road, so that side is residential. Stand on the ring and look at it.
                camGo.transform.position = new Vector3(370f, 5f, -420f);
                camGo.transform.LookAt(new Vector3(392f, 7f, -420f));
                Shoot(cam, Path.Combine(Out, "probe-city-frontage.png"));

                // And obliquely along the same run, which is where a mass standing proud of the
                // wall separates from it.
                camGo.transform.position = new Vector3(372f, 4f, -452f);
                camGo.transform.LookAt(new Vector3(391f, 6f, -410f));
                Shoot(cam, Path.Combine(Out, "probe-city-oblique.png"));

                Debug.Log("[stack] wrote probe-city-frontage.png and probe-city-oblique.png");

                // HOW FAR DOES ANYTHING STAND OUTSIDE ITS OWN BLOCK? A district lot is the block,
                // and a perimeter building is seated with its FACED front on the block's edge - so
                // nothing should reach past it by more than a cornice. Anything that reaches
                // metres past is standing in the street.
                var blocks = new List<TileRect>();
                foreach (var pl in world.AllPlaces)
                    if (PlaceKindTable.Current.Row(pl.Kind).Name == "district") blocks.Add(pl.Bounds);

                float worst = 0f; string where = "nothing";
                int over = 0;

                var district = GameObject.Find("CityDistrict");
                if (district != null)
                foreach (Transform child in district.transform)
                {
                    var rends = child.GetComponentsInChildren<Renderer>();
                    if (rends.Length == 0) continue;
                    var b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

                    // village coords
                    float x0 = b.min.x, x1 = b.max.x, y0 = -b.max.z, y1 = -b.min.z;

                    foreach (var blk in blocks)
                    {
                        // Only judge it against the block it belongs to.
                        float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
                        if (cx < blk.X || cx > blk.X + blk.W || cy < blk.Y || cy > blk.Y + blk.H) continue;

                        float outBy = Mathf.Max(
                            Mathf.Max(blk.X - x0, x1 - (blk.X + blk.W)),
                            Mathf.Max(blk.Y - y0, y1 - (blk.Y + blk.H)));

                        if (outBy > 1.0f) over++;
                        if (outBy > worst)
                        {
                            worst = outBy;
                            where = $"{child.name} reaches {outBy:0.00}m outside block "
                                  + $"{blk.X},{blk.Y}";
                        }
                        break;
                    }
                }

                Debug.Log($"[outside] {over} buildings stand more than a metre outside their own "
                        + $"block. Worst: {where}");

                // IS THE BRICK ON THE BUILDING LINE, OR THE TAIL? For the west run of one block,
                // measure both. If the faced front sits metres behind the block edge while the
                // raw front sits on it, then Seat is aligning the unfaced tail after all.
                int shown = 0;
                if (district != null)
                foreach (Transform child in district.transform)
                {
                    if (!child.name.StartsWith("Squarehouse_390_")
                        && !child.name.StartsWith("Bayhouse_390_")) continue;
                    if (shown++ >= 4) break;

                    Bounds raw = default, faced = default;
                    bool anyRaw = false, anyFaced = false;

                    foreach (var mf in child.GetComponentsInChildren<MeshFilter>())
                    {
                        var mesh = mf.sharedMesh;
                        var mr = mf.GetComponent<MeshRenderer>();
                        if (mesh == null || mr == null) continue;
                        var mats = mr.sharedMaterials;
                        var verts = mesh.vertices;

                        for (int s = 0; s < mesh.subMeshCount; s++)
                        {
                            var mat = s < mats.Length ? mats[s] : null;
                            bool universal = mat == null ||
                                mat.name.StartsWith("M_Universal_A", StringComparison.Ordinal);

                            foreach (var idx in mesh.GetTriangles(s))
                            {
                                var w = mf.transform.TransformPoint(verts[idx]);
                                if (!anyRaw) { raw = new Bounds(w, Vector3.zero); anyRaw = true; }
                                else raw.Encapsulate(w);
                                if (universal) continue;
                                if (!anyFaced) { faced = new Bounds(w, Vector3.zero); anyFaced = true; }
                                else faced.Encapsulate(w);
                            }
                        }
                    }
                    if (!anyRaw) continue;

                    Debug.Log($"[line] {child.name}: block edge x=390, "
                            + $"raw front x={raw.min.x:0.00}, faced front x={faced.min.x:0.00}, "
                            + $"tail stands {faced.min.x - raw.min.x:0.00}m proud of the brick");
                }
            }
            catch (Exception ex) { Debug.LogError("[stack] FAILED: " + ex); }
            finally
            {
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
                if (sunGo != null) UnityEngine.Object.DestroyImmediate(sunGo);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        [MenuItem("Noir/Probe One Townhouse Stack")]
        public static void Run()
        {
            GameObject root = null, camGo = null, sunGo = null;

            try
            {
                Directory.CreateDirectory(Out);

                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
                var layout = VillageParser.Parse(ContentLoader.Read(VillageHost.MapFile));
                var world = WorldBuilder.Build(layout, VillageHost.Seed);

                Place any = null;
                foreach (var p in world.AllPlaces) { any = p; break; }

                root = new GameObject("StackProbe");

                // A floor to stand them on, so they are not silhouetted against nothing.
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.transform.SetParent(root.transform, false);
                floor.transform.position = new Vector3(30f, 0f, -30f);
                floor.transform.localScale = new Vector3(12f, 1f, 12f);

                // Three, twenty metres apart, all facing the camera (-z is south, yaw 0).
                CityBuildings.Stack(root.transform, any, new TileRect(10, 30, 6, 9), 0f, 1, false, out _);
                CityBuildings.Stack(root.transform, any, new TileRect(30, 30, 6, 9), 0f, 3, true, out _);
                CityBuildings.Stack(root.transform, any, new TileRect(50, 30, 6, 9), 0f, 1, false,
                                    out _, only: "Bayhouse");

                sunGo = new GameObject("Sun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                sun.intensity = 1.1f;
                sunGo.transform.rotation = Quaternion.Euler(38f, 20f, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.52f);
                RenderSettings.fog = false;

                camGo = new GameObject("Cam");
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 50f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.62f, 0.72f, 0.85f);
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 500f;

                // THE FRONTS FACE -z. yaw 0 seats the front on the lot's high-y edge, which is
                // Unity z = -(lot.Y + lot.H) = -39, so the camera has to stand FURTHER out in -z
                // to be looking at the facades. Standing at -8 photographs the backs, which is
                // how the first run of this probe managed to indict the wrong thing.
                camGo.transform.position = new Vector3(33f, 6f, -52f);
                camGo.transform.LookAt(new Vector3(33f, 6f, -39f));
                Shoot(cam, Path.Combine(Out, "probe-stack-front.png"));

                camGo.transform.position = new Vector3(6f, 5f, -50f);
                camGo.transform.LookAt(new Vector3(34f, 5f, -38f));
                Shoot(cam, Path.Combine(Out, "probe-stack-oblique.png"));

                // And the backs, deliberately, because a terrace has gaps in it and a block
                // interior is looked at from above.
                camGo.transform.position = new Vector3(4f, 5f, -12f);
                camGo.transform.LookAt(new Vector3(32f, 5f, -31f));
                Shoot(cam, Path.Combine(Out, "probe-stack-back.png"));

                Debug.Log("[stack] wrote probe-stack-front.png and probe-stack-oblique.png");

                // IS THE TAIL ITS OWN RENDERER? If the unfaced mass behind the brick is a
                // separate object it can simply not be drawn; if it shares a mesh with the brick
                // it cannot, and the fix has to be a different bottom piece.
                foreach (var which in new[] { "Squarehouse_Bottom_A_AS_City",
                                              "Bayhouse_Bottom_A_AS_City" })
                {
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/polyperfect/Poly Universal Pack/Prefabs/City/Buildings Modular City/"
                        + which + ".prefab");
                    if (pf == null) continue;

                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                    inst.transform.position = Vector3.zero;
                    Debug.Log($"[tail] --- {which}");

                    foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
                    {
                        var mats = mr.sharedMaterials;
                        var names = new System.Text.StringBuilder();
                        bool allUniversalA = mats.Length > 0;
                        foreach (var m in mats)
                        {
                            names.Append(m == null ? "null" : m.name).Append(' ');
                            if (m == null || !m.name.StartsWith("M_Universal_A", StringComparison.Ordinal))
                                allUniversalA = false;
                        }
                        var bb = mr.bounds;
                        Debug.Log($"[tail]   {mr.name,-46} z {bb.min.z,6:0.00}..{bb.max.z,-6:0.00} "
                                + $"{(allUniversalA ? "ALL-UNIVERSAL" : "faced")}  [{names}]");
                    }

                    UnityEngine.Object.DestroyImmediate(inst);
                }

                // WHERE EACH SECTION ACTUALLY SITS. A gap between one section's top and the
                // next's bottom is the whole fault, and it is invisible in any number that
                // measures a section on its own.
                foreach (Transform house in root.transform)
                {
                    if (!house.name.Contains("house")) continue;
                    Debug.Log($"[stack] --- {house.name}");

                    float previousTop = float.NaN;
                    foreach (Transform section in house)
                    {
                        var rends = section.GetComponentsInChildren<Renderer>();
                        if (rends.Length == 0) continue;
                        var b = rends[0].bounds;
                        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

                        string gap = float.IsNaN(previousTop) ? ""
                            : $"   gap below: {b.min.y - previousTop:0.00}m";
                        Debug.Log($"[stack]   {section.name,-46} y {b.min.y,6:0.00}..{b.max.y,-6:0.00}"
                                + $" (extent {b.size.y:0.00}){gap}");
                        previousTop = b.max.y;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[stack] FAILED: " + ex);
            }
            finally
            {
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
                if (sunGo != null) UnityEngine.Object.DestroyImmediate(sunGo);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Shoot(Camera cam, string path)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var shot = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            shot.Apply();

            File.WriteAllBytes(path, shot.EncodeToPNG());

            RenderTexture.active = null;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(shot);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
