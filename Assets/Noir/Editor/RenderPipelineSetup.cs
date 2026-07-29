using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Noir.Editor
{
    /// <summary>
    /// Switches the project from URP's 2D Renderer to the Universal (3D) Renderer.
    ///
    /// This exists because it is one of the very few things that genuinely can only be done
    /// inside the editor — creating a ScriptableObject asset and pointing the pipeline at it.
    /// Rather than hand that job to a human as a click-path through Project Settings, it is an
    /// editor script: written once, run automatically, and reversible from a menu.
    ///
    /// It matters because the 2D Renderer will happily draw 3D meshes and then light none of
    /// them and cast no shadows — you get flat silhouettes and no obvious reason why.
    /// </summary>
    [InitializeOnLoad]
    public static class RenderPipelineSetup
    {
        private const string RendererPath = "Assets/Settings/Renderer3D.asset";
        private const string PipelinePath = "Assets/Settings/UniversalRP.asset";
        private const string DonePrefKey = "Noir.RenderPipelineSetup.v1";

        static RenderPipelineSetup()
        {
            // Run once per project, on load, without nagging afterwards.
            if (SessionState.GetBool(DonePrefKey, false)) return;
            SessionState.SetBool(DonePrefKey, true);

            // In batch mode there is no editor loop to defer into: -executeMethod runs and
            // -quit exits before delayCall ever fires, so the pipeline silently stays however
            // it was last saved. That is how an offline render came back with the renderer
            // still on plain Forward long after this code claimed to have changed it.
            if (Application.isBatchMode) Configure(verbose: false);
            else EditorApplication.delayCall += () => Configure(verbose: false);
        }

        [MenuItem("Noir/Use 3D Renderer")]
        public static void ConfigureFromMenu() => Configure(verbose: true);

        private static void Configure(bool verbose)
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                if (verbose) Debug.LogError($"No URP asset at {PipelinePath}.");
                return;
            }

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                Directory.CreateDirectory("Assets/Settings");
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = "Renderer3D";
                AssetDatabase.CreateAsset(renderer, RendererPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"Noir: created {RendererPath}");
            }

            // The renderer list and default index are serialised fields with no public API, so
            // they are edited through SerializedObject. That is also the version-safe route:
            // the field names have outlived several URP releases, the C# surface has not.
            var so = new SerializedObject(pipeline);
            var list = so.FindProperty("m_RendererDataList");
            var defaultIndex = so.FindProperty("m_DefaultRendererIndex");

            if (list == null || defaultIndex == null)
            {
                Debug.LogError("Noir: could not find the renderer list on the URP asset. "
                             + "Set it by hand: select Assets/Settings/UniversalRP.asset and add "
                             + "Renderer3D as the default renderer.");
                return;
            }

            int found = -1;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == renderer) { found = i; break; }

            if (found < 0)
            {
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = renderer;
                found = list.arraySize - 1;
            }

            bool changed = defaultIndex.intValue != found;
            defaultIndex.intValue = found;
            so.ApplyModifiedPropertiesWithoutUndo();

            changed |= ConfigureLighting(pipeline, renderer);

            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();

            // Make sure the pipeline is actually the one in use.
            if (GraphicsSettings.defaultRenderPipeline != pipeline)
            {
                GraphicsSettings.defaultRenderPipeline = pipeline;
                changed = true;
            }
            for (int q = 0; q < QualitySettings.count; q++)
            {
                QualitySettings.SetQualityLevel(q, false);
                if (QualitySettings.renderPipeline != null && QualitySettings.renderPipeline != pipeline)
                {
                    QualitySettings.renderPipeline = pipeline;
                    changed = true;
                }
            }

            if (changed || verbose)
                Debug.Log("Noir: URP is using the 3D Universal Renderer. "
                        + "Lighting and shadows are available.");
        }

        /// <summary>
        /// Forward+ rendering, and per-pixel additional lights.
        ///
        /// This is the difference between a village with street lamps and a village with
        /// street lamps that light nothing. Plain Forward caps additional lights PER OBJECT -
        /// four, by default - and the entire floor of the village is one mesh, so out of sixty
        /// lamps exactly four could ever reach the ground. The lamps were on, the glass was
        /// glowing, and the road under them stayed black; there is no error and nothing in the
        /// logs to suggest a limit was hit.
        ///
        /// Forward+ bins lights by screen tile instead of by object, so the count stops being
        /// a per-renderer budget. The per-object limit below is set anyway, since it still
        /// applies if anything ever falls back to Forward.
        /// </summary>
        private static bool ConfigureLighting(UniversalRenderPipelineAsset pipeline,
                                              UniversalRendererData renderer)
        {
            bool changed = false;

            var rso = new SerializedObject(renderer);
            var mode = rso.FindProperty("m_RenderingMode");
            if (mode != null && mode.intValue != (int)RenderingMode.ForwardPlus)
            {
                mode.intValue = (int)RenderingMode.ForwardPlus;
                rso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(renderer);
                changed = true;
            }

            changed |= AttachPostProcessData(renderer);

            var pso = new SerializedObject(pipeline);
            changed |= SetInt(pso, "m_AdditionalLightsRenderingMode", 2);   // per pixel
            changed |= SetInt(pso, "m_AdditionalLightsPerObjectLimit", 8);
            changed |= SetBool(pso, "m_AdditionalLightShadowsSupported", false);

            // Both the sun and the snapshot's light ask for LightShadows.Soft. With this
            // pipeline flag off that request is silently discarded and every shadow in the
            // village renders hard-edged - the code reads as though soft shadows were chosen
            // and the picture disagrees, with nothing anywhere to say why.
            changed |= SetBool(pso, "m_SoftShadowsSupported", true);

            // HDR colour grading, which is what makes the tonemapper mean anything.
            //
            // In Low Dynamic Range mode URP clamps the frame to [0,1] BEFORE grading, so
            // everything brighter than white is thrown away and the tonemapper is handed a
            // picture with nothing left to roll off. The symptom is precise and misleading:
            // every lit window renders as a flat pure-white rectangle with no warmth in it
            // and no bloom around it, exactly as if no post-processing existed at all.
            changed |= SetInt(pso, "m_ColorGradingMode", 1);
            pso.ApplyModifiedPropertiesWithoutUndo();

            return changed;
        }

        /// <summary>
        /// Give the renderer the shaders post-processing is made of.
        ///
        /// This is the price of creating the renderer asset in code. Unity's own "create
        /// renderer" menu fills `postProcessData` in from the URP package; ScriptableObject.
        /// CreateInstance leaves it null, and a renderer with no PostProcessData skips the
        /// entire post-processing pass in silence. Every symptom points somewhere else: the
        /// Volume exists, the camera has renderPostProcessing set, the profile has bloom and
        /// tonemapping in it with their overrides on - and the frame comes back byte-identical
        /// to one rendered with no post-processing at all, which is exactly what it is.
        /// </summary>
        private static bool AttachPostProcessData(UniversalRendererData renderer)
        {
            if (renderer.postProcessData != null) return false;

            // Search rather than hard-code: the package folder carries a content hash that
            // changes with every URP update.
            foreach (var guid in AssetDatabase.FindAssets("t:PostProcessData"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<PostProcessData>(path);
                if (data == null) continue;

                renderer.postProcessData = data;
                EditorUtility.SetDirty(renderer);
                Debug.Log($"Noir: attached post-processing data from {path}.");
                return true;
            }

            Debug.LogWarning("Noir: no PostProcessData found; bloom and tonemapping will not "
                           + "run and lit windows will clip to flat white.");
            return false;
        }

        private static bool SetInt(SerializedObject so, string path, int value)
        {
            var p = so.FindProperty(path);
            if (p == null || p.intValue == value) return false;
            p.intValue = value;
            return true;
        }

        private static bool SetBool(SerializedObject so, string path, bool value)
        {
            var p = so.FindProperty(path);
            if (p == null || p.boolValue == value) return false;
            p.boolValue = value;
            return true;
        }

        [MenuItem("Noir/Regenerate Village Meshes")]
        public static void Regenerate()
        {
            if (Application.isPlaying) { Debug.Log("Stop play mode first."); return; }
            Debug.Log("Meshes are generated at Play time; just press Play.");
        }
    }
}
