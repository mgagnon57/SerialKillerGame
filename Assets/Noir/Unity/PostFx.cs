using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Noir.Unity
{
    /// <summary>
    /// Tonemapping, bloom and a little vignette, created in code.
    ///
    /// This exists because of what the night renders looked like without it. A lit window is
    /// an emissive surface with a value above 1, and with no tonemapper everything above 1 is
    /// simply clipped to white - so forty warm tungsten windows came out as forty identical
    /// flat white rectangles, with no glow, no falloff, and no colour left in them at all.
    /// The lighting was right; there was nothing at the end of the pipeline to bring it back
    /// into a range a screen can show.
    ///
    /// Bloom is doing real work here rather than decorating: it is the only thing that makes a
    /// lamp read as a light source rather than as a bright box, and a street of lit windows
    /// seen from the far end of the lane is the defining image this project is aiming at.
    ///
    /// Deliberately restrained. This is 1979 and the register is Zodiac, not a bloom demo.
    /// </summary>
    public static class PostFx
    {
        public static Volume Create(Transform parent)
        {
            var go = new GameObject("Post Processing");
            if (parent != null) go.transform.SetParent(parent, false);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "NoirPostFx";

            if (profile.Add<Tonemapping>() is Tonemapping tone)
            {
                tone.mode.overrideState = true;
                // Neutral rather than ACES: ACES pushes contrast and cools shadows hard, which
                // fights a palette that is already muted on purpose.
                tone.mode.value = TonemappingMode.Neutral;
            }

            if (profile.Add<Bloom>() is Bloom bloom)
            {
                bloom.threshold.overrideState = true;
                bloom.threshold.value = 0.95f;      // only genuinely emissive things bloom
                bloom.intensity.overrideState = true;
                bloom.intensity.value = 0.85f;
                bloom.scatter.overrideState = true;
                bloom.scatter.value = 0.62f;
                bloom.tint.overrideState = true;
                bloom.tint.value = new Color(1.00f, 0.93f, 0.82f);   // tungsten, not white
            }

            if (profile.Add<Vignette>() is Vignette vignette)
            {
                vignette.intensity.overrideState = true;
                vignette.intensity.value = 0.18f;
                vignette.smoothness.overrideState = true;
                vignette.smoothness.value = 0.45f;
            }

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = profile;

            return volume;
        }

        /// <summary>
        /// Turn post-processing on for a camera.
        ///
        /// A Volume in the scene does nothing on its own - the camera has to be told to run the
        /// post-processing stack, and that flag lives on URP's own camera data component rather
        /// than on Camera. Forgetting it produces exactly the same picture as having no volume,
        /// which is a difficult thing to debug by looking.
        /// </summary>
        public static void EnableOn(Camera camera)
        {
            if (camera == null) return;

            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();

            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.Medium;
        }
    }
}
