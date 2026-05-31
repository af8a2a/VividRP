using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class ClusterDebugPass : RasterPass
    {
        internal const string ClusterDebugShaderName = "Hidden/VividRP/ClusterDebug";

        private static readonly int SourceTextureId = Shader.PropertyToID("_SourceTexture");
        private static readonly int CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int SourceTextureScaleBiasId = Shader.PropertyToID("_SourceTextureScaleBias");
        private static readonly int CameraDepthTextureScaleBiasId = Shader.PropertyToID("_CameraDepthTextureScaleBias");
        private static readonly int TileClusterDebugId = Shader.PropertyToID("_TileClusterDebug");
        private static readonly int ViewTilesFlagsId = Shader.PropertyToID("_ViewTilesFlags");
        private static readonly int ClusterDebugModeId = Shader.PropertyToID("_ClusterDebugMode");
        private static readonly int ClusterDebugDistanceId = Shader.PropertyToID("_ClusterDebugDistance");
        private static readonly int ClusterDebugLightViewportSizeId = Shader.PropertyToID("_ClusterDebugLightViewportSize");
        private static readonly int ClusterDebugMaxLightCountId = Shader.PropertyToID("_ClusterDebugMaxLightCount");
        private static readonly int PunctualLightsId = Shader.PropertyToID("_PunctualLights");
        private static readonly int AreaLightsId = Shader.PropertyToID("_AreaLights");
        private static readonly int DecalDataId = Shader.PropertyToID("_DecalData");
        private static readonly int BigTileLightListId = Shader.PropertyToID("g_vBigTileLightList");
        private static readonly int BigTileLightListEnabledId = Shader.PropertyToID("_BigTileLightListEnabled");
        private static readonly int PunctualLightCountId = Shader.PropertyToID("_PunctualLightCount");
        private static readonly int AreaLightCountId = Shader.PropertyToID("_AreaLightCount");
        private static readonly int ReflectionProbeCountId = Shader.PropertyToID("_ReflectionProbeCount");
        private static readonly int DecalCountId = Shader.PropertyToID("_DecalCount");
        private static readonly int ClusteredPunctualLightGridEnabledId = Shader.PropertyToID("_ClusteredPunctualLightGridEnabled");
        private static readonly int ClusteredAreaLightGridEnabledId = Shader.PropertyToID("_ClusteredAreaLightGridEnabled");
        private static readonly int ClusteredReflectionProbeGridEnabledId = Shader.PropertyToID("_ClusteredReflectionProbeGridEnabled");
        private static readonly int ClusteredDecalGridEnabledId = Shader.PropertyToID("_ClusteredDecalGridEnabled");
        private static readonly int LayeredLightListId = Shader.PropertyToID("g_vLayeredLightList");
        private static readonly int LayeredOffsetId = Shader.PropertyToID("g_LayeredOffset");
        private static readonly int LogBaseBufferId = Shader.PropertyToID("g_logBaseBuffer");
        private static readonly int ClusterScaleId = Shader.PropertyToID("g_fClustScale");
        private static readonly int ClusterBaseId = Shader.PropertyToID("g_fClustBase");
        private static readonly int NearPlaneId = Shader.PropertyToID("g_fNearPlane");
        private static readonly int FarPlaneId = Shader.PropertyToID("g_fFarPlane");
        private static readonly int Log2NumClustersId = Shader.PropertyToID("g_iLog2NumClusters");
        private static readonly int IsLogBaseBufferEnabledId = Shader.PropertyToID("g_isLogBaseBufferEnabled");
        private static readonly int NumTileClusteredXId = Shader.PropertyToID("_NumTileClusteredX");
        private static readonly int NumTileClusteredYId = Shader.PropertyToID("_NumTileClusteredY");
        private static readonly int NumTileBigTileXId = Shader.PropertyToID("_NumTileBigTileX");
        private static readonly int NumTileBigTileYId = Shader.PropertyToID("_NumTileBigTileY");
        private static readonly int ClusterTileSizeId = Shader.PropertyToID("_ClusterTileSize");
        private static readonly int BigTileSizeId = Shader.PropertyToID("_BigTileSize");
        private static readonly int ClusterSliceCountId = Shader.PropertyToID("_ClusterSliceCount");
        private static readonly int ClusterTileCountXId = Shader.PropertyToID("_ClusterTileCountX");
        private static readonly int ClusterTileCountYId = Shader.PropertyToID("_ClusterTileCountY");
        private static readonly int ClusterNearClipId = Shader.PropertyToID("_ClusterNearClip");
        private static readonly int ClusterFarClipId = Shader.PropertyToID("_ClusterFarClip");
        private static readonly int ClusterIsOrthographicId = Shader.PropertyToID("_ClusterIsOrthographic");
        private const int BigTileMaxLightCount = 511;

        [RenderGraphResource(Name = "SourceTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SourceTexture;

        [RenderGraphResource(Name = "DepthTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(
            Name = "OutputTexture",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        [RenderGraphResource(Name = "PunctualLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_PunctualLightBuffer;

        [RenderGraphResource(Name = "AreaLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_AreaLightBuffer;

        [RenderGraphResource(Name = "DecalData", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_DecalDataBuffer;

        [RenderGraphResource(Name = "BigTileLightList", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_BigTileLightListBuffer;

        [RenderGraphResource(Name = "LayeredOffset", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LayeredOffsetBuffer;

        [RenderGraphResource(Name = "LayeredLightList", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LayeredLightListBuffer;

        [RenderGraphResource(Name = "LogBaseBuffer", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LogBaseBuffer;

        private Material m_Material;
        private ClusterDebugSettingsData m_ResolvedSettings;
        private readonly RenderGraphBuffer m_LocalPunctualLightBuffer;
        private readonly RenderGraphBuffer m_LocalAreaLightBuffer;
        private readonly RenderGraphBuffer m_LocalDecalDataBuffer;
        private readonly RenderGraphBuffer m_LocalBigTileLightListBuffer;
        private readonly RenderGraphBuffer m_LocalLayeredOffsetBuffer;
        private readonly RenderGraphBuffer m_LocalLayeredLightListBuffer;
        private readonly RenderGraphBuffer m_LocalLogBaseBuffer;
        private Vector4 m_ClusterDebugLightViewportSize = new(1f, 1f, 1f, 1f);
        private int m_PunctualLightCount;
        private int m_AreaLightCount;
        private int m_ReflectionProbeCount;
        private int m_DecalCount;
        private int m_ClusterTileSize = LightGridPass.ClusterTileSize;
        private int m_ClusterSliceCount = LightGridPass.ClusterSliceCount;
        private int m_ClusterTileCountX = 1;
        private int m_ClusterTileCountY = 1;
        private int m_BigTileCountX = 1;
        private int m_BigTileCountY = 1;
        private float m_ClusterNearClip = 0.1f;
        private float m_ClusterFarClip = 1000.0f;
        private int m_ClusterIsOrthographic;
        private float m_ClusterScale;
        private float m_ClusterBase = LightGridPass.ClusterLogBase;
        private int m_ClusterLog2SliceCount = LightGridPass.ClusterLog2SliceCount;
        private bool m_SupportsClusteredPunctualLights;
        private bool m_SupportsClusteredAreaLights;
        private bool m_SupportsClusteredReflectionProbes;
        private bool m_SupportsClusteredDecals;
        private bool m_SupportsBigTileLightList;
        private bool m_IsLogBaseBufferEnabled;
        private bool m_ShouldSkipExecution;

        internal readonly struct ClusterDebugSettingsData
        {
            public readonly TileClusterDebug tileClusterDebug;
            public readonly TileClusterCategoryDebug tileClusterDebugByCategory;
            public readonly ClusterDebugMode clusterDebugMode;
            public readonly float clusterDebugDistance;

            public ClusterDebugSettingsData(
                TileClusterDebug tileClusterDebug,
                TileClusterCategoryDebug tileClusterDebugByCategory,
                ClusterDebugMode clusterDebugMode,
                float clusterDebugDistance)
            {
                this.tileClusterDebug = tileClusterDebug;
                this.tileClusterDebugByCategory = tileClusterDebugByCategory;
                this.clusterDebugMode = clusterDebugMode;
                this.clusterDebugDistance = clusterDebugDistance;
            }
        }

        public ClusterDebugPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ClusterDebugPass));

            m_SourceTexture = RenderGraphTexture.CreateInput("SourceTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_DepthTexture = RenderGraphTexture.CreateInput("DepthTexture", GraphicsFormat.R32_SFloat);
            m_DepthTexture.desc.FilterMode = FilterMode.Point;
            m_OutputTexture = RenderGraphTexture.CreateColorTarget("OutputTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture.desc.ClearBuffer = false;
            m_LocalPunctualLightBuffer = RenderGraphBuffer.CreateStructured("PunctualLights", VividLightData.PunctualLightData.Stride);
            m_LocalAreaLightBuffer = RenderGraphBuffer.CreateStructured("AreaLights", VividLightData.AreaLightData.Stride);
            m_LocalDecalDataBuffer = RenderGraphBuffer.CreateStructured("DecalData", VividLightData.DecalClusterData.Stride);
            m_LocalBigTileLightListBuffer = RenderGraphBuffer.CreateStructured("BigTileLightList", sizeof(uint));
            m_LocalLayeredOffsetBuffer = RenderGraphBuffer.CreateStructured("LayeredOffset", sizeof(uint));
            m_LocalLayeredLightListBuffer = RenderGraphBuffer.CreateStructured("LayeredLightList", sizeof(uint));
            m_LocalLogBaseBuffer = RenderGraphBuffer.CreateStructured("LogBaseBuffer", sizeof(float));
            m_PunctualLightBuffer = m_LocalPunctualLightBuffer;
            m_AreaLightBuffer = m_LocalAreaLightBuffer;
            m_DecalDataBuffer = m_LocalDecalDataBuffer;
            m_BigTileLightListBuffer = m_LocalBigTileLightListBuffer;
            m_LayeredOffsetBuffer = m_LocalLayeredOffsetBuffer;
            m_LayeredLightListBuffer = m_LocalLayeredLightListBuffer;
            m_LogBaseBuffer = m_LocalLogBaseBuffer;
            m_ResolvedSettings = new ClusterDebugSettingsData(
                TileClusterDebug.None,
                TileClusterCategoryDebug.Punctual,
                ClusterDebugMode.VisualizeOpaque,
                1f);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var shader = resources?.ClusterDebugShader;
            shader ??= Shader.Find(ClusterDebugShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{ClusterDebugShaderName}' for {nameof(ClusterDebugPass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_ResolvedSettings = ResolveSettings(VividRenderingDebugDisplaySettings.Data);

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_ShouldSkipExecution = DebugPassCameraUtility.ShouldSkipExecution(cameraData);
            var width = RenderGraphTextureDescUtility.ResolveMaxExplicitDimension(
                descriptor => descriptor.Width,
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width,
                m_SourceTexture?.desc);
            var height = RenderGraphTextureDescUtility.ResolveMaxExplicitDimension(
                descriptor => descriptor.Height,
                cameraData.actualHeight,
                cameraData.pixelHeight,
                Screen.height,
                m_SourceTexture?.desc);

            ConfigureOutputTexture(width, height, GetPreferredSourceDescriptor());
            m_ClusterDebugLightViewportSize = new Vector4(
                width,
                height,
                1f / Mathf.Max(1, width),
                1f / Mathf.Max(1, height));
            PrepareClusteredLightingParameters(frameData, cameraData, width, height);
        }

        public override void Record(RasterPassContext context)
        {
            if (m_ShouldSkipExecution)
            {
                DebugPassCameraUtility.TryPassThrough(context, m_SourceTexture, m_OutputTexture);
                return;
            }

            if (m_Material == null
                || !m_SourceTexture.innerHandle.IsValid()
                || !m_OutputTexture.innerHandle.IsValid())
            {
                return;
            }

            var sourceTexture = TextureResolveUtility.ResolveTexture(m_SourceTexture.innerHandle);
            if (sourceTexture == null)
                return;

            var depthTexture = TextureResolveUtility.ResolveTexture(m_DepthTexture.innerHandle) ?? Texture2D.whiteTexture;
            var tileClusterDebug = m_ResolvedSettings.tileClusterDebug;

            if (depthTexture == Texture2D.whiteTexture && tileClusterDebug == TileClusterDebug.Cluster)
                tileClusterDebug = TileClusterDebug.None;

            ApplyClusteredLightingProperties();

            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetTexture(SourceTextureId, sourceTexture);
            mpb.SetTexture(CameraDepthTextureId, depthTexture);
            mpb.SetVector(SourceTextureScaleBiasId, TextureScaleBiasUtility.GetScaleBias(m_SourceTexture.innerHandle));
            mpb.SetVector(CameraDepthTextureScaleBiasId, TextureScaleBiasUtility.GetScaleBias(m_DepthTexture.innerHandle));
            mpb.SetInt(TileClusterDebugId, (int)tileClusterDebug);
            mpb.SetInt(ViewTilesFlagsId, (int)m_ResolvedSettings.tileClusterDebugByCategory);
            mpb.SetInt(ClusterDebugModeId, (int)m_ResolvedSettings.clusterDebugMode);
            mpb.SetFloat(ClusterDebugDistanceId, m_ResolvedSettings.clusterDebugDistance);
            mpb.SetVector(ClusterDebugLightViewportSizeId, m_ClusterDebugLightViewportSize);
            mpb.SetFloat(
                ClusterDebugMaxLightCountId,
                tileClusterDebug == TileClusterDebug.Tile
                    ? BigTileMaxLightCount
                    : VividRP.Runtime.LightGridPass.ClusterMaxLightsPerCluster);

            CoreUtils.DrawFullScreen(context.cmd, m_Material, mpb, 0);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }

            m_PunctualLightBuffer = m_LocalPunctualLightBuffer;
            m_AreaLightBuffer = m_LocalAreaLightBuffer;
            m_DecalDataBuffer = m_LocalDecalDataBuffer;
            m_BigTileLightListBuffer = m_LocalBigTileLightListBuffer;
            m_LayeredOffsetBuffer = m_LocalLayeredOffsetBuffer;
            m_LayeredLightListBuffer = m_LocalLayeredLightListBuffer;
            m_LogBaseBuffer = m_LocalLogBaseBuffer;
            m_PunctualLightCount = 0;
            m_AreaLightCount = 0;
            m_ReflectionProbeCount = 0;
            m_DecalCount = 0;
            m_ClusterTileSize = LightGridPass.ClusterTileSize;
            m_ClusterSliceCount = LightGridPass.ClusterSliceCount;
            m_ClusterTileCountX = 1;
            m_ClusterTileCountY = 1;
            m_BigTileCountX = 1;
            m_BigTileCountY = 1;
            m_ClusterNearClip = 0.1f;
            m_ClusterFarClip = 1000.0f;
            m_ClusterIsOrthographic = 0;
            m_ClusterScale = 0.0f;
            m_ClusterBase = LightGridPass.ClusterLogBase;
            m_ClusterLog2SliceCount = LightGridPass.ClusterLog2SliceCount;
            m_SupportsClusteredPunctualLights = false;
            m_SupportsClusteredAreaLights = false;
            m_SupportsClusteredReflectionProbes = false;
            m_SupportsClusteredDecals = false;
            m_SupportsBigTileLightList = false;
            m_IsLogBaseBufferEnabled = false;
            m_ShouldSkipExecution = false;
        }

        internal static ClusterDebugSettingsData ResolveSettings(VividRenderingDebugSettingsData data)
        {
            var tileClusterDebug = TileClusterDebug.None;
            var tileClusterDebugByCategory = TileClusterCategoryDebug.Punctual;
            var clusterDebugMode = ClusterDebugMode.VisualizeOpaque;
            var clusterDebugDistance = 1f;

            if (data == null)
            {
                return new ClusterDebugSettingsData(
                    tileClusterDebug,
                    tileClusterDebugByCategory,
                    clusterDebugMode,
                    clusterDebugDistance);
            }

            tileClusterDebug = data.tileClusterDebug;
            tileClusterDebugByCategory = data.tileClusterDebugByCategory;
            clusterDebugMode = data.clusterDebugMode;
            clusterDebugDistance = Mathf.Max(0f, data.clusterDebugDistance);

            return new ClusterDebugSettingsData(
                tileClusterDebug,
                tileClusterDebugByCategory,
                clusterDebugMode,
                clusterDebugDistance);
        }

        private void PrepareClusteredLightingParameters(
            ContextContainer frameData,
            VividCameraData cameraData,
            int width,
            int height)
        {
            var clusteredLightingData = frameData.GetOrCreate<VividClusteredLightingData>();
            var camera = cameraData.camera;

            m_PunctualLightBuffer = clusteredLightingData.punctualLights ?? m_LocalPunctualLightBuffer;
            m_AreaLightBuffer = clusteredLightingData.areaLights ?? m_LocalAreaLightBuffer;
            m_DecalDataBuffer = clusteredLightingData.decalData ?? m_LocalDecalDataBuffer;
            m_BigTileLightListBuffer = clusteredLightingData.bigTileLightList ?? m_LocalBigTileLightListBuffer;
            m_LayeredOffsetBuffer = clusteredLightingData.layeredOffset ?? m_LocalLayeredOffsetBuffer;
            m_LayeredLightListBuffer = clusteredLightingData.layeredLightList ?? m_LocalLayeredLightListBuffer;
            m_LogBaseBuffer = clusteredLightingData.logBaseBuffer ?? m_LocalLogBaseBuffer;
            m_PunctualLightCount = 0;
            m_AreaLightCount = 0;
            m_ReflectionProbeCount = 0;
            m_DecalCount = 0;
            m_ClusterTileSize = LightGridPass.ClusterTileSize;
            m_ClusterSliceCount = LightGridPass.ClusterSliceCount;
            m_ClusterTileCountX = Mathf.Max(1, Mathf.CeilToInt(width / (float)m_ClusterTileSize));
            m_ClusterTileCountY = Mathf.Max(1, Mathf.CeilToInt(height / (float)m_ClusterTileSize));
            m_BigTileCountX = Mathf.Max(1, Mathf.CeilToInt(width / (float)LightGridPass.ClusterBigTileSize));
            m_BigTileCountY = Mathf.Max(1, Mathf.CeilToInt(height / (float)LightGridPass.ClusterBigTileSize));
            m_ClusterNearClip = Mathf.Max(camera != null ? camera.nearClipPlane : 0.1f, 0.01f);
            m_ClusterFarClip = Mathf.Max(camera != null ? camera.farClipPlane : 1000.0f, m_ClusterNearClip + 0.01f);
            m_ClusterIsOrthographic = camera != null && camera.orthographic ? 1 : 0;
            m_ClusterScale = 0.0f;
            m_ClusterBase = LightGridPass.ClusterLogBase;
            m_ClusterLog2SliceCount = LightGridPass.ClusterLog2SliceCount;
            m_SupportsClusteredPunctualLights = false;
            m_SupportsClusteredAreaLights = false;
            m_SupportsClusteredReflectionProbes = false;
            m_SupportsClusteredDecals = false;
            m_SupportsBigTileLightList = false;
            m_IsLogBaseBufferEnabled = false;

            if (!HasClusteredLightingData(clusteredLightingData))
                return;

            m_ClusterTileSize = clusteredLightingData.clusterTileSize > 0
                ? clusteredLightingData.clusterTileSize
                : LightGridPass.ClusterTileSize;
            m_ClusterSliceCount = clusteredLightingData.clusterSliceCount > 0
                ? clusteredLightingData.clusterSliceCount
                : LightGridPass.ClusterSliceCount;
            m_ClusterTileCountX = clusteredLightingData.clusterTileCountX > 0
                ? clusteredLightingData.clusterTileCountX
                : Mathf.Max(1, Mathf.CeilToInt(width / (float)Mathf.Max(m_ClusterTileSize, 1)));
            m_ClusterTileCountY = clusteredLightingData.clusterTileCountY > 0
                ? clusteredLightingData.clusterTileCountY
                : Mathf.Max(1, Mathf.CeilToInt(height / (float)Mathf.Max(m_ClusterTileSize, 1)));
            m_BigTileCountX = clusteredLightingData.bigTileCountX > 0
                ? clusteredLightingData.bigTileCountX
                : Mathf.Max(1, Mathf.CeilToInt(width / (float)LightGridPass.ClusterBigTileSize));
            m_BigTileCountY = clusteredLightingData.bigTileCountY > 0
                ? clusteredLightingData.bigTileCountY
                : Mathf.Max(1, Mathf.CeilToInt(height / (float)LightGridPass.ClusterBigTileSize));
            m_ClusterNearClip = clusteredLightingData.clusterNearClip > 0.0f
                ? clusteredLightingData.clusterNearClip
                : m_ClusterNearClip;
            m_ClusterFarClip = clusteredLightingData.clusterFarClip > m_ClusterNearClip
                ? clusteredLightingData.clusterFarClip
                : Mathf.Max(m_ClusterNearClip + 0.01f, m_ClusterFarClip);
            m_ClusterIsOrthographic = clusteredLightingData.clusterIsOrthographic;
            m_ClusterScale = clusteredLightingData.clusterScale;
            m_ClusterBase = clusteredLightingData.clusterBase > 0.0f
                ? clusteredLightingData.clusterBase
                : LightGridPass.ClusterLogBase;
            m_ClusterLog2SliceCount = clusteredLightingData.clusterLog2SliceCount > 0
                ? clusteredLightingData.clusterLog2SliceCount
                : LightGridPass.ClusterLog2SliceCount;
            m_PunctualLightCount = Mathf.Max(0, clusteredLightingData.punctualLightCount);
            m_AreaLightCount = Mathf.Max(0, clusteredLightingData.areaLightCount);
            m_ReflectionProbeCount = Mathf.Max(0, clusteredLightingData.reflectionProbeCount);
            m_DecalCount = Mathf.Max(0, clusteredLightingData.decalCount);
            var supportsClusteredFiniteLights = clusteredLightingData.supportsClusteredPunctualLights;
            m_SupportsClusteredPunctualLights = supportsClusteredFiniteLights
                && m_PunctualLightCount > 0
                && HasBoundPunctualLightResources();
            m_SupportsClusteredAreaLights = supportsClusteredFiniteLights
                && m_AreaLightCount > 0
                && HasBoundAreaLightResources();
            m_SupportsClusteredReflectionProbes = supportsClusteredFiniteLights
                && m_ReflectionProbeCount > 0
                && HasBoundReflectionProbeResources();
            m_SupportsClusteredDecals = supportsClusteredFiniteLights
                && m_DecalCount > 0
                && HasBoundDecalResources();
            m_SupportsBigTileLightList = supportsClusteredFiniteLights
                && (m_PunctualLightCount + m_AreaLightCount + m_ReflectionProbeCount + m_DecalCount) > 0
                && HasBoundBigTileLightListResources();
            m_IsLogBaseBufferEnabled = supportsClusteredFiniteLights
                && clusteredLightingData.isLogBaseBufferEnabled
                && !ReferenceEquals(m_LogBaseBuffer, m_LocalLogBaseBuffer);
        }

        private void ApplyClusteredLightingProperties()
        {
            m_Material.SetInt(ClusteredPunctualLightGridEnabledId, m_SupportsClusteredPunctualLights ? 1 : 0);
            m_Material.SetInt(ClusteredAreaLightGridEnabledId, m_SupportsClusteredAreaLights ? 1 : 0);
            m_Material.SetInt(ClusteredReflectionProbeGridEnabledId, m_SupportsClusteredReflectionProbes ? 1 : 0);
            m_Material.SetInt(ClusteredDecalGridEnabledId, m_SupportsClusteredDecals ? 1 : 0);
            m_Material.SetInt(BigTileLightListEnabledId, m_SupportsBigTileLightList ? 1 : 0);
            m_Material.SetInt(PunctualLightCountId, m_PunctualLightCount);
            m_Material.SetInt(AreaLightCountId, m_AreaLightCount);
            m_Material.SetInt(ReflectionProbeCountId, m_ReflectionProbeCount);
            m_Material.SetInt(DecalCountId, m_DecalCount);
            m_Material.SetInt(ClusterTileSizeId, m_ClusterTileSize);
            m_Material.SetInt(BigTileSizeId, LightGridPass.ClusterBigTileSize);
            m_Material.SetInt(ClusterSliceCountId, m_ClusterSliceCount);
            m_Material.SetInt(ClusterTileCountXId, m_ClusterTileCountX);
            m_Material.SetInt(ClusterTileCountYId, m_ClusterTileCountY);
            m_Material.SetInt(ClusterIsOrthographicId, m_ClusterIsOrthographic);
            m_Material.SetFloat(ClusterNearClipId, m_ClusterNearClip);
            m_Material.SetFloat(ClusterFarClipId, m_ClusterFarClip);
            m_Material.SetFloat(ClusterScaleId, m_ClusterScale);
            m_Material.SetFloat(ClusterBaseId, m_ClusterBase);
            m_Material.SetFloat(NearPlaneId, m_ClusterNearClip);
            m_Material.SetFloat(FarPlaneId, m_ClusterFarClip);
            m_Material.SetInt(Log2NumClustersId, m_ClusterLog2SliceCount);
            m_Material.SetInt(IsLogBaseBufferEnabledId, m_IsLogBaseBufferEnabled ? 1 : 0);
            m_Material.SetInt(NumTileClusteredXId, m_ClusterTileCountX);
            m_Material.SetInt(NumTileClusteredYId, m_ClusterTileCountY);
            m_Material.SetInt(NumTileBigTileXId, m_BigTileCountX);
            m_Material.SetInt(NumTileBigTileYId, m_BigTileCountY);

            var punctualLights = m_PunctualLightBuffer?.ImportedGraphicsBuffer;
            if (punctualLights != null)
                m_Material.SetBuffer(PunctualLightsId, punctualLights);

            var areaLights = m_AreaLightBuffer?.ImportedGraphicsBuffer;
            if (areaLights != null)
                m_Material.SetBuffer(AreaLightsId, areaLights);

            var decalData = m_DecalDataBuffer?.ImportedGraphicsBuffer;
            if (decalData != null)
                m_Material.SetBuffer(DecalDataId, decalData);

            var bigTileLightList = m_BigTileLightListBuffer?.ImportedGraphicsBuffer;
            if (bigTileLightList != null)
                m_Material.SetBuffer(BigTileLightListId, bigTileLightList);

            var layeredOffset = m_LayeredOffsetBuffer?.ImportedGraphicsBuffer;
            if (layeredOffset != null)
                m_Material.SetBuffer(LayeredOffsetId, layeredOffset);

            var layeredLightList = m_LayeredLightListBuffer?.ImportedGraphicsBuffer;
            if (layeredLightList != null)
                m_Material.SetBuffer(LayeredLightListId, layeredLightList);

            var logBaseBuffer = m_LogBaseBuffer?.ImportedGraphicsBuffer;
            if (logBaseBuffer != null)
                m_Material.SetBuffer(LogBaseBufferId, logBaseBuffer);
        }

        private bool HasBoundPunctualLightResources()
        {
            return !ReferenceEquals(m_PunctualLightBuffer, m_LocalPunctualLightBuffer)
                && !ReferenceEquals(m_LayeredOffsetBuffer, m_LocalLayeredOffsetBuffer)
                && !ReferenceEquals(m_LayeredLightListBuffer, m_LocalLayeredLightListBuffer)
                && !ReferenceEquals(m_LogBaseBuffer, m_LocalLogBaseBuffer);
        }

        private bool HasBoundAreaLightResources()
        {
            return !ReferenceEquals(m_AreaLightBuffer, m_LocalAreaLightBuffer)
                && !ReferenceEquals(m_LayeredOffsetBuffer, m_LocalLayeredOffsetBuffer)
                && !ReferenceEquals(m_LayeredLightListBuffer, m_LocalLayeredLightListBuffer);
        }

        private bool HasBoundReflectionProbeResources()
        {
            return !ReferenceEquals(m_LayeredOffsetBuffer, m_LocalLayeredOffsetBuffer)
                && !ReferenceEquals(m_LayeredLightListBuffer, m_LocalLayeredLightListBuffer);
        }

        private bool HasBoundDecalResources()
        {
            return !ReferenceEquals(m_DecalDataBuffer, m_LocalDecalDataBuffer)
                && !ReferenceEquals(m_LayeredOffsetBuffer, m_LocalLayeredOffsetBuffer)
                && !ReferenceEquals(m_LayeredLightListBuffer, m_LocalLayeredLightListBuffer);
        }

        private bool HasBoundBigTileLightListResources()
        {
            return !ReferenceEquals(m_BigTileLightListBuffer, m_LocalBigTileLightListBuffer);
        }

        private static bool HasClusteredLightingData(VividClusteredLightingData clusteredLightingData)
        {
            return clusteredLightingData != null
                && (clusteredLightingData.punctualLights != null
                    || clusteredLightingData.areaLights != null
                    || clusteredLightingData.decalData != null
                    || clusteredLightingData.bigTileLightList != null
                    || clusteredLightingData.layeredOffset != null
                    || clusteredLightingData.layeredLightList != null
                    || clusteredLightingData.logBaseBuffer != null
                    || clusteredLightingData.clusterTileSize > 0
                    || clusteredLightingData.clusterSliceCount > 0
                    || clusteredLightingData.bigTileCountX > 0
                    || clusteredLightingData.bigTileCountY > 0
                    || clusteredLightingData.punctualLightCount > 0
                    || clusteredLightingData.areaLightCount > 0
                    || clusteredLightingData.reflectionProbeCount > 0
                    || clusteredLightingData.decalCount > 0);
        }

        private void ConfigureOutputTexture(int width, int height, RenderGraphTextureDesc sourceDescriptor)
        {
            if (m_OutputTexture?.desc == null)
                return;

            m_OutputTexture.desc.Width = width;
            m_OutputTexture.desc.Height = height;
            m_OutputTexture.desc.ColorFormat = RenderGraphTextureDescUtility.ResolveColorFormat(sourceDescriptor);
            m_OutputTexture.desc.DepthBufferBits = DepthBits.None;
            m_OutputTexture.desc.MsaaSamples = MSAASamples.None;
            m_OutputTexture.desc.FilterMode = sourceDescriptor?.FilterMode ?? FilterMode.Bilinear;
            m_OutputTexture.desc.WrapMode = sourceDescriptor?.WrapMode ?? TextureWrapMode.Clamp;
            m_OutputTexture.desc.ClearBuffer = false;
            m_OutputTexture.desc.UseMipMap = false;
            m_OutputTexture.desc.AutoGenerateMips = false;
            m_OutputTexture.desc.MipCount = 1;
            m_OutputTexture.desc.EnableRandomWrite = false;
            m_OutputTexture.desc.BindTextureMS = false;
            m_OutputTexture.desc.Name = "OutputTexture";

            if (sourceDescriptor == null)
                return;

            m_OutputTexture.desc.Dimension = sourceDescriptor.Dimension;
            m_OutputTexture.desc.Slices = Mathf.Max(1, sourceDescriptor.Slices);
            m_OutputTexture.desc.UseDynamicScale = sourceDescriptor.UseDynamicScale;
            m_OutputTexture.desc.UseDynamicScaleExplicit = sourceDescriptor.UseDynamicScaleExplicit;
            m_OutputTexture.desc.ScaleFactor = sourceDescriptor.ScaleFactor;
        }

        private RenderGraphTextureDesc GetPreferredSourceDescriptor()
        {
            if (RenderGraphTextureDescUtility.HasExplicitSize(m_SourceTexture?.desc))
                return m_SourceTexture.desc;

            return m_SourceTexture?.desc;
        }

    }
}
