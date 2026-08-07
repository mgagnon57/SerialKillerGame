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
                Log("--- smoke test ---");

                if (!ContentLoader.Exists)
                    throw new Exception($"content not found at {ContentLoader.Root}");

                // Before the first `place` line is read. Core cannot open a file, so every entry
                // point has to hand it the table itself - and this one does not go through
                // VillageHost, which is the only place that used to do it.
                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));

                // THE TOWN THE GAME BUILDS, not the fixture. This read village.txt - the small
                // retired village the unit tests use - so the one check that claims to build
                // "the entire village and report what came out" was reporting on a town nobody
                // plays, and would have passed happily while Rossville was broken.
                var layout = VillageParser.Parse(ContentLoader.Read(VillageHost.MapFile));
                SurveyReport.Clear();
                SurveyRoads.Apply(layout);
                RuledAway.Apply(layout);
                SeatOnSurvey.Apply(layout);
                FillFromSurvey.Apply(layout);
                SurveyReport.Write();

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

                // EVERYTHING THE BROWSER MAP CAN SAY, THE GAME CAN HEAR.
                //
                // The map is the owner's memory of 1991 and it is the source: the tax roll starts
                // in 2007 and the imagery is 2016, so where they disagree the person who was there
                // wins. The failure worth guarding is silent - the map learns to write a ruling,
                // the game never learns to read it, nothing breaks, nothing is logged, and a
                // hundred rulings are quietly ignored. So every line of every authored file is
                // checked against the vocabulary the loaders actually implement.
                failures += CheckRulingsAreHeard();

                // EVERY LAYER ON. A smoke test exists to run the code that only breaks when it
                // runs, so building a town with the scenery switched off tests the half nobody
                // was worried about. The massing is built lazily now - see Layers.RegisterLazy -
                // so with it off VillageMesh puts up the ground and nothing else, and the x-ray
                // check below then finds nothing to hide and fails saying so.
                foreach (var kind in Layers.All) Layers.Set(kind, true);

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

        /// <summary>
        /// Every line of every authored file, against the vocabulary the game implements.
        ///
        /// Reports what it FOUND as well as what it could not read, because a loader that reads
        /// the file and finds nothing in it is the same silence seen from the other end - and has
        /// happened here: parcel-1991.txt carried 359 rulings for a day with no reader at all.
        /// </summary>
        private static int CheckRulingsAreHeard()
        {
            int bad = 0;

            bad += Vet("parcel-1991.txt", Rulings.KnownVerbs, line =>
            {
                // parcel <id> <verb> <rest>
                var p = line.Split(new[] { ' ' }, 4);
                return p.Length >= 4 && p[0] == "parcel" ? p[2] : null;
            });

            bad += Vet(RoadRulings.FileName, RoadRulings.KnownVerbs, line =>
            {
                // road <name> walk <side>, or block <road> <from> <to> walk <side>
                var p = line.Split(' ');
                if (p.Length == 4 && p[2] == "walk") return p[0];
                if (p.Length == 6 && p[4] == "walk") return p[0];
                return p.Length > 0 ? p[0] : null;
            });

            Log($"rulings    {Rulings.Count} lots, {RoadRulings.Count} roads and blocks, "
              + $"{RoadRulings.Blocks.Count} blocks known");

            if (Rulings.Count == 0)
            {
                LogError("rulings: parcel-1991.txt read as empty - the game is ignoring the "
                       + "browser map. That file is the owner's memory and cannot be rebuilt.");
                bad++;
            }
            return bad;
        }

        /// <summary>One authored file, checked line by line. `verb` pulls the word that decides
        /// what the line means; a null answer is a line whose shape nothing recognises.</summary>
        private static int Vet(string file, string[] known, System.Func<string, string> verb)
        {
            string text;
            try { text = ContentLoader.Read(file); }
            catch { return 0; }                       // an absent authored file is not an error

            int bad = 0;
            var seen = new System.Collections.Generic.HashSet<string>();
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                var word = verb(line);
                if (word != null && System.Array.IndexOf(known, word) >= 0) continue;
                if (!seen.Add(word ?? "(unreadable)")) continue;   // one complaint per verb

                LogError($"rulings: {file} line {i + 1} says '{word ?? line}', which no loader in "
                       + "the game understands. The browser map is the source - if it can write "
                       + "this, something here has to read it.");
                bad++;
            }
            return bad;
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
