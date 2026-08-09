using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class TemporalAAPass : ComputePass
    {
        private static readonly int InputColorId = Shader.PropertyToID("_InputColor");
        private static readonly int MotionVectorsId = Shader.PropertyToID("_MotionVectors");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int HistoryColorId = Shader.PropertyToID("_HistoryColor");
        private static readonly int OutputColorId = Shader.PropertyToID("_OutputColor");
        private static readonly int HistoryColorWriteId = Shader.PropertyToID("_HistoryColorWrite");
        private static readonly int TAAParamsId = Shader.PropertyToID("_TAAParams");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_ScreenSize");
        private static readonly int JitterId = Shader.PropertyToID("_Jitter");

        [RenderGraphResource(Name = "Color", Access = AccessFlags.Read)]
        private RenderGraphTexture m_ColorInput;

        [RenderGraphResource(Name = "MotionVectors", Access = AccessFlags.Read)]
        private RenderGraphTexture m_MotionVectors;

        [RenderGraphResource(Name = "CameraDepth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        private RenderGraphTexture m_HistoryColorPrevious;

        private RenderGraphTexture m_HistoryColorCurrent;

        [RenderGraphResource(Name = "TAAOutput", Access = AccessFlags.WriteAll)]
        private RenderGraphTexture m_OutputTexture;

        private ComputeShader m_ComputeShader;
        private int m_TaaKernel = -1;
        private int m_CopyKernel = -1;
        private int m_Width;
        private int m_Height;
        private TAASettings m_TAASettings;
        private readonly RenderGraphTextureDesc m_HistoryColorDescriptor =
            RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R16G16B16A16_SFloat);
        private CameraHistoryTexture m_HistoryColor;
        private bool m_HasValidHistory;
        private Vector2 m_Jitter;
        private Vector2 m_PreviousJitter;
        private bool m_IsFirstFrame;

        public TemporalAAPass()
        {
            profilingSampler = new ProfilingSampler(nameof(TemporalAAPass));

            m_ColorInput = RenderGraphTexture.CreateInput("Color", GraphicsFormat.R16G16B16A16_SFloat);
            m_MotionVectors = RenderGraphTexture.CreateInput("MotionVectors", GraphicsFormat.R16G16_SFloat);
            m_DepthTexture = RenderGraphTexture.CreateInput("CameraDepth", GraphicsFormat.None, DepthBits.Depth32);
            m_HistoryColorPrevious = RenderGraphTexture.CreateInput("TAAHistoryColor", GraphicsFormat.R16G16B16A16_SFloat);
            m_HistoryColorCurrent = CreatePassOwnedTexture("TAAHistoryColorCurrent", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_OutputTexture = CreatePassOwnedTexture("TAAOutput", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_ComputeShader = resources?.TemporalAACompute;
            if (m_ComputeShader == null)
                return;

            m_TaaKernel = m_ComputeShader.FindKernel("TemporalAA");
            m_CopyKernel = m_ComputeShader.FindKernel("CopyColor");
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var temporalData = frameData.Get<VividTemporalData>();

            m_Width = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            m_Height = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;
            if (m_Width <= 0)
                m_Width = Mathf.Max(1, Screen.width);
            if (m_Height <= 0)
                m_Height = Mathf.Max(1, Screen.height);

            m_TAASettings = TAASettings.FromCamera(cameraData.additionalData);
            m_IsFirstFrame = temporalData != null && temporalData.isFirstFrame;
            m_Jitter = temporalData != null ? temporalData.jitter : Vector2.zero;
            m_PreviousJitter = temporalData != null ? temporalData.previousJitter : Vector2.zero;

            ResizePassOwned(m_OutputTexture, m_Width, m_Height);
            ResizePassOwned(m_HistoryColorCurrent, m_Width, m_Height);

            var historyDesc = CreateHistoryDescriptor();
            m_HistoryColor = null;

            if (m_TAASettings.Enabled)
            {
                m_HasValidHistory = CameraHistoryRenderGraphBridge.PrepareTexturePair(
                    this,
                    cameraData.camera,
                    CameraHistoryIds.TemporalAa,
                    m_HistoryColorPrevious,
                    m_HistoryColorCurrent,
                    historyDesc,
                    out m_HistoryColor);
            }
            else
            {
                m_HasValidHistory = false;
            }
        }

        public override void Record(ComputePassContext context)
        {
            if (m_ComputeShader == null)
                return;

            if (m_ColorInput?.innerHandle.IsValid() != true || m_OutputTexture?.innerHandle.IsValid() != true)
                return;

            if (m_TAASettings.Enabled && m_TaaKernel >= 0)
            {
                RecordTAA(context);
            }
            else
            {
                RecordPassthrough(context);
            }
        }

        public override void Dispose()
        {
            m_ComputeShader = null;
            m_TaaKernel = -1;
            m_CopyKernel = -1;
            m_HistoryColor = null;
            m_HasValidHistory = false;
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

        private void RecordTAA(ComputeGraphContext context)
        {
            var cmd = context.cmd;

            cmd.SetComputeTextureParam(m_ComputeShader, m_TaaKernel, InputColorId, m_ColorInput.innerHandle);

            if (m_MotionVectors?.innerHandle.IsValid() == true)
                cmd.SetComputeTextureParam(m_ComputeShader, m_TaaKernel, MotionVectorsId, m_MotionVectors.innerHandle);

            if (m_DepthTexture?.innerHandle.IsValid() == true)
                cmd.SetComputeTextureParam(m_ComputeShader, m_TaaKernel, DepthTextureId, m_DepthTexture.innerHandle);

            var historyHandle = m_HasValidHistory && !m_IsFirstFrame && m_HistoryColorPrevious?.innerHandle.IsValid() == true
                ? m_HistoryColorPrevious.innerHandle
                : m_ColorInput.innerHandle;
            cmd.SetComputeTextureParam(m_ComputeShader, m_TaaKernel, HistoryColorId, historyHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_TaaKernel, OutputColorId, m_OutputTexture.innerHandle);

            if (m_HistoryColorCurrent?.innerHandle.IsValid() == true)
                cmd.SetComputeTextureParam(m_ComputeShader, m_TaaKernel, HistoryColorWriteId, m_HistoryColorCurrent.innerHandle);

            float hasHistory = m_HasValidHistory && !m_IsFirstFrame ? 1.0f : 0.0f;
            cmd.SetComputeVectorParam(
                m_ComputeShader,
                TAAParamsId,
                new Vector4(
                    m_TAASettings.BaseBlendFactor,
                    m_TAASettings.MotionWeightDecay,
                    m_TAASettings.AntiFlickerIntensity,
                    hasHistory));
            cmd.SetComputeVectorParam(
                m_ComputeShader,
                ScreenSizeId,
                new Vector4(m_Width, m_Height, 1.0f / m_Width, 1.0f / m_Height));
            cmd.SetComputeVectorParam(
                m_ComputeShader,
                JitterId,
                new Vector4(m_Jitter.x, m_Jitter.y, m_PreviousJitter.x, m_PreviousJitter.y));

            int dispatchX = CoreUtils.DivRoundUp(m_Width, 8);
            int dispatchY = CoreUtils.DivRoundUp(m_Height, 8);
            m_HistoryColor?.MarkWritten();
            cmd.DispatchCompute(m_ComputeShader, m_TaaKernel, dispatchX, dispatchY, 1);
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

        private RenderGraphTextureDesc CreateHistoryDescriptor()
        {
            var desc = m_HistoryColorDescriptor;
            if (m_HistoryColorPrevious?.desc != null)
                m_HistoryColorPrevious.desc.Copy(desc);

            desc.Name = "TAAHistoryColorCurrent";
            desc.Width = m_Width;
            desc.Height = m_Height;
            desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            desc.DepthBufferBits = DepthBits.None;
            desc.MsaaSamples = MSAASamples.None;
            desc.ClearBuffer = false;
            desc.EnableRandomWrite = true;
            desc.FilterMode = FilterMode.Point;
            desc.WrapMode = TextureWrapMode.Clamp;
            desc.UseMipMap = false;
            desc.AutoGenerateMips = false;
            desc.MipCount = 1;
            desc.BindTextureMS = false;
            return desc;
        }
    }

}
