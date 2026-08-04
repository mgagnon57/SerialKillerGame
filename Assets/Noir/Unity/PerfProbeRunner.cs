using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// Presses Play, moves the camera, samples every frame, writes a report, and stops.
    ///
    /// WHY IT IS A COMPONENT AND A FILE. Every Unity_RunCommand compiles a script, which
    /// triggers a domain reload and drops out of play mode - so a probe cannot be one command
    /// that enters play and reads a number. It has to survive the reload, which means the
    /// measurement runs here, across frames, and lands in a FILE that can be read afterwards.
    ///
    /// IT DRIVES THE CAMERA ITSELF. "Jumps all over the place when moving around" is a claim
    /// about motion, and a probe that sits still cannot test it. Each phase below is a different
    /// kind of movement, timed separately, so a cost that only appears while panning is
    /// separable from one that is there the whole time.
    ///
    /// The camera is moved in LateUpdate, after OrbitCamera has had its say, because culling and
    /// draw calls answer to the transform the frame is actually rendered from.
    /// </summary>
    public sealed class PerfProbeRunner : MonoBehaviour
    {
        /// <summary>Where the report lands. Read this, not the console.</summary>
        public static string ReportPath =>
            Path.Combine(Path.GetTempPath(), "noir-perf-probe.txt");

        private sealed class Phase
        {
            public string Name;
            public float Seconds;
            public System.Action<PerfProbeRunner, float> Move;
            public readonly List<float> Ms = new List<float>();
            public readonly List<int> Draws = new List<int>();
            public readonly List<int> Tris = new List<int>();
        }

        private Camera _cam;
        private Vector3 _home;
        private Quaternion _homeRot;
        private readonly List<Phase> _phases = new List<Phase>();
        private Phase _current;

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            // Let the town finish building and the first frames settle - the build frame itself
            // is 1.2 s and would swamp every average that followed it.
            yield return new WaitForSecondsRealtime(2.5f);

            _cam = Camera.main;
            if (_cam == null) { Write("no camera found"); Finish(); yield break; }
            _home = _cam.transform.position;
            _homeRot = _cam.transform.rotation;

            _phases.Add(new Phase { Name = "still", Seconds = 2.5f, Move = null });
            _phases.Add(new Phase
            {
                Name = "panning",
                Seconds = 4f,
                Move = (p, t) => p._cam.transform.position =
                    p._home + new Vector3(Mathf.Sin(t * 1.2f) * 220f, 0f, Mathf.Cos(t * 0.9f) * 220f),
            });
            _phases.Add(new Phase { Name = "still again", Seconds = 2.5f, Move = null });
            _phases.Add(new Phase
            {
                Name = "orbiting",
                Seconds = 4f,
                Move = (p, t) =>
                {
                    var pivot = p._home + p._cam.transform.forward * 300f;
                    float a = t * 40f;
                    p._cam.transform.position = pivot + Quaternion.Euler(0f, a, 0f)
                                                       * (p._home - pivot);
                    p._cam.transform.LookAt(pivot);
                },
            });
            _phases.Add(new Phase
            {
                Name = "zooming",
                Seconds = 4f,
                Move = (p, t) => p._cam.transform.position =
                    p._home + p._cam.transform.forward * (Mathf.Sin(t * 1.5f) * 250f),
            });

            foreach (var phase in _phases)
            {
                _current = phase;
                _cam.transform.position = _home;
                _cam.transform.rotation = _homeRot;
                float until = Time.realtimeSinceStartup + phase.Seconds;
                while (Time.realtimeSinceStartup < until) yield return null;
            }
            _current = null;

            _cam.transform.position = _home;
            _cam.transform.rotation = _homeRot;
            Write(Report());
            Finish();
        }

        private void LateUpdate()
        {
            if (_current == null || _cam == null) return;

            _current.Move?.Invoke(this, Time.realtimeSinceStartup);
            _current.Ms.Add(Time.unscaledDeltaTime * 1000f);
#if UNITY_EDITOR
            _current.Draws.Add(UnityEditor.UnityStats.drawCalls);
            _current.Tris.Add(UnityEditor.UnityStats.triangles);
#endif
        }

        private static float Percentile(List<float> xs, float p)
        {
            if (xs.Count == 0) return 0f;
            var copy = new List<float>(xs);
            copy.Sort();
            return copy[Mathf.Clamp(Mathf.RoundToInt(copy.Count * p) - 1, 0, copy.Count - 1)];
        }

        private static float Mean(List<float> xs)
        {
            if (xs.Count == 0) return 0f;
            float s = 0f; foreach (var x in xs) s += x; return s / xs.Count;
        }

        private static int MeanI(List<int> xs)
        {
            if (xs.Count == 0) return 0;
            long s = 0; foreach (var x in xs) s += x; return (int)(s / xs.Count);
        }

        /// <summary>
        /// Everything needed to answer "is it my PC, is it Unity, or is it the project" without
        /// asking a human to go and look. Written into the same report as the frame timings so
        /// the two are never read apart.
        /// </summary>
        private static void Machine(StringBuilder sb)
        {
            sb.AppendLine("--- machine ---");
            sb.AppendLine($"cpu        {SystemInfo.processorType} "
                        + $"({SystemInfo.processorCount} threads, {SystemInfo.processorFrequency} MHz)");
            sb.AppendLine($"memory     {SystemInfo.systemMemorySize:N0} MB");
            sb.AppendLine($"gpu        {SystemInfo.graphicsDeviceName} "
                        + $"({SystemInfo.graphicsMemorySize:N0} MB, {SystemInfo.graphicsDeviceType})");
            sb.AppendLine($"driver     {SystemInfo.graphicsDeviceVersion}");
            sb.AppendLine();

            sb.AppendLine("--- what the frame has to fill ---");
            sb.AppendLine($"screen     {Screen.width} x {Screen.height} "
                        + $"({Screen.width * Screen.height / 1_000_000f:0.00} megapixels)");
            sb.AppendLine($"vsync      {QualitySettings.vSyncCount} "
                        + $"(target frame rate {Application.targetFrameRate})");
            sb.AppendLine($"quality    {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            sb.AppendLine($"shadows    {QualitySettings.shadows}, "
                        + $"{QualitySettings.shadowCascades} cascade(s), "
                        + $"distance {QualitySettings.shadowDistance:0}");
            sb.AppendLine();

            sb.AppendLine("--- unity's own memory ---");
            sb.AppendLine($"mono heap  {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576:N0} MB used "
                        + $"of {UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / 1048576:N0} MB");
            sb.AppendLine($"allocated  {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576:N0} MB "
                        + $"of {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576:N0} MB reserved");
            sb.AppendLine($"gc so far  {System.GC.CollectionCount(0)} gen0, "
                        + $"{System.GC.CollectionCount(1)} gen1, {System.GC.CollectionCount(2)} gen2");
            sb.AppendLine();

            sb.AppendLine("--- what is in the scene ---");
            int meshes = 0, active = 0, skinned = 0, lights = 0, tris = 0;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                meshes++;
                if (r.enabled && r.gameObject.activeInHierarchy) active++;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && r.enabled && r.gameObject.activeInHierarchy)
                    tris += mf.sharedMesh.triangles.Length / 3;
            }
            skinned = FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Length;
            lights = FindObjectsByType<Light>(FindObjectsSortMode.None).Length;
            sb.AppendLine($"renderers  {active:N0} drawing of {meshes:N0} built");
            sb.AppendLine($"skinned    {skinned:N0} (animated figures)");
            sb.AppendLine($"lights     {lights:N0}");
            sb.AppendLine($"triangles  {tris:N0} in the drawing set");
            sb.AppendLine($"behaviours {FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Length:N0} "
                        + "(everything with an Update)");
            sb.AppendLine();
        }

        private string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("NOIR PERFORMANCE PROBE");
            sb.AppendLine($"layers on: {Layers.CountOn()} of {Layers.All.Length}");
            var host = FindFirstObjectByType<VillageHost>();
            if (host != null && host.Sim != null)
                sb.AppendLine($"agents simulated: {host.Sim.AgentCount}");
            sb.AppendLine();
            Machine(sb);
            sb.AppendLine("phase          frames    mean     p50     p99    worst    fps   draws     tris");
            foreach (var p in _phases)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-13} {1,6}  {2,6:0.0}  {3,6:0.0}  {4,6:0.0}  {5,7:0.0}  {6,5:0}  {7,6}  {8,7}",
                    p.Name, p.Ms.Count, Mean(p.Ms), Percentile(p.Ms, 0.50f),
                    Percentile(p.Ms, 0.99f), Percentile(p.Ms, 1f),
                    Mean(p.Ms) > 0.01f ? 1000f / Mean(p.Ms) : 0f,
                    MeanI(p.Draws), MeanI(p.Tris)));
            }
            sb.AppendLine();
            sb.AppendLine("p99 and worst are the chop. A good mean with a bad p99 is a hitch,");
            sb.AppendLine("and a bad mean with a matching p99 is just a slow frame.");
            sb.AppendLine();
            sb.AppendLine("--- reading it ---");

            var still = _phases.Find(p => p.Name == "still");
            var pan = _phases.Find(p => p.Name == "panning");
            if (still != null && pan != null && still.Ms.Count > 4 && pan.Ms.Count > 4)
            {
                float sm = Mean(still.Ms), pm = Mean(pan.Ms);
                int sd = MeanI(still.Draws), pd = MeanI(pan.Draws);

                if (sm > 25f && pm < sm * 1.3f)
                    sb.AppendLine("SLOW EVEN STANDING STILL, and moving barely changes it. The cost "
                                + "is per-frame work that does not care about the camera - look at "
                                + "Update methods and the behaviour count above, not at geometry.");
                else if (pm > sm * 1.5f && pd > sd * 1.3f)
                    sb.AppendLine("MOVING COSTS MORE AND DRAWS MORE. Culling is bringing new "
                                + "geometry into view faster than it can be pushed - chunk sizes "
                                + "and draw call count are the lever.");
                else if (pm > sm * 1.5f)
                    sb.AppendLine("MOVING COSTS MORE BUT DRAWS THE SAME. Not the geometry - "
                                + "something is doing work in response to the camera moving.");
                else if (sm < 20f && Percentile(still.Ms, 0.99f) > 50f)
                    sb.AppendLine("FAST ON AVERAGE WITH HITCHES. Look at the gc counts above and "
                                + "at anything allocating per frame; a stall is not a slow frame.");
                else
                    sb.AppendLine("Nothing here is slow. If it FEELS slow, the editor itself is "
                                + "the suspect - a Game view at a silly resolution, the profiler "
                                + "attached, or another window redrawing behind it.");
            }
            return sb.ToString();
        }

        private static void Write(string text)
        {
            try { File.WriteAllText(ReportPath, text); Debug.Log("[probe] wrote " + ReportPath); }
            catch (System.Exception e) { Debug.LogError("[probe] could not write: " + e.Message); }
        }

        private static void Finish()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
