using System.IO;
using UnityEditor;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// Enters play mode, from the menu or from outside the editor entirely.
    ///
    /// The editor's dynamic-command host is the normal way an assistant presses Play, and it is
    /// not always there: a session that reaches this project over an IDE integration rather than
    /// over that host has no way to touch a running editor at all. This gives it two that need
    /// nothing but a process launch and a file:
    ///
    ///   Unity.exe -projectPath C:\SerialKillerGame -executeMethod Noir.Editor.PlayNow.Run
    ///
    /// on a COLD start, and for an editor that is already open, the same marker-file trick
    /// PerfProbeAutostart uses - drop the marker, make the editor lose and regain focus, and the
    /// recompile picks it up on the way back in. A recompile is the one hook that always fires.
    ///
    /// Deliberately NOT batchmode. Play mode in -batchmode has no window to look at and the point
    /// of this is to leave the game running in front of somebody.
    /// </summary>
    internal static class PlayNow
    {
        private static readonly string Marker =
            Path.Combine(Path.GetTempPath(), "noir-play-please.txt");

        [MenuItem("Noir/Play Now")]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.Log("[play] already in play mode - nothing to do.");
                return;
            }

            // Not straight from -executeMethod's own call. Unity runs that while the editor is
            // still assembling itself, and setting isPlaying at that point is unreliable in
            // exactly the way PerfProbeAutostart's header describes. delayCall runs once the
            // editor has finished coming up.
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlaying) return;
                Debug.Log("[play] entering play mode.");
                EditorApplication.isPlaying = true;
            };
        }

        /// <summary>
        /// Writes the marker an already-open editor picks up on its next recompile. Callable from
        /// outside; it does not need the editor at all, which is the whole point.
        /// </summary>
        public static void RequestFromOutside() => File.WriteAllText(Marker, "play");

        [InitializeOnLoad]
        internal static class Autostart
        {
            static Autostart()
            {
                if (!File.Exists(Marker)) return;

                EditorApplication.delayCall += () =>
                {
                    // Deleted HERE and not in the constructor, for the reason PerfProbeAutostart
                    // records: a second domain reload landing before this callback fires throws
                    // the callback away, and a marker deleted early is a request lost outright.
                    if (!File.Exists(Marker)) return;
                    if (EditorApplication.isPlaying) return;
                    File.Delete(Marker);

                    Debug.Log("[play] autostart marker found - entering play mode.");
                    EditorApplication.isPlaying = true;
                };
            }
        }
    }
}
