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
        internal const int ElementCount =
            ConditionalOffset + ConditionalResolution * MarginalResolution;
        internal const int ElementStride = sizeof(float);
    }

    /// <summary>
    /// Builds a persistent, equiareal HDRI importance distribution. The output packs metadata,
    /// the marginal CDF, and all per-row conditional CDFs into one structured buffer so the
    /// path-tracing graph only needs one explicit dependency.
    /// </summary>
    public sealed class ReferencedPathTracingEnvironmentSamplingPass : ComputePass
    {
        private const string ConditionalKernelName = "ComputeConditional";
        private const string MarginalKernelName = "ComputeMarginal";

        private static readonly int EnvironmentTextureId =
            Shader.PropertyToID("_ReferencedEnvironmentTexture");
        private static readonly int EnvironmentTintId =
            Shader.PropertyToID("_ReferencedEnvironmentTint");
        private static readonly int EnvironmentParametersId =
            Shader.PropertyToID("_ReferencedEnvironmentParameters");
        private static readonly int EnvironmentImportanceDistributionId =
            Shader.PropertyToID("_ReferencedEnvironmentImportanceDistribution");

        [RenderGraphResource(Name = "PathTracingEnvironment", Access = AccessFlags.Read)]
        private RenderGraphTexture m_EnvironmentTexture;

        [RenderGraphResource(
            Name = "EnvironmentImportanceDistribution",
            Access = AccessFlags.Write)]
        private RenderGraphBuffer m_EnvironmentImportanceDistribution;

        private ComputeShader m_ComputeShader;
        private int m_ConditionalKernel = -1;
        private int m_MarginalKernel = -1;
        private bool m_DistributionInitialized;
        private bool m_HasBuiltDistribution;
        private bool m_ShouldBuild;
        private ulong m_LastBuiltSignature;
        private ReferencedPathTracingEnvironmentState m_EnvironmentState;

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
            if (m_ConditionalKernel >= 0 && m_MarginalKernel >= 0)
                return;

            m_ComputeShader = null;
            m_ConditionalKernel = -1;
            m_MarginalKernel = -1;
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_EnvironmentTexture.ClearImportedHandle();
            var skyData = frameData.GetOrCreate<VividSkyData>();
            m_EnvironmentState = ReferencedPathTracingEnvironmentState.Resolve(skyData);
            SkyManager.ImportSpecularCubemap(
                m_EnvironmentTexture,
                m_EnvironmentState.mode
                        == ReferencedPathTracingEnvironmentMode.Hdri
                    && m_EnvironmentState.hasHdri
                    ? skyData
                    : null);

            ConfigureDistributionBuffer();
            EnsureDistributionBufferInitialized();

            m_ShouldBuild =
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
        }

        public override void Record(ComputePassContext context)
        {
            if (!m_ShouldBuild
                || m_EnvironmentTexture?.innerHandle.IsValid() != true
                || m_EnvironmentImportanceDistribution?.innerHandle.IsValid() != true)
            {
                return;
            }

            using (new ProfilingScope(context.cmd, profilingSampler))
            {
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
                context.cmd.DispatchCompute(m_ComputeShader, m_MarginalKernel, 1, 1, 1);
            }

            m_LastBuiltSignature = m_EnvironmentState.samplingSignature;
            m_HasBuiltDistribution = true;
            m_ShouldBuild = false;
        }

        public override void Dispose()
        {
            m_EnvironmentTexture?.ClearImportedHandle();
            m_EnvironmentImportanceDistribution?.ClearImportedBuffer();
            m_ComputeShader = null;
            m_ConditionalKernel = -1;
            m_MarginalKernel = -1;
            m_DistributionInitialized = false;
            m_HasBuiltDistribution = false;
            m_ShouldBuild = false;
            m_LastBuiltSignature = 0;
            m_EnvironmentState = default;
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
