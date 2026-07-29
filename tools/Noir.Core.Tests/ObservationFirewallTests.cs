using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Noir.Core.Observation;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The investigation layer must be PROVABLY unable to read ground truth. In Unity that is a
    /// build failure: Noir.Core.Observation.asmdef references Noir.Core.Contracts and nothing
    /// else, so a type in that assembly cannot name an AgentState, a Citizen or a WorldModel.
    ///
    /// This fixture is the same rule enforced where the asmdef cannot reach. Two gaps to close:
    ///
    ///  1. The parallel netstandard build compiles all of Assets/Noir/Core into ONE assembly, so
    ///     `dotnet build` alone would happily let Observation reach into Sim. The reflection walk
    ///     below closes that gap by treating the namespace as the boundary, which is exact here
    ///     because every asmdef sets rootNamespace to its own name.
    ///  2. An asmdef is one line of JSON away from being "fixed" by somebody in a hurry who just
    ///     wants their code to compile. The second test pins the reference list so that widening
    ///     it is a deliberate act with a red test attached, not a quiet convenience.
    ///
    /// The walk is transitive on purpose. The realistic failure is not `Sighting.Culprit` — it is
    /// a convenience field three types deep, added years from now by someone who never read any
    /// of this.
    /// </summary>
    [TestFixture]
    public class ObservationFirewallTests
    {
        private const string ObservationNamespace = "Noir.Core.Observation";

        /// <summary>The assemblies that hold what actually happened. Observation may not see them.</summary>
        private static readonly string[] GroundTruthNamespaces =
        {
            "Noir.Core.Sim",        // AgentState and everything the sim knows
            "Noir.Core.World",      // WorldModel, the grid, the places
            "Noir.Core.People",     // Citizen, Household, DayPlan
        };

        /// <summary>
        /// Contracts is referenced, so these ARE visible from Observation — and they are the most
        /// likely breach by far. Nobody will put a whole Citizen in a Sighting; somebody will put
        /// a CitizenId in one, because it is small and it compiles. An id that names a person is
        /// ground truth in four bytes.
        /// </summary>
        private static readonly string[] ForbiddenIdentityTypes =
        {
            "Noir.Core.Contracts.CitizenId",
            "Noir.Core.Contracts.HouseholdId",

            // The stable key is MORE dangerous than the index, not less. It is the thing the
            // rest of the game is now told to use for anything persisted or observed, so it is
            // exactly what somebody will reach for when a Sighting needs to remember who — and
            // unlike an array slot it stays right across a save, a reload and a death.
            "Noir.Core.Contracts.CitizenKey",

            // A place is not a person, and this one is here for a different reason. The 2:1
            // instrument's whole defence against becoming a shopping list is that its log holds
            // no place: a hospital cannot be tagged, named or located, so it can only reach the
            // useful column by making somebody predictably alone or their house predictably
            // empty. A PlaceId in an Observed would undo that in four bytes, and it is exactly
            // what somebody will reach for the first time a report wants to say WHERE.
            "Noir.Core.Contracts.PlaceId",
        };

        [Test]
        public void ObservationPublicSurfaceCannotReachGroundTruth()
        {
            List<Type> roots = ObservationTypes();
            Assert.That(roots, Is.Not.Empty,
                "Found no public types in " + ObservationNamespace + ". Either the assembly moved " +
                "or this test is silently passing on nothing, which is worse than failing.");

            // How each type was first reached, so a violation can be reported as a path rather
            // than as a bare type name. Breadth-first, so the reported path is the shortest one.
            var arrivedBy = new Dictionary<Type, Step>();
            var queue = new Queue<Type>();
            foreach (Type root in roots)
            {
                if (arrivedBy.ContainsKey(root)) continue;
                arrivedBy[root] = new Step(null, null);
                queue.Enqueue(root);
            }

            var violations = new List<string>();

            while (queue.Count > 0)
            {
                Type type = queue.Dequeue();

                string reason = ForbiddenReason(type);
                if (reason != null)
                {
                    violations.Add(DescribePath(type, reason, arrivedBy));
                    continue;   // no point walking into it
                }

                // Check every type we reach, but only walk INTO our own — otherwise the walk
                // wanders off through System.String and never comes back. A forbidden type
                // hiding inside a framework generic is still caught, because generic arguments
                // and array elements are unwrapped before this decision is made.
                if (!IsOurs(type)) continue;

                foreach (KeyValuePair<Type, string> edge in OutgoingEdges(type))
                {
                    if (arrivedBy.ContainsKey(edge.Key)) continue;
                    arrivedBy[edge.Key] = new Step(type, edge.Value);
                    queue.Enqueue(edge.Key);
                }
            }

            if (violations.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine("THE OBSERVATION FIREWALL HAS BEEN BREACHED.");
            sb.AppendLine();
            sb.AppendLine("Noir.Core.Observation may describe what somebody could have noticed, and nothing else.");
            sb.AppendLine("Its public surface must never reach what actually happened: AgentState, Citizen,");
            sb.AppendLine("WorldModel, anything in Noir.Core.Sim / World / People, or an id that names a person.");
            sb.AppendLine();
            sb.AppendLine(violations.Count == 1 ? "One path reaches ground truth:" : violations.Count + " paths reach ground truth:");
            sb.AppendLine();
            foreach (string v in violations) sb.AppendLine(v);
            sb.AppendLine("DO NOT FIX THIS BY ADDING A REFERENCE to");
            sb.AppendLine("Assets/Noir/Core/Observation/Noir.Core.Observation.asmdef.");
            sb.AppendLine();
            sb.AppendLine("That short reference list is the only thing making the investigation honest: the");
            sb.AppendLine("detective is supposed to reason from what COULD have been observed, never from what");
            sb.AppendLine("actually happened. Widen it and the game's central mechanic becomes decorative.");
            sb.AppendLine("Carry a coarse, fallible description instead, and resolve it to a person outside this");
            sb.AppendLine("assembly, where guessing wrong is allowed.");

            Assert.Fail(sb.ToString());
        }

        [Test]
        public void ObservationAsmdefReferencesContractsAndNothingElse()
        {
            string path = Path.Combine(RepoRoot(), "Assets", "Noir", "Core", "Observation",
                                       "Noir.Core.Observation.asmdef");
            Assert.That(File.Exists(path), Is.True, "Missing asmdef at " + path);

            string json = File.ReadAllText(path);

            var refs = new List<string>();
            Match block = Regex.Match(json, "\"references\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            Assert.That(block.Success, Is.True, "Could not find a \"references\" array in " + path);
            foreach (Match m in Regex.Matches(block.Groups[1].Value, "\"([^\"]*)\""))
                refs.Add(m.Groups[1].Value);

            Assert.That(refs, Is.EqualTo(new[] { "Noir.Core.Contracts" }),
                "Noir.Core.Observation.asmdef now references [" + string.Join(", ", refs) + "].\n\n" +
                "It must reference Noir.Core.Contracts and nothing else. That list is not a build\n" +
                "detail — it is the proof that the observation layer cannot read ground truth, because\n" +
                "the compiler cannot show it AgentState, Citizen or WorldModel. If code in Observation\n" +
                "will not compile without a new reference, the code is wrong, not the reference list:\n" +
                "describe what a witness could have noticed and resolve it elsewhere.\n\n" +
                "If a genuinely new observation-side assembly has to be referenced, change it here too,\n" +
                "deliberately, and say why in the commit.");

            Assert.That(Regex.IsMatch(json, "\"noEngineReferences\"\\s*:\\s*true"), Is.True,
                "Noir.Core.Observation.asmdef must keep \"noEngineReferences\": true, like every other\n" +
                "Core assembly. Core runs headless under dotnet test; a UnityEngine reference ends that.");
        }

        // ---- the walk -------------------------------------------------------------------------

        /// <summary>How one type was reached from another. Root types have a null parent.</summary>
        private readonly struct Step
        {
            public readonly Type From;
            public readonly string Via;
            public Step(Type from, string via) { From = from; Via = via; }
        }

        private static List<Type> ObservationTypes()
        {
            var found = new List<Type>();
            foreach (Type t in typeof(Sighting).Assembly.GetExportedTypes())
                if (IsObservation(t)) found.Add(t);
            return found;
        }

        private static bool IsObservation(Type t) => InNamespace(t, ObservationNamespace);

        private static bool IsOurs(Type t) => t.FullName != null && t.FullName.StartsWith("Noir.", StringComparison.Ordinal);

        private static bool InNamespace(Type t, string ns) =>
            t.Namespace != null && (t.Namespace == ns || t.Namespace.StartsWith(ns + ".", StringComparison.Ordinal));

        /// <summary>Null if the type is allowed here; otherwise why it is not.</summary>
        private static string ForbiddenReason(Type t)
        {
            foreach (string ns in GroundTruthNamespaces)
                if (InNamespace(t, ns))
                    return "lives in " + ns + " — that is what actually happened, not what was seen";

            foreach (string name in ForbiddenIdentityTypes)
                if (t.FullName == name)
                    return "names a person outright; a witness cannot";

            return null;
        }

        /// <summary>
        /// Every type reachable in one hop from this one, with a human description of the hop.
        /// Fields and properties are the stated rule; base types, interfaces, method signatures
        /// and constructor parameters are included because they leak just as effectively.
        /// </summary>
        private static Dictionary<Type, string> OutgoingEdges(Type type)
        {
            var edges = new Dictionary<Type, string>();
            const BindingFlags Surface = BindingFlags.Public | BindingFlags.Instance |
                                         BindingFlags.Static | BindingFlags.DeclaredOnly;

            if (type.BaseType != null && type.BaseType != typeof(object) &&
                type.BaseType != typeof(ValueType) && type.BaseType != typeof(Enum))
                Add(edges, type.BaseType, "derives from");

            foreach (Type i in type.GetInterfaces())
                Add(edges, i, "implements");

            foreach (FieldInfo f in type.GetFields(Surface))
                Add(edges, f.FieldType, "field '" + f.Name + "'");

            foreach (PropertyInfo p in type.GetProperties(Surface))
                Add(edges, p.PropertyType, "property '" + p.Name + "'");

            foreach (MethodInfo m in type.GetMethods(Surface))
            {
                Add(edges, m.ReturnType, "return value of '" + m.Name + "()'");
                foreach (ParameterInfo p in m.GetParameters())
                    Add(edges, p.ParameterType, "parameter '" + p.Name + "' of '" + m.Name + "()'");
            }

            foreach (ConstructorInfo c in type.GetConstructors(Surface))
                foreach (ParameterInfo p in c.GetParameters())
                    Add(edges, p.ParameterType, "constructor parameter '" + p.Name + "'");

            return edges;
        }

        /// <summary>Unwraps arrays and generics so a Citizen cannot hide inside a List&lt;&gt;.</summary>
        private static void Add(Dictionary<Type, string> edges, Type t, string via)
        {
            foreach (Type leaf in Unwrap(t))
                if (leaf != null && !edges.ContainsKey(leaf))
                    edges[leaf] = via;
        }

        private static IEnumerable<Type> Unwrap(Type t)
        {
            if (t == null) yield break;
            while (t.IsArray || t.IsByRef || t.IsPointer)
            {
                t = t.GetElementType();
                if (t == null) yield break;
            }
            if (t.IsGenericParameter) yield break;

            if (t.IsGenericType)
            {
                yield return t.GetGenericTypeDefinition();
                foreach (Type arg in t.GetGenericArguments())
                    foreach (Type leaf in Unwrap(arg))
                        yield return leaf;
            }
            else
            {
                yield return t;
            }
        }

        /// <summary>
        /// Renders the route from a public Observation type down to the forbidden one. Whoever
        /// trips this test will not have read a word of the design, so the path has to be enough
        /// on its own to show them which member to delete.
        /// </summary>
        private static string DescribePath(Type offender, string reason, Dictionary<Type, Step> arrivedBy)
        {
            var chain = new List<string>();
            Type current = offender;
            while (true)
            {
                Step step = arrivedBy[current];
                if (step.From == null) { chain.Add("  " + current.FullName); break; }
                chain.Add("    -> " + step.Via + " : " + current.FullName);
                current = step.From;
            }
            chain.Reverse();

            var sb = new StringBuilder();
            foreach (string line in chain) sb.AppendLine(line);
            sb.AppendLine("       ^ FORBIDDEN: " + offender.FullName + " " + reason);
            return sb.ToString();
        }

        // ---- finding the repo -------------------------------------------------------------------

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "Noir", "Core", "Observation")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not find Assets/Noir/Core/Observation above " + AppContext.BaseDirectory);
        }
    }
}
