using System.IO;
using UnityEditor;
using UnityEngine;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Writes <see cref="StrandedSurvey"/> to a file, for when you want the answer on demand
    /// rather than as a side effect of a performance run.
    ///
    /// The survey itself is runtime code. This is only the menu item, because the probe drives
    /// Play by itself and has to be able to produce the same answer without anybody clicking.
    /// </summary>
    public static class StrandedReport
    {
        public static string ReportPath =>
            Path.Combine(Path.GetTempPath(), "noir-stranded.txt");

        [MenuItem("Noir/Report Stranded People")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[stranded] press Play first - the simulation has to be running.");
                return;
            }

            var host = Object.FindFirstObjectByType<VillageHost>();
            if (host == null || host.Sim == null)
            {
                Debug.LogWarning("[stranded] no VillageHost with a simulation in the scene.");
                return;
            }

            File.WriteAllText(ReportPath, StrandedSurvey.Describe(host));
            Debug.Log($"[stranded] {host.Sim.StrandedCount} stranded, "
                    + $"{host.Sim.Regions.RegionCount} walkable areas -> {ReportPath}");
        }
    }
}
