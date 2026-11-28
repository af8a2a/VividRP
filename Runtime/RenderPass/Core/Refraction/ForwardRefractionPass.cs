using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Standard RenderGraph forward refraction pass (LightMode = "VividRefraction")
    /// for solid optical glass, frosted glass, volumetric Beer-Lambert absorption,
    /// and Cauchy/Abbe RGB dispersion. Binds same-frame OpaqueColorPyramid, OpaqueDepth,
    /// BackfaceData, Clustered LightGrid, Reflection Probes, Sky IBL, and CSM Shadow.
    /// </summary>
    public sealed class ForwardRefractionPass : UnsafePass, IAllowGlobalStateModificationPass
    {
        public const string RefractionShaderTagName = "VividRefraction";

        private static readonly int OpaqueColorPyramidId = Shader.PropertyToID("_OpaqueColorPyramid");
        private static readonly int OpaqueDepthTextureId = Shader.PropertyToID("_OpaqueDepthTexture");
        private static readonly int BackfaceDataId = Shader.PropertyToID("_RefractionBackfaceData");
        private static readonly int RefractionScreenSizeId = Shader.PropertyToID("_RefractionScreenSize");
        private static readonly int OpaqueColorPyramidMaxMipId = Shader.PropertyToID("_OpaqueColorPyramidMaxMip");
        private static readonly int HasBackfaceDataId = Shader.PropertyToID("_RefractionHasBackfaceData");
        private static readonly int EnableRefractionId = Shader.PropertyToID("_RefractionEnable");
        private static readonly int EnableDispersionId = Shader.PropertyToID("_RefractionEnableDispersion");
        private static readonly int EnableBackfaceId = Shader.PropertyToID("_RefractionEnableBackface");
        private static readonly int DebugModeId = Shader.PropertyToID("_RefractionDebugMode");

        private static readonly int DirectionalShadowTextureId = Shader.PropertyToID("_DirectionalShadowTexture");
        private static readonly int DirectionalLightsId = Shader.PropertyToID("_DirectionalLights");
        private static readonly int DirectionalLightCountId = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int MainDirectionalLightIndexId = Shader.PropertyToID("_MainDirectionalLightIndex");
        private static readonly int PunctualLightsId = Shader.PropertyToID("_PunctualLights");
        private static readonly int PunctualLightCountId = Shader.PropertyToID("_PunctualLightCount");
        private static readonly int AreaLightsId = Shader.PropertyToID("_AreaLights");
        private static readonly int AreaLightCountId = Shader.PropertyToID("_AreaLightCount");
        private static readonly int ReflectionProbesId = Shader.PropertyToID("_ReflectionProbes");
        private static readonly int ReflectionProbeCountId = Shader.PropertyToID("_ReflectionProbeCount");
        private static readonly int LayeredOffsetId = Shader.PropertyToID("g_LayeredOffset");
        private static readonly int LayeredLightListId = Shader.PropertyToID("g_vLayeredLightList");
        private static readonly int LogBaseBufferId = Shader.PropertyToID("g_logBaseBuffer");
        private static readonly int ClusterTileSizeId = Shader.PropertyToID("_ClusterTileSize");
        private static readonly int ClusterSliceCountId = Shader.PropertyToID("_ClusterSliceCount");
        private static readonly int ClusterTileCountXId = Shader.PropertyToID("_ClusterTileCountX");
        private static readonly int ClusterTileCountYId = Shader.PropertyToID("_ClusterTileCountY");
        private static readonly int ClusterNearClipId = Shader.PropertyToID("_ClusterNearClip");
        private static readonly int ClusterFarClipId = Shader.PropertyToID("_ClusterFarClip");
        private static readonly int ClusterIsOrthographicId = Shader.PropertyToID("_ClusterIsOrthographic");
        private static readonly int ClusterScaleId = Shader.PropertyToID("g_fClustScale");
        private static readonly int ClusterBaseId = Shader.PropertyToID("g_fClustBase");
        private static readonly int NearPlaneId = Shader.PropertyToID("g_fNearPlane");
        private static readonly int FarPlaneId = Shader.PropertyToID("g_fFarPlane");
        private static readonly int Log2NumClustersId = Shader.PropertyToID("g_iLog2NumClusters");
        private static readonly int IsLogBaseBufferEnabledId = Shader.PropertyToID("g_isLogBaseBufferEnabled");
        private static readonly int NumTileClusteredXId = Shader.PropertyToID("_NumTileClusteredX");
        private static readonly int NumTileClusteredYId = Shader.PropertyToID("_NumTileClusteredY");
        private static readonly int ClusteredPunctualEnabledId = Shader.PropertyToID("_ClusteredPunctualLightGridEnabled");
        private static readonly int ClusteredAreaEnabledId = Shader.PropertyToID("_ClusteredAreaLightGridEnabled");
        private static readonly int ClusteredProbesEnabledId = Shader.PropertyToID("_ClusteredReflectionProbeGridEnabled");
        private static readonly int SkyTextureId = Shader.PropertyToID("_SkyTexture");
        private static readonly int SkyTextureParamsId = Shader.PropertyToID("_SkyTextureParams");

        private static readonly VividLightData.DirectionalLightData[] s_DummyDirLights = { default };
        private static readonly VividLightData.PunctualLightData[] s_DummyPunctualLights = { default };
        private static readonly VividLightData.AreaLightData[] s_DummyAreaLights = { default };
        private static readonly VividLightData.ReflectionProbeData[] s_DummyProbes = { default };
        private static readonly uint[] s_DummyUints = { 0u };
        private static readonly float[] s_DummyFloats = { 0.0f };

        public static bool EnableRefraction { get; set; } = true;
        public static bool EnableDispersion { get; set; } = true;
        public static bool EnableBackfaceRefraction { get; set; } = true;
        public static int DebugMode { get; set; } = 0; // 0 = Composite, 1 = Thickness, 2 = BackfaceNormal, 3 = BeerLambertTransmittance

        [RenderGraphResource(
            Name = "RenderList",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphRenderList m_RenderList;

        [RenderGraphResource(Name = "Color", Access = AccessFlags.ReadWrite, AttachmentIndex = 0)]
        private RenderGraphTexture m_ColorTexture;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "OpaqueColorPyramid", Access = AccessFlags.Read)]
        private RenderGraphTexture m_OpaqueColorPyramid;

        [RenderGraphResource(Name = "OpaqueDepth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_OpaqueDepth;

        [RenderGraphResource(Name = "BackfaceData", Access = AccessFlags.Read)]
        private RenderGraphTexture m_BackfaceData;

        [RenderGraphResource(Name = "DirectionalShadowTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DirectionalShadowTexture;

        [RenderGraphResource(Name = "DirectionalLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_DirectionalLightBuffer;

        [RenderGraphResource(Name = "PunctualLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_PunctualLightBuffer;

        [RenderGraphResource(Name = "AreaLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_AreaLightBuffer;

        [RenderGraphResource(Name = "ReflectionProbes", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_ReflectionProbeBuffer;

        [RenderGraphResource(Name = "LayeredOffset", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LayeredOffsetBuffer;

        [RenderGraphResource(Name = "LayeredLightList", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LayeredLightListBuffer;

        [RenderGraphResource(Name = "LogBaseBuffer", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LogBaseBuffer;

        [RenderGraphResource(Name = "SkyIBLCubemap", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SkyIBLCubemap;

        private readonly RenderGraphTexture m_LocalOpaqueColorPyramid;
        private readonly RenderGraphTexture m_LocalOpaqueDepth;
        private readonly RenderGraphTexture m_LocalBackfaceData;
        private readonly RenderGraphTexture m_LocalDirectionalShadow;
        private readonly RenderGraphBuffer m_LocalDirectionalLightBuffer;
        private readonly RenderGraphBuffer m_LocalPunctualLightBuffer;
        private readonly RenderGraphBuffer m_LocalAreaLightBuffer;
        private readonly RenderGraphBuffer m_LocalReflectionProbeBuffer;
        private readonly RenderGraphBuffer m_LocalLayeredOffsetBuffer;
        private readonly RenderGraphBuffer m_LocalLayeredLightListBuffer;
        private readonly RenderGraphBuffer m_LocalLogBaseBuffer;

        private GraphicsBuffer m_DummyDirBuffer;
        private GraphicsBuffer m_DummyPunctualBuffer;
        private GraphicsBuffer m_DummyAreaBuffer;
        private GraphicsBuffer m_DummyProbeBuffer;
        private GraphicsBuffer m_DummyUintBuffer;
        private GraphicsBuffer m_DummyFloatBuffer;

        private int m_Width = 1;
        private int m_Height = 1;
        private int m_OpaquePyramidMaxMip;
        private int m_DirectionalLightCount;
        private int m_PunctualLightCount;
        private int m_AreaLightCount;
        private int m_ReflectionProbeCount;
        private int m_MainDirectionalLightIndex = -1;
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
        private bool m_SupportsClusteredPunctual;
        private bool m_SupportsClusteredArea;
        private bool m_SupportsClusteredProbes;
        private bool m_IsLogBaseBufferEnabled;
        private Vector4 m_SkyTextureParams;

        public ForwardRefractionPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ForwardRefractionPass));
            m_RenderList = new RenderGraphRenderList
            {
                desc = RenderGraphRenderListDesc.CreateTransparent(RefractionShaderTagName)
            };

            m_ColorTexture = RenderGraphTexture.CreateColorTarget("Color", GraphicsFormat.R16G16B16A16_SFloat);
            m_DepthTexture = RenderGraphTexture.CreateDepthTarget("Depth", DepthBits.Depth32);

            m_LocalOpaqueColorPyramid = RenderGraphTexture.CreateInput("OpaqueColorPyramid", GraphicsFormat.R16G16B16A16_SFloat);
            m_LocalOpaqueDepth = RenderGraphTexture.CreateInput("OpaqueDepth", GraphicsFormat.R32_SFloat);
            m_LocalBackfaceData = RenderGraphTexture.CreateInput("BackfaceData", GraphicsFormat.R16G16B16A16_SFloat);
            m_LocalDirectionalShadow = RenderGraphTexture.CreateInput("DirectionalShadowTexture", GraphicsFormat.R16_SFloat);
            m_OpaqueColorPyramid = m_LocalOpaqueColorPyramid;
            m_OpaqueDepth = m_LocalOpaqueDepth;
            m_BackfaceData = m_LocalBackfaceData;
            m_DirectionalShadowTexture = m_LocalDirectionalShadow;

            m_LocalDirectionalLightBuffer = RenderGraphBuffer.CreateStructured("DirectionalLights", VividLightData.DirectionalLightData.Stride);
            m_LocalPunctualLightBuffer = RenderGraphBuffer.CreateStructured("PunctualLights", VividLightData.PunctualLightData.Stride);
            m_LocalAreaLightBuffer = RenderGraphBuffer.CreateStructured("AreaLights", VividLightData.AreaLightData.Stride);
            m_LocalReflectionProbeBuffer = RenderGraphBuffer.CreateStructured("ReflectionProbes", VividLightData.ReflectionProbeData.Stride);
            m_LocalLayeredOffsetBuffer = RenderGraphBuffer.CreateStructured("LayeredOffset", sizeof(uint));
            m_LocalLayeredLightListBuffer = RenderGraphBuffer.CreateStructured("LayeredLightList", sizeof(uint));
            m_LocalLogBaseBuffer = RenderGraphBuffer.CreateStructured("LogBaseBuffer", sizeof(float));

            m_DirectionalLightBuffer = m_LocalDirectionalLightBuffer;
            m_PunctualLightBuffer = m_LocalPunctualLightBuffer;
            m_AreaLightBuffer = m_LocalAreaLightBuffer;
            m_ReflectionProbeBuffer = m_LocalReflectionProbeBuffer;
            m_LayeredOffsetBuffer = m_LocalLayeredOffsetBuffer;
            m_LayeredLightListBuffer = m_LocalLayeredLightListBuffer;
            m_LogBaseBuffer = m_LocalLogBaseBuffer;

            m_SkyIBLCubemap = CreateSkyCubemapTexture("SkyIBLCubemap");
        }

        public override void Create()
        {
            EnsureDummyBuffers();
        }

        public override bool IsActive(ContextContainer frameData)
        {
            return EnableRefraction;
        }

        public override void Prepare(ContextContainer frameData)
        {
            EnsureDummyBuffers();
            var cameraData = frameData.Get<VividCameraData>();
            var camera = cameraData?.camera;

            m_Width = CameraDimensionUtility.ResolveCameraDimension(cameraData?.actualWidth ?? 0, cameraData?.pixelWidth ?? 0, Screen.width);
            m_Height = CameraDimensionUtility.ResolveCameraDimension(cameraData?.actualHeight ?? 0, cameraData?.pixelHeight ?? 0, Screen.height);

            m_OpaquePyramidMaxMip = 0;
            if (frameData.Contains<VividRefractionFrameData>())
            {
                var rfd = frameData.Get<VividRefractionFrameData>();
                if (rfd.HasOpaquePyramid)
                    m_OpaquePyramidMaxMip = Mathf.Max(0, rfd.MipCount - 1);
            }

            // Prepare Sky IBL Cubemap
            var skyData = frameData.Contains<VividSkyData>() ? frameData.Get<VividSkyData>() : null;
            bool hasActiveSky = skyData != null && skyData.activeSkyType != SkyType.None;
            int skyMaxMip = hasActiveSky ? SkyManager.GetSpecularCubemapMaxMip(skyData) : 0;
            float skyIntensity = hasActiveSky ? skyData.exposure : 1.0f;
            float skyRotation = hasActiveSky ? skyData.rotation : 0.0f;
            SkyManager.ImportSpecularCubemap(m_SkyIBLCubemap, skyData);
            m_SkyTextureParams = DeferredLightingPass.BuildSkyTextureParams(skyMaxMip, skyIntensity, skyRotation, hasActiveSky);

            // Prepare Clustered LightGrid metadata
            PrepareClusteredLighting(frameData, camera);
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_ColorTexture == null
                || !m_ColorTexture.innerHandle.IsValid()
                || m_DepthTexture == null
                || !m_DepthTexture.innerHandle.IsValid())
            {
                return;
            }

            var nativeCmd = context.GetNativeCommandBuffer();
            var defaultRes = context.renderGraphContext.defaultResources;
            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                bool hasOpaquePyramid = !ReferenceEquals(m_OpaqueColorPyramid, m_LocalOpaqueColorPyramid)
                    && m_OpaqueColorPyramid != null
                    && m_OpaqueColorPyramid.innerHandle.IsValid();
                bool hasOpaqueDepth = !ReferenceEquals(m_OpaqueDepth, m_LocalOpaqueDepth)
                    && m_OpaqueDepth != null
                    && m_OpaqueDepth.innerHandle.IsValid();
                bool hasBackface = EnableBackfaceRefraction
                    && !ReferenceEquals(m_BackfaceData, m_LocalBackfaceData)
                    && m_BackfaceData != null
                    && m_BackfaceData.innerHandle.IsValid();
                bool hasShadow = !ReferenceEquals(m_DirectionalShadowTexture, m_LocalDirectionalShadow)
                    && m_DirectionalShadowTexture != null
                    && m_DirectionalShadowTexture.innerHandle.IsValid();

                nativeCmd.SetGlobalTexture(OpaqueColorPyramidId, hasOpaquePyramid ? (RenderTargetIdentifier)m_OpaqueColorPyramid.innerHandle : defaultRes.blackTexture);
                nativeCmd.SetGlobalTexture(OpaqueDepthTextureId, hasOpaqueDepth ? (RenderTargetIdentifier)m_OpaqueDepth.innerHandle : defaultRes.blackTexture);
                nativeCmd.SetGlobalTexture(BackfaceDataId, hasBackface ? (RenderTargetIdentifier)m_BackfaceData.innerHandle : defaultRes.blackTexture);
                nativeCmd.SetGlobalTexture(DirectionalShadowTextureId, hasShadow ? (RenderTargetIdentifier)m_DirectionalShadowTexture.innerHandle : defaultRes.whiteTexture);

                nativeCmd.SetGlobalVector(
                    RefractionScreenSizeId,
                    new Vector4(m_Width, m_Height, 1.0f / Mathf.Max(1, m_Width), 1.0f / Mathf.Max(1, m_Height)));
                nativeCmd.SetGlobalFloat(OpaqueColorPyramidMaxMipId, m_OpaquePyramidMaxMip);
                nativeCmd.SetGlobalInt(HasBackfaceDataId, hasBackface ? 1 : 0);
                nativeCmd.SetGlobalInt(EnableRefractionId, EnableRefraction ? 1 : 0);
                nativeCmd.SetGlobalInt(EnableDispersionId, EnableDispersion ? 1 : 0);
                nativeCmd.SetGlobalInt(EnableBackfaceId, hasBackface ? 1 : 0);
                nativeCmd.SetGlobalInt(DebugModeId, DebugMode);

                // Bind Sky IBL
                if (m_SkyIBLCubemap != null && m_SkyIBLCubemap.innerHandle.IsValid())
                    nativeCmd.SetGlobalTexture(SkyTextureId, m_SkyIBLCubemap.innerHandle);
                nativeCmd.SetGlobalVector(SkyTextureParamsId, m_SkyTextureParams);

                // Bind Clustered LightGrid + Reflection Probes
                BindLightBuffer(nativeCmd, DirectionalLightsId, m_DirectionalLightBuffer, m_LocalDirectionalLightBuffer, m_DummyDirBuffer);
                BindLightBuffer(nativeCmd, PunctualLightsId, m_PunctualLightBuffer, m_LocalPunctualLightBuffer, m_DummyPunctualBuffer);
                BindLightBuffer(nativeCmd, AreaLightsId, m_AreaLightBuffer, m_LocalAreaLightBuffer, m_DummyAreaBuffer);
                BindLightBuffer(nativeCmd, ReflectionProbesId, m_ReflectionProbeBuffer, m_LocalReflectionProbeBuffer, m_DummyProbeBuffer);
                BindLightBuffer(nativeCmd, LayeredOffsetId, m_LayeredOffsetBuffer, m_LocalLayeredOffsetBuffer, m_DummyUintBuffer);
                BindLightBuffer(nativeCmd, LayeredLightListId, m_LayeredLightListBuffer, m_LocalLayeredLightListBuffer, m_DummyUintBuffer);
                BindLightBuffer(nativeCmd, LogBaseBufferId, m_LogBaseBuffer, m_LocalLogBaseBuffer, m_DummyFloatBuffer);

                nativeCmd.SetGlobalInt(DirectionalLightCountId, m_DirectionalLightCount);
                nativeCmd.SetGlobalInt(MainDirectionalLightIndexId, m_MainDirectionalLightIndex);
                nativeCmd.SetGlobalInt(PunctualLightCountId, m_PunctualLightCount);
                nativeCmd.SetGlobalInt(AreaLightCountId, m_AreaLightCount);
                nativeCmd.SetGlobalInt(ReflectionProbeCountId, m_ReflectionProbeCount);

                nativeCmd.SetGlobalInt(ClusterTileSizeId, m_ClusterTileSize);
                nativeCmd.SetGlobalInt(ClusterSliceCountId, m_ClusterSliceCount);
                nativeCmd.SetGlobalInt(ClusterTileCountXId, m_ClusterTileCountX);
                nativeCmd.SetGlobalInt(ClusterTileCountYId, m_ClusterTileCountY);
                nativeCmd.SetGlobalFloat(ClusterNearClipId, m_ClusterNearClip);
                nativeCmd.SetGlobalFloat(ClusterFarClipId, m_ClusterFarClip);
                nativeCmd.SetGlobalInt(ClusterIsOrthographicId, m_ClusterIsOrthographic);

                nativeCmd.SetGlobalFloat(ClusterScaleId, m_ClusterScale);
                nativeCmd.SetGlobalFloat(ClusterBaseId, m_ClusterBase);
                nativeCmd.SetGlobalFloat(NearPlaneId, m_ClusterNearClip);
                nativeCmd.SetGlobalFloat(FarPlaneId, m_ClusterFarClip);
                nativeCmd.SetGlobalInt(Log2NumClustersId, m_ClusterLog2SliceCount);
                nativeCmd.SetGlobalInt(IsLogBaseBufferEnabledId, m_IsLogBaseBufferEnabled ? 1 : 0);
                nativeCmd.SetGlobalInt(NumTileClusteredXId, m_ClusterTileCountX);
                nativeCmd.SetGlobalInt(NumTileClusteredYId, m_ClusterTileCountY);
                nativeCmd.SetGlobalInt(ClusteredPunctualEnabledId, m_SupportsClusteredPunctual ? 1 : 0);
                nativeCmd.SetGlobalInt(ClusteredAreaEnabledId, m_SupportsClusteredArea ? 1 : 0);
                nativeCmd.SetGlobalInt(ClusteredProbesEnabledId, m_SupportsClusteredProbes ? 1 : 0);

                // Draw onto existing SceneColor + Depth (preserve opaque background and depth!)
                CoreUtils.SetRenderTarget(
                    nativeCmd,
                    m_ColorTexture.innerHandle,
                    m_DepthTexture.innerHandle,
                    ClearFlag.None);

                if (m_RenderList != null && m_RenderList.IsValid)
                    nativeCmd.DrawRendererList(m_RenderList);
            }
        }

        public override void Dispose()
        {
            m_DummyDirBuffer?.Dispose(); m_DummyDirBuffer = null;
            m_DummyPunctualBuffer?.Dispose(); m_DummyPunctualBuffer = null;
            m_DummyAreaBuffer?.Dispose(); m_DummyAreaBuffer = null;
            m_DummyProbeBuffer?.Dispose(); m_DummyProbeBuffer = null;
            m_DummyUintBuffer?.Dispose(); m_DummyUintBuffer = null;
            m_DummyFloatBuffer?.Dispose(); m_DummyFloatBuffer = null;
        }

        private void PrepareClusteredLighting(ContextContainer frameData, Camera camera)
        {
            m_DirectionalLightCount = 0;
            m_PunctualLightCount = 0;
            m_AreaLightCount = 0;
            m_ReflectionProbeCount = 0;
            m_MainDirectionalLightIndex = -1;
            m_ClusterTileSize = LightGridPass.ClusterTileSize;
            m_ClusterSliceCount = LightGridPass.ClusterSliceCount;
            m_ClusterTileCountX = Mathf.Max(1, Mathf.CeilToInt(m_Width / (float)m_ClusterTileSize));
            m_ClusterTileCountY = Mathf.Max(1, Mathf.CeilToInt(m_Height / (float)m_ClusterTileSize));
            m_ClusterNearClip = Mathf.Max(camera != null ? camera.nearClipPlane : 0.1f, 0.01f);
            m_ClusterFarClip = Mathf.Max(camera != null ? camera.farClipPlane : 1000.0f, m_ClusterNearClip + 0.01f);
            m_ClusterIsOrthographic = camera != null && camera.orthographic ? 1 : 0;
            m_ClusterScale = 0.0f;
            m_ClusterBase = LightGridPass.ClusterLogBase;
            m_ClusterLog2SliceCount = LightGridPass.ClusterLog2SliceCount;
            m_SupportsClusteredPunctual = false;
            m_SupportsClusteredArea = false;
            m_SupportsClusteredProbes = false;
            m_IsLogBaseBufferEnabled = false;

            if (!frameData.Contains<VividClusteredLightingData>())
                return;

            var cl = frameData.Get<VividClusteredLightingData>();
            if (cl == null)
                return;

            m_MainDirectionalLightIndex = cl.mainDirectionalLightIndex;
            if (cl.clusterTileSize > 0) m_ClusterTileSize = cl.clusterTileSize;
            if (cl.clusterSliceCount > 0) m_ClusterSliceCount = cl.clusterSliceCount;
            if (cl.clusterTileCountX > 0) m_ClusterTileCountX = cl.clusterTileCountX;
            if (cl.clusterTileCountY > 0) m_ClusterTileCountY = cl.clusterTileCountY;
            if (cl.clusterNearClip > 0.0f) m_ClusterNearClip = cl.clusterNearClip;
            if (cl.clusterFarClip > m_ClusterNearClip) m_ClusterFarClip = cl.clusterFarClip;
            m_ClusterIsOrthographic = cl.clusterIsOrthographic;
            m_ClusterScale = cl.clusterScale;
            if (cl.clusterBase > 0.0f) m_ClusterBase = cl.clusterBase;
            if (cl.clusterLog2SliceCount > 0) m_ClusterLog2SliceCount = cl.clusterLog2SliceCount;

            bool hasDir = !ReferenceEquals(m_DirectionalLightBuffer, m_LocalDirectionalLightBuffer);
            bool hasGrid = !ReferenceEquals(m_LayeredOffsetBuffer, m_LocalLayeredOffsetBuffer)
                && !ReferenceEquals(m_LayeredLightListBuffer, m_LocalLayeredLightListBuffer);

            if (hasDir)
                m_DirectionalLightCount = Mathf.Max(0, cl.directionalLightCount);
            else
                m_MainDirectionalLightIndex = -1;

            bool supportsFinite = cl.supportsClusteredPunctualLights && hasGrid;
            m_SupportsClusteredPunctual = supportsFinite
                && cl.punctualLightCount > 0
                && !ReferenceEquals(m_PunctualLightBuffer, m_LocalPunctualLightBuffer);
            m_PunctualLightCount = m_SupportsClusteredPunctual ? Mathf.Max(0, cl.punctualLightCount) : 0;

            m_SupportsClusteredArea = supportsFinite
                && cl.areaLightCount > 0
                && !ReferenceEquals(m_AreaLightBuffer, m_LocalAreaLightBuffer);
            m_AreaLightCount = m_SupportsClusteredArea ? Mathf.Max(0, cl.areaLightCount) : 0;

            m_SupportsClusteredProbes = supportsFinite
                && cl.reflectionProbeCount > 0
                && !ReferenceEquals(m_ReflectionProbeBuffer, m_LocalReflectionProbeBuffer);
            m_ReflectionProbeCount = m_SupportsClusteredProbes ? Mathf.Max(0, cl.reflectionProbeCount) : 0;

            m_IsLogBaseBufferEnabled = supportsFinite
                && cl.isLogBaseBufferEnabled
                && !ReferenceEquals(m_LogBaseBuffer, m_LocalLogBaseBuffer);
        }

        private static void BindLightBuffer(
            CommandBuffer cmd,
            int propId,
            RenderGraphBuffer boundBuffer,
            RenderGraphBuffer localBuffer,
            GraphicsBuffer dummyFallback)
        {
            if (!ReferenceEquals(boundBuffer, localBuffer) && boundBuffer != null && boundBuffer.innerHandle.IsValid())
                cmd.SetGlobalBuffer(propId, boundBuffer.innerHandle);
            else if (dummyFallback != null && dummyFallback.IsValid())
                cmd.SetGlobalBuffer(propId, dummyFallback);
        }

        private void EnsureDummyBuffers()
        {
            if (m_DummyDirBuffer == null || !m_DummyDirBuffer.IsValid())
            {
                m_DummyDirBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, VividLightData.DirectionalLightData.Stride);
                m_DummyDirBuffer.SetData(s_DummyDirLights);
            }
            if (m_DummyPunctualBuffer == null || !m_DummyPunctualBuffer.IsValid())
            {
                m_DummyPunctualBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, VividLightData.PunctualLightData.Stride);
                m_DummyPunctualBuffer.SetData(s_DummyPunctualLights);
            }
            if (m_DummyAreaBuffer == null || !m_DummyAreaBuffer.IsValid())
            {
                m_DummyAreaBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, VividLightData.AreaLightData.Stride);
                m_DummyAreaBuffer.SetData(s_DummyAreaLights);
            }
            if (m_DummyProbeBuffer == null || !m_DummyProbeBuffer.IsValid())
            {
                m_DummyProbeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, VividLightData.ReflectionProbeData.Stride);
                m_DummyProbeBuffer.SetData(s_DummyProbes);
            }
            if (m_DummyUintBuffer == null || !m_DummyUintBuffer.IsValid())
            {
                m_DummyUintBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
                m_DummyUintBuffer.SetData(s_DummyUints);
            }
            if (m_DummyFloatBuffer == null || !m_DummyFloatBuffer.IsValid())
            {
                m_DummyFloatBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(float));
                m_DummyFloatBuffer.SetData(s_DummyFloats);
            }
        }

        private static RenderGraphTexture CreateSkyCubemapTexture(string name)
        {
            var tex = new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 1,
                    Height = 1,
                    Slices = 6,
                    Dimension = TextureDimension.Cube,
                    ColorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    DepthBufferBits = DepthBits.None,
                    MsaaSamples = MSAASamples.None,
                    FilterMode = FilterMode.Trilinear,
                    WrapMode = TextureWrapMode.Clamp,
                    UseMipMap = true,
                    AutoGenerateMips = false,
                    MipCount = 1,
                    Name = name,
                }
            };
            return tex;
        }
    }
}
