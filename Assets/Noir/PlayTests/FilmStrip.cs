using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Noir.PlayTests
{
    /// <summary>
    /// Photographs the city at intervals WHILE IT IS RUNNING, so the moving parts can be looked
    /// at rather than only asserted about.
    ///
    /// The assertions next door prove the invariants - nothing leaves the road, no junction shows
    /// green twice, somebody waits at a red. What they cannot tell anybody is whether it LOOKS
    /// right: whether the queue at the lights reads as a queue, whether a car turning left cuts
    /// the corner like a car or like a compass. That is a matter for eyes, and this is how eyes
    /// get to see it without anybody having to sit and watch.
    ///
    /// Frames land in docs/snapshots/motion/ numbered in order.
    /// </summary>
    public class FilmStrip
    {
        private const int Width = 1280, Height = 720;

        /// <summary>Game seconds between frames, and how many to take.</summary>
        private const float Every = 3f;
        private const int Frames = 12;

        private static string Dir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "snapshots", "motion"));

        [UnitySetUp]
        public IEnumerator Ready()
        {
            // Slower than the assertion tests: this one wants frames that are far enough apart
            // to show change but close enough to read as a sequence.
            Time.timeScale = 4f;
            yield return CityUnderTest.WaitUntilBuilt();
        }

        [TearDown]
        public void Slow() => Time.timeScale = 1f;

        [UnityTest, Timeout(900000)]
        public IEnumerator PhotographTheJunctionWhileItRuns()
        {
            Directory.CreateDirectory(Dir);

            // Looking down on Northgate Avenue meeting First Street, from high enough to see
            // both approaches and low enough to tell one car from another.
            var camGo = new GameObject("FilmCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            cam.farClipPlane = 3000f;
            cam.clearFlags = CameraClearFlags.Skybox;

            var rotation = Quaternion.Euler(38f, 70f, 0f);
            camGo.transform.rotation = rotation;
            camGo.transform.position = new Vector3(75f, 0f, -75f)
                                     + Vector3.up * 2f - rotation * Vector3.forward * 62f;

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);

            for (int frame = 0; frame < Frames; frame++)
            {
                float until = Time.time + Every;
                while (Time.time < until) yield return null;

                // NO WaitForEndOfFrame HERE. It never fires in batchmode - the test runner
                // raises "UnityTest yielded WaitForEndOfFrame, which is not evoked in batchmode"
                // and the whole capture dies. It is not needed either: this renders its own
                // camera into its own target rather than reading back the screen, so there is no
                // frame to wait for the end of.
                var wasTarget = cam.targetTexture;
                var wasActive = RenderTexture.active;

                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                shot.Apply();

                cam.targetTexture = wasTarget;
                RenderTexture.active = wasActive;

                string path = Path.Combine(Dir, $"motion-{frame:00}.png");
                File.WriteAllBytes(path, shot.EncodeToPNG());
                Debug.Log($"[film] {path}  t={Time.time:0.0}s  "
                        + $"{CityUnderTest.Vehicles().Count} vehicles");
            }

            Object.DestroyImmediate(shot);
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);

            Assert.That(Directory.GetFiles(Dir, "motion-*.png").Length, Is.EqualTo(Frames));
        }
    }
}
