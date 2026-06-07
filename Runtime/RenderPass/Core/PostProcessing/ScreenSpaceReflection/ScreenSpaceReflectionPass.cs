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
        VividAndHDRPComparison = 2,
        Hybrid = 3,
        RayTracing = 4
    }

    public sealed class ScreenSpaceReflectionPass : ComputePass, IStablePassResourceLayout, IRenderGraphPreparePass, IBlueNoiseConsumerPass
    {
        private const int ThreadGroupSize = 8;
        private const int IndirectArgsElementCount = 4;
        private const int RayDispatchArgsElementCount = 3;
        private const int MaxDepthPyramidMipCount = 15;
        private const string RenderSSRProfilerTag = "RenderSSR";
        private const string SSRClassifyTilesProfilerTag = "SSRClassifyTiles";
        private const string SSRTracingProfilerTag = "SSRTracing";
        private const string SSRHybridTraceProfilerTag = "SSRHybridTrace";
        private const string SSRRayTracingProfilerTag = "RaytracingReflectionEvaluation";
        private const string SSRRayTracingDenoiseProfilerTag = "RaytracingReflectionFilter";
        private const string ReBlurPreBlurProfilerTag = "ReBlurPreBlur";
        private const string ReBlurTemporalAccumulationProfilerTag = "ReBlurTemporalAccumulation";
        private const string ReBlurMipGenerationProfilerTag = "ReBlurMipGeneration";
        private const string ReBlurMipHistoryFixProfilerTag = "ReBlurMipHistoryFix";
        private const string ReBlurBlurProfilerTag = "ReBlurBlur";
        private const string ReBlurCopyHistoryProfilerTag = "ReBlurCopyHistory";
        private const string ReBlurTemporalStabilizationProfilerTag = "ReBlurTemporalStabilization";
        private const string ReBlurCopyHistoryStabProfilerTag = "ReBlurCopyHistoryStab";
        private const string ReBlurPostBlurProfilerTag = "ReBlurPostBlur";
        private const string SSRResolveProfilerTag = "SSRResolve";
        private const string SSRAccumulateProfilerTag = "SSRAccumulate";
        private const string SSRHDRPTracingProfilerTag = "SsrTracing";
        private const string SSRHDRPReprojectionProfilerTag = "SsrReprojection";
        private const string SSRHDRPAccumulateProfilerTag = "SsrAccumulate";
        private const string HybridRayGenName = "RayGenScreenSpaceReflectionsHybridTrace";
        private const string RayTracingRayGenName = "RayGenIntegration";
        private const string AccumulationHistoryKey = "SSRAccumulation";
        private const string HDRPAccumulationHistoryKey = "SSRHDRPAccumulation";
        private const string AccumulationFrameCountHistoryKey = "SSRAccumulationFrameCount";
        private const float HDRPDefaultAccumulationFactor = 0.75f;
        private const float HDRPDefaultSpeedRejection = 0.5f;
        private const string ReBlurLightingDistanceHistoryKey = "SSRReBlurLightingDistance";
        private const string ReBlurAccumulationHistoryKey = "SSRReBlurAccumulation";
        private const string ReBlurStabilizationHistoryKey = "SSRReBlurStabilization";

        private static readonly uint[] s_InitialDispatchIndirectArgsData = { 0u, 1u, 1u, 0u };
        private static readonly uint[] s_InitialRayDispatchIndirectArgsData = { 0u, 1u, 1u };
        private static readonly float[] s_ReBlurPreBlurRands =
        {
            0.840188f, 0.394383f, 0.783099f, 0.79844f, 0.911647f, 0.197551f, 0.335223f, 0.76823f,
            0.277775f, 0.55397f, 0.477397f, 0.628871f, 0.364784f, 0.513401f, 0.95223f, 0.916195f,
            0.635712f, 0.717297f, 0.141603f, 0.606969f, 0.0163006f, 0.242887f, 0.137232f, 0.804177f,
            0.156679f, 0.400944f, 0.12979f, 0.108809f, 0.998924f, 0.218257f, 0.512932f, 0.839112f
        };
        private static readonly float[] s_ReBlurBlurRands =
        {
            0.61264f, 0.296032f, 0.637552f, 0.524287f, 0.493583f, 0.972775f, 0.292517f, 0.771358f,
            0.526745f, 0.769914f, 0.400229f, 0.891529f, 0.283315f, 0.352458f, 0.807725f, 0.919026f,
            0.0697553f, 0.949327f, 0.525995f, 0.0860558f, 0.192214f, 0.663227f, 0.890233f, 0.348893f,
            0.0641713f, 0.020023f, 0.457702f, 0.0630958f, 0.23828f, 0.970634f, 0.902208f, 0.85092f
        };
        private static readonly float[] s_ReBlurPostBlurRands =
        {
            0.266666f, 0.53976f, 0.375207f, 0.760249f, 0.512535f, 0.667724f, 0.531606f, 0.0392803f,
            0.437638f, 0.931835f, 0.93081f, 0.720952f, 0.284293f, 0.738534f, 0.639979f, 0.354049f,
            0.687861f, 0.165974f, 0.440105f, 0.880075f, 0.829201f, 0.330337f, 0.228968f, 0.893372f,
            0.35036f, 0.68667f, 0.956468f, 0.58864f, 0.657304f, 0.858676f, 0.43956f, 0.92397f
        };
        private static readonly ProfilingSampler s_SSRClassifyTilesProfilingSampler = new(SSRClassifyTilesProfilerTag);
        private static readonly ProfilingSampler s_SSRTracingProfilingSampler = new(SSRTracingProfilerTag);
        private static readonly ProfilingSampler s_SSRHybridTraceProfilingSampler = new(SSRHybridTraceProfilerTag);
        private static readonly ProfilingSampler s_SSRRayTracingProfilingSampler = new(SSRRayTracingProfilerTag);
        private static readonly ProfilingSampler s_SSRRayTracingDenoiseProfilingSampler = new(SSRRayTracingDenoiseProfilerTag);
        private static readonly ProfilingSampler s_ReBlurPreBlurProfilingSampler = new(ReBlurPreBlurProfilerTag);
        private static readonly ProfilingSampler s_ReBlurTemporalAccumulationProfilingSampler = new(ReBlurTemporalAccumulationProfilerTag);
        private static readonly ProfilingSampler s_ReBlurMipGenerationProfilingSampler = new(ReBlurMipGenerationProfilerTag);
        private static readonly ProfilingSampler s_ReBlurMipHistoryFixProfilingSampler = new(ReBlurMipHistoryFixProfilerTag);
        private static readonly ProfilingSampler s_ReBlurBlurProfilingSampler = new(ReBlurBlurProfilerTag);
        private static readonly ProfilingSampler s_ReBlurCopyHistoryProfilingSampler = new(ReBlurCopyHistoryProfilerTag);
        private static readonly ProfilingSampler s_ReBlurTemporalStabilizationProfilingSampler = new(ReBlurTemporalStabilizationProfilerTag);
        private static readonly ProfilingSampler s_ReBlurCopyHistoryStabProfilingSampler = new(ReBlurCopyHistoryStabProfilerTag);
        private static readonly ProfilingSampler s_ReBlurPostBlurProfilingSampler = new(ReBlurPostBlurProfilerTag);
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
        private static readonly int SSRHybridCandidateBufferId = Shader.PropertyToID("_SSRHybridCandidateBuffer");
        private static readonly int SSRHybridDispatchIndirectArgsId = Shader.PropertyToID("_SSRHybridDispatchIndirectArgs");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int HZBTextureId = Shader.PropertyToID("_HZBTexture");
        private static readonly int GBuffer0Id = Shader.PropertyToID("_GBuffer0");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int GBuffer2Id = Shader.PropertyToID("_GBuffer2");
        private static readonly int MotionVectorsId = Shader.PropertyToID("_MotionVectors");
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
        private static readonly int SsrUseHDRPAccumulationHistoryId = Shader.PropertyToID("_SsrUseHDRPAccumulationHistory");
        private static readonly int SsrHDRPAccumulationAmountId = Shader.PropertyToID("_SsrHDRPAccumulationAmount");
        private static readonly int SsrHDRPAccumulationSpeedRejectionId = Shader.PropertyToID("_SsrHDRPAccumulationSpeedRejection");
        private static readonly int SSRAccumTextureId = Shader.PropertyToID("_SSRAccumTexture");
        private static readonly int SSRAvgRadianceTextureId = Shader.PropertyToID("_SSRAvgRadianceTexture");
        private static readonly int ReBlurLightingDistanceTextureId = Shader.PropertyToID("_ReBlurLightingDistanceTexture");
        private static readonly int ReBlurLightingDistanceTextureRWId = Shader.PropertyToID("_ReBlurLightingDistanceTextureRW");
        private static readonly int ReBlurAccumulationTextureId = Shader.PropertyToID("_ReBlurAccumulationTexture");
        private static readonly int ReBlurAccumulationTextureRWId = Shader.PropertyToID("_ReBlurAccumulationTextureRW");
        private static readonly int ReBlurLightingDistanceHistoryId = Shader.PropertyToID("_ReBlurLightingDistanceHistory");
        private static readonly int ReBlurLightingDistanceHistoryRWId = Shader.PropertyToID("_ReBlurLightingDistanceHistoryRW");
        private static readonly int ReBlurAccumulationHistoryId = Shader.PropertyToID("_ReBlurAccumulationHistory");
        private static readonly int ReBlurAccumulationHistoryRWId = Shader.PropertyToID("_ReBlurAccumulationHistoryRW");
        private static readonly int ReBlurStabilizationHistoryId = Shader.PropertyToID("_ReBlurStabilizationHistory");
        private static readonly int ReBlurStabilizationHistoryRWId = Shader.PropertyToID("_ReBlurStabilizationHistoryRW");
        private static readonly int ReBlurMipChainId = Shader.PropertyToID("_ReBlurMipChain");
        private static readonly int ReBlurMipChainRWId = Shader.PropertyToID("_ReBlurMipChainRW");
        private static readonly int ReBlurTargetMipLevelId = Shader.PropertyToID("_TargetMipLevel");
        private const string AccelerationStructureName = "_AccelerationStructure";
        private static readonly int SsrTraceScreenSizeId = Shader.PropertyToID("_SsrTraceScreenSize");
        private static readonly int SsrRoughnessFadeEndId = Shader.PropertyToID("_SsrRoughnessFadeEnd");
        private static readonly int SsrRoughnessFadeRcpLengthId = Shader.PropertyToID("_SsrRoughnessFadeRcpLength");
        private static readonly int SsrRoughnessFadeEndTimesRcpLengthId = Shader.PropertyToID("_SsrRoughnessFadeEndTimesRcpLength");
        private static readonly int SsrEdgeFadeRcpLengthId = Shader.PropertyToID("_SsrEdgeFadeRcpLength");
        private static readonly int SsrIntensityId = Shader.PropertyToID("_SsrIntensity");
        private static readonly int SsrIntensityClampId = Shader.PropertyToID("_SsrIntensityClamp");
        private static readonly int SsrReflectsSkyId = Shader.PropertyToID("_SsrReflectsSky");
        private static readonly int SsrFrameIndexId = Shader.PropertyToID("_SsrFrameIndex");
        private static readonly int SsrHistoryColorPyramidSizeId = Shader.PropertyToID("_SsrHistoryColorPyramidSize");
        private static readonly int SsrUseHistoryColorPyramidId = Shader.PropertyToID("_SsrUseHistoryColorPyramid");
        private static readonly int SsrHistoryColorPyramidMaxMipId = Shader.PropertyToID("_SsrHistoryColorPyramidMaxMip");
        private static readonly int SsrWorldSpaceCameraPosId = Shader.PropertyToID("_SsrWorldSpaceCameraPos");
        private static readonly int SsrViewProjMatrixId = Shader.PropertyToID("_SsrViewProjMatrix");
        private static readonly int SsrInvViewProjMatrixId = Shader.PropertyToID("_SsrInvViewProjMatrix");
        private static readonly int SsrPrevViewProjMatrixId = Shader.PropertyToID("_SsrPrevViewProjMatrix");
        private static readonly int SsrHybridRayBiasId = Shader.PropertyToID("_SsrHybridRayBias");
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
            public Vector4 ReBlurPreBlurRotator;
            public Vector4 ReBlurBlurRotator;
            public Vector4 ReBlurPostBlurRotator;
            public Vector4 ReBlurHistorySizeAndScale;
            public float ReBlurDenoiserRadius;
            public float ReBlurAntiFlickeringStrength;
            public float ReBlurHistoryValidity;
            public float ReBlurPadding;
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

        [RenderGraphResource(Name = "MotionVectors", Access = AccessFlags.Read)]
        private RenderGraphTexture m_MotionVectors;

        [RenderGraphResource(Name = "SceneRTAS", Access = AccessFlags.Read)]
        private readonly RenderGraphAccelerationStructure m_SceneAccelerationStructure;

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
            Name = "ScreenSpaceReflectionResolveAccum",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_ResolveAccumTexture;

        [RenderGraphResource(
            Name = "ScreenSpaceReflectionAvgRadiance",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_AvgRadianceTexture;

        [RenderGraphResource(
            Name = "SSRTileList",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private readonly RenderGraphBuffer m_TileListBuffer;

        [RenderGraphResource(
            Name = "SSRDispatchIndirectArgs",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private readonly RenderGraphBuffer m_DispatchIndirectArgsBuffer;

        [RenderGraphResource(
            Name = "SSRHybridCandidateBuffer",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_HybridCandidateBuffer;

        [RenderGraphResource(
            Name = "SSRHybridDispatchIndirectArgs",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_HybridDispatchIndirectArgsBuffer;
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
            Name = "ScreenSpaceReflectionHDRPAccumPrev",
            Access = AccessFlags.Read)]
        private readonly RenderGraphTexture m_HDRPAccumHistoryPrevious;

        [RenderGraphResource(
            Name = "ScreenSpaceReflectionHDRPAccumTexture",
            Access = AccessFlags.Write)]
        private readonly RenderGraphTexture m_HDRPAccumHistoryCurrent;

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

        [RenderGraphResource(
            Name = "ReBlurLightingDistance",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_ReBlurLightingDistanceTexture;

        [RenderGraphResource(
            Name = "ReBlurLightingDistanceIntermediate",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_ReBlurIntermediateTexture;

        [RenderGraphResource(
            Name = "ReBlurMipChain",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_ReBlurMipTexture;

        [RenderGraphResource(
            Name = "ReBlurAccumulation",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_ReBlurAccumulationTexture;

        [RenderGraphResource(
            Name = "ReBlurLightingDistanceHistory",
            Access = AccessFlags.Read)]
        private readonly RenderGraphTexture m_ReBlurLightingDistanceHistoryPrevious;

        [RenderGraphResource(
            Name = "ReBlurLightingDistanceHistoryTexture",
            Access = AccessFlags.Write)]
        private readonly RenderGraphTexture m_ReBlurLightingDistanceHistoryCurrent;

        [RenderGraphResource(
            Name = "ReBlurAccumulationHistory",
            Access = AccessFlags.Read)]
        private readonly RenderGraphTexture m_ReBlurAccumulationHistoryPrevious;

        [RenderGraphResource(
            Name = "ReBlurAccumulationHistoryTexture",
            Access = AccessFlags.Write)]
        private readonly RenderGraphTexture m_ReBlurAccumulationHistoryCurrent;

        [RenderGraphResource(
            Name = "ReBlurStabilizationHistory",
            Access = AccessFlags.Read)]
        private readonly RenderGraphTexture m_ReBlurStabilizationHistoryPrevious;

        [RenderGraphResource(
            Name = "ReBlurStabilizationHistoryTexture",
            Access = AccessFlags.Write)]
        private readonly RenderGraphTexture m_ReBlurStabilizationHistoryCurrent;

        private int m_SSRClassifyTilesKernel = -1;
        private int m_SSRTracingKernel = -1;
        private int m_SSRHybridCandidatesKernel = -1;
        private int m_SSRRayTracingTemporalKernel = -1;
        private int m_SSRRayTracingDenoiseHKernel = -1;
        private int m_SSRRayTracingDenoiseVKernel = -1;
        private int m_ReBlurPreBlurKernel = -1;
        private int m_ReBlurTemporalAccumulationKernel = -1;
        private int m_ReBlurMipGenerationKernel = -1;
        private int m_ReBlurHistoryFixKernel = -1;
        private int m_ReBlurBlurKernel = -1;
        private int m_ReBlurCopyHistoryAccumulationKernel = -1;
        private int m_ReBlurCopyHistoryKernel = -1;
        private int m_ReBlurTemporalStabilizationKernel = -1;
        private int m_ReBlurPostBlurKernel = -1;
        private int m_SSRResolveKernel = -1;
        private int m_SSRAccumulateKernel = -1;
        private int m_SSRHDRPTracingKernel = -1;
        private int m_SSRHDRPReprojectionKernel = -1;
        private int m_SSRHDRPAccumulateKernel = -1;
        private int m_CopyKernel = -1;
        private RayTracingShader m_HybridTraceRayTracingShader;
        private int m_Width = 1;
        private int m_Height = 1;
        private int m_TileCountX = 1;
        private int m_TileCountY = 1;
        private bool m_ShouldApply;
        private bool m_IsPassResourceLayoutDirty;
        private bool m_UseHistoryColorPyramid;
        private bool m_HasValidAccumulationHistory;
        private bool m_HasValidHDRPAccumulationHistory;
        private bool m_HasValidReBlurHistory;
        private bool m_HistoryInvalidated = true;
        private bool m_HDRPHistoryInvalidated = true;
        private bool m_SupportsRayTracing;

        private ScreenSpaceReflectionExecutionPath m_ExecutionPath = ScreenSpaceReflectionExecutionPath.Vivid;

        [RenderGraphResource(Name = "PreviousColorPyramid", Access = AccessFlags.Read)]
        private RenderGraphTexture m_PreviousColorPyramidTexture;
        private Color m_SkyTextureTint = Color.white;
        private Vector4 m_SkyTextureParams;
        private ScreenSpaceReflectionSettingsData m_Settings;
        private ScreenSpaceReflectionConstantBufferData m_ConstantBuffer;
        private ShaderVariablesRayTracing m_ShaderVariablesRayTracing;

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
            m_MotionVectors = RenderGraphTexture.CreateInput("MotionVectors", GraphicsFormat.R16G16_SFloat);
            m_SceneAccelerationStructure = CreateSceneAccelerationStructure();
            output = CreateColorTexture("ScreenSpaceReflectionOutput", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_TraceTexture = CreateColorTexture("ScreenSpaceReflectionTrace", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_ResolveTexture = CreateColorTexture("ScreenSpaceReflectionResolve", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_RayInfoTexture = CreateColorTexture("ScreenSpaceReflectionRayInfo", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_ResolveAccumTexture = CreateColorTexture("ScreenSpaceReflectionResolveAccum", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_AvgRadianceTexture = CreateColorTexture("ScreenSpaceReflectionAvgRadiance", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_TileListBuffer = RenderGraphBuffer.CreateStructured("SSRTileList", 1, sizeof(uint));
            m_DispatchIndirectArgsBuffer = RenderGraphBuffer.CreateStructured(
                "SSRDispatchIndirectArgs",
                IndirectArgsElementCount,
                sizeof(uint),
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments);
            m_HybridCandidateBuffer = RenderGraphBuffer.CreateStructured("SSRHybridCandidateBuffer", 1, sizeof(uint));
            m_HybridDispatchIndirectArgsBuffer = RenderGraphBuffer.CreateStructured(
                "SSRHybridDispatchIndirectArgs",
                RayDispatchArgsElementCount,
                sizeof(uint),
                GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments);
            m_SkyTexture = CreateSkyCubemapTexture("ScreenSpaceReflectionSkyTexture");
            m_DebugTexture = CreateColorTexture("ScreenSpaceReflectionDebug", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_HDRPHitPointTexture = CreateColorTexture("ScreenSpaceReflectionHDRPHitPoint", 1, 1, GraphicsFormat.R16G16_UNorm);
            m_HDRPAccumTexture = CreateColorTexture("ScreenSpaceReflectionHDRPAccum", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_HDRPOutputTexture = CreateColorTexture("ScreenSpaceReflectionHDRPOutput", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_DefaultPreviousColorPyramidTexture = RenderGraphTexture.CreateInput(
                "PreviousColorPyramid",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_PreviousColorPyramidTexture = m_DefaultPreviousColorPyramidTexture;
            m_HDRPAccumHistoryPrevious = RenderGraphTexture.CreateInput(
                "ScreenSpaceReflectionHDRPAccumPrev",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_HDRPAccumHistoryCurrent = CreateColorTexture(
                "ScreenSpaceReflectionHDRPAccumTexture",
                1,
                1,
                GraphicsFormat.R16G16B16A16_SFloat);
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
            m_ReBlurLightingDistanceTexture = CreateColorTexture("ReBlurLightingDistance", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_ReBlurIntermediateTexture = CreateColorTexture("ReBlurLightingDistanceIntermediate", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_ReBlurMipTexture = CreateColorTexture("ReBlurMipChain", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
            m_ReBlurAccumulationTexture = CreateColorTexture("ReBlurAccumulation", 1, 1, GraphicsFormat.R8_UInt);
            m_ReBlurLightingDistanceHistoryPrevious = RenderGraphTexture.CreateInput(
                "ReBlurLightingDistanceHistory",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_ReBlurLightingDistanceHistoryCurrent = CreateColorTexture(
                "ReBlurLightingDistanceHistoryTexture",
                1,
                1,
                GraphicsFormat.R16G16B16A16_SFloat);
            m_ReBlurAccumulationHistoryPrevious = RenderGraphTexture.CreateInput(
                "ReBlurAccumulationHistory",
                GraphicsFormat.R8_UInt);
            m_ReBlurAccumulationHistoryCurrent = CreateColorTexture(
                "ReBlurAccumulationHistoryTexture",
                1,
                1,
                GraphicsFormat.R8_UInt);
            m_ReBlurStabilizationHistoryPrevious = RenderGraphTexture.CreateInput(
                "ReBlurStabilizationHistory",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_ReBlurStabilizationHistoryCurrent = CreateColorTexture(
                "ReBlurStabilizationHistoryTexture",
                1,
                1,
                GraphicsFormat.R16G16B16A16_SFloat);

            ConfigureHZBDescriptor(m_HZBTexture);
            ConfigureInternalTextureDescriptor(m_TraceTexture, "ScreenSpaceReflectionTrace", 1, 1);
            ConfigureInternalTextureDescriptor(m_ResolveTexture, "ScreenSpaceReflectionResolve", 1, 1);
            ConfigureInternalTextureDescriptor(m_RayInfoTexture, "ScreenSpaceReflectionRayInfo", 1, 1);
            ConfigureInternalTextureDescriptor(m_ResolveAccumTexture, "ScreenSpaceReflectionResolveAccum", 1, 1);
            ConfigureAvgRadianceDescriptor(m_AvgRadianceTexture, 1, 1);
            ConfigureInternalTextureDescriptor(m_DebugTexture, "ScreenSpaceReflectionDebug", 1, 1);
            ConfigureHDRPHitPointDescriptor(m_HDRPHitPointTexture, 1, 1);
            ConfigureInternalTextureDescriptor(m_HDRPAccumTexture, "ScreenSpaceReflectionHDRPAccum", 1, 1);
            ConfigureInternalTextureDescriptor(m_HDRPOutputTexture, "ScreenSpaceReflectionHDRPOutput", 1, 1);
            ConfigureInternalTextureDescriptor(m_HDRPAccumHistoryPrevious, "ScreenSpaceReflectionHDRPAccumPrev", 1, 1);
            ConfigureInternalTextureDescriptor(m_HDRPAccumHistoryCurrent, "ScreenSpaceReflectionHDRPAccumTexture", 1, 1);
            ConfigureInternalTextureDescriptor(m_AccumulationHistoryPrevious, "ScreenSpaceReflectionAccumPrev", 1, 1);
            ConfigureInternalTextureDescriptor(m_AccumulationHistoryCurrent, "ScreenSpaceReflectionAccumTexture", 1, 1);
            ConfigureSingleChannelHistoryDescriptor(m_NumFramesHistoryPrevious, "ScreenSpaceReflectionPrevNumFramesAccum", 1, 1);
            ConfigureSingleChannelHistoryDescriptor(m_NumFramesHistoryCurrent, "ScreenSpaceReflectionNumFramesAccum", 1, 1);
            ConfigureInternalTextureDescriptor(m_ReBlurLightingDistanceTexture, "ReBlurLightingDistance", 1, 1);
            ConfigureInternalTextureDescriptor(m_ReBlurIntermediateTexture, "ReBlurLightingDistanceIntermediate", 1, 1);
            ConfigureReBlurMipDescriptor(m_ReBlurMipTexture, 1, 1);
            ConfigureReBlurAccumulationDescriptor(m_ReBlurAccumulationTexture, "ReBlurAccumulation", 1, 1);
            ConfigureInternalTextureDescriptor(m_ReBlurLightingDistanceHistoryPrevious, "ReBlurLightingDistanceHistory", 1, 1);
            ConfigureInternalTextureDescriptor(m_ReBlurLightingDistanceHistoryCurrent, "ReBlurLightingDistanceHistoryTexture", 1, 1);
            ConfigureReBlurAccumulationDescriptor(m_ReBlurAccumulationHistoryPrevious, "ReBlurAccumulationHistory", 1, 1);
            ConfigureReBlurAccumulationDescriptor(m_ReBlurAccumulationHistoryCurrent, "ReBlurAccumulationHistoryTexture", 1, 1);
            ConfigureInternalTextureDescriptor(m_ReBlurStabilizationHistoryPrevious, "ReBlurStabilizationHistory", 1, 1);
            ConfigureInternalTextureDescriptor(m_ReBlurStabilizationHistoryCurrent, "ReBlurStabilizationHistoryTexture", 1, 1);
        }

        public void ClearPassResourceLayoutDirty()
        {
            m_IsPassResourceLayoutDirty = false;
        }

        private void ResetReBlurKernels()
        {
            m_ReBlurPreBlurKernel = -1;
            m_ReBlurTemporalAccumulationKernel = -1;
            m_ReBlurMipGenerationKernel = -1;
            m_ReBlurHistoryFixKernel = -1;
            m_ReBlurBlurKernel = -1;
            m_ReBlurCopyHistoryAccumulationKernel = -1;
            m_ReBlurCopyHistoryKernel = -1;
            m_ReBlurTemporalStabilizationKernel = -1;
            m_ReBlurPostBlurKernel = -1;
        }

        public override void Create()
        {
            m_SupportsRayTracing = SystemInfo.supportsRayTracing;
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_ComputeShader = resources?.ScreenSpaceReflectionCompute;
            m_HybridTraceRayTracingShader = resources?.ScreenSpaceReflectionHybridTraceRayTracing;
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
                m_SSRHybridCandidatesKernel = -1;
                m_SSRRayTracingTemporalKernel = -1;
                m_SSRRayTracingDenoiseHKernel = -1;
                m_SSRRayTracingDenoiseVKernel = -1;
                ResetReBlurKernels();
                m_SSRResolveKernel = -1;
                m_SSRAccumulateKernel = -1;
                m_SSRHDRPTracingKernel = -1;
                m_SSRHDRPReprojectionKernel = -1;
                m_SSRHDRPAccumulateKernel = -1;
                m_CopyKernel = -1;
            }

            m_SSRHybridCandidatesKernel = TryFindKernel(m_ComputeShader, "ScreenSpaceReflectionsHybridCandidates");
            m_SSRRayTracingTemporalKernel = TryFindKernel(m_ComputeShader, "ScreenSpaceReflectionsRayTracingTemporal");
            m_SSRRayTracingDenoiseHKernel = TryFindKernel(m_ComputeShader, "ScreenSpaceReflectionsRayTracingDenoiseH");
            m_SSRRayTracingDenoiseVKernel = TryFindKernel(m_ComputeShader, "ScreenSpaceReflectionsRayTracingDenoiseV");
            m_ReBlurPreBlurKernel = TryFindKernel(m_ComputeShader, "PreBlur");
            m_ReBlurTemporalAccumulationKernel = TryFindKernel(m_ComputeShader, "TemporalAccumulation");
            m_ReBlurMipGenerationKernel = TryFindKernel(m_ComputeShader, "MipGeneration");
            m_ReBlurHistoryFixKernel = TryFindKernel(m_ComputeShader, "HistoryFix");
            m_ReBlurBlurKernel = TryFindKernel(m_ComputeShader, "Blur");
            m_ReBlurCopyHistoryAccumulationKernel = TryFindKernel(m_ComputeShader, "CopyHistoryAccumulation");
            m_ReBlurCopyHistoryKernel = TryFindKernel(m_ComputeShader, "CopyHistory");
            m_ReBlurTemporalStabilizationKernel = TryFindKernel(m_ComputeShader, "TemporalStabilization");
            m_ReBlurPostBlurKernel = TryFindKernel(m_ComputeShader, "PostBlur");
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
            m_ExecutionPath = m_Settings.executionPath;
            m_ShouldApply = postProcessingAllowed && m_Settings.enabled;
            PrepareSkyTextureState(frameData.GetOrCreate<VividSkyData>());

            ResizeInputTexture(m_DepthTexture, m_Width, m_Height);
            ResizeInputTexture(m_GBuffer0, m_Width, m_Height);
            ResizeInputTexture(m_GBuffer1, m_Width, m_Height);
            ResizeInputTexture(m_GBuffer2, m_Width, m_Height);
            ResizeInputTexture(m_MotionVectors, m_Width, m_Height);
            if (ReferenceEquals(m_HZBTexture, m_DefaultHZBTexture))
            {
                ResizeInputTexture(m_HZBTexture, m_Width, m_Height);
                ConfigureHZBDescriptor(m_HZBTexture);
            }

            if (ReferenceEquals(m_HZBMipLevelOffsets, m_DefaultHZBMipLevelOffsets))
                ConfigureHZBMipLevelOffsetBuffer(m_HZBMipLevelOffsets);

            UpdateOutputDescriptor(m_Width, m_Height);
            UpdateTileResourcesDescriptor(m_Width, m_Height);
            UpdateReBlurResourcesDescriptor(m_Width, m_Height);

            m_ConstantBuffer = BuildConstantBuffer(
                cameraData,
                m_Width,
                m_Height,
                m_Settings);
            m_ShaderVariablesRayTracing =
                ShaderVariablesRayTracingUtility.Create(frameData.GetOrCreate<VividRayTracingSettingsData>());
            PrepareReBlurHistory(frameData);
            PrepareAccumulationHistory(frameData);
            PrepareHDRPAccumulationHistory(frameData);
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
                BindDebugTexture(cmd, m_SSRHybridCandidatesKernel);
                BindDebugTexture(cmd, m_SSRRayTracingTemporalKernel);
                BindDebugTexture(cmd, m_SSRRayTracingDenoiseHKernel);
                BindDebugTexture(cmd, m_SSRRayTracingDenoiseVKernel);
                BindDebugTexture(cmd, m_ReBlurPreBlurKernel);
                BindDebugTexture(cmd, m_ReBlurTemporalAccumulationKernel);
                BindDebugTexture(cmd, m_ReBlurMipGenerationKernel);
                BindDebugTexture(cmd, m_ReBlurHistoryFixKernel);
                BindDebugTexture(cmd, m_ReBlurBlurKernel);
                BindDebugTexture(cmd, m_ReBlurCopyHistoryAccumulationKernel);
                BindDebugTexture(cmd, m_ReBlurCopyHistoryKernel);
                BindDebugTexture(cmd, m_ReBlurTemporalStabilizationKernel);
                BindDebugTexture(cmd, m_ReBlurPostBlurKernel);
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
                    if (ShouldRunRayTracingPath())
                    {
                        using (new ProfilingScope(cmd, s_SSRRayTracingProfilingSampler))
                            DispatchRayTracing(cmd, context);

                        using (new ProfilingScope(cmd, s_SSRRayTracingDenoiseProfilingSampler))
                            DispatchRayTracingReBlur(cmd);
                    }
                    else
                    {
                        ResetDispatchIndirectArgs(cmd);

                        using (new ProfilingScope(cmd, s_SSRClassifyTilesProfilingSampler))
                            DispatchClassifyTiles(cmd);

                        using (new ProfilingScope(cmd, s_SSRTracingProfilingSampler))
                            DispatchTrace(cmd, context);

                        if (ShouldRunHybridPath() && CanExecuteHybridIntersection())
                        {
                            using (new ProfilingScope(cmd, s_SSRHybridTraceProfilingSampler))
                            {
                                ResetHybridDispatchIndirectArgs(cmd);
                                DispatchHybridCandidates(cmd);
                                DispatchHybridTrace(cmd, context);
                            }
                        }

                        using (new ProfilingScope(cmd, s_SSRResolveProfilingSampler))
                            DispatchResolve(cmd);

                        using (new ProfilingScope(cmd, s_SSRAccumulateProfilingSampler))
                            DispatchAccumulate(cmd);
                    }

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
            m_HybridTraceRayTracingShader = null;
            m_SSRClassifyTilesKernel = -1;
            m_SSRTracingKernel = -1;
            m_SSRHybridCandidatesKernel = -1;
            m_SSRRayTracingTemporalKernel = -1;
            m_SSRRayTracingDenoiseHKernel = -1;
            m_SSRRayTracingDenoiseVKernel = -1;
            ResetReBlurKernels();
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
            m_HasValidHDRPAccumulationHistory = false;
            m_HasValidReBlurHistory = false;
            m_HistoryInvalidated = true;
            m_HDRPHistoryInvalidated = true;
            m_SupportsRayTracing = false;
            m_PreviousColorPyramidTexture = m_DefaultPreviousColorPyramidTexture;
            m_SkyTextureTint = Color.white;
            m_SkyTextureParams = Vector4.zero;
            m_Settings = ScreenSpaceReflectionSettingsData.CreateDefault();
            m_ExecutionPath = m_Settings.executionPath;
            m_ConstantBuffer = default;
            m_ShaderVariablesRayTracing = default;
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

        private void PrepareHDRPAccumulationHistory(ContextContainer frameData)
        {
            m_HasValidHDRPAccumulationHistory = false;
            ConfigureInternalTextureDescriptor(
                m_HDRPAccumHistoryPrevious,
                "ScreenSpaceReflectionHDRPAccumPrev",
                m_Width,
                m_Height);
            ConfigureInternalTextureDescriptor(
                m_HDRPAccumHistoryCurrent,
                "ScreenSpaceReflectionHDRPAccumTexture",
                m_Width,
                m_Height);

            if (!m_ShouldApply || !ShouldRunHDRPPath())
            {
                m_HDRPHistoryInvalidated = true;
                return;
            }

            bool hasAccumulationHistory = AllocHistoryTexture(
                HDRPAccumulationHistoryKey,
                m_HDRPAccumHistoryPrevious,
                m_HDRPAccumHistoryCurrent,
                m_HDRPAccumHistoryCurrent.desc);

            var temporalData = frameData.Get<VividTemporalData>();
            bool isFirstFrame = temporalData != null && temporalData.isFirstFrame;
            m_HasValidHDRPAccumulationHistory = hasAccumulationHistory
                && !isFirstFrame
                && !m_HDRPHistoryInvalidated;
            m_HDRPHistoryInvalidated = false;
        }

        private void PrepareReBlurHistory(ContextContainer frameData)
        {
            m_HasValidReBlurHistory = false;
            m_ConstantBuffer.ReBlurHistoryValidity = 0.0f;

            ConfigureInternalTextureDescriptor(
                m_ReBlurLightingDistanceHistoryPrevious,
                "ReBlurLightingDistanceHistory",
                m_Width,
                m_Height);
            ConfigureInternalTextureDescriptor(
                m_ReBlurLightingDistanceHistoryCurrent,
                "ReBlurLightingDistanceHistoryTexture",
                m_Width,
                m_Height);
            ConfigureReBlurAccumulationDescriptor(
                m_ReBlurAccumulationHistoryPrevious,
                "ReBlurAccumulationHistory",
                m_Width,
                m_Height);
            ConfigureReBlurAccumulationDescriptor(
                m_ReBlurAccumulationHistoryCurrent,
                "ReBlurAccumulationHistoryTexture",
                m_Width,
                m_Height);
            ConfigureInternalTextureDescriptor(
                m_ReBlurStabilizationHistoryPrevious,
                "ReBlurStabilizationHistory",
                m_Width,
                m_Height);
            ConfigureInternalTextureDescriptor(
                m_ReBlurStabilizationHistoryCurrent,
                "ReBlurStabilizationHistoryTexture",
                m_Width,
                m_Height);

            if (!m_ShouldApply || !ShouldRunRayTracingPath())
                return;

            bool hasLightingHistory = AllocHistoryTexture(
                ReBlurLightingDistanceHistoryKey,
                m_ReBlurLightingDistanceHistoryPrevious,
                m_ReBlurLightingDistanceHistoryCurrent,
                m_ReBlurLightingDistanceHistoryCurrent.desc);
            bool hasAccumulationHistory = AllocHistoryTexture(
                ReBlurAccumulationHistoryKey,
                m_ReBlurAccumulationHistoryPrevious,
                m_ReBlurAccumulationHistoryCurrent,
                m_ReBlurAccumulationHistoryCurrent.desc);
            bool hasStabilizationHistory = AllocHistoryTexture(
                ReBlurStabilizationHistoryKey,
                m_ReBlurStabilizationHistoryPrevious,
                m_ReBlurStabilizationHistoryCurrent,
                m_ReBlurStabilizationHistoryCurrent.desc);

            var temporalData = frameData.Get<VividTemporalData>();
            bool isFirstFrame = temporalData != null && temporalData.isFirstFrame;
            m_HasValidReBlurHistory = hasLightingHistory
                && hasAccumulationHistory
                && hasStabilizationHistory
                && !isFirstFrame
                && !m_HistoryInvalidated;
            m_ConstantBuffer.ReBlurHistoryValidity = m_HasValidReBlurHistory ? 1.0f : 0.0f;
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
            if (ShouldRunRayTracingPath())
                return CanExecuteRayTracingPath() && CanExecuteRayTracingDenoisePath();

            return CanExecuteVividDenoisePath() && CanExecuteScreenSpaceTracePath();
        }

        private bool CanExecuteVividDenoisePath()
        {
            return m_SSRClassifyTilesKernel >= 0
                && m_SSRResolveKernel >= 0
                && m_SSRAccumulateKernel >= 0
                && output?.innerHandle.IsValid() == true
                && m_TraceTexture?.innerHandle.IsValid() == true
                && m_ResolveTexture?.innerHandle.IsValid() == true
                && m_RayInfoTexture?.innerHandle.IsValid() == true
                && m_ResolveAccumTexture?.innerHandle.IsValid() == true
                && m_AvgRadianceTexture?.innerHandle.IsValid() == true
                && m_AccumulationHistoryPrevious?.innerHandle.IsValid() == true
                && m_AccumulationHistoryCurrent?.innerHandle.IsValid() == true
                && m_NumFramesHistoryPrevious?.innerHandle.IsValid() == true
                && m_NumFramesHistoryCurrent?.innerHandle.IsValid() == true
                && m_TileListBuffer?.innerHandle.IsValid() == true
                && m_DispatchIndirectArgsBuffer?.innerHandle.IsValid() == true
                && m_DepthTexture?.innerHandle.IsValid() == true
                && m_GBuffer1?.innerHandle.IsValid() == true;
        }

        private bool CanExecuteScreenSpaceTracePath()
        {
            return (CanSampleHistoryColorPyramid() || CanSampleSkyFallback())
                && m_SSRClassifyTilesKernel >= 0
                && m_SSRTracingKernel >= 0
                && m_SkyTexture?.innerHandle.IsValid() == true
                && m_HZBTexture?.innerHandle.IsValid() == true
                && m_HZBMipLevelOffsets?.innerHandle.IsValid() == true
                && m_GBuffer0?.innerHandle.IsValid() == true
                && m_GBuffer2?.innerHandle.IsValid() == true;
        }

        private bool CanExecuteRayTracingPath()
        {
            return m_SupportsRayTracing
                && m_HybridTraceRayTracingShader != null
                && m_SceneAccelerationStructure != null
                && (m_SceneAccelerationStructure.innerHandle.IsValid()
                    || m_SceneAccelerationStructure.HasAccelerationStructure)
                && m_TraceTexture?.innerHandle.IsValid() == true
                && m_RayInfoTexture?.innerHandle.IsValid() == true
                && m_DepthTexture?.innerHandle.IsValid() == true
                && m_GBuffer1?.innerHandle.IsValid() == true
                && m_SkyTexture?.innerHandle.IsValid() == true;
        }

        private bool CanExecuteRayTracingDenoisePath()
        {
            return CanExecuteReBlurPath()
                && output?.innerHandle.IsValid() == true
                && m_TraceTexture?.innerHandle.IsValid() == true
                && m_RayInfoTexture?.innerHandle.IsValid() == true
                && m_DepthTexture?.innerHandle.IsValid() == true
                && m_GBuffer1?.innerHandle.IsValid() == true;
        }

        private bool CanExecuteReBlurPath()
        {
            return m_ReBlurPreBlurKernel >= 0
                && m_ReBlurTemporalAccumulationKernel >= 0
                && m_ReBlurMipGenerationKernel >= 0
                && m_ReBlurHistoryFixKernel >= 0
                && m_ReBlurBlurKernel >= 0
                && m_ReBlurCopyHistoryAccumulationKernel >= 0
                && m_ReBlurCopyHistoryKernel >= 0
                && m_ReBlurTemporalStabilizationKernel >= 0
                && m_ReBlurPostBlurKernel >= 0
                && m_ReBlurLightingDistanceTexture?.innerHandle.IsValid() == true
                && m_ReBlurIntermediateTexture?.innerHandle.IsValid() == true
                && m_ReBlurMipTexture?.innerHandle.IsValid() == true
                && m_ReBlurAccumulationTexture?.innerHandle.IsValid() == true
                && m_ReBlurLightingDistanceHistoryPrevious?.innerHandle.IsValid() == true
                && m_ReBlurLightingDistanceHistoryCurrent?.innerHandle.IsValid() == true
                && m_ReBlurAccumulationHistoryPrevious?.innerHandle.IsValid() == true
                && m_ReBlurAccumulationHistoryCurrent?.innerHandle.IsValid() == true
                && m_ReBlurStabilizationHistoryPrevious?.innerHandle.IsValid() == true
                && m_ReBlurStabilizationHistoryCurrent?.innerHandle.IsValid() == true;
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
                && m_HDRPAccumHistoryCurrent?.innerHandle.IsValid() == true
                && output?.innerHandle.IsValid() == true
                && m_DepthTexture?.innerHandle.IsValid() == true
                && m_HZBTexture?.innerHandle.IsValid() == true
                && m_HZBMipLevelOffsets?.innerHandle.IsValid() == true
                && m_GBuffer1?.innerHandle.IsValid() == true;
        }

        private bool CanExecuteHybridIntersection()
        {
            return m_SupportsRayTracing
                && m_SSRHybridCandidatesKernel >= 0
                && m_HybridTraceRayTracingShader != null
                && CanSampleHistoryColorPyramid()
                && m_SceneAccelerationStructure != null
                && (m_SceneAccelerationStructure.innerHandle.IsValid()
                    || m_SceneAccelerationStructure.HasAccelerationStructure)
                && m_TraceTexture?.innerHandle.IsValid() == true
                && m_RayInfoTexture?.innerHandle.IsValid() == true
                && m_TileListBuffer?.innerHandle.IsValid() == true
                && m_DispatchIndirectArgsBuffer?.innerHandle.IsValid() == true
                && m_HybridCandidateBuffer?.innerHandle.IsValid() == true
                && m_HybridDispatchIndirectArgsBuffer?.innerHandle.IsValid() == true
                && m_DepthTexture?.innerHandle.IsValid() == true
                && m_GBuffer1?.innerHandle.IsValid() == true
                && m_PreviousColorPyramidTexture?.innerHandle.IsValid() == true
                && m_SkyTexture?.innerHandle.IsValid() == true;
        }

        private bool ShouldRunVividPath()
        {
            return m_ExecutionPath == ScreenSpaceReflectionExecutionPath.Vivid
                || m_ExecutionPath == ScreenSpaceReflectionExecutionPath.VividAndHDRPComparison
                || m_ExecutionPath == ScreenSpaceReflectionExecutionPath.Hybrid
                || m_ExecutionPath == ScreenSpaceReflectionExecutionPath.RayTracing;
        }

        private bool ShouldRunHybridPath()
        {
            return m_ExecutionPath == ScreenSpaceReflectionExecutionPath.Hybrid;
        }

        private bool ShouldRunRayTracingPath()
        {
            return m_ExecutionPath == ScreenSpaceReflectionExecutionPath.RayTracing;
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
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRClassifyTilesKernel, SSRAccumTextureId, m_ResolveAccumTexture.innerHandle);
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

        private void ResetHybridDispatchIndirectArgs(ComputeCommandBuffer cmd)
        {
            GraphicsBuffer dispatchIndirectArgsBuffer = m_HybridDispatchIndirectArgsBuffer;
            if (dispatchIndirectArgsBuffer == null)
                return;

            cmd.SetBufferData(dispatchIndirectArgsBuffer, s_InitialRayDispatchIndirectArgsData);
        }

        private void DispatchHybridCandidates(ComputeCommandBuffer cmd)
        {
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHybridCandidatesKernel, SSRTraceTextureId, m_TraceTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHybridCandidatesKernel, SSRRayInfoTextureId, m_RayInfoTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHybridCandidatesKernel, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHybridCandidatesKernel, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, m_SSRHybridCandidatesKernel, SSRTileListId, m_TileListBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_SSRHybridCandidatesKernel,
                SSRHybridCandidateBufferId,
                m_HybridCandidateBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ComputeShader,
                m_SSRHybridCandidatesKernel,
                SSRHybridDispatchIndirectArgsId,
                m_HybridDispatchIndirectArgsBuffer.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_SSRHybridCandidatesKernel, m_DispatchIndirectArgsBuffer.innerHandle, 0);
        }

        private void DispatchHybridTrace(ComputeCommandBuffer cmd, ComputePassContext context)
        {
            var accelerationStructure = (RayTracingAccelerationStructure)m_SceneAccelerationStructure;
            if (accelerationStructure == null)
                return;

            BindHybridRayTracingParameters(cmd, context);
            cmd.SetRayTracingShaderPass(m_HybridTraceRayTracingShader, "IndirectDXR");
            cmd.SetRayTracingAccelerationStructure(m_HybridTraceRayTracingShader, AccelerationStructureName, accelerationStructure);
            cmd.SetRayTracingTextureParam(m_HybridTraceRayTracingShader, SSRTraceTextureId, m_TraceTexture.innerHandle);
            cmd.SetRayTracingTextureParam(m_HybridTraceRayTracingShader, SSRRayInfoTextureId, m_RayInfoTexture.innerHandle);
            cmd.SetRayTracingTextureParam(m_HybridTraceRayTracingShader, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetRayTracingTextureParam(m_HybridTraceRayTracingShader, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetRayTracingTextureParam(m_HybridTraceRayTracingShader, SkyTextureId, m_SkyTexture.innerHandle);
            cmd.SetRayTracingBufferParam(
                m_HybridTraceRayTracingShader,
                SSRHybridCandidateBufferId,
                m_HybridCandidateBuffer.innerHandle);

            if (!ReferenceEquals(m_PreviousColorPyramidTexture, m_DefaultPreviousColorPyramidTexture)
                && m_PreviousColorPyramidTexture?.innerHandle.IsValid() == true)
            {
                cmd.SetRayTracingTextureParam(m_HybridTraceRayTracingShader, PreviousColorPyramidTextureId, m_PreviousColorPyramidTexture.innerHandle);
            }
            else
            {
                cmd.SetRayTracingTextureParam(
                    m_HybridTraceRayTracingShader,
                    PreviousColorPyramidTextureId,
                    context.renderGraphContext.defaultResources.blackTexture);
            }

            cmd.DispatchRays(m_HybridTraceRayTracingShader, HybridRayGenName, m_HybridDispatchIndirectArgsBuffer.innerHandle, 0, null);
        }

        private void DispatchRayTracing(ComputeCommandBuffer cmd, ComputePassContext context)
        {
            var accelerationStructure = (RayTracingAccelerationStructure)m_SceneAccelerationStructure;
            if (accelerationStructure == null)
                return;

            BindHybridRayTracingParameters(cmd, context);
            cmd.SetRayTracingShaderPass(m_HybridTraceRayTracingShader, "IndirectDXR");
            cmd.SetRayTracingAccelerationStructure(m_HybridTraceRayTracingShader, AccelerationStructureName, accelerationStructure);
            cmd.SetRayTracingTextureParam(m_HybridTraceRayTracingShader, SSRTraceTextureId, m_TraceTexture.innerHandle);
            cmd.SetRayTracingTextureParam(m_HybridTraceRayTracingShader, SSRRayInfoTextureId, m_RayInfoTexture.innerHandle);
            cmd.SetRayTracingTextureParam(m_HybridTraceRayTracingShader, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetRayTracingTextureParam(m_HybridTraceRayTracingShader, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetRayTracingTextureParam(m_HybridTraceRayTracingShader, SkyTextureId, m_SkyTexture.innerHandle);

            if (m_HybridCandidateBuffer?.innerHandle.IsValid() == true)
                cmd.SetRayTracingBufferParam(m_HybridTraceRayTracingShader, SSRHybridCandidateBufferId, m_HybridCandidateBuffer.innerHandle);

            if (!ReferenceEquals(m_PreviousColorPyramidTexture, m_DefaultPreviousColorPyramidTexture)
                && m_PreviousColorPyramidTexture?.innerHandle.IsValid() == true)
            {
                cmd.SetRayTracingTextureParam(m_HybridTraceRayTracingShader, PreviousColorPyramidTextureId, m_PreviousColorPyramidTexture.innerHandle);
            }
            else
            {
                cmd.SetRayTracingTextureParam(
                    m_HybridTraceRayTracingShader,
                    PreviousColorPyramidTextureId,
                    context.renderGraphContext.defaultResources.blackTexture);
            }

            cmd.DispatchRays(m_HybridTraceRayTracingShader, RayTracingRayGenName, (uint)m_Width, (uint)m_Height, 1, null);
        }

        private void DispatchRayTracingDenoise(ComputeCommandBuffer cmd)
        {
            DispatchRayTracingTemporal(cmd);
            DispatchRayTracingDenoiseH(cmd);
            DispatchRayTracingDenoiseV(cmd);
        }

        private void DispatchRayTracingReBlur(ComputeCommandBuffer cmd)
        {
            int groupsX = CoreUtils.DivRoundUp(m_Width, ThreadGroupSize);
            int groupsY = CoreUtils.DivRoundUp(m_Height, ThreadGroupSize);

            using (new ProfilingScope(cmd, s_ReBlurPreBlurProfilingSampler))
                DispatchReBlurPreBlur(cmd, groupsX, groupsY);

            using (new ProfilingScope(cmd, s_ReBlurTemporalAccumulationProfilingSampler))
                DispatchReBlurTemporalAccumulation(cmd, groupsX, groupsY);

            using (new ProfilingScope(cmd, s_ReBlurMipGenerationProfilingSampler))
                DispatchReBlurMipGeneration(cmd);

            using (new ProfilingScope(cmd, s_ReBlurMipHistoryFixProfilingSampler))
                DispatchReBlurHistoryFix(cmd, groupsX, groupsY);

            using (new ProfilingScope(cmd, s_ReBlurBlurProfilingSampler))
                DispatchReBlurBlur(cmd, groupsX, groupsY);

            using (new ProfilingScope(cmd, s_ReBlurCopyHistoryProfilingSampler))
                DispatchReBlurCopyHistory(cmd, groupsX, groupsY);

            using (new ProfilingScope(cmd, s_ReBlurTemporalStabilizationProfilingSampler))
                DispatchReBlurTemporalStabilization(cmd, groupsX, groupsY);

            using (new ProfilingScope(cmd, s_ReBlurCopyHistoryStabProfilingSampler))
                DispatchReBlurCopyHistoryStab(cmd, groupsX, groupsY);

            using (new ProfilingScope(cmd, s_ReBlurPostBlurProfilingSampler))
                DispatchReBlurPostBlur(cmd, groupsX, groupsY);
        }

        private void DispatchReBlurPreBlur(ComputeCommandBuffer cmd, int groupsX, int groupsY)
        {
            BindReBlurGBufferInputs(cmd, m_ReBlurPreBlurKernel);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ReBlurPreBlurKernel, SSRTraceTextureId, m_TraceTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ReBlurPreBlurKernel, SSRRayInfoTextureId, m_RayInfoTexture.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_ReBlurPreBlurKernel,
                ReBlurLightingDistanceTextureRWId,
                m_ReBlurLightingDistanceTexture.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_ReBlurPreBlurKernel, groupsX, groupsY, 1);
        }

        private void DispatchReBlurTemporalAccumulation(ComputeCommandBuffer cmd, int groupsX, int groupsY)
        {
            BindReBlurGBufferInputs(cmd, m_ReBlurTemporalAccumulationKernel);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_ReBlurTemporalAccumulationKernel,
                ReBlurLightingDistanceTextureId,
                m_ReBlurLightingDistanceTexture.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_ReBlurTemporalAccumulationKernel,
                ReBlurLightingDistanceHistoryId,
                m_ReBlurLightingDistanceHistoryPrevious.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_ReBlurTemporalAccumulationKernel,
                ReBlurAccumulationHistoryId,
                m_ReBlurAccumulationHistoryPrevious.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_ReBlurTemporalAccumulationKernel,
                ReBlurLightingDistanceTextureRWId,
                m_ReBlurIntermediateTexture.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_ReBlurTemporalAccumulationKernel,
                ReBlurAccumulationTextureRWId,
                m_ReBlurAccumulationTexture.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_ReBlurTemporalAccumulationKernel, groupsX, groupsY, 1);
        }

        private void DispatchReBlurMipGeneration(ComputeCommandBuffer cmd)
        {
            if (m_ReBlurMipTexture?.innerHandle.IsValid() != true)
                return;

            int mipCount = Mathf.Min(4, Mathf.Max(1, m_ReBlurMipTexture.desc.MipCount));
            for (int mip = 0; mip < mipCount; mip++)
            {
                int mipWidth = Mathf.Max(1, m_Width >> mip);
                int mipHeight = Mathf.Max(1, m_Height >> mip);
                cmd.SetComputeIntParam(m_ComputeShader, ReBlurTargetMipLevelId, mip);
                cmd.SetComputeTextureParam(
                    m_ComputeShader,
                    m_ReBlurMipGenerationKernel,
                    ReBlurLightingDistanceTextureId,
                    mip == 0 ? m_ReBlurIntermediateTexture.innerHandle : m_ReBlurMipTexture.innerHandle);
                cmd.SetComputeTextureParam(
                    m_ComputeShader,
                    m_ReBlurMipGenerationKernel,
                    ReBlurMipChainId,
                    m_ReBlurMipTexture.innerHandle);
                cmd.SetComputeTextureParam(
                    m_ComputeShader,
                    m_ReBlurMipGenerationKernel,
                    ReBlurMipChainRWId,
                    m_ReBlurMipTexture.innerHandle,
                    mip);
                cmd.DispatchCompute(
                    m_ComputeShader,
                    m_ReBlurMipGenerationKernel,
                    CoreUtils.DivRoundUp(mipWidth, ThreadGroupSize),
                    CoreUtils.DivRoundUp(mipHeight, ThreadGroupSize),
                    1);
            }
        }

        private void DispatchReBlurHistoryFix(ComputeCommandBuffer cmd, int groupsX, int groupsY)
        {
            BindReBlurGBufferInputs(cmd, m_ReBlurHistoryFixKernel);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_ReBlurHistoryFixKernel,
                ReBlurLightingDistanceTextureId,
                m_ReBlurIntermediateTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ReBlurHistoryFixKernel, ReBlurAccumulationTextureId, m_ReBlurAccumulationTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ReBlurHistoryFixKernel, ReBlurMipChainId, m_ReBlurMipTexture.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_ReBlurHistoryFixKernel,
                ReBlurLightingDistanceTextureRWId,
                m_ReBlurLightingDistanceTexture.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_ReBlurHistoryFixKernel, groupsX, groupsY, 1);
        }

        private void DispatchReBlurBlur(ComputeCommandBuffer cmd, int groupsX, int groupsY)
        {
            BindReBlurGBufferInputs(cmd, m_ReBlurBlurKernel);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ReBlurBlurKernel, ReBlurLightingDistanceTextureId, m_ReBlurLightingDistanceTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ReBlurBlurKernel, ReBlurAccumulationTextureId, m_ReBlurAccumulationTexture.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_ReBlurBlurKernel,
                ReBlurLightingDistanceTextureRWId,
                m_ReBlurIntermediateTexture.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_ReBlurBlurKernel, groupsX, groupsY, 1);
        }

        private void DispatchReBlurCopyHistory(ComputeCommandBuffer cmd, int groupsX, int groupsY)
        {
            cmd.SetComputeTextureParam(m_ComputeShader, m_ReBlurCopyHistoryAccumulationKernel, ReBlurLightingDistanceTextureId, m_ReBlurIntermediateTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ReBlurCopyHistoryAccumulationKernel, ReBlurAccumulationTextureId, m_ReBlurAccumulationTexture.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_ReBlurCopyHistoryAccumulationKernel,
                ReBlurLightingDistanceHistoryRWId,
                m_ReBlurLightingDistanceHistoryCurrent.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_ReBlurCopyHistoryAccumulationKernel,
                ReBlurAccumulationHistoryRWId,
                m_ReBlurAccumulationHistoryCurrent.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_ReBlurCopyHistoryAccumulationKernel, groupsX, groupsY, 1);
        }

        private void DispatchReBlurTemporalStabilization(ComputeCommandBuffer cmd, int groupsX, int groupsY)
        {
            BindReBlurGBufferInputs(cmd, m_ReBlurTemporalStabilizationKernel);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_ReBlurTemporalStabilizationKernel,
                ReBlurLightingDistanceTextureId,
                m_ReBlurIntermediateTexture.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_ReBlurTemporalStabilizationKernel,
                ReBlurStabilizationHistoryId,
                m_ReBlurStabilizationHistoryPrevious.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_ReBlurTemporalStabilizationKernel,
                ReBlurLightingDistanceTextureRWId,
                m_ReBlurLightingDistanceTexture.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_ReBlurTemporalStabilizationKernel, groupsX, groupsY, 1);
        }

        private void DispatchReBlurCopyHistoryStab(ComputeCommandBuffer cmd, int groupsX, int groupsY)
        {
            cmd.SetComputeTextureParam(m_ComputeShader, m_ReBlurCopyHistoryKernel, ReBlurLightingDistanceTextureId, m_ReBlurLightingDistanceTexture.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_ReBlurCopyHistoryKernel,
                ReBlurStabilizationHistoryRWId,
                m_ReBlurStabilizationHistoryCurrent.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_ReBlurCopyHistoryKernel, groupsX, groupsY, 1);
        }

        private void DispatchReBlurPostBlur(ComputeCommandBuffer cmd, int groupsX, int groupsY)
        {
            BindReBlurGBufferInputs(cmd, m_ReBlurPostBlurKernel);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ReBlurPostBlurKernel, ReBlurLightingDistanceTextureId, m_ReBlurLightingDistanceTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ReBlurPostBlurKernel, ReBlurAccumulationTextureId, m_ReBlurAccumulationTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ReBlurPostBlurKernel, SSRTraceTextureId, m_TraceTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ReBlurPostBlurKernel, SSRRayInfoTextureId, m_RayInfoTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ReBlurPostBlurKernel, OutputColorTextureId, output.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_ReBlurPostBlurKernel, groupsX, groupsY, 1);
        }

        private void BindReBlurGBufferInputs(ComputeCommandBuffer cmd, int kernel)
        {
            cmd.SetComputeTextureParam(m_ComputeShader, kernel, SSRTraceTextureId, m_TraceTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, kernel, SSRRayInfoTextureId, m_RayInfoTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, kernel, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, kernel, GBuffer1Id, m_GBuffer1.innerHandle);
        }

        private void DispatchRayTracingTemporal(ComputeCommandBuffer cmd)
        {
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRRayTracingTemporalKernel, SSRTraceTextureId, m_TraceTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRRayTracingTemporalKernel, SSRResolveTextureId, m_ResolveTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRRayTracingTemporalKernel, SSRRayInfoTextureId, m_RayInfoTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRRayTracingTemporalKernel, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRRayTracingTemporalKernel, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRRayTracingTemporalKernel, SsrAccumPrevId, m_AccumulationHistoryPrevious.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRRayTracingTemporalKernel, SsrAccumTextureId, m_AccumulationHistoryCurrent.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_SSRRayTracingTemporalKernel,
                SSRPrevNumFramesAccumTextureId,
                m_NumFramesHistoryPrevious.innerHandle);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_SSRRayTracingTemporalKernel,
                SSRNumFramesAccumTextureId,
                m_NumFramesHistoryCurrent.innerHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, m_SSRRayTracingTemporalKernel, SSRTileListId, m_TileListBuffer.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_SSRRayTracingTemporalKernel, m_DispatchIndirectArgsBuffer.innerHandle, 0);
        }

        private void DispatchRayTracingDenoiseH(ComputeCommandBuffer cmd)
        {
            BindRayTracingDenoiseSpatialInputs(cmd, m_SSRRayTracingDenoiseHKernel);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRRayTracingDenoiseHKernel, SSRResolveTextureId, m_ResolveTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRRayTracingDenoiseHKernel, SSRAccumTextureId, m_ResolveAccumTexture.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_SSRRayTracingDenoiseHKernel, m_DispatchIndirectArgsBuffer.innerHandle, 0);
        }

        private void DispatchRayTracingDenoiseV(ComputeCommandBuffer cmd)
        {
            BindRayTracingDenoiseSpatialInputs(cmd, m_SSRRayTracingDenoiseVKernel);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRRayTracingDenoiseVKernel, OutputColorTextureId, output.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRRayTracingDenoiseVKernel, SSRAccumTextureId, m_ResolveAccumTexture.innerHandle);
            cmd.DispatchCompute(m_ComputeShader, m_SSRRayTracingDenoiseVKernel, m_DispatchIndirectArgsBuffer.innerHandle, 0);
        }

        private void BindRayTracingDenoiseSpatialInputs(ComputeCommandBuffer cmd, int kernel)
        {
            cmd.SetComputeTextureParam(m_ComputeShader, kernel, SSRTraceTextureId, m_TraceTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, kernel, SSRRayInfoTextureId, m_RayInfoTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, kernel, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, kernel, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, kernel, SSRTileListId, m_TileListBuffer.innerHandle);
        }

        private void DispatchResolve(ComputeCommandBuffer cmd)
        {
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRResolveKernel, SSRResolveTextureId, m_ResolveTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRResolveKernel, SSRAccumTextureId, m_ResolveAccumTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRResolveKernel, SSRAvgRadianceTextureId, m_AvgRadianceTexture.innerHandle);
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
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRAccumulateKernel, SSRAccumTextureId, m_ResolveAccumTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_SSRAccumulateKernel, SSRAvgRadianceTextureId, m_AvgRadianceTexture.innerHandle);
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
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPReprojectionKernel, DepthTextureId, m_DepthTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPReprojectionKernel, GBuffer1Id, m_GBuffer1.innerHandle);
                cmd.SetComputeTextureParam(
                    m_ComputeShader,
                    m_SSRHDRPReprojectionKernel,
                    MotionVectorsId,
                    m_MotionVectors?.innerHandle.IsValid() == true
                        ? m_MotionVectors.innerHandle
                        : context.renderGraphContext.defaultResources.blackTexture);
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
                cmd.SetComputeIntParam(
                    m_ComputeShader,
                    SsrUseHDRPAccumulationHistoryId,
                    m_HasValidHDRPAccumulationHistory ? 1 : 0);
                cmd.SetComputeFloatParam(
                    m_ComputeShader,
                    SsrHDRPAccumulationAmountId,
                    m_HasValidHDRPAccumulationHistory
                        ? Mathf.Pow(2.0f, Mathf.Lerp(0.0f, -7.0f, HDRPDefaultAccumulationFactor))
                        : 1.0f);
                cmd.SetComputeFloatParam(
                    m_ComputeShader,
                    SsrHDRPAccumulationSpeedRejectionId,
                    HDRPDefaultSpeedRejection);
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPAccumulateKernel, OutputColorTextureId, output.innerHandle);
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPAccumulateKernel, SSRHDRPHitPointTextureId, m_HDRPHitPointTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPAccumulateKernel, SSRHDRPOutputTextureId, m_HDRPOutputTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPAccumulateKernel, SSRHDRPAccumTextureId, m_HDRPAccumTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ComputeShader, m_SSRHDRPAccumulateKernel, GBuffer1Id, m_GBuffer1.innerHandle);
                cmd.SetComputeTextureParam(
                    m_ComputeShader,
                    m_SSRHDRPAccumulateKernel,
                    SsrAccumPrevId,
                    m_HasValidHDRPAccumulationHistory && m_HDRPAccumHistoryPrevious?.innerHandle.IsValid() == true
                        ? m_HDRPAccumHistoryPrevious.innerHandle
                        : context.renderGraphContext.defaultResources.blackTexture);
                cmd.SetComputeTextureParam(
                    m_ComputeShader,
                    m_SSRHDRPAccumulateKernel,
                    SsrAccumTextureId,
                    m_HDRPAccumHistoryCurrent.innerHandle);
                cmd.SetComputeTextureParam(
                    m_ComputeShader,
                    m_SSRHDRPAccumulateKernel,
                    MotionVectorsId,
                    m_MotionVectors?.innerHandle.IsValid() == true
                        ? m_MotionVectors.innerHandle
                        : context.renderGraphContext.defaultResources.blackTexture);
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

        private void BindHybridRayTracingParameters(ComputeCommandBuffer cmd, ComputePassContext context)
        {
            if (cmd == null || m_HybridTraceRayTracingShader == null)
                return;

            BlueNoise.Instance?.Bind(cmd, m_HybridTraceRayTracingShader);

            cmd.SetRayTracingVectorParam(m_HybridTraceRayTracingShader, SkyTextureTintId, m_SkyTextureTint);
            cmd.SetRayTracingVectorParam(m_HybridTraceRayTracingShader, SkyTextureParamsId, m_SkyTextureParams);
            cmd.SetRayTracingVectorParam(m_HybridTraceRayTracingShader, SsrTraceScreenSizeId, m_ConstantBuffer.SsrTraceScreenSize);
            cmd.SetRayTracingFloatParam(m_HybridTraceRayTracingShader, SsrRoughnessFadeEndId, m_ConstantBuffer.SsrRoughnessFadeEnd);
            cmd.SetRayTracingFloatParam(m_HybridTraceRayTracingShader, SsrRoughnessFadeRcpLengthId, m_ConstantBuffer.SsrRoughnessFadeRcpLength);
            cmd.SetRayTracingFloatParam(
                m_HybridTraceRayTracingShader,
                SsrRoughnessFadeEndTimesRcpLengthId,
                m_ConstantBuffer.SsrRoughnessFadeEndTimesRcpLength);
            cmd.SetRayTracingFloatParam(m_HybridTraceRayTracingShader, SsrEdgeFadeRcpLengthId, m_ConstantBuffer.SsrEdgeFadeRcpLength);
            cmd.SetRayTracingFloatParam(m_HybridTraceRayTracingShader, SsrIntensityId, m_ConstantBuffer.SsrIntensity);
            cmd.SetRayTracingFloatParam(m_HybridTraceRayTracingShader, SsrIntensityClampId, m_ConstantBuffer.SsrIntensityClamp);
            cmd.SetRayTracingIntParam(m_HybridTraceRayTracingShader, SsrReflectsSkyId, m_ConstantBuffer.SsrReflectsSky);
            cmd.SetRayTracingIntParam(m_HybridTraceRayTracingShader, SsrFrameIndexId, m_ConstantBuffer.SsrFrameIndex);
            cmd.SetRayTracingVectorParam(m_HybridTraceRayTracingShader, SsrHistoryColorPyramidSizeId, m_ConstantBuffer.SsrHistoryColorPyramidSize);
            cmd.SetRayTracingIntParam(
                m_HybridTraceRayTracingShader,
                SsrUseHistoryColorPyramidId,
                m_ConstantBuffer.SsrUseHistoryColorPyramid);
            cmd.SetRayTracingIntParam(
                m_HybridTraceRayTracingShader,
                SsrHistoryColorPyramidMaxMipId,
                m_ConstantBuffer.SsrHistoryColorPyramidMaxMip);
            cmd.SetRayTracingVectorParam(m_HybridTraceRayTracingShader, SsrWorldSpaceCameraPosId, m_ConstantBuffer.SsrWorldSpaceCameraPos);
            cmd.SetRayTracingMatrixParam(m_HybridTraceRayTracingShader, SsrViewProjMatrixId, m_ConstantBuffer.SsrViewProjMatrix);
            cmd.SetRayTracingMatrixParam(m_HybridTraceRayTracingShader, SsrInvViewProjMatrixId, m_ConstantBuffer.SsrInvViewProjMatrix);
            cmd.SetRayTracingMatrixParam(m_HybridTraceRayTracingShader, SsrPrevViewProjMatrixId, m_ConstantBuffer.SsrPrevViewProjMatrix);
            cmd.SetRayTracingFloatParam(m_HybridTraceRayTracingShader, SsrHybridRayBiasId, m_ShaderVariablesRayTracing._RayTracingRayBias);
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
            ConfigureInternalTextureDescriptor(m_ResolveAccumTexture, "ScreenSpaceReflectionResolveAccum", width, height);
            ConfigureAvgRadianceDescriptor(m_AvgRadianceTexture, m_TileCountX, m_TileCountY);
            ConfigureInternalTextureDescriptor(m_DebugTexture, "ScreenSpaceReflectionDebug", width, height);
            ConfigureHDRPHitPointDescriptor(m_HDRPHitPointTexture, width, height);
            ConfigureInternalTextureDescriptor(m_HDRPAccumTexture, "ScreenSpaceReflectionHDRPAccum", width, height);
            ConfigureInternalTextureDescriptor(m_HDRPOutputTexture, "ScreenSpaceReflectionHDRPOutput", width, height);
            ConfigureInternalTextureDescriptor(m_HDRPAccumHistoryPrevious, "ScreenSpaceReflectionHDRPAccumPrev", width, height);
            ConfigureInternalTextureDescriptor(m_HDRPAccumHistoryCurrent, "ScreenSpaceReflectionHDRPAccumTexture", width, height);
            ConfigureInternalTextureDescriptor(m_AccumulationHistoryPrevious, "ScreenSpaceReflectionAccumPrev", width, height);
            ConfigureInternalTextureDescriptor(m_AccumulationHistoryCurrent, "ScreenSpaceReflectionAccumTexture", width, height);
            ConfigureSingleChannelHistoryDescriptor(m_NumFramesHistoryPrevious, "ScreenSpaceReflectionPrevNumFramesAccum", width, height);
            ConfigureSingleChannelHistoryDescriptor(m_NumFramesHistoryCurrent, "ScreenSpaceReflectionNumFramesAccum", width, height);
            ConfigureTileListBuffer(m_TileListBuffer, maxTileCount);
            ConfigureIndirectArgsBuffer(m_DispatchIndirectArgsBuffer);
            ConfigureHybridCandidateBuffer(m_HybridCandidateBuffer, width, height);
            ConfigureHybridDispatchIndirectArgsBuffer(m_HybridDispatchIndirectArgsBuffer);
            m_DispatchIndirectArgsBuffer.SetData(s_InitialDispatchIndirectArgsData);
        }

        private void UpdateReBlurResourcesDescriptor(int width, int height)
        {
            ConfigureInternalTextureDescriptor(m_ReBlurLightingDistanceTexture, "ReBlurLightingDistance", width, height);
            ConfigureInternalTextureDescriptor(m_ReBlurIntermediateTexture, "ReBlurLightingDistanceIntermediate", width, height);
            ConfigureReBlurMipDescriptor(m_ReBlurMipTexture, width, height);
            ConfigureReBlurAccumulationDescriptor(m_ReBlurAccumulationTexture, "ReBlurAccumulation", width, height);
        }

        private static ScreenSpaceReflectionConstantBufferData BuildConstantBuffer(
            VividCameraData cameraData,
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
            var prevViewProjMatrix = ResolveSsrPrevViewProjMatrix(cameraData, viewProjMatrix);
            int reBlurFrameIndex = Time.frameCount & 31;

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
                SsrPrevViewProjMatrix = prevViewProjMatrix,
                ReBlurPreBlurRotator = EvaluateReBlurRotator(s_ReBlurPreBlurRands[reBlurFrameIndex]),
                ReBlurBlurRotator = EvaluateReBlurRotator(s_ReBlurBlurRands[reBlurFrameIndex]),
                ReBlurPostBlurRotator = EvaluateReBlurRotator(s_ReBlurPostBlurRands[reBlurFrameIndex]),
                ReBlurHistorySizeAndScale = new Vector4(
                    width,
                    height,
                    1.0f / Mathf.Max(1, width),
                    1.0f / Mathf.Max(1, height)),
                ReBlurDenoiserRadius = Mathf.Lerp(0.5f, 1.0f, settings.reBlurDenoiserRadius),
                ReBlurAntiFlickeringStrength = Mathf.Lerp(0.0f, 3.5f, settings.reBlurAntiFlickeringStrength),
                ReBlurHistoryValidity = 0.0f,
                ReBlurPadding = 0.0f
            };
        }

        private static Vector4 EvaluateReBlurRotator(float rand)
        {
            float cos = Mathf.Cos(rand);
            float sin = Mathf.Sin(rand);
            return new Vector4(cos, sin, -sin, cos);
        }

        private static Matrix4x4 ResolveSsrViewProjMatrix(VividCameraData cameraData)
        {
            if (cameraData == null)
                return Matrix4x4.identity;

            return cameraData.GetGPUViewProjectionMatrix(renderIntoTexture: true);
        }

        private static Matrix4x4 ResolveSsrPrevViewProjMatrix(
            VividCameraData cameraData,
            Matrix4x4 fallbackViewProjMatrix)
        {
            return cameraData != null && cameraData.hasShaderVariablesGlobal
                ? cameraData.shaderVariablesGlobal._VividPrevViewProjMatrix
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

        private static int TryFindKernel(ComputeShader shader, string kernelName)
        {
            if (shader == null)
                return -1;

            try
            {
                return shader.FindKernel(kernelName);
            }
            catch (ArgumentException)
            {
                return -1;
            }
        }

        private static RenderGraphAccelerationStructure CreateSceneAccelerationStructure()
        {
            return new RenderGraphAccelerationStructure
            {
                desc = RenderGraphAccelerationStructureDesc.Create("SceneRTAS")
            };
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

        private static void ConfigureHybridCandidateBuffer(RenderGraphBuffer buffer, int width, int height)
        {
            if (buffer?.desc == null)
                return;

            buffer.desc.Count = Mathf.Max(1, Mathf.Max(1, width) * Mathf.Max(1, height));
            buffer.desc.Stride = sizeof(uint);
            buffer.desc.Target = GraphicsBuffer.Target.Structured;
            buffer.desc.Name = "SSRHybridCandidateBuffer";
        }

        private static void ConfigureHybridDispatchIndirectArgsBuffer(RenderGraphBuffer buffer)
        {
            if (buffer?.desc == null)
                return;

            buffer.desc.Count = RayDispatchArgsElementCount;
            buffer.desc.Stride = sizeof(uint);
            buffer.desc.Target = GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments;
            buffer.desc.Name = "SSRHybridDispatchIndirectArgs";
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

        private static void ConfigureHDRPHitPointDescriptor(RenderGraphTexture texture, int width, int height)
        {
            ConfigureInternalTextureDescriptor(texture, "ScreenSpaceReflectionHDRPHitPoint", width, height);
            if (texture?.desc == null)
                return;

            texture.desc.ColorFormat = GraphicsFormat.R16G16_UNorm;
        }

        private static void ConfigureReBlurAccumulationDescriptor(
            RenderGraphTexture texture,
            string name,
            int width,
            int height)
        {
            ConfigureInternalTextureDescriptor(texture, name, width, height);
            if (texture?.desc == null)
                return;

            texture.desc.ColorFormat = GraphicsFormat.R8_UInt;
            texture.desc.FilterMode = FilterMode.Point;
        }

        private static void ConfigureReBlurMipDescriptor(RenderGraphTexture texture, int width, int height)
        {
            ConfigureInternalTextureDescriptor(texture, "ReBlurMipChain", width, height);
            if (texture?.desc == null)
                return;

            texture.desc.UseMipMap = true;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = Mathf.Min(4, CalculateMipCount(width, height));
            texture.desc.FilterMode = FilterMode.Bilinear;
        }

        private static void ConfigureAvgRadianceDescriptor(RenderGraphTexture texture, int tileCountX, int tileCountY)
        {
            ConfigureInternalTextureDescriptor(
                texture,
                "ScreenSpaceReflectionAvgRadiance",
                Mathf.Max(1, tileCountX),
                Mathf.Max(1, tileCountY));
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
