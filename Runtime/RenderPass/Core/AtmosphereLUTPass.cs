using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class AtmosphereLUTPass : ComputePass
    {
        internal const int TransmittanceWidth = 256;
        internal const int TransmittanceHeight = 64;
        internal const int MultiScatteringWidth = 32;
        internal const int MultiScatteringHeight = 32;
        internal const int SkyViewWidth = 192;
        internal const int SkyViewHeight = 108;

        private const string ComputePathWarning = "[VividRP] Atmosphere LUT compute shader is missing. Re-sync PipelineResources after Unity imports the new compute asset.";
        private const string TransmittanceKernelName = "TransmittanceLUT";
        private const string MultiScatteringKernelName = "MultiScatteringLUT";
        private const string SkyViewKernelName = "SkyViewLUT";

        private static readonly int SkyCameraPositionPsId = Shader.PropertyToID("_SkyCameraPositionPS");
        private static readonly int SkySunDirectionId = Shader.PropertyToID("_SkySunDirection");
        private static readonly int SkySunColorId = Shader.PropertyToID("_SkySunColor");
        private static readonly int SkyPlanetParamsId = Shader.PropertyToID("_SkyPlanetParams");
        private static readonly int SkyAirScatteringId = Shader.PropertyToID("_SkyAirScattering");
        private static readonly int SkyAirExtinctionId = Shader.PropertyToID("_SkyAirExtinction");
        private static readonly int SkyAerosolScatteringId = Shader.PropertyToID("_SkyAerosolScattering");
        private static readonly int SkyAerosolExtinctionId = Shader.PropertyToID("_SkyAerosolExtinction");
        private static readonly int SkyOzoneExtinctionId = Shader.PropertyToID("_SkyOzoneExtinction");
        private static readonly int SkyOzoneParamsId = Shader.PropertyToID("_SkyOzoneParams");
        private static readonly int SkyGroundTintId = Shader.PropertyToID("_SkyGroundTint");
        private static readonly int SkyFogParamsId = Shader.PropertyToID("_SkyFogParams");
        private static readonly int TransmittanceLutId = Shader.PropertyToID("_TransmittanceLUT");
        private static readonly int MultiScatteringLutId = Shader.PropertyToID("_MultiScatteringLUT");
        private static readonly int TransmittanceLutOutputId = Shader.PropertyToID("_TransmittanceLUTOutput");
        private static readonly int MultiScatteringLutOutputId = Shader.PropertyToID("_MultiScatteringLUTOutput");
        private static readonly int SkyViewLutOutputId = Shader.PropertyToID("_SkyViewLUTOutput");

        [RenderGraphResource(Name = "TransmittanceLUT", Access = AccessFlags.Write)]
        private RenderGraphTexture m_TransmittanceLUT;

        [RenderGraphResource(Name = "MultiScatteringLUT", Access = AccessFlags.Write)]
        private RenderGraphTexture m_MultiScatteringLUT;

        [RenderGraphResource(Name = "SkyViewLUT", Access = AccessFlags.Write)]
        private RenderGraphTexture m_SkyViewLUT;

        private ComputeShader m_ComputeShader;
        private int m_TransmittanceKernel = -1;
        private int m_MultiScatteringKernel = -1;
        private int m_SkyViewKernel = -1;
        private bool m_IsActive;
        private PhysicallyBasedSkyShaderParameters m_Parameters;

        public AtmosphereLUTPass()
        {
            profilingSampler = new ProfilingSampler(nameof(AtmosphereLUTPass));

            m_TransmittanceLUT = RenderGraphTexture.CreateOutput("TransmittanceLUT", GraphicsFormat.R16G16B16A16_SFloat);
            m_MultiScatteringLUT = RenderGraphTexture.CreateOutput("MultiScatteringLUT", GraphicsFormat.R16G16B16A16_SFloat);
            m_SkyViewLUT = RenderGraphTexture.CreateOutput("SkyViewLUT", GraphicsFormat.R16G16B16A16_SFloat);

            ConfigureLutDescriptor(m_TransmittanceLUT, TransmittanceWidth, TransmittanceHeight);
            ConfigureLutDescriptor(m_MultiScatteringLUT, MultiScatteringWidth, MultiScatteringHeight);
            ConfigureLutDescriptor(m_SkyViewLUT, SkyViewWidth, SkyViewHeight);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_ComputeShader = resources?.AtmosphereLUTCompute;
            if (m_ComputeShader == null)
            {
                Debug.LogWarning(ComputePathWarning);
                return;
            }

            m_TransmittanceKernel = m_ComputeShader.FindKernel(TransmittanceKernelName);
            m_MultiScatteringKernel = m_ComputeShader.FindKernel(MultiScatteringKernelName);
            m_SkyViewKernel = m_ComputeShader.FindKernel(SkyViewKernelName);
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_IsActive = PhysicallyBasedSkyShaderParameterBuilder.TryBuild(frameData, out m_Parameters);

            ConfigureLutDescriptor(m_TransmittanceLUT, TransmittanceWidth, TransmittanceHeight);
            ConfigureLutDescriptor(m_MultiScatteringLUT, MultiScatteringWidth, MultiScatteringHeight);
            ConfigureLutDescriptor(m_SkyViewLUT, SkyViewWidth, SkyViewHeight);
        }

        public override void Record(ComputeGraphContext context)
        {
            if (!m_IsActive
                || m_ComputeShader == null
                || m_TransmittanceKernel < 0
                || m_MultiScatteringKernel < 0
                || m_SkyViewKernel < 0
                || m_TransmittanceLUT?.innerHandle.IsValid() != true
                || m_MultiScatteringLUT?.innerHandle.IsValid() != true
                || m_SkyViewLUT?.innerHandle.IsValid() != true)
            {
                return;
            }

            var cmd = context.cmd;

            BindCommonParameters(cmd);
            cmd.SetComputeTextureParam(m_ComputeShader, m_TransmittanceKernel, TransmittanceLutOutputId, m_TransmittanceLUT.innerHandle);
            cmd.DispatchCompute(
                m_ComputeShader,
                m_TransmittanceKernel,
                CoreUtils.DivRoundUp(TransmittanceWidth, 8),
                CoreUtils.DivRoundUp(TransmittanceHeight, 8),
                1);

            BindCommonParameters(cmd);
            cmd.SetComputeTextureParam(m_ComputeShader, m_MultiScatteringKernel, TransmittanceLutId, m_TransmittanceLUT.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_MultiScatteringKernel, MultiScatteringLutOutputId, m_MultiScatteringLUT.innerHandle);
            cmd.DispatchCompute(
                m_ComputeShader,
                m_MultiScatteringKernel,
                CoreUtils.DivRoundUp(MultiScatteringWidth, 8),
                CoreUtils.DivRoundUp(MultiScatteringHeight, 8),
                1);

            BindCommonParameters(cmd);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SkyViewKernel, TransmittanceLutId, m_TransmittanceLUT.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SkyViewKernel, MultiScatteringLutId, m_MultiScatteringLUT.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SkyViewKernel, SkyViewLutOutputId, m_SkyViewLUT.innerHandle);
            cmd.DispatchCompute(
                m_ComputeShader,
                m_SkyViewKernel,
                CoreUtils.DivRoundUp(SkyViewWidth, 8),
                CoreUtils.DivRoundUp(SkyViewHeight, 8),
                1);
        }

        public override void Dispose()
        {
            m_ComputeShader = null;
            m_TransmittanceKernel = -1;
            m_MultiScatteringKernel = -1;
            m_SkyViewKernel = -1;
        }

        private void BindCommonParameters(ComputeCommandBuffer cmd)
        {
            cmd.SetComputeVectorParam(m_ComputeShader, SkyCameraPositionPsId, m_Parameters.skyCameraPositionPS);
            cmd.SetComputeVectorParam(m_ComputeShader, SkySunDirectionId, m_Parameters.skySunDirection);
            cmd.SetComputeVectorParam(m_ComputeShader, SkySunColorId, m_Parameters.skySunColor);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyPlanetParamsId, m_Parameters.skyPlanetParams);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyAirScatteringId, m_Parameters.skyAirScattering);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyAirExtinctionId, m_Parameters.skyAirExtinction);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyAerosolScatteringId, m_Parameters.skyAerosolScattering);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyAerosolExtinctionId, m_Parameters.skyAerosolExtinction);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyOzoneExtinctionId, m_Parameters.skyOzoneExtinction);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyOzoneParamsId, m_Parameters.skyOzoneParams);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyGroundTintId, m_Parameters.skyGroundTint);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyFogParamsId, m_Parameters.skyFogParams);
        }

        private static void ConfigureLutDescriptor(RenderGraphTexture texture, int width, int height)
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
    }
}
