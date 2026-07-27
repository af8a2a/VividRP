using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    internal static class ReferencedPathTracingEnvironmentImportanceLayout
    {
        internal const int Version = 1;
        internal const int HeaderElementCount = 4;
        internal const int PdfNormalizationOffset = 0;
        internal const int AverageLuminanceOffset = 1;
        internal const int ValidOffset = 2;
        internal const int VersionOffset = 3;
        internal const int MarginalResolution = 64;
        internal const int ConditionalResolution = MarginalResolution * 2;
        internal const int MarginalOffset = HeaderElementCount;
        internal const int ConditionalOffset = MarginalOffset + MarginalResolution;
        internal const int EnvironmentElementCount =
            ConditionalOffset + ConditionalResolution * MarginalResolution;
        internal const int AtmosphereVersion = 1;
        internal const int AtmosphereHeaderElementCount = 4;
        internal const int AtmosphereValidOffset = EnvironmentElementCount;
        internal const int AtmosphereVersionOffset = AtmosphereValidOffset + 1;
        internal const int AtmosphereSampleCountOffset =
            AtmosphereVersionOffset + 1;
        internal const int AtmosphereReservedOffset =
            AtmosphereSampleCountOffset + 1;
        internal const int AtmosphereRadialResolution = 64;
        internal const int AtmosphereZenithResolution = 128;
        internal const int AtmosphereChannelCount = 3;
        internal const int AtmosphereDataOffset =
            EnvironmentElementCount + AtmosphereHeaderElementCount;
        internal const int AtmosphereReferenceSampleCount = 256;
        internal const int ElementCount =
            AtmosphereDataOffset
            + AtmosphereRadialResolution
            * AtmosphereZenithResolution
            * AtmosphereChannelCount;
        internal const int ElementStride = sizeof(float);
    }

    /// <summary>
    /// Builds persistent environment transport data. The output keeps the frozen equiareal
    /// HDRI distribution at its original offsets and appends a Reference Atmosphere optical-
    /// depth LUT, so the path-tracing graph retains one explicit dependency for both modes.
    /// </summary>
    public sealed class ReferencedPathTracingEnvironmentSamplingPass : ComputePass
    {
        private const string ConditionalKernelName = "ComputeConditional";
        private const string MarginalKernelName = "ComputeMarginal";
        private const string AtmosphereOpticalDepthKernelName =
            "ComputeAtmosphereOpticalDepth";

        private static readonly int EnvironmentTextureId =
            Shader.PropertyToID("_ReferencedEnvironmentTexture");
        private static readonly int EnvironmentTintId =
            Shader.PropertyToID("_ReferencedEnvironmentTint");
        private static readonly int EnvironmentParametersId =
            Shader.PropertyToID("_ReferencedEnvironmentParameters");
        private static readonly int EnvironmentImportanceDistributionId =
            Shader.PropertyToID("_ReferencedEnvironmentImportanceDistribution");
        private static readonly int AtmospherePlanetCenterBottomRadiusId =
            Shader.PropertyToID(
                "_ReferencedAtmospherePlanetCenterBottomRadius");
        private static readonly int AtmosphereTopRadiusMieAnisotropyId =
            Shader.PropertyToID(
                "_ReferencedAtmosphereTopRadiusMieAnisotropy");
        private static readonly int AtmosphereRayleighScatteringId =
            Shader.PropertyToID("_ReferencedAtmosphereRayleighScattering");
        private static readonly int AtmosphereMieScatteringId =
            Shader.PropertyToID("_ReferencedAtmosphereMieScattering");
        private static readonly int AtmosphereOzoneLayerId =
            Shader.PropertyToID("_ReferencedAtmosphereOzoneLayer");

        [RenderGraphResource(Name = "PathTracingEnvironment", Access = AccessFlags.Read)]
        private RenderGraphTexture m_EnvironmentTexture;

        [RenderGraphResource(
            Name = "EnvironmentImportanceDistribution",
            Access = AccessFlags.Write)]
        private RenderGraphBuffer m_EnvironmentImportanceDistribution;

        private ComputeShader m_ComputeShader;
        private int m_ConditionalKernel = -1;
        private int m_MarginalKernel = -1;
        private int m_AtmosphereOpticalDepthKernel = -1;
        private bool m_DistributionInitialized;
        private bool m_HasBuiltDistribution;
        private bool m_HasBuiltAtmosphereOpticalDepth;
        private bool m_ShouldBuildDistribution;
        private bool m_ShouldBuildAtmosphereOpticalDepth;
        private ulong m_LastBuiltSignature;
        private ulong m_LastBuiltAtmosphereOpticalDepthSignature;
        private ReferencedPathTracingEnvironmentState m_EnvironmentState;
        private ReferencedPathTracingAtmosphereState m_AtmosphereState;

        public ReferencedPathTracingEnvironmentSamplingPass()
        {
            profilingSampler =
                new ProfilingSampler(nameof(ReferencedPathTracingEnvironmentSamplingPass));
            m_EnvironmentTexture = CreateEnvironmentTexture();
            m_EnvironmentImportanceDistribution = RenderGraphBuffer.CreateStructured(
                "EnvironmentImportanceDistribution",
                ReferencedPathTracingEnvironmentImportanceLayout.ElementCount,
                ReferencedPathTracingEnvironmentImportanceLayout.ElementStride);
        }

        public override void Create()
        {
            SkyManager.Initialize();
            m_ComputeShader = PipelineResourceManager
                .Get<VividRPCoreResources>()
                ?.ReferencedPathTracingEnvironmentSamplingCompute;
            if (m_ComputeShader == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find the environment-sampling compute shader for {nameof(ReferencedPathTracingEnvironmentSamplingPass)}.");
                return;
            }

            m_ConditionalKernel = FindKernelOrLog(ConditionalKernelName);
            m_MarginalKernel = FindKernelOrLog(MarginalKernelName);
            m_AtmosphereOpticalDepthKernel =
                FindKernelOrLog(AtmosphereOpticalDepthKernelName);
            if (m_ConditionalKernel >= 0
                && m_MarginalKernel >= 0
                && m_AtmosphereOpticalDepthKernel >= 0)
            {
                return;
            }

            m_ComputeShader = null;
            m_ConditionalKernel = -1;
            m_MarginalKernel = -1;
            m_AtmosphereOpticalDepthKernel = -1;
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_EnvironmentTexture.ClearImportedHandle();
            var skyData = frameData.GetOrCreate<VividSkyData>();
            m_EnvironmentState = ReferencedPathTracingEnvironmentState.Resolve(skyData);
            m_AtmosphereState =
                ReferencedPathTracingAtmosphereState.Resolve(frameData);
            SkyManager.ImportSpecularCubemap(
                m_EnvironmentTexture,
                m_EnvironmentState.mode
                        == ReferencedPathTracingEnvironmentMode.Hdri
                    && m_EnvironmentState.hasHdri
                    ? skyData
                    : null);

            ConfigureDistributionBuffer();
            EnsureDistributionBufferInitialized();

            m_ShouldBuildDistribution =
                m_ComputeShader != null
                && m_ConditionalKernel >= 0
                && m_MarginalKernel >= 0
                && m_EnvironmentState.lightingEnabled
                && m_EnvironmentState.mode
                    == ReferencedPathTracingEnvironmentMode.Hdri
                && m_EnvironmentState.hasHdri
                && m_EnvironmentState.samplingMode
                    != ReferencedPathTracingEnvironmentSamplingMode.BsdfOnly
                && (!m_HasBuiltDistribution
                    || m_LastBuiltSignature != m_EnvironmentState.samplingSignature);
            m_ShouldBuildAtmosphereOpticalDepth =
                m_ComputeShader != null
                && m_AtmosphereOpticalDepthKernel >= 0
                && m_AtmosphereState.active
                && (!m_HasBuiltAtmosphereOpticalDepth
                    || m_LastBuiltAtmosphereOpticalDepthSignature
                        != m_AtmosphereState.opticalDepthSignature);
        }

        public override void Record(ComputePassContext context)
        {
            if ((!m_ShouldBuildDistribution
                    && !m_ShouldBuildAtmosphereOpticalDepth)
                || m_EnvironmentImportanceDistribution?.innerHandle.IsValid()
                    != true)
            {
                return;
            }

            using (new ProfilingScope(context.cmd, profilingSampler))
            {
                BuildEnvironmentDistribution(context);
                BuildAtmosphereOpticalDepth(context);
            }
        }

        public override void Dispose()
        {
            m_EnvironmentTexture?.ClearImportedHandle();
            m_EnvironmentImportanceDistribution?.ClearImportedBuffer();
            m_ComputeShader = null;
            m_ConditionalKernel = -1;
            m_MarginalKernel = -1;
            m_AtmosphereOpticalDepthKernel = -1;
            m_DistributionInitialized = false;
            m_HasBuiltDistribution = false;
            m_HasBuiltAtmosphereOpticalDepth = false;
            m_ShouldBuildDistribution = false;
            m_ShouldBuildAtmosphereOpticalDepth = false;
            m_LastBuiltSignature = 0;
            m_LastBuiltAtmosphereOpticalDepthSignature = 0;
            m_EnvironmentState = default;
            m_AtmosphereState = default;
        }

        private void BuildEnvironmentDistribution(ComputePassContext context)
        {
            if (!m_ShouldBuildDistribution
                || m_EnvironmentTexture?.innerHandle.IsValid() != true)
            {
                return;
            }

            var tint = m_EnvironmentState.tint;
            var environmentTint = new Vector4(tint.r, tint.g, tint.b, 1.0f);
            var environmentParameters = new Vector4(
                m_EnvironmentState.intensityMultiplier,
                m_EnvironmentState.rotation,
                m_EnvironmentState.maxMipLevel,
                1.0f);

            context.cmd.SetComputeVectorParam(
                m_ComputeShader,
                EnvironmentTintId,
                environmentTint);
            context.cmd.SetComputeVectorParam(
                m_ComputeShader,
                EnvironmentParametersId,
                environmentParameters);
            context.cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_ConditionalKernel,
                EnvironmentTextureId,
                m_EnvironmentTexture.innerHandle);
            context.cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_ConditionalKernel,
                EnvironmentImportanceDistributionId,
                m_EnvironmentImportanceDistribution.innerHandle);
            context.cmd.DispatchCompute(
                m_ComputeShader,
                m_ConditionalKernel,
                1,
                ReferencedPathTracingEnvironmentImportanceLayout.MarginalResolution,
                1);

            context.cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_MarginalKernel,
                EnvironmentImportanceDistributionId,
                m_EnvironmentImportanceDistribution.innerHandle);
            context.cmd.DispatchCompute(
                m_ComputeShader,
                m_MarginalKernel,
                1,
                1,
                1);

            m_LastBuiltSignature = m_EnvironmentState.samplingSignature;
            m_HasBuiltDistribution = true;
            m_ShouldBuildDistribution = false;
        }

        private void BuildAtmosphereOpticalDepth(
            ComputePassContext context)
        {
            if (!m_ShouldBuildAtmosphereOpticalDepth)
                return;

            var parameters = m_AtmosphereState.parameters;
            context.cmd.SetComputeVectorParam(
                m_ComputeShader,
                AtmospherePlanetCenterBottomRadiusId,
                new Vector4(
                    parameters.planetCenter.x,
                    parameters.planetCenter.y,
                    parameters.planetCenter.z,
                    parameters.bottomRadius));
            context.cmd.SetComputeVectorParam(
                m_ComputeShader,
                AtmosphereTopRadiusMieAnisotropyId,
                new Vector4(
                    parameters.topRadius,
                    parameters.mieAnisotropy,
                    parameters.intensityMultiplier,
                    0.0f));
            context.cmd.SetComputeVectorParam(
                m_ComputeShader,
                AtmosphereRayleighScatteringId,
                new Vector4(
                    parameters.rayleighScattering.x,
                    parameters.rayleighScattering.y,
                    parameters.rayleighScattering.z,
                    parameters.rayleighScaleHeight));
            context.cmd.SetComputeVectorParam(
                m_ComputeShader,
                AtmosphereMieScatteringId,
                new Vector4(
                    parameters.mieScattering.x,
                    parameters.mieScattering.y,
                    parameters.mieScattering.z,
                    parameters.mieScaleHeight));
            context.cmd.SetComputeVectorParam(
                m_ComputeShader,
                AtmosphereOzoneLayerId,
                new Vector4(
                    parameters.ozoneLayerStart,
                    parameters.ozoneLayerWidth,
                    0.0f,
                    0.0f));
            context.cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_AtmosphereOpticalDepthKernel,
                EnvironmentImportanceDistributionId,
                m_EnvironmentImportanceDistribution.innerHandle);
            context.cmd.DispatchCompute(
                m_ComputeShader,
                m_AtmosphereOpticalDepthKernel,
                (ReferencedPathTracingEnvironmentImportanceLayout
                        .AtmosphereZenithResolution
                    + 7)
                    / 8,
                (ReferencedPathTracingEnvironmentImportanceLayout
                        .AtmosphereRadialResolution
                    + 7)
                    / 8,
                1);

            m_LastBuiltAtmosphereOpticalDepthSignature =
                m_AtmosphereState.opticalDepthSignature;
            m_HasBuiltAtmosphereOpticalDepth = true;
            m_ShouldBuildAtmosphereOpticalDepth = false;
        }

        private int FindKernelOrLog(string kernelName)
        {
            if (m_ComputeShader == null || !m_ComputeShader.HasKernel(kernelName))
            {
                Debug.LogWarning(
                    $"[VividRP] Missing compute kernel '{kernelName}' for {nameof(ReferencedPathTracingEnvironmentSamplingPass)}.");
                return -1;
            }

            return m_ComputeShader.FindKernel(kernelName);
        }

        private void ConfigureDistributionBuffer()
        {
            if (m_EnvironmentImportanceDistribution?.desc == null)
                return;

            m_EnvironmentImportanceDistribution.desc.Count =
                ReferencedPathTracingEnvironmentImportanceLayout.ElementCount;
            m_EnvironmentImportanceDistribution.desc.Stride =
                ReferencedPathTracingEnvironmentImportanceLayout.ElementStride;
            m_EnvironmentImportanceDistribution.desc.Target =
                GraphicsBuffer.Target.Structured;
            m_EnvironmentImportanceDistribution.desc.Name =
                "EnvironmentImportanceDistribution";
        }

        private void EnsureDistributionBufferInitialized()
        {
            if (m_EnvironmentImportanceDistribution == null)
                return;

            m_EnvironmentImportanceDistribution.EnsureImportedBuffer();
            if (m_DistributionInitialized)
                return;

            m_EnvironmentImportanceDistribution.SetData(
                new float[ReferencedPathTracingEnvironmentImportanceLayout.ElementCount]);
            m_DistributionInitialized = true;
        }

        private static RenderGraphTexture CreateEnvironmentTexture()
        {
            return new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 1,
                    Height = 1,
                    Dimension = TextureDimension.Cube,
                    ColorFormat =
                        UnityEngine.Experimental.Rendering.GraphicsFormat
                            .R16G16B16A16_SFloat,
                    DepthBufferBits = DepthBits.None,
                    FilterMode = FilterMode.Trilinear,
                    WrapMode = TextureWrapMode.Clamp,
                    UseMipMap = true,
                    AutoGenerateMips = false,
                    Name = "PathTracingEnvironment"
                }
            };
        }
    }
}
