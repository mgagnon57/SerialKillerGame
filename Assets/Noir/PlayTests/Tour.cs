using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Noir.Unity;

namespace Noir.PlayTests
{
    /// <summary>
    /// Drives a camera round the whole of Rossville WHILE IT IS RUNNING and writes what it sees.
    ///
    /// Not a test - it asserts almost nothing. It exists so the city can be handed to somebody
    /// as a thing to look at rather than as a list of claims about itself. The stills the editor
    /// scripts produce are of a frozen city with no traffic in it; this one has the simulation
    /// running, the signals cycling and the cars moving, because that is the city that actually
    /// exists.
    ///
    /// JPEG, not PNG. A 1600x900 PNG of this scene is about a megabyte and a half, and twenty of
    /// those cannot be carried anywhere; the same frame as JPEG is a twentieth of that and the
    /// difference is invisible on flat-shaded low-poly.
    ///
    /// Frames land in docs/snapshots/tour/, numbered in the order they were taken.
    /// </summary>
    public class Tour
    {
        private const int Width = 1024, Height = 576;
        private static string Dir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "snapshots", "tour"));

        /// <summary>Somewhere to stand, and why.</summary>
        private readonly struct Stop
        {
            public readonly string Name;
            public readonly Vector2 Map;                // map coordinates, straight off city.txt
            public readonly float Eye, Distance, Pitch, Yaw;

            /// <summary>
            /// ON THE GROUND, NOT AT NOUGHT. This built `new Vector3(vx, eye, -vy)` - an ABSOLUTE
            /// world y - so an eye-level stop stood 1.6 m above sea level on a map with 24 m of
            /// relief, which is underground everywhere in town. Exactly the fault CityShot.At
            /// records fixing for eleven cameras and LayerShot had as well. Asked per stop rather
            /// than baked in, because the grid is not loaded when this array is constructed.
            /// </summary>
            public Vector3 Target => new Vector3(
                Map.x, ElevationGrid.HeightAt(Map.x, Map.y) + Eye, -Map.y);

            public Stop(string name, float vx, float vy, float eye,
                        float distance, float pitch, float yaw)
            {
                Name = name;
                Map = new Vector2(vx, vy);
                Eye = eye;
                Distance = distance;
                Pitch = pitch;
                Yaw = yaw;
            }
        }

        // EVERY CAMERA STANDS SOMEWHERE OPEN. Frame() puts the camera a long way BACK from what
        // it is aimed at, so a stop chosen by where you want to look rather than where that
        // leaves you ends up inside a building photographing wallpaper. Each of these has had
        // its resting position worked out and checked against the map.
        //
        // AND THE WHOLE ROUTE WAS IN ANOTHER TOWN. Measured 2026-08-09 against Content/city.txt:
        // every one of the nineteen stops sat inside 255..690 east by 255..700 south, a box that
        // holds TWENTY-ONE of Rossville's 477 places. The town's median place is at y=1494; the
        // route's median stop was at y=480. The names said so out loud and nobody read them -
        // "Fourth Street", "Franklin Park", "Home Farm", "Wicker End", "the old orchard" are
        // Ashcombe's, and Ashcombe was retired. So the longest diagnostic in the project, thirty
        // minutes of timeout, has been driving through empty farmland south-west of town and
        // asserting that nineteen JPEGs appeared. They did.
        //
        // Every coordinate below is now a real place out of city.txt, named for what stands
        // there. THE YAWS ARE A FIRST GUESS and must be corrected by looking at the first render
        // - GroundShot records three hand-computed bearings once photographing a wood forty
        // metres from anything, and ShotLog now goes red if two stops come out identical, which
        // is what "both of these are looking at a field" looks like from outside.
        private static readonly Stop[] Route =
        {
            new Stop("Chicago and Attica, the crossing",   750f, 1335f, 1.6f,  40f,  4f,   0f),
            new Stop("The Chicago Street diner",           726f, 1371f, 1.6f,  30f,  6f,  90f),
            new Stop("Main Street, looking north",         700f, 1250f, 1.6f,  55f,  5f,   0f),
            new Stop("The bank and the Opera House",       685f, 1255f, 0f,    70f, 20f,  45f),
            new Stop("The Commercial Hotel",               720f, 1179f, 1.6f,  35f,  8f, 270f),
            new Stop("The village office and the hall",    656f, 1152f, 0f,    60f, 22f,  60f),
            new Stop("The whole downtown block",           700f, 1280f, 0f,   180f, 35f,  20f),
            new Stop("The Rossville elevator",            1171f, 1353f, 0f,   110f, 18f, 270f),
            new Stop("205 Maple Ave, the church",          920f, 1455f, 0f,    60f, 15f, 300f),
            new Stop("408 Holmes Ave",                    1175f, 1218f, 1.6f,  26f,  4f,   0f),
            new Stop("The rooms over the grocer",          820f, 1490f, 1.6f,  40f,  8f, 180f),
            new Stop("Rossville Grade School",             533f, 1121f, 0f,    75f, 20f,  90f),
            new Stop("The high school on the north edge",  700f, 1020f, 0f,    90f, 22f, 160f),
            new Stop("The water tower",                    840f,  854f, 0f,   120f, 12f, 200f),
            new Stop("The depot yard",                     537f, 1730f, 0f,    80f, 20f,  30f),
            new Stop("Rossville Cemetery",                 271f, 1959f, 0f,    90f, 18f,  60f),
            new Stop("The Bishop road swings",            1171f, 1979f, 0f,    70f, 15f, 300f),
            new Stop("The town from the south",            750f, 1900f, 0f,   400f, 26f,   0f),
            new Stop("The whole map",                      750f, 1335f, 0f,  1400f, 50f,  30f),
        };

        [UnityTest, Category("Diagnostic"), Timeout(1800000)]
        public IEnumerator DriveTheWholeCity()
        {
            yield return CityUnderTest.WaitUntilBuilt();
            Directory.CreateDirectory(Dir);

            var host = CityUnderTest.Host;

            // Midday, so the tour is about the city rather than about the dark. The game opens
            // at six when the sun is barely up, which is honest about the experience and useless
            // for showing somebody what is there.
            host.SkipToHour(12);
            while (host.Skipping) yield return null;
            for (int settle = 0; settle < 10; settle++) yield return null;

            // Fast enough that traffic visibly moves between stops, slow enough to photograph.
            Time.timeScale = 3f;

            var camGo = new GameObject("TourCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 4000f;
            cam.clearFlags = CameraClearFlags.Skybox;

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            int frame = 0;

            for (int i = 0; i < Route.Length; i++)
            {
                var stop = Route[i];
                var rotation = Quaternion.Euler(stop.Pitch, stop.Yaw, 0f);
                camGo.transform.rotation = rotation;
                camGo.transform.position = stop.Target + Vector3.up * 2f
                                         - rotation * Vector3.forward * stop.Distance;

                // A beat at each stop so the traffic is somewhere different from the last one.
                float until = Time.time + 1.2f;
                while (Time.time < until) yield return null;

                Capture(cam, rt, shot, frame++, stop.Name);
            }

            // THE LAST FRAME IS TAKEN IN CLEAR AIR.
            //
            // SunRig's fog is tuned for standing in the place, and at a kilometre back it eats
            // the whole map - the grid came out as a grey suggestion of itself. The rig writes
            // fog every frame, so it has to be stood down rather than merely overridden, and put
            // back afterwards because the junction frames below want the weather they ship with.
            var weather = Object.FindFirstObjectByType<SunRig>();
            if (weather != null) weather.enabled = false;
            bool wasFog = RenderSettings.fog;
            RenderSettings.fog = false;

            var far = Route[Route.Length - 1];
            var farRotation = Quaternion.Euler(far.Pitch, far.Yaw, 0f);
            camGo.transform.rotation = farRotation;
            camGo.transform.position = far.Target + Vector3.up * 2f
                                     - farRotation * Vector3.forward * far.Distance;
            yield return null;
            Capture(cam, rt, shot, frame - 1, far.Name + " (clear air)");

            RenderSettings.fog = wasFog;
            if (weather != null) weather.enabled = true;

            // And then stand at one junction and watch it work: four frames far enough apart to
            // cover a change of phase, which is the only way to show a queue forming and going.
            var watchRotation = Quaternion.Euler(40f, 70f, 0f);
            camGo.transform.rotation = watchRotation;
            camGo.transform.position = new Vector3(435f, 2f, -435f)
                                     - watchRotation * Vector3.forward * 58f;

            for (int burst = 0; burst < 4; burst++)
            {
                float until = Time.time + 6f;
                while (Time.time < until) yield return null;
                Capture(cam, rt, shot, frame++, $"The junction working, {burst + 1} of 4");
            }

            Object.DestroyImmediate(shot);
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Time.timeScale = 1f;

            Assert.That(Directory.GetFiles(Dir, "*.jpg").Length, Is.EqualTo(frame));

            // NINETEEN STOPS, AND UNTIL NOW NOTHING COULD SAY WHICH OF THEM PHOTOGRAPHED
            // ANYTHING. The file count is not evidence - a camera aimed at open country writes a
            // perfectly good JPEG - so Stamp hashes them and goes red when two frames are the
            // same bytes, which is what "this stop and that stop are both looking at a field"
            // looks like from the outside.
            ShotLog.Stamp("tour", Dir, Directory.GetFiles(Dir, "*.jpg"));
            Debug.Log($"[tour] {frame} frames in {Dir}");
        }

        private static void Capture(Camera cam, RenderTexture rt, Texture2D shot,
                                    int index, string name)
        {
            var wasTarget = cam.targetTexture;
            var wasActive = RenderTexture.active;

            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            shot.Apply();

            cam.targetTexture = wasTarget;
            RenderTexture.active = wasActive;

            string path = Path.Combine(Dir, $"{index:00}.jpg");
            File.WriteAllBytes(path, shot.EncodeToJPG(78));
            Debug.Log($"[tour] {index:00}  {name}");
        }
    }
}
