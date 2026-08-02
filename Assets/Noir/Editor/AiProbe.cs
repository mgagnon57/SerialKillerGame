using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Noir.Editor
{
    /// <summary>
    /// What Unity's AI packages can actually do in THIS project, on THIS account.
    ///
    /// Read-only and spends nothing. It answers the question that has to be settled before any of
    /// it is worth designing around: the packages being installed is not the same as the account
    /// being signed in, entitled, and holding points - and the difference between those decides
    /// whether a plan is work or a wish.
    ///
    /// ENTIRELY BY REFLECTION, ON PURPOSE. `com.unity.ai.assistant` is 2.17.0-pre.1 - a
    /// pre-release - and almost everything interesting in it (`Account`, `SignInState`,
    /// `PointsBalanceState`) is `internal`. Referencing those assemblies from Noir.Editor's
    /// asmdef would tie this project's compilation to the internals of a beta package, so a point
    /// release could stop the whole project building. Reflection cannot: every lookup here fails
    /// to a printed "not found" line, which is itself the finding.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.AiProbe.Run -logFile &lt;log&gt;
    /// </summary>
    public static class AiProbe
    {
        [MenuItem("Noir/Probe The Unity AI Packages")]
        public static void Run()
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine("[ai] What the Unity AI packages offer this project:");

            // ---- which assemblies actually loaded ----
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetName().Name)
                .Where(n => n.StartsWith("Unity.AI.", StringComparison.Ordinal))
                .OrderBy(n => n)
                .ToArray();

            log.AppendLine($"  assemblies loaded: {loaded.Length}");
            foreach (var name in loaded) log.AppendLine($"     {name}");

            // ---- the entitlement gates ----
            //
            // These are the whole point of the probe. A generator that is installed, agreed to,
            // signed in and out of points is not a capability - it is a bill.
            var account = FindType("Unity.AI.Toolkit.Accounts.Services.Account");
            if (account == null)
            {
                log.AppendLine("  ACCOUNT STATE: not found - the accounts assembly did not load.");
            }
            else
            {
                // EVERY property of every state, rather than a list of names I guessed at. The
                // first cut asked for IsAiGeneratorsEnabled, HasProLicense and
                // apiAccessible.IsAvailable and got "no such property" for all three - they exist
                // in that package, just on other types. Guessing the shape of an internal API in
                // a pre-release and then reporting the misses as findings is worse than useless.
                log.AppendLine("  account state:");
                foreach (var f in account.GetFields(BindingFlags.Public | BindingFlags.Static))
                    ReportAll(log, f);
            }

            // ---- the programmatic door ----
            //
            // AssistantApi.RunHeadless is the one that matters: it is the difference between a
            // chat window a person drives by hand and something this project can put behind a
            // menu item and a headless run, like every other instrument in Assets/Noir/Editor.
            var api = FindType("Unity.AI.Assistant.API.AssistantApi")
                   ?? FindType("Unity.AI.Assistant.Editor.API.AssistantApi")
                   ?? AppDomain.CurrentDomain.GetAssemblies()
                          .SelectMany(SafeTypes)
                          .FirstOrDefault(t => t.Name == "AssistantApi");

            if (api == null)
            {
                log.AppendLine("  ASSISTANT API: no AssistantApi type found.");
            }
            else
            {
                log.AppendLine($"  assistant api: {api.FullName}");
                foreach (var m in api.GetMethods(BindingFlags.Public | BindingFlags.Static)
                                     .Where(m => !m.IsSpecialMethod())
                                     .OrderBy(m => m.Name))
                    log.AppendLine($"     {Signature(m)}");
            }

            // ---- what it can be asked to make, and by whom ----
            //
            // There is NO client-side cost API anywhere in the SDK - checked. Pricing is decided
            // server-side, so the only honest way to learn what a generation costs is to read the
            // points balance before and after one. That is worth knowing before planning around
            // "it's only a few points".
            var constants = FindType("Unity.AI.ModelSelector.Services.Stores.States.ModelConstants");
            if (constants == null)
            {
                log.AppendLine("  MODALITIES: ModelConstants not found.");
            }
            else
            {
                foreach (var group in new[] { "Modalities", "Providers", "Operations" })
                {
                    var t = constants.GetNestedType(group, BindingFlags.Public | BindingFlags.NonPublic);
                    if (t == null) { log.AppendLine($"  {group}: not found"); continue; }

                    var values = t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic)
                                  .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                                  .Select(f => (string)f.GetRawConstantValue())
                                  .Where(v => v != "None")
                                  .ToArray();
                    log.AppendLine($"  {group.ToLowerInvariant()}: {string.Join(", ", values)}");
                }
            }

            log.AppendLine("  NOTE: no cost/pricing API exists client-side. The only measure of "
                         + "what a generation costs is the points balance before and after one.");

            Debug.Log(log.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>Every readable property of one state object, whatever it turns out to have.</summary>
        private static void ReportAll(System.Text.StringBuilder log, FieldInfo field)
        {
            object state;
            try { state = field.GetValue(null); }
            catch (Exception e) { log.AppendLine($"     {field.Name}: threw {e.GetType().Name}"); return; }

            if (state == null) { log.AppendLine($"     {field.Name}: null"); return; }

            var props = state.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .OrderBy(p => p.Name);

            foreach (var p in props)
            {
                object v;
                try { v = p.GetValue(state); }
                catch (Exception e) { v = $"<threw {e.GetType().Name}>"; }
                log.AppendLine($"     {field.Name}.{p.Name} = {Redacted(p.Name, v)}");
            }
        }

        /// <summary>
        /// Names that must never reach the log.
        ///
        /// THIS IS NOT PRECAUTIONARY. Unity.AI.Toolkit.Accounts carries `public string
        /// accessToken`, `organizationKey` and `userId`, and this probe walks every public
        /// property of every account state and then recurses into whatever it finds. In batch
        /// mode they come back null, because the session never resolves - which is exactly the
        /// trap: the probe looks harmless in the only mode it was tested in, and the whole point
        /// of running it in an INTERACTIVE editor is that the session resolves and those fields
        /// fill in. A Unity Console is copied and pasted freely.
        /// </summary>
        private static readonly string[] Secrets =
        {
            "token", "secret", "key", "credential", "bearer", "password", "passwd",
            "jwt", "cookie", "session", "auth", "email", "userid", "user_id", "guid",
        };

        private static bool IsSecret(string name)
        {
            var n = name.ToLowerInvariant();
            foreach (var s in Secrets) if (n.Contains(s)) return true;
            return false;
        }

        /// <summary>
        /// What a value is, without saying what it holds. The TYPE and LENGTH of a token are the
        /// useful part for a probe - "is it there and does it look populated" - and the value
        /// itself never is.
        ///
        /// Long unbroken strings are masked whatever they are called, because the next release of
        /// a pre-release package is free to invent a field name this list has never heard of, and
        /// a credential does not stop being one because it was called something new.
        /// </summary>
        private static string Redacted(string name, object v)
        {
            if (v == null) return "null";
            if (IsSecret(name))
                return v is string s0
                    ? $"<redacted {v.GetType().Name}, {s0.Length} chars>"
                    : $"<redacted {v.GetType().Name}>";

            if (v is string s && s.Length > 24 && !s.Contains(' '))
                return $"<redacted long string, {s.Length} chars>";

            return Describe(v);
        }

        private static void Report(System.Text.StringBuilder log, Type owner, string field, string member)
        {
            try
            {
                var f = owner.GetField(field, BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                if (f == null) { log.AppendLine($"     {field}.{member}: no such field"); return; }

                var state = f.GetValue(null);
                if (state == null) { log.AppendLine($"     {field}.{member}: null"); return; }

                var p = state.GetType().GetProperty(member,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (p == null) { log.AppendLine($"     {field}.{member}: no such property"); return; }

                log.AppendLine($"     {field}.{member} = {Describe(p.GetValue(state))}");
            }
            catch (Exception e)
            {
                log.AppendLine($"     {field}.{member}: threw {e.GetType().Name} - {e.Message}");
            }
        }

        /// <summary>Points come back as a record rather than a number, and which fields it has is
        /// exactly the sort of thing that moves between pre-releases - so it is printed by
        /// walking whatever it turns out to have.</summary>
        private static string Describe(object v)
        {
            if (v == null) return "null";
            var t = v.GetType();
            if (t.IsPrimitive || v is string) return v.ToString();

            // Through Redacted too - this is the recursion that would otherwise carry an
            // accessToken out of a nested record after the top-level check had passed it.
            var parts = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.GetIndexParameters().Length == 0)
                         .Select(p =>
                         {
                             try { return $"{p.Name}={Redacted(p.Name, p.GetValue(v))}"; }
                             catch { return $"{p.Name}=?"; }
                         })
                         .ToArray();
            return parts.Length == 0 ? v.ToString() : "{ " + string.Join(", ", parts) + " }";
        }

        private static string Signature(MethodInfo m) =>
            $"{Pretty(m.ReturnType)} {m.Name}("
          + string.Join(", ", m.GetParameters().Select(p => $"{Pretty(p.ParameterType)} {p.Name}"))
          + ")";

        private static string Pretty(Type t)
        {
            if (!t.IsGenericType) return t.Name;
            return t.Name.Split('`')[0] + "<" + string.Join(", ", t.GetGenericArguments().Select(Pretty)) + ">";
        }

        private static Type FindType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => { try { return a.GetType(fullName); } catch { return null; } })
                .FirstOrDefault(t => t != null);

        private static Type[] SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); }
            catch { return Array.Empty<Type>(); }
        }

        private static bool IsSpecialMethod(this MethodInfo m) =>
            m.IsSpecialName || m.DeclaringType == typeof(object);
    }
}
