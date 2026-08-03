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
        };

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
        public static void ApplyPack(Material material, string name, float tilingMetres = TilingMetres)
        {
#if UNITY_EDITOR
            if (material != null && _packSets.TryGetValue(name, out var set))
            {
                var albedo = LoadPackTexture(set.Albedo);
                if (albedo != null)
                {
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);

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
            Debug.Log($"Surface textures: {found} loaded from Content/textures/.");
        }
    }
}
