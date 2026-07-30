using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// Places bought models on authored lots, instead of generating geometry to fill them.
    ///
    /// This is the inversion the city is built on. Ashcombe's renderer is given a footprint and
    /// invents walls and a roof to match it; here the MODEL came first and the lot in city.txt
    /// was sized from it. A terrace lot is 6 metres because the townhouse module is 6.1 metres,
    /// not the other way round.
    ///
    /// What it knows how to do:
    ///   - stack a townhouse out of Bottom / Floor / Roof sections to whatever height it is told
    ///   - give a terrace's interior units the front-only facade and its ends the all-sides one,
    ///     which is what the pack's _F and _AS suffixes are for
    ///   - drop a whole prefab for a landmark, centred and rotated onto its lot
    ///
    /// Editor-only in practice: the pieces load through AssetDatabase because the pack is not
    /// under Resources. Prototype, not shipping code.
    /// </summary>
    public static class CityBuildings
    {
        private const string City = "Assets/polyperfect/Poly Universal Pack/Prefabs/City/Buildings Modular City/";
        private const string Whole = "Assets/polyperfect/Poly Universal Pack/Prefabs/City/Buildings City/";

        /// <summary>Every floor in the kit is exactly three metres.</summary>
        private const float Floor = 3f;

        /// <summary>How many storeys a lot gets, from its position, so it never changes.</summary>
        private static int StoreysAt(TileRect lot) =>
            2 + (int)(Materials3D.Scatter(lot.X, lot.Y, 7717) % 3);   // 2, 3 or 4

        /// <summary>Which townhouse family, again stable per lot.</summary>
        private static bool SquareAt(TileRect lot) =>
            Materials3D.Scatter(lot.X, lot.Y, 4441) % 2 == 0;

        public static GameObject Build(WorldModel world, Transform parent)
        {
            var root = new GameObject("CityBuildings");
            root.transform.SetParent(parent, false);

#if UNITY_EDITOR
            // A terrace is only a terrace if its units know they are in one. Neighbours are
            // found by lot adjacency: anything sharing an edge with this lot hides that side,
            // so only the ends need a finished flank.
            var lots = new List<Place>();
            foreach (var place in world.AllPlaces)
                if (KindOf(place) == "apartment") lots.Add(place);

            int pieces = 0;
            foreach (var place in lots)
            {
                bool leftEnd = !HasNeighbour(lots, place, -1);
                bool rightEnd = !HasNeighbour(lots, place, +1);
                pieces += Townhouse(root.transform, place.Bounds, leftEnd || rightEnd, FacingOf(place));
            }

            foreach (var place in world.AllPlaces)
            {
                string prefab = KindOf(place) switch
                {
                    "diner"    => Whole + "Diner_City.prefab",
                    "precinct" => Whole + "Police_Station_City.prefab",
                    _          => null,
                };
                if (prefab == null) continue;

                if (Landmark(root.transform, prefab, place.Bounds)) pieces++;
            }

            Debug.Log($"[city] {lots.Count} townhouses + landmarks, {pieces} pieces, "
                    + $"{root.GetComponentsInChildren<Renderer>().Length} renderers before chunking.");
#endif
            return root;
        }

        /// <summary>
        /// The kind's canonical name from kinds.txt, not Kind.ToString().
        ///
        /// A kind the PlaceKind enum has never heard of - which is every city kind - is numbered
        /// after the enum members, so ToString() gives back a bare number and matching on it
        /// silently never fires.
        /// </summary>
        private static string KindOf(Place place) => PlaceKindTable.Current.Row(place.Kind).Name;

        /// <summary>
        /// Which way a house faces, taken from its authored front door rather than guessed.
        ///
        /// The door is already in city.txt because the simulation needs somewhere to walk to,
        /// so the renderer gets the facing for nothing - and a terrace on the far side of the
        /// avenue turns to look back across it without a word of extra content.
        /// </summary>
        private static float FacingOf(Place place)
        {
            var lot = place.Bounds;
            var door = place.Door;

            // Sections face -z at rest, which is the direction of DECREASING village y.
            if (door.Y <= lot.Y) return 180f;              // door on the north edge
            if (door.Y >= lot.Y + lot.H - 1) return 0f;    // door on the south edge
            if (door.X <= lot.X) return 90f;               // west
            return 270f;                                   // east
        }

        /// <summary>Is there another lot butted against this one along x?</summary>
        private static bool HasNeighbour(List<Place> lots, Place self, int dir)
        {
            foreach (var other in lots)
            {
                if (other.Id.Value == self.Id.Value) continue;
                if (other.Bounds.Y != self.Bounds.Y) continue;
                int wantX = dir < 0 ? self.Bounds.X - other.Bounds.W : self.Bounds.X + self.Bounds.W;
                if (other.Bounds.X == wantX) return true;
            }
            return false;
        }

#if UNITY_EDITOR
        /// <summary>
        /// One stacked townhouse, centred on its lot. Sections pivot at their own base, so the
        /// stack is nothing more cunning than adding the floor height each time.
        /// </summary>
        private static int Townhouse(Transform parent, TileRect lot, bool end, float yaw)
        {
            string family = SquareAt(lot) ? "Squarehouse" : "Bayhouse";

            // All-sides everywhere, not front-only for the interior of a terrace.
            //
            // _F is an optimisation for units whose flanks a neighbour completely hides, and it
            // pays for it by UV-ing those flanks onto a marker cell in the universal atlas - a
            // hard, saturated blue. Any gap at all and you get a blue wall. Ours are 6m lots
            // holding 6.1m buildings on a grid with cross streets, so flanks ARE seen; the
            // saving was never real here and the failure is the loudest thing in the frame.
            const string face = "AS";
            int storeys = StoreysAt(lot);

            // Every section of one house goes under a single node, which is then turned to face
            // the street and slid so the house sits on its lot. Placing each section by its own
            // pivot instead put a Bayhouse and a Squarehouse at different depths - the pivot is
            // not centred and the two families are 7m and 9m deep - so a terrace of mixed
            // families came out staggered like a broken tooth line.
            var house = new GameObject($"{family}_{lot.X}_{lot.Y}");
            house.transform.SetParent(parent, false);
            house.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            int n = 0;
            float y = 0f;

            var bottom = Place(house.transform, $"{City}{family}_Bottom_A_{face}_City.prefab", y);
            if (bottom > 0f) { y += bottom; n++; }

            var entrance = Place(house.transform, $"{City}{family}_Floor_A_Entrance_{face}_City.prefab", y);
            if (entrance > 0f) { y += entrance; n++; }

            for (int i = 0; i < storeys; i++)
            {
                var mid = Place(house.transform, $"{City}{family}_Floor_A_Mid_{face}_City.prefab", y);
                if (mid <= 0f) break;
                y += mid;
                n++;
            }

            if (Place(house.transform, $"{City}{family}_Roof_A_City.prefab", y) > 0f) n++;

            Seat(house, lot);
            return n;
        }

        /// <summary>
        /// Slide a finished house so its measured footprint centres on its lot, in x and z only -
        /// the stack has already settled its own height and must not be lifted off the ground.
        /// </summary>
        private static void Seat(GameObject house, TileRect lot)
        {
            var rends = house.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;

            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            // Village y runs into Unity -z, as everywhere else in the renderers.
            var want = new Vector3(lot.X + lot.W / 2f, 0f, -(lot.Y + lot.H / 2f));
            var drift = b.center - house.transform.position;

            house.transform.position = new Vector3(want.x - drift.x,
                                                   house.transform.position.y,
                                                   want.z - drift.z);
        }

        /// <summary>Instantiate one section at a height, returning how tall it turned out.</summary>
        private static float Place(Transform parent, string path, float y)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[city] missing " + path); return 0f; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.up * y;
            go.transform.localRotation = Quaternion.identity;
            Reglaze(go);

            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return Floor;

            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b.size.y;
        }

        /// <summary>
        /// Swap the pack's glazing for Ashcombe's, which is the one thing about these buildings
        /// that does not survive the move.
        ///
        /// M_Universal_Glass comes through as a flat, saturated blue - the same atlas fault the
        /// cottage hit, and far louder here because a terrace is mostly windows. Only the slots
        /// the pack itself named glass are touched; the brick and the stonework are the pack's
        /// own and are exactly right.
        /// </summary>
        private static void Reglaze(GameObject go)
        {
            var glass = Materials3D.WindowGlass;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var originals = r.sharedMaterials;
                Material[] slots = null;
                for (int i = 0; i < originals.Length; i++)
                {
                    if (originals[i] == null) continue;
                    if (originals[i].name.IndexOf("Glass", System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    slots ??= (Material[])originals.Clone();
                    slots[i] = glass;
                }
                if (slots != null) r.sharedMaterials = slots;
            }
        }

        /// <summary>A whole building, centred on its lot and turned to face the long way.</summary>
        private static bool Landmark(Transform parent, string path, TileRect lot)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[city] missing " + path); return false; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            Reglaze(go);

            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return false;

            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            // Turn it so its long side runs along the lot's long side, then sit its measured
            // centre on the lot's centre - the prefab's pivot is not reliably either.
            bool prefabWide = b.size.x >= b.size.z;
            bool lotWide = lot.W >= lot.H;
            if (prefabWide != lotWide) go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            rends = go.GetComponentsInChildren<Renderer>();
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            var want = new Vector3(lot.X + lot.W / 2f, 0f, -(lot.Y + lot.H / 2f));
            var drift = b.center - go.transform.position;
            go.transform.position = new Vector3(want.x - drift.x,
                                                go.transform.position.y - (b.min.y - go.transform.position.y),
                                                want.z - drift.z);
            return true;
        }
#endif
    }
}
