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

        // ---- Unity's own internal markers ---------------------------------------------------
        //
        // Forty stalls, focused, idle, no collection, no allocation, no new geometry - every
        // counter I had said "nothing did this", which means the thing doing it is inside Unity
        // where those counters cannot see. A Recorder reads the engine's OWN profiler markers,
        // so it can.
        //
        // EditorLoop is the one to watch. In the editor, the player loop and the editor's own
        // UI work share a thread, and time the editor spends repainting an inspector, a
        // hierarchy or a console is time the game is simply not running. It reads as a frozen
        // game and is nothing of the kind.
        //
        // Gfx.WaitForPresent is the other: the main thread parked waiting on the GPU or on
        // vsync, which is a completely different problem from being busy.
        //
        // Read these as a set, never as a winner. Recorder sums a marker across every thread
        // that hit it, so a marker that lives on eight idle job workers reports eight times the
        // frame's wall clock while doing nothing at all. Semaphore.WaitForSignal is exactly
        // that: parked worker threads. It topped the table on all forty stalls of the previous
        // run at four to five times the frame time, which is the signature of idling, not of
        // work. It stays in the list as a control, and it is excluded from the blame.
        private static readonly string[] Markers =
        {
            "EditorLoop",
            "PlayerLoop",
            "Gfx.WaitForPresentOnGfxThread",
            "Gfx.WaitForRenderThread",
            "Gfx.WaitForGfxCommandsFromMainThread",
            "Gfx.PresentFrame",
            "WaitForTargetFPS",
            "Camera.Render",
            "Physics.Processing",
            "GUI.Repaint",
            "Loading.UpdatePreloading",
            "Overhead",

            // The loop phases. BehaviourUpdate took the whole frame on all forty stalls of the
            // previous run and LateBehaviourUpdate never registered at all, so the block is in
            // some component's Update - not a LateUpdate, not a coroutine, not audio, animation
            // or canvas, all of which stayed silent and are kept here as controls.
            "BehaviourUpdate",
            "LateBehaviourUpdate",
            "CoroutinesDelayedCalls",
            "Update.ScriptRunBehaviourUpdate",
            "AudioManager.Update",

            // Per-script markers were tried here - "VillageHost.Update()" and the eleven others.
            // Recorder reports every one of them invalid: Unity only emits per-script samples
            // under a deep profile, which is not on in a normal play session. That is why the
            // component is being found by a speed sweep below rather than by reading a name.

            "Semaphore.WaitForSignal",
        };

        /// <summary>Summed over threads, so it cannot be compared against the others.</summary>
        private const string IdleThreadMarker = "Semaphore.WaitForSignal";

        private readonly List<UnityEngine.Profiling.Recorder> _recorders =
            new List<UnityEngine.Profiling.Recorder>();

        private void Awake()
        {
            foreach (var name in Markers)
            {
                var r = UnityEngine.Profiling.Recorder.Get(name);
                r.enabled = true;
                _recorders.Add(r);
            }
        }

        /// <summary>
        /// Every marker that read above a millisecond this frame, largest first, and separately
        /// the largest one that is not the idle-thread control. A stall whose breakdown is empty
        /// is a stall that happened outside everything Unity is able to time, which is itself
        /// the answer.
        /// </summary>
        private void Blame(out string who, out float ms, out string breakdown)
        {
            who = "unattributed"; ms = 0f;
            var parts = new List<string>();
            var order = new List<int>();
            for (int i = 0; i < _recorders.Count; i++)
            {
                if (!_recorders[i].isValid) continue;
                if (_recorders[i].elapsedNanoseconds / 1_000_000f < 1f) continue;
                order.Add(i);
            }
            order.Sort((a, b) => _recorders[b].elapsedNanoseconds
                                 .CompareTo(_recorders[a].elapsedNanoseconds));
            foreach (int i in order)
            {
                float took = _recorders[i].elapsedNanoseconds / 1_000_000f;
                parts.Add($"{Markers[i]} {took:0}");
                if (Markers[i] == IdleThreadMarker) continue;
                if (took > ms) { ms = took; who = Markers[i]; }
            }
            breakdown = parts.Count == 0 ? "(nothing above 1 ms)" : string.Join("  ", parts);
        }

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

            _host = FindFirstObjectByType<VillageHost>();
            if (_host == null) { Write("no VillageHost found"); Finish(); yield break; }

            // A SIMULATION SPEED SWEEP, camera held still throughout.
            //
            // Everything measurable has already been ruled out: the stall is a component's
            // Update, on the main thread, with the render thread starved behind it. Unity does
            // not register per-script markers for Recorder to read, so the component has to be
            // found by changing one thing at a time instead.
            //
            // VillageHost.Update is the only Update whose cost is switchable from outside: it
            // calls Sim.Tick only when Speeds[SpeedIndex] is above zero, and how MUCH it ticks
            // scales with that speed. Nothing else in the scene changes across these phases -
            // same camera, same geometry, same draw calls, same everything.
            //
            // So the stalls either track the speed or they do not, and that single fact either
            // convicts the simulation or clears it outright.
            // The first sweep found 0x, 1x and 10x completely clean - 13,189 frames without a
            // single stall - and 60x collapsing at once. But the earlier runs that stalled forty
            // times were at the default 10x, so one of two things is true: either the stall has
            // a threshold between 10x and 60x, or it depends on how much GAME TIME has passed
            // and the 14-second 10x phase simply ended before reaching it.
            //
            // So: hold 10x for a full minute, the same length as the watch that used to stall,
            // and stamp the game clock on every stall. If stalls appear here, the trigger is
            // something the town does at a certain hour, not the speed. If a minute at 10x stays
            // clean, the threshold is real and the speed is the whole story.
            _phases.Add(new Phase { Name = "10x LONG watch", Seconds = 60f,
                Move = (p, t) => p._host.SpeedIndex = 5 });
            _phases.Add(new Phase { Name = "60x", Seconds = 10f,
                Move = (p, t) => p._host.SpeedIndex = 6 });

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

        // ---- spike attribution -------------------------------------------------------------
        //
        // A frame time on its own says a stall happened. These say WHAT it was doing: whether a
        // garbage collection ran across it, how much managed memory moved, and how much the
        // draw call count changed. A 900 ms frame with a gen2 collection in it is a GC pause; a
        // 900 ms frame with no collection and no new draw calls is something blocking, and the
        // two have nothing to do with each other.
        private sealed class Spike
        {
            public string Phase; public int Frame; public float Ms;
            public int Gen0, Gen1, Gen2; public long AllocDelta; public int DrawDelta;

            // Whether the EDITOR was busy across the stall. Forty stalls with no collection, no
            // memory movement and no new draw calls means nothing in the game did it - so the
            // question becomes what the editor was doing to its own main thread.
            public bool Focused, Compiling, Updating;

            /// <summary>Which of Unity's own internal markers ate the frame, and for how long.
            /// The counters above say what did NOT cause the stall; this says what did.
            /// Breakdown carries every marker that read at all, because the largest single one
            /// is meaningless without the others beside it to scale against.</summary>
            public string Culprit; public float CulpritMs; public string Breakdown;

            /// <summary>The town's own clock, so a stall that recurs at a particular hour - a
            /// shift change, a school bell, everyone walking home - shows up as one.</summary>
            public string When; public int Ticks;

            /// <summary>The three things VillageHost.Update does, timed separately. Whichever of
            /// these is near the frame time is the stall - measured, not reasoned about.</summary>
            public float SimMs, RefreshMs, RigMs;

            /// <summary>The worst single tick's A* work inside this frame. If a 400 ms frame
            /// carries tens of thousands of nodes, the path budget is the stall.</summary>
            public int Nodes, Paths;
        }

        private VillageHost _host;
        private long _lastSimTick;
        private float _prevSimMs, _prevRefreshMs, _prevRigMs;
        private int _prevNodes, _prevPaths;

        private readonly List<Spike> _spikes = new List<Spike>();
        private int _g0, _g1, _g2, _lastDraws;
        private long _lastAlloc;
        private bool _primed;

        /// <summary>Anything at or over this is a stall a person notices, in milliseconds.</summary>
        private const float SpikeMs = 100f;

        private void LateUpdate()
        {
            if (_current == null || _cam == null) return;

            _current.Move?.Invoke(this, Time.realtimeSinceStartup);
            float ms = Time.unscaledDeltaTime * 1000f;
            _current.Ms.Add(ms);

            int draws = 0;
#if UNITY_EDITOR
            draws = UnityEditor.UnityStats.drawCalls;
            _current.Draws.Add(draws);
            _current.Tris.Add(UnityEditor.UnityStats.triangles);
#endif
            int g0 = System.GC.CollectionCount(0);
            int g1 = System.GC.CollectionCount(1);
            int g2 = System.GC.CollectionCount(2);
            long alloc = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();

            if (_primed && ms >= SpikeMs && _spikes.Count < 60)
            {
                Blame(out string who, out float whoMs, out string breakdown);
                var sim = _host != null ? _host.Sim : null;
                _spikes.Add(new Spike
                {
                    Culprit = who,
                    CulpritMs = whoMs,
                    Breakdown = breakdown,
                    When = sim != null
                        ? $"{sim.Clock.HourOfDay:00}:{sim.Clock.MinuteOfHour:00}" : "--:--",
                    // How many simulation ticks the host actually ran across this frame. This is
                    // the work itself, not a proxy for it: if the stall is the tick, this number
                    // is large exactly when the frame is long.
                    Ticks = sim != null ? (int)(sim.Clock.Tick - _lastSimTick) : 0,
                    // The PREVIOUS frame's costs, deliberately. Ms comes from unscaledDeltaTime,
                    // which is how long the frame BEFORE this one took, so pairing it with this
                    // frame's costs puts the cause one row away from its effect. That misalignment
                    // made the simulation look innocent - 840 ms of work printed against a 426 ms
                    // frame - when it was the culprit all along, one row down.
                    SimMs = _prevSimMs,
                    RefreshMs = _prevRefreshMs,
                    RigMs = _prevRigMs,
                    Nodes = _prevNodes,
                    Paths = _prevPaths,
                    Phase = _current.Name,
                    Frame = Time.frameCount,
                    Ms = ms,
                    Gen0 = g0 - _g0,
                    Gen1 = g1 - _g1,
                    Gen2 = g2 - _g2,
                    AllocDelta = alloc - _lastAlloc,
                    DrawDelta = draws - _lastDraws,
                    Focused = Application.isFocused,
#if UNITY_EDITOR
                    Compiling = UnityEditor.EditorApplication.isCompiling,
                    Updating = UnityEditor.EditorApplication.isUpdating,
#endif
                });
            }

            _g0 = g0; _g1 = g1; _g2 = g2;
            _lastAlloc = alloc; _lastDraws = draws;
            if (_host != null)
            {
                _prevSimMs = (float)_host.SimMs;
                _prevRefreshMs = (float)_host.RefreshMs;
                _prevRigMs = (float)_host.RigMs;
                if (_host.Sim != null)
                {
                    _lastSimTick = _host.Sim.Clock.Tick;
                    _prevNodes = _host.Sim.WorstPathNodesInATick;
                    _prevPaths = _host.Sim.WorstPathsInATick;
                    _host.Sim.ResetPathHighWater();
                }
            }
            _primed = true;
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
            {
                sb.AppendLine($"agents simulated: {host.Sim.AgentCount}");

            }
            sb.AppendLine();

            // A search that runs to the node cap is a search for somewhere that cannot be
            // reached. Capping the search only made that affordable; the town still has people
            // who can never get home, and this says which doors are the reason.
            if (host != null) sb.AppendLine(StrandedSurvey.Describe(host));
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

            // ---- every stall, and what it was doing --------------------------------------
            sb.AppendLine($"--- every frame over {SpikeMs:0} ms, and what ran across it ---");
            if (_spikes.Count == 0)
                sb.AppendLine("   none. Nothing stalled for a tenth of a second in the whole run.");
            else
            {
                sb.AppendLine("   frame       ms   ticks   sim.ms  refresh.ms   rig.ms   "
                              + "A*nodes  paths   ns/node  clock  phase");
                int gcSpikes = 0, unfocused = 0, editorBusy = 0;
                foreach (var s in _spikes)
                {
                    if (s.Gen2 > 0 || s.Gen1 > 0 || s.Gen0 > 0) gcSpikes++;
                    if (!s.Focused) unfocused++;
                    if (s.Compiling || s.Updating) editorBusy++;
                    // The alignment field takes a sign only to mean left-justify; "+10" is not a
                    // width and throws. The plus belongs in the FORMAT half, after the colon.
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "   {0,7} {1,8:0.0}  {2,6} {3,8:0.0} {4,11:0.0} {5,8:0.0} {6,9} {7,6} "
                        + "{8,9:0}  {9,-5}  {10}",
                        s.Frame, s.Ms, s.Ticks,
                        s.SimMs, s.RefreshMs, s.RigMs, s.Nodes, s.Paths,
                        s.Nodes > 0 ? s.SimMs * 1_000_000f / s.Nodes : 0f,
                        s.When, s.Phase));
                }

                // Which marker was to blame, over all the stalls, idle-thread control excluded.
                // One name dominating this is the answer; a spread means the block is somewhere
                // Unity does not mark. Read it against the frame's own milliseconds: a marker
                // that adds up to LESS than the stall did not eat the stall, whatever it says.
                var blame = new Dictionary<string, int>();
                var blameMs = new Dictionary<string, float>();
                foreach (var s in _spikes)
                {
                    blame[s.Culprit] = blame.TryGetValue(s.Culprit, out int c) ? c + 1 : 1;
                    blameMs[s.Culprit] = (blameMs.TryGetValue(s.Culprit, out float m) ? m : 0f)
                                       + s.CulpritMs;
                }
                sb.AppendLine();
                sb.AppendLine("   what ate the stalls:");
                foreach (var kv in blame)
                    sb.AppendLine($"      {kv.Key,-32} {kv.Value,3} stalls, "
                                + $"{blameMs[kv.Key]:0} ms total");
                sb.AppendLine();
                sb.AppendLine($"   {_spikes.Count} stalls, {gcSpikes} of them with a garbage "
                            + "collection running across the frame.");
                sb.AppendLine($"   {unfocused} of {_spikes.Count} happened while the editor "
                            + "window was NOT focused.");
                sb.AppendLine($"   {editorBusy} of {_spikes.Count} happened while the editor was "
                            + "compiling or importing.");
                if (unfocused == _spikes.Count && _spikes.Count > 4)
                    sb.AppendLine("   => EVERY stall was unfocused. The editor throttles its own "
                                + "loop in the background regardless of runInBackground; this is "
                                + "measurement artefact, not a fault in the game.");
                sb.AppendLine(gcSpikes > _spikes.Count / 2
                    ? "   => GARBAGE COLLECTION is the stall. Something is allocating hard enough "
                    + "to force collections on a heap this size, and each one stops the world."
                    : "   => the stalls are NOT collections. Look at the managed MB and draws "
                    + "columns: memory moving without a collection is loading, and draws moving "
                    + "is geometry coming into view.");
            }
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
