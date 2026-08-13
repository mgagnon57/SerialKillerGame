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
    /// This is the inversion the city is built on. Rossville's renderer is given a footprint and
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

        /// <summary>
        /// The kinds built as a stack of modules on their own lot, rather than as a bought model.
        ///
        /// A PARADE IS A TERRACE THAT SELLS THINGS. The pack has no shop, pub, post office or
        /// community hall as a whole building - it has a bank, a diner, a cinema and a casino,
        /// which are landmarks, and then the modular shopfront sections that downtown's six
        /// hundred buildings are made of. A corner shop IS one of those sections: ground floor
        /// glazed to the street, a flat or two over it, sharing its side walls with the next unit
        /// along. So these go through the same Stack the apartments do, and a row of them authored
        /// at adjacent x becomes a parade for free, because HasNeighbour already reads adjacency
        /// off the lots.
        ///
        /// The kinds differ in what the SIMULATION does with them - opening hours, a counter, a
        /// shopkeeper to employ - and not in how they are built, which is the right place for the
        /// difference to live.
        /// </summary>
        private static bool Stacked(string kind)
        {
            switch (kind)
            {
                // NOTHING. A town of twelve hundred has no stacked city block in it.
                //
                // This list used to be apartment, shop, pub, postoffice and villagehall, and it
                // put 2-4 storey modular townhouses on all of them. The shops are a Main Street
                // terrace now (see MassingGrammars), and the four "apartment" places are flats
                // OVER THE SHOPS - which is what the Sanborn sheet shows again and again,
                // "dwelling above" - not a block of their own.
                default:
                    return false;
            }
        }

        /// <summary>Which townhouse family, again stable per lot.</summary>
        private static bool SquareAt(TileRect lot) =>
            Materials3D.Scatter(lot.X, lot.Y, 4441) % 2 == 0;

        public static GameObject Build(WorldModel world, Transform parent)
        {
            var root = new GameObject("CityBuildings");
            root.transform.SetParent(parent, false);

            // GUARDED, because `_glazing` is declared inside this class's own editor-only block
            // and this line is outside it. It compiled every day, passed every test and ran fine
            // on Play; it could not be BUILT, and nobody found out because nobody had ever built
            // the game. Found by the first player build this project has ever attempted.
            //
            // Nothing is lost outside the editor: the dictionary only holds the bought buildings'
            // own glass, and in a player there are no bought buildings to hold it for.
#if UNITY_EDITOR
            _glazing.Clear();
#endif

            // EDITOR ONLY, AND UNLIKE THE PEOPLE THERE IS NO FALLBACK. Everything below loads
            // through AssetDatabase and PrefabUtility, which are UnityEditor APIs and do not exist
            // in a player - so in a standalone build this method returns an empty root and not one
            // bought prop is placed. The same is true of CityStreets, CityGreenery, CityTraffic,
            // CityParking, CitySigns and SunRig; a build today would be the procedural survey plan
            // and capsule people, which is a working game and is not the one anybody is looking at
            // in the editor.
            //
            // AgentMeshView has the same #if and says so out loud - "a bought person if there is
            // one, and the primitives if there is not" - because it HAS a `?? AgentFigure.Build`
            // to fall back to. This has nothing, so the failure is silent and the compiler is
            // happy. That asymmetry is the only reason this comment exists: the pattern was
            // understood when the people were written and the town got it by accident.
            //
            // NOT A FLAG FLIP TO FIX. It needs Resources/, Addressables, or a serialized
            // ScriptableObject catalogue built at edit time and read at runtime - real work, and
            // deliberately not started here. Cheap to write down now, expensive to discover in the
            // week somebody wants a playable build.
#if UNITY_EDITOR
            // A terrace is only a terrace if its units know they are in one. Neighbours are
            // found by lot adjacency: anything sharing an edge with this lot hides that side,
            // so only the ends need a finished flank.
            var lots = new List<Place>();
            foreach (var place in world.AllPlaces)
                if (Stacked(KindOf(place))) lots.Add(place);

            int pieces = 0;
            foreach (var place in lots)
            {
                // A HOUSE IS NOT A SHORT TOWNHOUSE. Everything on `lots` used to go through
                // Townhouse, which stacks two to four storeys of the CITY module family - and
                // the moment every dwelling in the village became its own lot, that put
                // five-storey brick tenements with fire escapes down both sides of Holmes
                // Avenue. A village street of them is the single worst thing this map has
                // looked like, and it was one function call away from being right: CitySuburb
                // had the recipe all along - Bayhouse, one storey or two - and nothing was
                // reading it because suburbs used to be cells rather than places.
                bool leftEnd = !HasNeighbour(lots, place, -1);
                bool rightEnd = !HasNeighbour(lots, place, +1);
                pieces += Townhouse(root.transform, place, place.Bounds, leftEnd || rightEnd, FacingOf(place));
            }

            foreach (var place in world.AllPlaces)
            {
                // One bought building per kind. The lot in city.txt is sized from the prefab,
                // never the other way round - these are the pack's own footprints rounded up.
                // WHAT A TOWN OF TWELVE HUNDRED ACTUALLY HAS ITS OWN BUILDING FOR.
                //
                // The pack ships a casino, a cinema, a hospital, a police station, a car wash, a
                // public convenience and a skyscraper, and the retired village used all of them. Rossville
                // has none: what the 1913 sheet shows at the crossing is a continuous terrace of
                // one- and two-storey brick shopfronts, and the bank, the drug store and the
                // motion picture house are UNITS IN THAT TERRACE, not standalone landmarks.
                // Those kinds are gone from this switch and fall through to the massing
                // grammars, where `massing shopfront` now means a Main Street block.
                //
                // What is left here is the handful of things that really do stand alone on their
                // own ground in a farm town: the fire house, the water tower, the grain
                // elevator, the filling station, and the farm buildings.
                string prefab = KindOf(place) switch
                {
                    // THE SCHOOL LEFT THIS SWITCH ON 2026-08-11, ruled by the owner off the
                    // wireframe of its own generated plan: "build your own like the houses."
                    // School_City is a three-mass city brick and it stood on Unit 30's campus
                    // like a postcard from the wrong town - while SchoolMassing, the `spine`
                    // corridor grammar and the `school` frontage sat fully wired beneath it,
                    // and the classroom run he approved was already carved into the grid of
                    // every build. Both campuses generate now, the same kind on both sides of
                    // Chicago Street, for the same reason school2 died: the enum, the day plans
                    // and the drawing must agree about what a school is.
                    //
                    // (school2's own lesson stays on kinds.txt line 154: a second kind cost
                    // Rossville's 165 children their school, and not one of them ever went.)
                    "firestation" => Whole + "Fire_Station_City.prefab",
                    "gasstation"  => Whole + "Gas_Station_City.prefab",

                    // The two things on a village skyline, and the FARM tower is the right one
                    // however wrong that sounds. `Water_Tower_City` lives under Props City/Roof
                    // Props and is a tank that sits ON a roof - a few metres of cistern, which
                    // is a city thing. `Tower_Water_Farm` is the tank on four legs standing in
                    // its own ground, which is what a village puts its name on and what you see
                    // from the section road two miles out. Measured by where the pack files
                    // them, not by what they are called.
                    "watertower"  => Land + "Tower_Water_Farm.prefab",
                    "elevator"    => BestFit(Land, new[]
                                     { "Silo_Grain_New", "Silo_Grain_Old" }, place.Bounds),

                    // The country. `farm` is the kind the old village already had, so the farmhouse
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
                // the school generates with the town's own massing - see the prefab switch
                case "firestation": case "gasstation":
                case "watertower": case "elevator":
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
        /// <summary>
        /// One detached house on its own lot, which is what a village is made of.
        ///
        /// Bayhouse and one storey or two, which is exactly what CitySuburb has always built -
        /// the recipe is lifted rather than re-invented, because it was already right and the
        /// only reason it was not being used is that a suburb used to be a 60x60 CELL that
        /// generated its houses, and a house is now a PLACE with an address. `only: "Bayhouse"`
        /// picks the family whose unfaced tail is about a metre rather than three, which on a
        /// detached house is the difference between a back garden and a blank wall.
        ///
        /// A bungalow one time in four. An estate built over a decade is not all one height,
        /// and a run of identical houses reads as a terrace even when they are ten metres apart.
        /// </summary>
        // A DWELLING USED TO GET A PACK BAYHOUSE HERE, one storey or two. It does not any more.
        //
        // The pack's houses are generic American suburban models and Rossville's are not: the
        // 1913 Sanborn sheets show 1 and 1.5 storey frame houses, L or T shaped with an ell
        // running back and a porch across the front, at a median 97 m2 footprint. None of that is
        // in a Bayhouse. So dwellings are no longer claimed by Handles(), which lets RoofBuilder
        // build them from the frame grammars instead - see HouseLayers, which picks which of the
        // three date-layers each lot gets from where the lot stands.
        //
        // CitySuburb still calls Stack() directly with only:"Bayhouse" for its own cells; that is
        // a different generator and is left alone.

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
        /// which is why every block in Rossville had housing at street level and the downtown
        /// read as tall flats rather than as a downtown.
        ///
        /// Only Squarehouse has them, so a market lot is a Squarehouse whatever the lot rolled:
        /// Bayhouse is seven metres deep against Squarehouse's nine, and a market ground floor
        /// under Bayhouse storeys would step in and out at the first floor.
        /// </param>
        /// <param name="only">
        /// Force one family rather than letting the lot roll for it. CitySuburb passes
        /// "Bayhouse" and the reason is measured rather than aesthetic: `Squarehouse_Bottom_A`
        /// carries three metres of unfaced `M_Universal_A` past the end of its brick and
        /// `Bayhouse_Bottom_A` about one. `Seat(dressedOnly: true)` puts the brick on the
        /// building line, which sends that tail out of the BACK - invisible in a terrace, where
        /// the block interior is built over it, and three metres of blank cream in a back garden,
        /// which is the one thing a suburb is mostly made of. Bayhouse is also 7m deep against 9
        /// and has a bay front, which is the register anyway.
        /// </param>
        public static int Stack(Transform parent, Place place, TileRect lot, float yaw,
                                int storeys, bool market, out GameObject house,
                                string only = null)
        {
            string family = only ?? (market || SquareAt(lot) ? "Squarehouse" : "Bayhouse");

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
            var at = new Vector3(door.X, ElevationGrid.HeightAt(door.X, door.Y), -door.Y) + outward * 4.5f;

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

            // SEATED ON THE REAL GROUND, not wherever the parent transform happened to be. Every
            // building used to inherit y=0 from its GameObject's default local position and never
            // have it touched again - Seat only ever moved x and z - so the whole town stood dead
            // level regardless of what the terrain under it did. Sampled at the lot's own centre,
            // which is what the building's footprint actually stands on.
            pos.y = ElevationGrid.HeightAt(lot.X + lot.W / 2f, lot.Y + lot.H / 2f);

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

            return Storey(go, b, sits);
        }

        /// <summary>
        /// How far up the NEXT section goes - the top of this one's FLOOR, not the top of whatever
        /// happens to stick out of it.
        ///
        /// This used to return the section's whole vertical extent, and for four of the kit's five
        /// pieces that is the same number. `Squarehouse_Bottom_A` and `Bayhouse_Bottom_A` are the
        /// exception: they are semi-basements with an AREA RAILING round the top, and the railing
        /// stands a metre proud of the slab. Measured:
        ///
        ///     Squarehouse_Bottom_A   floor at 2.00m, extent 3.01m   ->  1.01m of railing
        ///     Bayhouse_Bottom_A      floor at 2.00m, extent 2.68m   ->  0.68m
        ///     every other section    floor and extent identical     ->  0.00m
        ///
        /// So the storey above was being stood on the railing tops, and the whole building above
        /// the basement floated a metre clear of it. The visible symptom is the one that was
        /// reported: THE FRONT STEPS RISE TO A LANDING AND THE DOOR IS A METRE ABOVE WHERE THEY
        /// STOP. The stoop belongs to the bottom section and the door to the entrance floor, and
        /// they are modelled to meet exactly - it was the stacking that pushed them apart. It only
        /// showed on residential frontages because a shopfront ground floor is a Market piece,
        /// which has no railing and so no gap.
        ///
        /// THE FLOOR IS FOUND, NOT LISTED. A railing is thin and carries almost no horizontal
        /// area; a floor slab is the opposite. So this takes the highest sizeable UPWARD-FACING
        /// surface, which is a physical question about the mesh rather than a table of section
        /// names that would go stale the moment the kit gained a piece. Same lesson as the road
        /// tiles and the parking bays: measure the model.
        /// </summary>
        private static float Storey(GameObject section, Bounds raw, float baseY)
        {
            // A slab has to be a real floor, not a window cill or a doorstep: at least a sixth of
            // the section's own plan, and never less than a couple of square metres.
            float wanted = Mathf.Max(2f, raw.size.x * raw.size.z * 0.15f);

            var area = new Dictionary<int, float>();

            foreach (var mf in section.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;

                var verts = mesh.vertices;
                var tris = mesh.triangles;
                var xform = mf.transform;

                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    var a = xform.TransformPoint(verts[tris[t]]);
                    var b = xform.TransformPoint(verts[tris[t + 1]]);
                    var c = xform.TransformPoint(verts[tris[t + 2]]);

                    var cross = Vector3.Cross(b - a, c - a);
                    float size = cross.magnitude * 0.5f;
                    if (size < 0.0001f) continue;
                    if (cross.normalized.y < 0.9f) continue;      // not something you stand on

                    int at = Mathf.RoundToInt((a.y + b.y + c.y) / 3f * 20f);   // 5cm bands
                    area[at] = area.TryGetValue(at, out float had) ? had + size : size;
                }
            }

            float top = float.MinValue;
            foreach (var band in area)
                if (band.Value >= wanted) top = Mathf.Max(top, band.Key / 20f);

            // Nothing that reads as a floor - the roof cap, for one - so fall back to the extent,
            // which is what every section used to get and is right when nothing stands proud.
            return top > float.MinValue ? Mathf.Max(0.1f, top - baseY) : raw.size.y;
        }

        /// <summary>
        /// Swap the pack's glazing for Rossville's, which is the one thing about these buildings
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
            // This used to swap every material named Glass for Rossville's WindowGlass, to fix
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

            // Ground level here is the REAL local terrain, not a literal zero - a landmark on a
            // lot at the edge of the map's 24m of relief used to have its foundation floating or
            // buried by however far that lot's real height differed from the crossing's.
            float groundY = ElevationGrid.HeightAt(lot.X + lot.W / 2f, lot.Y + lot.H / 2f);
            var want = new Vector3(lot.X + lot.W / 2f, groundY, -(lot.Y + lot.H / 2f));
            var drift = b.center - go.transform.position;
            go.transform.position = new Vector3(want.x - drift.x,
                                                go.transform.position.y + (groundY - b.min.y),
                                                want.z - drift.z);
            return go;
        }
#endif
    }
}
