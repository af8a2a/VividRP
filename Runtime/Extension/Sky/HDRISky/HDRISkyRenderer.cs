using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class HDRISkyRenderer : SkyRenderer
    {
        private Material m_SkyHDRIMaterial;
        private Cubemap m_DefaultHDRISky;
        private MaterialPropertyBlock m_PropertyBlock;

        public HDRISkyRenderer()
        {
            SupportDynamicSunLight = false;
        }

        /// <summary>
        /// Create materials.
        /// </summary>
        public override void Build()
        {
            if (m_SkyHDRIMaterial == null)
            {
                var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<SkyRuntimeResources>();
                m_SkyHDRIMaterial = CoreUtils.CreateEngineMaterial(runtimeShaders.hdriSkyPS);
            }

            if (m_DefaultHDRISky == null)
            {
                var runtimeTextures = GraphicsSettings.GetRenderPipelineSettings<SkyRuntimeResources>();
                m_DefaultHDRISky = runtimeTextures.defaultHDRISky;
            }


            m_PropertyBlock = new MaterialPropertyBlock();
        }

        /// <summary>
        /// Clean up resources.
        /// </summary>
        public override void Cleanup()
        {
            CoreUtils.Destroy(m_SkyHDRIMaterial);
        }

        public override void RenderSky(CommandBuffer cmd, SkyBasePassData basePassData, SkySettings skySettings, bool renderForCubemap)
        {
            HDRISkySetting hdriSkySetting = skySettings as HDRISkySetting;

            float intensity = GetSkyIntensity(skySettings);
            float phi = -Mathf.Deg2Rad * hdriSkySetting.rotation.value;
            
            
            m_SkyHDRIMaterial.SetTexture(ShaderConstants._Cubemap, hdriSkySetting.hdriSky.value != null ? hdriSkySetting.hdriSky.value : m_DefaultHDRISky);
            m_SkyHDRIMaterial.SetVector(ShaderConstants._SkyParam, new Vector4(intensity, 0.0f, Mathf.Cos(phi), Mathf.Sin(phi)));
            m_PropertyBlock.SetMatrix(ShaderConstants._PixelCoordToViewDirWS, basePassData.pixelCoordToViewDirMatrix);

            CoreUtils.DrawFullScreen(cmd, m_SkyHDRIMaterial, m_PropertyBlock, renderForCubemap ? 0 : 1);
        }

        static class ShaderConstants
        {
            public static readonly int _Cubemap = Shader.PropertyToID("_Cubemap");
            public static readonly int _SkyParam = Shader.PropertyToID("_SkyParam");
            public static readonly int _PixelCoordToViewDirWS = Shader.PropertyToID("_PixelCoordToViewDirWS");
        }
    }

}
