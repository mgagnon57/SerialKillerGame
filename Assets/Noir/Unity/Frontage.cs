using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Noir.Core.Contracts;
using Noir.Core.World;
using Terrain = Noir.Core.World.Terrain;

namespace Noir.Unity
{
    /// <summary>
    /// Doors, signs and shutters - the front of a building, which is the only part of it most
    /// people ever see.
    ///
    /// Walls and roofs make the village read as buildings; this is what makes it read as a
    /// village that WORKS. Three signals, in order of how far away they carry:
    ///
    ///   A DOOR says which side is the front and where a person is going to disappear. A terrace
    ///   with four doors along it is visibly four households rather than one long shed, and that
    ///   is the same fact the chimneys are already telling you from above.
    ///
    ///   A SIGN says this is a business rather than a home. It needs no text: a board hanging on
    ///   a bracket over a door has meant "public house" for three hundred years.
    ///
    ///   SHUTTERS say the village runs on something. Opening hours are already authored data and
    ///   nothing in the picture spends it - the shop is as open at four in the morning as it is
    ///   at noon. A shuttered frontage is the cheapest way to put the clock into the scene, and
    ///   the only one you can read from the far end of the street.
    ///
    /// The shutters are built here and driven from outside: <see cref="SetOpen"/> takes a place
    /// and a state, and every frontage starts open. Nothing in this file knows what time it is.
    /// </summary>
    public static class Frontage
    {
        // A door is 2.05m in a 3m storey, which leaves the head of the opening to fill in.
        private const float DoorHeight = 2.05f;
        private const float DoorWidth = 0.86f;

        /// <summary>
        /// How far anything mounted on a wall stands off it. Placed flush, a door and the wall
        /// behind it are the same plane and the depth buffer picks a winner per pixel per frame,
        /// which reads as the whole frontage crawling with static.
        /// </summary>
        private const float Proud = 0.05f;

        /// <summary>Shutters clear the window panes, which SunRig stands 0.06 out from the wall.</summary>
        private const float ShutterProud = 0.09f;

        public static void Build(WorldModel world, Transform parent)
        {
            if (world == null) return;

            // A rebuild - the editor snapshot renderer does one per shot - must not leave the
            // registry pointing at the shutters of a village that has already been thrown away.
            _shutters.Clear();

            var root = new GameObject("Frontage");
            root.transform.SetParent(parent, false);

            var doorsRoot = Group(root.transform, "Doors");
            var signsRoot = Group(root.transform, "Signs");
            var shutterRoot = Group(root.transform, "Shutters");

            var openings = new List<Tile>();
            int doors = 0, signed = 0, shuttered = 0, pieces = 0;

            foreach (var place in world.AllPlaces)
            {
                // A bought model brings its own door and windows.
                if (CityBuildings.Handles(place)) continue;

                Openings(world, place, openings);

                foreach (var tile in openings)
                {
                    var opening = FrontAt(place.Bounds, tile);
                    if (!opening.Valid) continue;
                    pieces += Doorway(doorsRoot, place, tile, opening);
                    doors++;
                }

                // Signs and shutters hang off the authored door, which is the one on the street
                // even in a building that turned out to have several.
                var front = FrontAt(place.Bounds, place.Door.IsValid ? place.Door
                                  : openings.Count > 0 ? openings[0] : Tile.None);
                if (!front.Valid) continue;

                // UNCONDITIONAL NOW, because the `frontage` column carries the decision for both
                // `dwelling` and `apartment` and this enum test could only ever see one of them.
                // Seven apartment blocks wore an anonymous plank for exactly that reason.
                {
                    int n = Sign(signsRoot, place, front);
                    if (n > 0) { signed++; pieces += n; }
                }

                if (!place.AlwaysOpen)
                {
                    int n = Shutter(shutterRoot, place, front);
                    if (n > 0) { shuttered++; pieces += n; }
                }
            }

            Debug.Log($"Frontage: {doors} front doors, {signed} signs, "
                    + $"{shuttered} shuttered frontages, {pieces:N0} pieces.");
        }

        // ---------- shutter state ----------

        private static readonly Dictionary<int, GameObject> _shutters = new Dictionary<int, GameObject>();

        public static int ShutterCount => _shutters.Count;

        /// <summary>
        /// Tell a place it is open or closed. Returns false if that place has no shutters, so a
        /// caller sweeping every place in the village can do so without a filter of its own.
        ///
        /// This is the whole interface to the frontage's one moving part. Deliberately dumb: it
        /// takes a state rather than a clock, because what counts as open is the simulation's
        /// business and it already knows the answer.
        /// </summary>
        public static bool SetOpen(PlaceId place, bool open)
        {
            if (!_shutters.TryGetValue(place.Value, out var shutter) || shutter == null) return false;
            if (shutter.activeSelf != !open) shutter.SetActive(!open);
            return true;
        }

        public static bool SetOpen(Place place, bool open) => place != null && SetOpen(place.Id, open);

        public static void SetAllOpen(bool open)
        {
            foreach (var pair in _shutters)
                if (pair.Value != null && pair.Value.activeSelf != !open)
                    pair.Value.SetActive(!open);
        }

        /// <summary>A place with no shutters is never shut, which is true of a green or a house.</summary>
        public static bool IsOpen(PlaceId place)
        {
            if (!_shutters.TryGetValue(place.Value, out var shutter) || shutter == null) return true;
            return !shutter.activeSelf;
        }

        public static bool HasShutter(PlaceId place) =>
            _shutters.TryGetValue(place.Value, out var shutter) && shutter != null;

        // ---------- where the doors are ----------

        /// <summary>
        /// Every front door in a building, read back off the finished grid rather than worked out
        /// again from the layout.
        ///
        /// A Place carries ONE authored door however many homes are in it: Ash Terrace is four
        /// households behind a single `door` line. The world builder slices the footprint and
        /// punches a doorway per home, and those extra doorways exist only as tiles by the time
        /// anything here can see them. Finding them is a matter of looking: the interior
        /// generator insets every room by a tile, so the only floor on a building's perimeter is
        /// a way in. Reading the grid also means this cannot drift out of step with the slicing
        /// rule the way a second copy of that arithmetic would.
        /// </summary>
        private static void Openings(WorldModel world, Place place, List<Tile> into)
        {
            into.Clear();

            var b = place.Bounds;
            if (b.W < 3 || b.H < 3) return;   // too thin to have been given walls at all

            for (int y = b.Y; y <= b.Bottom; y++)
            for (int x = b.X; x <= b.Right; x++)
            {
                if (y != b.Y && y != b.Bottom && x != b.X && x != b.Right) continue;
                if (IsDoorway(world.Grid, b, x, y)) into.Add(new Tile(x, y));
            }

            if (into.Count == 0 && place.Door.IsValid) into.Add(place.Door);
        }

        /// <summary>
        /// A gap one tile wide with wall either side of it. Corners are excluded because a door
        /// in one faces two ways at once, and open places - the green, the churchyard - fail the
        /// wall test on every tile and so quietly get no doors.
        /// </summary>
        private static bool IsDoorway(TileGrid grid, TileRect b, int x, int y)
        {
            if (grid.TerrainAt(x, y) != Terrain.Floor) return false;

            bool acrossX = y == b.Y || y == b.Bottom;
            bool acrossZ = x == b.X || x == b.Right;
            if (acrossX && acrossZ) return false;

            if (acrossX)
                return grid.TerrainAt(x - 1, y) == Terrain.Wall && grid.TerrainAt(x + 1, y) == Terrain.Wall;
            if (acrossZ)
                return grid.TerrainAt(x, y - 1) == Terrain.Wall && grid.TerrainAt(x, y + 1) == Terrain.Wall;
            return false;
        }

        /// <summary>
        /// One doorway resolved into world space: the point on the outside of the wall at the
        /// threshold, which way is out, and how far the frontage runs each way.
        ///
        /// Everything else in this file is written in these terms - along the wall, up it, and
        /// out through it - so no piece of geometry has to know which of four walls it is on.
        /// </summary>
        private readonly struct Front
        {
            public readonly bool Valid;
            public readonly Vector3 Face;
            public readonly Vector3 Out;
            public readonly Vector3 Along;
            public readonly float Lo, Hi;

            public Front(Vector3 face, Vector3 outward, Vector3 along, float lo, float hi)
            {
                Valid = true;
                Face = face;
                Out = outward;
                Along = along;
                Lo = lo;
                Hi = hi;
            }

            private bool AlongX => Along.x > 0.5f;

            public float Span => Hi - Lo;
            public float FaceAlong => AlongX ? Face.x : Face.z;

            /// <summary>A point on this frontage: where along the wall, how high, and how far out.</summary>
            public Vector3 At(float along, float up, float outward) =>
                Face + Along * (along - FaceAlong) + Vector3.up * up + Out * outward;

            /// <summary>Straight over the threshold, at a height and a standoff.</summary>
            public Vector3 On(float up, float outward) => At(FaceAlong, up, outward);

            /// <summary>A box size in the same terms: along the wall, up it, and through it.</summary>
            public Vector3 Size(float along, float up, float through) =>
                new Vector3(AlongX ? along : through, up, AlongX ? through : along);

            /// <summary>Trim a board so it cannot overhang the end of the building it is nailed to.</summary>
            public float Fit(float width) => Mathf.Min(width, Mathf.Max(1.2f, Span - 1.2f));

            /// <summary>Centred over the door, then slid along until it fits on the frontage.</summary>
            public float Centred(float width) => Span <= width
                ? (Lo + Hi) * 0.5f
                : Mathf.Clamp(FaceAlong, Lo + width * 0.5f, Hi - width * 0.5f);
        }

        private static Front FrontAt(TileRect b, Tile door)
        {
            if (!door.IsValid) return default;

            // Same order the world builder uses when it decides which way to punch a doorway in,
            // so a door on a corner-ish tile is understood here exactly as it was carved there.
            Vector3 outward;
            if (door.X == b.X) outward = Vector3.left;
            else if (door.X == b.Right) outward = Vector3.right;
            else if (door.Y == b.Y) outward = Vector3.forward;    // grid rows count south, so row 0 faces +Z
            else if (door.Y == b.Bottom) outward = Vector3.back;
            else return default;

            bool acrossX = Mathf.Abs(outward.z) > 0.5f;
            var along = acrossX ? Vector3.right : Vector3.forward;
            float lo = acrossX ? b.X : -(b.Y + b.H);
            float hi = acrossX ? b.X + b.W : -b.Y;

            return new Front(Space3D.ToWorld(door) + outward * 0.5f, outward, along, lo, hi);
        }

        // ---------- doors ----------

        /// <summary>
        /// A front door: the leaf, its case, the wall over the top of it, and a step.
        ///
        /// The head panel is not decoration. A doorway is carved as a full tile of floor, so the
        /// opening in the wall mesh runs the whole storey - without something in the top of it
        /// you can see daylight over every front door in the village.
        /// </summary>
        private static int Doorway(Transform parent, Place place, Tile door, Front f)
        {
            // The building's OWN wall height, not the old global one. The opening runs the whole
            // storey, so on a church at 5.5 m a panel sized for a 3 m cottage leaves a
            // two-and-a-half-metre gap of daylight over the door.
            float eaves = MassingGrammars.Of(place).Eaves;

            // Set back into the reveal by five centimetres at each end, so the panel meets the
            // wall in a shadow line instead of sharing a plane with it.
            Piece(parent, "doorhead",
                  f.On((DoorHeight + eaves) * 0.5f, -0.5f),
                  f.Size(1.0f, eaves - DoorHeight, 0.9f), Materials3D.Wall);

            Piece(parent, "doorcase", f.On(DoorHeight * 0.5f + 0.03f, -0.05f),
                  f.Size(1.0f, DoorHeight + 0.06f, 0.14f), Materials3D.Stone);

            Piece(parent, "door", f.On((DoorHeight - 0.06f) * 0.5f, Proud - 0.06f),
                  f.Size(DoorWidth, DoorHeight - 0.06f, 0.12f), DoorPaint(place, door));

            // Sunk a little below zero, because a road is worn 4cm down and a step that started
            // exactly at ground level floated over it.
            Piece(parent, "doorstep", f.On(0.02f, 0.30f),
                  f.Size(1.30f, 0.16f, 0.62f), Materials3D.Stone);

            return 4;
        }

        /// <summary>
        /// The six colours a front door in 1979 was painted. Which one a house gets is a property
        /// of where it stands, so a street is varied and stays varied - the same house has the
        /// same door every time the village is built.
        /// </summary>
        private static readonly Color32[] DoorColours =
        {
            new Color32(0x2C, 0x46, 0x36, 0xFF),   // bottle green
            new Color32(0x6A, 0x24, 0x22, 0xFF),   // oxblood
            new Color32(0x24, 0x38, 0x50, 0xFF),   // navy
            new Color32(0x3A, 0x33, 0x2E, 0xFF),   // near black
            new Color32(0xD6, 0xD0, 0xC2, 0xFF),   // painted white
            new Color32(0x7C, 0x4E, 0x24, 0xFF),   // varnished timber
        };

        private static Material DoorPaint(Place place, Tile door)
        {
            // A church, a mill and a farmhouse have doors nobody has ever painted.
            if (place.Kind == PlaceKind.Church || place.Kind == PlaceKind.Mill ||
                place.Kind == PlaceKind.Farm)
                return Materials3D.Bark;

            int i = (int)(Materials3D.Scatter(door.X, door.Y, 7717) % (uint)DoorColours.Length);
            return Paint("Door" + i, DoorColours[i], 0.28f);
        }

        // ---------- signs ----------

        /// <summary>
        /// One sign per business, and a different sign per trade.
        ///
        /// The point is to be able to tell them apart down the street with no text at all: the
        /// pub swings a board on a bracket, the shop has a fascia over its window, the school
        /// has its name cut into stone, and the surgery has a small plate by the door and would
        /// be embarrassed by anything larger.
        /// </summary>
        /// <summary>
        /// Which shopfront a building wears, keyed on the AUTHORED `frontage` column rather than
        /// on PlaceKind.
        ///
        /// This mattered the moment a kind was authored purely in content. `factory` says
        /// `frontage mill` in kinds.txt and got a plain nameboard anyway, because the enum has
        /// never heard of PlaceKind.Factory and the switch fell through to its default - the
        /// column was authored, correct, and ignored.
        ///
        /// Switching on the frontage STYLE is safe in a way switching on the kind is not: styles
        /// are a closed set this file defines, kinds are an open set content can extend. Same
        /// distinction as RoofForm against PlaceKind in RoofBuilder.
        /// </summary>
        private static int Sign(Transform parent, Place place, Front f)
        {
            var board = BoardPaint(place);
            string style = PlaceKindTable.Current.Row(place.Kind).Frontage;

            switch (style)
            {
                case "tavern":
                    return Hanging(parent, f, "sign:pub", 1.45f, 0.95f, 1.00f, 2.25f, board);

                case "shop":
                    return Fascia(parent, f, "fascia:shop", 5.0f, 0.55f, 2.35f, board)
                         + Hanging(parent, f, "sign:shop", 0.95f, 0.70f, 0.70f, 1.85f, board);

                case "post":
                    return Fascia(parent, f, "fascia:post", 3.4f, 0.50f, 2.35f, board)
                         + Plate(parent, f, "plate:post", 0.55f, 0.40f, 1.55f, Brass);

                case "church":
                    return Notice(parent, f, "noticeboard:church", board);

                case "school":
                    return Fascia(parent, f, "nameboard:school", 3.0f, 0.60f, 2.30f, Materials3D.Stone);

                case "clinic":
                    return Fascia(parent, f, "fascia:surgery", 2.6f, 0.42f, 2.35f, board)
                         + Plate(parent, f, "plate:surgery", 0.62f, 0.45f, 1.55f, Brass);

                case "garage":
                    return Fascia(parent, f, "fascia:garage", 3.6f, 0.60f, 2.30f, board)
                         + Hanging(parent, f, "sign:garage", 1.60f, 1.35f, 1.30f, 2.05f, board);

                case "hall":
                    return Fascia(parent, f, "fascia:hall", 3.2f, 0.50f, 2.35f, board)
                         + Notice(parent, f, "noticeboard:hall", board);

                case "mill":
                    return Fascia(parent, f, "nameboard:mill", 6.0f, 0.80f, 2.10f, board);

                case "farm":
                    return Fascia(parent, f, "nameboard:farm", 2.4f, 0.50f, 2.35f, board);

                // A BANK IS THE ONE SHOPFRONT ON MAIN STREET THAT MUST NOT LOOK LIKE THE GENERAL
                // STORE. Stone name band and a brass plate, the grammar `post` and `clinic`
                // already use. Bank and icecream both declared `shopfront`, which no arm here
                // answered to, so both fell to the plain plank below - along with the precinct
                // and seven apartment blocks, ten of the town's thirty-seven signs.
                case "bank":
                    return Fascia(parent, f, "nameboard:bank", 4.0f, 0.70f, 2.40f, Materials3D.Stone)
                         + Plate(parent, f, "plate:bank", 0.62f, 0.45f, 1.55f, Brass);

                // NO SIGN, AND THAT IS AN ANSWER. The content author wrote `frontage none` or
                // `frontage dwelling`; a silo and a block of flats do not carry a nameboard, and
                // falling through to one was the file ignoring what it had been told. Returning 0
                // here is what stops these reaching the default and the warning below.
                case "none":
                case "dwelling":
                    return 0;

                default:
                    // WARN ONCE PER NAME, never per building - one typo in a common kind would
                    // otherwise print three hundred lines. Same shape as MassingGrammars.For, and
                    // for the same reason CLAUDE.md states: a frontage value nothing answers to
                    // does not throw, it silently draws the wrong thing. `factory` did this once
                    // already, which is what the docstring above records.
                    if (_warnedFrontage.Add(style))
                        Debug.LogWarning($"kinds.txt: no frontage answers to '{style}', so that "
                            + "building takes the plain nameboard. There is: "
                            + string.Join(", ", Styles) + ".");
                    return Fascia(parent, f, "nameboard", 2.4f, 0.45f, 2.35f, board);
            }
        }

        private static readonly HashSet<string> _warnedFrontage = new HashSet<string>();

        /// <summary>
        /// Every value the `frontage` column may take, which is the closed set <see cref="Sign"/>
        /// answers to. SmokeTest diffs `Content/kinds.txt` against this and fails on a value
        /// nothing here can draw - the check CLAUDE.md asks for and nothing in the tree did.
        /// </summary>
        public static readonly string[] Styles =
        {
            "tavern", "shop", "post", "church", "school", "clinic", "garage", "hall",
            "mill", "farm", "bank", "none", "dwelling",
        };

        /// <summary>A board flat on the wall over the door, in the manner of a shopfront.</summary>
        private static int Fascia(Transform parent, Front f, string name, float width, float height,
                                  float sill, Material material)
        {
            width = f.Fit(width);
            Piece(parent, name, f.At(f.Centred(width), sill + height * 0.5f, Proud + 0.02f + 0.07f),
                  f.Size(width, height, 0.14f), material);
            return 1;
        }

        /// <summary>
        /// A board out on an iron bracket, at right angles to the wall. This is the one that
        /// carries: a flat sign is invisible until you are square on to it, and a village street
        /// is something you look ALONG.
        /// </summary>
        private static int Hanging(Transform parent, Front f, string name, float project,
                                   float length, float height, float centreY, Material board)
        {
            // The bracket's UNDERSIDE has to meet the top of the board, not its centre line. At
            // +0.10 with a 7 cm section the iron sat 6.5 cm clear of the board it was supposedly
            // holding up, and from the sidewalk outside the tavern you could see daylight
            // through the gap. Half the section plus a five-millimetre bite into the board, so
            // the two overlap rather than sharing a plane and shimmering along the join.
            Piece(parent, name + ":bracket", f.On(centreY + height * 0.5f + 0.03f, project * 0.5f),
                  f.Size(0.07f, 0.07f, project), Materials3D.Ironwork);

            Piece(parent, name, f.On(centreY, project - length * 0.5f - 0.10f),
                  f.Size(0.09f, height, length), board);
            return 2;
        }

        /// <summary>A small plate beside the door, on whichever side the frontage has room for it.</summary>
        private static int Plate(Transform parent, Front f, string name, float width, float height,
                                 float centreY, Material material)
        {
            float reach = 0.95f + width * 0.5f;
            float side = f.FaceAlong + reach > f.Hi - 0.3f ? -1f : 1f;
            Piece(parent, name, f.At(f.FaceAlong + side * reach, centreY, Proud + 0.03f),
                  f.Size(width, height, 0.06f), material);
            return 1;
        }

        /// <summary>
        /// A noticeboard under a little hood, beside the door. What a church and a village hall
        /// put up is not a sign for the building - it is a list of what is happening in it.
        /// </summary>
        private static int Notice(Transform parent, Front f, string name, Material board)
        {
            const float width = 1.5f, height = 1.05f;
            float along = Mathf.Clamp(f.FaceAlong + 1.9f,
                                      f.Lo + width * 0.5f + 0.4f, f.Hi - width * 0.5f - 0.4f);

            Piece(parent, name, f.At(along, 1.55f, Proud + 0.09f), f.Size(width, height, 0.14f), board);
            Piece(parent, name + ":hood", f.At(along, 1.55f + height * 0.5f + 0.06f, Proud + 0.16f),
                  f.Size(width + 0.16f, 0.09f, 0.44f), Materials3D.Bark);
            return 2;
        }

        private static Material Brass => Paint("Brass", new Color32(0xB0, 0x8A, 0x3E, 0xFF), 0.55f, 0.7f);

        /// <summary>
        /// Sign colours by trade. The pub is the one place given two, because the whole point of
        /// a pub sign is that it belongs to that pub and not to pubs in general.
        /// </summary>
        private static Material BoardPaint(Place place)
        {
            switch (PlaceKindTable.Current.Row(place.Kind).Frontage)
            {
                case "tavern":
                    return Materials3D.Scatter(place.Bounds.X, place.Bounds.Y, 4111) % 2 == 0
                        ? Paint("SignPubGreen", new Color32(0x1F, 0x3A, 0x2A, 0xFF), 0.22f)
                        : Paint("SignPubRed", new Color32(0x5E, 0x23, 0x20, 0xFF), 0.22f);

                case "shop":
                    return Paint("SignShop", new Color32(0x21, 0x40, 0x2F, 0xFF), 0.22f);

                // USPS BLUE, NOT PILLAR-BOX RED. This returned Materials3D.Postbox under a comment
                // reading "the same red as the pillar box outside it, which is not a coincidence
                // anywhere in England" - true, and the wrong country. An American post office in
                // 1991 is blue and white, and so is the collection box outside it.
                case "post":
                    return Paint("SignPost", new Color32(0x1B, 0x3A, 0x6B, 0xFF), 0.25f);

                case "bank": return Materials3D.Stone;

                case "clinic":
                    return Paint("SignSurgery", new Color32(0xE4, 0xE2, 0xD8, 0xFF), 0.30f);

                case "garage":
                    return Paint("SignGarage", new Color32(0x1E, 0x47, 0x63, 0xFF), 0.35f);

                case "farm":
                    return Paint("SignFarm", new Color32(0xD6, 0xCD, 0xBA, 0xFF), 0.20f);

                case "school": return Materials3D.Stone;
                case "mill": return Materials3D.Bark;

                default:
                    return Paint("SignBoard", new Color32(0x2B, 0x2F, 0x2A, 0xFF), 0.20f);
            }
        }

        // ---------- shutters ----------

        /// <summary>
        /// The shuttering that goes up when a place is closed, built now and hidden until it is
        /// wanted. Toggling geometry that already exists costs nothing; building it on the hour
        /// would mean a hitch in the frame every time the shop shuts.
        /// </summary>
        private static int Shutter(Transform parent, Place place, Front f)
        {
            var go = new GameObject("shutter:" + place.Name);
            go.transform.SetParent(parent, false);

            int pieces = Boarded(place.Kind) ? Boarding(go.transform, place, f) : Gates(go.transform, f);

            // Open is the resting state, so a village nobody has told the time to still looks
            // like a working one rather than like a street that has been shut down.
            go.SetActive(false);
            _shutters[place.Id.Value] = go;
            return pieces;
        }

        /// <summary>
        /// A church and a farmyard are not boarded up out of hours - the door is simply shut, and
        /// putting shuttering across the front of St Anne's would be a louder lie than saying
        /// nothing.
        /// </summary>
        private static bool Boarded(PlaceKind kind) =>
            kind != PlaceKind.Church && kind != PlaceKind.Farm;

        /// <summary>
        /// Boards across the ground floor, covering the windows and the door together, which is
        /// what closing up actually looks like. Separate boards rather than one panel: at street
        /// distance the dark lines between them are the whole difference between shuttering and
        /// a rectangle of paint.
        /// </summary>
        private static int Boarding(Transform parent, Place place, Front f)
        {
            float width = f.Fit(Mathf.Clamp(f.Span - 2f, 2.4f, 7.0f));
            float centre = f.Centred(width);
            const float height = 2.20f;

            // The garage and the mill pull down a metal roller; everything else nails up timber.
            var material = place.Kind == PlaceKind.Garage || place.Kind == PlaceKind.Mill
                ? Materials3D.Ironwork
                : Paint("Shuttering", new Color32(0x6E, 0x6A, 0x58, 0xFF), 0.12f);

            int boards = Mathf.Max(3, Mathf.RoundToInt(width / 0.62f));
            float pitch = width / boards;

            for (int i = 0; i < boards; i++)
                Piece(parent, "board",
                      f.At(centre - width * 0.5f + pitch * (i + 0.5f), 0.02f + height * 0.5f,
                           ShutterProud + 0.05f),
                      f.Size(pitch - 0.06f, height, 0.10f), material);

            Batten(parent, f, centre, width, 0.55f);
            Batten(parent, f, centre, width, 1.80f);
            return boards + 2;
        }

        private static void Batten(Transform parent, Front f, float centre, float width, float y)
        {
            Piece(parent, "batten", f.At(centre, y, ShutterProud + 0.13f),
                  f.Size(width + 0.06f, 0.13f, 0.06f), Materials3D.Bark);
        }

        /// <summary>A pair of heavy doors closed over the opening, for the places that only lock.</summary>
        private static int Gates(Transform parent, Front f)
        {
            const float height = 2.30f;
            for (int i = 0; i < 2; i++)
                Piece(parent, "gate",
                      f.At(f.FaceAlong + (i == 0 ? -0.255f : 0.255f), height * 0.5f, ShutterProud + 0.05f),
                      f.Size(0.47f, height, 0.10f), Materials3D.Bark);
            return 2;
        }

        // ---------- plumbing ----------

        private static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void Piece(Transform parent, string name, Vector3 position, Vector3 size,
                                  Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = size;

            Discard(go.GetComponent<Collider>());

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
        }

        /// <summary>
        /// Throw away a component. Object.Destroy is a no-op outside play mode, and the village
        /// is built in edit mode for the offline shots - so every collider this asked to be rid
        /// of would have survived, along with an error line each saying so.
        /// </summary>
        private static void Discard(Object doomed)
        {
            if (doomed == null) return;
            if (Application.isPlaying) Object.Destroy(doomed);
            else Object.DestroyImmediate(doomed);
        }

        private static readonly Dictionary<string, Material> _paints = new Dictionary<string, Material>();

        /// <summary>
        /// A flat painted material, cached by name.
        ///
        /// Nothing here binds a texture, so unlike the walls and the roofs these carry their
        /// colour in the material and mean it. The moment one of them does get a texture it has
        /// to go white: a texture multiplies the base colour, and two mid-tones multiply to
        /// something far darker than either.
        /// </summary>
        private static Material Paint(string name, Color colour, float smoothness, float metallic = 0f)
        {
            if (_paints.TryGetValue(name, out var existing) && existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { name = name };
            m.enableInstancing = true;   // sixty front doors in six colours is six draw calls
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
            if (m.HasProperty("_Color")) m.SetColor("_Color", colour);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);

            _paints[name] = m;
            return m;
        }
    }
}
