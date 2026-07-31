using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// Fills a suburb cell.
    ///
    /// THE PACK HAS NO SUBURBAN HOUSE. All ~6,000 prefabs were searched for one: there are two
    /// farmhouses, both of which are already standing as Home Farm and Wicker End, one
    /// summer-camp cabin, and the Fantasy set. A street of detached houses built out of what
    /// exists would be forty copies of Home Farm - which is the same failure as the yard of
    /// featureless cream garages that CityDistrict.Interior rejects in its own comments, and for
    /// the same reason: placeholder geometry repeated is worse than nothing.
    ///
    /// IT DOES NOT NEED ONE, because the difference between downtown and a suburb was never the
    /// building. The townhouse modules ship in three face variants - front only, front and back,
    /// and ALL SIDES - and CityBuildings.Stack already takes the all-sides one, with a note
    /// saying flanks are seen and the saving was never real. So a freestanding house is buildable
    /// out of the same kit the terraces are stacked from, and what makes it a suburb instead of a
    /// terrace is:
    ///
    ///   THE SETBACK. A downtown block puts its buildings on the building line, hard against the
    ///   pavement. A house here stands twelve metres back with a garden in front of it.
    ///
    ///   THE SPACING. A terrace is pitched at six metres, which is narrower than the module, so
    ///   the units overlap and read as one run. These are pitched at fourteen, so they read as
    ///   separate houses with gaps you can see between.
    ///
    ///   WHAT IS IN THE GAP. A hedge along the boundary, a driveway, a garage, and a car standing
    ///   on it - which is a different fact about a household from a car on the road.
    ///
    /// And the height: one or two storeys against downtown's six.
    ///
    /// Static, so this goes inside the node CityChunker bakes.
    /// </summary>
    public static class CitySuburb
    {
        /// <summary>
        /// How far apart the houses stand along the street.
        ///
        /// The modules are 6.1-6.3m across, so fourteen metres leaves a gap of about eight - room
        /// for a driveway and a garage between one house and the next, which is what the gap in a
        /// suburb is actually for. Anything under about eleven and the garages meet.
        /// </summary>
        private const int Pitch = 14;

        /// <summary>How far the front wall stands back from the edge of the cell.</summary>
        private const int Setback = 12;

        /// <summary>Bayhouse's measured depth. See CityBuildings.Stack's `only` parameter.</summary>
        private const int Depth = 7;

        private const string City = "Assets/polyperfect/Poly Universal Pack/Prefabs/City/";
        private const string Fences = "Assets/polyperfect/Poly Universal Pack/Prefabs/Modular Parts/Fences";
        private const string Cars = "Assets/polyperfect/Poly Universal Pack/Prefabs/Cars/Cars City";

        public static GameObject Build(WorldModel world, Transform parent)
        {
            var root = new GameObject("CitySuburb");
            root.transform.SetParent(parent, false);

#if UNITY_EDITOR
            int cells = 0, houses = 0, pieces = 0, garages = 0, cars = 0, hedges = 0;

            foreach (var place in world.AllPlaces)
            {
                if (PlaceKindTable.Current.Row(place.Kind).Name != "suburb") continue;
                cells++;

                var lot = place.Bounds;

                // North and south frontages take the full width; east and west stop short of
                // them, exactly as a district's perimeter does, or two houses stand on the same
                // ground at every corner of every cell.
                for (int x = lot.X; x + Pitch <= lot.X + lot.W; x += Pitch)
                {
                    int i = (x - lot.X) / Pitch;
                    houses += One(root.transform, place, lot, i,
                                  new TileRect(x, lot.Y + Setback, Pitch, Depth), 180f,
                                  ref pieces, ref garages, ref cars);
                    houses += One(root.transform, place, lot, i + 40,
                                  new TileRect(x, lot.Y + lot.H - Setback - Depth, Pitch, Depth), 0f,
                                  ref pieces, ref garages, ref cars);
                }

                for (int y = lot.Y + Pitch; y + Pitch <= lot.Y + lot.H - Pitch; y += Pitch)
                {
                    int i = (y - lot.Y) / Pitch;
                    houses += One(root.transform, place, lot, i + 80,
                                  new TileRect(lot.X + Setback, y, Depth, Pitch), 90f,
                                  ref pieces, ref garages, ref cars);
                    houses += One(root.transform, place, lot, i + 120,
                                  new TileRect(lot.X + lot.W - Setback - Depth, y, Depth, Pitch), 270f,
                                  ref pieces, ref garages, ref cars);
                }

                hedges += Hedges(root.transform, lot);
            }

            Debug.Log($"[suburb] {cells} cells: {houses} houses, {pieces} sections, "
                    + $"{garages} garages, {cars} cars on drives, {hedges} lengths of hedge, "
                    + $"{root.GetComponentsInChildren<Renderer>().Length} renderers.");
#endif
            return root;
        }

#if UNITY_EDITOR

        /// <summary>
        /// One house, its garage, and whatever is on the drive - or a gap where the dice say so.
        ///
        /// ONE PLOT IN SEVEN IS EMPTY. Fewer than downtown's one in six, because a suburb built
        /// all at once is more uniform than a city block that filled in over eighty years, and
        /// more than none, because a run of houses with no break in it reads as a terrace again.
        /// </summary>
        private static int One(Transform parent, Place place, TileRect cell, int index,
                               TileRect lot, float yaw, ref int pieces, ref int garages,
                               ref int cars)
        {
            if (Materials3D.Scatter(cell.X + index, cell.Y + index, 5171) % 7 == 0) return 0;

            // One storey or two. A bungalow among the semis, which is what an estate built over
            // a decade actually looks like.
            int storeys = Materials3D.Scatter(cell.X + index, cell.Y, 5179) % 4 == 0 ? 0 : 1;

            pieces += CityBuildings.Stack(parent, place, lot, yaw, storeys, false, out _,
                                          only: "Bayhouse");

            // The garage goes BESIDE the house, towards the street, on the side away from the
            // next house along - so the drive runs off the road rather than through a garden.
            uint roll = Materials3D.Scatter(cell.X + index, cell.Y + index, 5189) % 100;
            if (roll < 65)
            {
                var drive = Beside(lot, yaw);
                if (Put(parent, City + "Buildings Modular City/Squarehouse_Garage_City.prefab",
                        drive.x, drive.y, yaw) != null)
                {
                    garages++;

                    // And a car on it about half the time. A car on a drive is a different fact
                    // about a household from a car on the road, and for this game it is the more
                    // useful one: it says somebody is in.
                    if (roll < 34)
                    {
                        _cars ??= Catalogue(Cars, "Car_");
                        if (_cars.Count > 0 &&
                            Put(parent, Pick(_cars, cell.X + index, cell.Y, 5197),
                                drive.x, drive.y + Nudge(yaw), yaw) != null) cars++;
                    }
                }
            }

            return 1;
        }

        /// <summary>The drive, beside the house and nearer the street than the house is.</summary>
        private static Vector2 Beside(TileRect lot, float yaw)
        {
            float cx = lot.X + lot.W * 0.5f, cy = lot.Y + lot.H * 0.5f;

            // yaw 180 faces -y (north edge), 0 faces +y, 90 faces -x, 270 faces +x.
            return yaw switch
            {
                180f => new Vector2(cx + 4.5f, cy - 4f),
                0f   => new Vector2(cx + 4.5f, cy + 4f),
                90f  => new Vector2(cx - 4f, cy + 4.5f),
                _    => new Vector2(cx + 4f, cy + 4.5f),
            };
        }

        /// <summary>How far in front of the garage a car on the drive stands.</summary>
        private static float Nudge(float yaw) => yaw == 180f ? -3.5f : yaw == 0f ? 3.5f : 0f;

        /// <summary>
        /// The garden boundary, laid as a RUN along each frontage.
        ///
        /// One prefab per metre would be four thousand objects for the ring and would read as a
        /// line of individual shrubs rather than a hedge - the same reason VillageMesh draws
        /// hedgerows as runs instead of one bush to a tile. The three-metre piece is used wherever
        /// three metres are left, which is nearly everywhere, and the shorter ones only make up
        /// the remainder at the end of a run.
        /// </summary>
        private static int Hedges(Transform parent, TileRect lot)
        {
            _hedge3 ??= Catalogue(Fences, "Fence_Hedge_Short_3m");
            if (_hedge3.Count == 0) return 0;

            string piece = _hedge3[0];
            int laid = 0;

            // Along the inside of the cell edge, in front of the gardens - so the frontage reads
            // as a boundary from the road rather than the houses floating on open grass.
            const int In = 3;

            for (int x = lot.X + In; x + 3 <= lot.X + lot.W - In; x += 3)
            {
                if (Put(parent, piece, x + 1.5f, lot.Y + In, 0f) != null) laid++;
                if (Put(parent, piece, x + 1.5f, lot.Y + lot.H - In, 0f) != null) laid++;
            }

            for (int y = lot.Y + In; y + 3 <= lot.Y + lot.H - In; y += 3)
            {
                if (Put(parent, piece, lot.X + In, y + 1.5f, 90f) != null) laid++;
                if (Put(parent, piece, lot.X + lot.W - In, y + 1.5f, 90f) != null) laid++;
            }

            return laid;
        }

        private static List<string> _hedge3, _cars;

        private static List<string> Catalogue(string folder, string prefix)
        {
            var found = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf("Collider", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                if (prefix != null && !file.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
                found.Add(path);
            }
            found.Sort(System.StringComparer.Ordinal);   // stable, so a street looks the same twice
            return found;
        }

        private static string Pick(List<string> from, int x, int y, int salt) =>
            from[(int)(Materials3D.Scatter(x, y, salt) % (uint)from.Count)];

        /// <summary>A piece by its own base, at a point in village coordinates.</summary>
        private static GameObject Put(Transform parent, string path, float vx, float vy, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(vx, 0.12f, -vy);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }
#endif
    }
}
