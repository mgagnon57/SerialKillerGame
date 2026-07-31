using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Noir.Core.World;
using Noir.Unity;

namespace Noir.Editor
{
    /// <summary>
    /// Turns on Read/Write for exactly the models the city instantiates, and nothing else.
    ///
    /// CityChunker combines the placed prefabs into a few big meshes, and Mesh.CombineMeshes
    /// cannot read a mesh whose importer has Read/Write off - which is the default, and which is
    /// how the whole pack ships. So the chunker was silently doing nothing.
    ///
    /// The blunt fix is to flip the flag on all four thousand models in the pack. That reimports
    /// 1.4GB and keeps a second copy of every mesh in system memory for the lifetime of the
    /// process, almost all of it for models this city never places. So instead: BUILD THE CITY,
    /// WRITE DOWN WHAT IT ACTUALLY USED, and flip only those. It stays correct as the city grows
    /// because it is derived from the city rather than from a list somebody maintains.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Noir.Editor.MeshReadable.Enable -logFile &lt;log&gt;
    /// </summary>
    public static class MeshReadable
    {
        [MenuItem("Noir/Make City Meshes Readable")]
        public static void Enable()
        {
            GameObject probe = null;
            int changed = 0, already = 0;

            try
            {
                PlaceKindTable.Install(PlaceKindTable.Parse(ContentLoader.Read("kinds.txt")));
                var layout = VillageParser.Parse(ContentLoader.Read(VillageHost.MapFile));
                var world = WorldBuilder.Build(layout, VillageHost.Seed);

                // EVERY RENDERER THAT PLACES A BOUGHT MODEL, or the ones left out get missed and
                // stay unbaked for ever. Four were: CityParking, CitySigns and CityDistrict have
                // been placing the road kit's parking tiles, the whole sign set and the seven
                // Squarehouse_Market shopfronts since they were written, and none of those meshes
                // was ever made readable - so the chunker quietly left several thousand renderers
                // alone and nobody noticed, because a chunker that skips something still succeeds.
                //
                // CitySuburb is the fourth and is what exposed it: two thousand hedge pieces are
                // hard to miss in a renderer count where a few hundred parking tiles were not.
                probe = new GameObject("ReadableProbe");
                CityStreets.Build(world, probe.transform);
                CityParking.Build(world, probe.transform);
                CitySigns.Build(world, probe.transform);
                CityBuildings.Build(world, probe.transform);
                CityDistrict.Build(world, probe.transform);
                CitySuburb.Build(world, probe.transform);
                CityStory.Build(world, probe.transform);
                CityRail.Build(world, probe.transform);
                CityFarm.Build(world, probe.transform);
                CityPowerlines.Build(world, probe.transform);
                CityGreenery.Build(world, probe.transform);

                // Which model asset each placed mesh came from.
                var models = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var f in probe.GetComponentsInChildren<MeshFilter>())
                {
                    if (f.sharedMesh == null) continue;
                    string path = AssetDatabase.GetAssetPath(f.sharedMesh);
                    if (!string.IsNullOrEmpty(path)) models.Add(path);
                }

                Debug.Log($"[readable] the city places meshes from {models.Count} model assets.");

                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var path in models)
                    {
                        if (AssetImporter.GetAtPath(path) is not ModelImporter mi) continue;
                        if (mi.isReadable) { already++; continue; }
                        mi.isReadable = true;
                        mi.SaveAndReimport();
                        changed++;
                    }
                }
                finally { AssetDatabase.StopAssetEditing(); }

                Debug.Log($"[readable] enabled Read/Write on {changed} models "
                        + $"({already} already had it). The rest of the pack is untouched.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[readable] FAILED: " + ex);
            }
            finally
            {
                if (probe != null) UnityEngine.Object.DestroyImmediate(probe);
                AssetDatabase.Refresh();
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
