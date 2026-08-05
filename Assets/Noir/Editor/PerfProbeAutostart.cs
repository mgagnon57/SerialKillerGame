using System.IO;
using UnityEditor;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// Starts a performance probe on the next domain reload, if and only if a marker file is
    /// sitting in the temp directory.
    ///
    /// This exists because the editor's dynamic-command host - the thing that normally lets an
    /// assistant press Play from outside - can wedge, compiling submitted scripts fine but
    /// refusing to execute them. A recompile is the one hook that always fires, so the request
    /// is left on disk and picked up on the way back in.
    ///
    /// It disarms itself: the marker is deleted BEFORE play mode is entered, so the reload that
    /// entering play mode causes does not start a second probe on top of the first.
    /// </summary>
    [InitializeOnLoad]
    internal static class PerfProbeAutostart
    {
        private static readonly string Marker =
            Path.Combine(Path.GetTempPath(), "noir-probe-please.txt");

        static PerfProbeAutostart()
        {
            if (!File.Exists(Marker)) return;
            File.Delete(Marker);

            // Not from the static constructor itself. The editor is still assembling itself at
            // that point and setting isPlaying here is unreliable; delayCall runs once the
            // editor has finished coming up.
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlaying) return;
                Debug.Log("[probe] autostart marker found - beginning probe.");
                PerfProbe.Begin();
            };
        }
    }
}
