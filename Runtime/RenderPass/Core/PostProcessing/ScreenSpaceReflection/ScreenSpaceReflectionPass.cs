using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public enum ScreenSpaceReflectionExecutionPath
    {
        Vivid = 0,
        HDRP = 1,
        VividAndHDRPComparison = 2
    }

    public sealed class ScreenSpaceReflectionPass : ComputePass, IStablePassResourceLayout, IRenderGraphPreparePass
    {
        private const int ThreadGroupSize = 8;
        private const int IndirectArgsElementCount = 4;
        private const int MaxDepthPyramidMipCount = 15;
        private const string RenderSSRProfilerTag = "RenderSSR";
        private const string SSRClassifyTilesProfilerTag = "SSRClassifyTiles";
        private const string SSRTracingProfilerTag = "SSRTracing";
        private const string SSRResolveProfilerTag = "SSRResolve";
        private const string SSRAccumulateProfilerTag = "SSRAccumulate";
        private const string SSRHDRPTracingProfilerTag = "SsrTracing";
        private const string SSRHDRPReprojectionProfilerTag = "SsrReprojection";
        private const string SSRHDRPAccumulateProfilerTag = "SsrAccumulate";
        private const string AccumulationHistoryKey = "SSRAccumulation";
        private const string AccumulationFrameCountHistoryKey = "SSRAccumulationFrameCount";

        private static readonly uint[] s_InitialDispatchIndirectArgsData = { 0u, 1u, 1u, 0u };
        private static readonly ProfilingSampler s_SSRClassifyTilesProfilingSampler = new(SSRClassifyTilesProfilerTag);
        private static readonly ProfilingSampler s_SSRTracingProfilingSampler = new(SSRTracingProfilerTag);
        private static readonly ProfilingSampler s_SSRResolveProfilingSampler = new(SSRResolveProfilerTag);
        private static readonly ProfilingSampler s_SSRAccumulateProfilingSampler = new(SSRAccumulateProfilerTag);
        private static readonly ProfilingSampler s_SSRHDRPTracingProfilingSampler = new(SSRHDRPTracingProfilerTag);
        private static readonly ProfilingSampler s_SSRHDRPReprojectionProfilingSampler = new(SSRHDRPReprojectionProfilerTag);
        private static readonly ProfilingSampler s_SSRHDRPAccumulateProfilingSampler = new(SSRHDRPAccumulateProfilerTag);

        private static readonly int ConstantBufferId = Shader.PropertyToID("ShaderVariablesScreenSpaceReflection");
        private static readonly int OutputColorTextureId = Shader.PropertyToID("_OutputColorTexture");
        private static readonly int SSRTraceTextureId = Shader.PropertyToID("_SSRTraceTexture");
        private static readonly int SSRResolveTextureId = Shader.PropertyToID("_SSRResolveTexture");
        private static readonly int SSRRayInfoTextureId = Shader.PropertyToID("_SSRRayInfoTexture");
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
        private static readonly int SSRDebugTextureId = Shader.PropertyToID("_SSRDebugTexture");
        private static readonly int SSRHDRPHitPointTextureId = Shader.PropertyToID("_SSRHDRPHitPointTexture");
        private static readonly int SSRHDRPAccumTextureId = Shader.PropertyToID("_SSRHDRPAccumTexture");
        private static readonly int SSRHDRPOutputTextureId = Shader.PropertyToID("_SSRHDRPOutputTexture");
        private static readonly int SsrWriteHDRPToOutputId = Shader.PropertyToID("_SsrWriteHDRPToOutput");
        private static readonly int SsrAccumPrevId = Shader.PropertyToID("_SsrAccumPrev");
        private static readonly int SsrAccumTextureId = Shader.PropertyToID("_SsrAccumTexture");
        private static readonly int SSRPrevNumFramesAccumTextureId = Shader.PropertyToID("_SSRPrevNumFramesAccumTexture");
        private static readonly int SSRNumFramesAccumTextureId = Shader.PropertyToID("_SSRNumFramesAccumTexture");

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
            public int SsrFrameIndex;
            public Vector4 SsrHistoryColorPyramidSize;
            public int SsrUseHistoryColorPyramid;
            public int SsrHistoryColorPyramidMaxMip;
            public int SsrUseAccumulationHistory;
            public float SsrAccumulationAmount;
            public Vector4 SsrAccumulationHistorySize;
            public Vector4 SsrWorldSpaceCameraPos;
            public Matrix4x4 SsrViewProjMatrix;
            public Matrix4x4 SsrInvViewProjMatrix;
            public Matrix4x4 SsrPrevViewProjMatrix;
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

        [RenderGraphResource(
            Name = "ScreenSpaceReflectionTrace",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_TraceTexture;

        [RenderGraphResource(
            Name = "ScreenSpaceReflectionResolve",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_ResolveTexture;

        [RenderGraphResource(
            Name = "ScreenSpaceReflectionRayInfo",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_RayInfoTexture;

        [RenderGraphResource(
            Name = "SSRTileList",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_TileListBuffer;

        [RenderGraphResource(
            Name = "SSRDispatchIndirectArgs",
            Access = AccessFlags.ReadWrite)]
        private readonly RenderGraphBuffer m_DispatchIndirectArgsBuffer;
        private readonly RenderGraphTexture m_SkyTexture;

        [RenderGraphResource(
            Name = "ScreenSpaceReflectionDebug",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private readonly RenderGraphTexture m_DebugTexture;

        [RenderGraphResource(
            Name = "ScreenSpaceReflectionHDRPHitPoint",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_HDRPHitPointTexture;

        [RenderGraphResource(
            Name = "ScreenSpaceReflectionHDRPAccum",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_HDRPAccumTexture;

        [RenderGraphResource(
            Name = "ScreenSpaceReflectionHDRPOutput",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private readonly RenderGraphTexture m_HDRPOutputTexture;

        private readonly RenderGraphTexture m_DefaultPreviousColorPyramidTexture;

        [RenderGraphResource(
            Name = "ScreenSpaceReflectionAccumPrev",
            Access = AccessFlags.Read)]
        private readonly RenderGraphTexture m_AccumulationHistoryPrevious;

        [RenderGraphResource(
            Name = "ScreenSpaceReflectionAccumTexture",
            Access = AccessFlags.Write)]
        private readonly RenderGraphTexture m_AccumulationHistoryCurrent;

        [RenderGraphResource(
            Name = "ScreenSpaceReflectionPrevNumFramesAccum",
            Access = AccessFlags.Read)]
        private readonly RenderGraphTexture m_NumFramesHistoryPrevious;

        [RenderGraphResource(
            Name = "ScreenSpaceReflectionNumFramesAccum",
            Access = AccessFlags.Write)]
        private readonly RenderGraphTexture m_NumFramesHistoryCurrent;

        private int m_SSRClassifyTilesKernel = -1;
        private int m_SSRTracingKernel = -1;
        private int m_SSRResolveKernel = -1;
        private int m_SSRAccumulateKernel = -1;
        private int m_SSRHDRPTracingKernel = -1;
        private int m_SSRHDRPReprojectionKernel = -1;
        private int m_SSRHDRPAccumulateKernel = -1;
        private int m_CopyKernel = -1;
        private int m_Width = 1;
        private int m_Height = 1;
        private int m_TileCountX = 1;
        private int m_TileCountY = 1;
        private bool m_ShouldApply;
        private bool m_IsPassResourceLayoutDirty;
        private bool m_UseHistoryColorPyramid;
        private bool m_HasValidAccumulationHistory;
        private bool m_HistoryInvalidated = true;

        [SerializeField]
        private ScreenSpaceReflectionExecutionPath m_ExecutionPath = ScreenSpaceReflectionExecutionPath.Vivid;

        [RenderGraphResource(Name = "PreviousColorPyramid", Access = AccessFlags.Read)]
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
            m_RayInfoTexture = CreateColorTexture("ScreenSpaceReflectionRayInfo", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_TileListBuffer = RenderGraphBuffer.CreateStructured("SSRTileList", 1, sizeof(uint));
            m_DispatchIndirectArgsBuffer = RenderGraphBuffer.CreateStructured(
                "SSRDispatchIndirectArgs",
                IndirectArgsElementCount,
                sizeof(uint),
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments);
            m_SkyTexture = CreateSkyCubemapTexture("ScreenSpaceReflectionSkyTexture");
            m_DebugTexture = CreateColorTexture("ScreenSpaceReflectionDebug", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_HDRPHitPointTexture = CreateColorTexture("ScreenSpaceReflectionHDRPHitPoint", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_HDRPAccumTexture = CreateColorTexture("ScreenSpaceReflectionHDRPAccum", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_HDRPOutputTexture = CreateColorTexture("ScreenSpaceReflectionHDRPOutput", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_DefaultPreviousColorPyramidTexture = RenderGraphTexture.CreateInput(
                "PreviousColorPyramid",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_PreviousColorPyramidTexture = m_DefaultPreviousColorPyramidTexture;
            m_AccumulationHistoryPrevious = RenderGraphTexture.CreateInput(
                "ScreenSpaceReflectionAccumPrev",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_AccumulationHistoryCurrent = CreateColorTexture(
                "ScreenSpaceReflectionAccumTexture",
                1,
                1,
                GraphicsFormat.R16G16B16A16_SFloat);
            m_NumFramesHistoryPrevious = RenderGraphTexture.CreateInput(
                "ScreenSpaceReflectionPrevNumFramesAccum",
                GraphicsFormat.R16_SFloat);
            m_NumFramesHistoryCurrent = CreateColorTexture(
                "ScreenSpaceReflectionNumFramesAccum",
                1,
                1,
                GraphicsFormat.R16_SFloat);

            ConfigureHZBDescriptor(m_HZBTexture);
            ConfigureInternalTextureDescriptor(m_TraceTexture, "ScreenSpaceReflectionTrace", 1, 1);
            ConfigureInternalTextureDescriptor(m_ResolveTexture, "ScreenSpaceReflectionResolve", 1, 1);
            ConfigureInternalTextureDescriptor(m_RayInfoTexture, "ScreenSpaceReflectionRayInfo", 1, 1);
            ConfigureInternalTextureDescriptor(m_DebugTexture, "ScreenSpaceReflectionDebug", 1, 1);
            ConfigureInternalTextureDescriptor(m_HDRPHitPointTexture, "ScreenSpaceReflectionHDRPHitPoint", 1, 1);
            ConfigureInternalTextureDescriptor(m_HDRPAccumTexture, "ScreenSpaceReflectionHDRPAccum", 1, 1);
            ConfigureInternalTextureDescriptor(m_HDRPOutputTexture, "ScreenSpaceReflectionHDRPOutput", 1, 1);
            ConfigureInternalTextureDescriptor(m_AccumulationHistoryPrevious, "ScreenSpaceReflectionAccumPrev", 1, 1);
            ConfigureInternalTextureDescriptor(m_AccumulationHistoryCurrent, "ScreenSpaceReflectionAccumTexture", 1, 1);
            ConfigureSingleChannelHistoryDescriptor(m_NumFramesHistoryPrevious, "ScreenSpaceReflectionPrevNumFramesAccum", 1, 1);
            ConfigureSingleChannelHistoryDescriptor(m_NumFramesHistoryCurrent, "ScreenSpaceReflectionNumFramesAccum", 1, 1);
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
                m_SSRHDRPTracingKernel = m_ComputeShader.FindKernel("ScreenSpaceReflectionsHDRPTracing");
                m_SSRHDRPReprojectionKernel = m_ComputeShader.FindKernel("ScreenSpaceReflectionsHDRPReprojection");
                m_SSRHDRPAccumulateKernel = m_ComputeShader.FindKernel("ScreenSpaceReflectionsHDRPAccumulate");
                m_CopyKernel = m_ComputeShader.FindKernel("CopyScreenSpaceReflection");
            }
            catch (ArgumentException)
            {
                m_SSRClassifyTilesKernel = -1;
                m_SSRTracingKernel = -1;
                m_SSRResolveKernel = -1;
                m_SSRAccumulateKernel = -1;
                m_SSRHDRPTracingKernel = -1;
                m_SSRHDRPReprojectionKernel = -1;
                m_SSRHDRPAccumulateKernel = -1;
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

            m_ConstantBuffer = BuildConstantBuffer(
                cameraData,
                frameData.Get<VividCameraShaderData>(),
                m_Width,
                m_Height,
                m_Settings);
            PrepareAccumulationHistory(frameData);
            PrepareFrameContextOutput(frameData);
        }

        public void PrepareRenderGraph(ContextContainer frameData)
        {
            ResolveColorPyramidHistory(frameData);
        }

        public override void Record(ComputePassContext context)
        {
            if (!ShouldRecordEffect() && !CanRecordCopy())
                return;

            var cmd = context.cmd;
            using (new ProfilingScope(cmd, profilingSampler))
            {
                BindSkyParameters(cmd);
                BindDebugTexture(cmd, m_SSRClassifyTilesKernel);
                BindDebugTexture(cmd, m_SSRTracingKernel);
                BindDebugTexture(cmd, m_SSRResolveKernel);
                BindDebugTexture(cmd, m_SSRAccumulateKernel);
                BindDebugTexture(cmd, m_SSRHDRPTracingKernel);
                BindDebugTexture(cmd, m_SSRHDRPReprojectionKernel);
                BindDebugTexture(cmd, m_SSRHDRPAccumulateKernel);

                if (!ShouldRecordEffect())
                {
                    DispatchCopy(cmd);
                    return;
                }

                ConstantBuffer.Push(cmd, m_ConstantBuffer, m_ComputeShader, ConstantBufferId);

                bool executedPath = false;
                if (ShouldRunVividPath() && CanExecuteVividPath())
                {
                    ResetDispatchIndirectArgs(cmd);

                    using (new ProfilingScope(cmd, s_SSRClassifyTilesProfilingSampler))
                        DispatchClassifyTiles(cmd);

                    using (new ProfilingScope(cmd, s_SSRTracingProfilingSampler))
                        DispatchTrace(cmd, context);

                    using (new ProfilingScope(cmd, s_SSRResolveProfilingSampler))
                        DispatchResolve(cmd);

                    using (new ProfilingScope(cmd, s_SSRAccumulateProfilingSampler))
                        DispatchAccumulate(cmd);

                    executedPath = true;
                }

                if (ShouldRunHDRPPath() && CanExecuteHDRPComparison())
                {
                    DispatchHDRPComparison(cmd, context);
                    executedPath = true;
                }

                if (!executedPath)
                    DispatchCopy(cmd);
            }
        }

        public override void Dispose()
        {
            m_ComputeShader = null;
            m_SSRClassifyTilesKernel = -1;
            m_SSRTracingKernel = -1;
            m_SSRResolveKernel = -1;
            m_SSRAccumulateKernel = -1;
            m_SSRHDRPTracingKernel = -1;
            m_SSRHDRPReprojectionKernel = -1;
            m_SSRHDRPAccumulateKernel = -1;
            m_CopyKernel = -1;
            m_ShouldApply = false;
            m_IsPassResourceLayoutDirty = false;
            m_UseHistoryColorPyramid = false;
            m_HasValidAccumulationHistory = false;
            m_HistoryInvalidated = true;
            m_PreviousColorPyramidTexture = m_DefaultPreviousColorPyramidTexture;
            m_SkyTextureTint = Color.white;
            m_SkyTextureParams = Vector4.zero;
            m_Settings = ScreenSpaceReflectionSettingsData.CreateDefault();
            m_ConstantBuffer = default;
        }

        private bool ResolveColorPyramidHistory(ContextContainer frameData)
        {
            m_UseHistoryColorPyramid = false;
            SetPreviousColorPyramidTexture(m_DefaultPreviousColorPyramidTexture);
            m_ConstantBuffer.SsrUseHistoryColorPyramid = 0;
            m_ConstantBuffer.SsrHistoryColorPyramidMaxMip = 0;
            m_ConstantBuffer.SsrHistoryColorPyramidSize = Vector4.zero;

            if (frameData == null || !frameData.Contains<VividColorPyramidData>())
                return false;

            var colorPyramidData = frameData.Get<VividColorPyramidData>();
            if (colorPyramidData == null
                || colorPyramidData.previousColorPyramid == null
                || colorPyramidData.width <= 0
                || colorPyramidData.height <= 0)
            {
                return false;
            }

            SetPreviousColorPyramidTexture(colorPyramidData.previousColorPyramid);
            m_ConstantBuffer.SsrHistoryColorPyramidMaxMip = Mathf.Max(0, colorPyramidData.mipCount - 1);
            m_ConstantBuffer.SsrHistoryColorPyramidSize = new Vector4(
                colorPyramidData.width,
                colorPyramidData.height,
                1.0f / Mathf.Max(1, colorPyramidData.width),
                1.0f / Mathf.Max(1, colorPyramidData.height));

            if (!colorPyramidData.hasValidHistory)
                return false;

            m_UseHistoryColorPyramid = true;
            m_ConstantBuffer.SsrUseHistoryColorPyramid = 1;
            return true;
        }

        private void PrepareAccumulationHistory(ContextContainer frameData)
        {
            m_HasValidAccumulationHistory = false;
            m_ConstantBuffer.SsrUseAccumulationHistory = 0;
            m_ConstantBuffer.SsrAccumulationAmount = 1.0f;
            m_ConstantBuffer.SsrAccumulationHistorySize = new Vector4(
                m_Width,
                m_Height,
                1.0f / Mathf.Max(1, m_Width),
                1.0f / Mathf.Max(1, m_Height));

            ConfigureInternalTextureDescriptor(
                m_AccumulationHistoryPrevious,
                "ScreenSpaceReflectionAccumPrev",
                m_Width,
                m_Height);
            ConfigureInternalTextureDescriptor(
                m_AccumulationHistoryCurrent,
                "ScreenSpaceReflectionAccumTexture",
                m_Width,
                m_Height);
            ConfigureSingleChannelHistoryDescriptor(
                m_NumFramesHistoryPrevious,
                "ScreenSpaceReflectionPrevNumFramesAccum",
                m_Width,
                m_Height);
            ConfigureSingleChannelHistoryDescriptor(
                m_NumFramesHistoryCurrent,
                "ScreenSpaceReflectionNumFramesAccum",
                m_Width,
                m_Height);

            if (!m_ShouldApply || !ShouldRunVividPath())
            {
                m_HistoryInvalidated = true;
                return;
            }

            bool hasAccumulationHistory = AllocHistoryTexture(
                AccumulationHistoryKey,
                m_AccumulationHistoryPrevious,
                m_AccumulationHistoryCurrent,
                m_AccumulationHistoryCurrent.desc);
            bool hasFrameCountHistory = AllocHistoryTexture(
                AccumulationFrameCountHistoryKey,
                m_NumFramesHistoryPrevious,
                m_NumFramesHistoryCurrent,
                m_NumFramesHistoryCurrent.desc);

            var temporalData = frameData.Get<VividTemporalData>();
            bool isFirstFrame = temporalData != null && temporalData.isFirstFrame;
            m_HasValidAccumulationHistory = hasAccumulationHistory
                && hasFrameCountHistory
                && !isFirstFrame
                && !m_HistoryInvalidated;
            m_ConstantBuffer.SsrUseAccumulationHistory = m_HasValidAccumulationHistory ? 1 : 0;
            m_HistoryInvalidated = false;
        }

        private void SetPreviousColorPyramidTexture(RenderGraphTexture texture)
        {
            var resolvedTexture = texture ?? m_DefaultPreviousColorPyramidTexture;
            if (ReferenceEquals(m_PreviousColorPyramidTexture, resolvedTexture))
                return;

            m_PreviousColorPyramidTexture = resolvedTexture;
            m_IsPassResourceLayoutDirty = true;
        }

        private bool ShouldRecordEffect()
        {
            return m_ShouldApply
                && m_ComputeShader != null
                && m_Width > 0
                && m_Height > 0
                && (!ShouldRunVividPath() || m_SSRTracingKernel >= 0)
                && (!ShouldRunHDRPPath() || m_SSRHDRPTracingKernel >= 0);
        }

        private bool CanRecordCopy()
        {
            return m_ComputeShader != null
                && m_CopyKernel >= 0
                && m_Width > 0
                && m_Height > 0;
        }

        private bool CanSampleHistoryColorPyramid()
        {
            return m_UseHistoryColorPyramid
                && m_PreviousColorPyramidTexture?.innerHandle.IsValid() == true;
        }

        private bool CanSampleSkyFallback()
        {
            return m_Settings.reflectSky
                && m_SkyTextureParams.w > 0.5f
                && m_SkyTexture?.innerHandle.IsValid() == true;
        }

        private bool CanExecuteVividPath()
        {
            return (CanSampleHistoryColorPyramid() || CanSampleSkyFallback())
                && m_SSRClassifyTilesKernel >= 0
                && m_SSRTracingKernel >= 0
                && m_SSRResolveKernel >= 0
                && m_SSRAccumulateKernel >= 0
                && output?.innerHandle.IsValid() == true
                && m_TraceTexture?.innerHandle.IsValid() == true
                && m_ResolveTexture?.innerHandle.IsValid() == true
                && m_RayInfoTexture?.innerHandle.IsValid() == true
                && m_AccumulationHistoryPrevious?.innerHandle.IsValid() == true
                && m_AccumulationHistoryCurrent?.innerHandle.IsValid() == true
                && m_NumFramesHistoryPrevious?.innerHandle.IsValid() == true
                && m_NumFramesHistoryCurrent?.innerHandle.IsValid() == true
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
                && output?.innerHandle.IsValid() == true
                && m_HDRPOutputTexture?.innerHandle.IsValid() == true;
        }

        private bool CanExecuteHDRPComparison()
        {
            return CanSampleHistoryColorPyramid()
                && m_ComputeShader != null
                && m_SSRHDRPTracingKernel >= 0
                && m_SSRHDRPReprojectionKernel >= 0
                && m_SSRHDRPAccumulateKernel >= 0
                && m_HDRPHitPointTexture?.innerHandle.IsValid() == true
                && m_HDRPAccumTexture?.innerHandle.IsValid() == true
                && m_HDRPOutputTexture?.innerHandle.IsValid() == true
                && output?.innerHandle.IsValid() == true
                && m_DepthTexture?.innerHandle.IsValid() == true
                && m_HZBTexture?.innerHandle.IsValid() == true
                && m_HZBMipLevelOffsets?.innerHandle.IsValid() == true
                && m_GBuffer1?.innerHandle.IsValid() == true;
        }

        private bool ShouldRunVividPath()
        {
            return m_ExecutionPath == ScreenSpaceReflectionExecutionPath.Vivid
                || m_ExecutionPath == ScreenSpaceReflectionExecutionPath.VividAndHDRPComparison;
        }

        private bool ShouldRunHDRPPath()
        {
            return m_ExecutionPath == ScreenSpaceReflectionExecutionPath.HDRP
                || m_ExecutionPath == ScreenSpaceReflectionExecutionPath.VividAndHDRPComparison;
        }

        private bool ShouldUseHDRPAsMainOutput()
        {
            return m_ExecutionPath == ScreenSpaceReflectionExecutionPath.HDRP;
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
            cmd.SetComputeTextureParam(m_ComputeShader, m_CopyKernel, SSRHDRPOutputTextureId, m_HDRPOutputTexture.innerHandle);
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
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRClassifyTilesKernel, SSRRayInfoTextureId, m_RayInfoTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRClassifyTilesKernel, SsrAccumTextureId, m_AccumulationHistoryCurrent.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRClassifyTilesKernel, SSRNumFramesAccumTextureId, m_NumFramesHistoryCurrent.innerHandle);
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

        private void ResetDispatchIndirectArgs(ComputeCommandBuffer cmd)
        {
            var dispatchIndirectArgsBuffer = m_DispatchIndirectArgsBuffer?.ImportedGraphicsBuffer;
            if (dispatchIndirectArgsBuffer == null)
                return;

            cmd.SetBufferData(dispatchIndirectArgsBuffer, s_InitialDispatchIndirectArgsData);
        }

        private void DispatchTrace(ComputeCommandBuffer cmd, ComputePassContext computePassContext)
        {
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRTracingKernel, SSRTraceTextureId, m_TraceTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRTracingKernel, SSRRayInfoTextureId, m_RayInfoTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRTracingKernel, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRTracingKernel, HZBTextureId, m_HZBTexture.innerHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, m_SSRTracingKernel, DepthPyramidMipLevelOffsetsId, m_HZBMipLevelOffsets.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRTracingKernel, GBuffer0Id, m_GBuffer0.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRTracingKernel, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRTracingKernel, GBuffer2Id, m_GBuffer2.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRTracingKernel, SkyTextureId, m_SkyTexture.innerHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, m_SSRTracingKernel, SSRTileListId, m_TileListBuffer.innerHandle);
            if (!ReferenceEquals(m_PreviousColorPyramidTexture, m_DefaultPreviousColorPyramidTexture)
                && m_PreviousColorPyramidTexture?.innerHandle.IsValid() == true)
            {
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRTracingKernel, PreviousColorPyramidTextureId, m_PreviousColorPyramidTexture.innerHandle);
            }
            else
            {
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRTracingKernel, PreviousColorPyramidTextureId, computePassContext.renderGraphContext.defaultResources.blackTexture);
            }

            cmd.DispatchCompute(m_ComputeShader, m_SSRTracingKernel, m_DispatchIndirectArgsBuffer.innerHandle, 0);
        }

        private void DispatchResolve(ComputeCommandBuffer cmd)
        {
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRResolveKernel, SSRResolveTextureId, m_ResolveTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRResolveKernel, SSRTraceTextureId, m_TraceTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRResolveKernel, SSRRayInfoTextureId, m_RayInfoTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRResolveKernel, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRResolveKernel, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, m_SSRResolveKernel, SSRTileListId, m_TileListBuffer.innerHandle);

            cmd.DispatchCompute(m_ComputeShader, m_SSRResolveKernel, m_DispatchIndirectArgsBuffer.innerHandle, 0);
        }

        private void DispatchAccumulate(ComputeCommandBuffer cmd)
        {
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRAccumulateKernel, OutputColorTextureId, output.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRAccumulateKernel, SSRResolveTextureId, m_ResolveTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRAccumulateKernel, SSRRayInfoTextureId, m_RayInfoTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRAccumulateKernel, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRAccumulateKernel, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRAccumulateKernel, SsrAccumPrevId, m_AccumulationHistoryPrevious.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRAccumulateKernel, SsrAccumTextureId, m_AccumulationHistoryCurrent.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_SSRAccumulateKernel,
                SSRPrevNumFramesAccumTextureId,
                m_NumFramesHistoryPrevious.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_SSRAccumulateKernel,
                SSRNumFramesAccumTextureId,
                m_NumFramesHistoryCurrent.innerHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, m_SSRAccumulateKernel, SSRTileListId, m_TileListBuffer.innerHandle);

            cmd.DispatchCompute(m_ComputeShader, m_SSRAccumulateKernel, m_DispatchIndirectArgsBuffer.innerHandle, 0);
        }

        private void DispatchHDRPComparison(ComputeCommandBuffer cmd, ComputePassContext context)
        {
            if (!CanExecuteHDRPComparison())
                return;

            using (new ProfilingScope(cmd, s_SSRHDRPTracingProfilingSampler))
            {
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPTracingKernel, SSRHDRPHitPointTextureId, m_HDRPHitPointTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPTracingKernel, SSRHDRPAccumTextureId, m_HDRPAccumTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPTracingKernel, SSRHDRPOutputTextureId, m_HDRPOutputTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPTracingKernel, DepthTextureId, m_DepthTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPTracingKernel, HZBTextureId, m_HZBTexture.innerHandle);
                cmd.SetComputeBufferParam(m_ComputeShader, m_SSRHDRPTracingKernel, DepthPyramidMipLevelOffsetsId, m_HZBMipLevelOffsets.innerHandle);
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPTracingKernel, GBuffer1Id, m_GBuffer1.innerHandle);
                DispatchFullScreen(cmd, m_SSRHDRPTracingKernel);
            }

            using (new ProfilingScope(cmd, s_SSRHDRPReprojectionProfilingSampler))
            {
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPReprojectionKernel, SSRHDRPHitPointTextureId, m_HDRPHitPointTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPReprojectionKernel, SSRHDRPAccumTextureId, m_HDRPAccumTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPReprojectionKernel, GBuffer1Id, m_GBuffer1.innerHandle);
                if (!ReferenceEquals(m_PreviousColorPyramidTexture, m_DefaultPreviousColorPyramidTexture)
                    && m_PreviousColorPyramidTexture?.innerHandle.IsValid() == true)
                {
                    cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPReprojectionKernel, PreviousColorPyramidTextureId, m_PreviousColorPyramidTexture.innerHandle);
                }
                else
                {
                    cmd.SetComputeTextureParam(
                        m_ComputeShader,
                        m_SSRHDRPReprojectionKernel,
                        PreviousColorPyramidTextureId,
                        context.renderGraphContext.defaultResources.blackTexture);
                }

                DispatchFullScreen(cmd, m_SSRHDRPReprojectionKernel);
            }

            using (new ProfilingScope(cmd, s_SSRHDRPAccumulateProfilingSampler))
            {
                cmd.SetComputeIntParam(m_ComputeShader, SsrWriteHDRPToOutputId, ShouldUseHDRPAsMainOutput() ? 1 : 0);
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPAccumulateKernel, OutputColorTextureId, output.innerHandle);
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPAccumulateKernel, SSRHDRPOutputTextureId, m_HDRPOutputTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPAccumulateKernel, SSRHDRPAccumTextureId, m_HDRPAccumTexture.innerHandle);
                DispatchFullScreen(cmd, m_SSRHDRPAccumulateKernel);
            }
        }

        private void DispatchFullScreen(ComputeCommandBuffer cmd, int kernel)
        {
            cmd.DispatchCompute(
                m_ComputeShader,
                kernel,
                CoreUtils.DivRoundUp(m_Width, ThreadGroupSize),
                CoreUtils.DivRoundUp(m_Height, ThreadGroupSize),
                1);
        }

        private void BindDebugTexture(ComputeCommandBuffer cmd, int kernel)
        {
            if (kernel < 0 || m_DebugTexture?.innerHandle.IsValid() != true)
                return;

            cmd.SetComputeTextureParam(m_ComputeShader, kernel, SSRDebugTextureId, m_DebugTexture.innerHandle);
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
            ConfigureInternalTextureDescriptor(m_RayInfoTexture, "ScreenSpaceReflectionRayInfo", width, height);
            ConfigureInternalTextureDescriptor(m_DebugTexture, "ScreenSpaceReflectionDebug", width, height);
            ConfigureInternalTextureDescriptor(m_HDRPHitPointTexture, "ScreenSpaceReflectionHDRPHitPoint", width, height);
            ConfigureInternalTextureDescriptor(m_HDRPAccumTexture, "ScreenSpaceReflectionHDRPAccum", width, height);
            ConfigureInternalTextureDescriptor(m_HDRPOutputTexture, "ScreenSpaceReflectionHDRPOutput", width, height);
            ConfigureInternalTextureDescriptor(m_AccumulationHistoryPrevious, "ScreenSpaceReflectionAccumPrev", width, height);
            ConfigureInternalTextureDescriptor(m_AccumulationHistoryCurrent, "ScreenSpaceReflectionAccumTexture", width, height);
            ConfigureSingleChannelHistoryDescriptor(m_NumFramesHistoryPrevious, "ScreenSpaceReflectionPrevNumFramesAccum", width, height);
            ConfigureSingleChannelHistoryDescriptor(m_NumFramesHistoryCurrent, "ScreenSpaceReflectionNumFramesAccum", width, height);
            ConfigureTileListBuffer(m_TileListBuffer, maxTileCount);
            ConfigureIndirectArgsBuffer(m_DispatchIndirectArgsBuffer);
            m_DispatchIndirectArgsBuffer.SetData(s_InitialDispatchIndirectArgsData);
        }

        private static ScreenSpaceReflectionConstantBufferData BuildConstantBuffer(
            VividCameraData cameraData,
            VividCameraShaderData cameraShaderData,
            int width,
            int height,
            ScreenSpaceReflectionSettingsData settings)
        {
            var camera = cameraData?.camera;
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

            var viewProjMatrix = ResolveSsrViewProjMatrix(cameraData);
            var invViewProjMatrix = viewProjMatrix.inverse;
            var prevViewProjMatrix = ResolveSsrPrevViewProjMatrix(cameraShaderData, viewProjMatrix);

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
                SsrFrameIndex = Time.frameCount,
                SsrHistoryColorPyramidSize = Vector4.zero,
                SsrUseHistoryColorPyramid = 0,
                SsrHistoryColorPyramidMaxMip = 0,
                SsrUseAccumulationHistory = 0,
                SsrAccumulationAmount = 1.0f,
                SsrAccumulationHistorySize = new Vector4(
                    width,
                    height,
                    1.0f / Mathf.Max(1, width),
                    1.0f / Mathf.Max(1, height)),
                SsrWorldSpaceCameraPos = ResolveSsrWorldSpaceCameraPos(cameraData),
                SsrViewProjMatrix = viewProjMatrix,
                SsrInvViewProjMatrix = invViewProjMatrix,
                SsrPrevViewProjMatrix = prevViewProjMatrix
            };
        }

        private static Matrix4x4 ResolveSsrViewProjMatrix(VividCameraData cameraData)
        {
            if (cameraData == null)
                return Matrix4x4.identity;

            return cameraData.GetGPUViewProjectionMatrix(renderIntoTexture: true);
        }

        private static Matrix4x4 ResolveSsrPrevViewProjMatrix(
            VividCameraShaderData cameraShaderData,
            Matrix4x4 fallbackViewProjMatrix)
        {
            return cameraShaderData != null && cameraShaderData.hasShaderVariablesGlobal
                ? cameraShaderData.shaderVariablesGlobal._VividPrevViewProjMatrix
                : fallbackViewProjMatrix;
        }

        private static Vector4 ResolveSsrWorldSpaceCameraPos(VividCameraData cameraData)
        {
            if (cameraData == null)
                return new Vector4(0.0f, 0.0f, 0.0f, 1.0f);

            var cameraPosition = cameraData.GetInverseViewMatrix().GetColumn(3);
            cameraPosition.w = 1.0f;
            return cameraPosition;
        }

        private static int CalculateMipCount(int width, int height)
        {
            int maxDimension = Mathf.Max(1, Mathf.Max(width, height));
            return Mathf.CeilToInt(Mathf.Log(maxDimension, 2.0f)) + 1;
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

        private static void ConfigureSingleChannelHistoryDescriptor(
            RenderGraphTexture texture,
            string name,
            int width,
            int height)
        {
            ConfigureInternalTextureDescriptor(texture, name, width, height);
            if (texture?.desc == null)
                return;

            texture.desc.ColorFormat = GraphicsFormat.R16_SFloat;
            texture.desc.FilterMode = FilterMode.Bilinear;
        }

        private void PrepareSkyTextureState(VividSkyData skyData)
        {
            m_SkyTexture.ClearImportedHandle();
            var hasActiveSky = skyData != null && skyData.activeSkyType != SkyType.None;
            var skyMaxMip = hasActiveSky ? SkyManager.GetSpecularCubemapMaxMip(skyData) : 0;

            if (PassRecorder.IsPassTextureImportActive)
            {
                SkyManager.ImportSpecularCubemap(m_SkyTexture, skyData);
                var skyCubemap = SkyManager.GetSpecularCubemapHandle();
                if (skyCubemap != null)
                    m_SkyTexture.SetImportedHandle(Import(skyCubemap));
            }

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
