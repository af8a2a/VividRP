using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class ClassificationPass : ComputePass, IAsyncComputeSupportedPass
    {
        private const int MaterialClassCount = 3;
        private const int IndirectArgsElementCount = 4;
        private const int ThreadGroupSizeX = 8;
        private const int ThreadGroupSizeY = 8;
        private const int BuildIndirectThreadGroupSizeX = 64;

        private static readonly int GBuffer0Id = Shader.PropertyToID("_GBuffer0");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int ClassificationWidthId = Shader.PropertyToID("_ClassificationWidth");
        private static readonly int ClassificationHeightId = Shader.PropertyToID("_ClassificationHeight");
        private static readonly int MaterialTileCountId = Shader.PropertyToID("_MaterialTileCount");
        private static readonly int MaterialTileCountXId = Shader.PropertyToID("_MaterialTileCountX");
        private static readonly int StandardMaterialIndicesId = Shader.PropertyToID("_StandardMaterialIndices");
        private static readonly int FabricMaterialIndicesId = Shader.PropertyToID("_FabricMaterialIndices");
        private static readonly int ClearCoatMaterialIndicesId = Shader.PropertyToID("_ClearCoatMaterialIndices");
        private static readonly int MaterialTileClassesId = Shader.PropertyToID("_MaterialTileClasses");
        private static readonly int MaterialClassCountsId = Shader.PropertyToID("_MaterialClassCounts");
        private static readonly int StandardIndirectArgsId = Shader.PropertyToID("_StandardIndirectArgs");
        private static readonly int FabricIndirectArgsId = Shader.PropertyToID("_FabricIndirectArgs");
        private static readonly int ClearCoatIndirectArgsId = Shader.PropertyToID("_ClearCoatIndirectArgs");
        [RenderGraphResource(Name = "GBuffer0", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer0;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(
            Name = "StandardMaterialIndices",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_StandardMaterialIndices;

        [RenderGraphResource(
            Name = "FabricMaterialIndices",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_FabricMaterialIndices;

        [RenderGraphResource(
            Name = "ClearCoatMaterialIndices",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_ClearCoatMaterialIndices;

        [RenderGraphResource(
            Name = "MaterialTileClasses",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_MaterialTileClasses;

        [RenderGraphResource(
            Name = "MaterialClassCounts",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_MaterialClassCounts;

        [RenderGraphResource(
            Name = "StandardIndirectArgs",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_StandardIndirectArgs;

        [RenderGraphResource(
            Name = "FabricIndirectArgs",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_FabricIndirectArgs;

        [RenderGraphResource(
            Name = "ClearCoatIndirectArgs",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_ClearCoatIndirectArgs;

        private ComputeShader m_ClassificationCompute;
        private int m_ClearCountsKernel = -1;
        private int m_ClassifyMaterialKernel = -1;
        private int m_BuildIndirectArgsKernel = -1;
        private int m_ClassificationWidth = 1;
        private int m_ClassificationHeight = 1;
        private int m_DispatchGroupCountX = 1;
        private int m_DispatchGroupCountY = 1;
        private int m_MaterialTileCount = 1;
        private int m_BuildIndirectDispatchGroupCountX = 1;
        private GraphicsBuffer m_StandardMaterialIndicesBuffer;
        private GraphicsBuffer m_FabricMaterialIndicesBuffer;
        private GraphicsBuffer m_ClearCoatMaterialIndicesBuffer;
        private GraphicsBuffer m_MaterialTileClassesBuffer;
        private GraphicsBuffer m_MaterialClassCountsBuffer;
        private GraphicsBuffer m_StandardIndirectArgsBuffer;
        private GraphicsBuffer m_FabricIndirectArgsBuffer;
        private GraphicsBuffer m_ClearCoatIndirectArgsBuffer;

        public ClassificationPass()
        {
            m_GBuffer0 = RenderGraphTexture.CreateInput("GBuffer0", GraphicsFormat.R8G8B8A8_UNorm);
            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);

            m_StandardMaterialIndices = CreateStructuredBuffer("StandardMaterialIndices", 1, sizeof(uint));
            m_FabricMaterialIndices = CreateStructuredBuffer("FabricMaterialIndices", 1, sizeof(uint));
            m_ClearCoatMaterialIndices = CreateStructuredBuffer("ClearCoatMaterialIndices", 1, sizeof(uint));
            m_MaterialTileClasses = CreateStructuredBuffer("MaterialTileClasses", 1, sizeof(uint));
            m_MaterialClassCounts = CreateStructuredBuffer("MaterialClassCounts", MaterialClassCount, sizeof(uint));
            m_StandardIndirectArgs = CreateIndirectArgsBuffer("StandardIndirectArgs");
            m_FabricIndirectArgs = CreateIndirectArgsBuffer("FabricIndirectArgs");
            m_ClearCoatIndirectArgs = CreateIndirectArgsBuffer("ClearCoatIndirectArgs");
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_ClassificationCompute = resources.MaterialClassificationCompute;

            if (m_ClassificationCompute == null)
                return;

            m_ClearCountsKernel = m_ClassificationCompute.FindKernel("ClearMaterialCounts");
            m_ClassifyMaterialKernel = m_ClassificationCompute.FindKernel("ClassifyMaterialIds");
            m_BuildIndirectArgsKernel = m_ClassificationCompute.FindKernel("BuildIndirectArgs");
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
            m_DepthTexture.Resize(m_ClassificationWidth, m_ClassificationHeight);

            var maxTileCount = Mathf.Max(1, m_DispatchGroupCountX * m_DispatchGroupCountY);
            m_MaterialTileCount = maxTileCount;
            m_BuildIndirectDispatchGroupCountX = Mathf.Max(
                1,
                (m_MaterialTileCount + BuildIndirectThreadGroupSizeX - 1) / BuildIndirectThreadGroupSizeX);
            ResizeStructuredBuffer(m_StandardMaterialIndices, maxTileCount, sizeof(uint));
            ResizeStructuredBuffer(m_FabricMaterialIndices, maxTileCount, sizeof(uint));
            ResizeStructuredBuffer(m_ClearCoatMaterialIndices, maxTileCount, sizeof(uint));
            ResizeStructuredBuffer(m_MaterialTileClasses, maxTileCount, sizeof(uint));
            ResizeStructuredBuffer(m_MaterialClassCounts, MaterialClassCount, sizeof(uint));
            ResizeIndirectArgsBuffer(m_StandardIndirectArgs);
            ResizeIndirectArgsBuffer(m_FabricIndirectArgs);
            ResizeIndirectArgsBuffer(m_ClearCoatIndirectArgs);
            EnsureImportedBuffers();
        }

        public override void Record(ComputeGraphContext context)
        {
            if (m_ClassificationCompute == null)
                return;

            var cmd = context.cmd;

            BindCommonParams(cmd);
            cmd.SetComputeBufferParam(m_ClassificationCompute, m_ClearCountsKernel, MaterialClassCountsId, m_MaterialClassCounts.innerHandle);
            cmd.SetComputeBufferParam(m_ClassificationCompute, m_ClearCountsKernel, StandardIndirectArgsId, m_StandardIndirectArgs.innerHandle);
            cmd.SetComputeBufferParam(m_ClassificationCompute, m_ClearCountsKernel, FabricIndirectArgsId, m_FabricIndirectArgs.innerHandle);
            cmd.SetComputeBufferParam(m_ClassificationCompute, m_ClearCountsKernel, ClearCoatIndirectArgsId, m_ClearCoatIndirectArgs.innerHandle);
            cmd.DispatchCompute(m_ClassificationCompute, m_ClearCountsKernel, 1, 1, 1);

            BindCommonParams(cmd);
            cmd.SetComputeTextureParam(m_ClassificationCompute, m_ClassifyMaterialKernel, GBuffer0Id, m_GBuffer0.innerHandle);
            cmd.SetComputeTextureParam(m_ClassificationCompute, m_ClassifyMaterialKernel, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeBufferParam(m_ClassificationCompute, m_ClassifyMaterialKernel, MaterialTileClassesId, m_MaterialTileClasses.innerHandle);
            cmd.DispatchCompute(m_ClassificationCompute, m_ClassifyMaterialKernel, m_DispatchGroupCountX, m_DispatchGroupCountY, 1);

            BindCommonParams(cmd);
            cmd.SetComputeBufferParam(m_ClassificationCompute, m_BuildIndirectArgsKernel, MaterialTileClassesId, m_MaterialTileClasses.innerHandle);
            cmd.SetComputeBufferParam(m_ClassificationCompute, m_BuildIndirectArgsKernel, StandardMaterialIndicesId, m_StandardMaterialIndices.innerHandle);
            cmd.SetComputeBufferParam(m_ClassificationCompute, m_BuildIndirectArgsKernel, FabricMaterialIndicesId, m_FabricMaterialIndices.innerHandle);
            cmd.SetComputeBufferParam(m_ClassificationCompute, m_BuildIndirectArgsKernel, ClearCoatMaterialIndicesId, m_ClearCoatMaterialIndices.innerHandle);
            cmd.SetComputeBufferParam(m_ClassificationCompute, m_BuildIndirectArgsKernel, MaterialClassCountsId, m_MaterialClassCounts.innerHandle);
            cmd.SetComputeBufferParam(m_ClassificationCompute, m_BuildIndirectArgsKernel, StandardIndirectArgsId, m_StandardIndirectArgs.innerHandle);
            cmd.SetComputeBufferParam(m_ClassificationCompute, m_BuildIndirectArgsKernel, FabricIndirectArgsId, m_FabricIndirectArgs.innerHandle);
            cmd.SetComputeBufferParam(m_ClassificationCompute, m_BuildIndirectArgsKernel, ClearCoatIndirectArgsId, m_ClearCoatIndirectArgs.innerHandle);
            cmd.DispatchCompute(m_ClassificationCompute, m_BuildIndirectArgsKernel, m_BuildIndirectDispatchGroupCountX, 1, 1);
        }

        public override void Dispose()
        {
            ReleaseImportedBuffers();
            m_ClassificationCompute = null;
            m_ClearCountsKernel = -1;
            m_ClassifyMaterialKernel = -1;
            m_BuildIndirectArgsKernel = -1;
        }

        private void BindCommonParams(ComputeCommandBuffer cmd)
        {
            cmd.SetComputeIntParam(m_ClassificationCompute, ClassificationWidthId, m_ClassificationWidth);
            cmd.SetComputeIntParam(m_ClassificationCompute, ClassificationHeightId, m_ClassificationHeight);
            cmd.SetComputeIntParam(m_ClassificationCompute, MaterialTileCountId, m_MaterialTileCount);
            cmd.SetComputeIntParam(m_ClassificationCompute, MaterialTileCountXId, m_DispatchGroupCountX);
        }


        private static RenderGraphBuffer CreateStructuredBuffer(string name, int count, int stride)
        {
            return new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = count,
                    Stride = stride,
                    Target = GraphicsBuffer.Target.Structured,
                    Name = name
                }
            };
        }

        private static RenderGraphBuffer CreateIndirectArgsBuffer(string name)
        {
            return new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = IndirectArgsElementCount,
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

            buffer.desc.Count = IndirectArgsElementCount;
            buffer.desc.Stride = sizeof(uint);
            buffer.desc.Target = GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments;
        }

        private void EnsureImportedBuffers()
        {
            EnsureImportedBuffer(ref m_StandardMaterialIndicesBuffer, m_StandardMaterialIndices);
            EnsureImportedBuffer(ref m_FabricMaterialIndicesBuffer, m_FabricMaterialIndices);
            EnsureImportedBuffer(ref m_ClearCoatMaterialIndicesBuffer, m_ClearCoatMaterialIndices);
            EnsureImportedBuffer(ref m_MaterialTileClassesBuffer, m_MaterialTileClasses);
            EnsureImportedBuffer(ref m_MaterialClassCountsBuffer, m_MaterialClassCounts);
            EnsureImportedBuffer(ref m_StandardIndirectArgsBuffer, m_StandardIndirectArgs);
            EnsureImportedBuffer(ref m_FabricIndirectArgsBuffer, m_FabricIndirectArgs);
            EnsureImportedBuffer(ref m_ClearCoatIndirectArgsBuffer, m_ClearCoatIndirectArgs);
        }

        private void ReleaseImportedBuffers()
        {
            ReleaseImportedBuffer(ref m_StandardMaterialIndicesBuffer, m_StandardMaterialIndices);
            ReleaseImportedBuffer(ref m_FabricMaterialIndicesBuffer, m_FabricMaterialIndices);
            ReleaseImportedBuffer(ref m_ClearCoatMaterialIndicesBuffer, m_ClearCoatMaterialIndices);
            ReleaseImportedBuffer(ref m_MaterialTileClassesBuffer, m_MaterialTileClasses);
            ReleaseImportedBuffer(ref m_MaterialClassCountsBuffer, m_MaterialClassCounts);
            ReleaseImportedBuffer(ref m_StandardIndirectArgsBuffer, m_StandardIndirectArgs);
            ReleaseImportedBuffer(ref m_FabricIndirectArgsBuffer, m_FabricIndirectArgs);
            ReleaseImportedBuffer(ref m_ClearCoatIndirectArgsBuffer, m_ClearCoatIndirectArgs);
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
