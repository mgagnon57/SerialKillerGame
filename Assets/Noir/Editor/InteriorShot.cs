using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Photographs the INSIDE of the generated buildings, which no other still ever has.
    ///
    /// Written the night the walls thinned from the full metre of their survey tile to their
    /// real inches (see Materials3D.WallDepthFor): every existing camera stands in a street,
    /// deliberately - CityShot's own header records the set that ended up "inside a townhouse,
    /// photographing wallpaper" as a fault - so the one change whose whole point is the inside
    /// of a room had no still that could see it. The tests prove the town builds and the doors
    /// walk; only a picture can say whether a five-inch partition READS as a wall.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.InteriorShot.Render -logFile &lt;log&gt;
    ///
    /// Four frames into docs/snapshots/: the home's own street front with the roofs on - so the
    /// survey line the skin is meant to hug can be checked against the kerb in the same set -
    /// then a dollhouse view down into a small home with the roofs off, an eye-level frame
    /// inside its main room facing the front door, and the same dollhouse view of a brick
    /// downtown unit.
    /// </summary>
    public static class InteriorShot
    {
        [MenuItem("Noir/Render Interiors (thin walls)")]
        public static void Render()
        {
            GameObject root = null, camGo = null, sunGo = null;
            try
            {
                var outDir = Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", "docs", "snapshots"));
                Directory.CreateDirectory(outDir);

                var built = TownPipeline.Build();
                var world = built.World;

                root = new GameObject("InteriorShot");
                VillageMesh.Build(world, root.transform, showDressing: true);

                // Sun and sky as CityShot takes them, at 13:00. The interiors are open to the
                // sky once the roofs go, and noon is the hour that lights a floor.
                sunGo = new GameObject("InteriorSun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.7f;
                var (colour, intensity, ambient) = SunRig.SkyAt(13f);
                sun.color = colour;
                sun.intensity = intensity;
                sunGo.transform.rotation = SunRig.SunRotation(13f);
                RenderSettings.sun = sun;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = ambient;
                RenderSettings.fog = false;

                camGo = new GameObject("InteriorCamera");
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 55f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 800f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.63f, 0.72f, 0.80f);

                // The buildings this set watches: the first small dwelling with a working door,
                // and the first brick unit - chosen by iteration order, which is build order,
                // which is deterministic under the seed like everything else in the town.
                Place home = null, store = null;
                foreach (var place in world.AllPlaces)
                {
                    var row = PlaceKindTable.Current.Row(place.Kind);
                    if (!row.IsBuilding) continue;
                    if (CityBuildings.Handles(place)) continue;   // a bought model brings its own walls
                    if (!place.Door.IsValid) continue;

                    int area = place.Bounds.W * place.Bounds.H;
                    if (home == null && row.IsHome && area >= 40 && area <= 90) home = place;
                    if (store == null && !row.IsHome && area >= 30 && area <= 160
                        && Materials3D.WallingFor(place) == Materials3D.BrickIndex) store = place;
                    if (home != null && store != null) break;
                }

                if (home == null)
                    throw new Exception("no small dwelling with a door - nothing to photograph");

                Debug.Log($"[interior] home: place {home.Id.Value} ({home.Kind}) "
                        + $"{home.Bounds.W}x{home.Bounds.H} door ({home.Door.X},{home.Door.Y})"
                        + (store == null
                            ? "; no brick unit matched"
                            : $"; store: place {store.Id.Value} ({store.Kind}) "
                            + $"{store.Bounds.W}x{store.Bounds.H}"));

                // ---- the street front first, roofs still on ----
                Shoot(cam, StreetEye(home), DoorPoint(home) + Vector3.up * 0.6f,
                      Path.Combine(outDir, "interior-street-front.png"));

                // ---- then the roofs come off and the rooms are lit ----
                int roofless = 0;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Roofs")
                        foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                        {
                            r.enabled = false;
                            roofless++;
                        }
                Debug.Log($"[interior] {roofless} roof renderer(s) off for the dollhouse frames.");

                Shoot(cam, DollEye(home), Centre(home),
                      Path.Combine(outDir, "interior-doll-home.png"));
                Shoot(cam, RoomEye(home), DoorPoint(home),
                      Path.Combine(outDir, "interior-room-home.png"));
                if (store != null)
                    Shoot(cam, DollEye(store), Centre(store),
                          Path.Combine(outDir, "interior-doll-store.png"));

                Debug.Log("[interior] done.");
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
                if (sunGo != null) UnityEngine.Object.DestroyImmediate(sunGo);
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
        }

        private static Vector3 Centre(Place p)
        {
            float mx = p.Bounds.X + p.Bounds.W * 0.5f;
            float my = p.Bounds.Y + p.Bounds.H * 0.5f;
            return new Vector3(mx, Space3D.GroundUnder(p.Bounds) + 0.4f, -my);
        }

        /// <summary>High enough to hold the whole footprint, backed off south so the frame
        /// reads as a dollhouse rather than a plan.</summary>
        private static Vector3 DollEye(Place p)
        {
            var c = Centre(p);
            float reach = Mathf.Max(p.Bounds.W, p.Bounds.H);
            return c + new Vector3(0f, reach * 1.35f, -reach * 0.55f);
        }

        private static Vector3 DoorPoint(Place p) =>
            new Vector3(p.Door.X + 0.5f,
                        Space3D.GroundUnder(p.Bounds) + 1.1f,
                        -(p.Door.Y + 0.5f));

        /// <summary>Standing inside, a stride or two back from the front door, eye height.</summary>
        private static Vector3 RoomEye(Place p)
        {
            var door = DoorPoint(p);
            var centre = Centre(p);
            var inward = new Vector3(centre.x - door.x, 0f, centre.z - door.z).normalized;
            var at = door + inward * 2.6f;
            return new Vector3(at.x, Space3D.GroundUnder(p.Bounds) + 1.55f, at.z);
        }

        /// <summary>Out through the front door and back off it, eye height on the real ground.</summary>
        private static Vector3 StreetEye(Place p)
        {
            var door = DoorPoint(p);
            var centre = Centre(p);
            var outward = new Vector3(door.x - centre.x, 0f, door.z - centre.z).normalized;
            var at = door + outward * 9f;
            return new Vector3(at.x, ElevationGrid.HeightAt(at.x, -at.z) + 1.6f, at.z);
        }

        private static void Shoot(Camera cam, Vector3 eye, Vector3 at, string path)
        {
            cam.transform.position = eye;
            cam.transform.LookAt(at);

            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var shot = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            shot.Apply();
            File.WriteAllBytes(path, shot.EncodeToPNG());
            cam.targetTexture = null;
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(shot);
            Debug.Log($"[interior] wrote {Path.GetFileName(path)}");
        }
    }
}
