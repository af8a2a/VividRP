using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Phase S2 Strand-Space Per-Vertex Shading Pass (IJGC SIGGRAPH 2025):
    /// Shades only visible strand vertices (marked by StrandHairVisibilityPass) using
    /// VividRP's Karis Marschner + Yan Medulla + Zinke Dual Scattering BCSDF
    /// (VividHairFurBSDF.hlsl), VividRP's Clustered Lighting light buffers,
    /// world-space CSM shadow atlas, Sky IBL cubemap, and baked Biased L1 SH self-shadowing.
    /// Implements IAsyncComputeSupportedPass for async compute overlap.
    /// </summary>
    public sealed class StrandHairShadingPass : ComputePass, IAsyncComputeSupportedPass
    {
        private const string ShadeKernelName = "KShadeHairVertices";
        private const string ShadeCompactKernelName = "KShadeCompactHairVertices";
        public const float DefaultSelfShadowExtinction = 0.72f;

        private static readonly int RestVerticesId = Shader.PropertyToID("_HairRestVertices");
        private static readonly int VisibilitySHId = Shader.PropertyToID("_HairVisibilitySH");
        private static readonly int WorldPosRadiusId = Shader.PropertyToID("_HairWorldPosRadius");
        private static readonly int PostTransformClipId = Shader.PropertyToID("_HairPostTransformClip");
        private static readonly int VisibleVertexMaskId = Shader.PropertyToID("_HairVisibleVertexMask");
        private static readonly int VisibleVertexListId = Shader.PropertyToID("_HairVisibleVertexList");
        private static readonly int IndirectShadeArgsId = Shader.PropertyToID("_HairIndirectShadeArgs");
        private static readonly int VertexShadingId = Shader.PropertyToID("_HairVertexShading");

        private static readonly int DirectionalLightsId = Shader.PropertyToID("_DirectionalLights");
        private static readonly int PunctualLightsId = Shader.PropertyToID("_PunctualLights");
        private static readonly int AreaLightsId = Shader.PropertyToID("_AreaLights");
        private static readonly int ReflectionProbesId = Shader.PropertyToID("_ReflectionProbes");
        private static readonly int LayeredLightListId = Shader.PropertyToID("g_vLayeredLightList");
        private static readonly int LayeredOffsetId = Shader.PropertyToID("g_LayeredOffset");
        private static readonly int LogBaseBufferId = Shader.PropertyToID("g_logBaseBuffer");
        private static readonly int DirectionalLightCountId = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int MainDirectionalLightIndexId = Shader.PropertyToID("_MainDirectionalLightIndex");
        private static readonly int PunctualLightCountId = Shader.PropertyToID("_HairPunctualLightCount");
        private static readonly int AreaLightCountId = Shader.PropertyToID("_HairAreaLightCount");
        private static readonly int ReflectionProbeCountId = Shader.PropertyToID("_ReflectionProbeCount");
        private static readonly int ClusteredPunctualLightGridEnabledId = Shader.PropertyToID("_ClusteredPunctualLightGridEnabled");
        private static readonly int ClusteredAreaLightGridEnabledId = Shader.PropertyToID("_ClusteredAreaLightGridEnabled");
        private static readonly int ClusteredReflectionProbeGridEnabledId = Shader.PropertyToID("_ClusteredReflectionProbeGridEnabled");
        private static readonly int ClusterScaleId = Shader.PropertyToID("g_fClustScale");
        private static readonly int ClusterBaseId = Shader.PropertyToID("g_fClustBase");
        private static readonly int NearPlaneId = Shader.PropertyToID("g_fNearPlane");
        private static readonly int FarPlaneId = Shader.PropertyToID("g_fFarPlane");
        private static readonly int Log2NumClustersId = Shader.PropertyToID("g_iLog2NumClusters");
        private static readonly int IsLogBaseBufferEnabledId = Shader.PropertyToID("g_isLogBaseBufferEnabled");
        private static readonly int NumTileClusteredXId = Shader.PropertyToID("_NumTileClusteredX");
        private static readonly int NumTileClusteredYId = Shader.PropertyToID("_NumTileClusteredY");
        private static readonly int ClusterTileSizeId = Shader.PropertyToID("_ClusterTileSize");
        private static readonly int ClusterSliceCountId = Shader.PropertyToID("_ClusterSliceCount");
        private static readonly int ClusterTileCountXId = Shader.PropertyToID("_ClusterTileCountX");
        private static readonly int ClusterTileCountYId = Shader.PropertyToID("_ClusterTileCountY");
        private static readonly int ClusterNearClipId = Shader.PropertyToID("_ClusterNearClip");
        private static readonly int ClusterFarClipId = Shader.PropertyToID("_ClusterFarClip");
        private static readonly int ClusterIsOrthographicId = Shader.PropertyToID("_ClusterIsOrthographic");
        private static readonly int CameraPosWSId = Shader.PropertyToID("_HairCameraPosWS");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_HairScreenSize");
        private static readonly int ViewMatrixId = Shader.PropertyToID("_HairViewMatrix");

        private static readonly int CSMShadowAtlasId = Shader.PropertyToID("_CSMShadowAtlas");
        private static readonly int CSMViewProjMatricesId = Shader.PropertyToID("_CSMViewProjMatrices");
        private static readonly int CSMCascadeSpheresId = Shader.PropertyToID("_CSMCascadeSpheres");
        private static readonly int CSMCascadeCountId = Shader.PropertyToID("_CSMCascadeCount");
        private static readonly int CSMMaxShadowDistanceId = Shader.PropertyToID("_CSMMaxShadowDistance");
        private static readonly int CSMCascadeResolutionId = Shader.PropertyToID("_CSMCascadeResolution");
        private static readonly int CSMNormalBiasId = Shader.PropertyToID("_CSMNormalBias");

        private static readonly int SkyTextureId = Shader.PropertyToID("_SkyTexture");
        private static readonly int SkyTextureTintId = Shader.PropertyToID("_SkyTextureTint");
        private static readonly int SkyTextureParamsId = Shader.PropertyToID("_SkyTextureParams");
        private static readonly int FallbackAmbientColorId = Shader.PropertyToID("_HairFallbackAmbientColor");

        private static readonly int LocalToWorldId = Shader.PropertyToID("_HairLocalToWorld");
        private static readonly int WorldToLocalId = Shader.PropertyToID("_HairWorldToLocal");
        private static readonly int VertexCountId = Shader.PropertyToID("_HairVertexCount");
        private static readonly int HasBakedSHId = Shader.PropertyToID("_HairHasBakedSH");
        private static readonly int BoundsCenterWSId = Shader.PropertyToID("_HairBoundsCenterWS");

        private static readonly int AbsorptionModeId = Shader.PropertyToID("_HairAbsorptionMode");
        private static readonly int BaseColorId = Shader.PropertyToID("_HairBaseColor");
        private static readonly int MelaninParamsId = Shader.PropertyToID("_HairMelaninParams");
        private static readonly int RoughnessParamsId = Shader.PropertyToID("_HairRoughnessParams");
        private static readonly int MedullaParamsId = Shader.PropertyToID("_HairMedullaParams");

        private ComputeShader m_Compute;
        private int m_ShadeKernel = -1;
        private int m_ShadeCompactKernel = -1;

        private StrandHairFrameData m_FrameData;
        private StrandHairGpuPool m_GpuPool;
        private GraphicsBuffer m_PreExposureBuffer;

        private GraphicsBuffer m_DummyDirectionalLightBuffer;
        private GraphicsBuffer m_DummyPunctualLightBuffer;
        private GraphicsBuffer m_DummyAreaLightBuffer;
        private GraphicsBuffer m_DummyReflectionProbeBuffer;
        private GraphicsBuffer m_DummyUintBuffer;
        private GraphicsBuffer m_DummyFloatBuffer;

        private Vector3 m_CameraPosWS;
        private Matrix4x4 m_ViewMatrix = Matrix4x4.identity;
        private Vector2 m_ScreenSize = new Vector2(1.0f, 1.0f);
        private int m_DirectionalLightCount;
        private int m_MainDirectionalLightIndex = -1;
        private int m_PunctualLightCount;
        private int m_AreaLightCount;
        private int m_ReflectionProbeCount;
        private bool m_SupportsClusteredPunctualLights;
        private bool m_SupportsClusteredAreaLights;
        private bool m_SupportsClusteredReflectionProbes;
        private bool m_IsLogBaseBufferEnabled;
        private int m_ClusterTileSize = LightGridPass.ClusterTileSize;
        private int m_ClusterSliceCount = LightGridPass.ClusterSliceCount;
        private int m_ClusterTileCountX = 1;
        private int m_ClusterTileCountY = 1;
        private float m_ClusterNearClip = 0.1f;
        private float m_ClusterFarClip = 1000.0f;
        private int m_ClusterIsOrthographic;
        private float m_ClusterScale;
        private float m_ClusterBase = LightGridPass.ClusterLogBase;
        private int m_ClusterLog2SliceCount = LightGridPass.ClusterLog2SliceCount;
        private Color m_SkyTextureTint = Color.white;
        private Vector4 m_SkyTextureParams;
        private Vector3 m_FallbackAmbientColor = new Vector3(0.06f, 0.065f, 0.075f);

        private readonly Matrix4x4[] m_CSMViewProjMatrices = new Matrix4x4[VividShadowData.MaxCascadeCount];
        private readonly Vector4[] m_CSMCascadeSpheres = new Vector4[VividShadowData.MaxCascadeCount];
        private int m_CSMCascadeCount;
        private float m_CSMMaxShadowDistance;
        private int m_CSMCascadeResolution;
        private float m_CSMNormalBias;

        public static float GlobalSelfShadowExtinction { get; set; } = DefaultSelfShadowExtinction;
        public static bool EnableClusteredLightGrid { get; set; } = true;

        public StrandHairShadingPass()
        {
            profilingSampler = new ProfilingSampler(nameof(StrandHairShadingPass));
        }

        public override void Create()
        {
            EnsureShaderLoaded();
            EnsureDummyLightBuffers();
        }

        private bool EnsureShaderLoaded()
        {
            if (m_Compute != null && m_ShadeKernel >= 0 && m_ShadeCompactKernel >= 0)
                return true;

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_Compute = resources?.StrandHairShadingCompute;
            if (m_Compute == null)
                return false;

            m_ShadeKernel = m_Compute.FindKernel(ShadeKernelName);
            m_ShadeCompactKernel = m_Compute.FindKernel(ShadeCompactKernelName);
            return m_ShadeKernel >= 0 && m_ShadeCompactKernel >= 0;
        }

        private void EnsureDummyLightBuffers()
        {
            if (m_DummyDirectionalLightBuffer == null || !m_DummyDirectionalLightBuffer.IsValid())
            {
                m_DummyDirectionalLightBuffer?.Dispose();
                m_DummyDirectionalLightBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    1,
                    VividLightData.DirectionalLightData.Stride);
            }

            if (m_DummyPunctualLightBuffer == null || !m_DummyPunctualLightBuffer.IsValid())
            {
                m_DummyPunctualLightBuffer?.Dispose();
                m_DummyPunctualLightBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    1,
                    VividLightData.PunctualLightData.Stride);
            }

            if (m_DummyAreaLightBuffer == null || !m_DummyAreaLightBuffer.IsValid())
            {
                m_DummyAreaLightBuffer?.Dispose();
                m_DummyAreaLightBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    1,
                    VividLightData.AreaLightData.Stride);
            }

            if (m_DummyReflectionProbeBuffer == null || !m_DummyReflectionProbeBuffer.IsValid())
            {
                m_DummyReflectionProbeBuffer?.Dispose();
                m_DummyReflectionProbeBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    1,
                    VividLightData.ReflectionProbeData.Stride);
            }

            if (m_DummyUintBuffer == null || !m_DummyUintBuffer.IsValid())
            {
                m_DummyUintBuffer?.Dispose();
                m_DummyUintBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    1,
                    sizeof(uint));
            }

            if (m_DummyFloatBuffer == null || !m_DummyFloatBuffer.IsValid())
            {
                m_DummyFloatBuffer?.Dispose();
                m_DummyFloatBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    1,
                    sizeof(float));
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            PrepareInternal(frameData, null, null);
        }

        internal bool PrepareInternal(
            ContextContainer frameData,
            StrandHairGpuPool gpuPool,
            DeferredLightingPass deferredLightingPass)
        {
            m_FrameData = frameData != null && frameData.Contains<StrandHairFrameData>()
                ? frameData.Get<StrandHairFrameData>()
                : null;
            m_GpuPool = gpuPool;

            if (m_FrameData == null || !m_FrameData.HasVisibleHair || !EnsureShaderLoaded())
                return false;

            var cameraData = frameData.Get<VividCameraData>();
            if (cameraData == null || cameraData.camera == null)
                return false;

            EnsureDummyLightBuffers();

            m_CameraPosWS = cameraData.camera.transform.position;
            m_ViewMatrix = cameraData.GetViewMatrix();
            int screenWidth = m_FrameData.ScreenWidth > 0
                ? m_FrameData.ScreenWidth
                : (cameraData.actualWidth > 0 ? cameraData.actualWidth : Mathf.Max(cameraData.pixelWidth, 1));
            int screenHeight = m_FrameData.ScreenHeight > 0
                ? m_FrameData.ScreenHeight
                : (cameraData.actualHeight > 0 ? cameraData.actualHeight : Mathf.Max(cameraData.pixelHeight, 1));
            m_ScreenSize = new Vector2(screenWidth, screenHeight);
            m_PreExposureBuffer = VividAutoExposureSystem.ResolvePreExposureBuffer(
                frameData.Get<VividExposureData>());

            // Read exact scene light counts, Clustered LightGrid state & Sky IBL prepared by DeferredLightingPass / ClusteredLightingData
            var clustered = frameData.Contains<VividClusteredLightingData>()
                ? frameData.Get<VividClusteredLightingData>()
                : null;

            if (deferredLightingPass != null)
            {
                m_DirectionalLightCount = Mathf.Max(0, deferredLightingPass.DirectionalLightCount);
                m_MainDirectionalLightIndex = deferredLightingPass.MainDirectionalLightIndex;
                m_PunctualLightCount = Mathf.Max(0, deferredLightingPass.PunctualLightCount);
                m_AreaLightCount = Mathf.Max(0, deferredLightingPass.AreaLightCount);
                m_ReflectionProbeCount = Mathf.Max(0, deferredLightingPass.ReflectionProbeCount);
                m_SupportsClusteredPunctualLights = deferredLightingPass.SupportsClusteredPunctualLights;
                m_SupportsClusteredAreaLights = deferredLightingPass.SupportsClusteredAreaLights;
                m_SupportsClusteredReflectionProbes = deferredLightingPass.SupportsClusteredReflectionProbes;
                m_IsLogBaseBufferEnabled = deferredLightingPass.IsLogBaseBufferEnabled;
                m_ClusterTileSize = Mathf.Max(1, deferredLightingPass.ClusterTileSize);
                m_ClusterSliceCount = Mathf.Max(1, deferredLightingPass.ClusterSliceCount);
                m_ClusterTileCountX = Mathf.Max(1, deferredLightingPass.ClusterTileCountX);
                m_ClusterTileCountY = Mathf.Max(1, deferredLightingPass.ClusterTileCountY);
                m_ClusterNearClip = Mathf.Max(deferredLightingPass.ClusterNearClip, 0.0001f);
                m_ClusterFarClip = Mathf.Max(deferredLightingPass.ClusterFarClip, m_ClusterNearClip + 0.01f);
                m_ClusterIsOrthographic = deferredLightingPass.ClusterIsOrthographic;
                m_ClusterScale = deferredLightingPass.ClusterScale;
                m_ClusterBase = deferredLightingPass.ClusterBase > 1.0f ? deferredLightingPass.ClusterBase : LightGridPass.ClusterLogBase;
                m_ClusterLog2SliceCount = deferredLightingPass.ClusterLog2SliceCount > 0 ? deferredLightingPass.ClusterLog2SliceCount : LightGridPass.ClusterLog2SliceCount;
                m_SkyTextureTint = deferredLightingPass.SkyTextureTint;
                m_SkyTextureParams = deferredLightingPass.SkyTextureParams;
            }
            else if (clustered != null)
            {
                m_DirectionalLightCount = Mathf.Max(0, clustered.directionalLightCount);
                m_MainDirectionalLightIndex = clustered.mainDirectionalLightIndex;
                m_PunctualLightCount = Mathf.Max(0, clustered.punctualLightCount);
                m_AreaLightCount = Mathf.Max(0, clustered.areaLightCount);
                m_ReflectionProbeCount = Mathf.Max(0, clustered.reflectionProbeCount);
                m_SupportsClusteredPunctualLights = false;
                m_SupportsClusteredAreaLights = false;
                m_SupportsClusteredReflectionProbes = false;
                m_IsLogBaseBufferEnabled = false;
                m_ClusterTileSize = clustered.clusterTileSize > 0 ? clustered.clusterTileSize : LightGridPass.ClusterTileSize;
                m_ClusterSliceCount = clustered.clusterSliceCount > 0 ? clustered.clusterSliceCount : LightGridPass.ClusterSliceCount;
                m_ClusterTileCountX = clustered.clusterTileCountX > 0
                    ? clustered.clusterTileCountX
                    : Mathf.Max(1, Mathf.CeilToInt(screenWidth / (float)m_ClusterTileSize));
                m_ClusterTileCountY = clustered.clusterTileCountY > 0
                    ? clustered.clusterTileCountY
                    : Mathf.Max(1, Mathf.CeilToInt(screenHeight / (float)m_ClusterTileSize));
                m_ClusterNearClip = Mathf.Max(clustered.clusterNearClip > 0.0f ? clustered.clusterNearClip : cameraData.camera.nearClipPlane, 0.0001f);
                m_ClusterFarClip = Mathf.Max(clustered.clusterFarClip > m_ClusterNearClip ? clustered.clusterFarClip : cameraData.camera.farClipPlane, m_ClusterNearClip + 0.01f);
                m_ClusterIsOrthographic = clustered.clusterIsOrthographic;
                m_ClusterScale = clustered.clusterScale;
                m_ClusterBase = clustered.clusterBase > 1.0f ? clustered.clusterBase : LightGridPass.ClusterLogBase;
                m_ClusterLog2SliceCount = clustered.clusterLog2SliceCount > 0 ? clustered.clusterLog2SliceCount : LightGridPass.ClusterLog2SliceCount;
                m_SkyTextureTint = Color.white;
                m_SkyTextureParams = Vector4.zero;
            }
            else
            {
                m_DirectionalLightCount = 0;
                m_MainDirectionalLightIndex = -1;
                m_PunctualLightCount = 0;
                m_AreaLightCount = 0;
                m_ReflectionProbeCount = 0;
                m_SupportsClusteredPunctualLights = false;
                m_SupportsClusteredAreaLights = false;
                m_SupportsClusteredReflectionProbes = false;
                m_IsLogBaseBufferEnabled = false;
                m_ClusterTileSize = LightGridPass.ClusterTileSize;
                m_ClusterSliceCount = LightGridPass.ClusterSliceCount;
                m_ClusterTileCountX = Mathf.Max(1, Mathf.CeilToInt(screenWidth / (float)m_ClusterTileSize));
                m_ClusterTileCountY = Mathf.Max(1, Mathf.CeilToInt(screenHeight / (float)m_ClusterTileSize));
                m_ClusterNearClip = Mathf.Max(cameraData.camera.nearClipPlane, 0.0001f);
                m_ClusterFarClip = Mathf.Max(cameraData.camera.farClipPlane, m_ClusterNearClip + 0.01f);
                m_ClusterIsOrthographic = cameraData.camera.orthographic ? 1 : 0;
                m_ClusterScale = 0.0f;
                m_ClusterBase = LightGridPass.ClusterLogBase;
                m_ClusterLog2SliceCount = LightGridPass.ClusterLog2SliceCount;
                m_SkyTextureTint = Color.white;
                m_SkyTextureParams = Vector4.zero;
            }

            // World-space CSM shadow metadata (never uses screen-space opaque shadow mask)
            m_CSMCascadeCount = 0;
            m_CSMMaxShadowDistance = 0.0f;
            m_CSMCascadeResolution = 0;
            m_CSMNormalBias = 0.0f;
            if (frameData.Contains<VividShadowData>())
            {
                var shadowData = frameData.Get<VividShadowData>();
                if (shadowData != null && shadowData.cascadeCount > 0 && shadowData.cascadeResolution > 0)
                {
                    m_CSMCascadeCount = Mathf.Min(shadowData.cascadeCount, VividShadowData.MaxCascadeCount);
                    m_CSMMaxShadowDistance = Mathf.Max(shadowData.maxShadowDistance, 0.0f);
                    m_CSMCascadeResolution = shadowData.cascadeResolution;
                    m_CSMNormalBias = Mathf.Max(shadowData.normalBias, 0.0f);
                    for (int i = 0; i < VividShadowData.MaxCascadeCount; i++)
                    {
                        m_CSMViewProjMatrices[i] = shadowData.viewProjMatrices[i];
                        m_CSMCascadeSpheres[i] = shadowData.cascadeSpheres[i];
                    }
                }
            }

            // Subtle neutral studio ambient fallback when no active sky cubemap is present
            Color envAmb = RenderSettings.ambientLight.linear * RenderSettings.ambientIntensity;
            float ambLum = 0.2126f * envAmb.r + 0.7152f * envAmb.g + 0.0722f * envAmb.b;
            m_FallbackAmbientColor = ambLum > 1e-4f
                ? new Vector3(envAmb.r, envAmb.g, envAmb.b) * 0.25f
                : new Vector3(0.055f, 0.060f, 0.068f);

            return true;
        }

        public override void Record(ComputePassContext context)
        {
            // Explicit graph entry point uses default handles if wired standalone
        }

        internal void RecordWithHandles(
            ComputeCommandBuffer cmd,
            RenderGraphDefaultResources defaultResources,
            StrandHairGpuPool gpuPool,
            BufferHandle directionalLights,
            BufferHandle punctualLights,
            BufferHandle areaLights,
            BufferHandle reflectionProbes,
            BufferHandle layeredLightList,
            BufferHandle layeredOffset,
            BufferHandle logBaseBuffer,
            TextureHandle csmShadowAtlas,
            TextureHandle skyIblCubemap)
        {
            if (m_FrameData == null
                || !m_FrameData.HasVisibleHair
                || !m_FrameData.VisibilityRecordedThisFrame
                || gpuPool == null
                || !EnsureShaderLoaded())
            {
                return;
            }

            EnsureDummyLightBuffers();

            bool useCompact = StrandHairGpuPool.EnablePhaseS3Optimizations && StrandHairGpuPool.EnableCompactShading;
            int activeKernel = useCompact ? m_ShadeCompactKernel : m_ShadeKernel;

            using (new ProfilingScope(cmd, profilingSampler))
            {
                if (m_PreExposureBuffer != null)
                {
                    cmd.SetComputeBufferParam(
                        m_Compute,
                        activeKernel,
                        VividAutoExposureSystem.PreExposureBufferId,
                        m_PreExposureBuffer);
                }

                // Bind scene light buffers (use dummy buffer when count == 0 or handle invalid)
                bool hasDir = directionalLights.IsValid() && m_DirectionalLightCount > 0;
                bool hasPunc = punctualLights.IsValid() && m_PunctualLightCount > 0;
                bool hasArea = areaLights.IsValid() && m_AreaLightCount > 0;
                bool hasProbes = reflectionProbes.IsValid() && m_ReflectionProbeCount > 0;
                bool hasClusterGridBuffers = layeredLightList.IsValid() && layeredOffset.IsValid();
                bool allowClustered = EnableClusteredLightGrid && hasClusterGridBuffers;

                bool useClusteredPunctual = allowClustered && hasPunc && m_SupportsClusteredPunctualLights;
                bool useClusteredArea = allowClustered && hasArea && m_SupportsClusteredAreaLights;
                bool useClusteredProbes = allowClustered && hasProbes && m_SupportsClusteredReflectionProbes;
                bool useLogBase = allowClustered && m_IsLogBaseBufferEnabled && logBaseBuffer.IsValid();

                if (hasDir)
                    cmd.SetComputeBufferParam(m_Compute, activeKernel, DirectionalLightsId, directionalLights);
                else
                    cmd.SetComputeBufferParam(m_Compute, activeKernel, DirectionalLightsId, m_DummyDirectionalLightBuffer);

                if (hasPunc)
                    cmd.SetComputeBufferParam(m_Compute, activeKernel, PunctualLightsId, punctualLights);
                else
                    cmd.SetComputeBufferParam(m_Compute, activeKernel, PunctualLightsId, m_DummyPunctualLightBuffer);

                if (hasArea)
                    cmd.SetComputeBufferParam(m_Compute, activeKernel, AreaLightsId, areaLights);
                else
                    cmd.SetComputeBufferParam(m_Compute, activeKernel, AreaLightsId, m_DummyAreaLightBuffer);

                if (hasProbes)
                    cmd.SetComputeBufferParam(m_Compute, activeKernel, ReflectionProbesId, reflectionProbes);
                else
                    cmd.SetComputeBufferParam(m_Compute, activeKernel, ReflectionProbesId, m_DummyReflectionProbeBuffer);

                if (hasClusterGridBuffers)
                {
                    cmd.SetComputeBufferParam(m_Compute, activeKernel, LayeredLightListId, layeredLightList);
                    cmd.SetComputeBufferParam(m_Compute, activeKernel, LayeredOffsetId, layeredOffset);
                }
                else
                {
                    cmd.SetComputeBufferParam(m_Compute, activeKernel, LayeredLightListId, m_DummyUintBuffer);
                    cmd.SetComputeBufferParam(m_Compute, activeKernel, LayeredOffsetId, m_DummyUintBuffer);
                }

                if (useLogBase)
                    cmd.SetComputeBufferParam(m_Compute, activeKernel, LogBaseBufferId, logBaseBuffer);
                else
                    cmd.SetComputeBufferParam(m_Compute, activeKernel, LogBaseBufferId, m_DummyFloatBuffer);

                cmd.SetComputeIntParam(m_Compute, DirectionalLightCountId, hasDir ? m_DirectionalLightCount : 0);
                cmd.SetComputeIntParam(m_Compute, MainDirectionalLightIndexId, hasDir ? m_MainDirectionalLightIndex : -1);
                cmd.SetComputeIntParam(m_Compute, PunctualLightCountId, hasPunc ? m_PunctualLightCount : 0);
                cmd.SetComputeIntParam(m_Compute, AreaLightCountId, hasArea ? m_AreaLightCount : 0);
                cmd.SetComputeIntParam(m_Compute, ReflectionProbeCountId, hasProbes ? m_ReflectionProbeCount : 0);

                cmd.SetComputeIntParam(m_Compute, ClusteredPunctualLightGridEnabledId, useClusteredPunctual ? 1 : 0);
                cmd.SetComputeIntParam(m_Compute, ClusteredAreaLightGridEnabledId, useClusteredArea ? 1 : 0);
                cmd.SetComputeIntParam(m_Compute, ClusteredReflectionProbeGridEnabledId, useClusteredProbes ? 1 : 0);
                cmd.SetComputeIntParam(m_Compute, ClusterTileSizeId, m_ClusterTileSize);
                cmd.SetComputeIntParam(m_Compute, ClusterSliceCountId, m_ClusterSliceCount);
                cmd.SetComputeIntParam(m_Compute, ClusterTileCountXId, m_ClusterTileCountX);
                cmd.SetComputeIntParam(m_Compute, ClusterTileCountYId, m_ClusterTileCountY);
                cmd.SetComputeIntParam(m_Compute, ClusterIsOrthographicId, m_ClusterIsOrthographic);
                cmd.SetComputeFloatParam(m_Compute, ClusterNearClipId, m_ClusterNearClip);
                cmd.SetComputeFloatParam(m_Compute, ClusterFarClipId, m_ClusterFarClip);
                cmd.SetComputeFloatParam(m_Compute, ClusterScaleId, m_ClusterScale);
                cmd.SetComputeFloatParam(m_Compute, ClusterBaseId, m_ClusterBase);
                cmd.SetComputeFloatParam(m_Compute, NearPlaneId, m_ClusterNearClip);
                cmd.SetComputeFloatParam(m_Compute, FarPlaneId, m_ClusterFarClip);
                cmd.SetComputeIntParam(m_Compute, Log2NumClustersId, m_ClusterLog2SliceCount);
                cmd.SetComputeIntParam(m_Compute, IsLogBaseBufferEnabledId, useLogBase ? 1 : 0);
                cmd.SetComputeIntParam(m_Compute, NumTileClusteredXId, m_ClusterTileCountX);
                cmd.SetComputeIntParam(m_Compute, NumTileClusteredYId, m_ClusterTileCountY);

                cmd.SetComputeVectorParam(m_Compute, CameraPosWSId, m_CameraPosWS);
                cmd.SetComputeVectorParam(m_Compute, ScreenSizeId, m_ScreenSize);
                cmd.SetComputeMatrixParam(m_Compute, ViewMatrixId, m_ViewMatrix);

                // World-space CSM shadow atlas (always bound to a RenderGraph-declared TextureHandle)
                bool hasCsm = csmShadowAtlas.IsValid() && m_CSMCascadeCount > 0 && m_CSMCascadeResolution > 0;
                if (csmShadowAtlas.IsValid())
                    cmd.SetComputeTextureParam(m_Compute, activeKernel, CSMShadowAtlasId, csmShadowAtlas);

                cmd.SetComputeIntParam(m_Compute, CSMCascadeCountId, hasCsm ? m_CSMCascadeCount : 0);
                cmd.SetComputeFloatParam(m_Compute, CSMMaxShadowDistanceId, hasCsm ? m_CSMMaxShadowDistance : 0.0f);
                cmd.SetComputeIntParam(m_Compute, CSMCascadeResolutionId, hasCsm ? m_CSMCascadeResolution : 0);
                cmd.SetComputeFloatParam(m_Compute, CSMNormalBiasId, hasCsm ? m_CSMNormalBias : 0.0f);
                if (hasCsm)
                {
                    cmd.SetComputeMatrixArrayParam(m_Compute, CSMViewProjMatricesId, m_CSMViewProjMatrices);
                    cmd.SetComputeVectorArrayParam(m_Compute, CSMCascadeSpheresId, m_CSMCascadeSpheres);
                }

                // Sky IBL cubemap (DeferredLightingPass always provides a valid Cube TextureHandle)
                bool hasSky = skyIblCubemap.IsValid() && m_SkyTextureParams.w > 0.5f;
                if (skyIblCubemap.IsValid())
                    cmd.SetComputeTextureParam(m_Compute, activeKernel, SkyTextureId, skyIblCubemap);

                cmd.SetComputeVectorParam(m_Compute, SkyTextureTintId, m_SkyTextureTint);
                cmd.SetComputeVectorParam(
                    m_Compute,
                    SkyTextureParamsId,
                    hasSky ? m_SkyTextureParams : Vector4.zero);
                cmd.SetComputeVectorParam(m_Compute, FallbackAmbientColorId, m_FallbackAmbientColor);

                // Shade visible vertices for each active hair model
                float selfShadowExtinction = Mathf.Max(GlobalSelfShadowExtinction, 0.05f);
                int allocCount = m_FrameData.Allocations.Count;
                for (int slotIdx = 0; slotIdx < allocCount; slotIdx++)
                {
                    StrandHairModelAtlasAllocation alloc = m_FrameData.Allocations[slotIdx];
                    if (!alloc.IsVisible || alloc.TileCount <= 0)
                        continue;

                    HairGroomRenderEntry entry = m_FrameData.ActiveEntries[alloc.EntryIndex];
                    HairGroomGpuResource gpuRes = entry.Asset?.GetOrCreateGpuResource();
                    if (gpuRes == null || !gpuRes.IsValid)
                        continue;

                    StrandHairModelScratchSlot slot = gpuPool.GetOrCreateSlot(
                        slotIdx,
                        gpuRes.VertexCount,
                        gpuRes.SegmentCount);

                    Color linearBase = entry.Shading.BaseColor.linear;
                    float ior = Mathf.Clamp(entry.Shading.Ior, 1.2f, 2.0f);
                    // Convert IOR -> F0 -> VividHairF0FromSpecular inverse (F0 = specular * 0.093)
                    float f0 = ((ior - 1.0f) / (ior + 1.0f));
                    f0 *= f0;
                    float specular = Mathf.Clamp01(f0 / 0.093f);

                    cmd.SetComputeMatrixParam(m_Compute, LocalToWorldId, entry.LocalToWorld);
                    cmd.SetComputeMatrixParam(m_Compute, WorldToLocalId, entry.LocalToWorld.inverse);
                    cmd.SetComputeIntParam(m_Compute, VertexCountId, gpuRes.VertexCount);
                    cmd.SetComputeIntParam(m_Compute, HasBakedSHId, gpuRes.HasBakedVisibilitySH ? 1 : 0);
                    cmd.SetComputeVectorParam(m_Compute, BoundsCenterWSId, entry.WorldBounds.center);

                    cmd.SetComputeIntParam(m_Compute, AbsorptionModeId, (int)entry.Shading.AbsorptionMode);
                    cmd.SetComputeVectorParam(
                        m_Compute,
                        BaseColorId,
                        new Vector4(linearBase.r, linearBase.g, linearBase.b, entry.Shading.Melanin));
                    cmd.SetComputeVectorParam(
                        m_Compute,
                        MelaninParamsId,
                        new Vector4(
                            entry.Shading.Melanin,
                            entry.Shading.MelaninRedness,
                            ior,
                            entry.Shading.DualScatterGain));
                    cmd.SetComputeVectorParam(
                        m_Compute,
                        RoughnessParamsId,
                        new Vector4(
                            entry.Shading.LongitudinalRoughness,
                            entry.Shading.AzimuthalRoughness,
                            entry.Shading.CuticleAngleDegrees * Mathf.Deg2Rad,
                            specular));
                    cmd.SetComputeVectorParam(
                        m_Compute,
                        MedullaParamsId,
                        new Vector4(
                            entry.Shading.MedullaWeight,
                            entry.Shading.MedullaRoughness,
                            selfShadowExtinction,
                            0.0f));

                    cmd.SetComputeBufferParam(m_Compute, activeKernel, RestVerticesId, gpuRes.RestVertexBuffer);
                    cmd.SetComputeBufferParam(m_Compute, activeKernel, VisibilitySHId, gpuRes.VisibilitySHBuffer);
                    cmd.SetComputeBufferParam(m_Compute, activeKernel, WorldPosRadiusId, slot.WorldPosRadiusBuffer);
                    cmd.SetComputeBufferParam(m_Compute, activeKernel, PostTransformClipId, slot.PostTransformClipBuffer);
                    cmd.SetComputeBufferParam(m_Compute, activeKernel, VertexShadingId, slot.VertexShadingBuffer);

                    if (useCompact)
                    {
                        cmd.SetComputeBufferParam(m_Compute, activeKernel, VisibleVertexListId, slot.VisibleVertexListBuffer);
                        cmd.SetComputeBufferParam(m_Compute, activeKernel, IndirectShadeArgsId, slot.IndirectShadeArgsBuffer);
                        cmd.DispatchCompute(m_Compute, activeKernel, slot.IndirectShadeArgsBuffer, 0u);
                    }
                    else
                    {
                        cmd.SetComputeBufferParam(m_Compute, activeKernel, VisibleVertexMaskId, slot.VisibleVertexMaskBuffer);
                        cmd.DispatchCompute(m_Compute, activeKernel, (gpuRes.VertexCount + 63) / 64, 1, 1);
                    }
                }
            }
        }

        public override void Dispose()
        {
            m_DummyDirectionalLightBuffer?.Dispose();
            m_DummyDirectionalLightBuffer = null;
            m_DummyPunctualLightBuffer?.Dispose();
            m_DummyPunctualLightBuffer = null;
            m_DummyAreaLightBuffer?.Dispose();
            m_DummyAreaLightBuffer = null;
            m_DummyReflectionProbeBuffer?.Dispose();
            m_DummyReflectionProbeBuffer = null;
            m_DummyUintBuffer?.Dispose();
            m_DummyUintBuffer = null;
            m_DummyFloatBuffer?.Dispose();
            m_DummyFloatBuffer = null;
            m_Compute = null;
            m_ShadeKernel = -1;
            m_ShadeCompactKernel = -1;
            m_FrameData = null;
            m_GpuPool = null;
            m_PreExposureBuffer = null;
        }
    }
}
