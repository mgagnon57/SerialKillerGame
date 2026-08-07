using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// What the game did with each ruling, written back where the browser map can read it.
    ///
    /// THE LOOP THIS CLOSES. The map is the source and the game obeys it - but not every ruling
    /// can be obeyed, and until now nothing said so. The survey passes refuse a building that
    /// would stand in a road, one that would overlap a neighbour, and one on ground ruled to be
    /// something that is not a building at all. That is 57 of them, and from the map they are
    /// indistinguishable from the ones that worked: the owner rules a lot, no house appears, and
    /// there is no symptom anywhere.
    ///
    /// So the passes say what they decided, this writes it down, and the map shows it. It is a
    /// REPORT and not content: derived, rewritten on every build, and safe to delete.
    /// </summary>
    public static class SurveyReport
    {
        public const string FileName = "game-verdict.json";

        private static readonly List<string> _rows = new List<string>();
        private static readonly Dictionary<string, int> _tally = new Dictionary<string, int>();

        public static void Clear() { _rows.Clear(); _tally.Clear(); }

        /// <summary>One lot, and what became of it. `what` is a short reason the map can show
        /// beside the lot - keep it a phrase, not a sentence.</summary>
        public static void Say(int parcel, bool built, string what)
        {
            _rows.Add($"{{\"p\":{parcel},\"b\":{(built ? "true" : "false")},"
                    + $"\"w\":\"{Escape(what)}\"}}");
            _tally[what] = _tally.TryGetValue(what, out int n) ? n + 1 : 1;
        }

        /// <summary>
        /// Beside roads-proposed.json, which is where the page already reads its own data from,
        /// so the server needs no new idea of where things live.
        /// </summary>
        public static void Write()
        {
            if (_rows.Count == 0) return;
            var path = System.IO.Path.Combine(ContentLoader.Root, "..", "tools", FileName);

            var sb = new StringBuilder();
            sb.Append("{\"when\":\"").Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
              .Append("\",\"lots\":[").Append(string.Join(",", _rows)).Append("]}");

            try
            {
                System.IO.File.WriteAllText(System.IO.Path.GetFullPath(path), sb.ToString());
                var said = new List<string>();
                foreach (var kv in _tally) said.Add($"{kv.Value} {kv.Key}");
                Debug.Log($"[verdict] {_rows.Count} lots reported back to the browser map - "
                        + string.Join(", ", said));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[verdict] could not write " + FileName + ": " + e.Message);
            }
        }

        private static string Escape(string s) =>
            (s ?? "").Replace("\\", "").Replace("\"", "'").Replace("\n", " ");
    }
}
