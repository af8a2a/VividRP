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
        private const int ProbeSpacing = 16; // Keep in sync with SdfDiffuseGather.compute.
        private const int GroupSize = 8;
        private const GraphicsFormat ProbeFormat = GraphicsFormat.R16G16B16A16_SFloat;
        private const GraphicsFormat GeometryFormat = GraphicsFormat.R32G32B32A32_SFloat;
        private static readonly CameraHistoryId ProbeHistoryId = CameraHistoryId.Create("SdfDiffuseProbeHistory");
        private static readonly CameraHistoryId GeometryHistoryId = CameraHistoryId.Create("SdfDiffuseGeometryHistory");
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

        [RenderGraphResource(Name = "RealtimeDiffuseGI", Access = AccessFlags.Write)]
        private RenderGraphTexture m_Output = CreateOutput("Realtime Diffuse GI", ProbeFormat);

        [RenderGraphResource(Name = "Probe Hit Lighting", Access = AccessFlags.Write)]
        private RenderGraphTexture m_HitLighting = CreateOutput("SDF Diffuse Probe Hit Lighting", ProbeFormat);

        [SerializeField, Min(0.1f)] private float m_MaxDistance = 12f;
        [SerializeField, Min(0.001f)] private float m_RayBias = 0.04f;
        [SerializeField, Min(1f)] private float m_SunShadowDistance = 64f;

        private sealed class HistoryState : CameraRelativeState
        {
            internal Matrix4x4 ViewProjection;
            internal VividSdfSurfaceCache Cache;
            internal bool OffscreenOnly;
            internal Vector4 LightPositionRange, LightColorShadow;
            internal uint SceneRevision;
            internal int Width, Height, LayerMask, LightLayerMask;
            internal float MaxDistance, RayBias;
            internal uint Frame;

            public override void Dispose() { }
        }

        private readonly CameraRelativeSystem<HistoryState> m_Cameras = new();
        private readonly RenderGraphTexture m_PreviousProbes = RenderGraphTexture.CreateInput("Previous SDF Diffuse Probes", ProbeFormat);
        private readonly RenderGraphTexture m_PreviousGeometry = RenderGraphTexture.CreateInput("Previous SDF Diffuse Geometry", GeometryFormat);
        private readonly RenderGraphTexture m_Probes = CreateOutput("SDF Diffuse Probes", ProbeFormat);
        private readonly RenderGraphTexture m_Geometry = CreateOutput("SDF Diffuse Geometry", GeometryFormat);
        private VividMeshSdfGpuScene m_Scene;
        private ComputeShader m_Compute;
        private CameraHistoryTexture m_ProbeHistory, m_GeometryHistory;
        private HistoryState m_State;
        private Matrix4x4 m_ViewProjection;
        private TextureHandle m_CardAlbedoDepth, m_CardNormalCoverage;
        private Vector4 m_LightPositionRange, m_LightColorShadow;
        private int m_TraceKernel = -1, m_CachedTraceKernel = -1, m_ResolveKernel = -1;
        private int m_Width, m_Height, m_ProbeWidth, m_ProbeHeight, m_LayerMask, m_LightLayerMask;
        private float m_TraceDistance, m_Bias;
        private bool m_HasHistory, m_RealtimeOnly, m_OffscreenOnly;

        public SdfDiffuseGatherPass() => profilingSampler = new ProfilingSampler(nameof(SdfDiffuseGatherPass));

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
            if (SystemInfo.supports2DArrayTextures && m_Compute.HasKernel("TraceSdfDiffuseCachedProbes"))
                m_CachedTraceKernel = m_Compute.FindKernel("TraceSdfDiffuseCachedProbes");
            m_Scene = new VividMeshSdfGpuScene(Shader.Find("VividRP/Material/StandardLit"));
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var camera = cameraData.camera;
            m_State = null;
            m_ProbeHistory = m_GeometryHistory = null;
            m_Width = CameraDimensionUtility.ResolveCameraDimension(cameraData.actualWidth, cameraData.pixelWidth, Screen.width);
            m_Height = CameraDimensionUtility.ResolveCameraDimension(cameraData.actualHeight, cameraData.pixelHeight, Screen.height);
            m_ProbeWidth = CoreUtils.DivRoundUp(m_Width, ProbeSpacing);
            m_ProbeHeight = CoreUtils.DivRoundUp(m_Height, ProbeSpacing);
            m_Output.Resize(m_Width, m_Height);
            m_HitLighting.Resize(m_ProbeWidth, m_ProbeHeight);
            if (m_TraceKernel < 0 || camera == null || camera.cullingMask == 0 || camera.stereoEnabled
                || DebugPassCameraUtility.ShouldSkipExecution(camera))
                return;

            var settings = VolumeManager.instance.stack.GetComponent<SdfDiffuseGISettings>();
            if (settings != null && settings.active && !settings.enabled.value)
                return;
            var cache = settings != null && settings.active ? settings.surfaceCache.value : null;
            if (cache != null && (m_CachedTraceKernel < 0 || !cache.IsUsable))
                return;
            m_Scene.SurfaceCache = cache;
            m_RealtimeOnly = settings != null && settings.active && settings.realtimeOnly.value;
            m_OffscreenOnly = settings != null && settings.active && settings.offscreenBounceOnly.value;
            m_Scene.Update(VividMeshletRendererDatabase.instance);
            if (m_Scene.NodeCount == 0 || m_Scene.DiffuseSurfaces == null)
                return;
            if (m_Scene.SurfaceCache != null)
            {
                m_CardAlbedoDepth = PassRecorder.ImportTextureForPass(this, cache.AlbedoDepthHandle);
                m_CardNormalCoverage = PassRecorder.ImportTextureForPass(this, cache.NormalCoverageHandle);
                if (!m_CardAlbedoDepth.IsValid() || !m_CardNormalCoverage.IsValid())
                    return;
            }

            m_ViewProjection = cameraData.GetGPUViewProjectionMatrix(renderIntoTexture: true);
            m_LayerMask = camera.cullingMask;
            m_TraceDistance = float.IsFinite(m_MaxDistance) ? Mathf.Max(0.1f, m_MaxDistance) : 12f;
            m_Bias = float.IsFinite(m_RayBias) ? Mathf.Clamp(m_RayBias, 0.001f, 0.25f) : 0.04f;
            ReadLight();
            m_Cameras.PurgeDestroyedCameras();
            m_State = m_Cameras.GetOrCreateBase(camera);
            m_Probes.Resize(m_ProbeWidth, m_ProbeHeight);
            m_Geometry.Resize(m_ProbeWidth, m_ProbeHeight);
            bool probesValid = CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, ProbeHistoryId,
                m_PreviousProbes, m_Probes, m_Probes.desc, out m_ProbeHistory);
            bool geometryValid = CameraHistoryRenderGraphBridge.PrepareTexturePair(this, camera, GeometryHistoryId,
                m_PreviousGeometry, m_Geometry, m_Geometry.desc, out m_GeometryHistory);
            m_HasHistory = probesValid && geometryValid && m_State.SceneRevision == m_Scene.Revision
                && ReferenceEquals(m_State.Cache, m_Scene.SurfaceCache)
                && m_State.OffscreenOnly == m_OffscreenOnly
                && m_State.Width == m_Width && m_State.Height == m_Height
                && m_State.LayerMask == m_LayerMask && m_State.LightLayerMask == m_LightLayerMask
                && m_State.MaxDistance == m_TraceDistance && m_State.RayBias == m_Bias
                && m_State.LightPositionRange.Equals(m_LightPositionRange) && m_State.LightColorShadow.Equals(m_LightColorShadow)
                && !frameData.Get<VividTemporalData>().isFirstFrame;
        }

        public override void Record(ComputePassContext context)
        {
            if (m_State == null || m_ProbeHistory == null || m_GeometryHistory == null
                || !m_Probes.innerHandle.IsValid() || !m_Geometry.innerHandle.IsValid()
                || !m_Output.innerHandle.IsValid() || !m_HitLighting.innerHandle.IsValid()
                || !m_Depth.innerHandle.IsValid() || !m_GBuffer0.innerHandle.IsValid() || !m_GBuffer1.innerHandle.IsValid())
                return;

            var cmd = context.cmd;
            var cache = m_Scene.SurfaceCache;
            int traceKernel = cache != null ? m_CachedTraceKernel : m_TraceKernel;
            BindReceiver(cmd, traceKernel);
            BindReceiver(cmd, m_ResolveKernel);
            m_Scene.Bind(cmd, m_Compute, traceKernel);
            cmd.SetComputeBufferParam(m_Compute, traceKernel, SurfaceId, m_Scene.DiffuseSurfaces);
            if (cache != null)
            {
                cmd.SetComputeTextureParam(m_Compute, traceKernel, CardAlbedoDepthId, m_CardAlbedoDepth);
                cmd.SetComputeTextureParam(m_Compute, traceKernel, CardNormalCoverageId, m_CardNormalCoverage);
            }
            bool hasHistory = m_HasHistory && m_PreviousProbes.innerHandle.IsValid() && m_PreviousGeometry.innerHandle.IsValid();
            var black = context.renderGraphContext.defaultResources.blackTexture;
            cmd.SetComputeTextureParam(m_Compute, traceKernel, PreviousProbeId, hasHistory ? m_PreviousProbes.innerHandle : black);
            cmd.SetComputeTextureParam(m_Compute, traceKernel, PreviousGeometryId, hasHistory ? m_PreviousGeometry.innerHandle : black);
            cmd.SetComputeTextureParam(m_Compute, traceKernel, HitLightingId, m_HitLighting.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, m_ResolveKernel, OutputId, m_Output.innerHandle);
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
            cmd.SetComputeIntParam(m_Compute, HasHistoryId, hasHistory ? 1 : 0);
            cmd.SetComputeIntParam(m_Compute, RealtimeOnlyId, m_RealtimeOnly ? 1 : 0);
            cmd.SetComputeIntParam(m_Compute, OffscreenOnlyId, m_OffscreenOnly ? 1 : 0);
            cmd.SetComputeMatrixParam(m_Compute, ViewProjectionId, m_ViewProjection);
            cmd.DispatchCompute(m_Compute, traceKernel, CoreUtils.DivRoundUp(m_ProbeWidth, GroupSize),
                CoreUtils.DivRoundUp(m_ProbeHeight, GroupSize), 1);
            cmd.DispatchCompute(m_Compute, m_ResolveKernel, CoreUtils.DivRoundUp(m_Width, GroupSize),
                CoreUtils.DivRoundUp(m_Height, GroupSize), 1);
            m_ProbeHistory.MarkWritten();
            m_GeometryHistory.MarkWritten();
            m_State.ViewProjection = m_ViewProjection;
            m_State.Cache = cache;
            m_State.OffscreenOnly = m_OffscreenOnly;
            m_State.SceneRevision = m_Scene.Revision;
            m_State.Width = m_Width;
            m_State.Height = m_Height;
            m_State.LayerMask = m_LayerMask;
            m_State.LightLayerMask = m_LightLayerMask;
            m_State.MaxDistance = m_TraceDistance;
            m_State.RayBias = m_Bias;
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
        }

        private void BindReceiver(ComputeCommandBuffer cmd, int kernel)
        {
            cmd.SetComputeTextureParam(m_Compute, kernel, DepthId, m_Depth.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, kernel, GBuffer0Id, m_GBuffer0.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, kernel, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, kernel, ProbeId, m_Probes.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, kernel, GeometryId, m_Geometry.innerHandle);
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
    }
}
