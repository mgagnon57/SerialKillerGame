using System.Collections.Generic;
using UnityEngine;
using Noir.Core.World;
using Terrain = Noir.Core.World.Terrain;
using FurnitureKind = Noir.Core.World.FurnitureKind;

namespace Noir.Unity
{
    /// <summary>
    /// The materials the village is built from - one per terrain, created in code.
    ///
    /// A material per terrain rather than vertex colours on a single material, because URP's
    /// standard Lit shader ignores vertex colour and the alternative is authoring a shader
    /// graph, which is editor work. Nine materials is nothing: the mesh is built with one
    /// submesh each, so it is still a handful of draw calls for the whole village.
    ///
    /// Colours are chosen a little brighter than they look here, because real lighting will
    /// darken everything - what you set is the fully-lit value, not the final one.
    /// </summary>
    public static class Materials3D
    {
        private static readonly Dictionary<Terrain, Material> _byTerrain = new Dictionary<Terrain, Material>();
        private static Material _wall, _agent;

        /// <summary>The order submeshes are built in. Index here == submesh index.</summary>
        public static readonly Terrain[] GroundOrder =
        {
            Terrain.Grass, Terrain.Field, Terrain.Wood, Terrain.Water,
            Terrain.Road, Terrain.Path, Terrain.Floor, Terrain.Churchyard,
        };

        private static Shader LitShader =>
            Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        /// <summary>
        /// How far the ground is taken towards black when the town is drawn as a plan.
        ///
        /// A plan is read off its LINES, and a line has to win against what is behind it. Against
        /// the ordinary daylight palette - 0x8E grass, 0xB0 field - a chalk lot boundary is one
        /// pale thing on another and the whole drawing turns to mush at any distance. Taken down
        /// to a tenth, the ground becomes the paper and the colours become the drawing.
        ///
        /// Not pure black: the terrain kinds still have to be told apart, so grass stays greener
        /// than field and water stays blue. It is a night-vision version of the same palette
        /// rather than a different one.
        /// </summary>
        private const float PlanGround = 0.11f;

        /// <summary>
        /// Take a ground material down to plan brightness, AFTER its texture has been applied.
        ///
        /// It has to be after, and that is the whole subtlety: SurfaceTextures.Apply sets
        /// _BaseColor to WHITE so the texture carries the colour on its own. Dimming inside
        /// Make - which is the obvious place, and where this was first written - is therefore
        /// wiped one line later by the texture, and the ground comes out full daylight green
        /// while every constant says it should be nearly black.
        ///
        /// (Which also means the comment beside that Apply call is wrong: the palette does NOT
        /// still govern the look once a texture loads. Left alone here; it is a separate thing.)
        /// </summary>
        /// <summary>
        /// Draw the ground in its real colours even in plan mode.
        ///
        /// PLAN MODE DIMS THE GROUND TO NEAR-BLACK ON PURPOSE - it is a survey drawing, and the
        /// lot lines and labels only read against a dark field. The cost is that everything
        /// GroundZoning decides about a tile - kept grass, field, hard, rough, bank, all of it
        /// driven by real county zoning and real USGS slope - is invisible while you are in it,
        /// so ground work cannot be checked without turning the whole city on.
        ///
        /// A REAL GATE RATHER THAN A TEMPORARY EDIT. The obvious way to look is to comment the
        /// dimming out and put it back afterwards, and that is how this was first done - which
        /// leaves an uncommitted change in the tree that somebody has to remember, and which
        /// already regenerated the committed snapshot in full colour once by accident. This sits
        /// beside ShowBuildings and ShowPeople because it is the same kind of switch: a thing you
        /// turn on to look at something, that costs nothing when it is off, and that no one has
        /// to clean up.
        ///
        /// Toggle it from the Noir menu, then press Play.
        ///
        /// Backed by PlayerPrefs rather than a plain static, because a static resets on every
        /// domain reload - which entering Play mode causes - so a menu item that set one would
        /// appear to work and then quietly forget between the click and the run. Read only while
        /// materials are being built, so the lookup costs nothing.
        /// </summary>
        /// ON BY DEFAULT WHILE THE WORLD IS BEING BUILT, and off in batch mode regardless.
        ///
        /// Defaulting it off meant pressing Play showed a black screen and the only way to see
        /// any of the ground work was to know about a menu item - which is indistinguishable
        /// from the work not existing, and was reported as exactly that. The person looking at
        /// this project every day should not have to opt in to seeing it.
        ///
        /// Batch mode is forced off so docs/snapshots/plan-top-down.png stays the dimmed survey
        /// drawing it has always been. Preflight regenerates that file on every run, and a
        /// default that changed it would rewrite the committed snapshot as a side effect of
        /// validating something unrelated.
        public static bool ShowGroundColour
        {
            get => !Application.isBatchMode && PlayerPrefs.GetInt(GroundColourKey, 1) == 1;
            set { PlayerPrefs.SetInt(GroundColourKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        private const string GroundColourKey = "noir.ground.colour";

        private static Material Plan(Material m)
        {
            // FlatGroundColour wins over the dimming. It exists to give the owner one readable
            // GREEN under the lot lines - "not just green grass or whatever" was the ask - and
            // dimming that to near-black produces a handsome survey drawing that is not the thing
            // he asked for. The two settings are answering different questions and this one is
            // the more specific.
            if (VillageHost.ShowBuildings || ShowGroundColour || VillageHost.FlatGroundColour
                || m == null) return m;

            var dim = new Color(PlanGround, PlanGround, PlanGround, 1f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", dim);
            if (m.HasProperty("_Color")) m.SetColor("_Color", dim);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            return m;
        }

        private static Material Make(string name, Color colour, float smoothness, float metallic = 0f)
        {
            var m = new Material(LitShader) { name = name };
            // Several hundred identical trees and fence posts share one material each, so
            // instancing turns a scary object count into a handful of draw calls.
            m.enableInstancing = true;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
            if (m.HasProperty("_Color")) m.SetColor("_Color", colour);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            return m;
        }

        public static Material ForTerrain(Terrain t)
        {
            if (_byTerrain.TryGetValue(t, out var existing)) return existing;

            Material m;
            string texture;
            switch (t)
            {
                case Terrain.Grass: m = Make("Grass", new Color32(0x8E, 0xA8, 0x76, 0xFF), 0.05f); texture = "grass"; break;
                case Terrain.Field: m = Make("Field", new Color32(0xB0, 0xA0, 0x76, 0xFF), 0.04f); texture = "field"; break;
                case Terrain.Wood: m = Make("Wood", new Color32(0x6A, 0x84, 0x62, 0xFF), 0.03f); texture = "wood"; break;
                case Terrain.Water: m = Make("Water", new Color32(0x5E, 0x8C, 0xAA, 0xFF), 0.85f); texture = "water"; break;
                case Terrain.Road: m = Make("Road", new Color32(0x9A, 0x96, 0x90, 0xFF), 0.10f); texture = "road"; break;
                case Terrain.Path: m = Make("Path", new Color32(0xA8, 0x9C, 0x86, 0xFF), 0.06f); texture = "path"; break;
                case Terrain.Floor: m = Make("Floor", new Color32(0xB4, 0x9A, 0x80, 0xFF), 0.15f); texture = "floor"; break;
                case Terrain.Churchyard: m = Make("Churchyard", new Color32(0x96, 0xA4, 0x7E, 0xFF), 0.05f); texture = "churchyard"; break;
                default: m = Make("Ground", new Color32(0x8E, 0xA8, 0x76, 0xFF), 0.05f); texture = "grass"; break;
            }

            // A texture multiplies the base colour, so the palette above still governs the look
            // and a greyscale or bought texture set inherits it rather than fighting it.
            SurfaceTextures.ApplyPack(m, texture);
            Plan(m);

            _byTerrain[t] = m;
            return m;
        }

        private static Material _pasture;

        /// <summary>
        /// The country beyond the village, tiled far coarser than a lawn.
        ///
        /// Same texture as grass, and that is the point: at the four-metre tiling that looks
        /// right on a garden, five hundred metres of open field showed the repeat as visible
        /// corduroy running to the horizon. Distance wants a bigger tile.
        /// </summary>
        public static Material Pasture
        {
            get
            {
                if (_pasture != null) return _pasture;
                _pasture = Make("Pasture", new Color32(0x8E, 0xA8, 0x76, 0xFF), 0.05f);
                SurfaceTextures.ApplyPack(_pasture, "grass", 21f);
                Plan(_pasture);
                return _pasture;
            }
        }

        /// <summary>
        /// The ground a DWELLING stands on, in the flat plan view: the same green, a quarter
        /// darker.
        ///
        /// With the buildings switched off, a house is painted as the land it stands on and
        /// disappears - which is right for judging a road against a lot line, and useless for
        /// seeing which lots are built on. This is the compromise the owner asked for: dark
        /// enough to notice, close enough in hue that it still reads as ground rather than as
        /// another kind of paint competing with the lot lines.
        ///
        /// Grass texture at the garden tiling, deliberately. A dwelling patch with no texture
        /// beside grass with one does not read as a darker patch of the same field; it reads as
        /// a hole.
        /// </summary>
        public static Material Dwelling
        {
            get
            {
                if (_dwelling != null) return _dwelling;

                // THE TINT GOES ON AFTER ApplyPack, NOT BEFORE. ApplyPack sets _BaseColor and
                // _Color to WHITE whenever it binds a texture - reasonably, since a pack's albedo
                // carries its own colour and a tint on top would double it. Set the green in
                // Make() above it, the way Pasture and Grass do, and the pack wipes it a line
                // later: the material comes out identical to grass and the shading is invisible.
                // That is exactly what happened the first time this was written.
                var tint = new Color32(0x6A, 0x7E, 0x58, 0xFF);
                _dwelling = Make("Dwelling", tint, 0.05f);
                SurfaceTextures.ApplyPack(_dwelling, "grass", 4f);
                if (_dwelling.HasProperty("_BaseColor")) _dwelling.SetColor("_BaseColor", tint);
                if (_dwelling.HasProperty("_Color")) _dwelling.SetColor("_Color", tint);

                Plan(_dwelling);
                return _dwelling;
            }
        }

        private static Material _dwelling;

        public static Material Wall
        {
            get
            {
                if (_wall != null) return _wall;
                _wall = Make("Wall", new Color32(0xC6, 0xB8, 0xA6, 0xFF), 0.05f);
                SurfaceTextures.Apply(_wall, "wall", 3f);
                return _wall;
            }
        }

        /// <summary>
        /// Ground materials for a Grass or Field tile that reads as something else once its real
        /// zoning or its real slope is asked - see GroundZoning. Not one of Noir.Core.World.
        /// Terrain's own eight kinds, because none of them is a placement anybody made; they are
        /// what the SAME fictional tile turns into once the real parcel underneath disagrees
        /// with it.
        /// </summary>
        public enum ZonedGround { Hard, Rough, Bank }

        private static readonly Dictionary<ZonedGround, Material> _byZoned = new Dictionary<ZonedGround, Material>();

        public static Material ForZoned(ZonedGround kind)
        {
            if (_byZoned.TryGetValue(kind, out var existing)) return existing;

            Material m; string texture; float tiling;
            switch (kind)
            {
                case ZonedGround.Hard:
                    // Commercial and industrial lots: packed earth and gravel, not turf - a feed
                    // store's yard or a garage's apron. Reuses the road texture's grain
                    // (hardstanding rather than lawn) at a duller, more olive tint than the road
                    // itself gets, so a commercial yard reads as worked ground beside a street
                    // rather than as more street.
                    m = Make("GroundHard", new Color32(0x8C, 0x86, 0x74, 0xFF), 0.06f);
                    texture = "road"; tiling = 4f;
                    break;
                case ZonedGround.Rough:
                    // Vacant lots: a lot mowed once a summer, weeds through bare dirt - not lawn,
                    // and not a farm field either. The path texture's own worn-dirt grain, tinted
                    // browner and flatter than the kept grass next door.
                    m = Make("GroundRough", new Color32(0x9C, 0x92, 0x66, 0xFF), 0.03f);
                    texture = "path"; tiling = 4f;
                    break;
                default:   // Bank
                    // Any tile too steep for turf to hold - a ditch side, a creek bank, whatever
                    // the sculpt tool has cut. Bare worn ground regardless of what the parcel
                    // standing on it is zoned for; see GroundZoning.BankGrade for why this is
                    // rare on a map this flat and not a slope map painted over farmland.
                    m = Make("GroundBank", new Color32(0x8A, 0x7A, 0x5C, 0xFF), 0.02f);
                    texture = "path"; tiling = 3f;
                    break;
            }

            SurfaceTextures.ApplyPack(m, texture, tiling);
            Plan(m);
            _byZoned[kind] = m;
            return m;
        }

        public static Material Agent =>
            _agent != null ? _agent : (_agent = Make("Person", Color.white, 0.12f));

        private static Material[] _roofs;

        /// <summary>
        /// The coverings a roof can have. Index here is the submesh index in the roof mesh.
        ///
        /// One roof material made the village read as an estate thrown up in a single year by
        /// a single contractor - forty identical salmon rectangles, which is the loudest "this
        /// is a diagram, not a place" signal in the whole wide shot. Four coverings fixes it
        /// for the cost of four textures, and which one a building gets is a property of the
        /// building rather than of the frame, so it stays put as you move.
        /// </summary>
        public static Material[] Roofs
        {
            get
            {
                if (_roofs != null) return _roofs;
                _roofs = new[]
                {
                    Roofing("RoofSlate", "roof_slate"),
                    Roofing("RoofTile", "roof_tile"),
                    Roofing("RoofWorn", "roof_worn"),
                    Roofing("RoofThatch", "roof_thatch"),
                    Roofing("Brick", "brick"),      // chimneys - see ChimneyIndex
                    Wall,                           // gable ends, towers - see WallIndex
                };
                return _roofs;
            }
        }

        /// <summary>
        /// Chimneys live in the roof mesh but are not roofing. Left in the building's own
        /// covering they came out slate blue or straw yellow, which is not a thing a chimney
        /// has ever been made of.
        /// </summary>
        public static int ChimneyIndex => 4;

        /// <summary>
        /// The building's own walling, carried in the roof mesh.
        ///
        /// A GABLE END is not roofing. It is the masonry of the building continued up to the
        /// ridge, and it was being drawn in roof tile - clay tiles on a vertical wall - and then
        /// in chimney brick, which is better and still wrong: brown brick against pale render.
        /// Church towers and bell-cotes are the same argument. They are walls that happen to be
        /// built by the roof pass.
        /// </summary>
        public static int WallIndex => 5;

        /// <summary>A spire is lead or slate, never brick.</summary>
        public static int SpireIndex => 0;

        private static Material Roofing(string name, string texture)
        {
            var m = Make(name, Color.white, 0.04f);
            SurfaceTextures.Apply(m, texture, 2.5f);
            return m;
        }

        /// <summary>
        /// Which covering a building has. Slate and tile are the village; worn tile is the older
        /// end of it; thatch is rare, which is what makes it worth noticing.
        /// </summary>
        public static int RoofingFor(int x, int y)
        {
            int roll = (int)(Scatter(x, y, 5309) % 100);
            if (roll < 34) return 0;   // slate
            if (roll < 70) return 1;   // tile
            if (roll < 92) return 2;   // worn tile
            return 3;                  // thatch
        }

        /// <summary>
        /// A stable hash of a position. Everything decided per-place rather than per-frame goes
        /// through here, so a building's roof and a field's trees are properties of where they
        /// are, and do not shuffle when you move the camera or reload.
        /// </summary>
        public static uint Scatter(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
                h ^= h >> 13; h *= 0x85EBCA6B; h ^= h >> 16;
                return h;
            }
        }

        private static Material _ironwork, _lampGlass;

        public static Material Ironwork =>
            _ironwork != null ? _ironwork : (_ironwork = Make("Ironwork", new Color32(0x2E, 0x2E, 0x30, 0xFF), 0.35f, 0.6f));

        /// <summary>
        /// The lantern itself. Emissive so it reads as the source of the light rather than as
        /// a box that happens to be near one - and so it is still visible at 300x speed when
        /// the point light is fading in.
        ///
        /// The emission is BLACK here and driven per-lamp from SunRig.SetLanternGlow, exactly
        /// as the window panes are. Baked into the material it burned at noon too, which was
        /// merely odd until bloom arrived and put a flare around every lamp post in the
        /// village in full sunlight. The keyword still has to be enabled here or per-instance
        /// emission is silently ignored.
        /// </summary>
        public static Material LampGlass
        {
            get
            {
                if (_lampGlass != null) return _lampGlass;
                _lampGlass = Make("LampGlass", new Color32(0x57, 0x59, 0x54, 0xFF), 0.6f);
                _lampGlass.EnableKeyword("_EMISSION");
                if (_lampGlass.HasProperty("_EmissionColor"))
                    _lampGlass.SetColor("_EmissionColor", Color.black);
                _lampGlass.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                return _lampGlass;
            }
        }

        private static Material _bag;

        public static Material Bag =>
            _bag != null ? _bag : (_bag = Make("Bag", new Color32(0x8A, 0x74, 0x50, 0xFF), 0.06f));

        private static Material _windowGlass;

        /// <summary>
        /// Window glass. Emission is driven per-pane from a MaterialPropertyBlock, so all
        /// several hundred windows in the village share one material and one batch - the
        /// keyword has to be enabled here or the per-instance emission is silently ignored.
        /// </summary>
        public static Material WindowGlass
        {
            get
            {
                if (_windowGlass != null) return _windowGlass;
                _windowGlass = Make("WindowGlass", new Color32(0x28, 0x2E, 0x38, 0xFF), 0.75f);
                _windowGlass.EnableKeyword("_EMISSION");
                if (_windowGlass.HasProperty("_EmissionColor"))
                    _windowGlass.SetColor("_EmissionColor", Color.black);
                _windowGlass.globalIlluminationFlags =
                    MaterialGlobalIlluminationFlags.RealtimeEmissive;
                return _windowGlass;
            }
        }

        private static Material _foliage, _hedge, _bark, _stone, _postbox;

        public static Material Foliage =>
            _foliage != null ? _foliage : (_foliage = Make("Foliage", new Color32(0x40, 0x5C, 0x35, 0xFF), 0.03f));

        private static Material[] _canopies;

        /// <summary>
        /// Four greens for tree canopies.
        ///
        /// One green turned a copse into a single mass with no depth to it - you could not
        /// tell where one tree stopped and the next began, which is the whole reason a wood
        /// reads as a wood. Species and age vary the colour far more than the shape does at
        /// this distance, so colour is the cheaper lever.
        /// </summary>
        public static Material[] Canopies
        {
            get
            {
                if (_canopies != null) return _canopies;
                _canopies = new[]
                {
                    Make("Canopy0", new Color32(0x3A, 0x54, 0x30, 0xFF), 0.03f),
                    Make("Canopy1", new Color32(0x4A, 0x66, 0x38, 0xFF), 0.03f),
                    Make("Canopy2", new Color32(0x58, 0x72, 0x40, 0xFF), 0.03f),
                    Make("Canopy3", new Color32(0x46, 0x60, 0x4A, 0xFF), 0.03f),
                };
                return _canopies;
            }
        }

        /// <summary>
        /// Clipped hedge - lighter than a tree canopy, because it is in full sun rather than
        /// shading itself. At the canopy colour a run of hedge read as a black wall.
        /// </summary>
        public static Material Hedge =>
            _hedge != null ? _hedge : (_hedge = Make("Hedge", new Color32(0x5E, 0x78, 0x4A, 0xFF), 0.04f));

        public static Material Bark =>
            _bark != null ? _bark : (_bark = Make("Bark", new Color32(0x4E, 0x3E, 0x2E, 0xFF), 0.03f));

        private static Material _timber, _ballast, _railSteel, _sleeper;

        /// <summary>
        /// Crushed limestone under a railroad. PALER than Stone deliberately - Stone is a wall,
        /// weathered and slightly warm, and a track bed built out of it disappeared into the
        /// grass from any distance. The bright grey ribbon is the thing that says "railroad" in
        /// an aerial view of farmland; the rails themselves are two lines a few centimetres wide
        /// and carry none of it.
        /// </summary>
        public static Material Ballast =>
            _ballast != null ? _ballast : (_ballast = Make("Ballast", new Color32(0x9E, 0x99, 0x8E, 0xFF), 0.04f));

        /// <summary>A creosoted sleeper: darker and greyer than Timber, which is a fence rail.</summary>
        public static Material Sleeper =>
            _sleeper != null ? _sleeper : (_sleeper = Make("Sleeper", new Color32(0x5A, 0x4C, 0x3E, 0xFF), 0.05f));

        /// <summary>
        /// Rail head, polished by traffic. Ironwork is a lamp column at 0x2E2E30 and reads as
        /// black; a rail in daylight is bright enough to be the one part of the track you see
        /// from a distance, which is the whole reason a line catches the eye across a field.
        /// </summary>
        public static Material RailSteel =>
            _railSteel != null ? _railSteel : (_railSteel = Make("RailSteel", new Color32(0x8A, 0x8D, 0x92, 0xFF), 0.62f, 0.85f));

        /// <summary>
        /// Weathered sawn timber - fences, benches, gateposts.
        ///
        /// Distinct from Bark, which is nearly black and correct for a trunk in its own shade.
        /// A run of fence panels in it came out as a line of black slabs standing round the
        /// allotments; from above they read as headstones, which is a long way from a fence.
        /// Cut timber that has been out in the weather for ten years is grey, not brown.
        /// </summary>
        public static Material Timber =>
            _timber != null ? _timber : (_timber = Make("Timber", new Color32(0x9A, 0x8A, 0x72, 0xFF), 0.04f));

        public static Material Stone =>
            _stone != null ? _stone : (_stone = Make("Stone", new Color32(0x8C, 0x89, 0x81, 0xFF), 0.06f));

        public static Material Postbox =>
            _postbox != null ? _postbox : (_postbox = Make("Postbox", new Color32(0x8E, 0x1F, 0x1C, 0xFF), 0.25f));

        private static Material _furniture;

        /// <summary>One shared material; per-piece colour comes from a MaterialPropertyBlock.</summary>
        public static Material Furniture =>
            _furniture != null ? _furniture : (_furniture = Make("Furniture", Color.white, 0.10f));

        /// <summary>
        /// Colour by material rather than by function - wood, fabric, porcelain, enamel. It is
        /// what lets you read a room from above without a single texture: white rectangles in a
        /// small room is a bathroom, a big pale slab against a wall is a bed.
        /// </summary>
        public static Color ColourOf(FurnitureKind kind)
        {
            switch (kind)
            {
                case FurnitureKind.Bed: return new Color32(0xD8, 0xD2, 0xC4, 0xFF);  // linen
                case FurnitureKind.Sofa: return new Color32(0x6B, 0x54, 0x48, 0xFF);  // worn fabric
                case FurnitureKind.Chair: return new Color32(0x7A, 0x5E, 0x42, 0xFF);
                case FurnitureKind.Table: return new Color32(0x8A, 0x69, 0x47, 0xFF);  // wood
                case FurnitureKind.Desk: return new Color32(0x7E, 0x60, 0x42, 0xFF);
                case FurnitureKind.Wardrobe: return new Color32(0x6E, 0x51, 0x37, 0xFF);  // dark wood
                case FurnitureKind.Dresser: return new Color32(0x78, 0x5A, 0x3E, 0xFF);
                case FurnitureKind.Cooker: return new Color32(0x4A, 0x4A, 0x4E, 0xFF);  // enamel
                case FurnitureKind.Sink: return new Color32(0xC4, 0xC8, 0xC6, 0xFF);
                case FurnitureKind.Counter: return new Color32(0x9E, 0x8E, 0x74, 0xFF);
                case FurnitureKind.Bath: return new Color32(0xE6, 0xE8, 0xE6, 0xFF);  // porcelain
                case FurnitureKind.Basin: return new Color32(0xE6, 0xE8, 0xE6, 0xFF);
                case FurnitureKind.Hearth: return new Color32(0x4E, 0x46, 0x42, 0xFF);  // stone
                default: return new Color32(0x90, 0x88, 0x7C, 0xFF);
            }
        }
    }
}
