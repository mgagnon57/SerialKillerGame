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
            if (_cache.TryGetValue(name, out var cached)) return cached;

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

            _cache[name] = tex;
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
            if (_packCache.TryGetValue(assetPath, out var cached)) return cached;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            _packCache[assetPath] = tex;
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
                        material.SetTexture("_OcclusionMap", ao);
                        material.SetTextureScale("_OcclusionMap", scale);
                        if (material.HasProperty("_OcclusionStrength")) material.SetFloat("_OcclusionStrength", 1f);
                    }

                    return;
                }
            }
#endif
            Apply(material, name, tilingMetres);
        }

        public static void ReportOnce()
        {
            if (_reported) return;
            _reported = true;

            if (!System.IO.Directory.Exists(Directory))
            {
                Debug.Log($"No surface textures at {Directory} - everything is flat colour. "
                        + "Run `dotnet run --project tools/Noir.Sim -- tiles` to generate a set.");
                return;
            }

            int found = 0;
            foreach (var kv in _cache) if (kv.Value != null) found++;
            // "LOOSE", NOT "LOADED", AND IT IS THE MOST USEFUL LINE IN THE BUILD LOG.
            //
            // This counts what fell back to the PROCEDURAL placeholder because ApplyPack found no
            // pack set, or found one and could not load it. It is therefore a failure count, not
            // an inventory, and reading it as an inventory is how a silent fallback survives:
            // before the roofs were moved onto the pack's shingle sets it read SEVEN - the four
            // English coverings plus water, wall and brick - and looked exactly like success.
            //
            // THREE is the healthy number: water, which the pack has nothing tileable for, and
            // wall and brick, which have no pack set yet. Anything higher means a pack path
            // silently failed and something in the town is still a flat placeholder.
            var loose = new List<string>();
            foreach (var kv in _cache) if (kv.Value != null) loose.Add(kv.Key);
            loose.Sort();

            Debug.Log($"Surface textures: {found} loose ({string.Join(", ", loose)}) - "
                    + "everything else is on a real pack set. Three is healthy; more means a "
                    + "pack path failed and fell back to a flat placeholder.");
        }
    }
}
