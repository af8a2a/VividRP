using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class CMAA2Pass : ComputePass, IStablePassResourceLayout
    {
        private const int InputKernelSize = 16;
        private const int OutputKernelSize = InputKernelSize - 2;

        private static readonly int InputColorId = Shader.PropertyToID("_InputColor");
        private static readonly int OutputColorId = Shader.PropertyToID("_OutputColor");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_ScreenSize");

        private static readonly int CmaaInputColorId = Shader.PropertyToID("g_inoutColorReadonly");
        private static readonly int CmaaOutputColorId = Shader.PropertyToID("g_inoutColorWriteonly");
        private static readonly int CmaaWorkingEdgesId = Shader.PropertyToID("g_workingEdges");
        private static readonly int CmaaWorkingShapeCandidatesId = Shader.PropertyToID("g_workingShapeCandidates");
        private static readonly int CmaaWorkingDeferredBlendLocationListId = Shader.PropertyToID("g_workingDeferredBlendLocationList");
        private static readonly int CmaaWorkingDeferredBlendItemListId = Shader.PropertyToID("g_workingDeferredBlendItemList");
        private static readonly int CmaaWorkingDeferredBlendItemListHeadsId = Shader.PropertyToID("g_workingDeferredBlendItemListHeads");
        private static readonly int CmaaWorkingControlBufferId = Shader.PropertyToID("g_workingControlBuffer");
        private static readonly int CmaaWorkingExecuteIndirectBufferId = Shader.PropertyToID("g_workingExecuteIndirectBuffer");

        [RenderGraphResource(Name = "Color", Access = AccessFlags.Read)]
        private RenderGraphTexture m_ColorInput;

        [RenderGraphResource(
            Name = "CMAA2Edges",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_CmaaEdgesTexture;

        [RenderGraphResource(
            Name = "CMAA2DeferredBlendItemListHeads",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_CmaaDeferredBlendItemListHeadsTexture;

        [RenderGraphResource(
            Name = "CMAA2ShapeCandidates",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphBuffer m_CmaaShapeCandidatesBuffer;

        [RenderGraphResource(
            Name = "CMAA2DeferredBlendItemList",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphBuffer m_CmaaDeferredBlendItemListBuffer;

        [RenderGraphResource(
            Name = "CMAA2DeferredBlendLocationList",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphBuffer m_CmaaDeferredBlendLocationListBuffer;

        [RenderGraphResource(
            Name = "CMAA2ControlBuffer",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphBuffer m_CmaaControlBuffer;

        [RenderGraphResource(
            Name = "CMAA2ExecuteIndirectBuffer",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphBuffer m_CmaaExecuteIndirectBuffer;

        [RenderGraphResource(Name = "CMAA2Output", Access = AccessFlags.Write)]
        private RenderGraphTexture m_OutputTexture;

        private ComputeShader m_ComputeShader;
        private int m_CopyKernel = -1;
        private int m_CmaaEdgesColorKernel = -1;
        private int m_CmaaComputeDispatchArgsKernel = -1;
        private int m_CmaaProcessCandidatesKernel = -1;
        private int m_CmaaDeferredColorApplyKernel = -1;
        private int m_Width;
        private int m_Height;
        private bool m_IsPassResourceLayoutDirty;

        public bool IsPassResourceLayoutDirty => m_IsPassResourceLayoutDirty;

        public CMAA2Pass()
        {
            profilingSampler = new ProfilingSampler(nameof(CMAA2Pass));

            m_ColorInput = RenderGraphTexture.CreateInput("Color", GraphicsFormat.R16G16B16A16_SFloat);
            m_CmaaEdgesTexture = CreatePassOwnedTexture("CMAA2Edges", 1, 1, GraphicsFormat.R8_UInt);
            m_CmaaDeferredBlendItemListHeadsTexture = CreatePassOwnedTexture("CMAA2DeferredBlendItemListHeads", 1, 1, GraphicsFormat.R32_UInt);
            m_CmaaShapeCandidatesBuffer = RenderGraphBuffer.CreateStructured("CMAA2ShapeCandidates", 1, sizeof(uint));
            m_CmaaDeferredBlendItemListBuffer = RenderGraphBuffer.CreateStructured("CMAA2DeferredBlendItemList", 1, sizeof(uint) * 2);
            m_CmaaDeferredBlendLocationListBuffer = RenderGraphBuffer.CreateStructured("CMAA2DeferredBlendLocationList", 1, sizeof(uint));
            m_CmaaControlBuffer = CreateRawBuffer("CMAA2ControlBuffer", 16);
            m_CmaaExecuteIndirectBuffer = CreateIndirectArgsBuffer("CMAA2ExecuteIndirectBuffer", 4);
            m_OutputTexture = CreatePassOwnedTexture("CMAA2Output", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
        }

        public void ClearPassResourceLayoutDirty()
        {
            m_IsPassResourceLayoutDirty = false;
        }

        internal void SetInput(RenderGraphTexture colorInput)
        {
            if (colorInput == null)
                throw new ArgumentNullException(nameof(colorInput));

            if (ReferenceEquals(m_ColorInput, colorInput))
                return;

            m_ColorInput = colorInput;
            m_IsPassResourceLayoutDirty = true;
        }

        internal RenderGraphTexture GetOutputTexture()
        {
            return m_OutputTexture;
        }

        internal void SetOutput(RenderGraphTexture outputTexture)
        {
            if (outputTexture == null)
                throw new ArgumentNullException(nameof(outputTexture));

            if (ReferenceEquals(m_OutputTexture, outputTexture))
                return;

            m_OutputTexture = outputTexture;
            m_IsPassResourceLayoutDirty = true;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_ComputeShader = resources?.TemporalAACompute;
            if (m_ComputeShader == null)
                return;

            m_CopyKernel = m_ComputeShader.FindKernel("CopyColor");
            m_CmaaEdgesColorKernel = m_ComputeShader.FindKernel("EdgesColor2x2CS");
            m_CmaaComputeDispatchArgsKernel = m_ComputeShader.FindKernel("ComputeDispatchArgsCS");
            m_CmaaProcessCandidatesKernel = m_ComputeShader.FindKernel("ProcessCandidatesCS");
            m_CmaaDeferredColorApplyKernel = m_ComputeShader.FindKernel("DeferredColorApply2x2CS");
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();

            m_Width = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            m_Height = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;
            if (m_Width <= 0)
                m_Width = Mathf.Max(1, Screen.width);
            if (m_Height <= 0)
                m_Height = Mathf.Max(1, Screen.height);

            ResizePassOwned(m_OutputTexture, m_Width, m_Height);
            PrepareWorkingResources();
        }

        public override void Record(ComputePassContext context)
        {
            if (m_ComputeShader == null)
                return;

            if (m_ColorInput?.innerHandle.IsValid() != true || m_OutputTexture?.innerHandle.IsValid() != true)
                return;

            if (CanRunCmaa2())
            {
                RecordCmaa2(context);
            }
            else
            {
                RecordPassthrough(context);
            }
        }

        public override void Dispose()
        {
            m_ComputeShader = null;
            m_CopyKernel = -1;
            m_CmaaEdgesColorKernel = -1;
            m_CmaaComputeDispatchArgsKernel = -1;
            m_CmaaProcessCandidatesKernel = -1;
            m_CmaaDeferredColorApplyKernel = -1;
            m_IsPassResourceLayoutDirty = false;
        }

        private bool CanRunCmaa2()
        {
            return m_CopyKernel >= 0
                && m_CmaaEdgesColorKernel >= 0
                && m_CmaaComputeDispatchArgsKernel >= 0
                && m_CmaaProcessCandidatesKernel >= 0
                && m_CmaaDeferredColorApplyKernel >= 0
                && m_CmaaEdgesTexture?.innerHandle.IsValid() == true
                && m_CmaaDeferredBlendItemListHeadsTexture?.innerHandle.IsValid() == true
                && m_CmaaShapeCandidatesBuffer?.innerHandle.IsValid() == true
                && m_CmaaDeferredBlendItemListBuffer?.innerHandle.IsValid() == true
                && m_CmaaDeferredBlendLocationListBuffer?.innerHandle.IsValid() == true
                && m_CmaaControlBuffer?.innerHandle.IsValid() == true
                && m_CmaaExecuteIndirectBuffer?.innerHandle.IsValid() == true;
        }

        private void RecordPassthrough(ComputeGraphContext context)
        {
            if (m_CopyKernel < 0)
                return;

            var cmd = context.cmd;
            cmd.SetComputeTextureParam(m_ComputeShader, m_CopyKernel, InputColorId, m_ColorInput.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_CopyKernel, OutputColorId, m_OutputTexture.innerHandle);
            cmd.SetComputeVectorParam(
                m_ComputeShader,
                ScreenSizeId,
                new Vector4(m_Width, m_Height, 1.0f / m_Width, 1.0f / m_Height));

            int dispatchX = CoreUtils.DivRoundUp(m_Width, 8);
            int dispatchY = CoreUtils.DivRoundUp(m_Height, 8);
            cmd.DispatchCompute(m_ComputeShader, m_CopyKernel, dispatchX, dispatchY, 1);
        }

        private void RecordCmaa2(ComputeGraphContext context)
        {
            RecordPassthrough(context);

            var cmd = context.cmd;
            ClearCmaaCounters(cmd);

            int edgeDispatchX = CoreUtils.DivRoundUp(m_Width, OutputKernelSize * 2);
            int edgeDispatchY = CoreUtils.DivRoundUp(m_Height, OutputKernelSize * 2);

            cmd.SetComputeTextureParam(m_ComputeShader, m_CmaaEdgesColorKernel, CmaaInputColorId, m_ColorInput.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_CmaaEdgesColorKernel, CmaaWorkingEdgesId, m_CmaaEdgesTexture.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_CmaaEdgesColorKernel,
                CmaaWorkingDeferredBlendItemListHeadsId,
                m_CmaaDeferredBlendItemListHeadsTexture.innerHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, m_CmaaEdgesColorKernel, CmaaWorkingControlBufferId, m_CmaaControlBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaEdgesColorKernel,
                CmaaWorkingShapeCandidatesId,
                m_CmaaShapeCandidatesBuffer.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_CmaaEdgesColorKernel, edgeDispatchX, edgeDispatchY, 1);

            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaComputeDispatchArgsKernel,
                CmaaWorkingShapeCandidatesId,
                m_CmaaShapeCandidatesBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaComputeDispatchArgsKernel,
                CmaaWorkingControlBufferId,
                m_CmaaControlBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaComputeDispatchArgsKernel,
                CmaaWorkingDeferredBlendLocationListId,
                m_CmaaDeferredBlendLocationListBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaComputeDispatchArgsKernel,
                CmaaWorkingExecuteIndirectBufferId,
                m_CmaaExecuteIndirectBuffer.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_CmaaComputeDispatchArgsKernel, 2, 1, 1);

            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaProcessCandidatesKernel,
                CmaaWorkingShapeCandidatesId,
                m_CmaaShapeCandidatesBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaProcessCandidatesKernel,
                CmaaWorkingControlBufferId,
                m_CmaaControlBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaProcessCandidatesKernel,
                CmaaWorkingDeferredBlendLocationListId,
                m_CmaaDeferredBlendLocationListBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaProcessCandidatesKernel,
                CmaaWorkingDeferredBlendItemListId,
                m_CmaaDeferredBlendItemListBuffer.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_CmaaProcessCandidatesKernel,
                CmaaWorkingDeferredBlendItemListHeadsId,
                m_CmaaDeferredBlendItemListHeadsTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_CmaaProcessCandidatesKernel, CmaaWorkingEdgesId, m_CmaaEdgesTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_CmaaProcessCandidatesKernel, CmaaInputColorId, m_ColorInput.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_CmaaProcessCandidatesKernel, m_CmaaExecuteIndirectBuffer.innerHandle, 0);

            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaComputeDispatchArgsKernel,
                CmaaWorkingShapeCandidatesId,
                m_CmaaShapeCandidatesBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaComputeDispatchArgsKernel,
                CmaaWorkingControlBufferId,
                m_CmaaControlBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaComputeDispatchArgsKernel,
                CmaaWorkingDeferredBlendLocationListId,
                m_CmaaDeferredBlendLocationListBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaComputeDispatchArgsKernel,
                CmaaWorkingExecuteIndirectBufferId,
                m_CmaaExecuteIndirectBuffer.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_CmaaComputeDispatchArgsKernel, 1, 2, 1);

            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaDeferredColorApplyKernel,
                CmaaWorkingShapeCandidatesId,
                m_CmaaShapeCandidatesBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaDeferredColorApplyKernel,
                CmaaWorkingControlBufferId,
                m_CmaaControlBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaDeferredColorApplyKernel,
                CmaaWorkingDeferredBlendLocationListId,
                m_CmaaDeferredBlendLocationListBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaDeferredColorApplyKernel,
                CmaaWorkingDeferredBlendItemListId,
                m_CmaaDeferredBlendItemListBuffer.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_CmaaDeferredColorApplyKernel,
                CmaaWorkingDeferredBlendItemListHeadsId,
                m_CmaaDeferredBlendItemListHeadsTexture.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_CmaaDeferredColorApplyKernel,
                CmaaWorkingEdgesId,
                m_CmaaEdgesTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_CmaaDeferredColorApplyKernel, CmaaOutputColorId, m_OutputTexture.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_CmaaDeferredColorApplyKernel, m_CmaaExecuteIndirectBuffer.innerHandle, 0);
        }

        private void ClearCmaaCounters(ComputeCommandBuffer cmd)
        {
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaComputeDispatchArgsKernel,
                CmaaWorkingShapeCandidatesId,
                m_CmaaShapeCandidatesBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaComputeDispatchArgsKernel,
                CmaaWorkingControlBufferId,
                m_CmaaControlBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaComputeDispatchArgsKernel,
                CmaaWorkingDeferredBlendLocationListId,
                m_CmaaDeferredBlendLocationListBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_CmaaComputeDispatchArgsKernel,
                CmaaWorkingExecuteIndirectBufferId,
                m_CmaaExecuteIndirectBuffer.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_CmaaComputeDispatchArgsKernel, 1, 2, 1);
        }

        private void PrepareWorkingResources()
        {
            int halfWidth = Mathf.Max(1, (m_Width + 1) / 2);
            int halfHeight = Mathf.Max(1, (m_Height + 1) / 2);

            ResizePassOwned(m_CmaaEdgesTexture, halfWidth, m_Height);
            ResizePassOwned(m_CmaaDeferredBlendItemListHeadsTexture, halfWidth, halfHeight);
            m_CmaaEdgesTexture.desc.FilterMode = FilterMode.Point;
            m_CmaaDeferredBlendItemListHeadsTexture.desc.FilterMode = FilterMode.Point;

            int requiredCandidatePixels = Mathf.Max(1, (m_Width * m_Height) / 4);
            int requiredDeferredBlendItems = Mathf.Max(1, (m_Width * m_Height) / 2);
            int requiredDeferredBlendLocations = Mathf.Max(1, (m_Width * m_Height + 3) / 6);

            ResizeStructuredBuffer(m_CmaaShapeCandidatesBuffer, requiredCandidatePixels, sizeof(uint));
            ResizeStructuredBuffer(m_CmaaDeferredBlendItemListBuffer, requiredDeferredBlendItems, sizeof(uint) * 2);
            ResizeStructuredBuffer(m_CmaaDeferredBlendLocationListBuffer, requiredDeferredBlendLocations, sizeof(uint));
            ResizeRawBuffer(m_CmaaControlBuffer, 16);
            ResizeIndirectArgsBuffer(m_CmaaExecuteIndirectBuffer, 4);
        }

        private static RenderGraphTexture CreatePassOwnedTexture(
            string name,
            int width,
            int height,
            GraphicsFormat format)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(width, height, format)
            };
            texture.desc.Name = name;
            texture.desc.EnableRandomWrite = true;
            texture.desc.ClearBuffer = false;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        private static void ResizePassOwned(RenderGraphTexture texture, int width, int height)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = Mathf.Max(1, width);
            texture.desc.Height = Mathf.Max(1, height);
            texture.desc.EnableRandomWrite = true;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
        }

        private static RenderGraphBuffer CreateRawBuffer(string name, int count)
        {
            return new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = count,
                    Stride = sizeof(uint),
                    Target = GraphicsBuffer.Target.Raw,
                    Name = name
                }
            };
        }

        private static RenderGraphBuffer CreateIndirectArgsBuffer(string name, int count)
        {
            return new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = count,
                    Stride = sizeof(uint),
                    Target = GraphicsBuffer.Target.IndirectArguments,
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

        private static void ResizeRawBuffer(RenderGraphBuffer buffer, int count)
        {
            if (buffer?.desc == null)
                return;

            buffer.desc.Count = Mathf.Max(1, count);
            buffer.desc.Stride = sizeof(uint);
            buffer.desc.Target = GraphicsBuffer.Target.Raw;
        }

        private static void ResizeIndirectArgsBuffer(RenderGraphBuffer buffer, int count)
        {
            if (buffer?.desc == null)
                return;

            buffer.desc.Count = Mathf.Max(1, count);
            buffer.desc.Stride = sizeof(uint);
            buffer.desc.Target = GraphicsBuffer.Target.IndirectArguments;
        }
    }
}
