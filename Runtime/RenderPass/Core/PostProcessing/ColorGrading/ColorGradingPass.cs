using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public class ColorGradingPass : UnsafePass
    {
        private ColorGradingLutBuilder m_LutBuilder;
        private ColorGradingSettingsData m_Settings;

        [RenderGraphResource(Name = "ColorGradingTexture", Access = AccessFlags.Write)]
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
            m_Settings = ColorGradingSettingsResolver.Resolve();

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

        public override void Record(UnsafeGraphContext context)
        {
            using (new ProfilingScope(context.cmd, profilingSampler))
            {
                if (m_LutBuilder == null
                    || !m_LutBuilder.IsValid
                    || !colorGradingTex.innerHandle.IsValid())
                {
                    return;
                }

                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                m_LutBuilder.Build(cmd, m_Settings, colorGradingTex);
            }
        }
    }
}