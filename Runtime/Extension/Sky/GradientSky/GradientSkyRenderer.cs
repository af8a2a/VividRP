using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    public class GradientSkyRenderer : SkyRenderer
    {
        Material m_GradientSkyMaterial; // Renders a cubemap into a render texture (can be cube or 2D)
        MaterialPropertyBlock m_PropertyBlock = new MaterialPropertyBlock();

        public GradientSkyRenderer()
        {
            SupportDynamicSunLight = false;
        }

        public override void Build()
        {
            if (m_GradientSkyMaterial == null)
            {
                var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<SkyRuntimeResources>();
                m_GradientSkyMaterial = CoreUtils.CreateEngineMaterial(runtimeShaders.gradientSkyPS);
            }
        }

        public override void Cleanup()
        {
            CoreUtils.Destroy(m_GradientSkyMaterial);
        }

        public override void RenderSky(CommandBuffer cmd, SkyBasePassData basePassData, SkySettings skySettings, bool renderForCubemap)
        {
            var gradientSky = skySettings as GradientSkySetting;
            m_GradientSkyMaterial.SetColor(ShaderConstants._GradientBottom, gradientSky.bottom.value);
            m_GradientSkyMaterial.SetColor(ShaderConstants._GradientMiddle, gradientSky.middle.value);
            m_GradientSkyMaterial.SetColor(ShaderConstants._GradientTop, gradientSky.top.value);
            m_GradientSkyMaterial.SetFloat(ShaderConstants._GradientDiffusion, gradientSky.gradientDiffusion.value);
            m_GradientSkyMaterial.SetFloat(ShaderConstants._SkyIntensity, GetSkyIntensity(gradientSky));

            // This matrix needs to be updated at the draw call frequency.
            m_PropertyBlock.SetMatrix(ShaderConstants._PixelCoordToViewDirWS, basePassData.pixelCoordToViewDirMatrix);

            CoreUtils.DrawFullScreen(cmd, m_GradientSkyMaterial, m_PropertyBlock, renderForCubemap ? 0 : 1);
        }

        static class ShaderConstants
        {
            public static readonly int _SkyIntensity = Shader.PropertyToID("_SkyIntensity");
            public static readonly int _GradientBottom = Shader.PropertyToID("_GradientBottom");
            public static readonly int _GradientMiddle = Shader.PropertyToID("_GradientMiddle");
            public static readonly int _GradientTop = Shader.PropertyToID("_GradientTop");
            public static readonly int _GradientDiffusion = Shader.PropertyToID("_GradientDiffusion");
            public static readonly int _PixelCoordToViewDirWS = Shader.PropertyToID("_PixelCoordToViewDirWS");
        }
    }
}
