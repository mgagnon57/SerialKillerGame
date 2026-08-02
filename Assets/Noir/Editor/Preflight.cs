using UnityEditor;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// Everything worth checking before a commit, in ONE editor launch.
    ///
    /// WHY THIS EXISTS: starting Unity in batch mode costs forty to sixty seconds before a line
    /// of project code runs - asset database, script compile, package resolve - and that is paid
    /// per invocation, not per unit of work. A typical validation pass used to be three separate
    /// launches (compile-check, MapAudit, a render), which is two to three minutes of pure
    /// startup to do about twenty seconds of actual work. Compiling is not even a separate step:
    /// ANY -executeMethod launch has already compiled the project by the time it calls you, so a
    /// dedicated "does it build" run was always the same launch twice.
    ///
    /// Run it headless:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.Preflight.Run
    ///
    /// PlayMode tests still need their own invocation - the test runner owns the update loop and
    /// cannot share a run with -executeMethod - so a full check is two launches, not one. Those
    /// two are independent and can go at the same time only if they are not the same project
    /// directory, which they are, so run them in sequence.
    /// </summary>
    public static class Preflight
    {
        [MenuItem("Noir/Preflight (audit + render, one pass)")]
        public static void Run()
        {
            Debug.Log("[preflight] start - the project has already compiled to get here.");

            // THE AUDIT GOES LAST AND THAT ORDER IS LOAD-BEARING. MapAudit.Run ends with
            // EditorApplication.Exit in batch mode, so anything sequenced after it simply never
            // runs - the first cut of this file put it first and the render was silently skipped,
            // with the log showing a clean VERDICT and no picture. Putting it last also makes its
            // exit code - 0 clean, 1 faults found - the exit code of the whole preflight, which
            // is what a script wants to branch on.
            try
            {
                CityShot.RenderPlanTopDownCore();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[preflight] the plan render threw: " + e);
            }

            Debug.Log("[preflight] render done, handing over to MapAudit, which exits the editor.");
            MapAudit.Run();
        }
    }
}
