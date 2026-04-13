using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    [System.Obsolete("AtmosphereLUTPass is deprecated. Atmosphere LUTs are updated by SkyManager.Update().")]
    public sealed class AtmosphereLUTPass : ComputePass
    {
        [RenderGraphResource(Name = "MultiScatteringLUT", Access = AccessFlags.Write)]
        private RenderGraphTexture m_MultiScatteringLUT;

        [RenderGraphResource(Name = "SkyViewLUT", Access = AccessFlags.Write)]
        private RenderGraphTexture m_SkyViewLUT;

        [RenderGraphResource(Name = "AtmosphericScatteringLUT", Access = AccessFlags.Write)]
        private RenderGraphTexture m_AtmosphericScatteringLUT;

        public AtmosphereLUTPass()
        {
            profilingSampler = new ProfilingSampler(nameof(AtmosphereLUTPass));

            m_MultiScatteringLUT = RenderGraphTexture.CreateOutput("MultiScatteringLUT", GraphicsFormat.R16G16B16A16_SFloat);
            m_SkyViewLUT = RenderGraphTexture.CreateOutput("SkyViewLUT", GraphicsFormat.R16G16B16A16_SFloat);
            m_AtmosphericScatteringLUT = RenderGraphTexture.CreateOutput("AtmosphericScatteringLUT", GraphicsFormat.R16G16B16A16_SFloat);

            Configure2DLutDescriptor(
                m_MultiScatteringLUT,
                PhysicallyBasedSkyAtmosphereLutCache.MultiScatteringWidth,
                PhysicallyBasedSkyAtmosphereLutCache.MultiScatteringHeight);
            Configure2DLutDescriptor(
                m_SkyViewLUT,
                PhysicallyBasedSkyAtmosphereLutCache.SkyViewWidth,
                PhysicallyBasedSkyAtmosphereLutCache.SkyViewHeight);
            Configure3DLutDescriptor(
                m_AtmosphericScatteringLUT,
                PhysicallyBasedSkyAtmosphereLutCache.AtmosphericScatteringWidth,
                PhysicallyBasedSkyAtmosphereLutCache.AtmosphericScatteringHeight,
                PhysicallyBasedSkyAtmosphereLutCache.AtmosphericScatteringDepth);
        }

        public override void Create()
        {
            SkyManager.Initialize();
        }

        public override void Prepare(ContextContainer frameData)
        {
            Configure2DLutDescriptor(
                m_MultiScatteringLUT,
                PhysicallyBasedSkyAtmosphereLutCache.MultiScatteringWidth,
                PhysicallyBasedSkyAtmosphereLutCache.MultiScatteringHeight);
            Configure2DLutDescriptor(
                m_SkyViewLUT,
                PhysicallyBasedSkyAtmosphereLutCache.SkyViewWidth,
                PhysicallyBasedSkyAtmosphereLutCache.SkyViewHeight);
            Configure3DLutDescriptor(
                m_AtmosphericScatteringLUT,
                PhysicallyBasedSkyAtmosphereLutCache.AtmosphericScatteringWidth,
                PhysicallyBasedSkyAtmosphereLutCache.AtmosphericScatteringHeight,
                PhysicallyBasedSkyAtmosphereLutCache.AtmosphericScatteringDepth);

            if (!PassRecorder.IsPassTextureImportActive)
                return;

            SkyManager.ImportMultiScatteringLut(m_MultiScatteringLUT);
            SkyManager.ImportSkyViewLut(m_SkyViewLUT);
            SkyManager.ImportAtmosphericScatteringLut(m_AtmosphericScatteringLUT);
        }

        public override void Record(ComputeGraphContext context)
        {
        }

        public override void Dispose()
        {
            m_MultiScatteringLUT?.ClearImportedHandle();
            m_SkyViewLUT?.ClearImportedHandle();
            m_AtmosphericScatteringLUT?.ClearImportedHandle();
        }

        private static void Configure2DLutDescriptor(RenderGraphTexture texture, int width, int height)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = width;
            texture.desc.Height = height;
            texture.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.Dimension = TextureDimension.Tex2D;
            texture.desc.Slices = 1;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.EnableRandomWrite = true;
            texture.desc.ClearBuffer = false;
        }

        private static void Configure3DLutDescriptor(RenderGraphTexture texture, int width, int height, int depth)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = width;
            texture.desc.Height = height;
            texture.desc.Slices = depth;
            texture.desc.Dimension = TextureDimension.Tex3D;
            texture.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.EnableRandomWrite = true;
            texture.desc.ClearBuffer = false;
        }
    }
}
