using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class ScreenSpaceReflectionPass : ComputePass, IRenderGraphRecordingPass, IStablePassResourceLayout
    {
        private const int ThreadGroupSize = 8;
        private const int IndirectArgsElementCount = 4;
        private const int MaxDepthPyramidMipCount = 15;
        private const string RenderSSRProfilerTag = "RenderSSR";
        private const string SSRClassifyTilesProfilerTag = "SSRClassifyTiles";
        private const string SSRTracingProfilerTag = "SSRTracing";
        private const string SSRResolveProfilerTag = "SSRResolve";
        private const string SSRAccumulateProfilerTag = "SSRAccumulate";

        private static readonly uint[] s_InitialDispatchIndirectArgsData = { 0u, 1u, 1u, 0u };
        private static readonly ProfilingSampler s_SSRClassifyTilesProfilingSampler = new(SSRClassifyTilesProfilerTag);
        private static readonly ProfilingSampler s_SSRTracingProfilingSampler = new(SSRTracingProfilerTag);
        private static readonly ProfilingSampler s_SSRResolveProfilingSampler = new(SSRResolveProfilerTag);
        private static readonly ProfilingSampler s_SSRAccumulateProfilingSampler = new(SSRAccumulateProfilerTag);

        private static readonly int ConstantBufferId = Shader.PropertyToID("ShaderVariablesScreenSpaceReflection");
        private static readonly int OutputColorTextureId = Shader.PropertyToID("_OutputColorTexture");
        private static readonly int SSRTraceTextureId = Shader.PropertyToID("_SSRTraceTexture");
        private static readonly int SSRResolveTextureId = Shader.PropertyToID("_SSRResolveTexture");
        private static readonly int SSRTileListId = Shader.PropertyToID("_SSRTileList");
        private static readonly int SSRDispatchIndirectArgsId = Shader.PropertyToID("_SSRDispatchIndirectArgs");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int HZBTextureId = Shader.PropertyToID("_HZBTexture");
        private static readonly int GBuffer0Id = Shader.PropertyToID("_GBuffer0");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int GBuffer2Id = Shader.PropertyToID("_GBuffer2");
        private static readonly int PreviousColorPyramidTextureId = Shader.PropertyToID("_PreviousColorPyramidTexture");
        private static readonly int DepthPyramidMipLevelOffsetsId = Shader.PropertyToID("_DepthPyramidMipLevelOffsets");
        private static readonly int SkyTextureId = Shader.PropertyToID("_SkyTexture");
        private static readonly int SkyTextureTintId = Shader.PropertyToID("_SkyTextureTint");
        private static readonly int SkyTextureParamsId = Shader.PropertyToID("_SkyTextureParams");

        [StructLayout(LayoutKind.Sequential)]
        private struct ScreenSpaceReflectionConstantBufferData
        {
            public Vector4 SsrTraceScreenSize;
            public float SsrThicknessScale;
            public float SsrThicknessBias;
            public int SsrIterLimit;
            public int SsrDepthPyramidMaxMip;
            public float SsrRoughnessFadeEnd;
            public float SsrRoughnessFadeRcpLength;
            public float SsrRoughnessFadeEndTimesRcpLength;
            public float SsrEdgeFadeRcpLength;
            public float SsrIntensity;
            public float SsrIntensityClamp;
            public int SsrReflectsSky;
            public float Padding0;
            public Vector4 SsrHistoryColorPyramidSize;
            public int SsrUseHistoryColorPyramid;
            public int SsrHistoryColorPyramidMaxMip;
            public Vector2 Padding1;
        }

        private sealed class GraphPassData
        {
            public ScreenSpaceReflectionPass Pass;
            public ContextContainer FrameData;
        }

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "HZB", Access = AccessFlags.Read)]
        private RenderGraphTexture m_HZBTexture;

        [RenderGraphResource(Name = "HZBMipLevelOffsets", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_HZBMipLevelOffsets;

        [RenderGraphResource(Name = "GBuffer0", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer0;

        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(Name = "GBuffer2", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer2;

        [RenderGraphResource(
            Name = "ScreenSpaceReflectionOutput",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture output;

        private ComputeShader m_ComputeShader;
        private readonly RenderGraphTexture m_DefaultHZBTexture;
        private readonly RenderGraphBuffer m_DefaultHZBMipLevelOffsets;
        private readonly RenderGraphTexture m_TraceTexture;
        private readonly RenderGraphTexture m_ResolveTexture;
        private readonly RenderGraphBuffer m_TileListBuffer;
        private readonly RenderGraphBuffer m_DispatchIndirectArgsBuffer;
        private readonly RenderGraphTexture m_SkyTexture;
        private int m_SSRClassifyTilesKernel = -1;
        private int m_SSRTracingKernel = -1;
        private int m_SSRResolveKernel = -1;
        private int m_SSRAccumulateKernel = -1;
        private int m_CopyKernel = -1;
        private int m_Width = 1;
        private int m_Height = 1;
        private int m_TileCountX = 1;
        private int m_TileCountY = 1;
        private bool m_ShouldApply;
        private bool m_IsPassResourceLayoutDirty;
        private bool m_UseHistoryColorPyramid;
        private RenderGraphTexture m_PreviousColorPyramidTexture;
        private Color m_SkyTextureTint = Color.white;
        private Vector4 m_SkyTextureParams;
        private ScreenSpaceReflectionSettingsData m_Settings;
        private ScreenSpaceReflectionConstantBufferData m_ConstantBuffer;

        public bool IsPassResourceLayoutDirty => m_IsPassResourceLayoutDirty;

        public ScreenSpaceReflectionPass()
        {
            profilingSampler = new ProfilingSampler(RenderSSRProfilerTag);

            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_HZBTexture = RenderGraphTexture.CreateInput("HZB", GraphicsFormat.R32_SFloat);
            m_HZBMipLevelOffsets = RenderGraphBuffer.CreateStructured("HZBMipLevelOffsets", MaxDepthPyramidMipCount, sizeof(int) * 2);
            m_DefaultHZBTexture = m_HZBTexture;
            m_DefaultHZBMipLevelOffsets = m_HZBMipLevelOffsets;
            m_GBuffer0 = RenderGraphTexture.CreateInput("GBuffer0", GraphicsFormat.R8G8B8A8_SRGB);
            m_GBuffer1 = RenderGraphTexture.CreateInput("GBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);
            m_GBuffer2 = RenderGraphTexture.CreateInput("GBuffer2", GraphicsFormat.R8G8B8A8_UNorm);
            output = CreateColorTexture("ScreenSpaceReflectionOutput", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_TraceTexture = CreateColorTexture("ScreenSpaceReflectionTrace", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_ResolveTexture = CreateColorTexture("ScreenSpaceReflectionResolve", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_TileListBuffer = RenderGraphBuffer.CreateStructured("SSRTileList", 1, sizeof(uint));
            m_DispatchIndirectArgsBuffer = RenderGraphBuffer.CreateStructured(
                "SSRDispatchIndirectArgs",
                IndirectArgsElementCount,
                sizeof(uint),
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments);
            m_SkyTexture = CreateSkyCubemapTexture("ScreenSpaceReflectionSkyTexture");

            ConfigureHZBDescriptor(m_HZBTexture);
            ConfigureInternalTextureDescriptor(m_TraceTexture, "ScreenSpaceReflectionTrace", 1, 1);
            ConfigureInternalTextureDescriptor(m_ResolveTexture, "ScreenSpaceReflectionResolve", 1, 1);
        }

        public void ClearPassResourceLayoutDirty()
        {
            m_IsPassResourceLayoutDirty = false;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_ComputeShader = resources?.ScreenSpaceReflectionCompute;
            if (m_ComputeShader == null)
                return;

            try
            {
                m_SSRClassifyTilesKernel = m_ComputeShader.FindKernel("ScreenSpaceReflectionsClassifyTiles");
                m_SSRTracingKernel = m_ComputeShader.FindKernel("ScreenSpaceReflectionsTracing");
                m_SSRResolveKernel = m_ComputeShader.FindKernel("ScreenSpaceReflectionsResolve");
                m_SSRAccumulateKernel = m_ComputeShader.FindKernel("ScreenSpaceReflectionsAccumulate");
                m_CopyKernel = m_ComputeShader.FindKernel("CopyScreenSpaceReflection");
            }
            catch (ArgumentException)
            {
                m_SSRClassifyTilesKernel = -1;
                m_SSRTracingKernel = -1;
                m_SSRResolveKernel = -1;
                m_SSRAccumulateKernel = -1;
                m_CopyKernel = -1;
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var camera = cameraData?.camera;
            var postProcessingAllowed = camera != null && CoreUtils.ArePostProcessesEnabled(camera);

            m_Width = CameraDimensionUtility.ResolveCameraDimension(cameraData?.actualWidth ?? 0, cameraData?.pixelWidth ?? 0, Screen.width);
            m_Height = CameraDimensionUtility.ResolveCameraDimension(cameraData?.actualHeight ?? 0, cameraData?.pixelHeight ?? 0, Screen.height);
            m_Settings = postProcessingAllowed
                ? ScreenSpaceReflectionSettingsResolver.Resolve()
                : ScreenSpaceReflectionSettingsData.CreateDefault();
            m_ShouldApply = postProcessingAllowed && m_Settings.enabled;
            PrepareSkyTextureState(frameData.GetOrCreate<VividSkyData>());

            ResizeInputTexture(m_DepthTexture, m_Width, m_Height);
            ResizeInputTexture(m_GBuffer0, m_Width, m_Height);
            ResizeInputTexture(m_GBuffer1, m_Width, m_Height);
            ResizeInputTexture(m_GBuffer2, m_Width, m_Height);
            if (ReferenceEquals(m_HZBTexture, m_DefaultHZBTexture))
            {
                ResizeInputTexture(m_HZBTexture, m_Width, m_Height);
                ConfigureHZBDescriptor(m_HZBTexture);
            }

            if (ReferenceEquals(m_HZBMipLevelOffsets, m_DefaultHZBMipLevelOffsets))
                ConfigureHZBMipLevelOffsetBuffer(m_HZBMipLevelOffsets);

            UpdateOutputDescriptor(m_Width, m_Height);
            UpdateTileResourcesDescriptor(m_Width, m_Height);

            m_ConstantBuffer = BuildConstantBuffer(camera, m_Width, m_Height, m_Settings);
            PrepareFrameContextOutput(frameData);
        }

        public void RecordGraph(RenderGraphRecordingContext context)
        {
            if (context?.RenderGraph == null)
                return;

            ResolveColorPyramidHistory(context);

            if (!ShouldRecordEffect() && !CanRecordCopy())
                return;

            var outputHandle = context.GetOrCreateTextureHandle(output);
            var depthHandle = context.GetOrCreateTextureHandle(m_DepthTexture);
            var hzbHandle = context.GetOrCreateTextureHandle(m_HZBTexture);
            var hzbMipLevelOffsetsHandle = context.GetOrCreateBufferHandle(m_HZBMipLevelOffsets);
            var gbuffer0Handle = context.GetOrCreateTextureHandle(m_GBuffer0);
            var gbuffer1Handle = context.GetOrCreateTextureHandle(m_GBuffer1);
            var gbuffer2Handle = context.GetOrCreateTextureHandle(m_GBuffer2);
            var traceHandle = context.GetOrCreateTextureHandle(m_TraceTexture);
            var resolveHandle = context.GetOrCreateTextureHandle(m_ResolveTexture);
            var tileListHandle = context.GetOrCreateBufferHandle(m_TileListBuffer);
            var dispatchIndirectArgsHandle = context.GetOrCreateBufferHandle(m_DispatchIndirectArgsBuffer);
            var skyTextureHandle = context.GetOrCreateTextureHandle(m_SkyTexture);

            output.innerHandle = outputHandle;
            m_DepthTexture.innerHandle = depthHandle;
            m_HZBTexture.innerHandle = hzbHandle;
            m_HZBMipLevelOffsets.innerHandle = hzbMipLevelOffsetsHandle;
            m_GBuffer0.innerHandle = gbuffer0Handle;
            m_GBuffer1.innerHandle = gbuffer1Handle;
            m_GBuffer2.innerHandle = gbuffer2Handle;
            m_TraceTexture.innerHandle = traceHandle;
            m_ResolveTexture.innerHandle = resolveHandle;
            m_TileListBuffer.innerHandle = tileListHandle;
            m_DispatchIndirectArgsBuffer.innerHandle = dispatchIndirectArgsHandle;
            m_SkyTexture.innerHandle = skyTextureHandle;

            using var builder = context.RenderGraph.AddComputePass<GraphPassData>(
                RenderSSRProfilerTag,
                out var passData);

            passData.Pass = this;
            passData.FrameData = context.FrameData;

            builder.UseTexture(outputHandle, AccessFlags.Write);
            builder.UseTexture(depthHandle, AccessFlags.Read);
            builder.UseTexture(hzbHandle, AccessFlags.Read);
            builder.UseBuffer(hzbMipLevelOffsetsHandle, AccessFlags.Read);
            builder.UseTexture(gbuffer0Handle, AccessFlags.Read);
            builder.UseTexture(gbuffer1Handle, AccessFlags.Read);
            builder.UseTexture(gbuffer2Handle, AccessFlags.Read);
            builder.UseTexture(traceHandle, AccessFlags.ReadWrite);
            builder.UseTexture(resolveHandle, AccessFlags.ReadWrite);
            builder.UseBuffer(tileListHandle, AccessFlags.ReadWrite);
            builder.UseBuffer(dispatchIndirectArgsHandle, AccessFlags.ReadWrite);
            builder.UseTexture(skyTextureHandle, AccessFlags.Read);

            if (m_UseHistoryColorPyramid && m_PreviousColorPyramidTexture?.innerHandle.IsValid() == true)
                builder.UseTexture(m_PreviousColorPyramidTexture.innerHandle, AccessFlags.Read);

            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (GraphPassData data, ComputeGraphContext graphContext) =>
            {
                data.Pass.Record(new ComputePassContext(graphContext, data.FrameData));
            });
        }

        public override void Record(ComputePassContext context)
        {
            var cmd = context.cmd;
            using (new ProfilingScope(cmd, profilingSampler))
            {
                BindSkyParameters(cmd);

                if (!ShouldRecordEffect())
                {
                    DispatchCopy(cmd);
                    return;
                }

                if (!CanExecute())
                {
                    DispatchCopy(cmd);
                    return;
                }

                ConstantBuffer.Push(cmd, m_ConstantBuffer, m_ComputeShader, ConstantBufferId);

                using (new ProfilingScope(cmd, s_SSRClassifyTilesProfilingSampler))
                    DispatchClassifyTiles(cmd);

                using (new ProfilingScope(cmd, s_SSRTracingProfilingSampler))
                    DispatchTrace(cmd);

                using (new ProfilingScope(cmd, s_SSRResolveProfilingSampler))
                    DispatchResolve(cmd);

                using (new ProfilingScope(cmd, s_SSRAccumulateProfilingSampler))
                    DispatchAccumulate(cmd);
            }
        }

        public override void Dispose()
        {
            m_ComputeShader = null;
            m_SSRClassifyTilesKernel = -1;
            m_SSRTracingKernel = -1;
            m_SSRResolveKernel = -1;
            m_SSRAccumulateKernel = -1;
            m_CopyKernel = -1;
            m_ShouldApply = false;
            m_IsPassResourceLayoutDirty = false;
            m_UseHistoryColorPyramid = false;
            m_PreviousColorPyramidTexture = null;
            m_SkyTextureTint = Color.white;
            m_SkyTextureParams = Vector4.zero;
            m_Settings = ScreenSpaceReflectionSettingsData.CreateDefault();
            m_ConstantBuffer = default;
        }

        private bool ResolveColorPyramidHistory(RenderGraphRecordingContext context)
        {
            m_UseHistoryColorPyramid = false;
            m_PreviousColorPyramidTexture = null;
            m_ConstantBuffer.SsrUseHistoryColorPyramid = 0;
            m_ConstantBuffer.SsrHistoryColorPyramidMaxMip = 0;
            m_ConstantBuffer.SsrHistoryColorPyramidSize = Vector4.zero;

            if (context?.FrameData == null || !context.FrameData.Contains<VividColorPyramidData>())
                return false;

            var colorPyramidData = context.FrameData.Get<VividColorPyramidData>();
            if (colorPyramidData == null
                || !colorPyramidData.hasValidHistory
                || colorPyramidData.previousColorPyramid == null
                || colorPyramidData.width <= 0
                || colorPyramidData.height <= 0)
            {
                return false;
            }

            var historyHandle = context.GetOrCreateTextureHandle(colorPyramidData.previousColorPyramid);
            if (!historyHandle.IsValid())
                return false;

            colorPyramidData.previousColorPyramid.innerHandle = historyHandle;
            m_PreviousColorPyramidTexture = colorPyramidData.previousColorPyramid;
            m_UseHistoryColorPyramid = true;
            m_ConstantBuffer.SsrUseHistoryColorPyramid = 1;
            m_ConstantBuffer.SsrHistoryColorPyramidMaxMip = Mathf.Max(0, colorPyramidData.mipCount - 1);
            m_ConstantBuffer.SsrHistoryColorPyramidSize = new Vector4(
                colorPyramidData.width,
                colorPyramidData.height,
                1.0f / Mathf.Max(1, colorPyramidData.width),
                1.0f / Mathf.Max(1, colorPyramidData.height));
            return true;
        }

        private bool ShouldRecordEffect()
        {
            return m_ShouldApply
                && m_ComputeShader != null
                && m_SSRTracingKernel >= 0
                && m_Width > 0
                && m_Height > 0;
        }

        private bool CanRecordCopy()
        {
            return m_ComputeShader != null
                && m_CopyKernel >= 0
                && m_Width > 0
                && m_Height > 0;
        }

        private bool CanExecute()
        {
            bool canSampleHistory = m_UseHistoryColorPyramid
                && m_PreviousColorPyramidTexture?.innerHandle.IsValid() == true;
            bool canSampleSkyFallback = m_Settings.reflectSky
                && m_SkyTextureParams.w > 0.5f
                && m_SkyTexture?.innerHandle.IsValid() == true;

            return (canSampleHistory || canSampleSkyFallback)
                && m_SSRClassifyTilesKernel >= 0
                && m_SSRTracingKernel >= 0
                && m_SSRResolveKernel >= 0
                && m_SSRAccumulateKernel >= 0
                && output?.innerHandle.IsValid() == true
                && m_TraceTexture?.innerHandle.IsValid() == true
                && m_ResolveTexture?.innerHandle.IsValid() == true
                && m_TileListBuffer?.innerHandle.IsValid() == true
                && m_DispatchIndirectArgsBuffer?.innerHandle.IsValid() == true
                && m_SkyTexture?.innerHandle.IsValid() == true
                && m_DepthTexture?.innerHandle.IsValid() == true
                && m_HZBTexture?.innerHandle.IsValid() == true
                && m_HZBMipLevelOffsets?.innerHandle.IsValid() == true
                && m_GBuffer0?.innerHandle.IsValid() == true
                && m_GBuffer1?.innerHandle.IsValid() == true
                && m_GBuffer2?.innerHandle.IsValid() == true;
        }

        private bool CanExecuteCopy()
        {
            return CanRecordCopy()
                && output?.innerHandle.IsValid() == true;
        }

        private void PrepareFrameContextOutput(ContextContainer frameData)
        {
            var ssrData = frameData.GetOrCreate<VividScreenSpaceReflectionData>();
            ssrData.Reset();

            if (!CanRecordCopy())
                return;

            ssrData.hasValidTexture = true;
            ssrData.reflectionTexture = output;
            ssrData.width = m_Width;
            ssrData.height = m_Height;
        }

        private void DispatchCopy(ComputeCommandBuffer cmd)
        {
            if (!CanExecuteCopy())
                return;

            cmd.SetComputeTextureParam(m_ComputeShader, m_CopyKernel, OutputColorTextureId, output.innerHandle);
            cmd.DispatchCompute(
                m_ComputeShader,
                m_CopyKernel,
                CoreUtils.DivRoundUp(m_Width, ThreadGroupSize),
                CoreUtils.DivRoundUp(m_Height, ThreadGroupSize),
                1);
        }

        private void DispatchClassifyTiles(ComputeCommandBuffer cmd)
        {
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRClassifyTilesKernel, OutputColorTextureId, output.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRClassifyTilesKernel, SSRTraceTextureId, m_TraceTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRClassifyTilesKernel, SSRResolveTextureId, m_ResolveTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRClassifyTilesKernel, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRClassifyTilesKernel, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, m_SSRClassifyTilesKernel, SSRTileListId, m_TileListBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_SSRClassifyTilesKernel,
                SSRDispatchIndirectArgsId,
                m_DispatchIndirectArgsBuffer.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_SSRClassifyTilesKernel, m_TileCountX, m_TileCountY, 1);
        }

        private void DispatchTrace(ComputeCommandBuffer cmd)
        {
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRTracingKernel, SSRTraceTextureId, m_TraceTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRTracingKernel, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRTracingKernel, HZBTextureId, m_HZBTexture.innerHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, m_SSRTracingKernel, DepthPyramidMipLevelOffsetsId, m_HZBMipLevelOffsets.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRTracingKernel, GBuffer0Id, m_GBuffer0.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRTracingKernel, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRTracingKernel, GBuffer2Id, m_GBuffer2.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRTracingKernel, SkyTextureId, m_SkyTexture.innerHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, m_SSRTracingKernel, SSRTileListId, m_TileListBuffer.innerHandle);
            if (m_UseHistoryColorPyramid && m_PreviousColorPyramidTexture?.innerHandle.IsValid() == true)
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRTracingKernel, PreviousColorPyramidTextureId, m_PreviousColorPyramidTexture.innerHandle);

            cmd.DispatchCompute(m_ComputeShader, m_SSRTracingKernel, m_DispatchIndirectArgsBuffer.innerHandle, 0);
        }

        private void DispatchResolve(ComputeCommandBuffer cmd)
        {
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRResolveKernel, SSRResolveTextureId, m_ResolveTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRResolveKernel, SSRTraceTextureId, m_TraceTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRResolveKernel, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRResolveKernel, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, m_SSRResolveKernel, SSRTileListId, m_TileListBuffer.innerHandle);

            cmd.DispatchCompute(m_ComputeShader, m_SSRResolveKernel, m_DispatchIndirectArgsBuffer.innerHandle, 0);
        }

        private void DispatchAccumulate(ComputeCommandBuffer cmd)
        {
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRAccumulateKernel, OutputColorTextureId, output.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRAccumulateKernel, SSRResolveTextureId, m_ResolveTexture.innerHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, m_SSRAccumulateKernel, SSRTileListId, m_TileListBuffer.innerHandle);

            cmd.DispatchCompute(m_ComputeShader, m_SSRAccumulateKernel, m_DispatchIndirectArgsBuffer.innerHandle, 0);
        }

        private void BindSkyParameters(ComputeCommandBuffer cmd)
        {
            if (cmd == null || m_ComputeShader == null)
                return;

            cmd.SetComputeVectorParam(m_ComputeShader, SkyTextureTintId, m_SkyTextureTint);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyTextureParamsId, m_SkyTextureParams);
        }

        private void UpdateOutputDescriptor(int width, int height)
        {
            if (output == null)
                output = CreateColorTexture("ScreenSpaceReflectionOutput", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);

            output.desc.Width = Mathf.Max(1, width);
            output.desc.Height = Mathf.Max(1, height);
            output.desc.Name = "ScreenSpaceReflectionOutput";
            output.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            output.desc.DepthBufferBits = DepthBits.None;
            output.desc.MsaaSamples = MSAASamples.None;
            output.desc.ClearBuffer = false;
            output.desc.FilterMode = FilterMode.Bilinear;
            output.desc.WrapMode = TextureWrapMode.Clamp;
            output.desc.UseMipMap = false;
            output.desc.AutoGenerateMips = false;
            output.desc.MipCount = 1;
            output.desc.EnableRandomWrite = true;
            output.desc.BindTextureMS = false;
        }

        private void UpdateTileResourcesDescriptor(int width, int height)
        {
            m_TileCountX = CoreUtils.DivRoundUp(Mathf.Max(1, width), ThreadGroupSize);
            m_TileCountY = CoreUtils.DivRoundUp(Mathf.Max(1, height), ThreadGroupSize);
            int maxTileCount = Mathf.Max(1, m_TileCountX * m_TileCountY);

            ConfigureInternalTextureDescriptor(m_TraceTexture, "ScreenSpaceReflectionTrace", width, height);
            ConfigureInternalTextureDescriptor(m_ResolveTexture, "ScreenSpaceReflectionResolve", width, height);
            ConfigureTileListBuffer(m_TileListBuffer, maxTileCount);
            ConfigureIndirectArgsBuffer(m_DispatchIndirectArgsBuffer);
            m_DispatchIndirectArgsBuffer.SetData(s_InitialDispatchIndirectArgsData);
        }

        private static ScreenSpaceReflectionConstantBufferData BuildConstantBuffer(
            Camera camera,
            int width,
            int height,
            ScreenSpaceReflectionSettingsData settings)
        {
            float nearPlane = camera != null ? Mathf.Max(camera.nearClipPlane, 0.0001f) : 0.1f;
            float farPlane = camera != null ? Mathf.Max(camera.farClipPlane, nearPlane + 0.0001f) : 1000.0f;
            float thickness = Mathf.Max(0.0001f, settings.depthBufferThickness);
            float thicknessScale = 1.0f / (1.0f + thickness);
            float thicknessBias = -nearPlane / Mathf.Max(farPlane - nearPlane, 0.0001f) * (thickness * thicknessScale);

            float roughnessFadeEnd = 1.0f - settings.minSmoothness;
            float roughnessFadeStart = 1.0f - settings.smoothnessFadeStart;
            float roughnessFadeLength = roughnessFadeEnd - roughnessFadeStart;
            float roughnessFadeRcpLength = Mathf.Abs(roughnessFadeLength) > 0.000001f
                ? 1.0f / roughnessFadeLength
                : 0.0f;

            return new ScreenSpaceReflectionConstantBufferData
            {
                SsrTraceScreenSize = new Vector4(
                    width,
                    height,
                    1.0f / Mathf.Max(1, width),
                    1.0f / Mathf.Max(1, height)),
                SsrThicknessScale = thicknessScale,
                SsrThicknessBias = thicknessBias,
                SsrIterLimit = settings.rayMaxIterations,
                SsrDepthPyramidMaxMip = Mathf.Clamp(CalculateMipCount(width, height) - 1, 0, MaxDepthPyramidMipCount - 1),
                SsrRoughnessFadeEnd = roughnessFadeEnd,
                SsrRoughnessFadeRcpLength = roughnessFadeRcpLength,
                SsrRoughnessFadeEndTimesRcpLength = roughnessFadeRcpLength > 0.0f
                    ? roughnessFadeEnd * roughnessFadeRcpLength
                    : 1.0f,
                SsrEdgeFadeRcpLength = 1.0f / Mathf.Max(settings.screenFadeDistance, 0.0001f),
                SsrIntensity = settings.intensity,
                SsrIntensityClamp = Mathf.Max(settings.clampValue, 0.001f),
                SsrReflectsSky = settings.reflectSky ? 1 : 0,
                Padding0 = 0.0f,
                SsrHistoryColorPyramidSize = Vector4.zero,
                SsrUseHistoryColorPyramid = 0,
                SsrHistoryColorPyramidMaxMip = 0,
                Padding1 = Vector2.zero
            };
        }

        private static int CalculateMipCount(int width, int height)
        {
            int maxDimension = Mathf.Max(1, Mathf.Max(width, height));
            return Mathf.FloorToInt(Mathf.Log(maxDimension, 2.0f)) + 1;
        }

        private static void ResizeInputTexture(RenderGraphTexture texture, int width, int height)
        {
            texture?.Resize(Mathf.Max(1, width), Mathf.Max(1, height));
        }

        private static void ConfigureHZBDescriptor(RenderGraphTexture texture)
        {
            if (texture?.desc == null)
                return;

            texture.desc.ColorFormat = GraphicsFormat.R32_SFloat;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.ClearBuffer = false;
        }

        private static void ConfigureHZBMipLevelOffsetBuffer(RenderGraphBuffer buffer)
        {
            if (buffer?.desc == null)
                return;

            buffer.desc.Count = MaxDepthPyramidMipCount;
            buffer.desc.Stride = sizeof(int) * 2;
            buffer.desc.Target = GraphicsBuffer.Target.Structured;
            buffer.desc.Name = "HZBMipLevelOffsets";
        }

        private static void ConfigureTileListBuffer(RenderGraphBuffer buffer, int maxTileCount)
        {
            if (buffer?.desc == null)
                return;

            buffer.desc.Count = Mathf.Max(1, maxTileCount);
            buffer.desc.Stride = sizeof(uint);
            buffer.desc.Target = GraphicsBuffer.Target.Structured;
            buffer.desc.Name = "SSRTileList";
        }

        private static void ConfigureIndirectArgsBuffer(RenderGraphBuffer buffer)
        {
            if (buffer?.desc == null)
                return;

            buffer.desc.Count = IndirectArgsElementCount;
            buffer.desc.Stride = sizeof(uint);
            buffer.desc.Target = GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments;
            buffer.desc.Name = "SSRDispatchIndirectArgs";
        }

        private static void ConfigureInternalTextureDescriptor(
            RenderGraphTexture texture,
            string name,
            int width,
            int height)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = Mathf.Max(1, width);
            texture.desc.Height = Mathf.Max(1, height);
            texture.desc.Name = name;
            texture.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.ClearBuffer = false;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.EnableRandomWrite = true;
            texture.desc.BindTextureMS = false;
        }

        private void PrepareSkyTextureState(VividSkyData skyData)
        {
            var hasActiveSky = skyData != null && skyData.activeSkyType != SkyType.None;
            var skyMaxMip = hasActiveSky ? SkyManager.GetSpecularCubemapMaxMip(skyData) : 0;

            SkyManager.ImportSpecularCubemap(m_SkyTexture, skyData);

            m_SkyTextureTint = hasActiveSky ? skyData.tint : Color.white;
            var skyIntensityMultiplier = hasActiveSky ? skyData.exposure : 1.0f;
            var skyRotation = hasActiveSky ? skyData.rotation : 0.0f;
            m_SkyTextureParams = DeferredLightingPass.BuildSkyTextureParams(
                skyMaxMip,
                skyIntensityMultiplier,
                skyRotation,
                hasActiveSky);
        }

        private static RenderGraphTexture CreateColorTexture(
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
            texture.desc.ClearBuffer = false;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.EnableRandomWrite = true;
            texture.desc.MsaaSamples = MSAASamples.None;
            return texture;
        }

        private static RenderGraphTexture CreateSkyCubemapTexture(string name)
        {
            return new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 1,
                    Height = 1,
                    Dimension = TextureDimension.Cube,
                    ColorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    DepthBufferBits = DepthBits.None,
                    FilterMode = FilterMode.Trilinear,
                    WrapMode = TextureWrapMode.Clamp,
                    UseMipMap = true,
                    AutoGenerateMips = false,
                    Name = name
                }
            };
        }
    }
}
