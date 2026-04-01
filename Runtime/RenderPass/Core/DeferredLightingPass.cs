using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class DeferredLightingPass : UnsafePass
    {
        private const int ClearThreadGroupSizeX = 8;
        private const int ClearThreadGroupSizeY = 8;
        private const string ClearDeferredLitKernelName = "ClearDeferredLit";
        private const string DeferredLitKernelName = "DeferredLit";

        private static readonly int GBuffer0Id = Shader.PropertyToID("_GBuffer0");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int GBuffer2Id = Shader.PropertyToID("_GBuffer2");
        private static readonly int GBuffer3Id = Shader.PropertyToID("_GBuffer3");
        private static readonly int GBuffer4Id = Shader.PropertyToID("_GBuffer4");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int DirectionalShadowTextureId = Shader.PropertyToID("_DirectionalShadowTexture");
        private static readonly int LightingTextureId = Shader.PropertyToID("_LightingTexture");
        private static readonly int LightingWidthId = Shader.PropertyToID("_LightingWidth");
        private static readonly int LightingHeightId = Shader.PropertyToID("_LightingHeight");
        private static readonly int MaterialPixelIndicesId = Shader.PropertyToID("_MaterialPixelIndices");
        private static readonly int SkyIBLCubemapId = Shader.PropertyToID("_VividSkyIBLCubemap");
        private static readonly int SkyIBLTintId = Shader.PropertyToID("_VividSkyIBLTint");
        private static readonly int SkyIBLParamsId = Shader.PropertyToID("_VividSkyIBLParams");
        private static readonly int DirectionalLightsId = Shader.PropertyToID("_DirectionalLights");
        private static readonly int DirectionalLightCountId = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int MainDirectionalLightIndexId = Shader.PropertyToID("_MainDirectionalLightIndex");
        private static readonly int PunctualLightsId = Shader.PropertyToID("_PunctualLights");
        private static readonly int PunctualLightCountId = Shader.PropertyToID("_PunctualLightCount");
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
        private static readonly int ClusterTileSizeId = Shader.PropertyToID("_ClusterTileSize");
        private static readonly int ClusterSliceCountId = Shader.PropertyToID("_ClusterSliceCount");
        private static readonly int ClusterTileCountXId = Shader.PropertyToID("_ClusterTileCountX");
        private static readonly int ClusterTileCountYId = Shader.PropertyToID("_ClusterTileCountY");
        private static readonly int ClusterNearClipId = Shader.PropertyToID("_ClusterNearClip");
        private static readonly int ClusterFarClipId = Shader.PropertyToID("_ClusterFarClip");
        private static readonly int ClusterIsOrthographicId = Shader.PropertyToID("_ClusterIsOrthographic");
        private const uint IndirectArgsOffset = 0u;

        [RenderGraphResource(Name = "GBuffer0", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer0;

        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(Name = "GBuffer2", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer2;

        [RenderGraphResource(Name = "GBuffer3", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer3;

        [RenderGraphResource(Name = "GBuffer4", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer4;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(
            Name = "DirectionalShadowTexture",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_DirectionalShadowTexture;

        [RenderGraphResource(Name = "Color", Access = AccessFlags.Write, AttachmentIndex = 0)]
        private RenderGraphTexture m_ColorTexture;

        [RenderGraphResource(
            Name = "SkyIBLCubemap",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_SkyIBLCubemap;

        [RenderGraphResource(Name = "StandardMaterialIndices", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_StandardMaterialIndices;

        [RenderGraphResource(Name = "FabricMaterialIndices", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_FabricMaterialIndices;

        [RenderGraphResource(Name = "ClearCoatMaterialIndices", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_ClearCoatMaterialIndices;

        [RenderGraphResource(Name = "StandardIndirectArgs", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_StandardIndirectArgs;

        [RenderGraphResource(Name = "FabricIndirectArgs", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_FabricIndirectArgs;

        [RenderGraphResource(Name = "ClearCoatIndirectArgs", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_ClearCoatIndirectArgs;

        [RenderGraphResource(
            Name = "DirectionalLights",
            Access = AccessFlags.Read)]
        private RenderGraphBuffer m_DirectionalLightBuffer;

        [RenderGraphResource(
            Name = "PunctualLights",
            Access = AccessFlags.Read)]
        private RenderGraphBuffer m_PunctualLightBuffer;

        [RenderGraphResource(
            Name = "LayeredOffset",
            Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LayeredOffsetBuffer;

        [RenderGraphResource(
            Name = "LayeredLightList",
            Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LayeredLightListBuffer;

        [RenderGraphResource(
            Name = "LogBaseBuffer",
            Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LogBaseBuffer;


        [RenderGraphResource(
            Name = "PreIntegratedFGD_GGXDisneyDiffuse",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_PreIntegratedFGDGGXDisneyDiffuseTexture;

        [RenderGraphResource(
            Name = "PreIntegratedFGD_CharlieAndFabric",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_PreIntegratedFGDCharlieAndFabricTexture;

        private ComputeShader m_DeferredLitCompute;
        private int m_ClearDeferredLitKernel = -1;
        private int m_DeferredLitKernel = -1;
        private int m_LightingWidth = 1;
        private int m_LightingHeight = 1;
        private int m_ClearDispatchGroupCountX = 1;
        private int m_ClearDispatchGroupCountY = 1;
        private int m_DirectionalLightCount;
        private int m_PunctualLightCount;
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
        private bool m_SupportsClusteredPunctualLights;
        private bool m_IsLogBaseBufferEnabled;
        private readonly RenderGraphTexture m_LocalGBuffer4;
        private readonly RenderGraphTexture m_LocalDirectionalShadowTexture;
        private readonly RenderGraphBuffer m_LocalDirectionalLightBuffer;
        private readonly RenderGraphBuffer m_LocalPunctualLightBuffer;
        private readonly RenderGraphBuffer m_LocalLayeredOffsetBuffer;
        private readonly RenderGraphBuffer m_LocalLayeredLightListBuffer;
        private readonly RenderGraphBuffer m_LocalLogBaseBuffer;
        private readonly RenderGraphTexture m_LocalPreIntegratedFGDGGXDisneyDiffuseTexture;
        private readonly RenderGraphTexture m_LocalPreIntegratedFGDCharlieAndFabricTexture;
        private VividPreIntegratedFGDTextures m_FallbackPreIntegratedFGDTextures;
        private Color m_SkyIBLTint = Color.white;
        private Vector4 m_SkyIBLParams;

        public DeferredLightingPass()
            : this(nameof(DeferredLightingPass))
        {
        }

        protected DeferredLightingPass(string profilerName)
        {
            profilingSampler = new ProfilingSampler(profilerName);

            m_GBuffer0 = RenderGraphTexture.CreateInput("GBuffer0", GraphicsFormat.R8G8B8A8_UNorm);
            m_GBuffer1 = RenderGraphTexture.CreateInput("GBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);
            m_GBuffer2 = RenderGraphTexture.CreateInput("GBuffer2", GraphicsFormat.R8G8B8A8_UNorm);
            m_GBuffer3 = RenderGraphTexture.CreateInput("GBuffer3", GraphicsFormat.B10G11R11_UFloatPack32);
            m_LocalGBuffer4 = RenderGraphTexture.CreateColorTarget("GBuffer4", GraphicsFormat.R16G16B16A16_SFloat);
            m_GBuffer4 = m_LocalGBuffer4;
            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_LocalDirectionalShadowTexture = RenderGraphTexture.CreateColorTarget("DirectionalShadowTexture", GraphicsFormat.R16_SFloat);
            m_LocalDirectionalShadowTexture.desc.ClearBuffer = true;
            m_LocalDirectionalShadowTexture.desc.ClearColor = Color.white;
            m_LocalDirectionalShadowTexture.desc.FilterMode = FilterMode.Point;
            m_LocalDirectionalShadowTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_DirectionalShadowTexture = m_LocalDirectionalShadowTexture;
            m_ColorTexture = RenderGraphTexture.CreateOutput("Color", GraphicsFormat.R16G16B16A16_SFloat);
            m_ColorTexture.desc.EnableRandomWrite = true;
            m_ColorTexture.desc.ClearBuffer = true;
            m_ColorTexture.desc.ClearColor = Color.clear;
            m_SkyIBLCubemap = CreateSkyIBLCubemapTexture("SkyIBLCubemap");
            m_StandardMaterialIndices = CreateStructuredBuffer("StandardMaterialIndices", sizeof(uint));
            m_FabricMaterialIndices = CreateStructuredBuffer("FabricMaterialIndices", sizeof(uint));
            m_ClearCoatMaterialIndices = CreateStructuredBuffer("ClearCoatMaterialIndices", sizeof(uint));
            m_StandardIndirectArgs = CreateIndirectArgsBuffer("StandardIndirectArgs");
            m_FabricIndirectArgs = CreateIndirectArgsBuffer("FabricIndirectArgs");
            m_ClearCoatIndirectArgs = CreateIndirectArgsBuffer("ClearCoatIndirectArgs");
            m_LocalDirectionalLightBuffer = CreateStructuredBuffer("DirectionalLights", VividLightData.DirectionalLightData.Stride);
            m_LocalPunctualLightBuffer = CreateStructuredBuffer("PunctualLights", VividLightData.PunctualLightData.Stride);
            m_LocalLayeredOffsetBuffer = CreateStructuredBuffer("LayeredOffset", sizeof(uint));
            m_LocalLayeredLightListBuffer = CreateStructuredBuffer("LayeredLightList", sizeof(uint));
            m_LocalLogBaseBuffer = CreateStructuredBuffer("LogBaseBuffer", sizeof(float));
            m_DirectionalLightBuffer = m_LocalDirectionalLightBuffer;
            m_PunctualLightBuffer = m_LocalPunctualLightBuffer;
            m_LayeredOffsetBuffer = m_LocalLayeredOffsetBuffer;
            m_LayeredLightListBuffer = m_LocalLayeredLightListBuffer;
            m_LogBaseBuffer = m_LocalLogBaseBuffer;
            m_LocalPreIntegratedFGDGGXDisneyDiffuseTexture = VividPreIntegratedFGD.CreateTexture("PreIntegratedFGD_GGXDisneyDiffuse");
            m_LocalPreIntegratedFGDCharlieAndFabricTexture = VividPreIntegratedFGD.CreateTexture("PreIntegratedFGD_CharlieAndFabric");
            m_PreIntegratedFGDGGXDisneyDiffuseTexture = m_LocalPreIntegratedFGDGGXDisneyDiffuseTexture;
            m_PreIntegratedFGDCharlieAndFabricTexture = m_LocalPreIntegratedFGDCharlieAndFabricTexture;
        }

        public override void Create()
        {
            m_DeferredLitCompute = PipelineResourceManager.Get<VividRPCoreResources>()?.DeferredLitCompute;

            if (m_DeferredLitCompute == null)
            {
                Debug.LogWarning($"[VividRP] Could not find compute shader resource 'Shaders/Material/DeferredLit' for {nameof(DeferredLightingPass)}.");
                return;
            }

            m_ClearDeferredLitKernel = m_DeferredLitCompute.FindKernel(ClearDeferredLitKernelName);
            m_DeferredLitKernel = m_DeferredLitCompute.FindKernel(DeferredLitKernelName);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var width = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            var height = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (width <= 0)
                width = Mathf.Max(1, Screen.width);

            if (height <= 0)
                height = Mathf.Max(1, Screen.height);

            m_LightingWidth = width;
            m_LightingHeight = height;
            m_ClearDispatchGroupCountX = Mathf.Max(1, (width + ClearThreadGroupSizeX - 1) / ClearThreadGroupSizeX);
            m_ClearDispatchGroupCountY = Mathf.Max(1, (height + ClearThreadGroupSizeY - 1) / ClearThreadGroupSizeY);

            m_GBuffer0.Resize(width, height);
            m_GBuffer1.Resize(width, height);
            m_GBuffer2.Resize(width, height);
            m_GBuffer3.Resize(width, height);
            m_GBuffer4.Resize(width, height);
            m_DepthTexture.Resize(width, height);
            m_ColorTexture.Resize(width, height);
            PrepareClusteredLightingParameters(frameData);
            PreparePreIntegratedFGDResources();
            PrepareSkyIblState(frameData.GetOrCreate<VividSkyData>());
        }

        public override void Record(UnsafeGraphContext context)
        {
            if (m_DeferredLitCompute == null
                || m_ClearDeferredLitKernel < 0
                || m_DeferredLitKernel < 0)
            {
                return;
            }

            var cmd = context.cmd;
            var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(cmd);

            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                BindSharedParameters(cmd, m_ClearDeferredLitKernel);
                cmd.DispatchCompute(m_DeferredLitCompute, m_ClearDeferredLitKernel, m_ClearDispatchGroupCountX, m_ClearDispatchGroupCountY, 1);

                BindSharedParameters(cmd, m_DeferredLitKernel);
                BindIndirectLightingParameters(cmd, m_DeferredLitKernel);
                BindLightLoopParameters(cmd, m_DeferredLitKernel);
                DispatchMaterialClass(cmd, m_StandardMaterialIndices, m_StandardIndirectArgs);
                // DispatchMaterialClass(cmd, m_FabricMaterialIndices, m_FabricIndirectArgs);
                // DispatchMaterialClass(cmd, m_ClearCoatMaterialIndices, m_ClearCoatIndirectArgs);
            }
        }

        public override void Dispose()
        {
            m_FallbackPreIntegratedFGDTextures?.Dispose();
            m_FallbackPreIntegratedFGDTextures = null;

            m_DeferredLitCompute = null;
            m_ClearDeferredLitKernel = -1;
            m_DeferredLitKernel = -1;
            m_DirectionalLightCount = 0;
            m_PunctualLightCount = 0;
            m_MainDirectionalLightIndex = -1;
            m_ClusterTileSize = LightGridPass.ClusterTileSize;
            m_ClusterSliceCount = LightGridPass.ClusterSliceCount;
            m_ClusterTileCountX = 1;
            m_ClusterTileCountY = 1;
            m_ClusterNearClip = 0.1f;
            m_ClusterFarClip = 1000.0f;
            m_ClusterIsOrthographic = 0;
            m_ClusterScale = 0.0f;
            m_ClusterBase = LightGridPass.ClusterLogBase;
            m_ClusterLog2SliceCount = LightGridPass.ClusterLog2SliceCount;
            m_SupportsClusteredPunctualLights = false;
            m_IsLogBaseBufferEnabled = false;
        }

        internal static Vector4 BuildSkyIblParams(Texture skyCubemap, float exposure, float rotation)
        {
            var maxMip = skyCubemap != null ? Mathf.Max(0, skyCubemap.mipmapCount - 1) : 0;
            return BuildSkyIblParams(maxMip, exposure, rotation, skyCubemap != null);
        }

        internal static Vector4 BuildSkyIblParams(int maxMip, float exposure, float rotation, bool enabled)
        {
            return new Vector4(
                Mathf.Max(0f, exposure),
                -rotation,
                Mathf.Max(0, maxMip),
                enabled ? 1f : 0f);
        }

        private void BindSharedParameters(UnsafeCommandBuffer cmd, int kernel)
        {
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, GBuffer0Id, m_GBuffer0.innerHandle);
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, GBuffer2Id, m_GBuffer2.innerHandle);
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, GBuffer3Id, m_GBuffer3.innerHandle);
            if (ReferenceEquals(m_GBuffer4, m_LocalGBuffer4)
                || m_GBuffer4 == null
                || !m_GBuffer4.innerHandle.IsValid())
            {
                cmd.SetComputeTextureParam(
                    m_DeferredLitCompute,
                    kernel,
                    GBuffer4Id,
                    Texture2D.blackTexture);
            }
            else
            {
                cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, GBuffer4Id, m_GBuffer4.innerHandle);
            }
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, DepthTextureId, m_DepthTexture.innerHandle);
            if (ReferenceEquals(m_DirectionalShadowTexture, m_LocalDirectionalShadowTexture)
                || m_DirectionalShadowTexture == null
                || !m_DirectionalShadowTexture.innerHandle.IsValid())
            {
                cmd.SetComputeTextureParam(
                    m_DeferredLitCompute,
                    kernel,
                    DirectionalShadowTextureId,
                    Texture2D.whiteTexture);
            }
            else
            {
                cmd.SetComputeTextureParam(
                    m_DeferredLitCompute,
                    kernel,
                    DirectionalShadowTextureId,
                    m_DirectionalShadowTexture.innerHandle);
            }
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, LightingTextureId, m_ColorTexture.innerHandle);
            cmd.SetComputeIntParam(m_DeferredLitCompute, LightingWidthId, m_LightingWidth);
            cmd.SetComputeIntParam(m_DeferredLitCompute, LightingHeightId, m_LightingHeight);
        }

        private void BindIndirectLightingParameters(UnsafeCommandBuffer cmd, int kernel)
        {
            cmd.SetComputeTextureParam(
                m_DeferredLitCompute,
                kernel,
                VividPreIntegratedFGD.GGXDisneyDiffuseTextureId,
                m_PreIntegratedFGDGGXDisneyDiffuseTexture.innerHandle);
            cmd.SetComputeTextureParam(
                m_DeferredLitCompute,
                kernel,
                VividPreIntegratedFGD.CharlieAndFabricTextureId,
                m_PreIntegratedFGDCharlieAndFabricTexture.innerHandle);
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, SkyIBLCubemapId, m_SkyIBLCubemap.innerHandle);
            cmd.SetComputeVectorParam(m_DeferredLitCompute, SkyIBLTintId, m_SkyIBLTint);
            cmd.SetComputeVectorParam(m_DeferredLitCompute, SkyIBLParamsId, m_SkyIBLParams);
        }

        private void BindLightLoopParameters(UnsafeCommandBuffer cmd, int kernel)
        {
            cmd.SetComputeIntParam(m_DeferredLitCompute, DirectionalLightCountId, m_DirectionalLightCount);
            cmd.SetComputeIntParam(m_DeferredLitCompute, MainDirectionalLightIndexId, m_MainDirectionalLightIndex);
            cmd.SetComputeIntParam(m_DeferredLitCompute, PunctualLightCountId, m_PunctualLightCount);
            cmd.SetComputeIntParam(m_DeferredLitCompute, ClusterTileSizeId, m_ClusterTileSize);
            cmd.SetComputeIntParam(m_DeferredLitCompute, ClusterSliceCountId, m_ClusterSliceCount);
            cmd.SetComputeIntParam(m_DeferredLitCompute, ClusterTileCountXId, m_ClusterTileCountX);
            cmd.SetComputeIntParam(m_DeferredLitCompute, ClusterTileCountYId, m_ClusterTileCountY);
            cmd.SetComputeIntParam(m_DeferredLitCompute, ClusterIsOrthographicId, m_ClusterIsOrthographic);
            cmd.SetComputeFloatParam(m_DeferredLitCompute, ClusterNearClipId, m_ClusterNearClip);
            cmd.SetComputeFloatParam(m_DeferredLitCompute, ClusterFarClipId, m_ClusterFarClip);
            cmd.SetComputeFloatParam(m_DeferredLitCompute, ClusterScaleId, m_ClusterScale);
            cmd.SetComputeFloatParam(m_DeferredLitCompute, ClusterBaseId, m_ClusterBase);
            cmd.SetComputeFloatParam(m_DeferredLitCompute, NearPlaneId, m_ClusterNearClip);
            cmd.SetComputeFloatParam(m_DeferredLitCompute, FarPlaneId, m_ClusterFarClip);
            cmd.SetComputeIntParam(m_DeferredLitCompute, Log2NumClustersId, m_ClusterLog2SliceCount);
            cmd.SetComputeIntParam(m_DeferredLitCompute, IsLogBaseBufferEnabledId, m_IsLogBaseBufferEnabled ? 1 : 0);
            cmd.SetComputeIntParam(m_DeferredLitCompute, NumTileClusteredXId, m_ClusterTileCountX);
            cmd.SetComputeIntParam(m_DeferredLitCompute, NumTileClusteredYId, m_ClusterTileCountY);

            SetLightLoopBuffer(cmd, kernel, DirectionalLightsId, m_DirectionalLightBuffer);
            SetLightLoopBuffer(cmd, kernel, PunctualLightsId, m_PunctualLightBuffer);
            SetLightLoopBuffer(cmd, kernel, LayeredOffsetId, m_LayeredOffsetBuffer);
            SetLightLoopBuffer(cmd, kernel, LayeredLightListId, m_LayeredLightListBuffer);
            SetLightLoopBuffer(cmd, kernel, LogBaseBufferId, m_LogBaseBuffer);
        }

        private void DispatchMaterialClass(UnsafeCommandBuffer cmd, RenderGraphBuffer materialIndices, RenderGraphBuffer materialDispatchArgs)
        {
            cmd.SetComputeBufferParam(m_DeferredLitCompute, m_DeferredLitKernel, MaterialPixelIndicesId, materialIndices.innerHandle);
            cmd.DispatchCompute(m_DeferredLitCompute, m_DeferredLitKernel, materialDispatchArgs, IndirectArgsOffset);
        }

        private void PrepareClusteredLightingParameters(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var clusteredLightingData = frameData.GetOrCreate<VividClusteredLightingData>();
            var camera = cameraData.camera;

            m_DirectionalLightCount = 0;
            m_PunctualLightCount = 0;
            m_MainDirectionalLightIndex = -1;
            m_ClusterTileSize = LightGridPass.ClusterTileSize;
            m_ClusterSliceCount = LightGridPass.ClusterSliceCount;
            m_ClusterTileCountX = Mathf.Max(1, Mathf.CeilToInt(m_LightingWidth / (float)m_ClusterTileSize));
            m_ClusterTileCountY = Mathf.Max(1, Mathf.CeilToInt(m_LightingHeight / (float)m_ClusterTileSize));
            m_ClusterNearClip = Mathf.Max(camera != null ? camera.nearClipPlane : 0.1f, 0.01f);
            m_ClusterFarClip = Mathf.Max(camera != null ? camera.farClipPlane : 1000.0f, m_ClusterNearClip + 0.01f);
            m_ClusterIsOrthographic = camera != null && camera.orthographic ? 1 : 0;
            m_ClusterScale = 0.0f;
            m_ClusterBase = LightGridPass.ClusterLogBase;
            m_ClusterLog2SliceCount = LightGridPass.ClusterLog2SliceCount;
            m_SupportsClusteredPunctualLights = false;
            m_IsLogBaseBufferEnabled = false;

            if (!HasClusteredLightingData(clusteredLightingData))
                return;

            m_MainDirectionalLightIndex = clusteredLightingData.mainDirectionalLightIndex;
            m_ClusterTileSize = clusteredLightingData.clusterTileSize > 0
                ? clusteredLightingData.clusterTileSize
                : LightGridPass.ClusterTileSize;
            m_ClusterSliceCount = clusteredLightingData.clusterSliceCount > 0
                ? clusteredLightingData.clusterSliceCount
                : LightGridPass.ClusterSliceCount;
            m_ClusterTileCountX = clusteredLightingData.clusterTileCountX > 0
                ? clusteredLightingData.clusterTileCountX
                : Mathf.Max(1, Mathf.CeilToInt(m_LightingWidth / (float)Mathf.Max(m_ClusterTileSize, 1)));
            m_ClusterTileCountY = clusteredLightingData.clusterTileCountY > 0
                ? clusteredLightingData.clusterTileCountY
                : Mathf.Max(1, Mathf.CeilToInt(m_LightingHeight / (float)Mathf.Max(m_ClusterTileSize, 1)));
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

            if (HasBoundDirectionalLightBuffer())
                m_DirectionalLightCount = Mathf.Max(0, clusteredLightingData.directionalLightCount);
            else
                m_MainDirectionalLightIndex = -1;

            m_SupportsClusteredPunctualLights = clusteredLightingData.supportsClusteredPunctualLights && HasBoundPunctualLightResources();
            m_PunctualLightCount = m_SupportsClusteredPunctualLights
                ? Mathf.Max(0, clusteredLightingData.punctualLightCount)
                : 0;
            m_IsLogBaseBufferEnabled = m_SupportsClusteredPunctualLights && clusteredLightingData.isLogBaseBufferEnabled;
        }

        private bool HasBoundDirectionalLightBuffer()
        {
            return !ReferenceEquals(m_DirectionalLightBuffer, m_LocalDirectionalLightBuffer);
        }

        private bool HasBoundPunctualLightResources()
        {
            return !ReferenceEquals(m_PunctualLightBuffer, m_LocalPunctualLightBuffer)
                && !ReferenceEquals(m_LayeredOffsetBuffer, m_LocalLayeredOffsetBuffer)
                && !ReferenceEquals(m_LayeredLightListBuffer, m_LocalLayeredLightListBuffer)
                && !ReferenceEquals(m_LogBaseBuffer, m_LocalLogBaseBuffer);
        }

        private static bool HasClusteredLightingData(VividClusteredLightingData clusteredLightingData)
        {
            return clusteredLightingData != null
                && (clusteredLightingData.directionalLights != null
                    || clusteredLightingData.punctualLights != null
                    || clusteredLightingData.clusterTileSize > 0
                    || clusteredLightingData.clusterSliceCount > 0);
        }

        private void SetLightLoopBuffer(UnsafeCommandBuffer cmd, int kernel, int propertyId, RenderGraphBuffer buffer)
        {
            if (buffer == null || !buffer.innerHandle.IsValid())
                return;

            cmd.SetComputeBufferParam(m_DeferredLitCompute, kernel, propertyId, buffer.innerHandle);
        }

        private static RenderGraphTexture CreateSkyIBLCubemapTexture(string name)
        {
            return new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 1,
                    Height = 1,
                    Dimension = TextureDimension.Cube,
                    ColorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    DepthBufferBits = DepthBits.None,
                    FilterMode = FilterMode.Trilinear,
                    WrapMode = TextureWrapMode.Clamp,
                    UseMipMap = true,
                    AutoGenerateMips = false,
                    Name = name
                }
            };
        }

        private static RenderGraphBuffer CreateStructuredBuffer(string name, int stride)
        {
            return new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = 1,
                    Stride = stride,
                    Target = GraphicsBuffer.Target.Structured,
                    Name = name
                }
            };
        }

        private static RenderGraphBuffer CreateIndirectArgsBuffer(string name)
        {
            return new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = 4,
                    Stride = sizeof(uint),
                    Target = GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                    Name = name
                }
            };
        }

        private void PreparePreIntegratedFGDResources()
        {
            if (!PassRecorder.IsPassTextureImportActive)
                return;

            var needsLocalGGXDisneyDiffuse = ReferenceEquals(
                m_PreIntegratedFGDGGXDisneyDiffuseTexture,
                m_LocalPreIntegratedFGDGGXDisneyDiffuseTexture);
            var needsLocalCharlieAndFabric = ReferenceEquals(
                m_PreIntegratedFGDCharlieAndFabricTexture,
                m_LocalPreIntegratedFGDCharlieAndFabricTexture);

            if (!needsLocalGGXDisneyDiffuse && !needsLocalCharlieAndFabric)
                return;

            m_FallbackPreIntegratedFGDTextures ??= new VividPreIntegratedFGDTextures();
            m_FallbackPreIntegratedFGDTextures.Create(PipelineResourceManager.Get<VividRPCoreResources>());

            if (needsLocalGGXDisneyDiffuse && m_FallbackPreIntegratedFGDTextures.GGXDisneyDiffuseTexture != null)
            {
                PassRecorder.ImportTexture(
                    m_PreIntegratedFGDGGXDisneyDiffuseTexture,
                    m_FallbackPreIntegratedFGDTextures.GGXDisneyDiffuseTexture);
            }

            if (needsLocalCharlieAndFabric && m_FallbackPreIntegratedFGDTextures.CharlieAndFabricTexture != null)
            {
                PassRecorder.ImportTexture(
                    m_PreIntegratedFGDCharlieAndFabricTexture,
                    m_FallbackPreIntegratedFGDTextures.CharlieAndFabricTexture);
            }
        }

        private void PrepareSkyIblState(VividSkyData skyData)
        {
            var hasActiveSky = skyData != null && skyData.activeSkyType != SkyType.None;
            var skyMaxMip = hasActiveSky ? SkyManager.GetSpecularCubemapMaxMip(skyData) : 0;

            SkyManager.ImportSpecularCubemap(m_SkyIBLCubemap, skyData);

            m_SkyIBLTint = hasActiveSky ? skyData.tint : Color.white;
            var skyExposure = hasActiveSky ? skyData.exposure : 1.0f;
            var skyRotation = hasActiveSky ? skyData.rotation : 0.0f;
            m_SkyIBLParams = BuildSkyIblParams(skyMaxMip, skyExposure, skyRotation, hasActiveSky);
        }
    }
}
