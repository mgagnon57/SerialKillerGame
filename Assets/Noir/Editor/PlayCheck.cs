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
    /// Runs exactly what VillageHost.Awake runs, headlessly, and says whether pressing Play will
    /// work.
    ///
    /// This exists because Awake catches a load failure, logs it, and RETURNS BEFORE BUILDING THE
    /// MESH - so the difference between "a city with nobody in it" and "a black screen with an
    /// error on it" is one exception inside the population generator, and guessing which is not
    /// good enough when somebody is about to press the button.
    ///
    ///   Unity.exe -batchmode -quit -nographics -projectPath . -executeMethod Noir.Editor.PlayCheck.Run -logFile &lt;log&gt;
    /// </summary>
    public static class PlayCheck
    {
        [MenuItem("Noir/Check Play Will Work")]
        public static void Run()
        {
            int bad = 0;
            try
            {
                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
                Debug.Log("[playcheck] kinds.txt parsed.");

                var layout = VillageParser.Parse(ContentLoader.Read(VillageHost.MapFile));
                var world = WorldBuilder.Build(layout);
                Debug.Log($"[playcheck] {VillageHost.MapFile}: {world.Width}x{world.Height}, "
                        + $"{world.PlaceCount} places.");

                var report = WorldValidator.Validate(world);
                foreach (var problem in report.Errors) { Debug.LogError("[playcheck] ERROR " + problem); bad++; }
                foreach (var warning in report.Warnings) Debug.LogWarning("[playcheck] warn " + warning);

                int dwellings = world.PlacesOfKind(PlaceKind.Dwelling).Count;
                Debug.Log($"[playcheck] places of enum kind Dwelling: {dwellings}");

                var names = NameTable.Parse(ContentLoader.Read("names.txt"));
                var particulars = ParticularsTable.Parse(ContentLoader.Read("particulars.txt"));

                var people = PopulationGenerator.Generate(world, names, particulars, VillageHost.Seed);
                Debug.Log($"[playcheck] population: {people.Count} people in "
                        + $"{people.HouseholdCount} households.");

                var sim = new Simulation(world, people, VillageHost.Seed, startMinuteOfDay: 6 * 60);
                sim.Tick(600);
                Debug.Log("[playcheck] simulation ticked 600 without throwing.");

                if (people.Count == 0)
                {
                    Debug.LogWarning("[playcheck] VERDICT: the city will DRAW but be EMPTY - "
                                   + "nobody lives here, because households are built from "
                                   + "PlaceKind.Dwelling and this map has none.");
                }
                else
                {
                    Debug.Log("[playcheck] VERDICT: Play should work.");
                }
            }
            catch (Exception ex)
            {
                bad++;
                Debug.LogError("[playcheck] VERDICT: Play will show ONLY AN ERROR - Awake throws "
                             + "before the mesh is built.\n" + ex);
            }

            if (Application.isBatchMode) EditorApplication.Exit(bad > 0 ? 1 : 0);
        }
    }
}
