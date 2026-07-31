using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// `-quit` schedules Unity's batchmode shutdown on the same editor update pump that
    /// `-runTests`/`-runEditorTests` uses to *start* the test run (see TestStarter.Init in
    /// com.unity.test-framework, which defers to the next EditorApplication.update). `-quit`
    /// wins that race: the editor quits, exit code is 0 ("clean"), and no results file is ever
    /// written. Confirmed by direct reproduction - the log shows "Batchmode quit successfully
    /// invoked" with no test run in between.
    ///
    /// So catch it here, synchronously, before the update pump even starts, and fail loudly
    /// instead of silently reporting a pass that never happened.
    /// </summary>
    public static class TestInvocationGuard
    {
        internal static bool RequestsTestsAndQuit(string[] args)
        {
            bool quits = args.Contains("-quit");
            bool testsRequested = args.Contains("-runTests") || args.Contains("-runEditorTests");
            return quits && testsRequested;
        }

        [InitializeOnLoadMethod]
        private static void Guard()
        {
            if (!Application.isBatchMode) return;

            var args = Environment.GetCommandLineArgs();
            if (!RequestsTestsAndQuit(args)) return;

            Debug.LogError("[TestInvocationGuard] '-quit' was passed together with a test-run "
                + "flag. '-quit' exits the editor on the same update pump the test runner uses "
                + "to start, and wins the race: Unity reports a clean exit (0) with no tests "
                + "run and no results file. Drop '-quit' - the editor exits on its own once the "
                + "run completes.");
            EditorApplication.Exit(1);
        }
    }
}
