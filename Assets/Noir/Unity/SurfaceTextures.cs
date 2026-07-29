using System.Collections.Generic;
using System.IO;
using UnityEngine;

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
