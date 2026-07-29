using System;
using UnityEditor;
using UnityEngine;
using Noir.Core.People;
using Noir.Core.Sim;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Builds the entire village outside of play mode and reports what came out.
    ///
    /// The point is to catch the errors that compiling cannot: a null reference in mesh
    /// generation, a missing shader, an index off the end of an array. Those only appear when
    /// the code actually runs, and "press Play and see" is a slow and unpleasant way to find
    /// them - especially for someone who is not going to be at the keyboard for a while.
    ///
    /// Run headlessly:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.SmokeTest.Run
    /// </summary>
    public static class SmokeTest
    {
        [MenuItem("Noir/Smoke Test")]
        public static void Run()
        {
            int failures = 0;
            GameObject root = null;

            try
            {
                Log("--- Ashcombe smoke test ---");

                if (!ContentLoader.Exists)
                    throw new Exception($"content not found at {ContentLoader.Root}");

                // Before the first `place` line is read. Core cannot open a file, so every entry
                // point has to hand it the table itself - and this one does not go through
                // VillageHost, which is the only place that used to do it.
                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));

                var layout = VillageParser.Parse(ContentLoader.Read("village.txt"));
                var world = WorldBuilder.Build(layout, VillageHost.Seed);
                Log($"world      {world.Width}x{world.Height}, {world.PlaceCount} places, "
                  + $"{world.RoomCount} rooms, {world.FurnitureCount} furniture, {world.PropCount} props");

                var report = WorldValidator.Validate(world);
                foreach (var e in report.Errors) { LogError("layout: " + e); failures++; }
                Log($"layout     {(report.IsValid ? "valid" : "INVALID")}, "
                  + $"{report.WalkableTiles} walkable, {report.RegionCount} region(s)");

                var names = NameTable.Parse(ContentLoader.Read("names.txt"));
                var particulars = ParticularsTable.Parse(ContentLoader.Read("particulars.txt"));
                var people = PopulationGenerator.Generate(world, names, particulars, VillageHost.Seed);
                Log($"people     {people.Count} in {people.HouseholdCount} households, "
                  + $"{people.WorkingCount} in work");

                var sim = new Simulation(world, people, VillageHost.Seed, 6 * 60);
                sim.Tick(Noir.Core.Contracts.GameClock.TicksPerMinute * 120);
                Log($"sim        ran two hours, clock at {sim.Clock}");

                // The part that only breaks at runtime: meshes, materials, shaders, primitives.
                root = new GameObject("SmokeTestVillage");
                var village = VillageMesh.Build(world, root.transform);
                Log($"render     built {CountRenderers(root)} renderers");

                // Every building must resolve to a massing grammar and survive being asked for
                // its profile. These are runtime failures - a null row, a missing registry
                // entry, bad index arithmetic in tower geometry - and compiling catches none of
                // them. This is the assertion that makes `massing` safe to extend.
                int shaped = 0;
                foreach (var place in world.AllPlaces)
                {
                    if (!PlaceKindTable.Current.Row(place.Kind).IsBuilding) continue;
                    var m = MassingGrammars.Of(place);
                    if (m.Eaves <= 0f)
                    {
                        LogError($"massing: '{place.Name}' has eaves {m.Eaves}");
                        failures++;
                    }
                    shaped++;
                }
                Log($"massing    {shaped} buildings shaped");

                // Both ways round, because they are different code paths and the second one is
                // the one that has to put back exactly what the first took away. A wireframe
                // that cannot be switched off is worse than no wireframe.
                var xray = XRay.Create(world, village);
                int lit = CountEnabledRenderers(root);

                xray.Set(true);
                int stripped = CountEnabledRenderers(root);

                xray.Set(false);
                int restored = CountEnabledRenderers(root);

                if (stripped >= lit) { LogError($"x-ray hid nothing: {lit} -> {stripped}"); failures++; }
                if (restored != lit) { LogError($"x-ray did not restore: {lit} -> {restored}"); failures++; }
                Log($"x-ray      {lit} renderers -> {stripped} stripped -> {restored} restored");
            }
            catch (Exception ex)
            {
                LogError("SMOKE TEST FAILED: " + ex);
                failures++;
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }

            Log(failures == 0 ? "--- SMOKE TEST PASSED ---" : $"--- SMOKE TEST FAILED ({failures}) ---");

            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        private static int CountRenderers(GameObject root) =>
            root.GetComponentsInChildren<Renderer>(true).Length;

        private static int CountEnabledRenderers(GameObject root)
        {
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.enabled && r.gameObject.activeInHierarchy) n++;
            return n;
        }

        private static void Log(string message) => Debug.Log("[smoke] " + message);
        private static void LogError(string message) => Debug.LogError("[smoke] " + message);
    }
}
