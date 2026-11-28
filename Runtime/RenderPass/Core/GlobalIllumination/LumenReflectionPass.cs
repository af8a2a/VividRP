using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// UE5 Lumen Specular Reflections subsystem (Software SDF: HZB Screen Trace + Mesh SDF + Global SDF,
    /// followed by UE5 Resolve + BRDF Spatial Reconstruction + Native Temporal &amp; Bilateral Spatial Denoiser).
    /// Runs alongside <see cref="LumenIndirectDiffusePass"/> so it shares ClosestHZB, MeshSDF CardGrid,
    /// Surface Cache FinalLighting, Global SDF Clipmap, and Surface Cache Feedback without duplicating GPU work
    /// or requiring .vrdg RenderGraph asset edits.
    /// </summary>
    internal sealed class LumenReflectionPass
    {
        private const int TileSize = 8;
        private const int CompactionThreadGroupSize = 64;
        private const int MaxRayDirections = 1024;
        private const float MaxHierarchicalScreenTraceIterations = 50f;
        private const float RelativeDepthThickness = 0.02f;
        private const float HistoryDepthTestRelativeThickness = 0.005f;
        private const float SpatialReconstructionRoughnessScale = 1.0f;
        private const float SpatialReconstructionMinWeight = 0.0f;

        private static readonly ProfilingSampler TotalSampler = new("Lumen Specular Reflections");
        private static readonly ProfilingSampler ClassifySampler = new("Lumen Reflections Tile Classification");
        private static readonly ProfilingSampler GenerateRaysSampler = new("Lumen Reflections Generate Rays");
        private static readonly ProfilingSampler ScreenTraceSampler = new("Lumen Reflections Screen Trace");
        private static readonly ProfilingSampler CompactSampler = new("Lumen Reflections Compact Traces");
        private static readonly ProfilingSampler MeshSdfTraceSampler = new("Lumen Reflections Mesh SDF Trace");
        private static readonly ProfilingSampler GlobalSdfTraceSampler = new("Lumen Reflections Global SDF Trace");
        private static readonly ProfilingSampler ResolveSampler = new("Lumen Reflections Resolve");
        private static readonly ProfilingSampler TemporalSampler = new("Lumen Reflections Temporal Denoise");
        private static readonly ProfilingSampler SpatialSampler = new("Lumen Reflections Spatial Denoise");
        private static readonly ProfilingSampler DebugSampler = new("Lumen Reflections Debug View");

        // CameraHistory IDs (cross-frame temporal rings only)
        private static readonly CameraHistoryId SpecularAndSecondMomentHistoryId = CameraHistoryId.Create("LumenReflectionSpecularHistory");
        private static readonly CameraHistoryId NumFramesAccumulatedHistoryId = CameraHistoryId.Create("LumenReflectionFramesHistory");
        private static readonly CameraHistoryId SceneDepthHistoryId = CameraHistoryId.Create("LumenReflectionSceneDepthHistory");
        private static readonly CameraHistoryId PreExposureHistoryId = CameraHistoryId.Create("LumenReflectionPreExposureHistory");

        // Shader property IDs
        private static readonly int MaxRoughnessToTraceId = Shader.PropertyToID("_LumenReflectionMaxRoughnessToTrace");
        private static readonly int MaxRoughnessToTraceForFoliageId = Shader.PropertyToID("_LumenReflectionMaxRoughnessToTraceForFoliage");
        private static readonly int InvRoughnessFadeLengthId = Shader.PropertyToID("_LumenReflectionInvRoughnessFadeLength");
        private static readonly int SmoothBiasId = Shader.PropertyToID("_LumenReflectionSmoothBias");
        private static readonly int TracingBufferSizeId = Shader.PropertyToID("_LumenReflectionTracingBufferSize");
        private static readonly int TracingBufferInvSizeId = Shader.PropertyToID("_LumenReflectionTracingBufferInvSize");
        private static readonly int DownsampleFactorXYId = Shader.PropertyToID("_LumenReflectionDownsampleFactorXY");
        private static readonly int TracingViewSizeId = Shader.PropertyToID("_LumenReflectionTracingViewSize");
        private static readonly int MaxRayIntensityId = Shader.PropertyToID("_LumenReflectionMaxRayIntensity");
        private static readonly int StateFrameIndexId = Shader.PropertyToID("_LumenReflectionStateFrameIndex");
        private static readonly int StateFrameIndexMod8Id = Shader.PropertyToID("_LumenReflectionStateFrameIndexMod8");
        private static readonly int RayDirectionFrameIndexId = Shader.PropertyToID("_LumenReflectionRayDirectionFrameIndex");
        private static readonly int GGXSamplingBiasId = Shader.PropertyToID("_LumenReflectionGGXSamplingBias");
        private static readonly int MaxTraceDistanceId = Shader.PropertyToID("_LumenReflectionMaxTraceDistance");
        private static readonly int MaxMeshSdfTraceDistanceId = Shader.PropertyToID("_LumenReflectionMaxMeshSdfTraceDistance");
        private static readonly int DenoiserOneOverTonemapRangeId = Shader.PropertyToID("_LumenReflectionDenoiserOneOverTonemapRange");
        private static readonly int HasBlueNoiseId = Shader.PropertyToID("_LumenReflectionHasBlueNoise");
        private static readonly int HasDualSlabSidecarId = Shader.PropertyToID("_LumenReflectionHasDualSlabSidecar");
        private static readonly int UseWorldCacheId = Shader.PropertyToID("_LumenReflectionUseWorldCache");
        private static readonly int UseGlobalSdfId = Shader.PropertyToID("_LumenReflectionUseGlobalSdf");
        private static readonly int BlueNoiseVec2Id = Shader.PropertyToID("_LumenReflectionBlueNoiseVec2");
        private static readonly int LayerAux0Id = Shader.PropertyToID("_LumenReflectionLayerAux0");
        private static readonly int LayerAux1Id = Shader.PropertyToID("_LumenReflectionLayerAux1");

        private static readonly int TileViewportDimensionsId = Shader.PropertyToID("_LumenTileViewportDimensions");
        private static readonly int ResolveTileViewportDimensionsId = Shader.PropertyToID("_LumenResolveTileViewportDimensions");
        private static readonly int RWClearTileArgsId = Shader.PropertyToID("_RWReflectionClearTileIndirectArgs");
        private static readonly int RWResolveTileArgsId = Shader.PropertyToID("_RWReflectionResolveTileIndirectArgs");
        private static readonly int RWClearUnusedTracingTileArgsId = Shader.PropertyToID("_RWReflectionClearUnusedTracingTileIndirectArgs");
        private static readonly int RWTracingTileArgsId = Shader.PropertyToID("_RWReflectionTracingTileIndirectArgs");
        private static readonly int RWTileBitmaskId = Shader.PropertyToID("_RWLumenTileBitmask");
        private static readonly int TileBitmaskTexId = Shader.PropertyToID("_LumenTileBitmask");
        private static readonly int RWClearTileDataId = Shader.PropertyToID("_RWReflectionClearTileData");
        private static readonly int RWTileArgsId = Shader.PropertyToID("_RWReflectionTileIndirectArgs");
        private static readonly int RWTileDataId = Shader.PropertyToID("_RWReflectionTileData");
        private static readonly int ClearUnusedTracingTileArgsReadId = Shader.PropertyToID("_ReflectionClearUnusedTracingTileIndirectArgs");
        private static readonly int ClearUnusedTracingTileDataReadId = Shader.PropertyToID("_ReflectionClearUnusedTracingTileData");
        private static readonly int TracingTileArgsReadId = Shader.PropertyToID("_ReflectionTracingTileIndirectArgs");
        private static readonly int TracingTileDataReadId = Shader.PropertyToID("_ReflectionTracingTileData");

        private static readonly int RWDownsampledDepthId = Shader.PropertyToID("_RWDownsampledDepth");
        private static readonly int RWRayBufferId = Shader.PropertyToID("_RWRayBuffer");
        private static readonly int DownsampledDepthReadId = Shader.PropertyToID("_DownsampledDepth");
        private static readonly int RayBufferReadId = Shader.PropertyToID("_RayBuffer");
        private static readonly int RWTraceHitId = Shader.PropertyToID("_RWTraceHit");
        private static readonly int RWTraceRadianceId = Shader.PropertyToID("_RWTraceRadiance");
        private static readonly int TraceHitReadId = Shader.PropertyToID("_TraceHit");
        private static readonly int TraceRadianceReadId = Shader.PropertyToID("_TraceRadiance");

        private static readonly int RWCompactedAllocatorId = Shader.PropertyToID("_RWCompactedTraceTexelAllocator");
        private static readonly int RWCompactionArgsId = Shader.PropertyToID("_RWReflectionCompactionIndirectArgs");
        private static readonly int RWCompactedDataId = Shader.PropertyToID("_RWCompactedTraceTexelData");
        private static readonly int RWCompactTracingArgsId = Shader.PropertyToID("_RWReflectionCompactTracingIndirectArgs");
        private static readonly int CompactedAllocatorReadId = Shader.PropertyToID("_CompactedTraceTexelAllocator");
        private static readonly int CompactedDataReadId = Shader.PropertyToID("_CompactedTraceTexelData");
        private static readonly int CompactionThreadGroupSizeId = Shader.PropertyToID("_LumenCompactionThreadGroupSize");
        private static readonly int CullByDistanceFromCameraId = Shader.PropertyToID("_LumenCullByDistanceFromCamera");
        private static readonly int CompactionTracingEndDistanceId = Shader.PropertyToID("_LumenCompactionTracingEndDistanceFromCamera");
        private static readonly int CompactionMaxTraceDistanceId = Shader.PropertyToID("_LumenCompactionMaxTraceDistance");
        private static readonly int MaxHierarchicalScreenTraceIterationsId = Shader.PropertyToID("_LumenMaxHierarchicalScreenTraceIterations");
        private static readonly int RelativeDepthThicknessId = Shader.PropertyToID("_LumenRelativeDepthThickness");
        private static readonly int HistoryDepthTestRelativeThicknessId = Shader.PropertyToID("_LumenHistoryDepthTestRelativeThickness");
        private static readonly int HasMeshSdfCardGridId = Shader.PropertyToID("_LumenReflectionHasMeshSdfCardGrid");
        private static readonly int SkyEnabledId = Shader.PropertyToID("_LumenReflectionSkyEnabled");
        private static readonly int CardTraceEndDistanceId = Shader.PropertyToID("_LumenCardTraceEndDistanceFromCamera");

        private static readonly int ClearTileDataReadId = Shader.PropertyToID("_ReflectionClearTileData");
        private static readonly int ResolveTileDataReadId = Shader.PropertyToID("_ReflectionResolveTileData");
        private static readonly int RWResolvedSpecularId = Shader.PropertyToID("_RWResolvedSpecular");
        private static readonly int RWResolvedSpecularDepthId = Shader.PropertyToID("_RWResolvedSpecularDepth");
        private static readonly int RWSpecularAndSecondMomentId = Shader.PropertyToID("_RWSpecularAndSecondMoment");
        private static readonly int RWNumFramesAccumulatedId = Shader.PropertyToID("_RWNumFramesAccumulated");
        private static readonly int RWSpecularIndirectAccumulatedId = Shader.PropertyToID("_RWSpecularIndirectAccumulated");
        private static readonly int RWSceneDepthHistoryId = Shader.PropertyToID("_RWSceneDepthHistory");
        private static readonly int RWPreExposureHistoryId = Shader.PropertyToID("_RWPreExposureHistory");

        private static readonly int ResolvedSpecularLightingReadId = Shader.PropertyToID("_ResolvedSpecularLighting");
        private static readonly int ResolvedReflectionsDepthReadId = Shader.PropertyToID("_ResolvedReflectionsDepth");
        private static readonly int SpecularHistoryTextureId = Shader.PropertyToID("_SpecularHistoryTexture");
        private static readonly int NumFramesAccumulatedHistoryTextureId = Shader.PropertyToID("_NumFramesAccumulatedHistoryTexture");
        private static readonly int SceneDepthHistoryReadId = Shader.PropertyToID("_SceneDepthHistory");
        private static readonly int PrevPreExposureHistoryReadId = Shader.PropertyToID("_PrevPreExposureHistory");
        private static readonly int SpecularLightingAndSecondMomentReadId = Shader.PropertyToID("_SpecularLightingAndSecondMomentTexture");
        private static readonly int NumFramesAccumulatedReadId = Shader.PropertyToID("_NumFramesAccumulatedTexture");

        private static readonly int NumSpatialReconstructionSamplesId = Shader.PropertyToID("_LumenNumSpatialReconstructionSamples");
        private static readonly int SpatialReconstructionKernelRadiusId = Shader.PropertyToID("_LumenSpatialReconstructionKernelRadius");
        private static readonly int SpatialReconstructionRoughnessScaleId = Shader.PropertyToID("_LumenSpatialReconstructionRoughnessScale");
        private static readonly int SpatialReconstructionMinWeightId = Shader.PropertyToID("_LumenSpatialReconstructionMinWeight");

        private static readonly int HistoryUVMinMaxId = Shader.PropertyToID("_LumenHistoryUVMinMax");
        private static readonly int HistoryGatherUVMinMaxId = Shader.PropertyToID("_LumenHistoryGatherUVMinMax");
        private static readonly int HistoryBufferSizeAndInvSizeId = Shader.PropertyToID("_LumenHistoryBufferSizeAndInvSize");
        private static readonly int TemporalMaxFramesAccumulatedId = Shader.PropertyToID("_LumenTemporalMaxFramesAccumulated");
        private static readonly int TemporalNeighborhoodClampScaleId = Shader.PropertyToID("_LumenTemporalNeighborhoodClampScale");
        private static readonly int HistoryDistanceThresholdId = Shader.PropertyToID("_LumenHistoryDistanceThreshold");

        private static readonly int SpatialFilterDepthWeightScaleId = Shader.PropertyToID("_LumenSpatialFilterDepthWeightScale");
        private static readonly int SpatialFilterKernelRadiusId = Shader.PropertyToID("_LumenSpatialFilterKernelRadius");
        private static readonly int SpatialFilterNumSamplesId = Shader.PropertyToID("_LumenSpatialFilterNumSamples");

        private static readonly int DebugReflectionTextureId = Shader.PropertyToID("_LumenDebugReflectionTexture");
        private static readonly int DebugReflectionOutputId = Shader.PropertyToID("_LumenDebugReflectionOutput");
        private static readonly int DebugReflectionModeId = Shader.PropertyToID("_LumenDebugReflectionMode");
        private static readonly int DebugReflectionParamsId = Shader.PropertyToID("_LumenDebugReflectionParams");

        private static readonly int ProbeCameraPositionId = Shader.PropertyToID("_SdfProbeCameraPosition");
        private static readonly int ViewProjectionId = Shader.PropertyToID("_SdfDiffuseVP");
        private static readonly int PreviousViewProjectionId = Shader.PropertyToID("_SdfDiffusePreviousVP");
        private static readonly int JitterDeltaId = Shader.PropertyToID("_SdfScreenJitterDelta");
        private static readonly int DepthThicknessScaleId = Shader.PropertyToID("_SdfDepthThicknessScale");
        private static readonly int ScreenTraceMinOccupancyId = Shader.PropertyToID("_SdfScreenTraceMinOccupancy");
        private static readonly int ClosestHzbId = Shader.PropertyToID("_SdfClosestHzb");
        private static readonly int ClosestHzbSizeId = Shader.PropertyToID("_SdfClosestHzbSize");
        private static readonly int MeshCardGridId = Shader.PropertyToID("_MeshSdfCardGrid");
        private static readonly int MeshCardGridParametersId = Shader.PropertyToID("_MeshSdfCardGridParameters");
        private static readonly int MeshCardGridViewId = Shader.PropertyToID("_MeshSdfCardGridView");
        private static readonly int MeshCardGridProjectionId = Shader.PropertyToID("_MeshSdfCardGridProjection");
        private static readonly int FarAtlasId = Shader.PropertyToID("_SdfFarRadiance");
        private static readonly int FarWindowId = Shader.PropertyToID("_SdfFarWindow");
        private static readonly int FarDistanceId = Shader.PropertyToID("_SdfFarTraceDistance");
        private static readonly int MotionId = Shader.PropertyToID("_SdfScreenMotion");

        // Pre-allocated texture descriptors (zero managed allocations per frame)
        private readonly RenderGraphTexture m_TileBitmaskDesc = CreateTarget("Lumen Reflection Tile Bitmask", GraphicsFormat.R32_UInt);
        private readonly RenderGraphTexture m_DownsampledDepthDesc = CreateTarget("Lumen Reflection Downsampled Depth", GraphicsFormat.R32_SFloat);
        private readonly RenderGraphTexture m_RayBufferDesc = CreateTarget("Lumen Reflection Ray Buffer", GraphicsFormat.R16G16B16A16_SFloat);
        private readonly RenderGraphTexture m_TraceRadianceDesc = CreateTarget("Lumen Reflection Trace Radiance", GraphicsFormat.R16G16B16A16_SFloat);
        private readonly RenderGraphTexture m_TraceHitDesc = CreateTarget("Lumen Reflection Trace Hit", GraphicsFormat.R32_SFloat);
        private readonly RenderGraphTexture m_ResolvedSpecularDesc = CreateTarget("Lumen Reflection Resolved Specular", GraphicsFormat.R16G16B16A16_SFloat);
        private readonly RenderGraphTexture m_ResolvedSpecularDepthDesc = CreateTarget("Lumen Reflection Resolved Depth", GraphicsFormat.R32_SFloat);
        private readonly RenderGraphTexture m_SpecularHistoryDesc = CreateTarget("Lumen Reflection Specular History", GraphicsFormat.R16G16B16A16_SFloat);
        private readonly RenderGraphTexture m_NumFramesHistoryDesc = CreateTarget("Lumen Reflection Num Frames History", GraphicsFormat.R8_UNorm);
        private readonly RenderGraphTexture m_SceneDepthHistoryDesc = CreateTarget("Lumen Reflection Depth History", GraphicsFormat.R32_SFloat);
        private readonly RenderGraphTexture m_SpecularOutputDesc = CreateTarget("Lumen Reflection Specular Output", GraphicsFormat.R16G16B16A16_SFloat);

        // Cached shaders & kernel IDs
        private ComputeShader m_TracingCompute;
        private ComputeShader m_DenoiserCompute;
        private int m_InitArgsKernel = -1;
        private int m_ClassifyKernel = -1;
        private int m_BuildListsKernel = -1;
        private int m_BuildListsDownsampledKernel = -1;
        private int m_ClearUnusedTracingKernel = -1;
        private int m_GenerateRaysKernel = -1;
        private int m_ClearTracesKernel = -1;
        private int m_TraceScreenKernel = -1;
        private int m_SetupCompactionArgsKernel = -1;
        private int m_CompactTracesKernel = -1;
        private int m_SetupCompactedArgsKernel = -1;
        private int m_TraceMeshSdfKernel = -1;
        private int m_TraceMeshSdfFeedbackKernel = -1;
        private int m_TraceVoxelsKernel = -1;
        private int m_TraceVoxelsFeedbackKernel = -1;

        private int m_DenoiserClearKernel = -1;
        private int m_ResolveKernel = -1;
        private int m_ResolveSpatialKernel = -1;
        private int m_ResolveDownsampledKernel = -1;
        private int m_ResolveDownsampledSpatialKernel = -1;
        private int m_TemporalKernel = -1;
        private int m_TemporalValidHistoryKernel = -1;
        private int m_SpatialBypassKernel = -1;
        private int m_SpatialFilterKernel = -1;
        private int m_DebugReflectionKernel = -1;

        // Per-frame prepared state
        private bool m_Prepared;
        private int m_Width;
        private int m_Height;
        private int m_DownsampleFactor;
        private int m_TracingWidth;
        private int m_TracingHeight;
        private int m_ResolveTileWidth;
        private int m_ResolveTileHeight;
        private int m_TracingTileWidth;
        private int m_TracingTileHeight;
        private float m_MaxRoughnessToTrace;
        private float m_MaxRoughnessToTraceForFoliage;
        private float m_InvRoughnessFadeLength;
        private float m_SmoothBias;
        private float m_GGXSamplingBias;
        private float m_MaxRayIntensity;
        private bool m_UseScreenTraces;
        private bool m_UseMeshSdfTraces;
        private bool m_UseSpatialReconstruction;
        private int m_ReconstructionSamples;
        private float m_ReconstructionKernelRadius;
        private bool m_UseTemporalFilter;
        private float m_TemporalMaxFramesAccumulated;
        private float m_TemporalNeighborhoodClampScale;
        private float m_HistoryDistanceThreshold;
        private float m_DenoiserOneOverTonemapRange;
        private bool m_UseBilateralFilter;
        private float m_BilateralKernelRadius;
        private int m_BilateralNumSamples;
        private float m_BilateralDepthWeightScale;
        private bool m_HistoryValid;

        // Imported per-frame handles
        private TextureHandle m_TileBitmask;
        private TextureHandle m_DownsampledDepth;
        private TextureHandle m_RayBuffer;
        private TextureHandle m_TraceRadiance;
        private TextureHandle m_TraceHit;
        private TextureHandle m_ResolvedSpecular;
        private TextureHandle m_ResolvedSpecularDepth;
        private TextureHandle m_PreviousSpecularHistory;
        private TextureHandle m_CurrentSpecularHistory;
        private TextureHandle m_PreviousNumFramesHistory;
        private TextureHandle m_CurrentNumFramesHistory;
        private TextureHandle m_PreviousSceneDepthHistory;
        private TextureHandle m_CurrentSceneDepthHistory;
        private BufferHandle m_PreviousPreExposureHistory;
        private BufferHandle m_CurrentPreExposureHistory;
        private TextureHandle m_SpecularOutput;

        private BufferHandle m_ClearTileData;
        private BufferHandle m_ResolveTileData;
        private BufferHandle m_ClearUnusedTracingTileData;
        private BufferHandle m_TracingTileData;
        private BufferHandle m_ClearTileArgs;
        private BufferHandle m_ResolveTileArgs;
        private BufferHandle m_ClearUnusedTracingTileArgs;
        private BufferHandle m_TracingTileArgs;
        private BufferHandle m_CompactedAllocator;
        private BufferHandle m_CompactedData;
        private BufferHandle m_CompactionArgs;
        private BufferHandle m_CompactTracingArgs;

        private CameraHistoryTexture m_SpecularHistoryRing;
        private CameraHistoryTexture m_NumFramesHistoryRing;
        private CameraHistoryTexture m_SceneDepthHistoryRing;
        private CameraHistoryBuffer m_PreExposureHistoryRing;

        private TextureHandle m_BlueNoiseVec2;
        private readonly int[] m_ScratchInt2 = new int[2];

        internal bool Prepared => m_Prepared;
        internal bool WantsScreenTraces => m_Prepared && m_UseScreenTraces;
        internal TextureHandle OutputHandle => m_SpecularOutput;

        internal Vector4 CombineParameters => BuildCombineParameters(
            m_MaxRoughnessToTrace,
            m_MaxRoughnessToTraceForFoliage,
            m_InvRoughnessFadeLength,
            m_Prepared);

        internal float SmoothBias => m_SmoothBias;

        internal static Vector4 BuildCombineParameters(
            float maxRoughnessToTrace,
            float maxRoughnessToTraceForFoliage,
            float invRoughnessFadeLength,
            bool enabled) => new(
                maxRoughnessToTrace,
                maxRoughnessToTraceForFoliage,
                invRoughnessFadeLength,
                enabled ? 1f : 0f);

        internal static void ComputeTracingAndTileDimensions(
            int width,
            int height,
            int requestedDownsample,
            bool supportsDownsampled,
            out int downsampleFactor,
            out int tracingWidth,
            out int tracingHeight,
            out int resolveTileWidth,
            out int resolveTileHeight,
            out int tracingTileWidth,
            out int tracingTileHeight)
        {
            downsampleFactor = Mathf.Clamp(requestedDownsample, 1, 2);
            if (downsampleFactor == 2 && !supportsDownsampled)
                downsampleFactor = 1;

            tracingWidth = CoreUtils.DivRoundUp(width, downsampleFactor);
            tracingHeight = CoreUtils.DivRoundUp(height, downsampleFactor);
            resolveTileWidth = CoreUtils.DivRoundUp(width, TileSize);
            resolveTileHeight = CoreUtils.DivRoundUp(height, TileSize);
            tracingTileWidth = CoreUtils.DivRoundUp(tracingWidth, TileSize);
            tracingTileHeight = CoreUtils.DivRoundUp(tracingHeight, TileSize);
        }

        // Verbatim UE5 LumenReflectionsCombine.ush::LumenCombineReflectionsAlpha
        internal static float ComputeCombineReflectionsAlpha(
            float roughness,
            bool hasBackfaceDiffuse,
            float maxRoughnessToTrace,
            float maxRoughnessToTraceForFoliage,
            float invRoughnessFadeLength)
        {
            float maxRoughness = hasBackfaceDiffuse ? maxRoughnessToTraceForFoliage : maxRoughnessToTrace;
            return Mathf.Clamp01((maxRoughness - roughness) * invRoughnessFadeLength);
        }

        // Verbatim UE5 LumenReflectionCommon.ush::ApplySmoothBias
        internal static float ApplySmoothBias(float roughness, float smoothBias)
        {
            if (smoothBias >= 0f)
            {
                float newRoughness = roughness * roughness * roughness * (3f - 2f * roughness);
                return Mathf.Lerp(roughness, newRoughness, smoothBias);
            }
            return roughness;
        }

        internal void Initialize(VividRPCoreResources resources)
        {
            var tracing = resources?.LumenReflectionsTracingCompute;
            var denoiser = resources?.LumenReflectionsDenoiserCompute;
            if (!ReferenceEquals(m_TracingCompute, tracing))
            {
                m_TracingCompute = tracing;
                m_InitArgsKernel = FindKernel(tracing, "InitReflectionIndirectArgsCS");
                m_ClassifyKernel = FindKernel(tracing, "ReflectionTileClassifyCS");
                m_BuildListsKernel = FindKernel(tracing, "ReflectionTileClassificationBuildListsCS");
                m_BuildListsDownsampledKernel = FindKernel(tracing, "ReflectionTileClassificationBuildListsDownsampledCS");
                m_ClearUnusedTracingKernel = FindKernel(tracing, "ReflectionClearUnusedTraceTileDataCS");
                m_GenerateRaysKernel = FindKernel(tracing, "ReflectionGenerateRaysCS");
                m_ClearTracesKernel = FindKernel(tracing, "ReflectionClearTracesCS");
                m_TraceScreenKernel = FindKernel(tracing, "ReflectionTraceScreenTexturesCS");
                m_SetupCompactionArgsKernel = FindKernel(tracing, "SetupCompactionIndirectArgsCS");
                m_CompactTracesKernel = FindKernel(tracing, "ReflectionCompactTracesCS");
                m_SetupCompactedArgsKernel = FindKernel(tracing, "SetupCompactedTracesIndirectArgsCS");
                m_TraceMeshSdfKernel = FindKernel(tracing, "ReflectionTraceMeshSDFsCS");
                m_TraceMeshSdfFeedbackKernel = FindKernel(tracing, "ReflectionTraceMeshSDFsFeedbackCS");
                m_TraceVoxelsKernel = FindKernel(tracing, "ReflectionTraceVoxelsCS");
                m_TraceVoxelsFeedbackKernel = FindKernel(tracing, "ReflectionTraceVoxelsFeedbackCS");
            }

            if (!ReferenceEquals(m_DenoiserCompute, denoiser))
            {
                m_DenoiserCompute = denoiser;
                m_DenoiserClearKernel = FindKernel(denoiser, "LumenReflectionDenoiserClearCS");
                m_ResolveKernel = FindKernel(denoiser, "LumenReflectionResolveCS");
                m_ResolveSpatialKernel = FindKernel(denoiser, "LumenReflectionResolveSpatialCS");
                m_ResolveDownsampledKernel = FindKernel(denoiser, "LumenReflectionResolveDownsampledCS");
                m_ResolveDownsampledSpatialKernel = FindKernel(denoiser, "LumenReflectionResolveDownsampledSpatialCS");
                m_TemporalKernel = FindKernel(denoiser, "LumenReflectionDenoiserTemporalCS");
                m_TemporalValidHistoryKernel = FindKernel(denoiser, "LumenReflectionDenoiserTemporalValidHistoryCS");
                m_SpatialBypassKernel = FindKernel(denoiser, "LumenReflectionDenoiserSpatialBypassCS");
                m_SpatialFilterKernel = FindKernel(denoiser, "LumenReflectionDenoiserSpatialCS");
                m_DebugReflectionKernel = FindKernel(denoiser, "LumenDebugReflectionCS");
            }
        }

        internal bool IsSupported()
        {
            return m_TracingCompute != null
                && m_DenoiserCompute != null
                && m_InitArgsKernel >= 0
                && m_ClassifyKernel >= 0
                && m_BuildListsKernel >= 0
                && m_GenerateRaysKernel >= 0
                && m_ClearTracesKernel >= 0
                && m_SetupCompactionArgsKernel >= 0
                && m_CompactTracesKernel >= 0
                && m_SetupCompactedArgsKernel >= 0
                && m_TraceVoxelsKernel >= 0
                && m_DenoiserClearKernel >= 0
                && m_ResolveKernel >= 0
                && m_TemporalKernel >= 0
                && m_SpatialFilterKernel >= 0;
        }

        internal void ResetFrame()
        {
            m_Prepared = false;
            m_HistoryValid = false;
            m_SpecularHistoryRing = null;
            m_NumFramesHistoryRing = null;
            m_SceneDepthHistoryRing = null;
            m_PreExposureHistoryRing = null;
            m_SpecularOutput = default;
            m_BlueNoiseVec2 = default;
        }

        internal void Prepare(
            IRenderPass owner,
            ContextContainer frameData,
            Camera camera,
            int width,
            int height,
            LumenIndirectDiffuse settings,
            bool resetHistory)
        {
            ResetFrame();

            if (!IsSupported() || settings == null || !settings.lumenReflections.value)
                return;

            if (width <= 0 || height <= 0)
                return;

            float maxRoughness = Mathf.Clamp01(settings.lumenReflectionMaxRoughnessToTrace.value);
            if (maxRoughness <= 0.0001f)
                return;

            var history = camera.GetVividCameraHistory();
            if (!history.IsFrameActive || !PassRecorder.HasActiveRenderGraph)
                return;

            m_Width = width;
            m_Height = height;
            ComputeTracingAndTileDimensions(
                width,
                height,
                settings.lumenReflectionDownsampleFactor.value,
                m_BuildListsDownsampledKernel >= 0 && m_ResolveDownsampledKernel >= 0,
                out m_DownsampleFactor,
                out m_TracingWidth,
                out m_TracingHeight,
                out m_ResolveTileWidth,
                out m_ResolveTileHeight,
                out m_TracingTileWidth,
                out m_TracingTileHeight);

            m_MaxRoughnessToTrace = maxRoughness;
            m_MaxRoughnessToTraceForFoliage = Mathf.Clamp01(settings.lumenReflectionMaxRoughnessToTraceForFoliage.value);
            m_InvRoughnessFadeLength = 1f / Mathf.Clamp(settings.lumenReflectionRoughnessFadeLength.value, 0.001f, 1f);
            m_SmoothBias = Mathf.Clamp01(settings.lumenReflectionSmoothBias.value);
            m_GGXSamplingBias = Mathf.Clamp(settings.lumenReflectionGGXSamplingBias.value, 0f, 0.99f);
            m_MaxRayIntensity = Mathf.Max(0f, settings.lumenReflectionMaxRayIntensity.value);
            m_UseScreenTraces = settings.lumenReflectionScreenTraces.value && m_TraceScreenKernel >= 0;
            m_UseMeshSdfTraces = settings.lumenReflectionMeshSdfTraces.value && m_TraceMeshSdfKernel >= 0;
            m_UseSpatialReconstruction = settings.lumenReflectionScreenSpaceReconstruction.value;
            m_ReconstructionSamples = Mathf.Clamp(settings.lumenReflectionReconstructionSamples.value, 1, 16);
            m_ReconstructionKernelRadius = Mathf.Clamp(settings.lumenReflectionReconstructionKernelRadius.value, 1f, 32f);
            m_UseTemporalFilter = settings.lumenReflectionTemporalFilter.value;
            m_TemporalMaxFramesAccumulated = Mathf.Clamp(settings.lumenReflectionTemporalMaxFramesAccumulated.value, 1f, 32f);
            m_TemporalNeighborhoodClampScale = Mathf.Clamp(settings.lumenReflectionTemporalNeighborhoodClampScale.value, 0.25f, 4f);
            m_HistoryDistanceThreshold = Mathf.Clamp(settings.lumenReflectionTemporalDistanceThreshold.value, 0.001f, 0.2f);
            float tonemapRange = settings.lumenReflectionDenoiserTonemapRange.value;
            m_DenoiserOneOverTonemapRange = tonemapRange > 0f ? 1f / tonemapRange : 0f;
            m_UseBilateralFilter = settings.lumenReflectionBilateralFilter.value;
            m_BilateralKernelRadius = Mathf.Clamp(settings.lumenReflectionBilateralFilterKernelRadius.value, 1f, 32f);
            m_BilateralNumSamples = Mathf.Clamp(settings.lumenReflectionBilateralFilterNumSamples.value, 0, 16);
            m_BilateralDepthWeightScale = Mathf.Max(1f, settings.lumenReflectionBilateralFilterDepthWeightScale.value);

            // Resize pre-allocated descriptors
            m_TileBitmaskDesc.Resize(m_ResolveTileWidth, m_ResolveTileHeight);
            m_DownsampledDepthDesc.Resize(m_TracingWidth, m_TracingHeight);
            m_RayBufferDesc.Resize(m_TracingWidth, m_TracingHeight);
            m_TraceRadianceDesc.Resize(m_TracingWidth, m_TracingHeight);
            m_TraceHitDesc.Resize(m_TracingWidth, m_TracingHeight);
            m_ResolvedSpecularDesc.Resize(width, height);
            m_ResolvedSpecularDepthDesc.Resize(width, height);
            m_SpecularHistoryDesc.Resize(width, height);
            m_NumFramesHistoryDesc.Resize(width, height);
            m_SceneDepthHistoryDesc.Resize(width, height);
            m_SpecularOutputDesc.Resize(width, height);

            // Same-frame output texture published to FrameResourceTable for DeferredLightingPass.
            // Owned by the Reflection sub-pass rather than the Diffuse pass so its lifetime starts at Reflections.
            TextureDesc specularOutputDesc = m_SpecularOutputDesc.desc;
            m_SpecularOutput = PassRecorder.CreateTextureHandle(in specularOutputDesc);
            FrameResourceTable.GetOrCreate(frameData).PublishTexture(
                FrameResourceSlots.LumenSpecularReflection,
                m_SpecularOutput);

            // Double-buffered temporal history textures & pre-exposure buffer (declared on Reflection sub-pass builder).
            m_SpecularHistoryRing = history.GetOrCreateTexture(SpecularAndSecondMomentHistoryId, 2,
                CameraHistoryRenderGraphBridge.CreateDescriptor(m_SpecularHistoryDesc.desc));
            m_PreviousSpecularHistory = CameraHistoryRenderGraphBridge.Import(m_SpecularHistoryRing, 1);
            m_CurrentSpecularHistory = CameraHistoryRenderGraphBridge.Import(m_SpecularHistoryRing, 0);

            m_NumFramesHistoryRing = history.GetOrCreateTexture(NumFramesAccumulatedHistoryId, 2,
                CameraHistoryRenderGraphBridge.CreateDescriptor(m_NumFramesHistoryDesc.desc));
            m_PreviousNumFramesHistory = CameraHistoryRenderGraphBridge.Import(m_NumFramesHistoryRing, 1);
            m_CurrentNumFramesHistory = CameraHistoryRenderGraphBridge.Import(m_NumFramesHistoryRing, 0);

            m_SceneDepthHistoryRing = history.GetOrCreateTexture(SceneDepthHistoryId, 2,
                CameraHistoryRenderGraphBridge.CreateDescriptor(m_SceneDepthHistoryDesc.desc));
            m_PreviousSceneDepthHistory = CameraHistoryRenderGraphBridge.Import(m_SceneDepthHistoryRing, 1);
            m_CurrentSceneDepthHistory = CameraHistoryRenderGraphBridge.Import(m_SceneDepthHistoryRing, 0);

            var preExposureDesc = new CameraHistoryBufferDescriptor(1, sizeof(float), GraphicsBuffer.Target.Structured);
            m_PreExposureHistoryRing = history.GetOrCreateBuffer(PreExposureHistoryId, 2, preExposureDesc);
            m_PreviousPreExposureHistory = CameraHistoryRenderGraphBridge.Import(m_PreExposureHistoryRing, 1);
            m_CurrentPreExposureHistory = CameraHistoryRenderGraphBridge.Import(m_PreExposureHistoryRing, 0);

            m_HistoryValid = m_UseTemporalFilter && !resetHistory
                && m_SpecularHistoryRing.IsValid()
                && m_NumFramesHistoryRing.IsValid()
                && m_SceneDepthHistoryRing.IsValid()
                && m_PreExposureHistoryRing.IsValid();

            if (BlueNoise.Instance != null && BlueNoise.Instance.VectorAtlas.IsValid())
                m_BlueNoiseVec2 = BlueNoise.Instance.VectorAtlas;

            if (!m_SpecularOutput.IsValid())
                return;

            m_Prepared = true;
        }

        /// <summary>
        /// Declares Reflection sub-pass resources on its own CoreRP ComputePass builder.
        /// All 7 scratch textures and 12 scratch buffers are created as true sub-pass transients
        /// so CoreRP aliases them against the preceding Diffuse sub-pass's released transients.
        /// </summary>
        internal void SetupSubPassResources(IComputeRenderGraphBuilder builder)
        {
            if (builder == null || !m_Prepared)
                return;

            m_TileBitmask = CreateSubPassTransientTexture(builder, m_TileBitmaskDesc);
            m_DownsampledDepth = CreateSubPassTransientTexture(builder, m_DownsampledDepthDesc);
            m_RayBuffer = CreateSubPassTransientTexture(builder, m_RayBufferDesc);
            m_TraceRadiance = CreateSubPassTransientTexture(builder, m_TraceRadianceDesc);
            m_TraceHit = CreateSubPassTransientTexture(builder, m_TraceHitDesc);
            m_ResolvedSpecular = CreateSubPassTransientTexture(builder, m_ResolvedSpecularDesc);
            m_ResolvedSpecularDepth = CreateSubPassTransientTexture(builder, m_ResolvedSpecularDepthDesc);

            int resolveTileCount = Mathf.Max(1, m_ResolveTileWidth * m_ResolveTileHeight);
            int tracingTileCount = Mathf.Max(1, m_TracingTileWidth * m_TracingTileHeight);
            int tracingTexelCount = Mathf.Max(1, m_TracingWidth * m_TracingHeight);
            const GraphicsBuffer.Target indirectStructured = GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments;

            m_ClearTileData = CreateSubPassTransientBuffer(builder, "LumenReflectionClearTileData", resolveTileCount, sizeof(uint), GraphicsBuffer.Target.Structured);
            m_ResolveTileData = CreateSubPassTransientBuffer(builder, "LumenReflectionResolveTileData", resolveTileCount, sizeof(uint), GraphicsBuffer.Target.Structured);
            m_ClearUnusedTracingTileData = CreateSubPassTransientBuffer(builder, "LumenReflectionClearUnusedTracingTileData", tracingTileCount, sizeof(uint), GraphicsBuffer.Target.Structured);
            m_TracingTileData = CreateSubPassTransientBuffer(builder, "LumenReflectionTracingTileData", tracingTileCount, sizeof(uint), GraphicsBuffer.Target.Structured);

            m_ClearTileArgs = CreateSubPassTransientBuffer(builder, "LumenReflectionClearTileArgs", 3, sizeof(uint), indirectStructured);
            m_ResolveTileArgs = CreateSubPassTransientBuffer(builder, "LumenReflectionResolveTileArgs", 3, sizeof(uint), indirectStructured);
            m_ClearUnusedTracingTileArgs = CreateSubPassTransientBuffer(builder, "LumenReflectionClearUnusedTracingTileArgs", 3, sizeof(uint), indirectStructured);
            m_TracingTileArgs = CreateSubPassTransientBuffer(builder, "LumenReflectionTracingTileArgs", 3, sizeof(uint), indirectStructured);

            m_CompactedAllocator = CreateSubPassTransientBuffer(builder, "LumenReflectionCompactedAllocator", 1, sizeof(uint), GraphicsBuffer.Target.Structured);
            m_CompactedData = CreateSubPassTransientBuffer(builder, "LumenReflectionCompactedData", tracingTexelCount, sizeof(uint), GraphicsBuffer.Target.Structured);
            m_CompactionArgs = CreateSubPassTransientBuffer(builder, "LumenReflectionCompactionArgs", 3, sizeof(uint), indirectStructured);
            m_CompactTracingArgs = CreateSubPassTransientBuffer(builder, "LumenReflectionCompactTracingArgs", 3, sizeof(uint), indirectStructured);

            if (m_SpecularOutput.IsValid())
                builder.UseTexture(m_SpecularOutput, AccessFlags.ReadWrite);
            if (m_PreviousSpecularHistory.IsValid())
                builder.UseTexture(m_PreviousSpecularHistory, AccessFlags.Read);
            if (m_CurrentSpecularHistory.IsValid())
                builder.UseTexture(m_CurrentSpecularHistory, AccessFlags.ReadWrite);
            if (m_PreviousNumFramesHistory.IsValid())
                builder.UseTexture(m_PreviousNumFramesHistory, AccessFlags.Read);
            if (m_CurrentNumFramesHistory.IsValid())
                builder.UseTexture(m_CurrentNumFramesHistory, AccessFlags.ReadWrite);
            if (m_PreviousSceneDepthHistory.IsValid())
                builder.UseTexture(m_PreviousSceneDepthHistory, AccessFlags.Read);
            if (m_CurrentSceneDepthHistory.IsValid())
                builder.UseTexture(m_CurrentSceneDepthHistory, AccessFlags.ReadWrite);
            if (m_PreviousPreExposureHistory.IsValid())
                builder.UseBuffer(m_PreviousPreExposureHistory, AccessFlags.Read);
            if (m_CurrentPreExposureHistory.IsValid())
                builder.UseBuffer(m_CurrentPreExposureHistory, AccessFlags.ReadWrite);
            if (m_BlueNoiseVec2.IsValid())
                builder.UseTexture(m_BlueNoiseVec2, AccessFlags.Read);
        }

        internal void MarkHistoriesWritten()
        {
            m_SpecularHistoryRing?.MarkWritten();
            m_NumFramesHistoryRing?.MarkWritten();
            m_SceneDepthHistoryRing?.MarkWritten();
            m_PreExposureHistoryRing?.MarkWritten();
        }

        internal void Record(
            ComputeCommandBuffer cmd,
            LumenIndirectDiffusePass owner,
            int frame,
            TextureHandle black,
            TextureHandle layerAux0,
            TextureHandle layerAux1,
            bool hasDualSlabSidecar,
            TextureHandle closestHzb,
            Vector4 closestHzbSize,
            float depthThicknessScale,
            bool hasScreenHistory,
            bool hasMeshSdfScene,
            bool hasMeshSdfCardGrid,
            BufferHandle meshCardGrid,
            Vector4 meshCardGridParameters,
            Matrix4x4 meshCardGridView,
            Matrix4x4 meshCardGridProjection,
            bool hasGlobalSdf,
            bool hasWorldCache,
            TextureHandle farAtlas,
            Vector4 farWindow,
            float farTraceDistance,
            float maxTraceDistance,
            float meshDetailDistance,
            bool skyEnabled,
            LumenDebugViewPass debugView)
        {
            if (!m_Prepared)
                return;

            using var totalScope = new ProfilingScope(cmd, TotalSampler);

            uint stateFrame = unchecked((uint)frame);
            uint rayDirFrame = stateFrame % MaxRayDirections;
            bool downsampled = m_DownsampleFactor > 1;
            float effectiveMaxTraceDistance = Mathf.Max(maxTraceDistance, 1f);
            float effectiveMeshSdfDistance = hasGlobalSdf
                ? Mathf.Clamp(meshDetailDistance > 0f ? meshDetailDistance : 10f, 1f, effectiveMaxTraceDistance)
                : effectiveMaxTraceDistance;
            bool recordFeedback = debugView != null && debugView.HasSurfaceCacheFeedback;

            // Bind common parameters to both shaders
            BindCommonParameters(cmd, m_TracingCompute, owner, stateFrame, rayDirFrame,
                hasDualSlabSidecar, hasWorldCache, hasGlobalSdf, effectiveMaxTraceDistance, effectiveMeshSdfDistance);
            BindCommonParameters(cmd, m_DenoiserCompute, owner, stateFrame, rayDirFrame,
                hasDualSlabSidecar, hasWorldCache, hasGlobalSdf, effectiveMaxTraceDistance, effectiveMeshSdfDistance);

            // -----------------------------------------------------------------
            // Stage 1: Tile Classification & Indirect Args
            // -----------------------------------------------------------------
            BufferHandle activeTracingTileData = downsampled ? m_TracingTileData : m_ResolveTileData;
            BufferHandle activeTracingTileArgs = downsampled ? m_TracingTileArgs : m_ResolveTileArgs;
            BufferHandle activeClearUnusedTracingTileData = downsampled ? m_ClearUnusedTracingTileData : m_ClearTileData;
            BufferHandle activeClearUnusedTracingTileArgs = downsampled ? m_ClearUnusedTracingTileArgs : m_ClearTileArgs;

            using (new ProfilingScope(cmd, ClassifySampler))
            {
                cmd.SetComputeBufferParam(m_TracingCompute, m_InitArgsKernel, RWClearTileArgsId, m_ClearTileArgs);
                cmd.SetComputeBufferParam(m_TracingCompute, m_InitArgsKernel, RWResolveTileArgsId, m_ResolveTileArgs);
                cmd.SetComputeBufferParam(m_TracingCompute, m_InitArgsKernel, RWClearUnusedTracingTileArgsId, m_ClearUnusedTracingTileArgs);
                cmd.SetComputeBufferParam(m_TracingCompute, m_InitArgsKernel, RWTracingTileArgsId, m_TracingTileArgs);
                cmd.DispatchCompute(m_TracingCompute, m_InitArgsKernel, 1, 1, 1);

                SetInt2(cmd, m_TracingCompute, ResolveTileViewportDimensionsId, m_ResolveTileWidth, m_ResolveTileHeight);
                BindMaterialInputs(cmd, m_TracingCompute, m_ClassifyKernel, owner, layerAux0, layerAux1, hasDualSlabSidecar, black);
                cmd.SetComputeTextureParam(m_TracingCompute, m_ClassifyKernel, RWTileBitmaskId, m_TileBitmask);
                cmd.DispatchCompute(m_TracingCompute, m_ClassifyKernel, m_ResolveTileWidth, m_ResolveTileHeight, 1);

                // Build full-res Resolve & Clear tile lists
                SetInt2(cmd, m_TracingCompute, TileViewportDimensionsId, m_ResolveTileWidth, m_ResolveTileHeight);
                cmd.SetComputeTextureParam(m_TracingCompute, m_BuildListsKernel, TileBitmaskTexId, m_TileBitmask);
                cmd.SetComputeBufferParam(m_TracingCompute, m_BuildListsKernel, RWTileDataId, m_ResolveTileData);
                cmd.SetComputeBufferParam(m_TracingCompute, m_BuildListsKernel, RWTileArgsId, m_ResolveTileArgs);
                cmd.SetComputeBufferParam(m_TracingCompute, m_BuildListsKernel, RWClearTileDataId, m_ClearTileData);
                cmd.SetComputeBufferParam(m_TracingCompute, m_BuildListsKernel, RWClearTileArgsId, m_ClearTileArgs);
                cmd.DispatchCompute(m_TracingCompute, m_BuildListsKernel,
                    CoreUtils.DivRoundUp(m_ResolveTileWidth, TileSize),
                    CoreUtils.DivRoundUp(m_ResolveTileHeight, TileSize), 1);

                if (downsampled)
                {
                    SetInt2(cmd, m_TracingCompute, TileViewportDimensionsId, m_TracingTileWidth, m_TracingTileHeight);
                    cmd.SetComputeTextureParam(m_TracingCompute, m_BuildListsDownsampledKernel, TileBitmaskTexId, m_TileBitmask);
                    cmd.SetComputeBufferParam(m_TracingCompute, m_BuildListsDownsampledKernel, RWTileDataId, m_TracingTileData);
                    cmd.SetComputeBufferParam(m_TracingCompute, m_BuildListsDownsampledKernel, RWTileArgsId, m_TracingTileArgs);
                    cmd.SetComputeBufferParam(m_TracingCompute, m_BuildListsDownsampledKernel, RWClearTileDataId, m_ClearUnusedTracingTileData);
                    cmd.SetComputeBufferParam(m_TracingCompute, m_BuildListsDownsampledKernel, RWClearTileArgsId, m_ClearUnusedTracingTileArgs);
                    cmd.DispatchCompute(m_TracingCompute, m_BuildListsDownsampledKernel,
                        CoreUtils.DivRoundUp(m_TracingTileWidth, TileSize),
                        CoreUtils.DivRoundUp(m_TracingTileHeight, TileSize), 1);
                }
            }

            // -----------------------------------------------------------------
            // Stage 2: Clear Unused Trace Tiles, Generate Rays & Clear Traces
            // -----------------------------------------------------------------
            using (new ProfilingScope(cmd, GenerateRaysSampler))
            {
                if ((m_UseSpatialReconstruction || downsampled) && m_ClearUnusedTracingKernel >= 0)
                {
                    cmd.SetComputeTextureParam(m_TracingCompute, m_ClearUnusedTracingKernel, RWDownsampledDepthId, m_DownsampledDepth);
                    cmd.SetComputeBufferParam(m_TracingCompute, m_ClearUnusedTracingKernel, ClearUnusedTracingTileArgsReadId, activeClearUnusedTracingTileArgs);
                    cmd.SetComputeBufferParam(m_TracingCompute, m_ClearUnusedTracingKernel, ClearUnusedTracingTileDataReadId, activeClearUnusedTracingTileData);
                    cmd.DispatchCompute(m_TracingCompute, m_ClearUnusedTracingKernel, activeClearUnusedTracingTileArgs, 0);
                }

                BindMaterialInputs(cmd, m_TracingCompute, m_GenerateRaysKernel, owner, layerAux0, layerAux1, hasDualSlabSidecar, black);
                cmd.SetComputeTextureParam(m_TracingCompute, m_GenerateRaysKernel, BlueNoiseVec2Id, m_BlueNoiseVec2.IsValid() ? m_BlueNoiseVec2 : black);
                cmd.SetComputeBufferParam(m_TracingCompute, m_GenerateRaysKernel, TracingTileDataReadId, activeTracingTileData);
                cmd.SetComputeTextureParam(m_TracingCompute, m_GenerateRaysKernel, RWRayBufferId, m_RayBuffer);
                cmd.SetComputeTextureParam(m_TracingCompute, m_GenerateRaysKernel, RWDownsampledDepthId, m_DownsampledDepth);
                cmd.DispatchCompute(m_TracingCompute, m_GenerateRaysKernel, activeTracingTileArgs, 0);

                cmd.SetComputeBufferParam(m_TracingCompute, m_ClearTracesKernel, TracingTileDataReadId, activeTracingTileData);
                cmd.SetComputeTextureParam(m_TracingCompute, m_ClearTracesKernel, RWTraceRadianceId, m_TraceRadiance);
                cmd.SetComputeTextureParam(m_TracingCompute, m_ClearTracesKernel, RWTraceHitId, m_TraceHit);
                cmd.DispatchCompute(m_TracingCompute, m_ClearTracesKernel, activeTracingTileArgs, 0);
            }

            // -----------------------------------------------------------------
            // Stage 3A: HZB Screen-Space Tracing (UE5 ReflectionTraceScreenTexturesCS)
            // -----------------------------------------------------------------
            if (m_UseScreenTraces && hasScreenHistory && closestHzb.IsValid())
            {
                using (new ProfilingScope(cmd, ScreenTraceSampler))
                {
                    owner.BindReceiverForReflections(cmd, m_TracingCompute, m_TraceScreenKernel);
                    owner.BindScreenHistoryForReflections(cmd, m_TracingCompute, m_TraceScreenKernel, black);
                    cmd.SetComputeVectorParam(m_TracingCompute, ClosestHzbSizeId, closestHzbSize);
                    cmd.SetComputeFloatParam(m_TracingCompute, DepthThicknessScaleId, depthThicknessScale);
                    // Matches UE 5.7.4 LumenReflectionTracing default (0) to avoid extra SDF fallback traces.
                    cmd.SetComputeIntParam(m_TracingCompute, ScreenTraceMinOccupancyId, 0);
                    cmd.SetComputeFloatParam(m_TracingCompute, MaxHierarchicalScreenTraceIterationsId, MaxHierarchicalScreenTraceIterations);
                    cmd.SetComputeFloatParam(m_TracingCompute, RelativeDepthThicknessId, RelativeDepthThickness);
                    cmd.SetComputeFloatParam(m_TracingCompute, HistoryDepthTestRelativeThicknessId, HistoryDepthTestRelativeThickness);
                    cmd.SetComputeTextureParam(m_TracingCompute, m_TraceScreenKernel, ClosestHzbId, closestHzb);
                    cmd.SetComputeTextureParam(m_TracingCompute, m_TraceScreenKernel, DownsampledDepthReadId, m_DownsampledDepth);
                    cmd.SetComputeTextureParam(m_TracingCompute, m_TraceScreenKernel, RayBufferReadId, m_RayBuffer);
                    cmd.SetComputeBufferParam(m_TracingCompute, m_TraceScreenKernel, TracingTileDataReadId, activeTracingTileData);
                    cmd.SetComputeTextureParam(m_TracingCompute, m_TraceScreenKernel, RWTraceRadianceId, m_TraceRadiance);
                    cmd.SetComputeTextureParam(m_TracingCompute, m_TraceScreenKernel, RWTraceHitId, m_TraceHit);
                    cmd.DispatchCompute(m_TracingCompute, m_TraceScreenKernel, activeTracingTileArgs, 0);
                }
            }

            // -----------------------------------------------------------------
            // Stage 3B: Compact & Trace Mesh SDFs (UE5 ReflectionTraceMeshSDFsCS)
            // -----------------------------------------------------------------
            if (m_UseMeshSdfTraces && hasMeshSdfScene)
            {
                float cardEndDistance = Mathf.Max(effectiveMeshSdfDistance * 2f, 40f);
                DispatchCompaction(cmd, activeTracingTileData, activeTracingTileArgs,
                    cullByCameraDistance: hasGlobalSdf,
                    endDistanceFromCamera: cardEndDistance,
                    maxTraceDistance: effectiveMeshSdfDistance);

                using (new ProfilingScope(cmd, MeshSdfTraceSampler))
                {
                    int meshKernel = recordFeedback && m_TraceMeshSdfFeedbackKernel >= 0
                        ? m_TraceMeshSdfFeedbackKernel
                        : m_TraceMeshSdfKernel;

                    owner.BindReceiverForReflections(cmd, m_TracingCompute, meshKernel);
                    owner.BindMeshSdfSurfaceCacheForReflections(cmd, m_TracingCompute, meshKernel, black);
                    if (recordFeedback)
                        debugView.BindFeedback(cmd, m_TracingCompute, meshKernel);

                    cmd.SetComputeIntParam(m_TracingCompute, HasMeshSdfCardGridId, hasMeshSdfCardGrid && meshCardGrid.IsValid() ? 1 : 0);
                    cmd.SetComputeFloatParam(m_TracingCompute, CardTraceEndDistanceId, hasGlobalSdf ? cardEndDistance : 0f);
                    cmd.SetComputeVectorParam(m_TracingCompute, MeshCardGridParametersId, meshCardGridParameters);
                    cmd.SetComputeMatrixParam(m_TracingCompute, MeshCardGridViewId, meshCardGridView);
                    cmd.SetComputeMatrixParam(m_TracingCompute, MeshCardGridProjectionId, meshCardGridProjection);
                    cmd.SetComputeBufferParam(m_TracingCompute, meshKernel, MeshCardGridId,
                        hasMeshSdfCardGrid && meshCardGrid.IsValid() ? meshCardGrid : m_CompactedAllocator);

                    cmd.SetComputeTextureParam(m_TracingCompute, meshKernel, DownsampledDepthReadId, m_DownsampledDepth);
                    cmd.SetComputeTextureParam(m_TracingCompute, meshKernel, RayBufferReadId, m_RayBuffer);
                    cmd.SetComputeBufferParam(m_TracingCompute, meshKernel, CompactedAllocatorReadId, m_CompactedAllocator);
                    cmd.SetComputeBufferParam(m_TracingCompute, meshKernel, CompactedDataReadId, m_CompactedData);
                    cmd.SetComputeTextureParam(m_TracingCompute, meshKernel, RWTraceRadianceId, m_TraceRadiance);
                    cmd.SetComputeTextureParam(m_TracingCompute, meshKernel, RWTraceHitId, m_TraceHit);
                    cmd.DispatchCompute(m_TracingCompute, meshKernel, m_CompactTracingArgs, 0);
                }
            }

            // -----------------------------------------------------------------
            // Stage 3C: Compact & Trace Global SDF + World Cache + Occluded Sky
            // -----------------------------------------------------------------
            DispatchCompaction(cmd, activeTracingTileData, activeTracingTileArgs,
                cullByCameraDistance: false,
                endDistanceFromCamera: effectiveMaxTraceDistance,
                maxTraceDistance: effectiveMaxTraceDistance);

            using (new ProfilingScope(cmd, GlobalSdfTraceSampler))
            {
                int voxelKernel = recordFeedback && m_TraceVoxelsFeedbackKernel >= 0
                    ? m_TraceVoxelsFeedbackKernel
                    : m_TraceVoxelsKernel;

                owner.BindReceiverForReflections(cmd, m_TracingCompute, voxelKernel);
                owner.BindMeshSdfSurfaceCacheForReflections(cmd, m_TracingCompute, voxelKernel, black);
                owner.BindGlobalSdfForReflections(cmd, m_TracingCompute, voxelKernel, hasGlobalSdf);
                owner.BindOccludedSkyForReflections(cmd, m_TracingCompute, voxelKernel);
                if (recordFeedback)
                    debugView.BindFeedback(cmd, m_TracingCompute, voxelKernel);

                cmd.SetComputeIntParam(m_TracingCompute, SkyEnabledId, skyEnabled ? 1 : 0);
                cmd.SetComputeTextureParam(m_TracingCompute, voxelKernel, FarAtlasId, hasWorldCache && farAtlas.IsValid() ? farAtlas : black);
                cmd.SetComputeVectorParam(m_TracingCompute, FarWindowId, farWindow);
                cmd.SetComputeFloatParam(m_TracingCompute, FarDistanceId, hasWorldCache ? farTraceDistance : 0f);
                cmd.SetComputeTextureParam(m_TracingCompute, voxelKernel, DownsampledDepthReadId, m_DownsampledDepth);
                cmd.SetComputeTextureParam(m_TracingCompute, voxelKernel, RayBufferReadId, m_RayBuffer);
                cmd.SetComputeBufferParam(m_TracingCompute, voxelKernel, CompactedAllocatorReadId, m_CompactedAllocator);
                cmd.SetComputeBufferParam(m_TracingCompute, voxelKernel, CompactedDataReadId, m_CompactedData);
                cmd.SetComputeTextureParam(m_TracingCompute, voxelKernel, RWTraceRadianceId, m_TraceRadiance);
                cmd.SetComputeTextureParam(m_TracingCompute, voxelKernel, RWTraceHitId, m_TraceHit);
                cmd.DispatchCompute(m_TracingCompute, voxelKernel, m_CompactTracingArgs, 0);
            }

            // -----------------------------------------------------------------
            // Stage 4: Clear Empty Tiles + Resolve & BRDF Spatial Reconstruction
            // -----------------------------------------------------------------
            using (new ProfilingScope(cmd, ResolveSampler))
            {
                // Clear unused tiles so temporal/spatial neighbor taps see INVALID_LIGHTING
                owner.BindReceiverForReflections(cmd, m_DenoiserCompute, m_DenoiserClearKernel);
                cmd.SetComputeBufferParam(m_DenoiserCompute, m_DenoiserClearKernel, ClearTileDataReadId, m_ClearTileData);
                cmd.SetComputeTextureParam(m_DenoiserCompute, m_DenoiserClearKernel, RWResolvedSpecularId, m_ResolvedSpecular);
                cmd.SetComputeTextureParam(m_DenoiserCompute, m_DenoiserClearKernel, RWSpecularAndSecondMomentId, m_CurrentSpecularHistory);
                cmd.SetComputeTextureParam(m_DenoiserCompute, m_DenoiserClearKernel, RWSpecularIndirectAccumulatedId, m_SpecularOutput);
                cmd.SetComputeTextureParam(m_DenoiserCompute, m_DenoiserClearKernel, RWNumFramesAccumulatedId, m_CurrentNumFramesHistory);
                cmd.SetComputeTextureParam(m_DenoiserCompute, m_DenoiserClearKernel, RWSceneDepthHistoryId, m_CurrentSceneDepthHistory);
                cmd.SetComputeBufferParam(m_DenoiserCompute, m_DenoiserClearKernel, RWPreExposureHistoryId, m_CurrentPreExposureHistory);
                cmd.DispatchCompute(m_DenoiserCompute, m_DenoiserClearKernel, m_ClearTileArgs, 0);

                // Resolve active tiles
                int resolveKernel = downsampled
                    ? (m_UseSpatialReconstruction && m_ResolveDownsampledSpatialKernel >= 0 ? m_ResolveDownsampledSpatialKernel : m_ResolveDownsampledKernel)
                    : (m_UseSpatialReconstruction && m_ResolveSpatialKernel >= 0 ? m_ResolveSpatialKernel : m_ResolveKernel);

                cmd.SetComputeIntParam(m_DenoiserCompute, NumSpatialReconstructionSamplesId, m_ReconstructionSamples);
                cmd.SetComputeFloatParam(m_DenoiserCompute, SpatialReconstructionKernelRadiusId, m_ReconstructionKernelRadius);
                cmd.SetComputeFloatParam(m_DenoiserCompute, SpatialReconstructionRoughnessScaleId, SpatialReconstructionRoughnessScale);
                cmd.SetComputeFloatParam(m_DenoiserCompute, SpatialReconstructionMinWeightId, SpatialReconstructionMinWeight);

                BindMaterialInputs(cmd, m_DenoiserCompute, resolveKernel, owner, layerAux0, layerAux1, hasDualSlabSidecar, black);
                cmd.SetComputeBufferParam(m_DenoiserCompute, resolveKernel, ResolveTileDataReadId, m_ResolveTileData);
                cmd.SetComputeTextureParam(m_DenoiserCompute, resolveKernel, DownsampledDepthReadId, m_DownsampledDepth);
                cmd.SetComputeTextureParam(m_DenoiserCompute, resolveKernel, RayBufferReadId, m_RayBuffer);
                cmd.SetComputeTextureParam(m_DenoiserCompute, resolveKernel, TraceRadianceReadId, m_TraceRadiance);
                cmd.SetComputeTextureParam(m_DenoiserCompute, resolveKernel, TraceHitReadId, m_TraceHit);
                cmd.SetComputeTextureParam(m_DenoiserCompute, resolveKernel, RWResolvedSpecularId, m_ResolvedSpecular);
                cmd.SetComputeTextureParam(m_DenoiserCompute, resolveKernel, RWResolvedSpecularDepthId, m_ResolvedSpecularDepth);
                cmd.SetComputeTextureParam(m_DenoiserCompute, resolveKernel, RWSceneDepthHistoryId, m_CurrentSceneDepthHistory);
                cmd.SetComputeBufferParam(m_DenoiserCompute, resolveKernel, RWPreExposureHistoryId, m_CurrentPreExposureHistory);
                cmd.DispatchCompute(m_DenoiserCompute, resolveKernel, m_ResolveTileArgs, 0);
            }

            // -----------------------------------------------------------------
            // Stage 5A: Temporal Accumulation (UE5 LumenReflectionDenoiserTemporalCS)
            // -----------------------------------------------------------------
            bool validHistory = m_HistoryValid && m_TemporalValidHistoryKernel >= 0;
            using (new ProfilingScope(cmd, TemporalSampler))
            {
                int temporalKernel = validHistory ? m_TemporalValidHistoryKernel : m_TemporalKernel;
                float invW = 1f / m_Width;
                float invH = 1f / m_Height;
                cmd.SetComputeVectorParam(m_DenoiserCompute, HistoryUVMinMaxId, new Vector4(0f, 0f, 1f, 1f));
                cmd.SetComputeVectorParam(m_DenoiserCompute, HistoryGatherUVMinMaxId,
                    new Vector4(0.51f * invW, 0.51f * invH, 1f - 0.51f * invW, 1f - 0.51f * invH));
                cmd.SetComputeVectorParam(m_DenoiserCompute, HistoryBufferSizeAndInvSizeId,
                    new Vector4(m_Width, m_Height, invW, invH));
                cmd.SetComputeFloatParam(m_DenoiserCompute, TemporalMaxFramesAccumulatedId,
                    m_UseTemporalFilter ? m_TemporalMaxFramesAccumulated : 1f);
                cmd.SetComputeFloatParam(m_DenoiserCompute, TemporalNeighborhoodClampScaleId, m_TemporalNeighborhoodClampScale);
                cmd.SetComputeFloatParam(m_DenoiserCompute, HistoryDistanceThresholdId, m_HistoryDistanceThreshold);

                BindMaterialInputs(cmd, m_DenoiserCompute, temporalKernel, owner, layerAux0, layerAux1, hasDualSlabSidecar, black);
                cmd.SetComputeBufferParam(m_DenoiserCompute, temporalKernel, ResolveTileDataReadId, m_ResolveTileData);
                cmd.SetComputeTextureParam(m_DenoiserCompute, temporalKernel, ResolvedSpecularLightingReadId, m_ResolvedSpecular);
                cmd.SetComputeTextureParam(m_DenoiserCompute, temporalKernel, RWSpecularAndSecondMomentId, m_CurrentSpecularHistory);
                cmd.SetComputeTextureParam(m_DenoiserCompute, temporalKernel, RWNumFramesAccumulatedId, m_CurrentNumFramesHistory);

                if (validHistory)
                {
                    cmd.SetComputeTextureParam(m_DenoiserCompute, temporalKernel, ResolvedReflectionsDepthReadId, m_ResolvedSpecularDepth);
                    cmd.SetComputeTextureParam(m_DenoiserCompute, temporalKernel, SpecularHistoryTextureId, m_PreviousSpecularHistory);
                    cmd.SetComputeTextureParam(m_DenoiserCompute, temporalKernel, NumFramesAccumulatedHistoryTextureId, m_PreviousNumFramesHistory);
                    cmd.SetComputeTextureParam(m_DenoiserCompute, temporalKernel, SceneDepthHistoryReadId, m_PreviousSceneDepthHistory);
                    cmd.SetComputeBufferParam(m_DenoiserCompute, temporalKernel, PrevPreExposureHistoryReadId, m_PreviousPreExposureHistory);
                    cmd.SetComputeTextureParam(m_DenoiserCompute, temporalKernel, MotionId, owner.MotionTextureOrFallback(black));
                }

                cmd.DispatchCompute(m_DenoiserCompute, temporalKernel, m_ResolveTileArgs, 0);
            }

            // -----------------------------------------------------------------
            // Stage 5B: Variance-Guided Bilateral Spatial Filter (UE5)
            // -----------------------------------------------------------------
            using (new ProfilingScope(cmd, SpatialSampler))
            {
                bool runSpatial = m_UseBilateralFilter && m_BilateralNumSamples > 0 && m_SpatialFilterKernel >= 0;
                int spatialKernel = runSpatial ? m_SpatialFilterKernel : m_SpatialBypassKernel;

                cmd.SetComputeFloatParam(m_DenoiserCompute, SpatialFilterDepthWeightScaleId, m_BilateralDepthWeightScale);
                cmd.SetComputeFloatParam(m_DenoiserCompute, SpatialFilterKernelRadiusId, m_BilateralKernelRadius);
                cmd.SetComputeIntParam(m_DenoiserCompute, SpatialFilterNumSamplesId, m_BilateralNumSamples);
                cmd.SetComputeFloatParam(m_DenoiserCompute, TemporalMaxFramesAccumulatedId,
                    m_UseTemporalFilter ? m_TemporalMaxFramesAccumulated : 1f);

                BindMaterialInputs(cmd, m_DenoiserCompute, spatialKernel, owner, layerAux0, layerAux1, hasDualSlabSidecar, black);
                cmd.SetComputeBufferParam(m_DenoiserCompute, spatialKernel, ResolveTileDataReadId, m_ResolveTileData);
                cmd.SetComputeTextureParam(m_DenoiserCompute, spatialKernel, SpecularLightingAndSecondMomentReadId, m_CurrentSpecularHistory);
                cmd.SetComputeTextureParam(m_DenoiserCompute, spatialKernel, NumFramesAccumulatedReadId, m_CurrentNumFramesHistory);
                cmd.SetComputeTextureParam(m_DenoiserCompute, spatialKernel, RWSpecularIndirectAccumulatedId, m_SpecularOutput);
                cmd.DispatchCompute(m_DenoiserCompute, spatialKernel, m_ResolveTileArgs, 0);
            }
        }

        internal bool RecordDebugView(
            ComputeCommandBuffer cmd,
            LumenIndirectDiffusePass owner,
            LumenDebugViewMode mode,
            TextureHandle output,
            int outputWidth,
            int outputHeight,
            float exposureMultiplier,
            int frame,
            TextureHandle layerAux0,
            TextureHandle layerAux1,
            bool hasDualSlabSidecar,
            TextureHandle black)
        {
            if (!m_Prepared || m_DebugReflectionKernel < 0 || !output.IsValid())
                return false;

            if (mode is not (LumenDebugViewMode.ReflectionView or LumenDebugViewMode.DedicatedReflectionRays or LumenDebugViewMode.Overview))
                return false;

            using var scope = new ProfilingScope(cmd, DebugSampler);
            BindMaterialInputs(cmd, m_DenoiserCompute, m_DebugReflectionKernel, owner, layerAux0, layerAux1, hasDualSlabSidecar, black);
            owner.BindOccludedSkyForReflections(cmd, m_DenoiserCompute, m_DebugReflectionKernel);
            cmd.SetComputeTextureParam(m_DenoiserCompute, m_DebugReflectionKernel, DebugReflectionTextureId, m_SpecularOutput);
            cmd.SetComputeTextureParam(m_DenoiserCompute, m_DebugReflectionKernel, DebugReflectionOutputId, output);
            cmd.SetComputeIntParam(m_DenoiserCompute, DebugReflectionModeId, (int)mode);
            cmd.SetComputeVectorParam(m_DenoiserCompute, DebugReflectionParamsId,
                new Vector4(exposureMultiplier, 1f, frame, mode == LumenDebugViewMode.Overview ? 1f : 0f));
            cmd.DispatchCompute(m_DenoiserCompute, m_DebugReflectionKernel,
                CoreUtils.DivRoundUp(outputWidth, TileSize),
                CoreUtils.DivRoundUp(outputHeight, TileSize), 1);
            return true;
        }

        private void DispatchCompaction(
            ComputeCommandBuffer cmd,
            BufferHandle tracingTileData,
            BufferHandle tracingTileArgs,
            bool cullByCameraDistance,
            float endDistanceFromCamera,
            float maxTraceDistance)
        {
            using var scope = new ProfilingScope(cmd, CompactSampler);

            cmd.SetComputeIntParam(m_TracingCompute, CompactionThreadGroupSizeId, CompactionThreadGroupSize);
            cmd.SetComputeBufferParam(m_TracingCompute, m_SetupCompactionArgsKernel, RWCompactedAllocatorId, m_CompactedAllocator);
            cmd.SetComputeBufferParam(m_TracingCompute, m_SetupCompactionArgsKernel, RWCompactionArgsId, m_CompactionArgs);
            cmd.SetComputeBufferParam(m_TracingCompute, m_SetupCompactionArgsKernel, TracingTileArgsReadId, tracingTileArgs);
            cmd.DispatchCompute(m_TracingCompute, m_SetupCompactionArgsKernel, 1, 1, 1);

            cmd.SetComputeIntParam(m_TracingCompute, CullByDistanceFromCameraId, cullByCameraDistance ? 1 : 0);
            cmd.SetComputeFloatParam(m_TracingCompute, CompactionTracingEndDistanceId, endDistanceFromCamera);
            cmd.SetComputeFloatParam(m_TracingCompute, CompactionMaxTraceDistanceId, maxTraceDistance);
            cmd.SetComputeTextureParam(m_TracingCompute, m_CompactTracesKernel, DownsampledDepthReadId, m_DownsampledDepth);
            cmd.SetComputeTextureParam(m_TracingCompute, m_CompactTracesKernel, TraceHitReadId, m_TraceHit);
            cmd.SetComputeBufferParam(m_TracingCompute, m_CompactTracesKernel, TracingTileDataReadId, tracingTileData);
            cmd.SetComputeBufferParam(m_TracingCompute, m_CompactTracesKernel, TracingTileArgsReadId, tracingTileArgs);
            cmd.SetComputeBufferParam(m_TracingCompute, m_CompactTracesKernel, RWCompactedAllocatorId, m_CompactedAllocator);
            cmd.SetComputeBufferParam(m_TracingCompute, m_CompactTracesKernel, RWCompactedDataId, m_CompactedData);
            cmd.DispatchCompute(m_TracingCompute, m_CompactTracesKernel, m_CompactionArgs, 0);

            cmd.SetComputeBufferParam(m_TracingCompute, m_SetupCompactedArgsKernel, CompactedAllocatorReadId, m_CompactedAllocator);
            cmd.SetComputeBufferParam(m_TracingCompute, m_SetupCompactedArgsKernel, RWCompactTracingArgsId, m_CompactTracingArgs);
            cmd.DispatchCompute(m_TracingCompute, m_SetupCompactedArgsKernel, 1, 1, 1);
        }

        private void BindCommonParameters(
            ComputeCommandBuffer cmd,
            ComputeShader shader,
            LumenIndirectDiffusePass owner,
            uint stateFrame,
            uint rayDirFrame,
            bool hasDualSlabSidecar,
            bool hasWorldCache,
            bool hasGlobalSdf,
            float maxTraceDistance,
            float maxMeshSdfTraceDistance)
        {
            owner.BindFrameConstantsForReflections(cmd, shader);
            cmd.SetComputeVectorParam(shader, ProbeCameraPositionId, owner.ProbeCameraPosition);
            cmd.SetComputeMatrixParam(shader, ViewProjectionId, owner.CurrentViewProjection);
            cmd.SetComputeMatrixParam(shader, PreviousViewProjectionId, owner.PreviousViewProjection);
            cmd.SetComputeVectorParam(shader, JitterDeltaId, owner.ScreenJitterDelta);

            cmd.SetComputeFloatParam(shader, MaxRoughnessToTraceId, m_MaxRoughnessToTrace);
            cmd.SetComputeFloatParam(shader, MaxRoughnessToTraceForFoliageId, m_MaxRoughnessToTraceForFoliage);
            cmd.SetComputeFloatParam(shader, InvRoughnessFadeLengthId, m_InvRoughnessFadeLength);
            cmd.SetComputeFloatParam(shader, SmoothBiasId, m_SmoothBias);

            SetInt2(cmd, shader, TracingBufferSizeId, m_TracingWidth, m_TracingHeight);
            cmd.SetComputeVectorParam(shader, TracingBufferInvSizeId, new Vector4(1f / m_TracingWidth, 1f / m_TracingHeight, 0f, 0f));
            SetInt2(cmd, shader, DownsampleFactorXYId, m_DownsampleFactor, m_DownsampleFactor);
            SetInt2(cmd, shader, TracingViewSizeId, m_TracingWidth, m_TracingHeight);

            cmd.SetComputeFloatParam(shader, MaxRayIntensityId, m_MaxRayIntensity);
            cmd.SetComputeIntParam(shader, StateFrameIndexId, unchecked((int)stateFrame));
            cmd.SetComputeIntParam(shader, StateFrameIndexMod8Id, (int)(stateFrame & 7u));
            cmd.SetComputeIntParam(shader, RayDirectionFrameIndexId, unchecked((int)rayDirFrame));
            cmd.SetComputeFloatParam(shader, GGXSamplingBiasId, m_GGXSamplingBias);
            cmd.SetComputeFloatParam(shader, MaxTraceDistanceId, maxTraceDistance);
            cmd.SetComputeFloatParam(shader, MaxMeshSdfTraceDistanceId, maxMeshSdfTraceDistance);
            cmd.SetComputeFloatParam(shader, DenoiserOneOverTonemapRangeId, m_DenoiserOneOverTonemapRange);
            cmd.SetComputeIntParam(shader, HasBlueNoiseId, m_BlueNoiseVec2.IsValid() ? 1 : 0);
            cmd.SetComputeIntParam(shader, HasDualSlabSidecarId, hasDualSlabSidecar ? 1 : 0);
            cmd.SetComputeIntParam(shader, UseWorldCacheId, hasWorldCache ? 1 : 0);
            cmd.SetComputeIntParam(shader, UseGlobalSdfId, hasGlobalSdf ? 1 : 0);
        }

        private static void BindMaterialInputs(
            ComputeCommandBuffer cmd,
            ComputeShader shader,
            int kernel,
            LumenIndirectDiffusePass owner,
            TextureHandle layerAux0,
            TextureHandle layerAux1,
            bool hasDualSlabSidecar,
            TextureHandle black)
        {
            owner.BindReceiverForReflections(cmd, shader, kernel);
            cmd.SetComputeTextureParam(shader, kernel, LayerAux0Id, hasDualSlabSidecar && layerAux0.IsValid() ? layerAux0 : black);
            cmd.SetComputeTextureParam(shader, kernel, LayerAux1Id, hasDualSlabSidecar && layerAux1.IsValid() ? layerAux1 : black);
        }

        private void SetInt2(ComputeCommandBuffer cmd, ComputeShader shader, int propertyId, int x, int y)
        {
            m_ScratchInt2[0] = x;
            m_ScratchInt2[1] = y;
            cmd.SetComputeIntParams(shader, propertyId, m_ScratchInt2);
        }

        private static TextureHandle CreateSubPassTransientTexture(
            IComputeRenderGraphBuilder builder,
            RenderGraphTexture desc)
        {
            TextureDesc textureDesc = desc.desc;
            return builder.CreateTransientTexture(in textureDesc);
        }

        private static BufferHandle CreateSubPassTransientBuffer(
            IComputeRenderGraphBuilder builder,
            string name,
            int count,
            int stride,
            GraphicsBuffer.Target target)
        {
            var bufferDesc = new BufferDesc(count, stride, target)
            {
                name = name,
            };
            return builder.CreateTransientBuffer(in bufferDesc);
        }

        private static int FindKernel(ComputeShader shader, string name)
        {
            if (shader == null || !shader.HasKernel(name))
                return -1;
            return shader.FindKernel(name);
        }

        private static RenderGraphTexture CreateTarget(string name, GraphicsFormat format)
        {
            var result = RenderGraphTexture.CreateOutput(name, format);
            result.desc.EnableRandomWrite = true;
            result.desc.FilterMode = FilterMode.Point;
            return result;
        }
    }
}
