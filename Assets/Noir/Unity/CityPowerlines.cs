using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// Poles and wires down the verges of the country roads.
    ///
    /// A road through open country with nothing beside it reads as a strip of tarmac laid on a
    /// lawn. What makes it a road somebody's council maintains is the line of poles marching
    /// along it - and the pack ships the pair authored to chain, which had never been placed.
    ///
    /// THE FARM SET, NOT THE CITY ONE. There are two: `Pole_Electric_A_City` at 6.88m with wires
    /// hanging 6.13-6.81, and `Pole_Electric_Old` at 7.37m with wires at 6.08-7.22. Both are
    /// internally consistent and the two do not mix, since the wire has to meet the pole where
    /// the pole actually ends. The old timber one belongs on a country road and the concrete
    /// city one does not, so that is the pair used here.
    ///
    /// WHERE THEY GO IS ASKED, NOT DECLARED. Poles want the rural stretches, and rather than
    /// hardcode where the town stops - which moves, and has moved four times - each candidate
    /// spot asks the map what its ground is. Pavement means town and gets nothing; grass, field
    /// or wood means country and gets a pole. A road that runs from the fields into the middle
    /// of Rossville therefore gets poles for exactly as far as the fields go.
    ///
    /// Static, so this goes inside the node CityChunker bakes.
    /// </summary>
    public static class CityPowerlines
    {
        private const string Farm = "Assets/polyperfect/Poly Universal Pack/Prefabs/Farm/";

        /// <summary>
        /// A pole every span, and the wire that reaches back to the one before it.
        ///
        /// MEASURED: `Pole_Electric_Wire_20m_Tri` spans z -20.02..0.18, so it is drawn BACKWARD
        /// along -z from wherever it is put. Place it at a pole facing the previous pole and the
        /// two ends land on the two tops. Twenty metres is the wire's own length and not a
        /// spacing anybody chose.
        /// </summary>
        private const float Span = 20f;

        /// <summary>
        /// How far off the centre line a pole stands, beyond the asphalt's own half-width.
        ///
        /// Far enough that a lorry's mirror clears it and near enough to read as belonging to
        /// the road rather than standing in a field by itself.
        /// </summary>
        private const float Verge = 3.5f;

        public static GameObject Build(WorldModel world, Transform parent)
        {
            var root = new GameObject("CityPowerlines");
            root.transform.SetParent(parent, false);

#if UNITY_EDITOR
            int poles = 0, wires = 0, roads = 0;

            foreach (var line in world.Roads.Lines)
            {
                if (!line.IsStraight) continue;
                if (line.Class == RoadClass.Track) continue;   // a farm track has no supply

                int on = Run(root.transform, world, line, ref poles, ref wires);
                if (on > 0) roads++;
            }

            Debug.Log($"[powerlines] {poles} poles and {wires} spans down {roads} country roads.");
#endif
            return root;
        }

#if UNITY_EDITOR

        /// <summary>
        /// One road's worth, walking its length and skipping everything that is not country.
        ///
        /// The wire is only hung where the PREVIOUS spot took a pole too, so a line that stops
        /// at the edge of town stops cleanly instead of throwing a twenty-metre span across the
        /// gap to the next field.
        /// </summary>
        private static int Run(Transform parent, WorldModel world, RoadLine line,
                               ref int poles, ref int wires)
        {
            // Which side of the road they run down. Kept to one side per road - both sides is a
            // transmission corridor, not a lane - and chosen off the road's own name so it does
            // not change between builds.
            float side = Materials3D.Scatter((int)line.Centre, line.Name.Length, 1201) % 2 == 0
                       ? 1f : -1f;
            float across = line.Centre + side * (line.HalfWidth + Verge);

            int placed = 0;
            bool previousTookOne = false;

            for (float along = line.From; along <= line.To; along += Span)
            {
                float gx = line.IsNorthSouth ? across : along;
                float gy = line.IsNorthSouth ? along : across;

                if (!Rural(world, gx, gy)) { previousTookOne = false; continue; }

                // Facing along the road, so the wire - which is drawn back along -z - reaches
                // the pole behind rather than out across the carriageway.
                float yaw = line.IsNorthSouth ? 0f : 90f;

                if (Put(parent, Farm + "Pole_Electric_Old.prefab", gx, gy, yaw) == null)
                {
                    previousTookOne = false;
                    continue;
                }
                poles++;
                placed++;

                if (previousTookOne && Put(parent, Farm + "Pole_Electric_Wire_20m_Tri.prefab",
                                           gx, gy, yaw) != null)
                    wires++;

                previousTookOne = true;
            }

            return placed;
        }

        /// <summary>
        /// Is this spot open country rather than town?
        ///
        /// Asked of the ground itself. Pavement is the town's, and a building, a wall or water
        /// is somebody else's whatever is around it.
        /// </summary>
        private static bool Rural(WorldModel world, float gx, float gy)
        {
            int x = Mathf.RoundToInt(gx), y = Mathf.RoundToInt(gy);
            if (x < 0 || y < 0 || x >= world.Width || y >= world.Height) return false;

            var ground = world.Grid.TerrainAt(x, y);
            if (ground != Noir.Core.World.Terrain.Grass
             && ground != Noir.Core.World.Terrain.Field
             && ground != Noir.Core.World.Terrain.Wood)
                return false;

            // Not inside anything that owns its own ground - a farmyard dresses itself, and a
            // pole in the middle of a paddock belongs to nobody.
            foreach (var place in world.AllPlaces)
                if (place.Bounds.Contains(x, y)) return false;

            return true;
        }

        private static GameObject Put(Transform parent, string path, float gx, float gy, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[powerlines] missing " + path); return null; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            // The pole pivots at its own base, so it sits on the REAL ground with no offset at
            // all beyond the terrain height under it.
            go.transform.position = new Vector3(gx, ElevationGrid.HeightAt(gx, gy), -gy);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }
#endif
    }
}
