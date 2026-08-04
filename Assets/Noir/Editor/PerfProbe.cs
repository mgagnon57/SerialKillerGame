using System.IO;
using UnityEditor;
using UnityEngine;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Enters Play, attaches the probe, and gets out of the way.
    ///
    /// The two halves have to be separate because ENTERING PLAY MODE RELOADS THE DOMAIN and
    /// throws away every static and every subscription made before it. So the request is parked
    /// in SessionState - which survives a reload and does not survive quitting the editor, which
    /// is exactly the lifetime wanted - and picked up again on the other side.
    ///
    /// Written so an assistant can run a performance measurement WITHOUT the owner having to
    /// press Play, move the camera, and read numbers off a HUD. That is not his job.
    /// </summary>
    public static class PerfProbe
    {
        private const string Pending = "noir.perfprobe.pending";

        [MenuItem("Noir/Probe Performance")]
        public static void Begin()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[probe] already in play mode - stop it first.");
                return;
            }
            if (File.Exists(PerfProbeRunner.ReportPath)) File.Delete(PerfProbeRunner.ReportPath);

            SessionState.SetBool(Pending, true);
            Debug.Log("[probe] entering play mode; report will land at "
                    + PerfProbeRunner.ReportPath);
            EditorApplication.isPlaying = true;
        }

        /// <summary>True while a probe is queued or running, for anyone polling from outside.</summary>
        public static bool InProgress => SessionState.GetBool(Pending, false);

        [InitializeOnLoadMethod]
        private static void Hook() => EditorApplication.playModeStateChanged += OnPlayMode;

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            if (!SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);

            var go = new GameObject("PerfProbeRunner");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<PerfProbeRunner>();
            Debug.Log("[probe] attached - sampling still, panning, orbiting and zooming.");
        }
    }
}
