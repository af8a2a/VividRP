using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class SdfDiffuseGatherPass : ComputePass
    {
#if UNITY_EDITOR
        internal static System.Action<ComputePassContext, Texture, Texture, Texture> EditorTraceCapture;
#endif
        private const int ProbeSpacing = 16; // Keep in sync with SdfDiffuseGather.compute.
        private const int RaysPerProbe = 8;
        private const int DirectionalProbePlanes = 4; // RGB baseline and three world-space tangent moments.
        private const int GroupSize = 8;
        private const GraphicsFormat ProbeFormat = GraphicsFormat.R16G16B16A16_SFloat;
        private const GraphicsFormat GeometryFormat = GraphicsFormat.R32G32B32A32_SFloat;
        private const GraphicsFormat PixelGeometryFormat = GraphicsFormat.R32_UInt;
        private const GraphicsFormat SunVisibilityFormat = GraphicsFormat.R16_SFloat;
        private const int SunAtlasTexelLimit = 4 * 1024 * 1024;
        private const int SunUpdateTexelBudget = 16 * 1024;
        private static readonly CameraHistoryId ProbeHistoryId = CameraHistoryId.Create("SdfDiffuseProbeHistory");
        private static readonly CameraHistoryId GeometryHistoryId = CameraHistoryId.Create("SdfDiffuseGeometryHistory");
        private static readonly CameraHistoryId PixelHistoryId = CameraHistoryId.Create("SdfDiffusePixelHistory");
        private static readonly CameraHistoryId PixelGeometryHistoryId = CameraHistoryId.Create("SdfDiffusePixelGeometryHistory");
        private static readonly CameraHistoryId ScreenRadianceHistoryId = CameraHistoryId.Create("SdfDiffuseScreenRadianceHistory");
        private static readonly CameraHistoryId ScreenGeometryHistoryId = CameraHistoryId.Create("SdfDiffuseScreenGeometryHistory");
        private static readonly ProfilingSampler ScreenTraceSampler = new("Diffuse GI Screen Trace");
        private static readonly ProfilingSampler GatherSampler = new("Diffuse GI Gather and SDF Fallback");
        private static readonly ProfilingSampler TotalSampler = new("Diffuse GI Total");
        private static readonly ProfilingSampler PlacementSampler = new("Screen Probe Adaptive Placement");
        private static readonly ProfilingSampler FilterSampler = new("Screen Probe Spatial Filter");
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
        private static readonly int RecordRayDiagnosticsId = Shader.PropertyToID("_SdfDiffuseRecordRayDiagnostics");
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
        private static readonly int RealtimeOnlyId = Shader.PropertyToID("_SdfDiffuseRealtimeOnly");
        private static readonly int OffscreenOnlyId = Shader.PropertyToID("_SdfDiffuseOffscreenOnly");
        private static readonly int ViewProjectionId = Shader.PropertyToID("_SdfDiffuseVP");
        private static readonly int CardAlbedoDepthId = Shader.PropertyToID("_SdfSurfaceAlbedoDepth");
        private static readonly int CardNormalCoverageId = Shader.PropertyToID("_SdfSurfaceNormalCoverage");

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Depth = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);

        [RenderGraphResource(Name = "GBuffer0", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer0 = RenderGraphTexture.CreateInput("GBuffer0", GraphicsFormat.R8G8B8A8_SRGB);

        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1 = RenderGraphTexture.CreateInput("GBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);

        [RenderGraphResource(Name = "Screen Trace HZB", Access = AccessFlags.Read)]
        private RenderGraphTexture m_ScreenHzb;

        [RenderGraphResource(Name = "Screen Trace Motion", Access = AccessFlags.Read)]
        private RenderGraphTexture m_ScreenMotion;

        [RenderGraphResource(Name = "RealtimeDiffuseGI", Access = AccessFlags.Write)]
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
            internal bool AdaptiveProbes, SpatialFilter;
            internal bool PixelTemporal;
            internal Vector2 ScreenJitterUv;
            internal Vector4 LightPositionRange, LightColorShadow;
            internal uint SceneRevision;
            internal int Width, Height, LayerMask, LightLayerMask;
            internal float MaxDistance, RayBias;
            internal uint Frame;
            internal bool SunConfigured;
            internal ulong SunCardVersion;
            internal int SunReadyCards;

            public override void Dispose() { }
        }

        private readonly CameraRelativeSystem<HistoryState> m_Cameras = new();
        private readonly RenderGraphTexture m_PreviousProbes = RenderGraphTexture.CreateInput("Previous SDF Diffuse Probes", ProbeFormat);
        private readonly RenderGraphTexture m_PreviousGeometry = RenderGraphTexture.CreateInput("Previous SDF Diffuse Geometry", GeometryFormat);
        private readonly RenderGraphTexture m_Probes = CreateOutput("SDF Diffuse Probes", ProbeFormat);
        private readonly RenderGraphTexture m_Geometry = CreateOutput("SDF Diffuse Geometry", GeometryFormat);
        private readonly RenderGraphTexture m_PreviousPixelLighting = RenderGraphTexture.CreateInput("Previous SDF Diffuse Pixel Lighting", ProbeFormat);
        private readonly RenderGraphTexture m_PreviousPixelGeometry = RenderGraphTexture.CreateInput("Previous SDF Diffuse Pixel Geometry", PixelGeometryFormat);
        private readonly RenderGraphTexture m_CurrentPixelLighting = CreateOutput("SDF Diffuse Pixel Lighting History", ProbeFormat);
        private readonly RenderGraphTexture m_CurrentPixelGeometry = CreateOutput("SDF Diffuse Pixel Geometry History", PixelGeometryFormat);
        [RenderGraphResource(Name = "Raw Resolved Diffuse GI", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_RawResolved = CreateScratch("SDF Diffuse Raw Resolved", ProbeFormat);
        [RenderGraphResource(Name = "Probe Ray Diagnostics", Access = AccessFlags.Write)]
        [TransientResource]
        private readonly RenderGraphTexture m_RayDiagnostics = CreateScratch("SDF Diffuse Ray Diagnostics", GeometryFormat);
        [RenderGraphResource(Name = "Adaptive Probe Placement", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_AdaptivePlacement = CreateScratch("Screen Probe Adaptive Placement", GraphicsFormat.R32_SFloat);
        [RenderGraphResource(Name = "Filtered Probe Lighting", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private readonly RenderGraphTexture m_FilteredProbes = CreateScratch("Screen Probe Filtered Lighting", ProbeFormat);
        private readonly RenderGraphTexture m_LocalHzb = RenderGraphTexture.CreateInput("Screen Trace HZB", GraphicsFormat.R32_SFloat);
        private readonly RenderGraphTexture m_LocalMotion = RenderGraphTexture.CreateInput("Screen Trace Motion", GraphicsFormat.R16G16_SFloat);
        private readonly RenderGraphTexture m_ScreenRadianceDesc = CreateOutput("Screen Diffuse Source Radiance", ProbeFormat);
        private readonly RenderGraphTexture m_ScreenGeometryDesc = CreateOutput("Screen Diffuse Source Geometry", GraphicsFormat.R32G32_SFloat);
        private readonly RenderGraphTexture m_SunHistoryDesc = CreateOutput("Surface Cache Sun Visibility", SunVisibilityFormat);
        [RenderGraphResource(Name = "Screen Ray Samples", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_ScreenRays = CreateOutput("Diffuse GI Screen Rays", ProbeFormat);
        private VividMeshSdfGpuScene m_Scene;
        private ComputeShader m_Compute;
        private CameraHistoryTexture m_ProbeHistory, m_GeometryHistory;
        private CameraHistoryTexture m_PixelHistory, m_PixelGeometryHistory;
        private CameraHistoryTexture m_SunHistory;
        private HistoryState m_State;
        private Matrix4x4 m_ViewProjection;
        private TextureHandle m_CardAlbedoDepth, m_CardNormalCoverage;
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
        private int m_PlacementKernel = -1, m_FilterKernel = -1;
        private int m_PixelTemporalKernel = -1;
        private int m_Width, m_Height, m_ProbeWidth, m_ProbeHeight, m_LayerMask, m_LightLayerMask;
        private int m_AtlasHeight;
        private Vector4 m_ProbeCameraForward;
        private float m_TraceDistance, m_Bias;
        private bool m_HasHistory, m_RealtimeOnly, m_OffscreenOnly;
        private bool m_HasScreenHistory, m_UseSdf;
        private bool m_DirectionalProbes;
        private bool m_AdaptiveProbes, m_SpatialFilter;
        private bool m_RecordRayDiagnostics;
        private SdfDiffuseTraceMode m_TraceMode;

        public SdfDiffuseGatherPass()
        {
            profilingSampler = new ProfilingSampler(nameof(SdfDiffuseGatherPass));
            m_ScreenHzb = m_LocalHzb;
            m_ScreenMotion = m_LocalMotion;
            m_ScreenRays.desc.ClearBuffer = false;
        }

        public override void Create()
        {
            m_Compute = PipelineResourceManager.Get<VividRPCoreResources>()?.SdfDiffuseGatherCompute;
            if (m_Compute == null || !SystemInfo.supportsComputeShaders
                || !SystemInfo.IsFormatSupported(ProbeFormat, GraphicsFormatUsage.LoadStore)
                || !SystemInfo.IsFormatSupported(GeometryFormat, GraphicsFormatUsage.LoadStore)
                || !m_Compute.HasKernel("TraceSdfDiffuseProbes") || !m_Compute.HasKernel("ResolveSdfDiffuseGI"))
                return;

            m_TraceKernel = m_Compute.FindKernel("TraceSdfDiffuseProbes");
            m_ResolveKernel = m_Compute.FindKernel("ResolveSdfDiffuseGI");
            if (SystemInfo.usesReversedZBuffer
                && SystemInfo.IsFormatSupported(PixelGeometryFormat, GraphicsFormatUsage.LoadStore)
                && m_Compute.HasKernel("TemporalSdfDiffuseGI"))
                m_PixelTemporalKernel = m_Compute.FindKernel("TemporalSdfDiffuseGI");
            if (SystemInfo.IsFormatSupported(GraphicsFormat.R32_SFloat, GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample)
                && m_Compute.HasKernel("PlaceSdfAdaptiveProbes"))
                m_PlacementKernel = m_Compute.FindKernel("PlaceSdfAdaptiveProbes");
            if (m_Compute.HasKernel("FilterSdfDiffuseProbes"))
                m_FilterKernel = m_Compute.FindKernel("FilterSdfDiffuseProbes");
            if (SystemInfo.supports2DArrayTextures && m_Compute.HasKernel("TraceSdfDiffuseCachedProbes"))
                m_CachedTraceKernel = m_Compute.FindKernel("TraceSdfDiffuseCachedProbes");
            if (m_CachedTraceKernel >= 0
                && SystemInfo.IsFormatSupported(SunVisibilityFormat, GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample)
                && m_Compute.HasKernel("UpdateSdfSurfaceSunVisibility") && m_Compute.HasKernel("TraceSdfDiffuseCachedSunProbes"))
            {
                m_SunUpdateKernel = m_Compute.FindKernel("UpdateSdfSurfaceSunVisibility");
                m_CachedSunTraceKernel = m_Compute.FindKernel("TraceSdfDiffuseCachedSunProbes");
            }
            if (SystemInfo.usesReversedZBuffer
                && SystemInfo.IsFormatSupported(GraphicsFormat.R32G32_SFloat, GraphicsFormatUsage.LoadStore)
                && m_Compute.HasKernel("TraceDiffuseScreenRays") && m_Compute.HasKernel("GatherScreenDiffuseProbes"))
            {
                m_ScreenTraceKernel = m_Compute.FindKernel("TraceDiffuseScreenRays");
                m_ScreenGatherKernel = m_Compute.FindKernel("GatherScreenDiffuseProbes");
            }
            m_Scene = new VividMeshSdfGpuScene(Shader.Find("VividRP/Material/StandardLit"));
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var camera = cameraData.camera;
            m_State = null;
            m_ProbeHistory = m_GeometryHistory = null;
            m_PixelHistory = m_PixelGeometryHistory = null;
            m_SunHistory = null;
            m_SunAtlas = default;
            m_SunReadyCards = m_SunUpdateCards = 0;
            m_HasScreenHistory = false;
            m_ScreenJitterUv = default;
            m_Width = CameraDimensionUtility.ResolveCameraDimension(cameraData.actualWidth, cameraData.pixelWidth, Screen.width);
            m_Height = CameraDimensionUtility.ResolveCameraDimension(cameraData.actualHeight, cameraData.pixelHeight, Screen.height);
            m_ProbeWidth = CoreUtils.DivRoundUp(m_Width, ProbeSpacing);
            m_ProbeHeight = CoreUtils.DivRoundUp(m_Height, ProbeSpacing);
            var settings = VolumeManager.instance.stack.GetComponent<SdfDiffuseGISettings>();
            bool useSettings = settings != null && settings.active && settings.enabled.value;
            m_AdaptiveProbes = useSettings && m_PlacementKernel >= 0 && settings.adaptiveScreenProbes.value;
            m_SpatialFilter = useSettings && m_FilterKernel >= 0 && settings.screenProbeSpatialFilter.value;
            m_AtlasHeight = m_ProbeHeight * (m_AdaptiveProbes ? 2 : 1);
            m_Output.Resize(m_Width, m_Height);
            m_HitLighting.Resize(m_ProbeWidth, m_AtlasHeight);
            m_TraceSources.Resize(m_ProbeWidth, m_AtlasHeight);
            m_RecordRayDiagnostics = useSettings && settings.recordRayDiagnostics.value;
            m_RayDiagnostics.Resize(m_RecordRayDiagnostics ? m_ProbeWidth * RaysPerProbe : 1,
                m_RecordRayDiagnostics ? m_AtlasHeight : 1);
            bool motionConnected = m_ScreenMotion != null && !ReferenceEquals(m_ScreenMotion, m_LocalMotion);
            bool pixelTemporal = useSettings && settings.pixelTemporal.value && motionConnected && m_PixelTemporalKernel >= 0;
            m_RawResolved.Resize(pixelTemporal ? m_Width : 1, pixelTemporal ? m_Height : 1);
            m_SunCacheHits.Resize(1, 1);
            m_AdaptivePlacement.Resize(m_AdaptiveProbes ? m_ProbeWidth : 1, m_AdaptiveProbes ? m_ProbeHeight : 1);
            if (!m_SpatialFilter)
                m_FilteredProbes.Resize(1, 1);
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
            m_ScreenRays.Resize(screenConfigured ? m_ProbeWidth * RaysPerProbe : 1, screenConfigured ? m_AtlasHeight : 1);
            if (!m_UseSdf && !screenConfigured)
                return;

            m_ViewProjection = cameraData.GetGPUViewProjectionMatrix(renderIntoTexture: true);
            var view = cameraData.GetViewMatrix();
            m_ProbeCameraForward = -view.GetRow(2);
            m_LayerMask = camera.cullingMask;
            m_TraceDistance = float.IsFinite(m_MaxDistance) ? Mathf.Max(0.1f, m_MaxDistance) : 12f;
            m_Bias = float.IsFinite(m_RayBias) ? Mathf.Clamp(m_RayBias, 0.001f, 0.25f) : 0.04f;
            ReadLight();
            m_Cameras.PurgeDestroyedCameras();
            m_State = m_Cameras.GetOrCreateBase(camera);
            if (m_UseSdf && cache != null && settings.cacheSunVisibility.value
                && m_SunUpdateKernel >= 0 && m_LightPositionRange.w < 0f && m_LightColorShadow.w > 0f)
                PrepareSunCache(camera, cache);
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
            m_Probes.Resize(atlasWidth, m_AtlasHeight);
            if (m_SpatialFilter)
                m_FilteredProbes.Resize(atlasWidth, m_AtlasHeight);
            m_Geometry.Resize(m_ProbeWidth, m_AtlasHeight);
            bool probesValid = CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, ProbeHistoryId,
                m_PreviousProbes, m_Probes, m_Probes.desc, out m_ProbeHistory);
            bool geometryValid = CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, GeometryHistoryId,
                m_PreviousGeometry, m_Geometry, m_Geometry.desc, out m_GeometryHistory);
            m_HasHistory = probesValid && geometryValid && m_State.SceneRevision == m_Scene.Revision
                && ReferenceEquals(m_State.Cache, m_Scene.SurfaceCache)
                && m_State.OffscreenOnly == m_OffscreenOnly && m_State.TraceMode == m_TraceMode
                && m_State.DirectionalProbes == m_DirectionalProbes
                && m_State.AdaptiveProbes == m_AdaptiveProbes && m_State.SpatialFilter == m_SpatialFilter
                && m_State.SunConfigured == (m_SunHistory != null)
                && (m_SunHistory == null || m_State.SunCardVersion == m_SunCardVersion)
                && m_State.Width == m_Width && m_State.Height == m_Height
                && m_State.LayerMask == m_LayerMask && m_State.LightLayerMask == m_LightLayerMask
                && m_State.MaxDistance == m_TraceDistance && m_State.RayBias == m_Bias
                && m_State.LightPositionRange.Equals(m_LightPositionRange) && m_State.LightColorShadow.Equals(m_LightColorShadow)
                && !temporal.isFirstFrame && !temporal.resetPostProcessingHistory;
        }

        public override void Record(ComputePassContext context)
        {
            if (m_State == null || m_ProbeHistory == null || m_GeometryHistory == null
                || !m_Probes.innerHandle.IsValid() || !m_Geometry.innerHandle.IsValid()
                || !m_Output.innerHandle.IsValid() || !m_HitLighting.innerHandle.IsValid() || !m_TraceSources.innerHandle.IsValid()
                || !m_Depth.innerHandle.IsValid() || !m_GBuffer0.innerHandle.IsValid() || !m_GBuffer1.innerHandle.IsValid())
                return;

            var cmd = context.cmd;
            var cache = m_UseSdf ? m_Scene.SurfaceCache : null;
            bool useSunCache = m_SunAtlas.IsValid() && m_SunCacheHits.innerHandle.IsValid();
            int traceKernel = !m_UseSdf ? m_ScreenGatherKernel : cache != null ? m_CachedTraceKernel : m_TraceKernel;
            if (useSunCache)
                traceKernel = m_CachedSunTraceKernel;
            BindReceiver(cmd, traceKernel);
            BindReceiver(cmd, m_ResolveKernel, bindProbes: false);
            if (m_UseSdf)
            {
                m_Scene.Bind(cmd, m_Compute, traceKernel);
                cmd.SetComputeBufferParam(m_Compute, traceKernel, SurfaceId, m_Scene.DiffuseSurfaces);
            }
            if (cache != null)
            {
                cmd.SetComputeTextureParam(m_Compute, traceKernel, CardAlbedoDepthId, m_CardAlbedoDepth);
                cmd.SetComputeTextureParam(m_Compute, traceKernel, CardNormalCoverageId, m_CardNormalCoverage);
            }
            bool hasHistory = m_HasHistory && m_PreviousProbes.innerHandle.IsValid() && m_PreviousGeometry.innerHandle.IsValid();
            var black = context.renderGraphContext.defaultResources.blackTexture;
            var placement = m_AdaptiveProbes ? m_AdaptivePlacement.innerHandle : black;
            if ((m_AdaptiveProbes && !placement.IsValid()) || (m_SpatialFilter && !m_FilteredProbes.innerHandle.IsValid()))
                return;
            bool useScreenRays = m_HasScreenHistory && m_ScreenRays.innerHandle.IsValid()
                && m_ScreenHzb.innerHandle.IsValid() && m_ScreenMotion.innerHandle.IsValid();
            bool pixelTemporal = m_PixelHistory != null && m_PixelGeometryHistory != null && m_ScreenMotion.innerHandle.IsValid()
                && m_RawResolved.innerHandle.IsValid() && m_CurrentPixelLighting.innerHandle.IsValid()
                && m_CurrentPixelGeometry.innerHandle.IsValid() && m_PreviousPixelLighting.innerHandle.IsValid()
                && m_PreviousPixelGeometry.innerHandle.IsValid();
            hasHistory &= m_State.HasScreenRays == useScreenRays && m_State.PixelTemporal == pixelTemporal;
            bool useProbeHistory = hasHistory && !pixelTemporal;
            cmd.SetComputeTextureParam(m_Compute, traceKernel, PreviousProbeId, useProbeHistory ? m_PreviousProbes.innerHandle : black);
            cmd.SetComputeTextureParam(m_Compute, traceKernel, PreviousGeometryId, useProbeHistory ? m_PreviousGeometry.innerHandle : black);
            cmd.SetComputeTextureParam(m_Compute, traceKernel, HitLightingId, m_HitLighting.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, traceKernel, TraceSourcesId, m_TraceSources.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, traceKernel, RayDiagnosticsId, m_RayDiagnostics.innerHandle);
            cmd.SetComputeIntParam(m_Compute, RecordRayDiagnosticsId, m_RecordRayDiagnostics ? 1 : 0);
            cmd.SetComputeTextureParam(m_Compute, traceKernel, ScreenRaysId, useScreenRays ? m_ScreenRays.innerHandle : black);
            cmd.SetComputeTextureParam(m_Compute, traceKernel, PlacementId, placement);
            cmd.SetComputeTextureParam(m_Compute, m_ResolveKernel, PlacementId, placement);
            cmd.SetComputeTextureParam(m_Compute, m_ResolveKernel, GeometryId, m_Geometry.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, m_ResolveKernel, ProbeInputId,
                m_SpatialFilter ? m_FilteredProbes.innerHandle : m_Probes.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, m_ResolveKernel, OutputId,
                pixelTemporal ? m_RawResolved.innerHandle : m_Output.innerHandle);
            cmd.SetComputeMatrixParam(m_Compute, InverseViewProjectionId, m_ViewProjection.inverse);
            cmd.SetComputeMatrixParam(m_Compute, PreviousViewProjectionId, m_State.ViewProjection);
            cmd.SetComputeVectorParam(m_Compute, ScreenSizeId, new Vector4(m_Width, m_Height, 1f / m_Width, 1f / m_Height));
            cmd.SetComputeVectorParam(m_Compute, LightPositionRangeId, m_LightPositionRange);
            cmd.SetComputeVectorParam(m_Compute, LightColorShadowId, m_LightColorShadow);
            cmd.SetComputeIntParam(m_Compute, LightLayerMaskId, m_LightLayerMask);
            cmd.SetComputeFloatParam(m_Compute, MaxDistanceId, m_TraceDistance);
            cmd.SetComputeFloatParam(m_Compute, RayBiasId, m_Bias);
            cmd.SetComputeIntParam(m_Compute, LayerMaskId, m_LayerMask);
            cmd.SetComputeIntParam(m_Compute, FrameId, unchecked((int)m_State.Frame++));
            cmd.SetComputeIntParam(m_Compute, HasHistoryId, useProbeHistory ? 1 : 0);
            cmd.SetComputeIntParam(m_Compute, DirectionalProbesId, m_DirectionalProbes ? 1 : 0);
            cmd.SetComputeIntParam(m_Compute, AdaptiveProbesId, m_AdaptiveProbes ? 1 : 0);
            cmd.SetComputeIntParam(m_Compute, SpatialFilterId, m_SpatialFilter ? 1 : 0);
            cmd.SetComputeVectorParam(m_Compute, ProbeCameraForwardId, m_ProbeCameraForward);
            cmd.SetComputeIntParam(m_Compute, RealtimeOnlyId, m_RealtimeOnly && !pixelTemporal ? 1 : 0);
            cmd.SetComputeIntParam(m_Compute, OffscreenOnlyId, m_OffscreenOnly ? 1 : 0);
            cmd.SetComputeMatrixParam(m_Compute, ViewProjectionId, m_ViewProjection);
            cmd.SetComputeIntParam(m_Compute, UseScreenRaysId, useScreenRays ? 1 : 0);
            int groupsX = CoreUtils.DivRoundUp(m_ProbeWidth, GroupSize);
            int groupsY = CoreUtils.DivRoundUp(m_AtlasHeight, GroupSize);
            using (new ProfilingScope(cmd, TotalSampler))
            {
                if (m_AdaptiveProbes)
                {
                    using var scope = new ProfilingScope(cmd, PlacementSampler);
                    BindReceiver(cmd, m_PlacementKernel, bindProbes: false);
                    cmd.SetComputeTextureParam(m_Compute, m_PlacementKernel, PlacementOutputId, placement);
                    cmd.DispatchCompute(m_Compute, m_PlacementKernel, groupsX, CoreUtils.DivRoundUp(m_ProbeHeight, GroupSize), 1);
                }
                if (useSunCache)
                    RecordSunCache(cmd, traceKernel);
                if (useScreenRays)
                    RecordScreenTrace(cmd, groupsX, groupsY, placement);
                using (new ProfilingScope(cmd, GatherSampler))
                    cmd.DispatchCompute(m_Compute, traceKernel, groupsX, groupsY, 1);
                if (m_SpatialFilter)
                {
                    using var scope = new ProfilingScope(cmd, FilterSampler);
                    cmd.SetComputeTextureParam(m_Compute, m_FilterKernel, GeometryId, m_Geometry.innerHandle);
                    cmd.SetComputeTextureParam(m_Compute, m_FilterKernel, ProbeInputId, m_Probes.innerHandle);
                    cmd.SetComputeTextureParam(m_Compute, m_FilterKernel, FilteredProbesId, m_FilteredProbes.innerHandle);
                    cmd.DispatchCompute(m_Compute, m_FilterKernel, groupsX, groupsY, 1);
                }
                using (new ProfilingScope(cmd, ResolveSampler))
                    cmd.DispatchCompute(m_Compute, m_ResolveKernel, CoreUtils.DivRoundUp(m_Width, GroupSize),
                        CoreUtils.DivRoundUp(m_Height, GroupSize), 1);
                if (pixelTemporal)
                    RecordPixelTemporal(cmd, hasHistory && m_PixelHistory.IsValid() && m_PixelGeometryHistory.IsValid());
            }
#if UNITY_EDITOR
            EditorTraceCapture?.Invoke(context, m_TraceSources.innerHandle.ResolveTexture(),
                m_Output.innerHandle.ResolveTexture(), m_Geometry.innerHandle.ResolveTexture());
#endif
            m_ProbeHistory.MarkWritten();
            m_GeometryHistory.MarkWritten();
            m_State.ViewProjection = m_ViewProjection;
            m_State.Cache = m_Scene.SurfaceCache;
            m_State.OffscreenOnly = m_OffscreenOnly;
            m_State.TraceMode = m_TraceMode;
            m_State.HasScreenRays = useScreenRays;
            m_State.DirectionalProbes = m_DirectionalProbes;
            m_State.AdaptiveProbes = m_AdaptiveProbes;
            m_State.SpatialFilter = m_SpatialFilter;
            m_State.PixelTemporal = pixelTemporal;
            m_State.ScreenJitterUv = m_ScreenJitterUv;
            m_State.SceneRevision = m_Scene.Revision;
            m_State.Width = m_Width;
            m_State.Height = m_Height;
            m_State.LayerMask = m_LayerMask;
            m_State.LightLayerMask = m_LightLayerMask;
            m_State.MaxDistance = m_TraceDistance;
            m_State.RayBias = m_Bias;
            m_State.SunConfigured = m_SunHistory != null;
            m_State.SunReadyCards = useSunCache ? m_SunReadyCards : 0;
            m_State.SunCardVersion = m_SunCardVersion;
            m_State.LightPositionRange = m_LightPositionRange;
            m_State.LightColorShadow = m_LightColorShadow;
        }

        public override void Dispose()
        {
            m_Cameras.Dispose();
            m_Scene?.Dispose();
            m_Scene = null;
            m_Compute = null;
            m_TraceKernel = m_CachedTraceKernel = m_ResolveKernel = -1;
            m_ScreenTraceKernel = m_ScreenGatherKernel = -1;
            m_CachedSunTraceKernel = m_SunUpdateKernel = -1;
            m_PlacementKernel = m_FilterKernel = -1;
            m_PixelTemporalKernel = -1;
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
            cmd.SetComputeVectorParam(m_Compute, SunLayoutId, m_SunLayout);
            if (m_SunUpdateCards > 0)
            {
                using var scope = new ProfilingScope(cmd, SunUpdateSampler);
                int kernel = m_SunUpdateKernel;
                m_Scene.Bind(cmd, m_Compute, kernel);
                cmd.SetComputeBufferParam(m_Compute, kernel, SurfaceId, m_Scene.DiffuseSurfaces);
                cmd.SetComputeBufferParam(m_Compute, kernel, SunInstancesId, m_Scene.SurfaceLightingInstances);
                cmd.SetComputeTextureParam(m_Compute, kernel, CardAlbedoDepthId, m_CardAlbedoDepth);
                cmd.SetComputeTextureParam(m_Compute, kernel, CardNormalCoverageId, m_CardNormalCoverage);
                cmd.SetComputeTextureParam(m_Compute, kernel, SunVisibilityOutputId, m_SunAtlas);
                cmd.SetComputeIntParam(m_Compute, SunFirstCardId, m_SunReadyCards);
                cmd.DispatchCompute(m_Compute, kernel, CoreUtils.DivRoundUp(m_SunCardSize.x, GroupSize),
                    CoreUtils.DivRoundUp(m_SunCardSize.y, GroupSize), m_SunUpdateCards);
                m_SunReadyCards += m_SunUpdateCards;
            }

            cmd.SetComputeIntParam(m_Compute, SunReadyCardsId, m_SunReadyCards);
            cmd.SetComputeTextureParam(m_Compute, traceKernel, SunVisibilityId, m_SunAtlas);
            cmd.SetComputeTextureParam(m_Compute, traceKernel, SunCacheHitsId, m_SunCacheHits.innerHandle);
            m_SunHistory.MarkWritten();
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

        private void PreparePixelHistory(Camera camera)
        {
            m_CurrentPixelLighting.Resize(m_Width, m_Height);
            m_CurrentPixelGeometry.Resize(m_Width, m_Height);
            CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, PixelHistoryId,
                m_PreviousPixelLighting, m_CurrentPixelLighting, m_CurrentPixelLighting.desc, out m_PixelHistory, AccessFlags.Write);
            CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, PixelGeometryHistoryId,
                m_PreviousPixelGeometry, m_CurrentPixelGeometry, m_CurrentPixelGeometry.desc, out m_PixelGeometryHistory, AccessFlags.Write);
        }

        private void RecordPixelTemporal(ComputeCommandBuffer cmd, bool hasHistory)
        {
            using var scope = new ProfilingScope(cmd, PixelTemporalSampler);
            int kernel = m_PixelTemporalKernel;
            BindReceiver(cmd, kernel, bindProbes: false);
            cmd.SetComputeIntParam(m_Compute, HasHistoryId, hasHistory ? 1 : 0);
            cmd.SetComputeIntParam(m_Compute, RealtimeOnlyId, m_RealtimeOnly ? 1 : 0);
            cmd.SetComputeMatrixParam(m_Compute, PreviousInverseVpId, m_State.ViewProjection.inverse);
            cmd.SetComputeVectorParam(m_Compute, JitterDeltaId, new Vector4(m_ScreenJitterDelta.x, m_ScreenJitterDelta.y, 0f, 0f));
            cmd.SetComputeTextureParam(m_Compute, kernel, MotionId, m_ScreenMotion.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, kernel, CurrentResolvedId, m_RawResolved.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, kernel, PreviousPixelLightingId, m_PreviousPixelLighting.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, kernel, PreviousPixelGeometryId, m_PreviousPixelGeometry.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, kernel, PixelLightingOutputId, m_CurrentPixelLighting.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, kernel, PixelGeometryOutputId, m_CurrentPixelGeometry.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, kernel, OutputId, m_Output.innerHandle);
            cmd.DispatchCompute(m_Compute, kernel, CoreUtils.DivRoundUp(m_Width, GroupSize), CoreUtils.DivRoundUp(m_Height, GroupSize), 1);
            m_PixelHistory.MarkWritten();
            m_PixelGeometryHistory.MarkWritten();
        }

        private void RecordScreenTrace(ComputeCommandBuffer cmd, int groupsX, int groupsY, TextureHandle placement)
        {
            using var scope = new ProfilingScope(cmd, ScreenTraceSampler);
            int kernel = m_ScreenTraceKernel;
            BindReceiver(cmd, kernel, bindProbes: false);
            cmd.SetComputeTextureParam(m_Compute, kernel, PlacementId, placement);
            cmd.SetComputeTextureParam(m_Compute, kernel, HzbId, m_ScreenHzb.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, kernel, MotionId, m_ScreenMotion.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, kernel, ScreenRadianceId, m_PreviousScreenRadiance);
            cmd.SetComputeTextureParam(m_Compute, kernel, ScreenGeometryId, m_PreviousScreenGeometry);
            cmd.SetComputeTextureParam(m_Compute, kernel, ScreenRaysOutputId, m_ScreenRays.innerHandle);
            cmd.SetComputeMatrixParam(m_Compute, PreviousInverseVpId, m_State.ViewProjection.inverse);
            cmd.SetComputeVectorParam(m_Compute, JitterDeltaId, new Vector4(m_ScreenJitterDelta.x, m_ScreenJitterDelta.y, 0f, 0f));
            cmd.DispatchCompute(m_Compute, kernel, groupsX, groupsY, 1);
        }

        private void BindReceiver(ComputeCommandBuffer cmd, int kernel, bool bindProbes = true)
        {
            cmd.SetComputeTextureParam(m_Compute, kernel, DepthId, m_Depth.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, kernel, GBuffer0Id, m_GBuffer0.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, kernel, GBuffer1Id, m_GBuffer1.innerHandle);
            if (bindProbes)
            {
                cmd.SetComputeTextureParam(m_Compute, kernel, ProbeId, m_Probes.innerHandle);
                cmd.SetComputeTextureParam(m_Compute, kernel, GeometryId, m_Geometry.innerHandle);
            }
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
    }
}
