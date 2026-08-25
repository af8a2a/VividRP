using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class MaterialClassificationPass : ComputePass, IAsyncComputeSupportedPass
    {
        private const int DeferredVariantCount = 4;
        private const int IndirectArgsElementCount = 4;
        private const int ThreadGroupSizeX = 8;
        private const int ThreadGroupSizeY = 8;
        private const int BuildIndirectThreadGroupSizeX = 64;
        internal const int Wave32SubGroupSize = 32;
        internal const int Wave64SubGroupSize = 64;

        private static readonly int GBuffer0Id = Shader.PropertyToID("_GBuffer0");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int ClassificationWidthId = Shader.PropertyToID("_ClassificationWidth");
        private static readonly int ClassificationHeightId = Shader.PropertyToID("_ClassificationHeight");
        private static readonly int MaterialTileCountId = Shader.PropertyToID("_MaterialTileCount");
        private static readonly int MaterialTileCountXId = Shader.PropertyToID("_MaterialTileCountX");
        private static readonly int MaterialTileFeatureFlagsId = Shader.PropertyToID("_MaterialTileFeatureFlags");
        private static readonly int MaterialFeatureTileListId = Shader.PropertyToID("_MaterialFeatureTileList");
        private static readonly int MaterialFeatureIndirectArgsId = Shader.PropertyToID("_MaterialFeatureIndirectArgs");

        [RenderGraphResource(Name = "GBuffer0", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer0;

        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(
            Name = "MaterialTileFeatureFlags",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_MaterialTileFeatureFlags;

        [RenderGraphResource(
            Name = "MaterialFeatureTileList",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_MaterialFeatureTileList;

        [RenderGraphResource(
            Name = "MaterialFeatureIndirectArgs",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_MaterialFeatureIndirectArgs;

        private ComputeShader m_ClassificationCompute;
        private int m_ClearDeferredVariantArgsKernel = -1;
        private int m_ClassifyDeferredExportsKernel = -1;
        private int m_ClassifyDeferredExportsWave32Kernel = -1;
        private int m_ClassifyDeferredExportsWave64Kernel = -1;
        private int m_SelectedClassifyDeferredExportsKernel = -1;
        private int m_BuildDeferredVariantIndirectArgsKernel = -1;
        private int m_BuildDeferredVariantIndirectArgsWave32Kernel = -1;
        private int m_BuildDeferredVariantIndirectArgsWave64Kernel = -1;
        private int m_SelectedBuildDeferredVariantIndirectArgsKernel = -1;
        private int m_ClassificationWidth = 1;
        private int m_ClassificationHeight = 1;
        private int m_DispatchGroupCountX = 1;
        private int m_DispatchGroupCountY = 1;
        private int m_MaterialTileCount = 1;
        private int m_BuildIndirectDispatchGroupCountX = 1;
        private GraphicsBuffer m_MaterialTileFeatureFlagsBuffer;
        private GraphicsBuffer m_MaterialFeatureTileListBuffer;
        private GraphicsBuffer m_MaterialFeatureIndirectArgsBuffer;

        public MaterialClassificationPass()
        {
            m_GBuffer0 = RenderGraphTexture.CreateInput("GBuffer0", GraphicsFormat.R8G8B8A8_SRGB);
            m_GBuffer1 = RenderGraphTexture.CreateInput("GBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);
            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);

            m_MaterialTileFeatureFlags = RenderGraphBuffer.CreateStructured("MaterialTileFeatureFlags", 1, sizeof(uint));
            m_MaterialFeatureTileList = RenderGraphBuffer.CreateStructured("MaterialFeatureTileList", 1, sizeof(uint));
            m_MaterialFeatureIndirectArgs = CreateIndirectArgsBuffer("MaterialFeatureIndirectArgs");
        }

        public override void Create()
        {
            m_ClassificationCompute = PipelineResourceManager.Get<VividRPCoreResources>()?.MaterialClassificationCompute;

            if (m_ClassificationCompute == null)
                return;

            m_ClearDeferredVariantArgsKernel = TryFindKernel(m_ClassificationCompute, "ClearDeferredVariantArgs");
            m_ClassifyDeferredExportsKernel = TryFindKernel(m_ClassificationCompute, "ClassifyDeferredExports");
            m_ClassifyDeferredExportsWave32Kernel = TryFindKernel(m_ClassificationCompute, "ClassifyDeferredExportsWave32");
            m_ClassifyDeferredExportsWave64Kernel = TryFindKernel(m_ClassificationCompute, "ClassifyDeferredExportsWave64");
            m_BuildDeferredVariantIndirectArgsKernel = TryFindKernel(m_ClassificationCompute, "BuildDeferredVariantIndirectArgs");
            m_BuildDeferredVariantIndirectArgsWave32Kernel = TryFindKernel(m_ClassificationCompute, "BuildDeferredVariantIndirectArgsWave32");
            m_BuildDeferredVariantIndirectArgsWave64Kernel = TryFindKernel(m_ClassificationCompute, "BuildDeferredVariantIndirectArgsWave64");
            SelectMaterialClassificationKernels(SystemInfo.computeSubGroupSize);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            m_ClassificationWidth = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            m_ClassificationHeight = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (m_ClassificationWidth <= 0)
                m_ClassificationWidth = Mathf.Max(1, Screen.width);

            if (m_ClassificationHeight <= 0)
                m_ClassificationHeight = Mathf.Max(1, Screen.height);

            m_DispatchGroupCountX = Mathf.Max(1, (m_ClassificationWidth + ThreadGroupSizeX - 1) / ThreadGroupSizeX);
            m_DispatchGroupCountY = Mathf.Max(1, (m_ClassificationHeight + ThreadGroupSizeY - 1) / ThreadGroupSizeY);

            m_GBuffer0.Resize(m_ClassificationWidth, m_ClassificationHeight);
            m_GBuffer1.Resize(m_ClassificationWidth, m_ClassificationHeight);
            m_DepthTexture.Resize(m_ClassificationWidth, m_ClassificationHeight);

            m_MaterialTileCount = Mathf.Max(1, m_DispatchGroupCountX * m_DispatchGroupCountY);
            m_BuildIndirectDispatchGroupCountX = Mathf.Max(
                1,
                (m_MaterialTileCount + BuildIndirectThreadGroupSizeX - 1) / BuildIndirectThreadGroupSizeX);

            ResizeStructuredBuffer(m_MaterialTileFeatureFlags, m_MaterialTileCount, sizeof(uint));
            ResizeStructuredBuffer(m_MaterialFeatureTileList, m_MaterialTileCount * DeferredVariantCount, sizeof(uint));
            ResizeIndirectArgsBuffer(m_MaterialFeatureIndirectArgs);
            EnsureImportedBuffers();
        }

        public override void Record(ComputePassContext context)
        {
            if (m_ClassificationCompute == null
                || m_ClearDeferredVariantArgsKernel < 0
                || m_SelectedClassifyDeferredExportsKernel < 0
                || m_SelectedBuildDeferredVariantIndirectArgsKernel < 0)
            {
                return;
            }

            var cmd = context.cmd;

            BindCommonParams(cmd);
            cmd.SetComputeBufferParam(
                m_ClassificationCompute,
                m_ClearDeferredVariantArgsKernel,
                MaterialFeatureIndirectArgsId,
                m_MaterialFeatureIndirectArgs.innerHandle);
            cmd.DispatchCompute(m_ClassificationCompute, m_ClearDeferredVariantArgsKernel, 1, 1, 1);

            BindCommonParams(cmd);
            cmd.SetComputeTextureParam(m_ClassificationCompute, m_SelectedClassifyDeferredExportsKernel, GBuffer0Id, m_GBuffer0.innerHandle);
            cmd.SetComputeTextureParam(m_ClassificationCompute, m_SelectedClassifyDeferredExportsKernel, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeTextureParam(m_ClassificationCompute, m_SelectedClassifyDeferredExportsKernel, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeBufferParam(
                m_ClassificationCompute,
                m_SelectedClassifyDeferredExportsKernel,
                MaterialTileFeatureFlagsId,
                m_MaterialTileFeatureFlags.innerHandle);
            cmd.DispatchCompute(
                m_ClassificationCompute,
                m_SelectedClassifyDeferredExportsKernel,
                m_DispatchGroupCountX,
                m_DispatchGroupCountY,
                1);

            BindCommonParams(cmd);
            cmd.SetComputeBufferParam(
                m_ClassificationCompute,
                m_SelectedBuildDeferredVariantIndirectArgsKernel,
                MaterialTileFeatureFlagsId,
                m_MaterialTileFeatureFlags.innerHandle);
            cmd.SetComputeBufferParam(
                m_ClassificationCompute,
                m_SelectedBuildDeferredVariantIndirectArgsKernel,
                MaterialFeatureTileListId,
                m_MaterialFeatureTileList.innerHandle);
            cmd.SetComputeBufferParam(
                m_ClassificationCompute,
                m_SelectedBuildDeferredVariantIndirectArgsKernel,
                MaterialFeatureIndirectArgsId,
                m_MaterialFeatureIndirectArgs.innerHandle);
            cmd.DispatchCompute(
                m_ClassificationCompute,
                m_SelectedBuildDeferredVariantIndirectArgsKernel,
                m_BuildIndirectDispatchGroupCountX,
                1,
                1);
        }

        public override void Dispose()
        {
            ReleaseImportedBuffers();
            m_ClassificationCompute = null;
            m_ClearDeferredVariantArgsKernel = -1;
            m_ClassifyDeferredExportsKernel = -1;
            m_ClassifyDeferredExportsWave32Kernel = -1;
            m_ClassifyDeferredExportsWave64Kernel = -1;
            m_SelectedClassifyDeferredExportsKernel = -1;
            m_BuildDeferredVariantIndirectArgsKernel = -1;
            m_BuildDeferredVariantIndirectArgsWave32Kernel = -1;
            m_BuildDeferredVariantIndirectArgsWave64Kernel = -1;
            m_SelectedBuildDeferredVariantIndirectArgsKernel = -1;
        }

        internal static int ResolveMaterialClassificationWaveSize(int computeSubGroupSize)
        {
            if (computeSubGroupSize == Wave64SubGroupSize)
                return Wave64SubGroupSize;

            if (computeSubGroupSize == Wave32SubGroupSize)
                return Wave32SubGroupSize;

            return 0;
        }

        private static int TryFindKernel(ComputeShader shader, string kernelName)
        {
            return shader != null && shader.HasKernel(kernelName) ? shader.FindKernel(kernelName) : -1;
        }

        private void SelectMaterialClassificationKernels(int computeSubGroupSize)
        {
            m_SelectedClassifyDeferredExportsKernel = m_ClassifyDeferredExportsKernel;
            m_SelectedBuildDeferredVariantIndirectArgsKernel = m_BuildDeferredVariantIndirectArgsKernel;

            var waveSize = ResolveMaterialClassificationWaveSize(computeSubGroupSize);
            if (waveSize == Wave64SubGroupSize
                && m_ClassifyDeferredExportsWave64Kernel >= 0
                && m_BuildDeferredVariantIndirectArgsWave64Kernel >= 0)
            {
                m_SelectedClassifyDeferredExportsKernel = m_ClassifyDeferredExportsWave64Kernel;
                m_SelectedBuildDeferredVariantIndirectArgsKernel = m_BuildDeferredVariantIndirectArgsWave64Kernel;
                return;
            }

            if (waveSize == Wave32SubGroupSize
                && m_ClassifyDeferredExportsWave32Kernel >= 0
                && m_BuildDeferredVariantIndirectArgsWave32Kernel >= 0)
            {
                m_SelectedClassifyDeferredExportsKernel = m_ClassifyDeferredExportsWave32Kernel;
                m_SelectedBuildDeferredVariantIndirectArgsKernel = m_BuildDeferredVariantIndirectArgsWave32Kernel;
            }
        }

        private void BindCommonParams(ComputeCommandBuffer cmd)
        {
            cmd.SetComputeIntParam(m_ClassificationCompute, ClassificationWidthId, m_ClassificationWidth);
            cmd.SetComputeIntParam(m_ClassificationCompute, ClassificationHeightId, m_ClassificationHeight);
            cmd.SetComputeIntParam(m_ClassificationCompute, MaterialTileCountId, m_MaterialTileCount);
            cmd.SetComputeIntParam(m_ClassificationCompute, MaterialTileCountXId, m_DispatchGroupCountX);
        }

        private static RenderGraphBuffer CreateIndirectArgsBuffer(string name)
        {
            return new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = DeferredVariantCount * IndirectArgsElementCount,
                    Stride = sizeof(uint),
                    Target = GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                    Name = name
                }
            };
        }

        private static void ResizeStructuredBuffer(RenderGraphBuffer buffer, int count, int stride)
        {
            if (buffer?.desc == null)
                return;

            buffer.desc.Count = Mathf.Max(1, count);
            buffer.desc.Stride = stride;
            buffer.desc.Target = GraphicsBuffer.Target.Structured;
        }

        private static void ResizeIndirectArgsBuffer(RenderGraphBuffer buffer)
        {
            if (buffer?.desc == null)
                return;

            buffer.desc.Count = DeferredVariantCount * IndirectArgsElementCount;
            buffer.desc.Stride = sizeof(uint);
            buffer.desc.Target = GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments;
        }

        private void EnsureImportedBuffers()
        {
            EnsureImportedBuffer(ref m_MaterialTileFeatureFlagsBuffer, m_MaterialTileFeatureFlags);
            EnsureImportedBuffer(ref m_MaterialFeatureTileListBuffer, m_MaterialFeatureTileList);
            EnsureImportedBuffer(ref m_MaterialFeatureIndirectArgsBuffer, m_MaterialFeatureIndirectArgs);
        }

        private void ReleaseImportedBuffers()
        {
            ReleaseImportedBuffer(ref m_MaterialTileFeatureFlagsBuffer, m_MaterialTileFeatureFlags);
            ReleaseImportedBuffer(ref m_MaterialFeatureTileListBuffer, m_MaterialFeatureTileList);
            ReleaseImportedBuffer(ref m_MaterialFeatureIndirectArgsBuffer, m_MaterialFeatureIndirectArgs);
        }

        private static void EnsureImportedBuffer(ref GraphicsBuffer graphicsBuffer, RenderGraphBuffer renderGraphBuffer)
        {
            if (renderGraphBuffer?.desc == null)
                return;

            var requiredCount = Mathf.Max(1, renderGraphBuffer.desc.Count);
            var requiredStride = Mathf.Max(1, renderGraphBuffer.desc.Stride);
            var requiredTarget = renderGraphBuffer.desc.Target;

            if (graphicsBuffer == null
                || graphicsBuffer.count < requiredCount
                || graphicsBuffer.stride != requiredStride)
            {
                graphicsBuffer?.Dispose();
                graphicsBuffer = new GraphicsBuffer(requiredTarget, requiredCount, requiredStride);
            }

            renderGraphBuffer.SetImportedBuffer(graphicsBuffer);
        }

        private static void ReleaseImportedBuffer(ref GraphicsBuffer graphicsBuffer, RenderGraphBuffer renderGraphBuffer)
        {
            renderGraphBuffer?.ClearImportedBuffer();
            graphicsBuffer?.Dispose();
            graphicsBuffer = null;
        }
    }
}
