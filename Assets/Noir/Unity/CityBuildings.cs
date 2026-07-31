using System.Collections.Generic;
using UnityEngine;
using Noir.Core.Contracts;
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
        private const string Land = "Assets/polyperfect/Poly Universal Pack/Prefabs/Farm/Buildings Farm/";

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
            _glazing.Clear();

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
                pieces += Townhouse(root.transform, place, place.Bounds, leftEnd || rightEnd, FacingOf(place));
            }

            foreach (var place in world.AllPlaces)
            {
                // One bought building per kind. The lot in city.txt is sized from the prefab,
                // never the other way round - these are the pack's own footprints rounded up.
                string prefab = KindOf(place) switch
                {
                    "diner"       => Whole + "Diner_City.prefab",
                    "precinct"    => Whole + "Police_Station_City.prefab",
                    // school2, not school: `school` is Ashcombe's village school and this is the
                    // pack's American elementary. Mapping the village kind here put a City
                    // school in a 1979 English village and left the city's own school - which
                    // is a different kind, numbered past the enum - with no model at all.
                    "school2"     => Whole + "School_City.prefab",
                    "hospital"    => Whole + "Hospital_City.prefab",
                    "firestation" => Whole + "Fire_Station_City.prefab",
                    "cinema"      => Whole + "Cinema_City.prefab",
                    "bank"        => Whole + "Bank_City.prefab",
                    "casino"      => Whole + "Casino_City.prefab",
                    "gasstation"  => Whole + "Gas_Station_City.prefab",
                    "icecream"    => Whole + "Shop_Icecream_City.prefab",
                    "carwash"     => Whole + "Car_Wash_City.prefab",
                    "restroom"    => Whole + "Restroom_City.prefab",
                    "newsstand"   => Whole + "Newspaper_Shop_City.prefab",
                    "tower"       => Skyscraper(place.Bounds),

                    // The country. `farm` is the kind Ashcombe already had, so the farmhouse
                    // needs no new row in the table - only a model.
                    "farm"        => Land + "House_Farm_British.prefab",
                    "barn"        => BestFit(Land, new[]
                                     { "Barn_Farm_British", "Barn_Farm_Scandinavian" }, place.Bounds),
                    "silo"        => Land + "Silo_Grain_Old.prefab",
                    _             => null,
                };
                if (prefab == null) continue;

                var built = Landmark(root.transform, prefab, place.Bounds);
                if (built != null) { Glazing(built, place); Record(place, built); pieces++; }
                pieces += Fleet(root.transform, place);
            }

            Debug.Log($"[city] {lots.Count} townhouses + landmarks, {pieces} pieces, "
                    + $"{root.GetComponentsInChildren<Renderer>().Length} renderers before chunking.");
#endif
            return root;
        }

        /// <summary>
        /// Does a bought model stand on this place?
        ///
        /// THE OLD RENDERER MUST NOT ALSO BUILD IT. VillageMesh walls it, RoofBuilder roofs it
        /// and Frontage hangs a shopfront on it - all keyed off the place, all still running -
        /// so every apartment and every landmark had a generated grey box standing inside the
        /// model. That is what X-ray was revealing: pressing X hides the procedural geometry and
        /// the bought building appears, because it had been inside the box the whole time.
        /// </summary>
        private static readonly Dictionary<int, float> _tops = new Dictionary<int, float>();

        /// <summary>
        /// How tall the model on this place turned out, measured after it was built.
        ///
        /// Picking needs it. A click is a ray, and deciding which building a ray touches means
        /// knowing how far up each one reaches - otherwise a click on the front of a terrace
        /// selects whatever stands behind it, which at street level is most of the time. Nothing
        /// else in the pack knows a building's height: a townhouse is a stack of modules chosen
        /// at build time and a skyscraper is picked by lot, so both are only knowable afterwards.
        /// </summary>
        public static bool TryHeight(PlaceId place, out float top) =>
            _tops.TryGetValue(place.Value, out top);

        private static void Record(Place place, GameObject built)
        {
            if (place == null || built == null) return;

            var rends = built.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;

            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            _tops[place.Id.Value] = b.max.y;
        }

        public static bool Handles(Place place)
        {
            switch (KindOf(place))
            {
                case "apartment":
                case "diner": case "precinct": case "school2": case "hospital":
                case "firestation": case "cinema": case "bank": case "casino":
                case "gasstation": case "icecream": case "carwash":
                case "restroom": case "newsstand": case "tower":
                case "farm": case "barn": case "silo":
                    return true;
                default:
                    return false;
            }
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
        private static int Townhouse(Transform parent, Place place, TileRect lot, bool end, float yaw)
        {
            int n = Stack(parent, place, lot, yaw, StoreysAt(lot), false, out var house);
            Record(place, house);        // after the roof, or the terrace picks up short
            return n;
        }

        /// <summary>
        /// One stacked building on a lot: ground floor, entrance floor, storeys, roof.
        ///
        /// Public because a DISTRICT lays out a whole block of these and has no business
        /// knowing how one is assembled - which family the lot takes, which face variant is
        /// safe, how tall a section is, where the glass lives. See CityDistrict.
        /// </summary>
        /// <param name="market">
        /// Put a SHOPFRONT on the ground floor instead of a residential bottom. Squarehouse_
        /// Market_A..G are seven ground-floor pieces, measured at 6.1 x 6.15 and exactly three
        /// metres tall - the same footprint and the same height as Squarehouse_Bottom - so they
        /// drop straight in underneath an ordinary stack. All seven had been sitting unused,
        /// which is why every block in Northgate had housing at street level and the downtown
        /// read as tall flats rather than as a downtown.
        ///
        /// Only Squarehouse has them, so a market lot is a Squarehouse whatever the lot rolled:
        /// Bayhouse is seven metres deep against Squarehouse's nine, and a market ground floor
        /// under Bayhouse storeys would step in and out at the first floor.
        /// </param>
        public static int Stack(Transform parent, Place place, TileRect lot, float yaw,
                                int storeys, bool market, out GameObject house)
        {
            string family = market || SquareAt(lot) ? "Squarehouse" : "Bayhouse";

            // All-sides everywhere, not front-only for the interior of a terrace.
            //
            // _F is an optimisation for units whose flanks a neighbour completely hides, and it
            // pays for it by UV-ing those flanks onto a marker cell in the universal atlas - a
            // hard, saturated blue. Any gap at all and you get a blue wall. Ours are 6m lots
            // holding 6.1m buildings on a grid with cross streets, so flanks ARE seen; the
            // saving was never real here and the failure is the loudest thing in the frame.
            const string face = "AS";

            // Every section of one house goes under a single node, which is then turned to face
            // the street and slid so the house sits on its lot. Placing each section by its own
            // pivot instead put a Bayhouse and a Squarehouse at different depths - the pivot is
            // not centred and the two families are 7m and 9m deep - so a terrace of mixed
            // families came out staggered like a broken tooth line.
            house = new GameObject($"{family}_{lot.X}_{lot.Y}");
            house.transform.SetParent(parent, false);
            house.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            int n = 0;
            float y = 0f;

            string ground = market
                ? $"{City}Squarehouse_Market_{"ABCDEFG"[(int)(Materials3D.Scatter(lot.X, lot.Y, 6151) % 7)]}_City.prefab"
                : $"{City}{family}_Bottom_A_{face}_City.prefab";

            var bottom = Place(house.transform, ground, y);
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

            Seat(house, lot, yaw, dressedOnly: true);
            Glazing(house, place);
            n += Roof(house, lot);
            n += Shopfront(house, lot, yaw);
            return n;
        }

        /// <summary>
        /// A skyscraper stacked to a stated height, rather than one of the three whole prefabs.
        ///
        /// Skyscraper_A_City is a single 76-metre model, and B and C are their own fixed
        /// heights, so a city built from them has exactly THREE building heights in it and a
        /// skyline that repeats. The pack also ships each of the three as Base + Floor + Roof,
        /// and a Floor is a fifteen-metre five-storey section, so a tower can be any height at
        /// all: A comes out at 17 + 15n metres. Those nine modules had never been placed.
        ///
        /// Measured, as ever - the sections are stacked by the height each one reports rather
        /// than by fifteen, because the base and the roof are neither of them fifteen and the
        /// three families differ.
        /// </summary>
        public static int Tower(Transform parent, Place place, TileRect lot, char family, int floors)
        {
            var tower = new GameObject($"Skyscraper_{family}_{lot.X}_{lot.Y}");
            tower.transform.SetParent(parent, false);

            int n = 0;
            float y = 0f;

            var b = Place(tower.transform, $"{Whole}Skyscraper_{family}_Base_City.prefab", y);
            if (b > 0f) { y += b; n++; }

            for (int i = 0; i < floors; i++)
            {
                var f = Place(tower.transform, $"{Whole}Skyscraper_{family}_Floor_City.prefab", y);
                if (f <= 0f) break;
                y += f;
                n++;
            }

            if (Place(tower.transform, $"{Whole}Skyscraper_{family}_Roof_City.prefab", y) > 0f) n++;

            Seat(tower, lot, 0f);
            Glazing(tower, place);
            return n;
        }

        private static List<string> _roofKit, _neon, _pipes;

        // ---- glazing ----
        //
        // Which renderers carry a bought building's windows, per place, so SunRig can light them
        // when somebody is home. Filled as the city is built and read once when the rig starts.
        private static readonly Dictionary<int, List<MeshRenderer>> _glazing =
            new Dictionary<int, List<MeshRenderer>>();

        private static Material _glassDay, _glassNight;

        /// <summary>Hand SunRig the glass belonging to one place.</summary>
        public static void CollectGlazing(Place place, List<MeshRenderer> into, List<PlaceId> places)
        {
            if (!_glazing.TryGetValue(place.Id.Value, out var mine)) return;
            foreach (var r in mine)
            {
                if (r == null) continue;
                into.Add(r);
                places.Add(place.Id);
            }
        }

        /// <summary>
        /// Switch one piece of glass between the pack's day and night materials.
        ///
        /// A swap rather than an emission tweak, because these are the pack's OWN materials and
        /// the night one is already the warm orange of a lit room. Nothing here invents a look;
        /// it picks between two the artist already drew.
        /// </summary>
        public static void SetGlazing(MeshRenderer r, bool lit)
        {
            if (r == null || _glassDay == null || _glassNight == null) return;

            var want = lit ? _glassNight : _glassDay;
            var slots = r.sharedMaterials;
            bool change = false;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != _glassDay && slots[i] != _glassNight) continue;
                if (slots[i] == want) continue;
                slots[i] = want;
                change = true;
            }
            if (change) r.sharedMaterials = slots;
        }

#if UNITY_EDITOR
        /// <summary>Note any renderer on this building that carries glass we can light.</summary>
        private static void Glazing(GameObject house, Place place)
        {
            _glassDay ??= AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/polyperfect/Poly Universal Pack/Materials/M_Universal_Glass.mat");
            _glassNight ??= AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/polyperfect/Poly Universal Pack/Materials/M_Universal_Glass_Night.mat");
            if (_glassDay == null || _glassNight == null) return;

            foreach (var r in house.GetComponentsInChildren<MeshRenderer>())
            {
                bool glass = false;
                foreach (var m in r.sharedMaterials)
                    if (m == _glassDay || m == _glassNight) { glass = true; break; }
                if (!glass) continue;

                if (!_glazing.TryGetValue(place.Id.Value, out var mine))
                    _glazing[place.Id.Value] = mine = new List<MeshRenderer>();
                mine.Add(r);
            }
        }
#endif

        private const string CarsCity = "Assets/polyperfect/Poly Universal Pack/Prefabs/Cars/Cars City/";
        private const string CarsTrucks = "Assets/polyperfect/Poly Universal Pack/Prefabs/Cars/Cars Trucks/";

        /// <summary>
        /// The vehicle that belongs to a building, parked at its door.
        ///
        /// A cruiser outside the precinct and an engine outside the fire station do more for
        /// "this town is staffed and running" than any amount of scatter, because they are the
        /// one kind of prop that is somewhere for a REASON. They are also the four vehicles
        /// deliberately kept out of the residential parking pool, so this is where they finally
        /// get used.
        ///
        /// Parked at the authored front door, which the simulation already needs, and pushed
        /// clear of the wall along the direction the door faces.
        /// </summary>
        private static int Fleet(Transform parent, Place place)
        {
            string vehicle = KindOf(place) switch
            {
                "precinct"    => CarsCity + "Car_Police_Modern.prefab",
                "hospital"    => CarsCity + "Car_Ambulance_Modern.prefab",
                "firestation" => CarsCity + "Car_Firetruck_Modern.prefab",
                "school"      => CarsCity + "Car_Bus_School_Modern.prefab",
                "school2"     => CarsCity + "Car_Bus_School_Modern.prefab",
                "gasstation"  => CarsTrucks + "Car_Truck_Modern_Cistern.prefab",
                "diner"       => CarsCity + "Car_Cargovan_Modern.prefab",
                _             => null,
            };
            if (vehicle == null) return 0;

            var lot = place.Bounds;
            var door = place.Door;

            // Out from the door, away from the building, and turned to lie along the frontage.
            float yaw = FacingOf(place);
            var outward = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
            var at = new Vector3(door.X, 0f, -door.Y) + outward * 4.5f;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(vehicle);
            if (prefab == null) { Debug.LogWarning("[city] missing " + vehicle); return 0; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0f, yaw + 90f, 0f);
            return 1;
        }

        /// <summary>
        /// Everything that lives on a flat roof: air conditioning, vents, satellite dishes,
        /// antennas, solar panels, a water tower.
        ///
        /// A flat roof is the emptiest surface in a city and it is in every overview shot, so
        /// this is the cheapest density in the whole pack - thirty-six prefabs that had never
        /// been instantiated once. It is also what stops a terrace reading as extruded boxes:
        /// rooflines differ even when the buildings below them do not.
        /// </summary>
        private static int Roof(GameObject house, TileRect lot)
        {
            _roofKit ??= Catalogue("Assets/polyperfect/Poly Universal Pack/Prefabs/City/Props City/Roof Props");
            if (_roofKit.Count == 0) return 0;

            var rends = house.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return 0;
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            int n = 0;
            int how = 2 + (int)(Materials3D.Scatter(lot.X, lot.Y, 307) % 3);
            for (int k = 0; k < how; k++)
            {
                // Kept a metre inside the parapet, or the dish hangs over the street.
                float fx = (Materials3D.Scatter(lot.X + k, lot.Y, 311) % 100) / 100f;
                float fz = (Materials3D.Scatter(lot.X, lot.Y + k, 313) % 100) / 100f;
                var on = new Vector3(Mathf.Lerp(b.min.x + 1f, b.max.x - 1f, fx),
                                     b.max.y,
                                     Mathf.Lerp(b.min.z + 1f, b.max.z - 1f, fz));

                var go = Spawn(house, Pick(_roofKit, lot.X + k, lot.Y, 317));
                if (go == null) continue;
                go.transform.position = on;
                go.transform.rotation = Quaternion.Euler(0f, Materials3D.Scatter(lot.X, lot.Y + k, 319) % 4 * 90f, 0f);
                n++;
            }
            return n;
        }

        /// <summary>
        /// A lit sign over the ground floor. The townhouse's bottom section is a shopfront and
        /// every one of them was a blank panel; the pack ships eight neons - bar, coffee, hotel,
        /// club, restaurant, casino, barber, OPEN - and none had been used.
        /// </summary>
        private static int Shopfront(GameObject house, TileRect lot, float yaw)
        {
            _neon ??= Catalogue("Assets/polyperfect/Poly Universal Pack/Prefabs/City/Props City/Neon Props");
            if (_neon.Count == 0) return 0;

            // Not every front door is a business. Roughly one unit in three.
            if (Materials3D.Scatter(lot.X, lot.Y, 331) % 3 != 0) return 0;

            var rends = house.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return 0;
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            // On the face the door is on, just above the shopfront glazing.
            var mid = new Vector3(b.center.x, 3.1f, b.center.z);
            var outward = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
            var on = mid + outward * (yaw == 0f || yaw == 180f ? b.extents.z + 0.15f : b.extents.x + 0.15f);

            var go = Spawn(house, Pick(_neon, lot.X, lot.Y, 337));
            if (go == null) return 0;
            go.transform.position = on;
            go.transform.rotation = Quaternion.Euler(0f, yaw + 180f, 0f);
            return 1;
        }

        private static List<string> Catalogue(string folder)
        {
            var found = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf("Collider", System.StringComparison.OrdinalIgnoreCase) < 0) found.Add(path);
            }
            found.Sort(System.StringComparer.Ordinal);
            return found;
        }

        private static string Pick(List<string> from, int x, int y, int salt) =>
            from[(int)(Materials3D.Scatter(x, y, salt) % (uint)from.Count)];

        private static GameObject Spawn(GameObject parent, string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent.transform, true);
            return go;
        }

        /// <summary>
        /// Slide a finished house so its measured footprint centres on its lot, in x and z only -
        /// the stack has already settled its own height and must not be lifted off the ground.
        /// </summary>
        /// <param name="dressedOnly">
        /// Measure by brick and glass, not by the raw combined mesh. `Squarehouse_Bottom_A`'s
        /// brick and glass end at z 3.01 but its `M_Universal_A` submesh runs to 5.96 - an
        /// unfaced tail the OTHER sections in the same stack (Entrance, Mid, Roof) do not carry
        /// anywhere near as far (0.1-0.6m, not 3m), so it is specific to Bottom rather than a
        /// real building depth. Seating by the whole combined bounds put that tail's far edge on
        /// the building line for a north-facing front, which pushed the actual brick wall up to
        /// 3m behind it - the tan slab on every building front that faced that way. This is
        /// probably also why Bayhouse and Squarehouse were measured as 7m and 9m deep elsewhere
        /// in this file: both numbers include a Bottom-only tail of a different size, and their
        /// brick footprints alone measure within a centimetre of each other. Towers pass false:
        /// Skyscraper meshes have no brick or glass submesh to measure by, so this falls back to
        /// the raw bounds unchanged.
        /// </param>
        private static void Seat(GameObject house, TileRect lot, float yaw, bool dressedOnly = false)
        {
            if (!dressedOnly || !TryDressedBounds(house, out var b))
            {
                var rends = house.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) return;

                b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            }

            // Village y runs into Unity -z, as everywhere else in the renderers.
            float westX = lot.X, eastX = lot.X + lot.W;
            float northZ = -lot.Y, southZ = -(lot.Y + lot.H);
            var pos = house.transform.position;

            // THE FRONT WALL GOES ON THE BUILDING LINE. Centring the footprint on the lot
            // instead - which is what this did - lines up the MIDDLES, and Bayhouse is 7m deep
            // where Squarehouse is 9m, so a terrace of mixed families stood in a ragged
            // zigzag with every other house set back a metre. A terrace is a terrace because
            // its FACADES agree; what the backs do is nobody's business.
            if (yaw == 0f)          // faces -z: front is the low-z face, on the south edge
                pos.z += southZ - b.min.z;
            else if (yaw == 180f)   // faces +z: front is the high-z face, on the north edge
                pos.z += northZ - b.max.z;
            else
                pos.z += -(lot.Y + lot.H / 2f) - b.center.z;

            if (yaw == 90f)         // faces -x
                pos.x += westX - b.min.x;
            else if (yaw == 270f)   // faces +x
                pos.x += eastX - b.max.x;
            else
                pos.x += (lot.X + lot.W / 2f) - b.center.x;

            house.transform.position = pos;
        }

        /// <summary>
        /// The house's bounds by its brick and glass submeshes only, skipping `M_Universal_A` -
        /// see Seat's `dressedOnly`. False if nothing in the house has either, so the caller
        /// falls back to the raw combined bounds instead of seating by an empty box.
        /// </summary>
        private static bool TryDressedBounds(GameObject house, out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            foreach (var mf in house.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mesh == null || mr == null) continue;

                var mats = mr.sharedMaterials;
                var verts = mesh.vertices;
                var xform = mf.transform;

                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var mat = s < mats.Length ? mats[s] : null;
                    if (mat == null
                        || mat.name.StartsWith("M_Universal_A", System.StringComparison.Ordinal))
                        continue;

                    foreach (var idx in mesh.GetTriangles(s))
                    {
                        var world = xform.TransformPoint(verts[idx]);
                        if (!any) { bounds = new Bounds(world, Vector3.zero); any = true; }
                        else bounds.Encapsulate(world);
                    }
                }
            }

            return any;
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

            // SEATED ON ITS OWN MEASURED BOTTOM, NOT ON ITS PIVOT. Most sections in the kit
            // start at y=0 and for those this changes nothing - but Skyscraper_B_Base_City
            // begins at y=1.00, so stacking it by its pivot left the whole tower hanging a metre
            // in the air AND put the next section a metre inside it, because the height returned
            // below is the section's extent and not the distance to its top. Same lesson as the
            // road tiles: measure the mesh, never the pivot.
            float sits = parent.position.y + y;
            if (Mathf.Abs(b.min.y - sits) > 0.001f)
                go.transform.position += Vector3.up * (sits - b.min.y);

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
            // NOTHING. Left as a named no-op because deleting the calls would hide what was
            // learned here.
            //
            // This used to swap every material named Glass for Ashcombe's WindowGlass, to fix
            // facades coming through saturated blue. That diagnosis was wrong: the blue was the
            // _F front-only variant UV-ing its hidden flanks onto a marker cell in the atlas,
            // fixed by using _AS everywhere. So the swap was solving nothing and costing a great
            // deal - M_Universal_Glass_Night is an EMISSIVE warm orange, which is to say the
            // pack ships its windows already lit, and this was painting over them with a flat
            // dark grey. The city could not light up at night because its glass had been
            // replaced with something that does not glow.
        }

        /// <summary>
        /// Which of the three skyscrapers stands on this lot: the TALLEST that fits it.
        ///
        /// Measured off the prefabs rather than listed here. Skyscraper_A is 46.9 x 38.2 and
        /// Skyscraper_C is 35.6 x 40.3, and a lot authored for one of them will not hold the
        /// other - so a name typed into a switch puts a building through the wall of its
        /// neighbour the first time a lot is resized.
        /// </summary>
        private static string Skyscraper(TileRect lot) =>
            BestFit(Whole, new[] { "Skyscraper_A_City", "Skyscraper_B_City", "Skyscraper_C_City" }, lot);

        /// <summary>
        /// The BIGGEST of these models that still fits the lot, measured rather than assumed.
        ///
        /// Two towers differ by eleven metres of footprint and the two barns by twenty-seven, so
        /// a name typed into a switch puts a building through the wall of its neighbour the
        /// first time a lot is resized. Asking the model how big it is costs one instantiation
        /// and cannot go stale.
        /// </summary>
        private static string BestFit(string folder, string[] names, TileRect lot)
        {
            string best = null;
            float biggest = -1f;

            foreach (var name in names)
            {
                string path = folder + name + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                probe.transform.position = Vector3.zero;
                probe.transform.rotation = Quaternion.identity;

                var rends = probe.GetComponentsInChildren<Renderer>();
                if (rends.Length > 0)
                {
                    var b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

                    // Landmark() turns a model to lie along its lot's long side, so it fits if
                    // it fits either way round.
                    bool fits = (b.size.x <= lot.W && b.size.z <= lot.H)
                             || (b.size.z <= lot.W && b.size.x <= lot.H);
                    // Biggest by footprint, so a generous lot gets the generous model. For the
                    // towers that also picks the tallest, which is what a downtown wants.
                    float size = b.size.x * b.size.z;
                    if (fits && size > biggest) { biggest = size; best = path; }
                }
                UnityEngine.Object.DestroyImmediate(probe);
            }

            if (best == null)
                Debug.LogWarning($"[city] none of {string.Join(", ", names)} fits a "
                               + $"{lot.W}x{lot.H} lot at {lot.X},{lot.Y}.");
            return best;
        }

        /// <summary>A whole building, centred on its lot and turned to face the long way.</summary>
        private static GameObject Landmark(Transform parent, string path, TileRect lot)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[city] missing " + path); return null; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            Reglaze(go);

            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return null;

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
            return go;
        }
#endif
    }
}
