using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Noir.Unity
{
    /// <summary>
    /// Compiles every shader the town is about to use, BEFORE the first frame, on purpose.
    ///
    /// WHY. A shader variant is compiled the first time something needs to draw with it. In the
    /// editor that happens mid-frame, and until it finishes the frame does not go out - so the
    /// cost lands as a freeze at whatever moment the camera first sees a particular material. It
    /// is not a slow game; it is a game stopping dead to build something it needed a moment ago.
    ///
    /// The owner named the fix from having seen it elsewhere: "it knows whether to recompile and
    /// does it up front before game starts". That is exactly right, and it is what every
    /// shipping game's "compiling shaders" screen is doing.
    ///
    /// HOW. After the town is built and before anything is handed over, walk every renderer,
    /// collect the distinct (shader, keywords) pairs actually in use, and warm them in one
    /// deliberate block. The cost does not go away - it is the same compilation either way - but
    /// it happens ONCE, at a moment where a pause is expected, instead of arriving as a hitch
    /// three seconds into looking at something.
    ///
    /// IT IS CHEAP WHEN THERE IS NOTHING TO DO. Compiled variants are cached to disk in Library,
    /// so on the second and every later run this costs milliseconds and reports zero. A run that
    /// reports a large number is telling you the material set changed - which, during a session
    /// spent editing rendering code, it does constantly.
    /// </summary>
    public static class ShaderWarmup
    {
        /// <summary>
        /// Warm everything drawable under a root. Returns how long it took, in milliseconds.
        /// </summary>
        public static float Run(GameObject root)
        {
            if (root == null) return 0f;

            float started = Time.realtimeSinceStartup;

            // Distinct materials only. A town of 300 renderers shares a couple of dozen
            // materials, and warming the same variant three hundred times is three hundred
            // dictionary hits and one compile - but the dictionary hits are free and the
            // bookkeeping below is not, so do it once.
            var materials = new HashSet<Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var shared = r.sharedMaterials;
                if (shared == null) continue;
                foreach (var m in shared)
                    if (m != null && m.shader != null) materials.Add(m);
            }

            var collection = new ShaderVariantCollection();
            int added = 0, refused = 0;

            foreach (var m in materials)
            {
                // The keywords the material is actually set up with. A variant asked for with
                // keywords nothing uses is a variant that does not exist, and Add throws on it -
                // hence the guard rather than a blanket sweep of every possible combination,
                // which for URP is tens of thousands and would take minutes.
                var keywords = m.shaderKeywords ?? new string[0];

                foreach (var pass in Passes)
                {
                    try
                    {
                        var variant = new ShaderVariantCollection.ShaderVariant(m.shader, pass, keywords);
                        if (collection.Add(variant)) added++;
                    }
                    catch (System.ArgumentException)
                    {
                        // This shader has no such pass. Ordinary, and not worth a log line each.
                        refused++;
                    }
                }
            }

            collection.WarmUp();

            float ms = (Time.realtimeSinceStartup - started) * 1000f;
            Debug.Log($"[shaders] warmed {added} variants across {materials.Count} materials "
                    + $"in {ms:0} ms ({refused} passes the shader does not have). "
                    + (ms > 500f
                        ? "That block is the compile you would otherwise have hit as a freeze "
                        + "mid-frame; it is cached now and the next run will be instant."
                        : "Cached from a previous run - nothing to compile."));

            // Destroy is a play-mode call and throws in edit mode, where the offline renderers
            // and the warm-ahead tooling both run this.
            if (Application.isPlaying) Object.Destroy(collection);
            else Object.DestroyImmediate(collection);
            return ms;
        }

        /// <summary>
        /// The passes worth asking for. ScriptableRenderPipeline covers URP's own lit and unlit
        /// passes, which is nearly everything here; ShadowCaster is asked for separately because
        /// a shadow pass compiles as its own variant and is exactly what stalls the first frame
        /// that a light touches new geometry.
        /// </summary>
        private static readonly PassType[] Passes =
        {
            PassType.ScriptableRenderPipeline,
            PassType.ShadowCaster,
            PassType.Normal,
        };
    }
}
