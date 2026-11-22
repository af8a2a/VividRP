using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Runtime.RenderPass.Core
{
    internal sealed class VividSdfReceiverFrameData : ContextItem
    {
        internal RenderGraphTexture Visibility, Depth, GBuffer0, GBuffer1;

        public override void Reset() => Visibility = Depth = GBuffer0 = GBuffer1 = null;
    }

    internal sealed class VividSdfDiffuseFrameData : ContextItem
    {
        internal RenderGraphTexture DeclaredOutput;
        internal RenderGraphTexture Output;
        internal CameraHistoryTexture BentNormalHistory;
        internal TextureHandle BentNormalOutput;
        internal bool CompositeBentNormal;
        internal CameraHistoryTexture RoughSpecularHistory;
        internal TextureHandle RoughSpecularOutput;
        internal bool RoughSpecularPreExposed;
        internal CameraHistoryTexture SpecularReflectionHistory;
        internal TextureHandle SpecularReflectionOutput;
        internal Vector4 SpecularReflectionCombineParams;
        internal float SpecularReflectionSmoothBias;
        internal bool SkyHandled;
        internal bool RealtimeOnly;
        internal SdfDiffuseGatherPass OpenPbrSource;

        public override void Reset()
        {
            DeclaredOutput = null;
            Output = null;
            BentNormalHistory = null;
            BentNormalOutput = default;
            CompositeBentNormal = false;
            RoughSpecularHistory = null;
            RoughSpecularOutput = default;
            RoughSpecularPreExposed = false;
            SpecularReflectionHistory = null;
            SpecularReflectionOutput = default;
            SpecularReflectionCombineParams = Vector4.zero;
            SpecularReflectionSmoothBias = 0f;
            SkyHandled = false;
            RealtimeOnly = false;
            OpenPbrSource = null;
        }
    }

    public sealed class SdfDiffuseGatherPass : ComputePass, IRenderGraphPreparePass
    {
#if UNITY_EDITOR
        internal static System.Action<ComputePassContext, Texture, Texture, Texture> EditorTraceCapture;
        internal static bool EditorSkipEmptyCompactMeshIntervals = true;
        internal static System.Action<ComputePassContext, Texture, Texture, Texture, Texture> EditorLinearCapture;
        internal static bool EditorRecordProbeAnchorDiagnostics;
        internal static int EditorRadiositySeed;
        internal static int EditorGatherFrameOverride = -1;
        internal static int EditorGatherHistoryDiagnostics;
        internal static float EditorProbeAnchorHistoryBlend = 0.85f;
        internal static int EditorProbeAnchorMaxAgeOverride = -1;
        internal static System.Action<ComputePassContext, int, Texture> EditorGatherStageCapture;
        // Diagnostic-only input binding after the normal probe binding, before dispatch.
        internal static System.Action<ComputePassContext, ComputeShader, int> EditorBindResolveProbe;
        internal static int EditorAtrousIterationOverride = -1;
        internal static System.Action<ComputePassContext, int, Texture, Texture, Texture, Texture> EditorAtrousDispatchCapture;
#endif
        private const int ProbeSpacing = 16; // Keep in sync with SdfScreenProbeCommon.hlsl.
        private const int RestirPixelSpacing = 2;
        private const int RaysPerProbe = 8;
        private const int DirectionalProbePlanes = 4; // RGB baseline and three world-space tangent moments.
        private const int DirectionalIrradiancePlanes = 7;
        // Normal SH, pre-calibration SH, unfiltered SH, and raw anchor (7 + 7 + 7 + 1).
        private const int DirectionalAnchorDiagnosticPlanes = 22;
        private static readonly int ProbeMovingOutputId = Shader.PropertyToID("_SdfProbeMovingOutput");
        private static readonly int ProbeMovingId = Shader.PropertyToID("_SdfProbeMoving");
        private static readonly int SpatialFilterScalarNoiseId = Shader.PropertyToID("_SdfSpatialFilterScalarNoise");
        private static readonly int SpatialFilterVectorNoiseId = Shader.PropertyToID("_SdfSpatialFilterVectorNoise");
        private static readonly int SpatialFilterPassIndexId = Shader.PropertyToID("_SdfSpatialFilterPassIndex");
        private static readonly int SpatialFilterWideModeId = Shader.PropertyToID("_SdfSpatialFilterWideMode");
        private static readonly int ResolvedMovingOutputId = Shader.PropertyToID("_SdfResolvedMovingOutput");
        private static readonly int ResolvedMovingId = Shader.PropertyToID("_SdfResolvedMoving");
        private static readonly int MotionFrameId = Shader.PropertyToID("_SdfMotionFrame");
        private static readonly int FullSphereId = Shader.PropertyToID("_SdfScreenProbeFullSphere");
        private static readonly int ProbeMaxIntensityId = Shader.PropertyToID("_SdfProbeMaxRayIntensity");
        private const int DirectionalGatherBins = 16; // 4x4 cosine-octahedral chart.
        private const int DirectionalTracePlanes = 3; // Radiance, ray metadata, geometric hit plane.
        private const int GroupSize = 8;
        private const GraphicsFormat ProbeFormat = GraphicsFormat.R16G16B16A16_SFloat;
        private const GraphicsFormat GeometryFormat = GraphicsFormat.R32G32B32A32_SFloat;
        private const GraphicsFormat PixelGeometryFormat = GraphicsFormat.R32_UInt;
        private const int RadiosityCardSize = 8; // One compute group per card.
        private const int RadiosityFeedbackRays = 16;
        private const int RadiosityColumns = 16;
        private const int RadiosityCardLimit = 4096;
        // Bound per-camera storage while covering the full work list of 64x64 cards.
        private const int FinalLightingTexelLimit = RadiosityCardLimit * 64 * 64;
        private const int DirectLightingUpdateFactor = 32;
        // Keep the grid and directional resolution in sync with SdfRadianceCacheCommon.hlsl.
        private const int FarGridSize = 16;
        private const int FarDirectionCount = 64;
        private const int FarProbeCount = FarGridSize * FarGridSize * FarGridSize;
        private const int FarProbesPerFrame = 64;
        private const float FarCellSize = 2.5f;
        private static readonly CameraHistoryId ProbeHistoryId = CameraHistoryId.Create("SdfDiffuseProbeHistory");
        private static readonly CameraHistoryId AnchorStatisticsHistoryId = CameraHistoryId.Create("SdfProbeAnchorStatistics");
        private static readonly CameraHistoryId GeometryHistoryId = CameraHistoryId.Create("SdfDiffuseGeometryHistory");
        private static readonly CameraHistoryId DirectionalHistoryId = CameraHistoryId.Create("SdfDirectionalCompositedRadianceHistory");
        private static readonly CameraHistoryId DirectionalGuideHistoryId = CameraHistoryId.Create("SdfDirectionalWeightedFilteredHistoryV2");
        private static readonly CameraHistoryId ProposalHistoryId = CameraHistoryId.Create("SdfDirectionalSamplingProposalHistory");
        private static readonly CameraHistoryId RestirHistoryId = CameraHistoryId.Create("SdfDiffuseRestirHistory");
        private static readonly CameraHistoryId PixelHistoryId = CameraHistoryId.Create("SdfDiffusePixelHistoryWithCoverage");
        private static readonly CameraHistoryId CoveredPixelHistoryId = CameraHistoryId.Create("SdfScreenProbeCoveredPixelHistory");
        private static readonly CameraHistoryId FastUpdateHistoryId = CameraHistoryId.Create("SdfScreenProbeFastUpdateAndFrames");
        private static readonly int PreviousFastUpdateId = Shader.PropertyToID("_SdfPreviousFastUpdate");
        private static readonly int ProbeDirectionNoiseId = Shader.PropertyToID("_SdfProbeDirectionNoise");
        private static readonly int HasProbeDirectionNoiseId = Shader.PropertyToID("_SdfHasProbeDirectionNoise");
        private static readonly int ScatterNoiseId = Shader.PropertyToID("_SdfScatterNoise");
        private static readonly int HasScatterNoiseId = Shader.PropertyToID("_SdfHasScatterNoise");
        private static readonly int DitherTraceDirectionsId = Shader.PropertyToID("_SdfDitherTraceDirections");
        private static readonly int FastUpdateOutputId = Shader.PropertyToID("_SdfFastUpdateOutput");
        private static readonly int TemporalScalarNoiseId = Shader.PropertyToID("_SdfTemporalScalarNoise");
        private static readonly int TemporalScalarNoiseEnabledId = Shader.PropertyToID("_SdfTemporalScalarNoiseEnabled");
        private static readonly int ProbePreviousCameraForwardId = Shader.PropertyToID("_SdfProbePreviousCameraForward");
        private static readonly int HistorySubPixelGridSizeAndInvSizeId = Shader.PropertyToID("_SdfHistorySubPixelGridSizeAndInvSize");
        private static readonly int HistoryGatherUvMinMaxId = Shader.PropertyToID("_SdfHistoryGatherUvMinMax");
        private static readonly int HistoryCoordShiftXId = Shader.PropertyToID("_SdfHistoryCoordShiftX");
        private static readonly int HistoryCoordShiftYId = Shader.PropertyToID("_SdfHistoryCoordShiftY");
        private static readonly int HistoryTemporalParamsId = Shader.PropertyToID("_SdfHistoryTemporalParams");
        private static readonly int FastUpdateNeighborhoodClampId = Shader.PropertyToID("_SdfFastUpdateNeighborhoodClamp");
        private static readonly int TemporalReprojectionFlagsId = Shader.PropertyToID("_SdfTemporalReprojectionFlags");
        private static readonly int ProbeCameraPositionId = Shader.PropertyToID("_SdfProbeCameraPosition");
        private static readonly int ProbePreviousCameraPositionId = Shader.PropertyToID("_SdfProbePreviousCameraPosition");
        private static readonly int PreviousPixelDepthId = Shader.PropertyToID("_SdfDiffusePreviousPixelDepth");
        private static readonly int PixelDepthOutputId = Shader.PropertyToID("_SdfDiffusePixelDepthOutput");
        private static readonly int PreviousPixelDepthValidId = Shader.PropertyToID("_SdfPreviousPixelDepthValid");
        private static readonly int PreviousPreExposureId = Shader.PropertyToID("_SdfPreviousPreExposure");
        private static readonly int CurrentPreExposureOutputId = Shader.PropertyToID("_SdfCurrentPreExposureOutput");
        private static readonly int PreExposureHistoryEnabledId = Shader.PropertyToID("_SdfPreExposureHistoryEnabled");
        private static readonly int PreviousPreExposureValidId = Shader.PropertyToID("_SdfPreviousPreExposureValid");
        private static readonly int DisocclusionSpatialFilterId = Shader.PropertyToID("_SdfDisocclusionSpatialFilter");
        private static readonly int WriteRoughSpecularId = Shader.PropertyToID("_SdfWriteRoughSpecular");
        private static readonly int RoughSpecularOutputId = Shader.PropertyToID("_SdfRoughSpecularOutput");
        private static readonly int CurrentRoughSpecularId = Shader.PropertyToID("_SdfCurrentRoughSpecular");
        private static readonly int PreviousRoughSpecularId = Shader.PropertyToID("_SdfPreviousRoughSpecular");
        private static readonly int RoughSpecularHistoryOutputId = Shader.PropertyToID("_SdfRoughSpecularHistoryOutput");
        private static readonly int RoughSpecularEnabledId = Shader.PropertyToID("_SdfRoughSpecularEnabled");
        private static readonly int PreviousRoughSpecularValidId = Shader.PropertyToID("_SdfPreviousRoughSpecularValid");
        private static readonly CameraHistoryId ScreenProbeMomentsHistoryId = CameraHistoryId.Create("SdfScreenProbePixelMoments");
        private static readonly CameraHistoryId PixelGeometryHistoryId = CameraHistoryId.Create("SdfDiffusePixelGeometryHistory");
        private static readonly CameraHistoryId PixelDepthHistoryId = CameraHistoryId.Create("SdfDiffusePixelDepthHistory");
        private static readonly CameraHistoryId PixelPreExposureHistoryId = CameraHistoryId.Create("SdfScreenProbePreExposure");
        private static readonly CameraHistoryId RoughSpecularHistoryId = CameraHistoryId.Create("SdfScreenProbeRoughSpecular");
        private static readonly CameraHistoryId BentNormalHistoryId = CameraHistoryId.Create("SdfShortRangeBentNormal");
        private static readonly CameraHistoryId RestirVarianceHistoryId = CameraHistoryId.Create("SdfRestirPixelVarianceHistory");
        private static readonly CameraHistoryId ScreenRadianceHistoryId = CameraHistoryId.Create("SdfDiffuseScreenLambertRadianceHistory");
        private static readonly CameraHistoryId ScreenGeometryHistoryId = CameraHistoryId.Create("SdfDiffuseScreenGeometryHistory");
        private static readonly ProfilingSampler ScreenTraceSampler = new("Diffuse GI Screen Trace");
        private static readonly ProfilingSampler GatherSampler = new("Diffuse GI Gather and SDF Fallback");
        private static readonly ProfilingSampler TotalSampler = new("Diffuse GI Total");
        private static readonly ProfilingSampler PlacementSampler = new("Screen Probe Adaptive Placement");
        private static readonly ProfilingSampler FilterSampler = new("Screen Probe Spatial Filter");
        private static readonly ProfilingSampler DirectionalGuideSampler = new("Screen Probe Directional Guide");
        private static readonly ProfilingSampler DirectionalCompositeSampler = new("Screen Probe Composite Traces");
        private static readonly ProfilingSampler DirectionalTemporalSampler = new("Screen Probe Temporal Trace Radiance");
        private static readonly ProfilingSampler DirectionalFilterSampler = new("Screen Probe Filter Gather Traces");
        private static readonly ProfilingSampler DirectionalIrradianceSampler = new("Screen Probe Convert to Irradiance");
        private static readonly ProfilingSampler AnchorPreFilterSampler = new("Screen Probe Anchor Prefilter");
        private static readonly int DirectionalCacheEnabledId = Shader.PropertyToID("_SdfDirectionalCacheEnabled");
        private static readonly int DirectionalGuideEnabledId = Shader.PropertyToID("_SdfDirectionalGuideEnabled");
        private static readonly int DirectionalWorldGuideId = Shader.PropertyToID("_SdfDirectionalWorldGuide");
        private static readonly int DirectionalHasHistoryId = Shader.PropertyToID("_SdfDirectionalHasHistory");
        private static readonly int DirectionalGuideId = Shader.PropertyToID("_SdfDirectionalGuide");
        private static readonly int DirectionalGuideOutputId = Shader.PropertyToID("_SdfDirectionalGuideOutput");
        private static readonly int ProposalHistoryTextureId = Shader.PropertyToID("_SdfDirectionalProposalHistory");
        private static readonly int HasProposalHistoryId = Shader.PropertyToID("_SdfDirectionalHasProposalHistory");
        private static readonly int DirectionalRaysId = Shader.PropertyToID("_SdfDirectionalRays");
        private static readonly int DirectionalPreviousId = Shader.PropertyToID("_SdfDirectionalPrevious");
        private static readonly int DirectionalHasMotionId = Shader.PropertyToID("_SdfDirectionalHasMotion");
        private static readonly int DirectionalCompositeId = Shader.PropertyToID("_SdfDirectionalComposite");
        private static readonly int DirectionalCompositeOutputId = Shader.PropertyToID("_SdfDirectionalCompositeOutput");
        private static readonly int DirectionalTemporalId = Shader.PropertyToID("_SdfDirectionalTemporal");
        private static readonly int DirectionalTemporalOutputId = Shader.PropertyToID("_SdfDirectionalTemporalOutput");
        private static readonly int DirectionalSpatialInputId = Shader.PropertyToID("_SdfDirectionalSpatialInput");
        private static readonly int DirectionalFilteredId = Shader.PropertyToID("_SdfDirectionalFiltered");
        private static readonly int DirectionalFilteredOutputId = Shader.PropertyToID("_SdfDirectionalFilteredOutput");
        private static readonly ProfilingSampler RestirTemporalSampler = new("ReSTIR Gather Temporal");
        private static readonly ProfilingSampler RestirSpatialSampler = new("ReSTIR Gather Spatial");
        private static readonly ProfilingSampler RestirResolveSampler = new("ReSTIR Gather Resolve");
        private static readonly ProfilingSampler RestirPixelTemporalSampler = new("ReSTIR Pixel Temporal");
        private static readonly ProfilingSampler RestirDenoiseSampler = new("ReSTIR Variance Bilateral");
        private static readonly ProfilingSampler AtrousFineSampler = new("Diffuse GI Atrous Step 1");
        private static readonly ProfilingSampler AtrousMiddleSampler = new("Diffuse GI Atrous Step 2");
        private static readonly ProfilingSampler AtrousCoarseSampler = new("Diffuse GI Atrous Step 4");
        private static readonly ProfilingSampler AtrousWideSampler = new("Diffuse GI Atrous Step 8");
        private static readonly ProfilingSampler ResolveSampler = new("Screen Probe Resolve");
        private static readonly ProfilingSampler PixelTemporalSampler = new("Screen Probe Pixel Temporal");
        private static readonly int PixelHistoryMaxAgeId = Shader.PropertyToID("_SdfPixelHistoryMaxAge");
        private static readonly ProfilingSampler RadiositySampler = new("Surface Cache Radiosity");
        private static readonly ProfilingSampler RadiosityTraceSampler = new("Radiosity Trace Global SDF");
        private static readonly ProfilingSampler RadiosityResolveSampler = new("Radiosity Resolve Cards");
        private static readonly ProfilingSampler RadiosityIntegrateSampler = new("Radiosity Integrate Feedback");
        private static readonly ProfilingSampler RadiosityRescaleSampler = new("Surface Cache Light Rescale");
        private static readonly CameraHistoryId RadiosityAId = CameraHistoryId.Create("SdfRadiosityA");
        private static readonly CameraHistoryId RadiosityBId = CameraHistoryId.Create("SdfRadiosityB");
        private static readonly CameraHistoryId RadiosityPublishedId = CameraHistoryId.Create("SdfRadiosityAccumulatedPublished");
        private static readonly CameraHistoryId RadiosityFeedbackId = CameraHistoryId.Create("SdfRadiosityFeedback");
        private static readonly CameraHistoryId RadiosityFeedbackCountId = CameraHistoryId.Create("SdfRadiosityFeedbackCount");
        private static readonly int FeedbackCountId = Shader.PropertyToID("_SdfRadiosityFeedbackCount");
        private static readonly int FeedbackProbeId = Shader.PropertyToID("_SdfRadiosityFeedbackProbes");
        private static readonly int FeedbackProbeWriteId = Shader.PropertyToID("_SdfRadiosityFeedbackProbesWrite");
        private static readonly int FeedbackRayId = Shader.PropertyToID("_SdfRadiosityFeedbackRays");
        private static readonly int FeedbackRayWriteId = Shader.PropertyToID("_SdfRadiosityFeedbackRaysWrite");
        private static readonly int FeedbackRadianceId = Shader.PropertyToID("_SdfRadiosityFeedbackRadiance");
        private static readonly int FeedbackRadianceWriteId = Shader.PropertyToID("_SdfRadiosityFeedbackRadianceWrite");
        private static readonly int FeedbackDispatchWidthId = Shader.PropertyToID("_SdfRadiosityFeedbackDispatchWidth");
        private static readonly CameraHistoryId[] RadiosityOrderIds =
        {
            CameraHistoryId.Create("SdfRadiosityOrder1"), CameraHistoryId.Create("SdfRadiosityOrder2"),
            CameraHistoryId.Create("SdfRadiosityOrder3"), CameraHistoryId.Create("SdfRadiosityOrder4"),
            CameraHistoryId.Create("SdfRadiosityOrder5"), CameraHistoryId.Create("SdfRadiosityOrder6"),
            CameraHistoryId.Create("SdfRadiosityOrder7"), CameraHistoryId.Create("SdfRadiosityOrder8")
        };
        private static readonly int RadiosityWorkId = Shader.PropertyToID("_SdfRadiosityWork");
        private static readonly int RadiosityLayoutId = Shader.PropertyToID("_SdfRadiosityLayout");
        private static readonly int RadiosityFirstCardId = Shader.PropertyToID("_SdfRadiosityFirstCard");
        private static readonly int RadiositySamplesId = Shader.PropertyToID("_SdfRadiositySamples");
        private static readonly int RadiositySeedId = Shader.PropertyToID("_SdfRadiositySeed");
        private static readonly int RadiosityBatchId = Shader.PropertyToID("_SdfRadiosityBatch");
        private static readonly int RadiosityBounceId = Shader.PropertyToID("_SdfRadiosityBounce");
        private static readonly int RadiosityCardCountId = Shader.PropertyToID("_SdfRadiosityCardCount");
        private static readonly int RadiosityPublishBlendId = Shader.PropertyToID("_SdfRadiosityPublishBlend");
        private static readonly int RadiosityHistoryScaleId = Shader.PropertyToID("_SdfRadiosityHistoryScale");
        private static readonly int HistoryLightScaleId = Shader.PropertyToID("_SdfHistoryLightScale");
        private static readonly int SkyRadianceBoundId = Shader.PropertyToID("_SdfSkyRadianceBound");
        private static readonly int SkyRadianceBoundSizeId = Shader.PropertyToID("_SdfSkyRadianceBoundSize");
        private static readonly int RadiosityInputId = Shader.PropertyToID("_SdfRadiosityInput");
        private static readonly int RadiosityOutputId = Shader.PropertyToID("_SdfRadiosityOutput");
        private static readonly CameraHistoryId CardDirectLightingHistoryId = CameraHistoryId.Create("SdfCardDirectLighting");
        private static readonly CameraHistoryId CardFinalLightingHistoryId = CameraHistoryId.Create("SdfCardFinalLighting");
        private static readonly int FinalLightingAtlasId = Shader.PropertyToID("_SdfFinalLightingAtlas");
        private static readonly int FinalLightingLayoutId = Shader.PropertyToID("_SdfFinalLightingLayout");
        private static readonly int FinalLightingCardCountId = Shader.PropertyToID("_SdfFinalLightingCardCount");
        private static readonly int FinalLightingOrderId = Shader.PropertyToID("_SdfFinalLightingOrder");
        private static readonly int CardLightingPriorityOrderId = Shader.PropertyToID("_SdfCardLightingPriorityOrder");
        private static readonly int CardLightingFirstCardId = Shader.PropertyToID("_SdfCardLightingFirstCard");
        private static readonly int CardLightingOutputId = Shader.PropertyToID("_SdfCardLightingOutput");
        private static readonly int CardLightingHasDirectId = Shader.PropertyToID("_SdfCardLightingHasDirect");
        private static readonly int CardDirectLightingId = Shader.PropertyToID("_SdfCardDirectLighting");
        private static readonly ProfilingSampler CardDirectLightingSampler = new("Surface Cache Card Direct Lighting");
        private static readonly ProfilingSampler CardFinalLightingSampler = new("Surface Cache Combine Final Lighting");
        private static readonly CameraHistoryId FarHistoryId = CameraHistoryId.Create("SdfFarRadiance");
        private static readonly CameraHistoryId WorldRawRadianceHistoryId = CameraHistoryId.Create("SdfWorldRawRadiance");
        private static readonly int WorldWorkId = Shader.PropertyToID("_SdfWorldWork");
        private static readonly int WorldDispatchArgsId = Shader.PropertyToID("_SdfWorldDispatchArgs");
        private static readonly int WorldRawRadianceId = Shader.PropertyToID("_SdfWorldRawRadiance");
        [RenderGraphResource(Name = "World Probe Update Work", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_WorldWork = CreateScratch("World Probe Update Work", GraphicsFormat.R32_UInt);
        [RenderGraphResource(Name = "World Probe Dispatch Arguments", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_WorldDispatchArgs = new()
        {
            desc = new RenderGraphBufferDesc
            {
                Name = "World Probe Dispatch Arguments",
                Count = 3,
                Stride = sizeof(uint),
                Target = GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
            },
        };
        private const int CompactRaysPerProbe = 64;
        // A conservative lower bound on WorldNearDistance(0) in SdfRadianceCacheCommon.hlsl.
        private const float CompactWorldNearDistanceFloor = 3.6f;
        private static readonly int CompactProbesId = Shader.PropertyToID("_SdfCompactProbes");
        private static readonly int CompactContributionsId = Shader.PropertyToID("_SdfCompactContributions");
        private static readonly int CompactCountsId = Shader.PropertyToID("_SdfCompactCounts");
        private static readonly int CompactMeshRaysId = Shader.PropertyToID("_SdfCompactMeshRays");
        private static readonly int CompactGlobalRaysId = Shader.PropertyToID("_SdfCompactGlobalRays");
        private static readonly int CompactRetryRaysId = Shader.PropertyToID("_SdfCompactRetryRays");
        private static readonly int CompactMeshResultsId = Shader.PropertyToID("_SdfCompactMeshResults");
        private static readonly int CompactMeshResultsReadId = Shader.PropertyToID("_SdfCompactMeshResultsRead");
        private static readonly int CompactGlobalResultsId = Shader.PropertyToID("_SdfCompactGlobalResults");
        private static readonly int CompactGlobalResultsReadId = Shader.PropertyToID("_SdfCompactGlobalResultsRead");
        private static readonly int CompactInputId = Shader.PropertyToID("_SdfCompactInput");
        private static readonly int CompactArgsId = Shader.PropertyToID("_SdfCompactArgs");
        private static readonly int CompactQueueIndexId = Shader.PropertyToID("_SdfCompactQueueIndex");
        private static readonly int CompactRetryId = Shader.PropertyToID("_SdfCompactRetry");
        private static readonly int MeshCardGridId = Shader.PropertyToID("_MeshSdfCardGrid");
        private static readonly int MeshCardGridWriteId = Shader.PropertyToID("_MeshSdfCardGridWrite");
        private static readonly int MeshCardGridViewId = Shader.PropertyToID("_MeshSdfCardGridView");
        private static readonly int MeshCardGridProjectionId = Shader.PropertyToID("_MeshSdfCardGridProjection");
        private static readonly int MeshCardGridInverseViewId = Shader.PropertyToID("_MeshSdfCardGridInverseView");
        private static readonly int MeshCardGridInverseProjectionId = Shader.PropertyToID("_MeshSdfCardGridInverseProjection");
        private static readonly int MeshCardGridParametersId = Shader.PropertyToID("_MeshSdfCardGridParameters");
        private static readonly ProfilingSampler MeshCardGridSampler = new("Build Mesh SDF CardGrid");
        // Must match MeshSdfCardGridSize and MeshSdfCardGridCapacity in MeshSdfTrace.hlsl.
        private const int MeshCardGridX = 16, MeshCardGridY = 12, MeshCardGridZ = 32, MeshCardGridStride = 97;
        [RenderGraphResource(Name = "Mesh SDF CardGrid", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_MeshCardGrid = RenderGraphBuffer.CreateStructured("Mesh SDF CardGrid", sizeof(uint));
        private static readonly ProfilingSampler CompactScreenSampler = new("Compact Screen Probe Traces");
        private static readonly ProfilingSampler CompactMeshSampler = new("Trace Compacted Mesh SDF");
        private static readonly ProfilingSampler CompactMeshLightingSampler = new("Resolve Compacted Mesh SDF");
        private static readonly ProfilingSampler CompactGlobalSampler = new("Trace Compacted Global SDF");
        private static readonly ProfilingSampler CompactGlobalLightingSampler = new("Resolve Compacted Global SDF");
        private static readonly ProfilingSampler CompactFinalizeSampler = new("Finalize Probe Gather");
        [RenderGraphResource(Name = "Compact Probe Receivers", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_CompactProbes = RenderGraphBuffer.CreateStructured("Compact Probe Receivers", 80);
        [RenderGraphResource(Name = "Compact Probe Contributions", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_CompactContributions = RenderGraphBuffer.CreateStructured("Compact Probe Contributions", 24);
        [RenderGraphResource(Name = "Compact Mesh SDF Rays", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_CompactMeshRays = RenderGraphBuffer.CreateStructured("Compact Mesh SDF Rays", sizeof(uint));
        [RenderGraphResource(Name = "Compact Mesh SDF Results", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_CompactMeshResults = RenderGraphBuffer.CreateStructured("Compact Mesh SDF Results", 48);
        [RenderGraphResource(Name = "Compact Global SDF Rays", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_CompactGlobalRays = RenderGraphBuffer.CreateStructured("Compact Global SDF Rays", sizeof(uint));
        [RenderGraphResource(Name = "Compact Global SDF Results", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_CompactGlobalResults = RenderGraphBuffer.CreateStructured("Compact Global SDF Results", 24);
        [RenderGraphResource(Name = "Compact Retry Rays", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_CompactRetryRays = RenderGraphBuffer.CreateStructured("Compact Retry Rays", sizeof(uint));
        [RenderGraphResource(Name = "Compact Trace Counts", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_CompactCounts = RenderGraphBuffer.CreateStructured("Compact Trace Counts", 5, sizeof(uint));
        [RenderGraphResource(Name = "Compact Mesh Dispatch Arguments", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_CompactMeshArgs = CreateCompactDispatchArgs("Compact Mesh Dispatch Arguments");
        [RenderGraphResource(Name = "Compact Mesh Hit Dispatch Arguments", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_CompactMeshHitArgs = CreateCompactDispatchArgs("Compact Mesh Hit Dispatch Arguments");
        [RenderGraphResource(Name = "Compact Global Dispatch Arguments", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_CompactGlobalArgs = CreateCompactDispatchArgs("Compact Global Dispatch Arguments");
        [RenderGraphResource(Name = "Compact Retry Mesh Dispatch Arguments", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_CompactRetryMeshArgs = CreateCompactDispatchArgs("Compact Retry Mesh Dispatch Arguments");
        [RenderGraphResource(Name = "Compact Retry Global Dispatch Arguments", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_CompactRetryGlobalArgs = CreateCompactDispatchArgs("Compact Retry Global Dispatch Arguments");

        private static RenderGraphBuffer CreateCompactDispatchArgs(string name) => RenderGraphBuffer.CreateStructured(name, 3, sizeof(uint),
            GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments);
        private readonly int[] m_CompactScreenKernels = new int[2];
        private readonly int[] m_CompactScreenSkipEmptyKernels = new int[2];
        private readonly int[] m_CompactMeshKernels = new int[2];
        private readonly int[] m_CompactResolveGlobalKernels = new int[2];
        private readonly int[] m_FeedbackTraceKernels = new int[2];
        private readonly int[] m_FeedbackCompactResolveKernels = new int[2];
        private int m_CompactResolveMeshKernel = -1, m_FeedbackCompactResolveMeshKernel = -1;
        private int m_CompactFinalizeKernel = -1;
        private int m_CompactResetKernel = -1, m_CompactArgsKernel = -1;
        private int m_MeshCardGridKernel = -1;
        private int m_CompactGlobalKernel = -1;
        private bool m_CompactTraceSupported, m_CompactTracePrepared, m_CompactTraceMayRetryMesh;

        private const int WorldAtlasHeight = 2052 + 4 * 48 * 48 * 48 / 2048;
        private int m_WorldReuseKernel, m_WorldAllocateKernel, m_WorldTicketsKernel;
        private int m_WorldArgsKernel, m_WorldFilterKernel;
        private Vector4 m_FarWindowParameter;
        private int m_WorldClearKernel, m_WorldMarkKernel, m_WorldSelectKernel, m_WorldUpdateKernel, m_WorldSkyKernel;
        private int m_WorldGlobalUpdateKernel = -1, m_WorldGlobalSkyKernel = -1;
        private static readonly CameraHistoryId FarHitIdsHistoryId = CameraHistoryId.Create("SdfFarRadianceHitIds");
        private static readonly CameraHistoryBufferDescriptor FarHitIdsDescriptor = new(FarProbeCount * FarDirectionCount,
            sizeof(uint), GraphicsBuffer.Target.Structured);
        private static readonly ProfilingSampler FarCacheSampler = new("Diffuse GI Far Radiance Update");
        private static readonly ProfilingSampler NearOcclusionSampler = new("Diffuse GI Near Contact Occlusion");
        private static readonly int FarRadianceId = Shader.PropertyToID("_SdfFarRadiance");
        private static readonly int FarOutputId = Shader.PropertyToID("_SdfFarOutput");
        private static readonly int FarHitIdsId = Shader.PropertyToID("_SdfFarHitIds");
        private static readonly int FarHitIdsOutputId = Shader.PropertyToID("_SdfFarHitIdsOutput");
        private static readonly int MeshDetailDistanceId = Shader.PropertyToID("_SdfMeshDetailDistance");
        private static readonly int UseGlobalClipmapId = Shader.PropertyToID("_SdfUseGlobalClipmap");
        private static readonly int ObjectPagesId = Shader.PropertyToID("_SdfObjectPages");
        private static readonly int ObjectWorkId = Shader.PropertyToID("_SdfObjectPageWork");
        private static readonly int ObjectGridId = Shader.PropertyToID("_SdfObjectGrid");
        private static readonly int ObjectGridWriteId = Shader.PropertyToID("_SdfObjectGridWrite");
        private static readonly int ObjectCountId = Shader.PropertyToID("_SdfObjectPageCount");
        private int m_ObjectGridKernel;
        private BufferHandle m_ObjectGrid, m_ObjectPageTable, m_ObjectPageWork;
        private static readonly int GlobalNearId = Shader.PropertyToID("_GlobalSdfNear");
        private static readonly int GlobalMiddleId = Shader.PropertyToID("_GlobalSdfMiddle");
        private static readonly int GlobalFarId = Shader.PropertyToID("_GlobalSdfFar");
        private static readonly int GlobalMipId = Shader.PropertyToID("_GlobalSdfMip");
        private static readonly int GlobalUseMipId = Shader.PropertyToID("_GlobalSdfUseMipTrace");
        private static readonly int GlobalLevelsId = Shader.PropertyToID("_GlobalSdfLevels");
        private static readonly int GlobalScrollId = Shader.PropertyToID("_GlobalSdfScroll");
        private static readonly int GlobalPagesId = Shader.PropertyToID("_GlobalSdfPages");
        private static readonly int GlobalThinBricksId = Shader.PropertyToID("_GlobalSdfThinBricks");
        private static readonly int GlobalLayerMaskId = Shader.PropertyToID("_GlobalSdfLayerMask");
        private static readonly int FarUpdatesId = Shader.PropertyToID("_SdfFarUpdates");
        private static readonly int FarClearId = Shader.PropertyToID("_SdfFarClear");
        private static readonly int FarDistanceId = Shader.PropertyToID("_SdfFarTraceDistance");
        private static readonly int FarWindowId = Shader.PropertyToID("_SdfFarWindow");
        private static readonly int NearOcclusionId = Shader.PropertyToID("_SdfNearOcclusion");
        private static readonly int BentSamplesId = Shader.PropertyToID("_SdfBentSamples");
        private static readonly int BentSamplesOutputId = Shader.PropertyToID("_SdfBentSamplesOutput");
        private static readonly int BentFurthestHzbId = Shader.PropertyToID("_SdfBentFurthestHZB");
        private static readonly int BentFurthestHzbOutputId = Shader.PropertyToID("_SdfBentFurthestHZBOutput");
        private static readonly int BentBlueNoiseId = Shader.PropertyToID("_SdfBentBlueNoise");
        private static readonly int BentScalarNoiseId = Shader.PropertyToID("_SdfBentScalarNoise");
        private static readonly int BentFlipYId = Shader.PropertyToID("_SdfBentFlipY");
        private static readonly int BentHistoryValidId = Shader.PropertyToID("_SdfBentHistoryValid");
        private static readonly int BentWorldToViewId = Shader.PropertyToID("_SdfBentWorldToView");
        private static readonly int BentInverseProjectionId = Shader.PropertyToID("_SdfBentInverseProjection");
        private static readonly int LumenBentNormalId = Shader.PropertyToID("_SdfLumenBentNormal");
        private static readonly int LumenBentNormalOutputId = Shader.PropertyToID("_SdfLumenBentNormalOutput");
        private static readonly int BentPreviousId = Shader.PropertyToID("_SdfBentPrevious");
        private static readonly int BentOutputId = Shader.PropertyToID("_SdfBentOutput");
        private static readonly int BentWriteGeometryId = Shader.PropertyToID("_SdfBentWriteGeometry");
        private static readonly int HzbId = Shader.PropertyToID("_SdfScreenHzb");
        private static readonly int MotionId = Shader.PropertyToID("_SdfScreenMotion");
        private static readonly CameraHistoryId SceneColorHistoryId = CameraHistoryId.Create("SdfResolvedSceneColor");
        private static readonly CameraHistoryId SceneDepthExposureId = CameraHistoryId.Create("SdfSceneDepthExposure");
        private static readonly int LumenTraceId = Shader.PropertyToID("_SdfLumenScreenTrace");
        private static readonly int ClosestHzbId = Shader.PropertyToID("_SdfClosestHzb");
        private static readonly int ClosestHzbOutputId = Shader.PropertyToID("_SdfClosestHzbOutput");
        private static readonly int ClosestHzbSizeId = Shader.PropertyToID("_SdfClosestHzbSize");
        private static readonly int HzbStartMipId = Shader.PropertyToID("_SdfHzbStartMip");
        private static readonly int ClipToPrevClipId = Shader.PropertyToID("_SdfClipToPrevClip");
        private static readonly int DepthThicknessScaleId = Shader.PropertyToID("_SdfDepthThicknessScale");
        private static readonly int ScreenRadianceId = Shader.PropertyToID("_SdfScreenPreviousRadiance");
        private static readonly int ScreenRadianceValidId = Shader.PropertyToID("_SdfScreenPreviousRadianceValid");
        private static readonly int ScreenGeometryId = Shader.PropertyToID("_SdfScreenPreviousGeometry");
        private static readonly int PreviousInverseVpId = Shader.PropertyToID("_SdfScreenPreviousInverseVP");
        private static readonly int JitterDeltaId = Shader.PropertyToID("_SdfScreenJitterDelta");
        private static readonly int ScreenRaysId = Shader.PropertyToID("_SdfScreenRays");
        private static readonly int ScreenRaysOutputId = Shader.PropertyToID("_SdfScreenRaysOutput");
        private static readonly int UseScreenRaysId = Shader.PropertyToID("_SdfDiffuseUseScreenRays");
        private static readonly int TraceSourcesId = Shader.PropertyToID("_SdfDiffuseTraceSources");
        private static readonly int RayDiagnosticsId = Shader.PropertyToID("_SdfDiffuseRayDiagnostics");
        private static readonly int FirstHitDiagnosticsId = Shader.PropertyToID("_SdfDiffuseFirstHitDiagnostics");
        private static readonly int ReceiverVisibilityId = Shader.PropertyToID("_SdfReceiverVisibility");
        private static readonly int ReceiverEntitiesId = Shader.PropertyToID("_SdfReceiverEntities");
        private static readonly int ReceiverCountId = Shader.PropertyToID("_SdfReceiverCount");
        private static readonly int CorrectReceiverId = Shader.PropertyToID("_SdfCorrectReceiverSelfHits");
        private static readonly int ScreenCardLightingId = Shader.PropertyToID("_SdfScreenCardLighting");
        private static readonly int ScreenMaterialLookupId = Shader.PropertyToID("_SdfScreenMaterialLookup");
        private static readonly int RecordRayDiagnosticsId = Shader.PropertyToID("_SdfDiffuseRecordRayDiagnostics");
        private static readonly int RecordFirstHitDiagnosticsId = Shader.PropertyToID("_SdfDiffuseRecordFirstHitDiagnostics");
        private static readonly int CurrentResolvedId = Shader.PropertyToID("_SdfDiffuseCurrentResolved");
        private static readonly int PreviousPixelLightingId = Shader.PropertyToID("_SdfDiffusePreviousPixelLighting");
        private static readonly int PreviousPixelGeometryId = Shader.PropertyToID("_SdfDiffusePreviousPixelGeometry");
        private static readonly int PixelLightingOutputId = Shader.PropertyToID("_SdfDiffusePixelLightingOutput");
        private static readonly int PixelGeometryOutputId = Shader.PropertyToID("_SdfDiffusePixelGeometryOutput");
        private static readonly int DepthId = Shader.PropertyToID("_SdfDiffuseDepth");
        private static readonly int GBuffer0Id = Shader.PropertyToID("_SdfDiffuseGBuffer0");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_SdfDiffuseGBuffer1");
        private static readonly int SurfaceId = Shader.PropertyToID("_SdfDiffuseSurfaces");
        private static readonly int ProbeId = Shader.PropertyToID("_SdfDiffuseProbes");
        private static readonly int GeometryId = Shader.PropertyToID("_SdfDiffuseGeometry");
        private static readonly int PreviousProbeId = Shader.PropertyToID("_SdfDiffusePreviousProbes");
        private static readonly int PreviousGeometryId = Shader.PropertyToID("_SdfDiffusePreviousGeometry");
        private static readonly int HitLightingId = Shader.PropertyToID("_SdfDiffuseHitLighting");
        private static readonly int OutputId = Shader.PropertyToID("_SdfDiffuseOutput");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_SdfDiffuseScreenSize");
        private static readonly int InverseViewProjectionId = Shader.PropertyToID("_SdfDiffuseInverseVP");
        private static readonly int PreviousViewProjectionId = Shader.PropertyToID("_SdfDiffusePreviousVP");
        private static readonly int LightPositionRangeId = Shader.PropertyToID("_SdfDiffuseLightPositionRange");
        private static readonly int LightColorShadowId = Shader.PropertyToID("_SdfDiffuseLightColorShadow");
        private static readonly int LightLayerMaskId = Shader.PropertyToID("_SdfDiffuseLightLayerMask");
        private static readonly int MaxDistanceId = Shader.PropertyToID("_SdfDiffuseMaxDistance");
        private static readonly int SkyTraceDistanceId = Shader.PropertyToID("_SdfDiffuseSkyTraceDistance");
        private static readonly int SkyTextureId = Shader.PropertyToID("_SkyTexture");
        private static readonly int SkyTextureParamsId = Shader.PropertyToID("_SkyTextureParams");
        private static readonly int SkyTextureTintId = Shader.PropertyToID("_SkyTextureTint");
        private static readonly int RayBiasId = Shader.PropertyToID("_SdfDiffuseRayBias");
        private static readonly int LayerMaskId = Shader.PropertyToID("_SdfDiffuseLayerMask");
        private static readonly int FrameId = Shader.PropertyToID("_SdfDiffuseFrame");
        private static readonly int ProbePreviousFrameId = Shader.PropertyToID("_SdfProbePreviousFrame");
        private static readonly int HasHistoryId = Shader.PropertyToID("_SdfDiffuseHasHistory");
        private static readonly int DirectionalProbesId = Shader.PropertyToID("_SdfDiffuseDirectionalProbes");
        private static readonly int AdaptiveProbesId = Shader.PropertyToID("_SdfDiffuseAdaptiveProbes");
        private static readonly int ProbeResolveRepairModeId = Shader.PropertyToID("_SdfProbeResolveRepairMode");
        private static readonly int IntegrationJitterId = Shader.PropertyToID("_SdfScreenProbeIntegrationJitter");
        private static readonly int StochasticInterpolationId = Shader.PropertyToID("_SdfScreenProbeStochasticInterpolation");
        private static readonly int InterpolationScalarNoiseId = Shader.PropertyToID("_SdfInterpolationScalarNoise");
        private static readonly int InterpolationVectorNoiseId = Shader.PropertyToID("_SdfInterpolationVectorNoise");
        private static readonly int InterpolationFlipYId = Shader.PropertyToID("_SdfInterpolationFlipY");
        private static readonly int SpatialFilterId = Shader.PropertyToID("_SdfDiffuseSpatialFilter");
        private static readonly int ProbeSpatialKernelId = Shader.PropertyToID("_SdfProbeSpatialKernel");
        private static readonly int ProbeHistoryBlendId = Shader.PropertyToID("_SdfProbeHistoryBlend");
        private static readonly int ProbeCameraForwardId = Shader.PropertyToID("_SdfProbeCameraForward");
        private static readonly int PlacementId = Shader.PropertyToID("_SdfAdaptivePlacement");
        private static readonly int PlacementOutputId = Shader.PropertyToID("_SdfAdaptivePlacementOutput");
        private static readonly int ProbeInputId = Shader.PropertyToID("_SdfDiffuseProbeInput");
        private static readonly int FilteredProbesId = Shader.PropertyToID("_SdfDiffuseFilteredProbes");
        private static readonly int ProbeAnchorDiagnosticsId = Shader.PropertyToID("_SdfProbeAnchorDiagnostics");
        private static readonly int GatherHistoryDiagnosticsId = Shader.PropertyToID("_SdfGatherHistoryDiagnostics");
        private const int FullPrecisionAnchorFlag = 32;
        private static readonly int AnchorHistoryBlendId = Shader.PropertyToID("_SdfProbeAnchorHistoryBlend");
        private static readonly int ProbeAnchorMaxAgeId = Shader.PropertyToID("_SdfProbeAnchorMaxAge");
        private static readonly int SpatialAnchorId = Shader.PropertyToID("_SdfSpatialProbeAnchor");
        private static readonly int SpatialAnchorInputId = Shader.PropertyToID("_SdfSpatialProbeAnchorInput");
        private static readonly int SpatialAnchorOutputId = Shader.PropertyToID("_SdfSpatialProbeAnchorOutput");
        private static readonly int SpatialAnchorPrefilteredId = Shader.PropertyToID("_SdfSpatialProbeAnchorPrefiltered");
        private static readonly int PreparedAnchorId = Shader.PropertyToID("_SdfPreparedProbeAnchor");
        private static readonly int PreparedAnchorOutputId = Shader.PropertyToID("_SdfPreparedProbeAnchorOutput");
        private static readonly int PreviousAnchorStatisticsId = Shader.PropertyToID("_SdfPreviousAnchorStatistics");
        private static readonly int AnchorStatisticsOutputId = Shader.PropertyToID("_SdfAnchorStatisticsOutput");
        private static readonly int AnchorStatisticsId = Shader.PropertyToID("_SdfAnchorStatistics");
        private static readonly int AnchorStatisticsValidId = Shader.PropertyToID("_SdfAnchorStatisticsHistoryValid");
        private static readonly int PreparedAnchorEnabledId = Shader.PropertyToID("_SdfPreparedAnchorEnabled");
        private static readonly int HitFilterEnabledId = Shader.PropertyToID("_SdfDiffuseHitFilterEnabled");
        private static readonly int HitSamplesId = Shader.PropertyToID("_SdfDiffuseHitSamples");
        private static readonly int HitSamplesInputId = Shader.PropertyToID("_SdfDiffuseHitSamplesInput");
        private static readonly int RestirEnabledId = Shader.PropertyToID("_SdfRestirEnabled");
        private static readonly int RestirHasHistoryId = Shader.PropertyToID("_SdfRestirHasHistory");
        private static readonly int RestirPreviousId = Shader.PropertyToID("_SdfRestirPrevious");
        private static readonly int RestirTemporalId = Shader.PropertyToID("_SdfRestirTemporal");
        private static readonly int RestirOutputId = Shader.PropertyToID("_SdfRestirOutput");
        private static readonly int RestirDenseId = Shader.PropertyToID("_SdfRestirDense");
        private static readonly int RestirOpenPbrDiffuseId = Shader.PropertyToID("_SdfRestirOpenPbrDiffuse");
        private static readonly int OpenPbrParametersId = Shader.PropertyToID("_SdfOpenPbrParameters");
        private static readonly int OpenPbrAux0Id = Shader.PropertyToID("_SdfOpenPbrAux0");
        private static readonly int OpenPbrAux1Id = Shader.PropertyToID("_SdfOpenPbrAux1");
        private static readonly int RestirSpatialId = Shader.PropertyToID("_SdfRestirSpatial");
        private static readonly int RestirVarianceId = Shader.PropertyToID("_SdfRestirVariance");
        private static readonly int RestirPreviousVarianceId = Shader.PropertyToID("_SdfRestirPreviousVariance");
        private static readonly int RestirVarianceHistoryOutputId = Shader.PropertyToID("_SdfRestirVarianceHistoryOutput");
        private static readonly int RestirFilterHistoryModeId = Shader.PropertyToID("_SdfRestirFilterHistoryMode");
        private static readonly int RestirVarianceOutputId = Shader.PropertyToID("_SdfRestirVarianceOutput");
        private static readonly int RestirFilterInputId = Shader.PropertyToID("_SdfRestirFilterInput");
        private static readonly int AtrousInputId = Shader.PropertyToID("_SdfAtrousInput");
        private static readonly int AtrousVarianceId = Shader.PropertyToID("_SdfAtrousVariance");
        private static readonly int AtrousVarianceOutputId = Shader.PropertyToID("_SdfAtrousVarianceOutput");
        private static readonly int AtrousStepId = Shader.PropertyToID("_SdfAtrousStep");
        private static readonly int AtrousUseRestirDomainId = Shader.PropertyToID("_SdfAtrousUseRestirDomain");
        private static readonly int AtrousHasVarianceId = Shader.PropertyToID("_SdfAtrousHasVariance");
        private static readonly int ScreenProbePreviousMomentsId = Shader.PropertyToID("_SdfScreenProbePreviousMoments");
        private static readonly int ScreenProbeMomentsOutputId = Shader.PropertyToID("_SdfScreenProbeMomentsOutput");
        private static readonly int ScreenProbeMomentsValidId = Shader.PropertyToID("_SdfScreenProbeMomentsHistoryValid");
        private static readonly int AtrousScreenProbeMomentsId = Shader.PropertyToID("_SdfAtrousScreenProbeMoments");
        private static readonly int AtrousUseScreenProbeMomentsId = Shader.PropertyToID("_SdfAtrousUseScreenProbeMoments");
        private static readonly int OffscreenOnlyId = Shader.PropertyToID("_SdfDiffuseOffscreenOnly");
        private static readonly int ViewProjectionId = Shader.PropertyToID("_SdfDiffuseVP");
        private static readonly int CardAlbedoDepthId = Shader.PropertyToID("_SdfSurfaceAlbedoDepth");
        private static readonly int CardNormalCoverageId = Shader.PropertyToID("_SdfSurfaceNormalCoverage");
        private static readonly int LocalCardTableId = Shader.PropertyToID("_SdfLocalCardTable");

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Depth = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);

        [RenderGraphResource(Name = "GBuffer0", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer0 = RenderGraphTexture.CreateInput("GBuffer0", GraphicsFormat.R8G8B8A8_SRGB);

        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1 = RenderGraphTexture.CreateInput("GBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);

        [RenderGraphResource(Name = "OpenPBR Parameters (RT4)", Access = AccessFlags.Read)]
        private RenderGraphTexture m_OpenPbrParameters;

        [RenderGraphResource(Name = "OpenPBR LayerAux0", Access = AccessFlags.Read)]
        private RenderGraphTexture m_OpenPbrAux0;

        [RenderGraphResource(Name = "OpenPBR LayerAux1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_OpenPbrAux1;

        [RenderGraphResource(Name = "Screen Trace HZB", Access = AccessFlags.Read)]
        private RenderGraphTexture m_ScreenHzb;

        [RenderGraphResource(Name = "Screen Trace Motion", Access = AccessFlags.Read)]
        private RenderGraphTexture m_ScreenMotion;

        [RenderGraphResource(Name = "Global SDF Near", Access = AccessFlags.Read)]
        private RenderGraphTexture m_ClipmapNear;

        [RenderGraphResource(Name = "Global SDF Fallback", Access = AccessFlags.Read)]
        [TransientResource]
        private readonly RenderGraphTexture m_GlobalFallback = CreateGlobalFallback();

        [RenderGraphResource(Name = "RealtimeDiffuseGI", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_Output = CreateOutput("Realtime Diffuse GI", ProbeFormat);

        [RenderGraphResource(Name = "Probe Hit Lighting", Access = AccessFlags.Write)]
        private RenderGraphTexture m_HitLighting = CreateOutput("SDF Diffuse Probe Hit Lighting", ProbeFormat);

        [RenderGraphResource(Name = "GI Trace Sources (Screen / SDF / Unresolved)", Access = AccessFlags.Write)]
        private RenderGraphTexture m_TraceSources = CreateOutput("Diffuse GI Trace Sources", ProbeFormat);


        [SerializeField, Min(0.1f)] private float m_MaxDistance = 12f;
        [SerializeField, Min(0.001f)] private float m_RayBias = 0.04f;
        [SerializeField, Min(1f)] private float m_SunShadowDistance = 64f;

        private sealed class HistoryState : CameraRelativeState
        {
            internal Matrix4x4 ViewProjection;
            internal VividSdfSurfaceCache Cache;
            internal bool OffscreenOnly;
            internal SdfDiffuseTraceMode TraceMode;
            internal bool HasScreenRays, LumenScreenTrace;
            internal bool DirectionalProbes;
            internal bool AdaptiveProbes, SpatialFilter, HitDistanceFilter;
            internal SdfScreenProbeSpatialKernel ProbeSpatialKernel;
            internal bool RestirGather;
            internal bool RestirPixelIntegration;
            internal bool RestirOpenPbrDiffuse;
            internal ulong RestirCardVersion;
            internal bool PixelTemporal, StochasticInterpolation;
            internal bool FastUpdateNeighborhoodClamp;
            internal int PixelHistoryMaxAge, ProbeAnchorMaxAge;
            internal Vector4 ProbeCameraForward;
            internal Vector4 ProbeCameraPosition;
            internal int DynamicLightingFastUpdateFrames;
            internal bool CorrectReceiverSelfHits;
            internal bool ScreenCardLighting;
            internal bool FullSphere, ProbeTemporal, DitherTraceDirections, RoughSpecular;
            internal float MaxRayIntensity;
            internal bool DirectionalCache, DirectionalGuide, DirectionalHierarchicalGuide, SpatialAnchor, AnchorPreFilter;
            internal float MeshDetailDistance;
            internal bool GlobalClipmapUsed;
            internal Vector4 NearOcclusion;
            internal Vector2 ScreenJitterUv;
            internal Vector4 LightPositionRange, LightColorShadow;
            internal uint SceneRevision;
            internal uint SceneTransportVersion;
            internal uint SceneNonMotionVersion;
            internal int Width, Height, LayerMask, LightLayerMask;
            internal float MaxDistance, RayBias;
            internal float SkyTraceDistance;
            internal int SkyContentHash;
            internal Vector4 SkyTextureParams, SkyTextureTint;
            internal uint Frame;
            internal int ProbeFrame;
            internal ulong RadiosityCardVersion;
            internal ulong FinalLightingCardVersion;
            internal bool FinalLightingHasIndirect;
            internal int FinalLightingInputOrder, FinalLightingReadyCards;
            internal int FinalLightingUpdateCursor;
            internal (uint Revision, int LayerMask, float Bias, Vector4 Light, Vector4 Color) FinalLightingDirectInputs;
            internal CardLightingPriority FinalLightingPriority;
            internal int RadiosityTargetOrders, RadiosityOrder, RadiosityCursor, GatherRadiosityOrder;
            internal int RadiositySamples, RadiositySeed;
            internal bool RadiosityAccumulate;
            internal int RadiosityBatchLimit, RadiosityBatch, RadiosityBounce;
            internal int RadiosityPublishedOrder, RadiosityBoostFrames;
            internal bool RadiosityProgressive, RadiosityRestarting;
            internal bool RadiosityFeedback;
            internal uint RadiosityLayoutVersion;
            internal uint RadiosityInputSceneRevision;
            internal uint RadiosityInputDiscontinuityVersion;
            internal uint RadiosityInputTransportVersion, RadiosityEmissionVersion;
            internal bool RadiosityFastRelight;
            internal Vector4 RadiosityLightPositionRange, RadiosityLightColorShadow;
            internal Vector4 RadiositySkyTextureParams, RadiositySkyTextureTint;
            internal int RadiositySkyContentHash;
            internal float RadiosityTransitionBlend;
            internal Vector3 RadiosityCameraPosition, RadiosityCameraForward;
            internal Vector3Int[] FarKeys;
            internal Vector3Int FarWindow;
            internal bool FarPending;
            internal float FarDistance;
            internal ulong FarCardVersion;
            internal uint FarSceneVersion;

            internal VividGlobalSdfObjectGrid ObjectGrid;
            public override void Dispose()
            {
                ObjectGrid?.Dispose();
                FinalLightingPriority?.Dispose();
            }
        }

        private sealed class CardLightingPriority : IDisposable
        {
            private struct Entry : IComparable<Entry>
            {
                internal int Card;
                internal bool InFrustum;
                internal float DistanceSquared;

                public readonly int CompareTo(Entry other)
                {
                    int order = other.InFrustum.CompareTo(InFrustum);
                    if (order == 0)
                        order = DistanceSquared.CompareTo(other.DistanceSquared);
                    return order != 0 ? order : Card.CompareTo(other.Card);
                }
            }

            private readonly Plane[] m_Planes = new Plane[6];
            private Entry[] m_Entries;
            private uint[] m_Order;
            internal GraphicsBuffer Buffer { get; private set; }

            internal void Update(Camera camera, VividMeshSdfGpuScene scene, int count)
            {
                if (m_Entries == null || m_Entries.Length < count)
                {
                    int capacity = Mathf.NextPowerOfTwo(count);
                    m_Entries = new Entry[capacity];
                    m_Order = new uint[2 * capacity];
                    Buffer?.Dispose();
                    Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_Order.Length, sizeof(uint))
                        { name = "Surface Cache Lighting Priority" };
                }

                GeometryUtility.CalculateFrustumPlanes(camera, m_Planes);
                Vector3 position = camera.transform.position;
                for (int card = 0; card < count; ++card)
                {
                    Bounds bounds = scene.GetLightingCardBounds(card);
                    m_Entries[card] = new Entry { Card = card,
                        InFrustum = GeometryUtility.TestPlanesAABB(m_Planes, bounds),
                        DistanceSquared = bounds.SqrDistance(position) };
                }
                Array.Sort(m_Entries, 0, count);
                for (int rank = 0; rank < count; ++rank)
                {
                    int card = m_Entries[rank].Card;
                    m_Order[rank] = (uint)card;
                    m_Order[count + card] = (uint)rank;
                }
                Buffer.SetData(m_Order, 0, 0, 2 * count);
            }

            public void Dispose() => Buffer?.Dispose();
        }

        private readonly CameraRelativeSystem<HistoryState> m_Cameras = new();
        private RenderGraphTexture m_ReceiverVisibilitySource;
        private TextureHandle m_ReceiverVisibility;
        private GraphicsBuffer m_ReceiverEntities;
        [StructLayout(LayoutKind.Sequential)]
        private struct ReceiverSource
        {
            public uint EntityLow, EntityHigh, SdfInstance, Padding;
        }

        private ReceiverSource[] m_ReceiverSources;
        private readonly Dictionary<ulong, uint> m_SdfInstancesByEntity = new();
        private uint m_ReceiverLookupRevision = uint.MaxValue;
        private bool m_CorrectReceiverSelfHits, m_ScreenCardLightingRequested;
        private int m_ReceiverCount;
        private int m_ReceiverTraceKernel = -1, m_ReceiverCachedKernel = -1;
        private readonly RenderGraphTexture m_PreviousProbes = RenderGraphTexture.CreateInput("Previous SDF Diffuse Probes", ProbeFormat);
        private readonly RenderGraphTexture m_PreviousDirectional = RenderGraphTexture.CreateInput("Previous Screen Probe Composited Radiance", ProbeFormat);
        private readonly RenderGraphTexture m_CurrentDirectional = CreateScratch("Screen Probe Composited Radiance", ProbeFormat);
        private readonly RenderGraphTexture m_PreviousProposal = RenderGraphTexture.CreateInput("Previous Screen Probe Sampling Proposal", ProbeFormat);
        private readonly RenderGraphTexture m_CurrentProposal = CreateScratch("Screen Probe Sampling Proposal", ProbeFormat);
        private readonly RenderGraphTexture m_PreviousDirectionalFiltered = RenderGraphTexture.CreateInput("Previous Screen Probe Filtered Radiance", ProbeFormat);
        private readonly RenderGraphTexture m_CurrentDirectionalFiltered = CreateScratch("Screen Probe Filtered Radiance History", ProbeFormat);
        [RenderGraphResource(Name = "Spatial Probe Radiance Scratch", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_DirectionalSpatialScratch = CreateScratch("Screen Probe Spatial Radiance Scratch", ProbeFormat);
        [RenderGraphResource(Name = "Temporal Probe Radiance", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_DirectionalTemporal = CreateScratch("Screen Probe Temporal Radiance", ProbeFormat);
        [RenderGraphResource(Name = "Temporal Probe Anchor", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_PreparedAnchor = CreateScratch("Screen Probe Temporal Anchor", ProbeFormat);
        [RenderGraphResource(Name = "Reconstructed Probe Anchor", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_ReconstructedAnchor = CreateScratch("Screen Probe Reconstructed Anchor", ProbeFormat);
        private readonly RenderGraphTexture m_PreviousAnchorStatistics = RenderGraphTexture.CreateInput("Previous Probe Anchor Statistics", ProbeFormat);
        private readonly RenderGraphTexture m_CurrentAnchorStatistics = CreateScratch("Probe Anchor Statistics", ProbeFormat);
        [RenderGraphResource(Name = "Probe Hit Motion", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_ProbeMoving = CreateScratch("Screen Probe Hit Motion", GraphicsFormat.R16_SFloat);
        [RenderGraphResource(Name = "Resolved Probe Hit Motion", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_ResolvedMoving = CreateScratch("Resolved Screen Probe Hit Motion", GraphicsFormat.R16_SFloat);
        [RenderGraphResource(Name = "Directional Probe Guide", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_DirectionalGuideTexture = CreateScratch("Screen Probe Directional Guide", ProbeFormat);
        [RenderGraphResource(Name = "Directional Probe Rays", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_DirectionalRayTexture = CreateScratch("Screen Probe Directional Rays", ProbeFormat);
        private readonly RenderGraphTexture m_PreviousGeometry = RenderGraphTexture.CreateInput("Previous SDF Diffuse Geometry", GeometryFormat);
        private readonly RenderGraphTexture m_Probes = CreateOutput("SDF Diffuse Probes", ProbeFormat);
        private readonly RenderGraphTexture m_Geometry = CreateOutput("SDF Diffuse Geometry", GeometryFormat);
        private readonly RenderGraphTexture m_PreviousRestir = RenderGraphTexture.CreateInput("Previous ReSTIR Gather", GeometryFormat);
        private readonly RenderGraphTexture m_CurrentRestir = CreateOutput("ReSTIR Gather History", GeometryFormat);
        private readonly RenderGraphTexture m_PreviousPixelLighting = RenderGraphTexture.CreateInput("Previous SDF Diffuse Pixel Lighting", ProbeFormat);
        private readonly RenderGraphTexture m_PreviousPixelGeometry = RenderGraphTexture.CreateInput("Previous SDF Diffuse Pixel Geometry", PixelGeometryFormat);
        private readonly RenderGraphTexture m_CurrentPixelLighting = CreateOutput("SDF Diffuse Pixel Lighting History", ProbeFormat);
        private readonly RenderGraphTexture m_CurrentPixelGeometry = CreateOutput("SDF Diffuse Pixel Geometry History", PixelGeometryFormat);
        private readonly RenderGraphTexture m_PreviousPixelDepth = RenderGraphTexture.CreateInput("Previous SDF Diffuse Pixel Depth", GraphicsFormat.R32_SFloat);
        private readonly RenderGraphTexture m_CurrentPixelDepth = CreateOutput("SDF Diffuse Pixel Depth History", GraphicsFormat.R32_SFloat);
        private readonly RenderGraphTexture m_PreviousPreExposure = RenderGraphTexture.CreateInput("Previous Screen Probe PreExposure", GraphicsFormat.R32_SFloat);
        private readonly RenderGraphTexture m_CurrentPreExposure = CreateOutput("Screen Probe PreExposure History", GraphicsFormat.R32_SFloat);
        [RenderGraphResource(Name = "Short Range Bent Normal Samples", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_BentSamples = CreateScratch("Short Range Bent Normal Samples", ProbeFormat);
        [RenderGraphResource(Name = "Short Range AO Furthest HZB", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_BentFurthestHzb = CreateScratch("Short Range AO Furthest HZB", GraphicsFormat.R16_SFloat);
        [RenderGraphResource(Name = "Lumen Short Range Bent Normal", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_LumenBentNormal = CreateScratch("Lumen Short Range Bent Normal", PixelGeometryFormat);
        private readonly RenderGraphTexture m_PreviousBentNormal = RenderGraphTexture.CreateInput("Previous Short Range Bent Normal", ProbeFormat);
        private readonly RenderGraphTexture m_CurrentBentNormal = CreateOutput("Short Range Bent Normal History", ProbeFormat);
        private CameraHistoryTexture m_BentNormalHistory;
        private bool m_BentNormalHistoryValid;
        private int m_BentSearchKernel = -1, m_BentTemporalKernel = -1;
        private int m_BentFurthestHzbKernel = -1, m_LumenBentKernel = -1;
        private TextureHandle m_BentBlueNoise;
        private TextureHandle m_BentScalarNoise;
        private int m_LumenBentHalfKernel = -1;
        private bool m_BentComposite, m_BentFlipY;
        private Matrix4x4 m_BentWorldToView, m_BentInverseProjection;
        private bool m_BentDuringIntegration;
        private readonly RenderGraphTexture m_PreviousScreenProbeMoments = RenderGraphTexture.CreateInput("Previous Screen Probe Moments", ProbeFormat);
        private readonly RenderGraphTexture m_CurrentScreenProbeMoments = CreateOutput("Screen Probe Moments", ProbeFormat);
        private readonly RenderGraphTexture m_PreviousFastUpdate = RenderGraphTexture.CreateInput("Previous Screen Probe Fast Update and Frames", GraphicsFormat.R8_UNorm);
        private readonly RenderGraphTexture m_CurrentFastUpdate = CreateOutput("Screen Probe Fast Update and Frames", GraphicsFormat.R8_UNorm);
        private CameraHistoryTexture m_FastUpdateHistory;
        [RenderGraphResource(Name = "Raw Resolved Diffuse GI", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_RawResolved = CreateScratch("SDF Diffuse Raw Resolved", ProbeFormat);
        [RenderGraphResource(Name = "Raw Rough Specular GI", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_RawRoughSpecular = CreateScratch("SDF Diffuse Raw Rough Specular", ProbeFormat);
        private readonly RenderGraphTexture m_PreviousRoughSpecular = RenderGraphTexture.CreateInput("Previous Screen Probe Rough Specular", ProbeFormat);
        private readonly RenderGraphTexture m_CurrentRoughSpecular = CreateOutput("Screen Probe Rough Specular History", ProbeFormat);
        private CameraHistoryTexture m_RoughSpecularHistory;
        private bool m_RoughSpecular;
        [RenderGraphResource(Name = "Diffuse GI Atrous Lighting", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_AtrousLighting = CreateScratch("Diffuse GI Atrous Lighting", ProbeFormat);
        [RenderGraphResource(Name = "Diffuse GI Atrous Variance A", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_AtrousVarianceA = CreateScratch("Diffuse GI Atrous Variance A", GraphicsFormat.R16_SFloat);
        [RenderGraphResource(Name = "Diffuse GI Atrous Variance B", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_AtrousVarianceB = CreateScratch("Diffuse GI Atrous Variance B", GraphicsFormat.R16_SFloat);
        [RenderGraphResource(Name = "Spatial ReSTIR Reservoirs", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_SpatialRestir = CreateScratch("Spatial ReSTIR Reservoirs", GeometryFormat);
        [RenderGraphResource(Name = "ReSTIR Resolve Variance", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_RestirVariance = CreateScratch("ReSTIR Resolve Variance", GraphicsFormat.R16_SFloat);
        private readonly RenderGraphTexture m_PreviousRestirVariance = RenderGraphTexture.CreateInput("Previous ReSTIR Variance", GraphicsFormat.R16_SFloat);
        private readonly RenderGraphTexture m_CurrentRestirVariance = CreateOutput("ReSTIR Variance History", GraphicsFormat.R16_SFloat);
        [RenderGraphResource(Name = "Probe Ray Diagnostics", Access = AccessFlags.Write)]
        [TransientResource]
        private readonly RenderGraphTexture m_RayDiagnostics = CreateScratch("SDF Diffuse Ray Diagnostics", GeometryFormat);
        [RenderGraphResource(Name = "Probe First Hit Diagnostics", Access = AccessFlags.Write)]
        [TransientResource]
        private readonly RenderGraphTexture m_FirstHitDiagnostics = CreateScratch("SDF Diffuse First Hit Diagnostics", GeometryFormat);
        [RenderGraphResource(Name = "Adaptive Probe Placement", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_AdaptivePlacement = CreateScratch("Screen Probe Adaptive Placement", GraphicsFormat.R32_SFloat);
        [RenderGraphResource(Name = "Filtered Probe Lighting", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_FilteredProbes = CreateScratch("Screen Probe Filtered Lighting", ProbeFormat);
        [RenderGraphResource(Name = "Screen Probe Hit Samples", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_HitSamples = CreateScratch("Screen Probe Hit Samples", GraphicsFormat.R32_SFloat);
        private readonly RenderGraphTexture m_LocalHzb = RenderGraphTexture.CreateInput("Screen Trace HZB", GraphicsFormat.R32_SFloat);
        private readonly RenderGraphTexture m_LocalMotion = RenderGraphTexture.CreateInput("Screen Trace Motion", MotionVectorPass.MotionVectorFormat);
        private readonly RenderGraphTexture m_ScreenRadianceDesc = CreateOutput("Screen Diffuse Source Radiance", ProbeFormat);
        private readonly RenderGraphTexture m_ScreenGeometryDesc = CreateOutput("Screen Diffuse Source Geometry", GraphicsFormat.R32G32_SFloat);
        private readonly RenderGraphTexture m_RadiosityDesc = CreateOutput("Surface Cache Radiosity", ProbeFormat);
        private readonly RenderGraphTexture m_RadiosityFeedbackCountDesc = CreateOutput("Radiosity Feedback Frames", GraphicsFormat.R16_SFloat);
        [RenderGraphResource(Name = "Radiosity Feedback Probes", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_RadiosityFeedbackProbes = RenderGraphBuffer.CreateStructured("Radiosity Feedback Probes", 32);
        [RenderGraphResource(Name = "Radiosity Feedback Rays", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_RadiosityFeedbackRays = RenderGraphBuffer.CreateStructured("Radiosity Feedback Rays", 32);
        [RenderGraphResource(Name = "Radiosity Feedback Radiance", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphBuffer m_RadiosityFeedbackRadiance = RenderGraphBuffer.CreateStructured("Radiosity Feedback Radiance", 12);
        private readonly RenderGraphTexture m_FarDesc = CreateOutput("World Diffuse Far Radiance", GeometryFormat);
        private readonly RenderGraphTexture m_WorldRawDesc = CreateOutput("World Probe Raw Radiance and Depth", GeometryFormat);
        private CameraHistoryTexture m_WorldRawHistory;
        private TextureHandle m_WorldRawRadiance;
        [RenderGraphResource(Name = "Screen Ray Samples", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_ScreenRays = CreateOutput("Diffuse GI Screen Rays", ProbeFormat);
        private VividMeshSdfGpuScene m_Scene;
        private ComputeShader m_TraceCompute;
        private ComputeShader m_GatherCompute;
        private ComputeShader m_ImportanceSamplingCompute;
        private ComputeShader m_FilterCompute;
        private ComputeShader m_GatherTemporalCompute;
        private ComputeShader m_RestirCompute;
        private ComputeShader m_CardLightingCompute;
        [RenderGraphResource(Name = "Surface Cache Bounce Lighting", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_BounceFinalLighting = CreateScratch("Surface Cache Bounce Lighting", ProbeFormat);
        private readonly RenderGraphTexture m_CardLightingDesc = CreateScratch("Surface Cache Card Lighting", ProbeFormat);
        private CameraHistoryTexture m_CardDirectHistory, m_CardFinalHistory;
        private TextureHandle m_CardDirectAtlas, m_CardFinalAtlas;
        private Vector4 m_FinalLightingLayout;
        private int m_CardDirectLightingKernel = -1, m_CardFinalLightingKernel = -1;
        private int m_FinalLightingReadyCards, m_FinalLightingNewCards;
        private int m_FinalLightingPreviousReadyCards;
        private int m_FinalLightingUpdateFirst;
        private (uint Revision, int LayerMask, float Bias, Vector4 Light, Vector4 Color) m_FinalLightingDirectInputs;
        private int m_FinalLightingDirtyFirst, m_FinalLightingDirtyEnd, m_FinalLightingInputOrder;
        private ComputeShader m_RadiosityCompute;
        private ComputeShader m_FarCompute;
        private ComputeShader m_NearOcclusionCompute;
        private CameraHistoryTexture m_ProbeHistory, m_GeometryHistory;
        private CameraHistoryTexture m_ScreenProbeMomentsHistory;
        private CameraHistoryTexture m_DirectionalHistory, m_DirectionalGuideHistory, m_ProposalHistory;
        private bool m_FrozenGuideActive, m_ProposalHistoryValid;
        private int m_DirectionalGuideKernel = -1, m_DirectionalCompositeKernel = -1, m_DirectionalTemporalKernel = -1;
        private int m_DirectionalFilterKernel = -1, m_DirectionalIntegrateKernel = -1, m_DirectionalResolveKernel = -1;
        private int m_LumenDirectionalFilterKernel = -1;
        private int m_PrepareAnchorKernel = -1;
        private int m_PrepareAnchorNoPixelKernel = -1;
        private int m_AnchorPreFilterKernel = -1;
        private CameraHistoryTexture m_AnchorStatisticsHistory;
        private bool m_AnchorStatisticsHistoryValid;
        private bool m_DirectionalCache, m_DirectionalGuide, m_DirectionalHistoryValid, m_DirectionalGuideHistoryValid;
        private bool m_DirectionalHierarchicalGuide, m_FullSphere, m_ProbeTemporal, m_DitherTraceDirections;
        private float m_MaxRayIntensity;
        private TextureHandle m_ProbeDirectionNoise, m_ScatterNoise;
        private TextureHandle m_SpatialFilterScalarNoise, m_SpatialFilterVectorNoise;
        private TextureHandle m_InterpolationScalarNoise, m_InterpolationVectorNoise;
        private TextureHandle m_TemporalScalarNoise;
        private bool m_StochasticInterpolation, m_InterpolationFlipY;
        private bool m_FastUpdateNeighborhoodClamp, m_TemporalRejectBasedOnNormal, m_TemporalPlaneDisocclusion, m_CoherentRadianceFastUpdate, m_DynamicLightingFastUpdateActive, m_TemporalCameraStatic, m_DisocclusionSpatialFilter;
        private Vector4 m_ProbeCameraPosition;
        private GraphicsBuffer m_PreExposure;
        private int m_SphereGuideKernel = -1;
        private CameraHistoryTexture m_RestirHistory;
        private CameraHistoryTexture m_RestirVarianceHistory;
        private CameraHistoryTexture m_PixelHistory, m_PixelGeometryHistory, m_PixelDepthHistory, m_PreExposureHistory;
        private CameraHistoryTexture m_RadiosityReadHistory, m_RadiosityWriteHistory;
        private TextureHandle m_RadiosityInput, m_RadiosityOutput;
        private int m_RadiosityKernel = -1;
        private int m_FeedbackPrepareKernel, m_FeedbackTraceKernel, m_FeedbackResolveKernel, m_FeedbackIntegrateKernel, m_FeedbackClearKernel;
        private bool m_RadiosityFeedbackSupported, m_RadiosityFeedback;
        private CameraHistoryTexture m_RadiosityFeedbackCountHistory;
        private TextureHandle m_RadiosityFeedbackCount;
        private int m_RadiosityTargetOrders, m_RadiosityOrder, m_RadiosityCursor, m_RadiosityUpdateCards;
        private int m_RadiositySamples, m_RadiositySeed;
        private int m_RadiosityAccumulationKernel = -1, m_RadiosityPublishKernel = -1;
        private int m_RadiosityPublishCardsKernel = -1, m_RadiosityClearKernel = -1;
        private int m_RadiosityRescaleKernel = -1;
        private bool m_RadiosityProgressive, m_RadiosityClear, m_RadiosityRestarting;
        private bool m_RadiosityRescale;
        private Vector4 m_RadiosityHistoryScale;
        private bool m_RadiosityFastRelight;
        private int m_RadiosityRelightSweepLimit, m_RadiositySweeps;
        private readonly TextureHandle[] m_RadiositySweepOutputs = new TextureHandle[8];
        private GraphicsBuffer m_SkyRadianceBoundBuffer;
        private readonly System.Action<AsyncGPUReadbackRequest> m_SkyRadianceBoundReadback;
        private readonly System.Action<ScriptableRenderContext, Camera> m_SkyBoundAfterSubmit;
        private Camera m_SkyBoundCamera;
        private bool m_SkyBoundAwaitSubmit;
        private int m_SkyBoundClearKernel = -1, m_SkyBoundMeasureKernel = -1;
        private int m_SkyBoundHash, m_SkyBoundRequestHash, m_SkyBoundSize;
        private bool m_SkyBoundValid, m_SkyBoundPending;
        private float m_SkyRadianceBound;
        private int m_RadiosityPublishedOrder, m_RadiosityBoostFrames, m_RadiosityBudgetBoost;
        private float m_LightingTolerance, m_RadiosityTransitionBlend;
        private bool m_RadiosityAccumulate;
        private int m_RadiosityBatchLimit, m_RadiosityBatch, m_RadiosityBounce;
        private readonly CameraHistoryTexture[] m_RadiosityOrderHistories = new CameraHistoryTexture[8];
        private CameraHistoryTexture m_RadiosityPublishedHistory;
        private TextureHandle m_RadiosityBounceInput, m_RadiosityPublished;
        private ulong m_RadiosityCardVersion;
        private readonly Vector4[] m_FarUpdates = new Vector4[FarProbesPerFrame];
        private CameraHistoryTexture m_FarHistory;
        private CameraHistoryBuffer m_FarHitIdsHistory;
        private TextureHandle m_FarAtlas;
        private BufferHandle m_FarHitIds;
        private VividGlobalSdfFrameData m_GlobalClipmap;
        private TextureHandle m_GlobalNear, m_GlobalMiddle, m_GlobalFar, m_GlobalMip;
        private BufferHandle m_GlobalPages, m_GlobalThinBricks;
        private float m_MeshDetailDistance;
        private Vector3Int m_FarWindow;
        private bool m_FarClear, m_FarPending;
        private int m_FarUpdateCount;
        private ulong m_FarCardVersion;
        private float m_FarDistance;
        private Vector4 m_NearOcclusion;
        private bool m_RestirOpenPbrDiffuse;
        private int m_OpenPbrResolveKernel = -1;
        private int m_FarKernel = -1, m_NearOcclusionKernel = -1;
        private HistoryState m_State;
        private Matrix4x4 m_ViewProjection, m_InverseViewProjection;
        private TextureHandle m_CardAlbedoDepth, m_CardNormalCoverage;
        private TextureHandle m_SkyTexture;
        private TextureHandle m_PreviousScreenRadiance, m_PreviousScreenGeometry;
        private Vector2 m_ScreenJitterUv, m_ScreenJitterDelta;
        private Vector4 m_LightPositionRange, m_LightColorShadow;
        private int m_TraceKernel = -1, m_CachedTraceKernel = -1, m_ResolveKernel = -1;
        [RenderGraphResource(Name = "Screen Probe Closest HZB", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_ClosestHzb = CreateScratch("Screen Probe Closest HZB", GraphicsFormat.R16_SFloat);
        private int m_ClosestHzbKernel = -1, m_LumenTraceKernel = -1, m_LumenReceiverKernel = -1, m_SceneColorKernel = -1;
        private Vector4 m_ClosestHzbSize;
        private float m_DepthThicknessScale;
        private Matrix4x4 m_ClipToPrevClip;
        private bool m_LumenScreenTrace;

        private int m_ScreenTraceKernel = -1, m_ScreenReceiverTraceKernel = -1, m_ScreenGatherKernel = -1;
        private readonly int[] m_CompactPlacementKernels = new int[5];
        private int m_PlacementKernel = -1, m_FilterKernel = -1, m_HitFilterKernel = -1;
        private int m_RestirTemporalKernel = -1, m_RestirSpatialKernel = -1;
        private int m_RestirDenoiseKernel = -1;
        private int m_AtrousKernel = -1, m_AtrousFinalKernel = -1, m_AtrousIterations;
        private int m_PixelTemporalKernel = -1, m_ScreenProbeTemporalKernel = -1, m_RestirPixelTemporalKernel = -1;
        private int m_ScreenProbeMomentsKernel = -1;
        private int m_ScreenProbeAoKernel = -1, m_ScreenProbeMomentsAoKernel = -1;
        private int m_PixelHistoryMaxAge, m_ProbeAnchorMaxAge;
        private int m_RestirVarianceTemporalKernel = -1;
        private int m_Width, m_Height, m_ProbeWidth, m_ProbeHeight, m_LayerMask, m_LightLayerMask;
        private int m_AtlasHeight;
        private Vector4 m_ProbeCameraForward;
        private float m_TraceDistance, m_Bias;
        private float m_SkyTraceDistance;
        private int m_SkyContentHash;
        private Vector4 m_SkyTextureParams, m_SkyTextureTint;
        private bool m_HasHistory, m_RealtimeOnly, m_OffscreenOnly;
        private bool m_HasScreenHistory, m_UseSdf;
        private bool m_DirectionalProbes;
        private bool m_AdaptiveProbes, m_SpatialFilter, m_HitDistanceFilter;
        private SdfScreenProbeSpatialKernel m_ProbeSpatialKernel;
        private bool m_RestirGather;
        private bool m_RestirPixelIntegration;
        private ulong m_RestirCardVersion;
        private bool m_RecordRayDiagnostics;
        private readonly LumenDebugViewPass m_LumenDebugView = new();
        private readonly LumenReflectionPass m_LumenReflections = new();
        private bool m_MeshCardGridRecorded;
        private Vector4 m_MeshCardGridParams;
        private Matrix4x4 m_MeshCardGridView, m_MeshCardGridProjection;
        private int m_DebugTextureKernel, m_DebugSurfaceKernel, m_DebugGlobalSdfKernel, m_DebugBentKernel, m_DebugCardFramesKernel;
        private int m_DebugClearCardFeedbackKernel, m_DebugSurfaceFeedbackKernel;
        private bool m_SurfaceCacheFeedbackSupported;
        private bool m_RecordFirstHitDiagnostics;
        private bool m_RecordProbeAnchorDiagnostics;
        private bool m_SpatialAnchor, m_AnchorPreFilter;
        private SdfDiffuseTraceMode m_TraceMode;

        public SdfDiffuseGatherPass()
        {
            m_SkyRadianceBoundReadback = OnSkyRadianceBoundReadback;
            m_SkyBoundAfterSubmit = OnSkyBoundAfterSubmit;
            profilingSampler = new ProfilingSampler(nameof(SdfDiffuseGatherPass));
            m_ScreenHzb = m_LocalHzb;
            m_ScreenMotion = m_LocalMotion;
            m_ScreenRays.desc.ClearBuffer = false;
        }

        private static bool HasKernel(ComputeShader shader, string name) => shader != null && shader.HasKernel(name);

        private static bool IsEnabledForGather(BoolParameter setting, bool useLegacyGatherSettings) =>
            setting.value || (!useLegacyGatherSettings && !setting.overrideState);

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_TraceCompute = resources?.SdfScreenProbeTracingCompute;
            m_GatherCompute = resources?.SdfDiffuseGatherCompute;
            m_ImportanceSamplingCompute = resources?.SdfScreenProbeImportanceSamplingCompute;
            m_FilterCompute = resources?.SdfScreenProbeFilteringCompute;
            m_GatherTemporalCompute = resources?.SdfScreenProbeGatherTemporalCompute;
            m_RestirCompute = resources?.SdfReSTIRGatherCompute;
            m_CardLightingCompute = resources?.SdfSceneDirectLightingCompute;
            m_RadiosityCompute = resources?.SdfRadiosityCompute;
            m_FarCompute = resources?.SdfRadianceCacheUpdateCompute;
            m_NearOcclusionCompute = resources?.SdfShortRangeAOCompute;
            m_LumenReflections.Initialize(resources);
            if (m_TraceCompute == null || !SystemInfo.supportsComputeShaders
                || !SystemInfo.IsFormatSupported(ProbeFormat, GraphicsFormatUsage.LoadStore)
                || !SystemInfo.IsFormatSupported(GeometryFormat, GraphicsFormatUsage.LoadStore)
                || !HasKernel(m_TraceCompute, "TraceSdfDiffuseProbes") || !HasKernel(m_GatherCompute, "ResolveSdfDiffuseGI"))
                return;

            m_TraceKernel = m_TraceCompute.FindKernel("TraceSdfDiffuseProbes");
            m_DebugTextureKernel = FindOptionalKernel("LumenDebugTextureCS");
            m_DebugSurfaceKernel = FindOptionalKernel("LumenDebugSurfaceCS");
            m_DebugGlobalSdfKernel = FindOptionalKernel("LumenDebugGlobalSdfCS");
            m_DebugBentKernel = FindOptionalKernel("LumenDebugBentNormalCS");
            m_DebugCardFramesKernel = FindOptionalKernel("LumenDebugCardFramesCS");
            m_DebugClearCardFeedbackKernel = FindOptionalKernel("LumenDebugClearCardFeedbackCS");
            m_DebugSurfaceFeedbackKernel = FindOptionalKernel("LumenDebugSurfaceFeedbackCS");
            if (HasKernel(m_TraceCompute, "TraceSdfReceiverProbes"))
                m_ReceiverTraceKernel = m_TraceCompute.FindKernel("TraceSdfReceiverProbes");
            if (HasKernel(m_TraceCompute, "TraceSdfReceiverCachedProbes"))
                m_ReceiverCachedKernel = m_TraceCompute.FindKernel("TraceSdfReceiverCachedProbes");
            if (HasKernel(m_RadiosityCompute, "UpdateSdfSurfaceRadiosity")
                && SystemInfo.IsFormatSupported(ProbeFormat, GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample))
                m_RadiosityKernel = m_RadiosityCompute.FindKernel("UpdateSdfSurfaceRadiosity");
            if (HasKernel(m_RadiosityCompute, "AccumulateSdfSurfaceRadiosity")
                && HasKernel(m_RadiosityCompute, "PublishSdfSurfaceRadiosity"))
            {
                m_RadiosityAccumulationKernel = m_RadiosityCompute.FindKernel("AccumulateSdfSurfaceRadiosity");
                m_RadiosityPublishKernel = m_RadiosityCompute.FindKernel("PublishSdfSurfaceRadiosity");
            }
            if (HasKernel(m_RadiosityCompute, "PublishSdfSurfaceRadiosityCards")
                && HasKernel(m_RadiosityCompute, "ClearSdfSurfaceRadiosity"))
            {
                m_RadiosityPublishCardsKernel = m_RadiosityCompute.FindKernel("PublishSdfSurfaceRadiosityCards");
                m_RadiosityClearKernel = m_RadiosityCompute.FindKernel("ClearSdfSurfaceRadiosity");
            }
            if (HasKernel(m_RadiosityCompute, "RescaleSdfSurfaceRadiosity"))
                m_RadiosityRescaleKernel = m_RadiosityCompute.FindKernel("RescaleSdfSurfaceRadiosity");
            m_RadiosityFeedbackSupported = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12
                && SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample)
                && HasKernel(m_RadiosityCompute, "PrepareRadiosityFeedbackProbesCS")
                && HasKernel(m_RadiosityCompute, "TraceRadiosityFeedbackRaysCS")
                && HasKernel(m_RadiosityCompute, "ResolveRadiosityFeedbackRaysCS")
                && HasKernel(m_RadiosityCompute, "IntegrateRadiosityFeedbackCS")
                && HasKernel(m_RadiosityCompute, "ClearRadiosityFeedbackCS");
            if (m_RadiosityFeedbackSupported)
            {
                m_FeedbackPrepareKernel = m_RadiosityCompute.FindKernel("PrepareRadiosityFeedbackProbesCS");
                m_FeedbackTraceKernel = m_RadiosityCompute.FindKernel("TraceRadiosityFeedbackRaysCS");
                m_FeedbackResolveKernel = m_RadiosityCompute.FindKernel("ResolveRadiosityFeedbackRaysCS");
                m_FeedbackIntegrateKernel = m_RadiosityCompute.FindKernel("IntegrateRadiosityFeedbackCS");
                m_FeedbackClearKernel = m_RadiosityCompute.FindKernel("ClearRadiosityFeedbackCS");
            }
            if (SystemInfo.supportsAsyncGPUReadback && HasKernel(m_RadiosityCompute, "MeasureSdfSkyRadianceBound")
                && HasKernel(m_RadiosityCompute, "ClearSdfSkyRadianceBound"))
            {
                m_SkyBoundMeasureKernel = m_RadiosityCompute.FindKernel("MeasureSdfSkyRadianceBound");
                m_SkyBoundClearKernel = m_RadiosityCompute.FindKernel("ClearSdfSkyRadianceBound");
                RenderPipelineManager.endCameraRendering -= m_SkyBoundAfterSubmit;
                RenderPipelineManager.endCameraRendering += m_SkyBoundAfterSubmit;
            }
            if (SystemInfo.IsFormatSupported(GeometryFormat, GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample)
                && HasKernel(m_FarCompute, "UpdateSdfFarRadiance"))
            {
                m_FarKernel = m_FarCompute.FindKernel("UpdateSdfFarRadiance");
                m_WorldClearKernel = m_FarCompute.FindKernel("ClearWorldProbeDemand");
                m_WorldMarkKernel = m_FarCompute.FindKernel("MarkWorldProbeDemand");
                m_ObjectGridKernel = m_FarCompute.FindKernel("BuildGlobalObjectPages");
                m_WorldReuseKernel = m_FarCompute.FindKernel("ReuseWorldProbes");
                m_WorldAllocateKernel = m_FarCompute.FindKernel("AllocateWorldProbes");
                m_WorldTicketsKernel = m_FarCompute.FindKernel("BuildWorldTickets");
                m_WorldSelectKernel = m_FarCompute.FindKernel("SelectWorldProbeUpdates");
                m_WorldArgsKernel = m_FarCompute.FindKernel("BuildWorldProbeDispatchArgs");
                m_WorldFilterKernel = m_FarCompute.FindKernel("FilterWorldProbeRadiance");
                m_WorldUpdateKernel = m_FarCompute.FindKernel("UpdateWorldProbes");
                m_WorldSkyKernel = m_FarCompute.FindKernel("UpdateWorldProbesWithSky");
                m_WorldGlobalUpdateKernel = HasKernel(m_FarCompute, "UpdateWorldProbesGlobal")
                    ? m_FarCompute.FindKernel("UpdateWorldProbesGlobal") : -1;
                m_WorldGlobalSkyKernel = HasKernel(m_FarCompute, "UpdateWorldProbesGlobalWithSky")
                    ? m_FarCompute.FindKernel("UpdateWorldProbesGlobalWithSky") : -1;
            }
            if (SystemInfo.IsFormatSupported(ProbeFormat, GraphicsFormatUsage.LoadStore)
                && HasKernel(m_NearOcclusionCompute, "ApplySdfNearOcclusion"))
                m_NearOcclusionKernel = m_NearOcclusionCompute.FindKernel("ApplySdfNearOcclusion");
            // Reprojection reads the R32_UInt geometry history with Texture2D.Load.
            if (m_NearOcclusionKernel >= 0 && SystemInfo.IsFormatSupported(PixelGeometryFormat, GraphicsFormatUsage.LoadStore)
                && HasKernel(m_NearOcclusionCompute, "SearchBentNormalHorizons")
                && HasKernel(m_NearOcclusionCompute, "AccumulateBentNormal"))
            {
                m_BentSearchKernel = m_NearOcclusionCompute.FindKernel("SearchBentNormalHorizons");
                m_BentTemporalKernel = m_NearOcclusionCompute.FindKernel("AccumulateBentNormal");
            }
            if (m_NearOcclusionKernel >= 0
                && SystemInfo.IsFormatSupported(PixelGeometryFormat, GraphicsFormatUsage.LoadStore)
                && SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, GraphicsFormatUsage.LoadStore)
                && HasKernel(m_NearOcclusionCompute, "BuildShortRangeAOFurthestHZB")
                && HasKernel(m_NearOcclusionCompute, "ScreenSpaceShortRangeAOCS"))
            {
                m_BentFurthestHzbKernel = m_NearOcclusionCompute.FindKernel("BuildShortRangeAOFurthestHZB");
                m_LumenBentKernel = m_NearOcclusionCompute.FindKernel("ScreenSpaceShortRangeAOCS");
            }
            if (m_NearOcclusionKernel >= 0
                && SystemInfo.IsFormatSupported(PixelGeometryFormat, GraphicsFormatUsage.LoadStore)
                && HasKernel(m_NearOcclusionCompute, "ScreenSpaceShortRangeAOHalfCS"))
                m_LumenBentHalfKernel = m_NearOcclusionCompute.FindKernel("ScreenSpaceShortRangeAOHalfCS");
            m_ResolveKernel = m_GatherCompute.FindKernel("ResolveSdfDiffuseGI");
            if (HasKernel(m_GatherCompute, "ResolveSdfRestirOpenPBR"))
            {
                int kernel = m_GatherCompute.FindKernel("ResolveSdfRestirOpenPBR");
                if (m_GatherCompute.IsSupported(kernel))
                    m_OpenPbrResolveKernel = kernel;
            }
            if (SystemInfo.usesReversedZBuffer
                && SystemInfo.IsFormatSupported(PixelGeometryFormat, GraphicsFormatUsage.LoadStore)
                && HasKernel(m_GatherTemporalCompute, "TemporalSdfDiffuseGI"))
            {
                m_PixelTemporalKernel = m_GatherTemporalCompute.FindKernel("TemporalSdfDiffuseGI");
                if (HasKernel(m_GatherTemporalCompute, "ScreenProbeTemporalReprojectionCS"))
                    m_ScreenProbeTemporalKernel = m_GatherTemporalCompute.FindKernel("ScreenProbeTemporalReprojectionCS");
                if (HasKernel(m_GatherTemporalCompute, "ScreenProbeTemporalReprojectionWithMomentsCS"))
                    m_ScreenProbeMomentsKernel = m_GatherTemporalCompute.FindKernel("ScreenProbeTemporalReprojectionWithMomentsCS");
                if (HasKernel(m_GatherTemporalCompute, "ScreenProbeTemporalReprojectionWithAOCS"))
                    m_ScreenProbeAoKernel = m_GatherTemporalCompute.FindKernel("ScreenProbeTemporalReprojectionWithAOCS");
                if (HasKernel(m_GatherTemporalCompute, "ScreenProbeTemporalReprojectionWithMomentsAndAOCS"))
                    m_ScreenProbeMomentsAoKernel = m_GatherTemporalCompute.FindKernel("ScreenProbeTemporalReprojectionWithMomentsAndAOCS");
                if (HasKernel(m_GatherTemporalCompute, "TemporalSdfRestirDiffuseGI"))
                    m_RestirPixelTemporalKernel = m_GatherTemporalCompute.FindKernel("TemporalSdfRestirDiffuseGI");
                if (HasKernel(m_GatherTemporalCompute, "TemporalSdfRestirVariance"))
                    m_RestirVarianceTemporalKernel = m_GatherTemporalCompute.FindKernel("TemporalSdfRestirVariance");
            }
            if (SystemInfo.IsFormatSupported(GraphicsFormat.R32_SFloat, GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample)
                && HasKernel(m_GatherCompute, "PlaceSdfAdaptiveProbes"))
            {
                m_PlacementKernel = m_GatherCompute.FindKernel("PlaceSdfAdaptiveProbes");
                m_CompactPlacementKernels[0] = m_GatherCompute.FindKernel("MarkAdaptiveCandidates");
                m_CompactPlacementKernels[1] = m_GatherCompute.FindKernel("PruneAdaptiveCandidates");
                m_CompactPlacementKernels[2] = m_GatherCompute.FindKernel("PrefixAdaptiveRows");
                m_CompactPlacementKernels[3] = m_GatherCompute.FindKernel("PrefixAdaptiveAtlas");
                m_CompactPlacementKernels[4] = m_GatherCompute.FindKernel("WriteAdaptiveAtlas");
            }
            if (SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample)
                && HasKernel(m_GatherCompute, "FilterSdfDiffuseAtrous") && HasKernel(m_GatherCompute, "FilterSdfDiffuseAtrousFinal"))
            {
                m_AtrousKernel = m_GatherCompute.FindKernel("FilterSdfDiffuseAtrous");
                m_AtrousFinalKernel = m_GatherCompute.FindKernel("FilterSdfDiffuseAtrousFinal");
            }
            if (HasKernel(m_FilterCompute, "FilterSdfDiffuseProbes"))
                m_FilterKernel = m_FilterCompute.FindKernel("FilterSdfDiffuseProbes");
            if (HasKernel(m_FilterCompute, "FilterSdfDirectionalHits"))
                m_HitFilterKernel = m_FilterCompute.FindKernel("FilterSdfDirectionalHits");
            if (HasKernel(m_ImportanceSamplingCompute, "PrepareSphereDirectionalGuide"))
                m_SphereGuideKernel = m_ImportanceSamplingCompute.FindKernel("PrepareSphereDirectionalGuide");
            if (HasKernel(m_ImportanceSamplingCompute, "PrepareSdfDirectionalGuide"))
                m_DirectionalGuideKernel = m_ImportanceSamplingCompute.FindKernel("PrepareSdfDirectionalGuide");
            if (HasKernel(m_FilterCompute, "ScreenProbeCompositeTracesWithScatterCS")
                && HasKernel(m_FilterCompute, "ScreenProbeTemporallyAccumulateTraceRadianceCS")
                && HasKernel(m_FilterCompute, "ScreenProbeFilterGatherLegacyTracesCS")
                && HasKernel(m_FilterCompute, "ScreenProbeConvertToIrradianceCS")
                && HasKernel(m_GatherCompute, "ScreenProbeIntegrateCS"))
            {
                m_DirectionalCompositeKernel = m_FilterCompute.FindKernel("ScreenProbeCompositeTracesWithScatterCS");
                m_DirectionalTemporalKernel = m_FilterCompute.FindKernel("ScreenProbeTemporallyAccumulateTraceRadianceCS");
                m_DirectionalFilterKernel = m_FilterCompute.FindKernel("ScreenProbeFilterGatherLegacyTracesCS");
                m_DirectionalIntegrateKernel = m_FilterCompute.FindKernel("ScreenProbeConvertToIrradianceCS");
                m_DirectionalResolveKernel = m_GatherCompute.FindKernel("ScreenProbeIntegrateCS");
            }
            if (HasKernel(m_FilterCompute, "ScreenProbeFilterGatherTracesCS"))
                m_LumenDirectionalFilterKernel = m_FilterCompute.FindKernel("ScreenProbeFilterGatherTracesCS");
            if (HasKernel(m_FilterCompute, "ScreenProbePrepareAnchorCS"))
                m_PrepareAnchorKernel = m_FilterCompute.FindKernel("ScreenProbePrepareAnchorCS");
            if (HasKernel(m_FilterCompute, "ScreenProbePrepareAnchorNoPixelHistoryCS"))
                m_PrepareAnchorNoPixelKernel = m_FilterCompute.FindKernel("ScreenProbePrepareAnchorNoPixelHistoryCS");
            if (HasKernel(m_FilterCompute, "ScreenProbeFilterAnchorCS"))
                m_AnchorPreFilterKernel = m_FilterCompute.FindKernel("ScreenProbeFilterAnchorCS");
            if (HasKernel(m_RestirCompute, "ResampleSdfRestirTemporal") && HasKernel(m_RestirCompute, "ResampleSdfRestirSpatial"))
            {
                m_RestirTemporalKernel = m_RestirCompute.FindKernel("ResampleSdfRestirTemporal");
                m_RestirSpatialKernel = m_RestirCompute.FindKernel("ResampleSdfRestirSpatial");
            }
            if (SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample)
                && HasKernel(m_RestirCompute, "DenoiseSdfRestir"))
                m_RestirDenoiseKernel = m_RestirCompute.FindKernel("DenoiseSdfRestir");
            if (SystemInfo.supports2DArrayTextures && HasKernel(m_TraceCompute, "TraceSdfDiffuseCachedProbes"))
                m_CachedTraceKernel = m_TraceCompute.FindKernel("TraceSdfDiffuseCachedProbes");
            if (SystemInfo.IsFormatSupported(ProbeFormat, GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample | GraphicsFormatUsage.Linear)
                && HasKernel(m_CardLightingCompute, "UpdateSdfCardDirectLighting") && HasKernel(m_CardLightingCompute, "CombineSdfCardFinalLighting"))
            {
                m_CardDirectLightingKernel = m_CardLightingCompute.FindKernel("UpdateSdfCardDirectLighting");
                m_CardFinalLightingKernel = m_CardLightingCompute.FindKernel("CombineSdfCardFinalLighting");
            }
            if (SystemInfo.usesReversedZBuffer
                && SystemInfo.IsFormatSupported(GraphicsFormat.R32G32_SFloat, GraphicsFormatUsage.LoadStore)
                && HasKernel(m_TraceCompute, "TraceDiffuseScreenRays") && HasKernel(m_TraceCompute, "GatherScreenDiffuseProbes"))
            {
                if (SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample)
                    && HasKernel(m_TraceCompute, "TraceLumenScreenRaysWithSdf") && HasKernel(m_TraceCompute, "TraceLumenScreenRays")
                    && HasKernel(m_TraceCompute, "BuildScreenProbeClosestHZB") && HasKernel(m_TraceCompute, "CaptureScreenProbeSceneColor"))
                {
                    m_LumenReceiverKernel = m_TraceCompute.FindKernel("TraceLumenScreenRaysWithSdf");
                    m_ClosestHzbKernel = m_TraceCompute.FindKernel("BuildScreenProbeClosestHZB");
                    m_LumenTraceKernel = m_TraceCompute.FindKernel("TraceLumenScreenRays");
                    m_SceneColorKernel = m_TraceCompute.FindKernel("CaptureScreenProbeSceneColor");
                }
                m_ScreenTraceKernel = m_TraceCompute.FindKernel("TraceDiffuseScreenRays");
                if (HasKernel(m_TraceCompute, "TraceReceiverDiffuseScreenRays"))
                    m_ScreenReceiverTraceKernel = m_TraceCompute.FindKernel("TraceReceiverDiffuseScreenRays");
                m_ScreenGatherKernel = m_TraceCompute.FindKernel("GatherScreenDiffuseProbes");
            }
            m_CompactTraceSupported = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12
                && FindCompactKernels();
            m_FeedbackTraceKernels[0] = FindOptionalKernel("TraceSdfDiffuseCachedProbesFeedback");
            m_FeedbackTraceKernels[1] = FindOptionalKernel("TraceSdfReceiverCachedProbesFeedback");
            m_SurfaceCacheFeedbackSupported = m_DebugClearCardFeedbackKernel >= 0 && m_DebugSurfaceFeedbackKernel >= 0
                && System.Array.IndexOf(m_FeedbackTraceKernels, -1) < 0
                && (!m_CompactTraceSupported || (m_FeedbackCompactResolveMeshKernel >= 0
                    && System.Array.IndexOf(m_FeedbackCompactResolveKernels, -1) < 0));
            m_Scene = new VividMeshSdfGpuScene(Shader.Find("VividRP/Material/StandardLit"));
        }

        private bool FindCompactKernels()
        {
            m_CompactResetKernel = FindOptionalKernel("ResetCompactTracesCS");
            m_CompactArgsKernel = FindOptionalKernel("SetupIndirectArgsCS");
            m_MeshCardGridKernel = FindOptionalKernel("BuildMeshSdfCardGridCS");
            m_CompactScreenKernels[0] = FindOptionalKernel("CompactScreenProbeTracesCS");
            m_CompactScreenKernels[1] = FindOptionalKernel("CompactScreenProbeTracesWithReceiverCS");
            m_CompactScreenSkipEmptyKernels[0] = FindOptionalKernel("CompactScreenProbeSkipEmptyMeshCS");
            m_CompactScreenSkipEmptyKernels[1] = FindOptionalKernel("CompactScreenProbeSkipEmptyMeshWithReceiverCS");
            m_CompactMeshKernels[0] = FindOptionalKernel("TraceMeshSdfCompactedCS");
            m_CompactMeshKernels[1] = FindOptionalKernel("TraceMeshSdfReceiverCompactedCS");
            m_CompactResolveMeshKernel = FindOptionalKernel("ResolveMeshSdfCompactedCS");
            m_FeedbackCompactResolveMeshKernel = FindOptionalKernel("ResolveMeshSdfCompactedFeedbackCS");
            m_CompactGlobalKernel = FindOptionalKernel("TraceGlobalSdfCompactedCS");
            m_CompactResolveGlobalKernels[0] = FindOptionalKernel("ResolveGlobalSdfCompactedCS");
            m_CompactResolveGlobalKernels[1] = FindOptionalKernel("ResolveGlobalSdfReceiverCompactedCS");
            m_FeedbackCompactResolveKernels[0] = FindOptionalKernel("ResolveGlobalSdfCompactedFeedbackCS");
            m_FeedbackCompactResolveKernels[1] = FindOptionalKernel("ResolveGlobalSdfReceiverCompactedFeedbackCS");
            m_CompactFinalizeKernel = FindOptionalKernel("FinalizeProbeGatherCS");

            return m_CompactResetKernel >= 0 && m_CompactArgsKernel >= 0 && m_MeshCardGridKernel >= 0
                && System.Array.IndexOf(m_CompactScreenKernels, -1) < 0
                && System.Array.IndexOf(m_CompactMeshKernels, -1) < 0
                && m_CompactResolveMeshKernel >= 0
                && m_CompactGlobalKernel >= 0 && System.Array.IndexOf(m_CompactResolveGlobalKernels, -1) < 0
                && m_CompactFinalizeKernel >= 0;
        }

        private int FindOptionalKernel(string name) => HasKernel(m_TraceCompute, name) ? m_TraceCompute.FindKernel(name) : -1;

        public override bool IsActive(ContextContainer frameData) =>
            VividRenderPipelineAsset.GetActiveAsset()?.SupportProbeVolume != true;

        public override void Prepare(ContextContainer frameData)
        {
            frameData.GetOrCreate<VividSdfDiffuseFrameData>().DeclaredOutput = m_Output;
            var cameraData = frameData.Get<VividCameraData>();
            var camera = cameraData.camera;
            m_State = null;
            m_ReceiverVisibilitySource = null;
            m_ReceiverVisibility = default;
            m_CorrectReceiverSelfHits = m_ScreenCardLightingRequested = false;
            m_ProbeHistory = m_GeometryHistory = null;
            m_AnchorStatisticsHistory = null;
            m_AnchorStatisticsHistoryValid = false;
            m_DirectionalHistory = m_DirectionalGuideHistory = m_ProposalHistory = null;
            m_FrozenGuideActive = m_ProposalHistoryValid = false;
            m_DirectionalHistoryValid = m_DirectionalGuideHistoryValid = false;
            m_RestirHistory = m_RestirVarianceHistory = null;
            m_PixelHistory = m_PixelGeometryHistory = m_PixelDepthHistory = m_PreExposureHistory = null;
            m_RoughSpecularHistory = null;
            m_RoughSpecular = false;
            m_RawRoughSpecular.Resize(1, 1);
            m_LumenReflections.ResetFrame();
            m_MeshCardGridRecorded = false;
            m_BentNormalHistory = null;
            m_BentNormalHistoryValid = false;
            m_BentSamples.Resize(1, 1);
            m_BentFurthestHzb.Resize(1, 1);
            m_LumenBentNormal.Resize(1, 1);
            m_BentBlueNoise = default;
            m_BentScalarNoise = default;
            m_StochasticInterpolation = false;
            m_FastUpdateNeighborhoodClamp = false;
            m_TemporalRejectBasedOnNormal = false;
            m_TemporalPlaneDisocclusion = false;
            m_CoherentRadianceFastUpdate = false;
            m_DynamicLightingFastUpdateActive = false;
            m_TemporalCameraStatic = false;
            m_DisocclusionSpatialFilter = false;
            m_ProbeCameraPosition = default;
            m_InterpolationScalarNoise = m_InterpolationVectorNoise = default;
            m_TemporalScalarNoise = default;
            m_ProbeDirectionNoise = m_ScatterNoise = default;
            m_SpatialFilterScalarNoise = m_SpatialFilterVectorNoise = default;
            m_BentComposite = false;
            m_BentDuringIntegration = false;
            m_ScreenProbeMomentsHistory = null;
            m_FastUpdateHistory = null;
            m_RadiosityReadHistory = m_RadiosityWriteHistory = null;
            m_RadiosityInput = m_RadiosityOutput = default;
            m_CardDirectHistory = m_CardFinalHistory = null;
            m_CardDirectAtlas = m_CardFinalAtlas = default;
            m_FinalLightingReadyCards = m_FinalLightingNewCards = 0;
            m_FinalLightingPreviousReadyCards = 0;
            m_FinalLightingUpdateFirst = 0;
            m_FinalLightingDirtyFirst = int.MaxValue;
            m_FinalLightingDirtyEnd = 0;
            m_BounceFinalLighting.Resize(1, 1);
            m_RadiosityBounceInput = m_RadiosityPublished = default;
            m_RadiosityPublishedHistory = null;
            m_RadiosityFeedback = false;
            m_RadiosityFeedbackCount = default;
            m_RadiosityFeedbackCountHistory = null;
            m_RadiosityFeedbackProbes.desc.Count = 1;
            m_RadiosityFeedbackRays.desc.Count = m_RadiosityFeedbackRadiance.desc.Count = 1;
            System.Array.Clear(m_RadiosityOrderHistories, 0, m_RadiosityOrderHistories.Length);
            m_RadiosityTargetOrders = m_RadiosityOrder = m_RadiosityCursor = m_RadiosityUpdateCards = 0;
            m_RadiosityCardVersion = 0;
            m_RadiosityPublishedOrder = m_RadiosityBoostFrames = 0;
            m_RadiosityClear = m_RadiosityRestarting = false;
            m_RadiosityRescale = false;
            m_RadiosityHistoryScale = Vector4.one;
            m_RadiosityFastRelight = false;
            m_RadiositySweeps = 1;
            m_FarHistory = null;
            m_WorldRawHistory = null;
            m_WorldRawRadiance = default;
            m_FarHitIdsHistory = null;
            m_FarAtlas = default;
            m_FarHitIds = default;
            m_GlobalClipmap = null;
            m_ObjectGrid = m_ObjectPageTable = m_ObjectPageWork = default;
            m_CompactTracePrepared = false;
            SizeCompactTraceBuffers(1);
            m_MeshDetailDistance = 0f;
            m_FarUpdateCount = 0;
            m_FarDistance = 0f;
            m_FarCardVersion = 0;
            m_FarPending = m_FarClear = false;
            m_NearOcclusion = default;
            m_RestirOpenPbrDiffuse = false;
            m_SkyTexture = default;
            m_SkyTraceDistance = 0f;
            m_SkyContentHash = 0;
            m_SkyTextureParams = m_SkyTextureTint = default;
            m_HasScreenHistory = false;
            m_ScreenJitterUv = default;
            m_Width = CameraDimensionUtility.ResolveCameraDimension(cameraData.actualWidth, cameraData.pixelWidth, Screen.width);
            m_Height = CameraDimensionUtility.ResolveCameraDimension(cameraData.actualHeight, cameraData.pixelHeight, Screen.height);
            var settings = VolumeManager.instance.stack.GetComponent<SdfDiffuseGISettings>();
            bool useSettings = settings != null && settings.active && settings.enabled.value;
            bool legacyGather = useSettings && settings.UsesLegacyGatherSettings;
            m_AdaptiveProbes = useSettings && m_PlacementKernel >= 0 && IsEnabledForGather(settings.adaptiveScreenProbes, legacyGather);
            m_SpatialFilter = useSettings && m_FilterKernel >= 0 && IsEnabledForGather(settings.screenProbeSpatialFilter, legacyGather);
            m_HitDistanceFilter = m_SpatialFilter && m_HitFilterKernel >= 0 && settings.screenProbeHitDistanceFilter.value;
            m_Output.Resize(m_Width, m_Height);
            var debugMode = VividRenderingDebugDisplaySettings.Data.lumenDebugMode;
            m_RecordRayDiagnostics = (useSettings && settings.recordRayDiagnostics.value)
                || debugMode == LumenDebugViewMode.ScreenProbeRayState;
            m_RecordFirstHitDiagnostics = useSettings && settings.recordRayDiagnostics.value && settings.recordFirstHitDiagnostics.value;
            m_RadiositySamples = useSettings ? Mathf.Clamp(settings.surfaceCacheRadiositySamples.value, 16, 4096) : 16;
            m_RadiosityAccumulate = useSettings && settings.surfaceCacheRadiosityAccumulate.value
                && m_RadiosityAccumulationKernel >= 0 && m_RadiosityPublishKernel >= 0;
            m_RadiosityBatchLimit = useSettings ? settings.surfaceCacheRadiosityBatchLimit.value : 64;
            m_RadiosityBatch = m_RadiosityBounce = 0;
            m_RadiosityProgressive = useSettings && settings.surfaceCacheProgressiveRadiosity.value
                && m_RadiosityPublishCardsKernel >= 0 && m_RadiosityClearKernel >= 0;
            m_LightingTolerance = useSettings ? settings.surfaceCacheLightingTolerance.value : 0.005f;
            m_RadiosityBudgetBoost = useSettings ? settings.surfaceCacheUpdateBoost.value : 4;
            m_RadiosityRelightSweepLimit = useSettings ? settings.surfaceCacheRelightSweeps.value : 1;
#if UNITY_EDITOR
            m_RecordProbeAnchorDiagnostics = EditorRecordProbeAnchorDiagnostics && EditorTraceCapture != null;
            m_RadiositySeed = EditorRadiositySeed;
#else
            m_RecordProbeAnchorDiagnostics = false;
            m_RadiositySeed = 0;
#endif
            bool motionConnected = m_ScreenMotion != null && !ReferenceEquals(m_ScreenMotion, m_LocalMotion);
            bool pixelTemporal = useSettings && motionConnected && m_PixelTemporalKernel >= 0
                && IsEnabledForGather(settings.pixelTemporal, legacyGather);
            m_PixelHistoryMaxAge = pixelTemporal ? Mathf.Clamp(settings.pixelHistoryMaxAge.value, 8, 32) : 8;
            m_ProbeAnchorMaxAge = useSettings ? Mathf.Clamp(settings.screenProbeAnchorMaxAge.value, 32, 512) : 32;
#if UNITY_EDITOR
            if (EditorTraceCapture != null && EditorProbeAnchorMaxAgeOverride >= 32)
                m_ProbeAnchorMaxAge = Mathf.Clamp(EditorProbeAnchorMaxAgeOverride, 32, 512);
#endif
            if (m_TraceKernel < 0 || camera == null || camera.cullingMask == 0 || camera.stereoEnabled
                || DebugPassCameraUtility.ShouldSkipExecution(camera))
                return;

            if (settings != null && settings.active && !settings.enabled.value)
                return;
            var cache = m_Scene.RefreshSurfaceCache();
            m_RealtimeOnly = settings != null && settings.active && settings.realtimeOnly.value;
            m_OffscreenOnly = settings != null && settings.active && settings.offscreenBounceOnly.value;
            m_TraceMode = settings != null && settings.active ? settings.traceMode.value : SdfDiffuseTraceMode.SdfOnly;
            m_UseSdf = m_TraceMode != SdfDiffuseTraceMode.ScreenOnly
                && (cache == null || (m_CachedTraceKernel >= 0 && cache.IsUsable));
            if (m_UseSdf)
            {
                m_Scene.Update(VividMeshletRendererDatabase.instance, cameraData.frameIndex);
                m_UseSdf = m_Scene.NodeCount != 0 && m_Scene.DiffuseSurfaces != null && m_Scene.PrepareBrickTexture(this);
                if (m_UseSdf && cache != null)
                {
                    m_CardAlbedoDepth = PassRecorder.ImportTextureForPass(this, cache.AlbedoDepthHandle);
                    m_CardNormalCoverage = PassRecorder.ImportTextureForPass(this, cache.NormalCoverageHandle);
                    m_UseSdf = m_CardAlbedoDepth.IsValid() && m_CardNormalCoverage.IsValid();
                }
            }

            var temporal = frameData.Get<VividTemporalData>();
            bool screenInputs = m_ScreenTraceKernel >= 0 && !m_OffscreenOnly && m_TraceMode != SdfDiffuseTraceMode.SdfOnly && motionConnected;
            bool screenConfigured = screenInputs && m_ScreenHzb != null
                && !ReferenceEquals(m_ScreenHzb, m_LocalHzb);
            m_CorrectReceiverSelfHits = m_UseSdf && useSettings && settings.correctReceiverSelfHits.value;
            m_ScreenCardLightingRequested = m_UseSdf && screenConfigured && useSettings && cache != null
                && settings.screenHitCardLighting.value && settings.surfaceCacheBounceOrders.value > 0;

            bool wantsRestir = useSettings && (legacyGather ? settings.restirGather.value
                : settings.gatherMethod.value == SdfDiffuseGatherMethod.ReSTIRGather);
            bool canRestir = wantsRestir && m_RestirTemporalKernel >= 0 && m_UseSdf && !m_OffscreenOnly;
            m_RestirPixelIntegration = canRestir && (!legacyGather || settings.restirPixelIntegration.value)
                && m_RestirDenoiseKernel >= 0
                && 4 * CoreUtils.DivRoundUp(m_Width, RestirPixelSpacing) <= SystemInfo.maxTextureSize
                && 2 * CoreUtils.DivRoundUp(m_Height, RestirPixelSpacing) <= SystemInfo.maxTextureSize;
            m_RestirGather = m_RestirPixelIntegration
                || (legacyGather && canRestir && m_TraceMode == SdfDiffuseTraceMode.SdfOnly);
            m_RestirOpenPbrDiffuse = m_RestirPixelIntegration && m_OpenPbrResolveKernel >= 0
                && settings.restirOpenPbrDiffuse.value && CoversReceiver(m_OpenPbrParameters)
                && CoversReceiver(m_OpenPbrAux0) && CoversReceiver(m_OpenPbrAux1)
                && PipelineResourceManager.Get<VividRPCoreResources>()?.OpenPBRLuts?.IsReady == true;
            m_AdaptiveProbes &= !m_RestirPixelIntegration;
            m_DirectionalProbes = useSettings && !m_RestirPixelIntegration && (!legacyGather || settings.directionalScreenProbes.value);
            m_SpatialFilter &= !m_RestirGather || legacyGather;
            m_HitDistanceFilter &= !m_RestirGather;
            int spacing = m_RestirPixelIntegration ? RestirPixelSpacing : ProbeSpacing;
            m_ProbeWidth = CoreUtils.DivRoundUp(m_Width, spacing);
            m_ProbeHeight = CoreUtils.DivRoundUp(m_Height, spacing);
            m_AtlasHeight = m_ProbeHeight * (m_AdaptiveProbes ? 2 : 1);
            m_DirectionalCache = useSettings && m_DirectionalProbes && !m_RestirGather && !m_OffscreenOnly
                && m_DirectionalCompositeKernel >= 0 && (!legacyGather || settings.screenProbeDirectionalCache.value)
                && DirectionalGatherBins * m_ProbeWidth <= SystemInfo.maxTextureSize && DirectionalTracePlanes * m_AtlasHeight <= SystemInfo.maxTextureSize;
            m_FullSphere = m_DirectionalCache && !legacyGather && settings.screenProbeFullSphere.value
                && 8 * DirectionalTracePlanes * (m_ProbeHeight + (m_AdaptiveProbes ? (m_ProbeHeight + 1) / 2 : 0)) <= SystemInfo.maxTextureSize;
            m_LumenScreenTrace = screenInputs && m_LumenTraceKernel >= 0 && m_FullSphere && settings.lumenScreenTracing.value;
            screenConfigured |= m_LumenScreenTrace;
            if (!m_UseSdf && !screenConfigured)
                return;
            if (m_LumenScreenTrace)
            {
                m_ScreenCardLightingRequested = false;
                int width = Mathf.Max(1, Mathf.NextPowerOfTwo(m_Width) >> 1);
                int height = Mathf.Max(1, Mathf.NextPowerOfTwo(m_Height) >> 1);
                int heightLog = Mathf.RoundToInt(Mathf.Log(height, 2));
                int mips = Mathf.Max(1, Mathf.RoundToInt(Mathf.Log(Mathf.Max(width, height), 2)));
                m_ClosestHzbSize = new Vector4(width, height, mips, heightLog);
                m_ClosestHzb.Resize(width, 2 * height - 1 + Mathf.Max(0, mips - heightLog - 1));
                var projection = cameraData.GetGPUProjectionMatrix(renderIntoTexture: true);
                m_DepthThicknessScale = camera.orthographic ? Mathf.Pow(10f,
                    -(int)Mathf.Clamp(Mathf.Log10(Mathf.Abs(projection.inverse.m22) * 100f), 0f, 9f)) : 1f;
                m_ClipToPrevClip = temporal.previousViewProjectionMatrix * temporal.nonJitteredViewProjectionMatrix.inverse;
            }
            else
                m_ClosestHzb.Resize(1, 1);
            if (m_FullSphere && m_AdaptiveProbes)
                m_AtlasHeight = m_ProbeHeight + (m_ProbeHeight + 1) / 2;
            if (m_FullSphere && BlueNoise.Instance != null)
            {
                m_ProbeDirectionNoise = BlueNoise.Instance.VectorAtlas;
                if (m_ProbeDirectionNoise.IsValid())
                    PassRecorder.RegisterImportedTextureForPass(this, m_ProbeDirectionNoise);
            }
            if (m_FullSphere && m_SpatialFilter && m_LumenDirectionalFilterKernel >= 0 && BlueNoise.Instance != null)
            {
                m_SpatialFilterScalarNoise = BlueNoise.Instance.ScalarAtlas;
                m_SpatialFilterVectorNoise = BlueNoise.Instance.VectorAtlas;
                PassRecorder.RegisterImportedTextureForPass(this, m_SpatialFilterScalarNoise);
                PassRecorder.RegisterImportedTextureForPass(this, m_SpatialFilterVectorNoise);
            }
            m_DitherTraceDirections = m_FullSphere && settings.screenProbeDitherTraceDirections.value;
            if (m_FullSphere && settings.screenProbeStochasticInterpolation.value && BlueNoise.Instance != null)
            {
                m_InterpolationScalarNoise = BlueNoise.Instance.ScalarAtlas;
                m_InterpolationVectorNoise = BlueNoise.Instance.VectorAtlas;
                m_StochasticInterpolation = m_InterpolationScalarNoise.IsValid() && m_InterpolationVectorNoise.IsValid();
                if (m_StochasticInterpolation)
                {
                    PassRecorder.RegisterImportedTextureForPass(this, m_InterpolationScalarNoise);
                    PassRecorder.RegisterImportedTextureForPass(this, m_InterpolationVectorNoise);
                }
            }
            m_ScatterNoise = m_DitherTraceDirections && BlueNoise.Instance != null ? BlueNoise.Instance.VectorAtlas : default;
            if (m_ScatterNoise.IsValid())
                PassRecorder.RegisterImportedTextureForPass(this, m_ScatterNoise);
            m_FastUpdateNeighborhoodClamp = useSettings && settings.screenProbeFastUpdateNeighborhoodClamp.value;
            m_TemporalRejectBasedOnNormal = !useSettings || settings.screenProbeTemporalRejectBasedOnNormal.value;
            m_TemporalPlaneDisocclusion = !useSettings || settings.screenProbeTemporalPlaneDisocclusion.value;
            m_CoherentRadianceFastUpdate = !useSettings || settings.screenProbeCoherentRadianceFastUpdate.value;
            m_DisocclusionSpatialFilter = !useSettings || settings.screenProbeDisocclusionSpatialFilter.value;
            m_TemporalScalarNoise = m_DirectionalCache && pixelTemporal && BlueNoise.Instance != null
                ? BlueNoise.Instance.ScalarAtlas : default;
            if (m_TemporalScalarNoise.IsValid())
                PassRecorder.RegisterImportedTextureForPass(this, m_TemporalScalarNoise);
            int gatherColumns = m_FullSphere ? 8 : DirectionalGatherBins;
            int directionHeight = m_AtlasHeight * (m_FullSphere ? 8 : 1);
            m_ProbeTemporal = m_DirectionalCache && settings.screenProbeTemporalFilter.value;
            m_MaxRayIntensity = m_DirectionalCache ? settings.screenProbeMaxRayIntensity.value : 0f;
            m_PreExposure = m_DirectionalCache ? VividAutoExposureSystem.ResolvePreExposureBuffer(frameData.Get<VividExposureData>()) : null;
            if (useSettings && m_NearOcclusionKernel >= 0 && !m_OffscreenOnly
                && float.IsFinite(settings.nearOcclusionStrength.value))
            {
                float strength = settings.nearOcclusionStrength.value;
                bool bent = strength > 0f && !m_RestirGather && settings.screenProbeBentNormalAO.value
                    && m_BentSearchKernel >= 0 && m_BentTemporalKernel >= 0;
                if (bent && m_FullSphere && BlueNoise.Instance != null)
                {
                    m_BentBlueNoise = BlueNoise.Instance.VectorAtlas;
                    m_BentDuringIntegration = settings.screenProbeBentNormalApplyDuringIntegration.value
                        && m_LumenBentKernel >= 0 && m_BentBlueNoise.IsValid();
                    if (!settings.screenProbeBentNormalApplyDuringIntegration.value && m_DirectionalCache && pixelTemporal
                        && m_LumenBentHalfKernel >= 0 && m_ScreenProbeAoKernel >= 0 && m_ScreenProbeMomentsAoKernel >= 0
                        && m_BentBlueNoise.IsValid())
                    {
                        m_BentScalarNoise = BlueNoise.Instance.ScalarAtlas;
                        m_BentComposite = m_BentScalarNoise.IsValid();
                        if (m_BentComposite)
                            PassRecorder.RegisterImportedTextureForPass(this, m_BentScalarNoise);
                    }
                    if ((m_BentDuringIntegration || m_BentComposite) && !m_ScatterNoise.IsValid())
                        PassRecorder.RegisterImportedTextureForPass(this, m_BentBlueNoise);
                }
                float worldRadius = settings.nearOcclusionRadius.value;
                if (!float.IsFinite(worldRadius))
                {
                    worldRadius = 0f;
                    if (!bent)
                        strength = 0f;
                }
                bool directional = strength > 0f && !bent && settings.directionalNearOcclusion.value;
                m_NearOcclusion = new Vector4(worldRadius, strength, directional ? 1f : 0f,
                    m_BentComposite ? 3f : m_BentDuringIntegration ? 2f : bent ? 1f : 0f);
                m_DirectionalProbes |= directional && !m_RestirPixelIntegration;
            }
            m_DirectionalGuide = m_DirectionalCache && (m_FullSphere ? m_SphereGuideKernel : m_DirectionalGuideKernel) >= 0 && settings.screenProbeSamplingGuide.value;
            m_DirectionalHierarchicalGuide = m_DirectionalGuide && (m_FullSphere || settings.screenProbeHierarchicalGuide.value);
#if UNITY_EDITOR
            m_FrozenGuideActive = !m_FullSphere && m_DirectionalGuide && EditorTraceCapture != null
                && (EditorGatherHistoryDiagnostics & 8388608) != 0;
#endif
            // Keep legacy resource names for diagnostic replay, but the production
            // directional path has no independent scalar anchor or anchor history.
            m_SpatialAnchor = false;
            m_AnchorPreFilter = false;
            m_PreparedAnchor.Resize(1, 1);
            m_ReconstructedAnchor.Resize(1, 1);
            m_CurrentAnchorStatistics.Resize(1, 1);
            m_Probes.desc.ColorFormat = ProbeFormat;
            m_PreparedAnchor.desc.ColorFormat = ProbeFormat;
            m_ReconstructedAnchor.desc.ColorFormat = ProbeFormat;
            m_DirectionalSpatialScratch.Resize(m_DirectionalCache && m_SpatialFilter ? gatherColumns * m_ProbeWidth : 1,
                m_DirectionalCache && m_SpatialFilter ? directionHeight : 1);
            m_HitDistanceFilter &= !m_DirectionalCache;
            m_ProbeSpatialKernel = m_SpatialFilter && !m_RestirGather && !m_HitDistanceFilter
                ? settings.screenProbeSpatialKernel.value : SdfScreenProbeSpatialKernel.Cross;
            int directionalWidth = m_DirectionalCache ? RaysPerProbe * m_ProbeWidth : 1;
            m_DirectionalGuideTexture.Resize(m_DirectionalGuide && !m_FrozenGuideActive ? directionalWidth : 1,
                m_DirectionalGuide && !m_FrozenGuideActive ? directionHeight : 1);
            m_DirectionalRayTexture.Resize(directionalWidth, m_DirectionalCache ? DirectionalTracePlanes * directionHeight : 1);
            m_DirectionalTemporal.Resize(m_DirectionalCache ? gatherColumns * m_ProbeWidth : 1,
                m_DirectionalCache ? directionHeight : 1);
            int rayCount = m_RestirPixelIntegration ? 1 : RaysPerProbe;
            m_ProbeMoving.Resize(m_DirectionalCache ? m_ProbeWidth : 1, m_DirectionalCache ? m_AtlasHeight : 1);
            m_ResolvedMoving.Resize(m_DirectionalCache ? m_Width : 1, m_DirectionalCache ? m_Height : 1);
            m_HitLighting.Resize(m_ProbeWidth, m_AtlasHeight);
            m_TraceSources.Resize(m_ProbeWidth, m_AtlasHeight);
            m_RayDiagnostics.Resize(m_RecordRayDiagnostics ? m_ProbeWidth * rayCount : 1,
                m_RecordRayDiagnostics ? directionHeight : 1);
            m_FirstHitDiagnostics.Resize(m_RecordFirstHitDiagnostics ? m_ProbeWidth * rayCount : 1,
                m_RecordFirstHitDiagnostics ? 3 * directionHeight : 1);
            m_AdaptivePlacement.Resize(m_AdaptiveProbes ? m_ProbeWidth : 1, m_AdaptiveProbes ? (m_FullSphere ? 5 * m_ProbeHeight + 1 : m_ProbeHeight) : 1);
            m_ScreenRays.Resize(screenConfigured ? m_ProbeWidth * rayCount : 1, screenConfigured ? directionHeight : 1);
            m_AtrousIterations = useSettings && m_AtrousKernel >= 0 ? settings.spatialDenoise.value switch
            {
                SdfDiffuseSpatialDenoise.Atrous2 => 2,
                SdfDiffuseSpatialDenoise.Atrous3 => 3,
                SdfDiffuseSpatialDenoise.Atrous4 => 4,
                _ => 0
            } : 0;
#if UNITY_EDITOR
            if (EditorTraceCapture != null && EditorAtrousIterationOverride == 4 && m_AtrousIterations == 3)
                m_AtrousIterations = 4;
#endif
            bool rawResolved = pixelTemporal || m_RestirPixelIntegration || m_AtrousIterations >= 3;
            m_RawResolved.Resize(rawResolved ? m_Width : 1, rawResolved ? m_Height : 1);
            m_RoughSpecular = m_DirectionalCache && m_FullSphere && m_SpatialFilter && m_LumenDirectionalFilterKernel >= 0
                && m_SpatialFilterScalarNoise.IsValid() && m_SpatialFilterVectorNoise.IsValid()
                && (!useSettings || settings.screenProbeRoughSpecular.value);
            bool rawRoughSpecular = m_RoughSpecular && pixelTemporal && m_ScreenProbeTemporalKernel >= 0;
            m_RawRoughSpecular.Resize(rawRoughSpecular ? m_Width : 1, rawRoughSpecular ? m_Height : 1);
            m_AtrousLighting.Resize(m_AtrousIterations > 0 ? m_Width : 1, m_AtrousIterations > 0 ? m_Height : 1);
            m_AtrousVarianceA.Resize(m_AtrousIterations > 0 ? m_Width : 1, m_AtrousIterations > 0 ? m_Height : 1);
            m_AtrousVarianceB.Resize(m_AtrousIterations >= 3 ? m_Width : 1, m_AtrousIterations >= 3 ? m_Height : 1);
            m_RestirVariance.Resize(m_RestirPixelIntegration ? m_Width : 1, m_RestirPixelIntegration ? m_Height : 1);
            m_SpatialRestir.Resize(m_RestirPixelIntegration ? 2 * m_ProbeWidth : 1, m_RestirPixelIntegration ? m_AtlasHeight : 1);
            bool storeRayData = m_HitDistanceFilter || m_RestirGather;
            // Dense reservoirs tile their eight scalars over two rows to stay within texture-width limits.
            m_HitSamples.Resize(storeRayData ? m_ProbeWidth * (m_RestirPixelIntegration ? 4 : RaysPerProbe) : 1,
                storeRayData ? m_AtlasHeight * (m_RestirPixelIntegration ? 2 : 1) : 1);
            m_RestirCardVersion = m_RestirGather && cache != null ? cache.CardVersion : 0;
            if (m_RestirGather)
            {
                // Two RGBA32F texels store each reservoir, matching the shader's two planes.
                m_CurrentRestir.Resize(2 * m_ProbeWidth, m_AtlasHeight);
                CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, RestirHistoryId,
                    m_PreviousRestir, m_CurrentRestir, m_CurrentRestir.desc, out m_RestirHistory);
            }

            if ((m_CorrectReceiverSelfHits || m_ScreenCardLightingRequested || m_LumenScreenTrace)
                && frameData.Contains<VividSdfReceiverFrameData>() && VividGPUDrivenSystem.HasInstance)
            {
                var receiver = frameData.Get<VividSdfReceiverFrameData>();
                if (receiver.Visibility != null && ReferenceEquals(receiver.Depth, m_Depth)
                    && ReferenceEquals(receiver.GBuffer0, m_GBuffer0) && ReferenceEquals(receiver.GBuffer1, m_GBuffer1))
                {
                    PrepareReceiverEntities(VividGPUDrivenSystem.instance.SceneData);
                    if (m_ReceiverCount > 0)
                        m_ReceiverVisibilitySource = receiver.Visibility;
                }
            }

            m_ViewProjection = cameraData.GetGPUViewProjectionMatrix(renderIntoTexture: true);
            var view = cameraData.GetViewMatrix();
            m_ProbeCameraForward = -view.GetRow(2);
            var cameraPosition = cameraData.GetInverseViewMatrix().GetColumn(3);
            m_ProbeCameraPosition = new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 1f);
            if (m_StochasticInterpolation)
                m_InterpolationFlipY = (cameraData.GetGPUProjectionMatrix(renderIntoTexture: true).m11 > 0f)
                    != SystemInfo.graphicsUVStartsAtTop;
            if (m_BentDuringIntegration || m_BentComposite)
            {
                m_BentWorldToView = view;
                m_BentInverseProjection = cameraData.GetGPUProjectionMatrix(renderIntoTexture: true).inverse;
                m_BentFlipY = (m_BentInverseProjection.m11 > 0f) != SystemInfo.graphicsUVStartsAtTop;
            }
            if (m_BentDuringIntegration)
            {
                m_BentFurthestHzb.Resize(CoreUtils.DivRoundUp(m_Width, 2), CoreUtils.DivRoundUp(m_Height, 2));
                m_LumenBentNormal.Resize(m_Width, m_Height);
            }
            if (m_BentComposite)
                m_LumenBentNormal.Resize(CoreUtils.DivRoundUp(m_Width, 2), CoreUtils.DivRoundUp(m_Height, 2));
            m_LayerMask = camera.cullingMask;
            m_TraceDistance = float.IsFinite(m_MaxDistance) ? Mathf.Max(0.1f, m_MaxDistance) : 12f;
            if (useSettings && m_UseSdf && !m_OffscreenOnly && settings.segmentedSdfTracing.value)
                m_MeshDetailDistance = Mathf.Clamp(
                    float.IsFinite(settings.meshSdfDetailDistance.value) ? settings.meshSdfDetailDistance.value : 2f,
                    0.1f, m_TraceDistance);
            m_Bias = float.IsFinite(m_RayBias) ? Mathf.Clamp(m_RayBias, 0.001f, 0.25f) : 0.04f;
            ReadLight(camera);
            bool farConfigured = useSettings && settings.farRadianceCache.value && m_UseSdf && cache != null
                && m_FarKernel >= 0 && !m_OffscreenOnly && (!m_RestirGather || m_RestirPixelIntegration)
                && float.IsFinite(settings.farTraceDistance.value)
                && (m_FullSphere ? settings.farTraceDistance.value > 2f * Mathf.Sqrt(3f) * (50f / 48f)
                    : settings.farTraceDistance.value > m_TraceDistance + FarCellSize);
            m_WorldWork.Resize(farConfigured && m_FullSphere ? 1024 : 1, farConfigured && m_FullSphere ? 445 : 1);
            if (m_UseSdf)
            {
                m_SkyTexture = PassRecorder.ImportTextureForPass(this, SkyManager.GetSkySourceCubemapHandle());
                if (useSettings && settings.occludedSky.value && (!m_RestirGather || farConfigured)
                    && !m_OffscreenOnly && m_SkyTexture.IsValid())
                    PrepareOccludedSky(frameData.GetOrCreate<VividSkyData>(), settings.skyTraceDistance.value);
            }
            m_Cameras.PurgeDestroyedCameras();
            m_State = m_Cameras.GetOrCreateBase(camera);
            if (m_UseSdf && cache != null && m_RadiosityKernel >= 0 && settings.surfaceCacheBounceOrders.value > 0)
            {
                m_RadiosityFeedback = settings.surfaceCacheRadiosityFeedback.value && m_RadiosityFeedbackSupported
                    && m_CardFinalLightingKernel >= 0 && m_MeshDetailDistance > 0f && m_FullSphere
                    && m_FarKernel >= 0 && m_ClipmapNear != null;
                PrepareRadiosity(camera, cache, settings.surfaceCacheBounceOrders.value,
                    settings.surfaceCacheCardsPerFrame.value, settings.surfaceCacheFullUpdateCardLimit.value);
            }
            if (m_UseSdf && cache != null && m_CardFinalLightingKernel >= 0)
                PrepareFinalLighting(camera, cache);
            m_LumenDebugView.Prepare(this, frameData, camera, m_Width, m_Height, debugMode,
                m_CardFinalAtlas.IsValid() ? (int)m_FinalLightingLayout.w : 0,
                m_FinalLightingPreviousReadyCards == 0 || !ReferenceEquals(m_State.Cache, cache));
            bool captureFeedback = m_SurfaceCacheFeedbackSupported && m_CardFinalAtlas.IsValid()
                && (LumenDebugViewPass.IsFeedbackView(debugMode) || VividRenderingDebugDisplaySettings.Data.lumenCaptureVisualizationFeedback);
            m_LumenDebugView.PrepareFeedback(this, camera, captureFeedback ? (int)m_FinalLightingLayout.w : 0,
                !ReferenceEquals(m_State.Cache, cache) || m_State.FinalLightingCardVersion != (cache?.CardVersion ?? 0)
                    || m_State.RadiosityLayoutVersion != m_Scene.RadiosityLayoutVersion);
            // Dense reservoirs and card lighting need the exact raster hit pixel.
            bool storeScreenPixel = screenConfigured && (m_RestirPixelIntegration
                || (m_ScreenCardLightingRequested && m_ReceiverVisibilitySource != null && m_RadiosityInput.IsValid()));
            m_ScreenRays.desc.ColorFormat = (m_LumenScreenTrace || storeScreenPixel) ? GeometryFormat : ProbeFormat;
            if (m_MeshDetailDistance > 0f && m_FullSphere && m_FarKernel >= 0 && m_ClipmapNear != null)
            {
                var grid = m_State.ObjectGrid ??= new VividGlobalSdfObjectGrid();
                grid.Prepare(camera.transform.position, m_Scene, m_LayerMask);
                m_ObjectGrid = PassRecorder.ImportBufferForPass(this, grid.Objects, grid.Dirty ? AccessFlags.ReadWrite : AccessFlags.Read);
                m_ObjectPageTable = PassRecorder.ImportBufferForPass(this, grid.Table, AccessFlags.Read);
                m_ObjectPageWork = PassRecorder.ImportBufferForPass(this, grid.PageWork, AccessFlags.Read);
                m_CompactTracePrepared = m_CompactTraceSupported && m_LumenScreenTrace
                    && cache != null && m_ObjectGrid.IsValid() && !m_RecordFirstHitDiagnostics;
                if (m_CompactTracePrepared)
                    SizeCompactTraceBuffers(m_ProbeWidth * m_AtlasHeight);
            }
            if (farConfigured)
                PrepareFarCache(camera, cache, settings.farTraceDistance.value);
            bool bentNormal = m_NearOcclusion.w == 1f || m_BentComposite;
            if (screenConfigured || pixelTemporal || bentNormal || m_DirectionalCache)
            {
                var jitter = cameraData.GetGPUProjectionMatrix(renderIntoTexture: true)
                    * cameraData.GetGPUProjectionMatrixNoJitter(renderIntoTexture: true).inverse;
                m_ScreenJitterUv = new Vector2(jitter.m03, SystemInfo.graphicsUVStartsAtTop ? -jitter.m13 : jitter.m13) * 0.5f;
                m_ScreenJitterDelta = m_State.ScreenJitterUv - m_ScreenJitterUv;
            }
            if (screenConfigured)
                PrepareScreenHistory(frameData, camera, temporal);
            if (pixelTemporal || bentNormal)
            {
                m_CurrentPixelGeometry.Resize(m_Width, m_Height);
                CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, PixelGeometryHistoryId,
                    m_PreviousPixelGeometry, m_CurrentPixelGeometry, m_CurrentPixelGeometry.desc, out m_PixelGeometryHistory, AccessFlags.Write);
            }
            if (pixelTemporal)
                PreparePixelHistory(camera);
            if (m_RoughSpecular)
            {
                m_CurrentRoughSpecular.Resize(m_Width, m_Height);
                CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, RoughSpecularHistoryId,
                    m_PreviousRoughSpecular, m_CurrentRoughSpecular, m_CurrentRoughSpecular.desc, out m_RoughSpecularHistory, AccessFlags.Write);
                frameData.GetOrCreate<VividSdfDiffuseFrameData>().RoughSpecularHistory = m_RoughSpecularHistory;
            }
            if (useSettings && !m_OffscreenOnly)
            {
                m_LumenReflections.Prepare(this, frameData, camera, m_Width, m_Height, settings,
                    temporal.isFirstFrame || temporal.resetPostProcessingHistory);
            }
            if (bentNormal)
            {
                if (!m_BentComposite)
                    m_BentSamples.Resize(CoreUtils.DivRoundUp(m_Width, 2), CoreUtils.DivRoundUp(m_Height, 2));
                m_CurrentBentNormal.desc.ColorFormat = m_BentComposite ? PixelGeometryFormat : ProbeFormat;
                m_CurrentBentNormal.Resize(m_Width, m_Height);
                m_BentNormalHistoryValid = CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, BentNormalHistoryId,
                    m_PreviousBentNormal, m_CurrentBentNormal, m_CurrentBentNormal.desc, out m_BentNormalHistory, AccessFlags.Write)
                    && motionConnected && m_PixelGeometryHistory != null && m_PixelGeometryHistory.IsValid()
                    && m_State.NearOcclusion.w == m_NearOcclusion.w && m_State.NearOcclusion.y == m_NearOcclusion.y
                    && !temporal.isFirstFrame && !temporal.resetPostProcessingHistory;
                frameData.GetOrCreate<VividSdfDiffuseFrameData>().BentNormalHistory = m_BentNormalHistory;
            }
            int atlasWidth = m_ProbeWidth * (m_DirectionalProbes ? DirectionalProbePlanes : 1);
            m_Probes.Resize(m_RestirPixelIntegration ? 1 : atlasWidth, m_RestirPixelIntegration ? 1 : m_AtlasHeight);
            bool needsProbeScratch = m_SpatialFilter || m_RestirGather || m_DirectionalCache;
            // The dense path keeps the exact far or sky direction alongside its radiance.
            int scratchWidth = m_DirectionalCache
                ? (m_RecordProbeAnchorDiagnostics ? DirectionalAnchorDiagnosticPlanes : DirectionalIrradiancePlanes) * m_ProbeWidth
                : m_RestirPixelIntegration && (m_NearOcclusion.z > 0f || m_RestirOpenPbrDiffuse)
                    ? 2 * m_ProbeWidth : atlasWidth;
            m_FilteredProbes.Resize(needsProbeScratch ? scratchWidth : 1, needsProbeScratch ? m_AtlasHeight : 1);
            m_Geometry.Resize(m_ProbeWidth, m_AtlasHeight);
            bool probesValid = CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, ProbeHistoryId,
                m_PreviousProbes, m_Probes, m_Probes.desc, out m_ProbeHistory);
            bool geometryValid = CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, GeometryHistoryId,
                m_PreviousGeometry, m_Geometry, m_Geometry.desc, out m_GeometryHistory);
            if (m_DirectionalCache)
            {
                int gatherWidth = gatherColumns * m_ProbeWidth;
                m_CurrentDirectional.Resize(gatherWidth, DirectionalTracePlanes * directionHeight);
                m_DirectionalHistoryValid = CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, DirectionalHistoryId,
                    m_PreviousDirectional, m_CurrentDirectional, m_CurrentDirectional.desc, out m_DirectionalHistory);
                m_CurrentDirectionalFiltered.Resize(gatherWidth, directionHeight);
                m_DirectionalGuideHistoryValid = CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, DirectionalGuideHistoryId,
                    m_PreviousDirectionalFiltered, m_CurrentDirectionalFiltered, m_CurrentDirectionalFiltered.desc, out m_DirectionalGuideHistory);
                if (m_FrozenGuideActive)
                {
                    m_CurrentProposal.Resize(RaysPerProbe * m_ProbeWidth, m_AtlasHeight);
                    m_ProposalHistoryValid = CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, ProposalHistoryId,
                        m_PreviousProposal, m_CurrentProposal, m_CurrentProposal.desc, out m_ProposalHistory);
                }
            }
            bool lightingMatches = MatchesEnvironmentLighting(m_State.SkyContentHash, m_State.SkyTextureParams, m_State.SkyTextureTint)
                && MatchesPreviousDirectionalLighting();
            m_State.DynamicLightingFastUpdateFrames = StepDynamicLightingFastUpdateWindow(
                m_State.DynamicLightingFastUpdateFrames,
                hasPreviousState: m_State.Width > 0 && !temporal.isFirstFrame && !temporal.resetPostProcessingHistory,
                lightingChanged: !lightingMatches);
            m_DynamicLightingFastUpdateActive = m_State.DynamicLightingFastUpdateFrames > 0;
            m_TemporalCameraStatic = !HasCameraMotion(
                m_State.ProbeCameraPosition,
                m_ProbeCameraPosition,
                m_State.ProbeCameraForward,
                m_ProbeCameraForward);
            m_HasHistory = probesValid && geometryValid
                && (m_State.SceneRevision == m_Scene.Revision
                    || m_FullSphere && m_State.SceneNonMotionVersion == m_Scene.NonMotionVersion
                    || PreserveRelitHistory && m_State.SceneTransportVersion == m_Scene.TransportVersion)
                && ReferenceEquals(m_State.Cache, m_Scene.SurfaceCache)
                && m_State.OffscreenOnly == m_OffscreenOnly && m_State.TraceMode == m_TraceMode
                && m_State.DirectionalProbes == m_DirectionalProbes
                && ((m_State.NearOcclusion.z == 0f && m_NearOcclusion.z == 0f) || m_State.NearOcclusion.Equals(m_NearOcclusion))
                && m_State.DirectionalCache == m_DirectionalCache && m_State.DirectionalGuide == m_DirectionalGuide
                && m_State.LumenScreenTrace == m_LumenScreenTrace && m_State.FullSphere == m_FullSphere && m_State.ProbeTemporal == m_ProbeTemporal
                && m_State.DitherTraceDirections == m_DitherTraceDirections
                && m_State.MaxRayIntensity == m_MaxRayIntensity
                && m_State.DirectionalHierarchicalGuide == m_DirectionalHierarchicalGuide
                && m_State.AdaptiveProbes == m_AdaptiveProbes && m_State.SpatialFilter == m_SpatialFilter
                && m_State.HitDistanceFilter == m_HitDistanceFilter
                && m_State.ProbeSpatialKernel == m_ProbeSpatialKernel
                && m_State.SpatialAnchor == m_SpatialAnchor
                && m_State.AnchorPreFilter == m_AnchorPreFilter
                && m_State.ProbeAnchorMaxAge == m_ProbeAnchorMaxAge
                && m_State.RestirGather == m_RestirGather && m_State.RestirCardVersion == m_RestirCardVersion
                && m_State.RestirPixelIntegration == m_RestirPixelIntegration
                && m_State.GatherRadiosityOrder == PublishedRadiosityOrder
                && m_State.RadiositySamples == m_RadiositySamples && m_State.RadiositySeed == m_RadiositySeed
                && m_State.RadiosityAccumulate == m_RadiosityAccumulate
                && m_State.RadiosityCardVersion == m_RadiosityCardVersion
                && m_State.FarDistance == m_FarDistance && m_State.FarCardVersion == m_FarCardVersion
                && m_State.Width == m_Width && m_State.Height == m_Height
                && m_State.LayerMask == m_LayerMask && (m_FullSphere || m_State.LightLayerMask == m_LightLayerMask)
                && m_State.MaxDistance == m_TraceDistance && m_State.RayBias == m_Bias
                && m_State.MeshDetailDistance == m_MeshDetailDistance
                && m_State.SkyTraceDistance == m_SkyTraceDistance
                && AllowsContinuousLightingHistory(m_FullSphere, PreserveRelitHistory,
                    MatchesEnvironmentLighting(m_State.SkyContentHash, m_State.SkyTextureParams, m_State.SkyTextureTint))
                && AllowsContinuousLightingHistory(m_FullSphere, PreserveRelitHistory, MatchesPreviousDirectionalLighting())
                && !temporal.isFirstFrame && !temporal.resetPostProcessingHistory;
        }

        private void PrepareOccludedSky(VividSkyData sky, float maxDistance)
        {
            if (sky.activeSkyType == SkyType.None || !SkyManager.HasValidSkyTexture(sky.specularCubemap))
                return;
            // Keep the directional sun in the direct-light path.
            if (sky.activeSkyType == SkyType.PhysicallyBased
                && SkySettingsVolume.GetIncludeSunInBaking(VividVolumeManagerUtility.GetSkySettingsVolume())
                && VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume()?.renderSunDisk.value == true)
                return;

            m_SkyTraceDistance = Mathf.Max(m_TraceDistance, float.IsFinite(maxDistance) ? maxDistance : 64f);
            m_SkyTextureParams = DeferredLightingPass.BuildSkyTextureParams(0, sky.exposure, sky.rotation, true);
            m_SkyTextureTint = sky.tint;
            m_SkyContentHash = sky.skyContentHash;
            m_SkyBoundSize = sky.specularCubemap.width;
        }

        private void PrepareReceiverEntities(VividGPUDrivenSceneData scene)
        {
            var sources = scene.InstanceSources;
            int count = sources.Count;
            if (count == 0)
            {
                m_ReceiverCount = 0;
                return;
            }

            bool lookupChanged = m_ReceiverLookupRevision != m_Scene.Revision;
            if (lookupChanged)
                m_Scene.CopyInstanceIndices(m_SdfInstancesByEntity);

            bool rebuild = lookupChanged || m_ReceiverCount != count;
            if (m_ReceiverEntities == null || m_ReceiverEntities.count < count)
            {
                int capacity = Mathf.NextPowerOfTwo(count);
                var sourcesBuffer = new ReceiverSource[capacity];
                var entitiesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 4 * sizeof(uint));
                m_ReceiverEntities?.Dispose();
                m_ReceiverEntities = entitiesBuffer;
                m_ReceiverSources = sourcesBuffer;
                rebuild = true;
            }
            bool changed = rebuild;
            for (int index = 0; index < count; ++index)
            {
                ulong entity = EntityId.ToULong(sources[index].PrimitiveEntityId);
                ref var source = ref m_ReceiverSources[index];
                uint low = (uint)entity;
                uint high = (uint)(entity >> 32);
                if (!rebuild && source.EntityLow == low && source.EntityHigh == high)
                    continue;
                source.EntityLow = low;
                source.EntityHigh = high;
                source.SdfInstance = m_SdfInstancesByEntity.TryGetValue(entity, out uint instance) ? instance : uint.MaxValue;
                changed = true;
            }
            if (changed)
            {
                // A failed upload must be retried even if the CPU mirror already matches.
                m_ReceiverCount = 0;
                m_ReceiverEntities.SetData(m_ReceiverSources, 0, 0, count);
            }
            m_ReceiverCount = count;
            m_ReceiverLookupRevision = m_Scene.Revision;
        }

        public void PrepareRenderGraph(ContextContainer frameData)
        {
            if (!IsActive(frameData))
                return;

            if (m_ReceiverVisibilitySource != null)
            {
                m_ReceiverVisibility = m_ReceiverVisibilitySource.innerHandle;
                PassRecorder.RegisterImportedTextureForPass(this, m_ReceiverVisibility);
            }
            if (m_MeshDetailDistance <= 0f || !frameData.Contains<VividGlobalSdfFrameData>())
                return;
            var clipmap = frameData.Get<VividGlobalSdfFrameData>();
            if (clipmap.State == null || clipmap.LayerMask != m_LayerMask
                || !ReferenceEquals(m_ClipmapNear, clipmap.Outputs[0]))
                return;
            m_GlobalNear = m_ClipmapNear.innerHandle;
            m_GlobalMiddle = clipmap.Outputs[1].innerHandle;
            m_GlobalFar = clipmap.Outputs[2].innerHandle;
            m_GlobalMip = clipmap.Mip.innerHandle;
            m_GlobalPages = clipmap.Pages.innerHandle;
            m_GlobalThinBricks = clipmap.ThinBricks.innerHandle;
            if (!m_GlobalNear.IsValid() || !m_GlobalMiddle.IsValid() || !m_GlobalFar.IsValid()
                || !m_GlobalPages.IsValid() || !m_GlobalThinBricks.IsValid())
                return;

            // Share the producer's handles so the graph orders the atlas and page-table accesses.
            PassRecorder.RegisterImportedTextureForPass(this, m_GlobalMiddle);
            PassRecorder.RegisterImportedTextureForPass(this, m_GlobalFar);
            PassRecorder.RegisterImportedTextureForPass(this, m_GlobalMip);
            PassRecorder.RegisterImportedBufferForPass(this, m_GlobalPages);
            PassRecorder.RegisterImportedBufferForPass(this, m_GlobalThinBricks);
            m_GlobalClipmap = clipmap;
        }

        private void SizeCompactTraceBuffers(int probeCount)
        {
            int rayCount = m_CompactTracePrepared ? probeCount * CompactRaysPerProbe : 1;
            m_CompactTraceMayRetryMesh = m_CompactTracePrepared && m_MeshDetailDistance > CompactWorldNearDistanceFloor;
            m_CompactProbes.desc.Count = probeCount;
            m_CompactContributions.desc.Count = rayCount;
            m_CompactMeshRays.desc.Count = rayCount;
            m_CompactMeshResults.desc.Count = rayCount;
            m_MeshCardGrid.desc.Count = m_CompactTracePrepared ? MeshCardGridX * MeshCardGridY * MeshCardGridZ * MeshCardGridStride : 1;
            m_CompactGlobalRays.desc.Count = rayCount;
            m_CompactGlobalResults.desc.Count = rayCount;
            m_CompactRetryRays.desc.Count = m_CompactTraceMayRetryMesh ? rayCount : 1;
        }

        public override void Record(ComputePassContext context)
        {
            if (m_State == null || m_GeometryHistory == null || !m_Geometry.innerHandle.IsValid()
                || (!m_DirectionalCache && (m_ProbeHistory == null || !m_Probes.innerHandle.IsValid()))
                || !m_Output.innerHandle.IsValid() || !m_HitLighting.innerHandle.IsValid() || !m_TraceSources.innerHandle.IsValid()
                || !m_Depth.innerHandle.IsValid() || !m_GBuffer0.innerHandle.IsValid() || !m_GBuffer1.innerHandle.IsValid())
                return;

            var cmd = context.cmd;
            RecordSkyRadianceBound(context);
            var cache = m_UseSdf ? m_Scene.SurfaceCache : null;
            bool useScreenRays = m_HasScreenHistory && m_ScreenRays.innerHandle.IsValid()
                && (m_LumenScreenTrace ? m_ClosestHzb.innerHandle.IsValid() : m_ScreenHzb.innerHandle.IsValid()) && m_ScreenMotion.innerHandle.IsValid();
            bool validScreenRadiance = useScreenRays && (m_LumenScreenTrace || !m_UseSdf || HasCurrentScreenLighting());
            bool repairScreenRays = !m_LumenScreenTrace && m_RealtimeOnly && validScreenRadiance && !m_OffscreenOnly;
            int traceKernel = !m_UseSdf ? m_ScreenGatherKernel : cache != null ? m_CachedTraceKernel : m_TraceKernel;
            int receiverKernel = cache != null ? m_ReceiverCachedKernel : m_ReceiverTraceKernel;
            bool hasReceiverSource = m_UseSdf && m_ReceiverVisibility.IsValid() && receiverKernel >= 0 && m_ReceiverCount > 0;
            bool correctReceiver = hasReceiverSource && m_CorrectReceiverSelfHits;
            bool screenCardLighting = hasReceiverSource && useScreenRays && m_ScreenCardLightingRequested && m_RadiosityInput.IsValid();
            if (hasReceiverSource)
                traceKernel = receiverKernel;
            bool useGlobalClipmap = m_UseSdf && m_GlobalClipmap?.Recorded == true;
            bool compactTrace = m_CompactTracePrepared && useGlobalClipmap
                && m_CompactProbes.IsValid() && m_CompactContributions.IsValid() && m_CompactCounts.IsValid()
                && m_CompactMeshRays.IsValid() && m_CompactMeshResults.IsValid()
                && m_CompactGlobalRays.IsValid() && m_CompactRetryRays.IsValid()
                && m_CompactGlobalResults.IsValid() && m_MeshCardGrid.IsValid()
                && m_CompactMeshArgs.IsValid() && m_CompactMeshHitArgs.IsValid() && m_CompactGlobalArgs.IsValid()
                && (!m_CompactTraceMayRetryMesh || (m_CompactRetryMeshArgs.IsValid() && m_CompactRetryGlobalArgs.IsValid()));
            int receiverVariant = hasReceiverSource ? 1 : 0;
            if (compactTrace)
                traceKernel = m_CompactMeshKernels[receiverVariant];
            else if (m_LumenDebugView.HasSurfaceCacheFeedback)
                traceKernel = m_FeedbackTraceKernels[receiverVariant];
            if (hasReceiverSource)
            {
                BindTraceReceiver(cmd, traceKernel);
            }
            cmd.SetComputeIntParam(m_TraceCompute, LumenTraceId, m_LumenScreenTrace ? 1 : 0);
            cmd.SetComputeIntParam(m_TraceCompute, CorrectReceiverId, correctReceiver ? 1 : 0);
            cmd.SetComputeIntParam(m_TraceCompute, ScreenCardLightingId, screenCardLighting ? 1 : 0);
            cmd.SetComputeIntParam(m_TraceCompute, ScreenMaterialLookupId, hasReceiverSource ? 1 : 0);
            BindReceiver(cmd, m_TraceCompute, traceKernel);
            TextureHandle probeTarget = m_Probes.innerHandle;
            if (m_DirectionalCache)
                probeTarget = m_DirectionalRayTexture.innerHandle;
            else if (m_HitDistanceFilter || m_RestirGather)
                probeTarget = m_FilteredProbes.innerHandle;
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, ProbeId, probeTarget);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, GeometryId, m_Geometry.innerHandle);
            if (m_UseSdf)
            {
                m_Scene.Bind(cmd, m_TraceCompute, traceKernel);
                if (useGlobalClipmap && m_ObjectGrid.IsValid() && m_State.ObjectGrid.Dirty)
                {
                    var grid = m_State.ObjectGrid;
                    if (grid.PageCount > 0)
                    {
                        m_Scene.Bind(cmd, m_FarCompute, m_ObjectGridKernel);
                        cmd.SetComputeIntParam(m_FarCompute, ObjectCountId, grid.PageCount);
                        cmd.SetComputeIntParam(m_FarCompute, GlobalLayerMaskId, m_LayerMask);
                        cmd.SetComputeBufferParam(m_FarCompute, m_ObjectGridKernel, ObjectWorkId, m_ObjectPageWork);
                        cmd.SetComputeBufferParam(m_FarCompute, m_ObjectGridKernel, ObjectGridWriteId, m_ObjectGrid);
                        cmd.DispatchCompute(m_FarCompute, m_ObjectGridKernel, Mathf.Min(256, grid.PageCount),
                            CoreUtils.DivRoundUp(grid.PageCount, 256), 1);
                    }
                    grid.Dirty = false;
                }
                BindSegmentedTracing(cmd, m_TraceCompute, traceKernel, useGlobalClipmap);
                cmd.SetComputeBufferParam(m_TraceCompute, traceKernel, SurfaceId, m_Scene.DiffuseSurfaces);
                BindOccludedSky(cmd, m_TraceCompute, traceKernel);
            }
            if (cache != null)
            {
                cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, CardAlbedoDepthId, m_CardAlbedoDepth);
                cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, CardNormalCoverageId, m_CardNormalCoverage);
                cmd.SetComputeBufferParam(m_TraceCompute, traceKernel, LocalCardTableId, cache.LocalCardTable);
            }
            bool hasHistory = m_HasHistory && m_PreviousGeometry.innerHandle.IsValid()
                && (m_DirectionalCache || m_PreviousProbes.innerHandle.IsValid());
            var black = context.renderGraphContext.defaultResources.blackTexture;
            var placement = m_AdaptiveProbes ? m_AdaptivePlacement.innerHandle : black;
            if ((m_AdaptiveProbes && !placement.IsValid())
                || ((m_SpatialFilter || m_RestirGather || m_DirectionalCache) && !m_FilteredProbes.innerHandle.IsValid())
                || (m_DirectionalCache && (m_DirectionalHistory == null || !m_CurrentDirectional.innerHandle.IsValid()
                    || m_DirectionalGuideHistory == null || !m_CurrentDirectionalFiltered.innerHandle.IsValid()
                    || !m_DirectionalTemporal.innerHandle.IsValid() || !m_DirectionalRayTexture.innerHandle.IsValid()
                    || (m_SpatialFilter && !m_DirectionalSpatialScratch.innerHandle.IsValid())))
                || (m_DirectionalGuide && !(m_FrozenGuideActive ? m_CurrentProposal.innerHandle : m_DirectionalGuideTexture.innerHandle).IsValid())
                || (m_SpatialAnchor && !m_PreparedAnchor.innerHandle.IsValid())
                || (m_AnchorPreFilter && !m_ReconstructedAnchor.innerHandle.IsValid())
                || (m_RestirGather && (m_RestirHistory == null || !m_CurrentRestir.innerHandle.IsValid()))
                || (m_RestirPixelIntegration && (!m_RestirVariance.innerHandle.IsValid() || !m_RawResolved.innerHandle.IsValid()))
                || !m_SpatialRestir.innerHandle.IsValid()
                || !m_HitSamples.innerHandle.IsValid())
                return;
            bool pixelTemporal = m_PixelHistory != null && m_PixelGeometryHistory != null && m_ScreenMotion.innerHandle.IsValid()
                && m_RawResolved.innerHandle.IsValid() && m_CurrentPixelLighting.innerHandle.IsValid()
                && m_CurrentPixelGeometry.innerHandle.IsValid() && m_PreviousPixelLighting.innerHandle.IsValid()
                && m_PreviousPixelGeometry.innerHandle.IsValid();
            bool restirVarianceTemporal = pixelTemporal && m_RestirVarianceHistory != null
                && m_PreviousRestirVariance.innerHandle.IsValid() && m_CurrentRestirVariance.innerHandle.IsValid();
            bool compositeBent = pixelTemporal && m_BentComposite && m_BentNormalHistory != null
                && m_PreviousBentNormal.innerHandle.IsValid() && m_CurrentBentNormal.innerHandle.IsValid()
                && m_LumenBentNormal.innerHandle.IsValid();
            m_RestirOpenPbrDiffuse &= m_OpenPbrParameters?.innerHandle.IsValid() == true
                && m_OpenPbrAux0?.innerHandle.IsValid() == true && m_OpenPbrAux1?.innerHandle.IsValid() == true;
            hasHistory &= m_State.HasScreenRays == useScreenRays && m_State.PixelTemporal == pixelTemporal
                && m_State.RestirOpenPbrDiffuse == m_RestirOpenPbrDiffuse
                && m_State.CorrectReceiverSelfHits == correctReceiver && m_State.ScreenCardLighting == screenCardLighting
                && m_State.GlobalClipmapUsed == useGlobalClipmap;
            bool wideProbeFilter = m_ProbeSpatialKernel != SdfScreenProbeSpatialKernel.Cross;
            bool useProbeHistory = hasHistory && (!pixelTemporal || wideProbeFilter) && !m_RestirGather && !m_DirectionalCache;
            bool useDirectionalHistory = m_DirectionalCache && hasHistory && m_DirectionalHistoryValid
                && m_PreviousDirectional.innerHandle.IsValid();
            bool useDirectionalGuideHistory = m_DirectionalGuide && hasHistory && m_DirectionalGuideHistoryValid
                && m_PreviousDirectionalFiltered.innerHandle.IsValid();
            BindDirectionalGuideMode(cmd, m_TraceCompute);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, ProbeDirectionNoiseId,
                m_ProbeDirectionNoise.IsValid() ? m_ProbeDirectionNoise : black);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, DirectionalGuideId, m_DirectionalGuide
                ? (m_FrozenGuideActive ? m_CurrentProposal.innerHandle : m_DirectionalGuideTexture.innerHandle) : black);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, PreviousProbeId, useProbeHistory ? m_PreviousProbes.innerHandle : black);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, PreviousGeometryId, useProbeHistory ? m_PreviousGeometry.innerHandle : black);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, ProbeMovingOutputId, m_ProbeMoving.innerHandle);
            var cameraData = context.Get<VividCameraData>();
            cmd.SetComputeIntParam(m_TraceCompute, MotionFrameId, cameraData.frameIndex);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, HitLightingId, m_HitLighting.innerHandle);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, TraceSourcesId, m_TraceSources.innerHandle);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, RayDiagnosticsId, m_RayDiagnostics.innerHandle);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, FirstHitDiagnosticsId, m_FirstHitDiagnostics.innerHandle);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, HitSamplesId, m_HitSamples.innerHandle);
            cmd.SetComputeIntParam(m_TraceCompute, HitFilterEnabledId, m_HitDistanceFilter ? 1 : 0);
            cmd.SetComputeIntParam(m_TraceCompute, RestirEnabledId, m_RestirGather ? 1 : 0);
            cmd.SetComputeIntParam(m_TraceCompute, RestirOpenPbrDiffuseId, m_RestirOpenPbrDiffuse ? 1 : 0);
            cmd.SetComputeIntParam(m_TraceCompute, RecordRayDiagnosticsId, m_RecordRayDiagnostics ? 1 : 0);
            cmd.SetComputeIntParam(m_TraceCompute, RecordFirstHitDiagnosticsId, m_RecordFirstHitDiagnostics ? 1 : 0);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, ScreenRaysId, useScreenRays ? m_ScreenRays.innerHandle : black);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, PlacementId, placement);
            cmd.SetComputeMatrixParam(m_TraceCompute, PreviousViewProjectionId, m_State.ViewProjection);
            m_InverseViewProjection = m_ViewProjection.inverse;
            BindProbeFrame(cmd, m_TraceCompute);
            BindSdfTraceConstants(cmd, m_TraceCompute);
            BindSunLighting(cmd, m_TraceCompute);
            int frame = unchecked((int)m_State.Frame++);
#if UNITY_EDITOR
            if (EditorTraceCapture != null && EditorGatherFrameOverride >= 0)
                frame = EditorGatherFrameOverride;
#endif
            cmd.SetComputeIntParam(m_TraceCompute, FrameId, frame);
            cmd.SetComputeIntParam(m_TraceCompute, HasHistoryId, useProbeHistory ? 1 : 0);
            cmd.SetComputeIntParam(m_TraceCompute, ProbeSpatialKernelId, (int)m_ProbeSpatialKernel);
            cmd.SetComputeFloatParam(m_TraceCompute, ProbeHistoryBlendId, pixelTemporal ? 0.55f : 0.75f);
            cmd.SetComputeIntParam(m_TraceCompute, DirectionalProbesId, m_DirectionalProbes ? 1 : 0);
            cmd.SetComputeIntParam(m_TraceCompute, SpatialFilterId, m_SpatialFilter || m_RestirGather ? 1 : 0);
            cmd.SetComputeIntParam(m_TraceCompute, OffscreenOnlyId, m_OffscreenOnly ? 1 : 0);
            cmd.SetComputeMatrixParam(m_TraceCompute, ViewProjectionId, m_ViewProjection);
            cmd.SetComputeIntParam(m_TraceCompute, UseScreenRaysId, useScreenRays ? 1 : 0);
            cmd.SetComputeVectorParam(m_TraceCompute, NearOcclusionId, m_NearOcclusion);
            int groupsX = CoreUtils.DivRoundUp(m_ProbeWidth, GroupSize);
            int groupsY = CoreUtils.DivRoundUp(m_AtlasHeight, GroupSize);
            using (new ProfilingScope(cmd, TotalSampler))
            {
                BindProbeFrame(cmd, m_GatherCompute);
                cmd.SetComputeMatrixParam(m_GatherCompute, ViewProjectionId, m_ViewProjection);
                cmd.SetComputeIntParam(m_GatherCompute, FrameId, frame);
                cmd.SetComputeIntParam(m_GatherCompute, DirectionalProbesId, m_DirectionalProbes ? 1 : 0);
                cmd.SetComputeIntParam(m_GatherCompute, SpatialFilterId, m_SpatialFilter || m_RestirGather ? 1 : 0);
                cmd.SetComputeIntParam(m_GatherCompute, RestirOpenPbrDiffuseId, m_RestirOpenPbrDiffuse ? 1 : 0);
                cmd.SetComputeVectorParam(m_GatherCompute, NearOcclusionId, m_NearOcclusion);
                cmd.SetComputeFloatParam(m_GatherCompute, RayBiasId, m_Bias);
                cmd.SetComputeFloatParam(m_GatherCompute, MaxDistanceId, m_TraceDistance);
                cmd.SetComputeIntParam(m_GatherCompute, ProbeResolveRepairModeId, repairScreenRays ? 2 : m_RealtimeOnly ? 1 : 0);
                if (m_AdaptiveProbes)
                {
                    using var scope = new ProfilingScope(cmd, PlacementSampler);
                    int tileGroupsY = CoreUtils.DivRoundUp(m_ProbeHeight, GroupSize);
                    if (m_FullSphere)
                    {
                        for (int i = 0; i < m_CompactPlacementKernels.Length; i++)
                        {
                            int kernel = m_CompactPlacementKernels[i];
                            if (i < 2)
                                BindReceiver(cmd, m_GatherCompute, kernel);
                            cmd.SetComputeTextureParam(m_GatherCompute, kernel, PlacementOutputId, placement);
                            int x = i == 2 ? CoreUtils.DivRoundUp(m_ProbeHeight, 64) : i == 3 ? 1 : groupsX;
                            cmd.DispatchCompute(m_GatherCompute, kernel, x, i == 2 || i == 3 ? 1 : tileGroupsY, 1);
                        }
                    }
                    else
                    {
                        BindReceiver(cmd, m_GatherCompute, m_PlacementKernel);
                        cmd.SetComputeTextureParam(m_GatherCompute, m_PlacementKernel, PlacementOutputId, placement);
                        cmd.DispatchCompute(m_GatherCompute, m_PlacementKernel, groupsX, CoreUtils.DivRoundUp(m_ProbeHeight, GroupSize), 1);
                    }
                }
                if (cache != null)
                {
                    RecordCardDirectLighting(cmd, cache);
                    RecordRadiosity(cmd, traceKernel, cache, black);
                    RecordCardFinalLighting(cmd, traceKernel, cache, black);
                    RecordFarCache(cmd, traceKernel, cache, black, placement);
                }
                else
                {
                    cmd.SetComputeFloatParam(m_TraceCompute, FarDistanceId, 0f);
                    cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, FarRadianceId, black);
                }
                if (m_DirectionalGuide)
                    RecordDirectionalGuide(cmd, placement, black, useDirectionalGuideHistory, frame, groupsX, groupsY);
                if (useScreenRays)
                    RecordScreenTrace(cmd, groupsX, groupsY, placement, black, validScreenRadiance);
                using (new ProfilingScope(cmd, GatherSampler))
                {
                    if (m_LumenDebugView.HasSurfaceCacheFeedback)
                    {
                        m_LumenDebugView.BeginFeedback(cmd, m_TraceCompute, m_DebugClearCardFeedbackKernel, m_State.Frame);
                        m_LumenDebugView.BindFeedback(cmd, m_TraceCompute, traceKernel);
                    }
                    if (compactTrace)
                    {
                        RecordMeshSdfCardGrid(cmd, traceKernel, cameraData);
                        RecordCompactProbeTraces(cmd, cache, black, placement, useScreenRays, receiverVariant,
                            traceKernel, groupsX, groupsY);
                    }
                    else
                        cmd.DispatchCompute(m_TraceCompute, traceKernel, groupsX, groupsY, 1);
                }
                if (m_DirectionalCache || m_HitDistanceFilter || (m_SpatialFilter && !m_RestirGather))
                {
                    BindProbeFrame(cmd, m_FilterCompute);
                    cmd.SetComputeIntParam(m_FilterCompute, FrameId, frame);
                    cmd.SetComputeFloatParam(m_FilterCompute, MaxDistanceId, m_TraceDistance);
                    cmd.SetComputeIntParam(m_FilterCompute, DirectionalProbesId, m_DirectionalProbes ? 1 : 0);
                    cmd.SetComputeIntParam(m_FilterCompute, SpatialFilterId, m_SpatialFilter ? 1 : 0);
                    cmd.SetComputeIntParam(m_FilterCompute, ProbeSpatialKernelId, (int)m_ProbeSpatialKernel);
                    if (m_HitDistanceFilter)
                    {
                        cmd.SetComputeIntParam(m_FilterCompute, HasHistoryId, useProbeHistory ? 1 : 0);
                        cmd.SetComputeMatrixParam(m_FilterCompute, PreviousViewProjectionId, m_State.ViewProjection);
                        cmd.SetComputeFloatParam(m_FilterCompute, MaxDistanceId, m_TraceDistance);
                        cmd.SetComputeFloatParam(m_FilterCompute, RayBiasId, m_Bias);
                    }
                }
                if (m_DirectionalCache)
                    RecordDirectionalCache(cmd, black, useDirectionalHistory, groupsX, groupsY);
                else if (m_RestirGather)
                    RecordRestir(cmd, black, useScreenRays ? m_ScreenHzb.innerHandle : black, hasHistory && m_RestirHistory.IsValid()
                        && m_PreviousRestir.innerHandle.IsValid(), useScreenRays, frame, groupsX, groupsY);
                else if (m_HitDistanceFilter)
                {
                    using var scope = new ProfilingScope(cmd, FilterSampler);
                    cmd.SetComputeTextureParam(m_FilterCompute, m_HitFilterKernel, GeometryId, m_Geometry.innerHandle);
                    cmd.SetComputeTextureParam(m_FilterCompute, m_HitFilterKernel, ProbeInputId, m_FilteredProbes.innerHandle);
                    cmd.SetComputeTextureParam(m_FilterCompute, m_HitFilterKernel, HitSamplesInputId, m_HitSamples.innerHandle);
                    cmd.SetComputeTextureParam(m_FilterCompute, m_HitFilterKernel, ProbeId, m_Probes.innerHandle);
                    cmd.SetComputeTextureParam(m_FilterCompute, m_HitFilterKernel, PreviousProbeId,
                        useProbeHistory ? m_PreviousProbes.innerHandle : black);
                    cmd.SetComputeTextureParam(m_FilterCompute, m_HitFilterKernel, PreviousGeometryId,
                        useProbeHistory ? m_PreviousGeometry.innerHandle : black);
                    // One thread-group row covers one probe, with one lane per ray.
                    cmd.DispatchCompute(m_FilterCompute, m_HitFilterKernel, groupsX, m_AtlasHeight, 1);
                }
                else if (m_SpatialFilter)
                {
                    using var scope = new ProfilingScope(cmd, FilterSampler);
                    cmd.SetComputeTextureParam(m_FilterCompute, m_FilterKernel, GeometryId, m_Geometry.innerHandle);
                    cmd.SetComputeTextureParam(m_FilterCompute, m_FilterKernel, ProbeInputId, m_Probes.innerHandle);
                    cmd.SetComputeTextureParam(m_FilterCompute, m_FilterKernel, FilteredProbesId, m_FilteredProbes.innerHandle);
                    cmd.DispatchCompute(m_FilterCompute, m_FilterKernel, groupsX, groupsY, 1);
                }
                if (m_BentDuringIntegration || compositeBent)
                    RecordLumenBentNormal(cmd, frame, compositeBent);
                RecordResolve(context, placement, pixelTemporal, repairScreenRays, black);
#if UNITY_EDITOR
                if (m_DirectionalCache && pixelTemporal)
                    EditorGatherStageCapture?.Invoke(context, 0, m_RawResolved.innerHandle.ResolveTexture());
#endif
                if (pixelTemporal)
                    RecordPixelTemporal(cmd, hasHistory && m_State.PixelHistoryMaxAge == m_PixelHistoryMaxAge
                        && m_State.StochasticInterpolation == m_StochasticInterpolation
                        && (m_State.NearOcclusion.w == 2f) == m_BentDuringIntegration
                        && (!m_BentDuringIntegration || m_State.NearOcclusion.y == m_NearOcclusion.y)
                        && m_PixelHistory.IsValid() && m_PixelGeometryHistory.IsValid(), restirVarianceTemporal, compositeBent, frame, black);
#if UNITY_EDITOR
                if (m_DirectionalCache && pixelTemporal)
                    EditorGatherStageCapture?.Invoke(context, 1, m_Output.innerHandle.ResolveTexture());
#endif
                bool atrous = m_AtrousIterations > 0 && m_AtrousLighting.innerHandle.IsValid()
                    && m_AtrousVarianceA.innerHandle.IsValid() && (m_AtrousIterations < 3
                        || (m_AtrousVarianceB.innerHandle.IsValid() && m_RawResolved.innerHandle.IsValid()));
                if (m_RestirPixelIntegration)
                    RecordRestirDenoise(cmd, pixelTemporal, restirVarianceTemporal);
                if (atrous)
                    RecordAtrousDenoise(context, black, restirVarianceTemporal);
                if (!m_BentComposite && m_BentNormalHistory != null && m_PixelGeometryHistory != null)
                    RecordBentNormal(cmd, frame, pixelTemporal, black);
                else if (!m_BentComposite && !m_BentDuringIntegration && m_NearOcclusion.y > 0f
                    && m_NearOcclusion.z == 0f && m_NearOcclusion.x > 0f)
                    RecordNearOcclusion(cmd);
            }
            if (m_LumenReflections.Prepared)
            {
                bool hasDualSlabSidecar = m_OpenPbrAux0?.innerHandle.IsValid() == true
                    && m_OpenPbrAux1?.innerHandle.IsValid() == true;
                bool hasWorldCache = m_FullSphere && IsFarCacheReady();
                float reflectionMaxTraceDistance = Mathf.Max(m_TraceDistance,
                    m_FarDistance > 0f ? m_FarDistance : m_SkyTraceDistance);
                m_LumenReflections.Record(
                    cmd,
                    this,
                    frame,
                    black,
                    hasDualSlabSidecar ? m_OpenPbrAux0.innerHandle : black,
                    hasDualSlabSidecar ? m_OpenPbrAux1.innerHandle : black,
                    hasDualSlabSidecar,
                    m_LumenScreenTrace && m_ClosestHzb.innerHandle.IsValid() ? m_ClosestHzb.innerHandle : default,
                    m_ClosestHzbSize,
                    m_DepthThicknessScale,
                    useScreenRays && validScreenRadiance,
                    m_UseSdf && cache != null,
                    m_MeshCardGridRecorded && m_MeshCardGrid.IsValid(),
                    m_MeshCardGrid.innerHandle,
                    m_MeshCardGridParams,
                    m_MeshCardGridView,
                    m_MeshCardGridProjection,
                    useGlobalClipmap && m_ObjectGrid.IsValid(),
                    hasWorldCache,
                    hasWorldCache ? m_FarAtlas : black,
                    m_FarWindowParameter,
                    m_FarDistance,
                    reflectionMaxTraceDistance,
                    m_MeshDetailDistance,
                    m_SkyTextureParams.w > 0.5f,
                    m_LumenDebugView);
            }
            var diffuse = context.GetOrCreate<VividSdfDiffuseFrameData>();
            diffuse.Output = m_Output;
            diffuse.BentNormalOutput = m_BentNormalHistory != null && m_PixelGeometryHistory != null
                && (!m_BentComposite || compositeBent) ? m_CurrentBentNormal.innerHandle : default;
            diffuse.CompositeBentNormal = compositeBent;
            diffuse.RoughSpecularOutput = m_RoughSpecular && m_RoughSpecularHistory != null
                && m_CurrentRoughSpecular.innerHandle.IsValid() ? m_CurrentRoughSpecular.innerHandle : default;
            diffuse.RoughSpecularPreExposed = m_RoughSpecular && pixelTemporal && m_ScreenProbeTemporalKernel >= 0
                && m_PreExposure != null && m_PreExposureHistory != null && m_CurrentPreExposure.innerHandle.IsValid();
            diffuse.SpecularReflectionOutput = m_LumenReflections.Prepared ? m_LumenReflections.OutputHandle : default;
            diffuse.SpecularReflectionCombineParams = m_LumenReflections.CombineParameters;
            diffuse.SpecularReflectionSmoothBias = m_LumenReflections.SmoothBias;
            diffuse.SkyHandled = m_SkyTextureParams.w > 0.5f;
            diffuse.RealtimeOnly = m_RealtimeOnly;
            diffuse.OpenPbrSource = m_RestirOpenPbrDiffuse ? this : null;
            if (m_LumenDebugView.Mode != LumenDebugViewMode.None)
                RecordLumenDebugView(context, cache, black, pixelTemporal, compositeBent, frame);
            m_LumenDebugView.MarkFeedbackWritten();
#if UNITY_EDITOR
            EditorTraceCapture?.Invoke(context, m_TraceSources.innerHandle.ResolveTexture(),
                m_Output.innerHandle.ResolveTexture(), m_Geometry.innerHandle.ResolveTexture());
            if (EditorLinearCapture != null)
                EditorLinearCapture(context, m_Output.innerHandle.ResolveTexture(), m_Depth.innerHandle.ResolveTexture(),
                    m_GBuffer0.innerHandle.ResolveTexture(), m_GBuffer1.innerHandle.ResolveTexture());
#endif
            m_ProbeHistory?.MarkWritten();
            m_AnchorStatisticsHistory?.MarkWritten();
            m_GeometryHistory.MarkWritten();
            m_DirectionalHistory?.MarkWritten();
            m_DirectionalGuideHistory?.MarkWritten();
            m_ProposalHistory?.MarkWritten();
            m_State.ViewProjection = m_ViewProjection;
            m_State.ProbeCameraForward = m_ProbeCameraForward;
            m_State.ProbeCameraPosition = m_ProbeCameraPosition;
            m_State.FastUpdateNeighborhoodClamp = m_FastUpdateNeighborhoodClamp;
            m_State.ProbeFrame = frame;
            m_State.Cache = m_Scene.SurfaceCache;
            m_State.OffscreenOnly = m_OffscreenOnly;
            m_State.TraceMode = m_TraceMode;
            m_State.HasScreenRays = useScreenRays;
            m_State.DirectionalProbes = m_DirectionalProbes;
            m_State.NearOcclusion = m_NearOcclusion;
            m_State.DirectionalCache = m_DirectionalCache;
            m_State.DirectionalGuide = m_DirectionalGuide;
            m_State.LumenScreenTrace = m_LumenScreenTrace;
            m_State.FullSphere = m_FullSphere;
            m_State.ProbeTemporal = m_ProbeTemporal;
            m_State.DitherTraceDirections = m_DitherTraceDirections;
            m_State.RoughSpecular = m_RoughSpecular;
            m_State.MaxRayIntensity = m_MaxRayIntensity;
            m_State.DirectionalHierarchicalGuide = m_DirectionalHierarchicalGuide;
            m_State.AdaptiveProbes = m_AdaptiveProbes;
            m_State.SpatialFilter = m_SpatialFilter;
            m_State.HitDistanceFilter = m_HitDistanceFilter;
            m_State.ProbeSpatialKernel = m_ProbeSpatialKernel;
            m_State.RestirGather = m_RestirGather;
            m_State.RestirPixelIntegration = m_RestirPixelIntegration;
            m_State.RestirOpenPbrDiffuse = m_RestirOpenPbrDiffuse;
            m_State.RestirCardVersion = m_RestirCardVersion;
            m_State.PixelTemporal = pixelTemporal;
            m_State.StochasticInterpolation = m_StochasticInterpolation;
            m_State.PixelHistoryMaxAge = m_PixelHistoryMaxAge;
            m_State.ProbeAnchorMaxAge = m_ProbeAnchorMaxAge;
            m_State.CorrectReceiverSelfHits = correctReceiver;
            m_State.ScreenCardLighting = screenCardLighting;
            m_State.SpatialAnchor = m_SpatialAnchor;
            m_State.AnchorPreFilter = m_AnchorPreFilter;
            m_State.MeshDetailDistance = m_MeshDetailDistance;
            m_State.GlobalClipmapUsed = useGlobalClipmap;
            m_State.ScreenJitterUv = m_ScreenJitterUv;
            m_State.SceneRevision = m_Scene.Revision;
            m_State.SceneTransportVersion = m_Scene.TransportVersion;
            m_State.SceneNonMotionVersion = m_Scene.NonMotionVersion;
            m_State.Width = m_Width;
            m_State.Height = m_Height;
            m_State.LayerMask = m_LayerMask;
            m_State.LightLayerMask = m_LightLayerMask;
            m_State.MaxDistance = m_TraceDistance;
            m_State.RayBias = m_Bias;
            m_State.SkyTraceDistance = m_SkyTraceDistance;
            m_State.SkyContentHash = m_SkyContentHash;
            m_State.SkyTextureParams = m_SkyTextureParams;
            m_State.SkyTextureTint = m_SkyTextureTint;
            m_State.RadiosityCardVersion = m_RadiosityCardVersion;
            m_State.FinalLightingCardVersion = cache != null ? cache.CardVersion : 0;
            m_State.FinalLightingReadyCards = m_CardFinalAtlas.IsValid() ? m_FinalLightingReadyCards : 0;
            m_State.FinalLightingUpdateCursor = m_FinalLightingUpdateFirst + m_FinalLightingNewCards;
            m_State.FinalLightingDirectInputs = m_FinalLightingDirectInputs;
            m_State.FinalLightingInputOrder = m_FinalLightingInputOrder;
            m_State.FinalLightingHasIndirect = m_RadiosityInput.IsValid();
            m_State.RadiosityTargetOrders = m_RadiosityTargetOrders;
            m_State.RadiositySamples = m_RadiositySamples;
            m_State.RadiositySeed = m_RadiositySeed;
            m_State.RadiosityAccumulate = m_RadiosityAccumulate;
            m_State.RadiosityBatchLimit = m_RadiosityBatchLimit;
            m_State.GatherRadiosityOrder = PublishedRadiosityOrder;
            m_State.RadiosityProgressive = m_RadiosityProgressive;
            m_State.RadiosityFeedback = m_RadiosityFeedback;
            m_State.RadiosityPublishedOrder = m_RadiosityPublishedOrder;
            m_State.RadiosityLayoutVersion = m_Scene.RadiosityLayoutVersion;
            m_State.RadiosityBoostFrames = Mathf.Max(0, m_RadiosityBoostFrames - 1);
            var cameraTransform = context.Get<VividCameraData>().camera.transform;
            m_State.RadiosityCameraPosition = cameraTransform.position;
            m_State.RadiosityCameraForward = cameraTransform.forward;
            int cursor = m_RadiosityCursor + m_RadiosityUpdateCards;
            int radiosityCards = m_RadiosityFeedback ? (int)m_FinalLightingLayout.w : m_Scene.RadiosityCardCount;
            bool finishedSweep = m_RadiosityUpdateCards > 0 && cursor == radiosityCards;
            m_State.RadiosityCursor = finishedSweep ? 0 : cursor;
            if (m_RadiosityFeedback)
                m_State.RadiosityOrder = 0;
            else if (m_RadiosityAccumulate)
            {
                bool finishedBatch = finishedSweep && m_RadiosityBounce + 1 == m_RadiosityTargetOrders;
                m_State.RadiosityBounce = finishedSweep
                    ? (finishedBatch ? 0 : m_RadiosityBounce + 1) : m_RadiosityBounce;
                m_State.RadiosityBatch = m_RadiosityBatch + (finishedBatch ? 1 : 0);
                m_State.RadiosityOrder = finishedBatch ? m_RadiosityTargetOrders : m_RadiosityOrder;
                if (finishedBatch)
                    m_RadiosityRestarting = false;
            }
            else
            {
                m_State.RadiosityOrder = m_RadiosityOrder + (finishedSweep ? 1 : 0);
                if (m_State.RadiosityOrder >= m_RadiosityTargetOrders)
                    m_RadiosityRestarting = false;
            }
            m_State.RadiosityRestarting = m_RadiosityRestarting;
            m_State.RadiosityFastRelight = m_RadiosityFastRelight && m_RadiosityRestarting;
            m_State.FarDistance = m_FarDistance;
            m_State.FarCardVersion = m_FarCardVersion;
            m_State.FarSceneVersion = FarSceneVersion;
            m_State.FarPending = m_FarPending;
            m_State.FarWindow = m_FarWindow;
            m_State.LightPositionRange = m_LightPositionRange;
            m_State.LightColorShadow = m_LightColorShadow;
        }

        public override void Dispose()
        {
            m_Cameras.Dispose();
            m_ReceiverEntities?.Dispose();
            m_ReceiverEntities = null;
            m_ReceiverSources = null;
            m_SdfInstancesByEntity.Clear();
            m_ReceiverLookupRevision = uint.MaxValue;
            m_Scene?.Dispose();
            m_Scene = null;
            m_SkyRadianceBoundBuffer?.Dispose();
            m_SkyRadianceBoundBuffer = null;
            RenderPipelineManager.endCameraRendering -= m_SkyBoundAfterSubmit;
            // An already issued readback retains its pending flag until its callback.
            // This prevents reuse of its request identity if this pass is recreated.
            if (m_SkyBoundAwaitSubmit)
                m_SkyBoundPending = false;
            m_SkyBoundAwaitSubmit = false;
            m_SkyBoundCamera = null;
            m_SkyBoundValid = false;
            m_SkyBoundClearKernel = m_SkyBoundMeasureKernel = -1;
            m_TraceCompute = m_GatherCompute = m_ImportanceSamplingCompute = m_FilterCompute = m_GatherTemporalCompute = null;
            m_RestirCompute = m_CardLightingCompute = m_RadiosityCompute = m_FarCompute = m_NearOcclusionCompute = null;
            m_TraceKernel = m_CachedTraceKernel = m_ResolveKernel = -1;
            m_ScreenTraceKernel = m_ScreenReceiverTraceKernel = m_ScreenGatherKernel = -1;
            m_LumenTraceKernel = m_LumenReceiverKernel = m_ClosestHzbKernel = m_SceneColorKernel = -1;
            m_CardDirectLightingKernel = m_CardFinalLightingKernel = -1;
            m_RadiosityKernel = -1;
            m_RadiosityAccumulationKernel = m_RadiosityPublishKernel = -1;
            m_RadiosityPublishCardsKernel = m_RadiosityClearKernel = -1;
            m_RadiosityRescaleKernel = -1;
            m_FarKernel = m_NearOcclusionKernel = m_BentSearchKernel = m_BentTemporalKernel = -1;
            m_LumenBentHalfKernel = -1;
            m_BentFurthestHzbKernel = m_LumenBentKernel = -1;
            m_OpenPbrResolveKernel = -1;
            m_PlacementKernel = m_FilterKernel = m_HitFilterKernel = -1;
            m_SphereGuideKernel = m_DirectionalGuideKernel = m_DirectionalCompositeKernel = m_DirectionalTemporalKernel = -1;
            m_DirectionalFilterKernel = m_DirectionalIntegrateKernel = m_DirectionalResolveKernel = -1;
            m_LumenDirectionalFilterKernel = -1;
            m_PrepareAnchorKernel = -1;
            m_PrepareAnchorNoPixelKernel = -1;
            m_AnchorPreFilterKernel = -1;
            m_RestirTemporalKernel = m_RestirSpatialKernel = -1;
            m_RestirDenoiseKernel = -1;
            m_AtrousKernel = m_AtrousFinalKernel = -1;
            m_PixelTemporalKernel = m_ScreenProbeTemporalKernel = m_RestirPixelTemporalKernel = -1;
            m_ScreenProbeMomentsKernel = -1;
            m_ScreenProbeAoKernel = m_ScreenProbeMomentsAoKernel = -1;
            m_RestirVarianceTemporalKernel = -1;
        }

        internal static int GetCardDirectUpdateBudget(int cards) => CoreUtils.DivRoundUp(cards, DirectLightingUpdateFactor);

        internal static int GetRadiosityUpdateCount(int cards, int cursor, int cardsPerFrame, int fullUpdateCardLimit)
        {
            int remaining = Mathf.Max(0, cards - cursor);
            int budget = cards <= fullUpdateCardLimit ? cards : Mathf.Max(1, cardsPerFrame);
            return Mathf.Min(remaining, budget);
        }

        internal static int GetRadiosityUpdateCount(int cards, int cursor, int cardsPerFrame,
            int fullUpdateCardLimit, int boost)
        {
            int budget = Mathf.Clamp(cardsPerFrame, 1, RadiosityCardLimit) * Mathf.Clamp(boost, 1, 4);
            return GetRadiosityUpdateCount(cards, cursor, budget, fullUpdateCardLimit);
        }

        internal static bool IsFastRadiosityCameraMotion(Vector3 previousPosition, Vector3 previousForward,
            Vector3 position, Vector3 forward) => (position - previousPosition).sqrMagnitude > 0.0625f
            || Vector3.Dot(previousForward, forward) < 0.9961947f; // 0.25 m or five degrees per frame.

        private int PublishedRadiosityOrder => m_RadiosityProgressive ? m_RadiosityPublishedOrder : m_RadiosityOrder;

        private bool PreserveRelitHistory
        {
            get
            {
#if UNITY_EDITOR
                if (EditorTraceCapture != null && (EditorGatherHistoryDiagnostics & 131072) != 0)
                    return false;
#endif
                // Reservoir weights and variance require separate rescaling rules.
                // The native path stores only radiance and invariant geometric metadata.
                return m_RadiosityRescale && m_DirectionalCache && !m_RestirGather;
            }
        }

        internal static bool TryGetRadiosityLightScale(Vector4 previous, Vector4 current, int orders, out Vector4 scale) =>
            TryGetRadiositySourceScale(previous, current, Vector4.zero, Vector4.zero, orders, out scale);

        internal static bool TryGetRadiositySourceScale(Vector4 previousSun, Vector4 sun,
            Vector4 previousEmission, Vector4 emission, int orders, out Vector4 scale) =>
            TryGetRadiosityLightingScale(previousSun, sun, previousEmission, emission,
                Vector4.zero, Vector4.zero, 0f, orders, out scale);

        internal static bool TryGetRadiosityLightingScale(Vector4 previousSun, Vector4 sun,
            Vector4 previousEmission, Vector4 emission, Vector4 previousSky, Vector4 sky,
            float skyRadianceBound, int orders, out Vector4 scale)
        {
            scale = Vector4.one;
            if (previousSun.w != sun.w || orders <= 0 || orders > 8
                || !float.IsFinite(skyRadianceBound) || skyRadianceBound < 0f)
                return false;
            int channels = 0;
            if (!VividMeshSdfGpuScene.TryMergeEmissionScale(previousSun, sun, ref scale, ref channels)
                || !VividMeshSdfGpuScene.TryMergeEmissionScale(previousEmission, emission, ref scale, ref channels)
                || !VividMeshSdfGpuScene.TryMergeEmissionScale(previousSky, sky, ref scale, ref channels))
                return false;
            float maximum = 0f;
            for (int i = 0; i < 3; i++)
                maximum = Mathf.Max(maximum, Mathf.Max(previousSun[i] + previousEmission[i] + previousSky[i] * skyRadianceBound,
                    sun[i] + emission[i] + sky[i] * skyRadianceBound));
            // Includes direct emission and the measured sky peak. No /PI reduction.
            return channels != 0 && maximum * (orders + 1) <= 60000f;
        }

        private void OnSkyRadianceBoundReadback(AsyncGPUReadbackRequest request)
        {
            m_SkyBoundPending = false;
            if (m_SkyRadianceBoundBuffer == null || request.hasError)
                return;
            m_SkyRadianceBound = System.BitConverter.Int32BitsToSingle((int)request.GetData<uint>()[0]);
            m_SkyBoundHash = m_SkyBoundRequestHash;
            m_SkyBoundValid = float.IsFinite(m_SkyRadianceBound) && m_SkyRadianceBound < float.MaxValue;
        }

        private void OnSkyBoundAfterSubmit(ScriptableRenderContext context, Camera camera)
        {
            if (!m_SkyBoundAwaitSubmit || camera != m_SkyBoundCamera || m_SkyRadianceBoundBuffer == null)
                return;
            // VividRenderPipeline submits this camera before EndCameraRendering.
            // Request on the same graphics queue, after the bound dispatch is submitted.
            m_SkyBoundAwaitSubmit = false;
            m_SkyBoundCamera = null;
            AsyncGPUReadback.Request(m_SkyRadianceBoundBuffer, m_SkyRadianceBoundReadback);
        }

        private void RecordSkyRadianceBound(ComputePassContext context)
        {
            // Full texel coverage is required for a bound; never downsample a larger sky.
            // Reuse the buffer/delegate and do no work for stable sky content.
            if (m_SkyTraceDistance <= 0f || m_SkyBoundMeasureKernel < 0 || !m_SkyTexture.IsValid()
                || m_SkyBoundSize <= 0 || m_SkyBoundSize > 1024 || m_SkyBoundPending
                || m_SkyBoundValid && m_SkyBoundHash == m_SkyContentHash)
                return;
            m_SkyRadianceBoundBuffer ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            var cmd = context.cmd;
            cmd.SetComputeBufferParam(m_RadiosityCompute, m_SkyBoundClearKernel, SkyRadianceBoundId, m_SkyRadianceBoundBuffer);
            cmd.DispatchCompute(m_RadiosityCompute, m_SkyBoundClearKernel, 1, 1, 1);
            cmd.SetComputeBufferParam(m_RadiosityCompute, m_SkyBoundMeasureKernel, SkyRadianceBoundId, m_SkyRadianceBoundBuffer);
            cmd.SetComputeTextureParam(m_RadiosityCompute, m_SkyBoundMeasureKernel, SkyTextureId, m_SkyTexture);
            cmd.SetComputeIntParam(m_RadiosityCompute, SkyRadianceBoundSizeId, m_SkyBoundSize);
            cmd.DispatchCompute(m_RadiosityCompute, m_SkyBoundMeasureKernel,
                CoreUtils.DivRoundUp(m_SkyBoundSize, 8), CoreUtils.DivRoundUp(m_SkyBoundSize, 8), 6);
            m_SkyBoundRequestHash = m_SkyContentHash;
            m_SkyBoundPending = true;
            m_SkyBoundAwaitSubmit = true;
            m_SkyBoundCamera = context.Get<VividCameraData>().camera;
        }

        private bool HasCompatibleRadiosityTransport() => !m_State.RadiosityRestarting
            && m_State.RadiosityPublishedOrder == m_RadiosityTargetOrders
            && m_State.RadiosityOrder >= m_RadiosityTargetOrders
            && m_State.RadiosityInputTransportVersion == m_Scene.TransportVersion
            && m_State.LayerMask == m_LayerMask && m_State.LightLayerMask == m_LightLayerMask
            && m_State.MaxDistance == m_TraceDistance && m_State.RayBias == m_Bias
            && m_State.RadiositySamples == m_RadiositySamples && m_State.RadiositySeed == m_RadiositySeed
            && m_State.RadiosityAccumulate == m_RadiosityAccumulate
            && m_State.RadiosityBatchLimit == m_RadiosityBatchLimit;

        private bool CanRescaleRadiosityHistory()
        {
#if UNITY_EDITOR
            if (EditorTraceCapture != null && (EditorGatherHistoryDiagnostics & 65536) != 0)
                return false;
#endif
            return m_RadiosityRescaleKernel >= 0 && HasCompatibleRadiosityTransport()
                && m_SkyTraceDistance == m_State.SkyTraceDistance
                && (m_SkyTraceDistance == 0f || m_SkyBoundValid && m_SkyBoundHash == m_SkyContentHash
                    && m_SkyBoundSize > 0 && m_SkyBoundSize <= 1024
                    && m_State.RadiositySkyContentHash == m_SkyContentHash
                    && m_State.RadiositySkyTextureParams.y == m_SkyTextureParams.y
                    && m_State.RadiositySkyTextureParams.z == m_SkyTextureParams.z
                    && m_State.RadiositySkyTextureParams.w == m_SkyTextureParams.w)
                && m_LightPositionRange.w <= 0f && m_State.RadiosityLightPositionRange.Equals(m_LightPositionRange)
                && m_Scene.TryGetEmissionChange(m_State.RadiosityEmissionVersion, out var previousEmission, out var emission)
                && TryGetRadiosityLightingScale(m_State.RadiosityLightColorShadow, m_LightColorShadow,
                    previousEmission, emission,
                    m_SkyTraceDistance > 0f ? m_State.RadiositySkyTextureTint * m_State.RadiositySkyTextureParams.x : Vector4.zero,
                    m_SkyTraceDistance > 0f ? m_SkyTextureTint * m_SkyTextureParams.x : Vector4.zero,
                    m_SkyTraceDistance > 0f ? m_SkyRadianceBound : 0f,
                    m_RadiosityTargetOrders, out m_RadiosityHistoryScale);
        }

        internal static int GetRadiosityRelightSweepCount(int cards, int fullUpdateLimit, int samples,
            int remainingOrders, int requestedSweeps)
        {
            if (cards <= 0 || cards > fullUpdateLimit || remainingOrders <= 0)
                return 1;
            // Bound burst work as well as dispatch count, including high-sample references.
            int budget = (int)System.Math.Max(1L, 65536L / ((long)cards * Mathf.Max(16, samples)));
            return Mathf.Clamp(Mathf.Min(remainingOrders, budget), 1, Mathf.Clamp(requestedSweeps, 1, 8));
        }

        private void PrepareRadiosity(Camera camera, VividSdfSurfaceCache cache, int targetOrders,
            int cardsPerFrame, int fullUpdateCardLimit)
        {
            var history = camera.GetVividCameraHistory();
            int maxDimension = SystemInfo.maxTextureSize;
            if (!history.IsFrameActive || maxDimension < RadiosityColumns * RadiosityCardSize)
                return;

            int limit = Mathf.Min(RadiosityCardLimit, maxDimension / RadiosityCardSize * RadiosityColumns);
            m_Scene.PrepareRadiosityCards(limit);
            int cards = m_Scene.RadiosityCardCount;
            if (cards == 0)
                return;

            m_RadiosityDesc.Resize(RadiosityColumns * RadiosityCardSize,
                CoreUtils.DivRoundUp(cards, RadiosityColumns) * RadiosityCardSize);
            var descriptor = CameraHistoryRenderGraphBridge.CreateDescriptor(m_RadiosityDesc.desc);
            m_RadiosityTargetOrders = targetOrders;
            m_RadiosityCardVersion = cache.CardVersion;
            if (m_RadiosityFeedback)
            {
                PrepareRadiosityFeedback(history, cache, descriptor, cards, cardsPerFrame, fullUpdateCardLimit);
                return;
            }
            if (m_RadiosityAccumulate)
            {
                PrepareRadiosityAccumulation(history, cache, descriptor, camera, cards, cardsPerFrame, fullUpdateCardLimit);
                return;
            }
            var atlasA = history.GetOrCreateTexture(RadiosityAId, 1, descriptor);
            var atlasB = history.GetOrCreateTexture(RadiosityBId, 1, descriptor);
            bool stable = atlasA.IsValid(0) && atlasB.IsValid(0)
                && MatchesRadiosityInputs(cache);
            m_RadiosityOrder = stable ? m_State.RadiosityOrder : 0;
            PrepareRadiosityTransition(history, cache, descriptor, camera, stable);
            m_RadiosityCursor = stable || CanContinueFirstRadiositySweep(cache)
                ? m_State.RadiosityCursor : 0;
            bool writeA = (m_RadiosityOrder & 1) == 0;
            m_RadiosityReadHistory = writeA ? atlasB : atlasA;
            m_RadiosityWriteHistory = writeA ? atlasA : atlasB;
            if (m_RadiosityOrder > 0)
                m_RadiosityBounceInput = CameraHistoryRenderGraphBridge.ImportForPass(this, m_RadiosityReadHistory, 0, AccessFlags.Read);
            if (!m_RadiosityProgressive)
                m_RadiosityInput = m_RadiosityBounceInput;
            if (m_RadiosityOrder < targetOrders)
            {
                if (m_RadiosityFastRelight && m_RadiosityCursor == 0)
                    m_RadiositySweeps = GetRadiosityRelightSweepCount(cards, fullUpdateCardLimit, m_RadiositySamples,
                        targetOrders - m_RadiosityOrder, m_RadiosityRelightSweepLimit);
                m_RadiosityOutput = CameraHistoryRenderGraphBridge.ImportForPass(this, m_RadiosityWriteHistory, 0,
                    m_RadiositySweeps > 1 ? AccessFlags.ReadWrite : AccessFlags.Write);
                m_RadiositySweepOutputs[0] = m_RadiosityOutput;
                if (m_RadiositySweeps > 1)
                {
                    m_RadiositySweepOutputs[1] = CameraHistoryRenderGraphBridge.ImportForPass(this,
                        m_RadiosityReadHistory, 0, AccessFlags.ReadWrite);
                    for (int sweep = 2; sweep < m_RadiositySweeps; ++sweep)
                        m_RadiositySweepOutputs[sweep] = m_RadiositySweepOutputs[sweep & 1];
                }
                m_RadiosityUpdateCards = m_RadiosityOutput.IsValid()
                    ? GetRadiosityUpdateCount(cards, m_RadiosityCursor, cardsPerFrame, fullUpdateCardLimit,
                        m_RadiosityBoostFrames > 0 ? m_RadiosityBudgetBoost : 1) : 0;
            }
        }

        private void PrepareRadiosityFeedback(CameraHistory history, VividSdfSurfaceCache cache,
            CameraHistoryTextureDescriptor descriptor, int cards, int cardsPerFrame, int fullUpdateCardLimit)
        {
            m_RadiosityProgressive = m_RadiosityAccumulate = false;
            m_RadiositySamples = RadiosityFeedbackRays;
            m_RadiosityPublishedHistory = history.GetOrCreateTexture(RadiosityFeedbackId, 1, descriptor);
            m_RadiosityFeedbackCountDesc.Resize(m_RadiosityDesc.desc.Width, m_RadiosityDesc.desc.Height);
            m_RadiosityFeedbackCountHistory = history.GetOrCreateTexture(RadiosityFeedbackCountId, 1,
                CameraHistoryRenderGraphBridge.CreateDescriptor(m_RadiosityFeedbackCountDesc.desc));
            bool compatible = m_State.RadiosityFeedback && ReferenceEquals(m_State.Cache, cache)
                && m_State.RadiosityCardVersion == cache.CardVersion
                && m_State.RadiosityLayoutVersion == m_Scene.RadiosityLayoutVersion
                && m_State.SceneNonMotionVersion == m_Scene.NonMotionVersion
                && m_State.LayerMask == m_LayerMask && m_State.MaxDistance == m_TraceDistance
                && m_RadiosityPublishedHistory.IsValid(0) && m_RadiosityFeedbackCountHistory.IsValid(0);
            m_RadiosityClear = !compatible;
            m_RadiosityCursor = compatible ? m_State.RadiosityCursor : 0;
            m_RadiosityInput = m_RadiosityPublished = CameraHistoryRenderGraphBridge.ImportForPass(this,
                m_RadiosityPublishedHistory, 0, AccessFlags.ReadWrite);
            m_RadiosityFeedbackCount = CameraHistoryRenderGraphBridge.ImportForPass(this,
                m_RadiosityFeedbackCountHistory, 0, AccessFlags.ReadWrite);
            if (m_RadiosityPublished.IsValid() && m_RadiosityFeedbackCount.IsValid())
                m_RadiosityUpdateCards = GetRadiosityUpdateCount(cards, m_RadiosityCursor, cardsPerFrame, fullUpdateCardLimit);
            else
                m_RadiosityInput = default;
        }

        private bool CanContinueFirstRadiositySweep(VividSdfSurfaceCache cache) => m_RadiosityProgressive
            && !m_RadiosityClear && m_State.RadiosityRestarting && m_State.RadiosityOrder == 0
            && m_State.RadiosityBounce == 0 && ReferenceEquals(m_State.Cache, cache)
            && m_State.RadiositySamples == m_RadiositySamples && m_State.RadiositySeed == m_RadiositySeed
            && m_State.RadiosityAccumulate == m_RadiosityAccumulate;

        private void PrepareRadiosityTransition(CameraHistory history, VividSdfSurfaceCache cache,
            CameraHistoryTextureDescriptor descriptor, Camera camera, bool stable)
        {
            bool compatible = false;
            if (m_RadiosityProgressive)
            {
                m_RadiosityPublishedHistory = history.GetOrCreateTexture(RadiosityPublishedId, 1, descriptor);
                compatible = m_RadiosityPublishedHistory.IsValid(0) && m_State.RadiosityProgressive
                    && ReferenceEquals(m_State.Cache, cache) && m_State.RadiosityCardVersion == m_RadiosityCardVersion
                    && m_State.RadiosityLayoutVersion == m_Scene.RadiosityLayoutVersion;
                m_RadiosityClear = !compatible;
                m_RadiosityPublishedOrder = compatible ? m_State.RadiosityPublishedOrder : 0;
                m_RadiosityPublished = CameraHistoryRenderGraphBridge.ImportForPass(this,
                    m_RadiosityPublishedHistory, 0, AccessFlags.ReadWrite);
                // Clear before any consumer on cold/incompatible allocations. Fresh
                // cards can then be sampled immediately, including the first sweep.
                m_RadiosityInput = m_RadiosityPublished;
            }
            m_RadiosityRestarting = !stable || m_State.RadiosityRestarting;
            m_RadiosityFastRelight = stable && m_State.RadiosityFastRelight;
            var transform = camera.transform;
            bool fastMotion = IsFastRadiosityCameraMotion(m_State.RadiosityCameraPosition,
                m_State.RadiosityCameraForward, transform.position, transform.forward);
            m_RadiosityBoostFrames = m_RadiosityRestarting || fastMotion ? 8 : m_State.RadiosityBoostFrames;
            if (!stable)
            {
                Vector3 delta = (Vector3)m_LightColorShadow - (Vector3)m_State.RadiosityLightColorShadow;
                float scale = Mathf.Max(Mathf.Max(((Vector3)m_LightColorShadow).magnitude,
                    ((Vector3)m_State.RadiosityLightColorShadow).magnitude), 1e-8f);
                // Small changes retain 75% of the old card per fresh update. A large
                // lighting change or new sky should replace stale energy promptly.
                m_RadiosityRescale = compatible && CanRescaleRadiosityHistory();
                m_RadiosityFastRelight = compatible && !m_RadiosityRescale && HasCompatibleRadiosityTransport();
#if UNITY_EDITOR
                if (EditorTraceCapture != null && (EditorGatherHistoryDiagnostics & 65536) != 0)
                    m_RadiosityFastRelight = false;
#endif
                bool keepHigherOrder = m_RadiosityRescale || compatible && m_State.RadiosityPublishedOrder == m_RadiosityTargetOrders
                    && m_State.RadiosityInputDiscontinuityVersion == m_Scene.RadiosityDiscontinuityVersion
                    && m_State.LayerMask == m_LayerMask && m_State.LightLayerMask == m_LightLayerMask
                    && m_State.MaxDistance == m_TraceDistance && m_State.RayBias == m_Bias
                    && m_State.SkyTraceDistance == m_SkyTraceDistance
                    && MatchesEnvironmentLighting(m_State.RadiositySkyContentHash,
                        m_State.RadiositySkyTextureParams, m_State.RadiositySkyTextureTint, 0.05f)
                    && LightingWithinTolerance(m_State.RadiosityLightPositionRange, m_State.RadiosityLightColorShadow,
                        m_LightPositionRange, m_LightColorShadow, 0.05f);
                // A low-order estimate omits higher bounces. Replacing a nearly
                // correct high-order cache with it creates an energy sawtooth even
                // under a smooth light ramp. Hold high order for small changes;
                // fresh target-order cards still replace it as soon as they arrive.
                m_State.RadiosityTransitionBlend = keepHigherOrder ? 0f
                    : compatible && m_State.RadiositySkyContentHash == m_SkyContentHash
                        ? Mathf.Clamp(delta.magnitude / scale * 8f, 0.25f, 0.75f) : 1f;
                m_State.RadiosityInputSceneRevision = m_Scene.Revision;
                m_State.RadiosityInputDiscontinuityVersion = m_Scene.RadiosityDiscontinuityVersion;
                m_State.RadiosityInputTransportVersion = m_Scene.TransportVersion;
                m_State.RadiosityEmissionVersion = m_Scene.EmissionVersion;
                m_State.RadiosityLightPositionRange = m_LightPositionRange;
                m_State.RadiosityLightColorShadow = m_LightColorShadow;
                m_State.RadiositySkyContentHash = m_SkyContentHash;
                m_State.RadiositySkyTextureParams = m_SkyTextureParams;
                m_State.RadiositySkyTextureTint = m_SkyTextureTint;
            }
            m_RadiosityTransitionBlend = m_State.RadiosityTransitionBlend;
        }

        private bool MatchesRadiosityInputs(VividSdfSurfaceCache cache)
        {
            if (m_State.RadiosityFeedback)
                return false;
            bool configuration = m_State.RadiositySamples == m_RadiositySamples && m_State.RadiositySeed == m_RadiositySeed
                && m_State.RadiosityAccumulate == m_RadiosityAccumulate
                && m_State.RadiosityProgressive == m_RadiosityProgressive
                && (!m_RadiosityAccumulate || m_State.RadiosityBatchLimit == m_RadiosityBatchLimit)
                && m_State.RadiosityTargetOrders == m_RadiosityTargetOrders
                && ReferenceEquals(m_State.Cache, cache) && m_State.RadiosityCardVersion == m_RadiosityCardVersion
                && m_State.LayerMask == m_LayerMask && m_State.LightLayerMask == m_LightLayerMask
                && m_State.MaxDistance == m_TraceDistance && m_State.RayBias == m_Bias
                && m_State.SkyTraceDistance == m_SkyTraceDistance;
            if (!configuration)
                return false;
            bool unchanged = m_State.RadiosityInputSceneRevision == m_Scene.Revision
                && MatchesEnvironmentLighting(m_State.RadiositySkyContentHash,
                    m_State.RadiositySkyTextureParams, m_State.RadiositySkyTextureTint)
                && LightingWithinTolerance(m_State.RadiosityLightPositionRange, m_State.RadiosityLightColorShadow,
                    m_LightPositionRange, m_LightColorShadow, m_LightingTolerance);
            if (unchanged)
                return true;
            // Finish an in-flight first batch under small ongoing changes instead
            // of restarting at order zero every few frames. Keep the accepted input
            // snapshot unchanged, so a pending change restarts after this target
            // completes. Large light changes, sky content or card identities restart now.
            return m_RadiosityProgressive && m_State.RadiosityRestarting
                && m_State.RadiosityLayoutVersion == m_Scene.RadiosityLayoutVersion
                && m_State.RadiosityInputDiscontinuityVersion == m_Scene.RadiosityDiscontinuityVersion
                && MatchesEnvironmentLighting(m_State.RadiositySkyContentHash,
                    m_State.RadiositySkyTextureParams, m_State.RadiositySkyTextureTint, 0.05f)
                && LightingWithinTolerance(m_State.RadiosityLightPositionRange, m_State.RadiosityLightColorShadow,
                    m_LightPositionRange, m_LightColorShadow, 0.05f);
        }

        private void PrepareRadiosityAccumulation(CameraHistory history, VividSdfSurfaceCache cache,
            CameraHistoryTextureDescriptor descriptor, Camera camera, int cards, int cardsPerFrame, int fullUpdateCardLimit)
        {
            m_RadiosityPublishedHistory = history.GetOrCreateTexture(RadiosityPublishedId, 1, descriptor);
            bool stable = m_RadiosityPublishedHistory.IsValid(0) && MatchesRadiosityInputs(cache);
            for (int order = 0; order < m_RadiosityTargetOrders; ++order)
            {
                var layer = history.GetOrCreateTexture(RadiosityOrderIds[order], 1, descriptor);
                m_RadiosityOrderHistories[order] = layer;
                stable &= layer.IsValid(0);
            }
            m_RadiosityOrder = stable ? m_State.RadiosityOrder : 0;
            PrepareRadiosityTransition(history, cache, descriptor, camera, stable);
            m_RadiosityCursor = stable || CanContinueFirstRadiositySweep(cache) ? m_State.RadiosityCursor : 0;
            m_RadiosityBatch = stable ? m_State.RadiosityBatch : 0;
            m_RadiosityBounce = stable ? m_State.RadiosityBounce : 0;
            bool updating = m_RadiosityBatch < m_RadiosityBatchLimit;
            if (!m_RadiosityProgressive)
            {
                m_RadiosityPublished = CameraHistoryRenderGraphBridge.ImportForPass(this, m_RadiosityPublishedHistory,
                    0, updating ? AccessFlags.ReadWrite : AccessFlags.Read);
                if (m_RadiosityOrder > 0)
                    m_RadiosityInput = m_RadiosityPublished;
            }
            if (updating)
            {
                if (m_RadiosityBounce > 0)
                    m_RadiosityBounceInput = CameraHistoryRenderGraphBridge.ImportForPass(this,
                        m_RadiosityOrderHistories[m_RadiosityBounce - 1], 0, AccessFlags.Read);
                if (m_RadiosityFastRelight && m_RadiosityBatch == 0 && m_RadiosityCursor == 0)
                    m_RadiositySweeps = GetRadiosityRelightSweepCount(cards, fullUpdateCardLimit, m_RadiositySamples,
                        m_RadiosityTargetOrders - m_RadiosityBounce, m_RadiosityRelightSweepLimit);
                for (int sweep = 0; sweep < m_RadiositySweeps; ++sweep)
                    m_RadiositySweepOutputs[sweep] = CameraHistoryRenderGraphBridge.ImportForPass(this,
                        m_RadiosityOrderHistories[m_RadiosityBounce + sweep], 0, AccessFlags.ReadWrite);
                m_RadiosityOutput = m_RadiositySweepOutputs[0];
                m_RadiosityUpdateCards = GetRadiosityUpdateCount(cards, m_RadiosityCursor, cardsPerFrame, fullUpdateCardLimit,
                    m_RadiosityBoostFrames > 0 ? m_RadiosityBudgetBoost : 1);
            }
        }

        private void BindTraceReceiver(ComputeCommandBuffer cmd, int kernel)
        {
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, ReceiverVisibilityId, m_ReceiverVisibility);
            cmd.SetComputeBufferParam(m_TraceCompute, kernel, ReceiverEntitiesId, m_ReceiverEntities);
            cmd.SetComputeIntParam(m_TraceCompute, ReceiverCountId, m_ReceiverCount);
        }

        private void BindCompactRayOutput(ComputeCommandBuffer cmd, int kernel)
        {
            cmd.SetComputeBufferParam(m_TraceCompute, kernel, CompactProbesId, m_CompactProbes.innerHandle);
            cmd.SetComputeBufferParam(m_TraceCompute, kernel, CompactContributionsId, m_CompactContributions.innerHandle);
            cmd.SetComputeBufferParam(m_TraceCompute, kernel, CompactCountsId, m_CompactCounts.innerHandle);
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, ProbeId, m_DirectionalRayTexture.innerHandle);
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, RayDiagnosticsId, m_RayDiagnostics.innerHandle);
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, HitSamplesId, m_HitSamples.innerHandle);
        }

        private void RecordMeshSdfCardGrid(ComputeCommandBuffer cmd, int traceKernel, VividCameraData cameraData)
        {
            using var scope = new ProfilingScope(cmd, MeshCardGridSampler);
            var shader = m_TraceCompute;
            var view = cameraData.GetViewMatrix();
            var projection = cameraData.GetGPUProjectionMatrix(renderIntoTexture: true);
            float padding = m_MeshDetailDistance + m_Bias;
            float camNear = Mathf.Max(0.05f, cameraData.camera.nearClipPlane);
            float camFar = Mathf.Max(camNear * 1.25f, cameraData.camera.farClipPlane);
            float gridNear = camNear;
            float gridFar = camFar;
            if (m_Scene.NodeCount > 0)
            {
                var root = m_Scene.BvhNodes[0];
                float minDepth = float.PositiveInfinity;
                float maxDepth = float.NegativeInfinity;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? root.Min.x : root.Max.x,
                        (c & 2) == 0 ? root.Min.y : root.Max.y,
                        (c & 4) == 0 ? root.Min.z : root.Max.z);
                    float depth = -view.MultiplyPoint3x4(corner).z;
                    if (depth < minDepth) minDepth = depth;
                    if (depth > maxDepth) maxDepth = depth;
                }
                gridNear = Mathf.Clamp(minDepth - padding, camNear, camFar);
                gridFar = Mathf.Clamp(maxDepth + padding, Mathf.Min(camFar, gridNear * 1.25f), camFar);
            }
            float logNear = Mathf.Log(gridNear, 2f);
            float logDepthRange = Mathf.Max(1e-4f, Mathf.Log(gridFar, 2f) - logNear);
            m_MeshCardGridParams = new Vector4(logNear, logDepthRange, padding, 0f);
            m_MeshCardGridView = view;
            m_MeshCardGridProjection = projection;
            m_MeshCardGridRecorded = true;
            cmd.SetComputeVectorParam(shader, MeshCardGridParametersId, m_MeshCardGridParams);
            cmd.SetComputeMatrixParam(shader, MeshCardGridViewId, view);
            cmd.SetComputeMatrixParam(shader, MeshCardGridProjectionId, projection);
            cmd.SetComputeMatrixParam(shader, MeshCardGridInverseViewId, view.inverse);
            cmd.SetComputeMatrixParam(shader, MeshCardGridInverseProjectionId, projection.inverse);
            m_Scene.BindBvhNodes(cmd, shader, m_MeshCardGridKernel);
            cmd.SetComputeBufferParam(shader, m_MeshCardGridKernel, MeshCardGridWriteId, m_MeshCardGrid.innerHandle);
            cmd.DispatchCompute(shader, m_MeshCardGridKernel, MeshCardGridX / 4, MeshCardGridY / 4, MeshCardGridZ / 2);
            cmd.SetComputeBufferParam(shader, traceKernel, MeshCardGridId, m_MeshCardGrid.innerHandle);
        }

        private void RecordCompactProbeTraces(ComputeCommandBuffer cmd, VividSdfSurfaceCache cache, TextureHandle black,
            TextureHandle placement, bool useScreenRays, int receiverVariant, int mesh, int groupsX, int groupsY)
        {
            var shader = m_TraceCompute;
            bool skipEmptyMesh = m_CompactScreenSkipEmptyKernels[receiverVariant] >= 0;
#if UNITY_EDITOR
            skipEmptyMesh = skipEmptyMesh && EditorSkipEmptyCompactMeshIntervals;
#endif
            int classify = skipEmptyMesh
                ? m_CompactScreenSkipEmptyKernels[receiverVariant]
                : m_CompactScreenKernels[receiverVariant];
            if (skipEmptyMesh)
                cmd.SetComputeBufferParam(shader, classify, CompactGlobalRaysId, m_CompactGlobalRays.innerHandle);
            int resolveMesh = m_LumenDebugView.HasSurfaceCacheFeedback
                ? m_FeedbackCompactResolveMeshKernel : m_CompactResolveMeshKernel;
            int global = m_CompactGlobalKernel;
            int resolveGlobal = (m_LumenDebugView.HasSurfaceCacheFeedback
                ? m_FeedbackCompactResolveKernels : m_CompactResolveGlobalKernels)[receiverVariant];
            int finalize = m_CompactFinalizeKernel;
            var screenRays = useScreenRays ? m_ScreenRays.innerHandle : black;
            var directionNoise = m_ProbeDirectionNoise.IsValid() ? m_ProbeDirectionNoise : black;
            var guide = m_DirectionalGuide
                ? (m_FrozenGuideActive ? m_CurrentProposal.innerHandle : m_DirectionalGuideTexture.innerHandle) : black;
            var far = IsFarCacheReady() ? m_FarAtlas : black;

            BindCompactRayOutput(cmd, classify);
            BindReceiver(cmd, shader, classify);
            if (receiverVariant != 0)
            {
                BindTraceReceiver(cmd, classify);
                m_Scene.Bind(cmd, shader, classify);
            }
            cmd.SetComputeTextureParam(shader, classify, PlacementId, placement);
            cmd.SetComputeTextureParam(shader, classify, ProbeDirectionNoiseId, directionNoise);
            cmd.SetComputeTextureParam(shader, classify, DirectionalGuideId, guide);
            cmd.SetComputeTextureParam(shader, classify, FarRadianceId, far);
            cmd.SetComputeTextureParam(shader, classify, ScreenRaysId, screenRays);
            cmd.SetComputeBufferParam(shader, classify, CompactMeshRaysId, m_CompactMeshRays.innerHandle);
            cmd.SetComputeBufferParam(shader, m_CompactResetKernel, CompactCountsId, m_CompactCounts.innerHandle);
            cmd.SetComputeBufferParam(shader, m_CompactArgsKernel, CompactCountsId, m_CompactCounts.innerHandle);

            cmd.SetComputeBufferParam(shader, mesh, CompactProbesId, m_CompactProbes.innerHandle);
            cmd.SetComputeBufferParam(shader, mesh, CompactCountsId, m_CompactCounts.innerHandle);
            cmd.SetComputeBufferParam(shader, mesh, CompactGlobalRaysId, m_CompactGlobalRays.innerHandle);
            cmd.SetComputeBufferParam(shader, mesh, CompactMeshResultsId, m_CompactMeshResults.innerHandle);

            BindCompactRayOutput(cmd, resolveMesh);
            BindReceiver(cmd, shader, resolveMesh);
            BindOccludedSky(cmd, shader, resolveMesh);
            m_Scene.Bind(cmd, shader, resolveMesh);
            cmd.SetComputeTextureParam(shader, resolveMesh, PlacementId, placement);
            cmd.SetComputeTextureParam(shader, resolveMesh, ProbeDirectionNoiseId, directionNoise);
            cmd.SetComputeTextureParam(shader, resolveMesh, DirectionalGuideId, guide);
            cmd.SetComputeTextureParam(shader, resolveMesh, CardAlbedoDepthId, m_CardAlbedoDepth);
            cmd.SetComputeTextureParam(shader, resolveMesh, CardNormalCoverageId, m_CardNormalCoverage);
            cmd.SetComputeTextureParam(shader, resolveMesh, RadiosityInputId, m_RadiosityInput.IsValid() ? m_RadiosityInput : black);
            cmd.SetComputeBufferParam(shader, resolveMesh, RadiosityWorkId, CardLightingWork);
            BindFinalLighting(cmd, shader, resolveMesh, m_CardFinalAtlas, black);
            m_LumenDebugView.BindFeedback(cmd, shader, resolveMesh);
            cmd.SetComputeBufferParam(shader, resolveMesh, SurfaceId, m_Scene.DiffuseSurfaces);
            cmd.SetComputeBufferParam(shader, resolveMesh, LocalCardTableId, cache.LocalCardTable);
            cmd.SetComputeBufferParam(shader, resolveMesh, CompactMeshResultsReadId, m_CompactMeshResults.innerHandle);

            cmd.SetComputeBufferParam(shader, global, CompactProbesId, m_CompactProbes.innerHandle);
            cmd.SetComputeBufferParam(shader, global, CompactCountsId, m_CompactCounts.innerHandle);
            cmd.SetComputeBufferParam(shader, global, CompactRetryRaysId, m_CompactRetryRays.innerHandle);
            cmd.SetComputeBufferParam(shader, global, CompactGlobalResultsId, m_CompactGlobalResults.innerHandle);
            BindReceiver(cmd, shader, global);
            BindGlobalTracingResources(cmd, shader, global, true);
            cmd.SetComputeTextureParam(shader, global, PlacementId, placement);
            cmd.SetComputeTextureParam(shader, global, ProbeDirectionNoiseId, directionNoise);
            cmd.SetComputeTextureParam(shader, global, DirectionalGuideId, guide);
            cmd.SetComputeTextureParam(shader, global, FarRadianceId, far);
            cmd.SetComputeTextureParam(shader, global, ScreenRaysId, screenRays);
            cmd.SetComputeBufferParam(shader, global, CompactInputId, m_CompactGlobalRays.innerHandle);

            BindCompactRayOutput(cmd, resolveGlobal);
            BindReceiver(cmd, shader, resolveGlobal);
            BindGlobalTracingResources(cmd, shader, resolveGlobal, true);
            BindOccludedSky(cmd, shader, resolveGlobal);
            m_Scene.Bind(cmd, shader, resolveGlobal);
            cmd.SetComputeTextureParam(shader, resolveGlobal, PlacementId, placement);
            cmd.SetComputeTextureParam(shader, resolveGlobal, ProbeDirectionNoiseId, directionNoise);
            cmd.SetComputeTextureParam(shader, resolveGlobal, DirectionalGuideId, guide);
            cmd.SetComputeTextureParam(shader, resolveGlobal, CardAlbedoDepthId, m_CardAlbedoDepth);
            cmd.SetComputeTextureParam(shader, resolveGlobal, CardNormalCoverageId, m_CardNormalCoverage);
            cmd.SetComputeTextureParam(shader, resolveGlobal, RadiosityInputId, m_RadiosityInput.IsValid() ? m_RadiosityInput : black);
            cmd.SetComputeBufferParam(shader, resolveGlobal, RadiosityWorkId, CardLightingWork);
            BindFinalLighting(cmd, shader, resolveGlobal, m_CardFinalAtlas, black);
            m_LumenDebugView.BindFeedback(cmd, shader, resolveGlobal);
            cmd.SetComputeBufferParam(shader, resolveGlobal, SurfaceId, m_Scene.DiffuseSurfaces);
            cmd.SetComputeBufferParam(shader, resolveGlobal, LocalCardTableId, cache.LocalCardTable);
            cmd.SetComputeBufferParam(shader, resolveGlobal, CompactInputId, m_CompactGlobalRays.innerHandle);
            cmd.SetComputeBufferParam(shader, resolveGlobal, CompactGlobalResultsReadId, m_CompactGlobalResults.innerHandle);

            using (new ProfilingScope(cmd, CompactScreenSampler))
            {
                cmd.DispatchCompute(shader, m_CompactResetKernel, 1, 1, 1);
                cmd.DispatchCompute(shader, classify, m_ProbeWidth, m_AtlasHeight, 1);
            }
            cmd.SetComputeIntParam(shader, CompactRetryId, 0);
            cmd.SetComputeBufferParam(shader, mesh, CompactInputId, m_CompactMeshRays.innerHandle);
            DispatchCompactQueue(cmd, mesh, 0, m_CompactMeshArgs, CompactMeshSampler);
            DispatchCompactGlobalQueue(cmd, global, resolveGlobal, 1, m_CompactGlobalArgs);
            if (m_CompactTraceMayRetryMesh && IsFarCacheReady())
            {
                cmd.SetComputeIntParam(shader, CompactRetryId, 1);
                cmd.SetComputeBufferParam(shader, mesh, CompactInputId, m_CompactRetryRays.innerHandle);
                DispatchCompactQueue(cmd, mesh, 2, m_CompactRetryMeshArgs, CompactMeshSampler);
                DispatchCompactGlobalQueue(cmd, global, resolveGlobal, 3, m_CompactRetryGlobalArgs);
            }
            DispatchCompactQueue(cmd, resolveMesh, 4, m_CompactMeshHitArgs, CompactMeshLightingSampler);

            using (new ProfilingScope(cmd, CompactFinalizeSampler))
            {
                cmd.SetComputeBufferParam(shader, finalize, CompactProbesId, m_CompactProbes.innerHandle);
                cmd.SetComputeBufferParam(shader, finalize, CompactContributionsId, m_CompactContributions.innerHandle);
                cmd.SetComputeTextureParam(shader, finalize, GeometryId, m_Geometry.innerHandle);
                cmd.SetComputeTextureParam(shader, finalize, HitLightingId, m_HitLighting.innerHandle);
                cmd.SetComputeTextureParam(shader, finalize, TraceSourcesId, m_TraceSources.innerHandle);
                cmd.SetComputeTextureParam(shader, finalize, ProbeMovingOutputId, m_ProbeMoving.innerHandle);
                cmd.DispatchCompute(shader, finalize, groupsX, groupsY, 1);
            }
        }

        private void DispatchCompactGlobalQueue(ComputeCommandBuffer cmd, int trace, int resolve, int queueIndex,
            RenderGraphBuffer arguments)
        {
            DispatchCompactQueue(cmd, trace, queueIndex, arguments, CompactGlobalSampler);
            using var scope = new ProfilingScope(cmd, CompactGlobalLightingSampler);
            cmd.DispatchCompute(m_TraceCompute, resolve, arguments.innerHandle, 0);
        }

        private void DispatchCompactQueue(ComputeCommandBuffer cmd, int kernel, int queueIndex, RenderGraphBuffer arguments,
            ProfilingSampler sampler)
        {
            using var scope = new ProfilingScope(cmd, sampler);
            cmd.SetComputeIntParam(m_TraceCompute, CompactQueueIndexId, queueIndex);
            cmd.SetComputeBufferParam(m_TraceCompute, m_CompactArgsKernel, CompactArgsId, arguments.innerHandle);
            cmd.DispatchCompute(m_TraceCompute, m_CompactArgsKernel, 1, 1, 1);
            cmd.DispatchCompute(m_TraceCompute, kernel, arguments.innerHandle, 0);
        }

        private void PrepareFinalLighting(Camera camera, VividSdfSurfaceCache cache)
        {
            var history = camera.GetVividCameraHistory();
            if (!history.IsFrameActive)
                return;
            int maxSize = SystemInfo.maxTextureSize;
            if (m_RadiosityTargetOrders == 0)
                m_Scene.PrepareRadiosityCards(Mathf.Min(RadiosityCardLimit, maxSize / RadiosityCardSize * RadiosityColumns));

            var size = cache.CardSize;
            if (size.x <= 0 || size.y <= 0 || size.x > maxSize || size.y > maxSize)
                return;
            int maxColumns = maxSize / size.x;
            int maxRows = maxSize / size.y;
            int texelsPerCard = size.x * size.y;
            int cards = Mathf.Min(m_Scene.RadiosityCardCount,
                Mathf.Min(maxColumns * maxRows, FinalLightingTexelLimit / texelsPerCard));
            if (cards == 0)
                return;
            int columns = Mathf.Max(CoreUtils.DivRoundUp(cards, maxRows), Mathf.Min(cards, Mathf.Min(16, maxColumns)));
            int width = columns * size.x;
            int height = CoreUtils.DivRoundUp(cards, columns) * size.y;

            m_CardLightingDesc.Resize(width, height);
            var descriptor = CameraHistoryRenderGraphBridge.CreateDescriptor(m_CardLightingDesc.desc);
            m_CardFinalHistory = history.GetOrCreateTexture(CardFinalLightingHistoryId, 1, descriptor);
            bool hasDirect = m_LightPositionRange.w != 0f;
            if (hasDirect)
                m_CardDirectHistory = history.GetOrCreateTexture(CardDirectLightingHistoryId, 1, descriptor);
            bool stable = ReferenceEquals(m_State.Cache, cache)
                && m_State.RadiosityLayoutVersion == m_Scene.RadiosityLayoutVersion
                && m_State.FinalLightingCardVersion == cache.CardVersion
                && m_State.FinalLightingReadyCards > 0 && m_State.FinalLightingPriority != null
                && (!hasDirect || m_State.LightPositionRange.w != 0f)
                && m_CardFinalHistory.IsValid(0) && (!hasDirect || m_CardDirectHistory.IsValid(0));
            m_FinalLightingReadyCards = stable ? Mathf.Min(m_State.FinalLightingReadyCards, cards) : 0;
            m_FinalLightingPreviousReadyCards = m_FinalLightingReadyCards;
            m_FinalLightingDirectInputs = m_State.FinalLightingDirectInputs;
            m_FinalLightingUpdateFirst = stable ? Mathf.Min(m_State.FinalLightingUpdateCursor, cards) : 0;
            var inputs = (m_Scene.Revision, m_LightLayerMask, m_Bias, m_LightPositionRange, m_LightColorShadow);
            bool directRemoved = !hasDirect && m_State.LightPositionRange.w != 0f;
            if (!stable || directRemoved || (m_FinalLightingUpdateFirst == cards && !inputs.Equals(m_FinalLightingDirectInputs)))
            {
                m_FinalLightingUpdateFirst = 0;
                m_FinalLightingDirectInputs = inputs;
                (m_State.FinalLightingPriority ??= new CardLightingPriority()).Update(camera, m_Scene, cards);
            }
            int updateBudget = hasDirect ? GetCardDirectUpdateBudget(cards) : cards;
            m_FinalLightingNewCards = Mathf.Min(cards - m_FinalLightingUpdateFirst, updateBudget);
            m_FinalLightingReadyCards = Mathf.Max(m_FinalLightingReadyCards, m_FinalLightingUpdateFirst + m_FinalLightingNewCards);

            // A cold bounce sweep would bake temporary missing direct light into its history.
            if (!m_RadiosityFeedback && m_FinalLightingReadyCards < cards)
                m_RadiosityUpdateCards = 0;
            if (m_RadiosityFeedback)
            {
                m_RadiosityUpdateCards = Mathf.Min(m_RadiosityUpdateCards, Mathf.Max(0, cards - m_RadiosityCursor));
                int probes = Mathf.Max(1, m_RadiosityUpdateCards * RadiosityCardSize * RadiosityCardSize);
                m_RadiosityFeedbackProbes.desc.Count = probes;
                m_RadiosityFeedbackRays.desc.Count = m_RadiosityFeedbackRadiance.desc.Count = probes * RadiosityFeedbackRays;
            }
            m_FinalLightingInputOrder = m_RadiosityProgressive || m_RadiosityAccumulate ? 0 : m_RadiosityOrder;
            if (m_RadiosityClear || m_RadiosityRescale
                || m_State.FinalLightingHasIndirect != m_RadiosityInput.IsValid()
                || m_State.FinalLightingInputOrder != m_FinalLightingInputOrder
                || m_State.RadiosityProgressive != m_RadiosityProgressive || m_State.RadiosityAccumulate != m_RadiosityAccumulate
                || (!m_RadiosityProgressive && !m_RadiosityAccumulate && m_RadiositySweeps > 1))
                InvalidateFinalLightingCards(0, cards);
            m_FinalLightingLayout = new Vector4(size.x, size.y, columns, cards);
            m_CardFinalAtlas = CameraHistoryRenderGraphBridge.ImportForPass(this, m_CardFinalHistory, 0,
                m_FinalLightingNewCards > 0 || m_FinalLightingDirtyEnd > 0 || m_RadiosityUpdateCards > 0
                    ? AccessFlags.ReadWrite : AccessFlags.Read);
            if (hasDirect)
            {
                m_CardDirectAtlas = CameraHistoryRenderGraphBridge.ImportForPass(this, m_CardDirectHistory, 0,
                    m_FinalLightingNewCards > 0 ? AccessFlags.ReadWrite : AccessFlags.Read);
                if (!m_CardDirectAtlas.IsValid())
                    m_CardFinalAtlas = default;
            }
            if (!m_RadiosityFeedback && m_CardFinalAtlas.IsValid() && m_RadiosityUpdateCards > 0)
                m_BounceFinalLighting.Resize(width, height);
        }

        private void InvalidateFinalLightingCards(int first, int count)
        {
            if (count <= 0) return;
            m_FinalLightingDirtyFirst = Mathf.Min(m_FinalLightingDirtyFirst, first);
            m_FinalLightingDirtyEnd = Mathf.Max(m_FinalLightingDirtyEnd, first + count);
        }

        private void BindCardLightingWork(ComputeCommandBuffer cmd, int kernel, TextureHandle output,
            VividSdfSurfaceCache cache, int first)
        {
            var shader = m_CardLightingCompute;
            m_Scene.Bind(cmd, shader, kernel);
            cmd.SetComputeBufferParam(shader, kernel, SurfaceId, m_Scene.DiffuseSurfaces);
            cmd.SetComputeBufferParam(shader, kernel, LocalCardTableId, cache.LocalCardTable);
            cmd.SetComputeBufferParam(shader, kernel, RadiosityWorkId, m_Scene.RadiosityWork);
            cmd.SetComputeTextureParam(shader, kernel, CardAlbedoDepthId, m_CardAlbedoDepth);
            cmd.SetComputeTextureParam(shader, kernel, CardNormalCoverageId, m_CardNormalCoverage);
            cmd.SetComputeTextureParam(shader, kernel, CardLightingOutputId, output);
            cmd.SetComputeIntParam(shader, CardLightingFirstCardId, first);
            cmd.SetComputeVectorParam(shader, FinalLightingLayoutId, m_FinalLightingLayout);
            cmd.SetComputeBufferParam(shader, kernel, FinalLightingOrderId, m_State.FinalLightingPriority.Buffer);
        }

        private void DispatchCardLighting(ComputeCommandBuffer cmd, int kernel, int count)
        {
            cmd.DispatchCompute(m_CardLightingCompute, kernel, CoreUtils.DivRoundUp((int)m_FinalLightingLayout.x, GroupSize),
                CoreUtils.DivRoundUp((int)m_FinalLightingLayout.y, GroupSize), count);
        }

        private void RecordCardDirectLighting(ComputeCommandBuffer cmd, VividSdfSurfaceCache cache)
        {
            if (!m_CardFinalAtlas.IsValid() || !m_CardDirectAtlas.IsValid())
                return;
            if (m_FinalLightingNewCards > 0)
            {
                using var scope = new ProfilingScope(cmd, CardDirectLightingSampler);
                BindSdfTraceConstants(cmd, m_CardLightingCompute);
                BindSunLighting(cmd, m_CardLightingCompute);
                int kernel = m_CardDirectLightingKernel;
                BindCardLightingWork(cmd, kernel, m_CardDirectAtlas, cache, m_FinalLightingUpdateFirst);
                DispatchCardLighting(cmd, kernel, m_FinalLightingNewCards);
            }
            m_CardDirectHistory.MarkWritten();
        }

        private void CombineCardLighting(ComputeCommandBuffer cmd, VividSdfSurfaceCache cache, TextureHandle indirect,
            TextureHandle output, TextureHandle black, int first, int count, bool priorityOrder)
        {
            using var scope = new ProfilingScope(cmd, CardFinalLightingSampler);
            var shader = m_CardLightingCompute;
            int kernel = m_CardFinalLightingKernel;
            BindCardLightingWork(cmd, kernel, output, cache, first);
            cmd.SetComputeIntParam(shader, CardLightingPriorityOrderId, priorityOrder ? 1 : 0);
            cmd.SetComputeIntParam(shader, FinalLightingCardCountId, m_FinalLightingReadyCards);
            cmd.SetComputeIntParam(shader, RadiosityLayoutId, indirect.IsValid() ? m_Scene.RadiosityCardCount : 0);
            cmd.SetComputeTextureParam(shader, kernel, RadiosityInputId, indirect.IsValid() ? indirect : black);
            cmd.SetComputeIntParam(shader, CardLightingHasDirectId, m_CardDirectAtlas.IsValid() ? 1 : 0);
            cmd.SetComputeTextureParam(shader, kernel, CardDirectLightingId, m_CardDirectAtlas.IsValid() ? m_CardDirectAtlas : black);
            DispatchCardLighting(cmd, kernel, count);
        }

        private void BindFinalLighting(ComputeCommandBuffer cmd, ComputeShader shader, int kernel,
            TextureHandle atlas, TextureHandle black, int readyCards = -1)
        {
            int count = readyCards >= 0 ? readyCards : m_FinalLightingReadyCards;
            cmd.SetComputeIntParam(shader, FinalLightingCardCountId, atlas.IsValid() ? count : 0);
            cmd.SetComputeVectorParam(shader, FinalLightingLayoutId, m_FinalLightingLayout);
            cmd.SetComputeBufferParam(shader, kernel, FinalLightingOrderId,
                atlas.IsValid() ? m_State.FinalLightingPriority.Buffer : m_Scene.LeafInstances);
            cmd.SetComputeTextureParam(shader, kernel, FinalLightingAtlasId, atlas.IsValid() ? atlas : black);
        }

        private void RecordCardFinalLighting(ComputeCommandBuffer cmd, int traceKernel, VividSdfSurfaceCache cache, TextureHandle black)
        {
            if (m_CardFinalAtlas.IsValid())
            {
                int capacity = (int)m_FinalLightingLayout.w;
                if (m_FinalLightingDirtyFirst == 0 && m_FinalLightingDirtyEnd >= capacity)
                    CombineCardLighting(cmd, cache, m_RadiosityInput, m_CardFinalAtlas, black, 0, m_FinalLightingReadyCards, true);
                else
                {
                    int count = Mathf.Min(m_FinalLightingDirtyEnd, capacity) - m_FinalLightingDirtyFirst;
                    if (count > 0)
                        CombineCardLighting(cmd, cache, m_RadiosityInput, m_CardFinalAtlas, black, m_FinalLightingDirtyFirst, count, false);
                    if (m_FinalLightingNewCards > 0)
                        CombineCardLighting(cmd, cache, m_RadiosityInput, m_CardFinalAtlas, black,
                            m_FinalLightingUpdateFirst, m_FinalLightingNewCards, true);
                    int feedbackCards = Mathf.Min(m_RadiosityUpdateCards, m_FinalLightingReadyCards - m_RadiosityCursor);
                    if (m_RadiosityFeedback && feedbackCards > 0)
                        CombineCardLighting(cmd, cache, m_RadiosityInput, m_CardFinalAtlas, black,
                            m_RadiosityCursor, feedbackCards, true);
                }
                m_CardFinalHistory.MarkWritten();
            }
            BindFinalLighting(cmd, m_TraceCompute, traceKernel, m_CardFinalAtlas, black);
        }

        private GraphicsBuffer CardLightingWork => m_RadiosityTargetOrders > 0 || m_CardFinalAtlas.IsValid()
            ? m_Scene.RadiosityWork : m_Scene.LeafInstances;

        private void RecordRadiosity(ComputeCommandBuffer cmd, int traceKernel, VividSdfSurfaceCache cache, TextureHandle black)
        {
            if (m_RadiosityFeedback)
            {
                RecordRadiosityFeedback(cmd, cache, black);
                BindRadiosityInput(cmd, traceKernel, black);
                return;
            }
            if (m_RadiosityClear)
            {
                cmd.SetComputeTextureParam(m_RadiosityCompute, m_RadiosityClearKernel,
                    RadiosityOutputId, m_RadiosityPublished);
                cmd.DispatchCompute(m_RadiosityCompute, m_RadiosityClearKernel,
                    m_RadiosityDesc.desc.Width / GroupSize, m_RadiosityDesc.desc.Height / GroupSize, 1);
            }
            if (m_RadiosityRescale)
            {
                using var scope = new ProfilingScope(cmd, RadiosityRescaleSampler);
                cmd.SetComputeTextureParam(m_RadiosityCompute, m_RadiosityRescaleKernel, RadiosityOutputId, m_RadiosityPublished);
                cmd.SetComputeVectorParam(m_RadiosityCompute, RadiosityHistoryScaleId, m_RadiosityHistoryScale);
                cmd.SetComputeIntParam(m_RadiosityCompute, RadiosityCardCountId, m_Scene.RadiosityCardCount);
                cmd.DispatchCompute(m_RadiosityCompute, m_RadiosityRescaleKernel, 1, 1, m_Scene.RadiosityCardCount);
            }
            BindRadiosityInput(cmd, traceKernel, black);
            var work = CardLightingWork;
            for (int sweep = 0; m_RadiosityUpdateCards > 0 && sweep < m_RadiositySweeps; ++sweep)
            {
                if (sweep > 0)
                {
                    // Each sweep consumes only the freshly computed previous order.
                    // Never feed retained high-order published lighting into a low order.
                    m_RadiosityBounceInput = m_RadiosityOutput;
                    m_RadiosityOutput = m_RadiositySweepOutputs[sweep];
                    if (m_RadiosityAccumulate)
                        ++m_RadiosityBounce;
                    else
                        ++m_RadiosityOrder;
                }
                using var scope = new ProfilingScope(cmd, RadiositySampler);
                int kernel = m_RadiosityAccumulate ? m_RadiosityAccumulationKernel : m_RadiosityKernel;
                bool bounceInputValid = m_RadiosityBounceInput.IsValid();
                var bounceInput = bounceInputValid ? m_RadiosityBounceInput : black;
                var bounceLighting = m_CardFinalAtlas.IsValid() ? m_BounceFinalLighting.innerHandle : default;
                if (bounceLighting.IsValid())
                    CombineCardLighting(cmd, cache, m_RadiosityBounceInput, bounceLighting, black, 0, m_FinalLightingReadyCards, true);
                BindFinalLighting(cmd, m_RadiosityCompute, kernel, bounceLighting, black);
                BindSdfTraceConstants(cmd, m_RadiosityCompute);
                BindSunLighting(cmd, m_RadiosityCompute);
                BindOccludedSky(cmd, m_RadiosityCompute, kernel);
                cmd.SetComputeIntParam(m_RadiosityCompute, RadiosityLayoutId, bounceInputValid ? m_Scene.RadiosityCardCount : 0);
                m_Scene.Bind(cmd, m_RadiosityCompute, kernel);
                cmd.SetComputeBufferParam(m_RadiosityCompute, kernel, SurfaceId, m_Scene.DiffuseSurfaces);
                cmd.SetComputeBufferParam(m_RadiosityCompute, kernel, LocalCardTableId, cache.LocalCardTable);
                cmd.SetComputeBufferParam(m_RadiosityCompute, kernel, RadiosityWorkId, work);
                cmd.SetComputeTextureParam(m_RadiosityCompute, kernel, CardAlbedoDepthId, m_CardAlbedoDepth);
                cmd.SetComputeTextureParam(m_RadiosityCompute, kernel, CardNormalCoverageId, m_CardNormalCoverage);
                cmd.SetComputeTextureParam(m_RadiosityCompute, kernel, RadiosityInputId, bounceInput);
                cmd.SetComputeTextureParam(m_RadiosityCompute, kernel, RadiosityOutputId, m_RadiosityOutput);
                cmd.SetComputeIntParam(m_RadiosityCompute, RadiosityFirstCardId, m_RadiosityCursor);
                cmd.SetComputeIntParam(m_RadiosityCompute, RadiositySamplesId, m_RadiositySamples);
                cmd.SetComputeIntParam(m_RadiosityCompute, RadiositySeedId, m_RadiositySeed);
                if (m_RadiosityAccumulate)
                {
                    cmd.SetComputeIntParam(m_RadiosityCompute, RadiosityBatchId, m_RadiosityBatch);
                    cmd.SetComputeIntParam(m_RadiosityCompute, RadiosityBounceId, m_RadiosityBounce);
                }
                cmd.DispatchCompute(m_RadiosityCompute, kernel, 1, 1, m_RadiosityUpdateCards);
                bool finishedSweep = m_RadiosityCursor + m_RadiosityUpdateCards == m_Scene.RadiosityCardCount;
                int order = (m_RadiosityAccumulate ? m_RadiosityBounce : m_RadiosityOrder) + 1;
                bool firstBatch = !m_RadiosityAccumulate || m_RadiosityBatch == 0;
                bool publishCards = firstBatch && (m_RadiosityTransitionBlend > 0f || order == m_RadiosityTargetOrders);
                if (m_RadiosityProgressive && (publishCards || !firstBatch && order == m_RadiosityTargetOrders && finishedSweep))
                {
                    // The first batch replaces cards as soon as fresh low-order light
                    // exists. Subsequent sample batches only replace the target order.
                    int first = m_RadiosityAccumulate && m_RadiosityBatch > 0 ? 0 : m_RadiosityCursor;
                    int count = m_RadiosityAccumulate && m_RadiosityBatch > 0
                        ? m_Scene.RadiosityCardCount : m_RadiosityUpdateCards;
                    int publishKernel = m_RadiosityPublishCardsKernel;
                    cmd.SetComputeTextureParam(m_RadiosityCompute, publishKernel, RadiosityInputId, m_RadiosityOutput);
                    cmd.SetComputeTextureParam(m_RadiosityCompute, publishKernel, RadiosityOutputId, m_RadiosityPublished);
                    cmd.SetComputeIntParam(m_RadiosityCompute, RadiosityFirstCardId, first);
                    cmd.SetComputeIntParam(m_RadiosityCompute, RadiosityCardCountId, m_Scene.RadiosityCardCount);
                    cmd.SetComputeFloatParam(m_RadiosityCompute, RadiosityPublishBlendId,
                        order == m_RadiosityTargetOrders ? 1f : m_RadiosityTransitionBlend);
                    cmd.DispatchCompute(m_RadiosityCompute, publishKernel, 1, 1, count);
                    InvalidateFinalLightingCards(first, count);
                    if (finishedSweep)
                        m_RadiosityPublishedOrder = order;
                }
                else if (!m_RadiosityProgressive && m_RadiosityAccumulate && m_RadiosityBounce + 1 == m_RadiosityTargetOrders
                    && m_RadiosityCursor + m_RadiosityUpdateCards == m_Scene.RadiosityCardCount)
                {
                    // Publish a complete target-order estimate, never a partially updated card sweep.
                    int publishKernel = m_RadiosityPublishKernel;
                    cmd.SetComputeTextureParam(m_RadiosityCompute, publishKernel, RadiosityInputId, m_RadiosityOutput);
                    cmd.SetComputeTextureParam(m_RadiosityCompute, publishKernel, RadiosityOutputId, m_RadiosityPublished);
                    cmd.DispatchCompute(m_RadiosityCompute, publishKernel,
                        m_RadiosityDesc.desc.Width / GroupSize, m_RadiosityDesc.desc.Height / GroupSize, 1);
                    InvalidateFinalLightingCards(0, m_Scene.RadiosityCardCount);
                }
            }

            // The atlases are single-slot histories. Carry validity through read-only
            // frames as well; the order and cursor gate all reads of unfinished sweeps.
            m_RadiosityReadHistory?.MarkWritten();
            m_RadiosityWriteHistory?.MarkWritten();
            m_RadiosityPublishedHistory?.MarkWritten();
            for (int order = 0; order < m_RadiosityTargetOrders && m_RadiosityAccumulate; ++order)
                m_RadiosityOrderHistories[order]?.MarkWritten();
        }

        private void BindRadiosityInput(ComputeCommandBuffer cmd, int traceKernel, TextureHandle black)
        {
            bool published = m_RadiosityInput.IsValid();
            cmd.SetComputeIntParam(m_TraceCompute, RadiosityLayoutId, published ? m_Scene.RadiosityCardCount : 0);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, RadiosityInputId, published ? m_RadiosityInput : black);
            cmd.SetComputeBufferParam(m_TraceCompute, traceKernel, RadiosityWorkId, CardLightingWork);
        }

        private void RecordRadiosityFeedback(ComputeCommandBuffer cmd, VividSdfSurfaceCache cache, TextureHandle black)
        {
            if (!m_RadiosityInput.IsValid() || !m_RadiosityFeedbackCount.IsValid())
            {
                m_RadiosityUpdateCards = 0;
                return;
            }

            var shader = m_RadiosityCompute;
            if (m_RadiosityClear)
            {
                cmd.SetComputeTextureParam(shader, m_FeedbackClearKernel, RadiosityOutputId, m_RadiosityInput);
                cmd.SetComputeTextureParam(shader, m_FeedbackClearKernel, FeedbackCountId, m_RadiosityFeedbackCount);
                cmd.DispatchCompute(shader, m_FeedbackClearKernel, m_RadiosityDesc.desc.Width / GroupSize,
                    m_RadiosityDesc.desc.Height / GroupSize, 1);
            }
            m_RadiosityPublishedHistory.MarkWritten();
            m_RadiosityFeedbackCountHistory.MarkWritten();

            if (m_GlobalClipmap?.Recorded != true || !m_ObjectGrid.IsValid() || !m_CardFinalAtlas.IsValid()
                || !m_RadiosityFeedbackProbes.IsValid() || !m_RadiosityFeedbackRays.IsValid() || !m_RadiosityFeedbackRadiance.IsValid())
                m_RadiosityUpdateCards = 0;
            if (m_RadiosityUpdateCards <= 0)
                return;

            using var scope = new ProfilingScope(cmd, RadiositySampler);
            var order = m_State.FinalLightingPriority.Buffer;
            var probes = m_RadiosityFeedbackProbes.innerHandle;
            var rays = m_RadiosityFeedbackRays.innerHandle;
            var radiance = m_RadiosityFeedbackRadiance.innerHandle;
            int rayGroups = m_RadiosityUpdateCards * RadiosityFeedbackRays;
            int width = Mathf.Min(rayGroups, 32768);
            int height = CoreUtils.DivRoundUp(rayGroups, width);
            cmd.SetComputeIntParam(shader, RadiosityFirstCardId, m_RadiosityCursor);
            cmd.SetComputeIntParam(shader, RadiosityCardCountId, m_RadiosityUpdateCards);
            cmd.SetComputeIntParam(shader, RadiositySeedId, m_RadiositySeed);
            cmd.SetComputeIntParam(shader, FrameId, unchecked((int)m_State.Frame));
            cmd.SetComputeIntParam(shader, FeedbackDispatchWidthId, width);
            BindSdfTraceConstants(cmd, shader);

            m_Scene.Bind(cmd, shader, m_FeedbackPrepareKernel);
            cmd.SetComputeBufferParam(shader, m_FeedbackPrepareKernel, SurfaceId, m_Scene.DiffuseSurfaces);
            cmd.SetComputeBufferParam(shader, m_FeedbackPrepareKernel, LocalCardTableId, cache.LocalCardTable);
            cmd.SetComputeBufferParam(shader, m_FeedbackPrepareKernel, RadiosityWorkId, m_Scene.RadiosityWork);
            cmd.SetComputeBufferParam(shader, m_FeedbackPrepareKernel, FinalLightingOrderId, order);
            cmd.SetComputeBufferParam(shader, m_FeedbackPrepareKernel, FeedbackProbeWriteId, probes);
            cmd.SetComputeTextureParam(shader, m_FeedbackPrepareKernel, CardAlbedoDepthId, m_CardAlbedoDepth);
            cmd.SetComputeTextureParam(shader, m_FeedbackPrepareKernel, CardNormalCoverageId, m_CardNormalCoverage);
            cmd.DispatchCompute(shader, m_FeedbackPrepareKernel, 1, 1, m_RadiosityUpdateCards);

            using (new ProfilingScope(cmd, RadiosityTraceSampler))
            {
                BindGlobalTracingResources(cmd, shader, m_FeedbackTraceKernel, true);
                cmd.SetComputeBufferParam(shader, m_FeedbackTraceKernel, FinalLightingOrderId, order);
                cmd.SetComputeBufferParam(shader, m_FeedbackTraceKernel, FeedbackProbeId, probes);
                cmd.SetComputeBufferParam(shader, m_FeedbackTraceKernel, FeedbackRayWriteId, rays);
                cmd.DispatchCompute(shader, m_FeedbackTraceKernel, width, height, 1);
            }

            using (new ProfilingScope(cmd, RadiosityResolveSampler))
            {
                int kernel = m_FeedbackResolveKernel;
                m_Scene.Bind(cmd, shader, kernel);
                BindGlobalTracingResources(cmd, shader, kernel, true);
                BindOccludedSky(cmd, shader, kernel);
                // Direct lighting writes its own atlas. FinalLighting is still the previous frame here.
                BindFinalLighting(cmd, shader, kernel, m_CardFinalAtlas, black, m_FinalLightingPreviousReadyCards);
                cmd.SetComputeBufferParam(shader, kernel, RadiosityWorkId, m_Scene.RadiosityWork);
                cmd.SetComputeBufferParam(shader, kernel, SurfaceId, m_Scene.DiffuseSurfaces);
                cmd.SetComputeBufferParam(shader, kernel, LocalCardTableId, cache.LocalCardTable);
                cmd.SetComputeTextureParam(shader, kernel, CardAlbedoDepthId, m_CardAlbedoDepth);
                cmd.SetComputeTextureParam(shader, kernel, CardNormalCoverageId, m_CardNormalCoverage);
                cmd.SetComputeBufferParam(shader, kernel, FeedbackProbeId, probes);
                cmd.SetComputeBufferParam(shader, kernel, FeedbackRayId, rays);
                cmd.SetComputeBufferParam(shader, kernel, FeedbackRadianceWriteId, radiance);
                cmd.DispatchCompute(shader, kernel, width, height, 1);
            }

            using (new ProfilingScope(cmd, RadiosityIntegrateSampler))
            {
                int kernel = m_FeedbackIntegrateKernel;
                cmd.SetComputeBufferParam(shader, kernel, FinalLightingOrderId, order);
                cmd.SetComputeBufferParam(shader, kernel, FeedbackProbeId, probes);
                cmd.SetComputeBufferParam(shader, kernel, FeedbackRadianceId, radiance);
                cmd.SetComputeTextureParam(shader, kernel, RadiosityOutputId, m_RadiosityInput);
                cmd.SetComputeTextureParam(shader, kernel, FeedbackCountId, m_RadiosityFeedbackCount);
                cmd.DispatchCompute(shader, kernel, 1, 1, m_RadiosityUpdateCards);
            }
        }

        private static int FarSlot(Vector3Int cell)
        {
            const int mask = FarGridSize - 1;
            return (cell.x & mask) + FarGridSize * ((cell.z & mask) + FarGridSize * (cell.y & mask));
        }

        private uint FarSceneVersion => m_FullSphere ? m_Scene.NonMotionTransportVersion : m_Scene.Revision;

        private void PrepareFarCache(Camera camera, VividSdfSurfaceCache cache, float distance)
        {
            var history = camera.GetVividCameraHistory();
            if (!history.IsFrameActive || SystemInfo.maxTextureSize < (m_FullSphere ? WorldAtlasHeight : FarGridSize * (FarDirectionCount + 1)))
                return;

            // 64 x 64 probe tiles, each 32 x 32 texels, followed by physical slot metadata and four 48-cubed indirection grids.
            m_FarDesc.Resize(m_FullSphere ? 2048 : FarGridSize * FarGridSize, m_FullSphere ? WorldAtlasHeight : FarGridSize * (FarDirectionCount + 1));
            m_FarHistory = history.GetOrCreateTexture(FarHistoryId, 1,
                CameraHistoryRenderGraphBridge.CreateDescriptor(m_FarDesc.desc));
            if (!m_FullSphere)
                m_FarHitIdsHistory = history.GetOrCreateBuffer(FarHitIdsHistoryId, 1, FarHitIdsDescriptor);
            m_FarDistance = distance;
            m_FarCardVersion = cache.CardVersion;
            var center = Vector3Int.FloorToInt(camera.transform.position / FarCellSize);
            m_FarWindow = center - new Vector3Int(FarGridSize / 2, FarGridSize / 2, FarGridSize / 2);
            var cameraPosition = camera.transform.position;
            m_FarWindowParameter = m_FullSphere
                ? new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, camera.orthographic ? 1f : 0f)
                : new Vector4(m_FarWindow.x, m_FarWindow.y, m_FarWindow.z, 0f);
            m_FarClear = !m_FarHistory.IsValid(0) || m_State.FarDistance != m_FarDistance
                || m_State.FarSceneVersion != FarSceneVersion || !ReferenceEquals(m_State.Cache, cache)
                || m_State.FarCardVersion != m_FarCardVersion || m_State.LayerMask != m_LayerMask
                || m_State.MaxDistance != m_TraceDistance || m_State.RayBias != m_Bias;
            if (m_FullSphere)
            {
                // Keep residency and probe ages through lighting updates and rigid motion.
                // The world scheduler refreshes them using current sky and card lighting.
                m_WorldRawDesc.Resize(2048, 2048);
                m_WorldRawHistory = history.GetOrCreateTexture(WorldRawRadianceHistoryId, 1,
                    CameraHistoryRenderGraphBridge.CreateDescriptor(m_WorldRawDesc.desc));
                m_FarClear |= !m_WorldRawHistory.IsValid(0);
                m_WorldRawRadiance = CameraHistoryRenderGraphBridge.ImportForPass(this, m_WorldRawHistory, 0, AccessFlags.ReadWrite);
                m_FarAtlas = CameraHistoryRenderGraphBridge.ImportForPass(this, m_FarHistory, 0, AccessFlags.ReadWrite);
                return;
            }

            // The legacy fixed grid fills missing probes and has no continuous refresh.
            bool finishedCardLighting = m_FinalLightingNewCards > 0
                && m_FinalLightingUpdateFirst + m_FinalLightingNewCards == (int)m_FinalLightingLayout.w;
            m_FarClear |= !m_FarHitIdsHistory.IsValid(0) || m_State.GatherRadiosityOrder != PublishedRadiosityOrder
                || !MatchesPreviousDirectionalLighting() || m_RadiosityRestarting && !m_State.RadiosityRestarting
                || finishedCardLighting;
            var keys = m_State.FarKeys ??= new Vector3Int[FarProbeCount];
            if (m_FarClear)
                System.Array.Fill(keys, new Vector3Int(int.MinValue, int.MinValue, int.MinValue));

            if (m_FarClear || m_State.FarPending || m_State.FarWindow != m_FarWindow)
            {
                for (int i = 0; i < FarProbeCount; i++)
                {
                    var cell = m_FarWindow + new Vector3Int(i % FarGridSize, i / (FarGridSize * FarGridSize),
                        (i / FarGridSize) % FarGridSize);
                    if (keys[FarSlot(cell)] == cell)
                        continue;
                    m_FarUpdates[m_FarUpdateCount++] = new Vector4(cell.x, cell.y, cell.z, 0f);
                    if (m_FarUpdateCount == FarProbesPerFrame)
                    {
                        m_FarPending = true;
                        break;
                    }
                }
            }
            m_FarAtlas = CameraHistoryRenderGraphBridge.ImportForPass(this, m_FarHistory, 0,
                m_FarClear || m_FarUpdateCount > 0 ? AccessFlags.ReadWrite : AccessFlags.Read);
            m_FarHitIds = CameraHistoryRenderGraphBridge.ImportForPass(this, m_FarHitIdsHistory, 0,
                m_FarUpdateCount > 0 ? AccessFlags.ReadWrite : AccessFlags.Read);
        }

        private bool IsFarCacheReady() => m_FarAtlas.IsValid() && (m_FullSphere
            ? m_WorldRawRadiance.IsValid() && m_WorldDispatchArgs.innerHandle.IsValid()
            : m_FarHitIds.IsValid());

        private void RecordFarCache(ComputeCommandBuffer cmd, int traceKernel, VividSdfSurfaceCache cache, TextureHandle black, TextureHandle placement)
        {
            bool ready = IsFarCacheReady();
            cmd.SetComputeFloatParam(m_TraceCompute, FarDistanceId, ready ? m_FarDistance : 0f);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, FarRadianceId, ready ? m_FarAtlas : black);
            if (ready && !m_FullSphere)
                cmd.SetComputeBufferParam(m_TraceCompute, traceKernel, FarHitIdsId, m_FarHitIds);
            else
                cmd.SetComputeBufferParam(m_TraceCompute, traceKernel, FarHitIdsId, m_Scene.LeafInstances);
            if (!ready)
                return;
            cmd.SetComputeVectorParam(m_TraceCompute, FarWindowId, m_FarWindowParameter);

            if (m_FullSphere || m_FarClear || m_FarUpdateCount > 0)
            {
                using var scope = new ProfilingScope(cmd, FarCacheSampler);
                bool worldSky = m_FullSphere && m_SkyTexture.IsValid() && m_SkyTextureParams.w > 0.5f;
                bool useWorldGlobal = m_FullSphere && m_GlobalClipmap?.Recorded == true
                    && (worldSky ? m_WorldGlobalSkyKernel >= 0 : m_WorldGlobalUpdateKernel >= 0);
                int kernel = m_FullSphere
                    ? (useWorldGlobal
                        ? (worldSky ? m_WorldGlobalSkyKernel : m_WorldGlobalUpdateKernel)
                        : (worldSky ? m_WorldSkyKernel : m_WorldUpdateKernel))
                    : m_FarKernel;
                BindSdfTraceConstants(cmd, m_FarCompute);
                BindSunLighting(cmd, m_FarCompute);
                if (m_FullSphere)
                    BindSegmentedTracing(cmd, m_FarCompute, kernel, m_GlobalClipmap?.Recorded == true);
                cmd.SetComputeFloatParam(m_FarCompute, FarDistanceId, m_FarDistance);
                cmd.SetComputeIntParam(m_FarCompute, RadiosityLayoutId, m_RadiosityInput.IsValid() ? m_Scene.RadiosityCardCount : 0);
                m_Scene.Bind(cmd, m_FarCompute, kernel);
                cmd.SetComputeBufferParam(m_FarCompute, kernel, SurfaceId, m_Scene.DiffuseSurfaces);
                cmd.SetComputeBufferParam(m_FarCompute, kernel, LocalCardTableId, cache.LocalCardTable);
                cmd.SetComputeTextureParam(m_FarCompute, kernel, CardAlbedoDepthId, m_CardAlbedoDepth);
                cmd.SetComputeTextureParam(m_FarCompute, kernel, CardNormalCoverageId, m_CardNormalCoverage);
                cmd.SetComputeBufferParam(m_FarCompute, kernel, RadiosityWorkId, CardLightingWork);
                BindFinalLighting(cmd, m_FarCompute, kernel, m_CardFinalAtlas, black);
                cmd.SetComputeTextureParam(m_FarCompute, kernel, RadiosityInputId,
                    m_RadiosityInput.IsValid() ? m_RadiosityInput : black);
                cmd.SetComputeTextureParam(m_FarCompute, kernel, FarOutputId, m_FarAtlas);
                if (m_FullSphere)
                {
                    BindProbeFrame(cmd, m_FarCompute);
                    cmd.SetComputeVectorParam(m_FarCompute, FarWindowId, m_FarWindowParameter);
                    cmd.SetComputeIntParam(m_FarCompute, FrameId, unchecked((int)m_State.Frame - 1));
                    cmd.SetComputeIntParam(m_FarCompute, FarClearId, m_FarClear ? 1 : 0);
                    var work = m_WorldWork.innerHandle;
                    cmd.SetComputeTextureParam(m_FarCompute, m_WorldClearKernel, WorldWorkId, work);
                    cmd.SetComputeTextureParam(m_FarCompute, m_WorldClearKernel, FarOutputId, m_FarAtlas);
                    cmd.DispatchCompute(m_FarCompute, m_WorldClearKernel, 128, 54, 1);
                    BindReceiver(cmd, m_FarCompute, m_WorldMarkKernel);
                    cmd.SetComputeTextureParam(m_FarCompute, m_WorldMarkKernel, PlacementId, placement);
                    cmd.SetComputeTextureParam(m_FarCompute, m_WorldMarkKernel, WorldWorkId, work);
                    cmd.DispatchCompute(m_FarCompute, m_WorldMarkKernel, CoreUtils.DivRoundUp(m_ProbeWidth, 8), CoreUtils.DivRoundUp(m_AtlasHeight, 8), 1);
                    cmd.SetComputeTextureParam(m_FarCompute, m_WorldReuseKernel, WorldWorkId, work);
                    cmd.SetComputeTextureParam(m_FarCompute, m_WorldReuseKernel, FarOutputId, m_FarAtlas);
                    cmd.DispatchCompute(m_FarCompute, m_WorldReuseKernel, 64, 1, 1);
                    cmd.SetComputeTextureParam(m_FarCompute, m_WorldAllocateKernel, WorldWorkId, work);
                    cmd.SetComputeTextureParam(m_FarCompute, m_WorldAllocateKernel, FarOutputId, m_FarAtlas);
                    cmd.DispatchCompute(m_FarCompute, m_WorldAllocateKernel, 128, 54, 1);
                    cmd.SetComputeTextureParam(m_FarCompute, m_WorldSelectKernel, WorldWorkId, work);
                    cmd.DispatchCompute(m_FarCompute, m_WorldSelectKernel, 1, 1, 1);
                    cmd.SetComputeTextureParam(m_FarCompute, m_WorldTicketsKernel, WorldWorkId, work);
                    cmd.SetComputeTextureParam(m_FarCompute, m_WorldTicketsKernel, FarOutputId, m_FarAtlas);
                    cmd.DispatchCompute(m_FarCompute, m_WorldTicketsKernel, 64, 1, 1);
                    var dispatchArgs = m_WorldDispatchArgs.innerHandle;
                    cmd.SetComputeTextureParam(m_FarCompute, m_WorldArgsKernel, WorldWorkId, work);
                    cmd.SetComputeBufferParam(m_FarCompute, m_WorldArgsKernel, WorldDispatchArgsId, dispatchArgs);
                    cmd.DispatchCompute(m_FarCompute, m_WorldArgsKernel, 1, 1, 1);
                    cmd.SetComputeTextureParam(m_FarCompute, kernel, WorldWorkId, work);
                    cmd.SetComputeTextureParam(m_FarCompute, kernel, WorldRawRadianceId, m_WorldRawRadiance);
                    if (worldSky)
                        BindOccludedSky(cmd, m_FarCompute, kernel);
                    cmd.DispatchCompute(m_FarCompute, kernel, dispatchArgs, 0);
                    cmd.SetComputeTextureParam(m_FarCompute, m_WorldFilterKernel, WorldWorkId, work);
                    cmd.SetComputeTextureParam(m_FarCompute, m_WorldFilterKernel, WorldRawRadianceId, m_WorldRawRadiance);
                    cmd.SetComputeTextureParam(m_FarCompute, m_WorldFilterKernel, FarOutputId, m_FarAtlas);
                    cmd.DispatchCompute(m_FarCompute, m_WorldFilterKernel, dispatchArgs, 0);
                    m_WorldRawHistory.MarkWritten();
                    m_FarHistory.MarkWritten();
                    return;
                }
                cmd.SetComputeBufferParam(m_FarCompute, kernel, FarHitIdsOutputId, m_FarHitIds);
                if (m_FarClear)
                {
                    cmd.SetComputeIntParam(m_FarCompute, FarClearId, 1);
                    cmd.DispatchCompute(m_FarCompute, kernel, FarProbeCount / FarDirectionCount, 1, 1);
                }
                cmd.SetComputeIntParam(m_FarCompute, FarClearId, 0);
                if (m_FarUpdateCount > 0)
                {
                    cmd.SetComputeVectorArrayParam(m_FarCompute, FarUpdatesId, m_FarUpdates);
                    cmd.DispatchCompute(m_FarCompute, kernel, m_FarUpdateCount, 1, 1);
                    for (int i = 0; i < m_FarUpdateCount; i++)
                    {
                        var p = m_FarUpdates[i];
                        var cell = new Vector3Int((int)p.x, (int)p.y, (int)p.z);
                        m_State.FarKeys[FarSlot(cell)] = cell;
                    }
                }
            }
            m_FarHistory.MarkWritten();
            m_FarHitIdsHistory.MarkWritten();
        }

        private void BindSegmentedTracing(ComputeCommandBuffer cmd, ComputeShader shader, int kernel, bool useGlobalClipmap)
        {
            cmd.SetComputeFloatParam(shader, MeshDetailDistanceId, m_MeshDetailDistance);
            cmd.SetComputeFloatParam(shader, FarDistanceId, 0f);
            cmd.SetComputeIntParam(shader, UseGlobalClipmapId, useGlobalClipmap ? (m_ObjectGrid.IsValid() ? 2 : 1) : 0);
            BindGlobalTracingResources(cmd, shader, kernel, useGlobalClipmap);
        }

        private void BindGlobalTracingResources(ComputeCommandBuffer cmd, ComputeShader shader, int kernel, bool useGlobalClipmap)
        {
            var fallback = m_GlobalFallback.innerHandle;
            cmd.SetComputeTextureParam(shader, kernel, GlobalNearId, useGlobalClipmap ? m_GlobalNear : fallback);
            cmd.SetComputeTextureParam(shader, kernel, GlobalMiddleId, useGlobalClipmap ? m_GlobalMiddle : fallback);
            cmd.SetComputeTextureParam(shader, kernel, GlobalFarId, useGlobalClipmap ? m_GlobalFar : fallback);
            bool useMip = useGlobalClipmap && m_GlobalMip.IsValid();
            cmd.SetComputeTextureParam(shader, kernel, GlobalMipId, useMip ? m_GlobalMip : fallback);
            cmd.SetComputeIntParam(shader, GlobalUseMipId, useMip ? 1 : 0);
            if (!useGlobalClipmap)
            {
                cmd.SetComputeBufferParam(shader, kernel, ObjectWorkId, m_Scene.LeafInstances);
                cmd.SetComputeBufferParam(shader, kernel, ObjectPagesId, m_Scene.LeafInstances);
                cmd.SetComputeBufferParam(shader, kernel, ObjectGridId, m_Scene.LeafInstances);
                cmd.SetComputeBufferParam(shader, kernel, GlobalPagesId, m_Scene.LeafInstances);
                cmd.SetComputeBufferParam(shader, kernel, GlobalThinBricksId, m_Scene.LeafInstances);
                return;
            }
            cmd.SetComputeBufferParam(shader, kernel, ObjectWorkId, m_ObjectPageWork.IsValid() ? m_ObjectPageWork : m_GlobalPages);
            cmd.SetComputeBufferParam(shader, kernel, ObjectPagesId, m_ObjectPageTable.IsValid() ? m_ObjectPageTable : m_GlobalPages);
            cmd.SetComputeBufferParam(shader, kernel, ObjectGridId, m_ObjectGrid.IsValid() ? m_ObjectGrid : m_GlobalPages);
            cmd.SetComputeBufferParam(shader, kernel, GlobalPagesId, m_GlobalPages);
            cmd.SetComputeBufferParam(shader, kernel, GlobalThinBricksId, m_GlobalThinBricks);
            cmd.SetComputeIntParam(shader, GlobalLayerMaskId, m_GlobalClipmap.LayerMask);
            cmd.SetComputeVectorArrayParam(shader, GlobalLevelsId, m_GlobalClipmap.Levels);
            cmd.SetComputeVectorArrayParam(shader, GlobalScrollId, m_GlobalClipmap.Scroll);
        }

        private void RecordLumenBentNormal(ComputeCommandBuffer cmd, int frame, bool halfResolution)
        {
            using var scope = new ProfilingScope(cmd, NearOcclusionSampler);
            var shader = m_NearOcclusionCompute;
            BindProbeFrame(cmd, shader);
            int kernel = halfResolution ? m_LumenBentHalfKernel : m_LumenBentKernel;
            if (!halfResolution)
            {
                cmd.SetComputeTextureParam(shader, m_BentFurthestHzbKernel, DepthId, m_Depth.innerHandle);
                cmd.SetComputeTextureParam(shader, m_BentFurthestHzbKernel, BentFurthestHzbOutputId, m_BentFurthestHzb.innerHandle);
                cmd.DispatchCompute(shader, m_BentFurthestHzbKernel, CoreUtils.DivRoundUp(m_Width, GroupSize * 2),
                    CoreUtils.DivRoundUp(m_Height, GroupSize * 2), 1);
                cmd.SetComputeTextureParam(shader, kernel, BentFurthestHzbId, m_BentFurthestHzb.innerHandle);
            }
            else
                cmd.SetComputeIntParam(shader, BentFlipYId, m_BentFlipY ? 1 : 0);
            BindReceiver(cmd, shader, kernel);
            cmd.SetComputeIntParam(shader, FrameId, frame);
            cmd.SetComputeMatrixParam(shader, BentWorldToViewId, m_BentWorldToView);
            cmd.SetComputeMatrixParam(shader, BentInverseProjectionId, m_BentInverseProjection);
            cmd.SetComputeTextureParam(shader, kernel, BentBlueNoiseId, m_BentBlueNoise);
            cmd.SetComputeTextureParam(shader, kernel, LumenBentNormalOutputId, m_LumenBentNormal.innerHandle);
            int groupSize = halfResolution ? GroupSize * 2 : GroupSize;
            cmd.DispatchCompute(shader, kernel, CoreUtils.DivRoundUp(m_Width, groupSize),
                CoreUtils.DivRoundUp(m_Height, groupSize), 1);
        }

        private void RecordBentNormal(ComputeCommandBuffer cmd, int frame, bool geometryWritten, TextureHandle black)
        {
            using var scope = new ProfilingScope(cmd, NearOcclusionSampler);
            var shader = m_NearOcclusionCompute;
            BindProbeFrame(cmd, shader);
            cmd.SetComputeIntParam(shader, FrameId, frame);
            cmd.SetComputeVectorParam(shader, NearOcclusionId, m_NearOcclusion);
            BindReceiver(cmd, shader, m_BentSearchKernel);
            cmd.SetComputeTextureParam(shader, m_BentSearchKernel, BentSamplesOutputId, m_BentSamples.innerHandle);
            cmd.DispatchCompute(shader, m_BentSearchKernel, CoreUtils.DivRoundUp(m_Width, GroupSize * 2),
                CoreUtils.DivRoundUp(m_Height, GroupSize * 2), 1);

            BindReceiver(cmd, shader, m_BentTemporalKernel);
            cmd.SetComputeMatrixParam(shader, PreviousViewProjectionId, m_State.ViewProjection);
            cmd.SetComputeMatrixParam(shader, PreviousInverseVpId, m_State.ViewProjection.inverse);
            cmd.SetComputeVectorParam(shader, JitterDeltaId, new Vector4(m_ScreenJitterDelta.x, m_ScreenJitterDelta.y, 0f, 0f));
            cmd.SetComputeIntParam(shader, HasHistoryId, m_BentNormalHistoryValid && m_ScreenMotion.innerHandle.IsValid() ? 1 : 0);
            cmd.SetComputeIntParam(shader, BentWriteGeometryId, geometryWritten ? 0 : 1);
            cmd.SetComputeTextureParam(shader, m_BentTemporalKernel, MotionId, m_ScreenMotion.innerHandle.IsValid() ? m_ScreenMotion.innerHandle : black);
            cmd.SetComputeTextureParam(shader, m_BentTemporalKernel, PreviousPixelGeometryId, m_PreviousPixelGeometry.innerHandle);
            cmd.SetComputeTextureParam(shader, m_BentTemporalKernel, PixelGeometryOutputId, m_CurrentPixelGeometry.innerHandle);
            cmd.SetComputeTextureParam(shader, m_BentTemporalKernel, BentSamplesId, m_BentSamples.innerHandle);
            cmd.SetComputeTextureParam(shader, m_BentTemporalKernel, BentPreviousId, m_PreviousBentNormal.innerHandle);
            cmd.SetComputeTextureParam(shader, m_BentTemporalKernel, BentOutputId, m_CurrentBentNormal.innerHandle);
            cmd.SetComputeTextureParam(shader, m_BentTemporalKernel, OutputId, m_Output.innerHandle);
            cmd.DispatchCompute(shader, m_BentTemporalKernel, CoreUtils.DivRoundUp(m_Width, GroupSize),
                CoreUtils.DivRoundUp(m_Height, GroupSize), 1);
            m_BentNormalHistory.MarkWritten();
            if (!geometryWritten)
                m_PixelGeometryHistory.MarkWritten();
        }

        private void RecordNearOcclusion(ComputeCommandBuffer cmd)
        {
            using var scope = new ProfilingScope(cmd, NearOcclusionSampler);
            int kernel = m_NearOcclusionKernel;
            BindProbeFrame(cmd, m_NearOcclusionCompute);
            cmd.SetComputeMatrixParam(m_NearOcclusionCompute, ViewProjectionId, m_ViewProjection);
            cmd.SetComputeVectorParam(m_NearOcclusionCompute, NearOcclusionId, m_NearOcclusion);
            BindReceiver(cmd, m_NearOcclusionCompute, kernel);
            cmd.SetComputeTextureParam(m_NearOcclusionCompute, kernel, OutputId, m_Output.innerHandle);
            cmd.DispatchCompute(m_NearOcclusionCompute, kernel, CoreUtils.DivRoundUp(m_Width, GroupSize),
                CoreUtils.DivRoundUp(m_Height, GroupSize), 1);
        }

        private void PrepareScreenHistory(ContextContainer frameData, Camera camera, VividTemporalData temporal)
        {
            var history = camera.GetVividCameraHistory();
            if (!history.IsFrameActive)
                return;

            m_ScreenRadianceDesc.Resize(m_Width, m_Height);
            m_ScreenGeometryDesc.Resize(m_Width, m_Height);
            var screen = frameData.GetOrCreate<VividSdfScreenTraceData>();
            var radiance = history.GetOrCreateTexture(m_LumenScreenTrace ? SceneColorHistoryId : ScreenRadianceHistoryId, 2,
                CameraHistoryRenderGraphBridge.CreateDescriptor(m_ScreenRadianceDesc.desc));
            var geometry = history.GetOrCreateTexture(m_LumenScreenTrace ? SceneDepthExposureId : ScreenGeometryHistoryId, 2,
                CameraHistoryRenderGraphBridge.CreateDescriptor(m_ScreenGeometryDesc.desc));
            if (m_LumenScreenTrace)
            {
                screen.sceneColor = radiance;
                screen.sceneDepthExposure = geometry;
                screen.preExposure = PassRecorder.ImportBufferHandle(m_PreExposure);
                screen.depth = m_Depth;
                screen.shader = m_TraceCompute;
                screen.kernel = m_SceneColorKernel;
                screen.currentColor = CameraHistoryRenderGraphBridge.Import(radiance, 0);
                screen.currentDepthExposure = CameraHistoryRenderGraphBridge.Import(geometry, 0);
            }
            else
            {
                screen.radiance = radiance;
                screen.geometry = geometry;
            }
            if (temporal.isFirstFrame || temporal.resetPostProcessingHistory || m_State.Width != m_Width
                || m_State.Height != m_Height || m_State.LumenScreenTrace != m_LumenScreenTrace
                || !radiance.IsValid() || !geometry.IsValid())
                return;

            m_PreviousScreenRadiance = CameraHistoryRenderGraphBridge.ImportForPass(this, radiance, 1, AccessFlags.Read);
            m_PreviousScreenGeometry = CameraHistoryRenderGraphBridge.ImportForPass(this, geometry, 1, AccessFlags.Read);
            m_HasScreenHistory = m_PreviousScreenRadiance.IsValid() && m_PreviousScreenGeometry.IsValid();
        }

        private void BindRestirReuse(ComputeCommandBuffer cmd, int kernel, TextureHandle hzb)
        {
            m_Scene.Bind(cmd, m_RestirCompute, kernel);
            cmd.SetComputeTextureParam(m_RestirCompute, kernel, GeometryId, m_Geometry.innerHandle);
            cmd.SetComputeTextureParam(m_RestirCompute, kernel, DepthId, m_Depth.innerHandle);
            cmd.SetComputeTextureParam(m_RestirCompute, kernel, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeTextureParam(m_RestirCompute, kernel, HzbId, hzb);
        }

        private void RecordRestir(ComputeCommandBuffer cmd, TextureHandle black, TextureHandle hzb, bool hasHistory,
            bool useScreenRays, int frame, int groupsX, int groupsY)
        {
            int kernel = m_RestirTemporalKernel;
            BindProbeFrame(cmd, m_RestirCompute);
            BindSdfTraceConstants(cmd, m_RestirCompute);
            cmd.SetComputeMatrixParam(m_RestirCompute, ViewProjectionId, m_ViewProjection);
            cmd.SetComputeMatrixParam(m_RestirCompute, PreviousViewProjectionId, m_State.ViewProjection);
            cmd.SetComputeIntParam(m_RestirCompute, FrameId, frame);
            cmd.SetComputeIntParam(m_RestirCompute, UseScreenRaysId, useScreenRays ? 1 : 0);
            cmd.SetComputeIntParam(m_RestirCompute, DirectionalProbesId, m_DirectionalProbes ? 1 : 0);
            using (new ProfilingScope(cmd, RestirTemporalSampler))
            {
                BindRestirReuse(cmd, kernel, hzb);
                cmd.SetComputeIntParam(m_RestirCompute, RestirHasHistoryId, hasHistory ? 1 : 0);
                cmd.SetComputeTextureParam(m_RestirCompute, kernel, PreviousGeometryId, hasHistory ? m_PreviousGeometry.innerHandle : black);
                cmd.SetComputeTextureParam(m_RestirCompute, kernel, HitSamplesInputId, m_HitSamples.innerHandle);
                cmd.SetComputeTextureParam(m_RestirCompute, kernel, RestirPreviousId, hasHistory ? m_PreviousRestir.innerHandle : black);
                cmd.SetComputeTextureParam(m_RestirCompute, kernel, RestirOutputId, m_CurrentRestir.innerHandle);
                cmd.DispatchCompute(m_RestirCompute, kernel, groupsX, groupsY, 1);
            }
            kernel = m_RestirSpatialKernel;
            using (new ProfilingScope(cmd, RestirSpatialSampler))
            {
                BindRestirReuse(cmd, kernel, hzb);
                cmd.SetComputeTextureParam(m_RestirCompute, kernel, RestirTemporalId, m_CurrentRestir.innerHandle);
                cmd.SetComputeTextureParam(m_RestirCompute, kernel, ProbeInputId, m_FilteredProbes.innerHandle);
                cmd.SetComputeTextureParam(m_RestirCompute, kernel, ProbeId, m_Probes.innerHandle);
                cmd.SetComputeTextureParam(m_RestirCompute, kernel, RestirOutputId, m_SpatialRestir.innerHandle);
                cmd.DispatchCompute(m_RestirCompute, kernel, groupsX, groupsY, 1);
            }
            m_RestirHistory.MarkWritten();
        }

        private bool CoversReceiver(RenderGraphTexture texture) =>
            texture != null && texture.desc.Width == m_Width && texture.desc.Height == m_Height;

        internal bool MatchesOpenPbrReceiver(RenderGraphTexture depth, RenderGraphTexture gbuffer0, RenderGraphTexture gbuffer1,
            RenderGraphTexture parameters, RenderGraphTexture aux0, RenderGraphTexture aux1) =>
            ReferenceEquals(m_Depth, depth) && ReferenceEquals(m_GBuffer0, gbuffer0) && ReferenceEquals(m_GBuffer1, gbuffer1)
            && ReferenceEquals(m_OpenPbrParameters, parameters) && ReferenceEquals(m_OpenPbrAux0, aux0)
            && ReferenceEquals(m_OpenPbrAux1, aux1);

        private void RecordResolve(ComputePassContext context, TextureHandle placement, bool pixelTemporal,
            bool repairScreenRays, TextureHandle black)
        {
            var cmd = context.cmd;
            using var scope = new ProfilingScope(cmd, m_RestirGather ? RestirResolveSampler : ResolveSampler);
            int kernel = m_DirectionalCache ? m_DirectionalResolveKernel
                : m_RestirOpenPbrDiffuse ? m_OpenPbrResolveKernel : m_ResolveKernel;
            bool writeRawRoughSpec = m_RoughSpecular && pixelTemporal && m_ScreenProbeTemporalKernel >= 0;
            if (m_DirectionalCache)
            {
                cmd.SetComputeTextureParam(m_GatherCompute, kernel, LumenBentNormalId, m_LumenBentNormal.innerHandle);
                cmd.SetComputeTextureParam(m_GatherCompute, kernel, ProbeMovingId, m_ProbeMoving.innerHandle);
                cmd.SetComputeTextureParam(m_GatherCompute, kernel, ResolvedMovingOutputId, m_ResolvedMoving.innerHandle);
                cmd.SetComputeTextureParam(m_GatherCompute, kernel, DirectionalFilteredId, m_CurrentDirectionalFiltered.innerHandle);
                cmd.SetComputeVectorParam(m_GatherCompute, ProbeCameraPositionId, m_ProbeCameraPosition);
                cmd.SetComputeIntParam(m_GatherCompute, WriteRoughSpecularId, m_RoughSpecular ? 1 : 0);
                cmd.SetComputeTextureParam(m_GatherCompute, kernel, RoughSpecularOutputId,
                    m_RoughSpecular && !writeRawRoughSpec ? m_CurrentRoughSpecular.innerHandle : m_RawRoughSpecular.innerHandle);
                bool jitter = pixelTemporal || m_StochasticInterpolation;
#if UNITY_EDITOR
                jitter &= EditorTraceCapture == null || (EditorGatherHistoryDiagnostics & 2) == 0;
#endif
                cmd.SetComputeIntParam(m_GatherCompute, IntegrationJitterId, jitter ? 1 : 0);
                cmd.SetComputeIntParam(m_GatherCompute, StochasticInterpolationId, m_StochasticInterpolation ? 1 : 0);
                cmd.SetComputeIntParam(m_GatherCompute, InterpolationFlipYId, m_InterpolationFlipY ? 1 : 0);
                cmd.SetComputeTextureParam(m_GatherCompute, kernel, InterpolationScalarNoiseId,
                    m_StochasticInterpolation ? m_InterpolationScalarNoise : black);
                cmd.SetComputeTextureParam(m_GatherCompute, kernel, InterpolationVectorNoiseId,
                    m_StochasticInterpolation ? m_InterpolationVectorNoise : black);
            }
            BindReceiver(cmd, m_GatherCompute, kernel);
            BindScreenTraceHistory(cmd, m_GatherCompute, kernel, repairScreenRays, black);
            if (m_RestirOpenPbrDiffuse)
            {
                cmd.SetComputeTextureParam(m_GatherCompute, kernel, OpenPbrParametersId, m_OpenPbrParameters.innerHandle);
                cmd.SetComputeTextureParam(m_GatherCompute, kernel, OpenPbrAux0Id, m_OpenPbrAux0.innerHandle);
                cmd.SetComputeTextureParam(m_GatherCompute, kernel, OpenPbrAux1Id, m_OpenPbrAux1.innerHandle);
            }
            cmd.SetComputeTextureParam(m_GatherCompute, kernel, GeometryId, m_Geometry.innerHandle);
            cmd.SetComputeTextureParam(m_GatherCompute, kernel, PlacementId, placement);
            bool useScratch = m_DirectionalCache || m_RestirPixelIntegration || (m_SpatialFilter && !m_HitDistanceFilter && !m_RestirGather);
            cmd.SetComputeTextureParam(m_GatherCompute, kernel, ProbeInputId,
                useScratch ? m_FilteredProbes.innerHandle : m_Probes.innerHandle);
#if UNITY_EDITOR
            if (m_DirectionalCache && EditorTraceCapture != null)
                EditorBindResolveProbe?.Invoke(context, m_GatherCompute, kernel);
#endif
            cmd.SetComputeTextureParam(m_GatherCompute, kernel, RestirSpatialId, m_SpatialRestir.innerHandle);
            cmd.SetComputeTextureParam(m_GatherCompute, kernel, RestirVarianceOutputId, m_RestirVariance.innerHandle);
            cmd.SetComputeTextureParam(m_GatherCompute, kernel, OutputId,
                pixelTemporal || m_RestirPixelIntegration ? m_RawResolved.innerHandle : m_Output.innerHandle);
            cmd.DispatchCompute(m_GatherCompute, kernel, CoreUtils.DivRoundUp(m_Width, GroupSize), CoreUtils.DivRoundUp(m_Height, GroupSize), 1);
            if (m_RoughSpecular && !writeRawRoughSpec)
                m_RoughSpecularHistory?.MarkWritten();
        }

        private void RecordRestirDenoise(ComputeCommandBuffer cmd, bool pixelTemporal, bool restirVarianceTemporal)
        {
            using var scope = new ProfilingScope(cmd, RestirDenoiseSampler);
            int kernel = m_RestirDenoiseKernel;
            BindReceiver(cmd, m_RestirCompute, kernel);
            cmd.SetComputeTextureParam(m_RestirCompute, kernel, CurrentResolvedId, m_RawResolved.innerHandle);
            cmd.SetComputeTextureParam(m_RestirCompute, kernel, RestirFilterInputId,
                pixelTemporal ? m_CurrentPixelLighting.innerHandle : m_RawResolved.innerHandle);
            cmd.SetComputeTextureParam(m_RestirCompute, kernel, RestirVarianceId,
                restirVarianceTemporal ? m_CurrentRestirVariance.innerHandle : m_RestirVariance.innerHandle);
            cmd.SetComputeIntParam(m_RestirCompute, RestirFilterHistoryModeId, restirVarianceTemporal ? 2 : pixelTemporal ? 1 : 0);
            cmd.SetComputeTextureParam(m_RestirCompute, kernel, OutputId, m_Output.innerHandle);
            cmd.DispatchCompute(m_RestirCompute, kernel, CoreUtils.DivRoundUp(m_Width, GroupSize), CoreUtils.DivRoundUp(m_Height, GroupSize), 1);
        }

        private void RecordAtrousDenoise(ComputePassContext context, TextureHandle black, bool restirVarianceTemporal)
        {
            var cmd = context.cmd;
            var input = m_Output.innerHandle;
            var variance = restirVarianceTemporal ? m_CurrentRestirVariance.innerHandle
                : m_RestirPixelIntegration ? m_RestirVariance.innerHandle : black;
            bool screenProbeMoments = m_DirectionalCache && m_ScreenProbeMomentsHistory != null
                && m_CurrentScreenProbeMoments.innerHandle.IsValid();
            int groupsX = CoreUtils.DivRoundUp(m_Width, GroupSize);
            int groupsY = CoreUtils.DivRoundUp(m_Height, GroupSize);
            for (int iteration = 0; iteration < m_AtrousIterations; iteration++)
            {
                bool first = iteration == 0;
                bool last = iteration == m_AtrousIterations - 1;
                int kernel = last ? m_AtrousFinalKernel : m_AtrousKernel;
                // The resolve and temporal passes are finished before the second wavelet write.
                bool even = (iteration & 1) == 0;
                var output = last ? m_Output.innerHandle : even ? m_AtrousLighting.innerHandle : m_RawResolved.innerHandle;
                var varianceOutput = even ? m_AtrousVarianceA.innerHandle : m_AtrousVarianceB.innerHandle;
                using var scope = new ProfilingScope(cmd, first ? AtrousFineSampler
                    : iteration == 1 ? AtrousMiddleSampler : iteration == 2 ? AtrousCoarseSampler : AtrousWideSampler);
                BindReceiver(cmd, m_GatherCompute, kernel);
                cmd.SetComputeTextureParam(m_GatherCompute, kernel, AtrousInputId, input);
                cmd.SetComputeTextureParam(m_GatherCompute, kernel, CurrentResolvedId,
                    first && m_RestirPixelIntegration ? m_RawResolved.innerHandle : black);
                cmd.SetComputeTextureParam(m_GatherCompute, kernel, AtrousVarianceId, variance);
                cmd.SetComputeTextureParam(m_GatherCompute, kernel, OutputId, output);
                if (!last)
                    cmd.SetComputeTextureParam(m_GatherCompute, kernel, AtrousVarianceOutputId, varianceOutput);
                cmd.SetComputeIntParam(m_GatherCompute, AtrousStepId, 1 << iteration);
                cmd.SetComputeIntParam(m_GatherCompute, AtrousUseRestirDomainId, first && m_RestirPixelIntegration ? 1 : 0);
                cmd.SetComputeIntParam(m_GatherCompute, AtrousHasVarianceId, !first || m_RestirPixelIntegration || screenProbeMoments ? 1 : 0);
                cmd.SetComputeIntParam(m_GatherCompute, AtrousUseScreenProbeMomentsId, screenProbeMoments ? 1 : 0);
                cmd.SetComputeTextureParam(m_GatherCompute, kernel, AtrousScreenProbeMomentsId,
                    screenProbeMoments ? m_CurrentScreenProbeMoments.innerHandle : black);
                cmd.DispatchCompute(m_GatherCompute, kernel, groupsX, groupsY, 1);
#if UNITY_EDITOR
                EditorAtrousDispatchCapture?.Invoke(context, iteration, input.ResolveTexture(), output.ResolveTexture(),
                    variance.ResolveTexture(), last ? null : varianceOutput.ResolveTexture());
#endif
                input = output;
                variance = varianceOutput;
            }
        }

        private void PreparePixelHistory(Camera camera)
        {
            m_CurrentPixelLighting.Resize(m_Width, m_Height);
            // Native Screen Probe stores premultiplied energy; other gather
            // paths retain their existing conditional-radiance history format.
            var lightingHistoryId = m_DirectionalCache && m_ScreenProbeTemporalKernel >= 0
                ? CoveredPixelHistoryId : PixelHistoryId;
            CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, lightingHistoryId,
                m_PreviousPixelLighting, m_CurrentPixelLighting, m_CurrentPixelLighting.desc, out m_PixelHistory,
                m_RestirPixelIntegration ? AccessFlags.ReadWrite : AccessFlags.Write);
            if (m_DirectionalCache && m_ScreenProbeTemporalKernel >= 0)
            {
                m_CurrentFastUpdate.Resize(m_Width, m_Height);
                CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, FastUpdateHistoryId,
                    m_PreviousFastUpdate, m_CurrentFastUpdate, m_CurrentFastUpdate.desc, out m_FastUpdateHistory);
                m_CurrentPixelDepth.Resize(m_Width, m_Height);
                CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, PixelDepthHistoryId,
                    m_PreviousPixelDepth, m_CurrentPixelDepth, m_CurrentPixelDepth.desc, out m_PixelDepthHistory, AccessFlags.Write);
                m_CurrentPreExposure.Resize(1, 1);
                CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, PixelPreExposureHistoryId,
                    m_PreviousPreExposure, m_CurrentPreExposure, m_CurrentPreExposure.desc, out m_PreExposureHistory, AccessFlags.Write);
            }
            if (m_DirectionalCache && m_AtrousIterations > 0 && m_ScreenProbeTemporalKernel >= 0 && m_ScreenProbeMomentsKernel >= 0)
            {
                m_CurrentScreenProbeMoments.Resize(m_Width, m_Height);
                CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, ScreenProbeMomentsHistoryId,
                    m_PreviousScreenProbeMoments, m_CurrentScreenProbeMoments, m_CurrentScreenProbeMoments.desc,
                    out m_ScreenProbeMomentsHistory);
            }
            if (m_RestirPixelIntegration && m_RestirVarianceTemporalKernel >= 0)
            {
                m_CurrentRestirVariance.Resize(m_Width, m_Height);
                CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, RestirVarianceHistoryId,
                    m_PreviousRestirVariance, m_CurrentRestirVariance, m_CurrentRestirVariance.desc, out m_RestirVarianceHistory);
            }
        }

        internal static void GetHistoryScreenCoordCodec(
            int width,
            int height,
            out Vector4 subPixelGridSizeAndInvSize,
            out Vector2Int decodeShift,
            out Vector4 gatherUvMinMax)
        {
            static int FloorLog2Positive(int value)
            {
                int shift = 0;
                while ((value >>= 1) > 0)
                    shift++;
                return shift;
            }

            if (width <= 0 || height <= 0)
            {
                subPixelGridSizeAndInvSize = Vector4.zero;
                decodeShift = Vector2Int.zero;
                gatherUvMinMax = Vector4.zero;
                return;
            }

            int rawSubPixelX = Mathf.Max(1, ushort.MaxValue / width);
            int rawSubPixelY = Mathf.Max(1, ushort.MaxValue / height);
            int shiftX = FloorLog2Positive(rawSubPixelX);
            int shiftY = FloorLog2Positive(rawSubPixelY);
            int subPixelX = 1 << shiftX;
            int subPixelY = 1 << shiftY;
            float invSubPixelX = 1f / subPixelX;
            float invSubPixelY = 1f / subPixelY;
            subPixelGridSizeAndInvSize = new Vector4(subPixelX, subPixelY, invSubPixelX, invSubPixelY);
            decodeShift = new Vector2Int(shiftX, shiftY);

            float invWidth = 1f / width;
            float invHeight = 1f / height;
            float guardX = 0.5f + Mathf.Max(invSubPixelX, 1f / 512f);
            float guardY = 0.5f + Mathf.Max(invSubPixelY, 1f / 512f);
            gatherUvMinMax = new Vector4(
                Mathf.Min(guardX * invWidth, 0.5f),
                Mathf.Min(guardY * invHeight, 0.5f),
                Mathf.Max(1f - guardX * invWidth, 0.5f),
                Mathf.Max(1f - guardY * invHeight, 0.5f));
        }

        private void RecordPixelTemporal(ComputeCommandBuffer cmd, bool hasHistory, bool restirVarianceTemporal,
            bool compositeBent, int frame, TextureHandle black)
        {
            using var scope = new ProfilingScope(cmd, m_RestirGather ? RestirPixelTemporalSampler : PixelTemporalSampler);
            int kernel = m_PixelTemporalKernel;
            bool screenProbeMoments = m_DirectionalCache && m_ScreenProbeTemporalKernel >= 0 && m_ScreenProbeMomentsKernel >= 0
                && m_ScreenProbeMomentsHistory != null && m_CurrentScreenProbeMoments.innerHandle.IsValid();
            bool roughSpecTemporal = false;
            if (m_DirectionalCache && m_ScreenProbeTemporalKernel >= 0)
            {
                kernel = compositeBent
                    ? (screenProbeMoments ? m_ScreenProbeMomentsAoKernel : m_ScreenProbeAoKernel)
                    : (screenProbeMoments ? m_ScreenProbeMomentsKernel : m_ScreenProbeTemporalKernel);
                hasHistory &= m_FastUpdateHistory.IsValid();
                bool preExposureEnabled = m_PreExposure != null && m_PreExposureHistory != null
                    && m_CurrentPreExposure.innerHandle.IsValid();
                if (preExposureEnabled)
                    hasHistory &= m_PreExposureHistory.IsValid();
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, PreviousFastUpdateId, m_PreviousFastUpdate.innerHandle);
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, FastUpdateOutputId, m_CurrentFastUpdate.innerHandle);

                GetHistoryScreenCoordCodec(
                    m_Width,
                    m_Height,
                    out var subPixelGridSizeAndInvSize,
                    out var decodeShift,
                    out var gatherUvMinMax);
                bool hasScalarNoise = m_TemporalScalarNoise.IsValid();
                Vector4 previousCameraForward = m_State.ProbeCameraForward.sqrMagnitude > 0.5f
                    ? m_State.ProbeCameraForward : m_ProbeCameraForward;
                cmd.SetComputeIntParam(m_GatherTemporalCompute, FrameId, frame);
                cmd.SetComputeIntParam(m_GatherTemporalCompute, TemporalScalarNoiseEnabledId, hasScalarNoise ? 1 : 0);
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, TemporalScalarNoiseId, hasScalarNoise ? m_TemporalScalarNoise : black);
                cmd.SetComputeVectorParam(m_GatherTemporalCompute, ProbePreviousCameraForwardId, previousCameraForward);
                cmd.SetComputeVectorParam(m_GatherTemporalCompute, HistorySubPixelGridSizeAndInvSizeId, subPixelGridSizeAndInvSize);
                cmd.SetComputeVectorParam(m_GatherTemporalCompute, HistoryGatherUvMinMaxId, gatherUvMinMax);
                cmd.SetComputeIntParam(m_GatherTemporalCompute, HistoryCoordShiftXId, decodeShift.x);
                cmd.SetComputeIntParam(m_GatherTemporalCompute, HistoryCoordShiftYId, decodeShift.y);
                cmd.SetComputeVectorParam(m_GatherTemporalCompute, HistoryTemporalParamsId, new Vector4(0.01f, 0.35f, 10f, 0.9f));
                cmd.SetComputeIntParam(m_GatherTemporalCompute, FastUpdateNeighborhoodClampId, m_FastUpdateNeighborhoodClamp ? 1 : 0);
                int reprojectionFlags = (m_TemporalPlaneDisocclusion ? 1 : 0)
                    | (m_TemporalRejectBasedOnNormal ? 2 : 0)
                    | (m_CoherentRadianceFastUpdate && m_DynamicLightingFastUpdateActive ? 4 : 0)
                    | (m_TemporalCameraStatic ? 8 : 0);
                cmd.SetComputeIntParam(m_GatherTemporalCompute, TemporalReprojectionFlagsId, reprojectionFlags);
                cmd.SetComputeVectorParam(m_GatherTemporalCompute, ProbeCameraPositionId, m_ProbeCameraPosition);
                Vector4 previousCameraPosition = m_State.ProbeCameraPosition.w > 0.5f
                    ? m_State.ProbeCameraPosition : m_ProbeCameraPosition;
                cmd.SetComputeVectorParam(m_GatherTemporalCompute, ProbePreviousCameraPositionId, previousCameraPosition);
                bool pixelDepthValid = hasHistory && m_PixelDepthHistory != null && m_PixelDepthHistory.IsValid()
                    && m_PreviousPixelDepth.innerHandle.IsValid();
                cmd.SetComputeIntParam(m_GatherTemporalCompute, PreviousPixelDepthValidId, pixelDepthValid ? 1 : 0);
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, PreviousPixelDepthId,
                    pixelDepthValid ? m_PreviousPixelDepth.innerHandle : black);
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, PixelDepthOutputId, m_CurrentPixelDepth.innerHandle);
                bool prevPreExposureValid = preExposureEnabled && hasHistory && m_PreviousPreExposure.innerHandle.IsValid();
                cmd.SetComputeIntParam(m_GatherTemporalCompute, PreExposureHistoryEnabledId, preExposureEnabled ? 1 : 0);
                cmd.SetComputeIntParam(m_GatherTemporalCompute, PreviousPreExposureValidId, prevPreExposureValid ? 1 : 0);
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, PreviousPreExposureId,
                    prevPreExposureValid ? m_PreviousPreExposure.innerHandle : black);
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, CurrentPreExposureOutputId,
                    preExposureEnabled ? m_CurrentPreExposure.innerHandle : m_CurrentPixelDepth.innerHandle);
                if (m_PreExposure != null)
                    cmd.SetComputeBufferParam(m_GatherTemporalCompute, kernel, VividAutoExposureSystem.PreExposureBufferId, m_PreExposure);
                roughSpecTemporal = m_RoughSpecular && m_RoughSpecularHistory != null
                    && m_CurrentRoughSpecular.innerHandle.IsValid() && m_RawRoughSpecular.innerHandle.IsValid();
                bool prevRoughSpecValid = roughSpecTemporal && hasHistory && m_State.RoughSpecular
                    && m_RoughSpecularHistory.IsValid() && m_PreviousRoughSpecular.innerHandle.IsValid();
                cmd.SetComputeIntParam(m_GatherTemporalCompute, RoughSpecularEnabledId, roughSpecTemporal ? 1 : 0);
                cmd.SetComputeIntParam(m_GatherTemporalCompute, PreviousRoughSpecularValidId, prevRoughSpecValid ? 1 : 0);
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, CurrentRoughSpecularId,
                    roughSpecTemporal ? m_RawRoughSpecular.innerHandle : black);
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, PreviousRoughSpecularId,
                    prevRoughSpecValid ? m_PreviousRoughSpecular.innerHandle : black);
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, RoughSpecularHistoryOutputId,
                    roughSpecTemporal ? m_CurrentRoughSpecular.innerHandle : m_RawRoughSpecular.innerHandle);
                cmd.SetComputeIntParam(m_GatherTemporalCompute, DisocclusionSpatialFilterId, m_DisocclusionSpatialFilter ? 1 : 0);
            }
            else if (restirVarianceTemporal)
                kernel = m_RestirVarianceTemporalKernel;
            else if (m_RestirGather && m_RestirPixelTemporalKernel >= 0)
                kernel = m_RestirPixelTemporalKernel;
            if (restirVarianceTemporal)
            {
                hasHistory &= m_RestirVarianceHistory.IsValid();
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, RestirVarianceId, m_RestirVariance.innerHandle);
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, RestirPreviousVarianceId, m_PreviousRestirVariance.innerHandle);
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, RestirVarianceHistoryOutputId, m_CurrentRestirVariance.innerHandle);
            }
            BindReceiver(cmd, m_GatherTemporalCompute, kernel);
            if (m_DirectionalCache && m_ScreenProbeTemporalKernel >= 0)
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, ResolvedMovingId, m_ResolvedMoving.innerHandle);
            if (screenProbeMoments)
            {
                bool momentsValid = hasHistory && m_ScreenProbeMomentsHistory.IsValid();
                cmd.SetComputeIntParam(m_GatherTemporalCompute, ScreenProbeMomentsValidId, momentsValid ? 1 : 0);
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, ScreenProbePreviousMomentsId,
                    m_PreviousScreenProbeMoments.innerHandle);
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, ScreenProbeMomentsOutputId,
                    m_CurrentScreenProbeMoments.innerHandle);
            }
            BindProbeFrame(cmd, m_GatherTemporalCompute);
            if (compositeBent)
            {
                cmd.SetComputeIntParam(m_GatherTemporalCompute, FrameId, frame);
                cmd.SetComputeIntParam(m_GatherTemporalCompute, BentFlipYId, m_BentFlipY ? 1 : 0);
                cmd.SetComputeIntParam(m_GatherTemporalCompute, BentHistoryValidId, m_BentNormalHistoryValid ? 1 : 0);
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, BentScalarNoiseId, m_BentScalarNoise);
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, LumenBentNormalId, m_LumenBentNormal.innerHandle);
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, BentPreviousId, m_PreviousBentNormal.innerHandle);
                cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, BentOutputId, m_CurrentBentNormal.innerHandle);
            }
            cmd.SetComputeMatrixParam(m_GatherTemporalCompute, PreviousViewProjectionId, m_State.ViewProjection);
            cmd.SetComputeIntParam(m_GatherTemporalCompute, HasHistoryId, hasHistory ? 1 : 0);
            cmd.SetComputeFloatParam(m_GatherTemporalCompute, PixelHistoryMaxAgeId, m_PixelHistoryMaxAge);
            cmd.SetComputeMatrixParam(m_GatherTemporalCompute, PreviousInverseVpId, m_State.ViewProjection.inverse);
            cmd.SetComputeVectorParam(m_GatherTemporalCompute, JitterDeltaId, new Vector4(m_ScreenJitterDelta.x, m_ScreenJitterDelta.y, 0f, 0f));
            cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, MotionId, m_ScreenMotion.innerHandle);
            cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, CurrentResolvedId, m_RawResolved.innerHandle);
            cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, PreviousPixelLightingId, m_PreviousPixelLighting.innerHandle);
            cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, PreviousPixelGeometryId, m_PreviousPixelGeometry.innerHandle);
            cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, PixelLightingOutputId, m_CurrentPixelLighting.innerHandle);
            cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, PixelGeometryOutputId, m_CurrentPixelGeometry.innerHandle);
            cmd.SetComputeTextureParam(m_GatherTemporalCompute, kernel, OutputId, m_Output.innerHandle);
            cmd.DispatchCompute(m_GatherTemporalCompute, kernel, CoreUtils.DivRoundUp(m_Width, GroupSize), CoreUtils.DivRoundUp(m_Height, GroupSize), 1);
            m_PixelHistory.MarkWritten();
            m_FastUpdateHistory?.MarkWritten();
            m_PixelGeometryHistory.MarkWritten();
            m_PixelDepthHistory?.MarkWritten();
            m_PreExposureHistory?.MarkWritten();
            if (roughSpecTemporal)
                m_RoughSpecularHistory.MarkWritten();
            if (compositeBent)
                m_BentNormalHistory.MarkWritten();
            if (screenProbeMoments)
                m_ScreenProbeMomentsHistory.MarkWritten();
            if (restirVarianceTemporal)
                m_RestirVarianceHistory.MarkWritten();
        }

        private void RecordScreenTrace(ComputeCommandBuffer cmd, int groupsX, int groupsY, TextureHandle placement,
            TextureHandle black, bool validScreenRadiance)
        {
            using var scope = new ProfilingScope(cmd, ScreenTraceSampler);
            bool materialLookup = m_UseSdf && m_ScreenReceiverTraceKernel >= 0
                && m_ReceiverVisibility.IsValid() && m_ReceiverEntities != null && m_ReceiverCount > 0;
            int kernel = m_LumenScreenTrace ? (materialLookup ? m_LumenReceiverKernel : m_LumenTraceKernel) : materialLookup ? m_ScreenReceiverTraceKernel : m_ScreenTraceKernel;
            if (m_LumenScreenTrace)
            {
                cmd.SetComputeVectorParam(m_TraceCompute, ClosestHzbSizeId, m_ClosestHzbSize);
                cmd.SetComputeTextureParam(m_TraceCompute, m_ClosestHzbKernel, DepthId, m_Depth.innerHandle);
                cmd.SetComputeTextureParam(m_TraceCompute, m_ClosestHzbKernel, ClosestHzbOutputId, m_ClosestHzb.innerHandle);
                for (int mip = 0; mip < (int)m_ClosestHzbSize.z; mip += 4)
                {
                    cmd.SetComputeIntParam(m_TraceCompute, HzbStartMipId, mip);
                    int width = Mathf.Max(1, (int)m_ClosestHzbSize.x >> mip);
                    int height = Mathf.Max(1, (int)m_ClosestHzbSize.y >> mip);
                    cmd.DispatchCompute(m_TraceCompute, m_ClosestHzbKernel, CoreUtils.DivRoundUp(width, 8), CoreUtils.DivRoundUp(height, 8), 1);
                }
                cmd.SetComputeTextureParam(m_TraceCompute, kernel, ClosestHzbId, m_ClosestHzb.innerHandle);
                cmd.SetComputeBufferParam(m_TraceCompute, kernel, VividAutoExposureSystem.PreExposureBufferId, m_PreExposure);
                cmd.SetComputeMatrixParam(m_TraceCompute, ClipToPrevClipId, m_ClipToPrevClip);
                cmd.SetComputeFloatParam(m_TraceCompute, DepthThicknessScaleId, m_DepthThicknessScale);
            }
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, FarRadianceId, m_FarAtlas.IsValid() ? m_FarAtlas : black);
            BindReceiver(cmd, m_TraceCompute, kernel);
            if (materialLookup)
            {
                cmd.SetComputeTextureParam(m_TraceCompute, kernel, ReceiverVisibilityId, m_ReceiverVisibility);
                cmd.SetComputeBufferParam(m_TraceCompute, kernel, ReceiverEntitiesId, m_ReceiverEntities);
                cmd.SetComputeIntParam(m_TraceCompute, ReceiverCountId, m_ReceiverCount);
                cmd.SetComputeBufferParam(m_TraceCompute, kernel, SurfaceId, m_Scene.DiffuseSurfaces);
            }
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, PlacementId, placement);
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, ProbeDirectionNoiseId,
                m_ProbeDirectionNoise.IsValid() ? m_ProbeDirectionNoise : black);
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, DirectionalGuideId, m_DirectionalGuide
                ? (m_FrozenGuideActive ? m_CurrentProposal.innerHandle : m_DirectionalGuideTexture.innerHandle) : black);
            BindScreenTraceHistory(cmd, m_TraceCompute, kernel, true, black);
            // Keep the screen hit distance so Hybrid can shade it through the current SDF path.
            cmd.SetComputeIntParam(m_TraceCompute, ScreenRadianceValidId, validScreenRadiance ? 1 : 0);
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, ScreenRaysOutputId, m_ScreenRays.innerHandle);
            cmd.DispatchCompute(m_TraceCompute, kernel, m_LumenScreenTrace ? m_ProbeWidth : groupsX, m_LumenScreenTrace ? m_AtlasHeight : groupsY, 1);
        }

        private void BindScreenTraceHistory(ComputeCommandBuffer cmd, ComputeShader shader, int kernel,
            bool available, TextureHandle black)
        {
            cmd.SetComputeTextureParam(shader, kernel, HzbId, available && !m_LumenScreenTrace ? m_ScreenHzb.innerHandle : black);
            cmd.SetComputeTextureParam(shader, kernel, MotionId, available ? m_ScreenMotion.innerHandle : black);
            cmd.SetComputeTextureParam(shader, kernel, ScreenRadianceId, available ? m_PreviousScreenRadiance : black);
            cmd.SetComputeTextureParam(shader, kernel, ScreenGeometryId, available ? m_PreviousScreenGeometry : black);
            if (!available)
                return;

            cmd.SetComputeMatrixParam(shader, PreviousViewProjectionId, m_State.ViewProjection);
            cmd.SetComputeMatrixParam(shader, PreviousInverseVpId, m_State.ViewProjection.inverse);
            cmd.SetComputeVectorParam(shader, JitterDeltaId, new Vector4(m_ScreenJitterDelta.x, m_ScreenJitterDelta.y, 0f, 0f));
        }

        internal static bool LightingWithinTolerance(Vector4 previousPosition, Vector4 previousColor,
            Vector4 position, Vector4 color, float tolerance)
        {
            bool directional = position.w < 0f;
            if (directional != (previousPosition.w < 0f) || !RelativeColorWithinTolerance(previousColor, color, tolerance))
                return false;
            if (previousPosition.Equals(position))
                return true;
            if (!directional || tolerance <= 0f || previousPosition.w != position.w)
                return false;
            Vector3 oldDirection = previousPosition, direction = position;
            float length = Mathf.Sqrt(oldDirection.sqrMagnitude * direction.sqrMagnitude);
            return length > 0f && Vector3.Dot(oldDirection, direction) >= 0.9999985f * length; // 0.1 degree.
        }

        private static bool RelativeColorWithinTolerance(Vector4 previous, Vector4 current, float tolerance)
        {
            Vector3 a = previous, b = current;
            float scaleSquared = Mathf.Max(a.sqrMagnitude, b.sqrMagnitude);
            return (a - b).sqrMagnitude <= tolerance * tolerance * scaleSquared
                && Mathf.Abs(previous.w - current.w) <= tolerance;
        }

        internal static bool AllowsContinuousLightingHistory(bool fullSphere, bool preserveRelitHistory, bool matchesLighting) =>
            fullSphere || preserveRelitHistory || matchesLighting;

        internal static int StepDynamicLightingFastUpdateWindow(int remainingFrames, bool hasPreviousState, bool lightingChanged)
        {
            if (!hasPreviousState)
                return 0;
            if (lightingChanged)
                return 8;
            return remainingFrames > 0 ? remainingFrames - 1 : 0;
        }

        internal static bool HasCameraMotion(
            Vector4 previousCameraPosition,
            Vector4 currentCameraPosition,
            Vector4 previousCameraForward,
            Vector4 currentCameraForward)
        {
            if (previousCameraPosition.w <= 0.5f || currentCameraPosition.w <= 0.5f)
                return true;
            Vector3 posDelta = new Vector3(
                currentCameraPosition.x - previousCameraPosition.x,
                currentCameraPosition.y - previousCameraPosition.y,
                currentCameraPosition.z - previousCameraPosition.z);
            if (posDelta.sqrMagnitude > 1e-8f)
                return true;
            Vector3 prevFwd = new Vector3(previousCameraForward.x, previousCameraForward.y, previousCameraForward.z);
            Vector3 currFwd = new Vector3(currentCameraForward.x, currentCameraForward.y, currentCameraForward.z);
            if (prevFwd.sqrMagnitude <= 0.5f || currFwd.sqrMagnitude <= 0.5f)
                return true;
            return Vector3.Dot(prevFwd, currFwd) < 0.999999f;
        }

        private bool MatchesPreviousDirectionalLighting() => m_State.LightLayerMask == m_LightLayerMask
            && LightingWithinTolerance(m_State.LightPositionRange, m_State.LightColorShadow,
                m_LightPositionRange, m_LightColorShadow, m_LightingTolerance);

        private bool HasCurrentScreenLighting()
        {
#if UNITY_EDITOR
            if (EditorTraceCapture != null && (EditorGatherHistoryDiagnostics & 65536) != 0)
                return MatchesPreviousDirectionalLighting();
#endif
            // Previous scene color contains emission, sky and indirect light as well
            // as the sun. Geometry can still screen-trace while its lighting is stale;
            // shade the hit through current SDF/cards until the first rebuild completes.
            return MatchesPreviousDirectionalLighting() && m_State.SceneRevision == m_Scene.Revision
                && MatchesEnvironmentLighting(m_State.SkyContentHash, m_State.SkyTextureParams, m_State.SkyTextureTint)
                && !m_RadiosityRestarting;
        }

        private bool MatchesEnvironmentLighting(int hash, Vector4 parameters, Vector4 tint) =>
            MatchesEnvironmentLighting(hash, parameters, tint, m_LightingTolerance);

        private bool MatchesEnvironmentLighting(int hash, Vector4 parameters, Vector4 tint, float tolerance)
        {
            float scale = Mathf.Max(Mathf.Abs(parameters.x), Mathf.Abs(m_SkyTextureParams.x));
            // Content identity and orientation are discrete invalidations. Only
            // physical energy/tint floats use a relative deadband.
            return hash == m_SkyContentHash && parameters.y == m_SkyTextureParams.y
                && parameters.z == m_SkyTextureParams.z && parameters.w == m_SkyTextureParams.w
                && Mathf.Abs(parameters.x - m_SkyTextureParams.x) <= tolerance * scale
                && RelativeColorWithinTolerance(tint, m_SkyTextureTint, tolerance);
        }

        private void RecordDirectionalGuide(ComputeCommandBuffer cmd, TextureHandle placement, TextureHandle black,
            bool hasHistory, int frame, int groupsX, int groupsY)
        {
            using var scope = new ProfilingScope(cmd, DirectionalGuideSampler);
            int kernel = m_FullSphere ? m_SphereGuideKernel : m_DirectionalGuideKernel;
            BindReceiver(cmd, m_ImportanceSamplingCompute, kernel);
            BindProbeFrame(cmd, m_ImportanceSamplingCompute);
            BindDirectionalGuideMode(cmd, m_ImportanceSamplingCompute);
            cmd.SetComputeIntParam(m_ImportanceSamplingCompute, FrameId, frame);
            cmd.SetComputeMatrixParam(m_ImportanceSamplingCompute, PreviousViewProjectionId, m_State.ViewProjection);
            cmd.SetComputeIntParam(m_ImportanceSamplingCompute, DirectionalHasHistoryId, hasHistory ? 1 : 0);
            cmd.SetComputeTextureParam(m_ImportanceSamplingCompute, kernel, PlacementId, placement);
            cmd.SetComputeTextureParam(m_ImportanceSamplingCompute, kernel, ProbeDirectionNoiseId,
                m_ProbeDirectionNoise.IsValid() ? m_ProbeDirectionNoise : black);
            cmd.SetComputeTextureParam(m_ImportanceSamplingCompute, kernel, PreviousGeometryId, hasHistory ? m_PreviousGeometry.innerHandle : black);
            cmd.SetComputeTextureParam(m_ImportanceSamplingCompute, kernel, DirectionalPreviousId,
                hasHistory ? m_PreviousDirectionalFiltered.innerHandle : black);
            bool worldGuide = m_DirectionalHierarchicalGuide && m_FarAtlas.IsValid() && (m_FullSphere || m_FarHitIds.IsValid());
            cmd.SetComputeFloatParam(m_ImportanceSamplingCompute, FarDistanceId, worldGuide ? m_FarDistance : 0f);
            if (worldGuide)
                cmd.SetComputeVectorParam(m_ImportanceSamplingCompute, FarWindowId,
                    m_FarWindowParameter);
            cmd.SetComputeIntParam(m_ImportanceSamplingCompute, DirectionalWorldGuideId, worldGuide ? 1 : 0);
            cmd.SetComputeTextureParam(m_ImportanceSamplingCompute, kernel, FarRadianceId, worldGuide ? m_FarAtlas : black);
            if (m_FullSphere)
            {
                bool motion = hasHistory && m_ScreenMotion != null && !ReferenceEquals(m_ScreenMotion, m_LocalMotion) && m_ScreenMotion.innerHandle.IsValid();
                cmd.SetComputeIntParam(m_ImportanceSamplingCompute, DirectionalHasMotionId, motion ? 1 : 0);
                cmd.SetComputeTextureParam(m_ImportanceSamplingCompute, kernel, MotionId, motion ? m_ScreenMotion.innerHandle : black);
                cmd.SetComputeVectorParam(m_ImportanceSamplingCompute, JitterDeltaId, new Vector4(m_ScreenJitterDelta.x, m_ScreenJitterDelta.y, 0f, 0f));
                cmd.SetComputeTextureParam(m_ImportanceSamplingCompute, kernel, DirectionalGuideOutputId, m_DirectionalGuideTexture.innerHandle);
                cmd.DispatchCompute(m_ImportanceSamplingCompute, kernel, m_ProbeWidth, m_AtlasHeight, 1);
                return;
            }
            bool proposalValid = m_FrozenGuideActive && hasHistory && m_ProposalHistoryValid && m_PreviousProposal.innerHandle.IsValid();
            cmd.SetComputeIntParam(m_ImportanceSamplingCompute, HasProposalHistoryId, proposalValid ? 1 : 0);
            cmd.SetComputeTextureParam(m_ImportanceSamplingCompute, kernel, ProposalHistoryTextureId,
                proposalValid ? m_PreviousProposal.innerHandle : black);
            cmd.SetComputeTextureParam(m_ImportanceSamplingCompute, kernel, DirectionalGuideOutputId,
                m_FrozenGuideActive ? m_CurrentProposal.innerHandle : m_DirectionalGuideTexture.innerHandle);
            cmd.DispatchCompute(m_ImportanceSamplingCompute, kernel, groupsX, groupsY, 1);
        }

        private void RecordDirectionalCache(ComputeCommandBuffer cmd, TextureHandle black, bool hasHistory, int groupsX, int groupsY)
        {
            var shader = m_FilterCompute;
            var geometry = m_Geometry.innerHandle;
            var composite = m_CurrentDirectional.innerHandle;
            var temporal = m_DirectionalTemporal.innerHandle;
            var filtered = m_CurrentDirectionalFiltered.innerHandle;
            int kernel = m_DirectionalCompositeKernel;
            cmd.SetComputeTextureParam(shader, kernel, ProbeDirectionNoiseId, m_ProbeDirectionNoise.IsValid() ? m_ProbeDirectionNoise : black);
            cmd.SetComputeIntParam(shader, HasProbeDirectionNoiseId, m_ProbeDirectionNoise.IsValid() ? 1 : 0);
            cmd.SetComputeTextureParam(shader, kernel, ScatterNoiseId, m_ScatterNoise.IsValid() ? m_ScatterNoise : black);
            cmd.SetComputeIntParam(shader, HasScatterNoiseId, m_ScatterNoise.IsValid() ? 1 : 0);
            cmd.SetComputeIntParam(shader, DitherTraceDirectionsId, m_DitherTraceDirections ? 1 : 0);
            cmd.SetComputeTextureParam(shader, kernel, DirectionalGuideId, m_DirectionalGuide ? m_DirectionalGuideTexture.innerHandle : black);
            cmd.SetComputeTextureParam(shader, kernel, PlacementId, m_AdaptiveProbes ? m_AdaptivePlacement.innerHandle : black);
            cmd.SetComputeIntParam(shader, DirectionalGuideEnabledId, m_DirectionalGuide ? 1 : 0);
            using (new ProfilingScope(cmd, DirectionalCompositeSampler))
            {
                cmd.SetComputeFloatParam(shader, ProbeMaxIntensityId, m_MaxRayIntensity);
                cmd.SetComputeBufferParam(shader, kernel, VividAutoExposureSystem.PreExposureBufferId, m_PreExposure);
                cmd.SetComputeTextureParam(shader, kernel, GeometryId, geometry);
                cmd.SetComputeTextureParam(shader, kernel, DirectionalRaysId, m_DirectionalRayTexture.innerHandle);
                cmd.SetComputeTextureParam(shader, kernel, DirectionalCompositeOutputId, composite);
                cmd.DispatchCompute(shader, kernel, m_ProbeWidth, m_AtlasHeight, 1);
            }
            using (new ProfilingScope(cmd, DirectionalTemporalSampler))
            {
                hasHistory &= m_ProbeTemporal;
                kernel = m_DirectionalTemporalKernel;
                cmd.SetComputeIntParam(shader, DirectionalHasHistoryId, hasHistory ? 1 : 0);
                bool hasMotion = hasHistory && m_ScreenMotion != null && !ReferenceEquals(m_ScreenMotion, m_LocalMotion)
                    && m_ScreenMotion.innerHandle.IsValid();
                cmd.SetComputeIntParam(shader, DirectionalHasMotionId, hasMotion ? 1 : 0);
                cmd.SetComputeTextureParam(shader, kernel, MotionId, hasMotion ? m_ScreenMotion.innerHandle : black);
                cmd.SetComputeTextureParam(shader, kernel, PlacementId, m_AdaptiveProbes ? m_AdaptivePlacement.innerHandle : black);
                cmd.SetComputeVectorParam(shader, JitterDeltaId, new Vector4(m_ScreenJitterDelta.x, m_ScreenJitterDelta.y, 0f, 0f));
                cmd.SetComputeMatrixParam(shader, PreviousViewProjectionId, m_State.ViewProjection);
                cmd.SetComputeTextureParam(shader, kernel, GeometryId, geometry);
                cmd.SetComputeTextureParam(shader, kernel, PreviousGeometryId, hasHistory ? m_PreviousGeometry.innerHandle : black);
                cmd.SetComputeTextureParam(shader, kernel, DirectionalPreviousId, hasHistory ? m_PreviousDirectional.innerHandle : black);
                cmd.SetComputeTextureParam(shader, kernel, DirectionalCompositeId, composite);
                cmd.SetComputeTextureParam(shader, kernel, DirectionalTemporalOutputId, temporal);
                cmd.DispatchCompute(shader, kernel, m_ProbeWidth, m_AtlasHeight, 1);
            }
            using (new ProfilingScope(cmd, DirectionalFilterSampler))
            {
                bool lumenFilter = m_SpatialFilterScalarNoise.IsValid() && m_SpatialFilterVectorNoise.IsValid();
                kernel = lumenFilter ? m_LumenDirectionalFilterKernel : m_DirectionalFilterKernel;
                cmd.SetComputeTextureParam(shader, kernel, PlacementId, m_AdaptiveProbes ? m_AdaptivePlacement.innerHandle : black);
                cmd.SetComputeTextureParam(shader, kernel, GeometryId, geometry);
                cmd.SetComputeTextureParam(shader, kernel, DirectionalCompositeId, composite);
                if (lumenFilter)
                {
                    cmd.SetComputeTextureParam(shader, kernel, ProbeMovingId, m_ProbeMoving.innerHandle);
                    cmd.SetComputeTextureParam(shader, kernel, SpatialFilterScalarNoiseId, m_SpatialFilterScalarNoise);
                    cmd.SetComputeTextureParam(shader, kernel, SpatialFilterVectorNoiseId, m_SpatialFilterVectorNoise);
                    cmd.SetComputeIntParam(shader, SpatialFilterWideModeId,
                        (m_DynamicLightingFastUpdateActive || !m_TemporalCameraStatic) ? 1 : 0);
                    cmd.SetComputeFloatParam(shader, SkyTraceDistanceId,
                        Mathf.Max(m_SkyTraceDistance, Mathf.Max(m_TraceDistance, m_FarDistance)));
                    cmd.SetComputeFloatParam(shader, FarDistanceId, m_FarDistance);
                }
                // Use this frame's hit distances with the two-frame radiance estimate.
                TextureHandle spatialInput = temporal;
                int spatialPasses = m_SpatialFilter ? 3 : 1;
                for (int pass = 0; pass < spatialPasses; ++pass)
                {
                    TextureHandle spatialOutput = pass == 1 ? m_DirectionalSpatialScratch.innerHandle : filtered;
                    if (lumenFilter)
                        cmd.SetComputeIntParam(shader, SpatialFilterPassIndexId, pass);
                    cmd.SetComputeTextureParam(shader, kernel, DirectionalSpatialInputId, spatialInput);
                    cmd.SetComputeTextureParam(shader, kernel, DirectionalFilteredOutputId, spatialOutput);
                    cmd.DispatchCompute(shader, kernel, m_ProbeWidth, m_AtlasHeight, 1);
                    spatialInput = spatialOutput;
                }
            }
            using (new ProfilingScope(cmd, DirectionalIrradianceSampler))
            {
                kernel = m_DirectionalIntegrateKernel;
                cmd.SetComputeTextureParam(shader, kernel, GeometryId, geometry);
                cmd.SetComputeTextureParam(shader, kernel, DirectionalCompositeId, composite);
                cmd.SetComputeTextureParam(shader, kernel, DirectionalFilteredId, filtered);
                cmd.SetComputeTextureParam(shader, kernel, FilteredProbesId, m_FilteredProbes.innerHandle);
                cmd.SetComputeIntParam(shader, ProbeAnchorDiagnosticsId, m_RecordProbeAnchorDiagnostics ? 1 : 0);
                cmd.SetComputeTextureParam(shader, kernel, DirectionalTemporalId, temporal);
                cmd.SetComputeTextureParam(shader, kernel, ProbeId, m_Probes.innerHandle);
                cmd.DispatchCompute(shader, kernel, groupsX, groupsY, 1);
            }
        }

        private void RecordLumenDebugView(ComputePassContext context, VividSdfSurfaceCache cache, TextureHandle black,
            bool pixelTemporal, bool compositeBent, int frame)
        {
            if (m_DebugTextureKernel < 0 || !context.TryGet<VividLumenDebugFrameData>(out var debug) || !debug.Output.IsValid())
                return;

            var cmd = context.cmd;
            var shader = m_TraceCompute;
            var mode = debug.Mode;
            int kernel = m_DebugTextureKernel;
            bool available;
            using var scope = new ProfilingScope(cmd, LumenDebugViewPass.Sampler);

            bool surface = LumenDebugViewPass.IsSurfaceView(mode) && m_DebugSurfaceKernel >= 0 && cache != null && m_UseSdf;
            surface &= mode switch
            {
                LumenDebugViewMode.DirectLighting => m_CardDirectAtlas.IsValid(),
                LumenDebugViewMode.IndirectLighting => m_RadiosityInput.IsValid(),
                LumenDebugViewMode.DirectLightingUpdates or LumenDebugViewMode.IndirectLightingUpdates =>
                    m_LumenDebugView.CardFrames.IsValid() && m_DebugCardFramesKernel >= 0,
                LumenDebugViewMode.LastUsedPages or LumenDebugViewMode.LastUsedHighResPages =>
                    m_LumenDebugView.HasSurfaceCacheFeedback,
                _ => true,
            };
            if (surface)
            {
                available = true;
                kernel = m_LumenDebugView.HasSurfaceCacheFeedback ? m_DebugSurfaceFeedbackKernel : m_DebugSurfaceKernel;
                m_LumenDebugView.RecordCardFrames(cmd, shader, m_DebugCardFramesKernel, m_State.FinalLightingPriority?.Buffer,
                    (int)m_FinalLightingLayout.w, frame,
                    new Vector4(m_CardDirectAtlas.IsValid() ? m_FinalLightingUpdateFirst : 0,
                        m_CardDirectAtlas.IsValid() ? m_FinalLightingUpdateFirst + m_FinalLightingNewCards : 0,
                        m_RadiosityCursor, m_RadiosityCursor + m_RadiosityUpdateCards), m_RadiosityFeedback);
                cmd.SetComputeVectorParam(shader, LumenDebugViewPass.FrameParametersId, new Vector4(frame, 0f, 0f, 0f));
                BindProbeFrame(cmd, shader);
                BindSdfTraceConstants(cmd, shader);
                m_Scene.Bind(cmd, shader, kernel);
                cmd.SetComputeBufferParam(shader, kernel, SurfaceId, m_Scene.DiffuseSurfaces);
                cmd.SetComputeBufferParam(shader, kernel, LocalCardTableId, cache.LocalCardTable);
                cmd.SetComputeBufferParam(shader, kernel, RadiosityWorkId, CardLightingWork);
                cmd.SetComputeIntParam(shader, RadiosityLayoutId, m_RadiosityInput.IsValid() ? m_Scene.RadiosityCardCount : 0);
                cmd.SetComputeTextureParam(shader, kernel, RadiosityInputId, m_RadiosityInput.IsValid() ? m_RadiosityInput : black);
                cmd.SetComputeTextureParam(shader, kernel, CardAlbedoDepthId, m_CardAlbedoDepth);
                cmd.SetComputeTextureParam(shader, kernel, CardNormalCoverageId, m_CardNormalCoverage);
                cmd.SetComputeTextureParam(shader, kernel, DepthId, m_Depth.innerHandle);
                cmd.SetComputeTextureParam(shader, kernel, LumenDebugViewPass.DirectId, m_CardDirectAtlas.IsValid() ? m_CardDirectAtlas : black);
                var cardFrames = m_LumenDebugView.CardFrames;
                cmd.SetComputeTextureParam(shader, kernel, LumenDebugViewPass.CardFramesId, cardFrames.IsValid() ? cardFrames : black);
                BindFinalLighting(cmd, shader, kernel, m_CardFinalAtlas, black);
                if (m_LumenDebugView.HasSurfaceCacheFeedback)
                {
                    m_LumenDebugView.BindFeedback(cmd, shader, kernel);
                    bool captureVisualization = VividRenderingDebugDisplaySettings.Data.lumenCaptureVisualizationFeedback
                        && mode is LumenDebugViewMode.LumenScene or LumenDebugViewMode.SurfaceCache;
                    cmd.SetComputeIntParam(shader, LumenDebugViewPass.VisualizeFeedbackId,
                        captureVisualization ? 1 : 0);
                }
            }
            else if (mode == LumenDebugViewMode.GlobalSdf && m_DebugGlobalSdfKernel >= 0 && m_GlobalClipmap?.Recorded == true)
            {
                available = true;
                kernel = m_DebugGlobalSdfKernel;
                BindProbeFrame(cmd, shader);
                BindGlobalTracingResources(cmd, shader, kernel, true);
            }
            else if ((mode == LumenDebugViewMode.ShortRangeAO || mode == LumenDebugViewMode.BentNormal)
                && m_DebugBentKernel >= 0 && (compositeBent || m_BentDuringIntegration) && m_LumenBentNormal.innerHandle.IsValid())
            {
                available = true;
                kernel = m_DebugBentKernel;
                cmd.SetComputeTextureParam(shader, kernel, LumenDebugViewPass.PackedFeedId,
                    compositeBent ? m_CurrentBentNormal.innerHandle : m_LumenBentNormal.innerHandle);
            }
            else
            {
                var source = mode switch
                {
                    LumenDebugViewMode.FastUpdateAmount or LumenDebugViewMode.FramesAccumulated
                        when pixelTemporal && m_FastUpdateHistory != null => m_CurrentFastUpdate.innerHandle,
                    LumenDebugViewMode.ScreenProbeTraceSources => m_TraceSources.innerHandle,
                    LumenDebugViewMode.ScreenProbeRadiance => m_HitLighting.innerHandle,
                    LumenDebugViewMode.DiffuseGI => m_Output.innerHandle,
                    LumenDebugViewMode.ShortRangeAO or LumenDebugViewMode.BentNormal
                        when !m_BentComposite && m_BentNormalHistory != null && m_PixelGeometryHistory != null => m_CurrentBentNormal.innerHandle,
                    LumenDebugViewMode.RadiosityAtlas => m_RadiosityInput,
                    LumenDebugViewMode.WorldRadianceCache or LumenDebugViewMode.WorldProbeAge when m_FullSphere && IsFarCacheReady() => m_FarAtlas,
                    LumenDebugViewMode.ScreenProbeRayState when m_RecordRayDiagnostics => m_RayDiagnostics.innerHandle,
                    _ => default,
                };
                available = source.IsValid();
                cmd.SetComputeTextureParam(shader, kernel, LumenDebugViewPass.FeedId, available ? source : black);
            }

            float exposure = VividRenderingDebugDisplaySettings.Data.lumenDebugExposure;
            float exposureMultiplier = Mathf.Pow(2f, Mathf.Clamp(float.IsFinite(exposure) ? exposure : 0f, -16f, 16f));
            bool hasDualSlabSidecar = m_OpenPbrAux0?.innerHandle.IsValid() == true
                && m_OpenPbrAux1?.innerHandle.IsValid() == true;
            if (mode is LumenDebugViewMode.ReflectionView or LumenDebugViewMode.DedicatedReflectionRays
                && m_LumenReflections.RecordDebugView(cmd, this, mode, debug.Output,
                    m_LumenDebugView.Width, m_LumenDebugView.Height, exposureMultiplier, frame,
                    hasDualSlabSidecar ? m_OpenPbrAux0.innerHandle : black,
                    hasDualSlabSidecar ? m_OpenPbrAux1.innerHandle : black,
                    hasDualSlabSidecar, black))
            {
                debug.Produced = true;
                return;
            }

            float distance = context.Get<VividCameraData>().camera.farClipPlane;
            int motionFrame = context.Get<VividCameraData>().frameIndex;
            cmd.SetComputeIntParam(shader, LumenDebugViewPass.ModeId, (int)mode);
            cmd.SetComputeVectorParam(shader, LumenDebugViewPass.ParametersId,
                new Vector4(exposureMultiplier, available ? 1f : 0f,
                    mode == LumenDebugViewMode.Velocity ? motionFrame : frame, distance));
            cmd.SetComputeTextureParam(shader, kernel, LumenDebugViewPass.OutputId, debug.Output);
            cmd.DispatchCompute(shader, kernel, CoreUtils.DivRoundUp(m_LumenDebugView.Width, GroupSize),
                CoreUtils.DivRoundUp(m_LumenDebugView.Height, GroupSize), 1);
            if (mode == LumenDebugViewMode.Overview)
            {
                m_LumenReflections.RecordDebugView(cmd, this, mode, debug.Output,
                    m_LumenDebugView.Width, m_LumenDebugView.Height, exposureMultiplier, frame,
                    hasDualSlabSidecar ? m_OpenPbrAux0.innerHandle : black,
                    hasDualSlabSidecar ? m_OpenPbrAux1.innerHandle : black,
                    hasDualSlabSidecar, black);
            }
            debug.Produced = true;
        }

        internal Vector4 ProbeCameraPosition => m_ProbeCameraPosition;
        internal Matrix4x4 CurrentViewProjection => m_ViewProjection;
        internal Matrix4x4 PreviousViewProjection => m_State != null ? m_State.ViewProjection : m_ViewProjection;
        internal Vector4 ScreenJitterDelta => new(m_ScreenJitterDelta.x, m_ScreenJitterDelta.y, 0f, 0f);

        internal TextureHandle MotionTextureOrFallback(TextureHandle black) =>
            m_ScreenMotion != null && m_ScreenMotion.innerHandle.IsValid() ? m_ScreenMotion.innerHandle : black;

        internal void BindFrameConstantsForReflections(ComputeCommandBuffer cmd, ComputeShader shader)
        {
            BindProbeFrame(cmd, shader);
            BindSdfTraceConstants(cmd, shader);
            cmd.SetComputeFloatParam(shader, MeshDetailDistanceId, m_MeshDetailDistance);
        }

        internal void BindReceiverForReflections(ComputeCommandBuffer cmd, ComputeShader shader, int kernel)
        {
            BindReceiver(cmd, shader, kernel);
        }

        internal void BindScreenHistoryForReflections(ComputeCommandBuffer cmd, ComputeShader shader, int kernel, TextureHandle black)
        {
            BindScreenTraceHistory(cmd, shader, kernel, true, black);
        }

        internal void BindMeshSdfSurfaceCacheForReflections(ComputeCommandBuffer cmd, ComputeShader shader, int kernel, TextureHandle black)
        {
            if (m_UseSdf && m_Scene != null)
            {
                m_Scene.Bind(cmd, shader, kernel);
                cmd.SetComputeBufferParam(shader, kernel, SurfaceId, m_Scene.DiffuseSurfaces);
                var cache = m_Scene.SurfaceCache;
                if (cache != null)
                {
                    cmd.SetComputeTextureParam(shader, kernel, CardAlbedoDepthId, m_CardAlbedoDepth);
                    cmd.SetComputeTextureParam(shader, kernel, CardNormalCoverageId, m_CardNormalCoverage);
                    cmd.SetComputeBufferParam(shader, kernel, LocalCardTableId, cache.LocalCardTable);
                    cmd.SetComputeBufferParam(shader, kernel, RadiosityWorkId, CardLightingWork);
                    BindFinalLighting(cmd, shader, kernel, m_CardFinalAtlas, black);
                }
            }
        }

        internal void BindGlobalSdfForReflections(ComputeCommandBuffer cmd, ComputeShader shader, int kernel, bool useGlobalClipmap)
        {
            BindGlobalTracingResources(cmd, shader, kernel, useGlobalClipmap);
        }

        internal void BindOccludedSkyForReflections(ComputeCommandBuffer cmd, ComputeShader shader, int kernel)
        {
            BindOccludedSky(cmd, shader, kernel);
        }

        private void BindProbeFrame(ComputeCommandBuffer cmd, ComputeShader shader)
        {
            cmd.SetComputeIntParam(shader, FullSphereId, m_FullSphere ? 1 : 0);
            int diagnostics = 0;
            float anchorBlend = 0.85f;
#if UNITY_EDITOR
            if (EditorTraceCapture != null)
            {
                diagnostics |= EditorGatherHistoryDiagnostics;
                anchorBlend = Mathf.Clamp(EditorProbeAnchorHistoryBlend, 0f, 0.99f);
            }
#endif
            cmd.SetComputeIntParam(shader, GatherHistoryDiagnosticsId, diagnostics);
            cmd.SetComputeIntParam(shader, ProbePreviousFrameId, m_State.ProbeFrame);
            cmd.SetComputeVectorParam(shader, HistoryLightScaleId, PreserveRelitHistory ? m_RadiosityHistoryScale : Vector4.one);
            cmd.SetComputeFloatParam(shader, AnchorHistoryBlendId, anchorBlend);
            cmd.SetComputeFloatParam(shader, ProbeAnchorMaxAgeId, m_ProbeAnchorMaxAge);
            cmd.SetComputeMatrixParam(shader, InverseViewProjectionId, m_InverseViewProjection);
            cmd.SetComputeVectorParam(shader, ScreenSizeId, new Vector4(m_Width, m_Height, 1f / m_Width, 1f / m_Height));
            cmd.SetComputeIntParam(shader, RestirDenseId, m_RestirPixelIntegration ? 1 : 0);
            cmd.SetComputeIntParam(shader, AdaptiveProbesId, m_AdaptiveProbes ? 1 : 0);
            cmd.SetComputeVectorParam(shader, ProbeCameraForwardId, m_ProbeCameraForward);
        }

        private void BindSdfTraceConstants(ComputeCommandBuffer cmd, ComputeShader shader)
        {
            cmd.SetComputeFloatParam(shader, MaxDistanceId, m_TraceDistance);
            cmd.SetComputeFloatParam(shader, RayBiasId, m_Bias);
            cmd.SetComputeIntParam(shader, LayerMaskId, m_LayerMask);
        }

        private void BindSunLighting(ComputeCommandBuffer cmd, ComputeShader shader)
        {
            cmd.SetComputeVectorParam(shader, LightPositionRangeId, m_LightPositionRange);
            cmd.SetComputeVectorParam(shader, LightColorShadowId, m_LightColorShadow);
            cmd.SetComputeIntParam(shader, LightLayerMaskId, m_LightLayerMask);
        }

        private void BindDirectionalGuideMode(ComputeCommandBuffer cmd, ComputeShader shader)
        {
            cmd.SetComputeIntParam(shader, DirectionalCacheEnabledId, m_DirectionalCache ? 1 : 0);
            cmd.SetComputeIntParam(shader, DirectionalGuideEnabledId,
                m_DirectionalHierarchicalGuide ? 2 : m_DirectionalGuide ? 1 : 0);
            cmd.SetComputeIntParam(shader, HasProbeDirectionNoiseId, m_ProbeDirectionNoise.IsValid() ? 1 : 0);
        }

        private void BindOccludedSky(ComputeCommandBuffer cmd, ComputeShader shader, int kernel)
        {
            cmd.SetComputeTextureParam(shader, kernel, SkyTextureId, m_SkyTexture);
            cmd.SetComputeVectorParam(shader, SkyTextureParamsId, m_SkyTextureParams);
            cmd.SetComputeVectorParam(shader, SkyTextureTintId, m_SkyTextureTint);
            cmd.SetComputeFloatParam(shader, SkyTraceDistanceId, m_SkyTraceDistance);
        }

        private void BindReceiver(ComputeCommandBuffer cmd, ComputeShader shader, int kernel)
        {
            cmd.SetComputeTextureParam(shader, kernel, DepthId, m_Depth.innerHandle);
            cmd.SetComputeTextureParam(shader, kernel, GBuffer0Id, m_GBuffer0.innerHandle);
            cmd.SetComputeTextureParam(shader, kernel, GBuffer1Id, m_GBuffer1.innerHandle);
        }

        private void ReadLight(Camera camera)
        {
            m_LightPositionRange = m_LightColorShadow = Vector4.zero;
            m_LightLayerMask = 0;
            var database = VividLightRenderDatabase.instance;
            var sun = RenderSettings.sun;
            bool useSun = sun != null && sun.isActiveAndEnabled && sun.type == LightType.Directional
                && sun.lightmapBakeType != LightmapBakeType.Baked && (sun.cullingMask & m_LayerMask) != 0;
            var source = useSun ? sun : database.FindBrightestPointLight(m_LayerMask);
            if (source == null)
                return;

            var light = database.UpdateLightData(source);
            var positionOrDirection = useSun ? -light.forwardWS : light.positionWS;
            float range = useSun ? -(float.IsFinite(m_SunShadowDistance) ? Mathf.Max(1f, m_SunShadowDistance) : 64f) : light.range;
            var color = light.color;
            // LightGrid attenuates directional uploads after creating VividLightData.
            // Surface Cache lighting must use that same ground-level sun color.
            if (useSun && (!source.TryGetComponent<VividAdditionalLightData>(out var additional)
                    || additional.interactsWithSky)
                && PhysicallyBasedSkyAtmosphericAttenuation.TryCreate(camera, out var atmosphere))
                color = Vector3.Scale(color,
                    PhysicallyBasedSkyAtmosphericAttenuation.Evaluate(in atmosphere, positionOrDirection));
            m_LightLayerMask = source.cullingMask & m_LayerMask;
            m_LightPositionRange = new Vector4(positionOrDirection.x, positionOrDirection.y, positionOrDirection.z, range);
            m_LightColorShadow = new Vector4(color.x, color.y, color.z, Mathf.Clamp01(light.shadowStrength));
        }

        private static RenderGraphTexture CreateOutput(string name, GraphicsFormat format)
        {
            var texture = RenderGraphTexture.CreateOutput(name, format);
            texture.desc.EnableRandomWrite = true;
            texture.desc.ClearBuffer = true;
            texture.desc.ClearColor = Color.clear;
            return texture;
        }

        private static RenderGraphTexture CreateScratch(string name, GraphicsFormat format)
        {
            var texture = CreateOutput(name, format);
            texture.desc.ClearBuffer = false;
            return texture;
        }

        private static RenderGraphTexture CreateGlobalFallback()
        {
            var texture = RenderGraphTexture.CreateOutput("Unused Global SDF Binding", GraphicsFormat.R32_SFloat);
            texture.Resize(1, 1);
            texture.desc.Dimension = TextureDimension.Tex3D;
            return texture;
        }
    }
}
