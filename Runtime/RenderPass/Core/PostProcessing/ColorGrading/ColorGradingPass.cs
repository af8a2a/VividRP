using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public class ColorGradingPass : ComputePass, IAsyncComputeSupportedPass, IAllowGlobalStateModificationPass
    {
        private enum ImportedTextureSlot
        {
            MasterCurve,
            RedCurve,
            GreenCurve,
            BlueCurve,
            HueVsHueCurve,
            HueVsSatCurve,
            SatVsSatCurve,
            LumVsSatCurve,
            ExternalLut,
            Count
        }

        private struct ImportedTextureState
        {
            public Texture Source;
            public RTHandle Handle;
            public TextureHandle ImportedHandle;
        }

        private ColorGradingLutBuilder m_LutBuilder;
        private ColorGradingSettingsData m_Settings;
        private readonly ImportedTextureState[] m_ImportedTextures = new ImportedTextureState[(int)ImportedTextureSlot.Count];
        private ColorGradingLutTextures m_LutTextures;

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
            ReleaseImportedTextures();
            m_LutBuilder?.Dispose();
            m_LutBuilder = null;
            m_LutTextures = default;
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

            m_LutTextures = default;
            if (m_LutBuilder == null)
                return;

            m_LutTextures.masterCurve = PrepareImportedTexture(
                ImportedTextureSlot.MasterCurve,
                m_LutBuilder.GetCurveTexture(ColorGradingCurveTextureSlot.Master, m_Settings.masterCurve));
            m_LutTextures.redCurve = PrepareImportedTexture(
                ImportedTextureSlot.RedCurve,
                m_LutBuilder.GetCurveTexture(ColorGradingCurveTextureSlot.Red, m_Settings.redCurve));
            m_LutTextures.greenCurve = PrepareImportedTexture(
                ImportedTextureSlot.GreenCurve,
                m_LutBuilder.GetCurveTexture(ColorGradingCurveTextureSlot.Green, m_Settings.greenCurve));
            m_LutTextures.blueCurve = PrepareImportedTexture(
                ImportedTextureSlot.BlueCurve,
                m_LutBuilder.GetCurveTexture(ColorGradingCurveTextureSlot.Blue, m_Settings.blueCurve));
            m_LutTextures.hueVsHueCurve = PrepareImportedTexture(
                ImportedTextureSlot.HueVsHueCurve,
                m_LutBuilder.GetCurveTexture(ColorGradingCurveTextureSlot.HueVsHue, m_Settings.hueVsHueCurve));
            m_LutTextures.hueVsSatCurve = PrepareImportedTexture(
                ImportedTextureSlot.HueVsSatCurve,
                m_LutBuilder.GetCurveTexture(ColorGradingCurveTextureSlot.HueVsSat, m_Settings.hueVsSatCurve));
            m_LutTextures.satVsSatCurve = PrepareImportedTexture(
                ImportedTextureSlot.SatVsSatCurve,
                m_LutBuilder.GetCurveTexture(ColorGradingCurveTextureSlot.SatVsSat, m_Settings.satVsSatCurve));
            m_LutTextures.lumVsSatCurve = PrepareImportedTexture(
                ImportedTextureSlot.LumVsSatCurve,
                m_LutBuilder.GetCurveTexture(ColorGradingCurveTextureSlot.LumVsSat, m_Settings.lumVsSatCurve));
            m_LutTextures.externalLut = PrepareImportedTexture(ImportedTextureSlot.ExternalLut, m_Settings.externalLut);
        }

        public override void Record(ComputeGraphContext context)
        {
            using (new ProfilingScope(context.cmd, profilingSampler))
            {
                if (m_LutBuilder == null
                    || !m_LutBuilder.IsValid
                    || !colorGradingTex.innerHandle.IsValid())
                {
                    return;
                }

                m_LutBuilder.Build(context.cmd, m_Settings, m_LutTextures, colorGradingTex.innerHandle);
            }
        }

        private TextureHandle PrepareImportedTexture(ImportedTextureSlot slot, Texture sourceTexture)
        {
            var index = (int)slot;
            var importedTexture = m_ImportedTextures[index];

            if (sourceTexture == null)
            {
                ReleaseImportedTexture(ref importedTexture);
                m_ImportedTextures[index] = importedTexture;
                return default;
            }

            if (importedTexture.Handle == null || importedTexture.Source != sourceTexture)
            {
                importedTexture.Handle?.Release();
                importedTexture.Handle = RTHandles.Alloc(sourceTexture);
                importedTexture.Source = sourceTexture;
            }

            importedTexture.ImportedHandle = PassRecorder.IsPassTextureImportActive
                ? Import(importedTexture.Handle)
                : default;
            m_ImportedTextures[index] = importedTexture;
            return importedTexture.ImportedHandle;
        }

        private void ReleaseImportedTextures()
        {
            for (var i = 0; i < m_ImportedTextures.Length; i++)
            {
                var importedTexture = m_ImportedTextures[i];
                ReleaseImportedTexture(ref importedTexture);
                m_ImportedTextures[i] = importedTexture;
            }
        }

        private static void ReleaseImportedTexture(ref ImportedTextureState importedTexture)
        {
            importedTexture.Handle?.Release();
            importedTexture.Handle = null;
            importedTexture.Source = null;
            importedTexture.ImportedHandle = default;
        }
    }
}
