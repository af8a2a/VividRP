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
        internal RenderGraphTexture Output;
        internal bool SkyHandled;
        internal bool RealtimeOnly;
        internal SdfDiffuseGatherPass OpenPbrSource;

        public override void Reset()
        {
            Output = null;
            SkyHandled = false;
            RealtimeOnly = false;
            OpenPbrSource = null;
        }
    }

    public sealed class SdfDiffuseGatherPass : ComputePass, IRenderGraphPreparePass
    {
#if UNITY_EDITOR
        internal static System.Action<ComputePassContext, Texture, Texture, Texture> EditorTraceCapture;
#endif
        private const int ProbeSpacing = 16; // Keep in sync with SdfScreenProbeCommon.hlsl.
        private const int RestirPixelSpacing = 2;
        private const int RaysPerProbe = 8;
        private const int DirectionalProbePlanes = 4; // RGB baseline and three world-space tangent moments.
        private const int DirectionalIrradiancePlanes = 7;
        private const int DirectionalGatherBins = 16; // 4x4 cosine-octahedral chart.
        private const int GroupSize = 8;
        private const GraphicsFormat ProbeFormat = GraphicsFormat.R16G16B16A16_SFloat;
        private const GraphicsFormat GeometryFormat = GraphicsFormat.R32G32B32A32_SFloat;
        private const GraphicsFormat PixelGeometryFormat = GraphicsFormat.R32_UInt;
        private const GraphicsFormat SunVisibilityFormat = GraphicsFormat.R16_SFloat;
        private const int SunAtlasTexelLimit = 4 * 1024 * 1024;
        private const int SunUpdateTexelBudget = 16 * 1024;
        private const int RadiosityCardSize = 8; // One compute group per card.
        private const int RadiosityColumns = 16;
        private const int RadiosityCardLimit = 4096;
        private const int RadiosityCardsPerFrame = 16;
        // Keep the grid and directional resolution in sync with SdfRadianceCacheCommon.hlsl.
        private const int FarGridSize = 16;
        private const int FarDirectionCount = 64;
        private const int FarProbeCount = FarGridSize * FarGridSize * FarGridSize;
        private const int FarProbesPerFrame = 64;
        private const float FarCellSize = 2.5f;
        private static readonly CameraHistoryId ProbeHistoryId = CameraHistoryId.Create("SdfDiffuseProbeHistory");
        private static readonly CameraHistoryId GeometryHistoryId = CameraHistoryId.Create("SdfDiffuseGeometryHistory");
        private static readonly CameraHistoryId DirectionalHistoryId = CameraHistoryId.Create("SdfDirectionalCompositeHistory");
        private static readonly CameraHistoryId DirectionalGuideHistoryId = CameraHistoryId.Create("SdfDirectionalFilteredHistory");
        private static readonly CameraHistoryId RestirHistoryId = CameraHistoryId.Create("SdfDiffuseRestirHistory");
        private static readonly CameraHistoryId PixelHistoryId = CameraHistoryId.Create("SdfDiffusePixelHistoryWithCoverage");
        private static readonly CameraHistoryId PixelGeometryHistoryId = CameraHistoryId.Create("SdfDiffusePixelGeometryHistory");
        private static readonly CameraHistoryId ScreenRadianceHistoryId = CameraHistoryId.Create("SdfDiffuseScreenRadianceHistory");
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
        private static readonly int DirectionalCacheEnabledId = Shader.PropertyToID("_SdfDirectionalCacheEnabled");
        private static readonly int DirectionalGuideEnabledId = Shader.PropertyToID("_SdfDirectionalGuideEnabled");
        private static readonly int DirectionalWorldGuideId = Shader.PropertyToID("_SdfDirectionalWorldGuide");
        private static readonly int DirectionalHasHistoryId = Shader.PropertyToID("_SdfDirectionalHasHistory");
        private static readonly int DirectionalGuideId = Shader.PropertyToID("_SdfDirectionalGuide");
        private static readonly int DirectionalGuideOutputId = Shader.PropertyToID("_SdfDirectionalGuideOutput");
        private static readonly int DirectionalRaysId = Shader.PropertyToID("_SdfDirectionalRays");
        private static readonly int DirectionalPreviousId = Shader.PropertyToID("_SdfDirectionalPrevious");
        private static readonly int DirectionalCompositeId = Shader.PropertyToID("_SdfDirectionalComposite");
        private static readonly int DirectionalCompositeOutputId = Shader.PropertyToID("_SdfDirectionalCompositeOutput");
        private static readonly int DirectionalTemporalId = Shader.PropertyToID("_SdfDirectionalTemporal");
        private static readonly int DirectionalTemporalOutputId = Shader.PropertyToID("_SdfDirectionalTemporalOutput");
        private static readonly int DirectionalFilteredId = Shader.PropertyToID("_SdfDirectionalFiltered");
        private static readonly int DirectionalFilteredOutputId = Shader.PropertyToID("_SdfDirectionalFilteredOutput");
        private static readonly ProfilingSampler RestirTemporalSampler = new("ReSTIR Gather Temporal");
        private static readonly ProfilingSampler RestirSpatialSampler = new("ReSTIR Gather Spatial");
        private static readonly ProfilingSampler RestirDenoiseSampler = new("ReSTIR Variance Bilateral");
        private static readonly ProfilingSampler ResolveSampler = new("Screen Probe Resolve");
        private static readonly ProfilingSampler PixelTemporalSampler = new("Screen Probe Pixel Temporal");
        private static readonly ProfilingSampler SunUpdateSampler = new("Surface Cache Sun Update");
        private static readonly CameraHistoryId SunHistoryId = CameraHistoryId.Create("SdfSurfaceSunVisibility");
        private static readonly int SunLayoutId = Shader.PropertyToID("_SdfSunLayout");
        private static readonly int SunInstancesId = Shader.PropertyToID("_SdfSunInstances");
        private static readonly int SunFirstCardId = Shader.PropertyToID("_SdfSunFirstCard");
        private static readonly int SunReadyCardsId = Shader.PropertyToID("_SdfSunReadyCards");
        private static readonly int SunVisibilityId = Shader.PropertyToID("_SdfSunVisibility");
        private static readonly int SunVisibilityOutputId = Shader.PropertyToID("_SdfSunVisibilityOutput");
        private static readonly int SunCacheHitsId = Shader.PropertyToID("_SdfSunCacheHits");
        private static readonly ProfilingSampler RadiositySampler = new("Surface Cache Radiosity");
        private static readonly CameraHistoryId RadiosityAId = CameraHistoryId.Create("SdfRadiosityA");
        private static readonly CameraHistoryId RadiosityBId = CameraHistoryId.Create("SdfRadiosityB");
        private static readonly int RadiosityWorkId = Shader.PropertyToID("_SdfRadiosityWork");
        private static readonly int RadiosityLayoutId = Shader.PropertyToID("_SdfRadiosityLayout");
        private static readonly int RadiosityFirstCardId = Shader.PropertyToID("_SdfRadiosityFirstCard");
        private static readonly int RadiosityInputId = Shader.PropertyToID("_SdfRadiosityInput");
        private static readonly int RadiosityOutputId = Shader.PropertyToID("_SdfRadiosityOutput");
        private static readonly CameraHistoryId FarHistoryId = CameraHistoryId.Create("SdfFarRadiance");
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
        private static readonly int GlobalNearId = Shader.PropertyToID("_GlobalSdfNear");
        private static readonly int GlobalMiddleId = Shader.PropertyToID("_GlobalSdfMiddle");
        private static readonly int GlobalFarId = Shader.PropertyToID("_GlobalSdfFar");
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
        private static readonly int HzbId = Shader.PropertyToID("_SdfScreenHzb");
        private static readonly int MotionId = Shader.PropertyToID("_SdfScreenMotion");
        private static readonly int ScreenRadianceId = Shader.PropertyToID("_SdfScreenPreviousRadiance");
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
        private static readonly int HasHistoryId = Shader.PropertyToID("_SdfDiffuseHasHistory");
        private static readonly int DirectionalProbesId = Shader.PropertyToID("_SdfDiffuseDirectionalProbes");
        private static readonly int AdaptiveProbesId = Shader.PropertyToID("_SdfDiffuseAdaptiveProbes");
        private static readonly int SpatialFilterId = Shader.PropertyToID("_SdfDiffuseSpatialFilter");
        private static readonly int ProbeCameraForwardId = Shader.PropertyToID("_SdfProbeCameraForward");
        private static readonly int PlacementId = Shader.PropertyToID("_SdfAdaptivePlacement");
        private static readonly int PlacementOutputId = Shader.PropertyToID("_SdfAdaptivePlacementOutput");
        private static readonly int ProbeInputId = Shader.PropertyToID("_SdfDiffuseProbeInput");
        private static readonly int FilteredProbesId = Shader.PropertyToID("_SdfDiffuseFilteredProbes");
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
        private static readonly int RestirVarianceOutputId = Shader.PropertyToID("_SdfRestirVarianceOutput");
        private static readonly int RestirFilterInputId = Shader.PropertyToID("_SdfRestirFilterInput");
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

        [RenderGraphResource(Name = "Surface Cache Sun Hits", Access = AccessFlags.Write)]
        private RenderGraphTexture m_SunCacheHits = CreateOutput("Surface Cache Sun Hits", ProbeFormat);

        [SerializeField, Min(0.1f)] private float m_MaxDistance = 12f;
        [SerializeField, Min(0.001f)] private float m_RayBias = 0.04f;
        [SerializeField, Min(1f)] private float m_SunShadowDistance = 64f;

        private sealed class HistoryState : CameraRelativeState
        {
            internal Matrix4x4 ViewProjection;
            internal VividSdfSurfaceCache Cache;
            internal bool OffscreenOnly;
            internal SdfDiffuseTraceMode TraceMode;
            internal bool HasScreenRays;
            internal bool DirectionalProbes;
            internal bool AdaptiveProbes, SpatialFilter, HitDistanceFilter;
            internal bool RestirGather;
            internal bool RestirPixelIntegration;
            internal bool RestirOpenPbrDiffuse;
            internal ulong RestirCardVersion;
            internal bool PixelTemporal;
            internal bool CorrectReceiverSelfHits;
            internal bool ScreenCardLighting;
            internal bool DirectionalCache, DirectionalGuide, DirectionalHierarchicalGuide;
            internal float MeshDetailDistance;
            internal bool GlobalClipmapUsed;
            internal Vector4 NearOcclusion;
            internal Vector2 ScreenJitterUv;
            internal Vector4 LightPositionRange, LightColorShadow;
            internal uint SceneRevision;
            internal int Width, Height, LayerMask, LightLayerMask;
            internal float MaxDistance, RayBias;
            internal float SkyTraceDistance;
            internal int SkyContentHash;
            internal Vector4 SkyTextureParams, SkyTextureTint;
            internal uint Frame;
            internal bool SunConfigured;
            internal ulong SunCardVersion;
            internal int SunReadyCards;
            internal ulong RadiosityCardVersion;
            internal int RadiosityTargetOrders, RadiosityOrder, RadiosityCursor, GatherRadiosityOrder;
            internal Vector3Int[] FarKeys;
            internal Vector3Int FarWindow;
            internal bool FarPending;
            internal float FarDistance;
            internal ulong FarCardVersion;

            public override void Dispose() { }
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
        private int m_ReceiverTraceKernel = -1, m_ReceiverCachedKernel = -1, m_ReceiverCachedSunKernel = -1;
        private readonly RenderGraphTexture m_PreviousProbes = RenderGraphTexture.CreateInput("Previous SDF Diffuse Probes", ProbeFormat);
        private readonly RenderGraphTexture m_PreviousDirectional = RenderGraphTexture.CreateInput("Previous Screen Probe Composited Radiance", ProbeFormat);
        private readonly RenderGraphTexture m_CurrentDirectional = CreateScratch("Screen Probe Composited Radiance", ProbeFormat);
        private readonly RenderGraphTexture m_PreviousDirectionalFiltered = RenderGraphTexture.CreateInput("Previous Screen Probe Filtered Radiance", ProbeFormat);
        private readonly RenderGraphTexture m_CurrentDirectionalFiltered = CreateScratch("Screen Probe Filtered Radiance History", ProbeFormat);
        [RenderGraphResource(Name = "Temporal Probe Radiance", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_DirectionalTemporal = CreateScratch("Screen Probe Temporal Radiance", ProbeFormat);
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
        [RenderGraphResource(Name = "Raw Resolved Diffuse GI", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_RawResolved = CreateScratch("SDF Diffuse Raw Resolved", ProbeFormat);
        [RenderGraphResource(Name = "Spatial ReSTIR Reservoirs", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_SpatialRestir = CreateScratch("Spatial ReSTIR Reservoirs", GeometryFormat);
        [RenderGraphResource(Name = "ReSTIR Resolve Variance", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_RestirVariance = CreateScratch("ReSTIR Resolve Variance", GraphicsFormat.R16_SFloat);
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
        private readonly RenderGraphTexture m_LocalMotion = RenderGraphTexture.CreateInput("Screen Trace Motion", GraphicsFormat.R16G16_SFloat);
        private readonly RenderGraphTexture m_ScreenRadianceDesc = CreateOutput("Screen Diffuse Source Radiance", ProbeFormat);
        private readonly RenderGraphTexture m_ScreenGeometryDesc = CreateOutput("Screen Diffuse Source Geometry", GraphicsFormat.R32G32_SFloat);
        private readonly RenderGraphTexture m_SunHistoryDesc = CreateOutput("Surface Cache Sun Visibility", SunVisibilityFormat);
        private readonly RenderGraphTexture m_RadiosityDesc = CreateOutput("Surface Cache Radiosity", ProbeFormat);
        private readonly RenderGraphTexture m_FarDesc = CreateOutput("World Diffuse Far Radiance", GeometryFormat);
        [RenderGraphResource(Name = "Screen Ray Samples", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_ScreenRays = CreateOutput("Diffuse GI Screen Rays", ProbeFormat);
        private VividMeshSdfGpuScene m_Scene;
        private ComputeShader m_TraceCompute;
        private ComputeShader m_GatherCompute;
        private ComputeShader m_ImportanceSamplingCompute;
        private ComputeShader m_FilterCompute;
        private ComputeShader m_GatherTemporalCompute;
        private ComputeShader m_RestirCompute;
        private ComputeShader m_SunCompute;
        private ComputeShader m_RadiosityCompute;
        private ComputeShader m_FarCompute;
        private ComputeShader m_NearOcclusionCompute;
        private CameraHistoryTexture m_ProbeHistory, m_GeometryHistory;
        private CameraHistoryTexture m_DirectionalHistory, m_DirectionalGuideHistory;
        private int m_DirectionalGuideKernel = -1, m_DirectionalCompositeKernel = -1, m_DirectionalTemporalKernel = -1;
        private int m_DirectionalFilterKernel = -1, m_DirectionalIntegrateKernel = -1, m_DirectionalResolveKernel = -1;
        private bool m_DirectionalCache, m_DirectionalGuide, m_DirectionalHistoryValid, m_DirectionalGuideHistoryValid;
        private bool m_DirectionalHierarchicalGuide;
        private CameraHistoryTexture m_RestirHistory;
        private CameraHistoryTexture m_PixelHistory, m_PixelGeometryHistory;
        private CameraHistoryTexture m_SunHistory;
        private CameraHistoryTexture m_RadiosityReadHistory, m_RadiosityWriteHistory;
        private TextureHandle m_RadiosityInput, m_RadiosityOutput;
        private int m_RadiosityKernel = -1;
        private int m_RadiosityTargetOrders, m_RadiosityOrder, m_RadiosityCursor, m_RadiosityUpdateCards;
        private ulong m_RadiosityCardVersion;
        private readonly Vector4[] m_FarUpdates = new Vector4[FarProbesPerFrame];
        private CameraHistoryTexture m_FarHistory;
        private CameraHistoryBuffer m_FarHitIdsHistory;
        private TextureHandle m_FarAtlas;
        private BufferHandle m_FarHitIds;
        private VividGlobalSdfFrameData m_GlobalClipmap;
        private TextureHandle m_GlobalNear, m_GlobalMiddle, m_GlobalFar;
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
        private TextureHandle m_SunAtlas;
        private Vector2Int m_SunCardSize;
        private Vector4 m_SunLayout;
        private ulong m_SunCardVersion;
        private int m_SunReadyCards, m_SunUpdateCards;
        private Vector2 m_ScreenJitterUv, m_ScreenJitterDelta;
        private Vector4 m_LightPositionRange, m_LightColorShadow;
        private int m_TraceKernel = -1, m_CachedTraceKernel = -1, m_ResolveKernel = -1;
        private int m_ScreenTraceKernel = -1, m_ScreenGatherKernel = -1;
        private int m_CachedSunTraceKernel = -1, m_SunUpdateKernel = -1;
        private int m_PlacementKernel = -1, m_FilterKernel = -1, m_HitFilterKernel = -1;
        private int m_RestirTemporalKernel = -1, m_RestirSpatialKernel = -1;
        private int m_RestirDenoiseKernel = -1;
        private int m_PixelTemporalKernel = -1, m_RestirPixelTemporalKernel = -1;
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
        private bool m_RestirGather;
        private bool m_RestirPixelIntegration;
        private ulong m_RestirCardVersion;
        private bool m_RecordRayDiagnostics;
        private bool m_RecordFirstHitDiagnostics;
        private SdfDiffuseTraceMode m_TraceMode;

        public SdfDiffuseGatherPass()
        {
            profilingSampler = new ProfilingSampler(nameof(SdfDiffuseGatherPass));
            m_ScreenHzb = m_LocalHzb;
            m_ScreenMotion = m_LocalMotion;
            m_ScreenRays.desc.ClearBuffer = false;
        }

        private static bool HasKernel(ComputeShader shader, string name) => shader != null && shader.HasKernel(name);

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_TraceCompute = resources?.SdfScreenProbeTracingCompute;
            m_GatherCompute = resources?.SdfDiffuseGatherCompute;
            m_ImportanceSamplingCompute = resources?.SdfScreenProbeImportanceSamplingCompute;
            m_FilterCompute = resources?.SdfScreenProbeFilteringCompute;
            m_GatherTemporalCompute = resources?.SdfScreenProbeGatherTemporalCompute;
            m_RestirCompute = resources?.SdfReSTIRGatherCompute;
            m_SunCompute = resources?.SdfSceneDirectLightingCompute;
            m_RadiosityCompute = resources?.SdfRadiosityCompute;
            m_FarCompute = resources?.SdfRadianceCacheUpdateCompute;
            m_NearOcclusionCompute = resources?.SdfShortRangeAOCompute;
            if (m_TraceCompute == null || !SystemInfo.supportsComputeShaders
                || !SystemInfo.IsFormatSupported(ProbeFormat, GraphicsFormatUsage.LoadStore)
                || !SystemInfo.IsFormatSupported(GeometryFormat, GraphicsFormatUsage.LoadStore)
                || !HasKernel(m_TraceCompute, "TraceSdfDiffuseProbes") || !HasKernel(m_GatherCompute, "ResolveSdfDiffuseGI"))
                return;

            m_TraceKernel = m_TraceCompute.FindKernel("TraceSdfDiffuseProbes");
            if (HasKernel(m_TraceCompute, "TraceSdfReceiverProbes"))
                m_ReceiverTraceKernel = m_TraceCompute.FindKernel("TraceSdfReceiverProbes");
            if (HasKernel(m_TraceCompute, "TraceSdfReceiverCachedProbes"))
                m_ReceiverCachedKernel = m_TraceCompute.FindKernel("TraceSdfReceiverCachedProbes");
            if (HasKernel(m_TraceCompute, "TraceSdfReceiverCachedSunProbes"))
                m_ReceiverCachedSunKernel = m_TraceCompute.FindKernel("TraceSdfReceiverCachedSunProbes");
            if (HasKernel(m_RadiosityCompute, "UpdateSdfSurfaceRadiosity")
                && SystemInfo.IsFormatSupported(ProbeFormat, GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample))
                m_RadiosityKernel = m_RadiosityCompute.FindKernel("UpdateSdfSurfaceRadiosity");
            if (SystemInfo.IsFormatSupported(GeometryFormat, GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample)
                && HasKernel(m_FarCompute, "UpdateSdfFarRadiance"))
                m_FarKernel = m_FarCompute.FindKernel("UpdateSdfFarRadiance");
            if (SystemInfo.IsFormatSupported(ProbeFormat, GraphicsFormatUsage.LoadStore)
                && HasKernel(m_NearOcclusionCompute, "ApplySdfNearOcclusion"))
                m_NearOcclusionKernel = m_NearOcclusionCompute.FindKernel("ApplySdfNearOcclusion");
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
                if (HasKernel(m_GatherTemporalCompute, "TemporalSdfRestirDiffuseGI"))
                    m_RestirPixelTemporalKernel = m_GatherTemporalCompute.FindKernel("TemporalSdfRestirDiffuseGI");
            }
            if (SystemInfo.IsFormatSupported(GraphicsFormat.R32_SFloat, GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample)
                && HasKernel(m_GatherCompute, "PlaceSdfAdaptiveProbes"))
                m_PlacementKernel = m_GatherCompute.FindKernel("PlaceSdfAdaptiveProbes");
            if (HasKernel(m_FilterCompute, "FilterSdfDiffuseProbes"))
                m_FilterKernel = m_FilterCompute.FindKernel("FilterSdfDiffuseProbes");
            if (HasKernel(m_FilterCompute, "FilterSdfDirectionalHits"))
                m_HitFilterKernel = m_FilterCompute.FindKernel("FilterSdfDirectionalHits");
            if (HasKernel(m_ImportanceSamplingCompute, "PrepareSdfDirectionalGuide")
                && HasKernel(m_FilterCompute, "ScreenProbeCompositeTracesWithScatterCS")
                && HasKernel(m_FilterCompute, "ScreenProbeTemporallyAccumulateTraceRadianceCS")
                && HasKernel(m_FilterCompute, "ScreenProbeFilterGatherTracesCS")
                && HasKernel(m_FilterCompute, "ScreenProbeConvertToIrradianceCS")
                && HasKernel(m_GatherCompute, "ResolveSdfProbeIrradiance"))
            {
                m_DirectionalGuideKernel = m_ImportanceSamplingCompute.FindKernel("PrepareSdfDirectionalGuide");
                m_DirectionalCompositeKernel = m_FilterCompute.FindKernel("ScreenProbeCompositeTracesWithScatterCS");
                m_DirectionalTemporalKernel = m_FilterCompute.FindKernel("ScreenProbeTemporallyAccumulateTraceRadianceCS");
                m_DirectionalFilterKernel = m_FilterCompute.FindKernel("ScreenProbeFilterGatherTracesCS");
                m_DirectionalIntegrateKernel = m_FilterCompute.FindKernel("ScreenProbeConvertToIrradianceCS");
                m_DirectionalResolveKernel = m_GatherCompute.FindKernel("ResolveSdfProbeIrradiance");
            }
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
            if (m_CachedTraceKernel >= 0
                && SystemInfo.IsFormatSupported(SunVisibilityFormat, GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample)
                && HasKernel(m_SunCompute, "UpdateSdfSurfaceSunVisibility") && HasKernel(m_TraceCompute, "TraceSdfDiffuseCachedSunProbes"))
            {
                m_SunUpdateKernel = m_SunCompute.FindKernel("UpdateSdfSurfaceSunVisibility");
                m_CachedSunTraceKernel = m_TraceCompute.FindKernel("TraceSdfDiffuseCachedSunProbes");
            }
            if (SystemInfo.usesReversedZBuffer
                && SystemInfo.IsFormatSupported(GraphicsFormat.R32G32_SFloat, GraphicsFormatUsage.LoadStore)
                && HasKernel(m_TraceCompute, "TraceDiffuseScreenRays") && HasKernel(m_TraceCompute, "GatherScreenDiffuseProbes"))
            {
                m_ScreenTraceKernel = m_TraceCompute.FindKernel("TraceDiffuseScreenRays");
                m_ScreenGatherKernel = m_TraceCompute.FindKernel("GatherScreenDiffuseProbes");
            }
            m_Scene = new VividMeshSdfGpuScene(Shader.Find("VividRP/Material/StandardLit"));
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var camera = cameraData.camera;
            m_State = null;
            m_ReceiverVisibilitySource = null;
            m_ReceiverVisibility = default;
            m_CorrectReceiverSelfHits = m_ScreenCardLightingRequested = false;
            m_ProbeHistory = m_GeometryHistory = null;
            m_DirectionalHistory = m_DirectionalGuideHistory = null;
            m_DirectionalHistoryValid = m_DirectionalGuideHistoryValid = false;
            m_RestirHistory = null;
            m_PixelHistory = m_PixelGeometryHistory = null;
            m_SunHistory = null;
            m_SunAtlas = default;
            m_RadiosityReadHistory = m_RadiosityWriteHistory = null;
            m_RadiosityInput = m_RadiosityOutput = default;
            m_RadiosityTargetOrders = m_RadiosityOrder = m_RadiosityCursor = m_RadiosityUpdateCards = 0;
            m_RadiosityCardVersion = 0;
            m_FarHistory = null;
            m_FarHitIdsHistory = null;
            m_FarAtlas = default;
            m_FarHitIds = default;
            m_GlobalClipmap = null;
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
            m_SunReadyCards = m_SunUpdateCards = 0;
            m_HasScreenHistory = false;
            m_ScreenJitterUv = default;
            m_Width = CameraDimensionUtility.ResolveCameraDimension(cameraData.actualWidth, cameraData.pixelWidth, Screen.width);
            m_Height = CameraDimensionUtility.ResolveCameraDimension(cameraData.actualHeight, cameraData.pixelHeight, Screen.height);
            var settings = VolumeManager.instance.stack.GetComponent<SdfDiffuseGISettings>();
            bool useSettings = settings != null && settings.active && settings.enabled.value;
            m_AdaptiveProbes = useSettings && m_PlacementKernel >= 0 && settings.adaptiveScreenProbes.value;
            m_SpatialFilter = useSettings && m_FilterKernel >= 0 && settings.screenProbeSpatialFilter.value;
            m_HitDistanceFilter = m_SpatialFilter && m_HitFilterKernel >= 0 && settings.screenProbeHitDistanceFilter.value;
            m_Output.Resize(m_Width, m_Height);
            m_RecordRayDiagnostics = useSettings && settings.recordRayDiagnostics.value;
            m_RecordFirstHitDiagnostics = m_RecordRayDiagnostics && settings.recordFirstHitDiagnostics.value;
            bool motionConnected = m_ScreenMotion != null && !ReferenceEquals(m_ScreenMotion, m_LocalMotion);
            bool pixelTemporal = useSettings && settings.pixelTemporal.value && motionConnected && m_PixelTemporalKernel >= 0;
            m_SunCacheHits.Resize(1, 1);
            if (m_TraceKernel < 0 || camera == null || camera.cullingMask == 0 || camera.stereoEnabled
                || DebugPassCameraUtility.ShouldSkipExecution(camera))
                return;

            if (settings != null && settings.active && !settings.enabled.value)
                return;
            var cache = settings != null && settings.active ? settings.surfaceCache.value : null;
            m_RealtimeOnly = settings != null && settings.active && settings.realtimeOnly.value;
            m_OffscreenOnly = settings != null && settings.active && settings.offscreenBounceOnly.value;
            m_TraceMode = settings != null && settings.active ? settings.traceMode.value : SdfDiffuseTraceMode.SdfOnly;
            m_DirectionalProbes = settings != null && settings.active && settings.directionalScreenProbes.value;
            m_UseSdf = m_TraceMode != SdfDiffuseTraceMode.ScreenOnly
                && (cache == null || (m_CachedTraceKernel >= 0 && cache.IsUsable));
            if (m_UseSdf)
            {
                m_Scene.SurfaceCache = cache;
                m_Scene.Update(VividMeshletRendererDatabase.instance);
                m_UseSdf = m_Scene.NodeCount != 0 && m_Scene.DiffuseSurfaces != null;
                if (m_UseSdf && cache != null)
                {
                    m_CardAlbedoDepth = PassRecorder.ImportTextureForPass(this, cache.AlbedoDepthHandle);
                    m_CardNormalCoverage = PassRecorder.ImportTextureForPass(this, cache.NormalCoverageHandle);
                    m_UseSdf = m_CardAlbedoDepth.IsValid() && m_CardNormalCoverage.IsValid();
                }
            }

            var temporal = frameData.Get<VividTemporalData>();
            bool screenConfigured = !m_OffscreenOnly && m_TraceMode != SdfDiffuseTraceMode.SdfOnly
                && m_ScreenTraceKernel >= 0 && m_ScreenHzb != null && motionConnected
                && !ReferenceEquals(m_ScreenHzb, m_LocalHzb);
            if (!m_UseSdf && !screenConfigured)
                return;
            m_CorrectReceiverSelfHits = m_UseSdf && useSettings && settings.correctReceiverSelfHits.value;
            m_ScreenCardLightingRequested = m_UseSdf && screenConfigured && useSettings && cache != null
                && settings.screenHitCardLighting.value && settings.surfaceCacheBounceOrders.value > 0;

            bool canRestir = useSettings && settings.restirGather.value && m_RestirTemporalKernel >= 0
                && m_UseSdf && !m_OffscreenOnly;
            m_RestirPixelIntegration = canRestir && settings.restirPixelIntegration.value && m_RestirDenoiseKernel >= 0
                && 4 * CoreUtils.DivRoundUp(m_Width, RestirPixelSpacing) <= SystemInfo.maxTextureSize
                && 2 * CoreUtils.DivRoundUp(m_Height, RestirPixelSpacing) <= SystemInfo.maxTextureSize;
            m_RestirGather = canRestir && (m_TraceMode == SdfDiffuseTraceMode.SdfOnly || m_RestirPixelIntegration);
            m_RestirOpenPbrDiffuse = m_RestirPixelIntegration && m_OpenPbrResolveKernel >= 0
                && settings.restirOpenPbrDiffuse.value && CoversReceiver(m_OpenPbrParameters)
                && CoversReceiver(m_OpenPbrAux0) && CoversReceiver(m_OpenPbrAux1)
                && PipelineResourceManager.Get<VividRPCoreResources>()?.OpenPBRLuts?.IsReady == true;
            m_AdaptiveProbes &= !m_RestirPixelIntegration;
            m_DirectionalProbes &= !m_RestirPixelIntegration;
            m_HitDistanceFilter &= !m_RestirGather;
            int spacing = m_RestirPixelIntegration ? RestirPixelSpacing : ProbeSpacing;
            m_ProbeWidth = CoreUtils.DivRoundUp(m_Width, spacing);
            m_ProbeHeight = CoreUtils.DivRoundUp(m_Height, spacing);
            m_AtlasHeight = m_ProbeHeight * (m_AdaptiveProbes ? 2 : 1);
            m_DirectionalCache = useSettings && m_DirectionalProbes && !m_RestirGather && !m_OffscreenOnly
                && m_DirectionalCompositeKernel >= 0 && settings.screenProbeDirectionalCache.value
                && DirectionalGatherBins * m_ProbeWidth <= SystemInfo.maxTextureSize && 2 * m_AtlasHeight <= SystemInfo.maxTextureSize;
            if (useSettings && m_NearOcclusionKernel >= 0 && !m_OffscreenOnly
                && float.IsFinite(settings.nearOcclusionStrength.value) && float.IsFinite(settings.nearOcclusionRadius.value))
            {
                float strength = settings.nearOcclusionStrength.value;
                bool directional = strength > 0f && settings.directionalNearOcclusion.value;
                m_NearOcclusion = new Vector4(settings.nearOcclusionRadius.value, strength, directional ? 1f : 0f, 0f);
                m_DirectionalProbes |= directional && !m_RestirPixelIntegration;
            }
            m_DirectionalGuide = m_DirectionalCache && settings.screenProbeSamplingGuide.value;
            m_DirectionalHierarchicalGuide = m_DirectionalGuide && settings.screenProbeHierarchicalGuide.value;
            m_HitDistanceFilter &= !m_DirectionalCache;
            int directionalWidth = m_DirectionalCache ? RaysPerProbe * m_ProbeWidth : 1;
            m_DirectionalGuideTexture.Resize(directionalWidth, m_DirectionalCache ? m_AtlasHeight : 1);
            m_DirectionalRayTexture.Resize(directionalWidth, m_DirectionalCache ? 2 * m_AtlasHeight : 1);
            m_DirectionalTemporal.Resize(m_DirectionalCache ? DirectionalGatherBins * m_ProbeWidth : 1,
                m_DirectionalCache ? m_AtlasHeight : 1);
            int rayCount = m_RestirPixelIntegration ? 1 : RaysPerProbe;
            m_HitLighting.Resize(m_ProbeWidth, m_AtlasHeight);
            m_TraceSources.Resize(m_ProbeWidth, m_AtlasHeight);
            m_RayDiagnostics.Resize(m_RecordRayDiagnostics ? m_ProbeWidth * rayCount : 1,
                m_RecordRayDiagnostics ? m_AtlasHeight : 1);
            m_FirstHitDiagnostics.Resize(m_RecordFirstHitDiagnostics ? m_ProbeWidth * rayCount : 1,
                m_RecordFirstHitDiagnostics ? 3 * m_AtlasHeight : 1);
            m_AdaptivePlacement.Resize(m_AdaptiveProbes ? m_ProbeWidth : 1, m_AdaptiveProbes ? m_ProbeHeight : 1);
            m_ScreenRays.Resize(screenConfigured ? m_ProbeWidth * rayCount : 1, screenConfigured ? m_AtlasHeight : 1);
            bool rawResolved = pixelTemporal || m_RestirPixelIntegration;
            m_RawResolved.Resize(rawResolved ? m_Width : 1, rawResolved ? m_Height : 1);
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

            if ((m_CorrectReceiverSelfHits || m_ScreenCardLightingRequested)
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
            m_LayerMask = camera.cullingMask;
            m_TraceDistance = float.IsFinite(m_MaxDistance) ? Mathf.Max(0.1f, m_MaxDistance) : 12f;
            if (useSettings && m_UseSdf && !m_OffscreenOnly && settings.segmentedSdfTracing.value)
                m_MeshDetailDistance = Mathf.Clamp(
                    float.IsFinite(settings.meshSdfDetailDistance.value) ? settings.meshSdfDetailDistance.value : 2f,
                    0.1f, m_TraceDistance);
            m_Bias = float.IsFinite(m_RayBias) ? Mathf.Clamp(m_RayBias, 0.001f, 0.25f) : 0.04f;
            ReadLight();
            bool farConfigured = useSettings && settings.farRadianceCache.value && m_UseSdf && cache != null
                && m_FarKernel >= 0 && !m_OffscreenOnly && (!m_RestirGather || m_RestirPixelIntegration)
                && float.IsFinite(settings.farTraceDistance.value) && settings.farTraceDistance.value > m_TraceDistance + FarCellSize;
            if (m_UseSdf)
            {
                m_SkyTexture = PassRecorder.ImportTextureForPass(this, SkyManager.GetSkySourceCubemapHandle());
                if (useSettings && settings.occludedSky.value && (!m_RestirGather || farConfigured)
                    && !m_OffscreenOnly && m_SkyTexture.IsValid())
                    PrepareOccludedSky(frameData.GetOrCreate<VividSkyData>(), settings.skyTraceDistance.value);
            }
            m_Cameras.PurgeDestroyedCameras();
            m_State = m_Cameras.GetOrCreateBase(camera);
            if (m_UseSdf && cache != null && settings.cacheSunVisibility.value
                && m_SunUpdateKernel >= 0 && m_LightPositionRange.w < 0f && m_LightColorShadow.w > 0f)
                PrepareSunCache(camera, cache);
            if (m_UseSdf && cache != null && m_RadiosityKernel >= 0 && settings.surfaceCacheBounceOrders.value > 0)
                PrepareRadiosity(camera, cache, settings.surfaceCacheBounceOrders.value);
            // Dense reservoirs and card lighting need the exact raster hit pixel.
            bool storeScreenPixel = screenConfigured && (m_RestirPixelIntegration
                || (m_ScreenCardLightingRequested && m_ReceiverVisibilitySource != null && m_RadiosityInput.IsValid()));
            m_ScreenRays.desc.ColorFormat = storeScreenPixel ? GeometryFormat : ProbeFormat;
            if (farConfigured)
                PrepareFarCache(camera, cache, settings.farTraceDistance.value);
            if (screenConfigured || pixelTemporal)
            {
                var jitter = cameraData.GetGPUProjectionMatrix(renderIntoTexture: true)
                    * cameraData.GetGPUProjectionMatrixNoJitter(renderIntoTexture: true).inverse;
                m_ScreenJitterUv = new Vector2(jitter.m03, SystemInfo.graphicsUVStartsAtTop ? -jitter.m13 : jitter.m13) * 0.5f;
                m_ScreenJitterDelta = m_State.ScreenJitterUv - m_ScreenJitterUv;
            }
            if (screenConfigured)
                PrepareScreenHistory(frameData, camera, temporal);
            if (pixelTemporal)
                PreparePixelHistory(camera);
            int atlasWidth = m_ProbeWidth * (m_DirectionalProbes ? DirectionalProbePlanes : 1);
            m_Probes.Resize(m_RestirPixelIntegration ? 1 : atlasWidth, m_RestirPixelIntegration ? 1 : m_AtlasHeight);
            bool needsProbeScratch = m_SpatialFilter || m_RestirGather || m_DirectionalCache;
            // The dense path keeps the exact far or sky direction alongside its radiance.
            int scratchWidth = m_DirectionalCache ? DirectionalIrradiancePlanes * m_ProbeWidth
                : m_RestirPixelIntegration && (m_NearOcclusion.z > 0f || m_RestirOpenPbrDiffuse)
                    ? 2 * m_ProbeWidth : atlasWidth;
            m_FilteredProbes.Resize(needsProbeScratch ? scratchWidth : 1, needsProbeScratch ? m_AtlasHeight : 1);
            m_Geometry.Resize(m_ProbeWidth, m_AtlasHeight);
            bool probesValid = m_DirectionalCache || CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, ProbeHistoryId,
                m_PreviousProbes, m_Probes, m_Probes.desc, out m_ProbeHistory);
            bool geometryValid = CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, GeometryHistoryId,
                m_PreviousGeometry, m_Geometry, m_Geometry.desc, out m_GeometryHistory);
            if (m_DirectionalCache)
            {
                int gatherWidth = DirectionalGatherBins * m_ProbeWidth;
                m_CurrentDirectional.Resize(gatherWidth, 2 * m_AtlasHeight);
                m_DirectionalHistoryValid = CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, DirectionalHistoryId,
                    m_PreviousDirectional, m_CurrentDirectional, m_CurrentDirectional.desc, out m_DirectionalHistory);
                m_CurrentDirectionalFiltered.Resize(gatherWidth, m_AtlasHeight);
                m_DirectionalGuideHistoryValid = CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, DirectionalGuideHistoryId,
                    m_PreviousDirectionalFiltered, m_CurrentDirectionalFiltered, m_CurrentDirectionalFiltered.desc, out m_DirectionalGuideHistory);
            }
            m_HasHistory = probesValid && geometryValid && m_State.SceneRevision == m_Scene.Revision
                && ReferenceEquals(m_State.Cache, m_Scene.SurfaceCache)
                && m_State.OffscreenOnly == m_OffscreenOnly && m_State.TraceMode == m_TraceMode
                && m_State.DirectionalProbes == m_DirectionalProbes
                && ((m_State.NearOcclusion.z == 0f && m_NearOcclusion.z == 0f) || m_State.NearOcclusion.Equals(m_NearOcclusion))
                && m_State.DirectionalCache == m_DirectionalCache && m_State.DirectionalGuide == m_DirectionalGuide
                && m_State.DirectionalHierarchicalGuide == m_DirectionalHierarchicalGuide
                && m_State.AdaptiveProbes == m_AdaptiveProbes && m_State.SpatialFilter == m_SpatialFilter
                && m_State.HitDistanceFilter == m_HitDistanceFilter
                && m_State.RestirGather == m_RestirGather && m_State.RestirCardVersion == m_RestirCardVersion
                && m_State.RestirPixelIntegration == m_RestirPixelIntegration
                && m_State.SunConfigured == (m_SunHistory != null)
                && (m_SunHistory == null || m_State.SunCardVersion == m_SunCardVersion)
                && m_State.GatherRadiosityOrder == m_RadiosityOrder
                && m_State.RadiosityCardVersion == m_RadiosityCardVersion
                && m_State.FarDistance == m_FarDistance && m_State.FarCardVersion == m_FarCardVersion
                && m_State.Width == m_Width && m_State.Height == m_Height
                && m_State.LayerMask == m_LayerMask && m_State.LightLayerMask == m_LightLayerMask
                && m_State.MaxDistance == m_TraceDistance && m_State.RayBias == m_Bias
                && m_State.MeshDetailDistance == m_MeshDetailDistance
                && m_State.SkyTraceDistance == m_SkyTraceDistance && m_State.SkyContentHash == m_SkyContentHash
                && m_State.SkyTextureParams.Equals(m_SkyTextureParams) && m_State.SkyTextureTint.Equals(m_SkyTextureTint)
                && m_State.LightPositionRange.Equals(m_LightPositionRange) && m_State.LightColorShadow.Equals(m_LightColorShadow)
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
            m_GlobalPages = clipmap.Pages.innerHandle;
            m_GlobalThinBricks = clipmap.ThinBricks.innerHandle;
            if (!m_GlobalNear.IsValid() || !m_GlobalMiddle.IsValid() || !m_GlobalFar.IsValid()
                || !m_GlobalPages.IsValid() || !m_GlobalThinBricks.IsValid())
                return;

            // Share the producer's handles so the graph orders the atlas and page-table accesses.
            PassRecorder.RegisterImportedTextureForPass(this, m_GlobalMiddle);
            PassRecorder.RegisterImportedTextureForPass(this, m_GlobalFar);
            PassRecorder.RegisterImportedBufferForPass(this, m_GlobalPages);
            PassRecorder.RegisterImportedBufferForPass(this, m_GlobalThinBricks);
            m_GlobalClipmap = clipmap;
        }

        public override void Record(ComputePassContext context)
        {
            if (m_State == null || m_GeometryHistory == null || !m_Geometry.innerHandle.IsValid()
                || (!m_DirectionalCache && (m_ProbeHistory == null || !m_Probes.innerHandle.IsValid()))
                || !m_Output.innerHandle.IsValid() || !m_HitLighting.innerHandle.IsValid() || !m_TraceSources.innerHandle.IsValid()
                || !m_Depth.innerHandle.IsValid() || !m_GBuffer0.innerHandle.IsValid() || !m_GBuffer1.innerHandle.IsValid())
                return;

            var cmd = context.cmd;
            var cache = m_UseSdf ? m_Scene.SurfaceCache : null;
            bool useScreenRays = m_HasScreenHistory && m_ScreenRays.innerHandle.IsValid()
                && m_ScreenHzb.innerHandle.IsValid() && m_ScreenMotion.innerHandle.IsValid();
            bool useSunCache = m_SunAtlas.IsValid() && m_SunCacheHits.innerHandle.IsValid();
            int traceKernel = !m_UseSdf ? m_ScreenGatherKernel : cache != null ? m_CachedTraceKernel : m_TraceKernel;
            if (useSunCache)
                traceKernel = m_CachedSunTraceKernel;
            int receiverKernel = useSunCache ? m_ReceiverCachedSunKernel : cache != null ? m_ReceiverCachedKernel : m_ReceiverTraceKernel;
            bool hasReceiverSource = m_UseSdf && m_ReceiverVisibility.IsValid() && receiverKernel >= 0;
            bool correctReceiver = hasReceiverSource && m_CorrectReceiverSelfHits;
            bool screenCardLighting = hasReceiverSource && useScreenRays && m_ScreenCardLightingRequested && m_RadiosityInput.IsValid();
            if (correctReceiver || screenCardLighting)
            {
                traceKernel = receiverKernel;
                cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, ReceiverVisibilityId, m_ReceiverVisibility);
                cmd.SetComputeBufferParam(m_TraceCompute, traceKernel, ReceiverEntitiesId, m_ReceiverEntities);
                cmd.SetComputeIntParam(m_TraceCompute, ReceiverCountId, m_ReceiverCount);
            }
            cmd.SetComputeIntParam(m_TraceCompute, CorrectReceiverId, correctReceiver ? 1 : 0);
            cmd.SetComputeIntParam(m_TraceCompute, ScreenCardLightingId, screenCardLighting ? 1 : 0);
            BindReceiver(cmd, m_TraceCompute, traceKernel);
            TextureHandle probeTarget = m_Probes.innerHandle;
            if (m_DirectionalCache)
                probeTarget = m_DirectionalRayTexture.innerHandle;
            else if (m_HitDistanceFilter || m_RestirGather)
                probeTarget = m_FilteredProbes.innerHandle;
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, ProbeId, probeTarget);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, GeometryId, m_Geometry.innerHandle);
            bool useGlobalClipmap = m_UseSdf && m_GlobalClipmap?.Recorded == true;
            if (m_UseSdf)
            {
                m_Scene.Bind(cmd, m_TraceCompute, traceKernel);
                BindSegmentedTracing(cmd, traceKernel, useGlobalClipmap);
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
                    || !m_DirectionalTemporal.innerHandle.IsValid()
                    || !m_DirectionalGuideTexture.innerHandle.IsValid() || !m_DirectionalRayTexture.innerHandle.IsValid()))
                || (m_RestirGather && (m_RestirHistory == null || !m_CurrentRestir.innerHandle.IsValid()))
                || (m_RestirPixelIntegration && (!m_RestirVariance.innerHandle.IsValid() || !m_RawResolved.innerHandle.IsValid()))
                || !m_SpatialRestir.innerHandle.IsValid()
                || !m_HitSamples.innerHandle.IsValid())
                return;
            bool pixelTemporal = m_PixelHistory != null && m_PixelGeometryHistory != null && m_ScreenMotion.innerHandle.IsValid()
                && m_RawResolved.innerHandle.IsValid() && m_CurrentPixelLighting.innerHandle.IsValid()
                && m_CurrentPixelGeometry.innerHandle.IsValid() && m_PreviousPixelLighting.innerHandle.IsValid()
                && m_PreviousPixelGeometry.innerHandle.IsValid();
            m_RestirOpenPbrDiffuse &= m_OpenPbrParameters?.innerHandle.IsValid() == true
                && m_OpenPbrAux0?.innerHandle.IsValid() == true && m_OpenPbrAux1?.innerHandle.IsValid() == true;
            hasHistory &= m_State.HasScreenRays == useScreenRays && m_State.PixelTemporal == pixelTemporal
                && m_State.RestirOpenPbrDiffuse == m_RestirOpenPbrDiffuse
                && m_State.CorrectReceiverSelfHits == correctReceiver && m_State.ScreenCardLighting == screenCardLighting
                && m_State.GlobalClipmapUsed == useGlobalClipmap;
            bool useProbeHistory = hasHistory && !pixelTemporal && !m_RestirGather && !m_DirectionalCache;
            bool useDirectionalHistory = m_DirectionalCache && hasHistory && m_DirectionalHistoryValid
                && m_PreviousDirectional.innerHandle.IsValid();
            bool useDirectionalGuideHistory = m_DirectionalGuide && hasHistory && m_DirectionalGuideHistoryValid
                && m_PreviousDirectionalFiltered.innerHandle.IsValid();
            BindDirectionalGuideMode(cmd, m_TraceCompute);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, DirectionalGuideId, m_DirectionalCache ? m_DirectionalGuideTexture.innerHandle : black);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, PreviousProbeId, useProbeHistory ? m_PreviousProbes.innerHandle : black);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, PreviousGeometryId, useProbeHistory ? m_PreviousGeometry.innerHandle : black);
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
            cmd.SetComputeIntParam(m_TraceCompute, FrameId, frame);
            cmd.SetComputeIntParam(m_TraceCompute, HasHistoryId, useProbeHistory ? 1 : 0);
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
                if (m_AdaptiveProbes)
                {
                    using var scope = new ProfilingScope(cmd, PlacementSampler);
                    BindReceiver(cmd, m_GatherCompute, m_PlacementKernel);
                    cmd.SetComputeTextureParam(m_GatherCompute, m_PlacementKernel, PlacementOutputId, placement);
                    cmd.DispatchCompute(m_GatherCompute, m_PlacementKernel, groupsX, CoreUtils.DivRoundUp(m_ProbeHeight, GroupSize), 1);
                }
                if (useSunCache)
                    RecordSunCache(cmd, traceKernel);
                if (cache != null)
                {
                    RecordRadiosity(cmd, traceKernel, cache, black);
                    RecordFarCache(cmd, traceKernel, cache, black);
                }
                if (m_DirectionalCache)
                    RecordDirectionalGuide(cmd, placement, black, useDirectionalGuideHistory, frame, groupsX, groupsY);
                if (useScreenRays)
                    RecordScreenTrace(cmd, groupsX, groupsY, placement, black);
                using (new ProfilingScope(cmd, GatherSampler))
                    cmd.DispatchCompute(m_TraceCompute, traceKernel, groupsX, groupsY, 1);
                if (m_DirectionalCache || m_HitDistanceFilter || (m_SpatialFilter && !m_RestirGather))
                {
                    BindProbeFrame(cmd, m_FilterCompute);
                    cmd.SetComputeIntParam(m_FilterCompute, DirectionalProbesId, m_DirectionalProbes ? 1 : 0);
                    cmd.SetComputeIntParam(m_FilterCompute, SpatialFilterId, m_SpatialFilter ? 1 : 0);
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
                RecordResolve(cmd, placement, pixelTemporal);
                if (pixelTemporal)
                    RecordPixelTemporal(cmd, hasHistory && m_PixelHistory.IsValid() && m_PixelGeometryHistory.IsValid());
                if (m_RestirPixelIntegration)
                    RecordRestirDenoise(cmd, pixelTemporal);
                if (m_NearOcclusion.y > 0f && m_NearOcclusion.z == 0f)
                    RecordNearOcclusion(cmd);
            }
            var diffuse = context.GetOrCreate<VividSdfDiffuseFrameData>();
            diffuse.Output = m_Output;
            diffuse.SkyHandled = m_SkyTextureParams.w > 0.5f;
            diffuse.RealtimeOnly = m_RealtimeOnly;
            diffuse.OpenPbrSource = m_RestirOpenPbrDiffuse ? this : null;
#if UNITY_EDITOR
            EditorTraceCapture?.Invoke(context, m_TraceSources.innerHandle.ResolveTexture(),
                m_Output.innerHandle.ResolveTexture(), m_Geometry.innerHandle.ResolveTexture());
#endif
            m_ProbeHistory?.MarkWritten();
            m_GeometryHistory.MarkWritten();
            m_DirectionalHistory?.MarkWritten();
            m_DirectionalGuideHistory?.MarkWritten();
            m_State.ViewProjection = m_ViewProjection;
            m_State.Cache = m_Scene.SurfaceCache;
            m_State.OffscreenOnly = m_OffscreenOnly;
            m_State.TraceMode = m_TraceMode;
            m_State.HasScreenRays = useScreenRays;
            m_State.DirectionalProbes = m_DirectionalProbes;
            m_State.NearOcclusion = m_NearOcclusion;
            m_State.DirectionalCache = m_DirectionalCache;
            m_State.DirectionalGuide = m_DirectionalGuide;
            m_State.DirectionalHierarchicalGuide = m_DirectionalHierarchicalGuide;
            m_State.AdaptiveProbes = m_AdaptiveProbes;
            m_State.SpatialFilter = m_SpatialFilter;
            m_State.HitDistanceFilter = m_HitDistanceFilter;
            m_State.RestirGather = m_RestirGather;
            m_State.RestirPixelIntegration = m_RestirPixelIntegration;
            m_State.RestirOpenPbrDiffuse = m_RestirOpenPbrDiffuse;
            m_State.RestirCardVersion = m_RestirCardVersion;
            m_State.PixelTemporal = pixelTemporal;
            m_State.CorrectReceiverSelfHits = correctReceiver;
            m_State.ScreenCardLighting = screenCardLighting;
            m_State.MeshDetailDistance = m_MeshDetailDistance;
            m_State.GlobalClipmapUsed = useGlobalClipmap;
            m_State.ScreenJitterUv = m_ScreenJitterUv;
            m_State.SceneRevision = m_Scene.Revision;
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
            m_State.SunConfigured = m_SunHistory != null;
            m_State.SunReadyCards = useSunCache ? m_SunReadyCards : 0;
            m_State.SunCardVersion = m_SunCardVersion;
            m_State.RadiosityCardVersion = m_RadiosityCardVersion;
            m_State.RadiosityTargetOrders = m_RadiosityTargetOrders;
            m_State.GatherRadiosityOrder = m_RadiosityOrder;
            int cursor = m_RadiosityCursor + m_RadiosityUpdateCards;
            bool finishedSweep = m_RadiosityUpdateCards > 0 && cursor == m_Scene.RadiosityCardCount;
            m_State.RadiosityCursor = finishedSweep ? 0 : cursor;
            m_State.RadiosityOrder = m_RadiosityOrder + (finishedSweep ? 1 : 0);
            m_State.FarDistance = m_FarDistance;
            m_State.FarCardVersion = m_FarCardVersion;
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
            m_TraceCompute = m_GatherCompute = m_ImportanceSamplingCompute = m_FilterCompute = m_GatherTemporalCompute = null;
            m_RestirCompute = m_SunCompute = m_RadiosityCompute = m_FarCompute = m_NearOcclusionCompute = null;
            m_TraceKernel = m_CachedTraceKernel = m_ResolveKernel = -1;
            m_ScreenTraceKernel = m_ScreenGatherKernel = -1;
            m_CachedSunTraceKernel = m_SunUpdateKernel = -1;
            m_RadiosityKernel = -1;
            m_FarKernel = m_NearOcclusionKernel = -1;
            m_OpenPbrResolveKernel = -1;
            m_PlacementKernel = m_FilterKernel = m_HitFilterKernel = -1;
            m_RestirTemporalKernel = m_RestirSpatialKernel = -1;
            m_RestirDenoiseKernel = -1;
            m_PixelTemporalKernel = m_RestirPixelTemporalKernel = -1;
        }

        internal static (int columns, int cards) ResolveSunAtlasLayout(Vector2Int size, int instanceCount, int maxDimension)
        {
            if (size.x <= 0 || size.y <= 0 || size.x > maxDimension || size.y > maxDimension
                || instanceCount <= 0 || (long)size.x * size.y > SunUpdateTexelBudget)
                return default;
            int columns = Mathf.Min(16, maxDimension / size.x);
            int rows = Mathf.Min(maxDimension / size.y, SunAtlasTexelLimit / (columns * size.x * size.y));
            int instances = Mathf.Min(instanceCount, columns * rows / VividSdfSurfaceCache.CardsPerMesh);
            return (columns, instances * VividSdfSurfaceCache.CardsPerMesh);
        }

        private void PrepareSunCache(Camera camera, VividSdfSurfaceCache cache)
        {
            m_SunCardSize = cache.CardSize;
            var (columns, cards) = ResolveSunAtlasLayout(m_SunCardSize, m_Scene.SurfaceLightingInstanceCount, SystemInfo.maxTextureSize);
            if (cards == 0 || m_Scene.SurfaceLightingInstances == null)
                return;

            var history = camera.GetVividCameraHistory();
            if (!history.IsFrameActive)
                return;
            m_SunHistoryDesc.Resize(columns * m_SunCardSize.x, CoreUtils.DivRoundUp(cards, columns) * m_SunCardSize.y);
            m_SunHistory = history.GetOrCreateTexture(SunHistoryId, 1,
                CameraHistoryRenderGraphBridge.CreateDescriptor(m_SunHistoryDesc.desc));
            m_SunLayout = new Vector4(m_SunCardSize.x, m_SunCardSize.y, columns, 0f);
            m_SunCardVersion = cache.CardVersion;

            // Moving lights or casters use the hit shadow path immediately.
            // Wait for one stable frame before spending the update budget again.
            bool stable = m_State.SunConfigured && m_State.SceneRevision == m_Scene.Revision
                && ReferenceEquals(m_State.Cache, cache) && m_State.SunCardVersion == m_SunCardVersion
                && m_State.LightLayerMask == m_LightLayerMask && m_State.RayBias == m_Bias
                && m_State.LightPositionRange.Equals(m_LightPositionRange);
            if (!stable)
                return;

            m_SunReadyCards = m_SunHistory.IsValid(0) ? m_State.SunReadyCards : 0;
            m_SunUpdateCards = Mathf.Min(cards - m_SunReadyCards, SunUpdateTexelBudget / (m_SunCardSize.x * m_SunCardSize.y));
            if (m_SunReadyCards + m_SunUpdateCards == 0)
                return;
            m_SunCacheHits.Resize(m_ProbeWidth, m_AtlasHeight);
            m_SunAtlas = CameraHistoryRenderGraphBridge.ImportForPass(this, m_SunHistory, 0,
                m_SunUpdateCards != 0 ? AccessFlags.ReadWrite : AccessFlags.Read);
        }

        private void RecordSunCache(ComputeCommandBuffer cmd, int traceKernel)
        {
            cmd.SetComputeVectorParam(m_TraceCompute, SunLayoutId, m_SunLayout);
            if (m_SunUpdateCards > 0)
            {
                using var scope = new ProfilingScope(cmd, SunUpdateSampler);
                int kernel = m_SunUpdateKernel;
                BindSdfTraceConstants(cmd, m_SunCompute);
                BindSunLighting(cmd, m_SunCompute);
                cmd.SetComputeVectorParam(m_SunCompute, SunLayoutId, m_SunLayout);
                m_Scene.Bind(cmd, m_SunCompute, kernel);
                cmd.SetComputeBufferParam(m_SunCompute, kernel, SurfaceId, m_Scene.DiffuseSurfaces);
                cmd.SetComputeBufferParam(m_SunCompute, kernel, SunInstancesId, m_Scene.SurfaceLightingInstances);
                cmd.SetComputeTextureParam(m_SunCompute, kernel, CardAlbedoDepthId, m_CardAlbedoDepth);
                cmd.SetComputeTextureParam(m_SunCompute, kernel, CardNormalCoverageId, m_CardNormalCoverage);
                cmd.SetComputeTextureParam(m_SunCompute, kernel, SunVisibilityOutputId, m_SunAtlas);
                cmd.SetComputeIntParam(m_SunCompute, SunFirstCardId, m_SunReadyCards);
                cmd.DispatchCompute(m_SunCompute, kernel, CoreUtils.DivRoundUp(m_SunCardSize.x, GroupSize),
                    CoreUtils.DivRoundUp(m_SunCardSize.y, GroupSize), m_SunUpdateCards);
                m_SunReadyCards += m_SunUpdateCards;
            }

            cmd.SetComputeIntParam(m_TraceCompute, SunReadyCardsId, m_SunReadyCards);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, SunVisibilityId, m_SunAtlas);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, SunCacheHitsId, m_SunCacheHits.innerHandle);
            m_SunHistory.MarkWritten();
        }

        private void PrepareRadiosity(Camera camera, VividSdfSurfaceCache cache, int targetOrders)
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
            var atlasA = history.GetOrCreateTexture(RadiosityAId, 1, descriptor);
            var atlasB = history.GetOrCreateTexture(RadiosityBId, 1, descriptor);
            m_RadiosityTargetOrders = targetOrders;
            m_RadiosityCardVersion = cache.CardVersion;
            bool stable = atlasA.IsValid(0) && atlasB.IsValid(0)
                && m_State.RadiosityTargetOrders == targetOrders && m_State.SceneRevision == m_Scene.Revision
                && ReferenceEquals(m_State.Cache, cache) && m_State.RadiosityCardVersion == m_RadiosityCardVersion
                && m_State.LayerMask == m_LayerMask && m_State.LightLayerMask == m_LightLayerMask
                && m_State.MaxDistance == m_TraceDistance && m_State.RayBias == m_Bias
                && m_State.SkyTraceDistance == m_SkyTraceDistance && m_State.SkyContentHash == m_SkyContentHash
                && m_State.SkyTextureParams.Equals(m_SkyTextureParams) && m_State.SkyTextureTint.Equals(m_SkyTextureTint)
                && m_State.LightPositionRange.Equals(m_LightPositionRange) && m_State.LightColorShadow.Equals(m_LightColorShadow);
            m_RadiosityOrder = stable ? m_State.RadiosityOrder : 0;
            m_RadiosityCursor = stable ? m_State.RadiosityCursor : 0;
            bool writeA = (m_RadiosityOrder & 1) == 0;
            m_RadiosityReadHistory = writeA ? atlasB : atlasA;
            m_RadiosityWriteHistory = writeA ? atlasA : atlasB;
            if (m_RadiosityOrder > 0)
                m_RadiosityInput = CameraHistoryRenderGraphBridge.ImportForPass(this, m_RadiosityReadHistory, 0, AccessFlags.Read);
            if (m_RadiosityOrder < targetOrders)
            {
                m_RadiosityOutput = CameraHistoryRenderGraphBridge.ImportForPass(this, m_RadiosityWriteHistory, 0, AccessFlags.Write);
                m_RadiosityUpdateCards = m_RadiosityOutput.IsValid() ? Mathf.Min(cards - m_RadiosityCursor, RadiosityCardsPerFrame) : 0;
            }
        }

        private void RecordRadiosity(ComputeCommandBuffer cmd, int traceKernel, VividSdfSurfaceCache cache, TextureHandle black)
        {
            bool published = m_RadiosityInput.IsValid();
            var input = published ? m_RadiosityInput : black;
            var work = m_RadiosityTargetOrders > 0 ? m_Scene.RadiosityWork : m_Scene.LeafInstances;
            cmd.SetComputeIntParam(m_TraceCompute, RadiosityLayoutId, published ? m_Scene.RadiosityCardCount : 0);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, RadiosityInputId, input);
            cmd.SetComputeBufferParam(m_TraceCompute, traceKernel, RadiosityWorkId, work);
            if (m_RadiosityUpdateCards > 0)
            {
                using var scope = new ProfilingScope(cmd, RadiositySampler);
                int kernel = m_RadiosityKernel;
                BindSdfTraceConstants(cmd, m_RadiosityCompute);
                BindSunLighting(cmd, m_RadiosityCompute);
                BindOccludedSky(cmd, m_RadiosityCompute, kernel);
                cmd.SetComputeIntParam(m_RadiosityCompute, RadiosityLayoutId, published ? m_Scene.RadiosityCardCount : 0);
                m_Scene.Bind(cmd, m_RadiosityCompute, kernel);
                cmd.SetComputeBufferParam(m_RadiosityCompute, kernel, SurfaceId, m_Scene.DiffuseSurfaces);
                cmd.SetComputeBufferParam(m_RadiosityCompute, kernel, LocalCardTableId, cache.LocalCardTable);
                cmd.SetComputeBufferParam(m_RadiosityCompute, kernel, RadiosityWorkId, work);
                cmd.SetComputeTextureParam(m_RadiosityCompute, kernel, CardAlbedoDepthId, m_CardAlbedoDepth);
                cmd.SetComputeTextureParam(m_RadiosityCompute, kernel, CardNormalCoverageId, m_CardNormalCoverage);
                cmd.SetComputeTextureParam(m_RadiosityCompute, kernel, RadiosityInputId, input);
                cmd.SetComputeTextureParam(m_RadiosityCompute, kernel, RadiosityOutputId, m_RadiosityOutput);
                cmd.SetComputeIntParam(m_RadiosityCompute, RadiosityFirstCardId, m_RadiosityCursor);
                cmd.DispatchCompute(m_RadiosityCompute, kernel, 1, 1, m_RadiosityUpdateCards);
            }

            // The atlases are single-slot histories. Carry validity through read-only
            // frames as well; the order and cursor gate all reads of unfinished sweeps.
            m_RadiosityReadHistory?.MarkWritten();
            m_RadiosityWriteHistory?.MarkWritten();
        }

        private static int FarSlot(Vector3Int cell)
        {
            const int mask = FarGridSize - 1;
            return (cell.x & mask) + FarGridSize * ((cell.z & mask) + FarGridSize * (cell.y & mask));
        }

        private void PrepareFarCache(Camera camera, VividSdfSurfaceCache cache, float distance)
        {
            var history = camera.GetVividCameraHistory();
            if (!history.IsFrameActive || SystemInfo.maxTextureSize < FarGridSize * (FarDirectionCount + 1))
                return;

            m_FarDesc.Resize(FarGridSize * FarGridSize, FarGridSize * (FarDirectionCount + 1));
            m_FarHistory = history.GetOrCreateTexture(FarHistoryId, 1,
                CameraHistoryRenderGraphBridge.CreateDescriptor(m_FarDesc.desc));
            m_FarHitIdsHistory = history.GetOrCreateBuffer(FarHitIdsHistoryId, 1, FarHitIdsDescriptor);
            m_FarDistance = distance;
            m_FarCardVersion = cache.CardVersion;
            var center = Vector3Int.FloorToInt(camera.transform.position / FarCellSize);
            m_FarWindow = center - new Vector3Int(FarGridSize / 2, FarGridSize / 2, FarGridSize / 2);
            m_FarClear = !m_FarHistory.IsValid(0) || !m_FarHitIdsHistory.IsValid(0) || m_State.FarDistance != m_FarDistance
                || m_State.SceneRevision != m_Scene.Revision || !ReferenceEquals(m_State.Cache, cache)
                || m_State.FarCardVersion != m_FarCardVersion || m_State.GatherRadiosityOrder != m_RadiosityOrder
                || m_State.LayerMask != m_LayerMask || m_State.LightLayerMask != m_LightLayerMask
                || m_State.MaxDistance != m_TraceDistance || m_State.RayBias != m_Bias
                || !m_State.LightPositionRange.Equals(m_LightPositionRange) || !m_State.LightColorShadow.Equals(m_LightColorShadow);
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

        private void RecordFarCache(ComputeCommandBuffer cmd, int traceKernel, VividSdfSurfaceCache cache, TextureHandle black)
        {
            bool ready = m_FarAtlas.IsValid() && m_FarHitIds.IsValid();
            cmd.SetComputeFloatParam(m_TraceCompute, FarDistanceId, ready ? m_FarDistance : 0f);
            cmd.SetComputeTextureParam(m_TraceCompute, traceKernel, FarRadianceId, ready ? m_FarAtlas : black);
            if (ready)
                cmd.SetComputeBufferParam(m_TraceCompute, traceKernel, FarHitIdsId, m_FarHitIds);
            else
                cmd.SetComputeBufferParam(m_TraceCompute, traceKernel, FarHitIdsId, m_Scene.LeafInstances);
            if (!ready)
                return;
            cmd.SetComputeVectorParam(m_TraceCompute, FarWindowId, new Vector4(m_FarWindow.x, m_FarWindow.y, m_FarWindow.z, 0f));

            if (m_FarClear || m_FarUpdateCount > 0)
            {
                using var scope = new ProfilingScope(cmd, FarCacheSampler);
                int kernel = m_FarKernel;
                BindSdfTraceConstants(cmd, m_FarCompute);
                BindSunLighting(cmd, m_FarCompute);
                cmd.SetComputeFloatParam(m_FarCompute, FarDistanceId, m_FarDistance);
                cmd.SetComputeIntParam(m_FarCompute, RadiosityLayoutId, m_RadiosityInput.IsValid() ? m_Scene.RadiosityCardCount : 0);
                m_Scene.Bind(cmd, m_FarCompute, kernel);
                cmd.SetComputeBufferParam(m_FarCompute, kernel, SurfaceId, m_Scene.DiffuseSurfaces);
                cmd.SetComputeBufferParam(m_FarCompute, kernel, LocalCardTableId, cache.LocalCardTable);
                cmd.SetComputeTextureParam(m_FarCompute, kernel, CardAlbedoDepthId, m_CardAlbedoDepth);
                cmd.SetComputeTextureParam(m_FarCompute, kernel, CardNormalCoverageId, m_CardNormalCoverage);
                cmd.SetComputeBufferParam(m_FarCompute, kernel, RadiosityWorkId,
                    m_RadiosityTargetOrders > 0 ? m_Scene.RadiosityWork : m_Scene.LeafInstances);
                cmd.SetComputeTextureParam(m_FarCompute, kernel, RadiosityInputId,
                    m_RadiosityInput.IsValid() ? m_RadiosityInput : black);
                cmd.SetComputeTextureParam(m_FarCompute, kernel, FarOutputId, m_FarAtlas);
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

        private void BindSegmentedTracing(ComputeCommandBuffer cmd, int kernel, bool useGlobalClipmap)
        {
            cmd.SetComputeFloatParam(m_TraceCompute, MeshDetailDistanceId, m_MeshDetailDistance);
            cmd.SetComputeFloatParam(m_TraceCompute, FarDistanceId, 0f);
            cmd.SetComputeIntParam(m_TraceCompute, UseGlobalClipmapId, useGlobalClipmap ? 1 : 0);
            var fallback = m_GlobalFallback.innerHandle;
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, GlobalNearId, useGlobalClipmap ? m_GlobalNear : fallback);
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, GlobalMiddleId, useGlobalClipmap ? m_GlobalMiddle : fallback);
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, GlobalFarId, useGlobalClipmap ? m_GlobalFar : fallback);
            if (!useGlobalClipmap)
            {
                cmd.SetComputeBufferParam(m_TraceCompute, kernel, GlobalPagesId, m_Scene.LeafInstances);
                cmd.SetComputeBufferParam(m_TraceCompute, kernel, GlobalThinBricksId, m_Scene.LeafInstances);
                return;
            }
            cmd.SetComputeBufferParam(m_TraceCompute, kernel, GlobalPagesId, m_GlobalPages);
            cmd.SetComputeBufferParam(m_TraceCompute, kernel, GlobalThinBricksId, m_GlobalThinBricks);
            cmd.SetComputeIntParam(m_TraceCompute, GlobalLayerMaskId, m_GlobalClipmap.LayerMask);
            cmd.SetComputeVectorArrayParam(m_TraceCompute, GlobalLevelsId, m_GlobalClipmap.Levels);
            cmd.SetComputeVectorArrayParam(m_TraceCompute, GlobalScrollId, m_GlobalClipmap.Scroll);
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
            screen.radiance = history.GetOrCreateTexture(ScreenRadianceHistoryId, 2,
                CameraHistoryRenderGraphBridge.CreateDescriptor(m_ScreenRadianceDesc.desc));
            screen.geometry = history.GetOrCreateTexture(ScreenGeometryHistoryId, 2,
                CameraHistoryRenderGraphBridge.CreateDescriptor(m_ScreenGeometryDesc.desc));
            if (temporal.isFirstFrame || temporal.resetPostProcessingHistory || m_State.Width != m_Width
                || m_State.Height != m_Height || !screen.radiance.IsValid() || !screen.geometry.IsValid())
                return;

            m_PreviousScreenRadiance = CameraHistoryRenderGraphBridge.ImportForPass(this, screen.radiance, 1, AccessFlags.Read);
            m_PreviousScreenGeometry = CameraHistoryRenderGraphBridge.ImportForPass(this, screen.geometry, 1, AccessFlags.Read);
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

        private void RecordResolve(ComputeCommandBuffer cmd, TextureHandle placement, bool pixelTemporal)
        {
            using var scope = new ProfilingScope(cmd, ResolveSampler);
            int kernel = m_DirectionalCache ? m_DirectionalResolveKernel
                : m_RestirOpenPbrDiffuse ? m_OpenPbrResolveKernel : m_ResolveKernel;
            BindReceiver(cmd, m_GatherCompute, kernel);
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
            cmd.SetComputeTextureParam(m_GatherCompute, kernel, RestirSpatialId, m_SpatialRestir.innerHandle);
            cmd.SetComputeTextureParam(m_GatherCompute, kernel, RestirVarianceOutputId, m_RestirVariance.innerHandle);
            cmd.SetComputeTextureParam(m_GatherCompute, kernel, OutputId,
                pixelTemporal || m_RestirPixelIntegration ? m_RawResolved.innerHandle : m_Output.innerHandle);
            cmd.DispatchCompute(m_GatherCompute, kernel, CoreUtils.DivRoundUp(m_Width, GroupSize), CoreUtils.DivRoundUp(m_Height, GroupSize), 1);
        }

        private void RecordRestirDenoise(ComputeCommandBuffer cmd, bool pixelTemporal)
        {
            using var scope = new ProfilingScope(cmd, RestirDenoiseSampler);
            int kernel = m_RestirDenoiseKernel;
            BindReceiver(cmd, m_RestirCompute, kernel);
            cmd.SetComputeTextureParam(m_RestirCompute, kernel, CurrentResolvedId, m_RawResolved.innerHandle);
            cmd.SetComputeTextureParam(m_RestirCompute, kernel, RestirFilterInputId,
                pixelTemporal ? m_CurrentPixelLighting.innerHandle : m_RawResolved.innerHandle);
            cmd.SetComputeTextureParam(m_RestirCompute, kernel, RestirVarianceId, m_RestirVariance.innerHandle);
            cmd.SetComputeTextureParam(m_RestirCompute, kernel, OutputId, m_Output.innerHandle);
            cmd.DispatchCompute(m_RestirCompute, kernel, CoreUtils.DivRoundUp(m_Width, GroupSize), CoreUtils.DivRoundUp(m_Height, GroupSize), 1);
        }

        private void PreparePixelHistory(Camera camera)
        {
            m_CurrentPixelLighting.Resize(m_Width, m_Height);
            m_CurrentPixelGeometry.Resize(m_Width, m_Height);
            CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, PixelHistoryId,
                m_PreviousPixelLighting, m_CurrentPixelLighting, m_CurrentPixelLighting.desc, out m_PixelHistory,
                m_RestirPixelIntegration ? AccessFlags.ReadWrite : AccessFlags.Write);
            CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, PixelGeometryHistoryId,
                m_PreviousPixelGeometry, m_CurrentPixelGeometry, m_CurrentPixelGeometry.desc, out m_PixelGeometryHistory, AccessFlags.Write);
        }

        private void RecordPixelTemporal(ComputeCommandBuffer cmd, bool hasHistory)
        {
            using var scope = new ProfilingScope(cmd, PixelTemporalSampler);
            int kernel = m_RestirGather && m_RestirPixelTemporalKernel >= 0
                ? m_RestirPixelTemporalKernel : m_PixelTemporalKernel;
            BindReceiver(cmd, m_GatherTemporalCompute, kernel);
            BindProbeFrame(cmd, m_GatherTemporalCompute);
            cmd.SetComputeMatrixParam(m_GatherTemporalCompute, PreviousViewProjectionId, m_State.ViewProjection);
            cmd.SetComputeIntParam(m_GatherTemporalCompute, HasHistoryId, hasHistory ? 1 : 0);
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
            m_PixelGeometryHistory.MarkWritten();
        }

        private void RecordScreenTrace(ComputeCommandBuffer cmd, int groupsX, int groupsY, TextureHandle placement, TextureHandle black)
        {
            using var scope = new ProfilingScope(cmd, ScreenTraceSampler);
            int kernel = m_ScreenTraceKernel;
            BindReceiver(cmd, m_TraceCompute, kernel);
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, PlacementId, placement);
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, DirectionalGuideId, m_DirectionalCache ? m_DirectionalGuideTexture.innerHandle : black);
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, HzbId, m_ScreenHzb.innerHandle);
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, MotionId, m_ScreenMotion.innerHandle);
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, ScreenRadianceId, m_PreviousScreenRadiance);
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, ScreenGeometryId, m_PreviousScreenGeometry);
            cmd.SetComputeTextureParam(m_TraceCompute, kernel, ScreenRaysOutputId, m_ScreenRays.innerHandle);
            cmd.SetComputeMatrixParam(m_TraceCompute, PreviousInverseVpId, m_State.ViewProjection.inverse);
            cmd.SetComputeVectorParam(m_TraceCompute, JitterDeltaId, new Vector4(m_ScreenJitterDelta.x, m_ScreenJitterDelta.y, 0f, 0f));
            cmd.DispatchCompute(m_TraceCompute, kernel, groupsX, groupsY, 1);
        }

        private void RecordDirectionalGuide(ComputeCommandBuffer cmd, TextureHandle placement, TextureHandle black,
            bool hasHistory, int frame, int groupsX, int groupsY)
        {
            using var scope = new ProfilingScope(cmd, DirectionalGuideSampler);
            int kernel = m_DirectionalGuideKernel;
            BindReceiver(cmd, m_ImportanceSamplingCompute, kernel);
            BindProbeFrame(cmd, m_ImportanceSamplingCompute);
            BindDirectionalGuideMode(cmd, m_ImportanceSamplingCompute);
            cmd.SetComputeIntParam(m_ImportanceSamplingCompute, FrameId, frame);
            cmd.SetComputeMatrixParam(m_ImportanceSamplingCompute, PreviousViewProjectionId, m_State.ViewProjection);
            cmd.SetComputeIntParam(m_ImportanceSamplingCompute, DirectionalHasHistoryId, hasHistory ? 1 : 0);
            cmd.SetComputeTextureParam(m_ImportanceSamplingCompute, kernel, PlacementId, placement);
            cmd.SetComputeTextureParam(m_ImportanceSamplingCompute, kernel, PreviousGeometryId, hasHistory ? m_PreviousGeometry.innerHandle : black);
            cmd.SetComputeTextureParam(m_ImportanceSamplingCompute, kernel, DirectionalPreviousId,
                hasHistory ? m_PreviousDirectionalFiltered.innerHandle : black);
            bool worldGuide = m_DirectionalHierarchicalGuide && m_FarAtlas.IsValid() && m_FarHitIds.IsValid();
            if (worldGuide)
                cmd.SetComputeVectorParam(m_ImportanceSamplingCompute, FarWindowId,
                    new Vector4(m_FarWindow.x, m_FarWindow.y, m_FarWindow.z, 0f));
            cmd.SetComputeIntParam(m_ImportanceSamplingCompute, DirectionalWorldGuideId, worldGuide ? 1 : 0);
            cmd.SetComputeTextureParam(m_ImportanceSamplingCompute, kernel, FarRadianceId, worldGuide ? m_FarAtlas : black);
            cmd.SetComputeTextureParam(m_ImportanceSamplingCompute, kernel, DirectionalGuideOutputId, m_DirectionalGuideTexture.innerHandle);
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
            using (new ProfilingScope(cmd, DirectionalCompositeSampler))
            {
                cmd.SetComputeTextureParam(shader, kernel, GeometryId, geometry);
                cmd.SetComputeTextureParam(shader, kernel, DirectionalRaysId, m_DirectionalRayTexture.innerHandle);
                cmd.SetComputeTextureParam(shader, kernel, DirectionalCompositeOutputId, composite);
                cmd.DispatchCompute(shader, kernel, m_ProbeWidth, m_AtlasHeight, 1);
            }
            using (new ProfilingScope(cmd, DirectionalTemporalSampler))
            {
                kernel = m_DirectionalTemporalKernel;
                cmd.SetComputeIntParam(shader, DirectionalHasHistoryId, hasHistory ? 1 : 0);
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
                kernel = m_DirectionalFilterKernel;
                cmd.SetComputeTextureParam(shader, kernel, GeometryId, geometry);
                cmd.SetComputeTextureParam(shader, kernel, DirectionalCompositeId, composite);
                cmd.SetComputeTextureParam(shader, kernel, DirectionalTemporalId, temporal);
                cmd.SetComputeTextureParam(shader, kernel, DirectionalFilteredOutputId, filtered);
                cmd.DispatchCompute(shader, kernel, m_ProbeWidth, m_AtlasHeight, 1);
            }
            using (new ProfilingScope(cmd, DirectionalIrradianceSampler))
            {
                kernel = m_DirectionalIntegrateKernel;
                cmd.SetComputeTextureParam(shader, kernel, GeometryId, geometry);
                cmd.SetComputeTextureParam(shader, kernel, DirectionalCompositeId, composite);
                cmd.SetComputeTextureParam(shader, kernel, DirectionalFilteredId, filtered);
                cmd.SetComputeTextureParam(shader, kernel, FilteredProbesId, m_FilteredProbes.innerHandle);
                cmd.DispatchCompute(shader, kernel, groupsX, groupsY, 1);
            }
        }

        private void BindProbeFrame(ComputeCommandBuffer cmd, ComputeShader shader)
        {
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

        private void ReadLight()
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
