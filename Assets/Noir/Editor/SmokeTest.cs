using System;
using UnityEditor;
using UnityEngine;
using Noir.Core.People;
using Noir.Core.Sim;
using Noir.Core.World;
using Noir.Unity;
using Noir.Core.Survey;

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

            System.Collections.Generic.Dictionary<Layers.Kind, bool> wasLayers = null;

            try
            {
                Log("--- smoke test ---");

                if (!ContentLoader.Exists)
                    throw new Exception($"content not found at {ContentLoader.Root}");

                // THE TOWN THE GAME BUILDS, not the fixture. This read village.txt - the small
                // retired village the unit tests use - so the one check that claims to build
                // "the entire village and report what came out" was reporting on a town nobody
                // plays, and would have passed happily while Rossville was broken. TownPipeline
                // now guarantees it: the kind table, VillageHost.MapFile and every survey pass in
                // the order they have to run, the same way Play gets them.
                var built = TownPipeline.Build();
                var world = built.World;
                Log($"world      {world.Width}x{world.Height}, {world.PlaceCount} places, "
                  + $"{world.RoomCount} rooms, {world.FurnitureCount} furniture, {world.PropCount} props");
                ReportHouseRooms(world);

                // The errors are already on the log from the pipeline; what is added here is the
                // count, because a smoke test that does not fail on them is not a test.
                var report = built.Validation;
                failures += report.Errors.Count;
                Log($"layout     {(report.IsValid ? "valid" : "INVALID")}, "
                  + $"{report.WalkableTiles} walkable, {report.RegionCount} region(s)");

                var names = NameTable.Parse(ContentLoader.Read("names.txt"));
                var particulars = ParticularsTable.Parse(ContentLoader.Read("particulars.txt"));
                var people = PopulationGenerator.Generate(world, names, particulars, VillageHost.Seed);
                Log($"people     {people.Count} in {people.HouseholdCount} households, "
                  + $"{people.WorkingCount} in work");

                var sim = new Simulation(world, people, VillageHost.Seed, 6 * 60);
                var ticked = System.Diagnostics.Stopwatch.StartNew();
                sim.Tick(Noir.Core.Contracts.GameClock.TicksPerMinute * 120);
                ticked.Stop();
                Log($"sim        ran two hours, clock at {sim.Clock}");

                // HOW THE SEARCH IS COPING, WITH ITS DENOMINATOR. Added 2026-08-11: pricing
                // private ground made every journey dearer, and the one PlayMode test that
                // busy-loops on Sim.Tick went from 293 s to a 900 s timeout. A gave-up count is
                // unreadable alone - see Simulation.GaveUpTotal's own header - so it is printed
                // beside what it succeeded at and beside the wall clock of the two hours, which
                // is the number that actually moved.
                long asked = sim.PathsFoundTotal + sim.GaveUpTotal + sim.NoRouteTotal;
                Log($"paths      {sim.PathsFoundTotal:N0} found, {sim.GaveUpTotal:N0} gave up at the "
                  + $"node cap, {sim.NoRouteTotal:N0} refused outright, of {asked:N0} asked "
                  + $"— two sim hours in {ticked.ElapsedMilliseconds:N0} ms");

                // WHICH journeys were abandoned, not just how many. A ceiling below what honest
                // long walks need is arithmetic; a SHORT walk being abandoned is a broken search,
                // and raising the ceiling would bury it. The two look identical in a count.
                if (sim.GaveUpTotal > 0)
                    Log($"gave up    nearest {sim.GaveUpNearest:N0} tiles, mean "
                      + $"{sim.GaveUpMeanDistance:N0}, farthest {sim.GaveUpFarthest:N0}; "
                      + $"{sim.GaveUpUnder200:N0} of them under 200 tiles. "
                      + $"Shortest was ({sim.GaveUpShortestFrom.X},{sim.GaveUpShortestFrom.Y}) -> "
                      + $"({sim.GaveUpShortestTo.X},{sim.GaveUpShortestTo.Y}). "
                      + $"Worst SUCCESSFUL search {sim.WorstNodesFound:N0} nodes against a "
                      + $"ceiling of {Noir.Core.Sim.Pathfinder.HardNodeCeiling:N0}.");
                // NoJourneyIsRefusedThatCanBeWalked.
                //
                // A GIVE-UP IS A REACHABLE DESTINATION THE SEARCH REFUSED, so the honest target
                // is zero and it measured zero the moment the ceiling was sized correctly. It is
                // a hard failure and not a warning because it is now a real regression signal
                // rather than a standing fault: on 2026-08-11 this read 171 of 781 against a
                // stale 100,000 ceiling measured on the pre-survey town, and raising the ceiling
                // to 300,000 took it to nothing.
                //
                // 1% and not 0, only because a search is allowed to be refused for being
                // genuinely pathological - that is what the guard is for. Anything approaching
                // this bar means the walkable grid has moved again and the ceiling needs
                // re-measuring against it. See Pathfinder.HardNodeCeiling for how.
                if (asked > 0 && sim.GaveUpTotal * 100 > asked)
                {
                    LogError($"paths: {sim.GaveUpTotal:N0} of {asked:N0} searches hit the node "
                           + $"cap - over 1%. These are REACHABLE places people could not set off "
                           + $"for. The worst search that DID succeed took {sim.WorstNodesFound:N0} "
                           + $"nodes against a ceiling of {Noir.Core.Sim.Pathfinder.HardNodeCeiling:N0}"
                           + " - if those are close, the ceiling is stale again.");
                    failures++;
                }

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
                //
                // AND PUT BACK IN THE `finally`, because `Layers.Set` writes PlayerPrefs whenever
                // there is a person - and `Noir > Smoke Test` is a menu item, so there is. This
                // turned every layer on and restored nothing, so running the smoke test once cost
                // the owner whatever he had switched off. Same mechanism as the 2026-08-07
                // incident, on the editor side of the batch-mode guard.
                wasLayers = Layers.Snapshot();
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
                Layers.Restore(wasLayers);

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

            bad += Vet(Placements.FileName, Placements.KnownVerbs, line =>
            {
                // building <parcel> <index> move <dx> <dy> turn <deg>
                var p = line.Split(' ');
                return p.Length > 0 ? p[0] : null;
            });

            // COUNTED, AND COUNTED FROM THE GAME'S SIDE. The overlay passing its vocabulary check
            // only proves the file is spellable; this is the number the game actually loaded, and
            // it is the difference between "the owner moved a house" and "the owner moved a house
            // and the town was built with it moved". The same silence parcel-1991.txt sat in for a
            // day - a file with rulings in it and no reader - would look exactly like a zero here.
            Log($"rulings    {Rulings.Count} lots, {RoadRulings.Count} roads and blocks, "
              + $"{RoadRulings.Blocks.Count} blocks known, "
              + $"{Placements.Count} house{(Placements.Count == 1 ? "" : "s")} moved");

            // HOW MUCH OF THIS TOWN IS ACTUALLY 1991, and how much is 2016 standing in for it.
            //
            // Every measured source here postdates the target by fifteen years or more: the tax
            // roll starts in 2007, the footprints are 2016 imagery, the road geometry is current
            // OSM. They are a FALLBACK - what to draw until somebody who was there says otherwise
            // - and never a source of truth about 1991. The failure they invite is silent, because
            // a footprint nobody has ruled on looks exactly like one that has been confirmed: a
            // shop built decades later sits downtown and reads as though it belonged.
            //
            // So the number is printed on every run. It is the only measure of how close this town
            // is to the year it claims to be.
            int checkedLots = 0, unchecked_ = 0;
            foreach (var kv in ParcelBuildings.All)
            {
                if (Rulings.For(kv.Key).Was != Rulings.Stood.Unruled) checkedLots += kv.Value.Count;
                else unchecked_ += kv.Value.Count;
            }
            int total = checkedLots + unchecked_;
            Log($"1991       {checkedLots} of {total} buildings confirmed for 1991; "
              + $"{unchecked_} still standing on post-2000 sources "
              + $"({(total == 0 ? 0 : 100 * unchecked_ / total)}% unchecked)");

            if (Rulings.Count == 0)
            {
                LogError("rulings: parcel-1991.txt read as empty - the game is ignoring the "
                       + "browser map. That file is the owner's memory and cannot be rebuilt.");
                bad++;
            }

            bad += CheckKindsTableKeysAreAnswered();
            return bad;
        }

        /// <summary>
        /// HOW MANY OF ROSSVILLE'S HOUSES HAVE A BATHROOM IN THEM, printed rather than assumed.
        ///
        /// `DomesticBsp` gave a house a bathroom only at four rooms or more, reasoning in the code
        /// that "a three-room cottage in 1979 has the privy outside, which is a fact rather than an
        /// oversight". It is a fact about rural England in 1979. Rossville has run its own water
        /// and sewer continuously since 1898 - kinds.txt says so, citing ROSSVILLE-HISTORY.md - so
        /// an occupied house here in 1991 has a bathroom in it, and the three-room bar was carrying
        /// a retired village's plumbing into an Illinois town.
        ///
        /// The number is printed on every run because rooms are what the whole simulation walks
        /// around in: DayPlan and the pathfinder both read them, so a change in this count is a
        /// change in what the people do, and it should never be discovered by surprise.
        /// </summary>
        private static void ReportHouseRooms(WorldModel world)
        {
            int houses = 0, withBath = 0;
            var byRooms = new System.Collections.Generic.SortedDictionary<int, int>();

            foreach (var place in world.AllPlaces)
            {
                // IsHome, so the seven apartment places are in the count. Written against the
                // enum member first, which is the exact fault this same session found in
                // Eyewitness, EncounterReport, VocabReport, RoofBuilder and Materials3D - five
                // places asking `Kind == PlaceKind.Dwelling` about a town whose kinds.txt declares
                // `apartment / home yes` and whose enum has never heard of apartment.
                if (!PlaceKindTable.Current.Row(place.Kind).IsHome) continue;
                houses++;

                var rooms = world.RoomsIn(place.Id);
                int n = rooms.Count;
                byRooms.TryGetValue(n, out int had);
                byRooms[n] = had + 1;

                foreach (var id in rooms)
                    if (world.GetRoom(id).Kind == RoomKind.Bathroom) { withBath++; break; }
            }

            var shape = new System.Collections.Generic.List<string>();
            foreach (var kv in byRooms) shape.Add($"{kv.Key}:{kv.Value}");

            Log($"houses     {houses} dwellings, {withBath} with a bathroom "
              + $"({(houses == 0 ? 0 : 100 * withBath / houses)}%); rooms per house  "
              + string.Join(" ", shape));
        }

        /// <summary>
        /// THE DIFF CLAUDE.MD ASKS FOR AND NOTHING IN THE TREE DID: every `frontage` and `massing`
        /// value `Content/kinds.txt` declares, against the keys the renderers answer to.
        ///
        /// "A value nothing answers to does NOT throw, it falls through to a default and the
        /// building just looks wrong." That has now happened twice in this one column - `factory`
        /// declaring `frontage mill` and getting a plain plank, then `bank` and `icecream`
        /// declaring `shopfront`, which no arm had ever heard of. Both were found by eye, months
        /// apart.
        ///
        /// It lives in the Editor assembly because Frontage and MassingGrammars are in Noir.Unity,
        /// which Core cannot see - and it is deliberately NOT a load-time throw in
        /// PlaceKindTable: one bad word should cost one wrong sign, not the whole game.
        /// </summary>
        private static int CheckKindsTableKeysAreAnswered()
        {
            int bad = 0;
            var frontages = new System.Collections.Generic.SortedDictionary<string, string>();
            var massings = new System.Collections.Generic.SortedDictionary<string, string>();

            var table = PlaceKindTable.Current;
            for (int i = 0; i < table.Count; i++)
            {
                var row = table.RowOrNull((PlaceKind)i);
                if (row == null) continue;
                if (!string.IsNullOrEmpty(row.Frontage)) frontages[row.Frontage] = row.Name;
                if (!string.IsNullOrEmpty(row.Massing)) massings[row.Massing] = row.Name;
            }

            foreach (var kv in frontages)
                if (System.Array.IndexOf(Frontage.Styles, kv.Key) < 0)
                {
                    LogError($"kinds.txt: frontage '{kv.Key}' (on {kv.Value}) is a word nothing "
                           + "draws - that building silently takes the plain nameboard. There is: "
                           + string.Join(", ", Frontage.Styles) + ".");
                    bad++;
                }

            foreach (var kv in massings)
                if (!MassingGrammars.Knows(kv.Key))
                {
                    LogError($"kinds.txt: massing '{kv.Key}' (on {kv.Value}) is a word nothing "
                           + "builds - that building silently falls back to the cottage grammar.");
                    bad++;
                }

            Log($"kinds      {frontages.Count} frontage and {massings.Count} massing key(s) "
              + "declared, all answered");
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
