using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class DeferredLightingPass : ComputePass, IStablePassResourceLayout
    {
        private const int ClearThreadGroupSizeX = 8;
        private const int ClearThreadGroupSizeY = 8;
        private const int MaterialFeatureVariantCount = 7;
        private const int IndirectArgsElementCount = 4;
        private const string ClearDeferredLitKernelName = "ClearDeferredLit";
        private static readonly string[] DeferredLitVariantKernelNames =
        {
            "DeferredLit_Variant0",
            "DeferredLit_Variant1",
            "DeferredLit_Variant2",
            "DeferredLit_Variant3",
            "DeferredLit_Variant4",
            "DeferredLit_Variant5",
            "DeferredLit_Variant6"
        };

        private static readonly int GBuffer0Id = Shader.PropertyToID("_GBuffer0");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int GBuffer2Id = Shader.PropertyToID("_GBuffer2");
        private static readonly int GBuffer3Id = Shader.PropertyToID("_GBuffer3");
        private static readonly int GBuffer4Id = Shader.PropertyToID("_GBuffer4");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int DirectionalShadowTextureId = Shader.PropertyToID("_DirectionalShadowTexture");
        private static readonly int GTAOTextureId = Shader.PropertyToID("_GTAOTexture");
        private static readonly int ScreenSpaceReflectionTextureId = Shader.PropertyToID("_ScreenSpaceReflectionTexture");
        private static readonly int ScreenSpaceReflectionEnabledId = Shader.PropertyToID("_ScreenSpaceReflectionEnabled");
        private static readonly int LightingTextureId = Shader.PropertyToID("_LightingTexture");
        private static readonly int LightingDebugTextureId = Shader.PropertyToID("_LightingDebugTexture");
        private static readonly int LightingWidthId = Shader.PropertyToID("_LightingWidth");
        private static readonly int LightingHeightId = Shader.PropertyToID("_LightingHeight");
        private static readonly int MaterialTileFeatureFlagsId = Shader.PropertyToID("_MaterialTileFeatureFlags");
        private static readonly int MaterialFeatureTileListId = Shader.PropertyToID("_MaterialFeatureTileList");
        private static readonly int MaterialTileCountXId = Shader.PropertyToID("_MaterialTileCountX");
        private static readonly int MaterialFeatureTileListOffsetId = Shader.PropertyToID("_MaterialFeatureTileListOffset");
        private static readonly int SkyTextureId = Shader.PropertyToID("_SkyTexture");
        private static readonly int SkyTextureTintId = Shader.PropertyToID("_SkyTextureTint");
        private static readonly int SkyTextureParamsId = Shader.PropertyToID("_SkyTextureParams");
        private static readonly int PixelCoordToViewDirWSId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly int DirectionalLightsId = Shader.PropertyToID("_DirectionalLights");
        private static readonly int DirectionalLightCountId = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int MainDirectionalLightIndexId = Shader.PropertyToID("_MainDirectionalLightIndex");
        private static readonly int PunctualLightsId = Shader.PropertyToID("_PunctualLights");
        private static readonly int AreaLightsId = Shader.PropertyToID("_AreaLights");
        private static readonly int ReflectionProbesId = Shader.PropertyToID("_ReflectionProbes");
        private static readonly int ReflectionProbeCountId = Shader.PropertyToID("_ReflectionProbeCount");
        private static readonly int ClusteredPunctualLightGridEnabledId = Shader.PropertyToID("_ClusteredPunctualLightGridEnabled");
        private static readonly int ClusteredAreaLightGridEnabledId = Shader.PropertyToID("_ClusteredAreaLightGridEnabled");
        private static readonly int ClusteredReflectionProbeGridEnabledId = Shader.PropertyToID("_ClusteredReflectionProbeGridEnabled");
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

        [RenderGraphResource(Name = "GTAOTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GTAOTexture;

        [RenderGraphResource(
            Name = "ScreenSpaceReflectionOutput",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_ScreenSpaceReflectionTexture;

        [RenderGraphResource(Name = "Color", Access = AccessFlags.Write, AttachmentIndex = 0)]
        private RenderGraphTexture m_ColorTexture;

        [RenderGraphResource(
            Name = "DeferredLightingDebug",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_DebugTexture;

        [RenderGraphResource(
            Name = "SkyIBLCubemap",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_SkyIBLCubemap;

        [RenderGraphResource(Name = "MaterialTileFeatureFlags", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_MaterialTileFeatureFlags;

        [RenderGraphResource(Name = "MaterialFeatureTileList", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_MaterialFeatureTileList;

        [RenderGraphResource(Name = "MaterialFeatureIndirectArgs", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_MaterialFeatureIndirectArgs;

        [RenderGraphResource(
            Name = "DirectionalLights",
            Access = AccessFlags.Read)]
        private RenderGraphBuffer m_DirectionalLightBuffer;

        [RenderGraphResource(
            Name = "PunctualLights",
            Access = AccessFlags.Read)]
        private RenderGraphBuffer m_PunctualLightBuffer;

        [RenderGraphResource(
            Name = "AreaLights",
            Access = AccessFlags.Read)]
        private RenderGraphBuffer m_AreaLightBuffer;

        [RenderGraphResource(
            Name = "ReflectionProbes",
            Access = AccessFlags.Read)]
        private RenderGraphBuffer m_ReflectionProbeBuffer;

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


        private RenderGraphTexture m_PreIntegratedFGDGGXDisneyDiffuseTexture;

        private RenderGraphTexture m_PreIntegratedFGDCharlieAndFabricTexture;

        private ComputeShader m_DeferredLitCompute;
        private int m_ClearDeferredLitKernel = -1;
        private readonly int[] m_DeferredLitVariantKernels = { -1, -1, -1, -1, -1, -1, -1 };
        private int m_LightingWidth = 1;
        private int m_LightingHeight = 1;
        private int m_ClearDispatchGroupCountX = 1;
        private int m_ClearDispatchGroupCountY = 1;
        private int m_MaterialTileCount = 1;
        private int m_MaterialTileCountX = 1;
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
        private bool m_SupportsClusteredPunctualLights;
        private bool m_SupportsClusteredAreaLights;
        private bool m_SupportsClusteredReflectionProbes;
        private bool m_IsLogBaseBufferEnabled;
        private bool m_IsPassResourceLayoutDirty;
        private readonly RenderGraphTexture m_LocalGBuffer4;
        private readonly RenderGraphTexture m_LocalDirectionalShadowTexture;
        private readonly RenderGraphTexture m_LocalGTAOTexture;
        private readonly RenderGraphTexture m_LocalScreenSpaceReflectionTexture;
        private readonly RenderGraphBuffer m_LocalDirectionalLightBuffer;
        private readonly RenderGraphBuffer m_LocalPunctualLightBuffer;
        private readonly RenderGraphBuffer m_LocalAreaLightBuffer;
        private readonly RenderGraphBuffer m_LocalReflectionProbeBuffer;
        private RenderGraphBuffer m_ResolvedReflectionProbeBuffer;
        private readonly RenderGraphBuffer m_LocalLayeredOffsetBuffer;
        private readonly RenderGraphBuffer m_LocalLayeredLightListBuffer;
        private readonly RenderGraphBuffer m_LocalLogBaseBuffer;
        private RenderGraphTexture m_FrameContextScreenSpaceReflectionTexture;
        private Color m_SkyTextureTint = Color.white;
        private Vector4 m_SkyTextureParams;
        private Matrix4x4 m_PixelCoordToViewDirWS = Matrix4x4.identity;

        public bool IsPassResourceLayoutDirty => m_IsPassResourceLayoutDirty;

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
            m_LocalGTAOTexture = RenderGraphTexture.CreateColorTarget("GTAOTexture", GraphicsFormat.R8_UNorm);
            m_LocalGTAOTexture.desc.ClearBuffer = true;
            m_LocalGTAOTexture.desc.ClearColor = Color.white;
            m_LocalGTAOTexture.desc.FilterMode = FilterMode.Point;
            m_LocalGTAOTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_GTAOTexture = m_LocalGTAOTexture;
            m_LocalScreenSpaceReflectionTexture = RenderGraphTexture.CreateColorTarget(
                "ScreenSpaceReflectionOutput",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_LocalScreenSpaceReflectionTexture.desc.ClearBuffer = true;
            m_LocalScreenSpaceReflectionTexture.desc.ClearColor = Color.clear;
            m_LocalScreenSpaceReflectionTexture.desc.FilterMode = FilterMode.Point;
            m_LocalScreenSpaceReflectionTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_ScreenSpaceReflectionTexture = m_LocalScreenSpaceReflectionTexture;
            m_ColorTexture = RenderGraphTexture.CreateOutput("Color", GraphicsFormat.R16G16B16A16_SFloat);
            m_ColorTexture.desc.EnableRandomWrite = true;
            m_ColorTexture.desc.ClearBuffer = true;
            m_ColorTexture.desc.ClearColor = Color.clear;
            m_DebugTexture = RenderGraphTexture.CreateOutput("DeferredLightingDebug", GraphicsFormat.R16G16B16A16_SFloat);
            m_DebugTexture.desc.EnableRandomWrite = true;
            m_DebugTexture.desc.ClearBuffer = true;
            m_DebugTexture.desc.ClearColor = Color.clear;
            m_DebugTexture.desc.FilterMode = FilterMode.Point;
            m_DebugTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_SkyIBLCubemap = CreateSkyIBLCubemapTexture("SkyIBLCubemap");
            m_MaterialTileFeatureFlags = RenderGraphBuffer.CreateStructured("MaterialTileFeatureFlags", sizeof(uint));
            m_MaterialFeatureTileList = RenderGraphBuffer.CreateStructured("MaterialFeatureTileList", sizeof(uint));
            m_MaterialFeatureIndirectArgs = CreateIndirectArgsBuffer("MaterialFeatureIndirectArgs");
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
            m_ResolvedReflectionProbeBuffer = m_LocalReflectionProbeBuffer;
            m_LayeredOffsetBuffer = m_LocalLayeredOffsetBuffer;
            m_LayeredLightListBuffer = m_LocalLayeredLightListBuffer;
            m_LogBaseBuffer = m_LocalLogBaseBuffer;
            m_PreIntegratedFGDGGXDisneyDiffuseTexture = VividPreIntegratedFGD.CreateTexture("PreIntegratedFGD_GGXDisneyDiffuse");
            m_PreIntegratedFGDCharlieAndFabricTexture = VividPreIntegratedFGD.CreateTexture("PreIntegratedFGD_CharlieAndFabric");
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
            for (var i = 0; i < MaterialFeatureVariantCount; i++)
                m_DeferredLitVariantKernels[i] = m_DeferredLitCompute.FindKernel(DeferredLitVariantKernelNames[i]);
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
            m_MaterialTileCountX = m_ClearDispatchGroupCountX;
            m_MaterialTileCount = Mathf.Max(1, m_ClearDispatchGroupCountX * m_ClearDispatchGroupCountY);
            m_PixelCoordToViewDirWS = cameraData.GetPixelCoordToViewDirWSMatrix();

            PrepareScreenSpaceReflectionResource(frameData);

            m_GBuffer0.Resize(width, height);
            m_GBuffer1.Resize(width, height);
            m_GBuffer2.Resize(width, height);
            m_GBuffer3.Resize(width, height);
            m_GBuffer4.Resize(width, height);
            m_DepthTexture.Resize(width, height);
            m_GTAOTexture.Resize(width, height);
            m_ScreenSpaceReflectionTexture.Resize(width, height);
            m_ColorTexture.Resize(width, height);
            m_DebugTexture.Resize(width, height);
            PrepareClusteredLightingParameters(frameData);
            PreparePreIntegratedFGDResources(frameData);
            PrepareSkyTextureState(frameData.GetOrCreate<VividSkyData>());
        }

        public void ClearPassResourceLayoutDirty()
        {
            m_IsPassResourceLayoutDirty = false;
        }

        public override void Record(ComputePassContext context)
        {
            if (m_DeferredLitCompute == null
                || m_ClearDeferredLitKernel < 0
                || !HasValidDeferredLitVariantKernels())
            {
                return;
            }

            var cmd = context.cmd;
            var nativeCmd = context.cmd;

            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                BindSharedParameters(context, cmd, m_ClearDeferredLitKernel);
                BindSkyTextureParameters(cmd, m_ClearDeferredLitKernel);
                cmd.DispatchCompute(m_DeferredLitCompute, m_ClearDeferredLitKernel, m_ClearDispatchGroupCountX, m_ClearDispatchGroupCountY, 1);

                for (var variant = 0; variant < MaterialFeatureVariantCount; variant++)
                {
                    var kernel = m_DeferredLitVariantKernels[variant];
                    BindSharedParameters(context, cmd, kernel);
                    BindIndirectLightingParameters(context, cmd, kernel);
                    BindLightLoopParameters(cmd, kernel);
                    BindMaterialFeatureVariantParameters(cmd, kernel, variant);
                    DispatchMaterialFeatureVariant(cmd, kernel, variant);
                }
            }
        }

        public override void Dispose()
        {
            m_DeferredLitCompute = null;
            m_ClearDeferredLitKernel = -1;
            ResetDeferredLitVariantKernels();
            m_ScreenSpaceReflectionTexture = m_LocalScreenSpaceReflectionTexture;
            m_FrameContextScreenSpaceReflectionTexture = null;
            m_PreIntegratedFGDGGXDisneyDiffuseTexture?.ClearImportedHandle();
            m_PreIntegratedFGDCharlieAndFabricTexture?.ClearImportedHandle();
            m_IsPassResourceLayoutDirty = false;
            m_DirectionalLightCount = 0;
            m_PunctualLightCount = 0;
            m_AreaLightCount = 0;
            m_ReflectionProbeCount = 0;
            m_ResolvedReflectionProbeBuffer = m_LocalReflectionProbeBuffer;
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
            m_SupportsClusteredAreaLights = false;
            m_SupportsClusteredReflectionProbes = false;
            m_IsLogBaseBufferEnabled = false;
            m_PixelCoordToViewDirWS = Matrix4x4.identity;
        }

        internal static Vector4 BuildSkyTextureParams(Texture skyCubemap, float intensityMultiplier, float rotation)
        {
            var maxMip = skyCubemap != null ? Mathf.Max(0, skyCubemap.mipmapCount - 1) : 0;
            return BuildSkyTextureParams(maxMip, intensityMultiplier, rotation, skyCubemap != null);
        }

        internal static Vector4 BuildSkyTextureParams(int maxMip, float intensityMultiplier, float rotation, bool enabled)
        {
            return new Vector4(
                Mathf.Max(intensityMultiplier, 0.0f),
                rotation,
                Mathf.Max(0, maxMip),
                enabled ? 1f : 0f);
        }

        internal static Vector4 BuildSkyIblParams(Texture skyCubemap, float intensityMultiplier, float rotation)
        {
            return BuildSkyTextureParams(skyCubemap, intensityMultiplier, rotation);
        }

        internal static Vector4 BuildSkyIblParams(int maxMip, float intensityMultiplier, float rotation, bool enabled)
        {
            return BuildSkyTextureParams(maxMip, intensityMultiplier, rotation, enabled);
        }

        private void BindSharedParameters(ComputePassContext context,ComputeCommandBuffer cmd, int kernel)
        {
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, GBuffer0Id, m_GBuffer0.innerHandle);
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, GBuffer2Id, m_GBuffer2.innerHandle);
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, GBuffer3Id, m_GBuffer3.innerHandle);
            var rgDefaultResource = context.renderGraphContext.defaultResources;
            if (ReferenceEquals(m_GBuffer4, m_LocalGBuffer4)
                || m_GBuffer4 == null
                || !m_GBuffer4.innerHandle.IsValid())
            {
                cmd.SetComputeTextureParam(
                    m_DeferredLitCompute,
                    kernel,
                    GBuffer4Id,
                    rgDefaultResource.blackTexture);
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
                    rgDefaultResource.whiteTexture);
            }
            else
            {
                cmd.SetComputeTextureParam(
                    m_DeferredLitCompute,
                    kernel,
                    DirectionalShadowTextureId,
                    m_DirectionalShadowTexture.innerHandle);
            }
            if (ReferenceEquals(m_GTAOTexture, m_LocalGTAOTexture)
                || m_GTAOTexture == null
                || !m_GTAOTexture.innerHandle.IsValid())
            {
                cmd.SetComputeTextureParam(
                    m_DeferredLitCompute,
                    kernel,
                    GTAOTextureId,
                    rgDefaultResource.whiteTexture);
            }
            else
            {
                cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, GTAOTextureId, m_GTAOTexture.innerHandle);
            }
            if (ReferenceEquals(m_ScreenSpaceReflectionTexture, m_LocalScreenSpaceReflectionTexture)
                || m_ScreenSpaceReflectionTexture == null
                || !m_ScreenSpaceReflectionTexture.innerHandle.IsValid())
            {
                cmd.SetComputeTextureParam(
                    m_DeferredLitCompute,
                    kernel,
                    ScreenSpaceReflectionTextureId,
                    rgDefaultResource.blackTexture);
                cmd.SetComputeIntParam(m_DeferredLitCompute, ScreenSpaceReflectionEnabledId, 0);
            }
            else
            {
                cmd.SetComputeTextureParam(
                    m_DeferredLitCompute,
                    kernel,
                    ScreenSpaceReflectionTextureId,
                    m_ScreenSpaceReflectionTexture.innerHandle);
                cmd.SetComputeIntParam(m_DeferredLitCompute, ScreenSpaceReflectionEnabledId, 1);
            }
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, LightingTextureId, m_ColorTexture.innerHandle);
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, LightingDebugTextureId, m_DebugTexture.innerHandle);
            cmd.SetComputeIntParam(m_DeferredLitCompute, LightingWidthId, m_LightingWidth);
            cmd.SetComputeIntParam(m_DeferredLitCompute, LightingHeightId, m_LightingHeight);
        }

        private void BindIndirectLightingParameters(ComputePassContext context, ComputeCommandBuffer cmd, int kernel)
        {
            var rgDefaultResource = context.renderGraphContext.defaultResources;
            BindPreIntegratedFGDTexture(
                cmd,
                kernel,
                VividPreIntegratedFGD.GGXDisneyDiffuseTextureId,
                m_PreIntegratedFGDGGXDisneyDiffuseTexture,
                rgDefaultResource.blackTexture);
            BindPreIntegratedFGDTexture(
                cmd,
                kernel,
                VividPreIntegratedFGD.CharlieAndFabricTextureId,
                m_PreIntegratedFGDCharlieAndFabricTexture,
                rgDefaultResource.blackTexture);
            BindSkyTextureParameters(cmd, kernel);
        }

        private void BindPreIntegratedFGDTexture(
            ComputeCommandBuffer cmd,
            int kernel,
            int propertyId,
            RenderGraphTexture texture,
            TextureHandle fallback)
        {
            cmd.SetComputeTextureParam(
                m_DeferredLitCompute,
                kernel,
                propertyId,
                texture != null && texture.innerHandle.IsValid()
                    ? texture.innerHandle
                    : fallback);
        }

        private void BindSkyTextureParameters(ComputeCommandBuffer cmd, int kernel)
        {
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, SkyTextureId, m_SkyIBLCubemap.innerHandle);
            cmd.SetComputeVectorParam(m_DeferredLitCompute, SkyTextureTintId, m_SkyTextureTint);
            cmd.SetComputeVectorParam(m_DeferredLitCompute, SkyTextureParamsId, m_SkyTextureParams);
            cmd.SetComputeMatrixParam(m_DeferredLitCompute, PixelCoordToViewDirWSId, m_PixelCoordToViewDirWS);
        }

        private void BindLightLoopParameters(ComputeCommandBuffer cmd, int kernel)
        {
            cmd.SetComputeIntParam(m_DeferredLitCompute, DirectionalLightCountId, m_DirectionalLightCount);
            cmd.SetComputeIntParam(m_DeferredLitCompute, MainDirectionalLightIndexId, m_MainDirectionalLightIndex);
            cmd.SetComputeIntParam(m_DeferredLitCompute, ReflectionProbeCountId, m_ReflectionProbeCount);
            cmd.SetComputeIntParam(m_DeferredLitCompute, ClusteredPunctualLightGridEnabledId, m_SupportsClusteredPunctualLights ? 1 : 0);
            cmd.SetComputeIntParam(m_DeferredLitCompute, ClusteredAreaLightGridEnabledId, m_SupportsClusteredAreaLights ? 1 : 0);
            cmd.SetComputeIntParam(m_DeferredLitCompute, ClusteredReflectionProbeGridEnabledId, m_SupportsClusteredReflectionProbes ? 1 : 0);
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
            SetLightLoopBuffer(cmd, kernel, AreaLightsId, m_AreaLightBuffer);
            SetLightLoopBuffer(cmd, kernel, ReflectionProbesId, m_ResolvedReflectionProbeBuffer);
            SetLightLoopBuffer(cmd, kernel, LayeredOffsetId, m_LayeredOffsetBuffer);
            SetLightLoopBuffer(cmd, kernel, LayeredLightListId, m_LayeredLightListBuffer);
            SetLightLoopBuffer(cmd, kernel, LogBaseBufferId, m_LogBaseBuffer);
        }

        private bool HasValidDeferredLitVariantKernels()
        {
            for (var i = 0; i < MaterialFeatureVariantCount; i++)
            {
                if (m_DeferredLitVariantKernels[i] < 0)
                    return false;
            }

            return true;
        }

        private void ResetDeferredLitVariantKernels()
        {
            for (var i = 0; i < MaterialFeatureVariantCount; i++)
                m_DeferredLitVariantKernels[i] = -1;
        }

        private void BindMaterialFeatureVariantParameters(ComputeCommandBuffer cmd, int kernel, int variant)
        {
            cmd.SetComputeBufferParam(
                m_DeferredLitCompute,
                kernel,
                MaterialTileFeatureFlagsId,
                m_MaterialTileFeatureFlags.innerHandle);
            cmd.SetComputeBufferParam(
                m_DeferredLitCompute,
                kernel,
                MaterialFeatureTileListId,
                m_MaterialFeatureTileList.innerHandle);
            cmd.SetComputeIntParam(m_DeferredLitCompute, MaterialTileCountXId, m_MaterialTileCountX);
            cmd.SetComputeIntParam(m_DeferredLitCompute, MaterialFeatureTileListOffsetId, variant * m_MaterialTileCount);
        }

        private void DispatchMaterialFeatureVariant(ComputeCommandBuffer cmd, int kernel, int variant)
        {
            var indirectArgsOffset = (uint)(variant * IndirectArgsElementCount * sizeof(uint));
            cmd.DispatchCompute(m_DeferredLitCompute, kernel, m_MaterialFeatureIndirectArgs, indirectArgsOffset);
        }

        private void PrepareClusteredLightingParameters(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var clusteredLightingData = frameData.GetOrCreate<VividClusteredLightingData>();
            var camera = cameraData.camera;

            m_ResolvedReflectionProbeBuffer = ResolveClusteredBuffer(
                m_ReflectionProbeBuffer,
                m_LocalReflectionProbeBuffer,
                clusteredLightingData.reflectionProbes);
            m_DirectionalLightCount = 0;
            m_PunctualLightCount = 0;
            m_AreaLightCount = 0;
            m_ReflectionProbeCount = 0;
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
            m_SupportsClusteredAreaLights = false;
            m_SupportsClusteredReflectionProbes = false;
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

            var supportsClusteredFiniteLights = clusteredLightingData.supportsClusteredPunctualLights;
            m_SupportsClusteredPunctualLights = supportsClusteredFiniteLights
                && clusteredLightingData.punctualLightCount > 0
                && HasBoundPunctualLightResources();
            m_PunctualLightCount = m_SupportsClusteredPunctualLights
                ? Mathf.Max(0, clusteredLightingData.punctualLightCount)
                : 0;
            m_SupportsClusteredAreaLights = supportsClusteredFiniteLights
                && clusteredLightingData.areaLightCount > 0
                && HasBoundAreaLightResources();
            m_AreaLightCount = m_SupportsClusteredAreaLights
                ? Mathf.Max(0, clusteredLightingData.areaLightCount)
                : 0;
            m_SupportsClusteredReflectionProbes = supportsClusteredFiniteLights
                && clusteredLightingData.reflectionProbeCount > 0
                && HasBoundReflectionProbeResources();
            m_ReflectionProbeCount = m_SupportsClusteredReflectionProbes
                ? Mathf.Max(0, clusteredLightingData.reflectionProbeCount)
                : 0;
            m_IsLogBaseBufferEnabled = supportsClusteredFiniteLights
                && clusteredLightingData.isLogBaseBufferEnabled
                && !ReferenceEquals(m_LogBaseBuffer, m_LocalLogBaseBuffer);
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

        private bool HasBoundAreaLightResources()
        {
            return !ReferenceEquals(m_AreaLightBuffer, m_LocalAreaLightBuffer)
                && !ReferenceEquals(m_LayeredOffsetBuffer, m_LocalLayeredOffsetBuffer)
                && !ReferenceEquals(m_LayeredLightListBuffer, m_LocalLayeredLightListBuffer);
        }

        private bool HasBoundReflectionProbeResources()
        {
            return !ReferenceEquals(m_ResolvedReflectionProbeBuffer, m_LocalReflectionProbeBuffer)
                && !ReferenceEquals(m_LayeredOffsetBuffer, m_LocalLayeredOffsetBuffer)
                && !ReferenceEquals(m_LayeredLightListBuffer, m_LocalLayeredLightListBuffer);
        }

        private static bool HasClusteredLightingData(VividClusteredLightingData clusteredLightingData)
        {
            return clusteredLightingData != null
                && (clusteredLightingData.directionalLights != null
                    || clusteredLightingData.punctualLights != null
                    || clusteredLightingData.areaLights != null
                    || clusteredLightingData.reflectionProbes != null
                    || clusteredLightingData.clusterTileSize > 0
                    || clusteredLightingData.clusterSliceCount > 0
                    || clusteredLightingData.reflectionProbeCount > 0
                    || clusteredLightingData.areaLightCount > 0);
        }

        private static RenderGraphBuffer ResolveClusteredBuffer(
            RenderGraphBuffer graphBuffer,
            RenderGraphBuffer localFallback,
            RenderGraphBuffer frameBuffer)
        {
            if (graphBuffer != null && !ReferenceEquals(graphBuffer, localFallback))
                return graphBuffer;

            return frameBuffer ?? localFallback;
        }

        private void SetLightLoopBuffer(ComputeCommandBuffer cmd, int kernel, int propertyId, RenderGraphBuffer buffer)
        {
            if (buffer == null || !buffer.innerHandle.IsValid())
                return;

            cmd.SetComputeBufferParam(m_DeferredLitCompute, kernel, propertyId, buffer.innerHandle);
        }

        private void PrepareScreenSpaceReflectionResource(ContextContainer frameData)
        {
            if (!ReferenceEquals(m_ScreenSpaceReflectionTexture, m_LocalScreenSpaceReflectionTexture)
                && !ReferenceEquals(m_ScreenSpaceReflectionTexture, m_FrameContextScreenSpaceReflectionTexture))
            {
                return;
            }

            var resolvedTexture = m_LocalScreenSpaceReflectionTexture;
            if (frameData != null && frameData.Contains<VividScreenSpaceReflectionData>())
            {
                var ssrData = frameData.Get<VividScreenSpaceReflectionData>();
                if (ssrData?.hasValidTexture == true && ssrData.reflectionTexture != null)
                    resolvedTexture = ssrData.reflectionTexture;
            }

            if (ReferenceEquals(m_ScreenSpaceReflectionTexture, resolvedTexture))
                return;

            m_ScreenSpaceReflectionTexture = resolvedTexture;
            m_FrameContextScreenSpaceReflectionTexture = ReferenceEquals(resolvedTexture, m_LocalScreenSpaceReflectionTexture)
                ? null
                : resolvedTexture;
            m_IsPassResourceLayoutDirty = true;
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

        private static RenderGraphBuffer CreateIndirectArgsBuffer(string name)
        {
            return new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = MaterialFeatureVariantCount * IndirectArgsElementCount,
                    Stride = sizeof(uint),
                    Target = GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                    Name = name
                }
            };
        }

        private void PreparePreIntegratedFGDResources(ContextContainer frameData)
        {
            m_PreIntegratedFGDGGXDisneyDiffuseTexture.ClearImportedHandle();
            m_PreIntegratedFGDCharlieAndFabricTexture.ClearImportedHandle();

            if (!PassRecorder.IsPassTextureImportActive)
                return;

            if (frameData == null || !frameData.Contains<VividPreIntegratedFGDData>())
                return;

            var fgdData = frameData.Get<VividPreIntegratedFGDData>();
            if (fgdData?.hasValidTextures != true)
                return;

            ImportPreIntegratedFGDTexture(
                m_PreIntegratedFGDGGXDisneyDiffuseTexture,
                fgdData.ggxDisneyDiffuseTexture);
            ImportPreIntegratedFGDTexture(
                m_PreIntegratedFGDCharlieAndFabricTexture,
                fgdData.charlieAndFabricTexture);
        }

        private void ImportPreIntegratedFGDTexture(RenderGraphTexture target, RTHandle source)
        {
            if (target == null || source == null)
                return;

            var handle = Import(source);
            if (handle.IsValid())
                target.SetImportedHandle(handle);
        }

        private void PrepareSkyTextureState(VividSkyData skyData)
        {
            var hasActiveSky = skyData != null && skyData.activeSkyType != SkyType.None;
            var skyMaxMip = hasActiveSky ? SkyManager.GetSpecularCubemapMaxMip(skyData) : 0;

            SkyManager.ImportSpecularCubemap(m_SkyIBLCubemap, skyData);

            m_SkyTextureTint = hasActiveSky ? skyData.tint : Color.white;
            var skyIntensityMultiplier = hasActiveSky ? skyData.exposure : 1.0f;
            var skyRotation = hasActiveSky ? skyData.rotation : 0.0f;
            m_SkyTextureParams = BuildSkyTextureParams(skyMaxMip, skyIntensityMultiplier, skyRotation, hasActiveSky);
        }
    }
}
