using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Noir.Unity;

namespace Noir.PlayTests
{
    /// <summary>
    /// Drives a camera round the whole of Northgate WHILE IT IS RUNNING and writes what it sees.
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
            public readonly Vector3 At;                 // village x, eye height, village y
            public readonly float Distance, Pitch, Yaw;

            public Stop(string name, float vx, float vy, float eye,
                        float distance, float pitch, float yaw)
            {
                Name = name;
                At = new Vector3(vx, eye, -vy);
                Distance = distance;
                Pitch = pitch;
                Yaw = yaw;
            }
        }

        // EVERY CAMERA STANDS SOMEWHERE OPEN. Frame() puts the camera a long way BACK from what
        // it is aimed at, so a stop chosen by where you want to look rather than where that
        // leaves you ends up inside a building photographing wallpaper. Each of these has had
        // its resting position worked out and checked against the map.
        private static readonly Stop[] Route =
        {
            new Stop("Northgate Avenue, looking east",       430f, 435f, 1.6f,  36f,  3f,  90f),
            new Stop("Northgate meets First Street",         435f, 435f, 0f,    55f, 45f,  90f),
            new Stop("Under the Elevated, at Second Street", 525f, 435f, 0f,    70f, 10f,  90f),
            new Stop("The terrace on Northgate",             480f, 422f, 1.5f,  30f,  8f,  20f),
            new Stop("Downtown, the Meridian",               478f, 479f, 0f,   120f, 25f,  40f),
            new Stop("The whole town",                       480f, 480f, 0f,   265f, 38f,  30f),
            new Stop("Franklin Park",                        390f, 570f, 0f,    60f, 20f, 300f),
            new Stop("First Street, leaving town",           435f, 610f, 1.6f,  45f,  5f, 180f),
            new Stop("Where the track meets the road",       435f, 660f, 0f,    70f, 25f, 200f),
            new Stop("Home Farm",                            390f, 640f, 0f,    60f, 20f, 300f),
            new Stop("The big barn and the silos",           385f, 685f, 0f,    55f, 18f, 250f),
            new Stop("The bottom field",                     570f, 660f, 0f,    90f, 30f,  30f),
            new Stop("The east belt",                        690f, 480f, 0f,    80f, 20f,  90f),
            new Stop("Wicker End, the back place",           660f, 638f, 0f,    55f, 18f, 210f),
            new Stop("The old orchard, gone to scrub",       660f, 700f, 0f,    55f, 15f, 340f),
            new Stop("Out on the ring",                      255f, 255f, 0f,   150f, 25f,  40f),
            new Stop("A rural arterial",                     255f, 500f, 1.8f,  60f,  4f, 180f),
            new Stop("The town from the ring road",          390f, 300f, 0f,   300f, 26f, 150f),
            new Stop("The whole map",                        480f, 480f, 0f,  1150f, 50f,  30f),
        };

        [UnityTest, Timeout(1800000)]
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
                camGo.transform.position = stop.At + Vector3.up * 2f
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
            camGo.transform.position = far.At + Vector3.up * 2f
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
