using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// Loads tiling surface textures from Content/textures/ and binds them to materials.
    ///
    /// Loose PNGs read straight off disk, exactly like the tile art before it: no import
    /// settings, no .meta files, no Inspector work. Drop a file in, press Play, see it. Delete
    /// it and that material goes back to flat colour.
    ///
    /// Every material keeps its base colour and TINTS the texture, so a bought texture set
    /// inherits the village's palette rather than fighting it - and a greyscale set works
    /// straight away.
    /// </summary>
    public static class SurfaceTextures
    {
        private static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();
        private static bool _reported;

        public static string Directory => Path.Combine(ContentLoader.Root, "textures");

        /// <summary>Tiling in world units. 6 means the texture repeats every 6 metres.</summary>
        public const float TilingMetres = 4f;

        public static Texture2D Load(string name)
        {
            // A MISS IS NOT AN ANSWER. `_cache[name] = tex` ran on every path, including the one
            // where the file did not exist and `tex` was never assigned - so a texture dropped
            // into Content/textures/ after any edit-mode tool had touched Materials3D stayed
            // invisible for the whole session, silently, because Apply just returns on null. The
            // class header's "drop a file in, press Play, see it" was never the broken case:
            // pressing Play reloads the domain and clears this. The broken case is the tool path
            // - Snapshot, CityShot, RoadSheet, HouseProto, GroundShot, LayerShot, MapAudit - which
            // never enters Play at all.
            //
            // `cached != null` is UnityEngine.Object's OVERLOADED ==, deliberately: it also
            // reports a texture Unity has destroyed under us. Do not "tidy" it into `is not null`
            // or ReferenceEquals - either silently reverts half of this.
            //
            // The retry costs one File.Exists per absent name per domain. The materials above are
            // cached, so this runs about fourteen times in a session, not per frame.
            if (_cache.TryGetValue(name, out var cached) && cached != null) return cached;

            string path = Path.Combine(Directory, name + ".png");
            Texture2D tex = null;

            if (File.Exists(path))
            {
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true)
                    {
                        wrapMode = TextureWrapMode.Repeat,
                        filterMode = FilterMode.Bilinear,
                        anisoLevel = 4,
                        name = name
                    };
                    if (!tex.LoadImage(bytes)) { Object.Destroy(tex); tex = null; }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"Could not read {path}: {ex.Message}");
                    tex = null;
                }
            }

            if (tex != null) _cache[name] = tex; else _cache.Remove(name);
            return tex;
        }

        /// <summary>
        /// Bind a surface texture to a material, if one exists for that name.
        ///
        /// The base colour is forced to WHITE when a texture is bound. A texture multiplies
        /// the base colour, so leaving a hue there multiplies two mid-tones together and the
        /// result is far darker than either - which is exactly what happened, and it made the
        /// whole village look like it was under a tarpaulin. Texture carries the colour;
        /// material carries white. That is also what any bought PBR set expects.
        /// </summary>
        public static void Apply(Material material, string name, float tilingMetres = TilingMetres)
        {
            var tex = Load(name);
            _bound[name] = tex == null ? Missing : "loose";
            if (tex == null || material == null) return;

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);

            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", tex);
                material.SetTextureScale("_BaseMap", Vector2.one / tilingMetres);
            }
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", tex);
                material.SetTextureScale("_MainTex", Vector2.one / tilingMetres);
            }
        }

        /// <summary>
        /// Real PBR ground sets already owned in the Universal Pack, mapped onto the same name
        /// keys ForTerrain hands to Apply. A name with no entry here - "water", currently, which
        /// the pack has nothing tileable for - falls straight back to the procedural placeholder.
        ///
        /// "field" and "path" deliberately do not share a texture even though both are dirt:
        /// Ground_Dirt_Stubble reads as a dry, worked field at a distance; Ground_Dirt_Flat reads
        /// as a worn track underfoot. The same file for both would make a farm track disappear
        /// into the field it crosses.
        /// </summary>
        private static readonly Dictionary<string, (string Albedo, string Normal, string Ao)> _packSets =
            new Dictionary<string, (string, string, string)>
        {
            ["grass"] = ("Assets/polyperfect/Poly Universal Pack/Textures/Nature/Grass_A_Alb.png",
                         "Assets/polyperfect/Poly Universal Pack/Textures/Nature/Grass_A_Nrm.png",
                         "Assets/polyperfect/Poly Universal Pack/Textures/Nature/Grass_A_AO.png"),
            ["churchyard"] = ("Assets/polyperfect/Poly Universal Pack/Textures/Nature/Grass_A_Alb.png",
                         "Assets/polyperfect/Poly Universal Pack/Textures/Nature/Grass_A_Nrm.png",
                         "Assets/polyperfect/Poly Universal Pack/Textures/Nature/Grass_A_AO.png"),
            ["field"] = ("Assets/polyperfect/Poly Universal Pack/Textures/Farm/Ground_Dirt_Stubble_Alb.png",
                         "Assets/polyperfect/Poly Universal Pack/Textures/Farm/Ground_Dirt_Stubble_Nrm.png",
                         "Assets/polyperfect/Poly Universal Pack/Textures/Farm/Ground_Dirt_Stubble_AO.png"),
            ["wood"] = ("Assets/polyperfect/Poly Universal Pack/Textures/Nature/Dirt_A_Alb.png",
                         "Assets/polyperfect/Poly Universal Pack/Textures/Nature/Dirt_A_Nrm.png",
                         "Assets/polyperfect/Poly Universal Pack/Textures/Nature/Dirt_A_AO.png"),
            ["road"] = ("Assets/polyperfect/Poly Universal Pack/Textures/City/Asphalt_A_City_Alb.png",
                         "Assets/polyperfect/Poly Universal Pack/Textures/City/Asphalt_A_City_Nrm.png",
                         null),
            ["path"] = ("Assets/polyperfect/Poly Universal Pack/Textures/Farm/Ground_Dirt_Flat_Alb.png",
                         "Assets/polyperfect/Poly Universal Pack/Textures/Farm/Ground_Dirt_Flat_Nrm.png",
                         "Assets/polyperfect/Poly Universal Pack/Textures/Farm/Ground_Dirt_Flat_AO.png"),
            ["floor"] = ("Assets/polyperfect/Poly Universal Pack/Textures/City/Concrete_A_City_Alb.png",
                         "Assets/polyperfect/Poly Universal Pack/Textures/City/Concrete_A_City_Nrm.png",
                         null),

            // ---- WHAT THE TOWN IS BUILT OF ------------------------------------------------
            //
            // Clapboard for the houses, brick for Main Street. See Materials3D.Walls: the
            // clapboard albedo is pure white with the board lines drawn into it, so the material
            // tint IS the paint and it needs no normal map (it ships without one). The brick
            // carries its colour and its coursing lives in a normal map - which is exactly why
            // the plan refused to bind it, "inert without tangents", and exactly what ROOF-0
            // fixed.
            ["wall"] = ("Assets/polyperfect/Poly Universal Pack/Textures/Buildings/Walls/Wall_Planks_Horizontal_B_Farm_Alb.png",
                         null, null),
            ["brick"] = ("Assets/polyperfect/Poly Universal Pack/Textures/City/Wall_Brick_A_City_Alb.png",
                         "Assets/polyperfect/Poly Universal Pack/Textures/City/Wall_Brick_A_City_Nrm.png",
                         null),

            // ---- THE ROOFS ----------------------------------------------------------------
            //
            // THREE ALBEDOS AND FOUR COVERINGS, and the arithmetic is why. The plan for this work
            // said the owner's settled mix "happens to match the four shades the pack already
            // ships, so no colour has to be invented". Measured, it does not:
            //
            //     Roof_Shingles_A_Farm_Alb   mean (64,64,64)   span 12   neutral mid grey
            //     Roof_Shingles_B_Farm_Alb   mean (63,62,61)   span 24   the SAME value as A
            //     Roof_Shingles_C_Farm_Alb   mean (105,70,42)  span 79   brown
            //     Roof_Shingles_D_Farm_Alb   mean (78,80,70)   span 57   GREEN cast - ruled out
            //     Roof_Shingles_E_Farm_Alb   mean (44,44,45)   span 21   charcoal, NO NORMAL MAP
            //
            // A and B are the same tone, D is the green he specifically excluded, and the only
            // true charcoal in the pack ships without the normal map that carries all the shingle.
            // So charcoal and brown-black are TINTED - see Materials3D.Roofing - and B is used for
            // charcoal rather than A because its albedo carries twice the tonal variation, which
            // is what stops a whole street of dark roofs reading as one flat shape.
            //
            // THE SPANS ARE THE POINT. Twelve levels out of 255 is not a texture, it is a colour:
            // every course line, every tab edge and every granule is in the NORMAL map, which does
            // nothing at all without a tangent stream. See MeshChunks.Emit.
            ["roof_shingle_grey"] = (Roofs + "Roof_Shingles_A_Farm_Alb.png",
                         Roofs + "Roof_Shingles_A_Farm_Nrm.png",
                         Roofs + "Roof_Shingles_A_Farm_AO.png"),
            ["roof_shingle_charcoal"] = (Roofs + "Roof_Shingles_B_Farm_Alb.png",
                         Roofs + "Roof_Shingles_B_Farm_Nrm.png",
                         Roofs + "Roof_Shingles_B_Farm_AO.png"),
            ["roof_shingle_brown"] = (Roofs + "Roof_Shingles_C_Farm_Alb.png",
                         Roofs + "Roof_Shingles_C_Farm_Nrm.png",
                         Roofs + "Roof_Shingles_C_Farm_AO.png"),
            ["roof_shingle_black"] = (Roofs + "Roof_Shingles_C_Farm_Alb.png",
                         Roofs + "Roof_Shingles_C_Farm_Nrm.png",
                         Roofs + "Roof_Shingles_C_Farm_AO.png"),

            // BUILT-UP TAR AND GRAVEL, off the SAND set, because the pack ships no gravel map at
            // all - the plan for this work assumed one. Sand is the only genuinely granular sheet
            // in it: span 141 levels against Rock's 29, and a real normal map. It is warm, so
            // Materials3D neutralises it with a per-channel tint rather than a grey one.
            ["roof_builtup"] = ("Assets/polyperfect/Poly Universal Pack/Textures/Nature/Sand_Alb.png",
                         "Assets/polyperfect/Poly Universal Pack/Textures/Nature/Sand_Nrm.png",
                         "Assets/polyperfect/Poly Universal Pack/Textures/Nature/Sand_AO.png"),
        };

        private const string Roofs =
            "Assets/polyperfect/Poly Universal Pack/Textures/Buildings/Roofs/";

#if UNITY_EDITOR
        private static readonly Dictionary<string, Texture2D> _packCache = new Dictionary<string, Texture2D>();

        private static Texture2D LoadPackTexture(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            // Same reason as Load, and one worse: AssetDatabase returns null while an asset is
            // still IMPORTING, which is the state polyperfect is in for minutes after a reimport
            // of 1.4 GB - and the null was then frozen for the session, so the whole ground stayed
            // on the 256px placeholders with nothing said. A reimport ALSO destroys the old
            // Texture2D, leaving this dictionary holding a dead reference for the next material
            // built. Both are caught by not trusting a null hit; see the note on `!= null` above.
            if (_packCache.TryGetValue(assetPath, out var cached) && cached != null) return cached;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (tex != null) _packCache[assetPath] = tex; else _packCache.Remove(assetPath);
            return tex;
        }
#endif

        /// <summary>
        /// Bind a real PBR ground set - albedo, normal and ambient occlusion, where the pack has
        /// all three - instead of the flat procedural placeholder. Falls back to <see cref="Apply"/>
        /// for any name not in <see cref="_packSets"/>, or if the mapped file cannot be found.
        ///
        /// EDITOR-ONLY, same reason as CityGreenery's prefab lookups: this reads the pack through
        /// AssetDatabase rather than copying its files into Content/textures/, so nothing bought
        /// gets duplicated into a tracked folder, and a build player without AssetDatabase falls
        /// straight back to Apply.
        /// </summary>
        /// <param name="tint">What to multiply the bound albedo by. WHITE for ground, where the
        /// texture carries the colour and a hue in the material would multiply two mid-tones into
        /// something far darker than either - the fault that once made the whole village look like
        /// it was under a tarpaulin. NOT white for the roofs: the pack ships three usable shingle
        /// albedos and the town needs four coverings, so charcoal and brown-black are the grey and
        /// the brown taken down. See the roof entries in _packSets for the measurement.</param>
        public static void ApplyPack(Material material, string name, float tilingMetres = TilingMetres,
                                     Color? tint = null)
        {
#if UNITY_EDITOR
            if (material != null && _packSets.TryGetValue(name, out var set))
            {
                var albedo = LoadPackTexture(set.Albedo);
                if (albedo != null)
                {
                    var colour = tint ?? Color.white;
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", colour);

                    var scale = Vector2.one / tilingMetres;
                    if (material.HasProperty("_BaseMap"))
                    {
                        material.SetTexture("_BaseMap", albedo);
                        material.SetTextureScale("_BaseMap", scale);
                    }
                    if (material.HasProperty("_MainTex"))
                    {
                        material.SetTexture("_MainTex", albedo);
                        material.SetTextureScale("_MainTex", scale);
                    }

                    // The keyword is not optional - URP's Lit shader ignores _BumpMap without
                    // it, the same trap WindowGlass and LampGlass hit with _EMISSION.
                    var normal = LoadPackTexture(set.Normal);
                    if (normal != null && material.HasProperty("_BumpMap"))
                    {
                        material.EnableKeyword("_NORMALMAP");
                        material.SetTexture("_BumpMap", normal);
                        material.SetTextureScale("_BumpMap", scale);
                        if (material.HasProperty("_BumpScale")) material.SetFloat("_BumpScale", 1f);
                    }

                    var ao = LoadPackTexture(set.Ao);
                    if (ao != null && material.HasProperty("_OcclusionMap"))
                    {
                        // SAME RULE AS _NORMALMAP THREE LINES UP, AND THE SAME TRAP. URP's Lit
                        // shader returns occlusion 1.0 unless the keyword is on, so every one of
                        // these maps was bound and then ignored. Nothing at runtime sets it - only
                        // the material Inspector does, which a material made in code never sees.
                        //
                        // WORTH 1-5% AND NOT A PIXEL MORE, measured over the maps actually bound
                        // (means 0.985 / 0.946 / 0.967 / 0.994). These are 2048px TILING sheets:
                        // they cannot know where a tree trunk or a wall foot is, so if you are
                        // hunting missing contact shadows this is not where they went.
                        material.EnableKeyword("_OCCLUSIONMAP");
                        material.SetTexture("_OcclusionMap", ao);
                        material.SetTextureScale("_OcclusionMap", scale);
                        if (material.HasProperty("_OcclusionStrength")) material.SetFloat("_OcclusionStrength", 1f);
                    }

                    _bound[name] = "pack";
                    return;
                }
            }
#endif
            Apply(material, name, tilingMetres);
        }

        private const string Missing = "MISSING";

        /// <summary>
        /// Every name that has ASKED for a texture, and what it actually got: `pack`, `loose`, or
        /// MISSING. Keyed by name rather than counted, because a count answered four different
        /// questions and none of them was the one anybody had.
        /// </summary>
        private static readonly SortedDictionary<string, string> _bound =
            new SortedDictionary<string, string>(System.StringComparer.Ordinal);

        /// <summary>Kept so the existing call site still reads once. See <see cref="Report"/>.</summary>
        public static void ReportOnce()
        {
            if (_reported) return;
            _reported = true;
            Report(partial: true);
        }

        /// <summary>
        /// WHAT EACH NAME GOT, AND IT IS THE MOST USEFUL LINE IN THE LOG.
        ///
        /// This used to count `_cache`, which only <see cref="Load"/> fills - and `ApplyPack`, the
        /// editor path for every ground name, returns without ever calling it. So the one line in
        /// the project reporting on the texture system counted a cache the main path never filled.
        /// Measured across every log on disk it read 7 (40 runs), 1 (7), 8 (1) and 14 (4): one
        /// instrument, four answers, and none of them said which textures the town was using.
        ///
        /// `loose` is not a synonym for `broken`. Water is loose on purpose - the pack has nothing
        /// tileable for it - and on a fresh clone with no `Assets/polyperfect` EVERYTHING is loose
        /// and the town still draws. MISSING is the failure: that name kept its material's
        /// fallback colour and is drawing flat.
        ///
        /// "SO FAR" is not hedging. `Layers.RegisterLazy` builds a layer immediately when it is
        /// switched on, so with Massing on the roof and wall materials exist before this runs and
        /// with Massing off only water has been touched. Moving the call earlier was tried and
        /// reverted - see VillageMesh, where it "always printed 0 loaded ... a log line that lied
        /// about a system that was working". The pack count below is computed directly and so is
        /// order-independent, which is why the sentence leads with it.
        /// </summary>
        public static void Report(bool partial = false)
        {
            var pack = new List<string>();
            var loose = new List<string>();
            var missing = new List<string>();
            foreach (var kv in _bound)
            {
                if (kv.Value == "pack") pack.Add(kv.Key);
                else if (kv.Value == Missing) missing.Add(kv.Key);
                else loose.Add(kv.Key);
            }

#if UNITY_EDITOR
            // Every pack name, asked directly rather than counted from what happens to have been
            // built - so this half of the line means the same thing whichever layers are on.
            int resolved = 0; string firstUnresolved = null;
            foreach (var kv in _packSets)
            {
                if (LoadPackTexture(kv.Value.Albedo) != null) resolved++;
                else firstUnresolved = firstUnresolved ?? kv.Value.Albedo;
            }

            Debug.Log($"Surface textures: {resolved} of {_packSets.Count} pack names resolve; "
                    + (partial ? "so far " : "") + $"{pack.Count} bound from the pack, "
                    + $"{loose.Count} from Content/textures/ ({string.Join(", ", loose)}), "
                    + $"{missing.Count} MISSING"
                    + (missing.Count > 0 ? ": " + string.Join(", ", missing) + "." : "."));

            if (firstUnresolved != null)
                Debug.LogWarning($"Surface textures: {_packSets.Count - resolved} pack texture(s) "
                    + "did not resolve, so that ground is on the 256px placeholder with no normal "
                    + "or occlusion map. Assets/polyperfect is gitignored - re-import the pack, and "
                    + "if you have just re-imported it, wait for the import to finish and rebuild. "
                    + $"First one: {firstUnresolved}");
#else
            // THE ONLY SIGNAL THE WHITE-ROOF CLASS OF FAULT WILL EVER EMIT FROM A PLAYER. The pack
            // path is `#if UNITY_EDITOR` by design - it reads through AssetDatabase so nothing
            // bought is copied into a tracked folder - so a shipped build draws Content/textures/
            // or nothing at all, and BuildPlayer copies Content's top level only.
            Debug.Log($"Surface textures: player build, no pack path. {loose.Count} loose "
                    + $"({string.Join(", ", loose)}), {missing.Count} MISSING"
                    + (missing.Count > 0 ? ": " + string.Join(", ", missing) + "." : "."));
#endif
            if (missing.Count > 0)
                Debug.LogWarning($"Surface textures: {missing.Count} name(s) got nothing at all - "
                    + string.Join(", ", missing) + ". Each keeps its material's fallback colour "
                    + "and draws flat; see Materials3D, where every fallback is the measured mean "
                    + "of the sheet it stands in for.");
        }
    }
}
