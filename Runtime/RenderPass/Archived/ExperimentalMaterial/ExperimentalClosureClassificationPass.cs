using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Experimental.Material
{
    public sealed class ExperimentalClosureClassificationPass
        : ComputePass, IAsyncComputeSupportedPass
    {
        internal const int VariantCount = 3;
        internal const int IndirectArgsElementCount = 4;
        internal const int TileSize = 8;

        private static readonly int ClosureBuffer0Id =
            Shader.PropertyToID("_ExperimentalClosureBuffer0");
        private static readonly int DepthTextureId =
            Shader.PropertyToID("_DepthTexture");
        private static readonly int ClassificationWidthId =
            Shader.PropertyToID("_ClassificationWidth");
        private static readonly int ClassificationHeightId =
            Shader.PropertyToID("_ClassificationHeight");
        private static readonly int TileCountId =
            Shader.PropertyToID("_ExperimentalClosureTileCount");
        private static readonly int TileCountXId =
            Shader.PropertyToID("_ExperimentalClosureTileCountX");
        private static readonly int TileClassesId =
            Shader.PropertyToID("_ExperimentalClosureTileClasses");
        private static readonly int TileListId =
            Shader.PropertyToID("_ExperimentalClosureTileList");
        private static readonly int IndirectArgsId =
            Shader.PropertyToID("_ExperimentalClosureIndirectArgs");

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer0",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_ClosureBuffer0;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(
            Name = "ExperimentalClosureTileClasses",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_TileClasses;

        [RenderGraphResource(
            Name = "ExperimentalClosureTileList",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_TileList;

        [RenderGraphResource(
            Name = "ExperimentalClosureIndirectArgs",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_IndirectArgs;

        private ComputeShader m_Compute;
        private int m_ClearKernel = -1;
        private int m_ClassifyKernel = -1;
        private int m_Width = 1;
        private int m_Height = 1;
        private int m_TileCountX = 1;
        private int m_TileCountY = 1;
        private int m_TileCount = 1;

        public ExperimentalClosureClassificationPass()
        {
            m_ClosureBuffer0 = RenderGraphTexture.CreateInput(
                "ExperimentalClosureBuffer0",
                GraphicsFormat.R8G8B8A8_UNorm);
            m_DepthTexture = RenderGraphTexture.CreateInput(
                "Depth",
                GraphicsFormat.None,
                DepthBits.Depth32);
            m_TileClasses = RenderGraphBuffer.CreateStructured(
                "ExperimentalClosureTileClasses",
                sizeof(uint));
            m_TileList = RenderGraphBuffer.CreateStructured(
                "ExperimentalClosureTileList",
                sizeof(uint));
            m_IndirectArgs = RenderGraphBuffer.CreateStructured(
                "ExperimentalClosureIndirectArgs",
                VariantCount * IndirectArgsElementCount,
                sizeof(uint),
                GraphicsBuffer.Target.Structured
                    | GraphicsBuffer.Target.IndirectArguments);
        }

        public override void Create()
        {
            m_Compute = PipelineResourceManager.Get<VividRPCoreResources>()
                ?.ExperimentalClosureClassificationCompute;
            if (m_Compute == null)
                return;

            m_ClearKernel = m_Compute.FindKernel(
                "ClearExperimentalClosureArgs");
            m_ClassifyKernel = m_Compute.FindKernel(
                "ClassifyExperimentalClosureTiles");
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            m_Width = cameraData.actualWidth > 0
                ? cameraData.actualWidth
                : cameraData.pixelWidth;
            m_Height = cameraData.actualHeight > 0
                ? cameraData.actualHeight
                : cameraData.pixelHeight;

            if (m_Width <= 0)
                m_Width = Mathf.Max(1, Screen.width);
            if (m_Height <= 0)
                m_Height = Mathf.Max(1, Screen.height);

            m_TileCountX = Mathf.Max(1, (m_Width + TileSize - 1) / TileSize);
            m_TileCountY = Mathf.Max(1, (m_Height + TileSize - 1) / TileSize);
            m_TileCount = Mathf.Max(1, m_TileCountX * m_TileCountY);

            m_ClosureBuffer0.Resize(m_Width, m_Height);
            m_DepthTexture.Resize(m_Width, m_Height);
            ResizeBuffer(m_TileClasses, m_TileCount, GraphicsBuffer.Target.Structured);
            ResizeBuffer(
                m_TileList,
                m_TileCount * VariantCount,
                GraphicsBuffer.Target.Structured);
            ResizeBuffer(
                m_IndirectArgs,
                VariantCount * IndirectArgsElementCount,
                GraphicsBuffer.Target.Structured
                    | GraphicsBuffer.Target.IndirectArguments);
            m_TileClasses.EnsureImportedBuffer();
            m_TileList.EnsureImportedBuffer();
            m_IndirectArgs.EnsureImportedBuffer();
        }

        public override void Record(ComputePassContext context)
        {
            if (m_Compute == null || m_ClearKernel < 0 || m_ClassifyKernel < 0)
                return;

            var cmd = context.cmd;
            cmd.SetComputeBufferParam(
                m_Compute,
                m_ClearKernel,
                IndirectArgsId,
                m_IndirectArgs.innerHandle);
            cmd.DispatchCompute(m_Compute, m_ClearKernel, 1, 1, 1);

            BindClassificationParameters(cmd);
            cmd.DispatchCompute(
                m_Compute,
                m_ClassifyKernel,
                m_TileCountX,
                m_TileCountY,
                1);
        }

        public override void Dispose()
        {
            m_TileClasses?.ClearImportedBuffer();
            m_TileList?.ClearImportedBuffer();
            m_IndirectArgs?.ClearImportedBuffer();
            m_Compute = null;
            m_ClearKernel = -1;
            m_ClassifyKernel = -1;
        }

        private void BindClassificationParameters(ComputeCommandBuffer cmd)
        {
            cmd.SetComputeIntParam(m_Compute, ClassificationWidthId, m_Width);
            cmd.SetComputeIntParam(m_Compute, ClassificationHeightId, m_Height);
            cmd.SetComputeIntParam(m_Compute, TileCountId, m_TileCount);
            cmd.SetComputeIntParam(m_Compute, TileCountXId, m_TileCountX);
            cmd.SetComputeTextureParam(
                m_Compute,
                m_ClassifyKernel,
                ClosureBuffer0Id,
                m_ClosureBuffer0.innerHandle);
            cmd.SetComputeTextureParam(
                m_Compute,
                m_ClassifyKernel,
                DepthTextureId,
                m_DepthTexture.innerHandle);
            cmd.SetComputeBufferParam(
                m_Compute,
                m_ClassifyKernel,
                TileClassesId,
                m_TileClasses.innerHandle);
            cmd.SetComputeBufferParam(
                m_Compute,
                m_ClassifyKernel,
                TileListId,
                m_TileList.innerHandle);
            cmd.SetComputeBufferParam(
                m_Compute,
                m_ClassifyKernel,
                IndirectArgsId,
                m_IndirectArgs.innerHandle);
        }

        private static void ResizeBuffer(
            RenderGraphBuffer buffer,
            int count,
            GraphicsBuffer.Target target)
        {
            buffer.desc.Count = Mathf.Max(1, count);
            buffer.desc.Stride = sizeof(uint);
            buffer.desc.Target = target;
        }
    }
}
