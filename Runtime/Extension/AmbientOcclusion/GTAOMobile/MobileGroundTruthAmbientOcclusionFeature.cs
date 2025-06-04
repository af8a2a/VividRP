using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

//reference by https://github.com/bladesero/GTAO_URP
namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature]
    [Tooltip(
        "The Ambient Occlusion effect darkens creases, holes, intersections and surfaces that are close to each other.")]
    internal class MobileGroundTruthAmbientOcclusionFeature : ScriptableRendererFeature
    {
        // Serialized Fields
        [SerializeField, HideInInspector] private Shader m_Shader = null;

        // [SerializeField]
        // private ScreenSpaceAmbientOcclusionSettings m_Settings = new ScreenSpaceAmbientOcclusionSettings();

        // Private Fields
        private Material m_Material;

        private MobileGroundTruthAmbientOcclusionPass m_SSAOPass = null;

        // Constants
        internal const string k_ShaderName = "Hidden/Universal Render Pipeline/GroundTruthAmbientOcclusion";
        internal const string k_OrthographicCameraKeyword = "_ORTHOGRAPHIC";
        internal const string k_NormalReconstructionLowKeyword = "_RECONSTRUCT_NORMAL_LOW";
        internal const string k_NormalReconstructionMediumKeyword = "_RECONSTRUCT_NORMAL_MEDIUM";
        internal const string k_NormalReconstructionHighKeyword = "_RECONSTRUCT_NORMAL_HIGH";
        internal const string k_SourceDepthKeyword = "_SOURCE_DEPTH";
        internal const string k_SourceDepthNormalsKeyword = "_SOURCE_DEPTH_NORMALS";


        /// <inheritdoc/>
        public override void Create()
        {
            // Create the pass...
            m_SSAOPass ??= new MobileGroundTruthAmbientOcclusionPass();

            GetMaterial();
        }

        /// <inheritdoc/>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData))
                return;

            if (!GetMaterial())
            {
                Debug.LogErrorFormat(
                    "{0}.AddRenderPasses(): Missing material. {1} render pass will not be added. Check for missing reference in the renderer resources.",
                    GetType().Name, name);
                return;
            }

            var settings = VolumeManager.instance.stack.GetComponent<MobileGroundTruthAmbientOcclusion>();
            if (!settings || !settings.IsActive())
            {
                return;
            }

            
            bool shouldAdd = m_SSAOPass.Setup(settings, m_Material);

            if (shouldAdd)
            {
                renderer.EnqueuePass(m_SSAOPass);
            }
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_Material);
        }

        private bool GetMaterial()
        {
            if (m_Material != null)
            {
                return true;
            }

            if (m_Shader == null)
            {
                m_Shader = Shader.Find(k_ShaderName);
                if (m_Shader == null)
                {
                    return false;
                }
            }

            m_Material = CoreUtils.CreateEngineMaterial(m_Shader);

            return m_Material != null;
        }
    }
}