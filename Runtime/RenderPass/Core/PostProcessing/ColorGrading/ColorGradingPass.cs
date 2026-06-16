using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public class ColorGradingPass : UnsafePass
    {
        private ColorGradingLutBuilder m_LutBuilder;
        private readonly ColorCurves m_DefaultCurves = new();
        private ColorGradingSettingsData m_Settings;
        private ColorCurves m_Curves;

        [RenderGraphResource(
            Name = "ColorGradingTexture",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture colorGradingTex = new();

        public ColorGradingPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ColorGradingPass));
        }

        public override void Create()
        {
            m_LutBuilder = new ColorGradingLutBuilder();
        }

        public override void Dispose()
        {
            m_LutBuilder?.Dispose();
            m_LutBuilder = null;
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData != null && frameData.Contains<VividCameraData>()
                ? frameData.Get<VividCameraData>()
                : null;
            var postProcessingAllowed = cameraData?.camera != null && CoreUtils.ArePostProcessesEnabled(cameraData.camera);
            if (!postProcessingAllowed && cameraData?.hdrOutputActive == true)
            {
                m_Settings = ColorGradingSettingsResolver.ResolveHDROutput(frameData);
                m_Curves = m_DefaultCurves;
            }
            else if (!postProcessingAllowed)
            {
                m_Settings = ColorGradingSettingsData.CreateDefault();
                m_Curves = m_DefaultCurves;
            }
            else
            {
                m_Settings = ColorGradingSettingsResolver.Resolve(frameData, out m_Curves);
                m_Curves ??= m_DefaultCurves;
            }

            colorGradingTex.desc.Width = ColorGradingLutBuilder.LutSize;
            colorGradingTex.desc.Height = ColorGradingLutBuilder.LutSize;
            colorGradingTex.desc.Slices = ColorGradingLutBuilder.LutSize;
            colorGradingTex.desc.Dimension = TextureDimension.Tex3D;
            colorGradingTex.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            colorGradingTex.desc.FilterMode = FilterMode.Bilinear;
            colorGradingTex.desc.WrapMode = TextureWrapMode.Clamp;
            colorGradingTex.desc.UseMipMap = false;
            colorGradingTex.desc.AutoGenerateMips = false;
            colorGradingTex.desc.MipCount = 1;
            colorGradingTex.desc.EnableRandomWrite = true;
            colorGradingTex.desc.Name = "ColorGradingTexture";
        }

        public override void Record(UnsafePassContext context)
        {
            using (new ProfilingScope(context.cmd, profilingSampler))
            {
                if (m_LutBuilder == null
                    || !m_LutBuilder.IsValid
                    || !colorGradingTex.innerHandle.IsValid()
                    || m_Curves == null)
                {
                    return;
                }

                m_LutBuilder.Build(context.GetNativeCommandBuffer(), m_Settings, m_Curves, m_Settings.externalLut, colorGradingTex.innerHandle);
            }
        }
    }
}
